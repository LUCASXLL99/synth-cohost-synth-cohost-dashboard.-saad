using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.AiResponse;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Dashboard character presentation for the SampleScene AvatarVerify rig.
    /// Plays existing Animator states. Does not talk to the socket itself.
    /// Local preview and Host config must not send <c>state.ack</c>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Synth Cohost/Dashboard Avatar Presenter")]
    public sealed class DashboardAvatarPresenter :
        AvatarBehaviorControllerBehaviour,
        IAiResponseSink,
        IAvatarVisualReset,
        IDashboardConfigApplier
    {
        private const float DebugOverlaySeconds = 2f;

        [SerializeField] private Animator animator;
        [SerializeField] private float crossFadeSeconds = 0.15f;
        [SerializeField] private float burstReturnSeconds = 3f;
        [SerializeField] private bool keepInPlace = true;
        [SerializeField] private bool lockEyes = true;
        [SerializeField] private bool drawCaptionOverlay = true;
        [SerializeField] private bool playReplySpeech = true;
        [SerializeField, Range(0f, 1f)] private float replySpeechVolume = 1f;

        public event Action<AvatarBehavior> BehaviorApplied;
        public event Action<AiResponsePayload> ResponseReceived;

        public bool HasBehavior { get; private set; }
        public AvatarBehavior LastBehavior { get; private set; } = AvatarBehavior.Idle;
        public AiResponsePayload LastResponse { get; private set; }
        public string CaptionText { get; private set; } = string.Empty;
        public string CurrentAnimatorStateName { get; private set; } = string.Empty;
        public string DebugOverlay { get; private set; } = string.Empty;
        public int LastFaceShapesApplied { get; private set; }
        public int LastMouthShapesApplied { get; private set; }
        public bool ReplyHoldActive { get; private set; }

        public bool LockEyes
        {
            get => lockEyes;
            set => lockEyes = value;
        }

        public bool DrawCaptionOverlay
        {
            get => drawCaptionOverlay;
            set
            {
                drawCaptionOverlay = value;
                captionView?.SetCaption(drawCaptionOverlay ? CaptionText : string.Empty);
            }
        }

        public bool PlayReplySpeech
        {
            get => playReplySpeech;
            set => playReplySpeech = value;
        }

        internal IReplySpeechPlayback SpeechPlayback
        {
            get => speechPlayback;
            set => speechPlayback = value;
        }

        private Transform rootM;
        private Transform eyeJointL;
        private Transform eyeJointR;
        private Vector3 restRootLocal;
        private Vector3 restBodyPosition;
        private Quaternion restBodyRotation;
        private Vector3 restEyePosL;
        private Vector3 restEyePosR;
        private Quaternion restEyeRotL;
        private Quaternion restEyeRotR;
        private bool hasEyeRest;
        private float burstDeadlineUnscaled = -1f;
        private float debugOverlayUntilUnscaled = -1f;
        private float replyHoldDeadlineUnscaled = -1f;
        private AvatarBehavior appliedBehavior = AvatarBehavior.Idle;
        private AiEmotion lastReplyEmotion = AiEmotion.Neutral;
        private DashboardCaptionView captionView;
        private SkinnedMeshRenderer[] faceRenderers;
        private AudioSource replyAudio;
        private IReplySpeechPlayback speechPlayback;

        private void Awake()
        {
            animator ??= GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            restBodyPosition = transform.position;
            restBodyRotation = transform.rotation;
            rootM = transform.Find("DeformationSystem/Root_M");
            if (rootM != null)
            {
                var p = rootM.localPosition;
                restRootLocal = Mathf.Abs(p.z) > 0.2f || Mathf.Abs(p.x) > 0.2f
                    ? new Vector3(0f, 1.007f, -0.005f)
                    : p;
            }

            eyeJointL = FindChildNamed(transform, "EyeJoint_L");
            eyeJointR = FindChildNamed(transform, "EyeJoint_R");
            if (eyeJointL != null && eyeJointR != null)
            {
                restEyePosL = eyeJointL.localPosition;
                restEyePosR = eyeJointR.localPosition;
                restEyeRotL = eyeJointL.localRotation;
                restEyeRotR = eyeJointR.localRotation;
                hasEyeRest = true;
            }

            faceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            captionView = DashboardCaptionView.Ensure(transform);
            replyAudio = GetComponent<AudioSource>();
            if (replyAudio == null)
            {
                replyAudio = gameObject.AddComponent<AudioSource>();
            }

            replyAudio.playOnAwake = false;
            replyAudio.loop = false;
            replyAudio.spatialBlend = 0f;
            replyAudio.mute = false;
            replyAudio.ignoreListenerPause = true;
            replyAudio.volume = replySpeechVolume;
        }

        private void Start()
        {
            ApplyLocal(AvatarBehavior.Idle, sendAppliedEvent: false);
        }

        private void Update()
        {
            if (burstDeadlineUnscaled >= 0f && Time.unscaledTime >= burstDeadlineUnscaled)
            {
                burstDeadlineUnscaled = -1f;
                if (DashboardBehaviorStateMap.IsBurst(appliedBehavior))
                {
                    ApplyLocal(AvatarBehavior.Idle, sendAppliedEvent: false);
                }
            }

            if (debugOverlayUntilUnscaled >= 0f && Time.unscaledTime >= debugOverlayUntilUnscaled)
            {
                debugOverlayUntilUnscaled = -1f;
                DebugOverlay = string.Empty;
            }

            LoopCurrentIfNeeded();
            RefreshAnimatorStateName();
            if (replyAudio != null)
            {
                replyAudio.volume = replySpeechVolume;
            }

            speechPlayback?.Tick(replyAudio);
            if (ReplyHoldActive &&
                replyHoldDeadlineUnscaled >= 0f &&
                Time.unscaledTime >= replyHoldDeadlineUnscaled &&
                (replyAudio == null || !replyAudio.isPlaying))
            {
                FinishReplyToLivingIdle();
            }
        }

        private void LoopCurrentIfNeeded()
        {
            if (animator == null || !DashboardBehaviorStateMap.IsLooping(appliedBehavior))
            {
                return;
            }

            if (animator.IsInTransition(0))
            {
                return;
            }

            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.normalizedTime >= 1f)
            {
                animator.Play(info.shortNameHash, 0, 0f);
            }
        }

        private void LateUpdate()
        {
            if (keepInPlace)
            {
                transform.SetPositionAndRotation(restBodyPosition, restBodyRotation);
                if (rootM != null)
                {
                    var p = rootM.localPosition;
                    p.x = restRootLocal.x;
                    p.z = restRootLocal.z;
                    rootM.localPosition = p;
                }
            }

            if (lockEyes && hasEyeRest)
            {
                eyeJointL.localPosition = restEyePosL;
                eyeJointR.localPosition = restEyePosR;
                eyeJointL.localRotation = restEyeRotL;
                eyeJointR.localRotation = restEyeRotR;
            }

            ApplyFaceOverlay();
        }

        public override Task<bool> ApplyAsync(AvatarBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applied = ApplyLocal(behavior, sendAppliedEvent: true);
            return Task.FromResult(applied);
        }

        /// <summary>
        /// Local live-test preview. Same Animator apply as the wire path; does not send
        /// <c>state.ack</c> and does not cancel in-flight reply speech.
        /// </summary>
        public Task<bool> PreviewLocalAsync(AvatarBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applied = ApplyLocal(behavior, sendAppliedEvent: false);
            return Task.FromResult(applied);
        }

        public Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastResponse = response;
            ResponseReceived?.Invoke(response);
            if (response == null)
            {
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                if (appliedBehavior == AvatarBehavior.Speaking ||
                    appliedBehavior == AvatarBehavior.Thinking)
                {
                    BeginReplyHold(string.Empty);
                }

                return Task.CompletedTask;
            }

            CaptionText =
                $"{response.Text}\n[{response.Emotion.ToWireValue()} · {response.Intent.ToWireValue()}]";
            if (drawCaptionOverlay)
            {
                captionView?.SetCaption(CaptionText);
            }

            LastFaceShapesApplied = DashboardFaceBlendDriver.ApplyEmotion(faceRenderers, response.Emotion);
            lastReplyEmotion = response.Emotion;
            Debug.Log(
                $"[SynthCohost/Avatar] Inbound reply emotion applied ({response.Emotion.ToWireValue()}); " +
                $"faceShapes={LastFaceShapesApplied}; reply text was not logged.");
            BeginReplyHold(response.Text);
            return Task.CompletedTask;
        }

        public void ResetToLivingIdle()
        {
            CancelReplyHold();
            burstDeadlineUnscaled = -1f;
            CaptionText = string.Empty;
            captionView?.SetCaption(string.Empty);
            DashboardSpeechMouthDriver.Clear(faceRenderers);
            DashboardFaceBlendDriver.Clear(faceRenderers);
            LastFaceShapesApplied = 0;
            LastMouthShapesApplied = 0;
            ApplyLocal(AvatarBehavior.Idle, sendAppliedEvent: false);
        }

        public void ClearCaption()
        {
            CaptionText = string.Empty;
            captionView?.SetCaption(string.Empty);
        }

        public void ApplyUnknownSafe(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            DashboardConfigPatch patch;
            try
            {
                patch = JsonUtility.FromJson<DashboardConfigPatch>(json.Trim());
            }
            catch (Exception)
            {
                return;
            }

            if (patch == null)
            {
                return;
            }

            if (TryParseToggle(patch.captions, out var captionsOn))
            {
                DrawCaptionOverlay = captionsOn;
            }

            if (TryParseToggle(patch.lockEyes, out var eyesLocked))
            {
                LockEyes = eyesLocked;
            }

            if (TryParseToggle(patch.tts, out var ttsOn))
            {
                PlayReplySpeech = ttsOn;
            }
        }

        private void OnDisable()
        {
            CancelReplyHold();
        }

        private void BeginReplyHold(string spokenText)
        {
            CancelReplyHold();
            ReplyHoldActive = true;
            replyHoldDeadlineUnscaled = Time.unscaledTime + ReplySpeech.EstimateHoldSeconds(spokenText) + 2.5f;
            EnsureSpeechPlayback();
            speechPlayback.Play(spokenText, OnReplyPlaybackCompleted);
        }

        private void OnReplyPlaybackCompleted()
        {
            if (!ReplyHoldActive)
            {
                return;
            }

            FinishReplyToLivingIdle();
        }

        private void FinishReplyToLivingIdle()
        {
            ReplyHoldActive = false;
            replyHoldDeadlineUnscaled = -1f;
            speechPlayback?.Cancel();
            if (replyAudio != null)
            {
                replyAudio.Stop();
            }

            DashboardSpeechMouthDriver.Clear(faceRenderers);
            DashboardRigFaceOverlay.Clear(faceRenderers);
            DashboardFaceBlendDriver.Clear(faceRenderers);
            LastFaceShapesApplied = 0;
            LastMouthShapesApplied = 0;
            lastReplyEmotion = AiEmotion.Neutral;
            ApplyLocal(AvatarBehavior.Idle, sendAppliedEvent: false);
        }

        private void CancelReplyHold()
        {
            ReplyHoldActive = false;
            replyHoldDeadlineUnscaled = -1f;
            speechPlayback?.Cancel();
            if (replyAudio != null)
            {
                replyAudio.Stop();
            }

            DashboardSpeechMouthDriver.Clear(faceRenderers);
            LastMouthShapesApplied = 0;
        }

        private void EnsureSpeechPlayback()
        {
            if (speechPlayback != null)
            {
                return;
            }

            speechPlayback = playReplySpeech && Application.isPlaying
                ? ReplySpeechPlaybackFactory.CreatePlatformDefault()
                : new EstimatedReplySpeechPlayback();
        }

        private bool ApplyLocal(AvatarBehavior behavior, bool sendAppliedEvent)
        {
            if (sendAppliedEvent && behavior != AvatarBehavior.Speaking)
            {
                CancelReplyHold();
                DashboardFaceBlendDriver.Clear(faceRenderers);
                LastFaceShapesApplied = 0;
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            if (!DashboardBehaviorStateMap.TryGetStateName(behavior, out var stateName) ||
                string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            var hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash))
            {
                return false;
            }

            ResetQueuedTriggers(animator);
            var fade = Mathf.Max(0f, crossFadeSeconds);
            if (fade <= 0.001f)
            {
                animator.Play(hash, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
            }

            appliedBehavior = behavior;
            HasBehavior = true;
            LastBehavior = behavior;
            CurrentAnimatorStateName = stateName;
            DebugOverlay = $"{behavior.ToWireValue()} → {stateName}";
            debugOverlayUntilUnscaled = Time.unscaledTime + DebugOverlaySeconds;
            burstDeadlineUnscaled = DashboardBehaviorStateMap.IsBurst(behavior)
                ? Time.unscaledTime + Mathf.Max(0.1f, burstReturnSeconds)
                : -1f;

            if (sendAppliedEvent)
            {
                BehaviorApplied?.Invoke(behavior);
            }

            return true;
        }

        private void ApplyFaceOverlay()
        {
            var peak = MeasurePlaybackPeak();
            var normalized = 0f;
            var finished = false;
            if (animator != null && !animator.IsInTransition(0))
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                normalized = info.normalizedTime;
                finished = !DashboardBehaviorStateMap.IsLooping(appliedBehavior) &&
                           normalized >= 1f;
            }

            if (ReplyHoldActive)
            {
                var hold = DashboardRigFaceOverlay.Resolve(CurrentAnimatorStateName);
                if (lastReplyEmotion == AiEmotion.Happy ||
                    lastReplyEmotion == AiEmotion.Excited ||
                    lastReplyEmotion == AiEmotion.Celebrate)
                {
                    hold.Smile = Mathf.Max(hold.Smile, 85f);
                }

                LastMouthShapesApplied = DashboardRigFaceOverlay.Apply(faceRenderers, hold, peak);
                LastFaceShapesApplied = LastMouthShapesApplied;
                return;
            }

            LastMouthShapesApplied = DashboardRigFaceOverlay.ApplyForAnimatorState(
                faceRenderers,
                CurrentAnimatorStateName,
                peak,
                normalized,
                finished);
            LastFaceShapesApplied = LastMouthShapesApplied;
        }

        private float MeasurePlaybackPeak()
        {
            if (replyAudio == null || !replyAudio.isPlaying)
            {
                return 0f;
            }

            var samples = new float[256];
            replyAudio.GetOutputData(samples, 0);
            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var v = samples[i] < 0f ? -samples[i] : samples[i];
                if (v > peak)
                {
                    peak = v;
                }
            }

            return peak;
        }

        private void RefreshAnimatorStateName()
        {
            if (animator == null || animator.IsInTransition(0))
            {
                return;
            }

            if (DashboardBehaviorStateMap.TryGetStateName(appliedBehavior, out var mapped) &&
                animator.GetCurrentAnimatorStateInfo(0).IsName(mapped))
            {
                CurrentAnimatorStateName = mapped;
            }
        }

        private static bool TryParseToggle(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "on":
                case "yes":
                    result = true;
                    return true;
                case "0":
                case "false":
                case "off":
                case "no":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        private static void ResetQueuedTriggers(Animator target)
        {
            foreach (var parameter in target.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    target.ResetTrigger(parameter.nameHash);
                }
            }
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].name == name)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
