using CW4Archipelago.Core;
using Xunit;

public class TrackerRulesTests
{
    private const string Hints = @"{
        ""starter_missions"": [""story1""],
        ""mission_requirements"": { ""story1"": [], ""story2"": [[""Cannon"", ""Mortar""]] },
        ""location_requirements"": { ""Home - Nullify"": [[""Nullifier""]] },
        ""ern_per_item"": 1 }";

    private static SlotState Make(params string[] items)
    {
        var s = new SlotState { Hints = SlotData.FromJson(Hints) };
        s.SetAllLocations(new[]
        {
            "Farsite - Custom", "Farsite - Mission Complete",
            "Home - Nullify", "Home - Totems", "Home - Collect", "Home - Mission Complete",
        });
        s.ApplyReceivedItems(items);
        return s;
    }

    [Fact]
    public void Locked_WhenUnlockMissing()
    {
        var s = Make("Cannon", "Nullifier");
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.Locked, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void Starter_InLogic_WithNothing()
    {
        var s = Make();
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.LocationStatus(s, 1, "Farsite - Custom"));
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.MissionStatus(s, 1));
    }

    [Fact]
    public void OutOfLogic_WhenUnlockedButNoOffense()
    {
        var s = Make("Mission Unlock: Home", "Nullifier");
        Assert.Equal(TrackerStatus.OutOfLogic, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.OutOfLogic, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void Partial_WhenSomeObjectivesOutOfLogic()
    {
        var s = Make("Mission Unlock: Home", "Mortar");
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.OutOfLogic, TrackerRules.LocationStatus(s, 2, "Home - Nullify"));
        Assert.Equal(TrackerStatus.Partial, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void InLogic_WhenEverythingHeld()
    {
        var s = Make("Mission Unlock: Home", "Cannon", "Nullifier");
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void Done_WhenAllChecked_AndDoneChecksIgnoredForRemaining()
    {
        var s = Make("Mission Unlock: Home", "Cannon");
        s.MarkChecked("Home - Nullify", true);
        Assert.Equal(TrackerStatus.Done, TrackerRules.LocationStatus(s, 2, "Home - Nullify"));
        // remaining (Totems, Collect, Complete) are all in logic -> green, not orange
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.MissionStatus(s, 2));

        foreach (var l in MissionRules.LocationsFor(s, 2))
            s.MarkChecked(l, true);
        Assert.Equal(TrackerStatus.Done, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void Done_TakesPriorityOverLocked()
    {
        var s = Make();
        s.MarkChecked("Home - Totems", true);
        Assert.Equal(TrackerStatus.Done, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
    }
}
