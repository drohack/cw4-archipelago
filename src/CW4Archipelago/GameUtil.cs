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
    // GetDataName() returns the build-pane key for every buildable except the
    // rift lab, which reports "CommandBase" rather than the "riftlab" key
    // UnitRules uses; cannon/tower/mortar/sniper/sprayer/terp were all verified
    // to match, so CommandBase is the lone alias.
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
    // STILL UNRESOLVED: "porter". Farsite grants it at story12 but the registry
    // has no Porter, so its real name is unknown and per-unit effects will skip
    // it. The literal "porter" in UnitRules cannot match - GetDataName() only
    // returns registry names.
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
            return key != null && _playerKeys.Contains(key);
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
