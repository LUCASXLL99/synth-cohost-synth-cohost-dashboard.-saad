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
        [JsonProperty("text", Required = Required.Always, Order = 1)]
        public string Text { get; set; }

        [JsonProperty("emotion", Required = Required.Always, Order = 2)]
        public AiEmotion Emotion { get; set; }

        [JsonProperty("intent", Required = Required.Always, Order = 3)]
        public AiIntent Intent { get; set; }
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
