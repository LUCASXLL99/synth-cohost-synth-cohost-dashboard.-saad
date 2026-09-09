using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Authentication;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Features.AiResponse;
using SynthCohost.Runtime.Features.Avatar;
using SynthCohost.Runtime.Features.Errors;
using SynthCohost.Runtime.Routing;
using SynthCohost.Runtime.Session;
using SynthCohost.Transport;
using UnityEngine;

namespace SynthCohost.Runtime.Bootstrap
{
    /// <summary>
    /// Cloud WebSocket/HTTP client for the dashboard co-host.
    /// Host (later) should call <see cref="SetAuthSession"/> or <see cref="SetRuntimeCredentials"/>,
    /// then <see cref="ConnectAsync"/>, then optionally <see cref="SendFinalTranscriptAsync"/>.
    /// Do not open a second socket.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SynthCohostClientBehaviour : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private SynthCohostConnectionSettings settings;

        [Header("Optional presentation adapters")]
        [SerializeField] private AvatarBehaviorControllerBehaviour avatarController;
        [SerializeField] private AiResponseSinkBehaviour aiResponseSink;
        [SerializeField] private SystemErrorSinkBehaviour systemErrorSink;

        [NonSerialized] private ICohostCredentialProvider credentialProvider;
        [NonSerialized] private RuntimeCredentialProvider runtimeCredentials;
        [NonSerialized] private RuntimeAuthSession authSession;
        [NonSerialized] private RefreshingCredentialProvider refreshingCredentials;
        [NonSerialized] private AccessTokenRenewalService tokenRenewal;
        [NonSerialized] private CredentialProviderSlot credentialSlot;
        [NonSerialized] private CohostSessionController session;
        [NonSerialized] private ICohostDiagnostics diagnostics;
        [NonSerialized] private Uri runtimeEndpointOverride;
        [NonSerialized] private bool shuttingDown;
        [NonSerialized] private bool authRecoveryAttempted;
        [NonSerialized] private bool tokenRotationReconnectInFlight;

        public SessionState State => session?.State ?? SessionState.Disconnected;
        public ConnectionStatusViewModel Status => session?.Status;
        public ICohostOutboundSession Outbound => session;
        public RuntimeAuthSession AuthSession => authSession;
        public bool HasRefreshableAuthSession =>
            authSession != null && authSession.HasRefreshToken && authSession.HasAccessCredentials;

        public Uri EffectiveEndpoint
        {
            get
            {
                if (runtimeEndpointOverride != null)
                {
                    return runtimeEndpointOverride;
                }

                return settings != null && settings.TryGetEndpoint(out var endpoint, out _)
                    ? endpoint
                    : null;
            }
        }

