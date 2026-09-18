using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Resolves a face pose from a baked curve catalog, falling back to heuristics.
    /// </summary>
    public static class DashboardFaceCurveSampler
    {
        public static bool TrySample(
            DashboardFaceCurveCatalog catalog,
            string stateName,
            float normalizedTime,
            out FaceOverlayPose pose)
        {
            pose = default;
            if (catalog == null || !catalog.TryGet(stateName, out var clip) || clip == null)
            {
                return false;
            }

            var t = normalizedTime;
            if (t < 0f)
            {
                t = 0f;
            }

            // Looping states may report normalizedTime > 1.
            if (t > 1f)
            {
                t -= Mathf.Floor(t);
            }

            pose = clip.SampleNormalized(t);
            return true;
        }
    }
}
