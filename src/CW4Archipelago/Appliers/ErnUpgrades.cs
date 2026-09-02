using System;
using System.Collections.Generic;
using CW4Archipelago.Core;
using HarmonyLib;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Applies the two ERN port upgrade items to CW4's own upgrade system.
///
/// The game's system, measured rather than assumed: an ERN port (ERNInterface)
/// has six upgrades. Docking an ERN into a slot starts that upgrade's efficiency
/// ramping from nothing to full over EFFICIENCY_TIME, tracked in dockedTimes[].
/// GetEff(i) reports where it is.
///
///     ERN Efficiency Rate: <upgrade>   fills faster   -> retreat dockedTimes[i]
///     ERN Efficiency Cap:  <upgrade>   higher ceiling -> cap UNCLAMPED elapsed time
///
/// HOW THE FILL RATE WORKS, and the correction that measurement forced. The
/// first version treated dockedTimes[i] as an accumulating counter and added to
/// it each tick. It is not: it is the TICK AT WHICH THE ERN DOCKED, and the game
/// computes
///
///     eff(i) = (GameSpace.tickCount - dockedTimes[i]) / EFFICIENCY_TIME
///
/// which was confirmed in game - efficiency rose 0.219 to 0.494 over 990 ticks,
/// and 990/3600 is 0.275 exactly, while dockedTimes never moved. So adding to it
/// was wrong twice over: the delta it keyed off was always zero, so the code
/// never fired, and had it fired it would have pushed the timestamp FORWARD and
/// made the ramp slower.
///
/// Moving the timestamp BACKWARD is what speeds it up. To fill at multiplier M,
/// the elapsed time must appear M times larger, so each tick the timestamp
/// retreats by (M - 1) ticks. Both sides are in ticks, so there is no unit to
/// guess at.
///
/// WHY THE CEILING IS A POSTFIX: the game clamps efficiency at 1.0 internally,
/// so writing dockedTimes past full cannot exceed 100 percent. Overriding the
/// returned value is the only lever that reaches higher.
///
/// It is an OVERRIDE and no longer a multiply. Multiplying the clamped 0..1
/// ramp steepened it, so a boosted slot reached every level twice as fast and
/// did Charge's job as well as its own. The value now comes from unclamped
/// elapsed time capped at the ceiling, which lengthens the ramp instead - see
/// ErnUpgrades.Effective. The two items are separate again, which is the whole
/// reason there are two.
/// </summary>
public sealed class ErnUpgrades
{
    /// <summary>Per port, the last dockedTimes we saw, and the fractional part of
    /// the extra progress we owe it. Keyed by the port's pointer.</summary>
    private sealed class PortState
    {
        public float[] Carry = new float[ErnUpgradeRules.UpgradeNames.Length];
    }

    private readonly Dictionary<IntPtr, PortState> _ports = new();
    private IntPtr _lastGameSpace = IntPtr.Zero;
    private bool _orderChecked;
    private int _lastTick = -1;

    /// <summary>Per upgrade, the efficiency a boosted slot should report, or -1
    /// for "leave the game's own value alone". Recomputed every tick and read by
    /// the efficiency patches.
    ///
    /// WHY THIS EXISTS - the ceiling used to be a MULTIPLY on the game's
    /// clamped 0..1 ramp, and multiplying a curve steepens it. Measured: an
    /// unboosted slot climbs at 1/3600 per tick, four boosts climbed at 2/3600,
    /// so a boosted slot crossed 100 percent at 1800 ticks instead of 3600 and
    /// topped out at 200 percent at 3600. It reached EVERY level twice as fast,
    /// which is ERN Efficiency Rate's entire job - so the two items were not separate at
    /// all, and Charge had nothing left to sell.
    ///
    /// The fix EXTENDS the ramp instead of steepening it: take elapsed time
    /// UNCLAMPED and cap it at the ceiling. The first 100 percent then arrives
    /// on exactly the game's own schedule and the extra accrues over a further
    /// 3600 ticks per 100 percent. Charge still speeds it up, because Charge
    /// works by retreating dockedTimes and that makes elapsed grow faster - so
    /// the two compose and holding both is what reaches the ceiling quickly.
    ///
    /// Computed here rather than in the patch because the STATIC
    /// ERNInterface.GetEfficiency has no port to read dockedTimes from.</summary>
    public static readonly float[] Effective = new float[ErnUpgradeRules.UpgradeNames.Length];

