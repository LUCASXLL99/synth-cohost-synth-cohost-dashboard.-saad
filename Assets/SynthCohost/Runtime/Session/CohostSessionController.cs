using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Authentication;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Features.Stt;
using SynthCohost.Runtime.Routing;
using SynthCohost.Transport;

namespace SynthCohost.Runtime.Session
{
    public sealed class CohostSessionController : ITranscriptClient, IDisposable
    {
        private const int MaximumPendingInboundMessages = 128;

        private readonly ConnectionRuntimeOptions options;
        private readonly ICohostCredentialProvider credentialProvider;
        private readonly IProtocolDialect dialect;
        private readonly IWebSocketTransportFactory transportFactory;
        private readonly ProtocolMessageRouter router;
        private readonly ICohostDiagnostics diagnostics;
        private readonly ConnectionStatusViewModel status;
        private readonly SessionStateMachine stateMachine = new SessionStateMachine();
        private readonly FinalTurnGate finalTurnGate = new FinalTurnGate();
        private readonly OutboundMessageRateLimiter outboundRateLimiter = new OutboundMessageRateLimiter();
        private readonly ReconnectController reconnectController;
        private readonly IClosePolicy closePolicy;
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly object connectionGate = new object();
        private readonly object inboundQueueGate = new object();
        private readonly IMainThreadDispatcher dispatcher;

        private CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private CancellationTokenSource connectionCancellation;
        private CancellationTokenSource finalTurnTimeoutCancellation;
        private IWebSocketTransport transport;
        private HeartbeatScheduler heartbeat;
        private string sessionId;
        private int connectionGeneration;
        private int reconnectAttempt;
        private bool sessionMismatchRetryUsed;
        private bool stopRequested;
        private bool disposed;
        private Task reconnectTask = Task.CompletedTask;
        private Task inboundTail = Task.CompletedTask;
        private int pendingInboundMessages;

        public CohostSessionController(
            ConnectionRuntimeOptions options,
            ICohostCredentialProvider credentialProvider,
            IProtocolDialect dialect,
            IWebSocketTransportFactory transportFactory,
            ProtocolMessageRouter router,
            ConnectionStatusViewModel status = null,
            ICohostDiagnostics diagnostics = null,
            IMainThreadDispatcher dispatcher = null,
            IClosePolicy closePolicy = null)
        {
            this.options = options;
            this.credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            this.dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            this.status = status ?? new ConnectionStatusViewModel();
            this.diagnostics = diagnostics ?? new NullCohostDiagnostics();
            this.dispatcher = dispatcher;
            this.closePolicy = closePolicy ?? new DeployedV2ClosePolicy();
            reconnectController = new ReconnectController(options.Reconnect);

            stateMachine.StateChanged += OnStateChanged;
            finalTurnGate.ActiveChanged += this.status.SetTurn;
        }

        public event Action<SessionState, SessionState> StateChanged;

        public SessionState State => stateMachine.State;
        public ConnectionStatusViewModel Status => status;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            stopRequested = false;
            sessionMismatchRetryUsed = false;
            EnsureLifetimeCancellation();

            try
            {
                await ConnectOnceAsync(false, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Write(DiagnosticLogLevel.Warning, "Session", "Initial connection attempt failed.", exception);
                if (State != SessionState.AuthRequired && State != SessionState.Faulted)
                {
                    stateMachine.TryTransition(SessionState.Reconnecting);
                    StartReconnectLoop(TimeSpan.Zero);
                }
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
            {
                return;
            }

            stopRequested = true;
            lifetimeCancellation.Cancel();
            if (State != SessionState.Disconnected && State != SessionState.Stopping)
            {
                stateMachine.TryTransition(SessionState.Stopping);
            }

            await lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                await ClearConnectionAsync(true, cancellationToken);
                stateMachine.TryTransition(SessionState.Disconnected);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public Task<CohostSendResult> SendPartialTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            return SendDomainMessageAsync(
                ProtocolEventTypes.SttPartial,
                id => dialect.CreateSttPartial(id, text),
                cancellationToken);
        }

        public async Task<CohostSendResult> SendFinalTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            if (!finalTurnGate.TryBegin(out var turnToken))
            {
                return CohostSendResult.Failure(
                    CohostSendStatus.TurnAlreadyInFlight,
                    "A final transcript is already awaiting a response.");
            }

            var result = await SendDomainMessageAsync(
                ProtocolEventTypes.SttFinal,
                id => dialect.CreateSttFinal(id, text),
                cancellationToken);
            if (!result.Succeeded)
            {
                finalTurnGate.Complete(turnToken);
                return result;
            }

            StartFinalTurnTimeout(turnToken);
            return result;
        }

