using System;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Completes after an estimated speak duration. Used in EditMode and when TTS is off/unavailable.
    /// Uses an approximate letter→viseme schedule so the mouth still articulates.
    /// </summary>
    internal sealed class EstimatedReplySpeechPlayback : IReplySpeechPlayback
    {
        private Action onCompleted;
        private float deadlineUnscaled = -1f;
        private float startedUnscaled = -1f;
        private LipSyncTrack track;

        public bool IsActive => onCompleted != null;

        public void Play(string text, Action onCompleted)
        {
            Cancel();
            this.onCompleted = onCompleted ?? (Action)(() => { });
            var seconds = ReplySpeech.EstimateHoldSeconds(text);
            startedUnscaled = Time.unscaledTime;
            deadlineUnscaled = startedUnscaled + seconds;
            track = LipSyncTrack.FromApproximateText(text, seconds);
        }

        public void Cancel()
        {
            onCompleted = null;
            deadlineUnscaled = -1f;
            startedUnscaled = -1f;
            track = null;
        }

        public void Tick(AudioSource output)
        {
            if (onCompleted == null || deadlineUnscaled < 0f || Time.unscaledTime < deadlineUnscaled)
            {
                return;
            }

            Complete();
        }

        public bool TrySampleLipSync(AudioSource output, out LipSyncPose pose)
        {
            pose = LipSyncPose.Rest;
            if (track == null || track.Count == 0 || startedUnscaled < 0f)
            {
                return false;
            }

            var time = Mathf.Max(0f, Time.unscaledTime - startedUnscaled);
            pose = track.Sample(time);
            return true;
        }

        internal void CompleteNow()
        {
            Complete();
        }

        private void Complete()
        {
            var callback = onCompleted;
            onCompleted = null;
            deadlineUnscaled = -1f;
            startedUnscaled = -1f;
            track = null;
            callback?.Invoke();
        }
    }
}
