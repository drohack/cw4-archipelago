"""Options for Creeper World 4.

Everything tunable lives here with a sensible default - no hard-coded counts.
Archipelago Range options are integers, so fractional values travel as TENTHS
and percentages as whole percents; each docstring says which.
"""
from dataclasses import dataclass

from Options import Choice, OptionGroup, PerGameCommonOptions, Range


class EarlyWeapon(Choice):
    """Which weapon is guaranteed to arrive first.

    Cannon and Mortar are interchangeable in logic - every rule that wants offense
    accepts either - so which one a seed hands you first is decided by the fill,
    not by the logic. Measured over 20 seeds it is a clean coin flip, 10 to 10.
    (An earlier count said 13-7; it read the spoiler's playthrough, which lists
    only the items needed to WIN and so cannot see the redundant half of an OR
    pair. Reversing the pair in the rules produced identical seeds either way, so
    list order does not choose the winner.)

    A coin flip is fine, but it is not a choice. This makes it one. Mortar is the
    slower opening - it does the same work as a cannon with more effort - so
    picking it stretches the early game out; cannon is the brisk one.

    `random` is not defined here and does not need to be: Archipelago accepts it
    for any Choice and picks among the values below, per seed. Defining it is in
    fact forbidden - Options.py asserts "Choice option 'random' cannot be manually
    assigned" - which is why the default is the string rather than a value.

    WHAT IT DOES NOT DO is make the other weapon arrive later. That was measured
    wrong once and the mistake is worth recording: comparing "Cannon with no
    forcing" against "Cannon when Mortar is forced" compares an item that opens
    half the seeds against one that never does, and shows a large regression where
    almost nothing moved. Comparing BY ROLE over 20 seeds each:

                        opening weapon      second weapon
        no forcing      median 2 (1 to 4)   median 9, 67% in
        random          median 1 (always)   median 10, 75% in
        mortar          median 1 (always)   median 8, 67% in

    The second weapon lands about two thirds of the way in whatever you choose:
    that is a property of an OR pair, not of this option. What forcing buys is a
    weapon in the very first sphere instead of somewhere in the first four.

    This never changes LOGIC, only placement. A rule saying "this mission needs a
    mortar specifically" would be false wherever a cannon also works, so the honest
    lever is Archipelago's early-items placement, which is what this uses.
    """
    display_name = "Early Weapon"
    option_mortar = 0
    option_cannon = 1
    default = "random"


class ErnUpgradeCopies(Range):
    """How many copies of each ERN port upgrade item go in the pool.

    There are twelve names - a Rate and a Cap for each of the game's six ERN
    port upgrades - so the default of 4 puts 48 items in the pool.

    FOUR IS THE CEILING, not a preference: the fourth copy lands exactly on the
    upgrade's maximum and a fifth would do nothing at all. Lower it to spend
    fewer slots on ERN upgrades and more on energy upgrades and traps; set it to
    0 to leave them out entirely.

    These items do nothing until you have the ERN Portal unlock, a portal built,
    and an ERN docked in the matching slot - so a seed that keeps ERN Portal
    late will see them sit dead for a while.
    """
    display_name = "ERN Upgrade Copies"
    range_start = 0
    range_end = 4
    default = 4


class ErnRateMax(Range):
    """What four ERN Efficiency Rate copies are worth, as a percent of the
    game's own fill speed.

    400 means a slot that normally takes 3600 ticks to reach full efficiency
    fills in 900. Copies step evenly to this, so the fourth always lands exactly
    on it: at 400 they are 175, 250, 325, 400 percent.

    This axis only shortens how long you WAIT for an upgrade, never how strong
    it gets, so it is safe to be generous. 100 makes the items inert.
    """
    display_name = "ERN Efficiency Rate Maximum"
    range_start = 100
    range_end = 800
    default = 400


class ErnCapMax(Range):
    """How high four ERN Efficiency Cap copies let an upgrade reach, as a
    percent. 200 is double the game's own ceiling.

    Measured effect at 200 percent: Mine Production triples production, Move
    Speed is about 2.8x, Fire Rate doubles, Fire Range takes a cannon from 9 to
    13 cells, Energy Production is +62.5 percent.

    Build Speed has its own option below, because the game's build-time curve is
    far steeper than the others.
    """
    display_name = "ERN Efficiency Cap Maximum"
    range_start = 100
    range_end = 400
    default = 200


