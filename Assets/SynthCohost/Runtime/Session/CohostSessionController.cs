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
        private bool authRefreshRetryUsed;
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
            authRefreshRetryUsed = false;
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
                    diagnostics.Write(DiagnosticLogLevel.Verbose, "Transport", "WebSocket opened.");
                }
            };
            boundTransport.MessageReceived += raw =>
                EnqueueInboundMessage(boundTransport, generation, raw);
            boundTransport.Error += error =>
            {
                if (IsCurrent(boundTransport, generation) && !error.IsCancellation)
                {
                    diagnostics.Write(DiagnosticLogLevel.Warning, "Transport", $"WebSocket {error.Operation} failed.", error.Exception);
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
            if (!string.IsNullOrEmpty(result.EventType))
            {
                status.SetLastEvent(result.EventType);
            }

            if (result.WasHandled &&
                (result.EventType == ProtocolEventTypes.AiResponse || result.EventType == ProtocolEventTypes.SystemError))
            {
                CompleteFinalTurn();
            }
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
                if (closeInfo?.Code == 4001 && !authRefreshRetryUsed)
                {
                    authRefreshRetryUsed = true;
                    decision = new ClosePolicyDecision(CloseDirective.Reconnect, TimeSpan.Zero);
                }
                else if (closeInfo?.Code == 4002)
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
                id => dialect.CreateHeartbeat(id),
                cancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Heartbeat could not be sent on the active session.");
            }
        }

        private async Task<CohostSendResult> SendDomainMessageAsync(
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
                        return CohostSendResult.RateLimited(retryAfter);
                    }

                    await activeTransport.SendTextAsync(json, cancellationToken);
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
                try
                {
                    await Task.Delay(delay, cancellationToken);
                    await ConnectOnceAsync(true, cancellationToken);
                    if (State == SessionState.Ready)
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

        private async Task ClearConnectionAsync(bool sendClose, CancellationToken cancellationToken)
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
                        await oldTransport.CloseAsync(WebSocketCloseCode.NormalClosure, "client shutdown", cancellationToken);
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
