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
