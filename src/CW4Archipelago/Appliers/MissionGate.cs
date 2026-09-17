using CW4Archipelago.Core;
using HarmonyLib;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Blocks launching or save-loading a mission whose Mission Unlock is not held.
/// Harmony prefixes on GalaxyMissionPanel.OnLaunch and
/// MissionPanelLoadBoxRow.OnLoad (both proven safe in the probe).
/// </summary>
[HarmonyPatch]
public static class MissionGate
{
    /// <summary>Whether a specifier may be launched or save-loaded.
    ///
    /// TWO SEPARATE QUESTIONS, and conflating them is a bug in both directions.
    ///
    /// "Is this one of ours?" - a specifier the plugin cannot resolve at all
    /// (colonies, custom maps) belongs to the game, and so does a mission this
    /// SEED does not contain. A campaign-only seed must leave all 26 SPAN
    /// Experiments playable from the game's own SPAN menu: they are not locked,
    /// they are simply not in the randomizer.
    ///
    /// "Is it unlocked?" - only then, and only for the 20 in the roster.
    ///
    /// This USED to fail open for anything that was not "storyN", which meant a
    /// SPAN map swapped into the spiral was launchable regardless of AP state.
    /// </summary>
    public static bool Allowed(string? specifier)
    {
        if (!MissionRules.TryParseSpecifier(specifier, out var mission))
            return true;
        var state = ModCore.Client.State;
        if (!MissionRules.InSeed(state, mission))
            return true;
        return MissionRules.IsUnlocked(state, mission);
    }

    [HarmonyPatch(typeof(GalaxyMissionPanel), nameof(GalaxyMissionPanel.OnLaunch))]
    [HarmonyPrefix]
    public static bool OnLaunchPrefix(string fileName)
    {
        if (Allowed(fileName))
            return true;
        ModCore.Log.LogInfo($"MISSION BLOCKED (launch): '{fileName}' locked");
        return false;
    }

    [HarmonyPatch(typeof(MissionPanelLoadBoxRow), nameof(MissionPanelLoadBoxRow.OnLoad))]
    [HarmonyPrefix]
    public static bool OnLoadPrefix(MissionPanelLoadBoxRow __instance)
    {
        string? spec = null;
        try { spec = __instance.missionPanelLoadBox?.specifier; } catch { }
        if (string.IsNullOrEmpty(spec) || Allowed(spec))
            return true;
        ModCore.Log.LogInfo($"SAVE LOAD BLOCKED: '{spec}' locked");
        return false;
    }
}
