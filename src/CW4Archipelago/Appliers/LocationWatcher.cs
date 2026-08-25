using System;
using CW4Archipelago.Core;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Watches the live mission for objective and mission completion, turning each
/// transition into an Archipelago location check. story20 completion also
/// sends the goal. Ported from the probe's WatchLocations, mapping game
/// objective indices to AP location names via MissionRules.
/// </summary>
public sealed class LocationWatcher
{
    private IntPtr _lastGameSpace = IntPtr.Zero;
    private int _mission;
    private bool[]? _objectiveDone;
    private bool _missionComplete;

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _lastGameSpace = IntPtr.Zero; return; }
        if (GameSpace.editMode) return;
        var world = gs.world;
        if (world == null) return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _mission = ResolveMission(gs);
            _objectiveDone = null;
            _missionComplete = false;
            ModCore.Log.LogInfo($"LocationWatcher: mission {_mission} ('{SpecifierOf(_mission)}')");
        }
        if (_mission == 0)
            return;

        var objs = world.missionObjectives;
        if (objs != null)
        {
            if (_objectiveDone == null || _objectiveDone.Length != objs.Length)
                _objectiveDone = new bool[objs.Length];
            for (int i = 0; i < objs.Length && i < MissionRules.ObjectiveTypes.Length; i++)
            {
                bool done = false;
                try { done = world.IsMissionObjectiveComplete(i); } catch { }
                if (done && !_objectiveDone[i])
                {
                    _objectiveDone[i] = true;
                    SendCheck(MissionRules.ObjectiveLocation(_mission, i));
                }
            }
        }

        bool mc = false;
        try { mc = world.IsMissionComplete(); } catch { }
        if (mc && !_missionComplete)
        {
            _missionComplete = true;
            if (_mission == MissionRules.FinalMission)
            {
                ModCore.Log.LogInfo("LocationWatcher: FINAL mission complete -> goal");
                ModCore.Client.SendGoal();
            }
            else
            {
                SendCheck(MissionRules.MissionCompleteLocation(_mission));
            }
        }
    }

    private void SendCheck(string location)
    {
        var state = ModCore.Client.State;
        if (!MissionRules.IsLocation(state, location))
            return;   // not a real location in this slot (e.g. non-required objective)
        if (state.MarkChecked(location, ModCore.Client.Connected))
        {
            ModCore.Log.LogInfo($"LOCATION CHECK: {location}");
            ModCore.Client.SendChecks(new[] { location });
        }
    }

    private static int ResolveMission(GameSpace gs)
    {
        // gs.specifier is the live current-mission id (storyN), reliable on
        // both the boot and resume-from-save paths.
        try
        {
            if (MissionRules.TryParseSpecifier(gs.specifier, out var n))
                return n;
        }
        catch { }
        return 0;
    }

    private static string SpecifierOf(int mission) => mission == 0 ? "?" : MissionRules.Specifier(mission);
}
