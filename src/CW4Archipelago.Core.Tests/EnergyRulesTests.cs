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

    [Fact]
    public void NoUpgrades_NoBonus()
    {
        var s = new SlotState();
        Assert.Equal(0f, EnergyRules.StorageBonus(s, 50, 80));
        Assert.Equal(0f, EnergyRules.GenerationBonus(s, 5, 2));
    }

    [Fact]
    public void Storage_Diminishes_PerCopy()
    {
        // 50, then 80 percent of each previous: 50, 40, 32.
        var one = WithItems(EnergyRules.StorageItem);
        var three = WithItems(EnergyRules.StorageItem, EnergyRules.StorageItem, EnergyRules.StorageItem);
        Assert.Equal(50f, EnergyRules.StorageBonus(one, 50, 80), 3);
        Assert.Equal(122f, EnergyRules.StorageBonus(three, 50, 80), 3);
    }

    [Fact]
    public void Storage_FlatWhenDecayIsFull()
    {
        var three = WithItems(EnergyRules.StorageItem, EnergyRules.StorageItem, EnergyRules.StorageItem);
        Assert.Equal(150f, EnergyRules.StorageBonus(three, 50, 100), 3);
    }

    [Fact]
    public void Generation_Ramps_PerCopy()
    {
        // Tenths: start 5 (+0.5), ramp 2 (+0.2 more each) -> 0.5, 0.7, 0.9.
        var one = WithItems(EnergyRules.GenerationItem);
        var three = WithItems(EnergyRules.GenerationItem, EnergyRules.GenerationItem, EnergyRules.GenerationItem);
        Assert.Equal(0.5f, EnergyRules.GenerationBonus(one, 5, 2), 3);
        Assert.Equal(2.1f, EnergyRules.GenerationBonus(three, 5, 2), 3);
    }

    [Fact]
    public void Generation_FlatWhenRampIsZero()
    {
        var three = WithItems(EnergyRules.GenerationItem, EnergyRules.GenerationItem, EnergyRules.GenerationItem);
        Assert.Equal(1.5f, EnergyRules.GenerationBonus(three, 5, 0), 3);
    }

    [Fact]
    public void OtherItems_DoNotCount()
    {
        var s = WithItems("Cannon", "Mission Unlock: Home", EnergyRules.StorageItem);
        Assert.Equal(1, EnergyRules.Count(s, EnergyRules.StorageItem));
        Assert.Equal(0, EnergyRules.Count(s, EnergyRules.GenerationItem));
    }
}
