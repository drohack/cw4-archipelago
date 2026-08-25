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
        Assert.Equal(new HashSet<string> { "riftlab", "tower", "pylon" }, allowed);
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
        Assert.Equal(6, allowed.Count);
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

public class MissionRulesTests
{
    [Theory]
    [InlineData("story1", 1)]
    [InlineData("story20", 20)]
    [InlineData("Story7", 7)]
    public void Specifier_RoundTrips(string spec, int mission)
    {
        Assert.True(MissionRules.TryParseSpecifier(spec, out var n));
        Assert.Equal(mission, n);
        Assert.Equal(spec.ToLowerInvariant(), MissionRules.Specifier(n));
    }

    [Theory]
    [InlineData("story0")]
    [InlineData("story21")]
    [InlineData("colony5")]
    [InlineData(null)]
    public void Specifier_RejectsNonStory(string? spec)
    {
        Assert.False(MissionRules.TryParseSpecifier(spec, out _));
    }

    [Fact]
    public void UnlockNames_AndLocationNames()
    {
        Assert.Equal("Mission Unlock: Not My Mars", MissionRules.UnlockItem(3));
        Assert.Equal("Home - Nullify", MissionRules.ObjectiveLocation(2, 0));
        Assert.Equal("Ever After - Custom", MissionRules.ObjectiveLocation(20, 5));
        Assert.Equal("Hints - Mission Complete", MissionRules.MissionCompleteLocation(7));
    }

    [Fact]
    public void Unlock_StarterOrItem()
    {
        var s = new SlotState();
        Assert.True(MissionRules.IsUnlocked(s, 1));
        Assert.False(MissionRules.IsUnlocked(s, 2));
        s.ReceiveItem("Mission Unlock: Home");
        Assert.True(MissionRules.IsUnlocked(s, 2));

        s.Hints = SlotData.FromJson("{\"starter_missions\":[\"story1\",\"story3\"]}");
        Assert.True(MissionRules.IsUnlocked(s, 3));
    }

    [Fact]
    public void LocationsFor_FiltersServerList()
    {
        var s = new SlotState();
        s.SetAllLocations(new[] { "Home - Nullify", "Home - Totems", "Home - Mission Complete", "Hints - Totems" });
        Assert.Equal(3, MissionRules.LocationsFor(s, 2).Count);
        Assert.Single(MissionRules.LocationsFor(s, 7));
        Assert.Empty(MissionRules.LocationsFor(s, 3));
    }
}

public class ErnRulesTests
{
    [Fact]
    public void ErnCount_MultipliesByHint()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Progressive ERN", "Progressive ERN", "Cannon" });
        Assert.Equal(2, ErnRules.ErnCount(s));
        s.Hints = SlotData.FromJson("{\"ern_per_item\":2}");
        Assert.Equal(4, ErnRules.ErnCount(s));
    }
}
