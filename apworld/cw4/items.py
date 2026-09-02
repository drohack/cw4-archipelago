"""Item tables for Creeper World 4.

Source of truth: docs/randomizer-design.md in the repository root.
Skeleton status: names and counts are real per the design doc; the pool is
padded with build-limit filler to match the location count.
"""
from BaseClasses import Item, ItemClassification

BASE_ID = 4_040_000

MISSION_TITLES = {
    1: "Farsite", 2: "Home", 3: "Not My Mars", 4: "Ruins Repurposed",
    5: "We Know Nothing", 6: "We Were Never Alone", 7: "Hints", 8: "Serious",
    9: "More and More", 10: "War and Peace", 11: "Shattered", 12: "Archon",
    13: "The Experiment", 14: "Somewhere in Spacetime", 15: "Tower of Darkness",
    16: "The Compound", 17: "Sequence", 18: "Wallis", 19: "Founders",
    20: "Ever After",
}

# Missions eligible to START unlocked.
#
# The only real constraint is that SOMETHING must be reachable with an empty
# inventory, or the generator has nowhere to put a first item. That means a
# mission whose cache can be taken with the rift lab and a single tower, with no
# weapon and no other building.
#
# Farsite IS eligible, via a per-instance waiver. The worksheet splits its two
# caches - "first item can get with just tower, 2nd item needs weapon to get over
# creep" - and an earlier version excluded mission 1 outright because a per-TYPE
# waiver could not say that without also claiming the second cache was free.
# rules.WAIVES_INSTANCE says it properly, so Farsite can open a seed like any
# other mission. Nothing requires the campaign to start at its beginning either:
# missions are open, so picking at random varies the opening between seeds.
#
# Archon is excluded despite waiving the weapon, because its caches are buried:
# they need a Terp and a Pylon, so they are not free.
#
# Kept in step with rules.WAIVES_MISSION_REQUIREMENTS and rules.WAIVES_INSTANCE
# by a test - rules.py cannot be imported here without a cycle (locations imports
# items).
STARTER_ELIGIBLE = (1, 2, 3, 4, 5, 7, 10, 11, 13, 14)

# EVERY mission's unlock exists as an item name, including the ones that end up
# starting unlocked. Item ids must not depend on which missions a seed happens to
# start with - excluding starters here would shift every later id whenever a
# player changed the option.
MISSION_UNLOCK_ITEMS = [f"Mission Unlock: {MISSION_TITLES[n]}" for n in range(1, 21)]


def starter_missions(world) -> list:
    """The missions this seed starts with, chosen at random from the eligible
    set. Deterministic for a seed because it draws from the world's random."""
    count = min(world.options.starter_missions.value, len(STARTER_ELIGIBLE))
    return sorted(world.random.sample(list(STARTER_ELIGIBLE), count))

# Vanilla-schedule units that become items. Only riftlab and tower are always
# available - without a base and energy a mission cannot be started at all.
# Pylon is an unlockable (towers relay on their own).
UNIT_ITEMS = [
    "Cannon", "Mortar", "Nullifier", "Miner", "Factory", "Greenar Refinery",
    "Missile Launcher", "Sprayer", "Terp", "ERN Portal", "Sniper", "Porter",
    "Pylon", "Bomber Pad", "Runway", "Shield", "AC Bomber Pad", "Chronat",
    "Microrift", "Platform", "Rocket Pad",
]

# Never unlocked in vanilla - pure bonus items.
BONUS_UNIT_ITEMS = ["Airship", "Bertha", "Sweeper"]

PROGRESSIVE_ERN = "Progressive ERN"
PROGRESSIVE_ERN_COUNT = 4

# Every name that HAS an id. Like Emitter Overdrive these are deliberately still
# here and deliberately not generated - see POOL_FILLER_KINDS below. Dropping the
# names outright would renumber every id after them, and ids must not move.
BUILD_LIMIT_ITEMS = [
    "Build Limit +1 (Tower)",
    "Build Limit +1 (Cannon)",
    "Build Limit +1 (Mortar)",
]

