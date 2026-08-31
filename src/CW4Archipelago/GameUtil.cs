using System;
using System.Collections.Generic;
using UnityEngine;

namespace CW4Archipelago;

/// <summary>
/// Shared IL2CPP-safe helpers ported from the probe. After LoadGame destroys
/// the previous mission scene, Resources scans can return DESTROYED instances,
/// so everything is liveness-checked; interop wrappers are compared by
/// .Pointer, never by reference.
/// </summary>
public static class GameUtil
{
    // UnitManager.enemy is NOT a player/enemy discriminator: story3 reports Pod,
    // Ultrac and SuperTower (all hostile) with enemy=false, and only Emitter
    // with enemy=true. So anything that buffs or debuffs "the player's units"
    // must filter on the authoritative list instead - the unit keys the player
    // can actually build, matched through UnitManager.GetDataName().
    //
    // GetDataName() does NOT return the build-pane key for every buildable, and
    // an earlier version of this comment claimed CommandBase was "the lone
    // alias". It is not: riftlab, pylon, miner, porter and ernportal are all
    // build-pane keys with no registry entry, and three more units are CMODs that
    // return a GUID instead of any name at all. The full table is in
    // docs/research-findings.md, "Unit naming"; the aliases are below.
    private static HashSet<string>? _playerKeys;

    // WHY THIS LIST EXISTS: Core.UnitRules.ItemToUnit holds BUILD-PANE KEYS, not
    // the game's unit names. The game's registry (UnitData.unitConstants, 88
    // entries) contains no "pylon", "miner", "porter" or "riftlab", so
    // GetDataName() can never return them and those buildings fell straight
    // through this filter - trap stun, weapon drain and spore targeting all
    // silently skipped them until 2026-08-28.
    //
    //     riftlab -> CommandBase      pylon      -> TowerBridge
    //     miner   -> Collector        ernportal  -> ERNInterface
    //
    // Also player-buildable but absent from UnitRules: SuperTower, Reactor,
    // DeliveryPad, StoragePad, Stash and the drones. SuperTower is ambiguous
    // (player button, but also pre-placed on maps).
    //
    // PORTER, resolved 2026-08-31, and it needed a FOURTH name space to explain.
    // Button GameObject names match neither the build-pane key nor the unit name:
    // granting one item at a time and watching structButtons go 1 -> 2 -> 3, then
    // reading the pane's own labels against the GameObject list, gives
    //
    //     label PYLON  -> SuperTowerButton      label MINER  -> ReactorButton
    //     label PORTER -> DeliveryPadButton
    //
    // So the porter is the DELIVERY family, and DeliveryPad, DeliveryDrone,
    // StoragePad and Stash are all already in this list - per-unit effects were
    // covering porters after all. The registry (dumped in full into
    // research-findings.md) contains no Porter and spawn:Porter places nothing,
    // which is consistent: "porter" is only ever a build-pane key.
    //
    // CONFIRMED by building one, 2026-08-31, because a button's object name does
    // not prove which prefab it places - PYLON's button is SuperTowerButton yet
    // the unit is TowerBridge. A hand-placed porter dumps as
    //
    //     DeliveryPad/DeliveryPad=MINEx1   DeliveryDrone/DeliveryDrone=MINEx1
    //
    // both already in this list, and ReportSkippedBuild stayed quiet about them
    // (its only complaints were Pod and Shot - an enemy and a projectile). So
    // there was never a coverage gap here.
    //
    // Full three-name-space explanation, the discriminators that do NOT work
    // (UnitManager.enemy, UnitConstants.ENEMY), and how to re-derive the mapping:
    // docs/research-findings.md, "Unit naming". CW4DevTools (Home key) dumps it.
    private static readonly string[] ExtraPlayerKeys =
    {
        "ern", "CommandBase", "TowerBridge",
        "Collector", "CollectorPanel3", "CollectorPanel5",
        "ERNInterface", "SuperTower", "Reactor",
        "DeliveryPad", "DeliveryDrone", "StoragePad", "Stash",
        "TerpDrone", "GreenarDrone", "Bomber", "ACBomber", "Rocket",
    };

