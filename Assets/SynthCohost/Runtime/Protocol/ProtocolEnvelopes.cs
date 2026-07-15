using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SynthCohost.Protocol
{
    /// <summary>The exact envelope emitted to the currently deployed v2 backend.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class V2Envelope<TPayload>
    {
        [JsonProperty("v", Required = Required.Always, Order = 1)]
        public int Version { get; set; }

        [JsonProperty("type", Required = Required.Always, Order = 2)]
        public string EventType { get; set; }

        [JsonProperty("session_id", Required = Required.Always, Order = 3)]
        public string SessionId { get; set; }

        [JsonProperty("ts", Required = Required.Always, Order = 4)]
        public string Timestamp { get; set; }

        [JsonProperty("payload", Required = Required.Always, Order = 5)]
        public TPayload Payload { get; set; }

        public V2Envelope() { }

        public V2Envelope(int version, string eventType, string sessionId, string timestamp, TPayload payload)
        {
            Version = version;
            EventType = eventType;
            SessionId = sessionId;
            Timestamp = timestamp;
            Payload = payload;
        }
    }

    /// <summary>
    /// Validated envelope metadata plus an intentionally untyped payload. Inspecting the envelope never
    /// deserializes an unknown event payload, so additive event types cannot terminate the receive loop.
    /// </summary>
    public sealed class IncomingEnvelope
    {
        public int Version { get; }
        public string EventType { get; }
        public string SessionId { get; }
        public string Timestamp { get; }
        public DateTimeOffset TimestampUtc { get; }
        public JObject Payload { get; }
        public bool IsKnownEventType => ProtocolEventTypes.IsKnown(EventType);

        internal IncomingEnvelope(
            int version,
            string eventType,
            string sessionId,
            string timestamp,
            DateTimeOffset timestampUtc,
            JObject payload)
        {
            Version = version;
            EventType = eventType;
            SessionId = sessionId;
            Timestamp = timestamp;
            TimestampUtc = timestampUtc;
            Payload = payload;
        }
    }
}
