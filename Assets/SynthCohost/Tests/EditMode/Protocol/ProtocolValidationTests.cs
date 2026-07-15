using System;
using NUnit.Framework;
using SynthCohost.Protocol;

namespace SynthCohost.Tests.Protocol
{
    public sealed class ProtocolValidationTests
    {
        [Test]
        public void SessionId_UsesUtf8ByteBoundaries()
        {
            Assert.That(ProtocolValidation.ValidateSessionId(new string('a', 128)).IsValid, Is.True);
            Assert.That(ProtocolValidation.ValidateSessionId(new string('a', 129)).IsValid, Is.False);
            Assert.That(ProtocolValidation.ValidateSessionId(string.Concat(Repeat("😀", 32))).IsValid, Is.True);
            Assert.That(ProtocolValidation.ValidateSessionId(string.Concat(Repeat("😀", 33))).IsValid, Is.False);
        }

        [Test]
        public void SessionId_RejectsEmptyAndInvalidSurrogate()
        {
            Assert.That(ProtocolValidation.ValidateSessionId(string.Empty).IsValid, Is.False);
            Assert.That(ProtocolValidation.ValidateSessionId("\uD800").IsValid, Is.False);
        }

        [Test]
        public void SttText_UsesUtf8ByteBoundaries()
        {
            Assert.That(ProtocolValidation.ValidateSttText(new string('a', 4_000)).IsValid, Is.True);
            Assert.That(ProtocolValidation.ValidateSttText(new string('a', 4_001)).IsValid, Is.False);
            Assert.That(ProtocolValidation.ValidateSttText(string.Concat(Repeat("😀", 1_000))).IsValid, Is.True);
            Assert.That(ProtocolValidation.ValidateSttText(string.Concat(Repeat("😀", 1_001))).IsValid, Is.False);
        }

        [Test]
        public void JsonSize_AllowsExactly64KiBAndRejectsNextByte()
        {
            Assert.That(ProtocolValidation.ValidateJsonSize(new string('x', 64 * 1024)).IsValid, Is.True);
            var result = ProtocolValidation.ValidateJsonSize(new string('x', 64 * 1024 + 1));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(ProtocolErrorCode.MessageTooLarge));
        }

        [Test]
        public void CodecRejectsSerializedFrameOver64KiB()
        {
            var codec = new ProtocolCodec();
            var envelope = new V2Envelope<AiResponsePayload>(
                2,
                ProtocolEventTypes.AiResponse,
                "session",
                GoldenV2Fixtures.TimestampZ,
                new AiResponsePayload
                {
                    Text = new string('x', 70_000),
                    Emotion = AiEmotion.Neutral,
                    Intent = AiIntent.Chat
                });

            Assert.That(codec.TrySerializeEnvelope(envelope, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo(ProtocolErrorCode.MessageTooLarge));
        }

        [Test]
        public void Timestamp_EmitsInvariantUtcRfc3339()
        {
            var localOffset = new DateTimeOffset(2026, 7, 14, 17, 34, 56, 789, TimeSpan.FromHours(5));

            Assert.That(
                ProtocolValidation.FormatUtcTimestamp(localOffset),
                Is.EqualTo("2026-07-14T12:34:56.789Z"));
        }

        [TestCase("2026-07-14T12:34:56Z")]
        [TestCase("2026-07-14T12:34:56.123Z")]
        [TestCase("2026-07-14T12:34:56.123456789+00:00")]
        [TestCase("2026-07-14T17:34:56+05:00")]
        public void Timestamp_AcceptsBackendRfc3339UtcForms(string timestamp)
        {
            Assert.That(ProtocolValidation.TryParseUtcTimestamp(timestamp, out var parsed), Is.True);
            Assert.That(parsed.Offset, Is.EqualTo(TimeSpan.Zero));
        }

        [TestCase("2026-07-14 12:34:56Z")]
        [TestCase("2026-07-14T12:34:56")]
        public void Timestamp_RejectsNonUtcOrNonRfc3339Forms(string timestamp)
        {
            Assert.That(ProtocolValidation.TryParseUtcTimestamp(timestamp, out _), Is.False);
        }

        [Test]
        public void CodecRejectsJsonBeyondDepthLimit()
        {
            var nested = new string('[', 40) + new string(']', 40);
            var json =
                "{\"v\":2,\"type\":\"future.event\",\"session_id\":\"s\",\"ts\":\"2026-07-14T12:34:56Z\",\"payload\":{\"nested\":" +
                nested + "}}";

            var codec = new ProtocolCodec();
            Assert.That(codec.TryParseEnvelope(json, out _, out var error), Is.False);
            Assert.That(
                error.Code == ProtocolErrorCode.JsonTooDeep || error.Code == ProtocolErrorCode.InvalidJson,
                Is.True);
        }

        [Test]
        public void AllEnumParsersAreCaseSensitiveAndExplicit()
        {
            Assert.That(ProtocolEnumValues.TryParseAvatarBehavior("celebrate", out var behavior), Is.True);
            Assert.That(behavior, Is.EqualTo(AvatarBehavior.Celebrate));
            Assert.That(ProtocolEnumValues.TryParseAiEmotion("concerned", out var emotion), Is.True);
            Assert.That(emotion, Is.EqualTo(AiEmotion.Concerned));
            Assert.That(ProtocolEnumValues.TryParseAiIntent("farewell", out var intent), Is.True);
            Assert.That(intent, Is.EqualTo(AiIntent.Farewell));
            Assert.That(ProtocolEnumValues.TryParseAvatarBehavior("Celebrate", out _), Is.False);
        }

        private static string[] Repeat(string value, int count)
        {
            var values = new string[count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = value;
            }
            return values;
        }
    }
}
