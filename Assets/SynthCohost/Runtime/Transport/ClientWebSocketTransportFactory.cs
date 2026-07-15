namespace SynthCohost.Transport
{
    /// <summary>Creates one-shot .NET ClientWebSocket transports.</summary>
    public sealed class ClientWebSocketTransportFactory : IWebSocketTransportFactory
    {
        public IWebSocketTransport Create()
        {
            return new ClientWebSocketTransport();
        }
    }
}
