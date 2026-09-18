using NUnit.Framework;
using SynthCohost.Runtime.Features.Avatar;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class LipSyncTests
    {
        [Test]
        public void VisemeMap_Aa_UsesPhonemeMoreThanJaw()
        {
            var pose = LipSyncVisemeMap.ToPose(2);
            Assert.That(pose.Aaa, Is.GreaterThan(24f));
            Assert.That(pose.Jaw, Is.LessThan(pose.Aaa));
            Assert.That(pose.Jaw, Is.LessThanOrEqualTo(LipSyncVisemeMap.MaxJaw));
        }

        [Test]
        public void VisemeMap_Iy_UsesIeeWide()
        {
            var pose = LipSyncVisemeMap.ToPose(6);
            Assert.That(pose.Iee, Is.GreaterThan(24f));
            Assert.That(pose.Wide, Is.GreaterThan(8f));
        }

        [Test]
        public void VisemeMap_Ow_UsesOhh()
        {
            var pose = LipSyncVisemeMap.ToPose(8);
            Assert.That(pose.Ohh, Is.GreaterThan(24f));
        }

        [Test]
        public void VisemeMap_Bilabial_UsesMbp()
        {
            var pose = LipSyncVisemeMap.ToPose(21);
            Assert.That(pose.Mbp, Is.GreaterThan(24f));
            Assert.That(pose.Close, Is.GreaterThan(12f));
            Assert.That(pose.Jaw, Is.LessThan(4f));
        }

        [Test]
        public void VisemeMap_Fv_UsesFff()
        {
            var pose = LipSyncVisemeMap.ToPose(18);
            Assert.That(pose.Fff, Is.GreaterThan(24f));
        }

        [Test]
        public void VisemeMap_Eh_UsesEhShape()
        {
            var pose = LipSyncVisemeMap.ToPose(4);
            Assert.That(pose.Eh, Is.GreaterThan(24f));
        }

        [Test]
        public void VisemeMap_Silence_IsRest()
        {
            var pose = LipSyncVisemeMap.ToPose(0);
            Assert.That(pose.Jaw, Is.EqualTo(0f));
            Assert.That(pose.Aaa, Is.EqualTo(0f));
            Assert.That(pose.Mbp, Is.EqualTo(0f));
        }

        [Test]
        public void Track_ParsesSapiLines_AndSamplesByTime()
        {
            const string lines = "0.0000,0\n0.1200,2\n0.4000,6\n0.7000,8\n1.0000,0\n";
            Assert.That(LipSyncTrack.TryParseLines(lines, out var track), Is.True);
            Assert.That(track.Count, Is.EqualTo(5));
            Assert.That(track.Sample(0.2f).Aaa, Is.GreaterThan(20f));
            Assert.That(track.Sample(0.2f).Iee, Is.EqualTo(0f));
            Assert.That(track.Sample(0.5f).Iee, Is.GreaterThan(20f));
            Assert.That(track.Sample(0.8f).Ohh, Is.GreaterThan(20f));
            Assert.That(track.Sample(1.1f).Jaw, Is.EqualTo(0f));
            var blended = track.Sample(0.38f);
            Assert.That(blended.Aaa + blended.Iee, Is.GreaterThan(5f));
        }

        [Test]
        public void ApproximateText_MapsVowelsDistinctly()
        {
            var track = LipSyncTrack.FromApproximateText("aeiou", 1f);
            Assert.That(track.Count, Is.GreaterThan(3));
            Assert.That(track.Sample(0.05f).Aaa, Is.GreaterThan(0f));
            Assert.That(LipSyncTrack.ApproximateViseme('o'), Is.EqualTo(8));
            Assert.That(LipSyncTrack.ApproximateViseme('e'), Is.EqualTo(4));
            Assert.That(LipSyncTrack.ApproximateViseme('m'), Is.EqualTo(21));
        }

        [Test]
        public void ForTalking_WithoutJawFloor_LeavesJawForLipSync()
        {
            var hold = DashboardRigFaceOverlay.Resolve("46_Smile");
            var talking = DashboardRigFaceOverlay.ForTalking(hold, softSmile: true, forceJawFloor: false);
            Assert.That(talking.Smile, Is.EqualTo(DashboardRigFaceOverlay.TalkingSmile));
            Assert.That(talking.Jaw, Is.EqualTo(0f));
        }

        [Test]
        public void MouthOpenTrack_SamplesOpennessOntoJaw()
        {
            var track = MouthOpenTrack.FromFrames(new[]
            {
                new SynthCohost.Protocol.SpeechMouthFrame { TimeMs = 0f, Openness = 0.1f },
                new SynthCohost.Protocol.SpeechMouthFrame { TimeMs = 100f, Openness = 1f }
            });
            Assert.That(track.Sample(0f), Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(track.Sample(0.1f), Is.EqualTo(1f).Within(0.001f));
            var mid = track.Sample(0.05f);
            Assert.That(mid, Is.GreaterThan(0.4f));
            Assert.That(mid, Is.LessThan(0.7f));
            var pose = MouthOpenTrack.ToPose(1f);
            Assert.That(pose.Jaw, Is.EqualTo(MouthOpenTrack.MaxMouthOpenWeight));
            Assert.That(pose.Aaa, Is.EqualTo(0f));
            Assert.That(pose.Ohh, Is.EqualTo(0f));
            Assert.That(pose.Iee, Is.EqualTo(0f));
            Assert.That(pose.Mbp, Is.EqualTo(0f));
            var rest = MouthOpenTrack.ToPose(0f);
            Assert.That(rest.Jaw, Is.EqualTo(0f));
            var scaled = MouthOpenTrack.ToPose(1f, 20f);
            Assert.That(scaled.Jaw, Is.EqualTo(20f));
            Assert.That(scaled.Aaa, Is.EqualTo(0f));
        }
    }
}
