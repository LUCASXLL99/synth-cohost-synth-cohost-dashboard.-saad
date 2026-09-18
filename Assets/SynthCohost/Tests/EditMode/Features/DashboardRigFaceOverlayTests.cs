using NUnit.Framework;
using SynthCohost.Runtime.Features.Avatar;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardRigFaceOverlayTests
    {
        [Test]
        public void Resolve_Wink_FullyClosesLeftEye()
        {
            var pose = DashboardRigFaceOverlay.Resolve("Wink");
            Assert.That(pose.BlinkLeft, Is.EqualTo(DashboardRigFaceOverlay.FullBlink));
            Assert.That(pose.SquintLeft, Is.EqualTo(0f));
            Assert.That(pose.BlinkRight, Is.EqualTo(0f));
        }

        [Test]
        public void Resolve_SlowBlinkAndSleepy_CloseBothEyes()
        {
            Assert.That(DashboardRigFaceOverlay.Resolve("Slow_Blink").BlinkRight, Is.EqualTo(DashboardRigFaceOverlay.FullBlink));
            Assert.That(DashboardRigFaceOverlay.Resolve("sleepy").BlinkLeft, Is.EqualTo(DashboardRigFaceOverlay.SleepyBlink));
        }

        [Test]
        public void Resolve_Expressions_UseNativeFaceShapes()
        {
            Assert.That(DashboardRigFaceOverlay.Resolve("46_Smile").Smile, Is.EqualTo(75f));
            Assert.That(DashboardRigFaceOverlay.Resolve("52_sad").Frown, Is.EqualTo(70f));
            Assert.That(DashboardRigFaceOverlay.Resolve("Gasp").EyeWideLeft, Is.EqualTo(90f));
            Assert.That(DashboardRigFaceOverlay.Resolve("sigh").Jaw, Is.EqualTo(7f));
            Assert.That(DashboardRigFaceOverlay.Resolve("57_Yawning").Jaw, Is.EqualTo(18f));
        }

        [Test]
        public void Resolve_Idle_LeavesBindPoseMouth()
        {
            var pose = DashboardRigFaceOverlay.Resolve("01_Idle_A_(Breathing)");
            Assert.That(pose.BlinkLeft, Is.EqualTo(0f));
            Assert.That(pose.Smile, Is.EqualTo(0f));
            Assert.That(pose.Jaw, Is.EqualTo(0f));
            Assert.That(pose.MouthClose, Is.EqualTo(0f));
            Assert.That(DashboardRigFaceOverlay.RestMouthClose, Is.EqualTo(0f));
        }

        [Test]
        public void WinkBlink_StaysBelowSocketCollapse()
        {
            Assert.That(DashboardRigFaceOverlay.FullBlink, Is.LessThanOrEqualTo(60f));
            Assert.That(DashboardRigFaceOverlay.Resolve("Wink").BlinkLeft, Is.EqualTo(60f));
        }

        [Test]
        public void BlinkMatchers_UseNativeShapesOnly()
        {
            Assert.That(DashboardRigFaceOverlay.IsBlinkLeft("blink_L"), Is.True);
            Assert.That(DashboardRigFaceOverlay.IsBlinkLeft("eyeBlinkLeft"), Is.False);
            Assert.That(DashboardRigFaceOverlay.IsDuplicateArkitBlink("eyeBlinkLeft"), Is.True);
        }

        [Test]
        public void WinkAndGasp_AreOneShotExpressions()
        {
            Assert.That(DashboardRigFaceOverlay.IsOneShotExpression("Wink"), Is.True);
            Assert.That(DashboardRigFaceOverlay.IsOneShotExpression("Gasp"), Is.True);
            Assert.That(DashboardRigFaceOverlay.IsOneShotExpression("52_sad"), Is.True);
            Assert.That(DashboardRigFaceOverlay.IsOneShotExpression("01_Idle_A_(Breathing)"), Is.False);
        }

        [Test]
        public void PlateauEnvelope_HoldsFullWeightInTheMiddle()
        {
            Assert.That(DashboardRigFaceOverlay.PlateauEnvelope(0f), Is.EqualTo(0f));
            Assert.That(DashboardRigFaceOverlay.PlateauEnvelope(0.5f), Is.EqualTo(1f));
            Assert.That(DashboardRigFaceOverlay.PlateauEnvelope(1f), Is.EqualTo(0f).Within(0.02f));
        }

        [Test]
        public void ForTalking_OpensJawAndCapsHappySmile()
        {
            var talking = DashboardRigFaceOverlay.ForTalking(
                DashboardRigFaceOverlay.Resolve("46_Smile"),
                softSmile: true);
            Assert.That(talking.Smile, Is.EqualTo(DashboardRigFaceOverlay.TalkingSmile));
            Assert.That(talking.Jaw, Is.GreaterThanOrEqualTo(DashboardRigFaceOverlay.TalkingJawFloor));
            Assert.That(talking.MouthClose, Is.EqualTo(0f));
        }
    }
}