# Energy upgrades, both applied to the rift lab - measured as the only real
# levers the game exposes (docs/research-findings.md, "Energy: the store is the
# rift lab's ammo"). Storage raises its MAX_AMMO, generation adds to its ammo
# each tick.
#
# The names carry NO amounts. The amounts are yaml options and travel in
# slot_data, because ITEM_NAME_TO_ID has to be identical across every yaml - a
# name like "Energy Storage +50" would change ids whenever a player retuned an
# option and break the client.
ENERGY_STORAGE_ITEM = "Energy Storage Upgrade"
BASE_GENERATION_ITEM = "Base Generation Upgrade"

# ERN port upgrade items. Two per upgrade, six upgrades, twelve names.
#
# Names must match CW4Archipelago.Core.ErnUpgradeRules exactly - the mod counts
# received items by these strings - and a test pins both halves.
#
# The two axes are deliberately separate, and measurement is why. An earlier
# version had the cap also double the fill rate, which left the rate item with
# nothing to sell; see docs/ern-upgrade-measurements.md.
#
#     ERN Efficiency Rate: <upgrade>   fills faster, 4x at four copies
#     ERN Efficiency Cap:  <upgrade>   reaches higher, 200% (150% Build Speed)
#
# Measured effect at the cap, all six confirmed in game:
#
#     Mine Production    3.00x production
#     Move Speed         about 2.8x
#     Fire Rate          2.00x rate (reload 8 -> 4)
#     Build Speed        1.88x (its own lower ceiling)
#     Energy Production  1.63x
#     Fire Range         1.50x range (cannon 9 -> 13 cells)
ERN_UPGRADE_NAMES_ORDER = [
    "Energy Production", "Mine Production", "Build Speed",
    "Move Speed", "Fire Range", "Fire Rate",
]
ERN_RATE_PREFIX = "ERN Efficiency Rate: "
ERN_CAP_PREFIX = "ERN Efficiency Cap: "

ERN_RATE_ITEMS = [ERN_RATE_PREFIX + u for u in ERN_UPGRADE_NAMES_ORDER]
ERN_CAP_ITEMS = [ERN_CAP_PREFIX + u for u in ERN_UPGRADE_NAMES_ORDER]
ERN_UPGRADE_ITEMS = ERN_RATE_ITEMS + ERN_CAP_ITEMS

# A FIFTH COPY DOES NOTHING, so four is the whole supply of each name.
#
# This is why they cannot go through filler_sequence: that draws with
# replacement by weight, so it could hand a player nine copies of one name -
# five of them inert - and none of another. An item that does nothing is the
# exact defect that got build limits pulled from the pool, so these are
# generated as FIXED counts instead.
ERN_UPGRADE_MAX_COPIES = 4

