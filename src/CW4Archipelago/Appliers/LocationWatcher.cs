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
    private int _lastNullifiableSeen = -999;

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
            _lastNullifiableSeen = -999;
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
            if (_queryFailed == null || _queryFailed.Length != objs.Length)
                _queryFailed = new bool[objs.Length];

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
            // Two sources, and the higher wins. The live sets give PARTIAL
            // progress; the game's own completion query is the only thing that
            // is right when a set cannot be counted.
            //
            // Measured on a resumed save (We Were Never Alone, 2026-09-05):
            // every one of the nine nullify targets was destroyed and the game
            // reported the objective complete, while all nine were still sitting
            // in GameSpace.nullifiableUnits. The set does NOT shrink on a
            // reload, so a rule that counts what is left can never notice - the
            // player got none of the nine checks. research-findings.md said
            // progress "is measured by that set shrinking"; that holds during
            // live play and not across a load.
            SendCounted(0, Math.Max(NullifiedCount(gs), AllIfObjectiveDone(world, 0)));
            SendCounted(1, Math.Max(TotemsCompleteCount(gs), AllIfObjectiveDone(world, 1)));
            SendCounted(4, Math.Max(CachesCollectedCount(gs), AllIfObjectiveDone(world, 4)));
        }

        bool mc = false;
        try { mc = world.IsMissionComplete(); } catch { }
        if (mc && !_missionComplete)
        {
            _missionComplete = true;
            DumpObjectiveSlots(world);
            SendRequiredObjectives();
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

    /// <summary>Every instance of an objective the GAME says is complete, or 0.
    ///
    /// The completion query is the one signal that survives a reload. Gated on
    /// this slot actually having locations for the objective, so a mission that
    /// reports a disabled objective as done cannot invent checks - and
    /// deliberately NOT gated on MissionObjectiveData.enabled, which is
    /// unreliable: Farsite's Collect slot reads enabled=False with two
    /// collectable caches on the map (research-findings.md).</summary>
    private int AllIfObjectiveDone(World world, int index)
    {
        try
        {
            if (!world.IsMissionObjectiveComplete(index))
                return 0;
            return MissionRules
                .LocationsForObjective(ModCore.Client.State, _mission, index).Count;
        }
        catch { return 0; }
    }

    /// <summary>Nullify targets destroyed, measured from ABSOLUTE state.
    ///
    /// This used to be the drop from the count at mission start, which is right
    /// only when the mission is started fresh. Resuming a save in which the
    /// targets were already destroyed meant the first tick saw an empty set, so
    /// the "start" was recorded as zero and progress stayed zero for the rest of
    /// the mission - a player who nullified all nine on We Were Never Alone,
    /// saved and came back received none of the nine checks. The rule now lives
    /// in NullifyRules where it is tested; see NullifyRulesTests.</summary>
    private int NullifiedCount(GameSpace gs)
    {
        try
        {
            // Count the SUPPRESSED targets. The set itself never shrinks and the
            // units are never destroyed, so anything that counts what is left
            // reports zero forever - see NullifyRules for the measurements.
            int total = 0, suppressed = 0;
            foreach (var u in gs.nullifiableUnits)
            {
                if (!GameUtil.IsAlive(u)) continue;
                total++;
                try { if (u.IsSuppressed()) suppressed++; } catch { }
            }

            int locations = MissionRules
                .LocationsForObjective(ModCore.Client.State, _mission, 0).Count;
            int done = NullifyRules.Completed(suppressed, locations);

            if (suppressed != _lastNullifiableSeen)
            {
                _lastNullifiableSeen = suppressed;
                ModCore.Log.LogInfo(
                    $"NULLIF: suppressed={suppressed}/{total} locations={locations} done={done}");
            }
            return done;
        }
        catch { return -1; }
    }

    /// <summary>Winning a mission means its REQUIRED objectives are done, so
    /// send their checks even if the per-objective query never said so.
    ///
    /// Farsite, in a real playthrough (seed 47803770604823003263): beaten in
    /// full, "Farsite - Mission Complete" sent, "Farsite - Custom" never sent.
    /// Both are read in the same Tick with the objective loop running FIRST, so
    /// IsMissionObjectiveComplete(5) was still false in the frame where
    /// IsMissionComplete() was true - the check could never have fired, and on
    /// a mission whose only required objective IS that one, it is also the
    /// mission's own check.
    ///
    /// Deliberately narrow: only slots the apworld lists as required for this
    /// mission, and only once the game itself says the mission is won. It never
    /// invents a check for an optional objective, which is why it reads the
    /// required table rather than the `enabled` flag - Farsite reports Reclaim
    /// enabled too, and has no Reclaim check at all.
    ///
    /// Logged as INFERRED, because this is a safety net over a query that
    /// should have worked: if these lines appear, the direct path missed.</summary>
    private void SendRequiredObjectives()
    {
        var state = ModCore.Client.State;
        var slots = state.Hints.RequiredObjectivesFor(MissionRules.Specifier(_mission));
        foreach (var i in slots)
        {
            if (i < 0 || i >= MissionRules.ObjectiveTypes.Length) continue;
            if (_objectiveDone != null && i < _objectiveDone.Length && _objectiveDone[i])
                continue;   // the direct path already sent it

            // COUNTED objectives (nullify, totems, collect) are one location per
            // INSTANCE - "Home - Nullify 1", not "Home - Nullify". This used to
            // build the single-check name for every objective, so IsLocation
            // rejected it and the loop skipped on: the safety net existed but
            // could only ever fire for Reclaim and Custom, the two that are
            // genuinely single checks. Winning the mission means every REQUIRED
            // objective is done, so every instance of one is owed.
            var owed = MissionRules.IsCounted(i)
                ? MissionRules.LocationsForObjective(state, _mission, i)
                : new System.Collections.Generic.List<string>
                    { MissionRules.ObjectiveLocation(_mission, i) };

            bool sentAny = false;
            foreach (var loc in owed)
            {
                if (state.CheckedLocations.Contains(loc)) continue;
                if (!MissionRules.IsLocation(state, loc)) continue;
                sentAny = true;
                ModCore.Log.LogInfo($"LocationWatcher: INFERRED '{loc}' from mission completion " +
                    $"(objective {i} {MissionRules.ObjectiveTypes[i]} never reported complete)");
                SendCheck(loc);
            }
            if (sentAny && _objectiveDone != null && i < _objectiveDone.Length)
                _objectiveDone[i] = true;
        }
    }

    private bool[]? _queryFailed;

    /// <summary>What every objective slot reports the moment the mission is
    /// won, next to whether its check actually went out.
    ///
    /// Farsite was beaten in full and its Custom check never arrived, which
    /// leaves several indistinguishable causes: the slot may be outside
    /// missionObjectives, its completion query may return false or throw, or
    /// the location may not belong to this slot. One line at the moment of
    /// victory separates them, and costs nothing on a normal run.</summary>
    private void DumpObjectiveSlots(World world)
    {
        try
        {
            var slots = world.missionObjectives;
            int n = slots == null ? -1 : slots.Length;
            ModCore.Log.LogInfo($"OBJDUMP: mission {_mission} complete, {n} objective slot(s)");
            if (slots == null) return;
            var state = ModCore.Client.State;
            for (int i = 0; i < slots.Length; i++)
            {
                bool done = false; string err = "";
                try { done = world.IsMissionObjectiveComplete(i); }
                catch (Exception e) { err = $" query threw: {e.Message}"; }
                int count = -1;
                try { count = slots[i].count; } catch { }
                bool en = false;
                try { en = slots[i].enabled; } catch { }
                string kind = i < MissionRules.ObjectiveTypes.Length
                    ? MissionRules.ObjectiveTypes[i] : "?";
                // Counted objectives are one location per INSTANCE. Asking
                // AllLocations for the single-check name reported isLocation=False
                // for every nullify, totem and collect objective in the game -
                // a diagnostic that lied in exactly the case it exists to explain.
                var locs = i < MissionRules.ObjectiveTypes.Length
                    ? (MissionRules.IsCounted(i)
                        ? MissionRules.LocationsForObjective(state, _mission, i)
                        : new System.Collections.Generic.List<string>
                            { MissionRules.ObjectiveLocation(_mission, i) })
                    : new System.Collections.Generic.List<string>();
                int isLoc = 0, sent = 0;
                foreach (var l in locs)
                {
                    if (!state.AllLocations.Contains(l)) continue;
                    isLoc++;
                    if (state.CheckedLocations.Contains(l)) sent++;
                }
                ModCore.Log.LogInfo($"OBJDUMP:   {i} {kind}: enabled={en} count={count} " +
                    $"done={done} locations={isLoc} checked={sent}/{isLoc}{err}");
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"OBJDUMP failed: {e.Message}"); }
    }

    /// <summary>Reclaim and Custom are not counts, so they send once when the
    /// objective completes.</summary>
    private void SendSingleObjective(World world, int index)
    {
        if (_objectiveDone == null) return;
        bool done = false;
        try { done = world.IsMissionObjectiveComplete(index); }
        catch (Exception e)
        {
            // This used to be a bare catch, which made a throwing query
            // indistinguishable from an objective that is merely not finished -
            // the check would simply never send, with nothing in the log.
            // Logged once per objective per mission so it cannot spam.
            if (_queryFailed != null && !_queryFailed[index])
            {
                _queryFailed[index] = true;
                ModCore.Log.LogWarning($"LocationWatcher: IsMissionObjectiveComplete({index}) threw on mission {_mission}: {e.Message}");
            }
        }
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
