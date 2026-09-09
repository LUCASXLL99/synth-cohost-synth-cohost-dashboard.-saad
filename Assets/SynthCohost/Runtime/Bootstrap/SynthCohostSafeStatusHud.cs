using SynthCohost.Runtime.Session;
using UnityEngine;

namespace SynthCohost.Runtime.Bootstrap
{
    /// <summary>
    /// Safe on-screen session status for the product scene. Never shows tokens, UUIDs, transcripts, or AI text.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Safe Status HUD")]
    public sealed class SynthCohostSafeStatusHud : MonoBehaviour
    {
        [SerializeField] private SynthCohostClientBehaviour client;
        [SerializeField] private SynthCohostRuntimeBootstrap bootstrap;

        private void Awake()
        {
            client ??= GetComponent<SynthCohostClientBehaviour>();
            bootstrap ??= GetComponent<SynthCohostRuntimeBootstrap>();
        }

        private void OnGUI()
        {
            var status = client != null ? client.Status : null;
            var state = client != null ? client.State.ToString() : "missing";
            if (status != null && status.IsWakingServer)
            {
                state += " (waking)";
            }

            var boot = bootstrap != null ? bootstrap.LastSafeStatus : string.Empty;
            var error = status != null ? status.SanitizedError : string.Empty;
            var inbound = status != null ? status.LastEventType : string.Empty;
            var line =
                $"Synth Cohost  {state}" +
                (string.IsNullOrEmpty(inbound) ? string.Empty : $"  in:{inbound}") +
                (status == null ? string.Empty : $"  hb:{status.HeartbeatSendCount}  err:{status.SanitizedErrorCount}");

            var rect = new Rect(12f, 12f, Mathf.Min(520f, Screen.width - 24f), 54f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 22f), line);
            var detail = string.IsNullOrWhiteSpace(error) ? boot : error;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 22f), detail);
            }
        }
    }
}
