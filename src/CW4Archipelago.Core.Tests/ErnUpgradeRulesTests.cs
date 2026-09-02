using System.Linq;
using CW4Archipelago.Core;
using Xunit;

public class ErnUpgradeRulesTests
{
    private static SlotState WithItems(params string[] items)
    {
        var s = new SlotState();
        s.ApplyReceivedItems(items);
        return s;
    }

    private static string[] Copies(string item, int n) =>
        Enumerable.Repeat(item, n).ToArray();

    private const int FireRange = 4;      // index into ErnUpgradeRules.UpgradeNames

    [Fact]
    public void NoItems_IsExactlyTheGamesOwnBehaviour()
    {
        // 1.0 rather than 0: these are MULTIPLIERS, and a zero here would stop
        // the ERN system working at all for a player holding no ERN items.
        var s = new SlotState();
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
        {
            Assert.Equal(1f, ErnUpgradeRules.RateMultiplier(s, i));
            Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(s, i));
        }
    }

    [Fact]
    public void EachCopyAddsAQuarter()
    {
        var item = ErnUpgradeRules.CapItem("Fire Range");
        Assert.Equal(1.25f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 1)), FireRange), 3);
        Assert.Equal(1.50f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 2)), FireRange), 3);
        Assert.Equal(1.75f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 3)), FireRange), 3);
        Assert.Equal(2.00f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 4)), FireRange), 3);
    }

    [Fact]
    public void AFifthCopyChangesNothing()
    {
        // The whole reason the cap lives in the rules: the pool must not generate
        // a copy that cannot do anything, which is what build limits were.
        var item = ErnUpgradeRules.CapItem("Fire Range");
        var four = ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 4)), FireRange);
        var nine = ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 9)), FireRange);
        Assert.Equal(four, nine, 3);
        Assert.Equal(ErnUpgradeRules.MaxMultiplier, nine, 3);
    }

    [Fact]
    public void ChargeAndBoostAreIndependent()
    {
        // Two separate items on the same upgrade. Holding one must not move the
        // other, or "fills faster" and "reaches higher" stop being distinct.
        var s = WithItems(Copies(ErnUpgradeRules.RateItem("Fire Range"), 4));
        Assert.Equal(ErnUpgradeRules.MaxRateMultiplier, ErnUpgradeRules.RateMultiplier(s, FireRange), 3);
        Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(s, FireRange), 3);
    }

    [Fact]
    public void ItemsOnlyAffectTheirOwnUpgrade()
    {
        var s = WithItems(Copies(ErnUpgradeRules.CapItem("Fire Range"), 4));
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
        {
            var expected = i == FireRange ? 2f : 1f;
            Assert.Equal(expected, ErnUpgradeRules.EfficiencyCap(s, i), 3);
        }
    }

    [Fact]
    public void IndexFourIsFireRange()
    {
        // Pins the order the applier maps onto the game's UPGRADE_* constants.
        Assert.Equal("Fire Range", ErnUpgradeRules.UpgradeNames[FireRange]);
        Assert.Equal(6, ErnUpgradeRules.UpgradeNames.Length);
    }

    [Fact]
    public void TwelveItemNames_SixPerKind_AllDistinct()
    {
        var all = ErnUpgradeRules.AllItemNames().ToList();
        Assert.Equal(12, all.Count);
        Assert.Equal(12, all.Distinct().Count());
        Assert.Equal(6, all.Count(n => n.StartsWith(ErnUpgradeRules.RatePrefix)));
        Assert.Equal(6, all.Count(n => n.StartsWith(ErnUpgradeRules.CapPrefix)));
    }

    [Fact]
    public void OutOfRangeIndexIsInert()
    {
        var s = WithItems(Copies(ErnUpgradeRules.CapItem("Fire Range"), 4));
        Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(s, -1));
        Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(s, 99));
        Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(null!, 0));
    }

    private const int BuildSpeed = 2;     // index into ErnUpgradeRules.UpgradeNames

    [Fact]
    public void IndexTwoIsBuildSpeed()
    {
        Assert.Equal("Build Speed", ErnUpgradeRules.UpgradeNames[BuildSpeed]);
    }

    [Fact]
    public void BuildSpeedHasALowerCeilingThanTheRest()
    {
        // Not a style preference - a measured balance fix. The game shortens
        // build time roughly linearly in efficiency (363 / 186 / 33 ticks at
        // 0 / 100 / 200 percent), so a 2.0 ceiling makes construction about 11x
        // base and dwarfs every other upgrade. The designer's target is double
        // the 100 percent rate.
        var build = ErnUpgradeRules.CeilingFor(BuildSpeed, 200, 150);
        Assert.True(build < ErnUpgradeRules.MaxMultiplier,
            $"Build Speed ceiling {build} should be below the usual {ErnUpgradeRules.MaxMultiplier}");
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
        {
            if (i == BuildSpeed) continue;
            Assert.Equal(ErnUpgradeRules.MaxMultiplier, ErnUpgradeRules.CeilingFor(i, 200, 150), 3);
        }
    }

    [Fact]
    public void TheLastCopyAlwaysLandsExactlyOnTheCeiling()
    {
        // The per-copy step is derived from the ceiling rather than fixed at
        // 0.25, so a lower ceiling means smaller steps and NOT a wasted copy.
        // A copy that changes nothing is the exact failure that got build
        // limits removed from the pool.
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
        {
            var item = ErnUpgradeRules.CapItem(ErnUpgradeRules.UpgradeNames[i]);
            var full = WithItems(Copies(item, ErnUpgradeRules.MaxCopies));
            Assert.Equal(ErnUpgradeRules.CeilingFor(i, 200, 150), ErnUpgradeRules.EfficiencyCap(full, i), 3);

            // and every copy up to it must move the number
            float prev = 1f;
            for (int c = 1; c <= ErnUpgradeRules.MaxCopies; c++)
            {
                var v = ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, c)), i);
                Assert.True(v > prev, $"copy {c} of {item} changed nothing ({prev} -> {v})");
                prev = v;
            }
        }
    }

    [Fact]
    public void BuildSpeedCeilingIsTheMeasuredValue()
    {
        // Pinned to the swept number so a future "tidy the constants" pass
        // cannot quietly restore the 11x behaviour. 1.5 measured 99 ticks
        // against a 186-tick reference; 1.8 and 2.0 both measured 33.
        Assert.Equal(1.50f, ErnUpgradeRules.CeilingFor(BuildSpeed, 200, 150), 3);

        // +12.5 percent per copy, four copies, landing exactly on the ceiling.
        var item = ErnUpgradeRules.CapItem("Build Speed");
        Assert.Equal(1.125f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 1)), BuildSpeed), 3);
        Assert.Equal(1.250f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 2)), BuildSpeed), 3);
        Assert.Equal(1.375f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 3)), BuildSpeed), 3);
        Assert.Equal(1.500f, ErnUpgradeRules.EfficiencyCap(WithItems(Copies(item, 4)), BuildSpeed), 3);
    }

    [Fact]
    public void FourRateCopiesAreFourTimesTheFillSpeed()
    {
        // The rate axis only shortens the WAIT, never the strength, so it is
        // deliberately more generous than the cap: 3600 ticks to fill becomes
        // 900. Every copy must still move the number.
        var item = ErnUpgradeRules.RateItem("Fire Range");
        Assert.Equal(4f, ErnUpgradeRules.MaxRateMultiplier, 3);
        Assert.Equal(1.75f, ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 1)), FireRange), 3);
        Assert.Equal(2.50f, ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 2)), FireRange), 3);
        Assert.Equal(3.25f, ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 3)), FireRange), 3);
        Assert.Equal(4.00f, ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 4)), FireRange), 3);
        // and a fifth is inert, same as the cap
        Assert.Equal(4.00f, ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 9)), FireRange), 3);
    }

    [Fact]
    public void RateIsUniformAcrossUpgrades()
    {
        // Unlike the CAP, which is lower for Build Speed because the game's
        // build-time curve is steep, the rate has no per-upgrade balance
        // problem - waiting less is waiting less.
        for (int i = 0; i < ErnUpgradeRules.UpgradeNames.Length; i++)
        {
            var item = ErnUpgradeRules.RateItem(ErnUpgradeRules.UpgradeNames[i]);
            Assert.Equal(ErnUpgradeRules.MaxRateMultiplier,
                ErnUpgradeRules.RateMultiplier(WithItems(Copies(item, 4)), i), 3);
        }
    }

    [Fact]
    public void MagnitudesComeFromTheSlotData()
    {
        // The player configures these in yaml and they arrive in slot_data, so
        // the rules must read them rather than bake them in. Defaults are the
        // measured values.
        var s = WithItems(Copies(ErnUpgradeRules.CapItem("Fire Range"), 4));
        s.Hints.ErnCapMaxPercent = 300;
        Assert.Equal(3f, ErnUpgradeRules.EfficiencyCap(s, FireRange), 3);

        var r = WithItems(Copies(ErnUpgradeRules.RateItem("Fire Range"), 4));
        r.Hints.ErnRateMaxPercent = 800;
        Assert.Equal(8f, ErnUpgradeRules.RateMultiplier(r, FireRange), 3);
    }

    [Fact]
    public void BuildSpeedTakesItsOwnConfiguredCeiling()
    {
        var s = WithItems(Copies(ErnUpgradeRules.CapItem("Build Speed"), 4));
        s.Hints.ErnCapMaxPercent = 200;
        s.Hints.ErnCapMaxBuildSpeedPercent = 150;
        Assert.Equal(1.5f, ErnUpgradeRules.EfficiencyCap(s, BuildSpeed), 3);

        // and the shared value does not leak into it
        s.Hints.ErnCapMaxBuildSpeedPercent = 175;
        Assert.Equal(1.75f, ErnUpgradeRules.EfficiencyCap(s, BuildSpeed), 3);
    }

    [Fact]
    public void AMagnitudeBelowTheGamesOwnIsClampedAway()
    {
        // A ceiling under 100 percent would make the item a PENALTY, and a rate
        // under 100 percent would SLOW the ramp. A filler item may do nothing;
        // it may not do harm.
        var s = WithItems(Copies(ErnUpgradeRules.CapItem("Fire Range"), 4));
        s.Hints.ErnCapMaxPercent = 25;
        Assert.Equal(1f, ErnUpgradeRules.EfficiencyCap(s, FireRange), 3);

        var r = WithItems(Copies(ErnUpgradeRules.RateItem("Fire Range"), 4));
        r.Hints.ErnRateMaxPercent = 10;
        Assert.Equal(1f, ErnUpgradeRules.RateMultiplier(r, FireRange), 3);
    }

    [Fact]
    public void BuildSpeedIndexMatchesTheNameTable()
    {
        Assert.Equal("Build Speed", ErnUpgradeRules.UpgradeNames[ErnUpgradeRules.BuildSpeedIndex]);
    }
}
