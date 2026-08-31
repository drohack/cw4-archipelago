using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CW4DevTools;

/// <summary>
/// File-command channel so the dev tools can be driven without a keyboard:
/// write a line to &lt;game&gt;/BepInEx/cw4dev-commands.txt and it runs on the
/// next tick.
///
/// This exists because the survey work previously borrowed the RANDOMIZER's
/// debug channel to boot missions and take screenshots - which meant enabling
/// the whole Archipelago layer just to test a cheat, and that layer then stayed
/// enabled by accident. Anything needed to drive or diagnose the dev tools
/// belongs here, in the plugin it belongs to.
///
/// Deliberately no Archipelago commands: no connect, no items, no checks. If a
/// test needs those, it is a randomizer test and belongs in its batteries.
/// </summary>
public sealed class DevCommands
{
    private static string FilePath => System.IO.Path.Combine(Paths.GameRootPath, "BepInEx", "cw4dev-commands.txt");

    private readonly ManualLogSource _log;
    private DateTime _lastWrite = DateTime.MinValue;
    private int _pollCountdown;

    public DevCommands(ManualLogSource log) => _log = log;

    public void Tick()
    {
        CapacityProbeTick();
        NullifyWatchTick();
        WatchTick();   // every frame: a readback must not wait on the poll

        if (--_pollCountdown > 0) return;
        _pollCountdown = 30;   // ~twice a second; this is a test hook, not a hot path

        var path = FilePath;
        if (!File.Exists(path)) return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return; }
        if (stamp == _lastWrite) return;
        _lastWrite = stamp;

        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try { Handle(line); }
            catch (Exception e) { _log.LogWarning($"DEVCMD '{line}' failed: {e.Message}"); }
        }
    }

    private void Handle(string line)
    {
        var lower = line.ToLowerInvariant();

        if (lower.StartsWith("boot:")) { Boot(line.Substring(5).Trim()); return; }
        if (lower == "ada:close") { CloseAda(); return; }
        if (lower.StartsWith("sim:")) { Sim(line.Substring(4).Trim()); return; }
        if (lower.StartsWith("spawn:")) { Spawn(line.Substring(6).Trim()); return; }
        if (lower.StartsWith("shot:")) { Shot(line.Substring(5).Trim()); return; }
        if (lower == "dump") { DevTools.DumpUnitsNow(); return; }
        if (lower == "story:open") { StoryOpen(); return; }
        if (lower == "planets:dump") { PlanetsDump(); return; }
        if (lower == "obj:dump") { ObjectiveDump(); return; }
        if (lower.StartsWith("null:")) { Nullify(line.Substring(5).Trim()); return; }
        if (lower.StartsWith("energy:")) { Energy(line.Substring(7).Trim()); return; }
        if (lower.StartsWith("span:goto")) { SpanGoto(line.Substring(9).Trim()); return; }
        if (lower.StartsWith("set:")) { Set(line.Substring(4).Trim()); return; }
        if (lower == "overlay:dump") { OverlayDump(); return; }

        _log.LogWarning($"DEVCMD unknown: {line} " +
                        "(boot:storyN | ada:close | sim:run [speed] | sim:pause | " +
                        "spawn:<UnitName> [n] | shot:<path> | dump | story:open | " +
                        "planets:dump | obj:dump | overlay:dump | null:* | energy:* | " +
                        "span:goto | set:<cheat>=on|off)");
    }

    /// <summary>Loads a mission directly. Unlike the randomizer's boot, there is
    /// no gate to satisfy - the dev tools never lock anything.</summary>
    private void Boot(string specifier)
    {
        GameSpace.specifierToApply = specifier;
        GameSpace.titleToApply = specifier;
        GameSpace.guidToApply = "";
        LoadingScreen.LoadGame(specifier, true, false, GameSpace.CATEGORY.FARSITE, -1);
        _log.LogInfo($"DEVCMD boot: {specifier}");
    }

    private void CloseAda()
    {
        int closed = 0;
        try
        {
            // FindObjectsOfTypeAll, not FindObjectsOfType: the log can be
            // inactive and still needs closing.
            var logs = Resources.FindObjectsOfTypeAll<ADAMessageLog>();
            if (logs != null)
                foreach (var lg in logs)
                {
                    if (lg == null) continue;
                    try { lg.Close(); closed++; } catch { }
                }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD ada:close: {e.Message}"); }
        _log.LogInfo($"DEVCMD ada:close: closed {closed} message log(s)");
    }

    /// <summary>Unpause by clearing every pause owner - a mission that is still
    /// showing its intro holds several, so setting one flag is not enough.</summary>
    private void Sim(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD sim: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "run";

        if (what == "pause") { gs.Pause("cw4dev", true); _log.LogInfo("DEVCMD sim: paused"); return; }

        var owners = new System.Collections.Generic.List<string>();
        try { foreach (var o in gs.pauseOwner) owners.Add(o); } catch { }
        foreach (var o in owners)
        {
            try { gs.Pause(o, false); } catch { }
        }
        if (tok.Length > 1 && int.TryParse(tok[1], out var sp)) gs.GAME_SPEED = sp;
        _log.LogInfo($"DEVCMD sim: cleared [{string.Join(",", owners)}] paused={gs.paused} speed={gs.GAME_SPEED}");
    }

    /// <summary>Places units for testing. Takes the game's REAL unit name, not a
    /// build-pane key: "TowerBridge" not "pylon", "Collector" not "miner",
    /// "CommandBase" not "riftlab". See docs/research-findings.md, "Unit
    /// naming" - passing a build-pane key returns null and places nothing.</summary>
    private void Spawn(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0) { _log.LogWarning("DEVCMD spawn: need a unit name"); return; }
        var name = tok[0];
        int count = tok.Length > 1 && int.TryParse(tok[1], out var c) ? c : 1;

        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD spawn: no GameSpace"); return; }

        Vector3 anchor;
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb != null) anchor = cb.transform.position;
        else
        {
            // World coordinates are 1:1 with cells; only the height needs a
            // lookup. Do NOT use World.GetCreeperVertex - it is mesh-local and
            // returns positions that land off the map.
            int cx = World.WORLD_CELL_WIDTH / 2, cy = World.WORLD_CELL_HEIGHT / 2;
            float y = 0f;
            try { y = UnitManager.GetMinHeight(new Vector3(cx, 0f, cy), 0f, 0, false, false, false); }
            catch { }
            anchor = new Vector3(cx, y, cy);
        }

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var pos = anchor + new Vector3(-8f - 4f * i, 0f, -6f);
            try { if (UnitManager.CreateUnitAtPosition(name, pos) != null) made++; }
            catch (Exception e) { _log.LogWarning($"DEVCMD spawn '{name}': {e.Message}"); break; }
        }
        _log.LogInfo(made == 0
            ? $"DEVCMD spawn {name}: 0/{count} - is that the REAL unit name? build-pane keys do not work"
            : $"DEVCMD spawn {name}: {made}/{count} placed");
    }

    /// <summary>Open the Farsite Expedition level select from the main menu.
    ///
    /// Needed because synthetic mouse input does not reach CW4's UI: SetCursorPos
    /// plus mouse_event moves the OS cursor and the game ignores the click, so
    /// the menu cannot be driven from outside. Invoking the button's own onClick
    /// is what actually works. (The randomizer has the same command; this is a
    /// deliberate copy, since the dev tools must not depend on it.)</summary>
    /// <summary>What the cheat strip currently says, and how many times it has
    /// been redrawn. Colour tags are left in: green means on, and asserting on
    /// the tag is how a test reads the strip without a screenshot.</summary>
    private void OverlayDump()
    {
        _log.LogInfo($"DEVCMD overlay: redraws={DevOverlay.Redraws} text='{DevOverlay.LastText}'");
    }

    private void StoryOpen()
    {
        try
        {
            var gg = GameGalaxy.instance;
            var btn = gg?.farsiteButton?.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) { _log.LogWarning("DEVCMD story:open: no farsite button (are you on the main menu?)"); return; }
            btn.onClick.Invoke();
            _log.LogInfo("DEVCMD story:open");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD story:open: {e.Message}"); }
    }

    /// <summary>Nullify experiments, for the in-game finale gate.
    ///
    /// The question: can a specific structure be made UN-nullifiable, so the
    /// mission cannot be completed until the Archipelago gate opens? Two
    /// candidate levers, both per-instance on UnitManager:
    ///   CAN_NULLIFY   whether a nullifier may target it at all
    ///   impervious    whether it can be harmed
    ///
    ///   null:list              every nullifiable unit, with an index
    ///   null:protect &lt;i|all&gt;   clear CAN_NULLIFY (and set impervious)
    ///   null:allow &lt;i|all&gt;     put both back
    ///   null:kill &lt;i&gt;          spawn a fed nullifier beside unit i
    /// </summary>
    private void Nullify(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD null: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "list";
        string target = tok.Length > 1 ? tok[1].ToLowerInvariant() : "";

        var units = new System.Collections.Generic.List<UnitManager>();
        try { foreach (var u in gs.nullifiableUnits) if (u != null) units.Add(u); } catch { }
        // Sorted by data name then position so the listing is stable to read.
        // NEVER address these by index across commands: the source is a HashSet
        // and its order changes between calls, which silently pointed an earlier
        // version of this probe at the wrong unit.
        units.Sort((a, b) =>
        {
            string na = "", nb = "";
            try { na = a.GetDataName() ?? ""; } catch { }
            try { nb = b.GetDataName() ?? ""; } catch { }
            int c = string.CompareOrdinal(na, nb);
            if (c != 0) return c;
            try { return a.transform.position.x.CompareTo(b.transform.position.x); } catch { return 0; }
        });

        // Match by data-name prefix - CMOD units report a GUID, so "abe9d7ea" is
        // enough to name the neutron reactor.
        System.Collections.Generic.List<UnitManager> Match(string prefix)
        {
            var hits = new System.Collections.Generic.List<UnitManager>();
            foreach (var u in units)
            {
                string nm = "";
                try { nm = (u.GetDataName() ?? "").ToLowerInvariant(); } catch { }
                if (prefix.Length > 0 && (prefix == "all" || nm.StartsWith(prefix)))
                    hits.Add(u);
            }
            return hits;
        }

        if (what == "list")
        {
            _log.LogWarning($"DEVNULL count={units.Count}");
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                string name = "?"; var pos = Vector3.zero;
                bool can = false, imp = false;
                try { name = u.GetDataName() ?? u.GetIl2CppType().Name; } catch { }
                try { pos = u.transform.position; } catch { }
                try { can = u.CAN_NULLIFY; } catch { }
                try { imp = u.impervious; } catch { }
                _log.LogWarning($"DEVNULL [{i}] {name} CAN_NULLIFY={can} impervious={imp} " +
                                $"pos=({pos.x:0},{pos.z:0})");
            }
            return;
        }

        if (what == "protect" || what == "allow")
        {
            bool allow = what == "allow";
            var hits = Match(target);
            foreach (var u in hits)
            {
                // A one-shot write; the sim restores it within a tick. Kept only
                // to demonstrate that, which is why the real lock in the
                // randomizer filters the targeting call instead.
                // Only CAN_NULLIFY is touched. impervious is left alone: these
                // structures are ALREADY impervious in vanilla and still
                // nullifiable, which is how we know nullifying is not damage.
                try { u.CAN_NULLIFY = allow; } catch { }
            }
            _log.LogWarning($"DEVNULL {what} '{target}': {hits.Count} unit(s) -> CAN_NULLIFY={allow}");
            return;
        }

        if (what == "targets")
        {
            // Ask the GAME what a nullifier would consider a valid target at
            // this spot. Better than trying to make a spawned nullifier fire -
            // those never join the network, so they never arm.
            var picked = Match(target);
            if (picked.Count == 0)
            {
                _log.LogWarning($"DEVCMD null:targets: nothing matches '{target}'");
                return;
            }
            try
            {
                var pos = picked[0].transform.position;
                int cx = UnitManager.GetCellX(pos.x), cy = UnitManager.GetCellY(pos.z);
                var found = Nullifier.GetNullifierTargets(cx, cy, 8);
                int n = found == null ? 0 : found.Count;
                _log.LogWarning($"DEVNULL targets near '{target}' at cell ({cx},{cy}): {n}");
                if (found != null)
                    foreach (var f in found)
                    {
                        if (f == null) continue;
                        string nm = "?"; bool can = false;
                        try { nm = f.GetDataName() ?? f.GetIl2CppType().Name; } catch { }
                        try { can = f.CAN_NULLIFY; } catch { }
                        _log.LogWarning($"DEVNULL   target {nm} CAN_NULLIFY={can}");
                    }
            }
            catch (Exception e) { _log.LogWarning($"DEVNULL targets: {e.Message}"); }
            return;
        }

        if (what == "kill")
        {
            if (!int.TryParse(target, out var idx) || idx < 0 || idx >= units.Count)
            {
                _log.LogWarning($"DEVCMD null:kill: index 0..{units.Count - 1}");
                return;
            }
            var u = units[idx];
            Vector3 pos;
            try { pos = u.transform.position; } catch { return; }

            // A nullifier has to sit within range. Place it right beside the
            // target, finish it, feed it, and make it indestructible so the
            // creeper cannot eat it before it fires.
            UnitManager? nul = null;
            try { nul = UnitManager.CreateUnitAtPosition("Nullifier", pos + new Vector3(3f, 0f, 0f)); }
            catch (Exception e) { _log.LogWarning($"DEVNULL kill: {e.Message}"); return; }
            if (nul == null) { _log.LogWarning("DEVNULL kill: could not place a Nullifier"); return; }
            try { nul.CompleteTheBuild(true); } catch { }
            try { nul.ammo = nul.MAX_AMMO; } catch { }
            try { nul.impervious = true; } catch { }
            try { gs.RefreshCollectors(); } catch { }

            string name = "?"; try { name = u.GetDataName() ?? ""; } catch { }
            bool can = false; try { can = u.CAN_NULLIFY; } catch { }
            _log.LogWarning($"DEVNULL kill: nullifier placed beside [{idx}] {name} " +
                            $"(CAN_NULLIFY={can}); watch the count");
            _watchNullCountdown = 900;
            return;
        }

        _log.LogWarning("DEVCMD null: expected list | protect <name> | allow <name> | targets <name> | kill <i>");
    }

    private int _watchNullCountdown;

    private void NullifyWatchTick()
    {
        if (_watchNullCountdown <= 0) return;
        _watchNullCountdown--;
        if (_watchNullCountdown % 120 != 0) return;
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            int n = 0;
            foreach (var u in gs.nullifiableUnits) if (u != null) n++;
            _log.LogWarning($"DEVNULL watch: nullifiable remaining = {n}");
        }
        catch { }
    }

    /// <summary>Objective slots and the live counters behind them.
    ///
    /// Answers what per-instance location checks need to know: whether
    /// MissionObjectiveData.count really is live progress, and what the target
    /// totals are. The totals come from the game's own sets - gs.totems,
    /// gs.nullifiableUnits, gs.mustCollect/maxMustCollect - not from a table we
    /// maintain.</summary>
    private void ObjectiveDump()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD obj:dump: no GameSpace"); return; }
        var world = gs.world;
        if (world == null) { _log.LogWarning("DEVCMD obj:dump: no World"); return; }

        string spec = ""; try { spec = gs.specifier ?? ""; } catch { }

        int totems = 0, totemsOn = 0;
        try
        {
            foreach (var t in gs.totems)
            {
                if (t == null) continue;
                totems++;
                try { if (t.totemComplete) totemsOn++; } catch { }
            }
        }
        catch { }

        int nullifiable = 0;
        try { foreach (var u in gs.nullifiableUnits) { if (u != null) nullifiable++; } } catch { }
        int mustCollect = 0;
        try { foreach (var u in gs.mustCollect) { if (u != null) mustCollect++; } } catch { }
        int caches = 0;
        try { foreach (var c in gs.infocaches) { if (c != null) caches++; } } catch { }
        int maxCollect = 0; try { maxCollect = gs.maxMustCollect; } catch { }

        _log.LogWarning($"DEVOBJ mission={spec} totems={totemsOn}/{totems} nullifiable={nullifiable} " +
                        $"mustCollect={mustCollect} maxMustCollect={maxCollect} infocaches={caches}");

        try
        {
            var objs = world.missionObjectives;
            if (objs == null) { _log.LogWarning("DEVOBJ no missionObjectives"); return; }
            for (int i = 0; i < objs.Length; i++)
            {
                var o = objs[i];
                if (o == null) { _log.LogWarning($"DEVOBJSLOT {i} null"); continue; }
                int count = -1, extra = -1; bool req = false, en = false, done = false; string custom = "";
                try { count = o.count; } catch { }
                try { extra = o.extra; } catch { }
                try { req = o.required; } catch { }
                try { en = o.enabled; } catch { }
                try { custom = o.customName ?? ""; } catch { }
                try { done = world.IsMissionObjectiveComplete(i); } catch { }
                _log.LogWarning($"DEVOBJSLOT {i} enabled={en} required={req} count={count} extra={extra} " +
                                $"complete={done} custom='{custom}'");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD obj:dump: {e.Message}"); }
    }

    // Energy probe state: capacity is measured by over-setting the store and
    // reading what the sim clamps it back to, which takes a tick.
    private int _capCountdown;
    private float _capSaved;

    /// <summary>Probes for the energy levers an AP "storage"/"generation" item
    /// would need. Nothing here ships - it exists to find out whether those
    /// items can do anything before they are added to the pool.
    ///
    ///   energy:cap            measure current storage capacity
    ///   energy:supply &lt;n&gt;     add n to SUPPLY on the player's units, then measure
    ///   energy:const &lt;n&gt;      add n to the Tower/CommandBase SUPPLY constant, then measure
    ///   energy:eff &lt;mult&gt;     multiply Tower.efficiency, then report production
    /// </summary>
    private void Energy(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD energy: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "cap";
        float amt = tok.Length > 1 && float.TryParse(tok[1], out var a) ? a : 0f;

        if (what == "cap") { BeginCapacityProbe(gs); return; }

        if (what == "supply")
        {
            int touched = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { u.SUPPLY = u.SUPPLY + (int)amt; touched++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY supply +{(int)amt} on {touched} unit(s)");
            BeginCapacityProbe(gs);
            return;
        }

        if (what == "const")
        {
            // Per-INSTANCE SUPPLY writes did nothing to capacity (measured), so
            // try the per-TYPE constant the instances are built from.
            var ud = gs.unitData;   // UnitData is per-GameSpace, not static
            if (ud == null) { _log.LogWarning("DEVENERGY const: no UnitData"); return; }
            string[] names = { "Tower", "CommandBase" };
            foreach (var n in names)
            {
                try
                {
                    var c = ud.GetUnitContants(n);
                    if (c == null) { _log.LogWarning($"DEVENERGY const: no constants for '{n}'"); continue; }
                    int before = c.SUPPLY;
                    c.SUPPLY = before + (int)amt;
                    ud.SetUnitConstants(n, c);
                    _log.LogWarning($"DEVENERGY const {n}.SUPPLY {before} -> {c.SUPPLY}");
                }
                catch (Exception e) { _log.LogWarning($"DEVENERGY const {n}: {e.Message}"); }
            }
            // Capacity looks cached rather than recomputed every tick - it moved
            // when units were added and then stopped responding. Poke the
            // refresh flag so the change has a chance to be picked up.
            try { gs.shouldRefreshCollectors = true; _log.LogWarning("DEVENERGY const: requested collector refresh"); }
            catch (Exception e) { _log.LogWarning($"DEVENERGY const refresh: {e.Message}"); }
            BeginCapacityProbe(gs);
            return;
        }

        if (what == "eff")
        {
            int towers = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    var t = u.TryCast<Tower>();
                    if (t == null) continue;
                    try { t.efficiency = t.efficiency * (amt <= 0f ? 1f : amt); towers++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY efficiency x{amt} on {towers} tower(s); " +
                            $"production now {gs.energyProduction:0.00} store {gs.energyStore:0.0}");
            return;
        }

        if (what == "scan")
        {
            // Log every numeric energy-ish field on GameSpace and World at once.
            // Run it before and after adding a tower: whichever value moves by
            // the per-tower storage amount IS the capacity. Searching by name
            // has failed twice, so search by behaviour instead.
            var w = gs.world;
            void F(string name, float v) => _log.LogWarning($"DEVSCAN {name}={v:0.###}");
            try { F("gs.energyStore", gs.energyStore); } catch { }
            try { F("gs.energyProduction", gs.energyProduction); } catch { }
            try { F("gs.energyProductionUnClamped", gs.energyProductionUnClamped); } catch { }
            try { F("gs.energyUse", gs.energyUse); } catch { }
            try { F("gs.energyDeficit", gs.energyDeficit); } catch { }
            try { F("gs.avg_energyUse", gs.avg_energyUse); } catch { }
            try { F("gs.avg_energyDeficit", gs.avg_energyDeficit); } catch { }
            try { F("gs.lifticStore", gs.lifticStore); } catch { }
            try { F("gs.argStore", gs.argStore); } catch { }
            try { F("gs.anticreeperStore", gs.anticreeperStore); } catch { }
            try { F("gs.ultracStore", gs.ultracStore); } catch { }
            try { F("gs.treeProduction", gs.treeProduction); } catch { }
            if (w != null)
            {
                try { F("world.statEnergyStore", w.statEnergyStore); } catch { }
                try { F("world.statEnergyGeneration", w.statEnergyGeneration); } catch { }
                try { F("world.statEnergyUse", w.statEnergyUse); } catch { }
                try { F("world.statEnergyBonus", w.statEnergyBonus); } catch { }
                try { F("world.statEnergyEco", w.statEnergyEco); } catch { }
            }
            // The real economy, found after energyStore turned out to be a
            // PERCENTAGE: supplyUsed/supplyMax are the absolute energy and its
            // capacity, and MAX_GEN_RATE caps production. The per-unit SUPPLY
            // constant feeds supplyMax, which is why it is named that.
            try { _log.LogWarning($"DEVSCAN gs.supplyUsed={gs.supplyUsed} gs.supplyMax={gs.supplyMax} gs.MAX_GEN_RATE={gs.MAX_GEN_RATE:0.###}"); }
            catch (Exception e) { _log.LogWarning($"DEVSCAN supply: {e.Message}"); }

            // Energy is held ON the units - the HUD STORE is the network total.
            // So the capacity lever should be per-unit ammo, not a global field.
            try
            {
                float ammo = 0f, maxAmmo = 0f;
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { ammo += u.ammo; maxAmmo += u.MAX_AMMO; } catch { }
                }
                _log.LogWarning($"DEVSCAN sum(unit.ammo)={ammo:0.###} sum(unit.MAX_AMMO)={maxAmmo:0.###}");
                var cb = gs.commandBase;
                if (cb != null)
                    _log.LogWarning($"DEVSCAN riftlab ammo={cb.ammo:0.###} MAX_AMMO={cb.MAX_AMMO:0.###} SUPPLY={cb.SUPPLY}");
            }
            catch (Exception e) { _log.LogWarning($"DEVSCAN ammo: {e.Message}"); }

            int units = 0, towers = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    units++;
                    if (u.TryCast<Tower>() != null) towers++;
                }
            }
            catch { }
            _log.LogWarning($"DEVSCAN units={units} towers={towers}");
            return;
        }

        if (what == "gen")
        {
            // Sustained generation: add energy to the rift lab every frame. The
            // base has no production-rate field, so this IS the lever - and it
            // is indistinguishable from the rift lab producing more, because the
            // store is its ammo.
            DevTools.GenerationBonus = amt;
            _log.LogWarning($"DEVENERGY generation bonus set to {amt:0.###}/sec");
            _watchCountdown = 900;
            return;
        }

        if (what == "build")
        {
            // Place a unit close to the rift lab and leave it UNFINISHED, so it
            // has to draw energy to build. This is how "does the store actually
            // get spent" gets answered - a bigger number proves nothing on its
            // own, as the generation display already taught us.
            string unit = tok.Length > 1 ? tok[1] : "Cannon";
            var cb = gs.commandBase;
            if (cb == null) { _log.LogWarning("DEVENERGY build: no command base"); return; }
            _addIndex++;
            float ang = _addIndex * 1.05f;
            var pos = cb.transform.position + new Vector3(Mathf.Cos(ang) * 5f, 0f, Mathf.Sin(ang) * 5f);
            UnitManager? made = null;
            try { made = UnitManager.CreateUnitAtPosition(unit, pos); } catch { }
            try { gs.RefreshCollectors(); } catch { }
            bool building = false;
            try { building = made != null && made.isBuilding; } catch { }
            _log.LogWarning($"DEVENERGY build {unit}: placed={made != null} isBuilding={building} " +
                            $"riftlab ammo={cb.ammo:0.#}/{cb.MAX_AMMO:0.#}");
            _watchCountdown = 900;
            return;
        }

        if (what == "give")
        {
            // The store IS the rift lab's ammo, so adding to that ammo is real
            // energy - unlike writing gs.energyStore, which is only a mirror the
            // sim recomputes.
            try
            {
                var cb = gs.commandBase;
                if (cb == null) { _log.LogWarning("DEVENERGY give: no command base"); return; }
                float was = cb.ammo;
                cb.ammo = Mathf.Min(cb.ammo + amt, cb.MAX_AMMO);
                _log.LogWarning($"DEVENERGY give: riftlab ammo {was:0.###} -> {cb.ammo:0.###} (max {cb.MAX_AMMO:0.###})");
                _watchCountdown = 600;
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY give: {e.Message}"); }
            return;
        }

        if (what == "ammo")
        {
            // Raise the energy capacity held on the player's units.
            int n = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { u.MAX_AMMO = u.MAX_AMMO + amt; n++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY MAX_AMMO +{amt:0.###} on {n} unit(s)");
            return;
        }

        if (what == "refresh")
        {
            // Units from CreateUnitAtPosition never join the packet network, so
            // every unit-based energy test has measured nothing. RefreshCollectors
            // is the game's own rebuild - if it adopts them, those tests become
            // possible.
            try { gs.RefreshCollectors(); _log.LogWarning("DEVENERGY RefreshCollectors() called"); }
            catch (Exception e) { _log.LogWarning($"DEVENERGY refresh: {e.Message}"); }
            return;
        }

        if (what == "max")
        {
            // Write the real capacity rather than the percentage mirror.
            try
            {
                int was = gs.supplyMax;
                gs.supplyMax = was + (int)amt;
                _log.LogWarning($"DEVENERGY supplyMax {was} -> {gs.supplyMax}");
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY max: {e.Message}"); }
            return;
        }

        if (what == "rate")
        {
            try
            {
                float was = gs.MAX_GEN_RATE;
                gs.MAX_GEN_RATE = was + amt;
                _log.LogWarning($"DEVENERGY MAX_GEN_RATE {was:0.###} -> {gs.MAX_GEN_RATE:0.###}");
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY rate: {e.Message}"); }
            return;
        }

        if (what == "set")
        {
            // Decisive test: is a one-shot write to energyStore kept, clamped to
            // a capacity, or discarded outright? Watch the value over several
            // seconds rather than reading it once.
            try
            {
                float was = gs.energyStore;
                gs.energyStore = amt;
                _log.LogWarning($"DEVENERGY set store {was:0.0} -> {amt:0.0}; watching");
                _watchCountdown = 600;   // ~10s at 60fps
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY set: {e.Message}"); }
            return;
        }

        if (what == "add")
        {
            // Spawn one unit of a named type and report the capacity delta, so
            // the per-unit storage contribution is measured directly.
            string unit = tok.Length > 1 ? tok[1] : "Tower";
            float before = MeasureNow(gs);
            var cb = gs.commandBase;
            // Spread them out. Towers collect from the land they claim, so
            // stacking them on one spot adds no generation at all - which is
            // exactly what made an earlier run read GEN 1 with three towers.
            // Close to the rift lab, spread by ANGLE only. A tower has to be
            // within the base's connection range to join the network, and it
            // collects from the land it claims - so an earlier version that grew
            // the radius each time put them out of range, and one before that
            // stacked them on a single spot. Both produced exactly nothing.
            _addIndex++;
            float ang = _addIndex * 1.05f;
            const float rad = 5f;
            var pos = cb != null
                ? cb.transform.position + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad)
                : Vector3.zero;
            UnitManager? created = null;
            try { created = UnitManager.CreateUnitAtPosition(unit, pos); } catch { }
            string kind = "null";
            bool building = false;
            if (created != null)
            {
                try { kind = created.GetIl2CppType().Name; } catch { }
                try { building = created.isBuilding; } catch { }
                // Finish it and rebuild the network, or it produces nothing.
                try { created.CompleteTheBuild(true); } catch { }
            }
            try { gs.RefreshCollectors(); } catch { }
            _log.LogWarning($"DEVENERGY add {unit}: type={kind} wasBuilding={building} store={before:0.0}");
            BeginCapacityProbe(gs);
            return;
        }

        _log.LogWarning("DEVCMD energy: expected cap | supply <n> | const <n> | eff <mult> | add <unit>");
    }

    /// <summary>Capacity right now, by over-setting the store and letting the
    /// sim clamp it back within the same call is not possible - this returns the
    /// last known value instead, for logging a before/after pair.</summary>
    private static float MeasureNow(GameSpace gs)
    {
        try { return gs.energyStore; } catch { return -1f; }
    }

    private void BeginCapacityProbe(GameSpace gs)
    {
        try
        {
            _capSaved = gs.energyStore;
            gs.energyStore = 1_000_000f;   // the sim clamps this to capacity
            _capCountdown = 15;            // ~a quarter second of ticks
            _log.LogWarning($"DEVENERGY capacity probe: store was {_capSaved:0.0}, over-set; reading back shortly");
        }
        catch (Exception e) { _log.LogWarning($"DEVENERGY capacity probe: {e.Message}"); }
    }

    private int _addIndex;
    private int _watchCountdown;

    private void WatchTick()
    {
        if (_watchCountdown <= 0) return;
        _watchCountdown--;
        if (_watchCountdown % 120 != 0) return;   // log about every 2 seconds
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            var w = gs.world;
            float hud = -1f; try { if (w != null) hud = w.statEnergyStore; } catch { }
            _log.LogWarning($"DEVENERGY watch t-{_watchCountdown / 60}s store={gs.energyStore:0.0} HUDstore={hud:0.0}");
        }
        catch { }
    }

    private void CapacityProbeTick()
    {
        if (_capCountdown <= 0) return;
        if (--_capCountdown > 0) return;
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            // Report the HUD's numbers alongside the raw ones. The GEN/USE/STORE
            // readout does not necessarily show absolute energy - if STORE reads
            // 100 when full it is a percentage, and the absolute cap is what
            // energyStore clamps to.
            float sStore = -1f, sGen = -1f, sUse = -1f, sBonus = -1f, sEco = -1f;
            var w = gs.world;
            if (w != null)
            {
                try { sStore = w.statEnergyStore; } catch { }
                try { sGen = w.statEnergyGeneration; } catch { }
                try { sUse = w.statEnergyUse; } catch { }
                try { sBonus = w.statEnergyBonus; } catch { }
                try { sEco = w.statEnergyEco; } catch { }
            }
            _log.LogWarning($"DEVENERGY measured cap={gs.energyStore:0.0} " +
                            $"prod={gs.energyProduction:0.00} use={gs.energyUse:0.00} | " +
                            $"HUD store={sStore:0.0} gen={sGen:0.00} use={sUse:0.00} " +
                            $"bonus={sBonus:0.00} eco={sEco:0.00}");
        }
        catch { }
    }

    private static System.Collections.Generic.IEnumerable<UnitManager> AllPlayerUnits(GameSpace gs)
    {
        foreach (var u in gs.units) if (u != null) yield return u;
        var fly = gs.flyingUnits;
        if (fly != null) foreach (var f in fly) if (f != null) yield return f;
    }

    /// <summary>Pan the level select so a named planet sits at the middle of the
    /// view: "span:goto story20".
    ///
    /// This is what a player does by dragging, done precisely. It exists to
    /// answer whether an off-cluster planet is REACHABLE - the map is drag-panned
    /// with a clamp, so "it has a position" and "you can get to it" are separate
    /// questions and only the second one matters.</summary>
    private void SpanGoto(string guid)
    {
        if (guid.Length == 0) { _log.LogWarning("DEVCMD span:goto: need a planet guid, e.g. story20"); return; }
        try
        {
            SpanNetworkPlanet? target = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            }
            if (target == null) { _log.LogWarning($"DEVCMD span:goto: no planet '{guid}'"); return; }

            // Move the CAMERA, not a UI container: the Farsite view is panned by
            // Span (panMouseDown / mainCamera), and SpanMissionNetwork - which
            // does carry drag clamps - belongs to a different screen and is not
            // present here.
            var cam = Camera.main;
            if (cam == null) { _log.LogWarning("DEVCMD span:goto: no main camera"); return; }

            var tw = target.transform.position;
            // The world point currently at the centre of the view: cast the
            // camera's centre ray onto the plane the planets sit on.
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
            float denom = ray.direction.y;
            if (Mathf.Abs(denom) < 0.0001f) { _log.LogWarning("DEVCMD span:goto: camera is not looking at the plane"); return; }
            float t = (tw.y - ray.origin.y) / denom;
            var centre = ray.origin + ray.direction * t;

            var before = cam.transform.position;
            var delta = new Vector3(tw.x - centre.x, 0f, tw.z - centre.z);
            cam.transform.position = before + delta;
            _log.LogWarning($"DEVCMD span:goto {guid}: centre was ({centre.x:0.0},{centre.z:0.0}), " +
                            $"planet at ({tw.x:0.0},{tw.z:0.0}); camera moved by ({delta.x:0.0},{delta.z:0.0})");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:goto: {e.Message}"); }
    }

    /// <summary>Every planet on the level select, with its unlock state.
    ///
    /// The question this answers: a mission that exists and boots fine can still
    /// be absent or locked on the map, and no other view shows that. Reports the
    /// planet's own title rather than any list we maintain, so a title we got
    /// wrong shows up as a mismatch rather than agreeing with itself.</summary>
    private void PlanetsDump()
    {
        try
        {
            var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
            if (planets == null || planets.Length == 0)
            {
                _log.LogWarning("DEVCMD planets:dump: no planets - open the level select first (story:open)");
                return;
            }
            _log.LogWarning($"DEVPLANETS count={planets.Length}");

            // Where the connecting lines actually live. SpanNetworkPlanet.lines
            // reads 0 on every planet even though lines are plainly drawn, so
            // enumerate the components themselves and report their parentage.
            try
            {
                // Where the lines really are: NOT SpanNetworkPlanet.lines, which
                // reads empty on every planet. Each line is a child of a
                // planet's lineContainer, drawn in LOCAL space from the origin
                // to the neighbour's offset. Counting them is also how a MISSING
                // link shows up - vanilla has 19 lines for 20 connections,
                // because Founders -> story20 is never drawn.
                var ls = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetLine>();
                _log.LogWarning($"DEVLINES count={(ls == null ? 0 : ls.Length)}");
                if (ls != null)
                    foreach (var l in ls)
                    {
                        if (l == null) continue;
                        string parent = "";
                        try { parent = l.transform.parent?.name ?? ""; } catch { }
                        string gp = "";
                        try { gp = l.transform.parent?.parent?.name ?? ""; } catch { }
                        var lr = l.lineRenderer;
                        if (lr == null) { _log.LogWarning($"DEVLINE parent={gp}/{parent} noRenderer"); continue; }
                        var a = lr.GetPosition(0);
                        var b = lr.GetPosition(lr.positionCount - 1);
                        _log.LogWarning($"DEVLINE parent={gp}/{parent} n={lr.positionCount} " +
                                        $"space={(lr.useWorldSpace ? "world" : "local")} " +
                                        $"({a.x:0.0},{a.y:0.0},{a.z:0.0})->({b.x:0.0},{b.y:0.0},{b.z:0.0})");
                    }
            }
            catch (Exception e) { _log.LogWarning($"DEVLINES: {e.Message}"); }

            // The level select is drag-panned but CLAMPED (SpanMissionNetwork
            // min/maxDrag). Whether a planet can be reached at all depends on
            // those limits, not on its position alone, so report both.
            try
            {
                // FindObjectsOfTypeAll: FindObjectsOfType returned nothing here,
                // and an inactive or not-yet-enabled container still carries the
                // clamp values we need.
                foreach (var n in Resources.FindObjectsOfTypeAll<SpanMissionNetwork>())
                {
                    if (n == null) continue;
                    var lp = n.transform.localPosition;
                    _log.LogWarning($"DEVSPANNET '{n.gameObject.name}' localPos=({lp.x:0.0},{lp.y:0.0},{lp.z:0.0}) " +
                                    $"dragX={n.minDragX:0.0}..{n.maxDragX:0.0} dragY={n.minDragY:0.0}..{n.maxDragY:0.0}");
                }
            }
            catch (Exception e) { _log.LogWarning($"DEVSPANNET: {e.Message}"); }
            foreach (var p in planets)
            {
                if (p == null) continue;
                string title = "";
                try { if (p.title != null) title = (p.title.text ?? "").Trim(); } catch { }
                if (title.Length == 0) { try { title = (p.map_title ?? "").Trim(); } catch { } }
                string guid = ""; try { guid = p.planetGUID ?? ""; } catch { }
                bool unlocked = false; try { unlocked = p.unlocked; } catch { }
                bool forced = false; try { forced = p.forceUnlocked; } catch { }
                bool set = false; try { set = p.unlockedSet; } catch { }
                int links = 0; string linkTo = "";
                try
                {
                    var cg = p.connectedPlanetGUIDS;
                    if (cg != null)
                    {
                        links = cg.Length;
                        var parts = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < cg.Length; i++) parts.Add(cg[i] ?? "");
                        linkTo = string.Join(",", parts);
                    }
                }
                catch { }
                bool active = false; try { active = p.gameObject.activeInHierarchy; } catch { }
                // World AND screen position: "active and unlocked" does not mean
                // visible, and a planet parked outside the framed view looks
                // exactly like a missing one.
                var wp = Vector3.zero; try { wp = p.transform.position; } catch { }
                var lpos = Vector3.zero; try { lpos = p.transform.localPosition; } catch { }
                var sp = Vector3.zero; bool onScreen = false;
                try
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        sp = cam.WorldToScreenPoint(wp);
                        onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                    }
                }
                catch { }
                _log.LogWarning($"DEVPLANET '{title}' guid={guid} unlocked={unlocked} forceUnlocked={forced} " +
                                $"unlockedSet={set} links={links}->[{linkTo}] active={active} " +
                                $"world=({wp.x:0},{wp.y:0},{wp.z:0}) local=({lpos.x:0.0},{lpos.y:0.0},{lpos.z:0.0}) " +
                                $"screen=({sp.x:0},{sp.y:0}) onScreen={onScreen}");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD planets:dump: {e.Message}"); }
    }

    /// <summary>In-engine capture. Desktop screenshots only grab a crop when the
    /// game renders above the reported desktop resolution, and they miss the
    /// overlay layer entirely.</summary>
    private void Shot(string path)
    {
        if (path.Length == 0) { _log.LogWarning("DEVCMD shot: need a path"); return; }
        try
        {
            ScreenCapture.CaptureScreenshot(path);
            _log.LogInfo($"DEVCMD shot: {path}");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD shot: {e.Message}"); }
    }

    /// <summary>"set:instantbuild=off" - the hotkeys with no keyboard.</summary>
    private void Set(string arg)
    {
        var kv = arg.Split('=');
        if (kv.Length != 2) { _log.LogWarning("DEVCMD set: expected <cheat>=on|off"); return; }
        bool on = kv[1].Trim().ToLowerInvariant() != "off";
        switch (kv[0].Trim().ToLowerInvariant())
        {
            case "instantbuild": DevConfig.InstantBuild.Value = on; break;
            case "allbuildings": DevConfig.AllBuildings.Value = on; break;
            case "infiniteresources": DevConfig.InfiniteResources.Value = on; break;
            case "indestructible": DevConfig.Indestructible.Value = on; break;
            case "freezecreeper": DevConfig.FreezeCreeper.Value = on; break;
            case "overlay": DevConfig.ShowOverlay.Value = on; break;
            default: _log.LogWarning($"DEVCMD set: unknown cheat '{kv[0]}'"); return;
        }
        _log.LogInfo($"DEVCMD set: {kv[0]}={(on ? "on" : "off")}");
    }
}
