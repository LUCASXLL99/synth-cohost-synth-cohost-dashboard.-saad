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
            ProtocolError protocolError = null,
            IncomingEnvelope envelope = null)
        {
            Status = status;
            EventType = eventType ?? string.Empty;
            ProtocolError = protocolError;
            Envelope = envelope;
        }

        public ProtocolRouteStatus Status { get; }
        public string EventType { get; }
        public ProtocolError ProtocolError { get; }
        /// <summary>
        /// Validated envelope when parsing succeeded. Session code may inspect terminal protocol
        /// semantics without parsing the same untrusted JSON frame twice.
        /// </summary>
        public IncomingEnvelope Envelope { get; }
        public bool WasHandled => Status == ProtocolRouteStatus.Handled;
    }
}
