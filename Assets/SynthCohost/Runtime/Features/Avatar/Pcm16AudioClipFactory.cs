using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    internal static class Pcm16AudioClipFactory
    {
        public static bool TryCreate(byte[] bytes, int sampleRate, int channels, out AudioClip clip)
        {
            clip = null;
            if (bytes == null || bytes.Length < 2)
            {
                return false;
            }

            if (WavAudioClipFactory.TryCreate(bytes, "SynthCohostSpeech", out clip))
            {
                return true;
            }

            var rate = sampleRate > 0 ? sampleRate : 24000;
            var ch = channels > 0 ? channels : 1;
            if ((bytes.Length % 2) != 0)
            {
                return false;
            }

            var sampleCount = bytes.Length / 2;
            var frames = sampleCount / ch;
            if (frames <= 0)
            {
                return false;
            }

            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = System.BitConverter.ToInt16(bytes, i * 2) / 32768f;
            }

            clip = AudioClip.Create("SynthCohostSpeechPcm", frames, ch, rate, false);
            clip.SetData(samples, 0);
            return true;
        }
    }
}
