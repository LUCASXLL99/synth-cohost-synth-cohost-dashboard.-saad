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

        /// <summary>LLM Avatar Response Contract shape: <c>response</c> + <c>behavior</c>.</summary>
        public const string AiResponseContractShape =
            "{\"v\":2,\"type\":\"ai.response\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"response\":\"That boss fight took me three tries, but we got there!\",\"emotion\":\"excited\",\"intent\":\"chat\",\"behavior\":\"speaking\"}}";

        public const string AiResponseListeningBackchannel =
            "{\"v\":2,\"type\":\"ai.response\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"response\":\"Mm-hm.\",\"emotion\":\"neutral\",\"intent\":\"chat\",\"behavior\":\"listening\"}}";

        public const string SystemError =
            "{\"v\":2,\"type\":\"system.error\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.123456789+00:00\",\"payload\":{\"code\":\"AI_GENERATION_FAILED\",\"message\":\"Unable to generate a response.\"}}";

        /// <summary>Abid/Lucas speech.audio inner payload wrapped in deployed v2 envelope. Audio is 16-bit PCM silence.</summary>
        public const string SpeechAudio =
            "{\"v\":2,\"type\":\"speech.audio\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"text\":\"Hey everyone!\",\"duration_ms\":800,\"sample_rate\":24000,\"audio\":{\"kind\":\"inline\",\"data\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"content_type\":\"audio/pcm\"},\"frames\":[{\"t\":0,\"o\":0.06},{\"t\":33,\"o\":0.16},{\"t\":66,\"o\":0.88},{\"t\":100,\"o\":1.0}],\"seq\":0,\"final_packet\":true}}";

        public const string SpeechFailed =
            "{\"v\":2,\"type\":\"speech.failed\",\"session_id\":\"smoke-test-1720953296\",\"ts\":\"2026-07-14T12:34:56.000Z\",\"payload\":{\"code\":\"TTS_FAILED\"}}";
    }
}
