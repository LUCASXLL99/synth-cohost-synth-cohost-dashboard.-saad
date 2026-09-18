using System;

namespace SynthCohost.Protocol
{
    /// <summary>
    /// Outbound and inbound contract boundary. A future protocol dialect can replace deployed v2
    /// without changing transport, session orchestration, or Unity presentation code.
    /// </summary>
    public interface IProtocolDialect
    {
        int Version { get; }
        TimeSpan HeartbeatInterval { get; }

        string GenerateSessionId();
        string CreateAuth(string sessionId, string accessToken, string avatarId);
        string CreateHeartbeat(string sessionId);
        string CreateSttPartial(string sessionId, string text);
        string CreateSttFinal(string sessionId, string text);
        string CreateStateAck(string sessionId, AvatarBehavior behavior);

        bool TryReadAvatarState(IncomingEnvelope envelope, out AvatarStatePayload payload, out ProtocolError error);
        bool TryReadAiResponse(IncomingEnvelope envelope, out AiResponsePayload payload, out ProtocolError error);
        bool TryReadSystemError(IncomingEnvelope envelope, out SystemErrorPayload payload, out ProtocolError error);
        bool TryReadSpeechAudio(IncomingEnvelope envelope, out SpeechAudioPayload payload, out ProtocolError error);
        bool TryReadSpeechFailed(IncomingEnvelope envelope, out SpeechFailedPayload payload, out ProtocolError error);
    }
}
