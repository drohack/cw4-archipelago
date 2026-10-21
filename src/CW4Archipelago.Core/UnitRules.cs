using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>
/// Maps Archipelago item names to the game's BUILD-PANE keys.
///
/// WARNING: these values are NOT the game's unit names, despite looking like
/// them. They are the names of the availability flags on BuildUnitManager
/// ("cannonAvailable" -> "cannon"), which is all UnitGate needs. The game's own
/// unit registry (UnitData.unitConstants, 88 entries) does not contain "pylon",
/// "miner", "porter" or "riftlab" at all, and UnitManager.GetDataName() never
/// returns them.
///
/// So do NOT compare these values against GetDataName() without translating
/// first - that bug silently excluded pylons, miners and ERN portals from every
/// per-unit effect (trap stun, weapon drain, spore targeting) until 2026-08-28.
/// GameUtil.IsPlayerUnit carries the aliases; the table is in
/// docs/research-findings.md under "Unit naming".
/// </summary>
public static class UnitRules
{
    /// <summary>Units the player always has regardless of items. The rift lab
    /// and tower must be available from the start - without them there is no
    /// base and no energy, so gating them would make a mission unstartable.
    /// Pylon is NOT in this set: it is an unlockable item (towers relay on
    /// their own, so the network still functions without it).</summary>
    public static readonly string[] AlwaysAvailable = { "riftlab", "tower" };

    public static readonly IReadOnlyDictionary<string, string> ItemToUnit = new Dictionary<string, string>
    {
        ["Cannon"] = "cannon",
        ["Mortar"] = "mortar",
        ["Nullifier"] = "nullifier",
        ["Miner"] = "miner",
        ["Factory"] = "factory",
        ["Missile Launcher"] = "missilelauncher",
        ["Sprayer"] = "sprayer",
        ["Terp"] = "terp",
        ["ERN Portal"] = "ernportal",
        ["Sniper"] = "sniper",
        ["Porter"] = "porter",
        ["Pylon"] = "pylon",
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

    /// <summary>Units an item unlocks IN ADDITION to its own.
    ///
    /// A greenar refinery with no factory can collect greenar and then has
    /// nowhere to put it - "there's litterlay no reason to have a refinery
    /// without the factory" (designer, 2026-09-03). As two separate items they
    /// were also the campaign's biggest generation hazard: totems on 14
    /// missions need BOTH, as do Chronat, Platform and Rocket Pad, so a lone
    /// one of the pair unlocked nothing at all and Archipelago's fill - which
    /// places one item at a time and cannot see pairs - could strand a seed on
    /// it.
    ///
    /// So Factory now unlocks the refinery too, and "Greenar Refinery" is
    /// retired: the apworld never generates it, and it is not in ItemToUnit, so
    /// one arriving by start_inventory or /send unlocks nothing. Its NAME stays
    /// in the apworld's item table, because item ids are positional and a
    /// removed name would shift every id after it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ItemAlsoUnlocks =
        new Dictionary<string, string[]>
        {
            ["Factory"] = new[] { "greenarrefinery" },
        };

    /// <summary>Unit keys the player may build given the received items.</summary>
    public static HashSet<string> AllowedUnits(SlotState state)
    {
        var set = new HashSet<string>(AlwaysAvailable);
        foreach (var item in state.ReceivedItems)
        {
            if (ItemToUnit.TryGetValue(item, out var key))
                set.Add(key);
            if (ItemAlsoUnlocks.TryGetValue(item, out var extra))
                foreach (var k in extra)
                    set.Add(k);
        }
        return set;
    }
}
