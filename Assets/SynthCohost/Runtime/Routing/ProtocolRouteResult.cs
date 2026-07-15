using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Routing
{
    public enum ProtocolRouteStatus
    {
        Handled,
        IgnoredUnknown,
        Malformed,
        SessionMismatch,
        HandlerFailed
    }

    public readonly struct ProtocolRouteResult
    {
        public ProtocolRouteResult(
            ProtocolRouteStatus status,
            string eventType,
            ProtocolError protocolError = null)
        {
            Status = status;
            EventType = eventType ?? string.Empty;
            ProtocolError = protocolError;
        }

        public ProtocolRouteStatus Status { get; }
        public string EventType { get; }
        public ProtocolError ProtocolError { get; }
        public bool WasHandled => Status == ProtocolRouteStatus.Handled;
    }
}
