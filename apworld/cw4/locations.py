"""Location tables for Creeper World 4.

One location per required objective plus one Mission Complete per mission
(story20's completion is the Victory event, not a location).
Required-objective counts come from the campaign survey in
docs/randomizer-design.md.
"""
from BaseClasses import Location

from .items import BASE_ID, MISSION_TITLES

LOCATION_BASE_ID = BASE_ID + 1000

# Required objective count per mission (survey data).
REQUIRED_OBJECTIVES = {
    1: 1, 2: 3, 3: 2, 4: 2, 5: 2, 6: 1, 7: 2, 8: 1, 9: 2, 10: 2,
    11: 2, 12: 2, 13: 2, 14: 2, 15: 3, 16: 2, 17: 1, 18: 1, 19: 2, 20: 4,
}


def _build_locations():
    names = []
    per_mission = {}
    for n in range(1, 21):
        title = MISSION_TITLES[n]
        mission_locs = []
        for i in range(1, REQUIRED_OBJECTIVES[n] + 1):
            mission_locs.append(f"{title} - Objective {i}")
        if n < 20:
            mission_locs.append(f"{title} - Mission Complete")
        per_mission[n] = mission_locs
        names.extend(mission_locs)
    return names, per_mission


_ALL_NAMES, LOCATIONS_PER_MISSION = _build_locations()
LOCATION_NAME_TO_ID = {name: LOCATION_BASE_ID + i for i, name in enumerate(_ALL_NAMES)}

VICTORY_EVENT = "Ever After - Victory"


class CW4Location(Location):
    game = "Creeper World 4"


def create_all_locations(world) -> None:
    for n in range(1, 21):
        region = world.get_region(f"story{n}")
        for name in LOCATIONS_PER_MISSION[n]:
            region.locations.append(
                CW4Location(world.player, name, LOCATION_NAME_TO_ID[name], region)
            )
    # Victory event on story20.
    story20 = world.get_region("story20")
    victory = CW4Location(world.player, VICTORY_EVENT, None, story20)
    from .items import CW4Item
    from BaseClasses import ItemClassification
    victory.place_locked_item(
        CW4Item("Victory", ItemClassification.progression, None, world.player)
    )
    story20.locations.append(victory)
