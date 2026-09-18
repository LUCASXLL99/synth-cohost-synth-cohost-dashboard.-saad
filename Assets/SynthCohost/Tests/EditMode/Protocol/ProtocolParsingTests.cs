using NUnit.Framework;
using SynthCohost.Protocol;

namespace SynthCohost.Tests.Protocol
{
    public sealed class ProtocolParsingTests
    {
        private ProtocolCodec _codec;
        private DeployedV2ProtocolDialect _dialect;

        [SetUp]
        public void SetUp()
        {
            _codec = new ProtocolCodec();
            _dialect = new DeployedV2ProtocolDialect(_codec);
        }

        [TestCase(GoldenV2Fixtures.AvatarThinking, AvatarBehavior.Thinking)]
        [TestCase(GoldenV2Fixtures.AvatarSpeaking, AvatarBehavior.Speaking)]
        public void AvatarState_GoldenFixturesParse(string json, AvatarBehavior expected)
        {
            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadAvatarState(envelope, out var payload, out var payloadError),
                Is.True, payloadError.ToString());
            Assert.That(payload.Behavior, Is.EqualTo(expected));
        }

        [Test]
        public void AiResponse_GoldenFixtureParses()
        {
            Assert.That(_codec.TryParseEnvelope(GoldenV2Fixtures.AiResponse, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadAiResponse(envelope, out var payload, out var payloadError),
                Is.True, payloadError.ToString());

            Assert.That(payload.Text, Is.EqualTo("Hey! I'm doing well."));
            Assert.That(payload.Emotion, Is.EqualTo(AiEmotion.Happy));
            Assert.That(payload.Intent, Is.EqualTo(AiIntent.Greeting));
            Assert.That(envelope.TimestampUtc.Offset, Is.EqualTo(System.TimeSpan.Zero));
        }

        [Test]
        public void AiResponse_ContractShape_UsesResponseFieldAndBehavior()
        {
            Assert.That(
                _codec.TryParseEnvelope(GoldenV2Fixtures.AiResponseContractShape, out var envelope, out var envelopeError),
                Is.True,
                envelopeError.ToString());
            Assert.That(
                _dialect.TryReadAiResponse(envelope, out var payload, out var payloadError),
                Is.True,
                payloadError.ToString());

            Assert.That(payload.Text, Does.Contain("boss fight"));
            Assert.That(payload.Emotion, Is.EqualTo(AiEmotion.Excited));
            Assert.That(payload.Intent, Is.EqualTo(AiIntent.Chat));
            Assert.That(payload.Behavior, Is.EqualTo(AvatarBehavior.Speaking));
        }

        [Test]
        public void AiResponse_ListeningBackchannel_ParsesBehaviorListening()
        {
            Assert.That(
                _codec.TryParseEnvelope(
                    GoldenV2Fixtures.AiResponseListeningBackchannel,
                    out var envelope,
                    out var envelopeError),
                Is.True,
                envelopeError.ToString());
            Assert.That(
                _dialect.TryReadAiResponse(envelope, out var payload, out var payloadError),
                Is.True,
                payloadError.ToString());

            Assert.That(payload.Text, Is.EqualTo("Mm-hm."));
            Assert.That(payload.Behavior, Is.EqualTo(AvatarBehavior.Listening));
        }

        [Test]
        public void SystemError_GoldenFixtureParses()
        {
            Assert.That(_codec.TryParseEnvelope(GoldenV2Fixtures.SystemError, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadSystemError(envelope, out var payload, out var payloadError),
                Is.True, payloadError.ToString());

            Assert.That(payload.Code, Is.EqualTo("AI_GENERATION_FAILED"));
            Assert.That(payload.Message, Is.EqualTo("Unable to generate a response."));
        }

        [Test]
        public void SpeechAudio_GoldenFixtureParses()
        {
            Assert.That(
                _codec.TryParseEnvelope(GoldenV2Fixtures.SpeechAudio, out var envelope, out var envelopeError),
                Is.True,
                envelopeError.ToString());
            Assert.That(envelope.IsKnownEventType, Is.True);
            Assert.That(
                _dialect.TryReadSpeechAudio(envelope, out var payload, out var payloadError),
                Is.True,
                payloadError.ToString());

            Assert.That(payload.Seq, Is.EqualTo(0));
            Assert.That(payload.FinalPacket, Is.True);
            Assert.That(payload.DurationMs, Is.EqualTo(800));
            Assert.That(payload.SampleRate, Is.EqualTo(24000));
            Assert.That(payload.Frames, Is.Not.Null);
            Assert.That(payload.Frames.Length, Is.EqualTo(4));
            Assert.That(payload.Frames[2].Openness, Is.EqualTo(0.88f).Within(0.001f));
            Assert.That(payload.Audio.ContentType, Does.Contain("pcm"));
            Assert.That(payload.TryGetAudioBytes(out var bytes), Is.True);
            Assert.That(bytes.Length, Is.GreaterThan(0));
        }

        [Test]
        public void SpeechFailed_GoldenFixtureParses()
        {
            Assert.That(
                _codec.TryParseEnvelope(GoldenV2Fixtures.SpeechFailed, out var envelope, out var envelopeError),
                Is.True,
                envelopeError.ToString());
            Assert.That(
                _dialect.TryReadSpeechFailed(envelope, out var payload, out var payloadError),
                Is.True,
                payloadError.ToString());
            Assert.That(payload.Code, Is.EqualTo("TTS_FAILED"));
        }

        [TestCase("AUTH_FAILED", "AUTH_FAILED")]
        [TestCase("AI_GENERATION_FAILED", "AI_GENERATION_FAILED")]
        [TestCase("AUTH_FAILED\nforged-log", ProtocolSystemErrorCodes.Unrecognized)]
        [TestCase("looks.like-a-token", ProtocolSystemErrorCodes.Unrecognized)]
        public void SystemErrorDiagnosticLabel_AllowsOnlyBoundedProtocolIdentifiers(
            string code,
            string expected)
        {
            Assert.That(ProtocolSystemErrorCodes.ToDiagnosticLabel(code), Is.EqualTo(expected));
        }

        [Test]
        public void SystemErrorDiagnosticLabel_RejectsOversizedIdentifier()
        {
            Assert.That(
                ProtocolSystemErrorCodes.ToDiagnosticLabel(new string('A', 65)),
                Is.EqualTo(ProtocolSystemErrorCodes.Unrecognized));
        }

        [Test]
        public void UnknownEventType_ProducesSafeUntypedEnvelope()
        {
            var json = GoldenV2Fixtures.Heartbeat.Replace("heartbeat", "future.additive.event");

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var error), Is.True, error.ToString());
            Assert.That(envelope.EventType, Is.EqualTo("future.additive.event"));
            Assert.That(envelope.IsKnownEventType, Is.False);
            Assert.That(_codec.TryParsePayload<HeartbeatPayload>(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.EventTypeMismatch));
        }

