using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardFaceBlendDriverTests
    {
        [Test]
        public void ApplyEmotion_WithNoRenderers_ReturnsZero()
        {
            Assert.That(DashboardFaceBlendDriver.ApplyEmotion(null, AiEmotion.Happy), Is.EqualTo(0));
            Assert.That(
                DashboardFaceBlendDriver.ApplyEmotion(System.Array.Empty<SkinnedMeshRenderer>(), AiEmotion.Happy),
                Is.EqualTo(0));
        }

        [Test]
        public void ApplyEmotion_WithRendererMissingMesh_ReturnsZero()
        {
            var go = new GameObject("face-mesh");
            try
            {
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                Assert.That(
                    DashboardFaceBlendDriver.ApplyEmotion(new[] { renderer }, AiEmotion.Happy),
                    Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsSmileShape_MatchesMouthSmileAndHappyM()
        {
            Assert.That(DashboardFaceBlendDriver.IsSmileShape("mouthSmileLeft"), Is.True);
            Assert.That(DashboardFaceBlendDriver.IsSmileShape("happy_M"), Is.True);
            Assert.That(DashboardFaceBlendDriver.IsSmileShape("cheekSquintLeft"), Is.False);
            Assert.That(DashboardFaceBlendDriver.IsSmileShape("eyeBlinkLeft"), Is.False);
        }
    }
}