class ErnCapMaxBuildSpeed(Range):
    """The ERN Efficiency Cap maximum for BUILD SPEED only.

    It needs its own value because the game shortens build time steeply and
    non-linearly. Measured, with a 363-tick baseline:

        100 percent -> 186 ticks     the game's own ceiling
        150 percent ->  99 ticks     1.88x the 100 percent rate
        160 percent ->  78 ticks
        170 percent ->  54 ticks
        200 percent ->  33 ticks     about 11x base, and the curve floors out

    At the shared 200 percent this one item would dwarf every other upgrade, so
    the default is 150. Raising it is a deliberate choice, not a tuning nudge.
    """
    display_name = "ERN Efficiency Cap Maximum (Build Speed)"
    range_start = 100
    range_end = 400
    default = 150


class ProgressiveErns(Range):
    """How many Progressive ERN items go in the pool.

    ERNs are never required to finish a mission - they make one easier - so this
    is purely how much of the pool is spent on them. Every ERN takes a slot that
    would otherwise hold an energy upgrade or a build limit.
    """
    display_name = "Progressive ERNs"
    range_start = 0
    range_end = 40
    default = 4


class MissionsForFinale(Range):
    """How many other missions must be completable before the finale unlocks.

    Without this the finale is gated only by its own unlock and its own building
    requirements, and a seed could be won having played as few as nine of the
    twenty missions. Requiring a count spreads progression across the campaign by
    construction, so no seed collapses to a handful of missions.

    Counts missions other than Founders, so the maximum is 19. Set to 0 for the
    old behaviour.
    """
    display_name = "Missions Required For Finale"
    range_start = 0
    range_end = 19
    default = 12


class LogicDifficulty(Choice):
    """How much defensive equipment logic assumes you have.

    Standard: only what is genuinely needed to WIN. Snipers and missile launchers
    are survival aids the worksheet repeatedly calls nice-to-have, so they gate
    nothing (except The Compound, whose saw blades die to nothing else). Lean,
    but a seed may legally hand you anti-air in the finale and leave the middle
    of the campaign a grind.

    Casual: additionally assumes anti-air - a Sniper or a Missile Launcher - from
    We Were Never Alone onward, the first mission with spores. Because that is
    real logic rather than a suggestion, Archipelago guarantees one is obtainable
    BEFORE those missions are in logic, which pulls it into an earlier sphere.
    """
    display_name = "Logic Difficulty"
    option_standard = 0
    option_casual = 1
    default = 0


class StarterMissions(Range):
    """How many missions start already unlocked.

    They are drawn at random from the missions whose cache can be collected with
    no weapon, so a seed always has something to do from the first minute. More
    starters means a broader opening but fewer unlock items in the pool.
    """
    display_name = "Starter Missions"
    range_start = 1
    range_end = 6
    default = 2


class TrapPercentage(Range):
    """Share of the non-progression slots that are traps.

    The pool has far more locations than real items, and the leftovers are split
    between traps and useful upgrades. Every trap is temporary and recoverable by
    design - none can make a mission unwinnable - but 50 percent is a lot of
    them in a solo game, so lower this if they grate.
    """
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 100
    default = 50


class TrapWeightSporeStrike(Range):
    """Relative weight of Spore Strike, which drops spores on one of your buildings."""
    display_name = "Trap Weight: Spore Strike"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightSporeScatter(Range):
    """Relative weight of Spore Scatter, which drops spores at random."""
    display_name = "Trap Weight: Spore Scatter"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightCreeperSurge(Range):
    """Relative weight of Creeper Surge, a dump of creeper near your base."""
    display_name = "Trap Weight: Creeper Surge"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightEnergyDrain(Range):
    """Relative weight of Energy Drain, which empties your energy store."""
    display_name = "Trap Weight: Energy Drain"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightEmitterOverdrive(Range):
    """Relative weight of Emitter Overdrive, a timed boost to enemy emitters.

    CURRENTLY UNUSED: this trap is not generated, so the weight has no effect.
    It does nothing on missions that have no emitters, which is a third of the
    campaign, and a trap that silently does nothing is worse than no trap. The
    option is kept so that an existing yaml naming it is not an error, and so it
    starts working again if the trap returns.
    """
    display_name = "Trap Weight: Emitter Overdrive"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightUnitStun(Range):
    """Relative weight of Unit Stun, which briefly disables your units."""
    display_name = "Trap Weight: Unit Stun"
    range_start = 0
    range_end = 100
    default = 100


class TrapWeightAmmoDrain(Range):
    """Relative weight of Ammo Drain, which empties your weapons' ammo."""
    display_name = "Trap Weight: Ammo Drain"
    range_start = 0
    range_end = 100
    default = 100


