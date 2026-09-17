using System.Collections.Generic;
using CW4Archipelago.Core;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Points the twenty planets of the Farsite level select at the twenty missions
/// THIS SEED actually contains.
///
/// With the SPAN Experiments off, the roster is story1..story20 in order and
/// every write below is a no-op. That is deliberate: there is one code path and
/// the common case exercises it, so a bug here shows up in ordinary play rather
/// than only in the experimental mode.
///
/// WHY THE PLANET IS THE THING THAT CHANGES, rather than adding planets for the
/// SPAN maps. The spiral is twenty authored positions with authored lines
/// between them; a seed holds twenty missions. Retargeting keeps the map the
/// player already knows, keeps the "?" on a locked slot, and keeps every
/// downstream applier working by title, which is how they already worked.
///
/// WHAT IT WRITES, and why each field:
///
///   planetGUID   what the launch path loads, and what FinalePlacement and the
///                click patch identify a planet by
///   map_guid     the same id on the mission record the panel builds
///   map_title    the game's own title field (blank on the shipped map, but the
///                tracker reads it as a fallback, so it must not disagree)
///   title.text   the label on screen, and the key EVERY other applier resolves
///                a planet by - TrackerView.MissionByTitle
///   map_objectives  the objective bitmask. Writing it does NOT move the icons
///                already on screen - that byte is read when the map is built -
///                so TrackerView.ReconcileGlyphs is what actually redraws them.
///                It is written anyway so the underlying data is not a lie.
///
/// WHAT IT DOES NOT TOUCH: the lock state. forceUnlocked / unlocked / the
/// lockedPlanet object all belong to TrackerView, which sets them from AP state
/// every frame. Writing them here would fight it, and the "?" on an unfound
/// mission is exactly the behaviour being preserved.
///
/// ORDER MATTERS: this runs BEFORE TrackerView.ApplyTints in the late tick. The
/// tracker resolves planets by title, so a paint that happened before the
/// retarget would resolve the OLD mission and light the wrong icons.
/// </summary>
public sealed class SpiralRoster
{
    /// <summary>Physical planet (by native pointer) to its ORIGINAL slot, 1..20.
    ///
    /// Recorded before the first rewrite and kept for the life of the scene,
    /// because after the rewrite a planet's guid names its new mission and there
    /// is no longer any way to ask which position it occupies. FinalePlacement
    /// needs that question answered - it moves the planet in the LAST slot, not
    /// the planet showing a particular mission.</summary>
    private static readonly Dictionary<System.IntPtr, int> SlotOfPlanet = new();

    private bool _applied;

    /// <summary>Re-run per visit: the planets are rebuilt with the scene, so both
    /// the flag and the recorded slots have to go.</summary>
    public void OnSceneChanged()
    {
        _applied = false;
        SlotOfPlanet.Clear();
    }

    /// <summary>Re-run without forgetting which planet sits in which slot.
    ///
    /// Called when Archipelago state changes, which is when the ROSTER can
    /// change - a connect, or a slot cache loading. The planets themselves are
    /// the same objects, so SlotOfPlanet must survive: it is the only remaining
    /// record of the authored order once a retarget has overwritten the guids.
    ///
    /// Without this the level select would show the campaign for the whole of
    /// the first visit whenever the connect lands after the first paint, which
    /// with AutoConnect is the ordinary case rather than an edge one.</summary>
    public void Invalidate() => _applied = false;

    /// <summary>The slot a planet occupies, or 0 if it is not one of the twenty.
    /// Answers correctly both before and after the retarget.</summary>
    public static int SlotOf(SpanNetworkPlanet planet)
    {
        try
        {
            if (SlotOfPlanet.TryGetValue(planet.Pointer, out var slot))
                return slot;
        }
        catch { }
        // Not recorded yet, so nothing has been rewritten and the guid is still
        // the authored one.
        try
        {
            if (MissionRules.TryParseSpecifier(planet.planetGUID, out var mission)
                && !MissionRules.IsSpan(mission))
                return mission;
        }
        catch { }
        return 0;
    }

    public void Apply()
    {
        if (_applied || ModCore.CurrentScene != "Galaxy")
            return;

        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null || planets.Length == 0)
            return;

        // Index by slot from the AUTHORED guid. Anything that is not story1..20
        // is not ours: story0 is the tutorial, and a planet already retargeted
        // would have been recorded in SlotOfPlanet on the pass that did it.
        var bySlot = new Dictionary<int, SpanNetworkPlanet>();
        foreach (var planet in planets)
        {
            if (!GameUtil.IsAlive(planet))
                continue;
            int slot = SlotOf(planet);
            if (slot >= 1 && slot <= 20)
                bySlot[slot] = planet;
        }

        // Wait for the whole map. A partial set would leave half the spiral
        // pointing at the campaign and half at the roster, and the flag below
        // would stop us ever fixing it.
        if (bySlot.Count < 20)
            return;

        var state = ModCore.Client.State;
        var roster = MissionRules.Roster(state);
        if (roster.Count != 20)
        {
            ModCore.Log.LogWarning(
                $"AP: roster has {roster.Count} missions, expected 20 - leaving the map alone");
            _applied = true;
            return;
        }

        int changed = 0;
        for (int slot = 1; slot <= 20; slot++)
        {
            var planet = bySlot[slot];
            try { SlotOfPlanet[planet.Pointer] = slot; } catch { }

            // ALWAYS WRITE, never "skip when the mission equals the slot". That
            // shortcut is right on a fresh map and wrong on a re-apply: a slot
            // showing a SPAN map would keep showing it when the roster went back
            // to the campaign under it, which is what a disconnect does. Writing
            // a campaign slot its own values costs nothing and cannot be stale.
            int mission = roster[slot - 1];
            string want = MissionRules.Specifier(mission);
            bool differs = GuidOf(planet) != want;
            if (Retarget(planet, mission) && differs)
                changed++;
        }

        _applied = true;
        if (changed > 0)
        {
            // The titles just moved, and every other applier resolves a planet by
            // its title. Without this the first paint of the visit reads the old
            // ones.
            ModCore.InvalidateTracker();
            ModCore.Log.LogInfo($"AP: retargeted {changed} of 20 level-select planets");
        }
    }

    private static string GuidOf(SpanNetworkPlanet planet)
    {
        try { return planet.planetGUID ?? ""; }
        catch { return ""; }
    }

    private static bool Retarget(SpanNetworkPlanet planet, int mission)
    {
        string specifier = MissionRules.Specifier(mission);
        string title = MissionRules.Titles[mission];
        try { planet.planetGUID = specifier; } catch { return false; }
        try { planet.map_guid = specifier; } catch { }
        try { planet.map_title = title; } catch { }
        try { if (planet.title != null) planet.title.text = title; } catch { }
        try { planet.map_objectives = ObjectiveMask(mission); } catch { }
        return true;
    }

    /// <summary>The objective bitmask for a mission: bit k is objective slot k.
    /// Built from the same table the icons are rebuilt from, so the byte and the
    /// glyphs cannot disagree.</summary>
    private static byte ObjectiveMask(int mission)
    {
        int mask = 0;
        if (MissionRules.MissionObjectives.TryGetValue(mission, out var slots))
            foreach (var slot in slots)
                mask |= 1 << slot;
        return (byte)mask;
    }
}
