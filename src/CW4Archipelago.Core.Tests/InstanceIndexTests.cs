using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

public class InstanceIndexTests
{
    [Fact]
    public void Assign_OrdersByYThenX_OneBased()
    {
        // Deliberately not already sorted, and not sorted by X either, so a
        // rule that ordered by X first or trusted the input order would fail.
        var cells = new List<(int X, int Y)> { (40, 9), (12, 3), (80, 3) };
        Assert.Equal(new[] { 3, 1, 2 }, InstanceIndex.Assign(cells));
    }

    /// <summary>THE property the whole approach rests on. The game holds these in
    /// HashSets, so the same structures can arrive in any order; the instance a
    /// structure gets must not depend on that.</summary>
    [Fact]
    public void Assign_IsIndependentOfInputOrder()
    {
        var a = new List<(int X, int Y)> { (40, 9), (12, 3), (80, 3) };
        var b = new List<(int X, int Y)> { (80, 3), (40, 9), (12, 3) };

        var ia = InstanceIndex.Assign(a);
        var ib = InstanceIndex.Assign(b);

        // Same structure, same number, whichever order it was handed over in.
        Assert.Equal(3, ia[0]);   // (40,9) in a
        Assert.Equal(3, ib[1]);   // (40,9) in b
        Assert.Equal(1, ia[1]);   // (12,3) in a
        Assert.Equal(1, ib[2]);   // (12,3) in b
        Assert.Equal(2, ia[2]);   // (80,3) in a
        Assert.Equal(2, ib[0]);   // (80,3) in b
    }

    /// <summary>An unreadable coordinate must produce NO assignment rather than a
    /// confident wrong one. A partial read is indistinguishable from a good one
    /// once it has been turned into numbers, and mislabelling a check is worse
    /// than losing the ordering and counting instead.</summary>
    [Fact]
    public void Assign_RefusesTheWholeSet_WhenAnyCellIsUnreadable()
    {
        var cells = new List<(int X, int Y)> { (12, 3), (InstanceIndex.Unknown, InstanceIndex.Unknown), (80, 3) };
        Assert.Empty(InstanceIndex.Assign(cells));
        Assert.Empty(InstanceIndex.DoneInstances(cells, new[] { true, false, true }, 3));
    }

    [Fact]
    public void Assign_HandlesEmptyAndSingle()
    {
        Assert.Empty(InstanceIndex.Assign(new List<(int X, int Y)>()));
        Assert.Equal(new[] { 1 }, InstanceIndex.Assign(new List<(int X, int Y)> { (5, 5) }));
    }

    [Fact]
    public void HasSharedCell_FindsTwoStructuresOnOneCell()
    {
        Assert.False(InstanceIndex.HasSharedCell(new List<(int X, int Y)> { (1, 1), (2, 1) }));
        Assert.True(InstanceIndex.HasSharedCell(new List<(int X, int Y)> { (1, 1), (2, 1), (1, 1) }));
        // A shared cell still assigns every structure a distinct number - the
        // two sharing are interchangeable, not merged.
        var idx = InstanceIndex.Assign(new List<(int X, int Y)> { (1, 1), (2, 1), (1, 1) });
        Assert.Equal(new[] { 1, 2, 3 }, Sorted(idx));
    }

    [Fact]
    public void DoneInstances_ReportsTheStructuresCompleted_NotHowMany()
    {
        // Sorted order is (12,3)=1, (80,3)=2, (40,9)=3. Complete only the LAST
        // one in sorted order; the answer must be 3, where a high-water mark
        // would have said 1. This is the bug this file exists to fix.
        var cells = new List<(int X, int Y)> { (40, 9), (12, 3), (80, 3) };
        var done = new[] { true, false, false };
        Assert.Equal(new[] { 3 }, InstanceIndex.DoneInstances(cells, done, 3));
    }

