using System;
using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>
/// The trap items and the effect each one names.
///
/// Every effect is TEMPORARY and RECOVERABLE by design - a trap may sting, but
/// nothing here may make a mission unwinnable. That rule is why terrain
/// deformation was dropped during the feasibility spike: it is permanent.
///
/// Pure C#, so the names stay in step with the apworld under test.
/// </summary>
public static class TrapRules
{
    public const string SporeStrike = "Spore Strike Trap";
    public const string SporeScatter = "Spore Scatter Trap";
    public const string RiftBreach = "Rift Breach Trap";
    public const string EnergyDrain = "Energy Drain Trap";
    public const string EmitterOverdrive = "Emitter Overdrive Trap";
    public const string UnitStun = "Unit Stun Trap";
    public const string AmmoDrain = "Ammo Drain Trap";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SporeStrike, SporeScatter, RiftBreach, EnergyDrain,
        EmitterOverdrive, UnitStun, AmmoDrain,
    };

    public static bool IsTrap(string item) =>
        item != null && Array.IndexOf(((string[])All), item) >= 0;
}
