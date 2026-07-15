using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynthCohost.Transport
{
    /// <summary>
    /// One WebSocket connection attempt. Implementations do not capture a Unity
    /// synchronization context: event handlers may run on arbitrary worker threads.
    /// Runtime/session code must marshal callbacks before accessing Unity objects.
    /// </summary>
    public interface IWebSocketTransport : IDisposable
    {
        TransportState State { get; }

        event Action Opened;

        /// <summary>Raised once per complete, UTF-8 text message.</summary>
        event Action<string> MessageReceived;

        event Action<TransportErrorInfo> Error;

        /// <summary>Raised at most once for this transport instance.</summary>
        event Action<TransportCloseInfo> Closed;

        /// <summary>
        /// Connects to a ws:// or wss:// endpoint. The timeout covers DNS, TCP,
        /// TLS, and HTTP upgrade, and is independent from caller cancellation.
        /// A 75-second value supports the documented Render cold start.
        /// </summary>
        Task ConnectAsync(
            Uri endpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        Task SendTextAsync(
            string message,
            CancellationToken cancellationToken = default);

        Task CloseAsync(
            int code = WebSocketCloseCode.NormalClosure,
            string reason = "",
            CancellationToken cancellationToken = default);
    }
}