    [Fact]
    public void DoneInstances_DropsInstancesPastTheLocationCount()
    {
        // A mission can hold more nullifiable structures than it has nullify
        // locations, and "Nullify 4" would not be a location at all.
        var cells = new List<(int X, int Y)> { (10, 1), (20, 1), (30, 1), (40, 1) };
        var done = new[] { true, true, true, true };
        Assert.Equal(new[] { 1, 2 }, InstanceIndex.DoneInstances(cells, done, 2));
    }

    [Fact]
    public void DoneInstances_IsEmptyOnMismatchedInput()
    {
        var cells = new List<(int X, int Y)> { (10, 1), (20, 1) };
        Assert.Empty(InstanceIndex.DoneInstances(cells, new[] { true }, 2));
        Assert.Empty(InstanceIndex.DoneInstances(cells, new[] { true, true }, 0));
    }

    /// <summary>The real shipped table, used the way LocationWatcher uses it.
    ///
    /// Farsite's caches sit at (66,51) and (150,63), which sort to instances 1
    /// and 2. If only the (66,51) one is still on the map then the OTHER was
    /// taken, so instance 2 is the check owed - and the designer's rule is that
    /// instance 1, the one nearest the rift lab, is the free one, so getting
    /// these the wrong way round would both mislabel the check and hand out the
    /// wrong requirement.</summary>
    [Fact]
    public void MapCells_NamesTheCollectedCache_WithoutEverSeeingItIntact()
    {
        var known = MapCells.CachesFor(1);
        Assert.NotNull(known);
        Assert.Equal(new[] { "66,51", "150,63" }, known!);

        // (150,63) gone: instance 2 was collected.
        var remaining = new List<(int X, int Y)> { (66, 51) };
        Assert.Equal(new[] { 2 }, InstanceIndex.DoneFromRemembered(known, remaining, 2));

        // (66,51) gone instead: instance 1. A count-based rule says "1" for
        // both of these, which is exactly the bug.
        remaining = new List<(int X, int Y)> { (150, 63) };
        Assert.Equal(new[] { 1 }, InstanceIndex.DoneFromRemembered(known, remaining, 2));

        // Nothing taken, nothing owed.
        remaining = new List<(int X, int Y)> { (66, 51), (150, 63) };
        Assert.Empty(InstanceIndex.DoneFromRemembered(known, remaining, 2));
    }

    /// <summary>Covers() is the staleness guard. A cache the table does not know
    /// about means a moved cache or a modded map, and the caller must fall back
    /// rather than assign an index from a table that no longer describes the
    /// mission.</summary>
    [Fact]
    public void MapCells_Covers_RejectsACellItDoesNotKnow()
    {
        Assert.True(MapCells.Covers(1, new[] { "66,51" }));
        Assert.True(MapCells.Covers(1, new[] { "66,51", "150,63" }));
        Assert.False(MapCells.Covers(1, new[] { "66,51", "999,999" }));
        // A mission with no caches has no row, so nothing is covered.
        Assert.Null(MapCells.CachesFor(6));
        Assert.False(MapCells.Covers(6, new[] { "1,1" }));
    }

    /// <summary>Every row must be in the ordering the assignment uses, or an
    /// index read out of the table means something different from an index
    /// computed from the map. Cheap to check, and it is the one property a
    /// hand-edit of the generated file would break.</summary>
    [Fact]
    public void MapCells_EveryRowIsInInstanceOrder()
    {
        for (int mission = 1; mission <= 20; mission++)
        {
            var row = MapCells.CachesFor(mission);
            if (row == null) continue;
            var cells = new List<(int X, int Y)>();
            foreach (var key in row)
            {
                var parts = key.Split(',');
                cells.Add((int.Parse(parts[0]), int.Parse(parts[1])));
            }
            // Assign must hand back 1..N in the row's own order.
            var expected = new int[cells.Count];
            for (int i = 0; i < expected.Length; i++) expected[i] = i + 1;
            Assert.Equal(expected, InstanceIndex.Assign(cells));
        }
    }

    private static int[] Sorted(int[] v)
    {
        var c = (int[])v.Clone();
        System.Array.Sort(c);
        return c;
    }
}
