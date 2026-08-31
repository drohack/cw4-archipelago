using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CW4DevTools;

/// <summary>
/// On-screen readout of which cheats are live, so it is never a guess whether a
/// mission was surveyed under normal rules or with the tools on - the difference
/// matters when the notes get written up.
///
/// Builds its OWN canvas rather than borrowing one of the game's: self-contained,
/// survives scene changes on its own terms, and carries no GraphicRaycaster so
/// it cannot intercept clicks.
/// </summary>
public static class DevOverlay
{
    private static GameObject? _root;
    private static GameObject? _panel;
    private static TextMeshProUGUI? _text;
    private static string _last = "";

    /// <summary>How many times the strip has actually been rewritten, and what it
    /// last said. Reported by "overlay:dump".
    ///
    /// The strip is now redrawn from an event rather than a per-frame signature,
    /// and the failure mode of that change is silent: no event, no redraw, a
    /// strip that quietly lies about which cheats are on. A counter makes it
    /// testable.</summary>
    public static int Redraws { get; private set; }
    public static string LastText => _last;

    /// <summary>Set when a setting changes; the only thing checked per frame.</summary>
    private static volatile bool _dirty = true;

    /// <summary>Redraw on the next tick. Wired to the config file's
    /// SettingChanged event in DevConfig.Init.</summary>
    public static void Invalidate() => _dirty = true;

    private const string On = "#7CFF7C";     // green  - enabled
    private const string Off = "#8A93A0";    // grey   - disabled but available
    private const string Dim = "#5A6472";    // dimmer - separators, one-shots
    private const string Head = "#FFC24A";   // amber  - title

    public static void Tick()
    {
        try
        {
            if (_root == null || _text == null)
            {
                Build();
                _dirty = true;         // rebuilt, so it has no text yet
            }
            if (_text == null) return;

            // Nothing to do unless a setting changed. DevConfig subscribes this
            // to the config file's SettingChanged event, which covers both the
            // hotkeys (they write config values) and hand edits to the .cfg
            // while the game runs.
            //
            // This replaced a hand-written signature of the displayed values.
            // That was cheap, but it had to be updated by hand whenever a
            // displayed option was added - and forgetting would silently stop the
            // strip updating. An event cannot drift out of step that way.
            if (!_dirty) return;
            _dirty = false;

            Reposition();

            var s = Compose();
            if (s == _last) return;   // TMP re-layout is not free; only set on change
            _last = s;
            Redraws++;
            _text.text = s;
            FitPanel();
            // Always visible: an all-grey strip says "vanilla" just as clearly as
            // a hidden one, and unlike hiding it also shows what is available.
            if (_panel != null) _panel.SetActive(true);
        }
        catch { /* a scene mid-teardown; rebuilt next frame */ }
    }

    /// <summary>Every option, always listed, each with the key that toggles it
    /// and coloured by state: green = on, grey = off.
    ///
    /// Two earlier attempts were worse and are worth recording. A full vertical
    /// list at top-left covered the GEN/USE/STORE readout and the build buttons.
    /// Replacing it with "only the active cheats plus F5-F10" fixed the overlap
    /// but was unreadable: you could not tell which key drove which option, what
    /// the other options even were, or whether something was off versus absent.
    /// Legibility and staying out of the HUD are both required, not a trade.</summary>
    private static string Compose()
    {
        // Prefix every key with the modifier, so the strip shows what to actually
        // press rather than a bare F-key that would also trigger a game action.
        var mod = DevConfig.HotkeyModifier.Value;
        string pre = mod == KeyCode.None ? "" : Short(mod) + "+";

        string Opt(string key, string label, bool on) =>
            $"<color={(on ? On : Off)}>{pre}{key} {label}</color>";

        int speed = DevConfig.GameSpeed.Value;
        string sep = $"<color={Dim}>  |  </color>";

        var row1 = string.Join(sep, new[]
        {
            Opt(DevConfig.KeyInstantBuild.Value.ToString(), "instant build", DevConfig.InstantBuild.Value),
            Opt(DevConfig.KeyAllBuildings.Value.ToString(), "all buildings", DevConfig.AllBuildings.Value),
            Opt(DevConfig.KeyInfiniteResources.Value.ToString(), "infinite resources", DevConfig.InfiniteResources.Value),
        });

        var row2 = string.Join(sep, new[]
        {
            Opt(DevConfig.KeyIndestructible.Value.ToString(), "indestructible", DevConfig.Indestructible.Value),
            Opt(DevConfig.KeyFreezeCreeper.Value.ToString(), "freeze creeper", DevConfig.FreezeCreeper.Value),
            Opt(DevConfig.KeyGameSpeed.Value.ToString(), speed > 0 ? $"speed x{speed}" : "speed", speed > 0),
        });

        // One-shots have no on/off state, so they stay dim and sit apart.
        var row3 = $"<color={Dim}>{pre}{DevConfig.KeyRevealFog.Value} reveal fog   " +
                   $"{pre}{DevConfig.KeyWinMission.Value} complete objectives   " +
                   $"{pre}{DevConfig.KeyDumpUnits.Value} log unit report</color>";

        // Joined with Environment.NewLine rather than an inline escape - TMP
        // renders either, and this reads more clearly as three rows.
        var indent = new string(' ', 12);
        return string.Join(System.Environment.NewLine, new[]
        {
            $"<color={Head}>DEV TOOLS</color>   {row1}",
            indent + row2,
            row3,
        });
    }

