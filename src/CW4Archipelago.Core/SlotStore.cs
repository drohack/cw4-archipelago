using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CW4Archipelago.Core;

/// <summary>JSON persistence of SlotState, one file per (seed, slot).</summary>
public sealed class SlotStore
{
    private readonly string _root;

    public SlotStore(string rootDirectory)
    {
        _root = rootDirectory;
    }

    public string PathFor(string seed, string slot)
        => Path.Combine(_root, "slots", $"{Sanitize(seed)}-{Sanitize(slot)}.json");

    /// <summary>Which (seed, slot) was last played.
    ///
    /// Written on every save so the game can come up on the cached slot when no
    /// server is reachable. Its own file rather than a parse of SaveArchiver's
    /// active.txt: that joins seed and slot with a hyphen, and slot names may
    /// contain hyphens, so it cannot be split back apart reliably.</summary>
    public string LastSessionPath => Path.Combine(_root, "last-session.json");

    public void Save(SlotState state)
    {
        var path = PathFor(state.Seed, state.Slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new Dto
        {
            Seed = state.Seed,
            Slot = state.Slot,
            HintsJson = state.Hints.ToJson(),
            ReceivedItems = state.ReceivedItems,
            AllLocations = state.AllLocations,
            CheckedLocations = state.CheckedLocations.ToArray(),
            PendingChecks = state.PendingChecks,
            GoalPending = state.GoalPending,
            // The trap/boon high-water mark. It was missing here, so it reset
            // to zero on every launch and connect - and connecting re-delivers
            // the whole received list, so every trap fired again.
            TrapsApplied = state.TrapsApplied,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(LastSessionPath, JsonSerializer.Serialize(
            new LastSession { Seed = state.Seed, Slot = state.Slot },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The state of the last slot played, for coming up offline.
    /// Null when nothing has ever been played or the pointer is unreadable.</summary>
    public SlotState? LoadLast()
    {
        try
        {
            if (!File.Exists(LastSessionPath))
                return null;
            var last = JsonSerializer.Deserialize<LastSession>(File.ReadAllText(LastSessionPath));
            if (last == null || string.IsNullOrEmpty(last.Seed) || string.IsNullOrEmpty(last.Slot))
                return null;
            return Load(last.Seed, last.Slot);
        }
        catch
        {
            // A corrupt pointer must never stop the game from starting. Coming
            // up with no cached slot is the same as never having played one.
            return null;
        }
    }

    public SlotState? Load(string seed, string slot)
    {
        var path = PathFor(seed, slot);
        if (!File.Exists(path))
            return null;
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
        if (dto == null)
            return null;
        return new SlotState
        {
            Seed = dto.Seed,
            Slot = dto.Slot,
            Hints = SlotData.FromJson(dto.HintsJson),
            ReceivedItems = dto.ReceivedItems,
            AllLocations = dto.AllLocations,
            CheckedLocations = new(dto.CheckedLocations),
            PendingChecks = dto.PendingChecks,
            GoalPending = dto.GoalPending,
            TrapsApplied = dto.TrapsApplied,
        };
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrEmpty(s) ? "_" : s;
    }

    private sealed class LastSession
    {
        public string Seed { get; set; } = "";
        public string Slot { get; set; } = "";
    }

    private sealed class Dto
    {
        public string Seed { get; set; } = "";
        public string Slot { get; set; } = "";
        public string HintsJson { get; set; } = "";
        public System.Collections.Generic.List<string> ReceivedItems { get; set; } = new();
        public System.Collections.Generic.List<string> AllLocations { get; set; } = new();
        public string[] CheckedLocations { get; set; } = Array.Empty<string>();
        public System.Collections.Generic.List<string> PendingChecks { get; set; } = new();
        public bool GoalPending { get; set; }
        public int TrapsApplied { get; set; }
    }
}
