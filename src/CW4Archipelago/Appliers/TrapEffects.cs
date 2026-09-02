using System;
using System.Linq;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// The Archipelago trap effects: hostile but RECOVERABLE in-mission events.
/// Nothing here may make a mission unwinnable - no terrain deformation, no
/// destroying the rift lab, no permanent difficulty change.
///
/// Six effects, from the traps spike (docs/design/2026-08-26-traps-spike.md):
/// spore strike, creeper surge, energy drain, emitter burst, unit stun, weapon
/// drain. Five are fire-and-forget; only the emitter burst carries state, and
/// ModCore.Tick drives its restore.
///
/// Five depend only on what EVERY mission has (world grid, energy store, the
/// player's units, the spore system). The emitter burst is the exception: it
/// no-ops where a mission ships no emitters, which is a balance caveat, not a
/// bug. Re-fog was the one effect dropped outright - see the block at the end.
///
/// Reachable through the config-gated DebugChannel "trap:" commands; wiring
/// them to real AP items is a separate step.
/// </summary>
public static class TrapEffects
{
    // ---------------------------------------------------------------------
    // Tuning. All the numbers a trap needs, in one place.
    //
    // Scale facts established by the spike, needed to read these:
    //  - creeper/spore amounts are fixed-point at 1,000,000 per unit of depth
    //    (World.DIGITALIS_CREEPER_DEPTH is 4,000,000 = depth 4; story3 emitters
    //    carry productionBaseAmt 40,000,000 = the "40" the editor shows).
    //  - the sim runs at 30 ticks per second.
    // ---------------------------------------------------------------------

    private const int TicksPerSecond = 30;
    private const int Depth = 1_000_000;          // one unit of displayed depth

    /// <summary>Spores per strike, and the creeper each carries on impact.
    /// Payload 20 depth is exactly what CW4's own launchers carry.</summary>
    public static int SporeCount = 2;
    public static int SporePayload = 20 * Depth;

    /// <summary>Where a spore strike aims. This is OUR mode, not the game's
    /// enum, because the game's three behaviours do not line up with what a trap
    /// wants (measured with trap:aim):
    ///   game RANDOM    - an arbitrary map point. Used by Scatter.
    ///   game STRUCTURE - a random building of ANY owner. All 12 test spores
    ///                    landed exactly on a unit, but on story1 the player
    ///                    owned 1 of 36 buildings, so it mostly hits scenery and
    ///                    enemy structures. Not steerable, so not shipped.
    ///   game LOCATION  - aims exactly where told (distance 0, verified). This
    ///                    is how PlayerBuilding and RiftLab are implemented: we
    ///                    pick the cell ourselves.
    /// </summary>
    public enum SporeAim
    {
        /// <summary>Random map points - what CW4's own launchers do.</summary>
        Scatter,
        /// <summary>A random building OF THE PLAYER'S, chosen per spore.</summary>
        PlayerBuilding,
        /// <summary>Straight at the rift lab. Surgical; available, not shipped.</summary>
        RiftLab,
    }

    public static SporeAim SporeTargeting = SporeAim.PlayerBuilding;

    /// <summary>Creeper surge: depth added per cell, and the radius it covers.
    /// Deliberately small - a trap should sting, not end the mission.</summary>
    public static int CreepDepth = 2 * Depth;
    public static int CreepRadius = 3;

    /// <summary>How far from the rift lab the surge lands, in cells. Dropping it
    /// directly on the base is a near-instant loss on an undefended start.</summary>
    public static int CreepOffset = 12;

    /// <summary>Stun window for the player's units, in seconds. 0 means "use
    /// each unit's own STUN_TIME", which is what the game itself applies when
    /// something stuns a unit - the rift lab reports STUN_TIME = 300 ticks = 10s.
    /// Defaulting to it makes the trap exactly one natural stun rather than a
    /// number I invented (15s was 1.5x the game's own).</summary>
    public static float StunSeconds = 0f;

    /// <summary>Fraction of the energy store to take. A flat amount is
    /// invisible (5 off a store of 100 refilled almost instantly), so this is
    /// proportional: 1.0 empties the bank.</summary>
    public static float EnergyFraction = 1.0f;

