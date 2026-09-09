using System;
using System.Threading;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Bootstrap;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;
using SynthCohost.Runtime.Features.Avatar;
using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    internal static class LiveTestPanelLayout
    {
        internal const float ScreenMargin = 16f;
        internal const float MinimumReadableContentWidth = 220f;
        internal const float ControlHorizontalReserve = 8f;
        internal const float ButtonGap = 6f;
        internal const float StackedButtonThreshold = 390f;
        internal const float CompactPreferredWidth = 360f;
        internal const float ShowButtonWidth = 220f;
        internal const float ShowButtonHeight = 32f;

        internal static Rect CalculateShowButtonRect()
        {
            return new Rect(ScreenMargin, ScreenMargin, ShowButtonWidth, ShowButtonHeight);
        }

        internal static Rect CalculatePanelRect(float preferredWidth, float screenWidth, float screenHeight)
        {
            var availableWidth = Mathf.Max(1f, screenWidth - ScreenMargin * 2f);
            var availableHeight = Mathf.Max(1f, screenHeight - ScreenMargin * 2f);
            var width = Mathf.Min(Mathf.Max(320f, preferredWidth), availableWidth);
            return new Rect(ScreenMargin, ScreenMargin, width, availableHeight);
        }

        internal static float CalculateContentWidth(float panelWidth)
        {
            // Room for the window padding and a vertical scrollbar; every child gets this cap so
            // a long masked JWT or transcript cannot widen the scroll view horizontally.
            return Mathf.Max(1f, panelWidth - 40f);
        }

        internal static bool ShouldStackButtons(float availableContentWidth)
        {
            return availableContentWidth < StackedButtonThreshold;
        }

        internal static float CalculateControlWidth(float contentWidth)
        {
            // IMGUI control styles add horizontal margins outside an explicit Width option.
            // Reserve room for those margins so the scroll-view content never grows sideways.
            return Mathf.Max(1f, contentWidth - ControlHorizontalReserve);
        }

        internal static float CalculateButtonWidth(float availableContentWidth, int buttonCount)
        {
            var count = Mathf.Max(1, buttonCount);
            return Mathf.Max(
                1f,
                (availableContentWidth - ButtonGap * (count - 1)) / count);
        }
    }

    /// <summary>
    /// Development harness for exercising the deployed v2 text flow from the dedicated live-test scene. Credentials
    /// remain private, non-serialized runtime values; the component never writes them to Unity assets or source.
    /// Remove this component from production scenes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Development/Live Test Panel")]
    public sealed class SynthCohostLiveTestPanel : MonoBehaviour
    {
        [Header("Wired by the live-test scene setup")]
        [SerializeField] private SynthCohostClientBehaviour client;
        [SerializeField] private SynthCohostConnectionSettings settings;
        [SerializeField] private LiveTestAvatarBehaviorAdapter avatarAdapter;
        [SerializeField] private DashboardAvatarPresenter dashboardAvatar;
        [SerializeField] private LiveTestAiResponseAdapter aiResponseAdapter;
        [SerializeField] private LiveTestSystemErrorAdapter systemErrorAdapter;

        [Header("Development UI")]
        [SerializeField, Min(420f)] private float panelWidth = 680f;
        [SerializeField] private string defaultTranscript = "Hello from the Unity live test.";

        [NonSerialized] private string endpointUrl = string.Empty;
        [NonSerialized] private string accessToken = string.Empty;
        [NonSerialized] private string avatarId = string.Empty;
        [NonSerialized] private string refreshToken = string.Empty;
        [NonSerialized] private string accountEmail = string.Empty;
        [NonSerialized] private string accountPassword = string.Empty;
        [NonSerialized] private string placeholderSourceSummary = "settings/manual";
        [NonSerialized] private string transcript = string.Empty;
        [NonSerialized] private string lastOperation = "Enter a temporary token and avatar UUID, then click Connect.";
        [NonSerialized] private string lastAvatarBehavior = "(none)";
        [NonSerialized] private string lastAiResponse = "(none)";
        [NonSerialized] private string lastSystemError = "(none)";
        [NonSerialized] private Vector2 scrollPosition;
        [NonSerialized] private CancellationTokenSource activeOperation;
        [NonSerialized] private LiveTestOperationKind? activeOperationKind;
        [NonSerialized] private double activeOperationStartedRealtime;
        [NonSerialized] private int activeProgressNotice;
        [NonSerialized] private LiveTestConnectionDiagnostics connectionDiagnostics;
        [NonSerialized] private GUIStyle titleStyle;
        [NonSerialized] private GUIStyle sectionStyle;
        [NonSerialized] private GUIStyle wrappedLabelStyle;
        [NonSerialized] private float contentWidth = LiveTestPanelLayout.MinimumReadableContentWidth;
        [NonSerialized] private float controlWidth =
            LiveTestPanelLayout.MinimumReadableContentWidth - LiveTestPanelLayout.ControlHorizontalReserve;
        [NonSerialized] private bool panelHidden;
        [NonSerialized] private bool compactPanel;
        [NonSerialized] private bool lockEyesToggle = true;

        private bool IsBusy => activeOperation != null;

        private bool HasTokenRefreshCredentials =>
            !string.IsNullOrWhiteSpace(refreshToken) ||
            (!string.IsNullOrWhiteSpace(accountEmail) &&
             !string.IsNullOrWhiteSpace(accountPassword));

        private void Awake()
        {
            client ??= GetComponent<SynthCohostClientBehaviour>();
            avatarAdapter ??= GetComponent<LiveTestAvatarBehaviorAdapter>();
            dashboardAvatar ??= FindFirstObjectByType<DashboardAvatarPresenter>();
            aiResponseAdapter ??= GetComponent<LiveTestAiResponseAdapter>();
            systemErrorAdapter ??= GetComponent<LiveTestSystemErrorAdapter>();
            transcript = string.IsNullOrWhiteSpace(defaultTranscript)
                ? "Hello from the Unity live test."
                : defaultTranscript;

            var placeholders = LiveTestPlaceholderSource.Load(
                settings != null ? settings.EndpointUrl : string.Empty);
            endpointUrl = placeholders.EndpointUrl;
            accessToken = placeholders.AccessToken;
            avatarId = placeholders.AvatarId;
            refreshToken = placeholders.RefreshToken;
            accountEmail = placeholders.Email;
            accountPassword = placeholders.Password;
            placeholderSourceSummary = placeholders.SourceSummary;
            if (!string.IsNullOrWhiteSpace(placeholders.SafeNotice))
            {
                lastOperation = placeholders.SafeNotice;
            }
            else if (HasTokenRefreshCredentials)
            {
                lastOperation =
                    "Enter or confirm account email/password, click Get access token, then Connect.";
            }
            else if (!string.IsNullOrWhiteSpace(accessToken) ||
                     !string.IsNullOrWhiteSpace(avatarId))
            {
                lastOperation =
                    "Review the loaded values, or enter email/password and click Get access token, then Connect.";
            }
            else
            {
                lastOperation =
                    "Enter account email, password, and avatar UUID. Click Get access token, then Connect.";
            }

            connectionDiagnostics = new LiveTestConnectionDiagnostics(this);
            if (dashboardAvatar != null)
            {
                lockEyesToggle = dashboardAvatar.LockEyes;
            }

            connectionDiagnostics.PanelInitialized(
                client != null &&
                settings != null &&
                avatarAdapter != null &&
                aiResponseAdapter != null &&
                systemErrorAdapter != null,
                !string.IsNullOrWhiteSpace(accessToken),
                !string.IsNullOrWhiteSpace(avatarId));
            if (!string.IsNullOrWhiteSpace(placeholders.SafeNotice))
            {
                connectionDiagnostics.PlaceholderLoadWarning();
            }
        }

        private void OnEnable()
        {
            if (dashboardAvatar != null)
            {
                dashboardAvatar.BehaviorApplied += OnBehaviorApplied;
                dashboardAvatar.ResponseReceived += OnAiResponseReceived;
            }
            else
            {
                if (avatarAdapter != null)
                {
                    avatarAdapter.BehaviorApplied += OnBehaviorApplied;
                }

                if (aiResponseAdapter != null)
                {
                    aiResponseAdapter.ResponseReceived += OnAiResponseReceived;
                }
            }

            if (systemErrorAdapter != null)
            {
                systemErrorAdapter.ErrorReceived += OnSystemErrorReceived;
            }
        }

        private void OnDisable()
        {
            if (dashboardAvatar != null)
            {
                dashboardAvatar.BehaviorApplied -= OnBehaviorApplied;
                dashboardAvatar.ResponseReceived -= OnAiResponseReceived;
            }

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
            activeOperationKind = null;
            activeProgressNotice = 0;
        }

        private void Update()
        {
            if (activeOperation == null ||
                !activeOperationKind.HasValue ||
                (activeOperationKind.Value != LiveTestOperationKind.Connect &&
                 activeOperationKind.Value != LiveTestOperationKind.Reconnect))
            {
                return;
            }

            var elapsedSeconds = Time.realtimeSinceStartupAsDouble - activeOperationStartedRealtime;
            var threshold = activeProgressNotice == 0
                ? 5
                : activeProgressNotice == 1
                    ? 30
                    : activeProgressNotice == 2
                        ? 60
                        : int.MaxValue;
            if (elapsedSeconds < threshold)
            {
                return;
            }

            activeProgressNotice++;
            connectionDiagnostics?.StillWaiting(
                activeOperationKind.Value,
                threshold,
                settings != null ? settings.ConnectTimeout : TimeSpan.FromSeconds(75));
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (panelHidden)
            {
                GUILayout.BeginArea(LiveTestPanelLayout.CalculateShowButtonRect());
                if (GUILayout.Button("Show live-test panel", GUILayout.ExpandHeight(true)))
                {
                    panelHidden = false;
                }

                GUILayout.EndArea();
                return;
            }

            var preferred = compactPanel
                ? LiveTestPanelLayout.CompactPreferredWidth
                : panelWidth;
            var panelRect = LiveTestPanelLayout.CalculatePanelRect(preferred, Screen.width, Screen.height);
            contentWidth = LiveTestPanelLayout.CalculateContentWidth(panelRect.width);
            controlWidth = LiveTestPanelLayout.CalculateControlWidth(contentWidth);
            scrollPosition.x = 0f;

            GUILayout.BeginArea(panelRect, GUI.skin.window);
            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));

            GUILayout.Label("Synth Cohost v2 Live Test", titleStyle);
            GUILayout.Label(
                "Development panel only. Inputs remain editable until Connect; credentials are never serialized into Unity assets. Use Game view 16:9 so the character is visible.",
                wrappedLabelStyle);

            DrawLayoutToggles();
            DrawStatus();
            DrawCredentials();
            DrawConnectionControls();
            DrawTranscriptControls();
            DrawPreviewControls();
            DrawResults();
            DrawActivityLog();

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLayoutToggles()
        {
            GUILayout.Space(4f);
            var previous = GUI.enabled;
            GUILayout.BeginHorizontal(GUILayout.Width(controlWidth));
            var hideWidth = LiveTestPanelLayout.CalculateButtonWidth(controlWidth, 3);
            if (DrawActionButton("Hide panel", true, hideWidth))
            {
                panelHidden = true;
            }

            GUILayout.Space(LiveTestPanelLayout.ButtonGap);
            compactPanel = GUILayout.Toggle(
                compactPanel,
                "Compact",
                GUILayout.Width(hideWidth));
            GUILayout.Space(LiveTestPanelLayout.ButtonGap);
            var eyes = GUILayout.Toggle(
                lockEyesToggle,
                "Lock eyes",
                GUILayout.Width(hideWidth));
            GUILayout.EndHorizontal();
            GUI.enabled = previous;
            if (eyes != lockEyesToggle)
            {
                lockEyesToggle = eyes;
                if (dashboardAvatar != null)
                {
                    dashboardAvatar.LockEyes = lockEyesToggle;
                }
            }
        }

        private void DrawStatus()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Connection", sectionStyle);
            var endpointDisplay = client != null && client.EffectiveEndpoint != null
                ? FormatEndpointForDisplay(client.EffectiveEndpoint)
                : "(client endpoint unavailable)";
            GUILayout.Label(
                $"Composed endpoint: {endpointDisplay}",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));

            var status = client != null ? client.Status : null;
            var state = client != null ? client.State.ToString() : "Client missing";
            if (status != null && status.IsWakingServer)
            {
                state += " (Waking server - a free-tier cold start can take about 50 seconds)";
            }
            else if (status != null && client != null && client.State == SessionState.Ready)
            {
                state += string.IsNullOrEmpty(status.LastEventType)
                    ? " (auth sent; current v2 has no positive session.ready acknowledgement)"
                    : " (backend activity received)";
            }

            GUILayout.Label($"State: {state}", wrappedLabelStyle, GUILayout.Width(controlWidth));
            if (status != null)
            {
                GUILayout.Label(
                    $"Client session allocated: {(status.HasSession ? "yes" : "no")}   " +
                    $"Turn active: {(status.HasActiveTurn ? "yes" : "no")}   " +
                    $"Reconnect attempt: {status.ReconnectAttempt}",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
                GUILayout.Label(
                    $"Last inbound: {FormatActivity(status.LastEventType, status.LastInboundAtUtc)}",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
                GUILayout.Label(
                    $"Last outbound: {FormatActivity(status.LastOutboundEventType, status.LastOutboundAtUtc)}",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
                if (!string.IsNullOrWhiteSpace(status.LastCloseSummary))
                {
                    GUILayout.Label(
                        $"Last close: {status.LastCloseSummary}",
                        wrappedLabelStyle,
                        GUILayout.Width(controlWidth));
                }
                if (!string.IsNullOrWhiteSpace(status.SanitizedError))
                {
                    GUILayout.Label(
                        $"Client error: {status.SanitizedError}",
                        wrappedLabelStyle,
                        GUILayout.Width(controlWidth));
                }
            }

            GUILayout.Label(
                $"Last operation: {lastOperation}",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));
        }

        private void DrawCredentials()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Runtime connection inputs", sectionStyle);
            var previousEnabled = GUI.enabled;
            var state = client != null ? client.State : SessionState.Disconnected;
            var canEditConnectionInputs = previousEnabled && !IsBusy &&
                                          (state == SessionState.Disconnected ||
                                           state == SessionState.AuthRequired ||
                                           state == SessionState.Faulted);
            var canEditAuthInputs = previousEnabled && !IsBusy &&
                                    (state == SessionState.Disconnected ||
                                     state == SessionState.AuthRequired ||
                                     state == SessionState.Faulted ||
                                     state == SessionState.Ready);

            GUI.enabled = canEditConnectionInputs;
            GUILayout.Label("WebSocket endpoint (editable while disconnected)");
            endpointUrl = GUILayout.TextField(
                endpointUrl ?? string.Empty,
                GUILayout.Width(controlWidth));
            GUI.enabled = previousEnabled;

            GUI.enabled = canEditAuthInputs;
            GUILayout.Label("Account email");
            accountEmail = GUILayout.TextField(
                accountEmail ?? string.Empty,
                GUILayout.Width(controlWidth));
            GUILayout.Label("Account password (masked)");
            accountPassword = GUILayout.PasswordField(
                accountPassword ?? string.Empty,
                '*',
                GUILayout.Width(controlWidth));
            GUILayout.Label("Avatar UUID");
            avatarId = GUILayout.TextField(avatarId ?? string.Empty, GUILayout.Width(controlWidth));
            GUI.enabled = previousEnabled;

            var canGetAccessToken = !IsBusy && HasTokenRefreshCredentials &&
                                    (state == SessionState.Disconnected ||
                                     state == SessionState.AuthRequired ||
                                     state == SessionState.Faulted ||
                                     state == SessionState.Ready);
            if (DrawActionButton("Get access token", canGetAccessToken, controlWidth))
            {
                RefreshAccessToken();
            }

            GUILayout.Label(
                HasTokenRefreshCredentials
                    ? "Get access token logs in with the email/password above (or a saved refresh token) and fills Access token."
                    : "Enter account email and password above to enable Get access token. Works in Editor and PC builds.",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));

            GUI.enabled = canEditAuthInputs;
            GUILayout.Label("Access token (masked, filled by Get access token or paste)");
            accessToken = GUILayout.PasswordField(
                accessToken ?? string.Empty,
                '*',
                GUILayout.Width(controlWidth));
            GUI.enabled = previousEnabled;

            GUILayout.Label(
                $"Placeholder source: {placeholderSourceSummary}",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));
            GUILayout.Label(
                $"Token status: {FormatTokenStatus(accessToken)}",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));
        }

        private void DrawConnectionControls()
        {
            var state = client != null ? client.State : SessionState.Disconnected;
            var canConnect = !IsBusy && client != null &&
                             (state == SessionState.Disconnected ||
                              state == SessionState.AuthRequired ||
                              state == SessionState.Faulted);
            var canReconnect = !IsBusy && client != null && state == SessionState.Ready;
            var canDisconnect = client != null && (IsBusy || state != SessionState.Disconnected);

            if (LiveTestPanelLayout.ShouldStackButtons(controlWidth))
            {
                if (DrawActionButton("Connect", canConnect, controlWidth)) Connect();
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton("Reconnect", canReconnect, controlWidth)) Reconnect();
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton(IsBusy ? "Cancel / Disconnect" : "Disconnect", canDisconnect, controlWidth)) Disconnect();
            }
            else
            {
                var buttonWidth = LiveTestPanelLayout.CalculateButtonWidth(controlWidth, 3);
                GUILayout.BeginHorizontal(GUILayout.Width(controlWidth));
                if (DrawActionButton("Connect", canConnect, buttonWidth)) Connect();
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton("Reconnect", canReconnect, buttonWidth)) Reconnect();
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton(IsBusy ? "Cancel / Disconnect" : "Disconnect", canDisconnect, buttonWidth)) Disconnect();
                GUILayout.EndHorizontal();
            }

            if (state == SessionState.AuthRequired)
            {
                GUILayout.Label(
                    "Authentication was rejected. Click Get access token for a fresh token, then Connect. Reconnect will not reuse the rejected token.",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
            }
        }

        private void DrawTranscriptControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Transcript", sectionStyle);
            transcript = GUILayout.TextArea(
                transcript ?? string.Empty,
                GUILayout.Width(controlWidth),
                GUILayout.MinHeight(70f));

            var canSend = !IsBusy && client != null &&
                          client.State == SessionState.Ready &&
                          !(client.Status?.HasActiveTurn ?? false);
            if (LiveTestPanelLayout.ShouldStackButtons(controlWidth))
            {
                if (DrawActionButton("Send Partial", canSend, controlWidth)) SendTranscript(false);
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton("Send Final", canSend, controlWidth)) SendTranscript(true);
            }
            else
            {
                var buttonWidth = LiveTestPanelLayout.CalculateButtonWidth(controlWidth, 2);
                GUILayout.BeginHorizontal(GUILayout.Width(controlWidth));
                if (DrawActionButton("Send Partial", canSend, buttonWidth)) SendTranscript(false);
                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton("Send Final", canSend, buttonWidth)) SendTranscript(true);
                GUILayout.EndHorizontal();
            }

            GUILayout.Label(
                "Only one final turn can be active at a time.",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));
        }

        private void DrawPreviewControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Local preview (no socket, no state.ack)", sectionStyle);
            GUILayout.Label(
                "Use these to see thinking / smile / cheer on the character while the live AI mock is failing.",
                wrappedLabelStyle,
                GUILayout.Width(controlWidth));

            DrawPreviewRow(AvatarBehavior.Idle, AvatarBehavior.Listening, AvatarBehavior.Thinking);
            GUILayout.Space(LiveTestPanelLayout.ButtonGap);
            DrawPreviewRow(AvatarBehavior.Speaking, AvatarBehavior.Happy, AvatarBehavior.Celebrate);

            if (dashboardAvatar != null && !string.IsNullOrWhiteSpace(dashboardAvatar.DebugOverlay))
            {
                GUILayout.Label(
                    $"Animator: {dashboardAvatar.DebugOverlay}",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
            }
            else if (dashboardAvatar != null && !string.IsNullOrWhiteSpace(dashboardAvatar.CurrentAnimatorStateName))
            {
                GUILayout.Label(
                    $"Animator state: {dashboardAvatar.CurrentAnimatorStateName}",
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
            }
        }

        private void DrawPreviewRow(
            AvatarBehavior first,
            AvatarBehavior second,
            AvatarBehavior third)
        {
            var canPreview = dashboardAvatar != null && !IsBusy;
            if (LiveTestPanelLayout.ShouldStackButtons(controlWidth))
            {
                if (DrawActionButton(first.ToWireValue(), canPreview, controlWidth))
                {
                    PreviewBehavior(first);
                }

                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton(second.ToWireValue(), canPreview, controlWidth))
                {
                    PreviewBehavior(second);
                }

                GUILayout.Space(LiveTestPanelLayout.ButtonGap);
                if (DrawActionButton(third.ToWireValue(), canPreview, controlWidth))
                {
                    PreviewBehavior(third);
                }

                return;
            }

            var buttonWidth = LiveTestPanelLayout.CalculateButtonWidth(controlWidth, 3);
            GUILayout.BeginHorizontal(GUILayout.Width(controlWidth));
            if (DrawActionButton(first.ToWireValue(), canPreview, buttonWidth))
            {
                PreviewBehavior(first);
            }

            GUILayout.Space(LiveTestPanelLayout.ButtonGap);
            if (DrawActionButton(second.ToWireValue(), canPreview, buttonWidth))
            {
                PreviewBehavior(second);
            }

            GUILayout.Space(LiveTestPanelLayout.ButtonGap);
            if (DrawActionButton(third.ToWireValue(), canPreview, buttonWidth))
            {
                PreviewBehavior(third);
            }

            GUILayout.EndHorizontal();
        }

        private async void PreviewBehavior(AvatarBehavior behavior)
        {
            if (dashboardAvatar == null)
            {
                lastOperation = "Dashboard presenter is missing; cannot preview.";
                return;
            }

            connectionDiagnostics?.PreviewRequested(behavior);
            try
            {
                var applied = await dashboardAvatar.PreviewLocalAsync(behavior, CancellationToken.None);
                lastOperation = applied
                    ? $"Local preview applied ({behavior.ToWireValue()}); no state.ack was sent."
                    : $"Local preview failed ({behavior.ToWireValue()}); Animator state missing.";
                if (applied)
                {
                    lastAvatarBehavior = behavior.ToWireValue();
                }
            }
            catch (Exception exception)
            {
                lastOperation = $"Local preview failed ({exception.GetType().Name}).";
                connectionDiagnostics?.OperationFailed(LiveTestOperationKind.Preview, exception);
            }
        }

        private void DrawResults()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Received", sectionStyle);
            GUILayout.Label($"Avatar behavior (dashboard presenter): {lastAvatarBehavior}", wrappedLabelStyle, GUILayout.Width(controlWidth));
            GUILayout.Label($"AI response: {lastAiResponse}", wrappedLabelStyle, GUILayout.Width(controlWidth));
            GUILayout.Label($"System error: {lastSystemError}", wrappedLabelStyle, GUILayout.Width(controlWidth));
        }

        private void DrawActivityLog()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Activity log (also written to Console)", sectionStyle);
            var entries = connectionDiagnostics?.Snapshot();
            if (entries == null || entries.Count == 0)
            {
                GUILayout.Label("(none)", wrappedLabelStyle, GUILayout.Width(controlWidth));
                return;
            }

            foreach (var entry in entries)
            {
                GUILayout.Label(
                    entry.ToDisplayLine(),
                    wrappedLabelStyle,
                    GUILayout.Width(controlWidth));
            }
        }

        private async void RefreshAccessToken()
        {
            connectionDiagnostics?.OperationRequested(
                LiveTestOperationKind.RefreshToken,
                client != null ? client.State : SessionState.Disconnected);

            if (!HasTokenRefreshCredentials)
            {
                lastOperation =
                    "Enter account email and password above, then click Get access token.";
                connectionDiagnostics?.InputRejected(LiveTestInputFailure.MissingRefreshCredentials);
                return;
            }

            if (!LiveTestAccessTokenRefresher.TryBuildRestBaseUri(
                    endpointUrl,
                    out var restBase,
                    out var endpointError))
            {
                lastOperation = endpointError;
                connectionDiagnostics?.InputRejected(LiveTestInputFailure.InvalidEndpoint);
                return;
            }

            var operation = BeginOperation(
                LiveTestOperationKind.RefreshToken,
                "Getting a fresh access token from the live auth API...");
            if (operation == null)
            {
                return;
            }

            try
            {
                var result = await LiveTestAccessTokenRefresher.RefreshAsync(
                    restBase,
                    refreshToken,
                    accountEmail,
                    accountPassword,
                    operation.Token);

                if (!result.Succeeded)
                {
                    lastOperation = result.SafeError;
                    connectionDiagnostics?.TokenRefreshRejected();
                    return;
                }

                accessToken = result.AccessToken;
                if (!string.IsNullOrWhiteSpace(result.RefreshToken))
                {
                    refreshToken = result.RefreshToken;
                }

                if (client != null &&
                    !string.IsNullOrWhiteSpace(refreshToken) &&
                    Guid.TryParse(avatarId?.Trim(), out var parsedAvatar) &&
                    parsedAvatar != Guid.Empty)
                {
                    client.SetAuthSession(accessToken, refreshToken, parsedAvatar);
                }

                if (!LiveTestPlaceholderSource.TrySaveLocalDraft(
                        endpointUrl,
                        accessToken,
                        avatarId,
                        refreshToken,
                        accountEmail,
                        accountPassword,
                        out var saveError))
                {
                    lastOperation =
                        $"Fresh access token loaded into the panel, but credentials were not saved for next launch. {saveError}";
                    connectionDiagnostics?.OperationCompleted(
                        LiveTestOperationKind.RefreshToken,
                        client != null ? client.State : SessionState.Disconnected);
                    return;
                }

                placeholderSourceSummary = Application.isEditor
                    ? "Editor UserSettings file"
                    : "persistent data credentials file";
                lastOperation =
                    result.Method == LiveTestTokenRefreshMethod.RefreshToken
                        ? $"Access token ready via saved refresh token. {FormatTokenStatus(accessToken)} Click Connect."
                        : $"Access token ready via email/password login. {FormatTokenStatus(accessToken)} Click Connect.";
                connectionDiagnostics?.OperationCompleted(
                    LiveTestOperationKind.RefreshToken,
                    client != null ? client.State : SessionState.Disconnected);
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Token refresh cancelled.";
                connectionDiagnostics?.OperationCancelled(LiveTestOperationKind.RefreshToken);
            }
            catch (Exception exception)
            {
                lastOperation =
                    $"Token refresh failed ({exception.GetType().Name}). Check the safe Console diagnostics.";
                connectionDiagnostics?.OperationFailed(LiveTestOperationKind.RefreshToken, exception);
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async void Connect()
        {
            connectionDiagnostics?.OperationRequested(
                LiveTestOperationKind.Connect,
                client != null ? client.State : SessionState.Disconnected);
            if (!TryValidateConnectionInputs(
                    out var parsedEndpoint,
                    out var parsedAccessToken,
                    out var parsedAvatarId,
                    out var validationFailure,
                    out var validationError))
            {
                lastOperation = validationError;
                connectionDiagnostics?.InputRejected(validationFailure);
                return;
            }

            var operation = BeginOperation(
                LiveTestOperationKind.Connect,
                "Connecting. Keep Play Mode running during a possible cold start...");
            if (operation == null)
            {
                return;
            }

            try
            {
                connectionDiagnostics?.ConnectStarting(parsedEndpoint, settings.ConnectTimeout);
                if (!client.TrySetRuntimeEndpoint(parsedEndpoint.AbsoluteUri, out var endpointError))
                {
                    lastOperation = endpointError;
                    connectionDiagnostics?.EndpointOverrideRejected();
                    return;
                }

                connectionDiagnostics?.RuntimeEndpointApplied();
                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    client.SetAuthSession(parsedAccessToken, refreshToken, parsedAvatarId);
                }
                else
                {
                    client.SetRuntimeCredentials(parsedAccessToken, parsedAvatarId);
                }

                connectionDiagnostics?.CredentialsStaged();
                accessToken = string.Empty;
                lastSystemError = "(none)";
                await client.ConnectAsync(operation.Token);
                lastOperation = client.State == SessionState.Ready
                    ? "WebSocket opened and auth was sent. Ready is provisional because current v2 has no session.ready response."
                    : $"Connection attempt completed; current state is {client.State}. Use State as the source of truth.";
                connectionDiagnostics?.OperationCompleted(LiveTestOperationKind.Connect, client.State);
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Connection cancelled.";
                connectionDiagnostics?.OperationCancelled(LiveTestOperationKind.Connect);
            }
            catch (Exception exception)
            {
                lastOperation = $"Connect failed ({exception.GetType().Name}). Check the safe Console diagnostics.";
                connectionDiagnostics?.OperationFailed(LiveTestOperationKind.Connect, exception);
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async void Reconnect()
        {
            connectionDiagnostics?.OperationRequested(
                LiveTestOperationKind.Reconnect,
                client != null ? client.State : SessionState.Disconnected);
            if (client == null)
            {
                lastOperation = "Client component is missing.";
                connectionDiagnostics?.InputRejected(LiveTestInputFailure.MissingClient);
                return;
            }

            if (client.State == SessionState.AuthRequired)
            {
                lastOperation =
                    "Authentication required. Click Get access token, then Connect. Reconnect will not reuse rejected credentials.";
                connectionDiagnostics?.ReconnectRequiresFreshCredentials();
                return;
            }

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                if (!TryValidateConnectionInputs(
                        out _,
                        out var parsedAccessToken,
                        out var parsedAvatarId,
                        out var validationFailure,
                        out var validationError))
                {
                    lastOperation = validationError;
                    connectionDiagnostics?.InputRejected(validationFailure);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    client.SetAuthSession(parsedAccessToken, refreshToken, parsedAvatarId);
                }
                else
                {
                    client.SetRuntimeCredentials(parsedAccessToken, parsedAvatarId);
                }

                connectionDiagnostics?.CredentialsStaged();
                accessToken = string.Empty;
                lastSystemError = "(none)";
            }

            var operation = BeginOperation(
                LiveTestOperationKind.Reconnect,
                "Reconnecting with the current runtime credentials...");
            if (operation == null)
            {
                return;
            }

            try
            {
                connectionDiagnostics?.ConnectStarting(
                    client.EffectiveEndpoint,
                    settings != null ? settings.ConnectTimeout : TimeSpan.FromSeconds(75));
                await client.ReconnectAsync(operation.Token);
                lastOperation = client.State == SessionState.Ready
                    ? "Reconnected with a fresh session ID and sent auth; Ready remains provisional until backend activity."
                    : $"Reconnect completed with state {client.State}.";
                connectionDiagnostics?.OperationCompleted(LiveTestOperationKind.Reconnect, client.State);
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Reconnect cancelled.";
                connectionDiagnostics?.OperationCancelled(LiveTestOperationKind.Reconnect);
            }
            catch (Exception exception)
            {
                lastOperation = $"Reconnect failed ({exception.GetType().Name}). Check the safe Console diagnostics.";
                connectionDiagnostics?.OperationFailed(LiveTestOperationKind.Reconnect, exception);
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async void Disconnect()
        {
            connectionDiagnostics?.OperationRequested(
                LiveTestOperationKind.Disconnect,
                client != null ? client.State : SessionState.Disconnected);
            activeOperation?.Cancel();
            if (client == null)
            {
                lastOperation = "Client component is missing.";
                connectionDiagnostics?.InputRejected(LiveTestInputFailure.MissingClient);
                return;
            }

            try
            {
                await client.DisconnectAsync();
                lastOperation = "Disconnected.";
                connectionDiagnostics?.OperationCompleted(LiveTestOperationKind.Disconnect, client.State);
            }
            catch (Exception exception)
            {
                lastOperation = $"Disconnect failed ({exception.GetType().Name}). Check the safe Console diagnostics.";
                connectionDiagnostics?.OperationFailed(LiveTestOperationKind.Disconnect, exception);
            }
        }

        private async void SendTranscript(bool final)
        {
            var operationKind = final
                ? LiveTestOperationKind.SendFinal
                : LiveTestOperationKind.SendPartial;
            connectionDiagnostics?.OperationRequested(
                operationKind,
                client != null ? client.State : SessionState.Disconnected);
            var text = transcript?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                lastOperation = "Enter transcript text before sending.";
                connectionDiagnostics?.TranscriptMissing(final);
                return;
            }

            var operation = BeginOperation(
                operationKind,
                final ? "Sending final transcript..." : "Sending partial transcript...");
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
                connectionDiagnostics?.SendCompleted(final, result);
            }
            catch (OperationCanceledException)
            {
                lastOperation = "Send cancelled.";
                connectionDiagnostics?.OperationCancelled(operationKind);
            }
            catch (Exception exception)
            {
                lastOperation = $"Send failed ({exception.GetType().Name}). Check the safe Console diagnostics.";
                connectionDiagnostics?.OperationFailed(operationKind, exception);
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private bool TryValidateConnectionInputs(
            out Uri parsedEndpoint,
            out string parsedAccessToken,
            out Guid parsedAvatarId,
            out LiveTestInputFailure failure,
            out string error)
        {
            parsedEndpoint = null;
            parsedAccessToken = string.Empty;
            parsedAvatarId = Guid.Empty;
            failure = LiveTestInputFailure.None;
            if (client == null)
            {
                failure = LiveTestInputFailure.MissingClient;
                error = "Client component is missing.";
                return false;
            }

            if (settings == null)
            {
                failure = LiveTestInputFailure.MissingSettings;
                error = "Connection settings asset is missing.";
                return false;
            }

            if (!SynthCohostConnectionSettings.TryParseEndpoint(
                    endpointUrl,
                    out parsedEndpoint,
                    out error))
            {
                failure = LiveTestInputFailure.InvalidEndpoint;
                return false;
            }

            parsedAccessToken = accessToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parsedAccessToken))
            {
                failure = LiveTestInputFailure.MissingToken;
                error = "Paste a valid access token.";
                return false;
            }

            if (JwtAccessTokenInspector.TryGetExpirationUtc(
                    parsedAccessToken,
                    out var expirationUtc))
            {
                var now = DateTimeOffset.UtcNow;
                if (expirationUtc <= now)
                {
                    failure = LiveTestInputFailure.ExpiredToken;
                    error =
                        $"Access token expired at {expirationUtc:HH:mm:ss} UTC. Click Get access token.";
                    return false;
                }

                if (expirationUtc - now < settings.ConnectTimeout + TimeSpan.FromSeconds(10))
                {
                    failure = LiveTestInputFailure.ExpiringSoonToken;
                    error =
                        "Access token expires too soon for a possible backend cold start. Click Get access token.";
                    return false;
                }
            }

            if (!Guid.TryParse(avatarId?.Trim(), out parsedAvatarId) || parsedAvatarId == Guid.Empty)
            {
                failure = LiveTestInputFailure.InvalidAvatar;
                error = "Enter a valid non-empty avatar UUID.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private CancellationTokenSource BeginOperation(
            LiveTestOperationKind operationKind,
            string message)
        {
            if (activeOperation != null)
            {
                lastOperation = "Another operation is already running.";
                connectionDiagnostics?.OperationBusy();
                return null;
            }

            activeOperation = new CancellationTokenSource();
            activeOperationKind = operationKind;
            activeOperationStartedRealtime = Time.realtimeSinceStartupAsDouble;
            activeProgressNotice = 0;
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
            activeOperationKind = null;
            activeProgressNotice = 0;
            operation.Dispose();
        }

        private void OnBehaviorApplied(AvatarBehavior behavior)
        {
            lastAvatarBehavior = behavior.ToWireValue();
            connectionDiagnostics?.AvatarBehaviorReceived(behavior);
        }

        private void OnAiResponseReceived(AiResponsePayload response)
        {
            lastAiResponse = $"[{response.Emotion.ToWireValue()} / {response.Intent.ToWireValue()}] {response.Text}";
            connectionDiagnostics?.AiResponseReceived(response.Emotion, response.Intent);
        }

        private void OnSystemErrorReceived(SystemErrorPayload error)
        {
            connectionDiagnostics?.SystemErrorReceived(error?.Code);
            if (ProtocolSystemErrorCodes.IsAuthenticationFailure(error?.Code))
            {
                lastSystemError =
                    "AUTH_FAILED: Authentication rejected. The backend message was hidden because it may contain credential details.";
                lastOperation = "Authentication rejected. Click Get access token, then Connect.";
                return;
            }

            var code = ProtocolSystemErrorCodes.ToDiagnosticLabel(error?.Code);
            var message = SanitizeForDisplay(error?.Message, 240);
            lastSystemError = $"{code}: {message}";
        }

        private static bool DrawActionButton(string label, bool enabled, float width)
        {
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            var clicked = GUILayout.Button(
                label,
                GUILayout.Width(width),
                GUILayout.Height(30f));
            GUI.enabled = previousEnabled;
            return clicked;
        }

        private static string FormatActivity(string eventType, DateTimeOffset? timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return "(none)";
            }

            return timestampUtc.HasValue
                ? $"{eventType} at {timestampUtc.Value:HH:mm:ss} UTC"
                : eventType;
        }

        internal static string FormatTokenStatus(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "not entered";
            }

            if (!JwtAccessTokenInspector.TryGetExpirationUtc(token, out var expirationUtc))
            {
                return "present; expiry is unavailable";
            }

            var remaining = expirationUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return $"expired at {expirationUtc:HH:mm:ss} UTC - click Get access token";
            }

            return $"expires at {expirationUtc:HH:mm:ss} UTC " +
                   $"(about {Math.Max(1, Math.Ceiling(remaining.TotalMinutes)):0} min remaining)";
        }

        internal static string FormatEndpointForDisplay(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri)
            {
                return "(invalid endpoint)";
            }

            var host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.Host}]"
                : endpoint.IdnHost;
            var port = endpoint.IsDefaultPort ? string.Empty : $":{endpoint.Port}";
            var path = string.IsNullOrEmpty(endpoint.AbsolutePath) ? "/" : endpoint.AbsolutePath;
            return $"{endpoint.Scheme}://{host}{port}{path}";
        }

        internal static string SanitizeForDisplay(string value, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(none)";
            }

            var limit = Math.Max(1, maximumCharacters);
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (char.IsControl(characters[index]))
                {
                    characters[index] = ' ';
                }
            }

            var sanitized = new string(characters).Trim();
            return sanitized.Length <= limit
                ? sanitized
                : sanitized.Substring(0, limit) + "...";
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
