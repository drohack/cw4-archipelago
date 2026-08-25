using System;
using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>Maps Archipelago item names to the game's build-unit keys.</summary>
public static class UnitRules
{
    /// <summary>Units the player always has regardless of items.</summary>
    public static readonly string[] AlwaysAvailable = { "riftlab", "tower", "pylon" };

    public static readonly IReadOnlyDictionary<string, string> ItemToUnit = new Dictionary<string, string>
    {
        ["Cannon"] = "cannon",
        ["Mortar"] = "mortar",
        ["Nullifier"] = "nullifier",
        ["Miner"] = "miner",
        ["Factory"] = "factory",
        ["Greenar Refinery"] = "greenarrefinery",
        ["Missile Launcher"] = "missilelauncher",
        ["Sprayer"] = "sprayer",
        ["Terp"] = "terp",
        ["ERN Portal"] = "ernportal",
        ["Sniper"] = "sniper",
        ["Porter"] = "porter",
        ["Bomber Pad"] = "bomberpad",
        ["Runway"] = "runway",
        ["Shield"] = "shield",
        ["AC Bomber Pad"] = "acbomberpad",
        ["Chronat"] = "chronat",
        ["Microrift"] = "microrift",
        ["Platform"] = "platform",
        ["Rocket Pad"] = "rocketpad",
        ["Airship"] = "airship",
        ["Bertha"] = "bertha",
        ["Sweeper"] = "sweeper",
    };

    private const string LimitPrefix = "Build Limit +1 (";

    /// <summary>"Build Limit +1 (Tower)" -> "tower". Unit display names match ItemToUnit keys
    /// or the always-available units (Tower, Pylon).</summary>
    public static bool TryParseLimitItem(string item, out string unitKey)
    {
        unitKey = "";
        if (!item.StartsWith(LimitPrefix, StringComparison.Ordinal) || !item.EndsWith(")", StringComparison.Ordinal))
            return false;
        var display = item.Substring(LimitPrefix.Length, item.Length - LimitPrefix.Length - 1);
        if (ItemToUnit.TryGetValue(display, out var key))
        {
            unitKey = key;
            return true;
        }
        var lower = display.Replace(" ", "").ToLowerInvariant();
        if (Array.IndexOf(AlwaysAvailable, lower) >= 0)
        {
            unitKey = lower;
            return true;
        }
        return false;
    }

    /// <summary>Unit keys the player may build given the received items.</summary>
    public static HashSet<string> AllowedUnits(SlotState state)
    {
        var set = new HashSet<string>(AlwaysAvailable);
        foreach (var item in state.ReceivedItems)
            if (ItemToUnit.TryGetValue(item, out var key))
                set.Add(key);
        return set;
    }

    /// <summary>Per-unit build-limit increments over the game's defaults.</summary>
    public static Dictionary<string, int> LimitIncrements(SlotState state)
    {
        var limits = new Dictionary<string, int>();
        foreach (var item in state.ReceivedItems)
            if (TryParseLimitItem(item, out var key))
                limits[key] = limits.TryGetValue(key, out var n) ? n + 1 : 1;
        return limits;
    }
}
