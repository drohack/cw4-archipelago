using System;
using System.Collections.Generic;
using CW4Archipelago.Core;
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

    private IntPtr _lastGameSpace = IntPtr.Zero;
    private bool _paneHidden;
    private int _revealCountdown = -1;
    private int _revealPhase;
    private int _revealWait;
    private int _revealAttempts;
    private int _lastItemCount = -1;

    private HashSet<string> Allowed => UnitRules.AllowedUnits(ModCore.Client.State);

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
        foreach (var kv in Setters)
            kv.Value(bum, allowed.Contains(kv.Key));
        ApplyLimits(bum);

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
