"""Access rules for Creeper World 4 - the SINGLE source of logic.

The tables below drive both the generator's access rules (set_all_rules) and the
hints shipped to the game client in slot_data (requirement_groups), so the
in-game tracker can never disagree with the generator.

A requirement is a list of any-of groups of item names: satisfied when every
group has at least one held item.

SOURCE OF TRUTH is the manual playthrough in
docs/design/mission-requirements-worksheet.md. Nothing here comes from the
vanilla unlock schedule, which is a teaching order rather than a requirements
order and would flatten every seed if used as logic.

The tables are written out mission by mission rather than derived from the
objective TYPE, because the type is not what decides. Two missions with a Totems
objective can differ completely: missions 2, 3 and 4 power theirs from liftic
lying on the ground, while mission 8 needs the whole greenar chain. Deriving from
type is exactly the mistake that produced the first version of this file, which
also missed that most missions let you take their cache with nothing but a rift
lab and one tower.

Reading rules applied to the worksheet:

1. Only what is genuinely REQUIRED becomes logic. "Helped but not needed" is
   difficulty-tier material. Snipers and missile launchers are survival aids on
   nearly every map and are NOT in logic - The Compound is the one exception.
2. Where the worksheet hedges, require rather than not. Too-tight logic only
   makes seeds more linear; too-loose logic can make a seed unwinnable. Hedged
   calls are marked HEDGED with the note they came from.

SLOT_DATA CONTRACT: each entry of location_requirements is the COMPLETE
requirement for that location. A consumer must NOT combine it with the mission's
entry - a mission's cache is often collectable long before the mission itself is
winnable, and ANDing the two would hide that.
"""
from worlds.generic.Rules import set_rule

from .locations import (
    BEATEN_ITEM,
    FINAL_MISSION,
    beaten_event_name,
    LOCATIONS_PER_MISSION,
    OBJECTIVE_TYPES,
    REQUIRED_OBJECTIVES,
    VICTORY_EVENT,
    VICTORY_ITEM,
    location_kind,
    mission_complete_location_name,
)

# Weapons that run on energy alone. Consistent across all 20 missions: cannons
# handle nearly anything, mortars the same with more effort. Sprayers are
# excluded - they burn bluite, and every bluite map was judged short of the
# resource for a sprayer-only run.
OFFENSE = ["Cannon", "Mortar"]

# Buildings that need the greenar chain before they can be built. Any rule that
# requires one also requires the chain; expansion happens in _expand so it can
# never be forgotten at a call site.
GREENAR_CHAIN = ["Greenar Refinery", "Factory"]
PREREQUISITES = {
    "Platform": GREENAR_CHAIN,
    "Chronat": GREENAR_CHAIN,
    "Rocket Pad": GREENAR_CHAIN,
}

# What COMPLETING a mission needs, beyond offense.
MISSION_EXTRA = {
    # Archon, two entries. An enemy shuts off energy production, and the map
    # rains creeper constantly:
    #   "So I think super hard to do anything without a nullifier."
    #   "This level gets doable/not super hard mode if you get SHIELDS as it
    #    creates a safe space from the rain."
    # Both HEDGED, both treated as required (designer, 2026-08-31) - the same
    # call, made the same way, on the same mission. Shields need redite through
    # the factory, but the factory is not added here: the Totems objective
    # already requires it, and a rule should not assert a dependency it has not
    # measured.
    12: [["Nullifier"], ["Shield"]],
    # Not hedged. The Compound's saw blades delete buildings and die only to
    # snipers: "You need snipers to get past them. no way to do any objectives
    # without." The ONLY mission where a sniper is in logic.
    16: [["Sniper"]],
}

