using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CW4Archipelago.Core;

/// <summary>
/// Logic hints shipped by the apworld in slot_data. A requirement is a list of
/// any-of groups of item names; it is satisfied when every group has at least
/// one held item. The client evaluates these lists and never encodes rules.
/// </summary>
public sealed class SlotData
{
    [JsonPropertyName("starter_missions")]
    public List<string> StarterMissions { get; set; } = new() { "story1" };

    [JsonPropertyName("mission_requirements")]
    public Dictionary<string, List<List<string>>> MissionRequirements { get; set; } = new();

    [JsonPropertyName("location_requirements")]
    public Dictionary<string, List<List<string>>> LocationRequirements { get; set; } = new();

    [JsonPropertyName("ern_per_item")]
    public int ErnPerItem { get; set; } = 1;

    public static readonly SlotData Empty = new();

    public static SlotData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SlotData();
        return JsonSerializer.Deserialize<SlotData>(json) ?? new SlotData();
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public IReadOnlyList<IReadOnlyList<string>> ForMission(string specifier)
        => MissionRequirements.TryGetValue(specifier, out var g) ? g : NoGroups;

    public IReadOnlyList<IReadOnlyList<string>> ForLocation(string location)
        => LocationRequirements.TryGetValue(location, out var g) ? g : NoGroups;

    private static readonly List<List<string>> NoGroups = new();
}
