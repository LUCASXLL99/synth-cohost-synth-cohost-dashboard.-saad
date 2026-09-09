using System;
using System.Threading;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Development;
using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Runtime.Bootstrap
{
    /// <summary>
    /// Product-scene bootstrap: loads gitignored/env credentials and connects.
    /// Not a debug credential panel. Host can replace this by calling the client APIs directly.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Runtime Bootstrap")]
    public sealed class SynthCohostRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private SynthCohostClientBehaviour client;
        [SerializeField] private SynthCohostConnectionSettings settings;
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool persistAcrossScenes;

        [NonSerialized] private string lastSafeStatus = "Waiting for credentials.";

        public string LastSafeStatus => lastSafeStatus;

        private async void Start()
        {
            client ??= GetComponent<SynthCohostClientBehaviour>();
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (client == null)
            {
                lastSafeStatus = "Client component is missing.";
                enabled = false;
                return;
            }

            var fallback = settings != null
                ? settings.EndpointUrl
                : SynthCohostConnectionSettings.DefaultLiveEndpoint;
            var draft = LiveTestPlaceholderSource.Load(fallback);

            if (!string.IsNullOrWhiteSpace(draft.SafeNotice))
            {
                lastSafeStatus = draft.SafeNotice;
            }

            if (!string.IsNullOrWhiteSpace(draft.EndpointUrl) &&
                !client.TrySetRuntimeEndpoint(draft.EndpointUrl, out var endpointError))
            {
                lastSafeStatus = endpointError;
                return;
            }

            if (!Guid.TryParse(draft.AvatarId, out var avatarId) || avatarId == Guid.Empty)
            {
                lastSafeStatus =
                    "Dashboard scene needs a tenant avatar UUID in the local credentials file or SYNTH_COHOST_AVATAR_ID.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(draft.AccessToken) &&
                !string.IsNullOrWhiteSpace(draft.RefreshToken))
            {
                client.SetAuthSession(draft.AccessToken, draft.RefreshToken, avatarId);
            }
            else if (!string.IsNullOrWhiteSpace(draft.AccessToken))
            {
                client.SetRuntimeCredentials(draft.AccessToken, avatarId);
            }
            else if (!string.IsNullOrWhiteSpace(draft.RefreshToken) ||
                     (!string.IsNullOrWhiteSpace(draft.Email) &&
                      !string.IsNullOrWhiteSpace(draft.Password)))
            {
                if (!LiveTestAccessTokenRefresher.TryBuildRestBaseUri(
                        client.EffectiveEndpoint != null
                            ? client.EffectiveEndpoint.AbsoluteUri
                            : draft.EndpointUrl,
                        out var restBase,
                        out var restError))
                {
                    lastSafeStatus = restError;
                    return;
                }

                var tokens = await LiveTestAccessTokenRefresher.RefreshAsync(
                    restBase,
                    draft.RefreshToken,
                    draft.Email,
                    draft.Password,
                    CancellationToken.None);
                if (!tokens.Succeeded)
                {
                    lastSafeStatus = tokens.SafeError;
                    return;
                }

                client.SetAuthSession(tokens.AccessToken, tokens.RefreshToken, avatarId);
                LiveTestPlaceholderSource.TrySaveLocalDraft(
                    draft.EndpointUrl,
                    tokens.AccessToken,
                    draft.AvatarId,
                    tokens.RefreshToken,
                    draft.Email,
                    draft.Password,
                    out _);
            }
            else
            {
                lastSafeStatus =
                    "Dashboard scene is waiting for Host or a local credentials file (access token + avatar UUID).";
                return;
            }

            if (!connectOnStart)
            {
                lastSafeStatus = "Credentials staged; connectOnStart is off.";
                return;
            }

            lastSafeStatus = "Connecting...";
            try
            {
                await client.ConnectAsync();
                lastSafeStatus = client.State == SessionState.Ready
                    ? "Ready (auth sent)."
                    : $"Connect finished; state={client.State}.";
            }
            catch (Exception exception)
            {
                lastSafeStatus = $"Connect failed ({exception.GetType().Name}).";
            }
        }
    }
}
