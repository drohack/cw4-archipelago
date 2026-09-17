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

    /// <summary>The Farsite campaign, missions 1..20. Declared separately from
    /// <see cref="Titles"/> because "is this a campaign mission" is a question
    /// several things below have to ask, and because static field initialisers
    /// run in textual order - this one has to exist before Titles is built.</summary>
    private static readonly Dictionary<int, string> CampaignTitles = new()
    {
        [1] = "Farsite", [2] = "Home", [3] = "Not My Mars", [4] = "Ruins Repurposed",
        [5] = "We Know Nothing", [6] = "We Were Never Alone", [7] = "Hints", [8] = "Serious",
        [9] = "More and More", [10] = "War and Peace", [11] = "Shattered", [12] = "Archon",
        [13] = "The Experiment", [14] = "Somewhere in Spacetime", [15] = "Tower of Darkness",
        [16] = "The Compound", [17] = "Sequence", [18] = "Wallis", [19] = "Founders",
        [20] = "Ever After",
    };

    /// <summary>Every mission this plugin can identify: the 20 Farsite missions
    /// and the 26 SPAN Experiments.
    ///
    /// ALL 46 ARE ALWAYS HERE, whatever the seed contains. This dictionary is
    /// simultaneously the planet identity, the unlock item name and the
    /// location-name prefix, and all three are class-level facts on the apworld
    /// side too - ids there are positional and cannot vary with a yaml option. So
    /// the toggle decides which missions a SEED uses, never which exist.</summary>
    public static readonly IReadOnlyDictionary<int, string> Titles = BuildTitles();

    private static IReadOnlyDictionary<int, string> BuildTitles()
    {
        var all = new Dictionary<int, string>(CampaignTitles);
        foreach (var entry in SpanMissionTable.Titles)
            all[entry.Key] = entry.Value;
        return all;
    }

    /// <summary>Launch specifier back to mission number, for both families.</summary>
    private static readonly Dictionary<string, int> MissionBySpecifier = BuildBySpecifier();

    private static Dictionary<string, int> BuildBySpecifier()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mission in CampaignTitles.Keys)
            map[$"story{mission}"] = mission;
        foreach (var entry in SpanMissionTable.Guids)
            map[entry.Value] = entry.Key;
        return map;
    }

    /// <summary>CW4's six objective slots are fixed by type.</summary>
    public static readonly string[] ObjectiveTypes = { "Nullify", "Totems", "Reclaim", "Hold", "Collect", "Custom" };

    /// <summary>What the GAME calls this mission - what LoadGame is handed.
    ///
    /// "storyN" for the campaign; a SPAN map is its guid, which was verified to
    /// be its specifier by booting all 26.</summary>
    public static string Specifier(int mission)
        => SpanMissionTable.Guids.TryGetValue(mission, out var guid) ? guid : $"story{mission}";

    public static bool TryParseSpecifier(string? specifier, out int mission)
    {
        mission = 0;
        return !string.IsNullOrEmpty(specifier)
               && MissionBySpecifier.TryGetValue(specifier!, out mission);
    }

    /// <summary>Whether this is one of the 26 SPAN Experiments.</summary>
    public static bool IsSpan(int mission) => mission >= SpanMissionTable.FirstMission;

    private static SlotData? _rosterSource;
    private static List<int> _roster = new();

    /// <summary>The 20 missions this seed contains, in level-select slot order.
    ///
    /// A seed generated before the roster key existed sends none, and then the
    /// answer is the campaign - which is what such a seed was built with. So
    /// every caller can treat this as always correct rather than branching on
    /// whether SPAN is on.
    ///
    /// Memoised on the SlotData instance: this is asked on every galaxy paint
    /// and the answer only changes when a different slot is loaded.</summary>
    public static IReadOnlyList<int> Roster(SlotState state)
    {
        var hints = state.Hints;
        if (ReferenceEquals(hints, _rosterSource))
            return _roster;

        var roster = new List<int>();
        foreach (var specifier in hints.MissionRoster)
            if (TryParseSpecifier(specifier, out var mission))
                roster.Add(mission);
        if (roster.Count == 0)
            for (int mission = 1; mission <= 20; mission++)
                roster.Add(mission);

        _rosterSource = hints;
        _roster = roster;
        return roster;
    }

    /// <summary>Whether this seed contains this mission at all.
    ///
    /// The distinction that matters to <see cref="MissionGate"/>: a mission the
    /// seed does not contain is not LOCKED, it is simply not part of the
    /// randomizer, and the game's own menus must keep working for it. Gating on
    /// "has no unlock item" instead would lock every SPAN Experiment out of the
    /// game's own SPAN menu on a campaign-only seed.</summary>
    public static bool InSeed(SlotState state, int mission)
        => Roster(state).Contains(mission);

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

    /// <summary>The name of one INSTANCE of a counted objective.
    ///
    /// `instance` identifies a particular STRUCTURE, not how many have been
    /// finished. It is that structure's rank when the mission's structures of
    /// that kind are ordered by map cell, (cellY, cellX) ascending - see
    /// InstanceIndex, which is the only thing allowed to compute it.
    ///
    /// This used to be activation order ("the game cannot tell one totem from
    /// another, so the Nth activation sends the Nth check"), and it can: every
    /// structure carries UnitManager.cellX/cellY. The old rule attached the
    /// apworld's per-instance requirements to whichever structure happened to be
    /// finished Nth, which showed Shattered's mover-gated totem as free.</summary>
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
        // Over every mission that EXISTS, not 1..20. A mixed seed's beaten count
        // has to include its SPAN missions or the finale stays locked behind a
        // number the player cannot reach. Missions the seed does not contain have
        // no checked location and contribute nothing, so this is safe to run wide.
        foreach (var mission in Titles.Keys)
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
    private static readonly Dictionary<int, int[]> CampaignObjectives = new()
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

    /// <summary>The campaign's authored objectives plus the 26 measured SPAN
    /// ones. DECLARED AFTER CampaignObjectives on purpose: C# runs static field
    /// initialisers in textual order, so building this above the table it copies
    /// would read a null and throw at type load.</summary>
    public static readonly IReadOnlyDictionary<int, int[]> MissionObjectives = BuildMissionObjectives();

    private static IReadOnlyDictionary<int, int[]> BuildMissionObjectives()
    {
        var all = new Dictionary<int, int[]>(CampaignObjectives);
        foreach (var entry in SpanMissionTable.Objectives)
            all[entry.Key] = entry.Value;
        return all;
    }

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
