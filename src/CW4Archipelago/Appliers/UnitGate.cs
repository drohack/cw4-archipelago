using System;
using System.Collections.Generic;
using CW4Archipelago.Core;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Enforces the allowed-unit whitelist and build limits on the live
/// BuildUnitManager, with the proven no-flash hide + multi-frame reveal.
/// Ported from the probe; driven by SlotState instead of a file whitelist.
/// </summary>
public sealed class UnitGate
{
    private static readonly Dictionary<string, Action<BuildUnitManager, bool>> Setters = new()
    {
        ["riftlab"] = (b, v) => b.riftLabAvailable = v,
        ["factory"] = (b, v) => b.factoryAvailable = v,
        ["ernportal"] = (b, v) => b.ernPortalAvailable = v,
        ["tower"] = (b, v) => b.towerAvailable = v,
        ["pylon"] = (b, v) => b.pylonAvailable = v,
        ["miner"] = (b, v) => b.minerAvailable = v,
        ["greenarrefinery"] = (b, v) => b.greenarRefineryAvailable = v,
        ["terp"] = (b, v) => b.terpAvailable = v,
        ["porter"] = (b, v) => b.porterAvailable = v,
        ["cannon"] = (b, v) => b.cannonAvailable = v,
        ["mortar"] = (b, v) => b.mortarAvailable = v,
        ["sprayer"] = (b, v) => b.sprayerAvailable = v,
        ["sniper"] = (b, v) => b.sniperAvailable = v,
        ["missilelauncher"] = (b, v) => b.missileLauncherAvailable = v,
        ["nullifier"] = (b, v) => b.nullifierAvailable = v,
        ["runway"] = (b, v) => b.runwayAvailable = v,
        ["bomberpad"] = (b, v) => b.bomberPadAvailable = v,
        ["acbomberpad"] = (b, v) => b.acBomberPadAvailable = v,
        ["rocketpad"] = (b, v) => b.rocketPadAvailable = v,
        ["platform"] = (b, v) => b.platformAvailable = v,
        ["shield"] = (b, v) => b.shieldAvailable = v,
        ["microrift"] = (b, v) => b.microRiftAvailable = v,
        ["chronat"] = (b, v) => b.chronatAvailable = v,
        ["airship"] = (b, v) => b.airshipAvailable = v,
        ["bertha"] = (b, v) => b.berthaAvailable = v,
        ["sweeper"] = (b, v) => b.sweeperAvailable = v,
    };

    /// <summary>Read side of <see cref="Setters"/>.
    ///
    /// Some missions GRANT a unit as part of their script - Farsite hands over
    /// the Cannon. Writing the flag back to false every frame stops it being
    /// built, but the game has already added its button to the build strip, and
    /// nothing rebuilds the strip afterwards: the player sees a Cannon button
    /// that does nothing, and switching panes does not clear it. Detecting the
    /// grant is what lets us rebuild the strip once and drop the button.</summary>
    private static readonly Dictionary<string, Func<BuildUnitManager, bool>> Getters = new()
    {
        ["riftlab"] = b => b.riftLabAvailable,
        ["factory"] = b => b.factoryAvailable,
        ["ernportal"] = b => b.ernPortalAvailable,
        ["tower"] = b => b.towerAvailable,
        ["pylon"] = b => b.pylonAvailable,
        ["miner"] = b => b.minerAvailable,
        ["greenarrefinery"] = b => b.greenarRefineryAvailable,
        ["terp"] = b => b.terpAvailable,
        ["porter"] = b => b.porterAvailable,
        ["cannon"] = b => b.cannonAvailable,
        ["mortar"] = b => b.mortarAvailable,
        ["sprayer"] = b => b.sprayerAvailable,
        ["sniper"] = b => b.sniperAvailable,
        ["missilelauncher"] = b => b.missileLauncherAvailable,
        ["nullifier"] = b => b.nullifierAvailable,
        ["runway"] = b => b.runwayAvailable,
        ["bomberpad"] = b => b.bomberPadAvailable,
        ["acbomberpad"] = b => b.acBomberPadAvailable,
        ["rocketpad"] = b => b.rocketPadAvailable,
        ["platform"] = b => b.platformAvailable,
        ["shield"] = b => b.shieldAvailable,
        ["microrift"] = b => b.microRiftAvailable,
        ["chronat"] = b => b.chronatAvailable,
        ["airship"] = b => b.airshipAvailable,
        ["bertha"] = b => b.berthaAvailable,
        ["sweeper"] = b => b.sweeperAvailable,
    };

    private IntPtr _lastGameSpace = IntPtr.Zero;
    private bool _paneHidden;
    private int _revealCountdown = -1;
    private int _revealPhase;
    private int _revealWait;
    private int _revealAttempts;
    private int _lastItemCount = -1;
    private int _grantRefreshCooldown;

