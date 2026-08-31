using CW4Archipelago.Core;
using Xunit;

public class FinaleTrackerTests
{
    private static SlotState Beaten(int count, int required)
    {
        var s = new SlotState();
        s.Hints.MissionsForFinale = required;
        // The finale must be unlocked and have a reachable check of its own, so
        // that a "locked" result can only come from the mission-count gate and
        // not from the ordinary reasons.
        s.ReceivedItems.Add(MissionRules.UnlockItem(MissionRules.FinalMission));
        s.AllLocations.Add(MissionRules.ObjectiveLocation(MissionRules.FinalMission, 4));
        for (int m = 1; m <= 20 && count > 0; m++)
        {
            if (m == MissionRules.FinalMission) continue;
            s.CheckedLocations.Add(MissionRules.MissionCompleteLocation(m));
            count--;
        }
        return s;
    }

    [Fact]
    public void Finale_ReadsLocked_UntilTheCountIsMet()
    {
        Assert.Equal(TrackerStatus.Locked,
            TrackerRules.MissionStatus(Beaten(11, 12), MissionRules.FinalMission));
    }

    [Fact]
    public void Finale_StopsReadingLocked_OnceTheCountIsMet()
    {
        Assert.NotEqual(TrackerStatus.Locked,
            TrackerRules.MissionStatus(Beaten(12, 12), MissionRules.FinalMission));
    }

    [Fact]
    public void OtherMissions_AreUnaffectedByTheGate()
    {
        // The gate must colour the finale only - greying anything else would
        // misrepresent a mission the player can actually play.
        var s = Beaten(0, 12);
        s.Hints.StarterMissions.Clear();
        s.Hints.StarterMissions.Add("story3");
        Assert.NotEqual(MissionRules.FinalMission, 3);
        // story3 is a starter, so it is unlocked regardless of the finale gate.
        Assert.True(MissionRules.IsUnlocked(s, 3));
    }

    [Fact]
    public void NoGate_MeansTheFinaleIsNeverLockedForThatReason()
    {
        Assert.NotEqual(TrackerStatus.Locked,
            TrackerRules.MissionStatus(Beaten(0, 0), MissionRules.FinalMission));
    }
}
