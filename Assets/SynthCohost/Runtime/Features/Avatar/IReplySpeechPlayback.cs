using System;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Local playback of <c>ai.response</c> text.
    /// Optional timed lip-sync track (SAPI visemes or approximate text schedule).
    /// </summary>
    internal interface IReplySpeechPlayback
    {
        bool IsActive { get; }

        void Play(string text, Action onCompleted);

        void Cancel();

        void Tick(AudioSource output);

        /// <summary>
        /// Samples mouth shapes for the current playback time when a viseme track exists.
        /// </summary>
        bool TrySampleLipSync(AudioSource output, out LipSyncPose pose);
    }
}
