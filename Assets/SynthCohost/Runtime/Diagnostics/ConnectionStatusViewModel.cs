using System;
using SynthCohost.Runtime.Session;

namespace SynthCohost.Runtime.Diagnostics
{
    public sealed class ConnectionStatusViewModel
    {
        private readonly IMainThreadDispatcher dispatcher;

        public ConnectionStatusViewModel(IMainThreadDispatcher dispatcher = null)
        {
            this.dispatcher = dispatcher;
        }

        public event Action Changed;

        public SessionState State { get; private set; } = SessionState.Disconnected;
        public string EndpointHost { get; private set; } = string.Empty;
        public bool HasSession { get; private set; }
        public bool HasActiveTurn { get; private set; }
        public int ReconnectAttempt { get; private set; }
        public string LastEventType { get; private set; } = string.Empty;
        public string LastOutboundEventType { get; private set; } = string.Empty;
        public DateTimeOffset? LastInboundAtUtc { get; private set; }
        public DateTimeOffset? LastOutboundAtUtc { get; private set; }
        public string LastCloseSummary { get; private set; } = string.Empty;
        public string SanitizedError { get; private set; } = string.Empty;
        public DateTimeOffset? ConnectingSince { get; private set; }
        public int ConnectCount { get; private set; }
        public int HeartbeatSendCount { get; private set; }
        public int InboundCount { get; private set; }
        public int SanitizedErrorCount { get; private set; }

        public bool IsWakingServer => State == SessionState.Connecting &&
                                      ConnectingSince.HasValue &&
                                      DateTimeOffset.UtcNow - ConnectingSince.Value >= TimeSpan.FromSeconds(5);

        internal void SetState(SessionState state, Uri endpoint = null)
        {
            Publish(() =>
            {
                State = state;
                if (endpoint != null)
                {
                    EndpointHost = endpoint.Host;
                }

                if (state == SessionState.Connecting)
                {
                    ConnectCount++;
                    ConnectingSince = DateTimeOffset.UtcNow;
                    LastEventType = string.Empty;
                    LastInboundAtUtc = null;
                    LastOutboundEventType = string.Empty;
                    LastOutboundAtUtc = null;
                }
                else if (state != SessionState.Reconnecting)
                {
                    ConnectingSince = null;
                }

                Changed?.Invoke();
            });
        }

        internal void SetSession(bool present)
        {
            Publish(() =>
            {
                HasSession = present;
                Changed?.Invoke();
            });
        }

        internal void SetTurn(bool active)
        {
            Publish(() =>
            {
                HasActiveTurn = active;
                Changed?.Invoke();
            });
        }

        internal void SetReconnectAttempt(int attempt)
        {
            Publish(() =>
            {
                ReconnectAttempt = attempt;
                Changed?.Invoke();
            });
        }

        internal void SetLastEvent(string eventType)
        {
            Publish(() =>
            {
                LastEventType = eventType ?? string.Empty;
                LastInboundAtUtc = DateTimeOffset.UtcNow;
                InboundCount++;
                Changed?.Invoke();
            });
        }

        internal void SetLastOutboundEvent(string eventType)
        {
            Publish(() =>
            {
                LastOutboundEventType = eventType ?? string.Empty;
                LastOutboundAtUtc = DateTimeOffset.UtcNow;
                if (string.Equals(eventType, "heartbeat", StringComparison.Ordinal))
                {
                    HeartbeatSendCount++;
                }

                Changed?.Invoke();
            });
        }

        internal void SetLastClose(
            int? code,
            bool wasClean,
            bool remoteInitiated,
            string directive)
        {
            Publish(() =>
            {
                var codeText = code.HasValue ? code.Value.ToString() : "none";
                LastCloseSummary =
                    $"code {codeText}; {(remoteInitiated ? "remote" : "local")}; " +
                    $"{(wasClean ? "clean" : "unclean")}; {directive ?? "unknown policy"}";
                Changed?.Invoke();
            });
        }

        internal void SetError(string sanitizedError)
        {
            Publish(() =>
            {
                SanitizedError = sanitizedError ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(SanitizedError))
                {
                    SanitizedErrorCount++;
                }

                Changed?.Invoke();
            });
        }

        private void Publish(Action update)
        {
            if (dispatcher == null || dispatcher.IsMainThread)
            {
                update();
                return;
            }

            dispatcher.Post(update);
        }
    }
}