    /// <summary>Forget every override. Called when a mission changes, so one
    /// mission's ramp cannot leak into the next.</summary>
    public static void ClearEffective()
    {
        for (int i = 0; i < Effective.Length; i++) Effective[i] = -1f;
    }

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;

        // Per-mission reset. ERNInterface state is save data, so carrying one
        // mission's progress into another would both be wrong and leak.
        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _ports.Clear();
            _orderChecked = false;
            _lastTick = -1;
            ClearEffective();
        }

        var state = ModCore.Client.State;
        if (state == null) { ClearEffective(); return; }

        // Recomputed from scratch each tick: a slot that has just been released
        // must stop overriding immediately, and a stale value would keep a
        // released upgrade running at its old ceiling.
        ClearEffective();

        // Sim ticks since the last visit. Ticks, not seconds: dockedTimes and
        // EFFICIENCY_TIME are both in ticks, and a paused game advances neither.
        int tick = 0;
        try { tick = gs.tickCount; } catch { }
        int dt = _lastTick < 0 ? 0 : tick - _lastTick;
        _lastTick = tick;
        if (dt < 0) dt = 0;                       // mission restarted under us

        foreach (var u in gs.units)
        {
            if (u == null) continue;
            ERNInterface? port = null;
            try { port = u.TryCast<ERNInterface>(); } catch { }
            if (port == null) continue;
            if (!_orderChecked) { CheckIndexOrder(); _orderChecked = true; }
            try { Advance(port, state, dt); } catch { /* mission tearing down */ }
            try { ComputeEffective(port, state, tick); } catch { }
        }
    }

    /// <summary>Speed up the ramp on every docked slot of one port.</summary>
    private void Advance(ERNInterface port, SlotState state, int dt)
    {
        if (dt <= 0) return;
        var docked = port.dockedTimes;
        if (docked == null) return;

        if (!_ports.TryGetValue(port.Pointer, out var ps))
        {
            ps = new PortState();
            _ports[port.Pointer] = ps;
        }

        int n = Math.Min(docked.Length, ErnUpgradeRules.UpgradeNames.Length);
        for (int i = 0; i < n; i++)
        {
            // -1 means nothing is docked in that slot; there is no ramp to speed.
            if (docked[i] < 0) { ps.Carry[i] = 0f; continue; }

            float mult = ErnUpgradeRules.RateMultiplier(state, i);
            if (mult <= 1f) continue;

            float retreat = (mult - 1f) * dt + ps.Carry[i];
            int whole = (int)retreat;
            ps.Carry[i] = retreat - whole;
            if (whole <= 0) continue;

            // Backwards: a docked time further in the past reads as more elapsed
            // time, which is what "fills faster" means here. Never below zero -
            // a negative timestamp is not a state the game produces.
            int next = docked[i] - whole;
            docked[i] = next < 0 ? 0 : next;
        }
    }

    /// <summary>Work out what each boosted slot's efficiency should be, from
    /// UNCLAMPED elapsed time capped at the ceiling.
    ///
    /// Only touches slots that actually hold a Boost item: with no items the
    /// override stays -1 and the game's own value passes through untouched,
    /// which keeps vanilla behaviour byte-identical.</summary>
    private static void ComputeEffective(ERNInterface port, SlotState state, int tick)
    {
        var docked = port.dockedTimes;
        if (docked == null) return;

        int effTime;
        try { effTime = ERNInterface.EFFICIENCY_TIME; } catch { return; }
        if (effTime <= 0) return;

        int n = Math.Min(docked.Length, ErnUpgradeRules.UpgradeNames.Length);
        for (int i = 0; i < n; i++)
        {
            if (docked[i] < 0) continue;                 // nothing docked here

            float ceiling = ErnUpgradeRules.EfficiencyCap(state, i);
            if (ceiling <= 1f) continue;                 // no boost held, no override

            // Unclamped on purpose. This is the whole fix: the game stops this
            // at 1.0, and letting it keep growing is what turns a steeper ramp
            // into a longer one.
            float elapsed = (tick - docked[i]) / (float)effTime;
            if (elapsed < 0f) elapsed = 0f;
            float eff = elapsed < ceiling ? elapsed : ceiling;

            // Several ports could hold the same upgrade; the best one wins,
            // matching the game's own "is this upgrade available" behaviour.
            if (eff > Effective[i]) Effective[i] = eff;
        }
    }

    /// <summary>Confirm our name order still matches the game's constants.
    ///
    /// Not paranoia: the items are addressed BY INDEX, so a reordering in a game
    /// update would quietly apply Fire Rate items to Move Speed and every number
    /// would still look plausible.</summary>
    private static void CheckIndexOrder()
    {
        try
        {
            var expect = new (string Name, int Index)[]
            {
                ("Energy Production", ERNInterface.UPGRADE_ENERGY_PRODUCTION),
                ("Mine Production",   ERNInterface.UPGRADE_MINE_PRODUCTION),
                ("Build Speed",       ERNInterface.UPGRADE_BUILD_SPEED),
                ("Move Speed",        ERNInterface.UPGRADE_MOVE_SPEED),
                ("Fire Range",        ERNInterface.UPGRADE_FIRE_RANGE),
                ("Fire Rate",         ERNInterface.UPGRADE_FIRE_RATE),
            };
            foreach (var (name, index) in expect)
            {
                if (!ErnUpgradeRules.IsValidIndex(index)
                    || ErnUpgradeRules.UpgradeNames[index] != name)
                {
                    ModCore.Log.LogError(
                        $"ERN UPGRADE ORDER MISMATCH: the game puts '{name}' at index {index}, " +
                        $"we have '{(ErnUpgradeRules.IsValidIndex(index) ? ErnUpgradeRules.UpgradeNames[index] : "out of range")}'. " +
                        "ERN upgrade items are addressed by index and are now wrong.");
                    return;
                }
            }
            ModCore.Log.LogInfo("ERN upgrade indices match the game's constants");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"ERN index check failed: {e.Message}"); }
    }
}

