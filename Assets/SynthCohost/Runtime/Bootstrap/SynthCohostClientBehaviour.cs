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
        [NonSerialized] private CredentialProviderSlot credentialSlot;
        [NonSerialized] private CohostSessionController session;
        [NonSerialized] private ICohostDiagnostics diagnostics;
        [NonSerialized] private Uri runtimeEndpointOverride;
        [NonSerialized] private bool shuttingDown;

        public SessionState State => session?.State ?? SessionState.Disconnected;
        public ConnectionStatusViewModel Status => session?.Status;
        public ICohostOutboundSession Outbound => session;

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
        }

        /// <summary>Convenience API for credentials already obtained at runtime. Never call with checked-in constants.</summary>
        public void SetRuntimeCredentials(string accessToken, Guid avatarId)
        {
            runtimeCredentials ??= new RuntimeCredentialProvider();
            runtimeCredentials.SetCredentials(accessToken, avatarId);
            credentialProvider = runtimeCredentials;
            credentialSlot?.Set(runtimeCredentials);
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
                error = string.Empty;
                return true;
            }

            if (session != null && !CanChangeRuntimeEndpoint(session.State))
            {
                error = "Disconnect before changing the WebSocket endpoint.";
                return false;
            }

            runtimeEndpointOverride = endpoint;
            if (session != null)
            {
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
            return session.ConnectAsync(cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return session == null ? Task.CompletedTask : session.DisconnectAsync(cancellationToken);
        }

        public async Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            EnsureComposed();
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
            credentialProvider ??= runtimeCredentials;
            credentialSlot = new CredentialProviderSlot();
            credentialSlot.Set(credentialProvider);
            Compose();
        }

        private void Start()
        {
            if (!settings.AutoConnect)
            {
                return;
            }

            if (ReferenceEquals(credentialProvider, runtimeCredentials) && !runtimeCredentials.HasCredentials)
            {
                diagnostics.Write(
                    DiagnosticLogLevel.Warning,
                    "Bootstrap",
                    "Auto-connect is waiting for runtime credentials. Call SetCredentialProvider or SetRuntimeCredentials, then ConnectAsync.");
                return;
            }

            Observe(session.ConnectAsync(), "Auto-connect failed.");
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            session?.Dispose();
            session = null;
        }

        private void OnDestroy()
        {
            if (!shuttingDown)
            {
                session?.Dispose();
                session = null;
            }
        }

        private void Compose()
        {
            var options = settings.CreateRuntimeOptions(runtimeEndpointOverride);
            diagnostics = new CohostDiagnostics(settings.DiagnosticLogLevel);
            var dispatcher = new MainThreadDispatcher(SynchronizationContext.Current);
            var codec = new ProtocolCodec();
            var dialect = new DeployedV2ProtocolDialect(codec);
            var deferredOutbound = new DeferredOutboundSession();

            IAvatarBehaviorController avatar = avatarController != null
                ? avatarController
                : new NullAvatarBehaviorController();
            IAiResponseSink ai = aiResponseSink != null
                ? aiResponseSink
                : new NullAiResponseSink();
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
            deferredOutbound.Bind(session);
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
