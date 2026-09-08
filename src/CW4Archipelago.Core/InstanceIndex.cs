using System;
using System.Collections.Generic;

namespace CW4Archipelago.Core;

/// <summary>
/// WHICH instance a particular structure is, so a check belongs to the thing you
/// completed rather than to how many you have completed.
///
/// The old rule was a high-water mark: the Nth completion sent "Nullify N". So
/// nullifying the hard enemy first sent "Nullify 1", and 203 of the 236
/// locations behaved that way. Worse than untidy - the apworld puts DIFFERENT
/// requirements on different instances (Sequence's five targets in darkness need
/// a Chronat; Shattered's top-left totem needs a mover), and under a high-water
/// mark those requirements attached to an arbitrary structure.
///
/// The identity is the structure's MAP CELL, which is fixed by the map and so
/// survives a save and reload. Confirmed against the interop metadata rather
/// than assumed - an earlier draft of this asserted cellX/cellY were on the unit
/// with nothing to back it up:
///
///   nullify, caches   UnitManager.cellX / .cellY, Int32 properties
///   totems            Totem carries no cell; the unit on the same object does,
///                     and UnitManager.GetCellX(float) converts a world axis
///                     otherwise
///
/// ORDERING IS BY (Y, X) ASCENDING, and it has to be an explicit sort because
/// the game holds these in HashSets - iteration order is not defined, so the
/// order structures arrive in is not an identity at all.
///
/// Nothing here knows about difficulty. A low index is not an easy target: once
/// an index is a fixed structure, difficulty is not monotonic in the index, which
/// is exactly why the apworld's escalating tiers had to become an explicit
/// per-instance map at the same time as this landed.
/// </summary>
public static class InstanceIndex
{
    /// <summary>A coordinate the game would not give us. The dumps print
    /// (-1,-1) for this.</summary>
    public const int Unknown = -1;

    /// <summary>1-based instance numbers, one per input structure, in the input's
    /// own order.
    ///
    /// Returns an EMPTY array when any cell is unreadable. That is deliberate and
    /// is the safety property of this whole file: a partial read would assign
    /// confident-looking numbers derived from a coordinate we never actually got,
    /// and the caller cannot tell that from a good answer. Empty means "no
    /// identity available - fall back to counting", which is wrong in a way that
    /// only loses ordering, not in a way that mislabels a check.</summary>
    public static int[] Assign(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count == 0)
            return Array.Empty<int>();
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].X < 0 || cells[i].Y < 0)
                return Array.Empty<int>();

        var order = new int[cells.Count];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(order, (a, b) =>
        {
            int cy = cells[a].Y.CompareTo(cells[b].Y);
            if (cy != 0) return cy;
            int cx = cells[a].X.CompareTo(cells[b].X);
            if (cx != 0) return cx;
            // Two structures on one cell. Their numbers are interchangeable and
            // this last comparison is only here so the sort is total - see
            // HasSharedCell, which is how a caller finds out it happened.
            return a.CompareTo(b);
        });

        var instance = new int[cells.Count];
        for (int rank = 0; rank < order.Length; rank++)
            instance[order[rank]] = rank + 1;
        return instance;
    }

    /// <summary>Whether two structures share a cell, which makes their instance
    /// numbers interchangeable and the assignment non-deterministic between
    /// them. Harmless where every instance of the objective carries the same
    /// requirement; worth a log line, because on a mission with per-instance
    /// requirements it would make the map's verdict depend on HashSet order.</summary>
    public static bool HasSharedCell(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count < 2)
            return false;
        var seen = new HashSet<(int, int)>();
        foreach (var c in cells)
            if (!seen.Add((c.X, c.Y)))
                return true;
        return false;
    }

    /// <summary>A cell as a persistable key.</summary>
    public static string Key(int x, int y) => x + "," + y;

    /// <summary>The cells in INSTANCE ORDER, as keys, ready to be remembered.
    /// Entry 0 is instance 1.</summary>
    public static List<string> Remember(IReadOnlyList<(int X, int Y)> cells)
    {
        var result = new List<string>();
        if (cells == null || cells.Count == 0)
            return result;
        foreach (var c in cells)
            if (c.X < 0 || c.Y < 0)
                return new List<string>();      // same all-or-nothing rule as Assign
        var sorted = new List<(int X, int Y)>(cells);
        sorted.Sort((a, b) =>
        {
            int cy = a.Y.CompareTo(b.Y);
            if (cy != 0) return cy;
            return a.X.CompareTo(b.X);
        });
        foreach (var c in sorted)
            result.Add(Key(c.X, c.Y));
        return result;
    }

    /// <summary>Instances done, for structures that DISAPPEAR when completed.
    ///
    /// Caches are the case, and they are the awkward one. A nullified structure
    /// stays in the scene wearing IsSuppressed(), and a finished totem stays
    /// wearing totemComplete - so for both, identity is readable from live state
    /// forever. A collected cache is DESTROYED: measured 2026-09-08 on Farsite,
    /// taking one moved mustCollect from 2 to 1 and the InfoCache objects in the
    /// scene from 2 to 1 with none of them flagged retrieved. So the live set
    /// can say which caches are LEFT and can never say which one was taken.
    /// (Measured through DestroyUnit, which is the path that moves mustCollect;
    /// a hands-on pickup cannot be scripted, so that is as close as automation
    /// reaches.)
    ///
    /// Hence remembering. `remembered` is the full cell list in instance order,
    /// recorded when the mission was first seen intact, and anything in it that
    /// is no longer present has been taken. Only Farsite, Archon and Sequence
    /// have two caches at all; the other 17 missions have one, where this
    /// degenerates to "the single cache, taken or not".</summary>
    public static List<int> DoneFromRemembered(
        IReadOnlyList<string> remembered,
        IReadOnlyList<(int X, int Y)> remaining,
        int locationCount)
    {
        var result = new List<int>();
        if (remembered == null || remembered.Count == 0 || locationCount <= 0)
            return result;
        var present = new HashSet<string>();
        if (remaining != null)
            foreach (var c in remaining)
                if (c.X >= 0 && c.Y >= 0)
                    present.Add(Key(c.X, c.Y));
        for (int i = 0; i < remembered.Count; i++)
        {
            int instance = i + 1;
            if (instance > locationCount) break;
            if (!present.Contains(remembered[i]))
                result.Add(instance);
        }
        return result;
    }

    /// <summary>The instance numbers whose structure is complete, ascending.
    ///
    /// `locationCount` is how many checks the slot actually has for the
    /// objective, and instances past it are dropped: a mission can hold more
    /// nullifiable structures than it has nullify locations, and a name past the
    /// last instance is not a location at all. This is the same guard
    /// NullifyRules.Completed applies to the count, kept here so the two paths
    /// cannot disagree.
    ///
    /// An empty result covers both "nothing done yet" and "no identity
    /// available"; the caller distinguishes them by asking Assign directly, and
    /// falls back to the counted path for the second.</summary>
    public static List<int> DoneInstances(
        IReadOnlyList<(int X, int Y)> cells,
        IReadOnlyList<bool> done,
        int locationCount)
    {
        var result = new List<int>();
        if (cells == null || done == null || cells.Count != done.Count || locationCount <= 0)
            return result;
        var instance = Assign(cells);
        if (instance.Length == 0)
            return result;
        for (int i = 0; i < instance.Length; i++)
            if (done[i] && instance[i] <= locationCount)
                result.Add(instance[i]);
        result.Sort();
        return result;
    }
}
