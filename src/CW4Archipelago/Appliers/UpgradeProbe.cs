using System;
using System.Collections.Generic;
using CW4Archipelago.Core;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Reads and drives CW4's ERN port upgrade system, so the Archipelago items that
/// act on it can be designed against measured numbers.
///
/// IMPORTANT, and the reason this file was rewritten: the obvious-looking
/// UpgradeManager singleton is never constructed in the Farsite campaign, so
/// every GetLevel/Purchase call on it silently does nothing. The live system is
/// on ERNInterface - the ERN port unit itself - which has upgradeSlots[],
/// dockedTimes[], EFFICIENCY_TIME, AssignERN/ReleaseERN and GetEff.
///
/// Six upgrades: energy production, mine production, build speed, move speed,
/// fire range, fire rate. Docking an ERN ramps one from nothing to full over
/// EFFICIENCY_TIME; GetEff reports where it has got to.
/// </summary>
public static class UpgradeProbe
{
    /// <summary>Every ERN port on the map.</summary>
    private static List<ERNInterface> Ports()
    {
        var list = new List<ERNInterface>();
        var gs = GameSpace.instance;
        if (gs == null) return list;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                var p = u.TryCast<ERNInterface>();
                if (p != null) list.Add(p);
            }
            catch { }
        }
        return list;
    }

    /// <summary>The live state of every ERN port: what is docked, how far each
    /// upgrade has ramped, and what the game reports as its efficiency.</summary>
    public static void Dump()
    {
        var ports = Ports();
        if (ports.Count == 0)
        {
            ModCore.Log.LogWarning("ERN dump: no ERN port on the map (spawn:erninterface)");
            return;
        }

        int n = 0;
        foreach (var port in ports)
        {
            // Sim state on every dump. tickCount is the only honest answer to "is
            // the mission actually running" - a paused game reads exactly like a
            // very slow ramp, and an entire measurement run was wasted on that.
            try
            {
                var g = GameSpace.instance;
                if (g != null)
                    ModCore.Log.LogInfo(
                        $"ERN sim: paused={g.paused} tickCount={g.tickCount} speed={g.GAME_SPEED}");
            }
            catch { }

            int effTime = -1;
            // Static, so it is one number for every port in the game - which is
            // why the per-upgrade fill rate goes through dockedTimes instead.
            try { effTime = ERNInterface.EFFICIENCY_TIME; } catch { }
            ModCore.Log.LogInfo($"ERN dump: port {n} EFFICIENCY_TIME={effTime}");
            for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
            {
                int docked = -1; float eff = -1f; bool avail = false, enroute = false, slot = false;
                try { var d = port.dockedTimes; if (d != null && i < d.Length) docked = d[i]; } catch { }
                try { eff = port.GetEff(i); } catch { }
                try { avail = port.IsUpgradeAvailable(i); } catch { }
                try { enroute = port.IsUpgradeEnroute(i); } catch { }
                try { var us = port.upgradeSlots; slot = us != null && i < us.Length && us[i] != null; } catch { }
                ModCore.Log.LogInfo(
                    $"ERN   [{i}] {ErnUpgradeRules.UpgradeNames[i].PadRight(18)} " +
                    $"eff={eff:0.###} {EffPair(port, i)} docked={docked} " +
                    $"slotFilled={slot} available={avail} enroute={enroute}");
            }
            n++;
        }
    }

    /// <summary>Every primitive property of a unit, or of GameSpace, so a
    /// before/after DIFF can find the field an upgrade moves.
    ///
    /// WHY THIS EXISTS. Guessing which field an upgrade writes has been wrong
    /// three times now, and each wrong guess reads as "the upgrade does
    /// nothing":
    ///
    ///   - Fire Range moves MYRANGE, not RANGE, and MYRANGE is declared per
    ///     weapon type rather than on the base class.
    ///   - Fire Rate moves COOL_DOWN, a SHOUTING-CASE property that looks like
    ///     an immutable base constant and is in fact where the effective reload
    ///     is written (8 -> 6 -> 4 as the upgrade climbs).
    ///   - Energy Production does not move gs.energyProduction at all, which
    ///     sits at 0 while gs.energyStore visibly climbs.
    ///
    /// So stop guessing. Il2CppInterop generates ordinary managed properties on
    /// the wrapper types, which means plain System.Reflection can enumerate
    /// them. Dump every int/float/bool, diff two dumps, and the field that
    /// moved names itself.</summary>
    public static void DumpAll(string filter)
    {
        if (string.Equals(filter, "gs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter, "gamespace", StringComparison.OrdinalIgnoreCase))
        {
            var gsp = GameSpace.instance;
            if (gsp == null) { ModCore.Log.LogWarning("DUMPALL: no GameSpace"); return; }
            DumpPrimitives("GameSpace", gsp);
            return;
        }

        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("DUMPALL: no GameSpace"); return; }
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                string name = u.GetDataName() ?? "?";
                if (!string.IsNullOrEmpty(filter)
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // The concrete type, not UnitManager: the interesting properties
                // (MYRANGE, COOL_DOWN) are declared on Cannon and friends.
                DumpPrimitives(name, u.TryCast<Cannon>() ?? (Il2CppSystem.Object)u);
                return;                          // one unit is what a diff needs
            }
            catch { }
        }
        ModCore.Log.LogWarning($"DUMPALL: no player unit matched '{filter}'");
    }

    /// <summary>Read every primitive-valued property off an interop wrapper.
    /// Each read is guarded: some properties throw when the object is not in a
    /// state that supports them, and one throwing must not stop the dump.</summary>
    private static void DumpPrimitives(string label, Il2CppSystem.Object obj)
    {
        var t = obj.GetType();
        var names = new List<string>();
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public
                                          | System.Reflection.BindingFlags.Instance))
        {
            var pt = p.PropertyType;
            if (pt != typeof(int) && pt != typeof(float) && pt != typeof(bool)
                && pt != typeof(short) && pt != typeof(double) && pt != typeof(long)) continue;
            if (p.GetIndexParameters().Length != 0) continue;
            string val;
            try { val = Convert.ToString(p.GetValue(obj, null)) ?? "?"; }
            catch { continue; }
            names.Add($"{p.Name}={val}");
        }
        names.Sort(StringComparer.Ordinal);
        ModCore.Log.LogInfo($"DUMPALL {label} ({names.Count} values):");
        // One property per line so a diff is line-oriented and readable.
        foreach (var n in names) ModCore.Log.LogInfo($"DUMPALL   {label}.{n}");
    }

    /// <summary>Every resource node and refinery on the map, with the fields a
    /// Mine Production upgrade would plausibly move.
    ///
    /// The designer's steer: measuring Mine Production needs a miner on reso
    /// ground, or a refinery near a greenar, or a tower near a reddite/bluite
    /// crystal - and all of them need a factory to hold the resource. That is a
    /// real economy to build, so before building it, check whether the effect
    /// is simply READABLE.
    ///
    /// The reason to expect it might be: Fire Rate turned out to write its
    /// effective value straight into Cannon.COOL_DOWN, a SHOUTING-CASE int that
    /// looks like an immutable constant. Resource.PRODUCTION_INTERVAL and
    /// GreenarRefinery.PROCESS_INTERVAL have exactly that shape. If either
    /// moves with the upgrade, the timed gathering test becomes a confirmation
    /// rather than the primary measurement.</summary>
    public static void Resources()
    {
        int n = 0;
        try
        {
            foreach (var r in UnityEngine.Resources.FindObjectsOfTypeAll<Resource>())
            {
                if (r == null) continue;
                try
                {
                    // Scene objects only: FindObjectsOfTypeAll also returns
                    // prefabs, which are not on the map and never produce.
                    if (!r.gameObject.scene.IsValid()) continue;
                    var p = r.transform.position;
                    ModCore.Log.LogInfo(
                        $"RESOURCE node: PRODUCTION_INTERVAL={r.PRODUCTION_INTERVAL} " +
                        $"blobInterval={r.BLOB_PRODUCTION_INTERVAL} counter={r.productionCounter} " +
                        $"ampCount={r.ampCount} wareAvailable={r.wareAvailable} " +
                        $"active={r.gameObject.activeInHierarchy} pos=({p.x:0.#},{p.z:0.#})");
                    n++;
                }
                catch { }
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"RESOURCE scan failed: {e.Message}"); }

        try
        {
            foreach (var g in UnityEngine.Resources.FindObjectsOfTypeAll<GreenarRefinery>())
            {
                if (g == null) continue;
                try
                {
                    if (!g.gameObject.scene.IsValid()) continue;
                    var p = g.transform.position;
                    ModCore.Log.LogInfo(
                        $"RESOURCE refinery: PROCESS_INTERVAL={g.PROCESS_INTERVAL} " +
                        $"processCount={g.processCount} greenar={g.greenar} " +
                        $"greenarCrystal={g.greenarCrystal} wareAvailable={g.wareAvailable} " +
                        $"buildDroneRate={g.BUILD_DRONE_RATE:0.###} pos=({p.x:0.#},{p.z:0.#})");
                    n++;
                }
                catch { }
            }
        }
        catch { }

        // Say so explicitly. "No output" has already been mistaken for "nothing
        // changed" once in this system, and a mission with no ore looks exactly
        // like a probe that failed.
        if (n == 0)
            ModCore.Log.LogWarning(
                "RESOURCE scan: no resource nodes or refineries in this mission - "
                + "Mine Production cannot be measured here, pick a mission with ore");
    }

    /// <summary>What the ERN port's own panel is SHOWING, per upgrade row.
    ///
    /// The designer's question, and it is the right one: a boosted ceiling is
    /// only a feature if the player can see it. The panel row (UpgradeItem) has
    /// an efficiencyText and an efficiencyBar, and they can disagree with each
    /// other - efficiencyBar is a Unity Image, whose fillAmount is CLAMPED to
    /// 0..1, so a 200 percent efficiency cannot overfill the bar no matter what
    /// the underlying value is. The text may well read 200 percent beside a bar
    /// that looks exactly like 100.
    ///
    /// Reads the live UI objects rather than reasoning about them, because the
    /// last thing this system taught us is that a value we compute and a value
    /// the game uses are different questions.</summary>
    public static void Ui()
    {
        UpgradeItem[]? items = null;
        try { items = UnityEngine.Resources.FindObjectsOfTypeAll<UpgradeItem>(); } catch { }
        if (items == null || items.Length == 0)
        {
            ModCore.Log.LogWarning("ERN ui: no UpgradeItem rows found");
            return;
        }

        int n = 0;
        foreach (var it in items)
        {
            if (it == null) continue;
            try
            {
                int type = -1; string text = "?"; float fill = -1f;
                bool active = false, locked = false, docked = false, enroute = false;
                try { type = it.upgradeType; } catch { }
                try { active = it.gameObject.activeInHierarchy; } catch { }
                try { locked = it.locked; } catch { }
                // Refresh first: an inactive row keeps whatever it last drew, so
                // reading it without refreshing reports a stale frame as truth.
                try { it.Refresh(); } catch { }
                try { text = (it.efficiencyText?.text ?? "<null>").Trim(); } catch { }
                try { if (it.efficiencyBar != null) fill = it.efficiencyBar.fillAmount; } catch { }
                try { docked = it.dockedLight != null && it.dockedLight.enabled; } catch { }
                try { enroute = it.enrouteLight != null && it.enrouteLight.enabled; } catch { }

                string name = ErnUpgradeRules.IsValidIndex(type)
                    ? ErnUpgradeRules.UpgradeNames[type] : $"type {type}";
                float raw = -1f;
                try { raw = ERNInterface.GetEfficiency(type); } catch { }

                ModCore.Log.LogInfo(
                    $"ERN ui: [{type}] {name.PadRight(18)} text=\"{text}\" barFill={fill:0.###} " +
                    $"getEfficiency={raw:0.###} active={active} locked={locked} " +
                    $"dockedLight={docked} enrouteLight={enroute}");
                n++;
            }
            catch { }
        }
        if (n == 0) ModCore.Log.LogWarning("ERN ui: rows found but none readable");
    }

    /// <summary>Dock an ERN into an upgrade slot without a human dragging it.
    /// This is what makes the whole system testable unattended.</summary>
    public static void Assign(int index)
    {
        var ports = Ports();
        if (ports.Count == 0) { ModCore.Log.LogWarning("ERN assign: no ERN port"); return; }
        try
        {
            ports[0].AssignERN(index);
            ModCore.Log.LogInfo($"ERN assign: slot {index} requested");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"ERN assign {index} failed: {e.Message}"); }
    }

    /// <summary>Undock a slot, so the ramp can be watched from zero again.</summary>
    public static void Release(int index)
    {
        var ports = Ports();
        if (ports.Count == 0) { ModCore.Log.LogWarning("ERN release: no ERN port"); return; }
        try
        {
            ports[0].ReleaseERN(index);
            ModCore.Log.LogInfo($"ERN release: slot {index} released");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"ERN release {index} failed: {e.Message}"); }
    }

    /// <summary>Every ERN on the map and what it is doing.
    ///
    /// Written because an assigned ERN sat "enroute" for 90 seconds and never
    /// arrived, with no way to tell whether it was flying, stuck, or BURIED -
    /// CW4 ships ERNs buried in the ground and they must be excavated before
    /// they can move at all.</summary>
    public static void Erns()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("ERN list: no GameSpace"); return; }
        int n = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            ERN? e = null;
            try { e = u.TryCast<ERN>(); } catch { }
            if (e == null) continue;
            string st = "?"; bool avail = false, docked = false, dig = false; float raised = -1f;
            try { st = e.state.ToString(); } catch { }
            try { avail = e.IsAvailable(); } catch { }
            try { docked = e.IsDocked(); } catch { }
            try { dig = e.beingExcavated; } catch { }
            try { raised = e.lastPercentRaised; } catch { }
            ModCore.Log.LogInfo(
                $"ERN unit {n}: state={st} available={avail} docked={docked} " +
                $"beingExcavated={dig} raised={raised:0.##}");
            n++;
        }
        if (n == 0) ModCore.Log.LogInfo("ERN list: no ERNs on the map");
    }

    /// <summary>Force every buried ERN to WAITING so a test can use it.
    ///
    /// Test scaffolding, not a game feature: excavating properly is a player
    /// activity and this is only here so an automated probe does not have to
    /// simulate one.</summary>
    public static void FreeErns()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("ERN free: no GameSpace"); return; }
        int freed = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                var e = u.TryCast<ERN>();
                if (e == null) continue;
                if (e.state != ERN.STATE.BURIED) continue;
                e.SetState(ERN.STATE.WAITING);
                freed++;
            }
            catch { }
        }
        ModCore.Log.LogInfo($"ERN free: {freed} buried ERN(s) set to WAITING");
    }

    /// <summary>A unit's EFFECTIVE range, which is the only number a range
    /// upgrade ever moves.
    ///
    /// WHY THIS IS A LADDER OF CASTS: MYRANGE is not on UnitManager. It is
    /// declared separately on each weapon type - Cannon, Mortar, Sniper,
    /// MissileLauncher, Sprayer, Terp, Nullifier - so there is no base-class
    /// property to read and a single TryCast finds nothing.
    ///
    /// The earlier probe cast only to CModUnitManager, which a Cannon is not,
    /// so it printed no effective range at all and the test fell back to
    /// UnitManager.RANGE. RANGE is the BASE and is constant by design, so the
    /// run concluded "the Fire Range upgrade does nothing" from a number that
    /// could never have changed.</summary>
    private static int? EffectiveRange(UnitManager u)
    {
        try { var c = u.TryCast<Cannon>();           if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Mortar>();           if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Sniper>();           if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<MissileLauncher>();  if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Sprayer>();          if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Terp>();             if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Nullifier>();        if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<Chronat>();          if (c != null) return c.MYRANGE; } catch { }
        try { var c = u.TryCast<CModUnitManager>();  if (c != null) return c.MYRANGE; } catch { }
        return null;
    }

    /// <summary>The unit types worth dumping for an upgrade sweep: the weapons
    /// a range or rate upgrade acts on, plus the collector an economy upgrade
    /// acts on.</summary>
    private static readonly string[] Watched =
        { "cannon", "mortar", "sniper", "missilelauncher", "collector" };

    private static bool IsWatched(string name)
    {
        foreach (var w in Watched)
            if (string.Equals(name, w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>What the game's own upgrade accessors report right now.
    ///
    /// Both are logged because they are DIFFERENT METHODS and disagreeing is
    /// the actual bug this probe exists to catch: GetEff is the per-port
    /// instance accessor that the UI reads, GetEfficiency is the static one the
    /// sim reads. A ceiling applied to only one of them looks like it works
    /// from every probe and changes nothing in the game.</summary>
    private static string EffPair(ERNInterface port, int i)
    {
        float inst = -1f, stat = -1f;
        try { inst = port.GetEff(i); } catch { }
        try { stat = ERNInterface.GetEfficiency(i); } catch { }
        return $"getEff={inst:0.###} getEfficiency(static)={stat:0.###}";
    }

    /// <summary>The observables each ERN upgrade is supposed to move.
    ///
    /// WHY THIS EXISTS: "the cannon's RANGE did not change" was a wrong test, not
    /// a failed upgrade. UnitManager.RANGE is the BASE range and never moves. The
    /// effective value is computed per unit type at use time - SniperRangeIndicator
    /// has its own MyRange() plus rangeUpgradeBoost and rangePZBoost fields - so
    /// the upgrade has to be observed where it is applied, not where it starts.
    ///
    /// Dumps one line per observable so a before/after diff shows which upgrade
    /// moved what, instead of guessing which field to watch.</summary>
    public static void Stats()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("STATS: no GameSpace"); return; }

        // Global economy: ENERGY_PRODUCTION and MINE_PRODUCTION should show here.
        try
        {
            ModCore.Log.LogInfo(
                $"STATS energy: production={gs.energyProduction:0.###} " +
                $"unclamped={gs.energyProductionUnClamped:0.###} store={gs.energyStore:0.#} " +
                $"use={gs.energyUse:0.###} deficit={gs.energyDeficit:0.###}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"STATS energy failed: {e.Message}"); }

        // FIRE_RANGE lands on the range indicators, which is the only place an
        // upgraded range is actually computed.
        int ind = 0;
        try
        {
            foreach (var r in UnityEngine.Resources.FindObjectsOfTypeAll<SniperRangeIndicator>())
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                ModCore.Log.LogInfo(
                    $"STATS sniperRange: range={r.range} upgradeBoost={r.rangeUpgradeBoost:0.###} " +
                    $"pzBoost={r.rangePZBoost:0.###}");
                if (++ind >= 3) break;
            }
        }
        catch { }
        try
        {
            foreach (var r in UnityEngine.Resources.FindObjectsOfTypeAll<MissileLauncherRangeIndicator>())
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                ModCore.Log.LogInfo($"STATS missileRange: upgradeBoost={r.rangeUpgradeBoost:0.###}");
                break;
            }
        }
        catch { }
        if (ind == 0) ModCore.Log.LogInfo("STATS sniperRange: no active indicator (build a sniper)");

        // MOVE_SPEED and BUILD_SPEED show as unit state over time.
        int n = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                string name = u.GetDataName() ?? "?";
                // Case-INSENSITIVE. GetDataName() returns the lowercased data
                // name, so an exact-case list matched nothing and the whole
                // per-unit half of this dump silently printed no lines at all -
                // which reads identically to "no units on the map".
                if (!IsWatched(name)) continue;
                var pos = u.transform.position;
                var eff = EffectiveRange(u);

                // FIRE_RATE has no effective-value property anywhere - it is
                // applied inline in native code - so the observable is the
                // cannon's own cooldown. COOL_DOWN is the base; coolDown is the
                // live countdown, and the LARGEST value seen just after a shot
                // is the effective reload. Sampling it needs the cannon to be
                // shooting, which is why this pairs with a creeper spawn.
                string fire = "";
                try
                {
                    var c = u.TryCast<Cannon>();
                    if (c != null) fire = $" coolDown={c.coolDown}/{c.COOL_DOWN}";
                }
                catch { }

                // BUILD_SPEED likewise: no property, so the measurement is the
                // wall-clock time isBuilding takes to go true -> false. Cubes
                // give a coarse progress readout while it is under way.
                string build = "";
                try { if (u.isBuilding) build = $" buildBarCubes={u.BuildBarCubes}"; }
                catch { }

                // MYRANGE first: it is the number that moves. RANGE is printed
                // beside it only so the pair shows base-vs-effective.
                ModCore.Log.LogInfo(
                    $"STATS unit {name}: MYRANGE={(eff.HasValue ? eff.Value.ToString() : "n/a")} " +
                    $"RANGE={u.RANGE} rangeBoost={u.UPGRADE_RANGE_BOOST:0.###} ammo={u.ammo:0.##} " +
                    $"building={u.isBuilding}{build}{fire} pos=({pos.x:0.#},{pos.z:0.#})");
                if (++n >= 8) break;
            }
            catch { }
        }
        // Say so explicitly. Printing nothing is what let a broken name filter
        // look exactly like an empty map for a whole run.
        if (n == 0)
            ModCore.Log.LogWarning(
                "STATS unit: none of " + string.Join("/", Watched) + " found on the map");
    }

    /// <summary>Every visible piece of UI text, with the object path that owns
    /// it, so an on-screen panel can be identified instead of guessed at.
    ///
    /// Written after ada:close and ada:clear both failed to remove a mission
    /// story panel: the text was plainly visible in a screenshot but belonged to
    /// neither the ADA log nor the revealed-message list, and hunting type names
    /// in the assembly was not converging.</summary>
    public static void UiText(string filter)
    {
        var texts = UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>();
        if (texts == null) { ModCore.Log.LogWarning("UI text: none found"); return; }
        int n = 0;
        foreach (var t in texts)
        {
            if (t == null) continue;
            try
            {
                if (!t.gameObject.activeInHierarchy) continue;
                string body = (t.text ?? "").Replace('\n', ' ').Trim();
                if (body.Length == 0) continue;
                if (!string.IsNullOrEmpty(filter)
                    && body.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var path = t.gameObject.name;
                var tr = t.transform.parent;
                for (int up = 0; up < 4 && tr != null; up++) { path = tr.name + "/" + path; tr = tr.parent; }
                if (body.Length > 60) body = body.Substring(0, 60) + "...";
                ModCore.Log.LogInfo($"UI text: [{path}] \"{body}\"");
                if (++n >= 30) { ModCore.Log.LogInfo("UI text: ... truncated at 30"); break; }
            }
            catch { }
        }
        if (n == 0) ModCore.Log.LogInfo($"UI text: nothing active matched '{filter}'");
    }

    /// <summary>Hide the top-level object owning a piece of UI text.
    /// Test scaffolding for getting story panels out of a screenshot.</summary>
    public static void UiHide(string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) { ModCore.Log.LogWarning("UI hide: need text"); return; }
        var texts = UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>();
        if (texts == null) return;
        int hidden = 0;
        foreach (var t in texts)
        {
            if (t == null) continue;
            try
            {
                if (!t.gameObject.activeInHierarchy) continue;
                if ((t.text ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Two levels up is the panel; the text itself is usually a child
                // of a layout row.
                var go = t.transform.parent?.parent?.gameObject ?? t.gameObject;
                go.SetActive(false);
                ModCore.Log.LogInfo($"UI hide: disabled '{go.name}'");
                hidden++;
            }
            catch { }
        }
        ModCore.Log.LogInfo($"UI hide: {hidden} object(s) hidden for '{needle}'");
    }

    /// <summary>Clear ADA's revealed messages, which is the banner across the
    /// top of the screen. ada:close only shuts the log panel.</summary>
    public static void ClearAda()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("ADA clear: no GameSpace"); return; }
        try
        {
            var msgs = gs.adaMessages;
            if (msgs == null) { ModCore.Log.LogWarning("ADA clear: no adaMessages"); return; }
            msgs.ClearAllRevealedMessages();

            // Clearing the message LIST does not remove panels already on
            // screen: the mission's story text lives in ADATextBlockRow objects
            // that keep rendering, and they reappear as new messages fire. Hide
            // the rows too, or every screenshot has a wall of text across it.
            int rows = 0;
            foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
            {
                if (t == null) continue;
                try
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    var tr = t.transform;
                    // Six, not four: the banner's path is
                    // MessageArea/ControlRow/Buttons/Button/Text, so the
                    // owner sits four levels ABOVE the text and a 4-step
                    // walk starting at the text stops one short of it.
                    for (int up = 0; up < 6 && tr != null; up++)
                    {
                        // Two separate systems put story text on screen:
                        // ADATextBlockRow inside the A.D.A. log, and MessageArea
                        // for the dismissible banner. Clearing one leaves the
                        // other, which is how a screenshot still had a wall of
                        // text after "0 story panels hidden" looked like success.
                        if (tr.name.StartsWith("ADATextBlockRow", StringComparison.Ordinal)
                            || tr.name.StartsWith("MessageArea", StringComparison.Ordinal))
                        {
                            tr.gameObject.SetActive(false);
                            rows++;
                            break;
                        }
                        tr = tr.parent;
                    }
                }
                catch { }
            }
            ModCore.Log.LogInfo($"ADA clear: revealed messages cleared, {rows} story panel(s) hidden");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"ADA clear failed: {e.Message}"); }
    }

    /// <summary>Per-unit numbers the upgrades are supposed to move.
    ///
    /// RANGE is the unit's base and MYRANGE its effective value, so the pair is
    /// the readable evidence that a FIRE_RANGE level did anything. ernDocked and
    /// CAN_ERN sit alongside because the question is not only "did it change" but
    /// "did it change only for units with an ERN in them".</summary>
    public static void Units(string filter)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("UPGRADE units: no GameSpace"); return; }

        int n = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                string name = u.GetDataName() ?? "?";
                if (!string.IsNullOrEmpty(filter)
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                int range = -1, cost = -1;
                float boost = -1f, ammo = -1f, maxAmmo = -1f;
                bool docked = false, canErn = false;
                try { range = u.RANGE; } catch { }
                var eff = EffectiveRange(u);
                string myRangeText = eff.HasValue ? $" MYRANGE={eff.Value}" : " MYRANGE=n/a";
                try { boost = u.UPGRADE_RANGE_BOOST; } catch { }
                try { cost = u.BUILD_COST; } catch { }
                try { ammo = u.ammo; } catch { }
                try { maxAmmo = u.MAX_AMMO; } catch { }
                try { docked = u.ernDocked; } catch { }
                try { canErn = u.CAN_ERN; } catch { }

                ModCore.Log.LogInfo(
                    $"UPGRADE unit {name.PadRight(16)} RANGE={range}{myRangeText} rangeBoost={boost:0.##} " +
                    $"buildCost={cost} ammo={ammo:0.#}/{maxAmmo:0.#} ernDocked={docked} canERN={canErn}");
                n++;
                if (n >= 40) { ModCore.Log.LogInfo("UPGRADE units: ... truncated at 40"); break; }
            }
            catch { }
        }
        if (n == 0) ModCore.Log.LogInfo($"UPGRADE units: nothing matched '{filter}'");
    }
}
