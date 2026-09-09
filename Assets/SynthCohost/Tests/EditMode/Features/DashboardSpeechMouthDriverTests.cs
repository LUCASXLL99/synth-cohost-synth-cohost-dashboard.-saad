using NUnit.Framework;
using SynthCohost.Runtime.Features.Avatar;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardSpeechMouthDriverTests
    {
        [Test]
        public void ApplyTalk_WithNoRenderers_ReturnsZero()
        {
            Assert.That(DashboardSpeechMouthDriver.ApplyTalk(null, 0f), Is.EqualTo(0));
            Assert.That(
                DashboardSpeechMouthDriver.ApplyTalk(System.Array.Empty<UnityEngine.SkinnedMeshRenderer>(), 1f),
                Is.EqualTo(0));
        }

        [Test]
        public void IsJawOrOpen_MatchesCommonTalkShapes()
        {
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("jawOpen"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("mouthOpen"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("mouth_open_M"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("viseme_aa"), Is.False);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("jawForward"), Is.False);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("mouthSmileLeft"), Is.False);
            Assert.That(DashboardSpeechMouthDriver.IsJawOrOpen("mouthFunnel"), Is.False);
        }

        [Test]
        public void IsNarrowTalk_MatchesFunnelAndPucker()
        {
            Assert.That(DashboardSpeechMouthDriver.IsNarrowTalk("mouthFunnel"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsNarrowTalk("mouthPucker"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsNarrowTalk("jawOpen"), Is.False);
        }

        [Test]
        public void IsChinTearShape_IncludesScreamOShapes()
        {
            Assert.That(DashboardSpeechMouthDriver.IsChinTearShape("mouthFunnel"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsChinTearShape("lip_upperPuckerPos_M"), Is.True);
            Assert.That(DashboardSpeechMouthDriver.IsChinTearShape("jawOpen"), Is.False);
            Assert.That(DashboardSpeechMouthDriver.IsChinTearShape("mouth_open_M"), Is.False);
        }
    }
}