/// <summary>The one place the ceiling is applied, so the two entry points below
/// cannot drift apart.</summary>
internal static class ErnCeiling
{
    public static void Scale(int index, ref float result)
    {
        try
        {
            if (!ErnUpgradeRules.IsValidIndex(index)) return;

            // NOT a multiply any more - see ErnUpgrades.Effective for why.
            // A multiply steepened the ramp and made Boost do Charge's job;
            // this takes the value computed from unclamped elapsed time.
            float over = ErnUpgrades.Effective[index];
            if (over < 0f) return;                  // no boost held for this upgrade

            // MAX, never a replacement: our value must not be able to drag the
            // game's own efficiency DOWN, whatever the arithmetic does.
            if (over > result) result = over;
        }
        catch { /* never break the sim over a filler item */ }
    }
}

/// <summary>Raises the ceiling an upgrade's efficiency can reach, as the SIM
/// sees it.
///
/// THIS IS THE ONE THAT MATTERS, and patching it was the fix for "the boost
/// raises the number but the cannon's range never moves". ERNInterface exposes
/// efficiency twice:
///
///     GetEff(int)          instance, on one port
///     GetEfficiency(int)   STATIC, the global the units actually read
///
/// Only the instance method was patched at first. Every probe read GetEff, so
/// the ceiling looked like it worked - eff went to 2.0 on demand - while the
/// sim went on calling the untouched static and a cannon's MYRANGE never
/// changed. A measurement that only reads the value it patched proves nothing.
/// </summary>
[HarmonyPatch(typeof(ERNInterface), nameof(ERNInterface.GetEfficiency))]
public static class ErnEfficiencyPatch
{
    [HarmonyPostfix]
    public static void Postfix(int __0, ref float __result) => ErnCeiling.Scale(__0, ref __result);
}

/// <summary>The per-port instance accessor. Patched as well so the port's own
/// UI agrees with what the sim is doing.</summary>
[HarmonyPatch(typeof(ERNInterface), nameof(ERNInterface.GetEff))]
public static class ErnEffPatch
{
    [HarmonyPostfix]
    public static void Postfix(int __0, ref float __result) => ErnCeiling.Scale(__0, ref __result);
}
