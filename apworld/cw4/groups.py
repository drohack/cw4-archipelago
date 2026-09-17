"""Item and location name groups.

Archipelago lists these as encouraged features for a world, and they are not
cosmetic: a group name works anywhere an item or location name does. That means
`!hint Traps`, and yaml entries like `non_local_items: [Units]` or
`exclude_locations: [Founders]` - which is the difference between a player being
able to express "keep my units in my own world" in one line versus twenty-four.

Groups are derived from the same tables that build the pool, so they cannot drift
from the items that actually exist.
"""
from .items import (
    BASE_GENERATION_ITEM,
    BONUS_UNIT_ITEMS,
    ENERGY_STORAGE_ITEM,
    MISSION_UNLOCK_ITEMS,
    POOL_TRAP_ITEMS,
    PROGRESSIVE_ERN,
    UNIT_ITEMS,
)
from .items import ALL_MISSION_TITLES
from .locations import (
    ALL_CUSTOM_MISSIONS,
    ALL_INSTANCE_COUNTS,
    ALL_RECLAIM_MISSIONS,
    FINAL_MISSION,
    instance_location_name,
    location_names_for_mission,
    mission_complete_location_name,
    single_location_name,
)

# Weapons and economy are split by what the player uses them FOR, because that is
# how someone writing a yaml thinks about them - "put my weapons in my own world"
# is a sentence people say. Nullifier sits in weapons: it is a weapon in shape,
# even though logic wants it for objectives rather than for fighting.
_WEAPONS = ["Cannon", "Mortar", "Sprayer", "Sniper", "Missile Launcher", "Nullifier",
            "Bertha", "Bomber Pad", "AC Bomber Pad", "Rocket Pad", "Runway", "Airship"]
_ECONOMY = ["Miner", "Factory", "Greenar Refinery", "ERN Portal", "Porter", "Platform",
            "Pylon", "Terp", "Microrift", "Chronat", "Shield", "Sweeper"]

ITEM_NAME_GROUPS = {
    "Mission Unlocks": set(MISSION_UNLOCK_ITEMS),
    "Units": set(UNIT_ITEMS) | set(BONUS_UNIT_ITEMS),
    "Weapons": {n for n in _WEAPONS if n in set(UNIT_ITEMS) | set(BONUS_UNIT_ITEMS)},
    "Economy": {n for n in _ECONOMY if n in set(UNIT_ITEMS) | set(BONUS_UNIT_ITEMS)},
    "Traps": set(POOL_TRAP_ITEMS),
    # No "Build Limits" group: those items are not generated, so the group would
    # match nothing. Groups exist to be typed into a yaml or a !hint, and one that
    # silently resolves to an empty set is worse than a name that does not exist -
    # `exclude_locations` would appear to work and do nothing. Groups derive from
    # the POOL_ tables for exactly this reason, the way "Traps" does.
    "Upgrades": {ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM, PROGRESSIVE_ERN},
}


ALL_MISSION_NUMBERS = tuple(sorted(ALL_MISSION_TITLES))


def _mission_locations(n: int) -> set:
    """Every location belonging to one mission."""
    return set(location_names_for_mission(n))


def _by_kind(kind: str) -> set:
    out = set()
    for n in ALL_MISSION_NUMBERS:
        caches, totems, nullify = ALL_INSTANCE_COUNTS[n]
        count = {"Cache": caches, "Totem": totems, "Nullify": nullify}[kind]
        for i in range(1, count + 1):
            out.add(instance_location_name(n, kind, i))
    return out


# One group per mission, named exactly as the mission is - so a player can write
# `exclude_locations: [Tower of Darkness]` without listing fourteen checks - plus
# one per kind, for "I do not want to hunt every cache".
# SPAN missions get groups too. Without them the per-mission groups stop
# PARTITIONING the location set, so `exclude_locations: [<mission>]` would
# silently miss every SPAN check - which is what
# test_mission_groups_cover_every_location caught the moment SPAN locations
# existed.
LOCATION_NAME_GROUPS = {ALL_MISSION_TITLES[n]: _mission_locations(n)
                        for n in ALL_MISSION_NUMBERS}
LOCATION_NAME_GROUPS.update({
    "Caches": _by_kind("Cache"),
    "Totems": _by_kind("Totem"),
    "Nullify Targets": _by_kind("Nullify"),
    "Reclaim": {single_location_name(n, "Reclaim") for n in ALL_RECLAIM_MISSIONS},
    "Custom Objectives": {single_location_name(n, "Custom")
                          for n in ALL_CUSTOM_MISSIONS},
    "Mission Completions": {mission_complete_location_name(n)
                            for n in ALL_MISSION_NUMBERS if n != FINAL_MISSION},
})
