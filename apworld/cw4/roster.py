"""Which twenty missions this seed contains, and how wide they open it.

SPLIT OUT OF items.py on 2026-09-17, and not for line count - items.py is 352
lines of code carrying 834 of measurement, and fragmenting that would lose the
only thing it has. This moved because it was in the wrong place in the import
graph.

items.py answers "what items exist": the name tables, the positional id
assignment, classification, the pool. Everything here answers "which missions
did this seed draw, and what does a first weapon open on them" - and that is
DOWNSTREAM of rules.py, which is downstream of locations.py, which is
downstream of items.py. Sitting in items.py meant this code was upstream of the
things it needs, so every one of its references to locations and rules had to be
a function-local import to break the cycle. items.py carried ten of those.

Here they are ordinary top-level imports, and the package graph is acyclic.
"""
import functools

from .items import SPAN_MISSION_TITLES
from .locations import FINAL_MISSION, location_names_for_mission
from .rules import OFFENSE, is_casual, location_requirements


# Missions eligible to START unlocked.
#
# The only real constraint is that SOMETHING must be reachable with an empty
# inventory, or the generator has nowhere to put a first item. That means a
# mission whose cache can be taken with the rift lab and a single tower, with no
# weapon and no other building.
#
# Farsite IS eligible, via a per-instance waiver. The worksheet splits its two
# caches - "first item can get with just tower, 2nd item needs weapon to get over
# creep" - and an earlier version excluded mission 1 outright because a per-TYPE
# waiver could not say that without also claiming the second cache was free.
# rules.WAIVES_INSTANCE says it properly, so Farsite can open a seed like any
# other mission. Nothing requires the campaign to start at its beginning either:
# missions are open, so picking at random varies the opening between seeds.
#
# Archon is excluded despite waiving the weapon, because its caches are buried:
# they need a Terp and a Pylon, so they are not free.
#
# Kept in step with rules.WAIVES_MISSION_REQUIREMENTS and rules.WAIVES_INSTANCE
# by a test - rules.py cannot be imported here without a cycle (locations imports
# items).
STARTER_ELIGIBLE = (1, 2, 3, 4, 5, 7, 10, 11, 13, 14)
# SPAN missions whose cache could open a seed. Exactly one qualifies: Far York
# Farm is the ONLY map in the whole 26 with a cache at all, and it is exposed
# (not buried) and in its map's single connected component.
#
# HELD LOOSELY, and deliberately not trusted alone. Whether creep covers that
# cache is UNVERIFIED - the survey cannot see creeper, and two campaign missions
# (More and More, Tower of Darkness) need a weapon for exactly that reason. So
# starter_missions below always keeps at least one CAMPAIGN starter, and this
# can only ever be the second one.
SPAN_STARTER_ELIGIBLE = (38,)   # Far York Farm


def span_enabled(world) -> bool:
    return bool(world.options.span_missions)


def starter_missions(world) -> list:
    """The missions this seed starts with, chosen at random from the eligible
    set. Deterministic for a seed because it draws from the world's random.

    WITH SPAN ON, AT LEAST ONE STARTER IS ALWAYS A CAMPAIGN MISSION. The whole
    SPAN roster holds one cache, so it cannot reliably supply an opening, and a
    seed whose only starter turns out to have a creep-covered cache has nothing
    reachable and fails to generate. Drawing the first from the campaign set
    makes that impossible rather than unlikely.
    """
    count = min(world.options.starter_missions.value, len(STARTER_ELIGIBLE))
    if not span_enabled(world):
        return sorted(world.random.sample(list(STARTER_ELIGIBLE), count))

    first = world.random.choice(list(STARTER_ELIGIBLE))
    rest_pool = [n for n in list(STARTER_ELIGIBLE) + list(SPAN_STARTER_ELIGIBLE)
                 if n != first]
    rest = world.random.sample(rest_pool, max(0, count - 1))
    return sorted([first] + rest)


# The least WEAPON BREADTH a roster may have - how many of its checks the first
# weapon opens, summed over the whole seed.
#
# NINE, WHICH IS THE CAMPAIGN'S OWN NUMBER, so the rule reads "a mixed seed never
# opens narrower than the campaign does" rather than naming an invented constant.
# The campaign's 9 is concentrated in five missions (Farsite 3, Home 2, More and
# More 1, Tower of Darkness 1, Sequence 2) and a campaign seed always contains
# all five. SPAN carries 16 across five more (the three Pod maps are worth 14 of
# it, because loose liftic means their totems need no factory), so a mixed roster
# averages MORE breadth than the campaign - 10.5 over 4,000 seeds.
#
# THE PROBLEM IS VARIANCE, NOT THE MEAN. Drawing 19 missions from 45 can miss
# nearly all ten, and measured over 4,000 seeds the retry depth tracks this one
# quantity and nothing else:
#
#     depth 1   3888 seeds   mean breadth 10.5
#     depth 2     98 seeds   mean breadth  8.9
#     depth 3     13 seeds   mean breadth  8.0
#     depth 4      1 seed    mean breadth  5.0
#
# Opening width and the early-mission candidate count barely moved across those
# rows. Six of the 4,000 had ZERO breadth: the first weapon opened nothing
# anywhere in the seed. With the cap raised to 25 to see the real tail, one seed
# in 10,000 needed SEVEN attempts - it would have failed at the shipped cap of 5,
# where the campaign's worst in 20,000 is 4.
MIN_ROSTER_BREADTH = 9


