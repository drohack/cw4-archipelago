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
    BUILD_LIMIT_ITEMS,
    ENERGY_STORAGE_ITEM,
    MISSION_UNLOCK_ITEMS,
    POOL_TRAP_ITEMS,
    PROGRESSIVE_ERN,
    UNIT_ITEMS,
)
from .items import MISSION_TITLES
from .locations import (
    CUSTOM_MISSIONS,
    FINAL_MISSION,
    INSTANCE_COUNTS,
    RECLAIM_MISSIONS,
    instance_location_name,
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
    "Build Limits": set(BUILD_LIMIT_ITEMS),
    "Upgrades": {ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM, PROGRESSIVE_ERN},
}


def _mission_locations(n: int) -> set:
    """Every location belonging to one mission."""
    caches, totems, nullify = INSTANCE_COUNTS[n]
    names = set()
    for i in range(1, caches + 1):
        names.add(instance_location_name(n, "Cache", i))
    for i in range(1, totems + 1):
        names.add(instance_location_name(n, "Totem", i))
    for i in range(1, nullify + 1):
        names.add(instance_location_name(n, "Nullify", i))
    if n in RECLAIM_MISSIONS:
        names.add(single_location_name(n, "Reclaim"))
    if n in CUSTOM_MISSIONS:
        names.add(single_location_name(n, "Custom"))
    if n != FINAL_MISSION:
        names.add(mission_complete_location_name(n))
    return names


def _by_kind(kind: str) -> set:
    out = set()
    for n in range(1, 21):
        caches, totems, nullify = INSTANCE_COUNTS[n]
        count = {"Cache": caches, "Totem": totems, "Nullify": nullify}[kind]
        for i in range(1, count + 1):
            out.add(instance_location_name(n, kind, i))
    return out


# One group per mission, named exactly as the mission is - so a player can write
# `exclude_locations: [Tower of Darkness]` without listing fourteen checks - plus
# one per kind, for "I do not want to hunt every cache".
LOCATION_NAME_GROUPS = {MISSION_TITLES[n]: _mission_locations(n) for n in range(1, 21)}
LOCATION_NAME_GROUPS.update({
    "Caches": _by_kind("Cache"),
    "Totems": _by_kind("Totem"),
    "Nullify Targets": _by_kind("Nullify"),
    "Reclaim": {single_location_name(n, "Reclaim") for n in RECLAIM_MISSIONS},
    "Custom Objectives": {single_location_name(n, "Custom") for n in CUSTOM_MISSIONS},
    "Mission Completions": {mission_complete_location_name(n)
                            for n in range(1, 21) if n != FINAL_MISSION},
})
