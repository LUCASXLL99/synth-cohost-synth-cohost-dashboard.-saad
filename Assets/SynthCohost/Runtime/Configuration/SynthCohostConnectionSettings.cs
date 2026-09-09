using System;
using UnityEngine;

namespace SynthCohost.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "SynthCohostConnectionSettings",
        menuName = "Synth Cohost/Connection Settings")]
    public sealed class SynthCohostConnectionSettings : ScriptableObject
    {
        public const string DefaultLiveEndpoint = "wss://synth-cohost-app-bzi4.onrender.com/ws";

        [Header("Connection")]
        [SerializeField] private string endpointUrl = DefaultLiveEndpoint;
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private bool runInBackground = true;

        [Header("Timeouts")]
        [Tooltip("Render can take about 50 seconds to wake. Keep this above that cold-start window.")]
        [SerializeField, Min(60f)] private float connectTimeoutSeconds = 75f;
        [SerializeField, Min(0.1f)] private float authSendTimeoutSeconds = 5f;
        [SerializeField, Min(1f)] private float finalTurnResponseTimeoutSeconds = 120f;

        [Header("Current deployed v2")]
        [Tooltip("Fixed by the deployed v2 contract. Proposed v2.1 may advertise this from the server later.")]
        [SerializeField, Min(1f)] private float heartbeatIntervalSeconds = 20f;

        [Header("Reconnect")]
        [SerializeField] private ReconnectPolicySettings reconnect = new ReconnectPolicySettings();

        [Header("Diagnostics")]
        [SerializeField] private DiagnosticLogLevel diagnosticLogLevel = DiagnosticLogLevel.Information;

        public string EndpointUrl => endpointUrl;
        public bool AutoConnect => autoConnect;
        public bool RunInBackground => runInBackground;
        public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(Math.Max(60f, connectTimeoutSeconds));
        public TimeSpan AuthSendTimeout => TimeSpan.FromSeconds(Math.Max(0.1f, authSendTimeoutSeconds));
        public TimeSpan FinalTurnResponseTimeout => TimeSpan.FromSeconds(Math.Max(1f, finalTurnResponseTimeoutSeconds));
        public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(Math.Max(1f, heartbeatIntervalSeconds));
        public ReconnectPolicySnapshot Reconnect => reconnect.CreateSnapshot();
        public DiagnosticLogLevel DiagnosticLogLevel => diagnosticLogLevel;

        public bool TryGetEndpoint(out Uri endpoint, out string error)
        {
            return TryParseEndpoint(endpointUrl, out endpoint, out error);
        }

        public static bool TryParseEndpoint(string value, out Uri endpoint, out string error)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out endpoint))
            {
                error = "Endpoint URL must be an absolute ws:// or wss:// URL.";
                return false;
            }

            if (!string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = null;
                error = "Endpoint URL scheme must be ws or wss.";
                return false;
            }

            if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                endpoint = null;
                error = "Endpoint URL must not contain user info, a query, or a fragment.";
                return false;
            }

            if (!IsLoopback(endpoint) && !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = null;
                error = "Non-loopback endpoints must use wss.";
                return false;
            }

            error = null;
            return true;
        }

        public ConnectionRuntimeOptions CreateRuntimeOptions()
        {
            if (!TryGetEndpoint(out var endpoint, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return new ConnectionRuntimeOptions(
                endpoint,
                ConnectTimeout,
                AuthSendTimeout,
                FinalTurnResponseTimeout,
                HeartbeatInterval,
                Reconnect);
        }

        public ConnectionRuntimeOptions CreateRuntimeOptions(Uri endpointOverride)
        {
            if (endpointOverride == null)
            {
                return CreateRuntimeOptions();
            }

            if (!TryParseEndpoint(endpointOverride.AbsoluteUri, out var endpoint, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return new ConnectionRuntimeOptions(
                endpoint,
                ConnectTimeout,
                AuthSendTimeout,
                FinalTurnResponseTimeout,
                HeartbeatInterval,
                Reconnect);
        }

        private static bool IsLoopback(Uri endpoint)
        {
            return endpoint.IsLoopback ||
                   string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        private void OnValidate()
        {
            connectTimeoutSeconds = Mathf.Max(60f, connectTimeoutSeconds);
            authSendTimeoutSeconds = Mathf.Max(0.1f, authSendTimeoutSeconds);
            finalTurnResponseTimeoutSeconds = Mathf.Max(1f, finalTurnResponseTimeoutSeconds);
            heartbeatIntervalSeconds = Mathf.Max(1f, heartbeatIntervalSeconds);
            reconnect ??= new ReconnectPolicySettings();
            reconnect.Validate();
        }
    }

    public readonly struct ConnectionRuntimeOptions
    {
        public ConnectionRuntimeOptions(
            Uri endpoint,
            TimeSpan connectTimeout,
            TimeSpan authSendTimeout,
            TimeSpan finalTurnResponseTimeout,
            TimeSpan heartbeatInterval,
            ReconnectPolicySnapshot reconnect)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            ConnectTimeout = connectTimeout;
            AuthSendTimeout = authSendTimeout;
            FinalTurnResponseTimeout = finalTurnResponseTimeout;
            HeartbeatInterval = heartbeatInterval;
            Reconnect = reconnect;
        }

        public Uri Endpoint { get; }
        public TimeSpan ConnectTimeout { get; }
        public TimeSpan AuthSendTimeout { get; }
        public TimeSpan FinalTurnResponseTimeout { get; }
        public TimeSpan HeartbeatInterval { get; }
        public ReconnectPolicySnapshot Reconnect { get; }
    }

    public enum DiagnosticLogLevel
    {
        Off = 0,
        Error = 1,
        Warning = 2,
        Information = 3,
        Verbose = 4
    }
}
