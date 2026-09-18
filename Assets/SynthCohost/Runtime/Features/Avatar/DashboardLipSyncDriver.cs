using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Writes CC phoneme mouth shapes for live TTS. Keeps ARKit funnel/pucker at 0.
    /// </summary>
    internal static class DashboardLipSyncDriver
    {
        public static int Apply(SkinnedMeshRenderer[] renderers, LipSyncPose pose)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return 0;
            }

            var written = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                written += ApplyOnRenderer(renderers[i], pose);
            }

            return written;
        }

        public static void Clear(SkinnedMeshRenderer[] renderers)
        {
            Apply(renderers, LipSyncPose.Rest);
        }

        private static int ApplyOnRenderer(SkinnedMeshRenderer renderer, LipSyncPose pose)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return 0;
            }

            if (!DashboardRigFaceOverlay.IsBodyFaceRenderer(renderer) &&
                !DashboardRigFaceOverlay.IsInnerMouthRenderer(renderer))
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

                var lower = name.ToLowerInvariant();
                float? weight = null;
                if (lower == "aaa_m")
                {
                    weight = pose.Aaa;
                }
                else if (lower == "ahh_m")
                {
                    weight = pose.Ahh;
                }
                else if (lower == "eh_m")
                {
                    weight = pose.Eh;
                }
                else if (lower == "ohh_m")
                {
                    weight = pose.Ohh;
                }
                else if (lower == "uuu_m")
                {
                    weight = pose.Uuu;
                }
                else if (lower == "iee_m")
                {
                    weight = pose.Iee;
                }
                else if (lower == "rrr_m")
                {
                    weight = pose.Rrr;
                }
                else if (lower == "www_m")
                {
                    weight = pose.Www;
                }
                else if (lower == "sss_m")
                {
                    weight = pose.Sss;
                }
                else if (lower == "fff_m")
                {
                    weight = pose.Fff;
                }
                else if (lower == "tth_m")
                {
                    weight = pose.Tth;
                }
                else if (lower == "mbp_m")
                {
                    weight = pose.Mbp;
                }
                else if (lower == "ssh_m")
                {
                    weight = pose.Ssh;
                }
                else if (lower == "schwa_m")
                {
                    weight = pose.Schwa;
                }
                else if (lower == "gk_m")
                {
                    weight = pose.Gk;
                }
                else if (lower == "lntd_m")
                {
                    weight = pose.Lntd;
                }
                else if (lower == "mouth_open_m")
                {
                    weight = pose.Jaw;
                }
                else if (lower == "mouth_wide_m")
                {
                    weight = pose.Wide;
                }
                else if (lower == "mouth_narrow_m")
                {
                    weight = pose.Narrow;
                }
                else if (lower == "mouth_close_m")
                {
                    weight = pose.Close;
                }
                else if (DashboardSpeechMouthDriver.IsChinTearShape(name) ||
                         lower == "jawopen" ||
                         lower == "mouthclose" ||
                         lower == "mouthfunnel" ||
                         lower == "mouthpucker")
                {
                    weight = 0f;
                }

                if (!weight.HasValue)
                {
                    continue;
                }

                renderer.SetBlendShapeWeight(i, weight.Value);
                written++;
            }

            return written;
        }
    }
}
