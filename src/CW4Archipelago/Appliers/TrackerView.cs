using System.Collections.Generic;
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

    private int _lastSignature = -1;

    public static Color StatusColor(TrackerStatus status) => status switch
    {
        TrackerStatus.Locked => Red,
        TrackerStatus.OutOfLogic => Yellow,
        TrackerStatus.Done => Grey,
        _ => Green,
    };

    /// <summary>Applied in LateUpdate (world-space quads track camera pan/zoom).</summary>
    public void ApplyTints()
    {
        if (ModCore.CurrentScene != "Galaxy")
            return;
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null || planets.Length == 0)
            return;

        var state = ModCore.Client.State;
        // Repaint only when the state that affects colors changes.
        int sig = state.ReceivedItems.Count * 31 + state.CheckedLocations.Count;
        bool refreshNeeded = sig != _lastSignature;
        _lastSignature = sig;

        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p))
                continue;
            int mission = ResolveMission(p);
            if (mission == 0)
                continue;

            bool unlocked = MissionRules.IsUnlocked(state, mission);
            try
            {
                if (p.forceUnlocked != unlocked)
                {
                    p.forceUnlocked = unlocked;
                    refreshNeeded = true;
                }
            }
            catch { }

            if (refreshNeeded)
            {
                try { p.Refresh(); } catch { }
            }

            // Enforce mutually-exclusive visuals AFTER Refresh so we win over
            // the native repaint (which would re-show the sphere for planets
            // the player's vanilla save has unlocked). Locked = only the "?";
            // unlocked = only the sphere + colored objective glyphs.
            SetVisuals(p, unlocked);
            if (unlocked)
                ColorGlyphs(p, mission, state);
        }
    }

    private static void SetVisuals(SpanNetworkPlanet p, bool unlocked)
    {
        try { if (p.lockedPlanet != null) p.lockedPlanet.gameObject.SetActive(!unlocked); } catch { }
        try { if (p.planet != null) p.planet.gameObject.SetActive(unlocked); } catch { }
        try { if (p.objectiveContainer != null) p.objectiveContainer.gameObject.SetActive(unlocked); } catch { }
    }

    private void ColorGlyphs(SpanNetworkPlanet p, int mission, SlotState state)
    {
        Transform container;
        try { container = p.objectiveContainer; } catch { return; }
        if (container == null)
            return;
        var markers = container.GetComponentsInChildren<SpanNetworkPlanetObjective>(true);
        if (markers == null)
            return;
        foreach (var m in markers)
        {
            if (!GameUtil.IsAlive(m))
                continue;
            int objIndex;
            try { objIndex = m.objective; } catch { continue; }
            if (objIndex < 0 || objIndex >= MissionRules.ObjectiveTypes.Length)
                continue;
            var location = MissionRules.ObjectiveLocation(mission, objIndex);
            if (!MissionRules.IsLocation(state, location))
                continue;   // not a tracked (required) objective
            var color = StatusColor(TrackerRules.LocationStatus(state, mission, location));
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
