using System;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Lookup table of baked face curves keyed by Animator state name.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FaceCurveCatalog",
        menuName = "Synth Cohost/Face Curve Catalog")]
    public sealed class DashboardFaceCurveCatalog : ScriptableObject
    {
        [SerializeField] private DashboardFaceCurveClip[] clips = Array.Empty<DashboardFaceCurveClip>();

        public DashboardFaceCurveClip[] Clips => clips;

        public void SetClips(DashboardFaceCurveClip[] next)
        {
            clips = next ?? Array.Empty<DashboardFaceCurveClip>();
        }

        public bool TryGet(string stateName, out DashboardFaceCurveClip clip)
        {
            clip = null;
            if (string.IsNullOrEmpty(stateName) || clips == null)
            {
                return false;
            }

            for (var i = 0; i < clips.Length; i++)
            {
                var candidate = clips[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.StateName))
                {
                    continue;
                }

                if (string.Equals(candidate.StateName, stateName, StringComparison.OrdinalIgnoreCase))
                {
                    clip = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
