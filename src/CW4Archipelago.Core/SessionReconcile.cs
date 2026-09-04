using System.Collections.Generic;
using System.Linq;

namespace CW4Archipelago.Core;

/// <summary>
/// What survives a connect, and what must not.
///
/// This decision used to live inline in ApClient.OnLoginSuccess, in the plugin,
/// where no test could reach it - and it was wrong in three ways at once,
/// because the function built a brand-new SlotState and carried only the
/// pending checks into it. It is pure logic, so it belongs here.
///
/// Archipelago's own reference client (CommonClient.py) is the model. It keys a
/// session on (seed_name, team, slot) and calls reset_session_state() when that
/// identity changes, explicitly "before stale checks/goal are replayed". Its
/// docs make the same two requirements of any client:
///
///   * "The client must send those location checks on connection so that they
///     are not permanently lost" - docs/adding games.md, a HARD requirement.
///     So offline progress is pushed, never discarded, and never reverted.
///   * A flag "to signal that status should be updated to Goal after
///     reconnecting" - so a goal earned offline is replayed too.
///
/// The server is authoritative for what has been CHECKED. Everything else here
/// is local bookkeeping the server cannot tell us.
/// </summary>
public static class SessionReconcile
{
    /// <summary>Is this in-memory state the same session we are joining?
    ///
    /// Both halves matter. Comparing the slot NAME alone (which is what the
    /// code here used to do) treats "Droha" in one seed as "Droha" in another,
    /// and location names are identical across seeds - so a check earned in one
    /// multiworld was replayed into the next one the same player name joined.
    /// </summary>
    public static bool SameSession(SlotState state, string seed, string slot)
        => state.Seed == seed && state.Slot == slot;

    /// <summary>
    /// Build the state to run with, having just connected as (seed, slot).
    /// </summary>
    /// <param name="live">The state held in memory, which may belong to a
    /// different session entirely.</param>
    /// <param name="cached">The state persisted for THIS (seed, slot), or null
    /// if this slot has never been seen. Always the right session by
    /// construction - it is loaded by seed and slot.</param>
    public static SlotState OnConnected(
        SlotState? live,
        SlotState? cached,
        string seed,
        string slot,
        SlotData hints,
        IReadOnlyList<string> allLocations,
        IReadOnlyList<string> receivedItems,
        IEnumerable<string> serverChecked)
    {
        var carried = new List<SlotState>();
        if (live != null && SameSession(live, seed, slot))
            carried.Add(live);
        if (cached != null)
            carried.Add(cached);

        var state = new SlotState { Seed = seed, Slot = slot, Hints = hints };
        state.SetAllLocations(allLocations);
        state.ApplyReceivedItems(receivedItems);

        var acknowledged = new HashSet<string>(serverChecked);
        state.ReconcileChecked(acknowledged);

        // Everything we still owe: what either source had queued, minus what the
        // server already knows about. Displayed as checked AND still queued -
        // the two are not the same thing, and folding them together is how a
        // pending re-send was once deduped away and never sent.
        foreach (var loc in carried.SelectMany(s => s.PendingChecks).Distinct())
        {
            if (acknowledged.Contains(loc))
                continue;
            state.CheckedLocations.Add(loc);
            if (!state.PendingChecks.Contains(loc))
                state.PendingChecks.Add(loc);
        }

        // A goal reached while disconnected still has to reach the server.
        state.GoalPending = carried.Any(s => s.GoalPending);

        // The trap/boon high-water mark, which is the ONLY thing stopping a
        // reconnect from replaying every trap the player has ever been sent:
        // connecting re-delivers the whole received list. Take the highest mark
        // either source has, because this may only ever move forwards.
        state.TrapsApplied = carried.Count == 0 ? 0 : carried.Max(s => s.TrapsApplied);

        return state;
    }
}
