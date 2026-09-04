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
# The Factory item unlocks the greenar refinery too, so the "chain" is one item
# now - see items.RETIRED_ITEMS for why the refinery's NAME still exists.
GREENAR_CHAIN = ["Factory"]
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
    # without."
    16: [["Sniper"]],
    # Wallis, the second standard-tier sniper mission, and stated as flatly:
    # "You need snipers to actually do this level as a hard requirement.
    #  (regardless of energy/weapons)".
    18: [["Sniper"]],
    # Ever After. The ONLY mission where logic demands BOTH weapons - two
    # separate groups, so both must be held:
    #   "you need Miners, morters and cannons. hard requirement. There's jsut too
    #    much generation of creep on the map otherwise. and not enough space,
    #    defendable area."
    # Confirmed as literal rather than loose phrasing (designer, 2026-09-03).
    20: [["Miner"], ["Cannon"], ["Mortar"]],
    # NOT here, and tested rather than assumed: story15. The worksheet suspected
    # mining might be needed for energy there, so it was played with only its
    # logic requirements and no Miner - "Yes very doable with no miners on Tower
    # of Darkness. not a requirement." Economy stays outside logic everywhere
    # except Not My Mars above, and test_miner_gates_only_not_my_mars pins that
    # the exception stays exactly one mission wide.
}

# Requirements that LOGIC asserts but physics does not.
#
# Two different questions get asked of these rules, and they used to share one
# answer. The generator asks "may I assume the player can do this?" and must not
# assume a hard-mode run. The in-game tracker asks "can this be reached at all?"
# and should paint an icon red only when the answer is no. A requirement that is
# true for the first question and false for the second belongs HERE, so the
# tracker can show it yellow: reachable, but not promised.
#
# Both entries are about energy rather than offense, and both were found by
# playing a seed where only Mortar had arrived (designer, 2026-09-03).
#
# Not My Mars - the objectives sit on separate islands and towers cannot carry
# energy across:
#   "we should set Not My Mars to require Miners to be unlocked. you can't get
#    energy from towers so this is basically impossible to beat."
# Not literally impossible: the rift lab can be hovered between the islands to
# walk the power over, and that is explicitly rejected as logic -
#   "I'd have to use the cheese/hard mode strategy of hovering the rift lab
#    between the islands to get the power across... which shouldn't be in logic."
#
# Ruins Repurposed - the same energy problem, judged one notch easier:
#   "i just can't get enough ground to get enough energy to support the mortars
#    to push through."
#   "I think it is doable, but it's hard mode/out of logic."
#
# Economy is outside logic everywhere else: story15 was played with no Miner and
# no energy items at all - "Yes very doable with no miners on Tower of Darkness.
# not a requirement." test_miner_gates_only_the_energy_missions pins the
# exception to these two.
MISSION_SOFT = {
    3: [["Miner"]],
    4: [["Miner"]],
}


