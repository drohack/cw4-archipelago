using System;
using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>
/// The two ERN port upgrade items, as pure arithmetic over received items.
///
/// CW4's ERN port (ERNInterface) has six upgrades. You dock an ERN into a slot
/// and that upgrade's efficiency ramps from nothing to full over EFFICIENCY_TIME.
/// Two Archipelago items act on that, one pair per upgrade:
///
///     ERN Efficiency Rate: Fire Rate    the efficiency FILLS faster
///     ERN Efficiency Cap:  Fire Rate    the efficiency CEILING is higher
///
/// Neither is progressive and neither depends on the other. Both do nothing at
/// all until the player owns a portal and docks an ERN, which is deliberate: the
/// gate is the game's own, so the items need no logic entry and can be pure
/// filler.
///
/// WHY THE CAP LIVES HERE and not at the call site: a fifth copy of an item that
/// is already at 200 percent does nothing, and an item that does nothing is the
/// exact failure that got build limits removed from the pool. Capping in one
/// place means the pool builder and the applier cannot disagree about how many
/// copies are worth generating.
///
/// Pure C#, no Unity, so it is unit tested.
/// </summary>
public static class ErnUpgradeRules
{
    /// <summary>The six upgrades, in the game's own index order.
    ///
    /// The order matters: it is asserted against ERNInterface's static
    /// UPGRADE_* constants at runtime rather than trusted, because a game update
    /// that reorders them would otherwise silently apply Fire Rate items to
    /// Move Speed.</summary>
    public static readonly string[] UpgradeNames =
    {
        "Energy Production",
        "Mine Production",
        "Build Speed",
        "Move Speed",
        "Fire Range",
        "Fire Rate",
    };

    public const string RatePrefix = "ERN Efficiency Rate: ";
    public const string CapPrefix = "ERN Efficiency Cap: ";

    /// <summary>Copies beyond this change nothing, so the pool should not hold
    /// more. Four copies at a quarter each is exactly 200 percent.</summary>
    public const int MaxCopies = 4;
    public const float StepPerCopy = 0.25f;
    public const float MaxMultiplier = 1f + MaxCopies * StepPerCopy;   // 2.0

    /// <summary>What four copies of the RATE item are worth: 4x the game's own
    /// fill speed, so a slot that normally takes 3600 ticks to fill takes 900.
    ///
    /// Higher than the cap's 2.0 on purpose, and safe to be: the rate only
    /// changes how long you WAIT for an upgrade, never how strong it gets, so a
    /// generous value shortens dead time without touching balance. The ceiling
    /// is the axis that changes power, and that is capped much lower.</summary>
    public const float MaxRateMultiplier = 4f;

    /// <summary>The ceiling each upgrade is allowed to reach, BY INDEX.
    ///
    /// Almost every upgrade responds linearly to efficiency, so a 2.0 ceiling
    /// delivers about twice the effect and the number a player reads (200
    /// percent) matches what they get. Measured: Fire Range 9/11/13 cells,
    /// Energy Production +31.25 percent per 100 percent.
    ///
    /// BUILD SPEED DOES NOT. The game shortens build TIME roughly linearly in
    /// efficiency - measured 363 / 186 / 33 ticks at 0 / 100 / 200 percent,
    /// which fits time = base * (1 - 0.4545 * eff) and would reach zero at
    /// about eff 2.2. So feeding it 2.0 makes construction near instant (11x
    /// base), wildly out of line with the +62.5 percent that four copies of
    /// Energy Production buy.
    ///
    /// The designer's target is "double the 100 percent rate" - 93 ticks rather
    /// than 33 - so Build Speed gets a lower ceiling. The value is MEASURED,
    /// not derived from the fit above, and the fit is exactly why: it predicted
    /// 1.64 for 93 ticks, and 1.7 really lands on 54.
    ///
    /// Swept with tools/ern-buildcap-test.sh, every level twice, one session:
    ///
    ///     eff 1.0 -> 186 ticks      the 100 percent reference
    ///     eff 1.4 ->  99
    ///     eff 1.5 ->  99            <- chosen
    ///     eff 1.6 ->  78
    ///     eff 1.7 ->  54
    ///     eff 1.8 ->  33
    ///     eff 2.0 ->  33            the curve floors out here
    ///
    /// The game QUANTIZES build time, so 93 is not attainable: the choice is 99
    /// (1.88x the 100 percent rate) or 78 (2.38x). 99 is nearest the target and
    /// errs on the conservative side. 1.5 also gives a clean +12.5 percent per
    /// copy, and sits on the 1.4-1.5 plateau so it is not sensitive to small
    /// changes.</summary>
    private static readonly float[] CeilingByUpgrade =
    {
        MaxMultiplier,   // 0 Energy Production
        MaxMultiplier,   // 1 Mine Production
        1.50f,           // 2 Build Speed  - measured 99 ticks, 1.88x
        MaxMultiplier,   // 3 Move Speed
        MaxMultiplier,   // 4 Fire Range
        MaxMultiplier,   // 5 Fire Rate
    };

