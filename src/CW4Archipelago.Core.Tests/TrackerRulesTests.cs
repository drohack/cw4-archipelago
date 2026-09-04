using CW4Archipelago.Core;
using Xunit;

public class TrackerRulesTests
{
    // FAITHFUL to what the apworld actually sends. rules.requirement_groups
    // documents location entries as COMPLETE - requirements_for_kind folds the
    // mission's own requirements into every location of that mission, except
    // where a per-instance waiver drops them on purpose.
    //
    // The old fixture here listed only "Home - Nullify": [["Nullifier"]] and
    // relied on the mission table being ANDed in separately. That is not the
    // contract, and writing the fixture that way is what kept the AND in
    // LocationStatus looking correct while it silently defeated the waiver on
    // Farsite's free first cache.
    //
    // "Farsite - Cache 1" is that waiver: mission 1 needs a weapon, the first
    // cache does not, so it carries NO entry at all.
    private const string Hints = @"{
        ""starter_missions"": [""story1""],
        ""mission_requirements"": { ""story1"": [[""Cannon"", ""Mortar""]], ""story2"": [[""Cannon"", ""Mortar""]] },
        ""location_requirements"": {
            ""Farsite - Cache 2"": [[""Cannon"", ""Mortar""]],
            ""Home - Nullify"": [[""Nullifier""], [""Cannon"", ""Mortar""]],
            ""Home - Totems"": [[""Cannon"", ""Mortar""]],
            ""Home - Collect"": [[""Cannon"", ""Mortar""]],
            ""Home - Mission Complete"": [[""Cannon"", ""Mortar""]] },
        ""ern_per_item"": 1 }";

    // Casual logic asks for anti-air the design notes call optional, so a check
    // gated only by that is REACHABLE but not promised - the one case that is
    // yellow rather than red. Strict keeps the weapon requirement only.
    private const string CasualHints = @"{
        ""starter_missions"": [""story1""],
        ""mission_requirements"": { ""story2"": [[""Cannon"", ""Mortar""]] },
        ""location_requirements"": {
            ""Home - Totems"": [[""Cannon"", ""Mortar""], [""Sniper"", ""Missile Launcher""]] },
        ""strict_location_requirements"": {
            ""Home - Totems"": [[""Cannon"", ""Mortar""]] },
        ""ern_per_item"": 1 }";

    private static SlotState Make(params string[] items) => Build(Hints, items);

    private static SlotState Casual(params string[] items) => Build(CasualHints, items);

    private static SlotState Build(string hints, string[] items)
    {
        var s = new SlotState { Hints = SlotData.FromJson(hints) };
        s.SetAllLocations(new[]
        {
            "Farsite - Cache 1", "Farsite - Cache 2", "Farsite - Mission Complete",
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

    /// <summary>The regression test for the waiver. Farsite's first cache needs
    /// nothing, so it is GREEN with an empty inventory even though the mission
    /// itself needs a weapon - and its second cache is red at the same moment.
    /// </summary>
    [Fact]
    public void WaivedInstance_IsInLogic_WhileItsSiblingIsUnreachable()
    {
        var s = Make();
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.LocationStatus(s, 1, "Farsite - Cache 1"));
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(s, 1, "Farsite - Cache 2"));
        // One takeable check and one unreachable one is ORANGE, not red: the map
        // must not hide the check that can be taken right now.
        Assert.Equal(TrackerStatus.Partial, TrackerRules.MissionStatus(s, 1));
    }

    /// <summary>No weapon means the check cannot be taken at all, so it is RED.
    /// It used to be yellow, because red was reserved for a locked MISSION and
    /// nothing inside an open one could ever be red however unreachable.
    /// </summary>
    [Fact]
    public void Locked_WhenUnlockedButNoOffense()
    {
        var s = Make("Mission Unlock: Home", "Nullifier");
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.Locked, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void OutOfLogic_WhenOnlyTheSoftTierIsUnmet()
    {
        var s = Casual("Mission Unlock: Home", "Cannon");
        // Reachable (the weapon is held) but casual logic wants anti-air: yellow.
        Assert.Equal(TrackerStatus.OutOfLogic, TrackerRules.LocationStatus(s, 2, "Home - Totems"));

        var armed = Casual("Mission Unlock: Home", "Cannon", "Sniper");
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.LocationStatus(armed, 2, "Home - Totems"));

        var unarmed = Casual("Mission Unlock: Home", "Sniper");
        // No weapon fails the STRICT tier, so red beats yellow.
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(unarmed, 2, "Home - Totems"));
    }

    [Fact]
    public void Partial_WhenSomeObjectivesUnreachable()
    {
        var s = Make("Mission Unlock: Home", "Mortar");
        Assert.Equal(TrackerStatus.InLogic, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(s, 2, "Home - Nullify"));
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

    /// <summary>A finished check stays grey even when what remains cannot be
    /// reached - the mission goes red, the done check does not.</summary>
    [Fact]
    public void Done_SurvivesAnUnreachableRemainder()
    {
        var s = Make("Mission Unlock: Home");
        s.MarkChecked("Home - Totems", true);
        Assert.Equal(TrackerStatus.Done, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
        Assert.Equal(TrackerStatus.Locked, TrackerRules.MissionStatus(s, 2));
    }

    [Fact]
    public void Done_TakesPriorityOverLocked()
    {
        var s = Make();
        s.MarkChecked("Home - Totems", true);
        Assert.Equal(TrackerStatus.Done, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
    }

    /// <summary>The plugin infers a mission's required-objective checks from its
    /// completion, so the slot_data field carrying that table has to survive the
    /// JSON round trip under its real wire name.</summary>
    [Fact]
    public void RequiredObjectives_ParseFromSlotData()
    {
        var d = SlotData.FromJson(@"{
            ""required_objectives"": { ""story1"": [5], ""story2"": [0, 1, 4] } }");
        Assert.Equal(new[] { 5 }, d.RequiredObjectivesFor("story1"));
        Assert.Equal(new[] { 0, 1, 4 }, d.RequiredObjectivesFor("story2"));
        // A seed generated before the table existed must simply switch the
        // inference off, not throw.
        Assert.Empty(SlotData.FromJson(@"{}").RequiredObjectivesFor("story1"));
        Assert.Empty(d.RequiredObjectivesFor("story9"));
    }

    [Fact]
    public void StrictTableAbsent_TreatsTheTierTableAsStrict()
    {
        // A seed generated before the strict table existed. Unmet requirements
        // must still read red rather than silently becoming yellow.
        var s = Make("Mission Unlock: Home");
        Assert.Empty(s.Hints.StrictLocationRequirements);
        Assert.Equal(TrackerStatus.Locked, TrackerRules.LocationStatus(s, 2, "Home - Totems"));
    }
}
