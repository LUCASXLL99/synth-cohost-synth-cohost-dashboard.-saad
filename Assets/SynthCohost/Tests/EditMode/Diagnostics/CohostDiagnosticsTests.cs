using System;
using NUnit.Framework;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using UnityEngine;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.EditMode.Diagnostics
{
    public sealed class CohostDiagnosticsTests
    {
        [Test]
        public void ProductionDiagnostics_LogExceptionTypeButNeverExceptionMessage()
        {
            const string secret = "SECRET_FROM_EXCEPTION_MESSAGE";
            var diagnostics = new CohostDiagnostics(DiagnosticLogLevel.Verbose);
            CohostDiagnosticEvent captured = default;
            diagnostics.EventWritten += diagnosticEvent => captured = diagnosticEvent;
            LogAssert.Expect(
                LogType.Warning,
                "[SynthCohost/Test] Safe diagnostic. (InvalidOperationException)");

            diagnostics.Write(
                DiagnosticLogLevel.Warning,
                "Test",
                "Safe diagnostic.",
                new InvalidOperationException(secret));

            Assert.That(captured.Message, Is.EqualTo("Safe diagnostic."));
            Assert.That(captured.ExceptionType, Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(captured.Message, Does.Not.Contain(secret));
        }
    }
}
