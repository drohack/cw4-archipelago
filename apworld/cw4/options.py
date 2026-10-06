"""Options for Creeper World 4.

Everything tunable lives here with a sensible default - no hard-coded counts.
Archipelago Range options are integers, so fractional values travel as TENTHS
and percentages as whole percents; each docstring says which.

HOUSE RULE: AN OPTION'S DOCSTRING IS FOR THE PLAYER. Archipelago shows it as the
website tooltip and writes it into every generated template ("Options'
docstrings are used as their user-facing documentation", docs/options api.md).
So it says what the option does and what values it takes, in a few sentences.
Why a default is what it is, what was measured and what was tried goes in the
comment above the class, which no player ever has to scroll past. The shipped
yaml is player.yaml, shorter still, and TestPlayerYaml holds it to these
classes.
"""
from dataclasses import dataclass

from Options import Choice, OptionGroup, PerGameCommonOptions, Range, Toggle, Visibility


# Cannon and Mortar are interchangeable in logic - every rule that wants offense
# accepts either - so which one a seed hands you first was decided by the fill,
# not by the logic. Measured over 20 seeds it is a clean coin flip, 10 to 10.
# (An earlier count said 13-7; it read the spoiler's playthrough, which lists
# only the items needed to WIN and so cannot see the redundant half of an OR
# pair. Reversing the pair in the rules produced identical seeds either way, so
# list order does not choose the winner.) A coin flip is fine, but it is not a
# choice; this makes it one.
#
# `random` is not defined here and does not need to be: Archipelago accepts it
# for any Choice and picks among the values below, per seed. Defining it is in
# fact forbidden - Options.py asserts "Choice option 'random' cannot be manually
# assigned" - which is why the default is the string rather than a value.
#
# WHAT IT DOES NOT DO is make the other weapon arrive later. That was measured
# wrong once and the mistake is worth recording: comparing "Cannon with no
# forcing" against "Cannon when Mortar is forced" compares an item that opens
# half the seeds against one that never does, and shows a large regression where
# almost nothing moved. Comparing BY ROLE over 20 seeds each (2026-09-01, before
# the own fill existed - see below):
#
#                     opening weapon      second weapon
#     no forcing      median 2 (1 to 4)   median 9, 67% in
#     random          median 1 (always)   median 10, 75% in
#     mortar          median 1 (always)   median 8, 67% in
#
# The second weapon lands about two thirds of the way in whatever you choose:
# that is a property of an OR pair, not of this option.
#
# This never changes LOGIC, only placement. A rule saying "this mission needs a
# mortar specifically" would be false wherever a cannon also works, so the
# honest lever is Archipelago's early-items placement, which is what this uses.
#
# FROM 2026-09-03 TO 2026-10-06 IT DID NOTHING ON SOLO SEEDS: our own
# progression fill placed every CW4 item before Archipelago's early step looked
# for them. See opening.force_early_weapon and opening.place_own_progression.
class EarlyWeapon(Choice):
    """Which weapon you are guaranteed first: Cannon or Mortar.

    Either can do everything the logic asks of a weapon, so this only picks
    your opening. The chosen one is placed in a check you can reach at the
    very start. Mortar is the slower start, Cannon the brisker one. The other
    weapon still turns up later in the seed.
    """
    display_name = "Early Weapon"
    option_mortar = 0
    option_cannon = 1
    default = "random"


# FOUR IS THE CEILING, not a preference: the fourth copy lands exactly on the
# upgrade's maximum and a fifth would do nothing at all.
#
# THE DEFAULT IS 2, NOT 4, AND THAT IS MEASURED. The ERN Portal gates nothing in
# logic, so it is placed at random; over 40 seeds it landed at median sphere 11
# of about 18. At 4 copies that put 48 items in the pool and an average of 21.6
# of them - 45 percent - arrived BEFORE the portal that makes them work, with a
# worst seed of 47 of 48. The designer hit exactly this in play (2026-09-14):
# "i was getting ERN upgrades, but never got the ERN port ... the ERN upgrades
# to the port itself were useless." At 2 copies that is 24 items, and the slots
# freed go to the energy upgrades, which pay out the moment they arrive.
#
# THE MAXIMA DO NOT MOVE. The per-copy step is the maximum divided by this
# count, so the last copy always lands exactly on ern_rate_max / ern_cap_max.
# It did not always work that way: the plugin used to divide by a hardcoded 4
# because the count was never sent to it, so lowering this silently lowered the
# CEILING - 2 copies capped efficiency at 150 percent and rate at 250. The count
# now travels in slot data (ern_upgrade_copies). A seed generated before that
# change sends no key and the plugin falls back to 4, which is what those seeds
# were built with.
class ErnUpgradeCopies(Range):
    """How many copies of each ERN port upgrade are in the pool, 0 to 4.

    There are twelve upgrades: a Rate and a Cap for each of the six ERN port
    slots. More copies means smaller steps to the same maximum, not more
    power, and 0 leaves them out. They do nothing until you have built an ERN
    Portal and docked an ERN in the matching slot.
    """
    display_name = "ERN Upgrade Copies"
    range_start = 0
    range_end = 4
    default = 2


