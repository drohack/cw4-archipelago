using System;
using System.Collections.Generic;
using System.Linq;
using CW4Archipelago.Core;
using HarmonyLib;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Drives the mission-map planets from AP state: locked planets keep the
/// native "?" display, unlocked planets show objective glyphs colored by the
/// Archipelago tracker convention (red/yellow/green/grey). Completion is
/// answered from checked locations by patching FakeIsMissionObjectiveComplete.
/// </summary>
public sealed class TrackerView
{
    // AP/PopTracker colors.
    private static readonly Color Red = new(0.90f, 0.32f, 0.30f, 1f);      // not reachable
    private static readonly Color Yellow = new(0.95f, 0.85f, 0.30f, 1f);   // reachable, out of logic
    private static readonly Color Green = new(0.32f, 0.90f, 0.38f, 1f);    // reachable, in logic
    private static readonly Color Grey = new(0.58f, 0.58f, 0.62f, 1f);     // finished

    // Populated by the scan; read by the Harmony patch. planetGUID -> mission.
    internal static readonly Dictionary<string, int> GuidToMission = new();

    /// <summary>Set by Invalidate; the only thing checked per frame.</summary>
    private volatile bool _dirty = true;

    /// <summary>CW4's objective slot 5 is Custom - the finale's "End the
    /// Beginning", which the mission-count gate blocks.</summary>
    private const int CustomObjectiveIndex = 5;

    // Cached while the map is open: the planets, and each planet's objective
    // markers. Both are stable for as long as the Galaxy scene is loaded.
    private SpanNetworkPlanet[]? _planets;
    private int _recolours;

    /// <summary>How many planet recolours have happened. Read by the debug
    /// channel so the per-frame audit's claims can be checked at runtime.</summary>
    public int Recolours => _recolours;

    /// <summary>The cached planet list, for the flash diagnostic only.</summary>
    internal SpanNetworkPlanet[]? Planets => _planets;
    private readonly Dictionary<IntPtr, SpanNetworkPlanetObjective[]> _markers = new();

    public static Color StatusColor(TrackerStatus status) => status switch
    {
        TrackerStatus.Locked => Red,
        TrackerStatus.OutOfLogic => Yellow,
        TrackerStatus.Done => Grey,
        _ => Green,
    };

    /// <summary>Forget one planet's cached marker array, because its children
    /// have changed. Called when the game refreshes a planet, which appends new
    /// markers.</summary>
    internal void DropMarkerCache(SpanNetworkPlanet p)
    {
        if (p == null) return;
        try { _markers.Remove(p.Pointer); } catch { }
    }