# Per-objective requirements, mission by mission, EXCLUDING whatever completing
# the mission needs (added separately unless waived below). Every entry traces to
# a line in the worksheet; objectives not listed need nothing of their own.
OBJECTIVE_OWN = {
    # Home: "Need nullifier to nullyfy the single enemy/objective". Its totems
    # run off a liftic cache, so they need no refinery.
    (2, "Nullify"): [["Nullifier"]],

    # We Know Nothing: "Refinery and Factory, to store liftic to give to totems."
    (5, "Totems"): [["Factory"]],

    # We Were Never Alone. HEDGED: "I think nullifier is probably nessesary to
    # reclaim as there's just too much to keep it under raps".
    (6, "Reclaim"): [["Nullifier"]],

    # Serious. The wall is an ENERGY BUDGET, not firepower: the map yields just
    # over 20 generation from tower ground and static defence spends all of it,
    # so there is never a surplus to push with. Measured by playing it four
    # times, one item set per run (2026-09-03):
    #
    #   Cannon  clears - holds a choke for less energy, freeing the surplus
    #   Terp    clears - walls cut the line to defend, carving adds tower spots
    #   Miner   clears - about +10 generation from this map's early RESO field
    #
    # Mortars alone are "outside of even possible logic (it's very hard mode)",
    # so this is HARD - red, not yellow. One OR-group: any of the three.
    (8, "Nullify"): [["Cannon", "Terp", "Miner"]],
    (8, "Reclaim"): [["Cannon", "Terp", "Miner"]],

    # Founders. The nullify targets are on the enemy islands and the starting
    # island cannot be left without platforms: "You will need Platforms to get
    # from the safe starter island to get to the enemies (pylon will not work,
    # and porter might, but would be very hard mode)." _expand adds the greenar
    # chain a platform needs.
    (19, "Nullify"): [["Platform"]],

    # Sequence' reclaim means clearing everything, so it inherits the whole
    # nullify stack including the darkness beacon - see OBJECTIVE_TIERS.
    (17, "Reclaim"): [["Sniper"], ["Miner"], ["Pylon", "Porter", "Platform"],
                      ["Chronat"]],

    # Wallis' reclaim likewise needs the firepower its later nullifies need.
    (18, "Reclaim"): [["Cannon"]],

    # Greenar-crystal missions: "requires refinery and factory to fill the
    # totems", repeated near-verbatim on each of these.
    (7, "Totems"): [["Factory"]],
    (8, "Totems"): [["Factory"]],
    (9, "Totems"): [["Factory"]],
    (10, "Totems"): [["Factory"]],
    (12, "Totems"): [["Factory"]],
    (13, "Totems"): [["Factory"]],
    (14, "Totems"): [["Factory"]],
    (15, "Totems"): [["Factory"]],
    (16, "Totems"): [["Factory"]],
    (20, "Totems"): [["Factory"]],

    # Founders. Same rule, found by BEING SOFT-LOCKED by its absence: with the
    # colours working, Founders' totems were the only green icon left on the map
    # and could not be taken.
    #
    #   "the totems on founders are the only green icon on my map, but i can't
    #    get them as they need greenar (refinery/factory) to get, there's no
    #    liftic cache on that mission."
    #
    # The test that no totem check is reachable without the greenar chain unless
    # its mission has loose liftic is what would have caught this before a
    # player did (designer, 2026-09-03).
    (19, "Totems"): [["Factory"]],

    # Shattered and Wallis, the last two totem sets logic still called free.
    # Checked by playing them with rift lab, tower and Mortar only:
    #
    #   "No both Shattered and Wallis need refinery/factory to collect greenar
    #    and convert to liftic."
    #
    # That leaves Home, Not My Mars and Ruins Repurposed as the ONLY missions
    # whose totems run on loose liftic, which is what test_totems_need_greenar
    # now pins - the free case is the exception and has to be named.
    (11, "Totems"): [["Factory"]],
    (18, "Totems"): [["Factory"]],

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
# Per-objective requirements that LOGIC asserts and PHYSICS does not.
#
# MISSION_SOFT says "this mission", which cannot express "this mission's Nullify
# and Reclaim but not its caches". Somewhere in Spacetime needs exactly that:
#
#   "with morters and nullifiers it is doable. i'd say medium/hard, so out of
#    logic, but not by much (yellow)."
#
# So the generator must not assume a mortar-only run there, while the map still
# shows the checks as reachable-but-unpromised. The members are the routes that
# make it comfortable on THAT map - Miner is deliberately absent, because
# "There's no RESO so miners won't help here", and a Miner in this group would
# tell a player the checks were reachable when they are not.
OBJECTIVE_SOFT = {
    (14, "Nullify"): [["Cannon", "Sniper", "Terp"]],
    (14, "Reclaim"): [["Cannon", "Sniper", "Terp"]],
}


# Requirements that ESCALATE across the instances of one counted objective.
#
# Sound because instance locations are numbered by ACTIVATION ORDER - the game
# cannot tell one nullify target from another, so "Nullify 3" is the third one
# the player does, and its requirement is the third-easiest target's. A flat
# per-mission rule would instead paint the two easy targets red until the whole
# late-game kit had arrived.
#
# Each entry is (highest instance this tier covers, extra groups), lowest first.
OBJECTIVE_TIERS = {
    # Sequence, from the map review (2026-09-03):
    #   "With nullifier, you can nullify 2 of the enemies (the 2 connected to
    #    your island on the left hand side ... you can get 7 more nullifies if
    #    you have sniper, plus miner access to get to the RESO (the reso needs
    #    pylon or porter or platform to actually get to, you cannot get there
    #    with just towers) ... The final 5 enemies are in darkness and need
    #    light/beacon to even get to to nullify."
    (17, "Nullify"): [
        (2, []),
        (9, [["Sniper"], ["Miner"], ["Pylon", "Porter", "Platform"]]),
        (14, [["Sniper"], ["Miner"], ["Pylon", "Porter", "Platform"],
              ["Chronat"]]),
    ],
    # Wallis:
    #   "I tried with miners and morters, and you could probably get the first 2
    #    nullifies with that, maybe even first 4. but after that you'll need some
    #    more firepower, so i would say need cannons for after."
    # The hedge is resolved the way the design doc says to resolve hedges -
    # require rather than not - so the cheap tier stops at 4.
    (18, "Nullify"): [
        (4, [["Miner"]]),
        (9, [["Miner"], ["Cannon"]]),
    ],
}


WAIVES_MISSION_REQUIREMENTS = {
    (2, "Collect"), (3, "Collect"), (4, "Collect"), (5, "Collect"),
    (7, "Collect"), (10, "Collect"), (11, "Collect"), (12, "Collect"),
    (13, "Collect"), (14, "Collect"),
}

# Waived for ONE INSTANCE rather than for the whole objective type.
#
# Farsite needs this and is the only mission that does. The worksheet splits its
# two caches: "first item can get with just tower, 2nd item needs weapon to get
# over creep." A per-TYPE waiver cannot express that - it would waive the weapon
# for both and claim the second cache is free, which is false - so Farsite was
# excluded from the starter set entirely. That was the wrong trade: it made
# mission 1 the one mission that can never open a seed, when the designer's whole
# point was that ANY mission with a free collectible should be able to.
#
# Locations are per instance, so the rule can be too. Keyed
# (mission, kind, instance); instances are numbered by activation order.
WAIVES_INSTANCE = {
    (1, "Collect", 1),
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
#
# Playtested on story15, 2026-08-31, with exactly that mission's logic
# requirements granted and nothing else. The designer's words, and the second
# sentence is the load-bearing one:
#
#   "the snipers are a little more important."
#   "snipers are not needed, but nice to haves. you can beat the level without
#    them."
#
# THIS IS NOT A HEDGE, and it must not be read as one. Archon's two MISSION_EXTRA
# entries came from genuine hedges ("I think super hard to do anything without a
# nullifier") and were promoted to requirements on purpose. This is the opposite:
# an explicit statement that the level is beatable without. It belongs in the
# casual tier and nowhere else. Do not promote it.
CASUAL_DEFENSE_FROM = 6


def is_casual(world) -> bool:
    return world.options.logic_difficulty.value == 1


def _casual_defense(mission: int, casual: bool) -> list:
    return [list(DEFENSIVE)] if casual and mission >= CASUAL_DEFENSE_FROM else []


def _simplify(groups: list) -> list:
    """Drop OR-groups that a REQUIRED single item already satisfies.

    Ever After requires both weapons, so its rules came out as
    "(Cannon or Mortar) + Miner + Cannon + Mortar" - correct, since holding
    Cannon satisfies the or-group, but three ways of saying the same thing. Any
    group containing an item that is required on its own is redundant by
    definition, so removing it cannot change what the rule accepts.

    Kept separate from _expand because it is presentation and size, not meaning:
    every entry travels to the client in slot_data and gets read by a human
    reviewing the logic.
    """
    singles = {g[0] for g in groups if len(g) == 1}
    out = []
    for g in groups:
        if len(g) > 1 and any(item in singles for item in g):
            continue
        if g not in out:
            out.append(g)
    return out


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
    return _simplify(out)


def mission_requirements(mission: int, casual: bool = False,
                         physical: bool = False) -> list:
    """What COMPLETING this mission needs, before its objectives are considered.

    Not "what every location on the mission needs" - a waived cache needs none
    of it.

    `physical` drops everything logic asserts but physics does not - the
    MISSION_SOFT entries and the casual anti-air tier - leaving only what a
    player genuinely cannot proceed without. That is the set the tracker needs
    in order to tell red (unreachable) from yellow (reachable, out of logic).
    """
    groups = [list(OFFENSE)]
    groups += [list(g) for g in MISSION_EXTRA.get(mission, [])]
    if not physical:
        groups += [list(g) for g in MISSION_SOFT.get(mission, [])]
        groups += _casual_defense(mission, casual)
    return _expand(groups)


def objective_requirements(mission: int, slot: int, casual: bool = False) -> list:
    """The COMPLETE requirement for one objective check, by slot index."""
    return requirements_for_kind(mission, OBJECTIVE_TYPES[slot], casual)


def requirements_for_kind(mission: int, kind: str, casual: bool = False,
                          physical: bool = False) -> list:
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

    # Type-wide, for the same reason: Reclaim is "clear the map", which cannot
    # be finished while anything is still producing creeper - so it inherits the
    # Nullifier whether or not that mission's own notes mention one.
    #
    #   "that's typically the last thing you do in a level as you need to lock
    #    down all the enemies, it's possible, but typically need a lot of
    #    firepower."
    #
    # Encoded per-mission it covered only We Were Never Alone, leaving nine of
    # the eleven Reclaim checks asking for nothing but a weapon - exactly the
    # hole the Nullify rule above was written to close. Found when Hints' Reclaim
    # read as the last check reachable with a lone Mortar and was not remotely
    # doable (designer, 2026-09-03).
    #
    # Firepower beyond that is deliberately NOT modelled: there is no item for
    # "more cannons", and anti-air stays in the casual tier where it already is
    # (designer's call, same date) rather than becoming a hard requirement here.
    # We Were Never Alone keeps its own hedged entry below; it dedupes.
    if kind == "Reclaim":
        groups.append(["Nullifier"])

    for group in OBJECTIVE_OWN.get((mission, kind), []):
        if list(group) not in groups:
            groups.append(list(group))
    if not physical:
        for group in OBJECTIVE_SOFT.get((mission, kind), []):
            if list(group) not in groups:
                groups.append(list(group))
    if (mission, kind) not in WAIVES_MISSION_REQUIREMENTS:
        for group in mission_requirements(mission, casual, physical):
            if group not in groups:
                groups.append(group)
    return _expand(groups)


def mission_complete_requirements(mission: int, casual: bool = False,
                                  physical: bool = False) -> list:
    """Completing a mission means completing every REQUIRED objective on it.

    So this is the mission's own requirements plus each objective's own. A waived
    cache waives the weapon for ITS check only - the mission still cannot be
    finished without one, which is why mission_requirements seeds the list.
    """
    groups = [list(g) for g in mission_requirements(mission, casual, physical)]
    for slot in REQUIRED_OBJECTIVES[mission]:
        kind = OBJECTIVE_TYPES[slot]
        for group in _expand([list(g) for g in OBJECTIVE_OWN.get((mission, kind), [])]):
            if group not in groups:
                groups.append(group)
    return groups


def _tier_for(tiers: list, index: int) -> list:
    """The extra groups for instance `index`: the first tier that covers it.

    Past the last tier the highest one applies - an extra instance appearing in
    a future game update should inherit the hardest known requirement rather
    than none at all.
    """
    for upto, extra in tiers:
        if index <= upto:
            return extra
    return tiers[-1][1] if tiers else []


def _instance_index(name: str) -> int | None:
    """The trailing instance number of a per-instance location name, or None."""
    tail = name.rsplit(" ", 1)[-1]
    return int(tail) if tail.isdigit() else None


def location_requirements(name: str, mission: int, casual: bool = False,
                          physical: bool = False) -> list:
    """The COMPLETE requirement for one location, by name.

    Instances of an objective type usually share their type's rule - the game
    cannot tell one totem from another, and neither can logic. WAIVES_INSTANCE is
    the exception, for the one case where the worksheet distinguishes them:
    Farsite's first cache is free and its second is not.
    """
    if name == mission_complete_location_name(mission):
        return mission_complete_requirements(mission, casual, physical)
    kind = location_kind(name)
    if not kind:
        return []
    index = _instance_index(name)
    tiers = OBJECTIVE_TIERS.get((mission, kind))
    if index is not None and tiers is not None:
        extra = _tier_for(tiers, index)
        groups = [list(g) for g in requirements_for_kind(mission, kind, casual,
                                                         physical)]
        for group in extra:
            if list(group) not in groups:
                groups.append(list(group))
        return _expand(groups)
    if index is not None and (mission, kind, index) in WAIVES_INSTANCE:
        # The instance's own requirements, without the mission's. Built the same
        # way requirements_for_kind does, minus the mission block.
        groups = []
        if kind == "Nullify":
            groups.append(["Nullifier"])
        for group in OBJECTIVE_OWN.get((mission, kind), []):
            if list(group) not in groups:
                groups.append(list(group))
        return _expand(groups)
    return requirements_for_kind(mission, kind, casual, physical)


def requirement_groups(casual: bool = False, physical: bool = False) -> dict:
    """The exact structure exported to slot_data. See the module docstring for
    the contract: location entries are complete and must not be combined."""
    missions = {f"story{n}": mission_requirements(n, casual, physical)
                for n in range(1, 21)}
    locations = {}
    for n in range(1, 21):
        for name in LOCATIONS_PER_MISSION[n]:
            reqs = location_requirements(name, n, casual, physical)
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
