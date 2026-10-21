using CW4Archipelago.Core;

/// <summary>
/// ERN upgrade item names and the default maxima, as the tests need them.
///
/// Test-only, so it lives here rather than in the shipped Core: the mod builds
/// an item name inline from RatePrefix / CapPrefix, and reads its maxima from
/// the slot data (ern_rate_max, ern_cap_max), never from a constant.
/// </summary>
internal static class ErnItems
{
    /// <summary>What a full set of CAP copies reaches at the default
    /// ern_cap_max of 200 percent.</summary>
    public const float MaxCapMultiplier = 2f;

    /// <summary>What a full set of RATE copies reaches at the default
    /// ern_rate_max of 400 percent: a slot that normally takes 3600 ticks to
    /// fill takes 900.</summary>
    public const float MaxRateMultiplier = 4f;

    public static string RateItem(string upgrade) => ErnUpgradeRules.RatePrefix + upgrade;
    public static string CapItem(string upgrade) => ErnUpgradeRules.CapPrefix + upgrade;

    /// <summary>Every ERN upgrade item name, as the apworld generates them.</summary>
    public static IEnumerable<string> AllItemNames()
    {
        foreach (var u in ErnUpgradeRules.UpgradeNames) yield return RateItem(u);
        foreach (var u in ErnUpgradeRules.UpgradeNames) yield return CapItem(u);
    }
}
