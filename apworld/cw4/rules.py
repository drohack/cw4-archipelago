"""Access rules for Creeper World 4 - the SINGLE source of logic.

The requirement tables below drive both the generator's access rules
(set_all_rules) and the hints shipped to the game client in slot_data
(requirement_groups), so the in-game tracker can never disagree with the
generator.

A requirement is a list of any-of groups of item names: satisfied when every
group has at least one held item. Mission reachability itself is the Mission
Unlock item (handled in regions.py); the groups here are the unit logic from
docs/randomizer-design.md ("Logic corrections from the designer"):
- offense (Cannon or Mortar) for every mission after the tutorial
- Nullifier only for Nullify objectives
Sprayer/blueite, porter, and factory-storage dependencies are open questions
and deliberately NOT in logic yet.
"""
from worlds.generic.Rules import set_rule

from .locations import (
    FINAL_MISSION,
    LOCATIONS_PER_MISSION,
    OBJECTIVE_TYPES,
    REQUIRED_OBJECTIVES,
    VICTORY_EVENT,
    VICTORY_ITEM,
    objective_location_name,
)

OFFENSE = ["Cannon", "Mortar"]


def mission_requirements(mission: int) -> list:
    return [] if mission == 1 else [list(OFFENSE)]


def objective_requirements(mission: int, slot: int) -> list:
    if OBJECTIVE_TYPES[slot] == "Nullify":
        return [["Nullifier"]]
    return []


def requirement_groups() -> dict:
    """The exact structure exported to slot_data."""
    missions = {f"story{n}": mission_requirements(n) for n in range(1, 21)}
    locations = {}
    for n in range(1, 21):
        for slot in REQUIRED_OBJECTIVES[n]:
            reqs = objective_requirements(n, slot)
            if reqs:
                locations[objective_location_name(n, slot)] = reqs
    return {"mission_requirements": missions, "location_requirements": locations}


def _satisfies(state, player: int, groups: list) -> bool:
    return all(state.has_any(tuple(group), player) for group in groups)


def set_all_rules(world) -> None:
    player = world.player
    groups = requirement_groups()

    # Mission-level unit requirements apply to every location in the mission,
    # plus any location-specific requirement.
    for n in range(1, 21):
        mission_groups = groups["mission_requirements"][f"story{n}"]
        for name in LOCATIONS_PER_MISSION[n]:
            loc_groups = mission_groups + groups["location_requirements"].get(name, [])
            if loc_groups:
                set_rule(
                    world.get_location(name),
                    lambda state, g=loc_groups: _satisfies(state, player, g),
                )

    # The Victory event sits in the final mission's region (unlock is on the
    # region entrance) and needs the mission's unit requirements too.
    final_groups = groups["mission_requirements"][f"story{FINAL_MISSION}"]
    set_rule(
        world.get_location(VICTORY_EVENT),
        lambda state, g=final_groups: _satisfies(state, player, g),
    )

    world.multiworld.completion_condition[player] = lambda state: state.has(VICTORY_ITEM, player)