    private HashSet<string> Allowed => UnitRules.AllowedUnits(ModCore.Client.State);

    /// <summary>Is this build-pane key one the gate knows how to control?
    ///
    /// UnitGrantPatch needs it to tell "a locked unit" from "a key this mod has
    /// no opinion about", and only the first should ever be refused.</summary>
    internal static bool IsModelledUnit(string key) => Setters.ContainsKey(key);

    public void OnSceneEnter(string scene)
    {
        if (scene == "Game")
        {
            // Beat the resume-from-save render: hide panes before GameSpace exists.
            foreach (var pn in GameUtil.AllPanes(true))
                pn.gameObject.SetActive(false);
            _paneHidden = true;
            _revealCountdown = -1;
        }
    }

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;
        var bum = gs.buildUnitManager;
        if (bum == null) return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            ModCore.Log.LogInfo($"New GameSpace - enforcing units: {string.Join(",", Allowed)}");
            _revealCountdown = _paneHidden ? 30 : -1;
            CaptureBaseLimits(bum);
            // Baseline the item count so the initial reveal (not a mid-mission
            // receipt) owns the first paint.
            _lastItemCount = ModCore.Client.State.ReceivedItems.Count;
        }

        var allowed = Allowed;
        bool overrodeGrant = false;
        foreach (var kv in Setters)
        {
            bool want = allowed.Contains(kv.Key);
            // A locked unit reading true means something else turned it on this
            // frame - a mission script granting it. Note that BEFORE writing
            // false, because after the write there is nothing left to see.
            if (!want && Getters.TryGetValue(kv.Key, out var read))
            {
                bool live = false;
                try { live = read(bum); } catch { }
                if (live)
                {
                    overrodeGrant = true;
                    ModCore.Log.LogInfo($"UNITGATE: the mission granted '{kv.Key}', which is not unlocked - refusing it and rebuilding the strip");
                }
            }
            kv.Value(bum, want);
        }
        ApplyLimits(bum);

        // Rebuilding the strip is the only way to remove a button the game has
        // already added. Throttled, because a mission that re-grants every
        // frame would otherwise refresh every frame.
        if (_grantRefreshCooldown > 0) _grantRefreshCooldown--;
        if (overrodeGrant && _grantRefreshCooldown == 0)
        {
            RefreshPane();
            _grantRefreshCooldown = 60;
        }

        // Live unlock: when the received-item count changes mid-mission, rebuild
        // the pane so a newly allowed unit appears immediately.
        var itemCount = ModCore.Client.State.ReceivedItems.Count;
        if (itemCount != _lastItemCount)
        {
            _lastItemCount = itemCount;
            RefreshPane();
        }

        if (!_paneHidden)
        {
            var early = GameUtil.AllPanes(false);
            if (early.Count > 0)
            {
                foreach (var pn in early) pn.gameObject.SetActive(false);
                _paneHidden = true;
                _revealCountdown = 30;
            }
        }
        else if (_revealCountdown > 0 && --_revealCountdown == 0)
        {
            _revealPhase = 1; _revealAttempts = 0;
        }

        RunReveal();
    }

    // Mission-default build limits, captured once per mission so "+1" items
    // apply as base+increment rather than accumulating every frame.
    private readonly Dictionary<string, int> _baseLimits = new();

    private void CaptureBaseLimits(BuildUnitManager bum)
    {
        _baseLimits.Clear();
        foreach (var key in UnitRules.ItemToUnit.Values)
        {
            try { _baseLimits[key] = bum.GetBuildCountLimit(key); } catch { }
        }
        foreach (var key in UnitRules.AlwaysAvailable)
        {
            try { _baseLimits[key] = bum.GetBuildCountLimit(key); } catch { }
        }
    }

    private void ApplyLimits(BuildUnitManager bum)
    {
        foreach (var kv in UnitRules.LimitIncrements(ModCore.Client.State))
        {
            if (!_baseLimits.TryGetValue(kv.Key, out var baseLimit))
                continue;
            // A negative base is the game's "unlimited" sentinel - a +N to
            // unlimited is meaningless, and setting a concrete value would
            // actually CAP a unit that had no cap. Leave those alone.
            //
            // This turns out to be EVERY building: all of them start unlimited, so
            // this branch always taken is why Build Limit items did nothing and are
            // no longer generated. The guard stays right as written - it is the
            // item that was wrong, not the refusal.
            if (baseLimit < 0)
                continue;
            try { bum.SetBuildCountLimit(kv.Key, baseLimit + kv.Value); } catch { }
        }
    }

    private bool RefreshPane()
    {
        var lp = GameUtil.FindLeftPane();
        if (lp == null) return false;
        GameUtil.NativeRefresh(lp, false);
        GameUtil.ResyncStrip(lp);
        return true;
    }

    private void RunReveal()
    {
        if (_revealPhase == 0) return;
        if (_revealWait > 0) { _revealWait--; return; }
        var lp = GameUtil.FindLeftPane();
        if (lp == null) return;

        switch (_revealPhase)
        {
            case 1:
                foreach (var pn in GameUtil.AllPanes(true)) pn.gameObject.SetActive(true);
                _revealPhase = 2; _revealWait = 5; break;
            case 2:
                GameUtil.NativeRefresh(lp, true);
                _revealPhase = 3; _revealWait = 3; break;
            case 3:
                if (lp.weaponTab != null) lp.weaponTab.isOn = false;
                if (lp.airTab != null) lp.airTab.isOn = false;
                if (lp.specialTab != null) lp.specialTab.isOn = false;
                if (lp.customTab != null) lp.customTab.isOn = false;
                if (lp.structTab != null) lp.structTab.isOn = false;
                _revealPhase = 4; _revealWait = 3; break;
            case 4:
                if (lp.structTab != null) lp.structTab.isOn = true;
                _revealPhase = 5; _revealWait = 3; break;
            case 5:
                GameUtil.ResyncStrip(lp, lp.structUnitBuildPane);
                _revealPhase = 6; _revealWait = 5; break;
            case 6:
                bool ok = false;
                var sp = lp.structUnitBuildPane;
                if (sp != null)
                {
                    try
                    {
                        var btns = sp.GetBuildButtons();
                        ok = sp.gameObject.activeSelf && btns != null && btns.Length > 0;
                    }
                    catch { }
                }
                if (ok) { ModCore.Log.LogInfo($"REVEAL OK (attempt {_revealAttempts + 1})"); _revealPhase = 0; }
                else if (++_revealAttempts < 5) { _revealPhase = 1; _revealWait = 10; }
                else { ModCore.Log.LogError("REVEAL FAILED after 5 attempts"); _revealPhase = 0; }
                break;
        }
    }
}

