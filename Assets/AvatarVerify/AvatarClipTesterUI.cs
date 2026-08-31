using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public sealed class AvatarClipTesterUI : MonoBehaviour
{
    public AvatarClipTester tester;
    public TMP_Text currentNameText;
    public TMP_Text indexText;
    public TMP_Text statusText;
    public TMP_InputField searchField;
    public Button prevButton;
    public Button nextButton;
    public Button replayButton;
    public Button idleButton;
    public Button walkButton;
    public Button runButton;
    public Button jumpButton;
    public Toggle keepInPlaceToggle;
    public RectTransform listContent;
    public Button clipButtonTemplate;

    Button[] _clipButtons;
    Image[] _clipImages;
    Color _normal = new Color(0.16f, 0.18f, 0.22f, 0.96f);
    Color _selected = new Color(0.18f, 0.42f, 0.72f, 1f);
    Color _broken = new Color(0.55f, 0.28f, 0.16f, 0.96f);

    void Awake()
    {
        if (tester == null)
            tester = FindFirstObjectByType<AvatarClipTester>();

        if (clipButtonTemplate != null)
            clipButtonTemplate.gameObject.SetActive(false);

        if (prevButton != null)
            prevButton.onClick.AddListener(() => tester.PlayIndex(tester.currentIndex - 1));
        if (nextButton != null)
            nextButton.onClick.AddListener(() => tester.PlayIndex(tester.currentIndex + 1));
        if (replayButton != null)
            replayButton.onClick.AddListener(() => tester.Replay());
        if (idleButton != null)
            idleButton.onClick.AddListener(() => tester.PlayNamedPrefix("01_Idle_A"));
        if (walkButton != null)
            walkButton.onClick.AddListener(() => tester.PlayNamedPrefix("04_Walk"));
        if (runButton != null)
            runButton.onClick.AddListener(() => tester.PlayNamedPrefix("05_Run"));
        if (jumpButton != null)
            jumpButton.onClick.AddListener(() => tester.PlayNamedPrefix("08_jump"));
        if (searchField != null)
            searchField.onValueChanged.AddListener(_ => ApplyFilter());
        if (keepInPlaceToggle != null && tester != null)
        {
            keepInPlaceToggle.isOn = tester.keepInPlace;
            keepInPlaceToggle.onValueChanged.AddListener(on => tester.keepInPlace = on);
        }
    }

    void OnEnable()
    {
        if (tester != null)
            tester.ClipChanged += OnClipChanged;
    }

    void OnDisable()
    {
        if (tester != null)
            tester.ClipChanged -= OnClipChanged;
    }

    void Start()
    {
        BuildList();
        OnClipChanged(tester != null ? tester.currentIndex : 0);
    }

    void BuildList()
    {
        if (tester == null || listContent == null || clipButtonTemplate == null)
            return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Transform child = listContent.GetChild(i);
            if (child.gameObject == clipButtonTemplate.gameObject)
                continue;
            Destroy(child.gameObject);
        }

        string[] names = tester.stateNames;
        _clipButtons = new Button[names.Length];
        _clipImages = new Image[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            Button button = Instantiate(clipButtonTemplate, listContent);
            button.gameObject.SetActive(true);
            button.name = names[i];
            int index = i;
            button.onClick.AddListener(() => tester.PlayIndex(index));
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = names[i];
            _clipButtons[i] = button;
            _clipImages[i] = button.GetComponent<Image>();
        }

        ApplyFilter();
    }

    void ApplyFilter()
    {
        if (_clipButtons == null || tester == null)
            return;

        string filter = searchField != null && searchField.text != null ? searchField.text.Trim() : string.Empty;
        for (int i = 0; i < _clipButtons.Length; i++)
        {
            bool match = string.IsNullOrEmpty(filter)
                || tester.stateNames[i].IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
            _clipButtons[i].gameObject.SetActive(match);
        }

        RefreshHighlight();
    }

    void OnClipChanged(int index)
    {
        if (tester == null)
            return;

        if (currentNameText != null)
            currentNameText.text = tester.CurrentStateName;
        if (indexText != null)
            indexText.text = (tester.currentIndex + 1) + " / " + tester.stateNames.Length;
        if (statusText != null)
        {
            if (tester.IsCurrentBroken)
            {
                statusText.text = "Hierarchy mismatch — this clip should not move the mesh.";
                statusText.color = new Color(1f, 0.55f, 0.35f);
            }
            else
            {
                statusText.text = "Should play on the character.";
                statusText.color = new Color(0.55f, 0.85f, 0.65f);
            }
        }

        RefreshHighlight();
    }

    void RefreshHighlight()
    {
        if (_clipImages == null || tester == null)
            return;

        for (int i = 0; i < _clipImages.Length; i++)
        {
            if (_clipImages[i] == null)
                continue;
            bool selected = i == tester.currentIndex;
            bool broken = tester.IsExpectedBroken(tester.stateNames[i]);
            _clipImages[i].color = selected ? _selected : (broken ? _broken : _normal);
        }
    }

    public static string BuildInScene()
    {
        var existing = GameObject.Find("AvatarClipTesterCanvas");
        if (existing != null)
            Object.DestroyImmediate(existing);

        var es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }
        else
        {
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null)
                Object.DestroyImmediate(legacy);
        }

        var canvasGo = new GameObject("AvatarClipTesterCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(24f, 0f);
        panel.sizeDelta = new Vector2(420f, -48f);
        panel.offsetMin = new Vector2(24f, 24f);
        panel.offsetMax = new Vector2(444f, -24f);
        panelGo.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.94f);

        var header = MakeText(panel, "Header", "Avatar Clip Tester", 22, FontStyles.Bold, new Color(0.92f, 0.95f, 1f));
        PlaceTop(header.rectTransform, 18f, 16f, 40f);

        var current = MakeText(panel, "CurrentName", "—", 18, FontStyles.Bold, Color.white);
        PlaceTop(current.rectTransform, 18f, 58f, 34f);

        var index = MakeText(panel, "Index", "0 / 0", 14, FontStyles.Normal, new Color(0.7f, 0.76f, 0.86f));
        PlaceTop(index.rectTransform, 18f, 94f, 26f);

        var status = MakeText(panel, "Status", "Should play on the character.", 13, FontStyles.Normal, new Color(0.55f, 0.85f, 0.65f));
        PlaceTop(status.rectTransform, 18f, 120f, 36f);

        var nav = MakeRow(panel, "NavRow");
        PlaceTop(nav, 16f, 162f, 46f);
        var prev = MakeButton(nav, "Prev", "Prev");
        var replay = MakeButton(nav, "Replay", "Replay");
        var next = MakeButton(nav, "Next", "Next");

        var quick = MakeRow(panel, "QuickRow");
        PlaceTop(quick, 16f, 216f, 46f);
        var idle = MakeButton(quick, "Idle", "Idle");
        var walk = MakeButton(quick, "Walk", "Walk");
        var run = MakeButton(quick, "Run", "Run");
        var jump = MakeButton(quick, "Jump", "Jump");

        var stay = MakeToggle(panel, "Stay in place (no travel)");
        PlaceTop(stay.GetComponent<RectTransform>(), 16f, 270f, 32f);

        var search = MakeSearch(panel);
        PlaceTop(search.GetComponent<RectTransform>(), 16f, 308f, 46f);

        RectTransform content;
        Button template;
        var scroll = MakeScroll(panel, out content, out template);
        var scrollRt = scroll.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(16f, 16f);
        scrollRt.offsetMax = new Vector2(-16f, -366f);

        var ui = canvasGo.AddComponent<AvatarClipTesterUI>();
        ui.tester = FindFirstObjectByType<AvatarClipTester>();
        ui.currentNameText = current;
        ui.indexText = index;
        ui.statusText = status;
        ui.searchField = search;
        ui.prevButton = prev;
        ui.nextButton = next;
        ui.replayButton = replay;
        ui.idleButton = idle;
        ui.walkButton = walk;
        ui.runButton = run;
        ui.jumpButton = jump;
        ui.keepInPlaceToggle = stay;
        ui.listContent = content;
        ui.clipButtonTemplate = template;
        return ui.tester != null ? "ok" : "canvas built but tester missing";
    }

    static TMP_Text MakeText(Transform parent, string name, string text, int size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    static RectTransform MakeRow(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        return go.GetComponent<RectTransform>();
    }

    static Button MakeButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.30f, 1f);
        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        var tr = textGo.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        return go.GetComponent<Button>();
    }

    static Toggle MakeToggle(Transform parent, string label)
    {
        var go = new GameObject("KeepInPlace", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.padding = new RectOffset(4, 0, 0, 0);

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(Toggle));
        box.transform.SetParent(go.transform, false);
        var boxLe = box.AddComponent<LayoutElement>();
        boxLe.minWidth = 26f;
        boxLe.preferredWidth = 26f;
        boxLe.minHeight = 26f;
        boxLe.preferredHeight = 26f;
        box.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.30f, 1f);

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(box.transform, false);
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.15f, 0.15f);
        checkRt.anchorMax = new Vector2(0.85f, 0.85f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        check.GetComponent<Image>().color = new Color(0.45f, 0.78f, 1f, 1f);

        var toggle = box.GetComponent<Toggle>();
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = true;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textLe = textGo.AddComponent<LayoutElement>();
        textLe.flexibleWidth = 1f;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.color = new Color(0.82f, 0.86f, 0.92f);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        return toggle;
    }

    static TMP_InputField MakeSearch(Transform parent)
    {
        var go = new GameObject("Search", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);

        var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(go.transform, false);
        var vpRt = viewport.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = new Vector2(12f, 6f);
        vpRt.offsetMax = new Vector2(-12f, -6f);

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(viewport.transform, false);
        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholder.font = TMP_Settings.defaultFontAsset;
        placeholder.text = "Search clips...";
        placeholder.fontSize = 15;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.raycastTarget = false;
        var phRt = placeholderGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = Vector2.zero;
        phRt.offsetMax = Vector2.zero;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(viewport.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 15;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        var tRt = textGo.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero;
        tRt.anchorMax = Vector2.one;
        tRt.offsetMin = Vector2.zero;
        tRt.offsetMax = Vector2.zero;

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = vpRt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = TMP_Settings.defaultFontAsset;
        input.caretColor = Color.white;
        return input;
    }

    static GameObject MakeScroll(Transform parent, out RectTransform content, out Button template)
    {
        var scrollGo = new GameObject("ClipList", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.65f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        var vp = viewport.GetComponent<RectTransform>();
        vp.anchorMin = Vector2.zero;
        vp.anchorMax = Vector2.one;
        vp.offsetMin = Vector2.zero;
        vp.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        content = contentGo.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = vp;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        template = MakeButton(content, "ClipButtonTemplate", "Clip Name");
        template.gameObject.SetActive(false);
        var le = template.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 38f;
        le.preferredHeight = 38f;
        template.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f, 0.96f);
        var label = template.GetComponentInChildren<TextMeshProUGUI>();
        label.fontSize = 14;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(12f, 0f, 8f, 0f);
        return scrollGo;
    }

    static void PlaceTop(RectTransform rt, float side, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-(side * 2f), height);
    }
}
