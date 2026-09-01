"""Location tables for Creeper World 4.

ONE CHECK PER INSTANCE. Every totem, every nullifiable enemy and every info
cache is its own location, plus one for the rift jump (mission complete). That
is 236 locations, against 58 when there was a single check per objective TYPE.

Two consequences worth stating:

- Optional objectives count. A mission's nullify targets are locations whether
  or not the mission requires nullifying them, so clearing a map fully is
  rewarded.
- Instances are numbered by ACTIVATION ORDER, not identity. The game cannot tell
  totem 3 from totem 5 and does not need to: the objective counter is live
  progress, so the Nth activation sends the Nth check. That keeps ids stable and
  independent of map layout.

Counts come from the campaign survey recorded in
docs/design/mission-requirements-worksheet.md, which was measured from the game
(GameSpace.mustCollect / totems / nullifiableUnits) and cross-checked. Deriving
the location set from those counts, rather than from a hand-written list of which
objectives are optional, keeps one source of truth.

CW4's six objective slots are fixed by type, indexed
  0 Nullify, 1 Totems, 2 Reclaim, 3 Hold, 4 Collect, 5 Custom.
No mission in the campaign uses Hold.
"""
from BaseClasses import Item, ItemClassification, Location

from .items import BASE_ID, MISSION_TITLES

LOCATION_BASE_ID = BASE_ID + 1000

OBJECTIVE_TYPES = {0: "Nullify", 1: "Totems", 2: "Reclaim", 3: "Hold", 4: "Collect", 5: "Custom"}

# Founders, not Ever After. Ever After plays as an epilogue rather than a climax
# (worksheet, mission 20), so it is an ordinary mission and Founders carries the
# goal.
FINAL_MISSION = 19

# Per mission: (caches, totems, nullifiable). Measured at mission start; the
# survey confirmed every required counting objective has a non-zero target, so
# nothing is hidden behind mid-mission spawning.
INSTANCE_COUNTS = {
    1: (2, 0, 0),    2: (1, 2, 1),    3: (1, 4, 2),    4: (1, 4, 4),
    5: (1, 4, 2),    6: (0, 0, 9),    7: (1, 3, 4),    8: (0, 2, 1),
    9: (1, 4, 6),    10: (1, 8, 5),   11: (1, 3, 4),   12: (2, 3, 3),
    13: (1, 2, 8),   14: (1, 4, 8),   15: (1, 4, 9),   16: (1, 6, 12),
    17: (2, 0, 14),  18: (1, 2, 9),   19: (1, 5, 17),  20: (0, 3, 2),
}

# Objectives that are a single check rather than a count: Reclaim is a
# percentage of the map, Custom is mission-scripted.
RECLAIM_MISSIONS = {6, 7, 8, 9, 10, 12, 14, 15, 17, 18, 20}
CUSTOM_MISSIONS = {1, 19, 20}

# Which objective TYPES a mission requires to be completed at all. Distinct from
# the counts above, which include optional objectives: this drives what finishing
# a mission needs, nothing else.
REQUIRED_OBJECTIVES = {
    1: [5], 2: [0, 1, 4], 3: [1, 4], 4: [1, 4], 5: [1, 4], 6: [2], 7: [1, 4],
    8: [1], 9: [1, 4], 10: [1, 4], 11: [0, 4], 12: [1, 4], 13: [1, 4],
    14: [1, 4], 15: [0, 1, 4], 16: [1, 4], 17: [4], 18: [4], 19: [4, 5],
    20: [0, 1, 2, 5],
}

# A location's prefix maps back to the objective type whose rules apply to it.
KIND_TO_OBJECTIVE = {
    "Cache": "Collect",
    "Totem": "Totems",
    "Nullify": "Nullify",
    "Reclaim": "Reclaim",
    "Custom": "Custom",
}


def instance_location_name(mission: int, kind: str, index: int) -> str:
    return f"{MISSION_TITLES[mission]} - {kind} {index}"


def single_location_name(mission: int, kind: str) -> str:
    return f"{MISSION_TITLES[mission]} - {kind}"


def mission_complete_location_name(mission: int) -> str:
    return f"{MISSION_TITLES[mission]} - Mission Complete"


