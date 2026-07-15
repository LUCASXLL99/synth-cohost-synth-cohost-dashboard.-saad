namespace SynthCohost.Transport
{
    /// <summary>
    /// Lifecycle state of a one-shot WebSocket transport instance.
    /// Create a new transport through <see cref="IWebSocketTransportFactory"/>
    /// for each reconnect attempt.
    /// </summary>
    public enum TransportState
    {
        Created,
        Connecting,
        Open,
        Closing,
        Closed,
        Faulted,
        Disposed
    }
}