class ErnRateMax(Range):
    """How fast ERN upgrades fill once you hold every Efficiency Rate copy, as
    a percent of the game's own speed, 100 to 800.

    400 fills four times as fast. This shortens the wait, never the strength,
    so it is safe to be generous. 100 makes the Rate items do nothing.
    """
    display_name = "ERN Efficiency Rate Maximum"
    range_start = 100
    range_end = 800
    default = 400


# Measured effect at 200 percent: Mine Production triples production, Move
# Speed is about 2.8x, Fire Rate doubles, Fire Range takes a cannon from 9 to
# 13 cells, Energy Production is +62.5 percent.
class ErnCapMax(Range):
    """How strong an ERN upgrade can get once you hold every Efficiency Cap
    copy, as a percent of the game's own ceiling, 100 to 400.

    At 200, Mine Production triples, Fire Rate doubles and a cannon's range
    goes from 9 to 13 cells. Build Speed has its own setting.
    """
    display_name = "ERN Efficiency Cap Maximum"
    range_start = 100
    range_end = 400
    default = 200


# It needs its own value because the game shortens build time steeply and
# non-linearly. Measured, with a 363-tick baseline:
#
#     100 percent -> 186 ticks     the game's own ceiling
#     150 percent ->  99 ticks     1.88x the 100 percent rate
#     160 percent ->  78 ticks
#     170 percent ->  54 ticks
#     200 percent ->  33 ticks     about 11x base, and the curve floors out
#
# At the shared 200 percent this one item would dwarf every other upgrade, so
# the default is 150.
class ErnCapMaxBuildSpeed(Range):
    """The same ceiling for Build Speed only, 100 to 400.

    The game's build-time curve is steep: 150 already builds almost twice as
    fast as the game's own ceiling, and 200 about eleven times the base speed.
    """
    display_name = "ERN Efficiency Cap Maximum (Build Speed)"
    range_start = 100
    range_end = 400
    default = 150


class ProgressiveErns(Range):
    """How many Progressive ERN items are in the pool, 0 to 40.

    ERNs make a mission easier but are never required, so this only decides
    how much of the pool they take.
    """
    display_name = "Progressive ERNs"
    range_start = 0
    range_end = 40
    default = 4


# Without this the finale is gated only by its own unlock and its own building
# requirements, and a seed could be won having played as few as nine of the
# twenty missions. Requiring a count spreads progression across the campaign by
# construction. Counts missions other than Founders, so the maximum is 19.
class MissionsForFinale(Range):
    """How many other missions you must be able to beat before the finale,
    Founders, can be won, 0 to 19.

    This spreads a seed across the campaign rather than letting it be won in
    a handful of missions. 0 turns the requirement off.
    """
    display_name = "Missions Required For Finale"
    range_start = 0
    range_end = 19
    default = 12


# Standard is lean on purpose: snipers and missile launchers are survival aids
# the worksheet repeatedly calls nice-to-have, so they gate nothing (except The
# Compound, whose saw blades die to nothing else). A standard seed may legally
# hand you anti-air in the finale and leave the middle of the campaign a grind.
# Casual's anti-air is real logic rather than a suggestion, so Archipelago
# guarantees one is obtainable BEFORE those missions are in logic, which pulls
# it into an earlier sphere.
#
# Easy (droha, 2026-10-06) asks what casual asks until The Experiment, the
# designer's "small spike in map difficulty", and from there all four of
# Sniper, Missile Launcher, Cannon and Mortar. Mission numbers are STORY
# numbers, as casual's are, so every SPAN map gets all four; and it applies to
# every check on those missions except the free caches, again as casual does.
# See rules.EASY_FULL_FROM. The values run in the order they assume more, and 2
# is not a reordering of 0 or 1, so no existing yaml changes meaning.
class LogicDifficulty(Choice):
    """How much the logic assumes you have before it expects a mission.

    standard: only what you need to win it.
    casual: also a Sniper or Missile Launcher from We Were Never Alone
    (mission 6), the first mission with spores, onward - so anti-air arrives
    earlier.
    easy: as casual, and from The Experiment (mission 13) onward both a
    Sniper and a Missile Launcher, and both a Cannon and a Mortar.
    """
    display_name = "Logic Difficulty"
    option_standard = 0
    option_casual = 1
    option_easy = 2
    default = 0


