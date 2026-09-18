using System;
using Newtonsoft.Json;

namespace SynthCohost.Protocol
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AuthPayload
    {
        [JsonProperty("token", Required = Required.Always, Order = 1)]
        public string Token { get; set; }

        [JsonProperty("avatar_id", Required = Required.Always, Order = 2)]
        public string AvatarId { get; set; }

        public AuthPayload() { }

        public AuthPayload(string token, string avatarId)
        {
            Token = token;
            AvatarId = avatarId;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class HeartbeatPayload
    {
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SttPartialPayload
    {
        [JsonProperty("text", Required = Required.Always, Order = 1)]
        public string Text { get; set; }

        [JsonProperty("final", Required = Required.Always, Order = 2)]
        public bool Final { get; set; }

        public SttPartialPayload() { }

        public SttPartialPayload(string text)
        {
            Text = text;
            Final = false;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SttFinalPayload
    {
        [JsonProperty("text", Required = Required.Always, Order = 1)]
        public string Text { get; set; }

        [JsonProperty("final", Required = Required.Always, Order = 2)]
        public bool Final { get; set; }

        public SttFinalPayload() { }

        public SttFinalPayload(string text)
        {
            Text = text;
            Final = true;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StateAckPayload
    {
        [JsonProperty("behavior", Required = Required.Always, Order = 1)]
        public AvatarBehavior Behavior { get; set; }

        public StateAckPayload() { }

        public StateAckPayload(AvatarBehavior behavior)
        {
            Behavior = behavior;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AvatarStatePayload
    {
        [JsonProperty("behavior", Required = Required.Always, Order = 1)]
        public AvatarBehavior Behavior { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AiResponsePayload
    {
        /// <summary>
        /// Spoken/caption body. Wire may send <c>text</c> (legacy) or <c>response</c> (LLM contract).
        /// </summary>
        [JsonIgnore]
        public string Text { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore, Order = 1)]
        private string TextWire
        {
            get => Text;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    Text = value;
                }
            }
        }

        [JsonProperty("response", NullValueHandling = NullValueHandling.Ignore, Order = 2)]
        private string ResponseWire
        {
            get => null;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    Text = value;
                }
            }
        }

        [JsonProperty("emotion", Required = Required.Always, Order = 3)]
        public AiEmotion Emotion { get; set; }

        [JsonProperty("intent", Required = Required.Always, Order = 4)]
        public AiIntent Intent { get; set; }

        /// <summary>
        /// Optional animation cue from the LLM layer (Avatar Response Contract).
        /// When set, preferred for this reply over a generic "always speaking" assumption.
        /// </summary>
        [JsonProperty("behavior", NullValueHandling = NullValueHandling.Ignore, Order = 5)]
        public AvatarBehavior? Behavior { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpeechInlineAudio
    {
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore, Order = 1)]
        public string Kind { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore, Order = 2)]
        public string Data { get; set; }

        [JsonIgnore]
        public string ContentType { get; set; }

        [JsonProperty("content_type", NullValueHandling = NullValueHandling.Ignore, Order = 3)]
        private string ContentTypeSnake
        {
            get => ContentType;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    ContentType = value;
                }
            }
        }

        [JsonProperty("contentType", NullValueHandling = NullValueHandling.Ignore, Order = 4)]
        private string ContentTypeCamel
        {
            get => null;
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    ContentType = value;
                }
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpeechMouthFrame
    {
        [JsonProperty("t", Order = 1)]
        public float TimeMs { get; set; }

        [JsonProperty("o", Order = 2)]
        public float Openness { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpeechAudioPayload
    {
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore, Order = 1)]
        public string Text { get; set; }

        [JsonProperty("duration_ms", NullValueHandling = NullValueHandling.Ignore, Order = 2)]
        public int DurationMs { get; set; }

        [JsonProperty("sample_rate", NullValueHandling = NullValueHandling.Ignore, Order = 3)]
        public int SampleRate { get; set; }

        [JsonProperty("audio", NullValueHandling = NullValueHandling.Ignore, Order = 4)]
        public SpeechInlineAudio Audio { get; set; }

        [JsonProperty("frames", NullValueHandling = NullValueHandling.Ignore, Order = 5)]
        public SpeechMouthFrame[] Frames { get; set; }

        [JsonProperty("seq", NullValueHandling = NullValueHandling.Ignore, Order = 6)]
        public int Seq { get; set; }

        [JsonIgnore]
        public bool FinalPacket { get; set; }

        [JsonProperty("final_packet", NullValueHandling = NullValueHandling.Ignore, Order = 7)]
        private bool? FinalPacketSnake
        {
            get => FinalPacket ? true : (bool?)null;
            set
            {
                if (value.HasValue)
                {
                    FinalPacket = value.Value;
                }
            }
        }

        [JsonProperty("finalPacket", NullValueHandling = NullValueHandling.Ignore, Order = 8)]
        private bool? FinalPacketCamel
        {
            get => null;
            set
            {
                if (value.HasValue)
                {
                    FinalPacket = value.Value;
                }
            }
        }

        public bool TryGetAudioBytes(out byte[] bytes)
        {
            bytes = null;
            var data = Audio != null ? Audio.Data : null;
            if (string.IsNullOrEmpty(data))
            {
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(data);
                return bytes != null && bytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SpeechFailedPayload
    {
        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore, Order = 1)]
        public string Code { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore, Order = 2)]
        public string Message { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SystemErrorPayload
    {
        [JsonProperty("code", Required = Required.Always, Order = 1)]
        public string Code { get; set; }

        [JsonProperty("message", Required = Required.Always, Order = 2)]
        public string Message { get; set; }
    }
}
