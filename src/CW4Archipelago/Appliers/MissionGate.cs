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
    /// <summary>Whether a storyN specifier may be launched/loaded.</summary>
    public static bool Allowed(string? specifier)
    {
        // Non-story specifiers (SPAN, colonies, custom) are left to the game.
        if (!MissionRules.TryParseSpecifier(specifier, out var mission))
            return true;
        return MissionRules.IsUnlocked(ModCore.Client.State, mission);
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
