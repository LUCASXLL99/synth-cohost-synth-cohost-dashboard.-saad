using System;
using NUnit.Framework;
using SynthCohost.Protocol;

namespace SynthCohost.Tests.Protocol
{
    public sealed class ProtocolSerializationTests
    {
        private static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 7, 14, 12, 34, 56, TimeSpan.Zero);

        private DeployedV2ProtocolDialect _dialect;

        [SetUp]
        public void SetUp()
        {
            _dialect = new DeployedV2ProtocolDialect(utcNow: () => FixedNow);
        }

        [Test]
        public void Auth_MatchesSmokeTestWireShapeExactly()
        {
            var json = _dialect.CreateAuth(
                GoldenV2Fixtures.SessionId,
                GoldenV2Fixtures.Token,
                GoldenV2Fixtures.AvatarId);

            Assert.That(json, Is.EqualTo(GoldenV2Fixtures.Auth));
        }

        [Test]
        public void Heartbeat_MatchesSmokeTestWireShapeExactly()
        {
            Assert.That(
                _dialect.CreateHeartbeat(GoldenV2Fixtures.SessionId),
                Is.EqualTo(GoldenV2Fixtures.Heartbeat));
        }

        [Test]
        public void SttFinal_MatchesSmokeTestWireShapeExactly()
        {
            Assert.That(
                _dialect.CreateSttFinal(GoldenV2Fixtures.SessionId, "hey, how's it going?"),
                Is.EqualTo(GoldenV2Fixtures.SttFinal));
        }

        [Test]
        public void SttPartial_EmitsRequiredFalseDiscriminator()
        {
            Assert.That(
                _dialect.CreateSttPartial(GoldenV2Fixtures.SessionId, "hey, how's it going?"),
                Is.EqualTo(GoldenV2Fixtures.SttPartial));
        }

        [Test]
        public void StateAck_UsesExplicitSnakeCaseEnumValue()
        {
            Assert.That(
                _dialect.CreateStateAck(GoldenV2Fixtures.SessionId, AvatarBehavior.Speaking),
                Is.EqualTo(GoldenV2Fixtures.StateAck));
        }

        [TestCase(AvatarBehavior.Idle, "idle")]
        [TestCase(AvatarBehavior.Listening, "listening")]
        [TestCase(AvatarBehavior.Thinking, "thinking")]
        [TestCase(AvatarBehavior.Speaking, "speaking")]
        [TestCase(AvatarBehavior.Happy, "happy")]
        [TestCase(AvatarBehavior.Celebrate, "celebrate")]
        public void EveryBehavior_HasExactWireValue(AvatarBehavior behavior, string wireValue)
        {
            Assert.That(behavior.ToWireValue(), Is.EqualTo(wireValue));
        }

        [Test]
        public void EveryOutboundFrame_ContainsIntegerV2AndSessionId()
        {
            var frames = new[]
            {
                _dialect.CreateAuth(GoldenV2Fixtures.SessionId, GoldenV2Fixtures.Token, GoldenV2Fixtures.AvatarId),
                _dialect.CreateHeartbeat(GoldenV2Fixtures.SessionId),
                _dialect.CreateSttPartial(GoldenV2Fixtures.SessionId, "partial"),
                _dialect.CreateSttFinal(GoldenV2Fixtures.SessionId, "final"),
                _dialect.CreateStateAck(GoldenV2Fixtures.SessionId, AvatarBehavior.Idle)
            };

            foreach (var frame in frames)
            {
                Assert.That(_dialect.Codec.TryParseEnvelope(frame, out var envelope, out var error),
                    Is.True, error.ToString());
                Assert.That(envelope.Version, Is.EqualTo(2));
                Assert.That(envelope.SessionId, Is.EqualTo(GoldenV2Fixtures.SessionId));
            }
        }

        [Test]
        public void GenerateSessionId_ReturnsCanonicalUuidV4()
        {
            var sessionId = _dialect.GenerateSessionId();

            Assert.That(Guid.TryParseExact(sessionId, "D", out var parsed), Is.True);
            Assert.That(parsed.ToByteArray()[7] >> 4, Is.EqualTo(4));
        }

        [Test]
        public void TryCreate_ReturnsSanitizedValidationErrorWithoutThrowing()
        {
            var result = _dialect.TryCreateSttFinal(
                GoldenV2Fixtures.SessionId,
                string.Empty,
                out var json,
                out var error);

            Assert.That(result, Is.False);
            Assert.That(json, Is.Null);
            Assert.That(error.Code, Is.EqualTo(ProtocolErrorCode.InvalidSttText));
        }

        [Test]
        public void Create_ThrowsTypedProtocolExceptionForInvalidInput()
        {
            var exception = Assert.Throws<ProtocolException>(() =>
                _dialect.CreateAuth(string.Empty, GoldenV2Fixtures.Token, GoldenV2Fixtures.AvatarId));

            Assert.That(exception.Error.Code, Is.EqualTo(ProtocolErrorCode.InvalidSessionId));
        }
    }
}