    /// <summary>Mark the map as needing a repaint. Safe to call from any thread:
    /// it only sets a bool, and all Unity work happens in ApplyTints on the main
    /// thread. Called when the map opens, when the scene changes, and when
    /// Archipelago state changes.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Called from LateUpdate, but does NOTHING unless something has
    /// actually changed.
    ///
    /// This used to poll: a whole-scene FindObjectsOfType every frame to notice
    /// the map had opened (2256 searches in twenty seconds on the menu alone),
    /// then a re-assert of every planet's visuals every frame because the game's
    /// own Refresh() overwrites them. Both are now events -
    /// SpanStartPatch and PlanetRefreshPatch below - so the per-frame cost is the
    /// one bool test above. Unity offers no way to run main-thread code without
    /// SOME frame hook, so that test is the floor, not a compromise.</summary>
    public void ApplyTints()
    {
        if (!_dirty)
            return;
        _dirty = false;

        if (ModCore.CurrentScene != "Galaxy")
        {
            _planets = null;          // planets die with the scene
            _markers.Clear();
            return;
        }

        // Built when Span.Start told us the map opened, not searched for.
        if (_planets == null || _planets.Length == 0 || !GameUtil.IsAlive(_planets[0]))
        {
            _planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
            _markers.Clear();
            // Logged so the claim is checkable at runtime rather than asserted.
            if (_planets != null && _planets.Length > 0)
                ModCore.Log.LogInfo($"TRACKER: scanned the map ({_planets.Length} planets)");
        }
        var planets = _planets;
        if (planets == null || planets.Length == 0)
            return;

        var state = ModCore.Client.State;
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p))
                continue;
            Paint(p, state);
        }
    }

    /// <summary>Everything one planet needs: unlock flag, visuals, glyph colours.
    /// Shared by the full repaint and by the Refresh patch, so the game cannot
    /// leave a planet in a state we did not choose.</summary>
    internal void Paint(SpanNetworkPlanet p, SlotState state)
    {
        TrackerDiag.PaintCalls++;
        int mission = ResolveMission(p);
        if (mission == 0)
            return;

        bool unlocked = MissionRules.IsUnlocked(state, mission);
        try
        {
            if (p.forceUnlocked != unlocked)
            {
                p.forceUnlocked = unlocked;
                // Refresh re-enters this method through the patch, which is
                // where the visuals below get re-applied.
                try { p.Refresh(); } catch { }
            }
        }
        catch { }

        // Mutually-exclusive visuals: locked = only the "?", unlocked = only the
        // sphere plus coloured glyphs.
        SetVisuals(p, unlocked);
        if (unlocked)
        {
            ReconcileGlyphs(p, mission, state);
            ColorGlyphs(p, mission, state);
            _recolours++;
        }
    }

    private static void SetVisuals(SpanNetworkPlanet p, bool unlocked)
    {
        try
        {
            if (p.lockedPlanet != null && p.lockedPlanet.gameObject.activeSelf == unlocked)
                TrackerDiag.VisualFixes++;   // it had drifted; someone else wrote it
        }
        catch { }
        try { if (p.lockedPlanet != null) p.lockedPlanet.gameObject.SetActive(!unlocked); } catch { }
        try { if (p.planet != null) p.planet.gameObject.SetActive(unlocked); } catch { }
        try { if (p.objectiveContainer != null) p.objectiveContainer.gameObject.SetActive(unlocked); } catch { }
    }

    /// <summary>Spacing between objective icons, in the container's local units.
    /// Measured off every planet on the map: icon k sits at x = 0.55 * k, y = z =
    /// 0, scale 0.7, in ascending objective order.</summary>
    private const float GlyphSpacing = 0.55f;

    /// <summary>Name given to the markers we add, so a later pass recognises its
    /// own work instead of cloning a second one.</summary>
    private const string AddedGlyphName = "APObjectiveGlyph";

    /// <summary>Make the planet's icon set match the objective slots that
    /// actually have checks in this slot.
    ///
    /// Why this is needed: the map draws one icon per objective in the MAP FILE's
    /// authored list, which is not always the mission's real objective set.
    /// Nineteen of the twenty missions agree; Farsite draws a Totems icon and has
    /// no totems at all, while its two caches and its custom objective get no
    /// icon - so their status could not be read off the map. Vanilla does the
    /// same, so this is the game's data rather than a regression, and the fix is
    /// generic rather than a special case for mission 1: it is driven by the
    /// locations, so it is a no-op wherever the game already agrees, and it
    /// self-corrects if a game update changes the map data.
    ///
    /// Three measured facts make this cheap:
    ///   - Writing `objective` RE-TEXTURES the marker. The property setter swaps
    ///     the material (ObjTotem -> ObjCollect, verified), so re-pointing an
    ///     existing icon needs no donor, no prefab hunt and no material copying.
    ///   - The layout is x = 0.55 * k with nothing else varying, so a new icon
    ///     can be placed exactly where the game would have put it. There is no
    ///     Unity layout component to do it for us.
    ///   - A clone of an existing marker keeps the SOURCE's local position, so the
    ///     position must always be written, including for the clone.
    ///
    /// And one that makes it worth doing anyway: the game's own Refresh APPENDS
    /// its authored markers without clearing the container, so every Refresh
    /// leaves another exact copy stacked on the previous one (measured: 3 -> 4 ->
    /// 5 children on consecutive calls). Those duplicates are invisible because
    /// they overlap perfectly, and hiding the surplus here absorbs them.
    ///
    /// Runs from Paint, so it is on the event path - map open, planet refresh, and
    /// Archipelago state change - and never per frame.</summary>
    private void ReconcileGlyphs(SpanNetworkPlanet p, int mission, SlotState state)
    {
        var want = MissionRules.ExpectedObjectiveIndices(state, mission);
        if (want.Count == 0)
            return;   // nothing known yet (no server); leave the map alone

        Transform container;
        try { container = p.objectiveContainer; } catch { return; }
        if (container == null)
            return;

        // includeInactive MUST be true. FindObjectsOfType and an active-only
        // walk both skip the markers of every locked planet, because the mod
        // deactivates their whole objectiveContainer - an earlier version of this
        // search reported "no marker with objective=4" on a map that has
        // fourteen of them. It also matters here for reusing markers this method
        // hid on a previous pass.
        SpanNetworkPlanetObjective[] markers;
        try { markers = container.GetComponentsInChildren<SpanNetworkPlanetObjective>(true); }
        catch { return; }
        if (markers == null || markers.Length == 0)
            return;

        // Already correct? Then touch nothing. This is the case on nineteen
        // planets, and it keeps the common path to a handful of field reads.
        if (Matches(markers, want, mission))
            return;

        var live = new List<SpanNetworkPlanetObjective>();
        foreach (var m in markers)
            if (GameUtil.IsAlive(m))
                live.Add(m);
        if (live.Count == 0)
            return;

        // The first want.Count markers become the icons we want, in order; any
        // beyond that are surplus (vanilla extras, or Refresh's duplicates) and
        // get hidden rather than destroyed - the game owns them, and hiding is
        // reversible.
        for (int k = 0; k < want.Count; k++)
        {
            SpanNetworkPlanetObjective slot;
            if (k < live.Count)
            {
                slot = live[k];
            }
            else
            {
                var made = CloneGlyph(live[0], container);
                if (made == null)
                    break;      // could not clone; the icons placed so far still stand
                slot = made;
                live.Add(made);
            }
            Configure(slot, mission, want[k], k);
        }
        for (int k = want.Count; k < live.Count; k++)
        {
            try { live[k].gameObject.SetActive(false); } catch { }
        }

        // The marker cache is built on the assumption that a planet's markers do
        // not change while the map is open. We just changed them.
        _markers.Remove(p.Pointer);
        ModCore.Log.LogInfo(
            $"TRACKER: reconciled {MissionRules.Specifier(mission)} icons to " +
            $"[{string.Join(",", want)}] ({live.Count} markers present)");
    }

    /// <summary>Whether the active markers are already exactly the wanted slots,
    /// in order. Cheap enough to run on every paint of every planet.</summary>
    private static bool Matches(SpanNetworkPlanetObjective[] markers, List<int> want, int mission)
    {
        int seen = 0;
        foreach (var m in markers)
        {
            if (!GameUtil.IsAlive(m)) continue;
            bool active;
            try { active = m.gameObject.activeSelf; } catch { continue; }
            if (!active) continue;              // hidden surplus is fine
            int obj;
            try { obj = m.objective; } catch { return false; }
            if (seen >= want.Count || obj != MissionRules.DisplayObjective(mission, want[seen]))
                return false;
            seen++;
        }
        return seen == want.Count;
    }

    /// <summary>Point one marker at an objective slot and put it where the game
    /// would have put it. Writing `objective` is what changes the icon.</summary>
    private static void Configure(SpanNetworkPlanetObjective m, int mission, int objective, int ordinal)
    {
        // Draw the ALIASED index - Farsite's Custom check is shown as a totem,
        // because lighting its two totems is what the mission actually asks.
        // ColorGlyphs inverts this when it reads the marker back.
        int drawn = MissionRules.DisplayObjective(mission, objective);
        try { if (!m.gameObject.activeSelf) m.gameObject.SetActive(true); } catch { }
        try { if (m.objective != drawn) m.objective = drawn; } catch { }
        try { m.transform.localPosition = new Vector3(GlyphSpacing * ordinal, 0f, 0f); } catch { }
    }

    /// <summary>Add one more marker to a planet that has fewer than it needs.
    ///
    /// Clones a marker the game itself built - they are all
    /// SpanNetworkPlanetObjective(Clone) already, so this is the same operation
    /// the game performs - following the Instantiate pattern FinalePlacement uses
    /// for the extra connector line: parent at instantiation, configure after.
    /// The clone shares the source's material instance, so it is given its own;
    /// otherwise a later colour write would tint both.</summary>
    private static SpanNetworkPlanetObjective? CloneGlyph(SpanNetworkPlanetObjective source, Transform container)
    {
        try
        {
            var go = UnityEngine.Object.Instantiate(source.gameObject, container, false);
            go.name = AddedGlyphName;
            var made = go.GetComponent<SpanNetworkPlanetObjective>();
            if (made == null)
            {
                UnityEngine.Object.Destroy(go);   // ours, never the game's
                return null;
            }
            try
            {
                var mr = go.GetComponent<MeshRenderer>();
                mr.material = new Material(mr.material);
            }
            catch { }
            return made;
        }
        catch (Exception e)
        {
            ModCore.Log.LogWarning($"TRACKER: could not add an objective icon: {e.Message}");
            return null;
        }
    }

    private void ColorGlyphs(SpanNetworkPlanet p, int mission, SlotState state)
    {
        Transform container;
        try { container = p.objectiveContainer; } catch { return; }
        if (container == null)
            return;
        // Cached: GetComponentsInChildren allocates and walks the hierarchy, and
        // a planet's markers do not change while the map is open.
        SpanNetworkPlanetObjective[]? markers;
        if (!_markers.TryGetValue(p.Pointer, out markers) || markers == null)
        {
            markers = container.GetComponentsInChildren<SpanNetworkPlanetObjective>(true);
            if (markers == null)
                return;
            _markers[p.Pointer] = markers;
        }
        foreach (var m in markers)
        {
            if (!GameUtil.IsAlive(m))
                continue;
            int drawnIndex;
            try { drawnIndex = m.objective; } catch { continue; }
            if (drawnIndex < 0 || drawnIndex >= MissionRules.ObjectiveTypes.Length)
                continue;
            // Undo the cosmetic alias: a totem icon on Farsite stands for its
            // Custom check, and colouring it from Farsite's (nonexistent) totem
            // locations would leave it permanently green.
            int objIndex = MissionRules.LogicalObjective(mission, drawnIndex);
            // One marker stands for every instance of that objective, so
            // aggregate them. Asking for a single type-shaped name matched
            // nothing after locations became per-instance, which quietly
            // disabled this colouring altogether.
            var locations = MissionRules.LocationsForObjective(state, mission, objIndex);

            TrackerStatus status;
            if (mission == MissionRules.FinalMission
                && objIndex == CustomObjectiveIndex
                && !MissionRules.FinaleCounts(state))
            {
                // The finale's custom objective is what the mission-count gate
                // blocks, so it reads as not-reachable until the gate opens.
                // Only that one: the other objectives really are collectable,
                // and coloring them red would be a lie.
                status = TrackerStatus.Locked;
            }
            else if (locations.Count == 0)
            {
                // The map's marker set does not always match the mission. Farsite
                // draws a TOTEMS icon and the mission has no totems at all - it
                // has two caches and a custom objective, measured live - so that
                // marker maps to no Archipelago location.
                //
                // Leaving it alone was the bug: it keeps the game's own bright
                // green, which in this map's language now means "reachable and in
                // logic". A planet was therefore showing a confident green icon
                // for something that is not a check. Falling back to the mission's
                // overall status keeps every visible icon meaningful, and keeps
                // the promise that a shown planet always reads as one of the four
                // tracker colours.
                //
                // The icon's SHAPE is still vanilla's mistake, not something this
                // can fix from here; correcting that means building the marker set
                // ourselves instead of colouring the game's.
                status = TrackerRules.MissionStatus(state, mission);
            }
            else
            {
                status = TrackerRules.Aggregate(
                    locations.Select(l => TrackerRules.LocationStatus(state, mission, l)));
            }
            var color = StatusColor(status);
            try { m.GetComponent<MeshRenderer>().material.SetColor("_color", color); }
            catch { }
        }
    }

    private static int ResolveMission(SpanNetworkPlanet p)
    {
        string title = TitleOf(p);
        string guid = "";
        try { guid = p.planetGUID ?? ""; } catch { }
        int mission = MissionByTitle(title);
        if (mission != 0 && !string.IsNullOrEmpty(guid))
            GuidToMission[guid] = mission;
        return mission;
    }

    /// <summary>Planet title: the TextMeshPro label (map_title is blank on the map).</summary>
    internal static string TitleOf(SpanNetworkPlanet p)
    {
        try { if (p.title != null && !string.IsNullOrEmpty(p.title.text)) return p.title.text.Trim(); }
        catch { }
        try { return (p.map_title ?? "").Trim(); }
        catch { return ""; }
    }

    internal static int MissionByTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
            return 0;
        foreach (var kv in MissionRules.Titles)
            if (kv.Value == title)
                return kv.Key;
        return 0;
    }
}

