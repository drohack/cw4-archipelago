using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

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