/// <summary>Refuse a grant at source instead of undoing it afterwards.
///
/// Some missions hand a unit over as part of their script - Farsite gives the
/// Cannon, Not My Mars the Miner. Writing the availability flag back to false
/// every frame stops the unit being BUILT, but by then the game has already
/// added its button to the build strip and nothing takes it away again: the
/// player gets a button that does nothing, and switching panes does not clear
/// it. UnitGate could rebuild the strip to remove it, which works but is a
/// repair of a state that should never have existed.
///
/// BuildUnitManager.SetAvailable(string, bool) is the string-keyed entry point,
/// which is what a mission script goes through. Refusing there means the flag
/// never turns true, so no button is ever created and there is nothing to
/// rebuild.
///
/// Only ever refuses a grant (value true) of a unit this slot has not unlocked;
/// a mission turning something OFF is always honoured, and so is any grant of a
/// unit the player legitimately has. If this patch fails to apply, Plugin
/// TryPatch logs it and UnitGate's per-frame enforcement still blocks the
/// build - the button comes back, but the unit stays unbuildable.
/// </summary>
[HarmonyPatch(typeof(BuildUnitManager), nameof(BuildUnitManager.SetAvailable))]
public static class UnitGrantPatch
{
    private static readonly HashSet<string> _unknownKeys = new();

    [HarmonyPrefix]
    public static bool Prefix(string __0, bool __1)
    {
        try
        {
            if (!__1 || string.IsNullOrEmpty(__0))
                return true;                       // never block a revoke
            var key = __0.ToLowerInvariant();
            var allowed = UnitRules.AllowedUnits(ModCore.Client.State);
            if (allowed.Contains(key))
                return true;
            // A key this mod does not model is passed THROUGH, not refused.
            // Refusing anything unrecognised would make the mod the reason a
            // mission could not hand over something the randomizer never had an
            // opinion about - a far worse failure than a stray button, and one
            // that could strand a mission. Logged once per key so an unmodelled
            // grant is visible rather than silent.
            if (!UnitGate.IsModelledUnit(key))
            {
                if (_unknownKeys.Add(key))
                    ModCore.Log.LogWarning($"UNITGATE: mission granted '{key}', which this mod does not model - allowing it");
                return true;
            }
            ModCore.Log.LogInfo($"UNITGATE: refused the mission's grant of '{key}' - not unlocked");
            return false;                          // skip the original
        }
        catch (Exception e)
        {
            // A throwing prefix would take the game's own call down with it.
            ModCore.Log.LogWarning($"UNITGATE: grant check failed: {e.Message}");
            return true;
        }
    }
}
