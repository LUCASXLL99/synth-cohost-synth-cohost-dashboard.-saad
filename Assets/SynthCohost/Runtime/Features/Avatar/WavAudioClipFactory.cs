using System;
using System.Text;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    internal static class WavAudioClipFactory
    {
        internal static bool TryCreate(byte[] wavBytes, string clipName, out AudioClip clip)
        {
            clip = null;
            if (wavBytes == null || wavBytes.Length < 44)
            {
                return false;
            }

            if (!HasFour(wavBytes, 0, "RIFF") || !HasFour(wavBytes, 8, "WAVE"))
            {
                return false;
            }

            var offset = 12;
            var channels = 1;
            var sampleRate = 22050;
            var bitsPerSample = 16;
            byte[] pcm = null;
            while (offset + 8 <= wavBytes.Length)
            {
                var chunkId = Encoding.ASCII.GetString(wavBytes, offset, 4);
                var chunkSize = BitConverter.ToInt32(wavBytes, offset + 4);
                offset += 8;
                if (chunkSize < 0 || offset + chunkSize > wavBytes.Length)
                {
                    break;
                }

                if (chunkId == "fmt ")
                {
                    var format = BitConverter.ToInt16(wavBytes, offset);
                    channels = BitConverter.ToInt16(wavBytes, offset + 2);
                    sampleRate = BitConverter.ToInt32(wavBytes, offset + 4);
                    bitsPerSample = BitConverter.ToInt16(wavBytes, offset + 14);
                    if (format != 1 || channels < 1 || sampleRate < 8000 || bitsPerSample != 16)
                    {
                        return false;
                    }
                }
                else if (chunkId == "data")
                {
                    pcm = new byte[chunkSize];
                    Buffer.BlockCopy(wavBytes, offset, pcm, 0, chunkSize);
                    break;
                }

                offset += chunkSize;
            }

            if (pcm == null || pcm.Length < 2)
            {
                return false;
            }

            var sampleCount = pcm.Length / 2;
            var frames = sampleCount / channels;
            if (frames <= 0)
            {
                return false;
            }

            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            }

            clip = AudioClip.Create(
                string.IsNullOrEmpty(clipName) ? "ReplySpeech" : clipName,
                frames,
                channels,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return true;
        }

        private static bool HasFour(byte[] data, int offset, string expected)
        {
            return Encoding.ASCII.GetString(data, offset, 4) == expected;
        }
    }
}
