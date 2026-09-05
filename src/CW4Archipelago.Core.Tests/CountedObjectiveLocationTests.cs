using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

namespace CW4Archipelago.Core.Tests;

/// <summary>
/// The contract a counted objective's location name obeys, pinned because
/// breaking it fails SILENTLY.
///
/// LocationWatcher's mission-completion backfill asked ObjectiveLocation for
/// every objective. For Nullify, Totems and Collect that produces a name with
/// no instance number - "Home - Nullify" where the slot holds "Home - Nullify 1"
/// - so the lookup missed, the loop skipped, and the safety net that exists to
/// catch a missed check could only ever fire for the two objectives that are
/// genuinely single checks. Nothing failed; the check simply never arrived.
/// The objective dump had the same bug and reported isLocation=False for every
/// counted objective in the game.
/// </summary>
public class CountedObjectiveLocationTests
{
    private static SlotState HomeState()
    {
        var s = new SlotState();
        s.SetAllLocations(new List<string>
        {
            "Home - Cache 1", "Home - Totem 1", "Home - Totem 2",
            "Home - Nullify 1", "Home - Mission Complete",
        });
        // Held, or every location reads Locked and the colour assertions below
        // measure the mission gate instead of the objective they are about.
        s.ReceiveItem(MissionRules.UnlockItem(2));
        return s;
    }

    [Theory]
    [InlineData(0)]   // Nullify
    [InlineData(1)]   // Totems
    [InlineData(4)]   // Collect
    public void CountedObjectivesAreNumberedPerInstance(int objective)
    {
        Assert.True(MissionRules.IsCounted(objective));
        Assert.NotEqual("", MissionRules.InstanceKind(objective));
    }

    [Theory]
    [InlineData(2)]   // Reclaim
    [InlineData(5)]   // Custom
    public void SingleCheckObjectivesAreNot(int objective)
    {
        Assert.False(MissionRules.IsCounted(objective));
        Assert.Equal("", MissionRules.InstanceKind(objective));
    }

    [Fact]
    public void TheSingleCheckNameIsNotARealLocationForACountedObjective()
    {
        // The exact mistake: "Home - Nullify" looks plausible and is not a
        // location, so every lookup built from it silently misses.
        var state = HomeState();
        foreach (var objective in new[] { 0, 1, 4 })
        {
            var single = MissionRules.ObjectiveLocation(2, objective);
            Assert.DoesNotContain(single, state.AllLocations);
        }
    }

    [Fact]
    public void PerInstanceLookupFindsEveryInstance()
    {
        var state = HomeState();
        Assert.Equal(new[] { "Home - Nullify 1" },
            MissionRules.LocationsForObjective(state, 2, 0));
        Assert.Equal(new[] { "Home - Totem 1", "Home - Totem 2" },
            MissionRules.LocationsForObjective(state, 2, 1));
        Assert.Equal(new[] { "Home - Cache 1" },
            MissionRules.LocationsForObjective(state, 2, 4));
    }

    [Fact]
    public void EveryCountedInstanceIsAKnownLocation()
    {
        // What the backfill needs to be true: everything it would send on
        // mission completion is a location this slot actually has.
        var state = HomeState();
        foreach (var objective in new[] { 0, 1, 4 })
            foreach (var loc in MissionRules.LocationsForObjective(state, 2, objective))
                Assert.True(MissionRules.IsLocation(state, loc), loc);
    }

    [Fact]
    public void TheMarkerGoesGreyOnlyWhenEveryInstanceIsChecked()
    {
        // Why a half-done objective still shows a colour: one totem is not both.
        var state = HomeState();
        state.MarkChecked("Home - Totem 1", connected: true);
        Assert.Equal(TrackerStatus.InLogic,
            TrackerRules.Aggregate(new[]
            {
                TrackerRules.LocationStatus(state, 2, "Home - Totem 1"),
                TrackerRules.LocationStatus(state, 2, "Home - Totem 2"),
            }));

        state.MarkChecked("Home - Totem 2", connected: true);
        Assert.Equal(TrackerStatus.Done,
            TrackerRules.Aggregate(new[]
            {
                TrackerRules.LocationStatus(state, 2, "Home - Totem 1"),
                TrackerRules.LocationStatus(state, 2, "Home - Totem 2"),
            }));
    }
}
