using System;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// The Archipelago trap effects: hostile but RECOVERABLE in-mission events.
/// Nothing here may make a mission unwinnable - no terrain deformation, no
/// destroying the rift lab, no permanent difficulty change.
///
/// Six effects, from the traps spike (docs/design/2026-08-26-traps-spike.md):
/// spore strike, spore scatter, creeper surge, energy drain, unit stun, weapon
/// drain. All fire-and-forget, and all depend only on what EVERY mission has
/// (world grid, energy store, the player's units, the spore system).
///
/// The spike's seventh, the emitter burst, is not here: it no-ops on any mission
/// that ships no emitters, so Emitter Overdrive Trap is never generated (see
/// items.POOL_TRAP_ITEMS) and the mod does not apply it. The effect lives on in
/// the debug plugin's "trap:emit". Re-fog was dropped outright - see the block
/// at the end.
///
/// THESE RUN IN A PLAYER'S GAME. TrapApplier dispatches every one of them from
/// received AP items, at trap_percentage. The debug plugin's "trap:" commands
/// are an additional way in for testing, and everything that exists only for
/// them - the readbacks, the live tuning parser, the aim experiment - lives in
/// that plugin, not here.
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
    private const int SporeCount = 2;
    private const int SporePayload = 20 * Depth;

    /// <summary>Where a spore strike aims. This is OUR mode, not the game's
    /// enum, because the game's three behaviours do not line up with what a trap
    /// wants (measured with the debug plugin's trap:aim):
    ///   game RANDOM    - an arbitrary map point. Used by Scatter.
    ///   game STRUCTURE - a random building of ANY owner. All 12 test spores
    ///                    landed exactly on a unit, but on story1 the player
    ///                    owned 1 of 36 buildings, so it mostly hits scenery and
    ///                    enemy structures. Not steerable, so not shipped.
    ///   game LOCATION  - aims exactly where told (distance 0, verified). This
    ///                    is how PlayerBuilding is implemented: we pick the cell
    ///                    ourselves.
    /// </summary>
    private enum SporeAim
    {
        /// <summary>Random map points - what CW4's own launchers do.</summary>
        Scatter,
        /// <summary>A random building OF THE PLAYER'S, chosen per spore.</summary>
        PlayerBuilding,
    }

    /// <summary>Creeper surge: depth added per cell, and the radius it covers.
    /// Deliberately small - a trap should sting, not end the mission.</summary>
    private const int CreepDepth = 2 * Depth;
    private const int CreepRadius = 3;

    /// <summary>How far from the rift lab the surge lands, in cells. Dropping it
    /// directly on the base is a near-instant loss on an undefended start.</summary>
    private const int CreepOffset = 12;

    /// <summary>Stun window for the player's units, in seconds. 0 means "use
    /// each unit's own STUN_TIME", which is what the game itself applies when
    /// something stuns a unit - the rift lab reports STUN_TIME = 300 ticks = 10s.
    /// Defaulting to it makes the trap exactly one natural stun rather than a
    /// number I invented (15s was 1.5x the game's own).</summary>
    private const float StunSeconds = 0f;

    /// <summary>Fraction of the energy store to take. A flat amount is
    /// invisible (5 off a store of 100 refilled almost instantly), so this is
    /// proportional: 1.0 empties the bank.</summary>
    private const float EnergyFraction = 1.0f;

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

    /// <summary>Launches a small wave of spores, aimed per aimMode.
    /// SporeLauncher.CreateSpore is static, so this needs no launcher unit and
    /// works on every mission. The player can shoot the spores down, which is
    /// what makes every variant of this the friendliest trap in the set.</summary>
    private static void SporeStrike(int count, int payload, SporeAim aimMode)
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
