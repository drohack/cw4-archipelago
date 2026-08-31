using CW4Archipelago.Core;
using Xunit;

public class GlyphLocationTests
{
    private static SlotState WithFounders()
    {
        var s = new SlotState();
        // The instance names the apworld actually generates.
        for (int i = 1; i <= 17; i++) s.AllLocations.Add($"Founders - Nullify {i}");
        s.AllLocations.Add("Founders - Cache 1");
        s.AllLocations.Add("Founders - Custom");
        return s;
    }

    [Fact]
    public void CountedObjective_FindsEveryInstance()
    {
        // Regression: the map's glyph colouring asked for "Founders - Nullify",
        // which matched nothing once locations became per-instance, silently
        // disabling the colouring entirely.
        var s = WithFounders();
        Assert.Equal(17, MissionRules.LocationsForObjective(s, 19, 0).Count);   // Nullify
        Assert.Single(MissionRules.LocationsForObjective(s, 19, 4));            // Collect -> Cache 1
    }

    [Fact]
    public void SingleObjective_FindsItsOneLocation()
    {
        var s = WithFounders();
        Assert.Single(MissionRules.LocationsForObjective(s, 19, 5));            // Custom
    }

    [Fact]
    public void UntrackedObjective_FindsNothing()
    {
        var s = WithFounders();
        Assert.Empty(MissionRules.LocationsForObjective(s, 19, 1));             // no totems listed
        Assert.Empty(MissionRules.LocationsForObjective(s, 19, 3));             // Hold, unused
    }

    [Fact]
    public void PrefixMatching_DoesNotBleedBetweenMissions()
    {
        var s = WithFounders();
        s.AllLocations.Add("Home - Nullify 1");
        Assert.Equal(17, MissionRules.LocationsForObjective(s, 19, 0).Count);
        Assert.Single(MissionRules.LocationsForObjective(s, 2, 0));
    }

    [Fact]
    public void Aggregate_FollowsTheTrackerConvention()
    {
        Assert.Equal(TrackerStatus.Done,
            TrackerRules.Aggregate(new[] { TrackerStatus.Done, TrackerStatus.Done }));
        Assert.Equal(TrackerStatus.Locked,
            TrackerRules.Aggregate(new[] { TrackerStatus.InLogic, TrackerStatus.Locked }));
        Assert.Equal(TrackerStatus.InLogic,
            TrackerRules.Aggregate(new[] { TrackerStatus.Done, TrackerStatus.InLogic }));
        Assert.Equal(TrackerStatus.Partial,
            TrackerRules.Aggregate(new[] { TrackerStatus.InLogic, TrackerStatus.OutOfLogic }));
    }
}
