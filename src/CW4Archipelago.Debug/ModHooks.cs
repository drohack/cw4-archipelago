using System;
using System.Reflection;
using BepInEx.Logging;
using CW4Archipelago.Core;
using HarmonyLib;
using UnityEngine.UI;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Everything the debug channel needs to see inside the mod, attached from
/// OUTSIDE it.
///
/// The counters, the flash watch, the message-box readbacks and the ERN ceiling
/// override all used to live in the shipped mod, reached through
/// InternalsVisibleTo - so every player ran a Harmony patch, per-planet interop
/// reads and a per-frame check that only this plugin ever looked at. They are
/// here now: Harmony patches of this plugin's own on the same game methods the
/// mod hooks, plus on the mod's own methods, and AccessTools reads of the mod's
/// private state. The mod carries no trace of any of it.
///
/// Patched at Load, which runs straight after the mod's (BepInDependency) and
/// before any of these methods is first called. That ordering matters for the
/// patches on the mod's managed methods: the detour also stops them being
/// inlined into callers JIT-compiled later.
///
/// The command names and the log lines they print are unchanged, so the
/// harnesses in tools/ that grep them (eventdriven-test.sh, map-visual-check.sh,
/// msgbox.sh, msgfilter.sh) run unedited - which is the regression test for
/// this move.
/// </summary>
internal static class ModHooks
{
    // --- mission-map counters ("diag:span", "perf") -----------------------
    //
    // For the flashing-planet problem: "Not My Mars" was seen flashing between
    // its planet and its locked "?", which means something repaints a planet by
    // a route the mod does not hook. These say which route and how often.

    /// <summary>Game Refresh calls the mod's refresh patch acted on (nested
    /// ones, from the mod's own repaint, excluded - as the mod's own counter
    /// did before it moved here).</summary>
    internal static int Refreshes;
    /// <summary>Writes to SpanNetworkPlanet.unlocked, the other way a planet's
    /// display changes - counted to find out whether the game drives it per
    /// frame.</summary>
    internal static int UnlockedSets;
    internal static int PaintCalls;
    /// <summary>Paints that found a planet's visuals already drifted - someone
    /// else wrote them.</summary>
    internal static int VisualFixes;
    /// <summary>Planet recolours, so the per-frame audit's claim that the map
    /// repaints only on a change can be checked at runtime.</summary>
    internal static int Recolours;

    // --- patch-fired counters ("perf") ------------------------------------
    //
    // Applying is not firing: a patch on a private method or a property setter
    // can attach to a method IL2CPP never actually calls, and the mod's safety
    // poll would quietly cover for it forever. These are how a test tells the
    // difference.
    internal static int TotemPokes;
    internal static int CachePokes;

    /// <summary>Frames left in a "diag:watch" window. While it runs, every frame
    /// is checked for a planet showing the wrong thing.</summary>
    internal static int WatchFrames;
    private static int _reported;

    /// <summary>The mod's refresh-patch recursion guard, read so a nested
    /// Refresh is excluded exactly as the mod's own counter excluded it.</summary>
    private static FieldInfo? _painting;

    /// <summary>"ern:cap": when set, every ERN upgrade's ceiling. Null means the
    /// player's configured values. So one game session can sweep candidate
    /// ceilings instead of costing a rebuild and relaunch per value.</summary>
    internal static float? CeilingOverride;

    private static ManualLogSource _log = null!;