/// <summary>
/// Swallows clicks on a locked planet so the mission popup (with its
/// non-functional Play button) never opens. Unlocked planets click normally.
/// </summary>
/// <summary>
/// The mission map just opened, so the planets now exist.
///
/// Span is the map's own controller and its Start runs when the panel opens.
/// Before this, the mod polled - a whole-scene FindObjectsOfType every frame -
/// purely to notice this moment, which on the main menu meant searching the
/// entire game about a hundred times a second and finding nothing, because the
/// Galaxy scene is both the menu and the map.
/// </summary>
[HarmonyPatch(typeof(Span), nameof(Span.Start))]
public static class SpanStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { ModCore.InvalidateTracker(); }
        catch { }
    }
}

/// <summary>
/// The game repainted a planet, so re-apply our version - to the WHOLE map.
///
/// SpanNetworkPlanet.Refresh rebuilds a planet's display from the player's
/// vanilla save, which would re-show spheres for missions Archipelago has locked.
/// The mod used to win that race by re-asserting every planet's visuals every
/// frame. Patching the repaint is the same lesson the finale lock taught twice:
/// hook the thing that overwrites you rather than outrunning it.
///
/// Repainting the whole map rather than just `__instance` is the fix for the
/// flashing planet, and the reason is worth keeping: refreshing one planet also
/// changes its NEIGHBOURS, because the map reveals connected planets. A
/// per-instance repaint therefore left the neighbour showing its unlocked sphere
/// with nothing to correct it. Measured: unlocking "Not My Mars" left "Ruins
/// Repurposed" showing its sphere for twenty-plus consecutive frames - the
/// Refresh counter unmoving throughout, so no per-planet hook could have caught
/// it - until an unrelated state change happened to trigger a full repaint.
///
/// The cost is one full repaint per Refresh, and Refresh runs about two dozen
/// times per visit to the map, not per frame.
/// </summary>
[HarmonyPatch(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.Refresh))]
public static class PlanetRefreshPatch
{
    /// <summary>Guards against recursion: Paint may call Refresh, which lands
    /// back here.</summary>
    private static bool _painting;