    /// <summary>Size the black background to the text it contains.
    ///
    /// The panel used to be a fixed 780x78, which was measured against the
    /// longest row - so every shorter row trailed a wide band of empty black
    /// that covered map for no reason. Ask TMP what the text actually needs
    /// instead: the strip then hugs its content and shrinks when rows do.</summary>
    private static void FitPanel()
    {
        if (_panel == null || _text == null) return;
        try
        {
            // GetPreferredValues, not the preferredWidth/preferredHeight
            // properties: those are measured against the CURRENT rect, so
            // during the first pass - when the rect is still the provisional
            // size - the height came back short and the third row rendered
            // below the black box. Asking for an unconstrained measurement of
            // the string itself has no such ordering problem.
            var need = _text.GetPreferredValues(_text.text, Mathf.Infinity, Mathf.Infinity);
            var want = new Vector2(need.x + 2f * PadX, need.y + 2f * PadY);
            if (want == _sized) return;
            _sized = want;
            _panel.GetComponent<RectTransform>().sizeDelta = want;
        }
        catch { }
    }

    private const float PadX = 12f;
    private const float PadY = 6f;
    private static Vector2 _sized = new(float.NaN, float.NaN);

    /// <summary>Follow the configured position, so the strip can be nudged out
    /// from under whichever HUD element it is currently fouling without a
    /// rebuild. Only writes when the value actually changed.</summary>
    private static void Reposition()
    {
        if (_panel == null) return;
        var want = new Vector2(DevConfig.OverlayX.Value, DevConfig.OverlayY.Value);
        if (want == _placed) return;
        _placed = want;
        try { _panel.GetComponent<RectTransform>().anchoredPosition = want; } catch { }
    }

    private static Vector2 _placed = new(float.NaN, float.NaN);

    private static void Build()
    {
        var font = FindFont();
        if (font == null) return;

        _root = new GameObject("CW4DevToolsOverlay");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above the game's HUD

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // No GraphicRaycaster: the overlay must never swallow a click.

        // Bottom centre, and actually centred: the old +100 nudge existed to keep
        // a fixed 780-wide panel off the creeper readout, and with the panel
        // sized to its text that clearance comes for free.
        // Bottom centre. The left edge holds GEN/USE/STORE and the build panes,
        // the top right the objectives panel, the bottom right the minimap; the
        // strip between the creeper readout and the minimap is the one reliably
        // free band in the CW4 HUD. A first attempt at top-left covered the
        // energy readout outright.
        //
        // "Reliably free" holds only for the PERMANENT HUD. Tool-specific panels
        // still appear there - the terp's terrain-height bar is the one that
        // caught this - so the height is a config value rather than a constant.
        // That bar measures 74 units tall (captured in terraform mode and read
        // off the pixels, not guessed), which is where the default 80 comes
        // from: clear of it, and no higher than it needs to be.
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(_root.transform, false);
        var bg = _panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        // Offset right of centre and sized for three rows. Measured against the
        // 1920-wide HUD: the creeper/emit readout ends near x=670 and the minimap
        // starts near x=1480, so a 780-wide panel centred at x=1060 sits in the
        // gap. A centred 900-wide panel overlapped the creeper readout, and 62px
        // of height clipped the third row off the bottom.
        // Position comes from config (see Reposition) - the terp's terrain-height
        // bar occupies the bottom centre while that tool is selected, so the
        // strip sits above it by default rather than on the bottom edge.
        prt.anchoredPosition = new Vector2(DevConfig.OverlayX.Value, DevConfig.OverlayY.Value);
        prt.sizeDelta = new Vector2(400f, 78f);   // provisional; FitPanel sets the real size

        var go = new GameObject("Text");
        go.transform.SetParent(_panel.transform, false);
        _text = go.AddComponent<TextMeshProUGUI>();
        _text.font = font;
        _text.fontSize = 15f;
        _text.richText = true;
        _text.raycastTarget = false;
        // TopLeft, not Left: with the panel sized to the text there is no spare
        // vertical room, and a middle alignment then bleeds the outer rows past
        // the background.
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.lineSpacing = -8f;
        _text.enableWordWrapping = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(PadX, PadY);
        rt.offsetMax = new Vector2(-PadX, -PadY);
    }

    /// <summary>"LeftControl" is too long for the strip; "Ctrl" is what a player
    /// would call it.</summary>
    private static string Short(KeyCode k) => k switch
    {
        KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
        KeyCode.LeftAlt or KeyCode.RightAlt => "Alt",
        KeyCode.LeftShift or KeyCode.RightShift => "Shift",
        _ => k.ToString(),
    };

    private static TMP_FontAsset? FindFont()
    {
        try
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null) return f;
        }
        catch { }
        return null;
    }
}
