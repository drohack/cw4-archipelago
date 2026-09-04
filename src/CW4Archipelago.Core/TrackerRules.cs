using System.Collections.Generic;
using System.Linq;

namespace CW4Archipelago.Core;

/// <summary>
/// Archipelago / PopTracker convention:
/// Locked = red (not accessible), OutOfLogic = yellow (reachable, not in logic),
/// InLogic = green, Partial = orange (some remaining checks in logic, some not),
/// Done = grey (finished).
/// </summary>
public enum TrackerStatus
{
    Locked,
    OutOfLogic,
    InLogic,
    Partial,
    Done,
}

public static class TrackerRules
{
    public static bool Satisfies(SlotState state, IReadOnlyList<IReadOnlyList<string>> groups)
        => groups.All(group => group.Any(state.Has));

    public static TrackerStatus LocationStatus(SlotState state, int mission, string location)
    {
        if (state.CheckedLocations.Contains(location))
            return TrackerStatus.Done;
        if (!MissionRules.IsUnlocked(state, mission))
            return TrackerStatus.Locked;

        // A location entry is COMPLETE - the apworld folds the mission's own
        // requirements into it, except where a per-instance waiver deliberately
        // drops them. This used to AND the mission's requirements back in,
        // which defeated the waiver: Farsite's first cache is free, and the
        // mission needs a weapon, so the free cache read as out of logic.
        if (!Satisfies(state, state.Hints.ForLocationStrict(location)))
            return TrackerStatus.Locked;                 // cannot be reached at all
        if (!Satisfies(state, state.Hints.ForLocation(location)))
            return TrackerStatus.OutOfLogic;             // reachable, not promised
        return TrackerStatus.InLogic;
    }

    public static TrackerStatus MissionStatus(SlotState state, int mission)
    {
        // The finale reads as LOCKED until enough missions are beaten, even
        // though its own checks are reachable. It cannot be won yet, and a map
        // that showed it as playable would be lying about the one thing the
        // player most needs to know.
        if (mission == MissionRules.FinalMission && !MissionRules.FinaleCounts(state))
            return TrackerStatus.Locked;

        var locations = MissionRules.LocationsFor(state, mission);
        if (locations.Count == 0)
            return MissionRules.IsUnlocked(state, mission) ? TrackerStatus.InLogic : TrackerStatus.Locked;

        return Aggregate(locations.Select(l => LocationStatus(state, mission, l)));
    }

    /// <summary>One status for a group of locations, by the tracker convention.
    /// Used for a whole mission and for a single map marker, which stands for
    /// every instance of one objective.</summary>
    public static TrackerStatus Aggregate(IEnumerable<TrackerStatus> input)
    {
        var statuses = input.ToList();
        if (statuses.Count == 0)
            return TrackerStatus.InLogic;
        if (statuses.All(s => s == TrackerStatus.Done))
            return TrackerStatus.Done;

        // Judge on what is LEFT to do. One unreachable check used to paint the
        // whole mission red, which hid the fact that another check on it could
        // be taken right now - the single most useful thing the map can say.
        // A mission with both is Partial (orange); red is for a mission with
        // nothing left that can be reached.
        var remaining = statuses.Where(s => s != TrackerStatus.Done).ToList();
        if (remaining.All(s => s == TrackerStatus.Locked))
            return TrackerStatus.Locked;
        if (remaining.All(s => s == TrackerStatus.InLogic))
            return TrackerStatus.InLogic;
        if (remaining.All(s => s == TrackerStatus.OutOfLogic))
            return TrackerStatus.OutOfLogic;
        return TrackerStatus.Partial;
    }
}
