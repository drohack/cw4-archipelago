"""Location tables for Creeper World 4.

CW4 missions have six fixed objective slots, indexed by type:
  0 Nullify, 1 Totems, 2 Reclaim, 3 Hold, 4 Collect, 5 Custom
(confirmed by the campaign survey; see docs/randomizer-design.md). Each
REQUIRED objective of each mission is one location, named
"<Mission Title> - <Objective Type>", plus "<Mission Title> - Mission Complete"
for every mission except the finale, whose completion is the Victory event.
"""
from BaseClasses import Item, ItemClassification, Location

from .items import BASE_ID, MISSION_TITLES

LOCATION_BASE_ID = BASE_ID + 1000

OBJECTIVE_TYPES = {0: "Nullify", 1: "Totems", 2: "Reclaim", 3: "Hold", 4: "Collect", 5: "Custom"}

# Required objective slot indices per mission (survey data, 2026-08-25).
REQUIRED_OBJECTIVES = {
    1: [5],
    2: [0, 1, 4],
    3: [1, 4],
    4: [1, 4],
    5: [1, 4],
    6: [2],
    7: [1, 4],
    8: [1],
    9: [1, 4],
    10: [1, 4],
    11: [0, 4],
    12: [1, 4],
    13: [1, 4],
    14: [1, 4],
    15: [0, 1, 4],
    16: [1, 4],
    17: [4],
    18: [4],
    19: [4, 5],
    20: [0, 1, 2, 5],
}

FINAL_MISSION = 20


def objective_location_name(mission: int, slot: int) -> str:
    return f"{MISSION_TITLES[mission]} - {OBJECTIVE_TYPES[slot]}"


def mission_complete_location_name(mission: int) -> str:
    return f"{MISSION_TITLES[mission]} - Mission Complete"


def _build_locations():
    names = []
    per_mission = {}
    for n in range(1, 21):
        mission_locs = [objective_location_name(n, slot) for slot in REQUIRED_OBJECTIVES[n]]
        if n != FINAL_MISSION:
            mission_locs.append(mission_complete_location_name(n))
        per_mission[n] = mission_locs
        names.extend(mission_locs)
    return names, per_mission


_ALL_NAMES, LOCATIONS_PER_MISSION = _build_locations()
LOCATION_NAME_TO_ID = {name: LOCATION_BASE_ID + i for i, name in enumerate(_ALL_NAMES)}

VICTORY_EVENT = f"{MISSION_TITLES[FINAL_MISSION]} - Victory"
VICTORY_ITEM = "Victory"


class CW4Location(Location):
    game = "Creeper World 4"


def create_all_locations(world) -> None:
    for n in range(1, 21):
        region = world.get_region(f"story{n}")
        for name in LOCATIONS_PER_MISSION[n]:
            region.locations.append(
                CW4Location(world.player, name, LOCATION_NAME_TO_ID[name], region)
            )
    final = world.get_region(f"story{FINAL_MISSION}")
    victory = CW4Location(world.player, VICTORY_EVENT, None, final)
    victory.place_locked_item(
        Item(VICTORY_ITEM, ItemClassification.progression, None, world.player)
    )
    final.locations.append(victory)
