using System.Collections;
using System.Threading;
using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Features.Avatar;
using SynthCohost.Runtime.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.PlayMode
{
    public sealed class DashboardSpeechPlayModeTests
    {
        [SetUp]
        public void DisableLiveTokenRefreshDuringSmokeTests()
        {
            SynthCohostLiveTestPanel.AutoRefreshSavedTokenOnPlay = false;
        }

        [TearDown]
        public void RestoreLiveTokenRefresh()
        {
            SynthCohostLiveTestPanel.AutoRefreshSavedTokenOnPlay = true;
        }

        [UnityTest]
        public IEnumerator SpeechAudio_EndsWaitWithoutStartingLocalTts()
        {
            yield return LoadLiveTestScene();
            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            var speech = new RecordingSpeechPlayback();
            presenter.SpeechPlayback = speech;

            var present = presenter.PresentAsync(
                new AiResponsePayload
                {
                    Text = "hello there",
                    Emotion = AiEmotion.Neutral,
                    Intent = AiIntent.Chat
                },
                CancellationToken.None);
            while (!present.IsCompleted)
            {
                yield return null;
            }

            Assert.That(presenter.WaitingForBackendSpeech, Is.True);

            var audio = presenter.HandleAudioAsync(PcmSpeech(), CancellationToken.None);
            while (!audio.IsCompleted)
            {
                yield return null;
            }

            Assert.That(presenter.WaitingForBackendSpeech, Is.False);
            Assert.That(speech.Plays, Is.EqualTo(0));
            Assert.That(presenter.ReplyHoldActive, Is.True);
        }

        [UnityTest]
        public IEnumerator SpeechFailed_StartsLocalFallback()
        {
            yield return LoadLiveTestScene();
            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            var speech = new RecordingSpeechPlayback();
            presenter.SpeechPlayback = speech;

            var present = presenter.PresentAsync(
                new AiResponsePayload
                {
                    Text = "hello there",
                    Emotion = AiEmotion.Neutral,
                    Intent = AiIntent.Chat
                },
                CancellationToken.None);
            while (!present.IsCompleted)
            {
                yield return null;
            }

            var failed = presenter.HandleFailedAsync(
                new SpeechFailedPayload { Code = "TTS_FAILED" },
                CancellationToken.None);
            while (!failed.IsCompleted)
            {
                yield return null;
            }

            Assert.That(presenter.WaitingForBackendSpeech, Is.False);
            Assert.That(speech.Plays, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ResetToLivingIdle_ClearsMidSpeechHold()
        {
            yield return LoadLiveTestScene();
            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            var speech = new RecordingSpeechPlayback();
            presenter.SpeechPlayback = speech;

            var present = presenter.PresentAsync(
                new AiResponsePayload
                {
                    Text = "hello there",
                    Emotion = AiEmotion.Neutral,
                    Intent = AiIntent.Chat
                },
                CancellationToken.None);
            while (!present.IsCompleted)
            {
                yield return null;
            }

            var audio = presenter.HandleAudioAsync(PcmSpeech(), CancellationToken.None);
            while (!audio.IsCompleted)
            {
                yield return null;
            }

            presenter.ResetToLivingIdle();
            Assert.That(presenter.WaitingForBackendSpeech, Is.False);
            Assert.That(presenter.ReplyHoldActive, Is.False);
            Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Idle));
        }

        [UnityTest]
        public IEnumerator FinalTurnGate_SecondBeginRejected_LeavesPresenterPose()
        {
            yield return LoadLiveTestScene();
            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            var thinking = presenter.PreviewLocalAsync(AvatarBehavior.Thinking, CancellationToken.None);
            while (!thinking.IsCompleted)
            {
                yield return null;
            }

            Assert.That(thinking.Result, Is.True);
            Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Thinking));

            var gate = new FinalTurnGate();
            Assert.That(gate.TryBegin(out var first), Is.True);
            Assert.That(first, Is.Not.Zero);
            Assert.That(gate.TryBegin(out var second), Is.False);
            Assert.That(second, Is.Zero);
            Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Thinking));
        }

        [UnityTest]
        public IEnumerator LiveTestClient_UsesDashboardPresenterNotRecorder()
        {
            yield return LoadLiveTestScene();
            var client = Object.FindFirstObjectByType<SynthCohostClientBehaviour>();
            var presenter = Object.FindFirstObjectByType<DashboardAvatarPresenter>();
            Assert.That(client.AvatarController, Is.SameAs(presenter));
        }

        private static SpeechAudioPayload PcmSpeech()
        {
            return new SpeechAudioPayload
            {
                Seq = 0,
                FinalPacket = true,
                DurationMs = 400,
                SampleRate = 24000,
                Audio = new SpeechInlineAudio
                {
                    Kind = "inline",
                    Data = "AAAAAAAAAAAAAAAA",
                    ContentType = "audio/pcm"
                },
                Frames = new[]
                {
                    new SpeechMouthFrame { TimeMs = 0f, Openness = 0.2f },
                    new SpeechMouthFrame { TimeMs = 80f, Openness = 0.9f }
                }
            };
        }

        private static IEnumerator LoadLiveTestScene()
        {
            var load = SceneManager.LoadSceneAsync("SynthCohostLiveTest", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        private sealed class RecordingSpeechPlayback : IReplySpeechPlayback
        {
            private System.Action onCompleted;

            public int Plays { get; private set; }

            public bool IsActive => onCompleted != null;

            public void Play(string text, System.Action completed)
            {
                Plays++;
                onCompleted = completed;
            }

            public void Cancel()
            {
                onCompleted = null;
            }

            public void Tick(AudioSource output)
            {
            }

            public bool TrySampleLipSync(AudioSource output, out LipSyncPose pose)
            {
                pose = default;
                return false;
            }
        }
    }
}
