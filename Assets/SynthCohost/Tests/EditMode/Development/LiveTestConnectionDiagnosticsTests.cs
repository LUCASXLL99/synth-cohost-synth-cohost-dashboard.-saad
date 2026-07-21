using System;
using System.Linq;
using NUnit.Framework;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace SynthCohost.Tests.EditMode.Development
{
    public sealed class LiveTestConnectionDiagnosticsTests
    {
        [TestCase(
            (int)LiveTestInputFailure.MissingClient,
            "Connect blocked before network use: client component is missing.")]
        [TestCase(
            (int)LiveTestInputFailure.MissingSettings,
            "Connect blocked before network use: connection settings are missing.")]
        [TestCase(
            (int)LiveTestInputFailure.InvalidEndpoint,
            "Connect blocked before network use: endpoint is invalid or unsafe.")]
        [TestCase(
            (int)LiveTestInputFailure.MissingToken,
            "Connect blocked before network use: access token is missing.")]
        [TestCase(
            (int)LiveTestInputFailure.ExpiredToken,
            "Connect blocked before network use: access token is expired; click Get access token.")]
        [TestCase(
            (int)LiveTestInputFailure.ExpiringSoonToken,
            "Connect blocked before network use: access token expires too soon for a possible cold start.")]
        [TestCase(
            (int)LiveTestInputFailure.InvalidAvatar,
            "Connect blocked before network use: avatar UUID is invalid or empty.")]
        [TestCase(
            (int)LiveTestInputFailure.MissingRefreshCredentials,
            "Get access token blocked: enter account email and password in the panel first.")]
        public void InputRejected_LogsAnExactSafeReason(
            int failureValue,
            string expected)
        {
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            LogAssert.Expect(LogType.Error, "[SynthCohost/LiveTest] " + expected);

            diagnostics.InputRejected((LiveTestInputFailure)failureValue);

            var entry = diagnostics.Snapshot().Single();
            Assert.That(entry.Severity, Is.EqualTo(LiveTestActivitySeverity.Error));
            Assert.That(entry.Message, Is.EqualTo(expected));
        }

        [Test]
        public void ConnectStarting_LogsOnlyEndpointAuthorityAndHidesCredentials()
        {
            const string secret = "SECRET_QUERY_VALUE";
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            var endpoint = new Uri(
                $"wss://user:password@example.com/private-path?token={secret}#fragment");
            const string expected =
                "[SynthCohost/LiveTest] Opening WebSocket to wss://example.com; " +
                "timeout=75s; credentials hidden.";
            LogAssert.Expect(LogType.Log, expected);

            diagnostics.ConnectStarting(endpoint, TimeSpan.FromSeconds(75));

            var message = diagnostics.Snapshot().Single().Message;
            Assert.That(message, Does.Not.Contain(secret));
            Assert.That(message, Does.Not.Contain("user"));
            Assert.That(message, Does.Not.Contain("password"));
            Assert.That(message, Does.Not.Contain("private-path"));
        }

        [Test]
        public void OperationFailed_LogsOnlyExceptionType()
        {
            const string secret = "SECRET_EXCEPTION_MESSAGE";
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            const string expected =
                "[SynthCohost/LiveTest] Connect failed (InvalidOperationException); " +
                "exception details were hidden.";
            LogAssert.Expect(LogType.Error, expected);

            diagnostics.OperationFailed(
                LiveTestOperationKind.Connect,
                new InvalidOperationException(secret));

            Assert.That(diagnostics.Snapshot().Single().Message, Does.Not.Contain(secret));
        }

        [Test]
        public void SystemErrorReceived_NormalizesUntrustedCodeAndHidesMessageChannel()
        {
            const string rawCode = "BAD\nSECRET_CODE";
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            const string expected =
                "[SynthCohost/LiveTest] Inbound system.error handled; " +
                "code=UNRECOGNIZED_SYSTEM_ERROR; backend message hidden.";
            LogAssert.Expect(LogType.Error, expected);

            diagnostics.SystemErrorReceived(rawCode);

            Assert.That(diagnostics.Snapshot().Single().Message, Does.Not.Contain(rawCode));
        }

        [Test]
        public void SendCompleted_DoesNotLogResultMessageOrTranscript()
        {
            const string secret = "SECRET_TRANSCRIPT_OR_SERVER_MESSAGE";
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            var result = CohostSendResult.Failure(CohostSendStatus.ValidationFailed, secret);
            const string expected =
                "[SynthCohost/LiveTest] Final transcript send rejected; " +
                "status=ValidationFailed; transcript text hidden.";
            LogAssert.Expect(LogType.Error, expected);

            diagnostics.SendCompleted(true, result);

            Assert.That(diagnostics.Snapshot().Single().Message, Does.Not.Contain(secret));
        }

        [Test]
        public void ActivityBuffer_IsBounded()
        {
            var diagnostics = new LiveTestConnectionDiagnostics(null);
            for (var index = 0;
                 index < LiveTestConnectionDiagnostics.MaximumActivityEntries + 5;
                 index++)
            {
                diagnostics.OperationRequested(
                    LiveTestOperationKind.Connect,
                    SessionState.Disconnected);
            }

            Assert.That(
                diagnostics.Snapshot().Count,
                Is.EqualTo(LiveTestConnectionDiagnostics.MaximumActivityEntries));
        }
    }
}
