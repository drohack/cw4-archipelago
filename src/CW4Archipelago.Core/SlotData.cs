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

    /// <summary>The 20 missions this seed contains, IN LEVEL-SELECT ORDER.
    ///
    /// Sent for every seed, not just a SPAN one: a campaign seed's roster is
    /// story1..story20 in order, which is exactly what the untouched galaxy view
    /// already shows, so the retarget is a no-op there. That is deliberate -
    /// there is one code path, and the common case exercises it.
    ///
    /// EMPTY MEANS AN OLD SEED. A seed generated before this key existed sends
    /// nothing, and the fallbacks below give it the campaign roster, which is
    /// what it was built with.</summary>
    [JsonPropertyName("mission_roster")]
    public List<string> MissionRoster { get; set; } = new();

    /// <summary>Specifier to display title, for the missions in the roster.
    ///
    /// DELIBERATELY UNREAD BY THIS PLUGIN, and it must stay that way. A title is
    /// the location-name prefix, the key TrackerView resolves a planet by, and
    /// the unlock item's name, all at once - so it has to be ONE value, and that
    /// value is <see cref="SpanMissionTable.Titles"/>, which CI regenerates from
    /// the apworld's span_data.py and fails on any diff. Preferring this field
    /// instead would let a seed and a plugin disagree about what a mission is
    /// called, and the symptom would be checks sent to names the server has never
    /// heard of - silent, because a location the server does not know is simply
    /// ignored.
    ///
    /// It is deserialised so an external tracker reading the same slot data has
    /// the names without needing the table.</summary>
    [JsonPropertyName("mission_titles")]
    public Dictionary<string, string> MissionTitles { get; set; } = new();

    /// <summary>Whether this seed mixed in the SPAN Experiments.</summary>
    [JsonPropertyName("span_missions")]
    public bool SpanMissions { get; set; }

    [JsonPropertyName("mission_requirements")]
    public Dictionary<string, List<List<string>>> MissionRequirements { get; set; } = new();

    [JsonPropertyName("location_requirements")]
    public Dictionary<string, List<List<string>>> LocationRequirements { get; set; } = new();

    /// <summary>Requirements at the STRICT (non-casual) tier.
    ///
    /// Lets the tracker separate "cannot be reached" from "reachable, but this
    /// logic tier does not promise it": casual logic adds anti-air the design
    /// notes call optional, so a check gated only by that is yellow, while one
    /// gated by a weapon or a factory is red. Identical to
    /// <see cref="LocationRequirements"/> under standard logic.</summary>
    [JsonPropertyName("strict_location_requirements")]
    public Dictionary<string, List<List<string>>> StrictLocationRequirements { get; set; } = new();

    /// <summary>Objective slots each mission must finish to be won.
    ///
    /// Lets a completion imply its required objectives' checks. Farsite was
    /// beaten in full and sent "Mission Complete" while "Custom" never went
    /// out, because the game reported the mission complete in the same frame
    /// that IsMissionObjectiveComplete(5) still read false.</summary>
    [JsonPropertyName("required_objectives")]
    public Dictionary<string, List<int>> RequiredObjectives { get; set; } = new();

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

    /// <summary>How many copies of each ERN upgrade item reach the maxima
    /// below, exactly as EnergyStorageCopies does for the energy curve.
    ///
    /// THE DEFAULT OF 4 IS THE COMPATIBILITY VALUE, not the current option
    /// default. A seed generated before this key existed sends nothing, and 4
    /// is what those seeds were built with - so an old seed keeps behaving
    /// exactly as it did. New seeds send their real count.
    ///
    /// Before this existed the divisor was the hardcoded MaxCopies, which meant
    /// lowering the pool's copy count silently lowered the CEILING rather than
    /// coarsening the steps - the opposite of how the energy knob behaves.
    /// Dropping the pool to 2 copies capped efficiency at 150 percent instead
    /// of 200 and rate at 250 instead of 400.</summary>
    [JsonPropertyName("ern_upgrade_copies")]
    public int ErnUpgradeCopies { get; set; } = 4;

    /// <summary>What a full set of ERN Efficiency Rate items is worth, as a
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

    /// <summary>The apworld version that generated this seed.
    ///
    /// EMPTY MEANS AN OLDER SEED, generated before the key existed, and is not a
    /// mismatch - it simply cannot be compared. See
    /// <see cref="VersionRules.Describe"/> for what is done with it.</summary>
    [JsonPropertyName("world_version")]
    public string WorldVersion { get; set; } = "";

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

    /// <summary>The strict-tier requirement for one location. A seed generated
    /// before the strict table existed sends none, and then the tier table IS
    /// the strict one - which is exactly right for a standard-logic seed and
    /// errs toward calling an unreachable check red rather than yellow for a
    /// casual one.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ForLocationStrict(string location)
    {
        var table = StrictLocationRequirements.Count > 0
            ? StrictLocationRequirements
            : LocationRequirements;
        return table.TryGetValue(location, out var g) ? g : NoGroups;
    }

    /// <summary>The objective slots this mission needs to be won. Empty for a
    /// seed generated before the table was sent, which simply means the
    /// completion-implies-objectives path stays off for it.</summary>
    public IReadOnlyList<int> RequiredObjectivesFor(string specifier)
        => RequiredObjectives.TryGetValue(specifier, out var v) ? v : NoSlots;

    private static readonly List<int> NoSlots = new();

    private static readonly List<List<string>> NoGroups = new();
}
