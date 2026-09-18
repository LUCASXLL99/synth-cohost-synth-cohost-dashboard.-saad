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
        public const string SpeechAudio = "speech.audio";
        public const string SpeechFailed = "speech.failed";

        /// <summary>
        /// Planned speech events that are not handled yet. Logged as a canned line.
        /// </summary>
        public static bool IsReservedUnimplementedSpeechEvent(string eventType)
        {
            switch (eventType)
            {
                case "speech.start":
                case "speech.end":
                case "speech.viseme":
                case "speech.chunk":
                    return true;
                default:
                    return false;
            }
        }

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
                case SpeechAudio:
                case SpeechFailed:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Known server error codes with session-level meaning for the deployed v2 client.</summary>
    public static class ProtocolSystemErrorCodes
    {
        public const string Unrecognized = "UNRECOGNIZED_SYSTEM_ERROR";
        public const string AuthFailed = "AUTH_FAILED";
        public const string AiGenerationFailed = "AI_GENERATION_FAILED";

        public static bool IsAuthenticationFailure(string code)
        {
            return string.Equals(code, AuthFailed, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns a bounded protocol-style identifier for logs/UI. Arbitrary backend text is not
        /// allowed through this path because error fields must never become a log-injection or
        /// accidental credential-disclosure channel.
        /// </summary>
        public static string ToDiagnosticLabel(string code)
        {
            const int maximumLength = 64;
            if (string.IsNullOrEmpty(code) || code.Length > maximumLength)
            {
                return Unrecognized;
            }

            for (var index = 0; index < code.Length; index++)
            {
                var character = code[index];
                var allowed = character == '_' ||
                              character >= 'A' && character <= 'Z' ||
                              character >= '0' && character <= '9';
                if (!allowed)
                {
                    return Unrecognized;
                }
            }

            return code;
        }
    }
}
