using System;
using System.Collections.Generic;
using System.Linq;

namespace CW4Archipelago.Core;

/// <summary>
/// The per-slot truth: what the server has sent us, what we have checked,
/// what still needs sending. Fed by the network layer (and the debug channel)
/// and observed by the in-game appliers. Pure C#: no Unity, no network.
/// </summary>
public sealed class SlotState
{
    public string Seed { get; set; } = "";
    public string Slot { get; set; } = "";
    public SlotData Hints { get; set; } = new();

    /// <summary>Received item names in server index order (duplicates allowed).</summary>
    public List<string> ReceivedItems { get; set; } = new();

    /// <summary>Every location name in this slot, as the server lists them.</summary>
    public List<string> AllLocations { get; set; } = new();

    public HashSet<string> CheckedLocations { get; set; } = new();

    /// <summary>Checks made while offline, awaiting a connection.</summary>
    public List<string> PendingChecks { get; set; } = new();

    public bool GoalPending { get; set; }

    /// <summary>How many received items have already had their trap effect
    /// applied. Traps must fire ONCE, and reconnecting re-delivers the whole
    /// received list, so firing on receipt alone would replay every trap in the
    /// game the moment a player reconnects. Persisted with the rest of the slot
    /// so it survives a restart too.</summary>
    public int TrapsApplied { get; set; }

    public event Action? ItemsChanged;
    public event Action? LocationsChanged;

    /// <summary>Announce a location change made by writing the set directly.
    /// Normal paths go through MarkChecked and raise it themselves.</summary>
    public void RaiseLocationsChanged() => LocationsChanged?.Invoke();

    public int Count(string item) => ReceivedItems.Count(i => i == item);
    public bool Has(string item) => ReceivedItems.Contains(item);

    /// <summary>
    /// Replace the received list with the server's full ordered list.
    /// Returns true if anything changed. Idempotent.
    /// </summary>
    public bool ApplyReceivedItems(IReadOnlyList<string> serverOrdered)
    {
        if (serverOrdered.SequenceEqual(ReceivedItems))
            return false;
        ReceivedItems = serverOrdered.ToList();
        ItemsChanged?.Invoke();
        return true;
    }

    /// <summary>Append one newly received item.</summary>
    public void ReceiveItem(string item)
    {
        ReceivedItems.Add(item);
        ItemsChanged?.Invoke();
    }

    /// <summary>
    /// Record a check. When not connected it is also queued for later sending.
    /// Returns true if the location was not already checked.
    /// </summary>
    public bool MarkChecked(string location, bool connected)
    {
        if (!CheckedLocations.Add(location))
            return false;
        if (!connected && !PendingChecks.Contains(location))
            PendingChecks.Add(location);
        LocationsChanged?.Invoke();
        return true;
    }

    /// <summary>Remove and return everything queued for sending.</summary>
    public IReadOnlyList<string> TakePendingChecks()
    {
        var taken = PendingChecks.ToList();
        PendingChecks.Clear();
        return taken;
    }

    /// <summary>Merge the server's authoritative checked set into ours.</summary>
    public bool ReconcileChecked(IEnumerable<string> serverChecked)
    {
        bool changed = false;
        foreach (var loc in serverChecked)
        {
            if (CheckedLocations.Add(loc))
                changed = true;
            PendingChecks.Remove(loc);
        }
        if (changed)
            LocationsChanged?.Invoke();
        return changed;
    }

    public void SetAllLocations(IEnumerable<string> locations)
    {
        AllLocations = locations.ToList();
    }
}