        [Test]
        public void KnownPayload_AllowsAdditiveUnknownFields()
        {
            var json = GoldenV2Fixtures.AvatarThinking.Replace(
                "\"behavior\":\"thinking\"",
                "\"behavior\":\"thinking\",\"future_hint\":123");

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadAvatarState(envelope, out var payload, out var payloadError),
                Is.True, payloadError.ToString());
            Assert.That(payload.Behavior, Is.EqualTo(AvatarBehavior.Thinking));
        }

        [Test]
        public void UnknownEnumValue_IsDiagnosedWithoutEscapingException()
        {
            var json = GoldenV2Fixtures.AvatarThinking.Replace("thinking", "future_dance");

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadAvatarState(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.InvalidPayload));
        }

        [Test]
        public void MissingRequiredPayloadField_IsDiagnosed()
        {
            var json = GoldenV2Fixtures.AiResponse.Replace(",\"intent\":\"greeting\"", string.Empty);

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_dialect.TryReadAiResponse(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.InvalidPayload));
        }

        [Test]
        public void MissingSttFinalDiscriminator_IsDiagnosed()
        {
            var json = GoldenV2Fixtures.SttFinal.Replace(",\"final\":true", string.Empty);

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_codec.TryParsePayload<SttFinalPayload>(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.InvalidPayload));
        }

        [Test]
        public void WrongSttFinalDiscriminator_IsDiagnosed()
        {
            var json = GoldenV2Fixtures.SttFinal.Replace("\"final\":true", "\"final\":false");

            Assert.That(_codec.TryParseEnvelope(json, out var envelope, out var envelopeError),
                Is.True, envelopeError.ToString());
            Assert.That(_codec.TryParsePayload<SttFinalPayload>(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.InvalidPayload));
        }

        [TestCase("{\"v\":\"2\",\"type\":\"heartbeat\",\"session_id\":\"s\",\"ts\":\"2026-07-14T12:34:56Z\",\"payload\":{}}", ProtocolErrorCode.InvalidFieldType)]
        [TestCase("{\"v\":3,\"type\":\"heartbeat\",\"session_id\":\"s\",\"ts\":\"2026-07-14T12:34:56Z\",\"payload\":{}}", ProtocolErrorCode.UnsupportedVersion)]
        [TestCase("{\"v\":2,\"type\":\"heartbeat\",\"session_id\":\"\",\"ts\":\"2026-07-14T12:34:56Z\",\"payload\":{}}", ProtocolErrorCode.InvalidSessionId)]
        [TestCase("{\"v\":2,\"type\":\"heartbeat\",\"session_id\":\"s\",\"ts\":\"not-a-time\",\"payload\":{}}", ProtocolErrorCode.InvalidTimestamp)]
        [TestCase("{\"v\":2,\"type\":\"heartbeat\",\"session_id\":\"s\",\"ts\":\"2026-07-14T12:34:56Z\",\"payload\":null}", ProtocolErrorCode.InvalidFieldType)]
        public void InvalidEnvelope_IsRejected(string json, ProtocolErrorCode expectedCode)
        {
            Assert.That(_codec.TryParseEnvelope(json, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(expectedCode));
        }

        [Test]
        public void DuplicateEnvelopeField_IsRejected()
        {
            var json = GoldenV2Fixtures.Heartbeat.Replace("\"v\":2", "\"v\":2,\"v\":2");

            Assert.That(_codec.TryParseEnvelope(json, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(ProtocolErrorCode.InvalidJson));
        }

        [Test]
        public void PayloadTypeCannotBeAppliedToWrongEvent()
        {
            Assert.That(_codec.TryParseEnvelope(GoldenV2Fixtures.AvatarThinking, out var envelope, out var error),
                Is.True, error.ToString());

            Assert.That(_codec.TryParsePayload<AiResponsePayload>(envelope, out _, out var payloadError), Is.False);
            Assert.That(payloadError.Code, Is.EqualTo(ProtocolErrorCode.EventTypeMismatch));
        }
    }
}
