using CW4Archipelago.Core;
using Xunit;

public class TrapRulesTests
{
    [Theory]
    [InlineData("Spore Strike Trap")]
    [InlineData("Spore Scatter Trap")]
    [InlineData("Rift Breach Trap")]
    [InlineData("Energy Drain Trap")]
    [InlineData("Emitter Overdrive Trap")]
    [InlineData("Unit Stun Trap")]
    [InlineData("Ammo Drain Trap")]
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
    public void TrapsAppliedMark_IsNotClobberedByAnItemResync()
    {
        // Narrow on purpose: all this shows is that replacing the received list
        // leaves the mark alone. It was called
        // "...PersistsAcrossReconnect" and read as proof of much more, while
        // the field was absent from the store's DTO and the reconnect path
        // built a fresh state that never carried it - so it reset to zero on
        // every connect and every trap fired again.
        // The reconnect and persistence claims are now tested where they
        // actually happen: see SessionReconcileTests.
        var s = new SlotState();
        Assert.Equal(0, s.TrapsApplied);
        s.ApplyReceivedItems(new[] { "Spore Strike Trap", "Cannon" });
        s.TrapsApplied = 2;
        s.ApplyReceivedItems(new[] { "Spore Strike Trap", "Cannon" });
        Assert.Equal(2, s.TrapsApplied);
    }
}
