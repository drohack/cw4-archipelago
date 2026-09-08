using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

public class SlotStateTests
{
    [Fact]
    public void ApplyReceivedItems_IsIdempotent_AndCounts()
    {
        var s = new SlotState();
        int events = 0;
        s.ItemsChanged += () => events++;

        Assert.True(s.ApplyReceivedItems(new[] { "Cannon", "Progressive ERN", "Progressive ERN" }));
        Assert.False(s.ApplyReceivedItems(new[] { "Cannon", "Progressive ERN", "Progressive ERN" }));

        Assert.Equal(1, events);
        Assert.Equal(2, s.Count("Progressive ERN"));
        Assert.True(s.Has("Cannon"));
        Assert.False(s.Has("Mortar"));
    }

    [Fact]
    public void ReceiveItem_AppendsAndRaises()
    {
        var s = new SlotState();
        int events = 0;
        s.ItemsChanged += () => events++;
        s.ReceiveItem("Mortar");
        Assert.True(s.Has("Mortar"));
        Assert.Equal(1, events);
    }

    [Fact]
    public void MarkChecked_Connected_DoesNotQueue()
    {
        var s = new SlotState();
        Assert.True(s.MarkChecked("Home - Totems", connected: true));
        Assert.False(s.MarkChecked("Home - Totems", connected: true));
        Assert.Empty(s.PendingChecks);
        Assert.Contains("Home - Totems", s.CheckedLocations);
    }

    [Fact]
    public void MarkChecked_Offline_QueuesOnce_AndTakeClears()
    {
        var s = new SlotState();
        int events = 0;
        s.LocationsChanged += () => events++;
        s.MarkChecked("Home - Totems", connected: false);
        s.MarkChecked("Home - Collect", connected: false);
        s.MarkChecked("Home - Totems", connected: false);

        Assert.Equal(new List<string> { "Home - Totems", "Home - Collect" }, s.PendingChecks);
        Assert.Equal(2, events);

        var taken = s.TakePendingChecks();
        Assert.Equal(2, taken.Count);
        Assert.Empty(s.PendingChecks);
    }

    // Real location names, deliberately. SlotState treats them as opaque
    // strings so "Home - Totems" tested the same behaviour, but that name
    // stopped existing when objectives became per-instance, and a test reading
    // as the authority on a name it gets wrong is how two harnesses came to
    // assert against locations the world does not have.
    [Fact]
    public void ReconcileChecked_MergesServerSet_AndDropsPending()
    {
        var s = new SlotState();
        s.MarkChecked("Home - Totem 1", connected: false);
        Assert.True(s.ReconcileChecked(new[] { "Home - Totem 1", "Home - Cache 1" }));
        Assert.False(s.ReconcileChecked(new[] { "Home - Totem 1" }));
        Assert.Equal(2, s.CheckedLocations.Count);
        Assert.Empty(s.PendingChecks);
    }

    /// <summary>The server reporting a check this client never made - an admin
    /// /send_location, a !collect, another client on the slot. It has to land in
    /// the checked set (so the map repaints) and be reported as a change exactly
    /// once, which is what ApClient relies on to avoid a persist per no-op.</summary>
    [Fact]
    public void ReconcileChecked_AcceptsALocationThisClientNeverTouched()
    {
        var s = new SlotState();
        Assert.True(s.ReconcileChecked(new[] { "Not My Mars - Cache 1" }));
        Assert.Contains("Not My Mars - Cache 1", s.CheckedLocations);
        Assert.False(s.ReconcileChecked(new[] { "Not My Mars - Cache 1" }));
    }
}
