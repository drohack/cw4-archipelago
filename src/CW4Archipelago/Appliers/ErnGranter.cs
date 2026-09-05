using System;
using CW4Archipelago.Core;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Spawns ERNs beside the rift lab: N at mission start for N Progressive ERN
/// items held, plus one more whenever an ERN item arrives mid-mission. The
/// grant queue waits for the rift lab (GameSpace.commandBase) to exist.
/// Ported from the probe's proven ern:grant path.
/// </summary>
public sealed class ErnGranter
{
    private IntPtr _lastGameSpace = IntPtr.Zero;
    private int _granted;         // ERNs spawned so far this mission
    private int _grantCountdown;
    private int _lastLoggedTarget = -1;

    // Pull-based: each tick the target is ErnRules.ErnCount (grows as ERN items
    // arrive live); we spawn until granted catches up. No event coupling.
    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _granted = 0;
            _lastLoggedTarget = -1;
        }

        var target = ErnRules.ErnCount(ModCore.Client.State);
        if (target != _lastLoggedTarget)
        {
            _lastLoggedTarget = target;
            ModCore.Log.LogInfo($"ERN: target {target} this mission");
        }
        if (_granted >= target) return;
        if (--_grantCountdown > 0) return;
        _grantCountdown = 30;

        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !GameUtil.IsAlive(cb))
            return;   // wait for the rift lab

        // Sit the ERN on the GROUND beside the lab, not at the lab's own height.
        // Offsetting X and Z while keeping the lab's Y buries the unit wherever
        // the terrain beside the lab is higher - reported from a playthrough on
        // v0.1.6 ("the ERN spawned in the ground again"), and invisible on the
        // flat ground most test missions start on.
        var pos = GameUtil.OnGroundNear(
            cb.transform.position, 4f + 3f * (target - _granted), 6f);
        try
        {
            var u = UnitManager.CreateUnitAtPosition("ern", pos);
            if (u != null)
            {
                _granted++;
                ModCore.Log.LogInfo($"ERN granted near rift lab ({_granted}/{target})");
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"ERN grant failed: {e.Message}"); }
    }
}