# OFF BY DEFAULT, AND THE REASON MATTERS. Every campaign requirement came from
# playing the mission. The SPAN requirements were DERIVED - objective counts and
# totem resources read out of the game, movers computed from terrain - and the
# detectors were validated against the campaign before being trusted here, but
# derived is not played. Three things no measurement can see:
#
#   - creep advance, so a route that exists on an empty map may be gone by the
#     time you need it
#   - reach requirements that are not about terrain at all (Archon needs a
#     Pylon on a map that is 100 percent land)
#   - environmental hazards (Archon again: it rains, so one of its caches sits
#     inside the starting shield and the other does not)
#
# Logic is therefore conservative - it over-requires rather than under-requires.
# docs/design/span-requirements-worksheet.md is how a player reports back.
class SpanMissions(Toggle):
    """Mix the game's 26 SPAN Experiment maps in with the campaign.
    Experimental: untested, but playable.

    The level select still holds 20 missions and the goal is still Founders;
    the other 19 are drawn from the campaign and the SPAN maps together. No
    one has finished a seed with these yet, and their logic is cautious, so
    expect a seed harder than it needs to be rather than one that cannot be
    finished.
    """
    display_name = "SPAN Experiments"


# MINIMUM IS 2, AND USED TO BE 1. One starter gives exactly one location
# reachable with no items, and Archipelago's fill places progression one item at
# a time without looking ahead: a single item that opens nothing ends the seed.
# That was measured at 12 percent of one-starter seeds originally, cut to about
# 1.3 percent by early items and bootstrap_opening, and after the 2026-09-03
# logic review it sat at 0.25 percent - still one seed in 400 failing to
# generate.
#
# Two further fixes were measured against it. Merging the greenar pair into one
# item (the campaign's biggest dud class) took one starter to 0.25 percent and
# TWO starters to 0.014 percent - one failure in 7200 seeds. Additionally
# forcing one starter to be a mission a weapon can open took one starter to
# 0.056 percent, still not zero, and bought nothing at two starters, so it was
# not adopted - it would have cost the varied openings that make random
# starters interesting.
#
# So one starter is not supportable and the floor is 2. The designer's rule for
# this, 2026-09-03: "we want to try for a default of 2, and get 1 as a
# possibility without errors. But if 1 is too hard then limit the minimum to 2."
class StarterMissions(Range):
    """How many missions start unlocked, 2 to 6.

    They are drawn at random from the missions whose first cache needs no
    weapon, so there is always something to do from the first minute. More
    starters means a wider opening and fewer unlock items in the pool.
    """
    display_name = "Starter Missions"
    range_start = 2
    range_end = 6
    default = 2


# The pool has far more locations than real items, and the leftovers are split
# between traps and useful filler. Every trap is temporary and recoverable by
# design - none can make a mission unwinnable.
class TrapPercentage(Range):
    """Percent of the filler slots that hold a trap, 0 to 100.

    Every trap is temporary and none can make a mission unwinnable, but 50 is
    a lot of them in a solo game; lower it if they grate. 0 means no traps.
    """
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 100
    default = 50


class TrapWeightSporeStrike(Range):
    """Relative weight of Spore Strike Trap, which drops spores on one of your
    buildings. 0 to 100; 0 means never."""
    display_name = "Trap Weight: Spore Strike"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightSporeScatter(Range):
    """Relative weight of Spore Scatter Trap, which drops spores at random.
    0 to 100; 0 means never."""
    display_name = "Trap Weight: Spore Scatter"
    range_start = 0
    range_end = 100
    default = 100


