using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// The "trap:" and "boon:cache" commands that are not a player's trap or boon:
/// readbacks, live tuning, the spore-aim experiment, the emitter burst, and a
/// single-ware resource cache with full logging. All of it used to sit in the
/// mod's TrapEffects / BoonEffects, compiled into every player's install; the
/// mod now holds only what a received item actually fires.
///
/// The player's tuned values are constants in TrapEffects. "trap:set" does not
/// change them: it sets overrides HERE, which the "trap:" commands pass as
/// arguments - every trap method takes its amounts as parameters, with 0
/// meaning "the tuned default". So a value can still be tried live, and a
/// received trap item always fires exactly what ships.
/// </summary>
internal static class DebugTraps
{
    /// <summary>One unit of displayed creeper depth in the game's fixed point -
    /// the same scale TrapEffects uses, so "trap:set" amounts read as depth.</summary>
    private const int Depth = 1_000_000;

    private static GameSpace? Live(string what)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogInfo($"TRAP {what}: no GameSpace (not in a mission)"); return null; }
        if (GameSpace.editMode) { ModCore.Log.LogInfo($"TRAP {what}: edit mode, skipped"); return null; }
        return gs;
    }

    // --- the "trap:" command -------------------------------------------------

    /// <summary>"trap:&lt;name&gt; [args]". Omitted or zero arguments mean the
    /// override from "trap:set" if there is one, else the mod's tuned default.
    /// Amounts are in depth units.</summary>
    internal static void Command(string[] tok)
    {
        var name = tok.Length > 0 ? tok[0].ToLowerInvariant() : "";
        float A(int i, string key) =>
            tok.Length > i && float.TryParse(tok[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0f
                ? v : Override(key);

        switch (name)
        {
            // Two distinct spore traps: a fair scatter, and a strike on a
            // random building. "trap:spore" uses the target from trap:set.
            case "spore":    Spore((int)A(1, "spores"), (int)(A(2, "payload") * Depth)); return;
            case "scatter":  TrapEffects.SporeStrikeScatter((int)A(1, "spores"), (int)(A(2, "payload") * Depth)); return;
            case "building": TrapEffects.SporeStrikeBuilding((int)A(1, "spores"), (int)(A(2, "payload") * Depth)); return;
            case "creep":  TrapEffects.Creep((int)A(1, "radius"), (int)(A(2, "depth") * Depth)); return;
            case "energy": TrapEffects.Energy(A(1, "energy")); return;
            case "emit":   Emit(A(1, "emitsec"), A(2, "emitmul")); return;
            case "stun":   TrapEffects.Stun(A(1, "stun")); return;
            case "drain":  TrapEffects.Drain(); return;
            case "status": Status(); return;
            case "set":    Set(tok.Skip(1).ToArray()); return;
            case "aim":    Aim(tok.Length > 1 ? tok[1] : ""); return;
            case "coord":  Coord(); return;
            default:
                ModCore.Log.LogWarning(
                    $"trap: unknown effect '{name}' - expected spore|scatter|building|creep|energy|emit|stun|drain|status|set|aim|coord");
                return;
        }
    }

    /// <summary>"trap:set" values, keyed as trap:set spells them. Absent means
    /// the mod's tuned default.</summary>
    private static readonly Dictionary<string, float> Overrides = new();

    private static float Override(string key) => Overrides.TryGetValue(key, out var v) ? v : 0f;

    /// <summary>A tuned default, read off the mod's constant for the status
    /// line. Constants are readable by reflection like any static field.</summary>
    private static string Shipped(string field)
    {
        try { return AccessTools.Field(typeof(TrapEffects), field)?.GetValue(null)?.ToString() ?? "?"; }
        catch { return "?"; }
    }

    // --- spore aim --------------------------------------------------------

    /// <summary>What "trap:spore" fires: the player-building strike by default,
    /// or the scatter. Set with "trap:set target=scatter|player".</summary>
    private static bool _sporeScatter;

    internal static void Spore(int count, int payload)
    {
        if (_sporeScatter) TrapEffects.SporeStrikeScatter(count, payload);
        else TrapEffects.SporeStrikeBuilding(count, payload);
    }

    // --- readback + live tuning -------------------------------------------

    /// <summary>Dumps what the traps act on, so their numbers can be tuned
    /// against the game's own values rather than guessed. Reports the real
    /// SporeLauncher and Emitter settings this mission ships with - those are
    /// the authoritative "what does a spore normally carry" reference.</summary>
    internal static void Status()
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
                if (!GameUtil.IsPlayerUnit(u)) { foreign++; continue; }
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
        var hist = new Dictionary<string, int>();
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            string key;
            try { key = (GameUtil.IsPlayerUnit(u) ? "P:" : "-:") + u.GetIl2CppType().Name + "/" + u.GetDataName(); } catch { continue; }
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
        // Shipped values as raw constants (amounts in fixed point), then the
        // trap:set overrides the commands are using on top of them.
        float emitMul = Override("emitmul") > 0f ? Override("emitmul") : EmitMultiplier;
        float emitSec = Override("emitsec") > 0f ? Override("emitsec") : EmitSeconds;
        ModCore.Log.LogInfo(
            $"TRAP tuning: shipped spores={Shipped("SporeCount")} payload={Shipped("SporePayload")} " +
            $"creepDepth={Shipped("CreepDepth")} r={Shipped("CreepRadius")} off={Shipped("CreepOffset")} " +
            $"energy={Shipped("EnergyFraction")} stun={Shipped("StunSeconds")}s | " +
            $"target={(_sporeScatter ? "Scatter" : "PlayerBuilding")} emit=x{emitMul}/{emitSec:0.#}s | " +
            $"overrides: {(Overrides.Count == 0 ? "none" : string.Join(" ", Overrides.Select(kv => $"{kv.Key}={kv.Value:0.##}")))}");
    }

    /// <summary>Coordinate sanity check. GetCellX/GetCellY convert world->cell,
    /// but the game exposes no inverse, and GetCreeperVertex(cellX, cellY) does
    /// NOT return a world position - trusting it put spawned test units at
    /// negative cells. This proves what the mapping actually is.</summary>
    internal static void Coord()
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
    internal static void Aim(string mode)
    {
        var gs = Live("aim");
        if (gs == null) return;

        if (!string.IsNullOrEmpty(mode)) Set(new[] { "target=" + mode });

        // Two reference sets: the PLAYER's structures, and EVERY unit. If
        // STRUCTURE means "a random building" rather than "a player building",
        // its targets will sit on top of units in the second set while being far
        // from the first - which is exactly what a lopsided map hides.
        var targets = new List<Vector2>();
        var allUnits = new List<Vector2>();
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                var p = u.transform.position;
                var cell = new Vector2(UnitManager.GetCellX(p.x), UnitManager.GetCellY(p.z));
                allUnits.Add(cell);
                if (GameUtil.IsPlayerUnit(u)) targets.Add(cell);
            }
            catch { }
        }

        Spore(0, 0);

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
            $"TRAP aim: setting={(_sporeScatter ? "Scatter" : "PlayerBuilding")} (per-spore mode below is what actually applied) " +
            $"liveSpores={n} playerStructures={targets.Count} at [{where}] allUnits={allUnits.Count} " +
            $"avgDistToPlayer={(n > 0 ? sum / n : -1f):0.#} avgDistToAnyUnit={(n > 0 ? sumAny / n : -1f):0.#} cells {detail}");
    }

    /// <summary>Live tuning: "trap:set spores=4 payload=8 depth=2 radius=3
    /// stun=15". Amounts are in DEPTH UNITS, not raw fixed-point, so the numbers
    /// stay readable. Sets overrides the "trap:" commands pass as arguments; 0
    /// clears one. The creep offset is not a parameter of the trap, so it is
    /// not tunable here.</summary>
    internal static void Set(string[] tokens)
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
                    case "scatter": case "random": _sporeScatter = true; break;
                    case "player": case "building": _sporeScatter = false; break;
                    default: ModCore.Log.LogWarning($"trap:set target='{raw}' - expected scatter|player"); break;
                }
                continue;
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            if (key == "energy") v = Mathf.Clamp01(v > 1f ? v / 100f : v);
            switch (key)
            {
                case "spores": case "payload": case "depth": case "radius":
                case "energy": case "emitmul": case "emitsec": case "stun":
                    if (v > 0f) Overrides[key] = v;
                    else Overrides.Remove(key);
                    break;
                default: ModCore.Log.LogWarning($"trap:set unknown key '{key}'"); break;
            }
        }
        Status();
    }

    // --- emitter burst (timed, self-restoring) -----------------------------
    //
    // The spike's seventh trap. Not a player's trap any more: it no-ops on any
    // mission that ships no emitters (measured absent on story1, story5 and
    // story8; present on story2 (1), story3 (2), story4 (4), story6 (3), story7
    // (2)), so Emitter Overdrive Trap is never generated and the mod does not
    // apply it. Kept here so the effect can still be exercised.

    /// <summary>How long enemy emitters run boosted, and by how much. ALWAYS
    /// restored from a snapshot - a permanent change would be a difficulty edit,
    /// not a trap.</summary>
    private const float EmitSeconds = 20f;
    private const float EmitMultiplier = 3f;

    private struct EmitterSnapshot
    {
        public long BaseAmt;
        public int Interval;
        public long BaseAmt2;
        public int Interval2;
    }

    private static readonly Dictionary<IntPtr, EmitterSnapshot> Burst = new();
    private static float _burstExpiry;
    private static IntPtr _burstGameSpace = IntPtr.Zero;

    /// <summary>Multiplies every enemy emitter's output for a window, then puts
    /// the snapshot back. Carries state, which is why DebugTick drives Tick()
    /// below - the restore must be guaranteed.</summary>
    internal static void Emit(float seconds, float multiplier)
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
    /// immediately if the mission changed underneath it. Called every frame;
    /// returns straight away when no burst is active.</summary>
    internal static void Tick()
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

    // --- single-ware resource cache ("boon:cache <ware> [amount] [create]") -

    /// <summary>One ware, every player factory, with a line per factory. The
    /// player's Resource Cache grants every channelled ware with one summary
    /// line (BoonEffects.ResourceCache); this is the measuring version.
    ///
    /// <paramref name="useCreate"/> selects CreateProducedWares over
    /// SetProducedWares, so a run can measure which write the sim honours
    /// instead of guessing. This repo has already been caught writing a
    /// recomputed summary (gs.energyStore) and moving nothing but the HUD.</summary>
    internal static void CacheOne(string which, int amount, bool useCreate)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("BOON resource cache: no GameSpace"); return; }

        int ware = BoonEffects.WareId(which);
        if (ware < 0)
        {
            ModCore.Log.LogWarning($"BOON resource cache: '{which}' is not a ware this mission defines");
            return;
        }
        if (amount <= 0) amount = BoonEffects.CacheWareAmount;

        int cap = int.MaxValue;
        try { cap = Factory.MAX_WARES; } catch { }

        // Both collections: flying units are not in gs.units.
        var all = new List<UnitManager>();
        foreach (var u in gs.units) all.Add(u);
        var fly = gs.flyingUnits;
        if (fly != null)
            foreach (var f in fly) all.Add(f);

        int factories = 0, granted = 0;
        foreach (var u in all)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                var f = u.TryCast<Factory>();
                if (f == null) continue;
                factories++;

                int channel = -1;
                try { channel = f.GetChannelForWare(ware); } catch { }
                if (channel < 0)
                {
                    ModCore.Log.LogInfo(
                        $"BOON resource cache: {which} on factory {factories}: " +
                        "no channel for it on this mission (whiff, not a failure)");
                    continue;
                }

                int before = f.GetProducedWares(ware);
                int want = before + amount;
                if (want > cap) want = cap;
                if (want <= before)
                {
                    ModCore.Log.LogInfo(
                        $"BOON resource cache: {which} on factory {factories}: " +
                        $"already at {before} of cap {cap}, nothing to add");
                    continue;
                }

                if (useCreate) f.CreateProducedWares(ware, want - before);
                else f.SetProducedWares(ware, want);

                int after = f.GetProducedWares(ware);
                granted += after - before;
                ModCore.Log.LogInfo(
                    $"BOON resource cache: {which} on factory {factories} ch{channel}: " +
                    $"{before} -> {after} (asked for {want}, cap {cap}, " +
                    $"{(useCreate ? "CreateProducedWares" : "SetProducedWares")})"
                    + (after == before ? "  <- WRITE DID NOT LAND" : ""));
            }
            catch (Exception e)
            {
                ModCore.Log.LogWarning($"BOON resource cache: factory threw {e.Message}");
            }
        }

        if (factories == 0)
            ModCore.Log.LogInfo(
                $"BOON resource cache: {which} whiffed - no factory built (this is allowed)");
        else
            ModCore.Log.LogInfo(
                $"BOON resource cache: {which} +{granted} across {factories} factory(ies)");
    }
}
