using System.Collections.Generic;
using System.Linq;
using CW4Archipelago.Core;
using Xunit;

/// <summary>
/// The SPAN Experiments as the plugin sees them: 26 maps that the apworld can
/// mix into a seed, identified by guid rather than by "storyN".
///
/// WHAT THESE PIN, and why each one is a real risk rather than a formality:
///
///   Identity        a mission is resolved from the live map's specifier. If the
///                   plugin cannot turn a guid back into a mission it returns 0,
///                   the watcher's Tick returns immediately, and the map plays
///                   perfectly while sending no checks at all. Silent.
///   Naming          the apworld builds location names from ITS titles and the
///                   plugin sends checks built from THESE. One character of
///                   difference sends a location the server never heard of.
///   Roster          which 20 missions a seed contains. An empty roster (a seed
///                   generated before the key existed) has to mean the campaign,
///                   not "no missions".
///   Gating          a mission the seed does not contain must stay playable. The
///                   26 SPAN maps are the game's own content on a campaign-only
///                   seed and locking them out would be a regression.
/// </summary>
public class SpanMissionTests
{
    [Fact]
    public void TwentySixSpanMissionsNumberedTwentyOneToFortySix()
    {
        Assert.Equal(26, SpanMissionTable.Titles.Count);
        Assert.Equal(21, SpanMissionTable.FirstMission);
        Assert.Equal(Enumerable.Range(21, 26), SpanMissionTable.Titles.Keys.OrderBy(k => k));
        Assert.Equal(SpanMissionTable.Titles.Keys.OrderBy(k => k),
                     SpanMissionTable.Guids.Keys.OrderBy(k => k));
        Assert.Equal(SpanMissionTable.Titles.Keys.OrderBy(k => k),
                     SpanMissionTable.Objectives.Keys.OrderBy(k => k));
    }

    [Fact]
    public void TitlesAreUniqueAcrossBothFamilies()
    {
        // A title is an ID: it is the location-name prefix and the unlock item
        // name. Two missions sharing one would merge their checks silently.
        Assert.Equal(46, MissionRules.Titles.Count);
        Assert.Equal(46, MissionRules.Titles.Values.Distinct().Count());
    }

    [Fact]
    public void SpecifierIsStoryNForCampaignAndTheGuidForSpan()
    {
        Assert.Equal("story1", MissionRules.Specifier(1));
        Assert.Equal("story20", MissionRules.Specifier(20));
        Assert.Equal("knucracker1", MissionRules.Specifier(21));
        Assert.Equal("demobonus4", MissionRules.Specifier(46));
    }

    [Fact]
    public void EverySpecifierRoundTrips()
    {
        foreach (var mission in MissionRules.Titles.Keys)
        {
            Assert.True(MissionRules.TryParseSpecifier(MissionRules.Specifier(mission), out var back),
                        $"mission {mission} did not parse back");
            Assert.Equal(mission, back);
        }
    }

    [Fact]
    public void UnknownSpecifiersStillFailToParse()
    {
        // Colonies, custom maps and the tutorial are not ours. Resolving one of
        // them to a mission would gate content the randomizer has no claim on.
        Assert.False(MissionRules.TryParseSpecifier("story0", out _));
        Assert.False(MissionRules.TryParseSpecifier("story21", out _));
        Assert.False(MissionRules.TryParseSpecifier("colony7", out _));
        Assert.False(MissionRules.TryParseSpecifier("", out _));
        Assert.False(MissionRules.TryParseSpecifier(null, out _));
    }

    [Fact]
    public void SpanMapsNeverDrawAHoldObjective()
    {
        // There is no location for Hold on either side of the wire. Four SPAN
        // maps do have one; they are recorded in span_data.SPAN_NOTES and given
        // no objective here, so the icon and the checks stay in agreement.
        foreach (var entry in SpanMissionTable.Objectives)
            Assert.DoesNotContain(3, entry.Value);
    }

    [Fact]
    public void AnEmptyRosterMeansTheCampaign()
    {
        // A seed generated before the roster key existed sends nothing, and it
        // was built with story1..story20.
        var state = new SlotState { Hints = new SlotData() };
        Assert.Equal(Enumerable.Range(1, 20), MissionRules.Roster(state));
    }

    [Fact]
    public void ARosterIsReadInSlotOrder()
    {
        var state = StateWithRoster("story1", "knucracker1", "story19");
        Assert.Equal(new[] { 1, 21, 19 }, MissionRules.Roster(state));
    }

    [Fact]
    public void AMissionOutsideTheRosterIsNotInTheSeed()
    {
        var state = StateWithRoster("story1", "knucracker1");
        Assert.True(MissionRules.InSeed(state, 1));
        Assert.True(MissionRules.InSeed(state, 21));
        Assert.False(MissionRules.InSeed(state, 22));
        Assert.False(MissionRules.InSeed(state, 5));
    }

    [Fact]
    public void BeatenCountsSpanMissionsToo()
    {
        // The finale is gated on a COUNT of beaten missions. If SPAN completions
        // did not count, a mixed seed could never reach the number.
        var state = StateWithRoster("story1", "knucracker1", "knucracker2");
        state.CheckedLocations.Add(MissionRules.MissionCompleteLocation(21));
        state.CheckedLocations.Add(MissionRules.MissionCompleteLocation(22));
        Assert.Equal(2, MissionRules.MissionsBeaten(state));
    }

    /// <summary>The roster is memoised on the SlotData instance, so a test that
    /// builds several states in a row is also checking the cache invalidates.</summary>
    private static SlotState StateWithRoster(params string[] specifiers)
        => new() { Hints = new SlotData { MissionRoster = new List<string>(specifiers) } };
}
