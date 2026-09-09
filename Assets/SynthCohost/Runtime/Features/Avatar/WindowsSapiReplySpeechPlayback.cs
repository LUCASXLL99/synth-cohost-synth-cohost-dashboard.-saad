using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Synthesizes <c>ai.response</c> to a WAV via System.Speech, then plays it on the
    /// avatar AudioSource. COM Speak-to-device hangs in this Editor and never ends the hold.
    /// </summary>
    internal sealed class WindowsSapiReplySpeechPlayback : IReplySpeechPlayback
    {
        private readonly object gate = new object();
        private CancellationTokenSource cts;
        private Action onCompleted;
        private byte[] pendingWav;
        private AudioClip playingClip;
        private bool finished;
        private bool failed;
        private bool startedClip;
        private float deadlineUnscaled = -1f;
        private string lastSafeStatus = "idle";

        public bool IsActive
        {
            get
            {
                lock (gate)
                {
                    return onCompleted != null;
                }
            }
        }

        public string LastSafeStatus
        {
            get
            {
                lock (gate)
                {
                    return lastSafeStatus;
                }
            }
        }

        public void Play(string text, Action onCompleted)
        {
            Cancel();
            var sanitized = ReplySpeech.Sanitize(text);
            this.onCompleted = onCompleted ?? (Action)(() => { });
            deadlineUnscaled = Time.unscaledTime + ReplySpeech.EstimateHoldSeconds(sanitized) + 2f;
            if (sanitized.Length == 0)
            {
                failed = true;
                lastSafeStatus = "empty-text";
                return;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;
            lastSafeStatus = "synthesizing";
            Debug.Log("[SynthCohost/Avatar] Reply speech started (Windows System.Speech WAV). Text was not logged.");
            var thread = new Thread(() => SynthesizeWav(sanitized, token))
            {
                IsBackground = true,
                Name = "SynthCohostTtsWav"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public void Cancel()
        {
            CancellationTokenSource local;
            AudioClip clip;
            lock (gate)
            {
                local = cts;
                cts = null;
                onCompleted = null;
                pendingWav = null;
                clip = playingClip;
                playingClip = null;
                finished = false;
                failed = false;
                startedClip = false;
                deadlineUnscaled = -1f;
                lastSafeStatus = "idle";
            }

            try
            {
                local?.Cancel();
                local?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }

        public void Tick(AudioSource output)
        {
            byte[] wav;
            lock (gate)
            {
                wav = pendingWav;
                pendingWav = null;
            }

            if (wav != null && output != null &&
                WavAudioClipFactory.TryCreate(wav, "SynthCohostReply", out var clip))
            {
                if (playingClip != null)
                {
                    UnityEngine.Object.Destroy(playingClip);
                }

                playingClip = clip;
                output.Stop();
                output.clip = clip;
                output.loop = false;
                output.spatialBlend = 0f;
                output.Play();
                startedClip = true;
                lastSafeStatus = "playing";
                Debug.Log("[SynthCohost/Avatar] Reply speech playing through avatar AudioSource.");
            }

            var audioPlaying = startedClip && output != null && output.isPlaying;
            var audioDone = startedClip && !audioPlaying;
            var timedOut = deadlineUnscaled > 0f && Time.unscaledTime >= deadlineUnscaled;
            var synthFailed = false;
            lock (gate)
            {
                synthFailed = failed;
                if (finished)
                {
                    audioDone = true;
                    finished = false;
                }
            }

            if (audioPlaying)
            {
                return;
            }

            if (!audioDone && !timedOut && !synthFailed)
            {
                return;
            }

            if (synthFailed && !startedClip)
            {
                Debug.LogWarning(
                    "[SynthCohost/Avatar] Windows speech synthesis failed; ending talk hold. " +
                    "Unmute the Game view / Audio Listener.");
            }
            else if (timedOut && !startedClip)
            {
                Debug.LogWarning("[SynthCohost/Avatar] Reply speech timed out without audio; returning to idle.");
            }
            else
            {
                Debug.Log("[SynthCohost/Avatar] Reply speech finished.");
            }

            Complete();
        }

        private void SynthesizeWav(string sanitized, CancellationToken token)
        {
            string wavPath = null;
            try
            {
                var synthType = FindSpeechSynthesizerType();
                if (synthType == null)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "speech-dll-missing";
                    }

                    return;
                }

                wavPath = Path.Combine(
                    Path.GetTempPath(),
                    "synthcohost-reply-" + Guid.NewGuid().ToString("N") + ".wav");
                var synth = Activator.CreateInstance(synthType);
                try
                {
                    synthType.InvokeMember(
                        "Volume",
                        BindingFlags.SetProperty,
                        null,
                        synth,
                        new object[] { 100 });
                    synthType.InvokeMember(
                        "SetOutputToWaveFile",
                        BindingFlags.InvokeMethod,
                        null,
                        synth,
                        new object[] { wavPath });
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    synthType.InvokeMember(
                        "Speak",
                        BindingFlags.InvokeMethod,
                        null,
                        synth,
                        new object[] { sanitized });
                }
                finally
                {
                    TryDispose(synth);
                }

                if (token.IsCancellationRequested || !File.Exists(wavPath))
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "wav-missing";
                    }

                    return;
                }

                var bytes = File.ReadAllBytes(wavPath);
                lock (gate)
                {
                    pendingWav = bytes;
                    lastSafeStatus = "wav-ready";
                }
            }
            catch (Exception)
            {
                if (!token.IsCancellationRequested)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "synth-failed";
                    }
                }
            }
            finally
            {
                TryDelete(wavPath);
            }
        }

        private void Complete()
        {
            Action callback;
            AudioClip clip;
            lock (gate)
            {
                callback = onCompleted;
                onCompleted = null;
                clip = playingClip;
                playingClip = null;
                startedClip = false;
                deadlineUnscaled = -1f;
            }

            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }

            callback?.Invoke();
        }

        private static Type FindSpeechSynthesizerType()
        {
            var loaded = Type.GetType(
                "System.Speech.Synthesis.SpeechSynthesizer, System.Speech, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            if (loaded != null)
            {
                return loaded;
            }

            var roots = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\Framework64\v4.0.30319\System.Speech.dll"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\Framework\v4.0.30319\System.Speech.dll")
            };

            for (var i = 0; i < roots.Length; i++)
            {
                if (!File.Exists(roots[i]))
                {
                    continue;
                }

                try
                {
                    var asm = Assembly.LoadFrom(roots[i]);
                    var type = asm.GetType("System.Speech.Synthesis.SpeechSynthesizer");
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        private static void TryDispose(object synth)
        {
            if (synth is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
