using System;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Stopwatches that run INSIDE the game loop.
///
/// WHY THIS EXISTS. Build Speed was measured from bash by spawning a unit and
/// polling "ern:stats" until isBuilding went false. Every poll is a file write,
/// a log flush and a read back, so the round trip costs seconds - and the
/// measurement came out as exactly 540 ticks at 0 percent AND at 100 percent.
/// Two identical numbers to the tick are the signature of a stopwatch measuring
/// ITSELF rather than the thing under test.
///
/// The same applies to an energy RATE: sampling a store from outside means the
/// interval is whatever the harness latency happened to be that time.
///
/// So the timing moves in here, where it can look every tick and the interval
/// is exact. The harness only starts a measurement and reads one result line.
/// </summary>
public static class MeasureProbe
{
    // --- build timing ----------------------------------------------------

    private static UnitManager? _watch;
    private static int _buildStart = -1;
    private static bool _sawBuilding;
    private static string _buildLabel = "";
    private static int _buildSeq;
    private static UnitManager? _lastBuilt;

    /// <summary>Spawn a unit and time it from placement to finished.</summary>
    public static void TimeBuild(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { ModCore.Log.LogWarning("MEASURE build: need a unit key"); return; }
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("MEASURE build: no GameSpace"); return; }

        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !GameUtil.IsAlive(cb)) { ModCore.Log.LogWarning("MEASURE build: no command base"); return; }

        // ONE known-good cell, cleared before reuse.
        //
        // Two wrong approaches came first. Reusing the cell WITHOUT clearing it
        // gave 363 / 186 / 33 ticks, and the 33 was an artifact of building
        // onto an occupied spot (indestructible keeps every earlier cannon
        // standing). Stepping to a fresh cell each time instead walked off the
        // buildable area, so later spawns returned null and the run reported
        // "NO RESULT" for the two levels that mattered.
        //
        // Destroying the previous one keeps the cell both free and known-good.
        if (_lastBuilt != null)
        {
            try { if (GameUtil.IsAlive(_lastBuilt)) _lastBuilt.DestroyUnit(false, false, false); }
            catch { }
            _lastBuilt = null;
        }

        _buildSeq++;
        var flat = cb.transform.position + new Vector3(6f, 0f, -10f);
        float ground = flat.y;
        try { ground = UnitManager.GetMinHeight(new Vector3(flat.x, 0f, flat.z), 0f, 0, false, false, false); }
        catch { }

        UnitManager? made = null;
        try { made = UnitManager.CreateUnitAtPosition(key.ToLowerInvariant(), new Vector3(flat.x, ground, flat.z)); }
        catch (Exception e) { ModCore.Log.LogWarning($"MEASURE build: spawn '{key}' threw {e.Message}"); return; }

        // Worded with "result=" so the harness greps ONE pattern for every
        // outcome. A failed placement previously matched no pattern at all
        // and surfaced as "NO RESULT", which reads like a hung mod rather
        // than a spawn that was refused.
        if (made == null)
        {
            ModCore.Log.LogWarning($"MEASURE build: result=noplace '{key}' could not be placed");
            return;
        }
        _lastBuilt = made;

        _watch = made;
        _buildLabel = key;
        _sawBuilding = false;
        try { _buildStart = gs.tickCount; } catch { _buildStart = -1; }
        ModCore.Log.LogInfo(
            $"MEASURE build: watching '{key}' from tick {_buildStart} "
            + $"at ({flat.x:0.#},{flat.z:0.#})");
    }

    // --- move timing -----------------------------------------------------

    private static UnitManager? _mover;
    private static int _moveStart = -1;
    private static Vector3 _moveFrom;
    private static bool _sawMoving;

    /// <summary>Order a cannon to relocate and time the trip.
    ///
    /// Cannons CAN move - CAN_MOVE reads true on one - which is what makes this
    /// possible, and an earlier run wrongly wrote Move Speed off as
    /// unmeasurable because it only watched a stationary cannon's fields.
    ///
    /// It has to be TIMED rather than read: unlike Fire Rate, which rewrites
    /// COOL_DOWN in place (8 -> 6 -> 4), MOVE_SPEED stays at 0.09 whatever the
    /// upgrade is doing, so the multiplier is applied while stepping and never
    /// stored anywhere a dump can see.</summary>
    public static void TimeMove(float dist)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("MEASURE move: no GameSpace"); return; }
        if (dist <= 0f) dist = 10f;

        UnitManager? pick = null;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                if (!u.CAN_MOVE) continue;
                if (u.isBuilding) continue;
                var nm = u.GetDataName() ?? "";
                if (!string.Equals(nm, "cannon", StringComparison.OrdinalIgnoreCase)) continue;
                if (u.moveState != UnitManager.MOVE_STATE.LANDED) continue;
                pick = u;
                break;
            }
            catch { }
        }
        if (pick == null) { ModCore.Log.LogWarning("MEASURE move: result=nounit no landed movable cannon"); return; }

        // STEP-LABELLED, because the first version wrapped the whole
        // sequence in one try and reported only
        // "result=threw NullReferenceException" - six times, with no way to
        // tell whether the ctor, IsLegal, the moveTargets list or SetMoveState
        // was the one that threw. A stack-free exception message is not a
        // diagnosis.
        string step = "start";
        try
        {
            step = "position";
            var pos = pick.transform.position;
            // Cell coordinates, which is what the MoveTarget ctor takes.
            int cx = (int)pos.x, cy = (int)pos.z;
            int tx = cx + (int)dist;

            step = "clear";
            // BEFORE constructing anything: a unit that has never moved can
            // have a null moveTargets list, and Clear is what establishes it.
            try { pick.ClearMoveTargets(); } catch (Exception ce) { ModCore.Log.LogWarning($"MEASURE move: clear threw {ce.Message}"); }

            // The five-argument ctor MoveTarget(um, cellX, cellY, waypoint,
            // temp) throws NullReferenceException inside itself on a
            // debug-spawned cannon - six attempts, every one at this step. It
            // presumably expects state the normal drag-a-ghost flow sets up.
            // The one-argument ctor plus an explicit Init is the lighter path.
            step = "ctor";
            var mt = new MoveTarget(pick);
            step = "init";
            mt.Init(pick, tx, cy, false, false, false);

            step = "islegal";
            bool legal;
            try { legal = mt.IsLegal(); }
            catch (Exception le)
            {
                // Treat an unanswerable legality check as "try it anyway"
                // rather than aborting: the move either starts or it does not,
                // and the tick watcher reports nomove if it does not.
                ModCore.Log.LogWarning($"MEASURE move: IsLegal threw {le.Message}, proceeding");
                legal = true;
            }

            if (!legal)
            {
                step = "ctor2";
                tx = cx - (int)dist;
                try { mt.DestroyMoveTarget(); } catch { }
                mt = new MoveTarget(pick);
                mt.Init(pick, tx, cy, false, false, false);
                step = "islegal2";
                bool legal2;
                try { legal2 = mt.IsLegal(); } catch { legal2 = true; }
                if (!legal2)
                {
                    ModCore.Log.LogWarning("MEASURE move: result=illegal no legal target either side");
                    return;
                }
            }

            step = "list";
            // moveTargets is genuinely null on a unit that has never been given
            // a move order - ClearMoveTargets() does not allocate it. The
            // property has a setter, so supply the list rather than giving up.
            var list = pick.moveTargets;
            if (list == null)
            {
                list = new Il2CppSystem.Collections.Generic.List<MoveTarget>();
                pick.moveTargets = list;
            }
            list.Add(mt);

            step = "setstate";
            pick.SetMoveState(UnitManager.MOVE_STATE.TAKINGOFF);

            _mover = pick;
            _moveFrom = pos;
            _sawMoving = false;
            _moveStart = gs.tickCount;
            ModCore.Log.LogInfo(
                $"MEASURE move: ordered cannon ({cx},{cy}) -> ({tx},{cy}) at tick {_moveStart}");
        }
        catch (Exception e)
        {
            ModCore.Log.LogWarning($"MEASURE move: result=threw at step '{step}': {e.Message}");
        }
    }

    // --- ware (mining) rate ----------------------------------------------

    private static int _wareUntil = -1;
    private static int _wareStart = -1;
    private static float _wareFrom;

    /// <summary>The ware types a resource node can yield. Read once from the
    /// game rather than hard-coded, because the indices are assigned by
    /// WaresManager and there is no reason to assume they are 0,1,2.</summary>
    private static int[] WareIds()
    {
        var ids = new System.Collections.Generic.List<int>();
        try { ids.Add(WaresManager.WARE_BLUITE); } catch { }
        try { ids.Add(WaresManager.WARE_REDON); } catch { }
        try { ids.Add(WaresManager.WARE_GREENAR); } catch { }
        return ids.ToArray();
    }

    /// <summary>Total mined ware held across every player unit.
    ///
    /// This is the Mine Production observable. The cheap route was tried first
    /// and failed: Resource.PRODUCTION_INTERVAL stays at 20 through 0, 100 and
    /// 200 percent, so unlike Fire Rate's COOL_DOWN the upgrade is not written
    /// into the node. The nodes also sat at counter=0 wareAvailable=False the
    /// whole time, because a node produces nothing until something is actually
    /// mining it - which is why every earlier attempt measured a flat zero.</summary>
    private static float TotalWares(GameSpace gs)
    {
        float total = 0f;
        var ids = WareIds();
        try
        {
            foreach (var u in gs.units)
            {
                if (u == null) continue;
                try
                {
                    if (!GameUtil.IsPlayerUnit(u)) continue;
                    foreach (var w in ids)
                    {
                        try { total += u.GetWareHeld(w); } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    public static void TimeWares(int ticks)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("MEASURE ware: no GameSpace"); return; }
        if (ticks <= 0) ticks = 1800;
        try
        {
            _wareStart = gs.tickCount;
            _wareUntil = _wareStart + ticks;
            _wareFrom = TotalWares(gs);
            ModCore.Log.LogInfo(
                $"MEASURE ware: start tick={_wareStart} held={_wareFrom:0.###}, " +
                $"reporting at tick {_wareUntil}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"MEASURE ware failed: {e.Message}"); }
    }

    // --- energy rate -----------------------------------------------------

    private static int _energyUntil = -1;
    private static int _energyStart = -1;
    private static float _storeStart;

    /// <summary>Measure energy accrual over an exact number of sim ticks.</summary>
    public static void TimeEnergy(int ticks)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("MEASURE energy: no GameSpace"); return; }
        if (ticks <= 0) ticks = 900;
        try
        {
            _energyStart = gs.tickCount;
            _energyUntil = _energyStart + ticks;
            _storeStart = gs.energyStore;
            ModCore.Log.LogInfo(
                $"MEASURE energy: start tick={_energyStart} store={_storeStart:0.##}, " +
                $"reporting at tick {_energyUntil}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"MEASURE energy failed: {e.Message}"); }
    }

    /// <summary>Called every tick from ModCore.</summary>
    public static void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) return;

        int tick;
        try { tick = gs.tickCount; } catch { return; }

        if (_watch != null)
        {
            bool alive = false, building = false;
            try { alive = GameUtil.IsAlive(_watch); } catch { }
            try { building = _watch.isBuilding; } catch { }

            if (!alive)
            {
                ModCore.Log.LogWarning($"MEASURE build: result=died '{_buildLabel}' died before finishing");
                _watch = null;
            }
            else if (building)
            {
                _sawBuilding = true;
            }
            else if (_sawBuilding)
            {
                // A build that was never seen in progress is NOT a zero-tick
                // build - it is a build that finished instantly because
                // instantbuild was left on, and reporting it as a fast result
                // would be the same mistake as before.
                ModCore.Log.LogInfo(
                    $"MEASURE build: result=ok '{_buildLabel}' took {tick - _buildStart} ticks " +
                    $"(start={_buildStart} end={tick})");
                _watch = null;
            }
            else if (tick - _buildStart > 20)
            {
                ModCore.Log.LogWarning(
                    $"MEASURE build: result=instant '{_buildLabel}' never showed isBuilding - " +
                    "instantbuild is probably still on, so there was nothing to time");
                _watch = null;
            }
        }

        if (_mover != null)
        {
            var st = UnitManager.MOVE_STATE.LANDED;
            bool alive = false;
            try { alive = GameUtil.IsAlive(_mover); } catch { }
            try { st = _mover.moveState; } catch { }

            if (!alive)
            {
                ModCore.Log.LogWarning("MEASURE move: result=died the mover died");
                _mover = null;
            }
            else if (st != UnitManager.MOVE_STATE.LANDED)
            {
                _sawMoving = true;
            }
            else if (_sawMoving)
            {
                var now = _mover.transform.position;
                float travelled = Vector3.Distance(
                    new Vector3(_moveFrom.x, 0f, _moveFrom.z), new Vector3(now.x, 0f, now.z));
                int span = tick - _moveStart;
                // Distance AND time, because a move that was refused partway
                // would otherwise look like a very fast trip.
                ModCore.Log.LogInfo(
                    $"MEASURE move: result=ok travelled {travelled:0.##} cells in {span} ticks " +
                    $"= {(span > 0 ? travelled / span : 0f):0.#####} cells/tick");
                _mover = null;
            }
            else if (tick - _moveStart > 200)
            {
                ModCore.Log.LogWarning("MEASURE move: result=nomove never left LANDED");
                _mover = null;
            }
        }

        if (_wareUntil > 0 && tick >= _wareUntil)
        {
            float held = TotalWares(gs);
            int span = tick - _wareStart;
            float delta = held - _wareFrom;
            // Zero is a REAL possible answer here (nothing is mining), so it is
            // reported plainly rather than suppressed - a silent nothing was
            // already mistaken for "the upgrade does nothing" once.
            ModCore.Log.LogInfo(
                $"MEASURE ware: {delta:0.###} ware over {span} ticks " +
                $"= {(span > 0 ? delta / span : 0f):0.#####}/tick " +
                $"(held {_wareFrom:0.###} -> {held:0.###})");
            _wareUntil = -1;
        }

        if (_energyUntil > 0 && tick >= _energyUntil)
        {
            float store = 0f, use = 0f;
            try { store = gs.energyStore; } catch { }
            try { use = gs.energyUse; } catch { }
            int span = tick - _energyStart;
            float delta = store - _storeStart;

            // CONSUMPTION IS REPORTED, because the store delta is production
            // MINUS use and reading it as production alone is what made this
            // measurement irreproducible. One run gave a clean linear
            // 0.053 / 0.070 / 0.087 per tick; the next gave
            // 0.043 / 0.021 / 0.033 - not because the upgrade changed, but
            // because cannons left over from the build test were drawing ammo
            // packets. A rate quoted without its consumer count is not a result.
            ModCore.Log.LogInfo(
                $"MEASURE energy: {delta:0.###} energy over {span} ticks " +
                $"= {(span > 0 ? delta / span : 0f):0.#####}/tick " +
                $"(store {_storeStart:0.##} -> {store:0.##}, useAtEnd={use:0.###}, " +
                $"consumers={CountConsumers(gs)})");
            _energyUntil = -1;
        }
    }

    /// <summary>Anything that could be drawing energy while a rate is measured:
    /// a unit still building, or one that wants ammo. Reported beside every
    /// energy figure so a confounded reading is visible in the result itself
    /// rather than inferred a run later.</summary>
    private static int CountConsumers(GameSpace gs)
    {
        int n = 0;
        try
        {
            foreach (var u in gs.units)
            {
                if (u == null) continue;
                try
                {
                    if (!GameUtil.IsPlayerUnit(u)) continue;
                    if (u.isBuilding) { n++; continue; }
                    if (u.MAX_AMMO > 0f && u.ammo < u.MAX_AMMO) n++;
                }
                catch { }
            }
        }
        catch { }
        return n;
    }

    /// <summary>Drop everything when a mission tears down.</summary>
    public static void Reset()
    {
        _watch = null;
        _energyUntil = -1;
        _mover = null;
        _wareUntil = -1;
    }
}
