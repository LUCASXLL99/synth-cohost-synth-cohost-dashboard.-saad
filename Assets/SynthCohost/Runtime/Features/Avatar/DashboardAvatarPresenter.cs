using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.AiResponse;
using SynthCohost.Runtime.Features.Errors;
using SynthCohost.Runtime.Features.Speech;
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
    [DefaultExecutionOrder(32000)]
    [AddComponentMenu("Synth Cohost/Dashboard Avatar Presenter")]
    public sealed class DashboardAvatarPresenter :
        AvatarBehaviorControllerBehaviour,
        IAiResponseSink,
        IAvatarVisualReset,
        IDashboardConfigApplier,
        ISpeechAudioSink,
        ISystemErrorSink
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
        [SerializeField] private DashboardFaceCurveCatalog faceCurveCatalog;
        [SerializeField] private bool enableRelaxedIdle = true;
        [SerializeField] private float relaxedIdleAfterSeconds = 18f;
        [SerializeField] private float backendSpeechWaitSeconds = 10f;
        [SerializeField, Range(8f, 80f)] private float mouthOpenMaxWeight = 40f;

        public event Action<AvatarBehavior> BehaviorApplied;
        public event Action<AiResponsePayload> ResponseReceived;
        public event Action SpeechWaitArmed;
        public event Action<int, string, int, int, bool> SpeechAudioAccepted;
        public event Action<string> SpeechFailedNotified;
        public event Action<string> SpeechFallbackRaised;

        public bool HasBehavior { get; private set; }
        public AvatarBehavior LastBehavior { get; private set; } = AvatarBehavior.Idle;
        public AiResponsePayload LastResponse { get; private set; }
        public string CaptionText { get; private set; } = string.Empty;
        public string CurrentAnimatorStateName { get; private set; } = string.Empty;
        public string DebugOverlay { get; private set; } = string.Empty;
        public int LastFaceShapesApplied { get; private set; }
        public int LastMouthShapesApplied { get; private set; }
        public bool ReplyHoldActive { get; private set; }
        public bool WaitingForBackendSpeech { get; private set; }

        internal float MouthOpenMaxWeight
        {
            get => mouthOpenMaxWeight;
            set => mouthOpenMaxWeight = Mathf.Clamp(value, 8f, 80f);
        }

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

        public DashboardFaceCurveCatalog FaceCurveCatalog
        {
            get => faceCurveCatalog;
            set => faceCurveCatalog = value;
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
        private float backendSpeechWaitDeadlineUnscaled = -1f;
        private string pendingLocalFallbackText = string.Empty;
        private float relaxedIdleAtUnscaled = -1f;
        private bool inRelaxedIdle;
        private float unmuteAudioAtUnscaled = -1f;
        private LipSyncPose smoothedLipPose;
        private bool hasSmoothedLipPose;
        private AvatarBehavior appliedBehavior = AvatarBehavior.Idle;
        private AiEmotion lastReplyEmotion = AiEmotion.Neutral;
        private DashboardCaptionView captionView;
        private SkinnedMeshRenderer[] faceRenderers;
        private AudioSource replyAudio;
        private IReplySpeechPlayback speechPlayback;
        private BackendSpeechPlayback backendSpeech;

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
            backendSpeech?.Tick(replyAudio);
            if (unmuteAudioAtUnscaled >= 0f && Time.unscaledTime >= unmuteAudioAtUnscaled)
            {
                unmuteAudioAtUnscaled = -1f;
                if (replyAudio != null)
                {
                    replyAudio.mute = false;
                }
            }

            if (WaitingForBackendSpeech &&
                backendSpeechWaitDeadlineUnscaled >= 0f &&
                Time.unscaledTime >= backendSpeechWaitDeadlineUnscaled)
            {
                FallbackToLocalSpeech("backend-speech-timeout");
            }

            if (ReplyHoldActive &&
                !WaitingForBackendSpeech &&
                (backendSpeech == null || !backendSpeech.IsActive) &&
                (speechPlayback == null || !speechPlayback.IsActive) &&
                backendSpeech != null &&
                backendSpeech.SawFinalPacket)
            {
                FinishReplyToLivingIdle();
            }

            if (ReplyHoldActive &&
                replyHoldDeadlineUnscaled >= 0f &&
                Time.unscaledTime >= replyHoldDeadlineUnscaled &&
                (replyAudio == null || !replyAudio.isPlaying) &&
                (backendSpeech == null || !backendSpeech.IsActive))
            {
                FinishReplyToLivingIdle();
            }

            TickRelaxedIdle();
        }

        private void TickRelaxedIdle()
        {
            if (!enableRelaxedIdle || ReplyHoldActive || animator == null)
            {
                return;
            }

            if (appliedBehavior != AvatarBehavior.Idle)
            {
                relaxedIdleAtUnscaled = -1f;
                inRelaxedIdle = false;
                return;
            }

            if (inRelaxedIdle)
            {
                if (animator.IsInTransition(0))
                {
                    return;
                }

                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(DashboardBehaviorStateMap.RelaxedEnter) && info.normalizedTime >= 1f)
                {
                    PlayNamedState(DashboardBehaviorStateMap.RelaxedLoop, loopName: true);
                }

                return;
            }

            if (relaxedIdleAtUnscaled < 0f)
            {
                relaxedIdleAtUnscaled = Time.unscaledTime + Mathf.Max(4f, relaxedIdleAfterSeconds);
                return;
            }

            if (Time.unscaledTime < relaxedIdleAtUnscaled)
            {
                return;
            }

            if (!animator.HasState(0, Animator.StringToHash(DashboardBehaviorStateMap.RelaxedEnter)))
            {
                enableRelaxedIdle = false;
                return;
            }

            inRelaxedIdle = true;
            PlayNamedState(DashboardBehaviorStateMap.RelaxedEnter, loopName: false);
        }

        private void PlayNamedState(string stateName, bool loopName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            var hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash))
            {
                return;
            }

            var fade = Mathf.Max(0f, crossFadeSeconds);
            if (fade <= 0.001f)
            {
                animator.Play(hash, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
            }

            CurrentAnimatorStateName = stateName;
            if (loopName)
            {
                // Keep Idle as the wire behavior; only the Animator state changes.
            }
        }

        private void LoopCurrentIfNeeded()
        {
            if (animator == null || !DashboardBehaviorStateMap.IsLooping(appliedBehavior))
            {
                return;
            }

            if (inRelaxedIdle)
            {
                if (animator.IsInTransition(0))
                {
                    return;
                }

                var relaxed = animator.GetCurrentAnimatorStateInfo(0);
                if (relaxed.IsName(DashboardBehaviorStateMap.RelaxedLoop) && relaxed.normalizedTime >= 1f)
                {
                    animator.Play(relaxed.shortNameHash, 0, 0f);
                }

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

            if (response.Behavior.HasValue)
            {
                // LLM contract: behavior on ai.response is the animation cue for this reply
                // (e.g. listening backchannel vs full speaking). Prefer it for this turn.
                ApplyLocal(response.Behavior.Value, sendAppliedEvent: false);
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

            // Idle / listening backchannels: still speak short cues, but do not force a long
            // "full reply" speaking hold when the contract says stay neutral/listening.
            var speak = ShouldSpeakReply(response);
            if (speak && playReplySpeech)
            {
                ArmBackendSpeechWait(response.Text);
            }

            return Task.CompletedTask;
        }

        private static bool ShouldSpeakReply(AiResponsePayload response)
        {
            if (!response.Behavior.HasValue)
            {
                return true;
            }

            switch (response.Behavior.Value)
            {
                case AvatarBehavior.Idle:
                    // Fixed fallback strings (moderation / capacity) — caption only unless very short ack.
                    return response.Text != null && response.Text.Trim().Length <= 48;
                case AvatarBehavior.Listening:
                case AvatarBehavior.Speaking:
                case AvatarBehavior.Happy:
                case AvatarBehavior.Celebrate:
                case AvatarBehavior.Thinking:
                    return true;
                default:
                    return true;
            }
        }

        public Task PresentAsync(SystemErrorPayload error, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error == null)
            {
                return Task.CompletedTask;
            }

            if (ProtocolSystemErrorCodes.IsAuthenticationFailure(error.Code))
            {
                ClearCaption();
                ResetToLivingIdle();
                return Task.CompletedTask;
            }

            CancelReplyHold();
            if (appliedBehavior == AvatarBehavior.Thinking ||
                appliedBehavior == AvatarBehavior.Speaking)
            {
                ApplyLocal(AvatarBehavior.Idle, sendAppliedEvent: false);
            }

            return Task.CompletedTask;
        }

        public void ResetToLivingIdle()
        {
            CancelReplyHold();
            burstDeadlineUnscaled = -1f;
            CaptionText = string.Empty;
            captionView?.SetCaption(string.Empty);
            DashboardSpeechMouthDriver.Clear(faceRenderers);
            DashboardLipSyncDriver.Clear(faceRenderers);
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

        public Task HandleAudioAsync(SpeechAudioPayload payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (payload == null)
            {
                return Task.CompletedTask;
            }

            WaitingForBackendSpeech = false;
            backendSpeechWaitDeadlineUnscaled = -1f;
            pendingLocalFallbackText = string.Empty;
            CutLocalSpeechForBackendHandoff();
            backendSpeech ??= new BackendSpeechPlayback();
            backendSpeech.MouthOpenMaxWeight = mouthOpenMaxWeight;
            ReplyHoldActive = true;
            var holdSeconds = payload.DurationMs > 0
                ? payload.DurationMs / 1000f
                : 4f;
            replyHoldDeadlineUnscaled = Time.unscaledTime + holdSeconds + 28f;
            AudioListener.pause = false;
            EnsureAudioListener();
            backendSpeech.Enqueue(payload);
            backendSpeech.Tick(replyAudio);
            payload.TryGetAudioBytes(out var audioBytes);
            SpeechAudioAccepted?.Invoke(
                payload.Seq,
                InferSafeAudioFormat(payload),
                audioBytes != null ? audioBytes.Length : 0,
                payload.Frames != null ? payload.Frames.Length : 0,
                payload.FinalPacket);
            return Task.CompletedTask;
        }

        public Task HandleFailedAsync(SpeechFailedPayload payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.LogWarning("[SynthCohost/Avatar] Inbound speech.failed; using local TTS fallback if a reply is waiting.");
            SpeechFailedNotified?.Invoke(payload != null ? payload.Code : string.Empty);
            if (WaitingForBackendSpeech)
            {
                FallbackToLocalSpeech("speech-failed");
            }

            return Task.CompletedTask;
        }

        internal void FlushBackendSpeechWaitForTests()
        {
            if (WaitingForBackendSpeech)
            {
                FallbackToLocalSpeech("test-flush");
            }
        }

        private void ArmBackendSpeechWait(string spokenText)
        {
            CancelReplyHold();
            WaitingForBackendSpeech = true;
            pendingLocalFallbackText = spokenText ?? string.Empty;
            ReplyHoldActive = true;
            backendSpeechWaitDeadlineUnscaled = Time.unscaledTime + Mathf.Max(0.5f, backendSpeechWaitSeconds);
            replyHoldDeadlineUnscaled = Time.unscaledTime + Mathf.Max(8f, backendSpeechWaitSeconds) + 28f;
            Debug.Log("[SynthCohost/Avatar] Waiting for backend speech.audio before local TTS fallback.");
            SpeechWaitArmed?.Invoke();
        }

        private void FallbackToLocalSpeech(string reason)
        {
            WaitingForBackendSpeech = false;
            backendSpeechWaitDeadlineUnscaled = -1f;
            var text = pendingLocalFallbackText;
            pendingLocalFallbackText = string.Empty;
            Debug.LogWarning(
                reason == "backend-speech-timeout"
                    ? "[SynthCohost/Avatar] Backend speech fallback (backend-speech-timeout); " +
                      "no inbound speech.audio or speech.failed on this turn. Using local Windows TTS."
                    : $"[SynthCohost/Avatar] Backend speech fallback ({reason}); using local TTS.");
            SpeechFallbackRaised?.Invoke(reason);
            if (replyAudio != null)
            {
                replyAudio.mute = false;
            }

            BeginReplyHold(text);
        }

        private void CutLocalSpeechForBackendHandoff()
        {
            speechPlayback?.Cancel();
            if (replyAudio == null)
            {
                return;
            }

            replyAudio.Stop();
            replyAudio.clip = null;
            replyAudio.mute = true;
            unmuteAudioAtUnscaled = Time.unscaledTime + 0.05f;
        }

        private static string InferSafeAudioFormat(SpeechAudioPayload payload)
        {
            var contentType = payload != null && payload.Audio != null
                ? payload.Audio.ContentType
                : string.Empty;
            if (string.IsNullOrEmpty(contentType))
            {
                return "unknown";
            }

            var lower = contentType.ToLowerInvariant();
            if (lower.IndexOf("mpeg", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("mp3", StringComparison.Ordinal) >= 0)
            {
                return "mp3";
            }

            if (lower.IndexOf("wav", StringComparison.Ordinal) >= 0)
            {
                return "wav";
            }

            if (lower.IndexOf("pcm", StringComparison.Ordinal) >= 0)
            {
                return "pcm";
            }

            return "other";
        }

        private void BeginReplyHold(string spokenText)
        {
            CancelReplyHold();
            ReplyHoldActive = true;
            replyHoldDeadlineUnscaled = Time.unscaledTime + ReplySpeech.EstimateHoldSeconds(spokenText) + 28f;
            AudioListener.pause = false;
            if (AudioListener.volume < 0.2f)
            {
                AudioListener.volume = 1f;
            }

            EnsureAudioListener();
            EnsureSpeechPlayback();
            speechPlayback.Play(spokenText, OnReplyPlaybackCompleted);
            ApplyFaceOverlay();
        }

        private static void EnsureAudioListener()
        {
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera != null)
            {
                camera.gameObject.AddComponent<AudioListener>();
            }
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
            WaitingForBackendSpeech = false;
            backendSpeechWaitDeadlineUnscaled = -1f;
            pendingLocalFallbackText = string.Empty;
            hasSmoothedLipPose = false;
            smoothedLipPose = LipSyncPose.Rest;
            backendSpeech?.Cancel();
            speechPlayback?.Cancel();
            if (replyAudio != null)
            {
                replyAudio.Stop();
            }

            DashboardSpeechMouthDriver.Clear(faceRenderers);
            DashboardLipSyncDriver.Clear(faceRenderers);
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
            WaitingForBackendSpeech = false;
            backendSpeechWaitDeadlineUnscaled = -1f;
            pendingLocalFallbackText = string.Empty;
            hasSmoothedLipPose = false;
            smoothedLipPose = LipSyncPose.Rest;
            backendSpeech?.Cancel();
            speechPlayback?.Cancel();
            if (replyAudio != null)
            {
                replyAudio.Stop();
            }

            DashboardSpeechMouthDriver.Clear(faceRenderers);
            DashboardLipSyncDriver.Clear(faceRenderers);
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

            inRelaxedIdle = false;
            relaxedIdleAtUnscaled = behavior == AvatarBehavior.Idle
                ? Time.unscaledTime + Mathf.Max(4f, relaxedIdleAfterSeconds)
                : -1f;

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
            var talking = ReplyHoldActive || appliedBehavior == AvatarBehavior.Speaking;
            var peak = ReplySpeech.TalkPeak(MeasurePlaybackPeak(), talking, Time.unscaledTime);
            var normalized = 0f;
            var finished = false;
            if (animator != null && !animator.IsInTransition(0))
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                normalized = info.normalizedTime;
                finished = !DashboardBehaviorStateMap.IsLooping(appliedBehavior) &&
                           normalized >= 1f;
            }

            if (talking)
            {
                FaceOverlayPose hold;
                if (!DashboardFaceCurveSampler.TrySample(
                        faceCurveCatalog,
                        CurrentAnimatorStateName,
                        normalized,
                        out hold))
                {
                    hold = DashboardRigFaceOverlay.Resolve(CurrentAnimatorStateName);
                }

                var hasLipSync = false;
                var lipPose = LipSyncPose.Rest;
                if (backendSpeech != null)
                {
                    hasLipSync = backendSpeech.TrySampleLipSync(replyAudio, out lipPose);
                }

                if (!hasLipSync && speechPlayback != null)
                {
                    hasLipSync = speechPlayback.TrySampleLipSync(replyAudio, out lipPose);
                }

                var synthesizing = !hasLipSync &&
                    (WaitingForBackendSpeech ||
                     (speechPlayback != null && speechPlayback.IsActive));
                hold = DashboardRigFaceOverlay.ForTalking(
                    hold,
                    lastReplyEmotion == AiEmotion.Happy ||
                    lastReplyEmotion == AiEmotion.Excited ||
                    lastReplyEmotion == AiEmotion.Celebrate,
                    forceJawFloor: !hasLipSync && !synthesizing);

                if (hasLipSync)
                {
                    if (!hasSmoothedLipPose)
                    {
                        smoothedLipPose = lipPose;
                        hasSmoothedLipPose = true;
                    }
                    else
                    {
                        var blend = Mathf.Clamp01(28f * Time.unscaledDeltaTime);
                        smoothedLipPose = LipSyncVisemeMap.Lerp(smoothedLipPose, lipPose, blend);
                    }

                    LastMouthShapesApplied = DashboardRigFaceOverlay.Apply(faceRenderers, hold, 0f);
                    LastMouthShapesApplied += DashboardLipSyncDriver.Apply(faceRenderers, smoothedLipPose);
                }
                else if (synthesizing)
                {
                    // PowerShell TTS takes 1–3s. Do not flap jaw 32–62 while waiting.
                    hasSmoothedLipPose = false;
                    LastMouthShapesApplied = DashboardRigFaceOverlay.Apply(faceRenderers, hold, 0f);
                }
                else
                {
                    hasSmoothedLipPose = false;
                    LastMouthShapesApplied = DashboardRigFaceOverlay.Apply(faceRenderers, hold, peak);
                }

                LastFaceShapesApplied = LastMouthShapesApplied;
                return;
            }

            LastMouthShapesApplied = DashboardRigFaceOverlay.ApplyForAnimatorState(
                faceRenderers,
                CurrentAnimatorStateName,
                peak,
                normalized,
                finished,
                faceCurveCatalog);
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
