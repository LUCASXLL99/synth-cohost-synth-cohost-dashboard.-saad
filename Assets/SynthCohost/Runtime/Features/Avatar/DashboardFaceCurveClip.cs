using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Authored face envelope baked from Maya face-rig JSON onto CC blendshape weights.
    /// Sampled by <see cref="DashboardFaceCurveSampler"/> at Animator normalized time.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FaceCurve",
        menuName = "Synth Cohost/Face Curve Clip")]
    public sealed class DashboardFaceCurveClip : ScriptableObject
    {
        [SerializeField] private string stateName = string.Empty;
        [SerializeField] private float durationSeconds = 1f;
        [SerializeField] private AnimationCurve blinkLeft = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve blinkRight = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve squintLeft = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve squintRight = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve smile = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve frown = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve jaw = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve mouthClose = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve eyeWideLeft = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve eyeWideRight = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve browInner = AnimationCurve.Constant(0f, 1f, 0f);
        [SerializeField] private AnimationCurve browDown = AnimationCurve.Constant(0f, 1f, 0f);

        public string StateName => stateName;
        public float DurationSeconds => durationSeconds;

        public void Configure(
            string name,
            float duration,
            AnimationCurve blinkL,
            AnimationCurve blinkR,
            AnimationCurve squintL,
            AnimationCurve squintR,
            AnimationCurve smileCurve,
            AnimationCurve frownCurve,
            AnimationCurve jawCurve,
            AnimationCurve mouthCloseCurve,
            AnimationCurve eyeWideL,
            AnimationCurve eyeWideR,
            AnimationCurve browInnerCurve,
            AnimationCurve browDownCurve)
        {
            stateName = name ?? string.Empty;
            durationSeconds = Mathf.Max(0.01f, duration);
            blinkLeft = blinkL ?? AnimationCurve.Constant(0f, 1f, 0f);
            blinkRight = blinkR ?? AnimationCurve.Constant(0f, 1f, 0f);
            squintLeft = squintL ?? AnimationCurve.Constant(0f, 1f, 0f);
            squintRight = squintR ?? AnimationCurve.Constant(0f, 1f, 0f);
            smile = smileCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
            frown = frownCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
            jaw = jawCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
            mouthClose = mouthCloseCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
            eyeWideLeft = eyeWideL ?? AnimationCurve.Constant(0f, 1f, 0f);
            eyeWideRight = eyeWideR ?? AnimationCurve.Constant(0f, 1f, 0f);
            browInner = browInnerCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
            browDown = browDownCurve ?? AnimationCurve.Constant(0f, 1f, 0f);
        }

        public FaceOverlayPose SampleNormalized(float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);
            return new FaceOverlayPose
            {
                BlinkLeft = Evaluate(blinkLeft, t),
                BlinkRight = Evaluate(blinkRight, t),
                SquintLeft = Evaluate(squintLeft, t),
                SquintRight = Evaluate(squintRight, t),
                Smile = Evaluate(smile, t),
                Frown = Evaluate(frown, t),
                Jaw = Evaluate(jaw, t),
                MouthClose = Evaluate(mouthClose, t),
                EyeWideLeft = Evaluate(eyeWideLeft, t),
                EyeWideRight = Evaluate(eyeWideRight, t),
                BrowInner = Evaluate(browInner, t),
                BrowDown = Evaluate(browDown, t)
            };
        }

        private static float Evaluate(AnimationCurve curve, float t)
        {
            return curve == null || curve.length == 0 ? 0f : curve.Evaluate(t);
        }
    }
}
