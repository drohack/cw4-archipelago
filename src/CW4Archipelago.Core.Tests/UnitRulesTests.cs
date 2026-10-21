using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

public class UnitRulesTests
{
    [Fact]
    public void AllowedUnits_AlwaysIncludesBaseUnits()
    {
        var s = new SlotState();
        var allowed = UnitRules.AllowedUnits(s);
        // Only the rift lab and tower are free: without a base and without energy
        // a mission cannot be started at all. The pylon is an unlockable item -
        // towers relay on their own.
        Assert.Equal(new HashSet<string> { "riftlab", "tower" }, allowed);
    }

    [Fact]
    public void AllowedUnits_MapsItemsAndIgnoresUnknown()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Cannon", "Mission Unlock: Home", "Progressive ERN", "Bertha" });
        var allowed = UnitRules.AllowedUnits(s);
        Assert.Contains("cannon", allowed);
        Assert.Contains("bertha", allowed);
        // riftlab + tower, plus cannon and bertha.
        Assert.Equal(4, allowed.Count);
    }

    [Fact]
    public void Factory_AlsoUnlocksTheRefinery()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Factory" });
        var allowed = UnitRules.AllowedUnits(s);
        Assert.Contains("factory", allowed);
        Assert.Contains("greenarrefinery", allowed);
    }

    // Retired items are never generated, and the mod does not apply them: one
    // arriving by start_inventory or /send unlocks nothing.
    [Theory]
    [InlineData("Greenar Refinery")]
    [InlineData("Build Limit +1 (Tower)")]
    [InlineData("Build Limit +1 (Cannon)")]
    [InlineData("Build Limit +1 (Mortar)")]
    public void RetiredItems_UnlockNothing(string item)
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { item });
        Assert.Equal(new HashSet<string> { "riftlab", "tower" }, UnitRules.AllowedUnits(s));
    }
}
