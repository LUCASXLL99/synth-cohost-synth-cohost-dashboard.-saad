namespace SynthCohost.Tests.Protocol
{
    internal static class GoldenV2Fixtures
    {
        public const string SessionId = "smoke-test-1720953296";
        public const string AvatarId = "00000000-0000-0000-0000-000000000001";
        public const string Token = "test-access-token";
        public const string TimestampZ = "2026-07-14T12:34:56.000Z";
        public const string TimestampOffset = "2026-07-14T12:34:56.123456789+00:00";

        public const string Auth =
            "{\"v\":2,\"type\":\"auth\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"token\":\"test-access-token\",\"avatar_id\":\"00000000-0000-0000-0000-000000000001\"}}";

        public const string Heartbeat =
            "{\"v\":2,\"type\":\"heartbeat\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{}}";

        public const string SttPartial =
            "{\"v\":2,\"type\":\"stt.partial\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"text\":\"hey, how's it going?\",\"final\":false}}";

        public const string SttFinal =
            "{\"v\":2,\"type\":\"stt.final\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"text\":\"hey, how's it going?\",\"final\":true}}";

        public const string StateAck =
            "{\"v\":2,\"type\":\"state.ack\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"behavior\":\"speaking\"}}";

        public const string AvatarThinking =
            "{\"v\":2,\"type\":\"avatar.state\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.123456789+00:00\",\"payload\":{\"behavior\":\"thinking\"}}";

        public const string AvatarSpeaking =
            "{\"v\":2,\"type\":\"avatar.state\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.123456789+00:00\",\"payload\":{\"behavior\":\"speaking\"}}";

        public const string AiResponse =
            "{\"v\":2,\"type\":\"ai.response\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.123456789+00:00\",\"payload\":{\"text\":\"Hey! I'm doing well.\",\"emotion\":\"happy\",\"intent\":\"greeting\"}}";

        public const string SystemError =
            "{\"v\":2,\"type\":\"system.error\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.123456789+00:00\",\"payload\":{\"code\":\"AI_GENERATION_FAILED\",\"message\":\"Unable to generate a response.\"}}";
    }
}
