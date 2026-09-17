using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>Which objective icon the mission map DRAWS, which is not always
/// the objective a mission really has.
///
/// Split out of MissionRules on 2026-09-17. MissionRules decides what a seed
/// CONTAINS - titles, rosters, location names - and that is read by the
/// generator-facing half of the mod. This decides a TEXTURE, and its own
/// comment below says PURELY COSMETIC in capitals. Two unrelated jobs under
/// one name; the test that covers this half was already called
/// GlyphLocationTests, named for a unit that had no file.</summary>
public static class ObjectiveIcons
{
    /// <summary>Objective icons the map should DRAW in place of the real ones.
    ///
    /// Farsite's only required objective is slot 5, Custom, so the map draws the
    /// skull. What the mission actually asks of the player is to light its two
    /// totems after taking the second cache, and the totem icon says that where
    /// the skull says nothing:
    ///
    ///   "Can we also change the Skull icon on Farsite into the Totem icon as
    ///    that's the real goal. I know it's technically custom in the map, but
    ///    it's just activating the 2 totems after getting the 2nd item."
    ///
    /// PURELY COSMETIC. The check is still "Farsite - Custom" and its status is
    /// still read from that location - only the texture changes. The pair has to
    /// be invertible because the marker's objective field is the only place the
    /// drawn index is stored, and the colouring pass reads it back to decide
    /// which locations the icon stands for.
    ///
    /// Safe on Farsite precisely because it has no Totems objective of its own,
    /// so nothing else can claim slot 1 and the mapping stays one-to-one.</summary>
    private static readonly Dictionary<int, (int Logical, int Display)> IconAlias = new()
    {
        [1] = (5, 1),      // Farsite: Custom is drawn as Totems
    };

    /// <summary>The index to DRAW for a real objective slot.</summary>
    public static int DisplayObjective(int mission, int logical)
        => IconAlias.TryGetValue(mission, out var a) && a.Logical == logical ? a.Display : logical;

    /// <summary>The real objective slot behind a drawn index - the inverse of
    /// <see cref="DisplayObjective"/>, used when reading a marker back.</summary>
    public static int LogicalObjective(int mission, int display)
        => IconAlias.TryGetValue(mission, out var a) && a.Display == display ? a.Logical : display;
}
