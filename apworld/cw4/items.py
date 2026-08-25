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

# story1 starts unlocked; story2..story20 are items.
MISSION_UNLOCK_ITEMS = [f"Mission Unlock: {MISSION_TITLES[n]}" for n in range(2, 21)]

# Vanilla-schedule units that become items (riftlab/tower/pylon always available).
UNIT_ITEMS = [
    "Cannon", "Mortar", "Nullifier", "Miner", "Factory", "Greenar Refinery",
    "Missile Launcher", "Sprayer", "Terp", "ERN Portal", "Sniper", "Porter",
    "Bomber Pad", "Runway", "Shield", "AC Bomber Pad", "Chronat", "Microrift",
    "Platform", "Rocket Pad",
]

# Never unlocked in vanilla - pure bonus items.
BONUS_UNIT_ITEMS = ["Airship", "Bertha", "Sweeper"]

PROGRESSIVE_ERN = "Progressive ERN"
PROGRESSIVE_ERN_COUNT = 4

FILLER_ITEMS = [
    "Build Limit +1 (Tower)",
    "Build Limit +1 (Cannon)",
    "Build Limit +1 (Mortar)",
]

_all_names = (
    MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS
    + [PROGRESSIVE_ERN] + FILLER_ITEMS
)
ITEM_NAME_TO_ID = {name: BASE_ID + i for i, name in enumerate(_all_names)}


class CW4Item(Item):
    game = "Creeper World 4"


def classification(name: str) -> ItemClassification:
    if name in MISSION_UNLOCK_ITEMS or name in UNIT_ITEMS:
        return ItemClassification.progression
    if name in BONUS_UNIT_ITEMS or name == PROGRESSIVE_ERN:
        return ItemClassification.useful
    return ItemClassification.filler


def create_item(world, name: str) -> CW4Item:
    return CW4Item(name, classification(name), ITEM_NAME_TO_ID[name], world.player)


def create_all_items(world) -> None:
    pool = []
    for name in MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS:
        pool.append(create_item(world, name))
    for _ in range(PROGRESSIVE_ERN_COUNT):
        pool.append(create_item(world, PROGRESSIVE_ERN))
    # Pad to the number of unfilled locations with filler.
    unfilled = len(world.multiworld.get_unfilled_locations(world.player))
    while len(pool) < unfilled:
        pool.append(create_item(world, FILLER_ITEMS[len(pool) % len(FILLER_ITEMS)]))
    world.multiworld.itempool += pool


def get_filler_item_name(world) -> str:
    return world.random.choice(FILLER_ITEMS)
