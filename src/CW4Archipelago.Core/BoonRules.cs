namespace CW4Archipelago.Core;

/// <summary>
/// One-shot BENEFICIAL items, the mirror of TrapRules.
///
/// Why these exist as a category: every other filler kind in the pool has a
/// ceiling. The ERN upgrades stop at four copies, and the two energy upgrades
/// stop at whatever count reaches their configured maximum. Capping them was
/// deliberate - a copy that does nothing is the defect that got build limits
/// pulled - but it means no cumulative item can absorb the leftover slots.
///
/// A one-shot effect can. Each firing is independent, so the tenth copy is
/// worth exactly what the first was, for the same reason a trap does not
/// saturate. That makes it the only honest shape for a padding item.
///
/// Fired through TrapApplier, which already walks the received list once with a
/// persisted counter - traps and boons need identical "exactly once, and not
/// again on reconnect" semantics, so they share it rather than having two.
/// </summary>
public static class BoonRules
{
    /// <summary>Fill every player weapon's ammo to full, once.
    ///
    /// Confirmed working in game. Deliberately leaves no lasting change behind,
    /// which is what makes it repeatable without saturating.</summary>
    public const string AmmoResupply = "Ammo Resupply";

    /// <summary>Drop a slug of energy into the rift lab's store, once. Granted
    /// as a FRACTION of the current ceiling rather than a flat number, so it
    /// stays meaningful after storage upgrades raise that ceiling.
    ///
    /// KNOWN WEAKNESS, and the reason it is filler rather than useful: it does
    /// nothing when the store is already full, which in an established base is
    /// most of the time. It pays out when it is most wanted - mid-build, or
    /// after an Energy Drain trap - and is wasted otherwise. That is acceptable
    /// for a one-shot filler and would not be for anything stronger.</summary>
    public const string EnergyCache = "Energy Cache";

    /// <summary>Make every player unit impervious for a while, once.
    ///
    /// Replaced "Field Repair", which set health to full. A health write is not
    /// enough - CW4 has four kill paths and a clamp sees only damage - so this
    /// uses the game's own impervious flag plus a lift of
    /// DESTROY_ON_UNEVEN_TERRAIN, exactly as the dev tools' indestructible
    /// cheat does.
    ///
    /// Map content is never touched: an indestructible map object can make a
    /// mission unwinnable.</summary>
    public const string FieldShield = "Field Shield";

    /// <summary>Top up every player factory with whatever wares it can hold.
    ///
    /// ONE item rather than one per ware, and that is a measured decision. A
    /// factory only holds wares the MISSION gives it a channel for: on story2
    /// liftic had channel 2 while bluite, redon and greenar had none at all. So
    /// a per-ware item would be structurally dead on most maps, for a reason
    /// the player can neither see nor act on - which is much worse than the
    /// accepted "you have not built a factory" whiff.
    ///
    /// Verified in game: stacks (10 -> 20 -> 30), clamps at Factory.MAX_WARES
    /// of 360, reports when already full, and whiffs cleanly with no factory
    /// and with no channel.</summary>
    public const string ResourceCache = "Resource Cache";

    /// <summary>Temporary ERN surges - one per upgrade, six names.
    ///
    /// Each grants its upgrade at the game's OWN 100 percent ceiling for one
    /// EFFICIENCY_TIME, with no portal and no docked ERN required. They are the
    /// biggest reason the padding is bearable: ten boon names instead of four
    /// cuts how many copies of any one a player sees to about a third.
    ///
    /// Deliberately capped at 100 percent, so the permanent
    /// "Progressive ERN Efficiency Cap" items remain strictly better - a surge
    /// is a taste of an upgrade, not a substitute for owning it.</summary>
    public const string SurgePrefix = "ERN Surge: ";

    private static string SurgeItem(string upgrade) => SurgePrefix + upgrade;

    private static bool IsSurge(string item)
        => item != null && item.StartsWith(SurgePrefix, System.StringComparison.Ordinal);

    /// <summary>The upgrade index a surge item names, or -1.</summary>
    public static int SurgeIndex(string item)
    {
        if (!IsSurge(item)) return -1;
        var name = item.Substring(SurgePrefix.Length);
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
            if (string.Equals(ErnUpgradeRules.UpgradeNames[i], name,
                              System.StringComparison.Ordinal)) return i;
        return -1;
    }

    public static readonly string[] All = BuildAll();

    private static string[] BuildAll()
    {
        var names = new System.Collections.Generic.List<string>
            { AmmoResupply, EnergyCache, FieldShield, ResourceCache };
        foreach (var u in ErnUpgradeRules.UpgradeNames) names.Add(SurgeItem(u));
        return names.ToArray();
    }

    public static bool IsBoon(string item)
    {
        foreach (var b in All)
            if (string.Equals(b, item, System.StringComparison.Ordinal)) return true;
        return false;
    }
}