        public Task<CohostSendResult> SendStateAcknowledgementAsync(
            AvatarBehavior behavior,
            CancellationToken cancellationToken = default)
        {
            return SendDomainMessageAsync(
                ProtocolEventTypes.StateAck,
                id => dialect.CreateStateAck(id, behavior),
                cancellationToken);
        }

        private async Task ConnectOnceAsync(bool reconnecting, CancellationToken callerCancellation)
        {
            await lifecycleGate.WaitAsync(callerCancellation);
            try
            {
                ThrowIfDisposed();
                if (State == SessionState.Ready || State == SessionState.Authenticating || State == SessionState.Connecting)
                {
                    return;
                }

                if (reconnecting && State != SessionState.Reconnecting)
                {
                    stateMachine.TryTransition(SessionState.Reconnecting);
                }

                status.SetError(string.Empty);
                stateMachine.Transition(SessionState.Connecting);
                var generation = Interlocked.Increment(ref connectionGeneration);
                var credentials = await credentialProvider.GetCredentialsAsync(callerCancellation);
                var nextSessionId = dialect.GenerateSessionId();
                var nextTransport = transportFactory.Create();
                outboundRateLimiter.Reset();
                var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);

                BindTransport(nextTransport, generation);
                lock (connectionGate)
                {
                    transport = nextTransport;
                    connectionCancellation = nextCancellation;
                    sessionId = nextSessionId;
                }

                status.SetSession(true);
                diagnostics.Write(DiagnosticLogLevel.Information, "Transport", $"Connecting to {options.Endpoint.Host}; the service may be waking.");
                using (var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                           nextCancellation.Token,
                           callerCancellation))
                {
                    await nextTransport.ConnectAsync(
                        options.Endpoint,
                        options.ConnectTimeout,
                        attemptCancellation.Token);
                    EnsureCurrent(nextTransport, generation);

                    stateMachine.Transition(SessionState.Authenticating);
                    var authJson = dialect.CreateAuth(
                        nextSessionId,
                        credentials.AccessToken,
                        credentials.AvatarId.ToString("D"));

                    using (var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                               attemptCancellation.Token))
                    {
                        authTimeout.CancelAfter(options.AuthSendTimeout);
                        if (!outboundRateLimiter.TryReserve(out _))
                        {
                            throw new InvalidOperationException("A fresh connection had no outbound auth capacity.");
                        }

                        await nextTransport.SendTextAsync(authJson, authTimeout.Token);
                        status.SetLastOutboundEvent(ProtocolEventTypes.Auth);
                        diagnostics.Write(
                            DiagnosticLogLevel.Information,
                            "Protocol",
                            "Sent 'auth' as the first frame; credentials and payload were not logged.");
                    }
                }

