using CW4Archipelago.Core;
using Xunit;

public class InstanceLocationTests
{
    [Theory]
    [InlineData(2, 0, 1, "Home - Nullify 1")]
    [InlineData(5, 1, 4, "We Know Nothing - Totem 4")]
    [InlineData(1, 4, 2, "Farsite - Cache 2")]
    [InlineData(19, 0, 17, "Founders - Nullify 17")]
    public void InstanceLocation_MatchesTheApworldNames(int mission, int slot, int n, string expected)
    {
        Assert.Equal(expected, MissionRules.InstanceLocation(mission, slot, n));
    }

    [Fact]
    public void CountedObjectives_AreNullifyTotemsAndCollect()
    {
        Assert.True(MissionRules.IsCounted(0));    // Nullify
        Assert.True(MissionRules.IsCounted(1));    // Totems
        Assert.True(MissionRules.IsCounted(4));    // Collect
        Assert.False(MissionRules.IsCounted(2));   // Reclaim is a percentage
        Assert.False(MissionRules.IsCounted(3));   // Hold, unused by the campaign
        Assert.False(MissionRules.IsCounted(5));   // Custom is scripted
    }

    [Fact]
    public void SingleObjectives_KeepTheirTypeName()
    {
        Assert.Equal("We Were Never Alone - Reclaim", MissionRules.ObjectiveLocation(6, 2));
        Assert.Equal("Founders - Custom", MissionRules.ObjectiveLocation(19, 5));
    }
}
