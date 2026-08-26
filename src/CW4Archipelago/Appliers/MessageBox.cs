using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CW4Archipelago.Appliers;

/// <summary>One colored run of text within a message line (AP palette part).</summary>
public sealed class MsgSpan
{
    public string Text;
    public string Hex;   // "RRGGBB"
    public MsgSpan(string text, string hex) { Text = text; Hex = hex; }
}

/// <summary>
/// Scrollable Archipelago message log shown during a mission, anchored in the
/// bottom-left above the HUD readout cluster (terrain height / creeper coverage
/// / progression), matching its combined width. Colors each part with the AP
/// dark palette, scrolls via a scrollbar + up/down buttons (no wheel). It reads
/// the cluster's live on-screen rect each frame (world corners through the
/// UICanvas camera, mapped into the overlay canvas), so it scales with both the
/// window size and the in-game UI Scale setting. Replaces the old fading toasts.
/// History lives in ModCore and survives scene changes.
/// </summary>
public sealed class ApMessageBox
{
    private const int MaxLines = 200;
    private const float HeaderHeight = 20f;    // local units (matches header child)

    // Box geometry in 1080p-reference pixels (multiplied by the live UI scale).
    // WidthRef ~= the combined width of the bottom-left HUD readout cluster
    // (terrain height / creeper coverage / progression), measured via hud:dump.
    // Mutable so they can be tuned live via the msgbox:set debug command; the
    // final values are baked back into these defaults.
    public static float WidthRef = 332f;       // matches the bottom-left readout cluster width
    public static float BaseHeightRef = 170f;  // box height
    public static float LeftInsetRef = 0f;     // flush to the left screen edge
    public static float BottomInsetRef = 126f; // box bottom, rests on top of the readout cluster
    public static float BgAlpha = 0.5f;        // panel background transparency

    private GameObject? _canvasGo;
    private RectTransform? _containerRt;    // full-screen container (pivot bottom-left)
    private GameObject? _panel;
    private RectTransform? _panelRt;
    private Image? _panelImg;
    private RectTransform? _content;
    private ScrollRect? _scroll;
    private GameObject? _body;              // everything but the header (for collapse)
    private TMP_FontAsset? _font;
    private bool _collapsed;
    private bool _autoScroll = true;
    private int _geomCountdown;

    public void LateTick(string scene)
    {
        if (scene != "Game")
        {
            if (_panel != null) Teardown();
            return;
        }
        if (_panel == null || !IsAlive())
        {
            TryBuild();
            return;
        }
        // Re-track the HUD cluster periodically so the box follows window
        // resizes and UI Scale changes without a rebuild.
        if (--_geomCountdown <= 0)
        {
            _geomCountdown = 10;
            UpdateGeometry();
        }
    }

    /// <summary>Append one line (called on the main thread from ModCore).</summary>
    public void AppendLine(IReadOnlyList<MsgSpan> spans)
    {
        if (_content == null || !IsAlive())
            return;
        AddLineObject(spans);
        TrimLines();
        if (_autoScroll)
            ScrollToBottom();
    }

    /// <summary>Rebuild all lines from history (on first build in a mission).</summary>
    public void RenderHistory(IReadOnlyList<IReadOnlyList<MsgSpan>> history)
    {
        if (_content == null) return;
        foreach (var line in history)
            AddLineObject(line);
        TrimLines();
        ScrollToBottom();
    }

    // ---- build ----

    private bool IsAlive()
    {
        try { return _panel != null && _panel.transform != null; }
        catch { return false; }
    }

