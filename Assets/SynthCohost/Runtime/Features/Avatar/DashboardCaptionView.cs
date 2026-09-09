using UnityEngine;
using UnityEngine.UI;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Screen-space caption used when the live-test IMGUI panel is hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DashboardCaptionView : MonoBehaviour
    {
        [SerializeField] private Text captionText;

        public string CurrentText => captionText != null ? captionText.text : string.Empty;

        public void SetCaption(string value)
        {
            if (captionText == null)
            {
                return;
            }

            var text = value ?? string.Empty;
            captionText.text = text;
            captionText.enabled = !string.IsNullOrWhiteSpace(text);
        }

        public static DashboardCaptionView Ensure(Transform owner)
        {
            if (owner == null)
            {
                return null;
            }

            var existing = owner.GetComponentInChildren<DashboardCaptionView>(true);
            if (existing != null)
            {
                return existing;
            }

            var canvasGo = new GameObject("DashboardCaptionCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(owner, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            canvasGo.AddComponent<CanvasScaler>();

            var textGo = new GameObject("Caption", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            var rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.15f, 0.04f);
            rect.anchorMax = new Vector2(0.85f, 0.16f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = string.Empty;
            text.enabled = false;

            var view = canvasGo.AddComponent<DashboardCaptionView>();
            view.captionText = text;
            return view;
        }
    }
}
