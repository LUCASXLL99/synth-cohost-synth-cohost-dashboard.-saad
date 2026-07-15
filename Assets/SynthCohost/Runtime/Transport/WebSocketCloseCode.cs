using System;
using System.Text;

namespace SynthCohost.Transport
{
    /// <summary>RFC 6455 close-code and close-reason validation.</summary>
    public static class WebSocketCloseCode
    {
        public const int NormalClosure = 1000;
        public const int UnsupportedData = 1003;
        public const int InvalidPayloadData = 1007;
        public const int PolicyViolation = 1008;
        public const int MessageTooBig = 1009;

        // A control frame has at most 125 payload bytes. A close code consumes two.
        public const int MaximumReasonUtf8Bytes = 123;

        public static bool IsValidToSend(int code)
        {
            if (code < 1000 || code > 4999)
            {
                return false;
            }

            // RFC-reserved values that must never appear in a close frame.
            return code != 1004 && code != 1005 && code != 1006 && code != 1015;
        }

        public static void ValidateToSend(int code, string reason)
        {
            if (!IsValidToSend(code))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(code),
                    code,
                    "WebSocket close codes must be in the range 1000-4999 and cannot use reserved values 1004, 1005, 1006, or 1015.");
            }

            var reasonByteCount = Encoding.UTF8.GetByteCount(reason ?? string.Empty);
            if (reasonByteCount > MaximumReasonUtf8Bytes)
            {
                throw new ArgumentException(
                    $"A WebSocket close reason cannot exceed {MaximumReasonUtf8Bytes} UTF-8 bytes.",
                    nameof(reason));
            }
        }
    }
}
