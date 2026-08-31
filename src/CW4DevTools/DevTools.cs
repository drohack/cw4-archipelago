using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace CW4DevTools;

/// <summary>
/// TOGGLING A CHEAT OFF MUST UNDO IT. Anything that CHANGES a game parameter
/// snapshots the original on the first apply and restores it on release
/// (AllBuildings, GameSpeed, FreezeCreeper); snapshots are dropped on mission
/// change so one mission's values are never restored into another.
///
/// Cheats that GRANT something - a finished building, ammo, health, resources -
/// are one-way by nature and are not undone; you cannot un-build a tower. That
/// is fine as long as what they grant is VALID. It was not once: ware slots were
/// filled with an invented 1000 rather than the unit's own capacity, which left
/// sprayers believing they were full so they ignored porter deliveries - and
/// switching the cheat off did not help, because the bad value was already
/// written into the units. Grant real values, never plausible-looking ones.
///
/// The cheats, each independently toggleable by config or hotkey. Every
/// one is scoped to the PLAYER's units; enemies, creeper, terrain and mission
/// objectives are never touched, so a mission still requires whatever it
/// required - only the grind is removed.
/// </summary>
public static class DevTools
{
    private static ManualLogSource _log = null!;
    private static DevCommands? _cmd;

    public static void Init(ManualLogSource log)
    {
        _log = log;
        _cmd = new DevCommands(log);
    }

