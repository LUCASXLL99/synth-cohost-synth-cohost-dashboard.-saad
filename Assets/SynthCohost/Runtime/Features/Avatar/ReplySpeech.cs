using System.Text;
using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Shared sanitizing and fallback timing for local reply speech.
    /// Does not log the source text.
    /// </summary>
    internal static class ReplySpeech
    {
        internal const int MaximumSpeakCharacters = 800;
        internal const float MinimumHoldSeconds = 1.2f;
        internal const float MaximumHoldSeconds = 20f;
        internal const float CharactersPerSecond = 12.5f;

        internal static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            if (trimmed.Length > MaximumSpeakCharacters)
            {
                trimmed = trimmed.Substring(0, MaximumSpeakCharacters);
            }

            var builder = new StringBuilder(trimmed.Length);
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (c == '<' || c == '>' || char.IsControl(c))
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        internal static float EstimateHoldSeconds(string text)
        {
            var sanitized = Sanitize(text);
            if (sanitized.Length == 0)
            {
                return MinimumHoldSeconds;
            }

            var seconds = 0.45f + (sanitized.Length / CharactersPerSecond);
            return Mathf.Clamp(seconds, MinimumHoldSeconds, MaximumHoldSeconds);
        }
    }
}
