using System;

namespace CW4Archipelago.Core;

/// <summary>
/// Turns received energy upgrades into the two numbers the game actually uses.
///
/// Both levers live on the rift lab, which is the only place CW4 exposes the
/// energy economy (docs/research-findings.md, "Energy: the store is the rift
/// lab's ammo"):
///
///   storage    -> commandBase.MAX_AMMO   the ceiling construction draws from
///   generation -> commandBase.ammo       added per second
///
/// The item names carry no amounts, because item ids must be identical across
/// every yaml. The amounts arrive in slot_data and are passed in here.
///
/// Pure C#, no Unity, so it is unit tested.
/// </summary>
public static class EnergyRules
{
    public const string StorageItem = "Progressive Energy Storage";
    public const string GenerationItem = "Progressive Base Generation";

    public static int Count(SlotState state, string item)
    {
        if (state == null) return 0;
        int n = 0;
        foreach (var received in state.ReceivedItems)
            if (string.Equals(received, item, StringComparison.Ordinal))
                n++;
        return n;
    }

    /// <summary>Total storage bonus, added to the rift lab's MAX_AMMO.
    ///
    /// The two settings are the MAXIMUM and the COPY COUNT THAT REACHES IT, so
    /// the per-copy step is derived rather than configured: each copy is worth
    /// maxTotal/copiesForMax, and the last one lands exactly on the maximum.
    ///
    /// That shape is deliberate and makes a dead copy impossible by
    /// construction. It replaced a geometric decay - each copy worth 80 percent
    /// of the last - which converged on step/(1-decay) and was 99 percent spent
    /// by copy 21 of 41, so about twenty items per seed granted under 0.6
    /// energy each. That is the defect that got build limits pulled from the
    /// pool.
    ///
    /// It also matches how the setting is actually described: "+200 when you
    /// have 8", not "+25 a time" - the same total, but the maximum is the part
    /// a player wants to choose.</summary>
    public static float StorageBonus(SlotState state, int maxTotal, int copiesForMax)
        => Bonus(Count(state, StorageItem), maxTotal, copiesForMax);

    /// <summary>Total generation bonus, in energy per second, added to the rift
    /// lab's ammo each tick.
    ///
    /// Same shape as storage, and capped much lower for scale: CW4's own
    /// production is about 3 to 4 energy/sec, so the default +10 roughly
    /// triples the economy at full stack.
    ///
    /// The old curve ramped UP per copy, growing quadratically to 313
    /// energy/sec at 54 copies - about eighty times the game's own economy. An
    /// item received dozens of times must not have its last copy matter more
    /// than its first.</summary>
    public static float GenerationBonus(SlotState state, int maxTotal, int copiesForMax)
        => Bonus(Count(state, GenerationItem), maxTotal, copiesForMax);

    /// <summary>maxTotal * min(held, copiesForMax) / copiesForMax.
    ///
    /// Integer settings, exact at the cap, and monotonic in between. Nothing
    /// below zero and nothing above the maximum, whatever a hand-edited yaml
    /// asks for.</summary>
    private static float Bonus(int held, int maxTotal, int copiesForMax)
    {
        if (held <= 0 || maxTotal <= 0 || copiesForMax <= 0) return 0f;
        if (held > copiesForMax) held = copiesForMax;
        return (float)maxTotal * held / copiesForMax;
    }

    /// <summary>How many copies are worth generating - which is simply the
    /// count that reaches the maximum. The apworld generates exactly this many,
    /// so no copy is ever inert.</summary>
    public static int UsefulCopies(int copiesForMax)
        => copiesForMax < 0 ? 0 : copiesForMax;
}
