using System;
using CW4Archipelago.Core;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Applies the energy upgrades to the live mission.
///
/// Both levers are on the rift lab, and they are the ONLY real ones CW4 exposes.
/// GameSpace.energyStore and energyProduction look like the economy and are not:
/// they are summaries the sim recomputes every tick, so writing them moves the
/// HUD and delivers nothing. Measured, with the numbers, in
/// docs/research-findings.md, "Energy: the store is the rift lab's ammo".
///
///   storage    commandBase.MAX_AMMO   the ceiling construction draws from
///   generation commandBase.ammo       topped up each frame
///
/// Storage follows the UnitGate.ApplyLimits pattern: capture the mission's own
/// ceiling once, then set base + bonus every frame, so nothing accumulates frame
/// over frame and the mission's value is restored when the bonus goes away.
///
/// Generation is a grant and is deliberately NOT reverted - a player cannot
/// un-spend energy, and clawing it back mid-mission would read as a bug.
///
/// The GEN readout is left alone on purpose. It is driven by energyProduction,
/// which the sim recomputes on its own cadence: writing it compounds (a bonus of
/// 20 displayed as 601) and faking it is exactly what made the old dev-tools
/// cheat look like it worked while giving no energy at all. The store tells the
/// truth instead.
/// </summary>
public sealed class EnergyGranter
{
    private IntPtr _lastGameSpace = IntPtr.Zero;
    private float _baseMaxAmmo = -1f;

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;

        // Drop the snapshot on mission change: restoring one mission's ceiling
        // into another would be worse than not restoring at all.
        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _baseMaxAmmo = -1f;
        }

        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !GameUtil.IsAlive(cb))
            return;

        var state = ModCore.Client.State;
        var slot = state.Hints;

        float storage = EnergyRules.StorageBonus(state, slot.EnergyStorageMax, slot.EnergyStorageCopies);
        float perSecond = EnergyRules.GenerationBonus(state, slot.BaseGenerationMax, slot.BaseGenerationCopies);

        try
        {
            if (_baseMaxAmmo < 0f)
                _baseMaxAmmo = cb.MAX_AMMO;

            float want = _baseMaxAmmo + storage;
            if (Math.Abs(cb.MAX_AMMO - want) > 0.01f)
                cb.MAX_AMMO = want;

            if (perSecond > 0f)
                cb.ammo = Mathf.Min(cb.ammo + perSecond * Time.deltaTime, cb.MAX_AMMO);
        }
        catch { /* mission tearing down */ }
    }
}
