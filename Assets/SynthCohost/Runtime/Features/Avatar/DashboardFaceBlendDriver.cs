using SynthCohost.Protocol;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Maps <see cref="AiEmotion"/> onto whatever smile/blink-style blendshapes exist.
    /// Returns how many weights were written. Zero means the rig has no usable shapes.
    /// </summary>
    public static class DashboardFaceBlendDriver
    {
        public static int ApplyEmotion(SkinnedMeshRenderer[] renderers, AiEmotion emotion)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return 0;
            }

            var smileWeight = SmileWeight(emotion);
            var concernWeight = ConcernWeight(emotion);
            var applied = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                applied += ApplyOnRenderer(renderers[i], smileWeight, concernWeight);
            }

            return applied;
        }

        public static void Clear(SkinnedMeshRenderer[] renderers)
        {
            ApplyEmotion(renderers, AiEmotion.Neutral);
        }

        private static int ApplyOnRenderer(
            SkinnedMeshRenderer renderer,
            float smileWeight,
            float concernWeight)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return 0;
            }

            var mesh = renderer.sharedMesh;
            var count = mesh.blendShapeCount;
            var written = 0;
            for (var i = 0; i < count; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var lower = name.ToLowerInvariant();
                if (IsSmileShape(lower))
                {
                    renderer.SetBlendShapeWeight(i, smileWeight);
                    written++;
                }
                else if (IsConcernShape(lower))
                {
                    renderer.SetBlendShapeWeight(i, concernWeight);
                    written++;
                }
            }

            return written;
        }

        internal static bool IsSmileShape(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            if (lower.Contains("bulge") || lower.Contains("blink"))
            {
                return false;
            }

            return lower.Contains("mouthsmile") ||
                   lower.Contains("mouth_smile") ||
                   lower == "happy_m" ||
                   lower.StartsWith("happy_") ||
                   lower.Contains("smile") ||
                   lower.Contains("grin");
        }

        private static bool IsConcernShape(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            return lower.Contains("sad") ||
                   lower.Contains("frown") ||
                   lower.Contains("concern") ||
                   lower.Contains("angry") ||
                   lower.Contains("sorrow");
        }

        private static float SmileWeight(AiEmotion emotion)
        {
            switch (emotion)
            {
                case AiEmotion.Happy:
                    return 85f;
                case AiEmotion.Excited:
                case AiEmotion.Celebrate:
                    return 90f;
                default:
                    return 0f;
            }
        }

        private static float ConcernWeight(AiEmotion emotion)
        {
            switch (emotion)
            {
                case AiEmotion.Concerned:
                    return 60f;
                case AiEmotion.Confused:
                    return 40f;
                default:
                    return 0f;
            }
        }
    }
}