    public static bool IsPlayerUnit(UnitManager u)
    {
        _playerKeys ??= new HashSet<string>(
            System.Linq.Enumerable.Concat(
                System.Linq.Enumerable.Concat(Core.UnitRules.AlwaysAvailable, Core.UnitRules.ItemToUnit.Values),
                ExtraPlayerKeys),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            if (u == null || u.enemy) return false;
            var key = u.GetDataName();
            if (key == null) return false;
            if (_playerKeys.Contains(key)) return true;
            // A CMOD unit's GetDataName() returns a GUID, never a name, so no
            // name whitelist can ever match one - and airship, bertha and sweeper
            // are CMODs. Without this they fall through, which meant trap stun,
            // ammo drain and spore targeting silently skipped three of the
            // player's units: the same bug class that skipped pylons and miners
            // until 2026-08-28, fixed in CW4DevTools at the time but never here.
            //
            // The ownership test is data-driven on purpose, so a new custom unit
            // needs no code change: a non-empty playerMenuUnitName means the unit
            // is offered in the PLAYER's build menu, which cleanly separates the
            // three player CMODs from the map/editor-only ones.
            return IsPlayerCmod(key);
        }
        catch { return false; }
    }

    /// <summary>Whether a CMOD GUID belongs to a unit the player can build.
    /// Mirrors DevTools.IsPlayerCmod - kept as its own copy because the two
    /// plugins must not depend on each other.</summary>
    private static bool IsPlayerCmod(string guid)
    {
        try
        {
            var cmods = GameSpace.instance?.cmods;
            if (cmods == null || !cmods.ContainsKey(guid)) return false;
            var cmod = cmods[guid];
            if (cmod == null) return false;
            return !string.IsNullOrEmpty(cmod.playerMenuUnitName);
        }
        catch { return false; }
    }

    public static bool IsAlive(Component c)
    {
        try
        {
            if (c == null) return false;
            var go = c.gameObject;
            if (go == null) return false;
            return go.scene.IsValid();
        }
        catch { return false; }
    }

    public static LeftPane? FindLeftPane()
    {
        try
        {
            var lp = GameSpace.instance?.leftPane;
            if (IsAlive(lp)) return lp;
        }
        catch { }
        var all = Resources.FindObjectsOfTypeAll<LeftPane>();
        if (all == null) return null;
        foreach (var lp in all)
            if (IsAlive(lp)) return lp;
        return null;
    }

    public static List<UnitBuildPane> AllPanes(bool includeInactive)
    {
        var result = new List<UnitBuildPane>();
        var lp = FindLeftPane();
        if (lp != null)
        {
            foreach (var pn in new[] { lp.structUnitBuildPane, lp.weaponUnitBuildPane,
                                       lp.airUnitBuildPane, lp.specialUnitBuildPane,
                                       lp.customUnitBuildPane })
                if (IsAlive(pn) && (includeInactive || pn.gameObject.activeInHierarchy))
                    result.Add(pn);
            return result;
        }
        var all = includeInactive
            ? Resources.FindObjectsOfTypeAll<UnitBuildPane>()
            : UnityEngine.Object.FindObjectsOfType<UnitBuildPane>();
        if (all != null)
            foreach (var p in all)
                if (IsAlive(p)) result.Add(p);
        return result;
    }

    public static void NativeRefresh(LeftPane lp, bool pickTab)
    {
        lp.RefreshUnitBuildPanes();
        if (pickTab)
            lp.PickActiveTab();
    }

    /// <summary>Keep exactly one pane active - the selected tab's (or a forced target).</summary>
    public static void ResyncStrip(LeftPane lp, UnitBuildPane? forcedTarget = null)
    {
        UnitBuildPane? target = forcedTarget;
        if (target == null)
        {
            target = lp.structUnitBuildPane;
            if (lp.weaponTab != null && lp.weaponTab.isOn) target = lp.weaponUnitBuildPane;
            else if (lp.airTab != null && lp.airTab.isOn) target = lp.airUnitBuildPane;
            else if (lp.specialTab != null && lp.specialTab.isOn) target = lp.specialUnitBuildPane;
            else if (lp.customTab != null && lp.customTab.isOn) target = lp.customUnitBuildPane;
        }
        var targetPtr = target != null ? target.Pointer : IntPtr.Zero;
        foreach (var pn in new[] { lp.structUnitBuildPane, lp.weaponUnitBuildPane,
                                   lp.airUnitBuildPane, lp.specialUnitBuildPane,
                                   lp.customUnitBuildPane })
            if (pn != null)
                pn.gameObject.SetActive(pn.Pointer == targetPtr);
    }
}
