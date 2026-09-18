using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Writes <c>ai.response</c> to a WAV outside the Editor process, then plays it
    /// on the avatar AudioSource. In-process System.Speech Speak hangs in this Editor.
    /// </summary>
    internal sealed class WindowsSapiReplySpeechPlayback : IReplySpeechPlayback
    {
        private readonly object gate = new object();
        private CancellationTokenSource cts;
        private DiagnosticsProcess synthProcess;
        private Action onCompleted;
        private byte[] pendingWav;
        private LipSyncTrack pendingLipSync;
        private LipSyncTrack activeLipSync;
        private AudioClip playingClip;
        private bool finished;
        private bool failed;
        private bool startedClip;
        private float synthDeadlineUnscaled = -1f;
        private float playDeadlineUnscaled = -1f;
        private float playStartedUnscaled = -1f;
        private string lastSafeStatus = "idle";
        private string lastSanitizedText = string.Empty;
        private string pendingLipSource = string.Empty;
        private string chosenVoiceName = string.Empty;

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
            lastSanitizedText = sanitized;
            synthDeadlineUnscaled = Time.unscaledTime + ReplySpeech.EstimateHoldSeconds(sanitized) + 22f;
            playDeadlineUnscaled = -1f;
            if (sanitized.Length == 0)
            {
                failed = true;
                lastSafeStatus = "empty-text";
                return;
            }

            AudioListener.pause = false;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            lastSafeStatus = "synthesizing";
            Debug.Log("[SynthCohost/Avatar] Reply speech synthesizing in a Windows TTS process. Text was not logged.");
            var thread = new Thread(() => SynthesizeWav(sanitized, token))
            {
                IsBackground = true,
                Name = "SynthCohostTtsWav"
            };
            thread.Start();
        }

        public void Cancel()
        {
            CancellationTokenSource local;
            DiagnosticsProcess process;
            AudioClip clip;
            lock (gate)
            {
                local = cts;
                cts = null;
                process = synthProcess;
                synthProcess = null;
                onCompleted = null;
                pendingWav = null;
                pendingLipSync = null;
                activeLipSync = null;
                clip = playingClip;
                playingClip = null;
                finished = false;
                failed = false;
                startedClip = false;
                synthDeadlineUnscaled = -1f;
                playDeadlineUnscaled = -1f;
                playStartedUnscaled = -1f;
                pendingLipSource = string.Empty;
                chosenVoiceName = string.Empty;
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

            TryKill(process);

            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }

        public void Tick(AudioSource output)
        {
            byte[] wav;
            LipSyncTrack lipTrack;
            string lipSource;
            lock (gate)
            {
                wav = pendingWav;
                pendingWav = null;
                lipTrack = pendingLipSync;
                pendingLipSync = null;
                lipSource = pendingLipSource;
                pendingLipSource = string.Empty;
            }

            if (wav != null)
            {
                if (output == null)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "no-audio-source";
                    }

                    Debug.LogWarning("[SynthCohost/Avatar] Reply WAV ready but the avatar has no AudioSource.");
                }
                else if (!WavAudioClipFactory.TryCreate(wav, "SynthCohostReply", out var clip))
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "wav-decode-failed";
                    }

                    Debug.LogWarning("[SynthCohost/Avatar] Reply WAV could not be decoded; ending talk hold.");
                }
                else
                {
                    if (playingClip != null)
                    {
                        UnityEngine.Object.Destroy(playingClip);
                    }

                    playingClip = clip;
                    activeLipSync = lipTrack;
                    if (activeLipSync == null || activeLipSync.Count == 0)
                    {
                        activeLipSync = LipSyncTrack.FromApproximateText(
                            lastSanitizedText,
                            Mathf.Max(0.4f, clip.length));
                        lipSource = "letter-approx";
                    }

                    output.enabled = true;
                    output.mute = false;
                    output.ignoreListenerPause = true;
                    output.spatialBlend = 0f;
                    output.loop = false;
                    output.Stop();
                    output.clip = clip;
                    output.Play();
                    startedClip = true;
                    playStartedUnscaled = Time.unscaledTime;
                    playDeadlineUnscaled = Time.unscaledTime + Mathf.Max(0.4f, clip.length) + 0.75f;
                    var voice = chosenVoiceName;
                    lastSafeStatus = "playing";
                    if (AudioListener.pause)
                    {
                        Debug.LogWarning(
                            "[SynthCohost/Avatar] Reply speech is playing on the avatar AudioSource, " +
                            "but the Audio Listener is paused (unmute Game view).");
                    }
                    else
                    {
                        var keyCount = activeLipSync != null ? activeLipSync.Count : 0;
                        var sourceLabel = string.IsNullOrEmpty(lipSource) ? "lip-sync" : lipSource;
                        var voiceLabel = string.IsNullOrEmpty(voice) ? "SAPI default" : voice;
                        Debug.Log(
                            "[SynthCohost/Avatar] Reply speech playing through avatar AudioSource " +
                            $"with {keyCount} {sourceLabel} keys; local voice={voiceLabel}.");
                    }
                }
            }

            var audioPlaying = startedClip && output != null && output.isPlaying;
            var elapsed = playStartedUnscaled > 0f ? Time.unscaledTime - playStartedUnscaled : 0f;
            var expectedSeconds = playingClip != null ? playingClip.length : 0f;
            var stillInExpectedWindow = startedClip &&
                                        expectedSeconds > 0f &&
                                        elapsed < expectedSeconds * 0.92f;
            var startGrace = startedClip && elapsed < 0.12f && !audioPlaying;
            var audioDone = startedClip && !audioPlaying && !startGrace && !stillInExpectedWindow;
            var synthTimedOut = !startedClip &&
                                synthDeadlineUnscaled > 0f &&
                                Time.unscaledTime >= synthDeadlineUnscaled;
            var playTimedOut = startedClip &&
                               playDeadlineUnscaled > 0f &&
                               Time.unscaledTime >= playDeadlineUnscaled;
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

            if ((audioPlaying || startGrace || stillInExpectedWindow) && !playTimedOut)
            {
                return;
            }

            if (!audioDone && !synthTimedOut && !playTimedOut && !synthFailed)
            {
                return;
            }

            if (synthFailed && !startedClip)
            {
                Debug.LogWarning(
                    "[SynthCohost/Avatar] Windows speech synthesis failed; ending talk hold " +
                    $"({LastSafeStatus}). Unmute Game view if you expected audio.");
            }
            else if (synthTimedOut && !startedClip)
            {
                Debug.LogWarning("[SynthCohost/Avatar] Reply speech timed out without audio; returning to idle.");
            }
            else
            {
                Debug.Log("[SynthCohost/Avatar] Reply speech finished.");
            }

            Complete();
        }

        public bool TrySampleLipSync(AudioSource output, out LipSyncPose pose)
        {
            pose = LipSyncPose.Rest;
            LipSyncTrack track;
            lock (gate)
            {
                track = activeLipSync;
            }

            if (track == null || track.Count == 0)
            {
                return false;
            }

            if (!startedClip || output == null || output.clip == null)
            {
                return false;
            }

            float time;
            if (output.isPlaying)
            {
                time = output.time;
            }
            else if (playStartedUnscaled > 0f)
            {
                time = Mathf.Min(output.clip.length, Mathf.Max(0f, Time.unscaledTime - playStartedUnscaled));
            }
            else
            {
                time = 0f;
            }

            pose = track.Sample(time);
            return true;
        }

        private void SynthesizeWav(string sanitized, CancellationToken token)
        {
            var id = Guid.NewGuid().ToString("N");
            var wavPath = Path.Combine(Path.GetTempPath(), "synthcohost-reply-" + id + ".wav");
            var visemePath = Path.Combine(Path.GetTempPath(), "synthcohost-reply-" + id + ".visemes.txt");
            try
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var timeoutMs = SynthTimeoutMs(sanitized);
                if (!TrySynthesizeWithPowerShell(sanitized, wavPath, visemePath, timeoutMs, token))
                {
                    lock (gate)
                    {
                        if (!failed)
                        {
                            failed = true;
                            lastSafeStatus = "synth-failed";
                        }
                    }

                    return;
                }

                if (token.IsCancellationRequested || !File.Exists(wavPath) || new FileInfo(wavPath).Length < 44)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "wav-missing";
                    }

                    return;
                }

                var bytes = File.ReadAllBytes(wavPath);
                LipSyncTrack track = null;
                var lipSource = "letter-approx";
                if (File.Exists(visemePath))
                {
                    if (LipSyncTrack.TryParseLines(File.ReadAllText(visemePath), out track) &&
                        track != null &&
                        track.Count > 0)
                    {
                        lipSource = "sapi-visemes";
                    }
                }

                if (track == null || track.Count == 0)
                {
                    track = LipSyncTrack.FromApproximateText(
                        sanitized,
                        EstimateWavDurationSeconds(bytes));
                    lipSource = "letter-approx";
                }

                var voiceName = string.Empty;
                var voiceSidecar = visemePath + ".voice.txt";
                if (File.Exists(voiceSidecar))
                {
                    voiceName = File.ReadAllText(voiceSidecar).Trim();
                    TryDelete(voiceSidecar);
                }

                lock (gate)
                {
                    pendingWav = bytes;
                    pendingLipSync = track;
                    pendingLipSource = lipSource;
                    chosenVoiceName = voiceName;
                    lastSafeStatus = string.IsNullOrEmpty(voiceName)
                        ? "wav-ready"
                        : "wav-ready:" + voiceName;
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
                TryDelete(visemePath);
                TryDelete(visemePath + ".voice.txt");
            }
        }

        private bool TrySynthesizeWithPowerShell(
            string sanitized,
            string wavPath,
            string visemePath,
            int timeoutMs,
            CancellationToken token)
        {
            try
            {
                var exe = FindPowerShell();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "powershell-missing";
                    }

                    return false;
                }

                var quotedText = ReplySpeech.ToPowerShellSingleQuoted(sanitized);
                var quotedPath = ReplySpeech.ToPowerShellSingleQuoted(wavPath);
                var quotedVisemes = ReplySpeech.ToPowerShellSingleQuoted(visemePath);
                var script =
                    "Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    "$female = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Gender -eq 'Female' } | Select-Object -First 1; " +
                    "if ($female -ne $null) { $s.SelectVoice($female.VoiceInfo.Name) } " +
                    "else { " +
                    "$zira = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Name -like '*Zira*' } | Select-Object -First 1; " +
                    "if ($zira -ne $null) { $s.SelectVoice($zira.VoiceInfo.Name) } }; " +
                    "$visemes = New-Object System.Collections.Generic.List[string]; " +
                    "$s.add_VisemeReached({ param($sender,$e) " +
                    "$sec = $e.AudioPosition.TotalSeconds.ToString('F4',[Globalization.CultureInfo]::InvariantCulture); " +
                    "$id = [int]$e.Viseme; $visemes.Add($sec + ',' + $id) }); " +
                    "$s.SetOutputToWaveFile(" + quotedPath + "); " +
                    "$s.Speak(" + quotedText + "); " +
                    "$chosen = $s.Voice.Name; " +
                    "$s.Dispose(); " +
                    "[System.IO.File]::WriteAllLines(" + quotedVisemes + ", $visemes); " +
                    "[System.IO.File]::WriteAllText(" + quotedVisemes + " + '.voice.txt', $chosen);";
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var start = new DiagnosticsProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetTempPath()
                };

                var process = DiagnosticsProcess.Start(start);
                if (process == null)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "powershell-start-failed";
                    }

                    return false;
                }

                lock (gate)
                {
                    synthProcess = process;
                }

                var exited = WaitForExitOrCancel(process, timeoutMs, token);
                lock (gate)
                {
                    if (ReferenceEquals(synthProcess, process))
                    {
                        synthProcess = null;
                    }
                }

                if (!exited || token.IsCancellationRequested)
                {
                    TryKill(process);
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "powershell-timeout";
                    }

                    return false;
                }

                if (process.ExitCode != 0)
                {
                    lock (gate)
                    {
                        failed = true;
                        lastSafeStatus = "powershell-exit";
                    }

                    return false;
                }

                return File.Exists(wavPath);
            }
            catch (Exception)
            {
                lock (gate)
                {
                    failed = true;
                    lastSafeStatus = "powershell-exception";
                }

                return false;
            }
        }

        private static float EstimateWavDurationSeconds(byte[] wav)
        {
            if (wav == null || wav.Length < 44)
            {
                return 1f;
            }

            // PCM WAV: sampleRate @ 24, byteRate @ 28, data size ≈ file-44
            var sampleRate = BitConverter.ToInt32(wav, 24);
            var byteRate = BitConverter.ToInt32(wav, 28);
            if (byteRate > 0)
            {
                return Mathf.Max(0.2f, (wav.Length - 44f) / byteRate);
            }

            if (sampleRate > 0)
            {
                return Mathf.Max(0.2f, (wav.Length - 44f) / (sampleRate * 2f));
            }

            return 1f;
        }

        private static int SynthTimeoutMs(string sanitized)
        {
            var seconds = ReplySpeech.EstimateHoldSeconds(sanitized) + 12f;
            return Mathf.Clamp(Mathf.RoundToInt(seconds * 1000f), 20000, 30000);
        }

        private static bool WaitForExitOrCancel(DiagnosticsProcess process, int timeoutMs, CancellationToken token)
        {
            const int sliceMs = 200;
            var waited = 0;
            while (waited < timeoutMs)
            {
                if (process.WaitForExit(sliceMs))
                {
                    return true;
                }

                if (token.IsCancellationRequested)
                {
                    return false;
                }

                waited += sliceMs;
            }

            return process.HasExited;
        }

        private static string FindPowerShell()
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var path = Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe");
            return path;
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
                activeLipSync = null;
                startedClip = false;
                synthDeadlineUnscaled = -1f;
                playDeadlineUnscaled = -1f;
                playStartedUnscaled = -1f;
            }

            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }

            callback?.Invoke();
        }

        private static void TryKill(DiagnosticsProcess process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (Exception)
            {
            }

            try
            {
                process.Dispose();
            }
            catch (Exception)
            {
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
