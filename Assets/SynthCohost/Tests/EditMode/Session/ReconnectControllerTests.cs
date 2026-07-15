using System;
using NUnit.Framework;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Tests.EditMode.Session
{
    public sealed class ReconnectControllerTests
    {
        private sealed class FixedRandom : IRandomSource
        {
            private readonly double value;
            public FixedRandom(double value) => this.value = value;
            public double NextUnit() => value;
        }

        [Test]
        public void DelayDoublesAndCapsWithoutJitter()
        {
            var policy = new ReconnectPolicySnapshot(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(4),
                0f,
                0);
            var reconnect = new ReconnectController(policy, new FixedRandom(0.5));

            Assert.That(reconnect.GetDelay(1), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(reconnect.GetDelay(2), Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(reconnect.GetDelay(3), Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(reconnect.GetDelay(8), Is.EqualTo(TimeSpan.FromSeconds(4)));
        }

        [Test]
        public void RateLimitMinimumOverridesShortBackoff()
        {
            var policy = new ReconnectPolicySnapshot(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                0.2f,
                0);
            var reconnect = new ReconnectController(policy, new FixedRandom(0));

            Assert.That(reconnect.GetDelay(1, TimeSpan.FromSeconds(10)), Is.EqualTo(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void AttemptLimitIsBoundedWhenConfigured()
        {
            var policy = new ReconnectPolicySnapshot(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                0f,
                2);
            var reconnect = new ReconnectController(policy, new FixedRandom(0.5));

            Assert.That(reconnect.CanAttempt(1), Is.True);
            Assert.That(reconnect.CanAttempt(2), Is.True);
            Assert.That(reconnect.CanAttempt(3), Is.False);
        }
    }
}
