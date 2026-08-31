using CW4Archipelago.Core;
using Xunit;

public class FinaleGateTests
{
    private static SlotState WithBeaten(int count, int required)
    {
        var s = new SlotState();
        s.Hints.MissionsForFinale = required;
        for (int m = 1; m <= 20 && count > 0; m++)
        {
            if (m == MissionRules.FinalMission) continue;
            s.CheckedLocations.Add(MissionRules.MissionCompleteLocation(m));
            count--;
        }
        return s;
    }

    [Fact]
    public void FinaleHeld_UntilEnoughMissionsAreBeaten()
    {
        Assert.False(MissionRules.FinaleCounts(WithBeaten(11, 12)));
        Assert.True(MissionRules.FinaleCounts(WithBeaten(12, 12)));
        Assert.True(MissionRules.FinaleCounts(WithBeaten(19, 12)));
    }

    [Fact]
    public void ZeroRequirement_MeansNoGate()
    {
        Assert.True(MissionRules.FinaleCounts(WithBeaten(0, 0)));
    }

    [Fact]
    public void TheFinaleItself_DoesNotCountTowardsTheGate()
    {
        // Founders has no Mission Complete check - finishing it IS the goal - so
        // counting it would let the finale satisfy its own requirement.
        var s = WithBeaten(0, 1);
        s.CheckedLocations.Add(MissionRules.MissionCompleteLocation(MissionRules.FinalMission));
        Assert.Equal(0, MissionRules.MissionsBeaten(s));
        Assert.False(MissionRules.FinaleCounts(s));
    }

    [Fact]
    public void OnlyCompletionChecks_Count()
    {
        // Objective checks on a mission must not count as beating it.
        var s = new SlotState();
        s.Hints.MissionsForFinale = 2;
        s.CheckedLocations.Add("Home - Totem 1");
        s.CheckedLocations.Add("Home - Nullify 1");
        s.CheckedLocations.Add("Home - Cache 1");
        Assert.Equal(0, MissionRules.MissionsBeaten(s));
    }
}
