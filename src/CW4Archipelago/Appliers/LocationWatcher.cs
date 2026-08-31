using System;
using CW4Archipelago.Core;
using HarmonyLib;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Watches the live mission and turns progress into Archipelago location checks.
///
/// Counted objectives - nullify, totems, collect - send ONE CHECK PER INSTANCE,
/// and progress is read from the game's own live SETS rather than from
/// MissionObjectiveData.count:
///
///   caches   maxMustCollect - mustCollect.Count
///   totems   totems where totemComplete
///   nullify  (count at mission start) - nullifiableUnits.Count
///
/// The counter cannot be trusted for this. Farsite proves it: its Collect slot
/// reads enabled=False with count=0 while two caches sit on the map and are
/// perfectly collectable, so checks driven off the counter would be dead there.
/// The same doubt applies to every OPTIONAL objective, which is 104 of the 120
/// nullify targets - far too much to leave resting on a field that is not
/// guaranteed to move. The sets are what the game itself counts.
///
/// Instances are numbered by activation order, because the game cannot tell one
/// totem from another.
///
/// Reclaim and Custom are not counts (a percentage and a script), so they stay a
/// single check on completion. Finishing the final mission sends the goal.
/// </summary>
public sealed class LocationWatcher
{
    private IntPtr _lastGameSpace = IntPtr.Zero;
    private int _mission;
    private bool[]? _objectiveDone;
    private int[]? _sentUpTo;
    private int _nullifiableAtStart = -1;

    /// <summary>Set by the game-event patches to scan on the next tick.</summary>
    internal static volatile bool Poke;

    /// <summary>How often each patch has actually FIRED. Reported by the debug
    /// channel's "perf".
    ///
    /// Applying is not firing: a patch on a private method or a property setter
    /// can attach to a method IL2CPP never actually calls, and the safety poll
    /// would quietly cover for it forever. These counters are how a test tells
    /// the difference.</summary>
    internal static int TotemPokes;
    internal static int CachePokes;

    /// <summary>Frames between safety-net scans. Sixty is about once a second.</summary>
    private const int SafetyInterval = 60;
    private int _countdown;
    private bool _missionComplete;

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;
        var world = gs.world;
        if (world == null) return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _mission = ResolveMission(gs);
            _objectiveDone = null;
            _sentUpTo = null;
            _nullifiableAtStart = -1;
            _missionComplete = false;
            ModCore.Log.LogInfo($"LocationWatcher: mission {_mission} ('{SpecifierOf(_mission)}')");
        }
        if (_mission == 0)
            return;

        var objs = world.missionObjectives;
        if (objs != null)
        {
            if (_objectiveDone == null || _objectiveDone.Length != objs.Length)
                _objectiveDone = new bool[objs.Length];
            if (_sentUpTo == null || _sentUpTo.Length != objs.Length)
                _sentUpTo = new int[objs.Length];

            for (int i = 0; i < objs.Length && i < MissionRules.ObjectiveTypes.Length; i++)
            {
                if (!MissionRules.IsCounted(i))
                    SendSingleObjective(world, i);
            }
        }

        // Scan when the game tells us something happened, and once a second
        // regardless.
        //
        // The patches (TotemCompletePatch, CacheDestroyedPatch) make totems and
        // caches respond immediately. The slow poll is a SAFETY NET and is the
        // important part of this design: a Harmony patch on a property setter or
        // a private method can silently fail to apply under IL2CPP, and checks
        // that stop firing altogether would be far worse than checks that arrive
        // a second late. Nullification has no hook at all - nothing on
        // UnitManager or GameSpace is named for it - so it relies on the poll.
        if (Poke || --_countdown <= 0)
        {
            Poke = false;
            _countdown = SafetyInterval;
            SendCounted(0, NullifiedCount(gs));
            SendCounted(1, TotemsCompleteCount(gs));
            SendCounted(4, CachesCollectedCount(gs));
        }

        bool mc = false;
        try { mc = world.IsMissionComplete(); } catch { }
        if (mc && !_missionComplete)
        {
            _missionComplete = true;
            if (_mission == MissionRules.FinalMission)
            {
                // Logic gates the goal on a count of missions beaten, so apply
                // the same rule here. Beating the finale early is not a win -
                // the generator never considered that reachable, and claiming it
                // would desync this slot from the multiworld's view of it.
                var state = ModCore.Client.State;
                if (MissionRules.FinaleCounts(state))
                {
                    ModCore.Log.LogInfo("LocationWatcher: FINAL mission complete -> goal");
                    ModCore.Client.SendGoal();
                }
                else
                {
                    int have = MissionRules.MissionsBeaten(state);
                    int need = state.Hints.MissionsForFinale;
                    ModCore.Log.LogWarning(
                        $"LocationWatcher: finale complete but the goal needs {need} missions beaten ({have} so far)");
                    ModCore.EnqueueToast($"Finale held: {have}/{need} missions beaten");
                }
            }
            else
            {
                SendCheck(MissionRules.MissionCompleteLocation(_mission));
            }
        }
    }

    /// <summary>Send a check for every new instance of a counted objective.
    ///
    /// A DECREASE means the mission was restarted, not that checks should be
    /// re-sent: reset the high-water mark and wait for progress to climb again.
    /// Sending is idempotent anyway (MarkChecked filters), but rewinding keeps
    /// the log honest about what actually happened.</summary>
    private void SendCounted(int index, int progress)
    {
        if (_sentUpTo == null || index >= _sentUpTo.Length || progress < 0)
            return;

        if (progress < _sentUpTo[index])
        {
            _sentUpTo[index] = progress;
            return;
        }
        while (_sentUpTo[index] < progress)
        {
            int next = _sentUpTo[index] + 1;
            _sentUpTo[index] = next;
            SendCheck(MissionRules.InstanceLocation(_mission, index, next));
        }
    }

    /// <summary>Caches taken so far. mustCollect holds the ones still wanted and
    /// maxMustCollect the total, and both are live regardless of whether the
    /// Collect objective is enabled.</summary>
    private static int CachesCollectedCount(GameSpace gs)
    {
        try
        {
            int remaining = 0;
            foreach (var u in gs.mustCollect) if (u != null) remaining++;
            return gs.maxMustCollect - remaining;
        }
        catch { return -1; }
    }

    private static int TotemsCompleteCount(GameSpace gs)
    {
        try
        {
            int done = 0;
            foreach (var t in gs.totems)
            {
                if (t == null) continue;
                try { if (t.totemComplete) done++; } catch { }
            }
            return done;
        }
        catch { return -1; }
    }

    /// <summary>Nullified so far. The set only shrinks, so progress is the drop
    /// from its size at mission start - captured on the first tick of the
    /// mission, before the player can have nullified anything.</summary>
    private int NullifiedCount(GameSpace gs)
    {
        try
        {
            int remaining = 0;
            foreach (var u in gs.nullifiableUnits) if (u != null) remaining++;
            if (_nullifiableAtStart < 0)
                _nullifiableAtStart = remaining;
            return _nullifiableAtStart - remaining;
        }
        catch { return -1; }
    }

    /// <summary>Reclaim and Custom are not counts, so they send once when the
    /// objective completes.</summary>
    private void SendSingleObjective(World world, int index)
    {
        if (_objectiveDone == null) return;
        bool done = false;
        try { done = world.IsMissionObjectiveComplete(index); } catch { }
        if (!done || _objectiveDone[index]) return;
        _objectiveDone[index] = true;
        SendCheck(MissionRules.ObjectiveLocation(_mission, index));
    }

    private void SendCheck(string location)
    {
        var state = ModCore.Client.State;
        if (!MissionRules.IsLocation(state, location))
            return;   // not a real location in this slot (e.g. non-required objective)
        if (state.MarkChecked(location, ModCore.Client.Connected))
        {
            ModCore.Log.LogInfo($"LOCATION CHECK: {location}");
            ModCore.Client.SendChecks(new[] { location });
        }
    }

    private static int ResolveMission(GameSpace gs)
    {
        // gs.specifier is the live current-mission id (storyN), reliable on
        // both the boot and resume-from-save paths.
        try
        {
            if (MissionRules.TryParseSpecifier(gs.specifier, out var n))
                return n;
        }
        catch { }
        return 0;
    }

    private static string SpecifierOf(int mission) => mission == 0 ? "?" : MissionRules.Specifier(mission);
}