# Per-objective requirements, mission by mission, EXCLUDING whatever completing
# the mission needs (added separately unless waived below). Every entry traces to
# a line in the worksheet; objectives not listed need nothing of their own.
OBJECTIVE_OWN = {
    # Home: "Need nullifier to nullyfy the single enemy/objective". Its totems
    # run off a liftic cache, so they need no refinery.
    (2, "Nullify"): [["Nullifier"]],

    # We Know Nothing: "Refinery and Factory, to store liftic to give to totems."
    (5, "Totems"): [["Greenar Refinery"], ["Factory"]],

    # We Were Never Alone. HEDGED: "I think nullifier is probably nessesary to
    # reclaim as there's just too much to keep it under raps".
    (6, "Reclaim"): [["Nullifier"]],

    # Greenar-crystal missions: "requires refinery and factory to fill the
    # totems", repeated near-verbatim on each of these.
    (7, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (8, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (9, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (10, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (12, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (13, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (14, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (15, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (16, "Totems"): [["Greenar Refinery"], ["Factory"]],
    (20, "Totems"): [["Greenar Refinery"], ["Factory"]],

    # Shattered: "To get to the Enemy (nullify) ... you either need porter, or
    # platform to cross space."
    (11, "Nullify"): [["Nullifier"], ["Porter", "Platform"]],

    # Archon: "Both items are burried, need TERP to get" and "If you have a pylon
    # and a terp you can get the 2nd item (no weapons needed)."
    (12, "Collect"): [["Terp"], ["Pylon"]],

    # Tower of Darkness: "Need beacon to get to the center where all enemies
    # are. nullifier to nullify enemies."
    (15, "Nullify"): [["Nullifier"], ["Chronat"]],

    # Buried caches.
    (16, "Collect"): [["Terp"]],
    (17, "Collect"): [["Terp"]],   # "The 2nd item ... is burried (TERP)"
    (18, "Collect"): [["Terp"]],

    # Founders: "The item is in darkness (BEACON), and burried (TERP), and behind
    # enemy lines. You will need Platforms" - pylon explicitly does not work.
    (19, "Collect"): [["Terp"], ["Chronat"], ["Platform"]],
    # "You need to nullify the 4 obelisk reactors, and 1 neutron reactor to
    # finish the custom 'End the Beginning' objective."
    (19, "Custom"): [["Nullifier"], ["Platform"]],

    # Ever After: "REFINERY, FACTORY for totems. Nullifier for enemies."
    (20, "Nullify"): [["Nullifier"]],
}

# Objectives that need NONE of what completing the mission needs - no weapon, no
# mission-wide extra. Almost all are the cache a player can take with the rift
# lab and a single tower before the creeper reaches it; the worksheet says so
# mission by mission ("You can get the item immediately with rift lab and single
# tower", "spawn in the rift lab near it and place a single tower").
#
# This is what makes a starting weapon unnecessary, and why cannon, mortar and
# sprayer can all stay real checks instead of one being handed over up front.
#
# Archon is here for a different reason: its caches are buried rather than close,
# but "If you have a pylon and a terp you can get the 2nd item (no weapons
# needed)" - a terp and a pylon stand in for the weapon.
#
# Deliberately NOT here, because the worksheet says the opposite: More and More
# ("No easy way to get the item at the start of this one. it starts under
# creep"), Tower of Darkness ("No easy way to get item. need to fight back creep
# to do it"), The Compound, Sequence, Wallis, Founders and Ever After.
WAIVES_MISSION_REQUIREMENTS = {
    (2, "Collect"), (3, "Collect"), (4, "Collect"), (5, "Collect"),
    (7, "Collect"), (10, "Collect"), (11, "Collect"), (12, "Collect"),
    (13, "Collect"), (14, "Collect"),
}


# Anti-air, for the casual logic tier. The worksheet never makes these a WIN
# requirement - "technically you don't need snipers", "possible without missles" -
# but it flags them on nearly every mission from the first spores onward:
# "medium difficulty without snipers" (War and Peace), "without i think it's hard
# mode" (Sequence), "really nice to have snipers" (Founders).
#
# Making that real logic rather than a note is the whole point. Logic is what
# decides sphere order, so requiring anti-air from mission 6 makes Archipelago
# place one before those missions are reachable, instead of a seed legally
# leaving the Sniper in the finale.
DEFENSIVE = ["Sniper", "Missile Launcher"]

# We Were Never Alone is the first mission with spores; skimmers arrive at 14 and
# air sacs at 19, so the pressure only grows from there.
CASUAL_DEFENSE_FROM = 6


def is_casual(world) -> bool:
    return world.options.logic_difficulty.value == 1


def _casual_defense(mission: int, casual: bool) -> list:
    return [list(DEFENSIVE)] if casual and mission >= CASUAL_DEFENSE_FROM else []


def _expand(groups: list) -> list:
    """Add the prerequisites of any required building.

    A rule asking for a Platform is really asking for a Platform AND the greenar
    chain that builds one. Only expands when EVERY option in a group shares the
    prerequisite - otherwise "Porter or Platform" would wrongly demand the chain
    that only the platform needs.
    """
    out = [list(g) for g in groups]
    for group in groups:
        shared = None
        for name in group:
            needs = PREREQUISITES.get(name)
            if needs is None:
                shared = None
                break
            shared = list(needs) if shared is None else [n for n in shared if n in needs]
        for name in shared or []:
            if [name] not in out:
                out.append([name])
    return out


def mission_requirements(mission: int, casual: bool = False) -> list:
    """What COMPLETING this mission needs, before its objectives are considered.

    Not "what every location on the mission needs" - a waived cache needs none
    of it.
    """
    groups = [list(OFFENSE)]
    groups += [list(g) for g in MISSION_EXTRA.get(mission, [])]
    groups += _casual_defense(mission, casual)
    return _expand(groups)


def objective_requirements(mission: int, slot: int, casual: bool = False) -> list:
    """The COMPLETE requirement for one objective check, by slot index."""
    return requirements_for_kind(mission, OBJECTIVE_TYPES[slot], casual)


def requirements_for_kind(mission: int, kind: str, casual: bool = False) -> list:
    """The COMPLETE requirement for every check of one objective type on one
    mission. Each instance of that type carries the same rule - the game cannot
    distinguish one totem from another, and neither can logic."""
    groups = []

    # Type-wide: nullifying anything needs the Nullifier, on every mission,
    # whether or not that mission REQUIRES its nullify objective. Encoding this
    # only on the four missions that require it left every optional nullify
    # target reachable bare-handed once they became checks.
    #
    # Deliberately no type-wide rule for Totems: missions 2, 3 and 4 power theirs
    # from loose liftic and genuinely need nothing.
    if kind == "Nullify":
        groups.append(["Nullifier"])

    for group in OBJECTIVE_OWN.get((mission, kind), []):
        if list(group) not in groups:
            groups.append(list(group))
    if (mission, kind) not in WAIVES_MISSION_REQUIREMENTS:
        for group in mission_requirements(mission, casual):
            if group not in groups:
                groups.append(group)
    return _expand(groups)


def mission_complete_requirements(mission: int, casual: bool = False) -> list:
    """Completing a mission means completing every REQUIRED objective on it.

    So this is the mission's own requirements plus each objective's own. A waived
    cache waives the weapon for ITS check only - the mission still cannot be
    finished without one, which is why mission_requirements seeds the list.
    """
    groups = [list(g) for g in mission_requirements(mission, casual)]
    for slot in REQUIRED_OBJECTIVES[mission]:
        kind = OBJECTIVE_TYPES[slot]
        for group in _expand([list(g) for g in OBJECTIVE_OWN.get((mission, kind), [])]):
            if group not in groups:
                groups.append(group)
    return groups


def location_requirements(name: str, mission: int, casual: bool = False) -> list:
    """The COMPLETE requirement for one location, by name.

    Every instance of an objective type shares its type's rule, so this is a
    lookup on the location's prefix rather than a per-instance table.
    """
    if name == mission_complete_location_name(mission):
        return mission_complete_requirements(mission, casual)
    kind = location_kind(name)
    if not kind:
        return []
    return requirements_for_kind(mission, kind, casual)


def requirement_groups(casual: bool = False) -> dict:
    """The exact structure exported to slot_data. See the module docstring for
    the contract: location entries are complete and must not be combined."""
    missions = {f"story{n}": mission_requirements(n, casual) for n in range(1, 21)}
    locations = {}
    for n in range(1, 21):
        for name in LOCATIONS_PER_MISSION[n]:
            reqs = location_requirements(name, n, casual)
            if reqs:
                locations[name] = reqs
    return {"mission_requirements": missions, "location_requirements": locations}


_LOGIC_ITEMS: set | None = None


def logic_item_names() -> set:
    """Every item name that appears in any access rule.

    Archipelago's rule is that an item referenced in logic MUST be classified
    progression. The converse matters too: marking an item progression when it
    gates nothing puts it in the pool the fill prioritises and that progression
    balancing pulls into early spheres, which distorts the sphere structure with
    items no one is waiting on.

    Deriving the set from the rules means classification cannot drift from
    logic - add a rule mentioning Sprayer and it becomes progression by itself.
    """
    global _LOGIC_ITEMS
    if _LOGIC_ITEMS is None:
        names = set()
        # BOTH tiers. An item that gates only under casual logic must still be
        # classified progression: classification is computed per item NAME, not
        # per seed, so a casual seed could otherwise place it behind itself.
        for casual in (False, True):
            groups = requirement_groups(casual)
            for table in (groups["mission_requirements"], groups["location_requirements"]):
                for entry in table.values():
                    for group in entry:
                        names.update(group)
        _LOGIC_ITEMS = names
    return _LOGIC_ITEMS


def _satisfies(state, player: int, groups: list) -> bool:
    return all(state.has_any(tuple(group), player) for group in groups)


def missions_for_finale(world) -> int:
    return world.options.missions_for_finale.value


def set_all_rules(world) -> None:
    player = world.player
    groups = requirement_groups(is_casual(world))

    for n in range(1, 21):
        for name in LOCATIONS_PER_MISSION[n]:
            loc_groups = groups["location_requirements"].get(name, [])
            if loc_groups:
                set_rule(
                    world.get_location(name),
                    lambda state, g=loc_groups: _satisfies(state, player, g),
                )
        # The completion event needs exactly what completing the mission needs.
        if n != FINAL_MISSION:
            done = mission_complete_requirements(n, is_casual(world))
            set_rule(
                world.get_location(beaten_event_name(n)),
                lambda state, g=done: _satisfies(state, player, g),
            )

    # The Victory event sits in the final mission's region and needs everything
    # finishing that mission needs.
    final_groups = mission_complete_requirements(FINAL_MISSION, is_casual(world))
    needed = missions_for_finale(world)
    set_rule(
        world.get_location(VICTORY_EVENT),
        lambda state, g=final_groups, n=needed: (
            _satisfies(state, player, g)
            and (n <= 0 or state.has(BEATEN_ITEM, player, n))
        ),
    )

    world.multiworld.completion_condition[player] = lambda state: state.has(VICTORY_ITEM, player)
