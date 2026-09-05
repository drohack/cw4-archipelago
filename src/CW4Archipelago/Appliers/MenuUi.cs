using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Main-menu presentation: hides the non-randomizer buttons and builds the
/// interactive Archipelago login panel. Ports the proven probe recipe
/// (ProbePlugin BuildApPanel / menu:hide) and wires it to the real client.
/// </summary>
public sealed class MenuUi
{
    private bool _menuEdited;
    private GameObject? _panel;
    private GameObject? _compact;              // small "connected" label for level select
    private TextMeshProUGUI? _compactText;
    private TMP_InputField? _server;
    private TMP_InputField? _slot;
    private TMP_InputField? _pass;
    private TextMeshProUGUI? _status;
    private TextMeshProUGUI? _autoLabel;
    private TextMeshProUGUI? _connectLabel;
    private Image? _connectImage;
    private TMP_FontAsset? _font;

    public void Tick(string scene)
    {
        if (scene == "Galaxy" && !_menuEdited)
            MaybeBuild();
        UpdateVisibility(scene);
        if (scene == "Galaxy" && _panel != null && _panel.activeSelf)
            FitPanel();
    }

    /// <summary>
    /// Scale the login panel so it never overlaps the FARSITE button. The game's
    /// story panel is center-anchored (slides left as the window narrows) while
    /// our panel is pinned to the left edge; at low resolution / high UI scale
    /// they collide. We read the FARSITE button's live screen-x each frame and
    /// shrink the panel (about its left-center pivot, so the left edge stays put)
    /// to keep its right edge a margin clear of the button. Adapts to any
    /// resolution and UI Scale setting because it uses the button's real rect.
    /// </summary>
    private void FitPanel()
    {
        try
        {
            var fb = GameGalaxy.instance?.farsiteButton;
            if (fb == null || _panel == null) return;
            var frt = fb.transform.TryCast<RectTransform>();
            var prt = _panel.transform.TryCast<RectTransform>();
            if (frt == null || prt == null) return;

            // Overlay canvases: TransformPoint(corner) is already in screen px.
            float farsiteLeft = frt.TransformPoint(new Vector3(frt.rect.xMin, 0f, 0f)).x;

            // Measure the panel's natural (unscaled) screen extent.
            prt.localScale = Vector3.one;
            float panelLeft = prt.TransformPoint(new Vector3(prt.rect.xMin, 0f, 0f)).x;
            float panelRight = prt.TransformPoint(new Vector3(prt.rect.xMax, 0f, 0f)).x;
            float naturalW = panelRight - panelLeft;
            if (naturalW <= 1f) return;

            float margin = 24f;
            float avail = farsiteLeft - margin - panelLeft;
            float want = avail / naturalW;

            // Never shrink below readable. The old floor was 0.2, which kept the
            // panel clear of FARSITE at 1080p by making it unreadable - the wrong
            // trade. If it cannot fit beside FARSITE at MinScale, MOVE it to the
            // bottom-left instead, where nothing competes for the space.
            const float MinScale = 0.65f;
            float fit = Mathf.Clamp(want, MinScale, 1f);
            prt.localScale = new Vector3(fit, fit, 1f);

            if (want < MinScale)
            {
                // Bottom-left corner, out from under FARSITE entirely.
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 0f);
                prt.anchoredPosition = new Vector2(24f, 24f);
            }
            else
            {
                // Default home: left edge, vertically centred.
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 0.5f);
                prt.anchoredPosition = new Vector2(30f, 120f);
            }
        }
        catch { /* menu not fully built yet */ }
    }

    /// <summary>
    /// The login panel belongs on the main menu only. On the level-select
    /// screen we show a compact status line instead; in a mission, nothing.
    /// </summary>
    private void UpdateVisibility(string scene)
    {
        bool mainMenu = scene == "Galaxy" && IsMainMenuShowing();
        bool levelSelect = scene == "Galaxy" && !mainMenu;
        try
        {
            if (_panel != null && _panel.activeSelf != mainMenu)
                _panel.SetActive(mainMenu);
            if (_compact != null && _compact.activeSelf != levelSelect)
                _compact.SetActive(levelSelect);
            if (levelSelect)
                RefreshCompact();
        }
        catch { /* panel destroyed on scene change; rebuilt on return to menu */ }
    }

    private static bool IsMainMenuShowing()
    {
        try
        {
            var gg = GameGalaxy.instance;
            if (gg == null) return false;
            if (gg.mainMenu != null) return gg.mainMenu.activeInHierarchy;
            return gg.farsiteButton != null && gg.farsiteButton.activeInHierarchy;
        }
        catch { return false; }
    }

    private void RefreshCompact()
    {
        if (_compactText == null) return;
        var c = ModCore.Client;
        _compactText.text = c.Status switch
        {
            ConnectionStatus.Connected => $"Archipelago: connected as {c.State.Slot}",
            ConnectionStatus.Connecting => "Archipelago: " + c.StatusText,
            _ => "Archipelago: " + c.StatusText,   // disconnected / retrying / failed
        };
        _compactText.color = c.Status switch
        {
            ConnectionStatus.Connected => new Color(0.45f, 0.85f, 0.5f, 1f),
            ConnectionStatus.Connecting => new Color(0.9f, 0.85f, 0.4f, 1f),
            ConnectionStatus.Failed => new Color(0.95f, 0.4f, 0.35f, 1f),
            _ => new Color(0.85f, 0.6f, 0.35f, 1f),
        };
    }

    public void OnGalaxyEntered()
    {
        _menuEdited = false;   // rebuild on each return to the menu
        _panel = null;
        _compact = null;
    }

    public void OnStateChanged()
    {
        if (_status != null)
        {
            var c = ModCore.Client;
            _status.text = "Status: " + c.StatusText;
            _status.color = c.Status switch
            {
                ConnectionStatus.Connected => new Color(0.4f, 0.9f, 0.4f, 1f),
                ConnectionStatus.Connecting => new Color(0.9f, 0.85f, 0.4f, 1f),
                ConnectionStatus.Failed => new Color(0.95f, 0.4f, 0.35f, 1f),
                _ => new Color(1f, 0.7f, 0.3f, 1f),
            };
        }
        // The button follows the connection, including a state change this panel
        // did not cause - an auto-connect at startup, or a dropped socket.
        if (_connectLabel != null)
        {
            // THREE states, not two. This was `Status != Disconnected`, so a
            // failed or retrying connection read "DISCONNECT" - the button
            // offered to end something that had never started, which is exactly
            // what a player reported. While an attempt is running or retrying
            // the honest word is CANCEL, and pressing it stops the retries.
            var st = ModCore.Client.Status;
            bool connected = st == ConnectionStatus.Connected;
            bool busy = st == ConnectionStatus.Connecting || st == ConnectionStatus.Failed;
            _connectLabel.text = connected ? "DISCONNECT" : busy ? "CANCEL" : "CONNECT";
            if (_connectImage != null)
                _connectImage.color = connected
                    ? new Color(0.45f, 0.16f, 0.16f, 1f)
                    : busy
                        ? new Color(0.45f, 0.35f, 0.12f, 1f)
                        : new Color(0.1f, 0.5f, 0.2f, 1f);
        }
        RefreshCompact();
    }

    /// <summary>Called every frame while at the menu; builds once the UI exists.</summary>
    public void MaybeBuild()
    {
        if (_menuEdited)
            return;
        try
        {
            HideMenuButtons();
            BuildPanel();
            _menuEdited = true;
            SyncFields();
            OnStateChanged();
        }
        catch (Exception e)
        {
            ModCore.Log.LogWarning($"MenuUi build deferred: {e.Message}");
        }
    }

    private void HideMenuButtons()
    {
        var ggm = GameGalaxy.instance;
        if (ggm == null)
            throw new Exception("no GameGalaxy yet");
        int hid = 0;
        foreach (var go in new[] { ggm.chronomButton, ggm.markVButton, ggm.coloniesButton, ggm.editorButton })
            if (go != null) { go.SetActive(false); hid++; }
        // SPAN Experiments: hidden by default (future expansion, config toggle).
        if (!ModCore.Config.ShowSpan.Value && ggm.spanButton != null)
        {
            ggm.spanButton.SetActive(false);
            hid++;
        }
        ModCore.Log.LogInfo($"MENU: hid {hid} buttons (span shown={ModCore.Config.ShowSpan.Value})");
    }

    private void SyncFields()
    {
        if (_server != null) _server.text = $"{ModCore.Config.Host.Value}:{ModCore.Config.Port.Value}";
        if (_slot != null) _slot.text = ModCore.Config.Slot.Value;
        if (_pass != null) _pass.text = ModCore.Config.Password.Value;
        UpdateAutoLabel();
    }

    private void UpdateAutoLabel()
    {
        if (_autoLabel != null)
            _autoLabel.text = (ModCore.Config.AutoConnect.Value ? "[x]" : "[ ]") + " Auto-connect";
    }

    private void BuildPanel()
    {
        // Pick the host canvas DETERMINISTICALLY.
        //
        // This used to take the first root canvas FindObjectsOfType returned,
        // and that order is not specified. The menu has three - MainMenuCanvas
        // (sortingOrder 0), "Modal And Notification Canvas" (50) and
        // AchievementCanvas (99) - so the panel could land under a canvas that
        // covers the screen, where it still RENDERS but never receives a click:
        // every field looks dead and nothing can be typed into it. Take the
        // topmost canvas that can actually be clicked instead, which is the one
        // it has been landing on by luck, and say so in the log so a repeat
        // report is diagnosable from LogOutput.log alone.
        Canvas host = null!;
        int bestOrder = int.MinValue;
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (cv == null || !cv.isRootCanvas || !cv.isActiveAndEnabled) continue;
            GraphicRaycaster? gr = null;
            try { gr = cv.GetComponent<GraphicRaycaster>(); } catch { }
            if (gr == null || !gr.enabled) continue;      // renders but cannot be clicked
            if (cv.sortingOrder < bestOrder) continue;
            bestOrder = cv.sortingOrder;
            host = cv;
        }
        if (host == null)
            throw new Exception("no host canvas");

        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            _font = f;
            break;
        }

        _panel = new GameObject("CW4ApPanel");
        _panel.transform.SetParent(host.transform, false);
        _panel.transform.SetAsLastSibling();
        var img = _panel.AddComponent<Image>();
        img.color = new Color(0.02f, 0.08f, 0.15f, 0.92f);
        var rt = _panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(30f, 120f);
        rt.sizeDelta = new Vector2(360f, 330f);

        var title = MakeText(_panel.transform, "ARCHIPELAGO", 24f, new Color(0.4f, 0.8f, 1f, 1f));
        Place(title.GetComponent<RectTransform>(), -12f, 34f);
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(15f, -12f);

        _server = MakeInput("server:port", -52f, "archipelago.gg:38281");
        _slot = MakeInput("slot name", -94f, "");
        _pass = MakeInput("password", -136f, "");
        _pass.contentType = TMP_InputField.ContentType.Password;

        var btn = MakeButton("CONNECT", -184f, new Color(0.1f, 0.5f, 0.2f, 1f));
        btn.onClick.AddListener((UnityEngine.Events.UnityAction)OnConnectClicked);
        _connectLabel = btn.GetComponentInChildren<TextMeshProUGUI>();
        _connectImage = btn.GetComponent<Image>();

        var autoBtn = MakeButton("", -226f, new Color(0.06f, 0.2f, 0.32f, 1f));
        _autoLabel = autoBtn.GetComponentInChildren<TextMeshProUGUI>();
        _autoLabel.alignment = TextAlignmentOptions.MidlineLeft;
        autoBtn.onClick.AddListener((UnityEngine.Events.UnityAction)OnAutoToggle);

        _status = MakeText(_panel.transform, "Status: not connected", 16f, new Color(1f, 0.7f, 0.3f, 1f));
        Place(_status.GetComponent<RectTransform>(), -272f, 44f);
        _status.GetComponent<RectTransform>().anchoredPosition = new Vector2(15f, -272f);
        _status.enableWordWrapping = true;

        BuildCompact(host);
        EnsureEventSystem();
        ModCore.Log.LogInfo($"MENU: AP panel created on canvas '{host.gameObject.name}' " +
            $"(sortingOrder {host.sortingOrder}, raycaster ok)");
    }

    /// <summary>A uGUI text field is dead without an EventSystem to focus it.
    /// The game supplies one, so this only ever fires if that changes - but the
    /// failure it prevents (a panel that draws and cannot be typed into) is
    /// indistinguishable by eye from several other faults, so it is worth the
    /// six lines to rule out.</summary>
    private static void EnsureEventSystem()
    {
        try
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var go = new GameObject("CW4ApEventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            ModCore.Log.LogWarning("MENU: no EventSystem found - created one so the panel can take input");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"MENU: EventSystem check failed: {e.Message}"); }
    }

    // Compact status line for the level-select screen (top-left).
    private void BuildCompact(Canvas host)
    {
        _compact = new GameObject("CW4ApStatus");
        _compact.transform.SetParent(host.transform, false);
        _compact.transform.SetAsLastSibling();
        var rt = _compact.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -20f);
        rt.sizeDelta = new Vector2(420f, 28f);
        _compactText = MakeText(_compact.transform, "Archipelago: not connected", 16f, new Color(0.85f, 0.6f, 0.35f, 1f));
        StretchToParent(_compactText.GetComponent<RectTransform>());
        _compact.SetActive(false);
    }

    /// <summary>CONNECT and DISCONNECT are the same button.
    ///
    /// There was no way to drop a connection from the UI at all: once connected
    /// the only route to a different slot or server was editing the config file
    /// or restarting the game.</summary>
    private void OnConnectClicked()
    {
        var st = ModCore.Client.Status;
        if (st == ConnectionStatus.Connected)
        {
            ModCore.Log.LogInfo("MENU: disconnect requested");
            ModCore.Client.Disconnect();
            OnStateChanged();
            return;
        }
        if (st == ConnectionStatus.Connecting || st == ConnectionStatus.Failed)
        {
            // Cancel: stop the attempt AND the backoff behind it. Without this
            // the only way out of a retry loop was to quit the game.
            ModCore.Log.LogInfo("MENU: connect attempt cancelled");
            ModCore.Client.Disconnect();
            OnStateChanged();
            return;
        }
        DoConnect();
    }

    private void DoConnect()
    {
        ModCore.Config.Host.Value = ParseHost(_server?.text ?? "");
        ModCore.Config.Port.Value = ParsePort(_server?.text ?? "");
        ModCore.Config.Slot.Value = _slot?.text ?? "";
        ModCore.Config.Password.Value = _pass?.text ?? "";
        ModCore.Connect();
    }

    private void OnAutoToggle()
    {
        ModCore.Config.AutoConnect.Value = !ModCore.Config.AutoConnect.Value;
        UpdateAutoLabel();
    }

    private static string ParseHost(string s)
    {
        var i = s.LastIndexOf(':');
        return i > 0 ? s.Substring(0, i) : s;
    }

    private static int ParsePort(string s)
    {
        var i = s.LastIndexOf(':');
        return i > 0 && int.TryParse(s.Substring(i + 1), out var p) ? p : 38281;
    }

    // ---- UI primitives (ported from the probe) ----

    private TextMeshProUGUI MakeText(Transform parent, string txt, float size, Color c)
    {
        var go = new GameObject("ApText");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = txt;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        return t;
    }

    private static void Place(RectTransform r, float y, float h)
    {
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(-30f, h);
    }

    private TMP_InputField MakeInput(string placeholder, float y, string initial)
    {
        var box = new GameObject("ApInput");
        box.transform.SetParent(_panel!.transform, false);
        box.SetActive(false);   // defer Awake until fully wired (caret setup)
        var bi = box.AddComponent<Image>();
        bi.color = new Color(0.05f, 0.18f, 0.3f, 1f);
        Place(box.GetComponent<RectTransform>(), y, 32f);
        var field = box.AddComponent<TMP_InputField>();

        var area = new GameObject("TextArea");
        area.transform.SetParent(box.transform, false);
        var art = area.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(10f, 4f); art.offsetMax = new Vector2(-10f, -4f);
        area.AddComponent<RectMask2D>();

        var ph = MakeText(area.transform, placeholder, 16f, new Color(0.5f, 0.62f, 0.72f, 1f));
        StretchToParent(ph.GetComponent<RectTransform>());
        var txt = MakeText(area.transform, "", 16f, new Color(0.9f, 0.96f, 1f, 1f));
        StretchToParent(txt.GetComponent<RectTransform>());

        field.textViewport = art;
        field.textComponent = txt;
        field.placeholder = ph;
        field.text = initial;
        if (_font != null) field.fontAsset = _font;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = new Color(0.9f, 0.96f, 1f, 1f);
        field.caretBlinkRate = 0.85f;
        field.selectionColor = new Color(0.2f, 0.5f, 0.9f, 0.5f);
        field.interactable = true;
        box.SetActive(true);
        return field;
    }

    private Button MakeButton(string label, float y, Color color)
    {
        var btn = new GameObject("ApButton");
        btn.transform.SetParent(_panel!.transform, false);
        var bimg = btn.AddComponent<Image>();
        bimg.color = color;
        Place(btn.GetComponent<RectTransform>(), y, 36f);
        var button = btn.AddComponent<Button>();
        var lbl = MakeText(btn.transform, label, 18f, Color.white);
        lbl.alignment = TextAlignmentOptions.Center;
        StretchToParent(lbl.GetComponent<RectTransform>());
        return button;
    }

    private static void StretchToParent(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }
}
