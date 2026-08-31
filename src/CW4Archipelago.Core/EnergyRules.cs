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
    public const string StorageItem = "Energy Storage Upgrade";
    public const string GenerationItem = "Base Generation Upgrade";

    public static int Count(SlotState state, string item)
    {
        if (state == null) return 0;
        int n = 0;
        foreach (var received in state.ReceivedItems)
            if (string.Equals(received, item, StringComparison.Ordinal))
                n++;
        return n;
    }

    /// <summary>Total storage bonus. Copies have DIMINISHING returns: the first
    /// grants <paramref name="step"/> and each later one grants
    /// <paramref name="decayPercent"/> percent of the one before, so a step of 50
    /// at 80 percent gives 50, 40, 32, 25...</summary>
    public static float StorageBonus(SlotState state, int step, int decayPercent)
    {
        int copies = Count(state, StorageItem);
        if (copies <= 0 || step <= 0) return 0f;

        float decay = Math.Clamp(decayPercent, 1, 100) / 100f;
        float grant = step;
        float total = 0f;
        for (int i = 0; i < copies; i++)
        {
            total += grant;
            grant *= decay;
        }
        return total;
    }

    /// <summary>Total generation bonus, in energy per second. Copies RAMP: the
    /// first grants <paramref name="startTenths"/> tenths and each later one
    /// grants <paramref name="rampTenths"/> tenths more, so 5 and 2 gives
    /// 0.5, 0.7, 0.9...</summary>
    public static float GenerationBonus(SlotState state, int startTenths, int rampTenths)
    {
        int copies = Count(state, GenerationItem);
        if (copies <= 0) return 0f;

        float start = Math.Max(0, startTenths) / 10f;
        float ramp = Math.Max(0, rampTenths) / 10f;
        float total = 0f;
        for (int i = 0; i < copies; i++)
            total += start + i * ramp;
        return total;
    }
}