# The yaml key still says creeper_surge: the item was renamed on 2026-09-07
# because "surge" reads as emitters ramping up, which is not what it does, but
# renaming the OPTION would invalidate every existing yaml for no benefit.
class TrapWeightCreeperSurge(Range):
    """Relative weight of Rift Breach Trap, a slab of creeper that lands a
    short way from your rift lab. 0 to 100; 0 means never."""
    display_name = "Trap Weight: Rift Breach"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightEnergyDrain(Range):
    """Relative weight of Energy Drain Trap, which empties your energy store.
    0 to 100; 0 means never."""
    display_name = "Trap Weight: Energy Drain"
    range_start = 0
    range_end = 100
    default = 100


# CURRENTLY UNUSED: this trap is not generated, so the weight has no effect. It
# does nothing on missions that have no emitters, which is a third of the
# campaign, and a trap that silently does nothing is worse than no trap. Kept so
# an existing yaml naming it is not an error, and so it starts working again if
# the trap returns. Hidden from the website and templates until then: offering a
# setting that does nothing is noise.
class TrapWeightEmitterOverdrive(Range):
    """Relative weight of Emitter Overdrive Trap. Currently unused: this trap is
    not generated."""
    display_name = "Trap Weight: Emitter Overdrive"
    range_start = 0
    range_end = 100
    default = 100
    visibility = Visibility.none


class TrapWeightUnitStun(Range):
    """Relative weight of Unit Stun Trap, which briefly disables your units.
    0 to 100; 0 means never."""
    display_name = "Trap Weight: Unit Stun"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightAmmoDrain(Range):
    """Relative weight of Ammo Drain Trap, which empties your weapons' ammo.
    0 to 100; 0 means never."""
    display_name = "Trap Weight: Ammo Drain"
    range_start = 0
    range_end = 100
    default = 100


# The rift lab's own store is about 100, so the 900 ceiling is roughly 1000
# total. Paired with the copy count: the per-copy value is derived from the two,
# so the last copy lands exactly on this maximum and no copy is ever wasted.
class EnergyStorageMax(Range):
    """How much more energy the rift lab can store once you hold every
    Progressive Energy Storage, 0 to 900.

    This is how much you can bank, not how fast it arrives. The rift lab
    stores about 100 on its own.
    """
    display_name = "Energy Storage Maximum"
    range_start = 0
    range_end = 900
    default = 200


# RAISED FROM 8 TO 20 to absorb the slots freed by halving ern_upgrade_copies.
# Because the per-copy value is the maximum divided by this count, more copies
# costs nothing and creates no dead items: the same total benefit arrives in
# smaller, more frequent pieces, and every piece pays out immediately instead of
# waiting on an ERN Portal. That is the whole trade - 24 items that might do
# nothing for half the run, swapped for 24 that never do nothing.
class EnergyStorageCopies(Range):
    """How many Progressive Energy Storage items are in the pool, 0 to 36.

    The maximum above is split evenly between them, so more copies means
    smaller, more frequent steps to the same total.
    """
    display_name = "Energy Storage Copies"
    range_start = 0
    range_end = 36
    default = 20


# For scale, CW4's own production is about 3 to 4 energy/sec, so the default of
# 10 roughly triples the economy at full stack and the 100 ceiling is a cheat
# setting.
class BaseGenerationMax(Range):
    """How much extra energy per second the rift lab makes once you hold every
    Progressive Base Generation, 0 to 100.

    The game's own production is about 3 to 4 per second, so the default of
    10 roughly triples it at full stack.
    """
    display_name = "Base Generation Maximum"
    range_start = 0
    range_end = 100
    default = 10


# Raised from 8 to 20 alongside energy_storage_copies, and for the same reason.
# 0.5 energy/sec per copy against CW4's own 3 to 4/sec is still a step you feel,
# and it lands the moment the item does.
class BaseGenerationCopies(Range):
    """How many Progressive Base Generation items are in the pool, 0 to 36.

    The maximum above is split evenly between them: 10 over 20 copies is 0.5
    per second each.
    """
    display_name = "Base Generation Copies"
    range_start = 0
    range_end = 36
    default = 20


# CURRENTLY UNUSED, like the two weights below it. The energy upgrades have
# their own copy-count options now (energy_storage_copies), because both curves
# are capped and the count that reaches the cap is the only count worth
# generating - a weight cannot express that. Kept so an existing yaml naming it
# is not an error; hidden from the website and templates.
class FillerEnergyStorageWeight(Range):
    """Currently unused: the count comes from Energy Storage Copies."""
    display_name = "Filler Weight: Energy Storage"
    range_start = 0
    range_end = 100
    default = 40
    visibility = Visibility.none