def location_names_for_mission(mission: int) -> list:
    """Every location belonging to one mission, in table order.

    Shared so that the location table, the name groups and the opening-width
    calculation cannot disagree about what a mission contains - they each used to
    rebuild this list from INSTANCE_COUNTS by hand.
    """
    caches, totems, nullifiable = INSTANCE_COUNTS[mission]
    names = [instance_location_name(mission, "Cache", i) for i in range(1, caches + 1)]
    names += [instance_location_name(mission, "Totem", i) for i in range(1, totems + 1)]
    names += [instance_location_name(mission, "Nullify", i)
              for i in range(1, nullifiable + 1)]
    if mission in RECLAIM_MISSIONS:
        names.append(single_location_name(mission, "Reclaim"))
    if mission in CUSTOM_MISSIONS:
        names.append(single_location_name(mission, "Custom"))
    if mission != FINAL_MISSION:
        names.append(mission_complete_location_name(mission))
    return names


def _build_locations():
    """Ordered location names, grouped per mission.

    Order within a mission is fixed - caches, totems, nullify, reclaim, custom,
    rift jump - because ids are assigned from this sequence and CANNOT move once
    a seed exists.
    """
    names = []
    per_mission = {}
    for n in range(1, 21):
        caches, totems, nullifiable = INSTANCE_COUNTS[n]
        mission_locs = []
        for i in range(1, caches + 1):
            mission_locs.append(instance_location_name(n, "Cache", i))
        for i in range(1, totems + 1):
            mission_locs.append(instance_location_name(n, "Totem", i))
        for i in range(1, nullifiable + 1):
            mission_locs.append(instance_location_name(n, "Nullify", i))
        if n in RECLAIM_MISSIONS:
            mission_locs.append(single_location_name(n, "Reclaim"))
        if n in CUSTOM_MISSIONS:
            mission_locs.append(single_location_name(n, "Custom"))
        if n != FINAL_MISSION:
            mission_locs.append(mission_complete_location_name(n))
        per_mission[n] = mission_locs
        names.extend(mission_locs)
    return names, per_mission


_ALL_NAMES, LOCATIONS_PER_MISSION = _build_locations()
LOCATION_NAME_TO_ID = {name: LOCATION_BASE_ID + i for i, name in enumerate(_ALL_NAMES)}

VICTORY_EVENT = f"{MISSION_TITLES[FINAL_MISSION]} - Victory"
VICTORY_ITEM = "Victory"

# One event per non-finale mission, granting a token the finale can count.
#
# Events are local: they have no id, are never sent to the multiworld, and cost
# no pool slot. The generator grants one the moment that mission's completion is
# reachable, so "beat 12 missions" becomes "twelve missions must be COMPLETABLE
# before the finale is in logic" - which is what actually forces the fill to
# spread progression across the campaign rather than stacking it on a few maps.
BEATEN_ITEM = "Mission Beaten"


def beaten_event_name(mission: int) -> str:
    return f"{MISSION_TITLES[mission]} - Beaten"


def location_kind(name: str) -> str:
    """The objective type a location's rules come from, or "" for a rift jump."""
    tail = name.split(" - ", 1)[1] if " - " in name else name
    if tail == "Mission Complete":
        return ""
    word = tail.split(" ")[0]
    return KIND_TO_OBJECTIVE.get(word, "")


class CW4Location(Location):
    game = "Creeper World 4"


def create_all_locations(world) -> None:
    for n in range(1, 21):
        region = world.get_region(f"story{n}")
        for name in LOCATIONS_PER_MISSION[n]:
            region.locations.append(
                CW4Location(world.player, name, LOCATION_NAME_TO_ID[name], region)
            )
    # Completion events, one per mission that is not the finale.
    for n in range(1, 21):
        if n == FINAL_MISSION:
            continue
        region = world.get_region(f"story{n}")
        event = CW4Location(world.player, beaten_event_name(n), None, region)
        event.place_locked_item(
            Item(BEATEN_ITEM, ItemClassification.progression, None, world.player)
        )
        region.locations.append(event)

    final = world.get_region(f"story{FINAL_MISSION}")
    victory = CW4Location(world.player, VICTORY_EVENT, None, final)
    victory.place_locked_item(
        Item(VICTORY_ITEM, ItemClassification.progression, None, world.player)
    )
    final.locations.append(victory)