class EnergyStorageStep(Range):
    """Energy capacity granted by the FIRST Progressive Energy Storage.

    Raises the rift lab's storage ceiling, which is the buffer construction draws
    from - a bigger buffer means more sustained building before you stall. The
    campaign starts with a ceiling of 100, so the default is a substantial boost.
    """
    display_name = "Energy Storage Step"
    range_start = 10
    range_end = 500
    default = 50


class EnergyStorageDecay(Range):
    """Percent of the previous upgrade that each LATER storage upgrade grants.

    Storage has diminishing returns, so copies are worth progressively less: at
    the default 80 percent, a 50-point step gives 50, 40, 32, 25 and so on. Set
    to 100 for a flat bonus per copy.
    """
    display_name = "Energy Storage Decay (percent)"
    range_start = 10
    range_end = 100
    default = 80


class BaseGenerationStart(Range):
    """Energy per second granted by the FIRST Progressive Base Generation, in TENTHS.

    The default of 5 is +0.5/sec. A fresh mission generates about 1/sec, so the
    first copy is already a noticeable lift.
    """
    display_name = "Base Generation Start (tenths per second)"
    range_start = 1
    range_end = 20
    default = 5


class BaseGenerationRamp(Range):
    """How much MORE each later generation upgrade grants, in TENTHS per second.

    Generation ramps rather than staying flat: at the default, copies grant
    0.5, 0.7, 0.9 and so on, so a long game keeps feeling like progress. Set to 0
    for a flat bonus per copy.
    """
    display_name = "Base Generation Ramp (tenths per second)"
    range_start = 0
    range_end = 20
    default = 2


class FillerEnergyStorageWeight(Range):
    """Relative weight of Progressive Energy Storages among the filler items."""
    display_name = "Filler Weight: Energy Storage"
    range_start = 0
    range_end = 100
    default = 40


class FillerBaseGenerationWeight(Range):
    """Relative weight of Progressive Base Generations among the filler items."""
    display_name = "Filler Weight: Base Generation"
    range_start = 0
    range_end = 100
    default = 40


class FillerBuildLimitWeight(Range):
    """Relative weight of Build Limit increases among the filler items.

    CURRENTLY UNUSED: build limits are not generated, so the weight has no effect.
    Every building starts unlimited, so there is no limit for a "+1" to raise and
    the item does nothing on any unit on any mission. The option is kept so that an
    existing yaml naming it is not an error, and so it starts working again if
    build limits return.
    """
    display_name = "Filler Weight: Build Limits"
    range_start = 0
    range_end = 100
    default = 20


@dataclass
class CW4Options(PerGameCommonOptions):
    missions_for_finale: MissionsForFinale
    logic_difficulty: LogicDifficulty
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
    energy_storage_step: EnergyStorageStep
    energy_storage_decay: EnergyStorageDecay
    base_generation_start: BaseGenerationStart
    base_generation_ramp: BaseGenerationRamp
    filler_energy_storage_weight: FillerEnergyStorageWeight
    filler_base_generation_weight: FillerBaseGenerationWeight
    filler_build_limit_weight: FillerBuildLimitWeight


# Eighteen options in one flat list is a wall. Grouped, the webhost shows the
# three a player has to decide about first and files the tuning behind them.
# Declared here rather than in the world class, matching how the worlds in the
# Archipelago tree do it (messenger, blasphemous, ahit).
option_groups = [
    OptionGroup("Goal and Logic", [
        MissionsForFinale,
        LogicDifficulty,
        StarterMissions,
        EarlyWeapon,
    ]),
    OptionGroup("Traps", [
        TrapPercentage,
        TrapWeightSporeStrike,
        TrapWeightSporeScatter,
        TrapWeightCreeperSurge,
        TrapWeightEnergyDrain,
        TrapWeightEmitterOverdrive,
        TrapWeightUnitStun,
        TrapWeightAmmoDrain,
    ], start_collapsed=True),
    OptionGroup("Item Pool", [
        ProgressiveErns,
        ErnUpgradeCopies,
        FillerEnergyStorageWeight,
        FillerBaseGenerationWeight,
        FillerBuildLimitWeight,
    ], start_collapsed=True),
    OptionGroup("ERN Upgrades", [
        ErnRateMax,
        ErnCapMax,
        ErnCapMaxBuildSpeed,
    ], start_collapsed=True),
    OptionGroup("Energy Upgrades", [
        EnergyStorageStep,
        EnergyStorageDecay,
        BaseGenerationStart,
        BaseGenerationRamp,
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