    public static void Install(ManualLogSource log)
    {
        _log = log;
        var h = new Harmony("com.droha.cw4apdebug");

        _painting = AccessTools.Field(typeof(PlanetRefreshPatch), "_painting");
        if (_painting == null)
            log.LogWarning("debug hooks: the mod's refresh guard was not found, counting every Refresh");

        Patch(h, "planet unlocked set",
            () => AccessTools.PropertySetter(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.unlocked)),
            postfix: nameof(OnUnlockedSet));
        Patch(h, "planet refresh",
            () => AccessTools.Method(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.Refresh)),
            postfix: nameof(OnRefresh));
        Patch(h, "tracker paint", () => AccessTools.Method(typeof(TrackerView), "Paint"),
            prefix: nameof(OnPaint));
        Patch(h, "tracker visuals", () => AccessTools.Method(typeof(TrackerView), "SetVisuals"),
            prefix: nameof(OnSetVisuals));
        Patch(h, "tracker recolour", () => AccessTools.Method(typeof(TrackerView), "ColorGlyphs"),
            postfix: nameof(OnColorGlyphs));
        Patch(h, "totem complete",
            () => AccessTools.PropertySetter(typeof(Totem), nameof(Totem.totemComplete)),
            postfix: nameof(OnTotemComplete));
        // By signature: InfoCache declares DestroyUnit(bool) and inherits
        // UnitManager.DestroyUnit(bool, bool, bool), so the name alone is an
        // AmbiguousMatchException. (The mod's [HarmonyPatch] attribute resolves
        // to the declared one; a bare AccessTools.Method does not.)
        Patch(h, "cache destroyed",
            () => AccessTools.Method(typeof(InfoCache), nameof(InfoCache.DestroyUnit), new[] { typeof(bool) }),
            postfix: nameof(OnCacheDestroyed));
        Patch(h, "ERN ceiling",
            () => AccessTools.Method(typeof(ErnUpgradeRules), nameof(ErnUpgradeRules.CeilingFor)),
            prefix: nameof(OnCeilingFor));
    }

    /// <summary>Each patch independently, as the mod does: a game update that
    /// breaks one costs that one counter, not the channel.
    ///
    /// The target is RESOLVED inside the try too. Resolving it at the call site
    /// once let an AmbiguousMatchException escape Install, which aborted every
    /// patch after it and failed the plugin's load.</summary>
    private static void Patch(Harmony h, string name, Func<MethodBase?> resolve,
                              string? prefix = null, string? postfix = null)
    {
        try
        {
            var target = resolve();
            if (target == null) throw new MissingMethodException(name);
            h.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(ModHooks), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(ModHooks), postfix));
        }
        catch (Exception e)
        {
            _log.LogError($"debug hook '{name}' failed (that readout stays at 0): {e.Message}");
        }
    }

    private static void OnUnlockedSet() => UnlockedSets++;

    private static void OnRefresh()
    {
        if (_painting?.GetValue(null) is true) return;
        Refreshes++;
    }

    private static void OnPaint() => PaintCalls++;

    // Parameter names must match TrackerView.SetVisuals - Harmony binds by name.
    private static void OnSetVisuals(SpanNetworkPlanet p, bool unlocked)
    {
        try
        {
            if (p.lockedPlanet != null && p.lockedPlanet.gameObject.activeSelf == unlocked)
                VisualFixes++;   // it had drifted; someone else wrote it
        }
        catch { }
    }

    private static void OnColorGlyphs() => Recolours++;

    private static void OnTotemComplete(bool value)
    {
        if (value) TotemPokes++;
    }

    private static void OnCacheDestroyed() => CachePokes++;

    private static bool OnCeilingFor(ref float __result)
    {
        if (!CeilingOverride.HasValue) return true;
        __result = CeilingOverride.Value;
        return false;
    }

    // --- the mod's private state, read from outside ----------------------

    private static TrackerView? Tracker
        => AccessTools.Field(typeof(ModCore), "_tracker")?.GetValue(null) as TrackerView;

    /// <summary>The cached planet list, while the map is open.</summary>
    private static SpanNetworkPlanet[]? Planets
        => Tracker == null ? null
            : AccessTools.Field(typeof(TrackerView), "_planets")?.GetValue(Tracker) as SpanNetworkPlanet[];

    internal static ApMessageBox? MessageBox
        => AccessTools.Field(typeof(ModCore), "_messageBox")?.GetValue(null) as ApMessageBox;

    /// <summary>Where the view is: 0 is the bottom (newest), 1 the top, -1 if
    /// there is no scroll rect. "The log opened half way up" was otherwise only
    /// checkable by eye, and the cause was a timing bug that an eye can easily
    /// miss on a short history.</summary>
    internal static float ScrollPosition(ApMessageBox box)
    {
        try
        {
            var scroll = AccessTools.Field(typeof(ApMessageBox), "_scroll")?.GetValue(box) as ScrollRect;
            return scroll == null ? -1f : scroll.verticalNormalizedPosition;
        }
        catch { return -1f; }
    }

    /// <summary>Whether the box is still following new lines.</summary>
    internal static bool AutoScroll(ApMessageBox box)
        => AccessTools.Field(typeof(ApMessageBox), "_autoScroll")?.GetValue(box) is true;

    /// <summary>Lines currently rendered, as opposed to lines in history.</summary>
    internal static int RenderedLines(ApMessageBox box)
    {
        try
        {
            var content = AccessTools.Field(typeof(ApMessageBox), "_content")?.GetValue(box)
                as UnityEngine.RectTransform;
            return content == null ? -1 : content.childCount;
        }
        catch { return -1; }
    }

    /// <summary>Announce a location change made by writing the set directly,
    /// as the debug commands do. The mod's own paths go through MarkChecked and
    /// raise it themselves; an event can only be raised from inside its class,
    /// so this invokes the backing delegate.</summary>
    internal static void RaiseLocationsChanged(SlotState state)
    {
        var handler = AccessTools.Field(typeof(SlotState), nameof(SlotState.LocationsChanged))
            ?.GetValue(state) as Action;
        handler?.Invoke();
    }

    // --- the flash watch ("diag:watch") ----------------------------------

    /// <summary>Called from this plugin's Update - i.e. BEFORE the mod's
    /// LateUpdate gets a chance to correct anything. A mismatch seen here is a
    /// frame the player actually saw wrong, which is what "the planet is
    /// flashing" means.</summary>
    internal static void Tick()
    {
        if (WatchFrames <= 0)
            return;
        WatchFrames--;
        var planets = Planets;
        if (planets == null)
            return;
        var state = ModCore.Client.State;
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            int mission = TrackerView.MissionByTitle(TrackerView.TitleOf(p));
            if (mission == 0) continue;
            bool want = MissionRules.IsUnlocked(state, mission);
            bool sphere;
            try { sphere = p.planet != null && p.planet.gameObject.activeSelf; } catch { continue; }
            if (sphere == want) continue;
            if (_reported++ > 40) { WatchFrames = 0; return; }   // enough to see the pattern
            bool fu = false, un = false;
            try { fu = p.forceUnlocked; } catch { }
            try { un = p.unlocked; } catch { }
            ModCore.Log.LogWarning(
                $"DIAG FLASH: frame={UnityEngine.Time.frameCount} story{mission} " +
                $"'{TrackerView.TitleOf(p)}' wantUnlocked={want} sphereOn={sphere} " +
                $"forceUnlocked={fu} unlocked={un} refreshes={Refreshes} unlockedSets={UnlockedSets}");
        }
    }
}
