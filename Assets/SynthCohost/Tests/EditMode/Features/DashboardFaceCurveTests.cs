using NUnit.Framework;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardFaceCurveTests
    {
        [Test]
        public void SampleKeys_InterpolatesBetweenFrames()
        {
            var keys = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, float>>
            {
                new System.Collections.Generic.KeyValuePair<float, float>(1f, 0f),
                new System.Collections.Generic.KeyValuePair<float, float>(14f, 1.66f),
                new System.Collections.Generic.KeyValuePair<float, float>(45f, 1.66f),
                new System.Collections.Generic.KeyValuePair<float, float>(60f, 0f)
            };

            Assert.That(SampleAt(keys, 1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SampleAt(keys, 14f), Is.EqualTo(1.66f).Within(0.001f));
            Assert.That(SampleAt(keys, 7.5f), Is.GreaterThan(0.7f));
            Assert.That(SampleAt(keys, 60f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SampleAt(keys, 100f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Clip_SampleNormalized_ReturnsAuthoredSmileEnvelope()
        {
            var clip = ScriptableObject.CreateInstance<DashboardFaceCurveClip>();
            var smile = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.25f, 75f),
                new Keyframe(0.75f, 75f),
                new Keyframe(1f, 0f));
            clip.Configure(
                "46_Smile",
                2f,
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                smile,
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f));

            Assert.That(clip.SampleNormalized(0f).Smile, Is.EqualTo(0f).Within(0.1f));
            Assert.That(clip.SampleNormalized(0.5f).Smile, Is.EqualTo(75f).Within(0.1f));
            Assert.That(clip.SampleNormalized(1f).Smile, Is.EqualTo(0f).Within(0.1f));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Catalog_TryGet_IsCaseInsensitive()
        {
            var clip = ScriptableObject.CreateInstance<DashboardFaceCurveClip>();
            clip.Configure(
                "Wink",
                1f,
                AnimationCurve.Constant(0f, 1f, 60f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f));
            var catalog = ScriptableObject.CreateInstance<DashboardFaceCurveCatalog>();
            catalog.SetClips(new[] { clip });

            Assert.That(catalog.TryGet("wink", out var found), Is.True);
            Assert.That(found.SampleNormalized(0.5f).BlinkLeft, Is.EqualTo(60f).Within(0.1f));
            Assert.That(
                DashboardFaceCurveSampler.TrySample(catalog, "WINK", 0.5f, out var pose),
                Is.True);
            Assert.That(pose.BlinkLeft, Is.EqualTo(60f).Within(0.1f));
            Assert.That(DashboardFaceCurveSampler.TrySample(null, "Wink", 0.5f, out _), Is.False);

            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void Overlay_PrefersCatalogOverHeuristic()
        {
            var clip = ScriptableObject.CreateInstance<DashboardFaceCurveClip>();
            clip.Configure(
                "46_Smile",
                2f,
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 42f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f),
                AnimationCurve.Constant(0f, 1f, 0f));
            var catalog = ScriptableObject.CreateInstance<DashboardFaceCurveCatalog>();
            catalog.SetClips(new[] { clip });

            Assert.That(DashboardFaceCurveSampler.TrySample(catalog, "46_Smile", 0.4f, out var pose), Is.True);
            Assert.That(pose.Smile, Is.EqualTo(42f).Within(0.1f));
            Assert.That(DashboardRigFaceOverlay.Resolve("46_Smile").Smile, Is.EqualTo(75f));

            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(catalog);
        }

        private static float SampleAt(
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, float>> keys,
            float frame)
        {
            // Mirror baker sampling for EditMode without Editor assembly reference.
            if (keys == null || keys.Count == 0)
            {
                return 0f;
            }

            if (frame <= keys[0].Key)
            {
                return keys[0].Value;
            }

            if (frame >= keys[keys.Count - 1].Key)
            {
                return keys[keys.Count - 1].Value;
            }

            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (frame < a.Key || frame > b.Key)
                {
                    continue;
                }

                if (Mathf.Approximately(a.Key, b.Key))
                {
                    return b.Value;
                }

                var u = (frame - a.Key) / (b.Key - a.Key);
                return Mathf.Lerp(a.Value, b.Value, u);
            }

            return keys[keys.Count - 1].Value;
        }
    }
}
