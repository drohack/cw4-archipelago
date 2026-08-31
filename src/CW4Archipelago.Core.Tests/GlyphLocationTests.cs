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

    /// <summary>Farsite is the mission the map gets wrong: the game draws a
    /// Totems icon, the mission has no totems, and its real checks are two
    /// caches plus a custom objective.</summary>
    private static SlotState Farsite()
    {
        var s = new SlotState();
        s.AllLocations.Add("Farsite - Cache 1");
        s.AllLocations.Add("Farsite - Cache 2");
        s.AllLocations.Add("Farsite - Custom");
        return s;
    }

    [Fact]
    public void ExpectedIndices_AreTheSlotsWithChecks()
    {
        // Collect (4) and Custom (5) - and NOT Totems (1), which is the icon the
        // map actually draws for this mission.
        Assert.Equal(new[] { 4, 5 }, MissionRules.ExpectedObjectiveIndices(Farsite(), 1));
    }

    [Fact]
    public void ExpectedIndices_MixCountedAndSingleObjectives()
    {
        // Nullify (0) and Collect (4) are counted, Custom (5) is a single check -
        // both naming schemes have to be recognised through the same call. Totems
        // is absent because this fixture lists none, which is the point: the
        // answer follows the LOCATIONS, not the objective types the map draws.
        Assert.Equal(new[] { 0, 4, 5 }, MissionRules.ExpectedObjectiveIndices(WithFounders(), 19));
    }

    [Fact]
    public void ExpectedIndices_AreAscending()
    {
        // The map lays its icons out left to right in this order, so the order
        // this returns IS the on-screen order.
        var got = MissionRules.ExpectedObjectiveIndices(WithFounders(), 19);
        Assert.Equal(got.OrderBy(i => i).ToList(), got);
    }

    [Fact]
    public void ExpectedIndices_FallBackToTheMissionWhenNothingIsKnown()
    {
        // This test used to assert EMPTY here, and that was the bug: with no
        // server there are no locations, so the map kept vanilla's icon set - a
        // totems icon on Farsite, which has no totems - every time the game was
        // opened unconnected. Which objectives are CHECKS is a per-seed question;
        // which objectives a mission HAS is not, so it falls back to the measured
        // authored set. See MissionObjectivesTests.
        Assert.Equal(new[] { 0, 1, 4, 5 },
            MissionRules.ExpectedObjectiveIndices(new SlotState(), 19));
    }

    [Fact]
    public void ExpectedIndices_IgnoreAnotherMissionsLocations()
    {
        var s = Farsite();
        s.AllLocations.Add("Home - Totem 1");
        s.AllLocations.Add("Home - Nullify 1");
        Assert.Equal(new[] { 4, 5 }, MissionRules.ExpectedObjectiveIndices(s, 1));
        Assert.Equal(new[] { 0, 1 }, MissionRules.ExpectedObjectiveIndices(s, 2));
    }
}
