using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.Avatar;
using UnityEngine;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class BackendSpeechPlaybackTests
    {
        [Test]
        public void FirstPacketDefinesSeq_DoesNotWaitForZero()
        {
            var playback = new BackendSpeechPlayback();
            playback.Enqueue(PcmPacket(seq: 1, finalPacket: true));
            playback.Tick(null);

            Assert.That(playback.IsActive, Is.True);
            Assert.That(playback.SawFinalPacket, Is.True);
            Assert.That(playback.LastSafeStatus, Is.EqualTo("playing").Or.EqualTo("pcm-decode-failed"));
        }

        [Test]
        public void PacketsPlayInSeqOrder()
        {
            var playback = new BackendSpeechPlayback();
            playback.Enqueue(PcmPacket(seq: 1, finalPacket: true));
            playback.Enqueue(PcmPacket(seq: 0, finalPacket: false));
            playback.Tick(null);

            Assert.That(playback.LastSafeStatus, Is.EqualTo("playing").Or.EqualTo("pcm-decode-failed"));
            playback.Tick(null);
            Assert.That(playback.SawFinalPacket, Is.True);
        }

        private static SpeechAudioPayload PcmPacket(int seq, bool finalPacket)
        {
            return new SpeechAudioPayload
            {
                Seq = seq,
                FinalPacket = finalPacket,
                DurationMs = 200,
                SampleRate = 24000,
                Audio = new SpeechInlineAudio
                {
                    Kind = "inline",
                    Data = "AAAAAAAAAAAAAAAA",
                    ContentType = "audio/pcm"
                },
                Frames = new[]
                {
                    new SpeechMouthFrame { TimeMs = 0f, Openness = 0.2f }
                }
            };
        }
    }
}
