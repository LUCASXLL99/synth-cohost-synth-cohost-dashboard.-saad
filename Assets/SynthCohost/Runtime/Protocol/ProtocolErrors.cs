using System;

namespace SynthCohost.Protocol
{
    public enum ProtocolErrorCode
    {
        None = 0,
        EmptyInput,
        MessageTooLarge,
        JsonTooDeep,
        InvalidJson,
        RootNotObject,
        MissingField,
        InvalidFieldType,
        InvalidEventType,
        UnsupportedVersion,
        InvalidSessionId,
        InvalidTimestamp,
        InvalidPayload,
        InvalidSttText,
        EventTypeMismatch,
        SerializationFailed
    }

    /// <summary>A sanitized protocol failure suitable for diagnostics; it never stores raw frames or tokens.</summary>
    public sealed class ProtocolError
    {
        public static readonly ProtocolError None = new ProtocolError(ProtocolErrorCode.None, string.Empty);

        public ProtocolErrorCode Code { get; }
        public string Message { get; }

        public ProtocolError(ProtocolErrorCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return Code == ProtocolErrorCode.None ? "None" : $"{Code}: {Message}";
        }
    }

    public readonly struct ProtocolValidationResult
    {
        public static readonly ProtocolValidationResult Success =
            new ProtocolValidationResult(true, ProtocolError.None);

        public bool IsValid { get; }
        public ProtocolError Error { get; }

        private ProtocolValidationResult(bool isValid, ProtocolError error)
        {
            IsValid = isValid;
            Error = error ?? ProtocolError.None;
        }

        public static ProtocolValidationResult Failure(ProtocolErrorCode code, string message)
        {
            return new ProtocolValidationResult(false, new ProtocolError(code, message));
        }
    }

    public sealed class ProtocolException : Exception
    {
        public ProtocolError Error { get; }

        public ProtocolException(ProtocolError error)
            : base(error?.ToString() ?? "Unknown protocol error")
        {
            Error = error ?? new ProtocolError(ProtocolErrorCode.SerializationFailed, "Unknown protocol error.");
        }
    }
}
