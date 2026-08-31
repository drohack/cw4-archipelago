using System;
using System.Collections.Generic;
using System.Linq;

namespace CW4Archipelago.Core;

/// <summary>Mission identity: storyN specifiers, titles, unlock items, location names.</summary>
public static class MissionRules
{
    /// <summary>Founders, not Ever After. Ever After plays as an epilogue rather
    /// than a climax, so it is an ordinary mission and Founders carries the goal.
    /// See docs/design/mission-requirements-worksheet.md, mission 20.</summary>
    public const int FinalMission = 19;

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

    /// <summary>Prefix used by a counted objective's per-instance locations.
    /// The apworld names them by instance, not by objective type, so slot 4
    /// (Collect) becomes "Cache 1", "Cache 2" and so on.</summary>
    public static string InstanceKind(int objectiveIndex) => objectiveIndex switch
    {
        0 => "Nullify",
        1 => "Totem",
        4 => "Cache",
        _ => "",
    };

    /// <summary>The Nth check of a counted objective. Instances are numbered by
    /// ACTIVATION ORDER: the game cannot tell one totem from another, so the Nth
    /// activation sends the Nth check.</summary>
    public static string InstanceLocation(int mission, int objectiveIndex, int instance)
        => $"{Titles[mission]} - {InstanceKind(objectiveIndex)} {instance}";

    /// <summary>Objectives that are a single check rather than a count: Reclaim
    /// is a percentage of the map, Custom is mission-scripted.</summary>
    public static bool IsCounted(int objectiveIndex) => InstanceKind(objectiveIndex).Length > 0;

    public static string ObjectiveLocation(int mission, int objectiveIndex)
        => $"{Titles[mission]} - {ObjectiveTypes[objectiveIndex]}";

    public static string MissionCompleteLocation(int mission) => $"{Titles[mission]} - Mission Complete";

    /// <summary>How many missions this slot has completed, counted from the
    /// Mission Complete checks the server has acknowledged.
    ///
    /// The finale is excluded because it has no completion check of its own -
    /// finishing it IS the goal.</summary>
    public static int MissionsBeaten(SlotState state)
    {
        int n = 0;
        for (int mission = 1; mission <= 20; mission++)
        {
            if (mission == FinalMission) continue;
            if (state.CheckedLocations.Contains(MissionCompleteLocation(mission)))
                n++;
        }
        return n;
    }

    /// <summary>Whether finishing the finale should send the goal yet.
    ///
    /// Logic gates the Victory event on a count of beaten missions, so the
    /// client has to apply the same rule - otherwise a player who reaches the
    /// finale early could beat it and claim a goal the generator never
    /// considered reachable.</summary>
    public static bool FinaleCounts(SlotState state)
        => MissionsBeaten(state) >= state.Hints.MissionsForFinale;

    /// <summary>This slot's locations for ONE objective of one mission.
    ///
    /// Counted objectives are per instance ("Founders - Nullify 1..17"), so a
    /// single map marker stands for many locations. Building the old
    /// type-shaped name ("Founders - Nullify") matches nothing since the
    /// per-instance rename, which silently turned the map's glyph colouring into
    /// dead code - hence this.</summary>
    public static List<string> LocationsForObjective(SlotState state, int mission, int objectiveIndex)
    {
        var kind = InstanceKind(objectiveIndex);
        if (kind.Length > 0)
        {
            var prefix = $"{Titles[mission]} - {kind} ";
            return state.AllLocations
                .Where(l => l.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
        }
        // Reclaim and Custom are a single check each.
        var single = ObjectiveLocation(mission, objectiveIndex);
        return state.AllLocations.Contains(single)
            ? new List<string> { single }
            : new List<string>();
    }

    /// <summary>All of this slot's locations belonging to the mission (from the server list).</summary>
    public static List<string> LocationsFor(SlotState state, int mission)
    {
        var prefix = Titles[mission] + " - ";
        return state.AllLocations.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>Whether a mission's objective index is a location in this slot.</summary>
    public static bool IsLocation(SlotState state, string location) => state.AllLocations.Contains(location);
}
