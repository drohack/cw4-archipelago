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
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
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
        };
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrEmpty(s) ? "_" : s;
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
    }
}
