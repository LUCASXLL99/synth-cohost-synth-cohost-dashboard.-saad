using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SynthCohost.Protocol
{
    public static class ProtocolValidation
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly Regex UtcRfc3339Pattern = new Regex(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$",
            RegexOptions.CultureInvariant);

        public static ProtocolValidationResult ValidateSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return ProtocolValidationResult.Failure(
                    ProtocolErrorCode.InvalidSessionId,
                    "session_id must contain between 1 and 128 UTF-8 bytes.");
            }

            if (!TryGetUtf8ByteCount(sessionId, out var byteCount) ||
                byteCount > ProtocolConstants.MaxSessionIdUtf8Bytes)
            {
                return ProtocolValidationResult.Failure(
                    ProtocolErrorCode.InvalidSessionId,
                    "session_id must contain valid UTF-16 and no more than 128 UTF-8 bytes.");
            }

            return ProtocolValidationResult.Success;
        }

        public static ProtocolValidationResult ValidateSttText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return ProtocolValidationResult.Failure(
                    ProtocolErrorCode.InvalidSttText,
                    "STT text must contain between 1 and 4000 UTF-8 bytes.");
            }

            if (!TryGetUtf8ByteCount(text, out var byteCount) ||
                byteCount > ProtocolConstants.MaxSttTextUtf8Bytes)
            {
                return ProtocolValidationResult.Failure(
                    ProtocolErrorCode.InvalidSttText,
                    "STT text must contain valid UTF-16 and no more than 4000 UTF-8 bytes.");
            }

            return ProtocolValidationResult.Success;
        }

        public static ProtocolValidationResult ValidateJsonSize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return ProtocolValidationResult.Failure(ProtocolErrorCode.EmptyInput, "JSON frame is empty.");
            }

            if (!TryGetUtf8ByteCount(json, out var byteCount))
            {
                return ProtocolValidationResult.Failure(ProtocolErrorCode.InvalidJson, "JSON contains invalid UTF-16.");
            }

            if (byteCount > ProtocolConstants.MaxJsonUtf8Bytes)
            {
                return ProtocolValidationResult.Failure(
                    ProtocolErrorCode.MessageTooLarge,
                    $"JSON frame exceeds {ProtocolConstants.MaxJsonUtf8Bytes} UTF-8 bytes.");
            }

            return ProtocolValidationResult.Success;
        }

        public static string FormatUtcTimestamp(DateTimeOffset timestamp)
        {
            return timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        public static bool TryParseUtcTimestamp(string value, out DateTimeOffset timestampUtc)
        {
            timestampUtc = default;
            if (string.IsNullOrEmpty(value) || !UtcRfc3339Pattern.IsMatch(value))
            {
                return false;
            }

            // RFC3339 permits more fractional precision than DateTimeOffset's seven ticks digits.
            // Retain the instant at available precision rather than rejecting a valid Rust/chrono timestamp.
            var normalized = TruncateFractionToTicks(value);
            if (!DateTimeOffset.TryParseExact(
                    normalized,
                    new[]
                    {
                        "yyyy-MM-dd'T'HH:mm:ss'Z'",
                        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                        "yyyy-MM-dd'T'HH:mm:sszzz",
                        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
                    },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out timestampUtc))
            {
                return false;
            }

            timestampUtc = timestampUtc.ToUniversalTime();
            return true;
        }

        public static ProtocolValidationResult ValidateAvatarId(string avatarId)
        {
            return Guid.TryParseExact(avatarId, "D", out _)
                ? ProtocolValidationResult.Success
                : ProtocolValidationResult.Failure(
                    ProtocolErrorCode.InvalidPayload,
                    "avatar_id must be a canonical UUID string.");
        }

        public static ProtocolValidationResult ValidateAccessToken(string token)
        {
            return string.IsNullOrEmpty(token)
                ? ProtocolValidationResult.Failure(ProtocolErrorCode.InvalidPayload, "Access token is required.")
                : ProtocolValidationResult.Success;
        }

        private static bool TryGetUtf8ByteCount(string value, out int byteCount)
        {
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                byteCount = 0;
                return false;
            }
        }

        private static string TruncateFractionToTicks(string value)
        {
            var dot = value.IndexOf('.');
            if (dot < 0)
            {
                return value;
            }

            var suffix = value.EndsWith("Z", StringComparison.Ordinal) ? value.Length - 1 : value.Length - 6;
            var fractionLength = suffix - dot - 1;
            if (fractionLength <= 7)
            {
                return value;
            }

            return value.Substring(0, dot + 1 + 7) + value.Substring(suffix);
        }
    }
}
