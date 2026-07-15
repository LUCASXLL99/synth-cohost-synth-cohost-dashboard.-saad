namespace SynthCohost.Runtime.Session
{
    public enum SessionState
    {
        Disconnected,
        Connecting,
        Authenticating,
        Ready,
        Reconnecting,
        Stopping,
        Faulted,
        AuthRequired
    }
}
