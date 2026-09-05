using CW4Archipelago.Core;
using Xunit;

namespace CW4Archipelago.Core.Tests;

/// <summary>
/// Nullify progress is the count of SUPPRESSED targets.
///
/// The two earlier models both asked what was LEFT in
/// GameSpace.nullifiableUnits, and that set never shrinks - measured on a save
/// with nine targets nullified, all nine still present and alive, with the game
/// reporting the objective complete. No nullify check had ever been sent by the
/// counter. IsSuppressed() is what separates them: true on every target of that
/// save, false on every target of a fresh start of the same mission.
/// </summary>
public class NullifyRulesTests
{
    private const int Locations = 9;   // We Were Never Alone

    [Fact]
    public void NothingSuppressedIsNoProgress()
        => Assert.Equal(0, NullifyRules.Completed(suppressed: 0, Locations));

    [Fact]
    public void ProgressIsTheSuppressedCount()
    {
        Assert.Equal(2, NullifyRules.Completed(suppressed: 2, Locations));
        Assert.Equal(9, NullifyRules.Completed(suppressed: 9, Locations));
    }

    [Fact]
    public void AResumedSaveCountsWhatIsAlreadySuppressed()
    {
        // The reported case: load a save with every target nullified and all
        // nine checks are owed at once, with no need to have watched it happen.
        Assert.Equal(9, NullifyRules.Completed(suppressed: 9, Locations));
    }

    [Fact]
    public void MoreSuppressedUnitsThanChecksDoesNotOverCount()
    {
        // A mission may hold nullifiable units that are not checks. Going past
        // the last instance would build a location name that does not exist.
        Assert.Equal(Locations, NullifyRules.Completed(suppressed: 12, Locations));
    }

    [Fact]
    public void AnUnreadableSetSendsNothing()
        => Assert.Equal(-1, NullifyRules.Completed(suppressed: -1, Locations));

    [Fact]
    public void AMissionWithNoNullifyChecksNeverSendsAny()
    {
        Assert.Equal(0, NullifyRules.Completed(suppressed: 0, locationCount: 0));
        Assert.Equal(0, NullifyRules.Completed(suppressed: 4, locationCount: 0));
    }
}