                EnsureCurrent(nextTransport, generation);
                stateMachine.Transition(SessionState.Ready);
                reconnectAttempt = 0;
                status.SetReconnectAttempt(0);
                StartHeartbeat(nextCancellation.Token);
                diagnostics.Write(DiagnosticLogLevel.Information, "Session", "Auth frame sent; current v2 is provisionally ready.");
            }
            catch (InvalidOperationException exception) when (exception.Message.IndexOf("credentials", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                stateMachine.TryTransition(SessionState.AuthRequired);
                status.SetError("Runtime credentials are required.");
                throw;
            }
            catch
            {
                await ClearConnectionAsync(false, CancellationToken.None);
                if (State != SessionState.AuthRequired && State != SessionState.Stopping)
                {
                    stateMachine.TryTransition(SessionState.Reconnecting);
                }
                throw;
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        private void BindTransport(IWebSocketTransport boundTransport, int generation)
        {
            boundTransport.Opened += () =>
            {
                if (IsCurrent(boundTransport, generation))
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Information,
                        "Transport",
                        "WebSocket opened; sending deployed-v2 authentication next.");
                }
            };
            boundTransport.MessageReceived += raw =>
                EnqueueInboundMessage(boundTransport, generation, raw);
            boundTransport.Error += error =>
            {
                if (IsCurrent(boundTransport, generation) && !error.IsCancellation)
                {
                    diagnostics.Write(DiagnosticLogLevel.Warning, "Transport", $"WebSocket {error.Operation} failed.", error.Exception);
                    status.SetError($"WebSocket {error.Operation} failed. Check the Console for the safe error type.");
                }
            };
            boundTransport.Closed += close =>
                Observe(HandleClosedAsync(boundTransport, generation, close), "Close handling failed.");
        }

        private async Task HandleMessageAsync(
            IWebSocketTransport source,
            int generation,
            string rawJson)
        {
            if (!TryGetCurrent(source, generation, out var activeSessionId, out var cancellationToken))
            {
                return;
            }

            var result = await router.RouteAsync(rawJson, activeSessionId, cancellationToken);
            await lifecycleGate.WaitAsync();
            try
            {
                if (!IsCurrent(source, generation))
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Verbose,
                        "Routing",
                        "Ignored a handler result from a stale connection generation.");
                    return;
                }

                var eventLabel = result.Status == ProtocolRouteStatus.IgnoredUnknown
                    ? "unknown"
                    : result.EventType;
                if (!string.IsNullOrEmpty(eventLabel))
                {
                    status.SetLastEvent(eventLabel);
                }

                if (result.WasHandled)
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Information,
                        "Protocol",
                        $"Handled inbound '{eventLabel}' frame.");
                }

                if ((result.Status == ProtocolRouteStatus.Handled ||
                     result.Status == ProtocolRouteStatus.HandlerFailed) &&
                    result.EventType == ProtocolEventTypes.SystemError &&
                    result.Envelope != null &&
                    dialect.TryReadSystemError(result.Envelope, out var systemError, out _))
                {
                    CompleteFinalTurn();
                    await HandleCurrentSystemErrorAsync(systemError);
                    return;
                }

                if (result.WasHandled && result.EventType == ProtocolEventTypes.AiResponse)
                {
                    CompleteFinalTurn();
                    status.SetError(string.Empty);
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        /// <summary>Called only while lifecycleGate is held for the current connection generation.</summary>
        private async Task HandleCurrentSystemErrorAsync(SystemErrorPayload systemError)
        {
            var diagnosticCode = ProtocolSystemErrorCodes.ToDiagnosticLabel(systemError.Code);
            diagnostics.Write(
                DiagnosticLogLevel.Warning,
                "Protocol",
                $"Backend reported system.error code '{diagnosticCode}'; the message was not logged.");

            if (!ProtocolSystemErrorCodes.IsAuthenticationFailure(systemError.Code))
            {
                status.SetError($"Backend reported {diagnosticCode}.");
                return;
            }

            diagnostics.Write(
                DiagnosticLogLevel.Warning,
                "Authentication",
                "Backend rejected the access token or avatar ownership (AUTH_FAILED). " +
                "A refreshable auth session may recover automatically; otherwise obtain a fresh login.");
            status.SetError(
                "Authentication failed. If a refresh token is available the client will retry once; " +
                "otherwise log in again and confirm the avatar belongs to the same account.");
            stateMachine.TryTransition(SessionState.AuthRequired);
            await ClearConnectionAsync(
                true,
                CancellationToken.None,
                "authentication rejected");
            stateMachine.TryTransition(SessionState.AuthRequired);
        }

        private void EnqueueInboundMessage(
            IWebSocketTransport source,
            int generation,
            string rawJson)
        {
            if (!IsCurrent(source, generation))
            {
                return;
            }

            Task previous;
            TaskCompletionSource<bool> completion;
            lock (inboundQueueGate)
            {
                if (pendingInboundMessages >= MaximumPendingInboundMessages)
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Warning,
                        "Routing",
                        "Inbound message queue limit reached; resetting the socket.");
                    Observe(
                        source.CloseAsync(WebSocketCloseCode.PolicyViolation, "inbound queue limit"),
                        "Inbound queue close failed.");
                    return;
                }

                pendingInboundMessages++;
                previous = inboundTail;
                completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                inboundTail = completion.Task;
            }

            _ = ProcessQueuedInboundAsync(
                previous,
                completion,
                source,
                generation,
                rawJson);
        }

        private async Task ProcessQueuedInboundAsync(
            Task previous,
            TaskCompletionSource<bool> completion,
            IWebSocketTransport source,
            int generation,
            string rawJson)
        {
            try
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                    // Each queue item isolates its own failure so later frames keep moving.
                }

                await HandleMessageAsync(source, generation, rawJson).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                diagnostics.Write(
                    DiagnosticLogLevel.Error,
                    "Routing",
                    "Inbound message handling failed.",
                    exception);
            }
            finally
            {
                lock (inboundQueueGate)
                {
                    pendingInboundMessages--;
                }

                completion.TrySetResult(true);
            }
        }

        private async Task HandleClosedAsync(
            IWebSocketTransport source,
            int generation,
            TransportCloseInfo closeInfo)
        {
            if (!IsCurrent(source, generation))
            {
                return;
            }

            await lifecycleGate.WaitAsync();
            try
            {
                if (!IsCurrent(source, generation))
                {
                    return;
                }

                await ClearConnectionAsync(false, CancellationToken.None);
                var decision = closePolicy.Decide(closeInfo, stopRequested);
                if (closeInfo?.Code == 4002)
                {
                    if (!sessionMismatchRetryUsed)
                    {
                        sessionMismatchRetryUsed = true;
                        decision = new ClosePolicyDecision(CloseDirective.Reconnect, TimeSpan.Zero);
                    }
                    else
                    {
                        decision = new ClosePolicyDecision(CloseDirective.Fault, TimeSpan.Zero);
                    }
                }

                var closeCode = closeInfo?.Code;
                var closeLevel = closeCode == WebSocketCloseCode.NormalClosure
                    ? DiagnosticLogLevel.Information
                    : DiagnosticLogLevel.Warning;
                diagnostics.Write(
                    closeLevel,
                    "Transport",
                    $"WebSocket closed (code {(closeCode.HasValue ? closeCode.Value.ToString() : "none")}, " +
                    $"{(closeInfo?.RemoteInitiated == true ? "remote" : "local")}, " +
                    $"{(closeInfo?.WasClean == true ? "clean" : "unclean")}); policy={decision.Directive}.");
                status.SetLastClose(
                    closeCode,
                    closeInfo?.WasClean == true,
                    closeInfo?.RemoteInitiated == true,
                    decision.Directive.ToString());

                switch (decision.Directive)
                {
                    case CloseDirective.StayDisconnected:
                        stateMachine.TryTransition(SessionState.Disconnected);
                        break;
                    case CloseDirective.Fault:
                        stateMachine.TryTransition(SessionState.Faulted);
                        status.SetError("The backend rejected the current protocol message.");
                        break;
                    case CloseDirective.RequireAuthentication:
                        stateMachine.TryTransition(SessionState.AuthRequired);
                        status.SetError("Authentication must be renewed.");
                        break;
                    case CloseDirective.Reconnect:
                        stateMachine.TryTransition(SessionState.Reconnecting);
                        StartReconnectLoop(decision.MinimumDelay);
                        break;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        private void StartHeartbeat(CancellationToken cancellationToken)
        {
            var scheduler = new HeartbeatScheduler(
                options.HeartbeatInterval,
                token => SendHeartbeatAsync(token));
            scheduler.Faulted += exception =>
            {
                diagnostics.Write(DiagnosticLogLevel.Warning, "Heartbeat", "Heartbeat send failed.", exception);
                ForceReconnect();
            };
            heartbeat = scheduler;
            scheduler.Start(cancellationToken);
        }

        private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
        {
            var result = await SendDomainMessageAsync(
                ProtocolEventTypes.Heartbeat,
                id => dialect.CreateHeartbeat(id),
                cancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Heartbeat could not be sent on the active session.");
            }
        }

        private async Task<CohostSendResult> SendDomainMessageAsync(
            string eventType,
            Func<string, string> createJson,
            CancellationToken cancellationToken)
        {
            if (State != SessionState.Ready)
            {
                return CohostSendResult.Failure(CohostSendStatus.NotReady, "The session is not ready.");
            }

            IWebSocketTransport activeTransport;
            string activeSessionId;
            lock (connectionGate)
            {
                activeTransport = transport;
                activeSessionId = sessionId;
            }

            if (activeTransport == null || string.IsNullOrEmpty(activeSessionId))
            {
                return CohostSendResult.Failure(CohostSendStatus.NotReady, "There is no active socket session.");
            }

            string json;
            try
            {
                json = createJson(activeSessionId);
            }
            catch (ProtocolException exception)
            {
                return CohostSendResult.Failure(CohostSendStatus.ValidationFailed, exception.Error.Code.ToString());
            }

            try
            {
                await sendGate.WaitAsync(cancellationToken);
                try
                {
                    if (State != SessionState.Ready || !ReferenceEquals(activeTransport, transport))
                    {
                        return CohostSendResult.Failure(CohostSendStatus.NotReady, "The session changed before send.");
                    }

                    if (!outboundRateLimiter.TryReserve(out var retryAfter))
                    {
                        diagnostics.Write(
                            DiagnosticLogLevel.Warning,
                            "Protocol",
                            $"Rejected outbound '{eventType}' because the local v2 rate limit was reached.");
                        return CohostSendResult.RateLimited(retryAfter);
                    }

                    await activeTransport.SendTextAsync(json, cancellationToken);
                    status.SetLastOutboundEvent(eventType);
                    var logLevel = eventType == ProtocolEventTypes.Heartbeat ||
                                   eventType == ProtocolEventTypes.SttPartial ||
                                   eventType == ProtocolEventTypes.StateAck
                        ? DiagnosticLogLevel.Verbose
                        : DiagnosticLogLevel.Information;
                    diagnostics.Write(
                        logLevel,
                        "Protocol",
                        $"Sent outbound '{eventType}' frame; payload content was not logged.");
                    return CohostSendResult.Sent();
                }
                finally
                {
                    sendGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CohostSendResult.Failure(CohostSendStatus.Cancelled, "The send was cancelled.");
            }
            catch (Exception exception)
            {
                diagnostics.Write(DiagnosticLogLevel.Warning, "Transport", "A session message could not be sent.", exception);
                return CohostSendResult.Failure(CohostSendStatus.TransportFailed, "The transport send failed.");
            }
        }

        private void StartFinalTurnTimeout(long turnToken)
        {
            finalTurnTimeoutCancellation?.Cancel();
            finalTurnTimeoutCancellation?.Dispose();
            finalTurnTimeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            Observe(
                RunFinalTurnTimeoutAsync(turnToken, finalTurnTimeoutCancellation.Token),
                "Final-turn timeout handling failed.");
        }

        private async Task RunFinalTurnTimeoutAsync(long token, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(options.FinalTurnResponseTimeout, cancellationToken);
                if (finalTurnGate.Complete(token))
                {
                    status.SetError("The response timed out; the turn was not replayed.");
                    diagnostics.Write(DiagnosticLogLevel.Warning, "Turns", "Final transcript response timed out.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void CompleteFinalTurn()
        {
            finalTurnTimeoutCancellation?.Cancel();
            finalTurnTimeoutCancellation?.Dispose();
            finalTurnTimeoutCancellation = null;
            finalTurnGate.CompleteActive();
        }

        private void StartReconnectLoop(TimeSpan minimumFirstDelay)
        {
            lock (connectionGate)
            {
                if (stopRequested || (reconnectTask != null && !reconnectTask.IsCompleted))
                {
                    return;
                }

                reconnectTask = ReconnectLoopAsync(minimumFirstDelay, lifetimeCancellation.Token);
            }
        }

        private async Task ReconnectLoopAsync(TimeSpan minimumFirstDelay, CancellationToken cancellationToken)
        {
            var minimumDelay = minimumFirstDelay;
            while (!cancellationToken.IsCancellationRequested && !stopRequested)
            {
                if (State == SessionState.AuthRequired ||
                    State == SessionState.Faulted ||
                    State == SessionState.Stopping)
                {
                    return;
                }

                var attempt = Interlocked.Increment(ref reconnectAttempt);
                if (!reconnectController.CanAttempt(attempt))
                {
                    stateMachine.TryTransition(SessionState.Faulted);
                    status.SetError("Reconnect attempts were exhausted.");
                    return;
                }

                status.SetReconnectAttempt(attempt);
                var delay = reconnectController.GetDelay(attempt, minimumDelay);
                minimumDelay = TimeSpan.Zero;
                diagnostics.Write(
                    DiagnosticLogLevel.Information,
                    "Reconnect",
                    $"Reconnect attempt {attempt} is scheduled in {delay.TotalSeconds:0.0}s.");
                try
                {
                    await Task.Delay(delay, cancellationToken);
                    await ConnectOnceAsync(true, cancellationToken);
                    if (State == SessionState.Ready)
                    {
                        return;
                    }

                    if (State == SessionState.AuthRequired || State == SessionState.Faulted)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    diagnostics.Write(DiagnosticLogLevel.Warning, "Reconnect", $"Reconnect attempt {attempt} failed.", exception);
                    if (State == SessionState.AuthRequired || State == SessionState.Faulted)
                    {
                        return;
                    }

                    stateMachine.TryTransition(SessionState.Reconnecting);
                }
            }
        }

        private void ForceReconnect()
        {
            IWebSocketTransport active;
            lock (connectionGate)
            {
                active = transport;
            }

            if (active != null)
            {
                Observe(active.CloseAsync(1011, "heartbeat failure"), "Heartbeat failure close failed.");
            }
        }

        private async Task ClearConnectionAsync(
            bool sendClose,
            CancellationToken cancellationToken,
            string closeReason = "client shutdown")
        {
            HeartbeatScheduler oldHeartbeat;
            IWebSocketTransport oldTransport;
            CancellationTokenSource oldCancellation;
            lock (connectionGate)
            {
                oldHeartbeat = heartbeat;
                heartbeat = null;
                oldTransport = transport;
                transport = null;
                oldCancellation = connectionCancellation;
                connectionCancellation = null;
                sessionId = null;
            }

            oldCancellation?.Cancel();
            CompleteFinalTurn();
            finalTurnGate.Reset();
            status.SetSession(false);

            if (oldHeartbeat != null)
            {
                await oldHeartbeat.StopAsync();
                oldHeartbeat.Dispose();
            }

            if (oldTransport != null)
            {
                if (sendClose && oldTransport.State == TransportState.Open)
                {
                    try
                    {
                        await oldTransport.CloseAsync(
                            WebSocketCloseCode.NormalClosure,
                            closeReason,
                            cancellationToken);
                    }
                    catch (Exception exception) when (!(exception is OperationCanceledException))
                    {
                        diagnostics.Write(DiagnosticLogLevel.Verbose, "Transport", "Graceful close did not complete.", exception);
                    }
                }

                oldTransport.Dispose();
            }

            oldCancellation?.Dispose();
        }

        private void EnsureCurrent(IWebSocketTransport expected, int generation)
        {
            if (!IsCurrent(expected, generation) || expected.State != TransportState.Open)
            {
                throw new InvalidOperationException("The socket closed before authentication completed.");
            }
        }

        private bool IsCurrent(IWebSocketTransport expected, int generation)
        {
            lock (connectionGate)
            {
                return generation == connectionGeneration && ReferenceEquals(expected, transport);
            }
        }

        private bool TryGetCurrent(
            IWebSocketTransport expected,
            int generation,
            out string activeSessionId,
            out CancellationToken cancellationToken)
        {
            lock (connectionGate)
            {
                if (generation != connectionGeneration || !ReferenceEquals(expected, transport) || connectionCancellation == null)
                {
                    activeSessionId = null;
                    cancellationToken = CancellationToken.None;
                    return false;
                }

                activeSessionId = sessionId;
                cancellationToken = connectionCancellation.Token;
                return true;
            }
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            status.SetState(next, options.Endpoint);
            diagnostics.Write(
                DiagnosticLogLevel.Information,
                "Session",
                $"State changed: {previous} -> {next}.");
            if (dispatcher == null || dispatcher.IsMainThread)
            {
                StateChanged?.Invoke(previous, next);
                return;
            }

            dispatcher.Post(() => StateChanged?.Invoke(previous, next));
        }

        private void EnsureLifetimeCancellation()
        {
            if (!lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            lifetimeCancellation.Dispose();
            lifetimeCancellation = new CancellationTokenSource();
        }

        private void Observe(Task task, string failureMessage)
        {
            if (task == null)
            {
                return;
            }

            _ = ObserveCoreAsync(task, failureMessage);
        }

        private async Task ObserveCoreAsync(Task task, string failureMessage)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                diagnostics.Write(DiagnosticLogLevel.Error, "Session", failureMessage, exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CohostSessionController));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopRequested = true;
            lifetimeCancellation.Cancel();

            IWebSocketTransport oldTransport;
            HeartbeatScheduler oldHeartbeat;
            CancellationTokenSource oldConnectionCancellation;
            lock (connectionGate)
            {
                Interlocked.Increment(ref connectionGeneration);
                oldTransport = transport;
                transport = null;
                oldHeartbeat = heartbeat;
                heartbeat = null;
                oldConnectionCancellation = connectionCancellation;
                connectionCancellation = null;
                sessionId = null;
            }

            oldConnectionCancellation?.Cancel();
            finalTurnTimeoutCancellation?.Cancel();
            oldHeartbeat?.Dispose();
            oldTransport?.Dispose();
            finalTurnGate.Reset();
            status.SetSession(false);
            lifetimeCancellation.Dispose();
            oldConnectionCancellation?.Dispose();
            finalTurnTimeoutCancellation?.Dispose();

            // In-flight continuations can still unwind after cancellation. The two small
            // semaphores intentionally remain alive so teardown cannot race their finally blocks.
        }
    }
}
