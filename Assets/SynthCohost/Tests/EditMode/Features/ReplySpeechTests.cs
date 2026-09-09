using NUnit.Framework;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class ReplySpeechTests
    {
        [Test]
        public void Sanitize_StripsMarkupAndCapsLength()
        {
            var sanitized = ReplySpeech.Sanitize("<speak>hi\nthere</speak>");
            Assert.That(sanitized, Does.Not.Contain("<"));
            Assert.That(sanitized, Does.Not.Contain(">"));

            var longText = new string('a', ReplySpeech.MaximumSpeakCharacters + 40);
            Assert.That(ReplySpeech.Sanitize(longText).Length, Is.EqualTo(ReplySpeech.MaximumSpeakCharacters));
        }

        [Test]
        public void EstimateHoldSeconds_IsBounded()
        {
            Assert.That(ReplySpeech.EstimateHoldSeconds(" "), Is.EqualTo(ReplySpeech.MinimumHoldSeconds));
            Assert.That(
                ReplySpeech.EstimateHoldSeconds(new string('a', 5000)),
                Is.EqualTo(ReplySpeech.MaximumHoldSeconds));
            var mid = ReplySpeech.EstimateHoldSeconds("Hello from the live test.");
            Assert.That(mid, Is.GreaterThan(ReplySpeech.MinimumHoldSeconds));
            Assert.That(mid, Is.LessThan(ReplySpeech.MaximumHoldSeconds));
        }

        [Test]
        public void WavFactory_RejectsTooSmallBuffers()
        {
            Assert.That(WavAudioClipFactory.TryCreate(null, "x", out var clip), Is.False);
            Assert.That(clip, Is.Null);
            Assert.That(WavAudioClipFactory.TryCreate(new byte[8], "x", out _), Is.False);
        }
    }
}
