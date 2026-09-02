using CW4Archipelago.Core;
using Xunit;

public class EnergyRulesTests
{
    private static SlotState WithItems(params string[] items)
    {
        var s = new SlotState();
        s.ApplyReceivedItems(items);
        return s;
    }

    private static SlotState Copies(string item, int n)
    {
        var s = new SlotState();
        var all = new string[n];
        for (int i = 0; i < n; i++) all[i] = item;
        s.ApplyReceivedItems(all);
        return s;
    }

    // Both curves are LINEAR AND CAPPED, replacing two shapes that were wrong in
    // opposite directions:
    //
    //   storage    decayed geometrically, converging on step/(1-decay), and was
    //              99 percent spent by copy 21 of 41 - about twenty dead items
    //   generation ramped UP per copy, so the total grew quadratically to 313
    //              energy/sec at 54 copies against a natural 3 to 4/sec
    //
    // The settings are the MAXIMUM and the COPY COUNT THAT REACHES IT, so the
    // per-copy step is derived. That makes a dead copy impossible by
    // construction and matches how the setting is described - "+200 when you
    // have 8" - and it is also why the step cannot be the option: +10 over 8
    // copies is 1.25 each, which is not an integer setting.

    [Fact]
    public void NoUpgrades_NoBonus()
    {
        var s = new SlotState();
        Assert.Equal(0f, EnergyRules.StorageBonus(s, 200, 8));
        Assert.Equal(0f, EnergyRules.GenerationBonus(s, 10, 8));
    }

    [Fact]
    public void Storage_ReachesTheMaximumOnItsLastCopy()
    {
        // "+200 when you have 8" - so 25 each, exactly on the cap at copy 8.
        Assert.Equal(25f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 1), 200, 8), 3);
        Assert.Equal(100f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 4), 200, 8), 3);
        Assert.Equal(200f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 8), 200, 8), 3);
    }

    [Fact]
    public void Storage_AtTheCeiling_Is36CopiesOf25()
    {
        // The top of the option range: +900 over 36 copies is still 25 each.
        Assert.Equal(25f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 1), 900, 36), 3);
        Assert.Equal(900f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 36), 900, 36), 3);
    }

    [Fact]
    public void Generation_ReachesTheMaximumOnItsLastCopy()
    {
        // "+10 max when you have 8" - 1.25 each, which is why the step is
        // DERIVED rather than configured: 1.25 is not an integer setting.
        Assert.Equal(1.25f, EnergyRules.GenerationBonus(Copies(EnergyRules.GenerationItem, 1), 10, 8), 3);
        Assert.Equal(5f, EnergyRules.GenerationBonus(Copies(EnergyRules.GenerationItem, 4), 10, 8), 3);
        Assert.Equal(10f, EnergyRules.GenerationBonus(Copies(EnergyRules.GenerationItem, 8), 10, 8), 3);
    }

    [Fact]
    public void NeitherRunsPastItsMaximum()
    {
        // A hand-edited yaml, or a seed generated before a cap was lowered.
        Assert.Equal(200f, EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, 99), 200, 8), 3);
        Assert.Equal(10f, EnergyRules.GenerationBonus(Copies(EnergyRules.GenerationItem, 99), 10, 8), 3);
    }

    [Fact]
    public void EveryCopyUpToTheMaximumIsWorthTheSame()
    {
        // The whole point of dropping the decay: under the old curve copy 22
        // onward granted under 0.6 energy each. Now every copy is identical and
        // the count that reaches the cap is the count generated.
        float prev = 0f;
        for (int n = 1; n <= 8; n++)
        {
            var v = EnergyRules.StorageBonus(Copies(EnergyRules.StorageItem, n), 200, 8);
            Assert.Equal(25f, v - prev, 3);
            prev = v;
        }
    }

    [Fact]
    public void UsefulCopies_IsTheConfiguredCount()
    {
        Assert.Equal(8, EnergyRules.UsefulCopies(8));
        Assert.Equal(36, EnergyRules.UsefulCopies(36));
        Assert.Equal(0, EnergyRules.UsefulCopies(0));
    }

    [Fact]
    public void ZeroOrNegativeSettings_AreInertRatherThanHarmful()
    {
        var s = Copies(EnergyRules.StorageItem, 5);
        Assert.Equal(0f, EnergyRules.StorageBonus(s, 0, 8));
        Assert.Equal(0f, EnergyRules.StorageBonus(s, 200, 0));
        Assert.Equal(0f, EnergyRules.StorageBonus(s, -5, 8));
    }

    [Fact]
    public void OtherItems_DoNotCount()
    {
        var s = WithItems("Cannon", "Mission Unlock: Home", EnergyRules.StorageItem);
        Assert.Equal(1, EnergyRules.Count(s, EnergyRules.StorageItem));
        Assert.Equal(0, EnergyRules.Count(s, EnergyRules.GenerationItem));
    }
}
