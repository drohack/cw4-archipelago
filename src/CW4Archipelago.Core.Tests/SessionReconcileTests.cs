using System;
using System.IO;
using CW4Archipelago.Core;
using Xunit;

namespace CW4Archipelago.Core.Tests;

/// <summary>
/// The connect-time reconcile: what offline progress survives, and what must
/// not cross from one multiworld into another.
///
/// Each of the first three tests below reproduces a defect that was live in
/// v0.1.5. They failed before SessionReconcile existed.
/// </summary>
public class SessionReconcileTests
{
    private static SlotState Live(string seed, string slot)
        => new() { Seed = seed, Slot = slot };

    [Fact]
    public void OfflineChecksAreOwedToTheServerOnConnect()
    {
        // The hard requirement from docs/adding games.md: checks made while
        // disconnected must be sent on connection, not lost, not reverted.
        var live = Live("seedA", "Droha");
        live.MarkChecked("Home - Totem 1", connected: false);

        var state = SessionReconcile.OnConnected(
            live, null, "seedA", "Droha", new SlotData(),
            new[] { "Home - Totem 1", "Home - Totem 2" },
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Contains("Home - Totem 1", state.PendingChecks);
        Assert.Contains("Home - Totem 1", state.CheckedLocations);
    }

    [Fact]
    public void ChecksFromAnotherSeedAreNotReplayedIntoThisOne()
    {
        // The old test was `State.Slot == slot`, with no seed comparison. Since
        // location names are identical across seeds, a check earned in seedA
        // was accepted by seedB's server as a real check there.
        var live = Live("seedA", "Droha");
        live.MarkChecked("Home - Totem 1", connected: false);

        var state = SessionReconcile.OnConnected(
            live, null, "seedB", "Droha", new SlotData(),
            new[] { "Home - Totem 1" },
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Empty(state.PendingChecks);
        Assert.DoesNotContain("Home - Totem 1", state.CheckedLocations);
    }

    [Fact]
    public void AGoalReachedOfflineIsStillOwedOnConnect()
    {
        // CommonClient keeps finished_game precisely "to signal that status
        // should be updated to Goal after reconnecting". Beating the finale
        // while the server was down used to be dropped silently.
        var live = Live("seedA", "Droha");
        live.GoalPending = true;

        var state = SessionReconcile.OnConnected(
            live, null, "seedA", "Droha", new SlotData(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(state.GoalPending);
    }

    [Fact]
    public void TheTrapHighWaterMarkSurvivesAConnect()
    {
        // Connecting re-delivers the whole received list, so a mark that resets
        // to zero re-fires every trap and boon the player has ever been sent.
        var live = Live("seedA", "Droha");
        live.ApplyReceivedItems(new[] { "Spore Strike", "Cannon", "Ammo Cache" });
        live.TrapsApplied = 3;

        var state = SessionReconcile.OnConnected(
            live, null, "seedA", "Droha", new SlotData(),
            Array.Empty<string>(),
            new[] { "Spore Strike", "Cannon", "Ammo Cache" },
            Array.Empty<string>());

        Assert.Equal(3, state.TrapsApplied);
    }

    [Fact]
    public void TheTrapMarkNeverMovesBackwards()
    {
        // Two sources, two marks. Taking the lower one would replay the
        // difference.
        var live = Live("seedA", "Droha");
        live.TrapsApplied = 5;
        var cached = Live("seedA", "Droha");
        cached.TrapsApplied = 2;

        var state = SessionReconcile.OnConnected(
            live, cached, "seedA", "Droha", new SlotData(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(5, state.TrapsApplied);
    }

    [Fact]
    public void TheTrapMarkFromAnotherSeedIsNotCarried()
    {
        var live = Live("seedA", "Droha");
        live.TrapsApplied = 7;

        var state = SessionReconcile.OnConnected(
            live, null, "seedB", "Droha", new SlotData(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(0, state.TrapsApplied);
    }

    [Fact]
    public void WhatTheServerAlreadyKnowsIsNotResent()
    {
        var live = Live("seedA", "Droha");
        live.MarkChecked("Home - Totem 1", connected: false);
        live.MarkChecked("Home - Totem 2", connected: false);

        var state = SessionReconcile.OnConnected(
            live, null, "seedA", "Droha", new SlotData(),
            new[] { "Home - Totem 1", "Home - Totem 2" },
            Array.Empty<string>(),
            new[] { "Home - Totem 1" });

        Assert.Equal(new[] { "Home - Totem 2" }, state.PendingChecks);
        Assert.Contains("Home - Totem 1", state.CheckedLocations);
    }

    [Fact]
    public void CachedOfflineProgressIsOwedAfterAGameRestart()
    {
        // The player quit while disconnected. There is no live state at all on
        // the next launch, so everything has to come off disk.
        var cached = Live("seedA", "Droha");
        cached.MarkChecked("Home - Cache 1", connected: false);
        cached.GoalPending = true;
        cached.TrapsApplied = 4;

        var state = SessionReconcile.OnConnected(
            null, cached, "seedA", "Droha", new SlotData(),
            new[] { "Home - Cache 1" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.Contains("Home - Cache 1", state.PendingChecks);
        Assert.True(state.GoalPending);
        Assert.Equal(4, state.TrapsApplied);
    }

    [Fact]
    public void TheTrapMarkRoundTripsThroughTheStore()
    {
        // The old test for this asserted a property assignment in memory and
        // was named as if it proved persistence. It never touched the store -
        // where the field was in fact missing from the DTO entirely, so it
        // reset to zero on every launch.
        var dir = Path.Combine(Path.GetTempPath(), "cw4-reconcile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SlotStore(dir);
            var s = Live("seedA", "Droha");
            s.ApplyReceivedItems(new[] { "Spore Strike", "Cannon" });
            s.TrapsApplied = 2;
            store.Save(s);

            var loaded = store.Load("seedA", "Droha");
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.TrapsApplied);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
