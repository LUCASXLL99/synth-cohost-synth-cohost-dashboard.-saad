using System;

namespace SynthCohost.Protocol
{
    /// <summary>Exact current wire dialect: integer v=2 and a client-owned session ID on every frame.</summary>
    public sealed class DeployedV2ProtocolDialect : IProtocolDialect
    {
        private readonly Func<DateTimeOffset> _utcNow;

        public int Version => ProtocolConstants.DeployedVersion;
        public TimeSpan HeartbeatInterval => ProtocolConstants.DeployedHeartbeatInterval;
        public IProtocolCodec Codec { get; }

        public DeployedV2ProtocolDialect(IProtocolCodec codec = null, Func<DateTimeOffset> utcNow = null)
        {
            Codec = codec ?? new ProtocolCodec();
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public string GenerateSessionId()
        {
            return Guid.NewGuid().ToString("D");
        }

        public string CreateAuth(string sessionId, string accessToken, string avatarId)
        {
            return Create(
                ProtocolEventTypes.Auth,
                sessionId,
                new AuthPayload(accessToken, avatarId));
        }

        public string CreateHeartbeat(string sessionId)
        {
            return Create(ProtocolEventTypes.Heartbeat, sessionId, new HeartbeatPayload());
        }

        public string CreateSttPartial(string sessionId, string text)
        {
            return Create(ProtocolEventTypes.SttPartial, sessionId, new SttPartialPayload(text));
        }

        public string CreateSttFinal(string sessionId, string text)
        {
            return Create(ProtocolEventTypes.SttFinal, sessionId, new SttFinalPayload(text));
        }

        public string CreateStateAck(string sessionId, AvatarBehavior behavior)
        {
            return Create(ProtocolEventTypes.StateAck, sessionId, new StateAckPayload(behavior));
        }

        public bool TryCreateAuth(
            string sessionId,
            string accessToken,
            string avatarId,
            out string json,
            out ProtocolError error)
        {
            return TryCreate(ProtocolEventTypes.Auth, sessionId, new AuthPayload(accessToken, avatarId), out json, out error);
        }

        public bool TryCreateHeartbeat(string sessionId, out string json, out ProtocolError error)
        {
            return TryCreate(ProtocolEventTypes.Heartbeat, sessionId, new HeartbeatPayload(), out json, out error);
        }

        public bool TryCreateSttPartial(string sessionId, string text, out string json, out ProtocolError error)
        {
            return TryCreate(ProtocolEventTypes.SttPartial, sessionId, new SttPartialPayload(text), out json, out error);
        }

        public bool TryCreateSttFinal(string sessionId, string text, out string json, out ProtocolError error)
        {
            return TryCreate(ProtocolEventTypes.SttFinal, sessionId, new SttFinalPayload(text), out json, out error);
        }

        public bool TryCreateStateAck(
            string sessionId,
            AvatarBehavior behavior,
            out string json,
            out ProtocolError error)
        {
            return TryCreate(ProtocolEventTypes.StateAck, sessionId, new StateAckPayload(behavior), out json, out error);
        }

        public bool TryReadAvatarState(
            IncomingEnvelope envelope,
            out AvatarStatePayload payload,
            out ProtocolError error)
        {
            return Codec.TryParsePayload(envelope, out payload, out error);
        }

        public bool TryReadAiResponse(
            IncomingEnvelope envelope,
            out AiResponsePayload payload,
            out ProtocolError error)
        {
            return Codec.TryParsePayload(envelope, out payload, out error);
        }

        public bool TryReadSystemError(
            IncomingEnvelope envelope,
            out SystemErrorPayload payload,
            out ProtocolError error)
        {
            return Codec.TryParsePayload(envelope, out payload, out error);
        }

        public bool TryReadSpeechAudio(
            IncomingEnvelope envelope,
            out SpeechAudioPayload payload,
            out ProtocolError error)
        {
            return Codec.TryParsePayload(envelope, out payload, out error);
        }

        public bool TryReadSpeechFailed(
            IncomingEnvelope envelope,
            out SpeechFailedPayload payload,
            out ProtocolError error)
        {
            return Codec.TryParsePayload(envelope, out payload, out error);
        }

        private string Create<TPayload>(string eventType, string sessionId, TPayload payload)
        {
            if (!TryCreate(eventType, sessionId, payload, out var json, out var error))
            {
                throw new ProtocolException(error);
            }

            return json;
        }

        private bool TryCreate<TPayload>(
            string eventType,
            string sessionId,
            TPayload payload,
            out string json,
            out ProtocolError error)
        {
            var timestamp = ProtocolValidation.FormatUtcTimestamp(_utcNow());
            var envelope = new V2Envelope<TPayload>(Version, eventType, sessionId, timestamp, payload);
            return Codec.TrySerializeEnvelope(envelope, out json, out error);
        }
    }
}