FILLER_ITEMS = BUILD_LIMIT_ITEMS + [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Build limits are NOT generated (designer, 2026-09-01).
#
# Every building starts at the game's "unlimited" sentinel of -1, so there is no
# limit to raise. UnitGate.ApplyLimits already refuses to touch those, and
# correctly: writing base+1 over an unlimited unit would CAP something that had no
# cap, turning a bonus item into a penalty. The consequence is that a build-limit
# item does nothing, on every unit, on every mission - not "nothing yet", nothing
# at all.
#
# That is the same rule Emitter Overdrive was removed under, only more so: it fails
# on the whole campaign rather than a third of it. At the default weights this was
# 24 of 256 items in a seed, so roughly one check in ten paid out a message with
# nothing behind it, and there was no in-game signal to notice.
#
# HELD LOOSELY, and cheap to reopen. Nothing else was removed: the ids, the
# UnitRules mapping, UnitGate's base-capture and increment, and the yaml weight all
# still work. If a mission is found that ships a real limit - or if limits are ever
# introduced deliberately, which is the more likely route - putting "build_limit"
# back in this list is the whole change.
POOL_FILLER_KINDS = [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Traps. Every effect is temporary and recoverable by design - a trap may sting,
# but none may make a mission unwinnable, which is why permanent terrain
# deformation was dropped during the feasibility spike. Names must match
# CW4Archipelago.Core.TrapRules exactly; a test pins that.
# Every trap that HAS an id. Emitter Overdrive is deliberately still here and
# deliberately not in POOL_TRAP_ITEMS below: dropping the name outright would
# renumber every id after it, and ids are the one thing that must not move.
TRAP_ITEMS = [
    "Spore Strike",
    "Spore Scatter",
    "Creeper Surge",
    "Energy Drain",
    "Emitter Overdrive",
    "Unit Stun",
    "Ammo Drain",
]

# Emitter Overdrive is NOT generated (designer, 2026-08-31).
#
# The traps spike set the rule that admits an effect to the pool: it must fire on
# essentially every mission, or carry a fallback for the ones it cannot. Its own
# reasoning for that rule is that "a trap item that silently does nothing is a bad
# item" - the player spends a check, receives a trap, nothing happens, and the
# whole trap pool starts to feel broken.
#
# Emitter Overdrive meets neither half. It no-ops where a mission ships no
# emitters, logging "no emitters on this map - trap had no effect", and it has no
# fallback. Emitters are present at mission START on 11 of 20 missions, so it is
# dead on roughly a quarter to a third of the campaign. Every other trap depends
# only on things every mission has: the world grid, the energy store, and the
# player's own units.
#
# HELD LOOSELY, and here is exactly what would reopen it. The 11-of-20 figure
# counts emitters at mission start only; enemies arrive during play, so real
# coverage is better and possibly much better. If someone measures emitter counts
# a few minutes in and it is more like 16 of 20, "essentially every mission"
# becomes arguable and this decision should be revisited. Nothing else was
# removed to make that easy: the effect, the applier mapping, the trap: debug
# command and the yaml weight all still work, so putting the name back in this
# list is the whole change.
POOL_TRAP_ITEMS = [t for t in TRAP_ITEMS if t != "Emitter Overdrive"]

# APPENDED, never inserted. Item ids are positional, and the client's
# ITEM_NAME_TO_ID has to match across every yaml - inserting a name anywhere but
# the end renumbers everything after it.
_all_names = (
    MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS
    + [PROGRESSIVE_ERN] + FILLER_ITEMS + TRAP_ITEMS
    + ERN_UPGRADE_ITEMS
)
ITEM_NAME_TO_ID = {name: BASE_ID + i for i, name in enumerate(_all_names)}


class CW4Item(Item):
    game = "Creeper World 4"


def classification(name: str) -> ItemClassification:
    # Mission unlocks gate their region (see regions.py), so they are always
    # progression even though they never appear in an access RULE.
    if name in MISSION_UNLOCK_ITEMS:
        return ItemClassification.progression

    if name in UNIT_ITEMS:
        # Only buildings that actually gate something are progression. The rest
        # are real benefits nobody is blocked on - marking them progression would
        # have the fill prioritise them and progression balancing drag them into
        # early spheres, crowding out the items that genuinely open the game.
        #
        # Imported late: locations.py imports this module, so a top-level import
        # of rules would be a cycle.
        from .rules import logic_item_names
        return (ItemClassification.progression if name in logic_item_names()
                else ItemClassification.useful)
    if name in BONUS_UNIT_ITEMS or name == PROGRESSIVE_ERN:
        return ItemClassification.useful
    # Energy upgrades are a real, measurable benefit - more building before you
    # stall - so they are useful rather than filler.
    if name in (ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM):
        return ItemClassification.useful
    if name in TRAP_ITEMS:
        return ItemClassification.trap
    # ERN upgrades are FILLER rather than useful, unlike the energy upgrades.
    #
    # The difference is that they do nothing at all until the player has been
    # given the ERN Portal unlock, built one, and docked an ERN in the matching
    # slot. That is real optional infrastructure, so an ERN upgrade arriving
    # early can sit dead for a long time, where an energy upgrade always pays
    # out immediately.
    #
    # HELD LOOSELY: the effects are large once live (Mine Production triples
    # production), so if these end up feeling like real rewards rather than
    # padding, promoting them to useful is a one-line change.
    if name in ERN_UPGRADE_ITEMS:
        return ItemClassification.filler
    return ItemClassification.filler


def create_item(world, name: str) -> CW4Item:
    return CW4Item(name, classification(name), ITEM_NAME_TO_ID[name], world.player)




# How many empty reachable locations the opening needs before Archipelago's fill
# is safe to take over. Two is not enough: one wasted placement still strands it.
SAFE_OPENING = 4

# Below this, the opening cannot absorb a single wasted placement and the world
# steps in. At or above it, Archipelago's fill is left alone.
SAFE_OPENING_MIN = 2


def force_early_mission(world) -> None:
    # Another free-cache mission, and not one already unlocked, so the opening
    # reliably widens beyond the starters.
    if opening_width(world) < SAFE_OPENING_MIN:
        return  # bootstrap_opening owns these slots
    options = [n for n in STARTER_ELIGIBLE if n not in world.starter_missions]
    if not options:
        return
    name = f"Mission Unlock: {MISSION_TITLES[world.random.choice(options)]}"
    world.multiworld.early_items[world.player][name] = 1


def opening_width(world) -> int:
    """How many locations are collectable from a standing start, holding nothing.

    This is what decides whether the seed can afford more than one early item.
    Imported late for the same reason `classification` does it: locations imports
    items, so a top-level import of either here would be a cycle.
    """
    from .locations import location_names_for_mission
    from .rules import is_casual, location_requirements
    casual = is_casual(world)
    return sum(
        1
        for mission in world.starter_missions
        for name in location_names_for_mission(mission)
        if not location_requirements(name, mission, casual)
    )


def force_early_weapon(world) -> None:
    """Guarantee which of the two offense weapons arrives first.

    OFFENSE is an OR group, so a seed only ever needs one of Cannon and Mortar -
    and the one it does not need becomes logically redundant and can turn up
    anywhere. Which one wins was previously the fill's business, measured at 13
    Cannon to 7 Mortar over 20 seeds, with the reversed-order control proving that
    a coin flip rather than an ordering bias.

    early_items is the right tool and the only honest one. Logic CANNOT express
    this preference: a rule saying a mission needs a mortar specifically would be
    false wherever a cannon also works, and logic is not the place to record a
    taste in pacing. Placement is.

    The same mechanism already forces a second mission unlock early, so a seed can
    ask for two early items; if the opening is narrow enough that there are not
    two free locations to hold them, Archipelago places what it can and the rest
    fall where they fall.
    """
    # "random" needs no branch here: Archipelago resolves it while parsing the
    # yaml, so by now the option holds mortar or cannon either way.
    opt = world.options.early_weapon
    name = "Mortar" if opt.value == opt.option_mortar else "Cannon"

    # If the opening is one location wide, that location must hold a MISSION
    # UNLOCK, and the weapon has to wait. This is not a preference, it is the
    # difference between a seed that generates and one that does not.
    #
    # Measured at starter_missions: 1, 60 seeds per setting:
    #
    #     early items requested        seeds that failed to generate
    #     mission unlock only          0 of 60
    #     nothing                      2 of 60
    #     both (unlock and weapon)     7 of 60
    #     weapon only                  13 of 60
    #
    # The reason is what each item opens. An unlock chains: it turns the one free
    # cache into another mission with a free cache, and so on, which is the
    # intended shape of a narrow opening. A weapon widens the mission you are
    # already in but adds no new MISSIONS, so the other nineteen unlocks all have
    # to thread through one mission's locations, and fill_restrictive runs out of
    # legal spots - always with exactly one item left over, typically a mission
    # whose own locations are the only ones still empty.
    #
    # Archipelago was already dropping one of the two here and saying so ("Ran out
    # of early locations for early items"), so the guarantee was never being kept
    # at this width. This makes which one gets dropped deterministic, and picks
    # the one that does not break generation.
    if opening_width(world) < SAFE_OPENING_MIN:
        # bootstrap_opening handles this width instead, and can still honour the
        # weapon - see there. Requesting it here would only claim the single
        # sphere-0 location and leave the chain nowhere to go.
        world.early_weapon = name
        return

    world.early_weapon = name
    world.multiworld.early_items[world.player][name] = 1


def bootstrap_opening(world) -> list:
    """Widen a one-location opening before the general fill runs.

    WHY THIS EXISTS. At `starter_missions: 1` exactly one check is reachable
    holding nothing, because every starter-eligible mission has exactly one cache
    collectable with a rift lab and a single tower. Archipelago's fill places
    progression items one at a time without looking ahead, which is fine when
    there is slack and fatal when there is none: one item that opens nothing ends
    the seed. Measured, that killed 12 percent of one-starter seeds before any of
    this, and 1.3 percent after the early-items half of the fix.

    A worked example, seed 20100, which is what this is written against:

        We Know Nothing - Cache 1         -> Mission Unlock: Somewhere in Spacetime
        Somewhere in Spacetime - Cache 1  -> Cannon
        Somewhere in Spacetime - Reclaim  -> Factory      <- opens nothing

    Totems want Greenar Refinery AND Factory, so a lone Factory is half a pair and
    unlocks nothing; 29 items were left with nowhere to go. Archipelago cannot see
    that the two are a pair, and it has no reason to.

    WHAT THIS DOES NOT DO is script the opening. The item is drawn at random from
    everything that would actually open something, and the location from every
    reachable empty one, so two seeds with the same starter still differ. It only
    refuses to pick an item that opens nothing while the opening is too narrow to
    survive a dud, and it stops as soon as there is slack - after that the fill
    is on its own, exactly as everywhere else.

    The chosen early weapon gets first refusal, so `early_weapon` still means
    something at this width whenever the weapon is one of the productive choices.
    On eight of the ten possible starters a weapon opens between one and five
    checks; on We Know Nothing and The Experiment it opens none, and there it will
    not be picked, because picking it would end the seed.
    """
    from BaseClasses import CollectionState

    mw = world.multiworld
    player = world.player

    def open_empty(state):
        return [loc for loc in mw.get_locations(player)
                if loc.item is None and loc.address is not None and loc.can_reach(state)]

    state = CollectionState(mw)
    placed = []
    for _ in range(SAFE_OPENING + 2):
        empty = open_empty(state)
        if len(empty) >= SAFE_OPENING:
            break
        # One candidate per NAME - the pool holds duplicates and they are
        # interchangeable here.
        seen = {}
        for item in mw.itempool:
            if item.player == player and item.advancement:
                seen.setdefault(item.name, item)
        productive = []
        for item in seen.values():
            trial = state.copy()
            trial.collect(item, prevent_sweep=False)
            if len(open_empty(trial)) > len(empty):
                productive.append(item)
        if not productive or not empty:
            break
        # The weapon the player asked for goes first when it is a real choice.
        wanted = [i for i in productive if i.name == getattr(world, "early_weapon", "")]
        item = wanted[0] if wanted else world.random.choice(productive)
        location = world.random.choice(empty)
        location.place_locked_item(item)
        mw.itempool.remove(item)
        state.collect(item, prevent_sweep=False)
        placed.append((location.name, item.name))
    return placed


def create_all_items(world) -> None:
    pool = []
    # No weapon is granted. Every weapon is a real check, so cannon, mortar and
    # sprayer each arrive when the multiworld decides rather than all at once.
    #
    # A starter mission's unlock is not in the pool - the player already has it -
    # but the ITEM still exists, so ids are unaffected.
    starters = {f"Mission Unlock: {MISSION_TITLES[n]}" for n in world.starter_missions}
    for name in MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS:
        if name in starters:
            continue
        pool.append(create_item(world, name))
    for _ in range(world.options.progressive_erns.value):
        pool.append(create_item(world, PROGRESSIVE_ERN))

    # ERN port upgrades, as FIXED counts before any weighted padding.
    #
    # Not part of filler_sequence on purpose: that draws with replacement, so it
    # could hand out nine copies of one name (five of them inert, since a fifth
    # copy does nothing) and none of another. Fixed counts guarantee every copy
    # generated is a copy that works.
    #
    # Clamped to what is actually left. At the default 4 copies this is 48
    # items, which fits comfortably, but a seed with few locations - a small
    # missions_for_finale, or heavy starter_missions - must not overflow its
    # own location count.
    unfilled = len(world.multiworld.get_unfilled_locations(world.player))
    ern_copies = world.options.ern_upgrade_copies.value
    for name in ERN_UPGRADE_ITEMS:
        for _ in range(min(ern_copies, ERN_UPGRADE_MAX_COPIES)):
            if len(pool) >= unfilled:
                break
            pool.append(create_item(world, name))

    # Pad to the number of unfilled locations, drawing filler by the player's
    # weights. Round-robin used to do this, which ignored the weights entirely
    # and made the mix depend on how many real items happened to precede it.
    remaining = max(0, unfilled - len(pool))

    # Split what is left between traps and useful upgrades.
    traps = remaining * world.options.trap_percentage.value // 100
    for name in trap_sequence(world, traps):
        pool.append(create_item(world, name))
    for name in filler_sequence(world, remaining - traps):
        pool.append(create_item(world, name))
    world.multiworld.itempool += pool


def trap_weights(world) -> dict:
    o = world.options
    return {
        "Spore Strike": o.trap_weight_spore_strike.value,
        "Spore Scatter": o.trap_weight_spore_scatter.value,
        "Creeper Surge": o.trap_weight_creeper_surge.value,
        "Energy Drain": o.trap_weight_energy_drain.value,
        # Read but unused while Emitter Overdrive is out of the pool - see
        # POOL_TRAP_ITEMS. Kept so the option keeps working if it goes back in,
        # and so an existing yaml naming it is not an error.
        "Emitter Overdrive": o.trap_weight_emitter_overdrive.value,
        "Unit Stun": o.trap_weight_unit_stun.value,
        "Ammo Drain": o.trap_weight_ammo_drain.value,
    }


def trap_sequence(world, count: int) -> list:
    """`count` trap names drawn by the configured weights.

    If a player zeroes every trap weight while still asking for traps, the slots
    become useful items rather than failing generation - an unfillable
    preference should degrade, not break the seed.
    """
    if count <= 0:
        return []
    weights = {k: v for k, v in trap_weights(world).items()
               if v > 0 and k in POOL_TRAP_ITEMS}
    if not weights:
        return filler_sequence(world, count)
    kinds = list(weights)
    return world.random.choices(kinds, weights=[weights[k] for k in kinds], k=count)


def filler_weights(world) -> dict:
    return {
        ENERGY_STORAGE_ITEM: world.options.filler_energy_storage_weight.value,
        BASE_GENERATION_ITEM: world.options.filler_base_generation_weight.value,
        # Read but unused while build limits are out of the pool - see
        # POOL_FILLER_KINDS. Kept so the option keeps working if they go back in,
        # and so an existing yaml naming it is not an error.
        "build_limit": world.options.filler_build_limit_weight.value,
    }


def filler_sequence(world, count: int) -> list:
    """`count` filler item names, drawn by the configured weights.

    Falls back to energy storage if a player zeroes every weight, because the pool
    still has to be filled - an empty draw would fail generation rather than
    respecting an unfillable preference. This used to fall back to build limits,
    which now means falling back to an item that does nothing.
    """
    weights = {k: v for k, v in filler_weights(world).items()
               if v > 0 and k in POOL_FILLER_KINDS}
    if not weights:
        weights = {ENERGY_STORAGE_ITEM: 1}
    kinds = list(weights)
    picks = world.random.choices(kinds, weights=[weights[k] for k in kinds], k=count)
    out = []
    for i, kind in enumerate(picks):
        if kind == "build_limit":
            out.append(BUILD_LIMIT_ITEMS[i % len(BUILD_LIMIT_ITEMS)])
        else:
            out.append(kind)
    return out


def get_filler_item_name(world) -> str:
    return filler_sequence(world, 1)[0]