    [HarmonyPostfix]
    public static void Postfix(SpanNetworkPlanet __instance)
    {
        if (_painting)
            return;
        try
        {
            _painting = true;
            TrackerDiag.Refreshes++;
            // Refresh APPENDS markers to the objective container without
            // clearing it, so this planet's cached marker array is now short by
            // however many it just added.
            ModCore.DropPlanetMarkerCache(__instance);
            // This planet now; its neighbours on the next LateUpdate.
            //
            // This terminates: the sweep only calls Refresh again when a
            // planet's forceUnlocked actually CHANGES, and the recursion guard
            // means such a nested Refresh cannot schedule a further sweep. So the
            // worst case is one extra repaint frame, not a loop.
            ModCore.RepaintPlanet(__instance);
            ModCore.InvalidateTracker();
        }
        catch { }
        finally { _painting = false; }
    }
}

/// <summary>
/// Diagnostic counters for the mission map. Not a feature: they exist because
/// "Not My Mars" was seen flashing between its planet and its locked "?", which
/// means something repaints a planet by a route the mod does not hook, and the
/// old per-frame re-assert had been hiding it.
///
/// Read with the debug channel's "diag:span".
/// </summary>
internal static class TrackerDiag
{
    internal static int Refreshes;
    internal static int UnlockedSets;
    internal static int PaintCalls;
    internal static int VisualFixes;

