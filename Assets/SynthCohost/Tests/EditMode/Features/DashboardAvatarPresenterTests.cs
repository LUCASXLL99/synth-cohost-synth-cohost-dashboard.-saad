using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.AiResponse;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardAvatarPresenterTests
    {
        [Test]
        public async Task ApplyAsync_WithoutAnimatorController_ReturnsFalse()
        {
            var go = new GameObject("presenter-test");
            try
            {
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var ok = await presenter.ApplyAsync(AvatarBehavior.Idle, CancellationToken.None);
                Assert.That(ok, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task ApplyAsync_UnknownBehavior_ReturnsFalse()
        {
            var go = new GameObject("presenter-unknown");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var ok = await presenter.ApplyAsync((AvatarBehavior)999, CancellationToken.None);
                Assert.That(ok, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentAsync_EmptyText_DoesNotSetCaption()
        {
            var go = new GameObject("caption-test");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = " ",
                        Emotion = AiEmotion.Happy,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);
                Assert.That(presenter.CaptionText, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task CompositeSink_ForwardsToEveryTarget()
        {
            var first = new RecordingSink();
            var second = new RecordingSink();
            var composite = new CompositeAiResponseSink(first, second);
            var payload = new AiResponsePayload
            {
                Text = "hello",
                Emotion = AiEmotion.Neutral,
                Intent = AiIntent.Chat
            };

            await composite.PresentAsync(payload, CancellationToken.None);

            Assert.That(first.Last, Is.SameAs(payload));
            Assert.That(second.Last, Is.SameAs(payload));
        }

        [Test]
        public async Task PreviewLocalAsync_DoesNotRequireASession()
        {
            var go = new GameObject("preview-test");
            try
            {
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var ok = await presenter.PreviewLocalAsync(AvatarBehavior.Thinking, CancellationToken.None);
                Assert.That(ok, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentAsync_SetsCaptionForNonEmptyText()
        {
            var go = new GameObject("caption-present");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hello there",
                        Emotion = AiEmotion.Happy,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);
                Assert.That(presenter.CaptionText, Does.Contain("hello there"));
                Assert.That(presenter.CaptionText, Does.Contain("happy"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentAsync_ContractBehavior_AppliesListeningWithoutTreatingAsMissing()
        {
            var go = new GameObject("ai-response-behavior");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;

                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "Mm-hm.",
                        Emotion = AiEmotion.Neutral,
                        Intent = AiIntent.Chat,
                        Behavior = AvatarBehavior.Listening
                    },
                    CancellationToken.None);

                Assert.That(presenter.LastResponse.Behavior, Is.EqualTo(AvatarBehavior.Listening));
                Assert.That(speech.Plays, Is.EqualTo(0));
                Assert.That(presenter.WaitingForBackendSpeech, Is.True);
                Assert.That(presenter.CaptionText, Does.Contain("Mm-hm."));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentAsync_StartsReplyHoldWithoutRequiringASession()
        {
            var go = new GameObject("reply-hold");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;

                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hello there",
                        Emotion = AiEmotion.Happy,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                Assert.That(presenter.ReplyHoldActive, Is.True);
                Assert.That(presenter.WaitingForBackendSpeech, Is.True);
                Assert.That(speech.Plays, Is.EqualTo(0));

                presenter.FlushBackendSpeechWaitForTests();
                Assert.That(speech.Plays, Is.EqualTo(1));
                Assert.That(speech.LastText, Is.EqualTo("hello there"));

                speech.Complete();
                Assert.That(presenter.ReplyHoldActive, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task WireBehaviorOtherThanSpeaking_CancelsReplyHold()
        {
            var go = new GameObject("reply-cancel");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hold this",
                        Emotion = AiEmotion.Neutral,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                await presenter.ApplyAsync(AvatarBehavior.Thinking, CancellationToken.None);

                Assert.That(presenter.ReplyHoldActive, Is.False);
                Assert.That(presenter.WaitingForBackendSpeech, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PreviewLocalAsync_DoesNotCancelReplyHold()
        {
            var go = new GameObject("preview-keeps-hold");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;
                presenter.BehaviorApplied += _ => Assert.Fail("Local preview must not raise BehaviorApplied.");

                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "keep talking",
                        Emotion = AiEmotion.Happy,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                await presenter.PreviewLocalAsync(AvatarBehavior.Listening, CancellationToken.None);

                Assert.That(presenter.ReplyHoldActive, Is.True);
                Assert.That(presenter.WaitingForBackendSpeech, Is.True);
                Assert.That(speech.Plays, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentSpeechAudio_StopsWaitingForLocalTts()
        {
            var go = new GameObject("backend-speech");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hello there",
                        Emotion = AiEmotion.Neutral,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                await presenter.HandleAudioAsync(
                    new SpeechAudioPayload
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
                    },
                    CancellationToken.None);

                Assert.That(presenter.WaitingForBackendSpeech, Is.False);
                Assert.That(speech.Plays, Is.EqualTo(0));
                Assert.That(presenter.ReplyHoldActive, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task PresentSpeechAudio_AfterLocalFallback_CancelsLocalPlayback()
        {
            var go = new GameObject("late-backend-speech");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                var speech = new RecordingSpeechPlayback();
                presenter.SpeechPlayback = speech;
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hello there",
                        Emotion = AiEmotion.Neutral,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                presenter.FlushBackendSpeechWaitForTests();
                Assert.That(speech.Plays, Is.EqualTo(1));

                await presenter.HandleAudioAsync(
                    new SpeechAudioPayload
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
                        }
                    },
                    CancellationToken.None);

                Assert.That(speech.Cancelled, Is.True);
                Assert.That(presenter.WaitingForBackendSpeech, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public async Task SystemError_AuthFailed_ResetsIdleAndClearsCaption()
        {
            var go = new GameObject("auth-failed-visual");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                await presenter.PresentAsync(
                    new AiResponsePayload
                    {
                        Text = "hello there",
                        Emotion = AiEmotion.Happy,
                        Intent = AiIntent.Chat
                    },
                    CancellationToken.None);

                Assert.That(presenter.WaitingForBackendSpeech, Is.True);
                await presenter.PresentAsync(
                    new SystemErrorPayload
                    {
                        Code = ProtocolSystemErrorCodes.AuthFailed,
                        Message = "nope"
                    },
                    CancellationToken.None);

                Assert.That(presenter.WaitingForBackendSpeech, Is.False);
                Assert.That(presenter.ReplyHoldActive, Is.False);
                Assert.That(presenter.CaptionText, Is.Empty);
                Assert.That(presenter.LastBehavior, Is.EqualTo(AvatarBehavior.Idle));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ConfigApplier_IgnoresUnknownFieldsAndAppliesKnownToggles()
        {
            var go = new GameObject("config-test");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                presenter.LockEyes = true;
                presenter.DrawCaptionOverlay = true;
                presenter.ApplyUnknownSafe(
                    "{\"captions\":\"off\",\"lockEyes\":\"false\",\"tts\":\"off\",\"futureField\":\"ignored\"}");
                Assert.That(presenter.DrawCaptionOverlay, Is.False);
                Assert.That(presenter.LockEyes, Is.False);
                Assert.That(presenter.PlayReplySpeech, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetToLivingIdle_ClearsCaptionWithoutThrowing()
        {
            var go = new GameObject("reset-test");
            try
            {
                go.AddComponent<Animator>();
                var presenter = go.AddComponent<DashboardAvatarPresenter>();
                presenter.ResetToLivingIdle();
                Assert.That(presenter.CaptionText, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class RecordingSpeechPlayback : IReplySpeechPlayback
        {
            private System.Action onCompleted;

            public string LastText { get; private set; }

            public int Plays { get; private set; }

            public bool Cancelled { get; private set; }

            public bool IsActive => onCompleted != null;

            public void Play(string text, System.Action completed)
            {
                LastText = text;
                Plays++;
                Cancelled = false;
                onCompleted = completed;
            }

            public void Cancel()
            {
                Cancelled = true;
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

            public void Complete()
            {
                var callback = onCompleted;
                onCompleted = null;
                callback?.Invoke();
            }
        }

        private sealed class RecordingSink : IAiResponseSink
        {
            public AiResponsePayload Last { get; private set; }

            public Task PresentAsync(AiResponsePayload response, CancellationToken cancellationToken)
            {
                Last = response;
                return Task.CompletedTask;
            }
        }
    }
}
