using System;
using System.IO;
using CW4Archipelago.Core;
using Xunit;

public class SlotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cw4ap-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var store = new SlotStore(_dir);
        var s = new SlotState
        {
            Seed = "AB12",
            Slot = "Droha/CW4",
            Hints = SlotData.FromJson("{\"starter_missions\":[\"story1\"],\"mission_requirements\":{\"story2\":[[\"Cannon\",\"Mortar\"]]},\"ern_per_item\":2}"),
            GoalPending = true,
        };
        s.SetAllLocations(new[] { "Home - Totems", "Home - Collect" });
        s.ApplyReceivedItems(new[] { "Cannon", "Progressive ERN" });
        s.MarkChecked("Home - Totems", connected: false);

        store.Save(s);
        var loaded = store.Load("AB12", "Droha/CW4");

        Assert.NotNull(loaded);
        Assert.Equal("AB12", loaded!.Seed);
        Assert.Equal("Droha/CW4", loaded.Slot);
        Assert.Equal(s.ReceivedItems, loaded.ReceivedItems);
        Assert.Equal(s.AllLocations, loaded.AllLocations);
        Assert.Contains("Home - Totems", loaded.CheckedLocations);
        Assert.Equal(new[] { "Home - Totems" }, loaded.PendingChecks);
        Assert.True(loaded.GoalPending);
        Assert.Equal(2, loaded.Hints.ErnPerItem);
        Assert.Single(loaded.Hints.ForMission("story2"));
    }

    [Fact]
    public void Load_ReturnsNull_WhenMissing()
    {
        var store = new SlotStore(_dir);
        Assert.Null(store.Load("nope", "nobody"));
    }

    [Fact]
    public void PathFor_SanitizesSlotNames()
    {
        var store = new SlotStore(_dir);
        var path = store.PathFor("seed", "a/b:c");
        Assert.DoesNotContain("a/b", path.Replace(_dir, ""));
        Assert.EndsWith(".json", path);
    }
}
