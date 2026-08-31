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
# Farsite is deliberately NOT in this list even though it is mission 1: its
# Custom objective and its rift jump both need a weapon, and while the worksheet
# says its FIRST cache is free, its second needs one - and instances of an
# objective share a rule, so the pair cannot be split. Nothing else requires the
# campaign to start at its beginning: missions are open, so any of these makes a
# perfectly good opening and picking at random varies the start between seeds.
#
# Archon is excluded despite waiving the weapon, because its caches are buried:
# they need a Terp and a Pylon, so they are not free.
#
# Kept in step with rules.WAIVES_MISSION_REQUIREMENTS by a test - rules.py cannot
# be imported here without a cycle (locations imports items).
STARTER_ELIGIBLE = (2, 3, 4, 5, 7, 10, 11, 13, 14)

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

FILLER_ITEMS = BUILD_LIMIT_ITEMS + [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Traps. Every effect is temporary and recoverable by design - a trap may sting,
# but none may make a mission unwinnable, which is why permanent terrain
# deformation was dropped during the feasibility spike. Names must match
# CW4Archipelago.Core.TrapRules exactly; a test pins that.
TRAP_ITEMS = [
    "Spore Strike",
    "Spore Scatter",
    "Creeper Surge",
    "Energy Drain",
    "Emitter Overdrive",
    "Unit Stun",
    "Ammo Drain",
]

_all_names = (
    MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS
    + [PROGRESSIVE_ERN] + FILLER_ITEMS + TRAP_ITEMS
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
    return ItemClassification.filler


def create_item(world, name: str) -> CW4Item:
    return CW4Item(name, classification(name), ITEM_NAME_TO_ID[name], world.player)




def force_early_mission(world) -> None:
    # Another free-cache mission, and not one already unlocked, so the opening
    # reliably widens beyond the starters.
    options = [n for n in STARTER_ELIGIBLE if n not in world.starter_missions]
    if not options:
        return
    name = f"Mission Unlock: {MISSION_TITLES[world.random.choice(options)]}"
    world.multiworld.early_items[world.player][name] = 1


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

    # Pad to the number of unfilled locations, drawing filler by the player's
    # weights. Round-robin used to do this, which ignored the weights entirely
    # and made the mix depend on how many real items happened to precede it.
    unfilled = len(world.multiworld.get_unfilled_locations(world.player))
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
    weights = {k: v for k, v in trap_weights(world).items() if v > 0}
    if not weights:
        return filler_sequence(world, count)
    kinds = list(weights)
    return world.random.choices(kinds, weights=[weights[k] for k in kinds], k=count)


def filler_weights(world) -> dict:
    return {
        ENERGY_STORAGE_ITEM: world.options.filler_energy_storage_weight.value,
        BASE_GENERATION_ITEM: world.options.filler_base_generation_weight.value,
        "build_limit": world.options.filler_build_limit_weight.value,
    }


def filler_sequence(world, count: int) -> list:
    """`count` filler item names, drawn by the configured weights.

    Falls back to build limits if a player zeroes every weight, because the pool
    still has to be filled - an empty draw would fail generation rather than
    respecting an unfillable preference.
    """
    weights = {k: v for k, v in filler_weights(world).items() if v > 0}
    if not weights:
        weights = {"build_limit": 1}
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
