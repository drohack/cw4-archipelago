using System;
using System.Collections.Generic;
using CW4Archipelago.Core;
using HarmonyLib;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Watches the live mission and turns progress into Archipelago location checks.
///
/// Counted objectives - nullify, totems, collect - send ONE CHECK PER INSTANCE.
/// There are TWO separate questions here, and only the second involves position.
///
/// IS IT DONE? Read off the structure ITSELF. Never inferred from where it is:
///
///   nullify  u.IsSuppressed()          the unit stays in the scene, marked
///   totems   tm.totemComplete          the totem stays in the scene, marked
///   caches   absent from gs.mustCollect (the unit is DESTROYED on pickup)
///
/// So running liftic into a totem is not detected by anything positional: the
/// watcher walks the game's own list, points at each totem and asks that object
/// whether it is complete. Same for enemies. The object answers for itself, and
/// because the object survives being finished, that answer is still there after
/// a save and reload.
///
/// WHICH ONE IS IT? Its map cell - UnitManager.cellX/cellY, ordered
/// (cellY, cellX) ascending. See InstanceIndex. That is a NAMING rule, not a
/// detector: it turns "this object is done" into "Totem 3". Caches are the sole
/// exception and there it genuinely is positional, because a destroyed cache
/// leaves nothing to ask - the one that went is the one whose cell is no longer
/// occupied (MapCells).
///
/// Neither question uses MissionObjectiveData.count, and Farsite is why: its
/// Collect slot reads enabled=False with count=0 while two caches sit on the map
/// perfectly collectable, so checks driven off the counter would be dead there.
/// The same doubt applies to every OPTIONAL objective, which is 104 of the 120
/// nullify targets - far too much to rest on a field not guaranteed to move.
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

    /// <summary>Warn once per mission load, not once a second, if the known
    /// cache-cell table does not fit the mission in front of us.</summary>
    private bool _cacheTableWarned;

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
            _cacheTableWarned = false;
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
            //
            // Each of the three sends BY IDENTITY where it can, and falls back to
            // counting where it cannot. Identity is the point: the apworld puts
            // different requirements on different instances - Sequence's five
            // targets under the dark tower need a Chronat, Shattered's top-left
            // totem needs a mover - and a high-water mark attached those
            // requirements to whichever structure happened to be finished Nth.
            SendNullify(gs, world);
            SendTotems(gs, world);
            SendCaches(gs, world);
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
    /// <summary>Nullify targets, by identity.
    ///
    /// The easy case. The set never shrinks and the units are never destroyed -
    /// a nullified structure is marked SUPPRESSED and stays exactly where it is
    /// - so both the cell and the done-flag are readable from live state at any
    /// time, including after a reload.</summary>
    private void SendNullify(GameSpace gs, World world)
    {
        var cells = new List<(int X, int Y)>();
        var done = new List<bool>();
        int total = 0, suppressed = 0;
        try
        {
            foreach (var u in gs.nullifiableUnits)
            {
                if (!GameUtil.IsAlive(u)) continue;
                total++;
                bool supp = false;
                try { supp = u.IsSuppressed(); } catch { }
                if (supp) suppressed++;
                int cx = InstanceIndex.Unknown, cy = InstanceIndex.Unknown;
                try { cx = u.cellX; cy = u.cellY; } catch { }
                cells.Add((cx, cy));
                done.Add(supp);
            }
        }
        catch { return; }

        int locations = MissionRules
            .LocationsForObjective(ModCore.Client.State, _mission, 0).Count;
        if (suppressed != _lastNullifiableSeen)
        {
            _lastNullifiableSeen = suppressed;
            ModCore.Log.LogInfo(
                $"NULLIF: suppressed={suppressed}/{total} locations={locations}");
        }
        SendByIdentity(0, cells, done, locations,
                       NullifyRules.Completed(suppressed, locations),
                       AllIfObjectiveDone(world, 0));
    }

    /// <summary>Totems, by identity. Totem is a component and carries no cell of
    /// its own; the unit on the same object does, and the game's own scalar
    /// converter UnitManager.GetCellX turns a world axis into a cell
    /// otherwise.</summary>
    private void SendTotems(GameSpace gs, World world)
    {
        var cells = new List<(int X, int Y)>();
        var done = new List<bool>();
        int complete = 0;
        try
        {
            foreach (var tm in gs.totems)
            {
                if (tm == null) continue;
                bool isDone = false;
                try { isDone = tm.totemComplete; } catch { }
                if (isDone) complete++;
                int cx = InstanceIndex.Unknown, cy = InstanceIndex.Unknown;
                try
                {
                    var um = tm.GetComponent<UnitManager>();
                    if (um != null) { cx = um.cellX; cy = um.cellY; }
                }
                catch { }
                if (cx < 0)
                {
                    try
                    {
                        var p = tm.transform.position;
                        cx = UnitManager.GetCellX(p.x);
                        cy = UnitManager.GetCellX(p.z);
                    }
                    catch { }
                }
                cells.Add((cx, cy));
                done.Add(isDone);
            }
        }
        catch { return; }

        int locations = MissionRules
            .LocationsForObjective(ModCore.Client.State, _mission, 1).Count;
        SendByIdentity(1, cells, done, locations, complete, AllIfObjectiveDone(world, 1));
    }

    /// <summary>Caches, by identity where it is possible at all.
    ///
    /// A collected cache is DESTROYED and leaves GameSpace.mustCollect, so the
    /// live set names what is LEFT and can never name what was taken. The full
    /// list is therefore recorded in SlotState the first time the mission is
    /// seen intact (mustCollect still at maxMustCollect), and after that a
    /// remembered cell no longer present is a cache that was collected.
    ///
    /// No memory means a save whose caches were taken before the mod ever saw
    /// the mission; that falls through to counting, which loses the ordering but
    /// never mislabels a check.</summary>
    private void SendCaches(GameSpace gs, World world)
    {
        var remaining = new List<(int X, int Y)>();
        int max = -1;
        try { max = gs.maxMustCollect; } catch { }
        try
        {
            foreach (var u in gs.mustCollect)
            {
                if (u == null) continue;
                int cx = InstanceIndex.Unknown, cy = InstanceIndex.Unknown;
                try { cx = u.cellX; cy = u.cellY; } catch { }
                remaining.Add((cx, cy));
            }
        }
        catch { return; }

        int locations = MissionRules
            .LocationsForObjective(ModCore.Client.State, _mission, 4).Count;
        if (locations <= 0)
            return;
        int collected = max < 0 ? -1 : max - remaining.Count;
        var state = ModCore.Client.State;
        var key = MissionRules.Specifier(_mission);
        int fromGame = AllIfObjectiveDone(world, 4);

        // The cells still on the map, as keys, for the table check below.
        var present = new List<string>();
        foreach (var c in remaining)
            if (c.X >= 0 && c.Y >= 0)
                present.Add(InstanceIndex.Key(c.X, c.Y));

        // FIRST CHOICE: the known map cells. Cache positions do not vary with a
        // save, a seed or a playthrough, so the intact set does not have to be
        // observed - it is a fact about the mission. That matters for the one
        // case the observed-set memory below can never serve: a save whose
        // caches were already taken before this mod was installed.
        //
        // Guarded three ways, because a wrong index here is a mislabelled check:
        // the table must know the mission, its row must be the length the GAME
        // reports, and every cache still standing must appear in it. A cache
        // moved by a game update fails the third test.
        var table = MapCells.CachesFor(_mission);
        if (table != null && max > 0 && table.Count == max && MapCells.Covers(_mission, present))
        {
            foreach (var n in InstanceIndex.DoneFromRemembered(table, remaining, locations))
                SendCheck(MissionRules.InstanceLocation(_mission, 4, n));
            SendCounted(4, fromGame);   // the game's verdict still backfills
            return;
        }
        if (table != null && !_cacheTableWarned)
        {
            _cacheTableWarned = true;
            ModCore.Log.LogWarning(
                $"CACHES: the known cell table does not fit {key} " +
                $"(table={table.Count} game={max} covers={MapCells.Covers(_mission, present)}) " +
                "- falling back to what this run observed");
        }

        // SECOND: the set as first seen this playthrough. Guarded on the mission
        // actually BEING intact at that moment - remembering a half-collected
        // first sighting as the whole thing would shift every index after the
        // gap.
        if (max > 0 && remaining.Count == max && !state.CacheCells.ContainsKey(key))
        {
            var remembered = InstanceIndex.Remember(remaining);
            if (remembered.Count == max)
            {
                state.CacheCells[key] = remembered;
                ModCore.Log.LogInfo(
                    $"CACHES: remembered {remembered.Count} cell(s) for {key} " +
                    $"[{string.Join(" ", remembered)}]");
            }
        }

        if (state.CacheCells.TryGetValue(key, out var known) && known != null && known.Count > 0)
        {
            foreach (var n in InstanceIndex.DoneFromRemembered(known, remaining, locations))
                SendCheck(MissionRules.InstanceLocation(_mission, 4, n));
            SendCounted(4, fromGame);
            return;
        }

        // LAST: count. Loses which cache, never claims the wrong one.
        SendCounted(4, Math.Max(collected, fromGame));
    }

    /// <summary>Send the identified instances, or fall back to counting.
    ///
    /// The fallback is not a nicety. Identity needs every cell to read, and
    /// InstanceIndex refuses the whole set if any one of them did not, because a
    /// partial read yields confident numbers derived from a coordinate we never
    /// got. Counting is wrong in a way that only loses ordering; a bad index is
    /// wrong in a way that mislabels a check, and the apworld hangs different
    /// requirements off different indices.
    ///
    /// The game's own completion query is layered on top either way - it is the
    /// one signal that is still right on a resumed save.</summary>
    private void SendByIdentity(int objectiveIndex,
                                List<(int X, int Y)> cells,
                                List<bool> done,
                                int locations,
                                int countedProgress,
                                int gameVerdict)
    {
        if (locations <= 0)
            return;
        if (InstanceIndex.Assign(cells).Length > 0)
        {
            if (InstanceIndex.HasSharedCell(cells))
                ModCore.Log.LogWarning(
                    $"INSTANCE: two {MissionRules.InstanceKind(objectiveIndex)} structures share " +
                    $"a cell on {MissionRules.Specifier(_mission)} - their numbers are interchangeable");
            foreach (var n in InstanceIndex.DoneInstances(cells, done, locations))
                SendCheck(MissionRules.InstanceLocation(_mission, objectiveIndex, n));
            SendCounted(objectiveIndex, gameVerdict);
            return;
        }
        SendCounted(objectiveIndex, Math.Max(countedProgress, gameVerdict));
    }

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
