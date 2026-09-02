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

    /// <summary>How many other missions must be completed before the finale
    /// counts as the goal. 0 disables the requirement.</summary>
    [JsonPropertyName("missions_for_finale")]
    public int MissionsForFinale { get; set; }

    // Amounts for the energy upgrades. They travel here rather than in the item
    // names so that item ids stay identical across every yaml. Fractions are
    // sent as TENTHS and percentages as whole percents; see EnergyRules.
    /// <summary>Where the storage total stops. The rift lab's own store is
    /// about 100, so the 900 ceiling is roughly 1000 total.</summary>
    [JsonPropertyName("energy_storage_max")]
    public int EnergyStorageMax { get; set; } = 200;

    /// <summary>How many copies reach that maximum. The per-copy step is
    /// derived from the pair, so the last copy lands exactly on the cap and
    /// none is wasted: 200 over 8 copies is 25 each.</summary>
    [JsonPropertyName("energy_storage_copies")]
    public int EnergyStorageCopies { get; set; } = 8;

    /// <summary>How many copies reach the generation maximum. 10 over 8 copies
    /// is 1.25 energy/sec each.</summary>
    [JsonPropertyName("base_generation_copies")]
    public int BaseGenerationCopies { get; set; } = 8;

    /// <summary>Where the generation total stops, in energy per second. For
    /// scale, CW4's own production is about 3 to 4/sec, so the default of 10
    /// roughly triples the economy at full stack.</summary>
    [JsonPropertyName("base_generation_max")]
    public int BaseGenerationMax { get; set; } = 10;

    // Magnitudes for the ERN port upgrade items, as whole PERCENTS, travelling
    // here for the same reason the energy amounts do: item ids must be
    // identical across every yaml, so a name can never carry an amount.
    //
    // The defaults are the measured values, not guesses - see
    // docs/ern-upgrade-measurements.md.

    /// <summary>What four copies of an ERN Efficiency Rate item are worth, as a
    /// percent of the game's own fill speed. 400 means a slot that normally
    /// takes 3600 ticks fills in 900.</summary>
    [JsonPropertyName("ern_rate_max_percent")]
    public int ErnRateMaxPercent { get; set; } = 400;

    /// <summary>How high four copies of an ERN Efficiency Cap item let an
    /// upgrade's efficiency reach, as a percent. 200 is double.</summary>
    [JsonPropertyName("ern_cap_max_percent")]
    public int ErnCapMaxPercent { get; set; } = 200;

    /// <summary>The same, for Build Speed only, which needs its own value.
    ///
    /// The game shortens build time steeply and non-linearly: measured 363 /
    /// 186 / 33 ticks at 0 / 100 / 200 percent, so a 200 percent ceiling makes
    /// construction about 11x base and dwarfs every other upgrade. 150 lands on
    /// 99 ticks, which is 1.88x the 100 percent rate.</summary>
    [JsonPropertyName("ern_cap_max_build_speed_percent")]
    public int ErnCapMaxBuildSpeedPercent { get; set; } = 150;

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
