using System.Collections.Generic;
using SynthCohost.Protocol;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Backend mouth openness (0–1) keyed by milliseconds from utterance start.
    /// </summary>
    internal sealed class MouthOpenTrack
    {
        public const float MaxMouthOpenWeight = 40f;

        private readonly List<KeyValuePair<float, float>> keys =
            new List<KeyValuePair<float, float>>(64);

        public int Count => keys.Count;

        public static MouthOpenTrack FromFrames(SpeechMouthFrame[] frames)
        {
            var track = new MouthOpenTrack();
            if (frames == null || frames.Length == 0)
            {
                return track;
            }

            for (var i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                if (frame == null)
                {
                    continue;
                }

                track.keys.Add(new KeyValuePair<float, float>(
                    Mathf.Max(0f, frame.TimeMs / 1000f),
                    Mathf.Clamp01(frame.Openness)));
            }

            track.keys.Sort((a, b) => a.Key.CompareTo(b.Key));
            return track;
        }

        public float Sample(float timeSeconds)
        {
            if (keys.Count == 0)
            {
                return 0f;
            }

            if (timeSeconds <= keys[0].Key)
            {
                return keys[0].Value;
            }

            if (timeSeconds >= keys[keys.Count - 1].Key)
            {
                return keys[keys.Count - 1].Value;
            }

            for (var i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (timeSeconds < a.Key || timeSeconds > b.Key)
                {
                    continue;
                }

                var span = b.Key - a.Key;
                if (span <= 0.0001f)
                {
                    return b.Value;
                }

                return Mathf.Lerp(a.Value, b.Value, (timeSeconds - a.Key) / span);
            }

            return keys[keys.Count - 1].Value;
        }

        public static LipSyncPose ToPose(float openness)
        {
            return ToPose(openness, MaxMouthOpenWeight);
        }

        public static LipSyncPose ToPose(float openness, float maxWeight)
        {
            var o = Mathf.Clamp01(openness);
            return new LipSyncPose
            {
                Jaw = o * Mathf.Max(0f, maxWeight)
            };
        }
    }
}