def mission_roster(world) -> list:
    """The missions this seed actually contains - always exactly 20.

    The finale is always Founders, so 19 slots are drawn. With SPAN off that is
    simply the campaign. With it on, the starters are already fixed (see above)
    and the remaining slots come from the campaign and SPAN together.

    THE DRAW IS THEN REPAIRED RATHER THAN REJECTED. A roster short of
    MIN_ROSTER_BREADTH swaps its least useful missions for breadth-carrying ones
    until it clears, instead of being re-rolled: re-rolling would quietly bias
    the whole roster toward maps that happen to sit near breadth-carrying ones in
    the draw, while a swap changes only the missions it has to.
    """
    if not span_enabled(world):
        return list(range(1, 21))

    starters = list(world.starter_missions)
    fixed = set(starters) | {FINAL_MISSION}
    chosen = set(fixed)
    pool = [n for n in list(range(1, 21)) + sorted(SPAN_MISSION_TITLES)
            if n not in chosen]
    need = max(0, 20 - len(chosen))
    chosen |= set(world.random.sample(pool, min(need, len(pool))))

    casual = _is_casual(world)
    if roster_breadth(chosen, casual) < MIN_ROSTER_BREADTH:
        chosen = _widen_roster(world, chosen, fixed, casual)
    return _slot_order(chosen)


# Which slot of the level-select spiral the GOAL occupies, 1-based.
#
# Nineteen, which is where Founders sits in the untouched campaign. The twentieth
# position is the one vanilla parks off the map for Ever After, and
# FinalePlacement moves whatever lands there onto the map as a side branch - so
# putting the goal in it would hang the goal off the branch instead of the
# chain.
#
# Missions are OPEN, so this changes nothing about what is playable. It is about
# whether the spiral still reads as building toward the finale, which a plain
# ascending sort loses: with the SPAN Experiments on, mission numbers 21..46 all
# sort after Founders, so the goal ends up somewhere in the middle with ten maps
# drawn after it.
GOAL_SLOT = 19


def _slot_order(chosen: set) -> list:
    """The roster in LEVEL-SELECT SLOT ORDER, goal in its usual place.

    Everything else is ascending by mission number, so the campaign keeps its
    familiar order and the SPAN maps follow it in a stable one.
    """
    rest = sorted(n for n in chosen if n != FINAL_MISSION)
    if FINAL_MISSION not in chosen:
        return rest
    index = min(GOAL_SLOT - 1, len(rest))
    return rest[:index] + [FINAL_MISSION] + rest[index:]


def _is_casual(world) -> bool:
    return is_casual(world)


def roster_breadth(roster, casual: bool = False) -> int:
    """How many checks the first weapon opens across a whole roster."""
    return sum(weapon_breadth(n, casual) for n in roster)


def _widen_roster(world, chosen: set, fixed: set, casual: bool) -> set:
    """Swap breadth INTO a roster that has too little of it.

    Takes out the missions that contribute nothing a weapon can open, richest
    candidate first, and never touches a starter or the finale - those are
    already committed and swapping one out would leave the seed starting on a
    mission it does not contain.
    """
    chosen = set(chosen)
    candidates = sorted(
        (n for n in list(range(1, 21)) + sorted(SPAN_MISSION_TITLES)
         if n not in chosen and weapon_breadth(n, casual) > 0),
        key=lambda n: (-weapon_breadth(n, casual), n),
    )
    removable = sorted(n for n in chosen
                       if n not in fixed and weapon_breadth(n, casual) == 0)
    world.random.shuffle(removable)

    for candidate in candidates:
        if roster_breadth(chosen, casual) >= MIN_ROSTER_BREADTH:
            break
        if not removable:
            break            # nothing left to trade; take what breadth we have
        chosen.discard(removable.pop())
        chosen.add(candidate)
    return chosen
@functools.lru_cache(maxsize=None)
def weapon_breadth(mission: int, casual: bool = False) -> int:
    """How many of a mission's checks a WEAPON alone opens, beyond the free ones.

    CACHED because it is a pure function of the rules, and the rules are module
    state rather than seed state. It was cheap enough when only
    force_early_mission asked, once per seed; mission_roster now sums it over
    the whole roster on every SPAN seed, and _widen_roster ranks all 45
    candidates by it, which without this would re-derive every location
    requirement in the game several times per seed.

    The opening's real capacity is not how many checks are free - it is how many
    the fill's first progression item can unlock. A mission whose only early
    check is its waived cache contributes one location and then nothing, however
    many objectives it has.

    Measured, not listed, so it tracks the rules: when Miner became a logic
    requirement on Not My Mars and Ruins Repurposed, this began reporting 0 for
    them without anything else being edited.
    """
    held = {OFFENSE[0]}
    n = 0
    for name in location_names_for_mission(mission):
        reqs = location_requirements(name, mission, casual)
        if not reqs:
            continue                       # free already, not weapon-opened
        if all(any(item in held for item in group) for group in reqs):
            n += 1
    return n