/// <summary>
/// A totem finished, so look for new checks now rather than waiting for the
/// safety poll.
///
/// Patching the property SETTER is deliberate: there is no event, and polling
/// gs.totems every frame was walking a set of up to eight units sixty times a
/// second to notice something that happens a handful of times per mission.
///
/// If this patch fails to apply - property setters can be inlined under IL2CPP -
/// nothing breaks: LocationWatcher's once-a-second poll still finds it, and
/// Plugin.TryPatch logs the failure.
/// </summary>
[HarmonyPatch(typeof(Totem), nameof(Totem.totemComplete), MethodType.Setter)]
public static class TotemCompletePatch
{
    [HarmonyPostfix]
    public static void Postfix(bool value)
    {
        if (value)
        {
            LocationWatcher.TotemPokes++;
            LocationWatcher.Poke = true;
        }
    }
}

/// <summary>
/// A collected info cache is destroyed, so look for new checks now.
///
/// The hook was originally InfoCache.Retrieved, which was WRONG and was caught
/// only by a real pickup: after a human collected the cache in story2,
/// cachePokes was still 0 while mustCollect had gone 1 -> 0 and the Collect
/// objective read DONE. Retrieved is never called on the pickup path at all -
/// it sets the cache's own `retrieved` flag and nothing else, and the collected
/// unit is simply gone from gs.units. Every cache check up to that point was
/// being delivered by the once-a-second safety poll, which is exactly the
/// silent failure the poke counters were added to expose.
///
/// DestroyUnit is what the pickup actually does. It also fires if a cache dies
/// some other way, which is harmless: the postfix only asks for a rescan, and
/// SendCounted decides whether anything is owed.
///
/// If this patch fails to apply the poll still finds it, a second late.
/// </summary>
[HarmonyPatch(typeof(InfoCache), nameof(InfoCache.DestroyUnit))]
public static class CacheDestroyedPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        LocationWatcher.CachePokes++;
        LocationWatcher.Poke = true;
    }
}
