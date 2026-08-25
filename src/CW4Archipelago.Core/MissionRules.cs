using System;
using System.Collections.Generic;
using System.Linq;

namespace CW4Archipelago.Core;

/// <summary>Mission identity: storyN specifiers, titles, unlock items, location names.</summary>
public static class MissionRules
{
    public const int FinalMission = 20;

    public static readonly IReadOnlyDictionary<int, string> Titles = new Dictionary<int, string>
    {
        [1] = "Farsite", [2] = "Home", [3] = "Not My Mars", [4] = "Ruins Repurposed",
        [5] = "We Know Nothing", [6] = "We Were Never Alone", [7] = "Hints", [8] = "Serious",
        [9] = "More and More", [10] = "War and Peace", [11] = "Shattered", [12] = "Archon",
        [13] = "The Experiment", [14] = "Somewhere in Spacetime", [15] = "Tower of Darkness",
        [16] = "The Compound", [17] = "Sequence", [18] = "Wallis", [19] = "Founders",
        [20] = "Ever After",
    };

    /// <summary>CW4's six objective slots are fixed by type.</summary>
    public static readonly string[] ObjectiveTypes = { "Nullify", "Totems", "Reclaim", "Hold", "Collect", "Custom" };

    public static string Specifier(int mission) => $"story{mission}";

    public static bool TryParseSpecifier(string? specifier, out int mission)
    {
        mission = 0;
        if (specifier == null || !specifier.StartsWith("story", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(specifier.Substring(5), out mission) && Titles.ContainsKey(mission);
    }

    public static string UnlockItem(int mission) => $"Mission Unlock: {Titles[mission]}";

    public static bool IsStarter(SlotState state, int mission)
        => state.Hints.StarterMissions.Contains(Specifier(mission));

    public static bool IsUnlocked(SlotState state, int mission)
        => IsStarter(state, mission) || state.Has(UnlockItem(mission));

    public static string ObjectiveLocation(int mission, int objectiveIndex)
        => $"{Titles[mission]} - {ObjectiveTypes[objectiveIndex]}";

    public static string MissionCompleteLocation(int mission) => $"{Titles[mission]} - Mission Complete";

    /// <summary>All of this slot's locations belonging to the mission (from the server list).</summary>
    public static List<string> LocationsFor(SlotState state, int mission)
    {
        var prefix = Titles[mission] + " - ";
        return state.AllLocations.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>Whether a mission's objective index is a location in this slot.</summary>
    public static bool IsLocation(SlotState state, string location) => state.AllLocations.Contains(location);
}
