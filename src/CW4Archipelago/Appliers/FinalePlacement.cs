using System.Collections.Generic;
using CW4Archipelago.Core;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Brings the stranded planet ("Ever After", story20) onto the visible part of
/// the Farsite level select.
///
/// Vanilla parks it at local (40, 0, -80) while all twenty other missions sit
/// inside roughly a 20x16 box - about 82 units of empty starfield away, with the
/// line from Founders running off-screen with it. Nothing is broken: it is
/// active, unlocked and renders correctly once the camera gets there. It is just
/// undiscoverable, and a player who has never seen it has no reason to drag in
/// that direction. In vanilla that hardly matters, because the story hands off
/// to it after the Founders cutscene. Under Archipelago it matters a great deal,
/// because beating it IS the goal.
///
/// So move it onto the map and draw the connection. It hangs off Wallis rather
/// than Founders, and to Wallis's RIGHT, because Ever After is no longer the
/// finale - Founders is - and the worksheet asks for it as a side branch:
/// "Probably don't have this connected to the final level 19, have it connected
/// to level 18 as a new branch and it can be to the right of Wallis instead of
/// underneath it."
///
/// All cosmetic - a transform write and one line object on a menu screen. It
/// changes no unlock, no objective and no mission content; clicking the planet
/// boots story20 exactly as before.
///
/// See docs/research-findings.md, "Ever After (story20) is parked off the galaxy
/// map", for the measurements this is based on and how to re-derive them
/// (CW4DevTools: story:open, planets:dump, span:goto story20).
/// </summary>
public sealed class FinalePlacement
{
    /// <summary>The planet parked off the map.</summary>
    private const int Stranded = 20;

    /// <summary>What it hangs off. Wallis, not Founders: Founders is the goal
    /// mission, and Ever After is a side branch rather than what follows it.</summary>
    private const int Anchor = 18;

    /// <summary>How far off the cluster a planet must be before we treat it as
    /// stranded. The campaign spans about 20 units, so anything beyond this is
    /// unambiguous - and the test means a patched or modded map that already
    /// places story20 sensibly is left alone.</summary>
    private const float StrandedDistance = 30f;

    /// <summary>Directions sampled around Founders when choosing the new spot.
    /// Fixed count and fixed starting angle, so the placement is deterministic:
    /// the planet must not wander between launches.</summary>
    private const int Samples = 24;

    private bool _placed;

    /// <summary>Re-run per visit to the map: the planets are rebuilt with the
    /// scene, so a flag that survived it would skip the move on the second
    /// visit.</summary>
    public void OnSceneChanged() => _placed = false;

    public void Apply()
    {
        if (_placed || ModCore.CurrentScene != "Galaxy")
            return;

        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null || planets.Length == 0)
            return;

        string strandedGuid = MissionRules.Specifier(Stranded);
        string anchorGuid = MissionRules.Specifier(Anchor);

