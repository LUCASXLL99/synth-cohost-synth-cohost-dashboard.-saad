using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Timed Microsoft SAPI viseme ids (0–21) aligned to WAV playback seconds.
    /// </summary>
    internal sealed class LipSyncTrack
    {
        private readonly List<KeyValuePair<float, int>> events =
            new List<KeyValuePair<float, int>>(64);

        /// <summary>
        /// SAPI visemes are held until the next id. Blend only in this tail window
        /// so the mouth is not constantly halfway between two shapes.
        /// </summary>
        public const float CrossfadeSeconds = 0.07f;

        public int Count => events.Count;

        public void Clear()
        {
            events.Clear();
        }

        public void Add(float timeSeconds, int visemeId)
        {
            events.Add(new KeyValuePair<float, int>(
                Mathf.Max(0f, timeSeconds),
                Mathf.Clamp(visemeId, 0, 21)));
        }

        public void Sort()
        {
            events.Sort((a, b) => a.Key.CompareTo(b.Key));
        }

        public static bool TryParseLines(string text, out LipSyncTrack track)
        {
            track = new LipSyncTrack();
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var comma = line.IndexOf(',');
                if (comma <= 0 || comma >= line.Length - 1)
                {
                    continue;
                }

                if (!float.TryParse(
                        line.Substring(0, comma),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var time))
                {
                    continue;
                }

                if (!int.TryParse(
                        line.Substring(comma + 1).Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var id))
                {
                    continue;
                }

                track.Add(time, id);
            }

            if (track.Count == 0)
            {
                return false;
            }

            track.Sort();
            return true;
        }

        /// <summary>
        /// Builds a coarse letter→viseme schedule when SAPI visemes are unavailable.
        /// </summary>
        public static LipSyncTrack FromApproximateText(string text, float durationSeconds)
        {
            var track = new LipSyncTrack();
            track.Add(0f, 0);
            if (string.IsNullOrEmpty(text) || durationSeconds < 0.05f)
            {
                return track;
            }

            var letters = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]))
                {
                    letters++;
                }
            }

            if (letters == 0)
            {
                return track;
            }

            var step = durationSeconds / letters;
            var t = 0f;
            for (var i = 0; i < text.Length; i++)
            {
                var c = char.ToLowerInvariant(text[i]);
                if (!char.IsLetter(c))
                {
                    continue;
                }

                track.Add(t, ApproximateViseme(c));
                t += step;
            }

            track.Add(durationSeconds, 0);
            return track;
        }

        public LipSyncPose Sample(float timeSeconds)
        {
            if (events.Count == 0)
            {
                return LipSyncPose.Rest;
            }

            if (timeSeconds <= events[0].Key)
            {
                return LipSyncVisemeMap.ToPose(events[0].Value);
            }

            if (timeSeconds >= events[events.Count - 1].Key)
            {
                return LipSyncVisemeMap.ToPose(events[events.Count - 1].Value);
            }

            for (var i = 0; i < events.Count - 1; i++)
            {
                var a = events[i];
                var b = events[i + 1];
                if (timeSeconds < a.Key || timeSeconds > b.Key)
                {
                    continue;
                }

                var span = b.Key - a.Key;
                if (span <= 0.0001f)
                {
                    return LipSyncVisemeMap.ToPose(b.Value);
                }

                var fade = Mathf.Min(CrossfadeSeconds, span * 0.45f);
                var blendStart = b.Key - fade;
                if (timeSeconds <= blendStart)
                {
                    return LipSyncVisemeMap.ToPose(a.Value);
                }

                var u = fade <= 0.0001f
                    ? 1f
                    : Mathf.SmoothStep(0f, 1f, (timeSeconds - blendStart) / fade);
                return LipSyncVisemeMap.Lerp(
                    LipSyncVisemeMap.ToPose(a.Value),
                    LipSyncVisemeMap.ToPose(b.Value),
                    u);
            }

            return LipSyncVisemeMap.ToPose(events[events.Count - 1].Value);
        }

        internal static int ApproximateViseme(char letter)
        {
            switch (letter)
            {
                case 'a':
                    return 2; // aa
                case 'e':
                    return 4; // eh/ey
                case 'i':
                case 'y':
                    return 6; // iy
                case 'o':
                    return 8; // ow
                case 'u':
                case 'w':
                    return 7; // uw
                case 'f':
                case 'v':
                    return 18;
                case 't':
                case 'd':
                case 'n':
                    return 19;
                case 's':
                case 'z':
                    return 15;
                case 'h':
                    return 12;
                case 'r':
                    return 13;
                case 'l':
                    return 14;
                case 'p':
                case 'b':
                case 'm':
                    return 21;
                case 'c':
                case 'k':
                case 'g':
                case 'q':
                case 'x':
                    return 20;
                case 'j':
                    return 16;
                default:
                    return 1; // schwa-ish
            }
        }
    }
}
