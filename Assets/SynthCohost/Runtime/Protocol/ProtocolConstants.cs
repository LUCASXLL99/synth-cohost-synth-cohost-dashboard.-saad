using System;

namespace SynthCohost.Protocol
{
    /// <summary>Hard limits and timing values in the currently deployed WebSocket v2 contract.</summary>
    public static class ProtocolConstants
    {
        public const int DeployedVersion = 2;
        public const int MaxSessionIdUtf8Bytes = 128;
        public const int MaxSttTextUtf8Bytes = 4_000;
        public const int MaxJsonUtf8Bytes = 64 * 1024;
        public const int MaxJsonDepth = 32;

        public static readonly TimeSpan DeployedHeartbeatInterval = TimeSpan.FromSeconds(20);
    }

    /// <summary>Exact, case-sensitive event names used by the deployed v2 service.</summary>
    public static class ProtocolEventTypes
    {
        public const string Auth = "auth";
        public const string Heartbeat = "heartbeat";
        public const string SttPartial = "stt.partial";
        public const string SttFinal = "stt.final";
        public const string StateAck = "state.ack";

        public const string AvatarState = "avatar.state";
        public const string AiResponse = "ai.response";
        public const string SystemError = "system.error";

        public static bool IsKnown(string eventType)
        {
            return IsClientToServer(eventType) || IsServerToClient(eventType);
        }

        public static bool IsClientToServer(string eventType)
        {
            switch (eventType)
            {
                case Auth:
                case Heartbeat:
                case SttPartial:
                case SttFinal:
                case StateAck:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsServerToClient(string eventType)
        {
            switch (eventType)
            {
                case AvatarState:
                case AiResponse:
                case SystemError:
                    return true;
                default:
                    return false;
            }
        }
    }
}