    private void TryBuild()
    {
        // Wait until the mission HUD canvas exists.
        if (FindCanvas("UICanvas") == null) return;

        // Host on AchievementCanvas - the overlay canvas where our other custom
        // UI (login panel, toasts) verifiably renders. UICanvas (ScreenSpaceCamera)
        // does not render mod-added children, and a self-created canvas doesn't
        // render at all in this game.
        Canvas host = null!;
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (cv != null && cv.gameObject.name == "AchievementCanvas") { host = cv; break; }
        if (host == null)
            foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (cv != null && cv.isRootCanvas && cv.renderMode == RenderMode.ScreenSpaceOverlay) { host = cv; break; }
        if (host == null) return;

        if (_font == null)
            _font = FindGameFont();

        // Full-screen stretch container (pivot bottom-left so screen->local maps
        // cleanly), then the box panel as a bottom-left-anchored child. Exact
        // size/position are set every frame by UpdateGeometry from the live HUD.
        _canvasGo = new GameObject("CW4ApMessageBoxRoot");
        _canvasGo.transform.SetParent(host.transform, false);
        _canvasGo.transform.SetAsLastSibling();
        _containerRt = _canvasGo.AddComponent<RectTransform>();
        _containerRt.anchorMin = Vector2.zero; _containerRt.anchorMax = Vector2.one;
        _containerRt.pivot = Vector2.zero;
        _containerRt.offsetMin = Vector2.zero; _containerRt.offsetMax = Vector2.zero;

        _panel = new GameObject("CW4ApMessageBox");
        _panel.transform.SetParent(_canvasGo.transform, false);
        var prt = _panel.AddComponent<RectTransform>();
        _panelRt = prt;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.zero;
        prt.pivot = Vector2.zero;
        prt.sizeDelta = new Vector2(300f, 170f);
        prt.anchoredPosition = new Vector2(12f, 120f);
        _panelImg = _panel.AddComponent<Image>();
        _panelImg.color = new Color(0.03f, 0.05f, 0.09f, BgAlpha);

        // Header (title + collapse button)
        var header = MakePanelChild("Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        var hrt = header.GetComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(0f, 20f);
        hrt.anchoredPosition = new Vector2(0f, 0f);
        var hbg = header.AddComponent<Image>();
        hbg.color = new Color(0.08f, 0.12f, 0.2f, 0.9f);
        var title = MakeText(header.transform, "Archipelago", 12f, new Color(0.7f, 0.85f, 1f, 1f));
        Stretch(title.GetComponent<RectTransform>(), 6f, 0f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        var collapseBtn = MakeButton(header.transform, "_", new Color(0.15f, 0.2f, 0.3f, 1f));
        var cbr = collapseBtn.GetComponent<RectTransform>();
        cbr.anchorMin = new Vector2(1f, 0.5f); cbr.anchorMax = new Vector2(1f, 0.5f); cbr.pivot = new Vector2(1f, 0.5f);
        cbr.sizeDelta = new Vector2(20f, 18f); cbr.anchoredPosition = new Vector2(-2f, 0f);
        collapseBtn.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleCollapse);

        // Body: viewport + content (scroll) + scrollbar + up/down buttons
        _body = MakePanelChild("Body", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        var brt = _body.GetComponent<RectTransform>();
        brt.offsetMin = new Vector2(0f, 0f); brt.offsetMax = new Vector2(0f, -20f);   // below header

        _scroll = _body.AddComponent<ScrollRect>();
        _scroll.horizontal = false; _scroll.vertical = true; _scroll.scrollSensitivity = 0f;   // no wheel
        _scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(_body.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(2f, 2f); vrt.offsetMax = new Vector2(-16f, -2f);   // room for scrollbar
        viewport.AddComponent<RectMask2D>();
        var vimg = viewport.AddComponent<Image>();
        vimg.color = new Color(0f, 0f, 0f, 0.25f);

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _content = content.AddComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero; _content.sizeDelta = new Vector2(0f, 0f);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 1f; vlg.padding = new RectOffset(3, 3, 2, 2);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scroll.viewport = vrt;
        _scroll.content = _content;

        // Vertical scrollbar on the right edge of the body
        var sbGo = new GameObject("Scrollbar");
        sbGo.transform.SetParent(_body.transform, false);
        var sbrt = sbGo.AddComponent<RectTransform>();
        sbrt.anchorMin = new Vector2(1f, 0f); sbrt.anchorMax = new Vector2(1f, 1f); sbrt.pivot = new Vector2(1f, 0.5f);
        sbrt.sizeDelta = new Vector2(12f, 0f); sbrt.anchoredPosition = Vector2.zero;
        var sbBg = sbGo.AddComponent<Image>();
        sbBg.color = new Color(0.1f, 0.14f, 0.22f, 0.9f);
        var scrollbar = sbGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(sbGo.transform, false);
        var hnd = handleGo.AddComponent<Image>();
        hnd.color = new Color(0.4f, 0.55f, 0.75f, 1f);
        var hndRt = handleGo.GetComponent<RectTransform>();
        hndRt.anchorMin = Vector2.zero; hndRt.anchorMax = Vector2.one; hndRt.sizeDelta = Vector2.zero;
        scrollbar.targetGraphic = hnd;
        scrollbar.handleRect = hndRt;
        _scroll.verticalScrollbar = scrollbar;
        _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        // Track whether the user scrolled away from the bottom (pause auto-scroll)
        scrollbar.onValueChanged.AddListener((UnityEngine.Events.UnityAction<float>)(v =>
        {
            _autoScroll = v <= 0.02f;   // BottomToTop: 0 == bottom
        }));

        ModCore.Log.LogInfo($"MSGBOX: built on '{host.gameObject.name}'");

        UpdateGeometry();
        ApplyCollapsed();
        RenderHistory(ModCore.MessageHistory);
    }

    // Screen-space rect of a RectTransform via TransformPoint (the Vector3[]
    // GetWorldCorners marshalling returns zeros under IL2CPP).
    private static bool ScreenRect(RectTransform rt, Camera? cam, out Vector2 bl, out Vector2 tr)
    {
        bl = Vector2.zero; tr = Vector2.zero;
        if (rt == null) return false;
        var r = rt.rect;
        var wbl = rt.TransformPoint(new Vector3(r.xMin, r.yMin, 0f));
        var wtr = rt.TransformPoint(new Vector3(r.xMax, r.yMax, 0f));
        bl = RectTransformUtility.WorldToScreenPoint(cam, wbl);
        tr = RectTransformUtility.WorldToScreenPoint(cam, wtr);
        return true;
    }

    private static Canvas? FindCanvas(string name)
    {
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (cv != null && cv.gameObject.name == name) return cv;
        return null;
    }

    /// <summary>
    /// Size and place the box in the bottom-left using reference dimensions
    /// scaled by the live UI scale. The scale comes from the always-present
    /// BOTTOM corner container (100 reference units -> N screen px), so it
    /// tracks both window size and the in-game UI Scale setting. Reference dims
    /// are used instead of literally tracking the readout rects because those
    /// toggle visibility at runtime (progression/emit only show sometimes),
    /// which would make the box jitter. Everything is computed in screen pixels
    /// then mapped into the overlay canvas.
    /// </summary>
    private void UpdateGeometry()
    {
        if (_panel == null || _containerRt == null) return;
        var ui = FindCanvas("UICanvas");
        if (ui == null) return;
        Camera? cam = ui.renderMode == RenderMode.ScreenSpaceOverlay ? null : ui.worldCamera;

        // UI scale from the BOTTOM corner (a stable 100x100 reference square).
        float scale = 0f;
        foreach (var rt in ui.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.gameObject.name == "BOTTOM")
            {
                if (ScreenRect(rt, cam, out var b0, out var t0))
                    scale = Mathf.Max(0.25f, (t0.x - b0.x) / 100f);
                break;
            }
        }
        if (scale <= 0f) scale = Screen.height / 1080f;   // fallback

        float boxLeftS = LeftInsetRef * scale;
        float boxBottomS = BottomInsetRef * scale;
        float boxWidthS = WidthRef * scale;
        float boxHeightS = (_collapsed ? HeaderHeight : BaseHeightRef) * scale;

        if (!ScreenToLocal(new Vector2(boxLeftS, boxBottomS), out var blLocal)) return;
        if (!ScreenToLocal(new Vector2(boxLeftS + boxWidthS, boxBottomS + boxHeightS), out var trLocal)) return;

        var prt = _panelRt;
        if (prt == null) return;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.zero; prt.pivot = Vector2.zero;
        prt.anchoredPosition = blLocal;
        prt.sizeDelta = new Vector2(Mathf.Max(1f, trLocal.x - blLocal.x), Mathf.Max(1f, trLocal.y - blLocal.y));
        if (_panelImg != null) _panelImg.color = new Color(0.03f, 0.05f, 0.09f, BgAlpha);

        // Log only on a meaningful change, so it is verifiable without spamming.
        if (Mathf.Abs(boxWidthS - _lastLogW) > 4f || Mathf.Abs(boxBottomS - _lastLogB) > 4f)
        {
            _lastLogW = boxWidthS; _lastLogB = boxBottomS;
            ModCore.Log.LogInfo($"MSGBOX GEO: scale={scale:F2} boxScreen=(x={boxLeftS:F0} y={boxBottomS:F0} " +
                $"w={boxWidthS:F0} h={boxHeightS:F0}) local(pos={blLocal} size={prt.sizeDelta})");
        }
    }

    private float _lastLogW = -1f, _lastLogB = -1f;

    private bool ScreenToLocal(Vector2 screen, out Vector2 local)
    {
        local = Vector2.zero;
        if (_containerRt == null) return false;
        // Host is a ScreenSpaceOverlay canvas, so the camera is null.
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(_containerRt, screen, null, out local);
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        ApplyCollapsed();
    }

    private void ApplyCollapsed()
    {
        if (_body != null) _body.SetActive(!_collapsed);
        // Height (full vs header-only) is applied by UpdateGeometry, which also
        // keeps the width/position tracking the HUD cluster.
        UpdateGeometry();
    }

    // ---- lines ----

    private void AddLineObject(IReadOnlyList<MsgSpan> spans)
    {
        if (_content == null) return;
        var go = new GameObject("Line");
        go.transform.SetParent(_content, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = 15f;
        t.richText = true;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
        // A dark outline keeps every AP palette color legible over any
        // background (dark item colors were washing out on the dark panel).
        try
        {
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(0, 0, 0, 255);
        }
        catch { }
        var sb = new StringBuilder();
        foreach (var s in spans)
            sb.Append("<color=#").Append(s.Hex).Append('>').Append(Escape(s.Text)).Append("</color>");
        t.text = sb.ToString();
        t.color = Color.white;
    }

    /// <summary>Use the font the game renders its own UI with (crisp SDF atlas),
    /// found from an in-use TextMeshPro label rather than an arbitrary asset.</summary>
    private static TMP_FontAsset? FindGameFont()
    {
        try
        {
            var texts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            if (texts != null)
                foreach (var t in texts)
                    if (t != null && t.font != null)
                        return t.font;
        }
        catch { }
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            return f;
        return null;
    }

    private static string Escape(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<​");   // neutralize stray tags

    private void TrimLines()
    {
        if (_content == null) return;
        while (_content.childCount > MaxLines)
        {
            var child = _content.GetChild(0);
            child.SetParent(null, false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    private void ScrollToBottom()
    {
        if (_scroll == null) return;
        // Force the layout to update first, or the scroll position is set
        // against stale content bounds and won't reach the true bottom.
        try { Canvas.ForceUpdateCanvases(); } catch { }
        _scroll.verticalNormalizedPosition = 0f;   // 0 == bottom (newest)
        try { Canvas.ForceUpdateCanvases(); } catch { }
    }

    // ---- UI helpers ----

    private GameObject MakePanelChild(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_panel!.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    private TextMeshProUGUI MakeText(Transform parent, string txt, float size, Color c)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = txt; t.fontSize = size; t.color = c;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
    }

    private Button MakeButton(Transform parent, string label, Color color)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        var lbl = MakeText(go.transform, label, 12f, Color.white);
        lbl.alignment = TextAlignmentOptions.Center;
        Stretch(lbl.GetComponent<RectTransform>(), 0f, 0f);
        return btn;
    }

    private static void Stretch(RectTransform r, float padX, float padY)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(padX, padY); r.offsetMax = new Vector2(-padX, -padY);
    }

    private void Teardown()
    {
        try { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); } catch { }
        _canvasGo = null; _containerRt = null; _panel = null; _panelRt = null; _panelImg = null; _content = null; _scroll = null; _body = null;
    }
}
