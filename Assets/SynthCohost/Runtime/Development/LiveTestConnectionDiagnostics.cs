using System;
using System.Collections.Generic;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    internal enum LiveTestOperationKind
    {
        Connect,
        Reconnect,
        Disconnect,
        SendPartial,
        SendFinal
    }

    internal enum LiveTestInputFailure
    {
        None,
        MissingClient,
        MissingSettings,
        InvalidEndpoint,
        MissingToken,
        ExpiredToken,
        ExpiringSoonToken,
        InvalidAvatar
    }

    internal enum LiveTestActivitySeverity
    {
        Information,
        Warning,
        Error
    }

    internal readonly struct LiveTestActivityEntry
    {
        internal LiveTestActivityEntry(
            DateTimeOffset timestampUtc,
            LiveTestActivitySeverity severity,
            string message)
        {
            TimestampUtc = timestampUtc;
            Severity = severity;
            Message = message ?? string.Empty;
        }

        internal DateTimeOffset TimestampUtc { get; }
        internal LiveTestActivitySeverity Severity { get; }
        internal string Message { get; }

        internal string ToDisplayLine()
        {
            return $"[{TimestampUtc:HH:mm:ss} UTC] {Severity}: {Message}";
        }
    }

    /// <summary>
    /// Development-only diagnostics for paths that stop before the reusable session diagnostics
    /// are reached. Every message is constructed from fixed text, enums, booleans, a host-only
    /// endpoint view, or an exception type. Raw credentials, identifiers, transcript/response
    /// text, backend messages, and exception messages never enter this logger.
    /// </summary>
    internal sealed class LiveTestConnectionDiagnostics
    {
        internal const int MaximumActivityEntries = 32;
        private const string ConsolePrefix = "[SynthCohost/LiveTest] ";

        private readonly UnityEngine.Object context;
        private readonly Queue<LiveTestActivityEntry> entries =
            new Queue<LiveTestActivityEntry>(MaximumActivityEntries);

        internal LiveTestConnectionDiagnostics(UnityEngine.Object context)
        {
            this.context = context;
        }

        internal IReadOnlyList<LiveTestActivityEntry> Snapshot()
        {
            return entries.ToArray();
        }

        internal void PanelInitialized(
            bool dependenciesReady,
            bool tokenPresent,
            bool avatarPresent)
        {
            var message =
                $"Panel initialized; dependencies={(dependenciesReady ? "ready" : "missing")}; " +
                $"token={(tokenPresent ? "present" : "missing")}; " +
                $"avatar={(avatarPresent ? "present" : "missing")}; values hidden.";
            Write(
                dependenciesReady ? LiveTestActivitySeverity.Information : LiveTestActivitySeverity.Error,
                message);
        }

        internal void PlaceholderLoadWarning()
        {
            Write(
                LiveTestActivitySeverity.Warning,
                "A local placeholder source reported a safe warning; review Last operation in the panel.");
        }

        internal void OperationRequested(LiveTestOperationKind operation, SessionState state)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"{GetOperationLabel(operation)} requested; current state={state}.");
        }

        internal void InputRejected(LiveTestInputFailure failure)
        {
            Write(LiveTestActivitySeverity.Error, GetInputFailureMessage(failure));
        }

        internal void OperationBusy()
        {
            Write(
                LiveTestActivitySeverity.Error,
                "Request blocked because another live-test operation is already running.");
        }

        internal void EndpointOverrideRejected()
        {
            Write(
                LiveTestActivitySeverity.Error,
                "Connect blocked before network use because the runtime endpoint override was rejected.");
        }

        internal void ReconnectRequiresFreshCredentials()
        {
            Write(
                LiveTestActivitySeverity.Error,
                "Reconnect blocked: authentication was rejected; paste a fresh token and use Connect.");
        }

        internal void TranscriptMissing(bool final)
        {
            Write(
                LiveTestActivitySeverity.Error,
                $"{(final ? "Final" : "Partial")} transcript send blocked: transcript text is empty.");
        }

        internal void ConnectStarting(Uri endpoint, TimeSpan timeout)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"Opening WebSocket to {FormatEndpointAuthority(endpoint)}; " +
                $"timeout={Math.Ceiling(timeout.TotalSeconds):0}s; credentials hidden.");
        }

        internal void RuntimeEndpointApplied()
        {
            Write(
                LiveTestActivitySeverity.Information,
                "Runtime endpoint accepted; the settings asset was not modified.");
        }

        internal void CredentialsStaged()
        {
            Write(
                LiveTestActivitySeverity.Information,
                "Runtime credentials staged for the auth frame; values hidden.");
        }

        internal void OperationCompleted(LiveTestOperationKind operation, SessionState state)
        {
            if ((operation == LiveTestOperationKind.Connect ||
                 operation == LiveTestOperationKind.Reconnect) &&
                state == SessionState.Ready)
            {
                Write(
                    LiveTestActivitySeverity.Information,
                    $"{GetOperationLabel(operation)} completed; state=Ready; auth sent; " +
                    "Ready is provisional because deployed v2 has no session.ready.");
                return;
            }

            var suffix = state == SessionState.Reconnecting
                ? " Automatic reconnect is active; Disconnect cancels it."
                : string.Empty;
            Write(
                LiveTestActivitySeverity.Information,
                $"{GetOperationLabel(operation)} completed; state={state}.{suffix}");
        }

        internal void StillWaiting(
            LiveTestOperationKind operation,
            int elapsedSeconds,
            TimeSpan timeout)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"{GetOperationLabel(operation)} is still waiting after {elapsedSeconds}s; " +
                $"a sleeping backend may take about 50s; timeout={Math.Ceiling(timeout.TotalSeconds):0}s.");
        }

        internal void OperationCancelled(LiveTestOperationKind operation)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"{GetOperationLabel(operation)} was cancelled.");
        }

        internal void OperationFailed(LiveTestOperationKind operation, Exception exception)
        {
            var exceptionType = exception?.GetType().Name ?? "UnknownException";
            Write(
                LiveTestActivitySeverity.Error,
                $"{GetOperationLabel(operation)} failed ({exceptionType}); exception details were hidden.");
        }

        internal void SendCompleted(bool final, CohostSendResult result)
        {
            var label = final ? "Final transcript send" : "Partial transcript send";
            var message = result.Succeeded
                ? $"{label} completed; status={result.Status}; transcript text hidden."
                : $"{label} rejected; status={result.Status}; transcript text hidden.";
            Write(
                result.Succeeded
                    ? LiveTestActivitySeverity.Information
                    : LiveTestActivitySeverity.Error,
                message);
        }

        internal void AvatarBehaviorReceived(AvatarBehavior behavior)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"Inbound avatar.state handled; behavior={behavior.ToWireValue()}."
            );
        }

        internal void AiResponseReceived(AiEmotion emotion, AiIntent intent)
        {
            Write(
                LiveTestActivitySeverity.Information,
                $"Inbound ai.response handled; emotion={emotion.ToWireValue()}; " +
                $"intent={intent.ToWireValue()}; response text hidden.");
        }

        internal void SystemErrorReceived(string rawCode)
        {
            var code = ProtocolSystemErrorCodes.ToDiagnosticLabel(rawCode);
            Write(
                LiveTestActivitySeverity.Error,
                $"Inbound system.error handled; code={code}; backend message hidden.");
        }

        private void Write(LiveTestActivitySeverity severity, string message)
        {
            var entry = new LiveTestActivityEntry(DateTimeOffset.UtcNow, severity, message);
            while (entries.Count >= MaximumActivityEntries)
            {
                entries.Dequeue();
            }

            entries.Enqueue(entry);
            var formatted = ConsolePrefix + message;
            switch (severity)
            {
                case LiveTestActivitySeverity.Error:
                    Debug.LogError(formatted, context);
                    break;
                case LiveTestActivitySeverity.Warning:
                    Debug.LogWarning(formatted, context);
                    break;
                default:
                    Debug.Log(formatted, context);
                    break;
            }
        }

        private static string GetInputFailureMessage(LiveTestInputFailure failure)
        {
            switch (failure)
            {
                case LiveTestInputFailure.MissingClient:
                    return "Connect blocked before network use: client component is missing.";
                case LiveTestInputFailure.MissingSettings:
                    return "Connect blocked before network use: connection settings are missing.";
                case LiveTestInputFailure.InvalidEndpoint:
                    return "Connect blocked before network use: endpoint is invalid or unsafe.";
                case LiveTestInputFailure.MissingToken:
                    return "Connect blocked before network use: access token is missing.";
                case LiveTestInputFailure.ExpiredToken:
                    return "Connect blocked before network use: access token is expired; paste a fresh token.";
                case LiveTestInputFailure.ExpiringSoonToken:
                    return "Connect blocked before network use: access token expires too soon for a possible cold start.";
                case LiveTestInputFailure.InvalidAvatar:
                    return "Connect blocked before network use: avatar UUID is invalid or empty.";
                default:
                    return "Connect blocked before network use: input validation failed.";
            }
        }

        private static string GetOperationLabel(LiveTestOperationKind operation)
        {
            switch (operation)
            {
                case LiveTestOperationKind.Connect:
                    return "Connect";
                case LiveTestOperationKind.Reconnect:
                    return "Reconnect";
                case LiveTestOperationKind.Disconnect:
                    return "Disconnect";
                case LiveTestOperationKind.SendPartial:
                    return "Partial transcript send";
                case LiveTestOperationKind.SendFinal:
                    return "Final transcript send";
                default:
                    return "Operation";
            }
        }

        private static string FormatEndpointAuthority(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri)
            {
                return "(invalid endpoint)";
            }

            var host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.Host}]"
                : endpoint.IdnHost;
            var port = endpoint.IsDefaultPort ? string.Empty : $":{endpoint.Port}";
            return $"{endpoint.Scheme}://{host}{port}";
        }
    }
}
