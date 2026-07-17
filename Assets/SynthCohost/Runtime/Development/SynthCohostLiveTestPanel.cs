using System;
using System.Threading;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    /// <summary>
    /// Development harness for exercising the deployed v2 text flow from SampleScene. Credentials
    /// are private, non-serialized runtime values and are never written to the scene or settings.
    /// Remove this component from production scenes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Development/Live Test Panel")]
    public sealed class SynthCohostLiveTestPanel : MonoBehaviour
    {
        [Header("Wired by the SampleScene setup")]
        [SerializeField] private SynthCohostClientBehaviour client;
        [SerializeField] private SynthCohostConnectionSettings settings;
        [SerializeField] private LiveTestAvatarBehaviorAdapter avatarAdapter;
        [SerializeField] private LiveTestAiResponseAdapter aiResponseAdapter;
        [SerializeField] private LiveTestSystemErrorAdapter systemErrorAdapter;

        [Header("Development UI")]
        [SerializeField, Min(420f)] private float panelWidth = 680f;
        [SerializeField] private string defaultTranscript = "Hello from the Unity live test.";

        [NonSerialized] private string accessToken = string.Empty;
        [NonSerialized] private string avatarId = string.Empty;
        [NonSerialized] private string transcript = string.Empty;
        [NonSerialized] private string lastOperation = "Enter a temporary token and avatar UUID, then click Connect.";
        [NonSerialized] private string lastAvatarBehavior = "(none)";
        [NonSerialized] private string lastAiResponse = "(none)";
        [NonSerialized] private string lastSystemError = "(none)";
        [NonSerialized] private Vector2 scrollPosition;
        [NonSerialized] private CancellationTokenSource activeOperation;
        [NonSerialized] private GUIStyle titleStyle;
        [NonSerialized] private GUIStyle sectionStyle;
        [NonSerialized] private GUIStyle wrappedLabelStyle;

        private bool IsBusy => activeOperation != null;

        private void Awake()
        {
            client ??= GetComponent<SynthCohostClientBehaviour>();
            avatarAdapter ??= GetComponent<LiveTestAvatarBehaviorAdapter>();
            aiResponseAdapter ??= GetComponent<LiveTestAiResponseAdapter>();
            systemErrorAdapter ??= GetComponent<LiveTestSystemErrorAdapter>();
            transcript = string.IsNullOrWhiteSpace(defaultTranscript)
                ? "Hello from the Unity live test."
                : defaultTranscript;

            // Optional process-memory convenience. These environment variables are never saved.
            accessToken = Environment.GetEnvironmentVariable("SYNTH_COHOST_ACCESS_TOKEN") ?? string.Empty;
            avatarId = Environment.GetEnvironmentVariable("SYNTH_COHOST_AVATAR_ID") ?? string.Empty;
        }

        private void OnEnable()
        {
            if (avatarAdapter != null)
            {
                avatarAdapter.BehaviorApplied += OnBehaviorApplied;
            }

            if (aiResponseAdapter != null)
            {
                aiResponseAdapter.ResponseReceived += OnAiResponseReceived;
            }

            if (systemErrorAdapter != null)
            {
                systemErrorAdapter.ErrorReceived += OnSystemErrorReceived;
            }
        }

        private void OnDisable()
        {
            if (avatarAdapter != null)
            {
                avatarAdapter.BehaviorApplied -= OnBehaviorApplied;
            }

            if (aiResponseAdapter != null)
            {
                aiResponseAdapter.ResponseReceived -= OnAiResponseReceived;
            }

            if (systemErrorAdapter != null)
            {
                systemErrorAdapter.ErrorReceived -= OnSystemErrorReceived;
            }

            activeOperation?.Cancel();
            activeOperation?.Dispose();
            activeOperation = null;
        }

        private void OnGUI()
        {
            EnsureStyles();

            var width = Mathf.Min(Mathf.Max(420f, panelWidth), Mathf.Max(420f, Screen.width - 32f));
            var height = Mathf.Max(300f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, width, height), GUI.skin.window);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Synth Cohost v2 Live Test", titleStyle);
            GUILayout.Label(
                "Development panel only. The token is masked, kept in process memory, and is not serialized.",
                wrappedLabelStyle);

            DrawStatus();
            DrawCredentials();
            DrawConnectionControls();
            DrawTranscriptControls();
            DrawResults();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawStatus()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Connection", sectionStyle);
            GUILayout.Label($"Endpoint: {(settings != null ? settings.EndpointUrl : "(settings missing)")}");

            var status = client != null ? client.Status : null;
            var state = client != null ? client.State.ToString() : "Client missing";
            if (status != null && status.IsWakingServer)
            {
                state += " (Waking server — a free-tier cold start can take about 50 seconds)";
            }

            GUILayout.Label($"State: {state}", wrappedLabelStyle);
            if (status != null)
            {
                GUILayout.Label(
                    $"Session: {(status.HasSession ? "yes" : "no")}   " +
                    $"Turn active: {(status.HasActiveTurn ? "yes" : "no")}   " +
                    $"Reconnect attempt: {status.ReconnectAttempt}");
                GUILayout.Label($"Last event: {ValueOrNone(status.LastEventType)}");
                if (!string.IsNullOrWhiteSpace(status.SanitizedError))
                {
                    GUILayout.Label($"Client error: {status.SanitizedError}", wrappedLabelStyle);
                }
            }

            GUILayout.Label($"Last operation: {lastOperation}", wrappedLabelStyle);
        }

        private void DrawCredentials()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Runtime credentials", sectionStyle);
            GUILayout.Label("Access token (masked)");
            accessToken = GUILayout.PasswordField(accessToken ?? string.Empty, '*');
            GUILayout.Label("Avatar UUID");
            avatarId = GUILayout.TextField(avatarId ?? string.Empty);
        }

        private void DrawConnectionControls()
        {
            GUILayout.BeginHorizontal();

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !IsBusy && client != null && client.State != SessionState.Ready;
            if (GUILayout.Button("Connect", GUILayout.Height(30f)))
            {
                Connect();
            }

            GUI.enabled = previousEnabled && !IsBusy && client != null && client.State != SessionState.Disconnected;
            if (GUILayout.Button("Reconnect", GUILayout.Height(30f)))
            {
                Reconnect();
            }

            GUI.enabled = previousEnabled && client != null &&
                          (IsBusy || client.State != SessionState.Disconnected);
            if (GUILayout.Button(IsBusy ? "Cancel / Disconnect" : "Disconnect", GUILayout.Height(30f)))
            {
                Disconnect();
            }

            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
        }

        private void DrawTranscriptControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Transcript", sectionStyle);
            transcript = GUILayout.TextArea(transcript ?? string.Empty, GUILayout.MinHeight(70f));

            GUILayout.BeginHorizontal();
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !IsBusy && client != null &&
                          client.State == SessionState.Ready &&
                          !(client.Status?.HasActiveTurn ?? false);

            if (GUILayout.Button("Send Partial", GUILayout.Height(30f)))
            {
                SendTranscript(false);
            }

            if (GUILayout.Button("Send Final", GUILayout.Height(30f)))
            {
                SendTranscript(true);
            }

            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label("Only one final turn can be active at a time.", wrappedLabelStyle);
        }

        private void DrawResults()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Received", sectionStyle);
            GUILayout.Label($"Avatar behavior (simulated adapter): {lastAvatarBehavior}", wrappedLabelStyle);
            GUILayout.Label($"AI response: {lastAiResponse}", wrappedLabelStyle);
            GUILayout.Label($"System error: {lastSystemError}", wrappedLabelStyle);
        }

        private async void Connect()
        {
            if (!TryValidateCredentials(out var parsedAvatarId, out var validationError))
            {
                lastOperation = validationError;
                return;
            }

            var operation = BeginOperation("Connecting. Keep Play Mode running during a possible cold start...");
            if (operation == null)
            {
                return;
            }

            try
            {
                var token = accessToken;
                client.SetRuntimeCredentials(token, parsedAvatarId);
                accessToken = string.Empty;
                await client.ConnectAsync(operation.Token);
                lastOperation = client.State == SessionState.Ready
                    ? "WebSocket opened and the v2 auth frame was sent. State is Ready."
                    : $"Connection attempt completed; current state is {client.State}. Use State as the source of truth.";
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Connection cancelled.";
            }
            catch (Exception exception)
            {
                lastOperation = $"Connect failed: {exception.Message}";
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async void Reconnect()
        {
            if (client == null)
            {
                lastOperation = "Client component is missing.";
                return;
            }

            var operation = BeginOperation("Reconnecting with the current runtime credentials...");
            if (operation == null)
            {
                return;
            }

            try
            {
                await client.ReconnectAsync(operation.Token);
                lastOperation = "Reconnected with a fresh session ID and sent auth.";
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Reconnect cancelled.";
            }
            catch (Exception exception)
            {
                lastOperation = $"Reconnect failed: {exception.Message}";
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async void Disconnect()
        {
            activeOperation?.Cancel();
            if (client == null)
            {
                lastOperation = "Client component is missing.";
                return;
            }

            try
            {
                await client.DisconnectAsync();
                lastOperation = "Disconnected.";
            }
            catch (Exception exception)
            {
                lastOperation = $"Disconnect failed: {exception.Message}";
            }
        }

        private async void SendTranscript(bool final)
        {
            var text = transcript?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                lastOperation = "Enter transcript text before sending.";
                return;
            }

            var operation = BeginOperation(final ? "Sending final transcript..." : "Sending partial transcript...");
            if (operation == null)
            {
                return;
            }

            try
            {
                var result = final
                    ? await client.SendFinalTranscriptAsync(text, operation.Token)
                    : await client.SendPartialTranscriptAsync(text, operation.Token);
                lastOperation = FormatSendResult(final, result);
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Send cancelled.";
            }
            catch (Exception exception)
            {
                lastOperation = $"Send failed: {exception.Message}";
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private bool TryValidateCredentials(out Guid parsedAvatarId, out string error)
        {
            parsedAvatarId = Guid.Empty;
            if (client == null)
            {
                error = "Client component is missing.";
                return false;
            }

            if (settings == null)
            {
                error = "Connection settings asset is missing.";
                return false;
            }

            if (!settings.TryGetEndpoint(out _, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                error = "Paste a valid access token.";
                return false;
            }

            if (!Guid.TryParse(avatarId?.Trim(), out parsedAvatarId) || parsedAvatarId == Guid.Empty)
            {
                error = "Enter a valid non-empty avatar UUID.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private CancellationTokenSource BeginOperation(string message)
        {
            if (activeOperation != null)
            {
                lastOperation = "Another operation is already running.";
                return null;
            }

            activeOperation = new CancellationTokenSource();
            lastOperation = message;
            return activeOperation;
        }

        private void EndOperation(CancellationTokenSource operation)
        {
            if (!ReferenceEquals(activeOperation, operation))
            {
                return;
            }

            activeOperation = null;
            operation.Dispose();
        }

        private void OnBehaviorApplied(AvatarBehavior behavior)
        {
            lastAvatarBehavior = behavior.ToWireValue();
        }

        private void OnAiResponseReceived(AiResponsePayload response)
        {
            lastAiResponse = $"[{response.Emotion.ToWireValue()} / {response.Intent.ToWireValue()}] {response.Text}";
        }

        private void OnSystemErrorReceived(SystemErrorPayload error)
        {
            lastSystemError = $"{error.Code}: {error.Message}";
        }

        private static string FormatSendResult(bool final, CohostSendResult result)
        {
            if (result.Succeeded)
            {
                return final
                    ? "Final transcript sent; waiting for ai.response."
                    : "Partial transcript sent.";
            }

            var retry = result.RetryAfter > TimeSpan.Zero
                ? $" Retry after approximately {result.RetryAfter.TotalSeconds:0.0}s."
                : string.Empty;
            return $"Send rejected ({result.Status}): {result.Message}{retry}";
        }

        private static string ValueOrNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            wrappedLabelStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true
            };
        }
    }
}
