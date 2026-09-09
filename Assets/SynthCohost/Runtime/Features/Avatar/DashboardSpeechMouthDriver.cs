using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Approximate talking mouth while local TTS plays. Not backend visemes.
    /// Uses a small jaw open only. Funnel/pucker/viseme stacks tear this rig's chin.
    /// </summary>
    internal static class DashboardSpeechMouthDriver
    {
        internal const float OpenMin = 5f;
        internal const float OpenMax = 16f;

        public static int ApplyTalk(SkinnedMeshRenderer[] renderers, float unscaledTime)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return 0;
            }

            var wave = 0.5f + 0.5f * Mathf.Sin(unscaledTime * 11f);
            var open = Mathf.Lerp(OpenMin, OpenMax, wave);
            return ApplyTalkWeights(renderers, open);
        }

        public static void Clear(SkinnedMeshRenderer[] renderers)
        {
            ApplyTalkWeights(renderers, 0f);
        }

        private static int ApplyTalkWeights(SkinnedMeshRenderer[] renderers, float open)
        {
            if (renderers == null)
            {
                return 0;
            }

            var written = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                written += ApplyOnRenderer(renderers[i], open);
            }

            return written;
        }

        private static int ApplyOnRenderer(SkinnedMeshRenderer renderer, float open)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return 0;
            }

            var mesh = renderer.sharedMesh;
            var written = 0;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (IsJawOrOpen(name))
                {
                    renderer.SetBlendShapeWeight(i, open);
                    written++;
                }
                else if (IsChinTearShape(name))
                {
                    renderer.SetBlendShapeWeight(i, 0f);
                }
            }

            return written;
        }

        internal static bool IsJawOrOpen(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            return lower == "jawopen" ||
                   lower == "mouthopen" ||
                   lower == "mouth_open_m" ||
                   lower.Contains("jawopen") ||
                   lower.Contains("mouth_open");
        }

        /// <summary>
        /// Shapes that stretch the lips into an O and pull the inner mouth bag through the chin.
        /// </summary>
        internal static bool IsChinTearShape(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            return lower.Contains("mouthfunnel") ||
                   lower.Contains("mouthpucker") ||
                   lower.Contains("viseme_aa") ||
                   lower.Contains("viseme_oh") ||
                   lower.Contains("viseme_ou") ||
                   lower.Contains("lip_upperpucker") ||
                   lower.Contains("lip_lowerpucker");
        }

        internal static bool IsNarrowTalk(string name)
        {
            return IsChinTearShape(name);
        }
    }
}
