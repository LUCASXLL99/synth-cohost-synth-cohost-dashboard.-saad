using System;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Completes after an estimated speak duration. Used in EditMode and when TTS is off/unavailable.
    /// </summary>
    internal sealed class EstimatedReplySpeechPlayback : IReplySpeechPlayback
    {
        private Action onCompleted;
        private float deadlineUnscaled = -1f;

        public bool IsActive => onCompleted != null;

        public void Play(string text, Action onCompleted)
        {
            Cancel();
            this.onCompleted = onCompleted ?? (Action)(() => { });
            deadlineUnscaled = Time.unscaledTime + ReplySpeech.EstimateHoldSeconds(text);
        }

        public void Cancel()
        {
            onCompleted = null;
            deadlineUnscaled = -1f;
        }

        public void Tick(AudioSource output)
        {
            if (onCompleted == null || deadlineUnscaled < 0f || Time.unscaledTime < deadlineUnscaled)
            {
                return;
            }

            Complete();
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
            callback?.Invoke();
        }
    }
}
