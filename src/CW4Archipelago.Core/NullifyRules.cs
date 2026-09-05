namespace CW4Archipelago.Core;

/// <summary>
/// How many of a mission's nullify targets are done.
///
/// The counting rule here was wrong twice, and both versions failed silently.
///
/// It began as "progress is the drop in GameSpace.nullifiableUnits since
/// mission start". Measured on 2026-09-05, that set does not shrink at all:
/// nine targets nullified, the game reporting the objective complete, and all
/// nine still in the set and still alive in the scene. Nullifying neither
/// removes nor destroys the structure. So the counter never moved, on a fresh
/// mission or a resumed save, and no nullify check had ever been sent by it.
///
/// The second attempt kept the same model and only fixed the total, which was
/// no better - it still asked what was LEFT.
///
/// What actually marks a nullified structure is UnitManager.IsSuppressed().
/// Measured against both fixtures: on a save with all nine nullified every unit
/// reads suppressed=true, and on a fresh start of the same mission every one
/// reads false, with dead, enabled, CAN_NULLIFY and health identical in both.
/// So the count is simply how many targets are suppressed.
/// </summary>
public static class NullifyRules
{
    /// <summary>Nullify checks owed, from the suppressed count.</summary>
    /// <param name="suppressed">Targets whose IsSuppressed() is true.</param>
    /// <param name="locationCount">Nullify locations this slot has for the
    /// mission - the number of checks that exist.</param>
    public static int Completed(int suppressed, int locationCount)
    {
        if (suppressed < 0)
            return -1;                  // the set could not be read; send nothing
        // Never claim more than there are checks: SendCounted turns this
        // straight into location names, and a name past the last instance does
        // not exist. A mission may hold more nullifiable units than checks.
        return suppressed > locationCount ? locationCount : suppressed;
    }
}
