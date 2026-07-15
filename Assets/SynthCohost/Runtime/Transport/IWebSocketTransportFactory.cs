namespace SynthCohost.Transport
{
    public interface IWebSocketTransportFactory
    {
        IWebSocketTransport Create();
    }
}
