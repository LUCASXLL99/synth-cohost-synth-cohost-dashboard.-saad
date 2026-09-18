using System;
using System.Collections.Generic;
using System.IO;
using SynthCohost.Protocol;
using UnityEngine;
#if !UNITY_WEBGL
using UnityEngine.Networking;
#endif

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Plays backend <c>speech.audio</c> packets in <c>seq</c> order and samples mouth openness.
    /// </summary>
    internal sealed class BackendSpeechPlayback
    {
        private readonly List<SpeechAudioPayload> pending = new List<SpeechAudioPayload>(8);
        private SpeechAudioPayload current;
        private AudioClip playingClip;
        private MouthOpenTrack mouthTrack;
        private bool startedClip;
        private bool sawFinalPacket;
        private int? nextSeq;
        private float playStartedUnscaled = -1f;
        private float playDeadlineUnscaled = -1f;
        private string lastSafeStatus = "idle";
        private float mouthOpenMaxWeight = MouthOpenTrack.MaxMouthOpenWeight;
#if !UNITY_WEBGL
        private UnityWebRequest mpegRequest;
        private string mpegTempPath;
#endif

        public bool IsActive =>
            pending.Count > 0 ||
            current != null ||
            startedClip ||
            IsDecodingMpeg;

        public bool SawFinalPacket => sawFinalPacket;

        public string LastSafeStatus => lastSafeStatus;

        public float MouthOpenMaxWeight
        {
            get => mouthOpenMaxWeight;
            set => mouthOpenMaxWeight = Mathf.Max(0f, value);
        }

        private bool IsDecodingMpeg =>
#if UNITY_WEBGL
            false;
#else
            mpegRequest != null;
#endif

        public void Enqueue(SpeechAudioPayload payload)
        {
            if (payload == null)
            {
                return;
            }

            pending.Add(payload);
            pending.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            if (payload.FinalPacket)
            {
                sawFinalPacket = true;
            }

            lastSafeStatus = "queued";
        }

        public void Cancel()
        {
            AbortMpeg();
            pending.Clear();
            current = null;
            mouthTrack = null;
            startedClip = false;
            sawFinalPacket = false;
            nextSeq = null;
            playStartedUnscaled = -1f;
            playDeadlineUnscaled = -1f;
            lastSafeStatus = "idle";
            if (playingClip != null)
            {
                UnityEngine.Object.Destroy(playingClip);
                playingClip = null;
            }
        }

        public void Tick(AudioSource output)
        {
            PollMpeg(output);

            if (current == null && pending.Count > 0)
            {
                TryStartNext(output);
            }

            if (current == null)
            {
                return;
            }

            if (!startedClip)
            {
                return;
            }

            var elapsed = playStartedUnscaled > 0f ? Time.unscaledTime - playStartedUnscaled : 0f;
            var expected = playingClip != null ? playingClip.length : Mathf.Max(0.2f, current.DurationMs / 1000f);
            var playing = output != null && output.isPlaying;
            var startGrace = elapsed < 0.12f && !playing;
            var inWindow = expected > 0f && elapsed < expected * 0.92f;
            var timedOut = playDeadlineUnscaled > 0f && Time.unscaledTime >= playDeadlineUnscaled;
            if ((playing || startGrace || inWindow) && !timedOut)
            {
                return;
            }

            FinishCurrent(output);
        }

        public bool TrySampleLipSync(AudioSource output, out LipSyncPose pose)
        {
            pose = LipSyncPose.Rest;
            if (mouthTrack == null || mouthTrack.Count == 0)
            {
                return false;
            }

            if (current == null && !startedClip)
            {
                return false;
            }

            float time;
            if (output != null && output.isPlaying)
            {
                time = output.time;
            }
            else if (playStartedUnscaled > 0f)
            {
                var length = playingClip != null
                    ? playingClip.length
                    : Mathf.Max(0.2f, current != null ? current.DurationMs / 1000f : 0.2f);
                time = Mathf.Min(length, Mathf.Max(0f, Time.unscaledTime - playStartedUnscaled));
            }
            else
            {
                return false;
            }

            pose = MouthOpenTrack.ToPose(mouthTrack.Sample(time), mouthOpenMaxWeight);
            return true;
        }

        private void TryStartNext(AudioSource output)
        {
            if (pending.Count == 0)
            {
                return;
            }

            if (!nextSeq.HasValue)
            {
                nextSeq = pending[0].Seq;
            }

            DropStalePackets();
            var index = IndexOfSeq(nextSeq.Value);
            if (index < 0)
            {
                lastSafeStatus = "await-seq";
                return;
            }

            current = pending[index];
            pending.RemoveAt(index);
            mouthTrack = MouthOpenTrack.FromFrames(current.Frames);
            startedClip = false;
            if (!current.TryGetAudioBytes(out var bytes))
            {
                lastSafeStatus = "missing-audio";
                Debug.LogWarning("[SynthCohost/Avatar] speech.audio had no usable audio bytes.");
                FinishCurrent(output);
                return;
            }

            var contentType = current.Audio != null ? current.Audio.ContentType : string.Empty;
            if (IsMpeg(contentType, bytes))
            {
                if (Application.isPlaying && output != null)
                {
                    BeginMpegDecode(bytes, output);
                    lastSafeStatus = "decoding-mp3";
                    Debug.Log(
                        $"[SynthCohost/Avatar] speech.audio seq={current.Seq} mp3 bytes={bytes.Length}; " +
                        $"frames={mouthTrack.Count}; final={current.FinalPacket}.");
                    return;
                }

                lastSafeStatus = "mp3-editmode";
                BeginMouthOnlyHold();
                Debug.LogWarning(
                    "[SynthCohost/Avatar] speech.audio MP3 cannot decode in Edit Mode; mouth frames only.");
                return;
            }

            var sampleRate = current.SampleRate > 0 ? current.SampleRate : 24000;
            if (!Pcm16AudioClipFactory.TryCreate(bytes, sampleRate, 1, out var clip))
            {
                lastSafeStatus = "pcm-decode-failed";
                Debug.LogWarning("[SynthCohost/Avatar] speech.audio PCM/WAV could not be decoded; mouth frames only.");
                BeginMouthOnlyHold();
                return;
            }

            PlayClip(output, clip, bytes.Length, "pcm");
        }

        private void DropStalePackets()
        {
            if (!nextSeq.HasValue)
            {
                return;
            }

            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Seq < nextSeq.Value)
                {
                    pending.RemoveAt(i);
                }
            }
        }

        private int IndexOfSeq(int seq)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].Seq == seq)
                {
                    return i;
                }
            }

            return -1;
        }

        private void PlayClip(AudioSource output, AudioClip clip, int byteCount, string format)
        {
            if (playingClip != null)
            {
                UnityEngine.Object.Destroy(playingClip);
            }

            playingClip = clip;
            if (output != null)
            {
                output.enabled = true;
                output.mute = false;
                output.ignoreListenerPause = true;
                output.spatialBlend = 0f;
                output.loop = false;
                output.Stop();
                output.clip = clip;
                output.Play();
            }

            startedClip = true;
            playStartedUnscaled = Time.unscaledTime;
            var seconds = clip != null ? clip.length : Mathf.Max(0.2f, current.DurationMs / 1000f);
            playDeadlineUnscaled = Time.unscaledTime + seconds + 0.75f;
            lastSafeStatus = "playing";
            Debug.Log(
                $"[SynthCohost/Avatar] speech.audio playing seq={current.Seq} format={format} bytes={byteCount}; " +
                $"frames={mouthTrack.Count}; final={current.FinalPacket}.");
        }

        private void BeginMouthOnlyHold()
        {
            startedClip = true;
            playStartedUnscaled = Time.unscaledTime;
            var seconds = Mathf.Max(0.25f, current.DurationMs / 1000f);
            playDeadlineUnscaled = Time.unscaledTime + seconds + 0.4f;
        }

        private void FinishCurrent(AudioSource output)
        {
            var finishedSeq = current != null ? (int?)current.Seq : null;
            AbortMpeg();
            if (output != null)
            {
                output.Stop();
            }

            if (playingClip != null)
            {
                UnityEngine.Object.Destroy(playingClip);
                playingClip = null;
            }

            current = null;
            mouthTrack = null;
            startedClip = false;
            playStartedUnscaled = -1f;
            playDeadlineUnscaled = -1f;
            if (finishedSeq.HasValue)
            {
                nextSeq = finishedSeq.Value + 1;
            }

            lastSafeStatus = pending.Count > 0 ? "queued" : "idle";
        }

        private static bool IsMpeg(string contentType, byte[] bytes)
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                var lower = contentType.ToLowerInvariant();
                if (lower.IndexOf("mpeg", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("mp3", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            if (bytes == null || bytes.Length < 3)
            {
                return false;
            }

            return bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3' ||
                   bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;
        }

        private void BeginMpegDecode(byte[] bytes, AudioSource output)
        {
#if UNITY_WEBGL
            BeginMouthOnlyHold();
#else
            AbortMpeg();
            mpegTempPath = Path.Combine(Path.GetTempPath(), "synthcohost-speech-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllBytes(mpegTempPath, bytes);
            mpegRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(mpegTempPath).AbsoluteUri, AudioType.MPEG);
            mpegRequest.SendWebRequest();
#endif
        }

        private void PollMpeg(AudioSource output)
        {
#if UNITY_WEBGL
#else
            if (mpegRequest == null || !mpegRequest.isDone)
            {
                return;
            }

            var request = mpegRequest;
            mpegRequest = null;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[SynthCohost/Avatar] speech.audio MP3 decode failed; mouth frames only.");
                request.Dispose();
                CleanupMpegTemp();
                BeginMouthOnlyHold();
                return;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            request.Dispose();
            CleanupMpegTemp();
            if (clip == null)
            {
                BeginMouthOnlyHold();
                return;
            }

            PlayClip(output, clip, 0, "mp3");
#endif
        }

        private void AbortMpeg()
        {
#if !UNITY_WEBGL
            if (mpegRequest != null)
            {
                mpegRequest.Abort();
                mpegRequest.Dispose();
                mpegRequest = null;
            }

            CleanupMpegTemp();
#endif
        }

        private void CleanupMpegTemp()
        {
#if !UNITY_WEBGL
            if (string.IsNullOrEmpty(mpegTempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(mpegTempPath))
                {
                    File.Delete(mpegTempPath);
                }
            }
            catch (IOException)
            {
            }

            mpegTempPath = null;
#endif
        }
    }
}
