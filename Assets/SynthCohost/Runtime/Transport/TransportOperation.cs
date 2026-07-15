namespace SynthCohost.Transport
{
    /// <summary>The transport operation that produced an error.</summary>
    public enum TransportOperation
    {
        Connect,
        Send,
        Receive,
        Close,
        Protocol
    }
}