    /// <summary>The Home-key diagnostic, reachable from the command channel too.</summary>
    public static void DumpUnitsNow()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVTOOLS: no GameSpace to dump"); return; }
        DumpUnits(gs);
    }

    // Availability flags the game exposes on BuildUnitManager. This mirrors the
    // full set (26); "all buildings" simply forces every one true. Kept as its
    // own copy on purpose - this plugin must not depend on the randomizer.
    private static readonly List<Action<BuildUnitManager, bool>> Setters = new()
    {
        (b, v) => b.riftLabAvailable = v,       (b, v) => b.towerAvailable = v,
        (b, v) => b.pylonAvailable = v,         (b, v) => b.minerAvailable = v,
        (b, v) => b.factoryAvailable = v,       (b, v) => b.ernPortalAvailable = v,
        (b, v) => b.greenarRefineryAvailable = v, (b, v) => b.terpAvailable = v,
        (b, v) => b.porterAvailable = v,        (b, v) => b.cannonAvailable = v,
        (b, v) => b.mortarAvailable = v,        (b, v) => b.sprayerAvailable = v,
        (b, v) => b.sniperAvailable = v,        (b, v) => b.missileLauncherAvailable = v,
        (b, v) => b.nullifierAvailable = v,     (b, v) => b.runwayAvailable = v,
        (b, v) => b.bomberPadAvailable = v,     (b, v) => b.acBomberPadAvailable = v,
        (b, v) => b.rocketPadAvailable = v,     (b, v) => b.platformAvailable = v,
        (b, v) => b.shieldAvailable = v,        (b, v) => b.microRiftAvailable = v,
        (b, v) => b.chronatAvailable = v,       (b, v) => b.airshipAvailable = v,
        (b, v) => b.berthaAvailable = v,        (b, v) => b.sweeperAvailable = v,
    };

    // Mirror of Setters for READING the flags, so AllBuildings can put back what
    // the mission had. Without this, turning AllBuildings off left every building
    // in the sidebar: the force stopped, but nothing restored the originals.
    private static readonly List<Func<BuildUnitManager, bool>> Getters = new()
    {
        b => b.riftLabAvailable,
        b => b.towerAvailable,
        b => b.pylonAvailable,
        b => b.minerAvailable,
        b => b.factoryAvailable,
        b => b.ernPortalAvailable,
        b => b.greenarRefineryAvailable,
        b => b.terpAvailable,
        b => b.porterAvailable,
        b => b.cannonAvailable,
        b => b.mortarAvailable,
        b => b.sprayerAvailable,
        b => b.sniperAvailable,
        b => b.missileLauncherAvailable,
        b => b.nullifierAvailable,
        b => b.runwayAvailable,
        b => b.bomberPadAvailable,
        b => b.acBomberPadAvailable,
        b => b.rocketPadAvailable,
        b => b.platformAvailable,
        b => b.shieldAvailable,
        b => b.microRiftAvailable,
        b => b.chronatAvailable,
        b => b.airshipAvailable,
        b => b.berthaAvailable,
        b => b.sweeperAvailable
    };

    private static bool[]? _savedAvail;
    private static bool _forcedAll;


    // Which units count as "yours". This has to be a name list, and getting it
    // right took three wrong turns worth recording:
    //
    //  1. UnitManager.enemy is not a player/enemy discriminator - hostile Pod,
    //     Ultrac and SuperTower all report false, only Emitter reports true.
    //  2. UnitConstants.ENEMY (the per-TYPE flag in UnitData) reads false for
    //     all 88 unit types, so it is a default template, not per-map truth.
    //  3. THE BUILD-PANE KEYS ARE NOT UNIT NAMES. The registry holds 88 names
    //     and none of them are "pylon", "miner" or "porter" - so filtering on
    //     those keys silently skipped exactly those buildings and every cheat
    //     passed them over. Build ghosts gave the real names:
    //         riftlab -> CommandBase      pylon      -> TowerBridge
    //         miner   -> Collector        ernportal  -> ERNInterface
    //     Both spellings are listed below and matched case-insensitively, so
    //     either works.
    //
    // A whitelist is deliberate over a blacklist: missing one of the player's
    // buildings is a visible annoyance, but missing one HOSTILE type would make
    // an emitter indestructible. If a building is being skipped, DumpUnits
    // (Home) prints its real name and this filter's verdict - add it here.
    //
    // The randomizer hit the identical bug in GameUtil.IsPlayerUnit; the shared
    // write-up is docs/research-findings.md, "Unit naming". This plugin keeps its
    // own copy because it must not depend on the randomizer.
    private static readonly HashSet<string> PlayerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core base
        "CommandBase", "riftlab", "Tower", "SuperTower", "TowerBridge", "pylon",
        // Economy
        "Collector", "CollectorPanel3", "CollectorPanel5", "miner",
        "Factory", "GreenarRefinery", "ERNInterface", "ernportal", "ERN",
        "StoragePad", "Stash", "DeliveryPad", "DeliveryDrone", "porter", "Reactor",
        // Weapons
        "Cannon", "Mortar", "Sprayer", "Sniper", "MissileLauncher", "Nullifier",
        "Bertha", "Sweeper",
        // Air
        "Runway", "BomberPad", "Bomber", "ACBomberPad", "ACBomber",
        "RocketPad", "Rocket", "Airship",
        // Utility
        "Terp", "TerpDrone", "GreenarDrone", "Platform", "Shield", "Microrift", "Chronat",
    };

    private static bool IsPlayerUnit(UnitManager u)
    {
        try
        {
            if (u == null) return false;
            var key = u.GetDataName();
            if (key == null) return false;
            if (PlayerKeys.Contains(key)) return true;

            // CMOD units (airship, bertha, sweeper and any custom unit) report a
            // GUID as their data name, never a name, so no name list can ever
            // match them - which is why those three kept building normally while
            // everything else was instant. Resolve the GUID through
            // GameSpace.cmods: a CMod with a non-empty playerMenuUnitName is
            // offered in the PLAYER's build menu, which is exactly the ownership
            // test needed. Data-driven, so new custom units work for free.
            return IsPlayerCmod(key);
        }
        catch { return false; }
    }

    /// <summary>Both unit collections. Flying units are NOT in gs.units.</summary>
    private static IEnumerable<UnitManager> AllUnits(GameSpace gs)
    {
        foreach (var u in gs.units) yield return u;
        var fly = gs.flyingUnits;
        if (fly != null)
            foreach (var f in fly) yield return f;
    }

    private static int CountPlayerCmods(GameSpace gs)
    {
        int n = 0;
        try
        {
            var cmods = gs.cmods;
            if (cmods != null)
                foreach (var kv in cmods)
                {
                    try { if (!string.IsNullOrEmpty(kv.Value?.playerMenuUnitName)) n++; } catch { }
                }
        }
        catch { }
        return n;
    }

    // Unit pointers present at mission start: the map's own content.
    private static readonly HashSet<IntPtr> PreExisting = new();
    private static bool _preExistingCaptured;

    private static bool IsPreExisting(UnitManager u)
    {
        try { return PreExisting.Contains(u.Pointer); }
        catch { return false; }
    }

    /// <summary>Snapshot the map's units once per mission, so everything built
    /// afterwards is known to be the player's.</summary>
    private static void CapturePreExisting(GameSpace gs)
    {
        PreExisting.Clear();
        try
        {
            foreach (var u in AllUnits(gs))
            {
                if (u == null) continue;
                try { PreExisting.Add(u.Pointer); } catch { }
            }
        }
        catch { }
        _preExistingCaptured = true;
        _log.LogInfo($"DEVTOOLS: {PreExisting.Count} pre-existing map unit(s) will not be touched");
    }

    private static bool IsPlayerCmod(string guid)
    {
        try
        {
            var cmods = GameSpace.instance?.cmods;
            if (cmods == null) return false;
            if (!cmods.ContainsKey(guid)) return false;
            var cmod = cmods[guid];
            if (cmod == null) return false;
            var menuName = cmod.playerMenuUnitName;
            return !string.IsNullOrEmpty(menuName);
        }
        catch { return false; }
    }

    public static void SafeTick()
    {
        try { Tick(); }
        catch (Exception e) { _log.LogError($"dev tick failed: {e.Message}"); }
    }

    private static IntPtr _lastGameSpace = IntPtr.Zero;
    private static int _captureCountdown;

    private static void Tick()
    {
        _cmd?.Tick();
        Hotkeys();
        if (DevConfig.ShowOverlay.Value) DevOverlay.Tick();

        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            // Drop every snapshot: restoring one mission's values into another
            // would be worse than not restoring at all.
            _forcedAll = false;
            _savedAvail = null;
            _savedSpeed = -1;
            _savedMaxAmmo = -1f;
            _frozen = false;
            _toughApplied = false;
            _savedTough.Clear();
            _preExistingCaptured = false;
            _captureCountdown = 120;   // ~2s: let the map finish spawning its units
            // Per-mission diagnostics, so each mission reports afresh.
            _completed.Clear();
            _skipped.Clear();
            _log.LogInfo($"DEVTOOLS: new mission - {DevConfig.Summary()}");
            if (DevConfig.DumpUnitsOnStart.Value) DumpUnits(gs);
        }

        if (!_preExistingCaptured)
        {
            if (--_captureCountdown > 0) return;   // nothing applied until the map settles
            CapturePreExisting(gs);
        }

        ApplyAllBuildings(gs);   // handles both on and the off transition
        if (DevConfig.InfiniteResources.Value) TopUpEnergy(gs); else ReleaseEnergy(gs);
        ApplyGenerationBonus(gs);
        ApplyGameSpeed(gs);
        ApplyCreeperFreeze(gs);

        bool build = DevConfig.InstantBuild.Value;
        bool res = DevConfig.InfiniteResources.Value;
        bool tough = DevConfig.Indestructible.Value;
        // Indestructible sets flags ON the unit, so releasing it needs one more
        // pass to put them back - the same "turning it off must undo it" rule
        // the other cheats follow. Without this term the early return below
        // would skip that pass and leave the flags set for the rest of the
        // mission, which is exactly the bug AllBuildings once had.
        bool releaseTough = !tough && _toughApplied;
        if (!build && !res && !tough && !releaseTough) return;

        // AllUnits covers gs.units AND gs.flyingUnits - flying units (the airship)
        // are absent from gs.units, so iterating that alone left them untouched
        // and the airship looked like it was merely "building slowly".
        try
        {
            foreach (var u in AllUnits(gs)) Apply(u, build, res, tough);
        }
        catch { }

        if (releaseTough)
        {
            _toughApplied = false;
            _savedTough.Clear();
            _log.LogInfo("DEVTOOLS: Indestructible off - unit damage flags restored");
        }
    }

    private static void Apply(UnitManager u, bool build, bool res, bool tough)
    {
        if (u == null) return;
        try
        {
            // Never touch map content. The name list cannot tell a player's
            // SuperTower or Stash from one the MAP placed - both dumped as
            // "MINE" - and an indestructible map object can make a mission
            // unwinnable, which is the opposite of a survey aid. Anything present
            // when the mission started is the map's; anything appearing later is
            // the player's. Simple, and it closes the whole class.
            if (IsPreExisting(u)) return;

            if (!IsPlayerUnit(u))
            {
                ReportSkippedBuild(u);
                return;
            }

            if (build) FinishBuild(u);
            if (res) TopUpUnit(u);
            if (tough) MakeTough(u);
            else if (_toughApplied) ReleaseTough(u);
        }
        catch { /* unit mid-teardown */ }
    }

    // What each unit's damage flags were before Indestructible touched them.
    // Keyed by pointer and cleared on release and on mission change, so a dead
    // unit's entry can never be restored onto a live one.
    private static readonly Dictionary<IntPtr, (bool Impervious, bool Uneven)> _savedTough = new();
    private static bool _toughApplied;

    /// <summary>Keep one of the player's units alive.
    ///
    /// Holding health at maximum is NOT enough on its own, and platforms are the
    /// case that proved it: CW4 has destruction paths that never reduce health.
    /// UnitManager carries DESTROY_ON_UNEVEN_TERRAIN, which removes a unit
    /// outright when the ground under it stops being flat, and Platform
    /// overrides DestroyUnit. A health clamp cannot see either of those - the
    /// unit simply disappears at full health.
    ///
    /// So drive the game's OWN switch, UnitManager.impervious, and lift the
    /// terrain rule. The clamp stays as well, because impervious only stops NEW
    /// damage: a unit already hurt when the cheat went on would otherwise stay
    /// hurt and read as the cheat not working.</summary>
    private static void MakeTough(UnitManager u)
    {
        try
        {
            var key = u.Pointer;
            if (!_savedTough.ContainsKey(key))
                _savedTough[key] = (u.impervious, u.DESTROY_ON_UNEVEN_TERRAIN);
            _toughApplied = true;

            if (!u.impervious) u.impervious = true;
            if (u.DESTROY_ON_UNEVEN_TERRAIN) u.DESTROY_ON_UNEVEN_TERRAIN = false;

            float max = u.MAX_HEALTH;
            if (max > 0f && u.health < max) u.health = max;
        }
        catch { }
    }

    /// <summary>Put back exactly what MakeTough changed. Health is deliberately
    /// left where it is: a grant cannot be un-granted, and re-damaging a unit on
    /// release would be a surprise rather than a revert.</summary>
    private static void ReleaseTough(UnitManager u)
    {
        try
        {
            if (!_savedTough.TryGetValue(u.Pointer, out var saved)) return;
            u.impervious = saved.Impervious;
            u.DESTROY_ON_UNEVEN_TERRAIN = saved.Uneven;
        }
        catch { }
    }

    /// <summary>Finish a unit under construction. CompleteTheBuild(force) skips
    /// the remaining cost as well as the wait.
    ///
    /// isBuilding is the correct and only signal here. A "does it have a build
    /// bar" fallback was tried and removed: HasBuildBar/BuildBarCubes describe
    /// the BAR (5 cubes on everything), not remaining progress, so it flagged
    /// every finished building - including ones instant-built a frame earlier.
    /// </summary>
    private static void FinishBuild(UnitManager u)
    {
        bool building;
        try { building = u.isBuilding; } catch { return; }
        if (!building) return;

        // CompleteTheBuild(force: true) already skips the remaining cost. An
        // ApplyBuildEnergy call here was belt-and-braces from a misdiagnosis and
        // is one more write for no benefit.
        try { u.CompleteTheBuild(true); } catch { }

        // One line per unit type: confirms which buildings InstantBuild reaches.
        // This is how the pylon/miner miss was proven fixed - 'towerbridge' and
        // 'collector' now appear; before the name fix they never did.
        try
        {
            var key = u.GetDataName() ?? "?";
            if (_completed.Add(key)) _log.LogInfo($"DEVTOOLS: instant-built '{key}'");
        }
        catch { }
    }

    private static readonly HashSet<string> _completed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A unit that is UNDER CONSTRUCTION but fails the player filter is
    /// almost certainly one of the player's buildings whose real name is missing
    /// from PlayerKeys - enemy structures do not build. This is the diagnostic
    /// that catches the next "pylon" without a false positive: it keys off
    /// isBuilding, not off HasBuildBar (which is true on every finished unit).
    ///
    /// Known unresolved: "porter" has no match in the game's 88-name registry
    /// (Farsite grants it at story12), so a placed porter should surface here.
    /// The literal "porter" in PlayerKeys can never match, because GetDataName()
    /// only ever returns a registry name - do not read its presence as coverage.
    /// Unverified candidates: Strider, Workall, Transformer, Max.</summary>
    private static void ReportSkippedBuild(UnitManager u)
    {
        try
        {
            if (!u.isBuilding) return;
            var key = u.GetDataName() ?? "?";
            if (!_skipped.Add(key)) return;
            _log.LogWarning(
                $"DEVTOOLS: '{key}' ({u.GetIl2CppType().Name}) is building but is not in the " +
                "player list, so the cheats are skipping it. If it is yours, add that name to " +
                "DevTools.PlayerKeys and GameUtil.ExtraPlayerKeys - see " +
                "docs/research-findings.md, 'Unit naming'.");
        }
        catch { }
    }

    /// <summary>Top up what this unit holds.
    ///
    /// Four versions; the failures are all the same shape - a rule that fixed one
    /// unit type broke the other - so the fix is ORDER, not a smarter rule:
    ///  1. Fill only non-empty slots -> an EMPTY factory never got anything.
    ///  2. Fill all 16 slots on everything -> factories worked, but weapon ammo
    ///     was clobbered, because weapon ammo is ware-backed.
    ///  3. Split on MAX_AMMO > 0 -> factories report a MAX_AMMO too, so they were
    ///     misread as weapons and skipped again.
    ///  4. Skip slots listed in AMMO_WARES -> a factory's storage IS its
    ///     AMMO_WARES, so that skipped the very slots meant to be filled.
    ///
    /// So: fill every slot, then set u.ammo LAST. Whatever the ware write does to
    /// a weapon's ammo, the assignment afterwards puts it right, every frame.
    /// Both cases hold without needing to tell the unit types apart at all.</summary>
    private static void TopUpUnit(UnitManager u)
    {
        // Fill each slot to the amount the unit ACTUALLY wants, from AMMO_WARES
        // (ware type -> required amount). An arbitrary large value is not
        // "generous", it is wrong: a unit holding more than its capacity believes
        // it is full and stops requesting deliveries, so a sprayer stuffed with
        // 1000 bluite silently refuses everything a porter brings it and never
        // sprays. That state also survives switching the cheat back off, which
        // makes it look unrelated to the tools.
        Il2CppSystem.Collections.Generic.Dictionary<int, int>? wants = null;
        try { wants = u.AMMO_WARES; } catch { }

        bool changed = false;
        if (wants != null)
        {
            foreach (var kv in wants)
            {
                try
                {
                    int slot = kv.Key;
                    float want = kv.Value;
                    if (want <= 0f) continue;
                    if (u.GetWareHeld(slot) >= want) continue;
                    u.SetWareHeld(slot, want);
                    changed = true;
                }
                catch { }
            }
        }

        if (changed)
        {
            try { u.UpdateBarsFromWaresHeld(-1); } catch { }
        }

        // LAST, deliberately: the ware writes above can disturb ware-backed ammo.
        try
        {
            float maxAmmo = u.MAX_AMMO;
            if (maxAmmo > 0f && u.ammo < maxAmmo) u.ammo = maxAmmo;
        }
        catch { }
    }

    private const int WareSlots = 16;

    /// <summary>Energy really infinite. The first version aimed at
    /// max(100, production * 10), which tracked production rather than demand and
    /// so still ran dry under heavy building. Writing a large value every frame is
    /// simpler and cannot stall: if the sim clamps it to capacity, it is clamped
    /// to FULL every frame, which is the same thing from the player's side.</summary>
    /// <summary>Infinite energy, done through the value the game actually uses.
    ///
    /// This was WRONG for a long time and the fix is worth recording. It used to
    /// write GameSpace.energyStore and energyProduction, which look like the
    /// energy economy and are not: they are summaries the sim RECOMPUTES every
    /// tick from the network. The HUD dutifully showed GEN in the millions while
    /// the store filled at its normal ~2/sec - the cheat inflated a scoreboard
    /// and delivered no energy at all.
    ///
    /// What is real: the energy store IS the rift lab's ammo, and its ceiling is
    /// the rift lab's MAX_AMMO. Measured directly - store 63 against riftlab
    /// ammo 62.999, and the store capped at exactly MAX_AMMO 100 until MAX_AMMO
    /// was raised, after which it climbed straight past. Writes to those two
    /// stick, because they are the source rather than the mirror.
    ///
    /// So: lift the ceiling, keep the tank full, and restore the ceiling on
    /// release.</summary>
    private static float _savedMaxAmmo = -1f;

    /// <summary>Extra energy per second injected into the rift lab. Applied HERE
    /// rather than in DevCommands because the GEN readout is recomputed by the
    /// sim after the command tick - a display write made there is gone before the
    /// HUD draws, which is why the store climbed while GEN still read 1.</summary>
    public static float GenerationBonus;

    /// <summary>Adds the generation bonus to the rift lab, which is where CW4's
    /// energy actually lives (docs/research-findings.md, "Energy: the store is
    /// the rift lab's ammo").
    ///
    /// Genuinely per-frame: "energy per second" has to be delivered per tick.
    /// The GEN readout is deliberately left alone - it is driven by
    /// energyProduction, which the sim recomputes on its own cadence, so writing
    /// it compounds and faking it is what made the old cheat look like it worked
    /// while delivering nothing.</summary>
    private static void ApplyGenerationBonus(GameSpace gs)
    {
        if (GenerationBonus <= 0f) return;
        try
        {
            var cb = gs.commandBase;
            if (cb == null) return;
            cb.ammo = Mathf.Min(cb.ammo + GenerationBonus * Time.deltaTime, cb.MAX_AMMO);
        }
        catch { }
    }

    private static void TopUpEnergy(GameSpace gs)
    {
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null) return;

        try
        {
            if (_savedMaxAmmo < 0f) _savedMaxAmmo = cb.MAX_AMMO;
            if (cb.MAX_AMMO < EnergyFloor) cb.MAX_AMMO = EnergyFloor;
            if (cb.ammo < EnergyFloor) cb.ammo = EnergyFloor;

            // Keep the GEN readout consistent with the tank being permanently
            // full. Writing this display field used to BE the bug, because
            // nothing backed it; now the energy is genuinely there and a GEN of
            // 1 beside an unlimited store is the confusing part.
            // energyProduction is NOT written. It is a display figure the sim
            // owns, and inflating it is precisely the bug this cheat used to
            // have - millions of GEN and no energy. The tank above is real.
        }
        catch { }
    }

    /// <summary>Put the rift lab's energy ceiling back. The energy itself is a
    /// grant and is left alone - it drains on its own, and yanking a player's
    /// stored energy mid-mission would read as a bug rather than a revert.</summary>
    private static void ReleaseEnergy(GameSpace gs)
    {
        if (_savedMaxAmmo < 0f) return;
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb != null)
        {
            try
            {
                cb.MAX_AMMO = _savedMaxAmmo;
                if (cb.ammo > _savedMaxAmmo) cb.ammo = _savedMaxAmmo;
            }
            catch { }
        }
        _log.LogInfo($"DEVTOOLS: InfiniteResources off - rift lab energy ceiling restored to {_savedMaxAmmo:0}");
        _savedMaxAmmo = -1f;
    }

    private const float EnergyFloor = 100000f;

    /// <summary>Forces every building available while on, and RESTORES what the
    /// mission had when switched off. The snapshot is taken on the first force and
    /// dropped on mission change, so a restore never writes another mission's
    /// values. The build panes are rebuilt afterwards or the sidebar keeps showing
    /// the removed buildings.</summary>
    private static void ApplyAllBuildings(GameSpace gs)
    {
        BuildUnitManager? bum = null;
        try { bum = gs.buildUnitManager; } catch { }
        if (bum == null) return;

        bool want = DevConfig.AllBuildings.Value;

        if (want)
        {
            if (!_forcedAll)
            {
                var snap = new bool[Getters.Count];
                for (int i = 0; i < Getters.Count; i++)
                {
                    try { snap[i] = Getters[i](bum); } catch { }
                }
                _savedAvail = snap;
                _forcedAll = true;
                _log.LogInfo("DEVTOOLS: AllBuildings on - saved the mission's own availability");
            }
            // Re-applied every frame: the game rewrites these itself.
            foreach (var set in Setters)
            {
                try { set(bum, true); } catch { }
            }
            return;
        }

        if (!_forcedAll) return;

        var saved = _savedAvail;
        if (saved != null)
            for (int i = 0; i < Setters.Count && i < saved.Length; i++)
            {
                try { Setters[i](bum, saved[i]); } catch { }
            }
        _forcedAll = false;
        _savedAvail = null;
        RefreshPanes(gs);
        _log.LogInfo("DEVTOOLS: AllBuildings off - restored the mission's availability");
    }

    /// <summary>Rebuild the build strip so removed buildings actually disappear.</summary>
    private static void RefreshPanes(GameSpace gs)
    {
        try { gs.leftPane?.RefreshUnitBuildPanes(); }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: pane refresh failed: {e.Message}"); }
    }

    // --- hotkeys ----------------------------------------------------------

    private static int _savedSpeed = -1;

    /// <summary>The in-game buttons cap at 4x; GAME_SPEED itself does not.
    /// Snapshots the game's own speed on the first force and puts it back on
    /// release - see "Toggling a cheat off must undo it" at the top of the file.</summary>
    private static void ApplyGameSpeed(GameSpace gs)
    {
        int want = DevConfig.GameSpeed.Value;

        if (want <= 0)
        {
            if (_savedSpeed < 0) return;
            try { gs.GAME_SPEED = _savedSpeed; } catch { }
            _log.LogInfo($"DEVTOOLS: GameSpeed off - restored the game's speed ({_savedSpeed})");
            _savedSpeed = -1;
            return;
        }

        if (_savedSpeed < 0)
        {
            try { _savedSpeed = gs.GAME_SPEED; } catch { _savedSpeed = 1; }
        }
        try { if (gs.GAME_SPEED != want) gs.GAME_SPEED = want; } catch { }
    }

    // Only written on a transition: the multiplier is a sim parameter, and
    // hammering it every frame would fight the game's own bookkeeping.
    private static bool _frozen;

    // The game exposes SetCreeperTransferMultiplier but NO getter, so the
    // pre-freeze value cannot be read back. 1.0 is the documented default and
    // the only value available to restore; recorded here so the assumption is
    // visible rather than buried.
    private const float CreeperMultiplierDefault = 1f;

    /// <summary>Stops creeper and anti-creeper moving, so a map can be studied
    /// without losing it. Emitters keep producing - the fluid just does not
    /// flow, which is the point: the map stays as authored.</summary>
    private static void ApplyCreeperFreeze(GameSpace gs)
    {
        bool want = DevConfig.FreezeCreeper.Value;
        if (want == _frozen) return;
        var w = gs.world;
        if (w == null) return;
        try
        {
            w.SetCreeperTransferMultiplier(want ? 0f : CreeperMultiplierDefault);
            _frozen = want;
            _log.LogInfo($"DEVTOOLS: creeper flow {(want ? "FROZEN" : "restored")}");
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: freeze failed: {e.Message}"); }
    }

    /// <summary>One-shot: clears the fog layer so the whole map is visible.</summary>
    private static void RevealFog(GameSpace gs)
    {
        var w = gs.world;
        if (w == null) return;
        int fog = 0, done = 0;
        try
        {
            for (int x = 0; x < World.WORLD_CELL_WIDTH; x++)
                for (int y = 0; y < World.WORLD_CELL_HEIGHT; y++)
                {
                    // fogTerrain is the map's fog DEFINITION; isFogTerrain is the
                    // derived "currently dark" flag and goes false as soon as a
                    // cell is revealed, so keying off it would find nothing.
                    short f = w.GetFogTerrain(x, y);
                    if (f <= 0) continue;
                    fog++;
                    w.SetDeFogTerrain(x, y, f);
                    done++;
                }
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: reveal failed: {e.Message}"); return; }
        _log.LogWarning(fog == 0
            ? "DEVTOOLS: this mission has no fog"
            : $"DEVTOOLS: revealed {done}/{fog} fog cell(s)");
    }

    /// <summary>One-shot: marks every objective complete, to leave a mission
    /// early once you know what it needed.</summary>
    private static void CompleteObjectives(GameSpace gs)
    {
        var w = gs.world;
        var mo = w?.missionObjectives;
        if (w == null || mo == null) { _log.LogWarning("DEVTOOLS: no objectives"); return; }
        int n = 0;
        for (int i = 0; i < mo.Length; i++)
        {
            try { w.AcquireMissionObjective(i, false); n++; } catch { }
        }
        _log.LogWarning($"DEVTOOLS: marked {n} objective slot(s) complete");
    }

    private static void Hotkeys()
    {
        Toggle(DevConfig.KeyInstantBuild.Value, DevConfig.InstantBuild, "InstantBuild");
        Toggle(DevConfig.KeyAllBuildings.Value, DevConfig.AllBuildings, "AllBuildings");
        Toggle(DevConfig.KeyInfiniteResources.Value, DevConfig.InfiniteResources, "InfiniteResources");
        Toggle(DevConfig.KeyIndestructible.Value, DevConfig.Indestructible, "Indestructible");
        Toggle(DevConfig.KeyFreezeCreeper.Value, DevConfig.FreezeCreeper, "FreezeCreeper");

        if (Pressed(DevConfig.KeyGameSpeed.Value))
        {
            int[] cycle = { 0, 2, 4, 8, 16 };
            int i = Array.IndexOf(cycle, DevConfig.GameSpeed.Value);
            DevConfig.GameSpeed.Value = cycle[(i < 0 ? 0 : i + 1) % cycle.Length];
            _log.LogWarning($"DEVTOOLS: GameSpeed {(DevConfig.GameSpeed.Value == 0 ? "left to the game" : "x" + DevConfig.GameSpeed.Value)}");
        }

        // One-shot actions need a live mission.
        bool fog = Pressed(DevConfig.KeyRevealFog.Value);
        bool win = Pressed(DevConfig.KeyWinMission.Value);
        bool dump = Pressed(DevConfig.KeyDumpUnits.Value);
        if (!fog && !win && !dump) return;
        var gs = GameSpace.instance;
        if (gs == null || GameSpace.editMode) return;
        if (fog) RevealFog(gs);
        if (win) CompleteObjectives(gs);
        if (dump) DumpUnits(gs);
    }

    /// <summary>Why a cheat might skip a building: if a unit's DATA name is not
    /// in PlayerKeys, IsPlayerUnit rejects it and every cheat passes it over.
    /// The rift lab already proved the build-pane key and the data name can
    /// differ ("riftlab" vs "CommandBase"), so this prints both the game's own
    /// name registry and the live verdict per unit.</summary>
    private static void DumpUnits(GameSpace gs)
    {
        try
        {
            var ud = gs.unitData;
            var consts = ud?.unitConstants;
            if (consts != null)
            {
                // UnitConstants.ENEMY is the game's own per-TYPE flag - far better
                // than the per-instance UnitManager.enemy, which reports false for
                // hostile Pod/Ultrac/SuperTower.
                var friendly = new List<string>();
                var hostile = new List<string>();
                foreach (var kv in consts)
                {
                    bool e;
                    try { e = kv.Value.ENEMY; } catch { continue; }
                    (e ? hostile : friendly).Add(kv.Key);
                }
                friendly.Sort(); hostile.Sort();
                _log.LogWarning($"DEVTOOLS ENEMY=false ({friendly.Count}): {string.Join(",", friendly)}");
                _log.LogWarning($"DEVTOOLS ENEMY=true ({hostile.Count}): {string.Join(",", hostile)}");
            }
            else _log.LogWarning("DEVTOOLS: unitConstants unavailable");
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: name dump failed: {e.Message}"); }

        // CMOD registry: GUID -> menu names. playerMenuUnitName being non-empty is
        // what marks a custom unit as the PLAYER's (airship/bertha/sweeper), which
        // is how IsPlayerUnit classifies them - they have no usable data name.
        try
        {
            var cmods = gs.cmods;
            if (cmods != null)
            {
                var mine = new List<string>();
                var others = 0;
                foreach (var kv in cmods)
                {
                    string pm = "", em = "";
                    try { pm = kv.Value?.playerMenuUnitName ?? ""; em = kv.Value?.editMenuUnitName ?? ""; }
                    catch { }
                    if (!string.IsNullOrEmpty(pm)) mine.Add($"{pm}[{kv.Key.Substring(0, 8)}]");
                    else others++;
                }
                mine.Sort();
                _log.LogWarning($"DEVTOOLS cmods: {mine.Count} player-buildable: {string.Join(" ", mine)}");
                _log.LogWarning($"DEVTOOLS cmods: {others} with no player menu name (map/editor only)");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: cmod dump failed: {e.Message}"); }

        // The decisive mapping: every build button owns a UnitBuildGhost, and the
        // ghost owns the PREFAB it places. Reading the prefab's data name gives
        // build-pane key -> real unit name with no guessing. unitConstants alone
        // cannot do this: it lists 88 names but none of them are "pylon",
        // "miner" or "porter", so those build keys are not data names at all.
        try
        {
            var pairs = new List<string>();
            foreach (var g in Resources.FindObjectsOfTypeAll<UnitBuildGhost>())
            {
                if (g == null) continue;
                string ghost = "?", data = "?";
                try { ghost = g.gameObject.name; } catch { }
                try
                {
                    var pf = g.prefab;
                    var um = pf?.GetComponent<UnitManager>();
                    data = um == null ? "(no UnitManager)" : (um.GetDataName() ?? "(null)");
                }
                catch (Exception e) { data = $"(err {e.Message})"; }
                pairs.Add($"{ghost}->{data}");
            }
            pairs.Sort();
            _log.LogWarning($"DEVTOOLS build ghosts ({pairs.Count}): {string.Join("  ", pairs)}");
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: ghost dump failed: {e.Message}"); }

        try
        {
            var seen = new Dictionary<string, int>();
            foreach (var u in gs.units)
            {
                if (u == null) continue;
                string line;
                try
                {
                    line = $"{u.GetIl2CppType().Name}/{u.GetDataName()}" +
                           $"{(IsPlayerUnit(u) ? "=MINE" : "=other")}" +
                           $"{(u.isBuilding ? ",building" : "")}" +
                           $"{(u.impervious ? ",impervious" : "")}";
                }
                catch { continue; }
                seen[line] = seen.TryGetValue(line, out var c) ? c + 1 : 1;
            }
            var parts = new List<string>();
            foreach (var kv in seen) parts.Add($"{kv.Key}x{kv.Value}");
            _log.LogWarning($"DEVTOOLS units on map: {string.Join(" ", parts)}");

            // Machine-readable summary for the regression test: every cheat's
            // observable effect in one line.
            int mine = 0, building = 0, withAmmo = 0, withWares = 0, fullHealth = 0;
            // Reported separately from fullHealth because they are different
            // protections: the clamp only covers damage, impervious covers the
            // destroy paths that never touch health. A platform can sit at
            // impervious=false with fullHealth counted and still be destroyed.
            int impervious = 0, uneven = 0;
            float ammoTotal = 0f, wareTotal = 0f;
            foreach (var u in AllUnits(gs))
            {
                if (u == null || !IsPlayerUnit(u)) continue;
                mine++;
                try { if (u.isBuilding) building++; } catch { }
                try { if (u.ammo > 0f) { withAmmo++; ammoTotal += u.ammo; } } catch { }
                try { if (u.MAX_HEALTH > 0f && u.health >= u.MAX_HEALTH) fullHealth++; } catch { }
                try { if (u.impervious) impervious++; } catch { }
                try { if (u.DESTROY_ON_UNEVEN_TERRAIN) uneven++; } catch { }
                for (int w = 0; w < WareSlots; w++)
                {
                    try { var h = u.GetWareHeld(w); if (h > 0f) { withWares++; wareTotal += h; } }
                    catch { }
                }
            }
            _log.LogWarning(
                $"DEVSTATE mine={mine} building={building} withAmmo={withAmmo} ammoTotal={ammoTotal:0} " +
                $"withWares={withWares} wareTotal={wareTotal:0} fullHealth={fullHealth} " +
                $"impervious={impervious} uneven={uneven} " +
                $"energyStore={gs.energyStore:0} energyProduction={gs.energyProduction:0} " +
                $"cmodsPlayer={CountPlayerCmods(gs)}");
        }
        catch (Exception e) { _log.LogWarning($"DEVTOOLS: unit dump failed: {e.Message}"); }
    }

    /// <summary>A dev hotkey fires only while the modifier is held. F5-F12 are
    /// Creeper World hotkeys too, so bare keys made every toggle also trigger a
    /// game action.</summary>
    private static bool Pressed(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        try
        {
            var mod = DevConfig.HotkeyModifier.Value;
            if (mod != KeyCode.None && !Input.GetKey(mod)) return false;
            return Input.GetKeyDown(key);
        }
        catch { return false; }
    }

    private static void Toggle(KeyCode key, BepInEx.Configuration.ConfigEntry<bool> entry, string name)
    {
        if (!Pressed(key)) return;
        entry.Value = !entry.Value;
        _log.LogWarning($"DEVTOOLS: {name} {(entry.Value ? "ON" : "OFF")}");
    }
}
