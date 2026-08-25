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
        bool inLogic = Satisfies(state, state.Hints.ForMission(MissionRules.Specifier(mission)))
                       && Satisfies(state, state.Hints.ForLocation(location));
        return inLogic ? TrackerStatus.InLogic : TrackerStatus.OutOfLogic;
    }

    public static TrackerStatus MissionStatus(SlotState state, int mission)
    {
        var locations = MissionRules.LocationsFor(state, mission);
        if (locations.Count == 0)
            return MissionRules.IsUnlocked(state, mission) ? TrackerStatus.InLogic : TrackerStatus.Locked;

        var statuses = locations.Select(l => LocationStatus(state, mission, l)).ToList();
        if (statuses.All(s => s == TrackerStatus.Done))
            return TrackerStatus.Done;
        if (statuses.Any(s => s == TrackerStatus.Locked))
            return TrackerStatus.Locked;

        var remaining = statuses.Where(s => s != TrackerStatus.Done).ToList();
        if (remaining.All(s => s == TrackerStatus.InLogic))
            return TrackerStatus.InLogic;
        if (remaining.All(s => s == TrackerStatus.OutOfLogic))
            return TrackerStatus.OutOfLogic;
        return TrackerStatus.Partial;
    }
}