        /// <summary>
        /// Supplies a runtime-only provider. Call before enabling auto-connect when credentials come
        /// from login/refresh code. The provider is never serialized by this component.
        /// </summary>
        public void SetCredentialProvider(ICohostCredentialProvider provider)
        {
            credentialProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            credentialSlot?.Set(credentialProvider);
            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                "A runtime credential provider was assigned; credential values were not logged.");
        }

        /// <summary>Convenience API for credentials already obtained at runtime. Never call with checked-in constants.</summary>
        public void SetRuntimeCredentials(string accessToken, Guid avatarId)
        {
            runtimeCredentials ??= new RuntimeCredentialProvider();
            runtimeCredentials.SetCredentials(accessToken, avatarId);
            credentialProvider = runtimeCredentials;
            credentialSlot?.Set(runtimeCredentials);
            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                "Runtime credentials were updated; token and avatar values were not logged.");
        }

        /// <summary>
        /// Stores access + refresh tokens with the selected avatar and enables background renewal
        /// plus refresh-before-reconnect for long live sessions.
        /// </summary>
        public void SetAuthSession(string accessToken, string refreshToken, Guid avatarId)
        {
            EnsureAuthStack();
            authSession.SetSession(accessToken, refreshToken, avatarId);
            credentialProvider = refreshingCredentials;
            credentialSlot?.Set(refreshingCredentials);
            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                "Auth session tokens were updated; access and refresh values were not logged.");
        }

        public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
        {
            EnsureAuthStack();
            authSession.TryGetRefreshToken(out var refreshToken);
            var restBase = TryGetRestBaseUri(out _);
            if (restBase != null && !string.IsNullOrWhiteSpace(refreshToken))
            {
                await AuthHttpClient.LogoutAsync(restBase, refreshToken, cancellationToken);
            }

            await StopTokenRenewalAsync();
            authSession.Clear();
            if (session != null && State != SessionState.Disconnected)
            {
                await session.DisconnectAsync(cancellationToken);
            }

            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                "Auth session cleared after logout.");
            return true;
        }

        /// <summary>
        /// Applies an in-memory endpoint override without modifying the settings asset. Because
        /// session options are immutable, a changed endpoint recomposes the client and is allowed
        /// only while no connection lifecycle is active.
        /// </summary>
        public bool TrySetRuntimeEndpoint(string endpointUrl, out string error)
        {
            if (!SynthCohostConnectionSettings.TryParseEndpoint(
                    endpointUrl,
                    out var endpoint,
                    out error))
            {
                diagnostics?.Write(
                    DiagnosticLogLevel.Warning,
                    "Bootstrap",
                    "A runtime endpoint override was rejected before transport use.");
                return false;
            }

            var current = EffectiveEndpoint;
            if (current != null && Uri.Compare(
                    current,
                    endpoint,
                    UriComponents.AbsoluteUri,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                diagnostics?.Write(
                    DiagnosticLogLevel.Verbose,
                    "Bootstrap",
                    "The requested runtime endpoint already matches the composed endpoint.");
                error = string.Empty;
                return true;
            }

            if (session != null && !CanChangeRuntimeEndpoint(session.State))
            {
                error = "Disconnect before changing the WebSocket endpoint.";
                diagnostics?.Write(
                    DiagnosticLogLevel.Warning,
                    "Bootstrap",
                    "A runtime endpoint change was rejected because a connection lifecycle is active.");
                return false;
            }

            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                "Applying an in-memory endpoint override and recomposing the client.");
            runtimeEndpointOverride = endpoint;
            if (session != null)
            {
                UnbindSessionEvents(session);
                session.Dispose();
                session = null;
                Compose();
            }

            error = string.Empty;
            return true;
        }

        internal static bool CanChangeRuntimeEndpoint(SessionState state)
        {
            return state == SessionState.Disconnected ||
                   state == SessionState.AuthRequired ||
                   state == SessionState.Faulted;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            EnsureComposed();
            diagnostics.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                $"Explicit Connect requested in state {session.State}.");
            return session.ConnectAsync(cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                $"Explicit Disconnect requested in state {State}.");
            await StopTokenRenewalAsync();
            if (session == null)
            {
                return;
            }

            await session.DisconnectAsync(cancellationToken);
        }

        public async Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            EnsureComposed();
            diagnostics.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                $"Explicit Reconnect requested in state {session.State}.");
            await session.DisconnectAsync(cancellationToken);
            await session.ConnectAsync(cancellationToken);
        }

        public Task<CohostSendResult> SendPartialTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            EnsureComposed();
            return session.SendPartialTranscriptAsync(text, cancellationToken);
        }

        public Task<CohostSendResult> SendFinalTranscriptAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            EnsureComposed();
            return session.SendFinalTranscriptAsync(text, cancellationToken);
        }

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError("[SynthCohost] Assign a SynthCohostConnectionSettings asset before connecting.", this);
                enabled = false;
                return;
            }

            Application.runInBackground = settings.RunInBackground;
            runtimeCredentials = new RuntimeCredentialProvider();
            EnsureAuthStack();
            credentialProvider ??= runtimeCredentials;
            credentialSlot = new CredentialProviderSlot();
            credentialSlot.Set(credentialProvider);
            Compose();
        }

        private void Start()
        {
            if (!settings.AutoConnect)
            {
                diagnostics.Write(
                    DiagnosticLogLevel.Information,
                    "Bootstrap",
                    "Auto-connect is disabled; waiting for an explicit Connect request.");
                return;
            }

            if (ReferenceEquals(credentialProvider, runtimeCredentials) && !runtimeCredentials.HasCredentials)
            {
                diagnostics.Write(
                    DiagnosticLogLevel.Warning,
                    "Bootstrap",
                    "Auto-connect is waiting for runtime credentials. Call SetCredentialProvider, SetAuthSession, or SetRuntimeCredentials, then ConnectAsync.");
                return;
            }

            Observe(session.ConnectAsync(), "Auto-connect failed.");
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            tokenRenewal?.Dispose();
            tokenRenewal = null;
            UnbindSessionEvents(session);
            session?.Dispose();
            session = null;
        }

        private void OnDestroy()
        {
            tokenRenewal?.Dispose();
            tokenRenewal = null;
            if (!shuttingDown)
            {
                UnbindSessionEvents(session);
                session?.Dispose();
                session = null;
            }
        }

        private void Compose()
        {
            var options = settings.CreateRuntimeOptions(runtimeEndpointOverride);
            diagnostics = new CohostDiagnostics(settings.DiagnosticLogLevel);
            EnsureAuthStack();
            if (tokenRenewal != null)
            {
                tokenRenewal.TokensRotated -= OnBackgroundTokensRotated;
                tokenRenewal.RefreshFailedRequiresLogin -= OnBackgroundRefreshFailed;
                tokenRenewal.Dispose();
            }

            tokenRenewal = new AccessTokenRenewalService(
                refreshingCredentials,
                authSession,
                diagnostics);
            tokenRenewal.TokensRotated += OnBackgroundTokensRotated;
            tokenRenewal.RefreshFailedRequiresLogin += OnBackgroundRefreshFailed;

            var dispatcher = new MainThreadDispatcher(SynchronizationContext.Current);
            var codec = new ProtocolCodec();
            var dialect = new DeployedV2ProtocolDialect(codec);
            var deferredOutbound = new DeferredOutboundSession();

            IAvatarBehaviorController avatar = avatarController != null
                ? avatarController
                : new NullAvatarBehaviorController();
            IAiResponseSink wiredAi = aiResponseSink != null
                ? aiResponseSink
                : null;
            IAiResponseSink ai = ComposeAiSink(wiredAi, avatar as IAiResponseSink);
            ISystemErrorSink errors = systemErrorSink != null
                ? systemErrorSink
                : new NullSystemErrorSink();

            var handlers = new List<IProtocolMessageHandler>
            {
                new AvatarStateMessageHandler(dialect, avatar, deferredOutbound),
                new AiResponseMessageHandler(dialect, ai),
                new SystemErrorMessageHandler(dialect, errors)
            };
            var router = new ProtocolMessageRouter(codec, dispatcher, handlers, diagnostics);
            var status = new ConnectionStatusViewModel(dispatcher);
            session = new CohostSessionController(
                options,
                credentialSlot,
                dialect,
                new ClientWebSocketTransportFactory(),
                router,
                status,
                diagnostics,
                dispatcher);
            session.StateChanged += OnSessionStateChanged;
            deferredOutbound.Bind(session);
            diagnostics.Write(
                DiagnosticLogLevel.Information,
                "Bootstrap",
                $"Client composed for {FormatEndpointAuthority(options.Endpoint)}; " +
                $"diagnostics={settings.DiagnosticLogLevel}; connect timeout={options.ConnectTimeout.TotalSeconds:0}s.");
        }

        private static IAiResponseSink ComposeAiSink(IAiResponseSink wired, IAiResponseSink fromAvatar)
        {
            if (wired == null && fromAvatar == null)
            {
                return new NullAiResponseSink();
            }

            if (wired == null)
            {
                return fromAvatar;
            }

            if (fromAvatar == null || ReferenceEquals(wired, fromAvatar))
            {
                return wired;
            }

            return new CompositeAiResponseSink(wired, fromAvatar);
        }

        private void EnsureAuthStack()
        {
            authSession ??= new RuntimeAuthSession();
            refreshingCredentials ??= new RefreshingCredentialProvider(
                authSession,
                () => TryGetRestBaseUri(out _));
        }

        private Uri TryGetRestBaseUri(out string error)
        {
            error = string.Empty;
            var endpoint = EffectiveEndpoint?.AbsoluteUri
                           ?? (settings != null ? settings.EndpointUrl : null);
            if (!AuthHttpClient.TryBuildRestBaseUri(endpoint, out var restBase, out error))
            {
                return null;
            }

            return restBase;
        }

        private void OnSessionStateChanged(SessionState previous, SessionState next)
        {
            if (next == SessionState.Ready)
            {
                authRecoveryAttempted = false;
                if (HasRefreshableAuthSession)
                {
                    tokenRenewal?.Start();
                }

                return;
            }

            if (next != SessionState.Ready && next != SessionState.Connecting &&
                next != SessionState.Authenticating)
            {
                (avatarController as IAvatarVisualReset)?.ResetToLivingIdle();
            }

            if (next == SessionState.AuthRequired)
            {
                Observe(StopTokenRenewalAsync(), "Stopping token renewal failed.");
                if (!tokenRotationReconnectInFlight &&
                    !authRecoveryAttempted &&
                    HasRefreshableAuthSession)
                {
                    authRecoveryAttempted = true;
                    Observe(RecoverAuthenticationAsync(), "Auth recovery failed.");
                }

                return;
            }

            if (next == SessionState.Disconnected ||
                next == SessionState.Faulted ||
                next == SessionState.Stopping)
            {
                Observe(StopTokenRenewalAsync(), "Stopping token renewal failed.");
            }
        }

        private async Task RecoverAuthenticationAsync()
        {
            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Auth",
                "Attempting one access-token refresh before requiring a full login.");
            var refreshed = await refreshingCredentials.RefreshNowAsync(CancellationToken.None);
            if (!refreshed)
            {
                diagnostics?.Write(
                    DiagnosticLogLevel.Warning,
                    "Auth",
                    "Access-token refresh failed; full login is required.");
                return;
            }

            EnsureComposed();
            await session.ConnectAsync(CancellationToken.None);
        }

        private void OnBackgroundTokensRotated()
        {
            if (State != SessionState.Ready || tokenRotationReconnectInFlight)
            {
                return;
            }

            diagnostics?.Write(
                DiagnosticLogLevel.Information,
                "Auth",
                "Reconnecting WebSocket with the renewed access token.");
            Observe(ReconnectForTokenRotationAsync(), "Token-rotation reconnect failed.");
        }

        private async Task ReconnectForTokenRotationAsync()
        {
            tokenRotationReconnectInFlight = true;
            try
            {
                await ReconnectAsync(CancellationToken.None);
            }
            finally
            {
                tokenRotationReconnectInFlight = false;
            }
        }

        private void OnBackgroundRefreshFailed()
        {
            diagnostics?.Write(
                DiagnosticLogLevel.Warning,
                "Auth",
                "Background token renewal requires login again.");
            Observe(DisconnectAsync(), "Disconnect after refresh failure failed.");
        }

        private async Task StopTokenRenewalAsync()
        {
            if (tokenRenewal == null)
            {
                return;
            }

            await tokenRenewal.StopAsync();
        }

        private void UnbindSessionEvents(CohostSessionController target)
        {
            if (target == null)
            {
                return;
            }

            target.StateChanged -= OnSessionStateChanged;
        }

        private static string FormatEndpointAuthority(Uri endpoint)
        {
            var host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.Host}]"
                : endpoint.IdnHost;
            var port = endpoint.IsDefaultPort ? string.Empty : $":{endpoint.Port}";
            return $"{endpoint.Scheme}://{host}{port}";
        }

        private void EnsureComposed()
        {
            if (session == null)
            {
                throw new InvalidOperationException("Synth Cohost client is not composed. Ensure the component is active and has settings.");
            }
        }

        private async void Observe(Task task, string message)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                diagnostics?.Write(DiagnosticLogLevel.Error, "Bootstrap", message, exception);
            }
        }

        private void OnValidate()
        {
            if (settings != null && !settings.TryGetEndpoint(out _, out var error))
            {
                Debug.LogWarning($"[SynthCohost] {error}", this);
            }
        }
    }
}