# CURRENTLY UNUSED: the count comes from base_generation_copies. See
# FillerEnergyStorageWeight.
class FillerBaseGenerationWeight(Range):
    """Currently unused: the count comes from Base Generation Copies."""
    display_name = "Filler Weight: Base Generation"
    range_start = 0
    range_end = 100
    default = 40
    visibility = Visibility.none


# CURRENTLY UNUSED: build limits are not generated, so the weight has no effect.
# Every building starts unlimited, so there is no limit for a "+1" to raise and
# the item does nothing on any unit on any mission. Kept so that an existing
# yaml naming it is not an error, and so it starts working again if build limits
# return; hidden from the website and templates until then.
class FillerBuildLimitWeight(Range):
    """Currently unused: build limits are not generated."""
    display_name = "Filler Weight: Build Limits"
    range_start = 0
    range_end = 100
    default = 20
    visibility = Visibility.none


# The declaration order is NOT the display order - option_groups below is. Keep
# this order as it is: Generate rolls weighted yaml options in this order, so
# moving a line would change which value a weighted yaml gets for a given seed.
@dataclass
class CW4Options(PerGameCommonOptions):
    missions_for_finale: MissionsForFinale
    logic_difficulty: LogicDifficulty
    span_missions: SpanMissions
    starter_missions: StarterMissions
    early_weapon: EarlyWeapon
    progressive_erns: ProgressiveErns
    ern_upgrade_copies: ErnUpgradeCopies
    ern_rate_max: ErnRateMax
    ern_cap_max: ErnCapMax
    ern_cap_max_build_speed: ErnCapMaxBuildSpeed
    trap_percentage: TrapPercentage
    trap_weight_spore_strike: TrapWeightSporeStrike
    trap_weight_spore_scatter: TrapWeightSporeScatter
    trap_weight_creeper_surge: TrapWeightCreeperSurge
    trap_weight_energy_drain: TrapWeightEnergyDrain
    trap_weight_emitter_overdrive: TrapWeightEmitterOverdrive
    trap_weight_unit_stun: TrapWeightUnitStun
    trap_weight_ammo_drain: TrapWeightAmmoDrain
    energy_storage_max: EnergyStorageMax
    energy_storage_copies: EnergyStorageCopies
    base_generation_max: BaseGenerationMax
    base_generation_copies: BaseGenerationCopies
    filler_energy_storage_weight: FillerEnergyStorageWeight
    filler_base_generation_weight: FillerBaseGenerationWeight
    filler_build_limit_weight: FillerBuildLimitWeight


# The order a player reads them in, on the website and in player.yaml alike:
# the run first, then what the pool holds, most noticeable first. Archipelago
# puts its own progression_balancing and accessibility above all of these, in a
# "Game Options" group of everything no group claims. Declared here
# rather than in the world class, matching how the worlds in the Archipelago
# tree do it (messenger, blasphemous, ahit). The hidden options appear in no
# group; Archipelago filters them out of every view anyway.
option_groups = [
    OptionGroup("Goal and Logic", [
        MissionsForFinale,
        LogicDifficulty,
        StarterMissions,
        EarlyWeapon,
        SpanMissions,
    ]),
    OptionGroup("Traps", [
        TrapPercentage,
        TrapWeightSporeStrike,
        TrapWeightSporeScatter,
        TrapWeightCreeperSurge,
        TrapWeightEnergyDrain,
        TrapWeightUnitStun,
        TrapWeightAmmoDrain,
    ], start_collapsed=True),
    OptionGroup("Energy Upgrades", [
        EnergyStorageMax,
        EnergyStorageCopies,
        BaseGenerationMax,
        BaseGenerationCopies,
    ], start_collapsed=True),
    OptionGroup("ERNs", [
        ProgressiveErns,
        ErnUpgradeCopies,
        ErnRateMax,
        ErnCapMax,
        ErnCapMaxBuildSpeed,
    ], start_collapsed=True),
]


# The three things people ask for before they have played a seed. Each is a
# complete answer rather than a hint, so a player can pick one and generate.
options_presets = {
    "No traps": {
        "trap_percentage": 0,
    },
    "Relaxed": {
        "logic_difficulty": "casual",
        "trap_percentage": 15,
        "starter_missions": 4,
    },
    "Short campaign": {
        "missions_for_finale": 6,
        "starter_missions": 4,
        "trap_percentage": 25,
    },
}
