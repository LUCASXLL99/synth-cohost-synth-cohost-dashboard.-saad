using System;
using SynthCohost.Runtime.Configuration;
using UnityEngine;

namespace SynthCohost.Runtime.Diagnostics
{
    public sealed class CohostDiagnostics : ICohostDiagnostics
    {
        private readonly DiagnosticLogLevel minimumLevel;

        public CohostDiagnostics(DiagnosticLogLevel minimumLevel)
        {
            this.minimumLevel = minimumLevel;
        }

        public event Action<CohostDiagnosticEvent> EventWritten;

        public void Write(
            DiagnosticLogLevel level,
            string category,
            string message,
            Exception exception = null)
        {
            if (minimumLevel == DiagnosticLogLevel.Off || level > minimumLevel)
            {
                return;
            }

            // Never include Exception.Message: credential-provider or HTTP exceptions may contain secrets.
            var diagnosticEvent = new CohostDiagnosticEvent(
                DateTimeOffset.UtcNow,
                level,
                category,
                message,
                exception?.GetType().Name);

            EventWritten?.Invoke(diagnosticEvent);

            var suffix = exception == null ? string.Empty : $" ({exception.GetType().Name})";
            var formatted = $"[SynthCohost/{category}] {message}{suffix}";
            switch (level)
            {
                case DiagnosticLogLevel.Error:
                    Debug.LogError(formatted);
                    break;
                case DiagnosticLogLevel.Warning:
                    Debug.LogWarning(formatted);
                    break;
                default:
                    Debug.Log(formatted);
                    break;
            }
        }
    }

    public sealed class NullCohostDiagnostics : ICohostDiagnostics
    {
        public event Action<CohostDiagnosticEvent> EventWritten
        {
            add { }
            remove { }
        }

        public void Write(DiagnosticLogLevel level, string category, string message, Exception exception = null)
        {
        }
    }
}
