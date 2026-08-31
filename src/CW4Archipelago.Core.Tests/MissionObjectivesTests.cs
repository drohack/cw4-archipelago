using System.Collections.Generic;
using System.Linq;
using CW4Archipelago.Core;
using Xunit;

/// <summary>
/// Pins MissionRules.MissionObjectives against what the mission map ACTUALLY
/// draws, dumped from the live game on 2026-08-31 with "glyphs:dump".
///
/// The point of the comparison: the map builds its icons from each map file's
/// authored objective list, and that list is not always the mission's real
/// objective set. Nineteen agree. Farsite does not - it draws a Totems icon and
/// the mission has no totems (totems=0, infoCaches=2, only objective slot 5
/// enabled, measured with "counts:dump"). Vanilla shows the same wrong icon, so
/// this is the game's data rather than a mod regression.
///
/// If a game update changes either side, that shows up here as a failed test
/// instead of a quietly wrong map.
/// </summary>
public class MissionObjectivesTests
{
    /// <summary>Objective indices the map's own markers carry, per mission.</summary>
    private static readonly Dictionary<int, int[]> DrawnByTheGame = new()
    {
        [1] = new[] { 1 },           [2] = new[] { 0, 1, 4 },
        [3] = new[] { 0, 1, 4 },     [4] = new[] { 0, 1, 4 },
        [5] = new[] { 0, 1, 4 },     [6] = new[] { 0, 2 },
        [7] = new[] { 0, 1, 2, 4 },  [8] = new[] { 0, 1, 2 },
        [9] = new[] { 0, 1, 2, 4 },  [10] = new[] { 0, 1, 2, 4 },
        [11] = new[] { 0, 1, 4 },    [12] = new[] { 0, 1, 2, 4 },
        [13] = new[] { 0, 1, 4 },    [14] = new[] { 0, 1, 2, 4 },
        [15] = new[] { 0, 1, 2, 4 }, [16] = new[] { 0, 1, 4 },
        [17] = new[] { 0, 2, 4 },    [18] = new[] { 0, 1, 2, 4 },
        [19] = new[] { 0, 1, 4, 5 }, [20] = new[] { 0, 1, 2, 5 },
    };

    [Fact]
    public void EveryMissionHasAnAuthoredSet()
    {
        for (int m = 1; m <= 20; m++)
            Assert.True(MissionRules.MissionObjectives.ContainsKey(m), $"mission {m} missing");
    }

    [Fact]
    public void OnlyFarsiteDisagreesWithTheMapsOwnIcons()
    {
        var disagree = Enumerable.Range(1, 20)
            .Where(m => !DrawnByTheGame[m].SequenceEqual(MissionRules.MissionObjectives[m]))
            .ToList();
        Assert.Equal(new[] { 1 }, disagree);
    }

    [Fact]
    public void FarsiteIsCollectAndCustom()
    {
        // The correction itself: two caches and a custom objective, no totems.
        Assert.Equal(new[] { 4, 5 }, MissionRules.MissionObjectives[1]);
    }

    [Fact]
    public void AuthoredSetsAreAscendingAndInRange()
    {
        foreach (var kv in MissionRules.MissionObjectives)
        {
            Assert.Equal(kv.Value.OrderBy(i => i).ToArray(), kv.Value);
            Assert.All(kv.Value, i => Assert.InRange(i, 0, MissionRules.ObjectiveTypes.Length - 1));
        }
    }

    [Fact]
    public void OfflineFallsBackToTheAuthoredSet()
    {
        // The reason this table exists: with no server there are no locations, and
        // the map still shows Farsite (the default starter). Before the fallback
        // it kept vanilla's totems icon every time the game opened unconnected.
        var empty = new SlotState();
        Assert.Equal(new[] { 4, 5 }, MissionRules.ExpectedObjectiveIndices(empty, 1));
    }

    [Fact]
    public void KnownLocationsBeatTheAuthoredSet()
    {
        // A seed may exclude locations, so once the server has spoken it wins.
        var s = new SlotState();
        s.AllLocations.Add("Farsite - Custom");
        Assert.Equal(new[] { 5 }, MissionRules.ExpectedObjectiveIndices(s, 1));
    }
}
