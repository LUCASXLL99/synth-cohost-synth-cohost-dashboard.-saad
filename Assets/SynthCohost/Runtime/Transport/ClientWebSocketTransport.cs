using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Transport
{
    /// <summary>
    /// Windows/.NET transport backed by <see cref="ClientWebSocket"/>.
    /// The instance is intentionally one-shot; use the factory for reconnects.
    /// </summary>
    public sealed class ClientWebSocketTransport : IWebSocketTransport
    {
        public const int MaximumReceiveMessageBytes = 64 * 1024;

        private const int ReceiveBufferBytes = 8 * 1024;
        private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(5);

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly TaskCompletionSource<TransportCloseInfo> _closeCompletion =
            new TaskCompletionSource<TransportCloseInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        private ClientWebSocket _socket;
        private CancellationTokenSource _lifetimeCancellation;
        private Task _receiveLoopTask;
        private int _state = (int)TransportState.Created;
        private int _closeNotificationSent;
        private int _disposed;
        private bool _localCloseRequested;
        private int _localCloseCode = WebSocketCloseCode.NormalClosure;
        private string _localCloseReason = string.Empty;

        public TransportState State => (TransportState)Volatile.Read(ref _state);

        public event Action Opened = delegate { };

        public event Action<string> MessageReceived = delegate { };

        public event Action<TransportErrorInfo> Error = delegate { };

        public event Action<TransportCloseInfo> Closed = delegate { };

        public async Task ConnectAsync(
            Uri endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(endpoint);
            ValidateConnectTimeout(timeout);
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)TransportState.Connecting,
                    (int)TransportState.Created) != (int)TransportState.Created)
            {
                throw new InvalidOperationException(
                    "A WebSocket transport is one-shot and can only connect once.");
            }

            var socket = new ClientWebSocket();
            var lifetimeCancellation = new CancellationTokenSource();
            lock (_sync)
            {
                _socket = socket;
                _lifetimeCancellation = lifetimeCancellation;
            }

            using (var timeoutCancellation = CreateTimeoutCancellation(timeout))
            using (var linkedCancellation = CreateConnectCancellation(
                       cancellationToken,
                       lifetimeCancellation.Token,
                       timeoutCancellation))
            {
                try
                {
                    await socket.ConnectAsync(endpoint, linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (linkedCancellation.IsCancellationRequested)
                {
                    var timedOut = timeoutCancellation != null &&
                                   timeoutCancellation.IsCancellationRequested &&
                                   !cancellationToken.IsCancellationRequested &&
                                   !lifetimeCancellation.IsCancellationRequested;

                    if (timedOut)
                    {
                        var timeoutException = new TimeoutException(
                            $"WebSocket connection did not open within {timeout.TotalSeconds:0.###} seconds.",
                            exception);
                        CompleteConnectFailure(
                            socket,
                            timeoutException,
                            reportError: true,
                            reason: "WebSocket connection timed out.");
                        throw timeoutException;
                    }

                    CompleteCanceledConnect(socket);
                    var cancellationCause = cancellationToken.IsCancellationRequested
                        ? cancellationToken
                        : lifetimeCancellation.Token;
                    throw new OperationCanceledException(
                        "The WebSocket connection was cancelled.",
                        exception,
                        cancellationCause);
                }
                catch (Exception exception)
                {
                    CompleteConnectFailure(
                        socket,
                        exception,
                        reportError: true,
                        reason: "WebSocket connection failed.");
                    throw;
                }
            }

            if (Volatile.Read(ref _disposed) != 0 || lifetimeCancellation.IsCancellationRequested)
            {
                AbortAndDispose(socket);
                NotifyClosedOnce(BuildRequestedOrAbnormalClose("Transport closed during connection."));
                throw new OperationCanceledException("The transport was closed while connecting.");
            }

            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)TransportState.Open,
                    (int)TransportState.Connecting) != (int)TransportState.Connecting)
            {
                AbortAndDispose(socket);
                NotifyClosedOnce(BuildRequestedOrAbnormalClose("Transport closed during connection."));
                throw new OperationCanceledException("The transport was closed while connecting.");
            }

            // Opened is deliberately raised before the receive loop starts so it
            // is always the first connection callback. Subscribers must still
            // treat callbacks as worker-thread events.
            InvokeSafely(Opened);

            // A subscriber may request a graceful close from Opened. The receive
            // loop is still required in Closing state to consume the peer's close
            // acknowledgement and finish that handshake without timing out.
            if ((State == TransportState.Open || State == TransportState.Closing) &&
                Volatile.Read(ref _disposed) == 0)
            {
                var receiveLoop = ReceiveLoopAsync(socket, lifetimeCancellation.Token);
                lock (_sync)
                {
                    _receiveLoopTask = receiveLoop;
                }
            }
        }

        public async Task SendTextAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            ThrowIfDisposed();
            EnsureOpenForSend();

            var bytes = Encoding.UTF8.GetBytes(message);
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureOpenForSend();
                var socket = GetSocket();
                if (socket == null)
                {
                    throw new InvalidOperationException("The WebSocket is not available.");
                }

                await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposed) == 0 && State != TransportState.Closing)
                {
                    FailActiveTransport(
                        TransportOperation.Send,
                        "WebSocket text send failed.",
                        exception);
                }

                throw;
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task CloseAsync(
            int code = WebSocketCloseCode.NormalClosure,
            string reason = "",
            CancellationToken cancellationToken = default)
        {
            WebSocketCloseCode.ValidateToSend(code, reason);

            if (Volatile.Read(ref _disposed) != 0 || _closeCompletion.Task.IsCompleted)
            {
                return;
            }

            bool initiateClose;
            lock (_sync)
            {
                if (_localCloseRequested || State == TransportState.Closing)
                {
                    initiateClose = false;
                }
                else
                {
                    _localCloseRequested = true;
                    _localCloseCode = code;
                    _localCloseReason = reason ?? string.Empty;
                    initiateClose = true;
                }
            }

            if (initiateClose)
            {
                await InitiateCloseAsync(code, reason ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await WaitForCloseCompletionAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _state, (int)TransportState.Disposed);

            var cancellation = GetLifetimeCancellation();
            TryCancel(cancellation);

            var socket = GetSocket();
            AbortAndDispose(socket);

            NotifyClosedOnce(BuildRequestedOrAbnormalClose("Transport disposed."));
        }

        private async Task ReceiveLoopAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            var receiveBuffer = new byte[ReceiveBufferBytes];
            TransportCloseInfo closeInfo = null;

            using (var accumulator = new TextMessageAccumulator(MaximumReceiveMessageBytes))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var result = await socket.ReceiveAsync(
                                new ArraySegment<byte>(receiveBuffer),
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            closeInfo = await HandleRemoteCloseAsync(socket, result)
                                .ConfigureAwait(false);
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            var exception = new InvalidDataException(
                                "Binary WebSocket frames are not supported by the current protocol.");
                            RaiseError(new TransportErrorInfo(
                                TransportOperation.Protocol,
                                exception.Message,
                                exception));
                            var sent = await TrySendCloseFrameAsync(
                                    socket,
                                    WebSocketCloseCode.UnsupportedData,
                                    "Binary messages are not supported.")
                                .ConfigureAwait(false);
                            closeInfo = new TransportCloseInfo(
                                WebSocketCloseCode.UnsupportedData,
                                "Binary messages are not supported.",
                                sent,
                                remoteInitiated: false);
                            break;
                        }

                        string message;
                        if (accumulator.Append(
                                receiveBuffer,
                                0,
                                result.Count,
                                result.EndOfMessage,
                                out message))
                        {
                            InvokeSafely(MessageReceived, message);
                        }
                    }
                }
                catch (TransportMessageTooLargeException exception)
                {
                    RaiseError(new TransportErrorInfo(
                        TransportOperation.Protocol,
                        exception.Message,
                        exception));
                    var sent = await TrySendCloseFrameAsync(
                            socket,
                            WebSocketCloseCode.MessageTooBig,
                            "Message exceeds 64 KiB receive limit.")
                        .ConfigureAwait(false);
                    closeInfo = new TransportCloseInfo(
                        WebSocketCloseCode.MessageTooBig,
                        "Message exceeds 64 KiB receive limit.",
                        sent,
                        remoteInitiated: false);
                }
                catch (DecoderFallbackException exception)
                {
                    RaiseError(new TransportErrorInfo(
                        TransportOperation.Protocol,
                        "Received text was not valid UTF-8.",
                        exception));
                    var sent = await TrySendCloseFrameAsync(
                            socket,
                            WebSocketCloseCode.InvalidPayloadData,
                            "Text message is not valid UTF-8.")
                        .ConfigureAwait(false);
                    closeInfo = new TransportCloseInfo(
                        WebSocketCloseCode.InvalidPayloadData,
                        "Text message is not valid UTF-8.",
                        sent,
                        remoteInitiated: false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    closeInfo = BuildRequestedOrAbnormalClose("WebSocket receive was canceled.");
                }
                catch (Exception exception)
                {
                    if (Volatile.Read(ref _disposed) == 0 &&
                        State != TransportState.Closing &&
                        State != TransportState.Faulted)
                    {
                        Interlocked.Exchange(ref _state, (int)TransportState.Faulted);
                        RaiseError(new TransportErrorInfo(
                            TransportOperation.Receive,
                            "WebSocket receive failed.",
                            exception));
                    }

                    closeInfo = BuildRequestedOrAbnormalClose("WebSocket receive failed.");
                }
                finally
                {
                    NotifyClosedOnce(closeInfo ??
                                     BuildRequestedOrAbnormalClose("WebSocket receive loop ended."));
                    ClearAndDisposeSocket(socket);
                }
            }
        }

        private async Task<TransportCloseInfo> HandleRemoteCloseAsync(
            ClientWebSocket socket,
            WebSocketReceiveResult result)
        {
            bool localCloseRequested;
            int localCode;
            string localReason;
            lock (_sync)
            {
                localCloseRequested = _localCloseRequested;
                localCode = _localCloseCode;
                localReason = _localCloseReason;
            }

            SetClosingUnlessTerminal();

            var code = result.CloseStatus.HasValue
                ? (int?)result.CloseStatus.Value
                : localCloseRequested ? localCode : (int?)null;
            var reason = !string.IsNullOrEmpty(result.CloseStatusDescription)
                ? result.CloseStatusDescription
                : localCloseRequested ? localReason : string.Empty;

            var acknowledged = true;
            if (!localCloseRequested && socket.State == WebSocketState.CloseReceived)
            {
                var responseCode = code.HasValue && WebSocketCloseCode.IsValidToSend(code.Value)
                    ? code.Value
                    : WebSocketCloseCode.NormalClosure;
                var responseReason = Encoding.UTF8.GetByteCount(reason) <=
                                     WebSocketCloseCode.MaximumReasonUtf8Bytes
                    ? reason
                    : string.Empty;
                acknowledged = await TrySendCloseFrameAsync(socket, responseCode, responseReason)
                    .ConfigureAwait(false);
            }

            return new TransportCloseInfo(
                code,
                reason,
                acknowledged,
                remoteInitiated: !localCloseRequested);
        }

        private async Task InitiateCloseAsync(
            int code,
            string reason,
            CancellationToken cancellationToken)
        {
            var state = State;
            if (state == TransportState.Created)
            {
                Interlocked.Exchange(ref _state, (int)TransportState.Closed);
                NotifyClosedOnce(new TransportCloseInfo(
                    code,
                    reason,
                    wasClean: true,
                    remoteInitiated: false));
                return;
            }

            SetClosingUnlessTerminal();

            if (state == TransportState.Connecting)
            {
                TryCancel(GetLifetimeCancellation());
                await WaitForCloseCompletionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (state != TransportState.Open && state != TransportState.Closing)
            {
                return;
            }

            var socket = GetSocket();
            if (socket == null)
            {
                NotifyClosedOnce(new TransportCloseInfo(
                    code,
                    reason,
                    wasClean: false,
                    remoteInitiated: false));
                return;
            }

            using (var timeoutCancellation = new CancellationTokenSource(CloseHandshakeTimeout))
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       timeoutCancellation.Token))
            {
                try
                {
                    await SendCloseFrameAsync(socket, code, reason, linkedCancellation.Token)
                        .ConfigureAwait(false);
                    await AwaitWithCancellation(
                            _closeCompletion.Task,
                            linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    AbortAndDispose(socket);
                    var timedOut = timeoutCancellation.IsCancellationRequested &&
                                   !cancellationToken.IsCancellationRequested;
                    if (timedOut)
                    {
                        var timeoutException = new TimeoutException(
                            "The WebSocket close handshake timed out.",
                            exception);
                        RaiseError(new TransportErrorInfo(
                            TransportOperation.Close,
                            timeoutException.Message,
                            timeoutException));
                        NotifyClosedOnce(new TransportCloseInfo(
                            code,
                            reason,
                            wasClean: false,
                            remoteInitiated: false));
                        throw timeoutException;
                    }

                    NotifyClosedOnce(new TransportCloseInfo(
                        code,
                        reason,
                        wasClean: false,
                        remoteInitiated: false));
                    throw;
                }
                catch (Exception exception)
                {
                    AbortAndDispose(socket);
                    RaiseError(new TransportErrorInfo(
                        TransportOperation.Close,
                        "WebSocket close failed.",
                        exception));
                    NotifyClosedOnce(new TransportCloseInfo(
                        code,
                        reason,
                        wasClean: false,
                        remoteInitiated: false));
                    throw;
                }
            }
        }

        private async Task WaitForCloseCompletionAsync(CancellationToken cancellationToken)
        {
            using (var timeoutCancellation = new CancellationTokenSource(CloseHandshakeTimeout))
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       timeoutCancellation.Token))
            {
                try
                {
                    await AwaitWithCancellation(_closeCompletion.Task, linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    if (timeoutCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested)
                    {
                        var timeoutException = new TimeoutException(
                            "Waiting for the WebSocket to close timed out.",
                            exception);
                        RaiseError(new TransportErrorInfo(
                            TransportOperation.Close,
                            timeoutException.Message,
                            timeoutException));
                        AbortAndDispose(GetSocket());
                        NotifyClosedOnce(BuildRequestedOrAbnormalClose(
                            "WebSocket close handshake timed out."));
                        throw timeoutException;
                    }

                    AbortAndDispose(GetSocket());
                    NotifyClosedOnce(BuildRequestedOrAbnormalClose(
                        "WebSocket close was canceled."));
                    throw;
                }
            }
        }

        private async Task<bool> TrySendCloseFrameAsync(
            ClientWebSocket socket,
            int code,
            string reason)
        {
            using (var cancellation = new CancellationTokenSource(CloseHandshakeTimeout))
            {
                try
                {
                    await SendCloseFrameAsync(socket, code, reason, cancellation.Token)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (Exception exception)
                {
                    RaiseError(new TransportErrorInfo(
                        TransportOperation.Close,
                        "Failed to send a WebSocket close frame.",
                        exception,
                        exception is OperationCanceledException));
                    return false;
                }
            }
        }

        private async Task SendCloseFrameAsync(
            ClientWebSocket socket,
            int code,
            string reason,
            CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open ||
                    socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(
                            (WebSocketCloseStatus)code,
                            reason,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private void CompleteCanceledConnect(ClientWebSocket socket)
        {
            AbortAndDispose(socket);
            if (State != TransportState.Disposed && State != TransportState.Closing)
            {
                Interlocked.Exchange(ref _state, (int)TransportState.Closed);
            }

            NotifyClosedOnce(BuildRequestedOrAbnormalClose("WebSocket connection was canceled."));
        }

        private void CompleteConnectFailure(
            ClientWebSocket socket,
            Exception exception,
            bool reportError,
            string reason)
        {
            AbortAndDispose(socket);
            if (State != TransportState.Disposed)
            {
                Interlocked.Exchange(ref _state, (int)TransportState.Faulted);
            }

            if (reportError)
            {
                RaiseError(new TransportErrorInfo(
                    TransportOperation.Connect,
                    reason,
                    exception));
            }

            NotifyClosedOnce(new TransportCloseInfo(
                code: null,
                reason,
                wasClean: false,
                remoteInitiated: false));
        }

        private void FailActiveTransport(
            TransportOperation operation,
            string message,
            Exception exception)
        {
            if (State != TransportState.Disposed)
            {
                Interlocked.Exchange(ref _state, (int)TransportState.Faulted);
            }

            RaiseError(new TransportErrorInfo(operation, message, exception));
            TryCancel(GetLifetimeCancellation());
            AbortAndDispose(GetSocket());
            NotifyClosedOnce(new TransportCloseInfo(
                code: null,
                message,
                wasClean: false,
                remoteInitiated: false));
        }

        private TransportCloseInfo BuildRequestedOrAbnormalClose(string fallbackReason)
        {
            lock (_sync)
            {
                if (_localCloseRequested)
                {
                    return new TransportCloseInfo(
                        _localCloseCode,
                        _localCloseReason,
                        wasClean: false,
                        remoteInitiated: false);
                }
            }

            return new TransportCloseInfo(
                code: null,
                fallbackReason,
                wasClean: false,
                remoteInitiated: false);
        }

        private void NotifyClosedOnce(TransportCloseInfo closeInfo)
        {
            if (closeInfo == null ||
                Interlocked.Exchange(ref _closeNotificationSent, 1) != 0)
            {
                return;
            }

            var state = State;
            if (state != TransportState.Faulted && state != TransportState.Disposed)
            {
                Interlocked.Exchange(ref _state, (int)TransportState.Closed);
            }

            _closeCompletion.TrySetResult(closeInfo);
            InvokeSafely(Closed, closeInfo);
        }

        private void RaiseError(TransportErrorInfo errorInfo)
        {
            InvokeSafely(Error, errorInfo);
        }

        private void SetClosingUnlessTerminal()
        {
            var state = State;
            while (state != TransportState.Closed &&
                   state != TransportState.Faulted &&
                   state != TransportState.Disposed &&
                   state != TransportState.Closing)
            {
                var original = (TransportState)Interlocked.CompareExchange(
                    ref _state,
                    (int)TransportState.Closing,
                    (int)state);
                if (original == state)
                {
                    return;
                }

                state = original;
            }
        }

        private void EnsureOpenForSend()
        {
            if (State != TransportState.Open)
            {
                throw new InvalidOperationException(
                    $"Cannot send a WebSocket message while transport state is {State}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ClientWebSocketTransport));
            }
        }

        private ClientWebSocket GetSocket()
        {
            lock (_sync)
            {
                return _socket;
            }
        }

        private CancellationTokenSource GetLifetimeCancellation()
        {
            lock (_sync)
            {
                return _lifetimeCancellation;
            }
        }

        private void ClearAndDisposeSocket(ClientWebSocket socket)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                }
            }

            AbortAndDispose(socket);
        }

        private static void AbortAndDispose(ClientWebSocket socket)
        {
            if (socket == null)
            {
                return;
            }

            try
            {
                socket.Abort();
            }
            catch
            {
                // Best-effort terminal cleanup.
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
                // Dispose must remain idempotent and non-throwing.
            }
        }

        private static void TryCancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent terminal path already completed cleanup.
            }
        }

        private static CancellationTokenSource CreateTimeoutCancellation(TimeSpan timeout)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return null;
            }

            return new CancellationTokenSource(timeout);
        }

        private static CancellationTokenSource CreateConnectCancellation(
            CancellationToken callerCancellation,
            CancellationToken lifetimeCancellation,
            CancellationTokenSource timeoutCancellation)
        {
            return timeoutCancellation == null
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellation,
                    lifetimeCancellation)
                : CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellation,
                    lifetimeCancellation,
                    timeoutCancellation.Token);
        }

        private static void ValidateEndpoint(Uri endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (!endpoint.IsAbsoluteUri ||
                (!string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "The WebSocket endpoint must be an absolute ws:// or wss:// URI.",
                    nameof(endpoint));
            }
        }

        private static void ValidateConnectTimeout(TimeSpan timeout)
        {
            if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Connect timeout must be positive or Timeout.InfiniteTimeSpan.");
            }

            if (timeout != Timeout.InfiniteTimeSpan && timeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Connect timeout is too large for CancellationTokenSource.");
            }
        }

        private static async Task<T> AwaitWithCancellation<T>(
            Task<T> task,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            var cancellationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                       state => ((TaskCompletionSource<bool>)state).TrySetResult(true),
                       cancellationCompletion))
            {
                if (task != await Task.WhenAny(task, cancellationCompletion.Task)
                        .ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task.ConfigureAwait(false);
        }

        private static void InvokeSafely(Action handlers)
        {
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch
                {
                    // Consumer exceptions cannot terminate the socket loop.
                }
            }
        }

        private static void InvokeSafely<T>(Action<T> handlers, T argument)
        {
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(argument);
                }
                catch
                {
                    // Consumer exceptions cannot terminate the socket loop.
                }
            }
        }
    }
}