    /// <summary>Frames left in a "diag:watch" window. While it runs, every frame
    /// is checked for a planet showing the wrong thing.</summary>
    internal static int WatchFrames;

    private static int _reported;

    /// <summary>Called from Update - i.e. BEFORE our own LateUpdate gets a chance
    /// to correct anything. A mismatch seen here is a frame the player actually
    /// saw wrong, which is what "the planet is flashing" means.</summary>
    internal static void Watch(SpanNetworkPlanet[]? planets, SlotState state)
    {
        if (WatchFrames <= 0 || planets == null)
            return;
        WatchFrames--;
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

/// <summary>
/// The `unlocked` property is the other way a planet's display changes. Counted
/// to find out whether the game drives it per frame.
/// </summary>
[HarmonyPatch(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.unlocked), MethodType.Setter)]
public static class PlanetUnlockedSetPatch
{
    [HarmonyPostfix]
    public static void Postfix() => TrackerDiag.UnlockedSets++;
}

[HarmonyPatch(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.OnPointerClick))]
public static class PlanetClickPatch
{
    [HarmonyPrefix]
    public static bool Prefix(SpanNetworkPlanet __instance)
    {
        try
        {
            var title = TrackerView.TitleOf(__instance);
            var mission = TrackerView.MissionByTitle(title);
            if (mission == 0)
                return true;   // not one of ours (e.g. the tutorial) - native behavior
            if (!MissionRules.IsUnlocked(ModCore.Client.State, mission))
            {
                ModCore.Log.LogInfo($"PLANET CLICK IGNORED: '{title}' locked");
                return false;  // skip the native click -> no popup
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// Answers the map's display-time completion query from AP checked-state so the
/// native completion indicators reflect the multiworld, not local save data.
/// </summary>
[HarmonyPatch(typeof(SpanNetworkPlanet), nameof(SpanNetworkPlanet.FakeIsMissionObjectiveComplete))]
public static class FakeCompletePatch
{
    [HarmonyPrefix]
    public static bool Prefix(string guid, int obj, ref bool __result)
    {
        if (!TrackerView.GuidToMission.TryGetValue(guid ?? "", out var mission))
            return true;   // unknown planet -> let the game answer
        if (obj < 0 || obj >= MissionRules.ObjectiveTypes.Length)
            return true;
        var location = MissionRules.ObjectiveLocation(mission, obj);
        var state = ModCore.Client.State;
        if (!MissionRules.IsLocation(state, location))
            return true;   // non-tracked objective -> native behavior
        __result = state.CheckedLocations.Contains(location);
        return false;
    }
}
