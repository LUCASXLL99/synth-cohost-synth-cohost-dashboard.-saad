using System;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Local playback of <c>ai.response</c> text. Not the backend <c>speech.*</c> viseme contract.
    /// </summary>
    internal interface IReplySpeechPlayback
    {
        bool IsActive { get; }

        void Play(string text, Action onCompleted);

        void Cancel();

        void Tick(AudioSource output);
    }
}
