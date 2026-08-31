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

    /// <summary>The objective slots each mission actually CONTAINS, in ascending
    /// order. Independent of Archipelago: this is what is in the map.
    ///
    /// Mirrors the apworld's INSTANCE_COUNTS / RECLAIM_MISSIONS / CUSTOM_MISSIONS
    /// (apworld/cw4/locations.py), and was checked against the live game: the
    /// mission map's own authored icon set agrees with it on nineteen of the
    /// twenty missions. The exception is Farsite, where the map draws a Totems
    /// icon and the mission has no totems at all - measured, and vanilla does the
    /// same. MissionObjectivesTests pins that comparison so a game update that
    /// changes either side shows up as a failed test rather than a wrong map.
    ///
    /// This exists because the AP location list answers "which objectives are
    /// CHECKS", which is a per-seed question and is unknown until a server says
    /// so. "Which objectives does this mission have" is neither of those things,
    /// and the map should not be showing a totems icon on a mission with no
    /// totems just because nobody is connected yet.</summary>
    public static readonly IReadOnlyDictionary<int, int[]> MissionObjectives = new Dictionary<int, int[]>
    {
        [1] = new[] { 4, 5 },        [2] = new[] { 0, 1, 4 },
        [3] = new[] { 0, 1, 4 },     [4] = new[] { 0, 1, 4 },
        [5] = new[] { 0, 1, 4 },     [6] = new[] { 0, 2 },
        [7] = new[] { 0, 1, 2, 4 },  [8] = new[] { 0, 1, 2 },
        [9] = new[] { 0, 1, 2, 4 },  [10] = new[] { 0, 1, 2, 4 },
        [11] = new[] { 0, 1, 4 },    [12] = new[] { 0, 1, 2, 4 },
        [13] = new[] { 0, 1, 4 },    [14] = new[] { 0, 1, 2, 4 },
        [15] = new[] { 0, 1, 2, 4 }, [16] = new[] { 0, 1, 4 },
        [17] = new[] { 0, 2, 4 },    [18] = new[] { 0, 1, 2, 4 },
        [19] = new[] { 0, 1, 4, 5 }, [20] = new[] { 0, 1, 2, 5 },
    };

    /// <summary>Which objective slots this slot actually has checks for, in
    /// ascending order.
    ///
    /// The mission map draws one icon per objective in the MAP FILE's authored
    /// list, which is not always the mission's real objective set. Farsite draws
    /// a Totems icon and has no totems at all - measured live, and vanilla does
    /// the same - so that icon stands for a category with no checks while its two
    /// caches and its custom objective get no icon at all.
    ///
    /// This is the answer the map should be drawing instead: the objective slots
    /// that have locations in THIS slot. On nineteen of the twenty missions it
    /// agrees with what the game already draws, so it is a no-op there.</summary>
    public static List<int> ExpectedObjectiveIndices(SlotState state, int mission)
    {
        var found = new List<int>();
        for (int i = 0; i < ObjectiveTypes.Length; i++)
            if (LocationsForObjective(state, mission, i).Count > 0)
                found.Add(i);
        if (found.Count > 0)
            return found;

        // Nothing known for this mission, which means no server has told us yet -
        // AllLocations is empty until a connection, and a cached slot only covers
        // the seed it came from. Fall back to what the MISSION contains.
        //
        // Returning nothing here was a real bug: the map leaves Farsite unlocked
        // (it is the default starter) and so displayed vanilla's totems icon on a
        // mission with no totems, every time the game was opened without a
        // connection. The fallback is not a guess - it is measured, and it agrees
        // with the game's own icons on nineteen of twenty missions.
        return MissionObjectives.TryGetValue(mission, out var authored)
            ? new List<int>(authored)
            : found;
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
