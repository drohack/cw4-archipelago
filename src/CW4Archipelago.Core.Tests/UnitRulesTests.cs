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
        s.ApplyReceivedItems(new[] { "Cannon", "Greenar Refinery", "Mission Unlock: Home", "Progressive ERN", "Bertha" });
        var allowed = UnitRules.AllowedUnits(s);
        Assert.Contains("cannon", allowed);
        Assert.Contains("greenarrefinery", allowed);
        Assert.Contains("bertha", allowed);
        // riftlab + tower, plus cannon, greenarrefinery and bertha.
        Assert.Equal(5, allowed.Count);
    }

    [Theory]
    [InlineData("Build Limit +1 (Tower)", "tower")]
    [InlineData("Build Limit +1 (Cannon)", "cannon")]
    [InlineData("Build Limit +1 (Missile Launcher)", "missilelauncher")]
    public void LimitItems_Parse(string item, string expected)
    {
        Assert.True(UnitRules.TryParseLimitItem(item, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("Cannon")]
    [InlineData("Build Limit +1 (Nonsense)")]
    [InlineData("Build Limit +1 (Tower")]
    public void LimitItems_RejectNonLimits(string item)
    {
        Assert.False(UnitRules.TryParseLimitItem(item, out _));
    }

    [Fact]
    public void LimitIncrements_Accumulate()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Build Limit +1 (Tower)", "Build Limit +1 (Tower)", "Build Limit +1 (Cannon)", "Cannon" });
        var limits = UnitRules.LimitIncrements(s);
        Assert.Equal(2, limits["tower"]);
        Assert.Equal(1, limits["cannon"]);
        Assert.Equal(2, limits.Count);
    }
}
