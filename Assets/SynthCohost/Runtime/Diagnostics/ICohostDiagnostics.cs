using System;
using SynthCohost.Runtime.Configuration;

namespace SynthCohost.Runtime.Diagnostics
{
    public interface ICohostDiagnostics
    {
        event Action<CohostDiagnosticEvent> EventWritten;

        void Write(
            DiagnosticLogLevel level,
            string category,
            string message,
            Exception exception = null);
    }

    public readonly struct CohostDiagnosticEvent
    {
        public CohostDiagnosticEvent(
            DateTimeOffset timestamp,
            DiagnosticLogLevel level,
            string category,
            string message,
            string exceptionType)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
            ExceptionType = exceptionType;
        }

        public DateTimeOffset Timestamp { get; }
        public DiagnosticLogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public string ExceptionType { get; }
    }
}