    /// <summary>Test-only override, so one game session can sweep candidate
    /// ceilings instead of costing a rebuild and relaunch per value. Null means
    /// use the table. Never set in normal play.</summary>
    public static float? CeilingOverride;

    public static float CeilingFor(int index)
    {
        if (CeilingOverride.HasValue) return CeilingOverride.Value;
        if (!IsValidIndex(index) || index >= CeilingByUpgrade.Length) return MaxMultiplier;
        return CeilingByUpgrade[index];
    }

    public static string RateItem(string upgrade) => RatePrefix + upgrade;
    public static string CapItem(string upgrade) => CapPrefix + upgrade;

    /// <summary>Every ERN upgrade item name, for the pool and for tests.</summary>
    public static IEnumerable<string> AllItemNames()
    {
        foreach (var u in UpgradeNames) yield return RateItem(u);
        foreach (var u in UpgradeNames) yield return CapItem(u);
    }

    public static bool IsValidIndex(int index) => index >= 0 && index < UpgradeNames.Length;

    /// <summary>How much faster this upgrade's efficiency fills. 1.0 means the
    /// game's own rate, 4.0 is four times as fast.
    ///
    /// Step derived from the maximum, like the cap, so the fourth copy lands
    /// exactly on 4.0 and no copy is ever a no-op: 1.75, 2.5, 3.25, 4.0.</summary>
    public static float RateMultiplier(SlotState state, int index)
    {
        if (state == null || !IsValidIndex(index)) return 1f;
        int copies = state.Count(RatePrefix + UpgradeNames[index]);
        if (copies <= 0) return 1f;
        if (copies > MaxCopies) copies = MaxCopies;
        return 1f + copies * (MaxRateMultiplier - 1f) / MaxCopies;
    }

    /// <summary>What this upgrade's efficiency may reach. 1.0 means the game's
    /// own ceiling.
    ///
    /// The per-copy step is derived from that upgrade's ceiling rather than
    /// fixed at 0.25, so the LAST copy always lands exactly on the ceiling and
    /// no copy is ever a no-op. Build Speed's 1.5 ceiling therefore means four
    /// copies of +12.5 percent instead of four of +25.</summary>
    public static float EfficiencyCap(SlotState state, int index)
    {
        if (state == null || !IsValidIndex(index)) return 1f;
        int copies = state.Count(CapPrefix + UpgradeNames[index]);
        if (copies <= 0) return 1f;
        if (copies > MaxCopies) copies = MaxCopies;
        float ceiling = CeilingFor(index);
        return 1f + copies * (ceiling - 1f) / MaxCopies;
    }

    private static float Multiplier(SlotState state, int index, string prefix)
    {
        if (state == null || !IsValidIndex(index)) return 1f;
        int copies = state.Count(prefix + UpgradeNames[index]);
        if (copies <= 0) return 1f;
        if (copies > MaxCopies) copies = MaxCopies;
        return 1f + copies * StepPerCopy;
    }
}