        SpanNetworkPlanet? finale = null;
        SpanNetworkPlanet? anchor = null;
        var others = new List<SpanNetworkPlanet>();
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p))
                continue;
            string guid = GuidOf(p);
            if (guid == strandedGuid) { finale = p; continue; }
            if (guid == anchorGuid) anchor = p;
            others.Add(p);
        }

        // Nothing to do until the whole map exists: placing against a partial
        // set would pick a spot that is free only because its neighbours have
        // not spawned yet.
        if (finale == null || anchor == null || others.Count < 2)
            return;

        var finalePos = LocalOf(finale);
        var anchorPos = LocalOf(anchor);
        if (Flat(finalePos - anchorPos).magnitude < StrandedDistance)
        {
            _placed = true;   // already reachable - leave the map alone
            return;
        }

        float radius = NearestNeighbourDistance(anchorPos, others);
        var target = FreestSpotAround(anchorPos, radius, others);

        try { finale.transform.localPosition = new Vector3(target.x, finalePos.y, target.z); }
        catch { return; }

        LinkAnchorTo(anchor, finale);
        HideTutorial(others);

        _placed = true;
        ModCore.Log.LogInfo(
            $"AP: moved '{MissionRules.Titles[Stranded]}' from ({finalePos.x:0.0},{finalePos.z:0.0}) " +
            $"to ({target.x:0.0},{target.z:0.0}), beside '{MissionRules.Titles[Anchor]}'");
    }

    /// <summary>Hide the tutorial planet.
    ///
    /// story0 ("09 Leo, 266") is not part of the randomizer - the missions are
    /// story1..story20 - so leaving it on the map only invites a player to spend
    /// time on a level that unlocks nothing and sends no check.</summary>
    private static void HideTutorial(List<SpanNetworkPlanet> planets)
    {
        string tutorial = "story0";
        foreach (var p in planets)
        {
            string guid = GuidOf(p);
            if (guid != tutorial)
                continue;
            try
            {
                if (p.gameObject.activeSelf)
                {
                    p.gameObject.SetActive(false);
                    ModCore.Log.LogInfo("AP: hid the tutorial planet (story0) - not part of the randomizer");
                }
            }
            catch { }
            return;
        }
    }

    /// <summary>Give the anchor a line to the moved planet.
    ///
    /// Vanilla draws no line for this link at all: the map has 19 line objects
    /// for 20 connections, and none of them is the ~82-unit one that Founders ->
    /// story20 would need. (SpanNetworkPlanet.lines is also empty on every
    /// planet - the real line objects live as children of each planet's
    /// lineContainer, drawn in LOCAL space from the origin to the neighbour's
    /// offset.) So the line has to be created, not just re-pointed, or Founders
    /// ends up as the only planet in the chain with no successor drawn.
    /// </summary>
    private static void LinkAnchorTo(SpanNetworkPlanet anchor, SpanNetworkPlanet finale)
    {
        try
        {
            Transform container;
            try { container = anchor.lineContainer; } catch { return; }
            if (container == null)
                return;

            // Local offset, matching how every other line is built.
            var offset = Flat(finale.transform.localPosition - anchor.transform.localPosition);

            // ALWAYS add a line; never re-point one that is already there.
            // Wallis already has a line to Founders, and an earlier version of
            // this method moved that line's far end instead of adding a second -
            // which silently disconnected Founders from the chain. Wallis has to
            // point at BOTH. _placed keeps this from running twice per mission,
            // so there is no risk of stacking duplicates.
            GameObject prefab;
            try { prefab = anchor.linePrefab; } catch { return; }
            if (prefab == null)
                return;

            var go = UnityEngine.Object.Instantiate(prefab, container, false);
            var made = go.GetComponent<SpanNetworkPlanetLine>();
            if (made == null)
                return;
            made.SetEnd(offset);
            MatchLineAppearance(made, container);
        }
        catch { }
    }

    /// <summary>Make the new line look like the map's other lines.
    ///
    /// A freshly instantiated prefab keeps its authoring colours, which read as
    /// a different kind of link entirely - the one line on the map that looks
    /// wrong, and it would be our doing. Copy the appearance from a line the
    /// game built rather than hard-coding colours, so it follows the map's own
    /// state and any future theme change.
    ///
    /// The copy is a snapshot: it does not re-tint later if the game recolours
    /// the chain. Acceptable because the map is rebuilt on every visit and the
    /// copy is taken fresh each time.</summary>
    private static void MatchLineAppearance(SpanNetworkPlanetLine made, Transform ownContainer)
    {
        try
        {
            var target = made.lineRenderer;
            if (target == null)
                return;

            foreach (var other in UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetLine>())
            {
                if (other == null || other.Pointer == made.Pointer)
                    continue;
                // Skip anything under our own container - that is only ever the
                // line we just made.
                try { if (other.transform.parent != null && other.transform.parent.Pointer == ownContainer.Pointer) continue; }
                catch { }

                var src = other.lineRenderer;
                if (src == null)
                    continue;

                try { target.sharedMaterial = src.sharedMaterial; } catch { }
                try { target.colorGradient = src.colorGradient; } catch { }
                try { target.startColor = src.startColor; } catch { }
                try { target.endColor = src.endColor; } catch { }
                try { target.startWidth = src.startWidth; } catch { }
                try { target.endWidth = src.endWidth; } catch { }
                return;
            }
        }
        catch { }
    }

    /// <summary>Spacing to use for the new planet, taken from the map itself
    /// rather than a constant, so it sits at the same distance the campaign
    /// already uses.</summary>
    private static float NearestNeighbourDistance(Vector3 from, List<SpanNetworkPlanet> others)
    {
        float best = float.MaxValue;
        foreach (var p in others)
        {
            var d = Flat(LocalOf(p) - from).magnitude;
            if (d > 0.01f && d < best)
                best = d;
        }
        return best == float.MaxValue ? 4f : best;
    }

    /// <summary>The point on a circle around the anchor that is furthest from
    /// every other planet - the campaign is a tight inward spiral, so the gap
    /// beside a planet has to be found rather than assumed.
    ///
    /// Only the right-hand half of the circle is considered: local +x is screen
    /// right (measured against the live map), and the branch is asked to sit to
    /// the right of Wallis rather than wherever happens to be emptiest.</summary>
    private static Vector3 FreestSpotAround(Vector3 anchor, float radius, List<SpanNetworkPlanet> others)
    {
        var best = anchor + new Vector3(radius, 0f, 0f);
        float bestClearance = -1f;

        for (int i = 0; i < Samples; i++)
        {
            // Sweep -80 to +80 degrees about +x, so every candidate is to the right.
            float angle = (-80f + i * (160f / (Samples - 1))) * Mathf.Deg2Rad;
            var candidate = anchor + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            float clearance = float.MaxValue;
            foreach (var p in others)
            {
                var d = Flat(LocalOf(p) - candidate).magnitude;
                if (d < clearance)
                    clearance = d;
            }
            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = candidate;
            }
        }
        return best;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    private static Vector3 LocalOf(SpanNetworkPlanet p)
    {
        try { return p.transform.localPosition; }
        catch { return Vector3.zero; }
    }

    private static string GuidOf(SpanNetworkPlanet p)
    {
        try { return p.planetGUID ?? ""; }
        catch { return ""; }
    }
}