    /// <summary>Emitter burst: how long enemy emitters run boosted, and by how
    /// much. ALWAYS restored from a snapshot - a permanent change would be a
    /// difficulty edit, not a trap.</summary>
    public static float EmitSeconds = 20f;
    public static float EmitMultiplier = 3f;

    // --- shared guards ----------------------------------------------------

    private static GameSpace? Live(string what)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogInfo($"TRAP {what}: no GameSpace (not in a mission)"); return null; }
        if (GameSpace.editMode) { ModCore.Log.LogInfo($"TRAP {what}: edit mode, skipped"); return null; }
        return gs;
    }

    // See GameUtil.IsPlayerUnit for why UnitManager.enemy cannot be used here.
    private static bool IsPlayerUnit(UnitManager u) => GameUtil.IsPlayerUnit(u);

    /// <summary>The rift lab's cell, or false if there is not one placed yet.
    /// Note it is x and Z, not x and y.</summary>
    private static bool BaseCell(GameSpace gs, out int cx, out int cy)
    {
        cx = cy = 0;
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !GameUtil.IsAlive(cb)) return false;
        var p = cb.transform.position;
        cx = UnitManager.GetCellX(p.x);
        cy = UnitManager.GetCellY(p.z);
        return true;
    }

    private static int ClampX(int x) => Mathf.Clamp(x, 0, World.WORLD_CELL_WIDTH - 1);
    private static int ClampY(int y) => Mathf.Clamp(y, 0, World.WORLD_CELL_HEIGHT - 1);

    /// <summary>Cell -> world. Measured identity: GetCellX(0)=0, GetCellX(50)=50,
    /// GetCellX(100)=100, and story1's rift lab sits at world (41,7,33) = cell
    /// (41,33). So x/z pass straight through and only the height needs looking
    /// up. Do NOT use World.GetCreeperVertex for this - it is mesh-local, not
    /// world space: GetCreeperVertex(50,50) returns (18,6,18), which put spawned
    /// units at NEGATIVE cells and launched spores from the wrong place.</summary>
    public static Vector3 CellToWorld(int cx, int cy)
    {
        float y = 0f;
        try { y = UnitManager.GetMinHeight(new Vector3(cx, 0f, cy), 0f, 0, false, false, false); }
        catch { }
        return new Vector3(cx, y, cy);
    }

    // --- 1. spore strike --------------------------------------------------

    /// <summary>Launches a small wave of spores with RANDOM targeting from
    /// random map cells. SporeLauncher.CreateSpore is static, so this needs no
    /// launcher unit and works on every mission. The player can shoot them
    /// down, which is what makes this the friendliest trap of the four.</summary>
    /// <summary>Trap variant 1 - "spore strike": a scatter at random map
    /// points. Exactly what the game's own launchers do, so it is the fair,
    /// authentic version - it may land somewhere harmless.</summary>
    public static void SporeStrikeScatter(int count, int payload)
        => SporeStrike(count, payload, SporeAim.Scatter);

    /// <summary>Trap variant 2 - "targeted spore strike": every spore aims at a
    /// RANDOM BUILDING OF THE PLAYER'S, picked independently. Scales with how
    /// built-up the player is, which is the good kind of scaling - it bites
    /// hardest late when there is something to lose. Spreading across buildings
    /// rather than always hitting the rift lab keeps it threatening without
    /// being surgical.</summary>
    public static void SporeStrikeBuilding(int count, int payload)
        => SporeStrike(count, payload, SporeAim.PlayerBuilding);

    public static void SporeStrike(int count, int payload)
        => SporeStrike(count, payload, SporeTargeting);

    public static void SporeStrike(int count, int payload, SporeAim aimMode)
    {
        var gs = Live("spore");
        if (gs == null) return;
        var world = gs.world;
        if (world == null) { ModCore.Log.LogWarning("TRAP spore: no world"); return; }

        if (count <= 0) count = SporeCount;
        if (payload <= 0) payload = SporePayload;

        // Resolve the aim points ONCE, so an unhittable mode degrades to a real
        // scatter rather than dumping the strike at (0,0) - a trap that lands in
        // a map corner is a trap that does nothing.
        var aimCells = new System.Collections.Generic.List<Vector2>();
        var effective = aimMode;

        if (aimMode == SporeAim.PlayerBuilding)
        {
            foreach (var u in gs.units)
            {
                if (u == null) continue;
                try
                {
                    if (!IsPlayerUnit(u)) continue;
                    var p = u.transform.position;
                    aimCells.Add(new Vector2(UnitManager.GetCellX(p.x), UnitManager.GetCellY(p.z)));
                }
                catch { }
            }
            if (aimCells.Count == 0)
            {
                effective = SporeAim.Scatter;
                ModCore.Log.LogInfo("TRAP spore: player has no buildings yet, falling back to scatter");
            }
        }
        else if (aimMode == SporeAim.RiftLab)
        {
            if (BaseCell(gs, out var rbx, out var rby)) aimCells.Add(new Vector2(rbx, rby));
            else
            {
                effective = SporeAim.Scatter;
                ModCore.Log.LogInfo("TRAP spore: no rift lab to aim at, falling back to scatter");
            }
        }

        int w = World.WORLD_CELL_WIDTH, h = World.WORLD_CELL_HEIGHT;
        int made = 0; string aimed = "";
        for (int i = 0; i < count; i++)
        {
            try
            {
                // Launch point is always a random cell; only the TARGET differs.
                var pos = CellToWorld(
                    UnityEngine.Random.Range(2, Math.Max(3, w - 2)),
                    UnityEngine.Random.Range(2, Math.Max(3, h - 2)));
                pos.y += 30f;   // launch from above the surface

                var behavior = global::Spore.TARGET_BEHAVIOR.RANDOM;
                var aim = Vector2.zero;
                if (effective != SporeAim.Scatter)
                {
                    // Each spore picks its own player building.
                    aim = aimCells[UnityEngine.Random.Range(0, aimCells.Count)];
                    behavior = global::Spore.TARGET_BEHAVIOR.LOCATION;
                    if (aimed.Length < 60) aimed += $"({aim.x:0},{aim.y:0})";
                }

                if (SporeLauncher.CreateSpore(behavior, aim, payload, pos) != null) made++;
            }
            catch (Exception e) { ModCore.Log.LogWarning($"TRAP spore: CreateSpore failed: {e.Message}"); return; }
        }

        ModCore.Log.LogInfo(
            $"TRAP spore: launched {made}/{count} aim={effective}" +
            (effective == SporeAim.Scatter ? "" : $" onto {aimCells.Count} candidate building(s) {aimed}") +
            $" payload={payload} ({payload / (float)Depth:0.#} depth) map {w}x{h}");
    }

    // --- 2. creeper surge -------------------------------------------------

    /// <summary>Dumps a shallow patch of creeper a short way off from the rift
    /// lab. Lands OFF-base on purpose: on top of an undefended start this is an
    /// instant loss rather than a setback.</summary>
    public static void Creep(int radius, int amt)
    {
        var gs = Live("creep");
        if (gs == null) return;
        var world = gs.world;
        if (world == null) { ModCore.Log.LogWarning("TRAP creep: no world"); return; }

        if (radius <= 0) radius = CreepRadius;
        if (amt <= 0) amt = CreepDepth;

        // Prefer a spot near the base so the player has to react, but not on it.
        int bx, by;
        if (BaseCell(gs, out var cbx, out var cby))
        {
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            bx = ClampX(cbx + (int)(Mathf.Cos(ang) * CreepOffset));
            by = ClampY(cby + (int)(Mathf.Sin(ang) * CreepOffset));
        }
        else
        {
            // No rift lab yet (most missions start that way) - land it anywhere.
            bx = UnityEngine.Random.Range(0, World.WORLD_CELL_WIDTH);
            by = UnityEngine.Random.Range(0, World.WORLD_CELL_HEIGHT);
        }

        int before = world.GetCreeper(bx, by);
        int cells = 0;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = bx + dx, y = by + dy;
                if (x < 0 || y < 0 || x >= World.WORLD_CELL_WIDTH || y >= World.WORLD_CELL_HEIGHT) continue;
                try { world.AddCreeper(x, y, amt); cells++; } catch { }
            }
        int after = world.GetCreeper(bx, by);
        ModCore.Log.LogInfo(
            $"TRAP creep: +{amt} ({amt / (float)Depth:0.#} depth) over {cells} cell(s) r={radius} " +
            $"at ({bx},{by}); centre {before} -> {after}");
    }

    // --- 3. unit stun -----------------------------------------------------

    /// <summary>Stuns the player's units. stunnedCount is a sim-tick countdown
    /// the game decrements on its own, so this cannot outlive its window even
    /// if the mod is unloaded mid-stun. ERNs appear immune (a per-type CAN_STUN
    /// flag), which is fine - they are not weapons.</summary>
    public static void Stun(float seconds)
    {
        var gs = Live("stun");
        if (gs == null) return;
        if (seconds <= 0f) seconds = StunSeconds;
        // 0 (the default) means per-unit STUN_TIME; anything else overrides it.
        int fixedTicks = seconds > 0f ? Math.Max(1, (int)(seconds * TicksPerSecond)) : 0;

        int n = 0, skipped = 0, immune = 0, totalTicks = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!IsPlayerUnit(u)) { skipped++; continue; }
                // CAN_STUN is a per-type flag; it is why ERNs shrugged the stun
                // off in the spike. Skipping them keeps the log honest instead
                // of counting units the sim was never going to stun.
                if (!u.CAN_STUN) { immune++; continue; }
                int ticks = fixedTicks > 0 ? fixedTicks : Math.Max(1, u.STUN_TIME);
                u.SetStunCount(ticks);
                totalTicks += ticks;
                n++;
            }
            catch { skipped++; }
        }
        string dur = fixedTicks > 0
            ? $"{fixedTicks} ticks (~{seconds:0.#}s, override)"
            : $"each unit's own STUN_TIME (avg {(n > 0 ? totalTicks / n : 0)} ticks ~{(n > 0 ? totalTicks / (float)n / TicksPerSecond : 0f):0.#}s)";
        ModCore.Log.LogInfo($"TRAP stun: {n} player unit(s) stunned for {dur}; {immune} cannot be stunned, {skipped} not the player's");
    }

    // --- 4. energy drain --------------------------------------------------

    /// <summary>Takes a FRACTION of the energy store. Proportional on purpose:
    /// a flat amount is invisible late and fatal early (5 off a store of 100
    /// refilled almost instantly in testing). Energy regenerates, so this is a
    /// setback, never a loss.
    ///
    /// WRITES commandBase.ammo, NOT GameSpace.energyStore. This trap wrote the
    /// latter and was therefore a NO-OP: EnergyGranter's own header says
    /// energyStore and energyProduction "are summaries the sim recomputes every
    /// tick, so writing them moves the HUD and delivers nothing". The trap
    /// moved the number on screen, the sim overwrote it, and nothing was ever
    /// taken.
    ///
    /// It went unnoticed because a drained HUD reading looks exactly like a
    /// working trap, and energy climbs back on its own - so "it refilled
    /// quickly" was the expected behaviour anyway.
    ///
    /// Its mirror, BoonEffects.EnergyCache, had the right field all along
    /// (verified moving the store 22.9 -> 47.9), which is what made the
    /// discrepancy visible: the two halves of one pair were writing different
    /// fields.</summary>
    public static void Energy(float fraction)
    {
        var gs = Live("energy");
        if (gs == null) return;
        if (fraction <= 0f) fraction = EnergyFraction;
        fraction = Mathf.Clamp01(fraction);

        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !GameUtil.IsAlive(cb))
        {
            // A campaign mission starts with the lab in the player's hand, so
            // this is a real state rather than an error.
            ModCore.Log.LogInfo("TRAP energy: no rift lab placed, nothing to drain");
            return;
        }

        float before = cb.ammo;
        float after = Math.Max(0f, before - before * fraction);
        cb.ammo = after;
        ModCore.Log.LogInfo(
            $"TRAP energy: store {before:0.##} -> {after:0.##} " +
            $"(took {fraction * 100f:0}%, {before - after:0.##}); " +
            $"summary gs.energyStore={gs.energyStore:0.##} is NOT the field written");
    }

    // --- 5. emitter burst (timed, self-restoring) -------------------------

    private struct EmitterSnapshot
    {
        public long BaseAmt;
        public int Interval;
        public long BaseAmt2;
        public int Interval2;
    }

    private static readonly System.Collections.Generic.Dictionary<IntPtr, EmitterSnapshot> Burst = new();
    private static float _burstExpiry;
    private static IntPtr _burstGameSpace = IntPtr.Zero;

    /// <summary>Whether a burst is currently running (for Status and tests).</summary>
    public static int BurstCount => Burst.Count;

    /// <summary>Multiplies every enemy emitter's output for a window, then puts
    /// the snapshot back. The ONLY trap that carries state, which is why
    /// ModCore.Tick drives Tick() below - the restore must be guaranteed.
    ///
    /// Caveat worth knowing: this no-ops on missions with no emitters. Measured
    /// absent on story1, story5 and story8; present on story2 (1), story3 (2),
    /// story4 (4), story6 (3), story7 (2).</summary>
    public static void Emit(float seconds, float multiplier)
    {
        var gs = Live("emit");
        if (gs == null) return;
        if (seconds <= 0f) seconds = EmitSeconds;
        if (multiplier <= 0f) multiplier = EmitMultiplier;

        if (Burst.Count > 0) { ModCore.Log.LogInfo("TRAP emit: burst already running, restoring first"); RestoreBurst(); }

        int n = 0;
        foreach (var e in gs.emitters)
        {
            if (e == null) continue;
            try
            {
                var snap = new EmitterSnapshot
                {
                    BaseAmt = e.productionBaseAmt,
                    Interval = e.productionInterval,
                    BaseAmt2 = e.productionBaseAmt2,
                    Interval2 = e.productionInterval2,
                };
                Burst[e.Pointer] = snap;
                e.productionBaseAmt = (long)(snap.BaseAmt * multiplier);
                e.productionBaseAmt2 = (long)(snap.BaseAmt2 * multiplier);
                n++;
                if (n == 1)
                    ModCore.Log.LogInfo(
                        $"TRAP emit: first emitter baseAmt {snap.BaseAmt} -> {e.productionBaseAmt}, interval {snap.Interval}");
            }
            catch (Exception ex) { ModCore.Log.LogWarning($"TRAP emit: boost failed: {ex.Message}"); }
        }

        if (n == 0)
        {
            ModCore.Log.LogInfo("TRAP emit: no emitters on this map - trap had no effect");
            Burst.Clear();
            return;
        }
        _burstGameSpace = gs.Pointer;
        _burstExpiry = Time.time + seconds;
        ModCore.Log.LogInfo($"TRAP emit: boosted {n} emitter(s) x{multiplier} for {seconds:0.#}s");
    }

    /// <summary>Reverts a running emitter burst when its window expires, or
    /// immediately if the mission changed underneath it. Called every frame from
    /// ModCore.Tick; returns straight away when no burst is active.</summary>
    public static void Tick()
    {
        if (Burst.Count == 0) return;

        var gs = GameSpace.instance;
        if (gs == null || gs.Pointer != _burstGameSpace)
        {
            // Mission torn down mid-burst: drop the snapshot rather than writing
            // through stale IL2CPP pointers.
            ModCore.Log.LogInfo($"TRAP emit: mission changed, dropping {Burst.Count} emitter snapshot(s)");
            Burst.Clear();
            return;
        }

        if (Time.time < _burstExpiry) return;
        RestoreBurst();
    }

    private static void RestoreBurst()
    {
        int n = 0;
        var gs = GameSpace.instance;
        if (gs != null)
        {
            foreach (var e in gs.emitters)
            {
                if (e == null) continue;
                if (!Burst.TryGetValue(e.Pointer, out var snap)) continue;
                try
                {
                    e.productionBaseAmt = snap.BaseAmt;
                    e.productionInterval = snap.Interval;
                    e.productionBaseAmt2 = snap.BaseAmt2;
                    e.productionInterval2 = snap.Interval2;
                    n++;
                }
                catch (Exception ex) { ModCore.Log.LogWarning($"TRAP emit: restore failed: {ex.Message}"); }
            }
        }
        Burst.Clear();
        ModCore.Log.LogInfo($"TRAP emit: restored {n} emitter(s)");
    }

    // --- 6. weapon drain --------------------------------------------------

    /// <summary>Empties every player weapon. No tuning knob on purpose: "all
    /// weapons go quiet until the packet network refills them" is the whole
    /// effect, and units resupply on their own, so it is a delay and never a
    /// loss.</summary>
    public static void Drain()
    {
        var gs = Live("drain");
        if (gs == null) return;

        int n = 0, ghosts = 0, stores = 0; float drained = 0f;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!IsPlayerUnit(u)) continue;
                // NOT THE RIFT LAB, and not a ghost - both fixed to match
                // BoonEffects.Resupply, which is this trap's exact mirror.
                //
                // The two had drifted in opposite directions. The lab's "ammo"
                // IS the energy store, so this trap was silently emptying the
                // whole economy to zero on top of the ammo it advertises -
                // which is a far harsher effect than "Ammo Drain" claims, and
                // it duplicated the Energy Drain trap by accident. It also
                // drained units still under construction, which Resupply
                // skipped.
                if (BoonEffects.IsEnergyStore(u)) { stores++; continue; }
                if (u.isBuilding) { ghosts++; continue; }
                float a = u.ammo;
                if (a <= 0f) continue;
                u.ammo = 0f;
                drained += a;
                n++;
            }
            catch { }
        }
        ModCore.Log.LogInfo($"TRAP drain: emptied {n} weapon(s), {drained:0.##} ammo removed"
                    + (ghosts > 0 ? $" (skipped {ghosts} still under construction)" : "")
                    + $" [energy stores skipped: {stores}]");
    }

    // --- readback + live tuning -------------------------------------------

    /// <summary>Dumps what the traps act on, so their numbers can be tuned
    /// against the game's own values rather than guessed. Reports the real
    /// SporeLauncher and Emitter settings this mission ships with - those are
    /// the authoritative "what does a spore normally carry" reference.</summary>
    public static void Status()
    {
        var gs = Live("status");
        if (gs == null) return;

        int units = 0, foreign = 0, stunned = 0, withAmmo = 0;
        float ammoTotal = 0f;
        string sample = "";
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!IsPlayerUnit(u)) { foreign++; continue; }
                units++;
                int sc = u.stunnedCount;
                if (sc > 0) stunned++;
                float a = u.ammo;
                if (a > 0f) { withAmmo++; ammoTotal += a; }
                if (sample.Length < 150)
                    sample += $"[{u.GetDataName()} stun={sc}/{u.STUN_TIME} canStun={u.CAN_STUN} ammo={a:0.#}]";
            }
            catch { }
        }

        // Type/dataName histogram. This is the diagnostic that catches an
        // IsPlayerUnit miss: if a player structure shows up on the "-:" side,
        // the debuff traps would silently do nothing to it.
        var hist = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            string key;
            try { key = (IsPlayerUnit(u) ? "P:" : "-:") + u.GetIl2CppType().Name + "/" + u.GetDataName(); } catch { continue; }
            hist[key] = hist.TryGetValue(key, out var hv) ? hv + 1 : 1;
        }
        var histText = string.Join(" ", hist.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key}={kv.Value}"));

        // The game's own spore settings: the reference for SporePayload.
        string spores = "none";
        try
        {
            var launchers = UnityEngine.Object.FindObjectsOfType<SporeLauncher>();
            if (launchers != null && launchers.Length > 0)
                spores = $"{launchers.Length} launcher(s), first payload={launchers[0].payload} " +
                         $"interval={launchers[0].sporeProductionInterval} behavior={launchers[0].targetBehavior}";
        }
        catch (Exception e) { spores = $"unreadable ({e.Message})"; }

        int emitters = 0; string emSample = "none";
        foreach (var e in gs.emitters)
        {
            if (e == null) continue;
            emitters++;
            if (emitters == 1)
            {
                try { emSample = $"baseAmt={e.productionBaseAmt} interval={e.productionInterval}"; }
                catch { emSample = "unreadable"; }
            }
        }

        ModCore.Log.LogInfo(
            $"TRAP status: playerUnits={units} otherUnits={foreign} stunned={stunned} withAmmo={withAmmo} " +
            $"ammoTotal={ammoTotal:0.#} energy={gs.energyStore:0.##} map={World.WORLD_CELL_WIDTH}x{World.WORLD_CELL_HEIGHT} " +
            $"emitters={emitters} ({emSample}) burstActive={Burst.Count} {sample}");
        ModCore.Log.LogInfo($"TRAP status types: {histText}");
        ModCore.Log.LogInfo($"TRAP status spores: {spores}");
        ModCore.Log.LogInfo(
            $"TRAP tuning: spores={SporeCount}x{SporePayload / (float)Depth:0.#}depth target={SporeTargeting} | " +
            $"creep={CreepDepth / (float)Depth:0.#}depth r={CreepRadius} off={CreepOffset} | " +
            $"energy={EnergyFraction * 100f:0}% | emit=x{EmitMultiplier}/{EmitSeconds:0.#}s | stun={StunSeconds}s");
    }

    /// <summary>Coordinate sanity check. GetCellX/GetCellY convert world->cell,
    /// but the game exposes no inverse, and GetCreeperVertex(cellX, cellY) does
    /// NOT return a world position - trusting it put spawned test units at
    /// negative cells. This proves what the mapping actually is.</summary>
    public static void Coord()
    {
        var gs = Live("coord");
        if (gs == null) return;

        ModCore.Log.LogInfo(
            $"TRAP coord: GetCellX(0)={UnitManager.GetCellX(0f)} GetCellX(50)={UnitManager.GetCellX(50f)} " +
            $"GetCellX(100)={UnitManager.GetCellX(100f)} GetCellY(50)={UnitManager.GetCellY(50f)}");

        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb != null && GameUtil.IsAlive(cb))
        {
            var p = cb.transform.position;
            ModCore.Log.LogInfo(
                $"TRAP coord: mission commandBase world=({p.x:0.##},{p.y:0.##},{p.z:0.##}) " +
                $"cell=({UnitManager.GetCellX(p.x)},{UnitManager.GetCellY(p.z)})");
        }
        else ModCore.Log.LogInfo("TRAP coord: no live commandBase to calibrate against");

        var w = gs.world;
        if (w != null)
        {
            var v = w.GetCreeperVertex(50, 50);
            ModCore.Log.LogInfo(
                $"TRAP coord: GetCreeperVertex(50,50)=({v.x:0.##},{v.y:0.##},{v.z:0.##}) " +
                $"-> cell=({UnitManager.GetCellX(v.x)},{UnitManager.GetCellY(v.z)})  [if this is not (50,50) it is not world space]");
        }
    }

    /// <summary>Spike experiment: fire a wave in one targeting mode and report
    /// where each live spore is actually aimed, as a distance to the nearest
    /// player structure. This is how RANDOM vs STRUCTURE was settled - a
    /// screenshot cannot tell you what a spore is aiming AT.</summary>
    public static void Aim(string mode)
    {
        var gs = Live("aim");
        if (gs == null) return;

        if (!string.IsNullOrEmpty(mode)) Set(new[] { "target=" + mode });

        // Two reference sets: the PLAYER's structures, and EVERY unit. If
        // STRUCTURE means "a random building" rather than "a player building",
        // its targets will sit on top of units in the second set while being far
        // from the first - which is exactly what a lopsided map hides.
        var targets = new System.Collections.Generic.List<Vector2>();
        var allUnits = new System.Collections.Generic.List<Vector2>();
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                var p = u.transform.position;
                var cell = new Vector2(UnitManager.GetCellX(p.x), UnitManager.GetCellY(p.z));
                allUnits.Add(cell);
                if (IsPlayerUnit(u)) targets.Add(cell);
            }
            catch { }
        }

        SporeStrike(0, 0, SporeTargeting);

        int n = 0; float sum = 0f, sumAny = 0f; string detail = "";
        try
        {
            foreach (var sp in UnityEngine.Object.FindObjectsOfType<global::Spore>())
            {
                if (sp == null) continue;
                var tp = sp.targetPosition;
                int tx = UnitManager.GetCellX(tp.x), ty = UnitManager.GetCellY(tp.z);
                var cell = new Vector2(tx, ty);
                float best = -1f, bestAny = -1f;
                foreach (var t in targets)
                {
                    float d = Vector2.Distance(cell, t);
                    if (best < 0f || d < best) best = d;
                }
                foreach (var t in allUnits)
                {
                    float d = Vector2.Distance(cell, t);
                    if (bestAny < 0f || d < bestAny) bestAny = d;
                }
                n++;
                if (best >= 0f) sum += best;
                if (bestAny >= 0f) sumAny += bestAny;
                if (detail.Length < 160)
                    detail += $"[{sp.targetBehavior}->({tx},{ty}) dPlayer={best:0.#} dAnyUnit={bestAny:0.#}]";
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"TRAP aim: {e.Message}"); }

        var where = string.Join(",", targets.Take(6).Select(t => $"({t.x:0},{t.y:0})"));
        ModCore.Log.LogInfo(
            $"TRAP aim: setting={SporeTargeting} (per-spore mode below is what actually applied) " +
            $"liveSpores={n} playerStructures={targets.Count} at [{where}] allUnits={allUnits.Count} " +
            $"avgDistToPlayer={(n > 0 ? sum / n : -1f):0.#} avgDistToAnyUnit={(n > 0 ? sumAny / n : -1f):0.#} cells {detail}");
    }

    /// <summary>Live tuning from the debug channel: "trap:set spores=4 payload=8
    /// depth=2 radius=3 offset=12 stun=15". Amounts are in DEPTH UNITS, not raw
    /// fixed-point, so the numbers stay readable.</summary>
    public static void Set(string[] tokens)
    {
        foreach (var t in tokens)
        {
            var kv = t.Split('=');
            if (kv.Length != 2) continue;
            var key = kv[0].ToLowerInvariant();
            var raw = kv[1];

            // target= takes a word, everything else a number.
            if (key == "target")
            {
                switch (raw.ToLowerInvariant())
                {
                    case "scatter": case "random": SporeTargeting = SporeAim.Scatter; break;
                    case "player": case "building": SporeTargeting = SporeAim.PlayerBuilding; break;
                    case "riftlab": case "base": SporeTargeting = SporeAim.RiftLab; break;
                    default: ModCore.Log.LogWarning($"trap:set target='{raw}' - expected scatter|player|riftlab"); break;
                }
                continue;
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            switch (key)
            {
                case "spores": SporeCount = (int)v; break;
                case "payload": SporePayload = (int)(v * Depth); break;
                case "depth": CreepDepth = (int)(v * Depth); break;
                case "radius": CreepRadius = (int)v; break;
                case "offset": CreepOffset = (int)v; break;
                case "energy": EnergyFraction = Mathf.Clamp01(v > 1f ? v / 100f : v); break;
                case "emitmul": EmitMultiplier = v; break;
                case "emitsec": EmitSeconds = v; break;
                case "stun": StunSeconds = v; break;
                default: ModCore.Log.LogWarning($"trap:set unknown key '{key}'"); break;
            }
        }
        Status();
    }

    // ---------------------------------------------------------------------
    // Proven, deliberately NOT shipped: RE-FOG.
    //
    //     for every cell where world.GetFogTerrain(x, y) > 0:
    //         world.SetDeFogTerrain(x, y, 0);
    //
    // Verified working in both directions on story15 (defogged 0 -> 7845 -> 0,
    // visually confirmed). Note the three layers, because they are easy to
    // confuse: `fogTerrain` is the map's fog DEFINITION, `defogTerrain` is how
    // much has been revealed, and `isFogTerrain` is derived "currently dark"
    // state - keying a re-fog scan off the last one finds nothing to do the
    // moment anything is revealed, which cost an hour in the spike.
    //
    // Dropped because only fog missions have any fog at all (0 cells across the
    // first 8 surveyed, 7845 on story15), and on those missions lifting the
    // darkness IS the objective, so re-fogging reads as progress loss rather
    // than a setback. Full detail in docs/design/2026-08-26-traps-spike.md.
    // ---------------------------------------------------------------------
}
