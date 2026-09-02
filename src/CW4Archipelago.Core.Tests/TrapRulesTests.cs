using CW4Archipelago.Core;
using Xunit;

public class TrapRulesTests
{
    [Theory]
    [InlineData("Spore Strike")]
    [InlineData("Spore Scatter")]
    [InlineData("Creeper Surge")]
    [InlineData("Energy Drain")]
    [InlineData("Emitter Overdrive")]
    [InlineData("Unit Stun")]
    [InlineData("Ammo Drain")]
    public void TrapNames_MatchTheApworld(string name) => Assert.True(TrapRules.IsTrap(name));

    [Theory]
    [InlineData("Cannon")]
    [InlineData("Mission Unlock: Home")]
    [InlineData("Progressive Energy Storage")]
    [InlineData("")]
    public void NonTraps_AreNotTraps(string name) => Assert.False(TrapRules.IsTrap(name));

    [Fact]
    public void SevenTraps_Exist() => Assert.Equal(7, TrapRules.All.Count);

    [Fact]
    public void TrapsAppliedMark_StartsAtZeroAndPersistsAcrossReconnect()
    {
        // Reconnecting re-delivers the whole received list. Traps must fire
        // once, so the mark is what stops a reconnect replaying every one.
        var s = new SlotState();
        Assert.Equal(0, s.TrapsApplied);
        s.ApplyReceivedItems(new[] { "Spore Strike", "Cannon" });
        s.TrapsApplied = 2;
        s.ApplyReceivedItems(new[] { "Spore Strike", "Cannon" });
        Assert.Equal(2, s.TrapsApplied);
    }
}
