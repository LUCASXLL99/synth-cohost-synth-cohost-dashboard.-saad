using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Face clips move Maya joints this Unity mesh is not skinned to.
    /// Drive native CC shapes only. ARKit copies of the same delta must stay at 0.
    /// <c>mouth_close_M</c> is a 3.5-unit morph — any rest weight pinches the lips,
    /// so idle keeps the bind pose. <c>blink_L</c> above ~65 collapses the socket.
    /// </summary>
    public static class DashboardRigFaceOverlay
    {
        public const float RestMouthClose = 0f;
        public const float FullBlink = 60f;
        public const float SleepyBlink = 42f;
        internal const float TalkingSmile = 12f;
        internal const float TalkingJawFloor = 12f;

        public static int ApplyForAnimatorState(
            SkinnedMeshRenderer[] renderers,
            string stateName,
            float audioPeak)
        {
            return ApplyForAnimatorState(renderers, stateName, audioPeak, 0f, clipFinished: false);
        }

        public static int ApplyForAnimatorState(
            SkinnedMeshRenderer[] renderers,
            string stateName,
            float audioPeak,
            float normalizedTime,
            bool clipFinished)
        {
            return ApplyForAnimatorState(
                renderers,
                stateName,
                audioPeak,
                normalizedTime,
                clipFinished,
                catalog: null);
        }

        public static int ApplyForAnimatorState(
            SkinnedMeshRenderer[] renderers,
            string stateName,
            float audioPeak,
            float normalizedTime,
            bool clipFinished,
            DashboardFaceCurveCatalog catalog)
        {
            FaceOverlayPose pose;
            if (clipFinished)
            {
                pose = RestPose();
            }
            else if (DashboardFaceCurveSampler.TrySample(catalog, stateName, normalizedTime, out pose))
            {
                // Authored Maya→CC curves already include the envelope.
            }
            else
            {
                pose = Resolve(stateName);
                if (IsOneShotExpression(stateName))
                {
                    var envelope = PlateauEnvelope(normalizedTime);
                    pose = BlendTowardRest(pose, envelope);
                }
            }

            return Apply(renderers, pose, audioPeak);
        }

        public static int Apply(
            SkinnedMeshRenderer[] renderers,
            FaceOverlayPose pose,
            float audioPeak)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return 0;
            }

            var jaw = pose.Jaw;
            if (audioPeak > 0.02f)
            {
                // Fallback only — viseme phonemes already open the mouth. 32–62
                // on mouth_open_M is why this rig looked over-open.
                jaw = Mathf.Max(jaw, Mathf.Lerp(6f, 16f, Mathf.Clamp01(audioPeak)));
            }

            var mouthClose = pose.MouthClose;
            if (jaw > 1f)
            {
                mouthClose = Mathf.Min(mouthClose, Mathf.Lerp(RestMouthClose, 0f, Mathf.Clamp01(jaw / 16f)));
            }

            var written = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (IsBodyFaceRenderer(renderer))
                {
                    written += ApplyOnRenderer(renderer, pose, jaw, mouthClose);
                }
                else if (IsInnerMouthRenderer(renderer))
                {
                    written += ApplyOnRenderer(renderer, MouthOnly(pose), jaw, mouthClose);
                }
                else
                {
                    written += ApplyOnRenderer(renderer, default, 0f, 0f);
                }
            }

            return written;
        }

        public static void Clear(SkinnedMeshRenderer[] renderers)
        {
            Apply(renderers, RestPose(), 0f);
        }

        internal static FaceOverlayPose RestPose()
        {
            return new FaceOverlayPose { MouthClose = RestMouthClose };
        }

        public static float PlateauEnvelope(float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);
            if (t < 0.12f)
            {
                return t / 0.12f;
            }

            if (t > 0.78f)
            {
                return (1f - t) / 0.22f;
            }

            return 1f;
        }

        public static bool IsOneShotExpression(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return false;
            }

            var n = stateName.ToLowerInvariant();
            return n.Contains("wink") ||
                   n.Contains("blink") ||
                   n.Contains("yawn") ||
                   n.Contains("sigh") ||
                   n.Contains("smile") ||
                   n.Contains("laugh") ||
                   n.Contains("cheer") ||
                   n.Contains("gasp") ||
                   n.Contains("nod") ||
                   n.Contains("sad") ||
                   n.Contains("shock");
        }

        public static FaceOverlayPose Resolve(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return RestPose();
            }

            var n = stateName.ToLowerInvariant();
            if (n.Contains("wink") && !n.Contains("blink"))
            {
                return new FaceOverlayPose
                {
                    BlinkLeft = FullBlink,
                    MouthClose = RestMouthClose
                };
            }

            if (n.Contains("slow_blink") || n.Contains("double_blink"))
            {
                return new FaceOverlayPose
                {
                    BlinkLeft = FullBlink,
                    BlinkRight = FullBlink,
                    MouthClose = RestMouthClose
                };
            }

            if (n.Contains("sleepy"))
            {
                return new FaceOverlayPose
                {
                    BlinkLeft = SleepyBlink,
                    BlinkRight = SleepyBlink,
                    MouthClose = RestMouthClose
                };
            }

            if (n.Contains("gasp") || n.Contains("shock"))
            {
                return new FaceOverlayPose
                {
                    EyeWideLeft = 90f,
                    EyeWideRight = 90f,
                    Jaw = 14f,
                    BrowInner = 55f,
                    MouthClose = 0f
                };
            }

            if (n.Contains("yawn"))
            {
                return new FaceOverlayPose
                {
                    Jaw = 18f,
                    BlinkLeft = 25f,
                    BlinkRight = 25f,
                    MouthClose = 0f
                };
            }

            if (n.Contains("sigh"))
            {
                return new FaceOverlayPose
                {
                    BlinkLeft = 30f,
                    BlinkRight = 30f,
                    Jaw = 7f,
                    BrowInner = 25f,
                    MouthClose = RestMouthClose
                };
            }

            if (n.Contains("smile") || n.Contains("happy") || n.Contains("laugh") || n.Contains("cheer"))
            {
                return new FaceOverlayPose
                {
                    Smile = 75f,
                    EyeWideLeft = n.Contains("cheer") || n.Contains("laugh") ? 25f : 0f,
                    EyeWideRight = n.Contains("cheer") || n.Contains("laugh") ? 25f : 0f,
                    MouthClose = RestMouthClose
                };
            }

            if (n.Contains("sad"))
            {
                return new FaceOverlayPose
                {
                    Frown = 70f,
                    BrowDown = 35f,
                    MouthClose = RestMouthClose
                };
            }

            return RestPose();
        }

        internal static FaceOverlayPose ForTalking(FaceOverlayPose hold, bool softSmile)
        {
            return ForTalking(hold, softSmile, forceJawFloor: true);
        }

        /// <param name="forceJawFloor">
        /// When false, leave jaw to lip-sync visemes (still caps smile / opens lips).
        /// </param>
        internal static FaceOverlayPose ForTalking(FaceOverlayPose hold, bool softSmile, bool forceJawFloor)
        {
            hold.Smile = softSmile ? TalkingSmile : Mathf.Min(hold.Smile, TalkingSmile);
            if (forceJawFloor)
            {
                hold.Jaw = Mathf.Max(hold.Jaw, TalkingJawFloor);
            }
            else
            {
                hold.Jaw = 0f;
            }

            hold.MouthClose = 0f;
            return hold;
        }

        private static FaceOverlayPose BlendTowardRest(FaceOverlayPose pose, float envelope)
        {
            var rest = RestPose();
            return new FaceOverlayPose
            {
                BlinkLeft = pose.BlinkLeft * envelope,
                BlinkRight = pose.BlinkRight * envelope,
                SquintLeft = pose.SquintLeft * envelope,
                SquintRight = pose.SquintRight * envelope,
                Smile = pose.Smile * envelope,
                Frown = pose.Frown * envelope,
                Jaw = pose.Jaw * envelope,
                EyeWideLeft = pose.EyeWideLeft * envelope,
                EyeWideRight = pose.EyeWideRight * envelope,
                BrowInner = pose.BrowInner * envelope,
                BrowDown = pose.BrowDown * envelope,
                MouthClose = Mathf.Lerp(rest.MouthClose, pose.MouthClose, envelope)
            };
        }

        private static int ApplyOnRenderer(
            SkinnedMeshRenderer renderer,
            FaceOverlayPose pose,
            float jaw,
            float mouthClose)
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

                var lower = name.ToLowerInvariant();
                float? weight = null;
                if (lower == "blink_l")
                {
                    weight = pose.BlinkLeft;
                }
                else if (lower == "blink_r")
                {
                    weight = pose.BlinkRight;
                }
                else if (lower == "squint_l")
                {
                    weight = pose.SquintLeft;
                }
                else if (lower == "squint_r")
                {
                    weight = pose.SquintRight;
                }
                else if (lower == "happy_m")
                {
                    weight = pose.Smile;
                }
                else if (lower == "sad_m")
                {
                    weight = pose.Frown;
                }
                else if (lower == "mouth_close_m")
                {
                    weight = mouthClose;
                }
                else if (lower == "mouth_open_m")
                {
                    weight = jaw;
                }
                else if (lower == "ctrleyewide_l")
                {
                    weight = pose.EyeWideLeft;
                }
                else if (lower == "ctrleyewide_r")
                {
                    weight = pose.EyeWideRight;
                }
                else if (lower == "brow_innerraiser_l" || lower == "brow_innerraiser_r")
                {
                    weight = pose.BrowInner;
                }
                else if (lower == "brow_innerlower_l" || lower == "brow_innerlower_r" ||
                         lower == "brow_lower_l" || lower == "brow_lower_r")
                {
                    weight = pose.BrowDown;
                }
                else if (IsDuplicateArkit(lower) || lower == "jawopen")
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

        private static FaceOverlayPose MouthOnly(FaceOverlayPose pose)
        {
            return new FaceOverlayPose
            {
                Smile = pose.Smile,
                Frown = pose.Frown,
                Jaw = pose.Jaw,
                MouthClose = pose.MouthClose
            };
        }

        internal static bool IsBodyFaceRenderer(SkinnedMeshRenderer renderer)
        {
            return renderer != null && renderer.name == "Full_Body";
        }

        internal static bool IsInnerMouthRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            var n = renderer.name.ToLowerInvariant();
            return n == "mouth.001" || n == "lower_teeth" || n == "upper_teeth" || n == "tongue";
        }

        internal static bool IsBlinkLeft(string name)
        {
            return !string.IsNullOrEmpty(name) && name.ToLowerInvariant() == "blink_l";
        }

        internal static bool IsBlinkRight(string name)
        {
            return !string.IsNullOrEmpty(name) && name.ToLowerInvariant() == "blink_r";
        }

        internal static bool IsDuplicateArkitBlink(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            return lower.Contains("eyeblinkleft") || lower.Contains("eyeblinkright");
        }

        private static bool IsDuplicateArkit(string lower)
        {
            return lower.Contains("eyeblinkleft") ||
                   lower.Contains("eyeblinkright") ||
                   lower.Contains("eyesquint") ||
                   lower.Contains("eyewide") ||
                   lower == "mouthclose" ||
                   lower.Contains("mouthsmile") ||
                   lower.Contains("mouthfrown");
        }
    }

    public struct FaceOverlayPose
    {
        public float BlinkLeft;
        public float BlinkRight;
        public float SquintLeft;
        public float SquintRight;
        public float Smile;
        public float Frown;
        public float Jaw;
        public float MouthClose;
        public float EyeWideLeft;
        public float EyeWideRight;
        public float BrowInner;
        public float BrowDown;
    }
}
