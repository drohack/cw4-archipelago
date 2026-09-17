"""How wide a seed opens, and what the world does when that is too narrow.

SPLIT OUT OF items.py on 2026-09-17, alongside roster.py and for the same
reason: this is the second half of the concern that sat upstream of everything
it depends on.

Everything here reasons about REACHABILITY - how many locations a player can
take holding nothing, and which single item to place early when that number is
too small to let Archipelago's own fill get going. All of it needs rules.py and
locations.py, and both are downstream of items.py, so in items.py every one of
these functions had to import them inside its own body.

The measurements are the point of this file. SAFE_OPENING, its rejected
alternatives and the nine-thousand-seed sweep below are why the numbers are the
numbers, and they are carried across unchanged.
"""
from .items import ALL_MISSION_TITLES
from .locations import location_names_for_mission
from .roster import STARTER_ELIGIBLE, weapon_breadth
from .rules import is_casual, location_requirements


# How many empty reachable locations the opening needs before Archipelago's fill
# is safe to take over. Two is not enough: one wasted placement still strands it.
#
# ONLY CASUAL SEEDS EVER READ THIS. Measured 2026-09-03: bootstrap_opening did
# not run on ANY of 200 default-option seeds, because bootstrap_threshold is
# SAFE_OPENING_MIN (2) outside casual while opening_width at two starters is
# exactly 2, so "2 < 2" is false. Tuning this value therefore cannot help a
# standard-logic seed - a fact worth knowing before anyone tries, as two
# sessions of slack tuning did.
#
# RAISED FROM 4 TO 6 on 2026-09-03, because casual logic got stricter and 4
# stopped being enough. Greenar is now required for the totems on three more missions
# (Shattered, Wallis, Founders), Nullifier for every Reclaim, and Miner for Not
# My Mars and Ruins Repurposed - which also turned Shattered from a mission a
# weapon could open into one it cannot, leaving Farsite and Home as the only two
# that carry an opening by themselves.
#
# More requirements means more ways for the fill's next pick to be half of a pair
# and open nothing, which is the failure bootstrap_opening exists to outlast.
# Measured on TestCasualLogic, the harshest tier, 400 seeds per value, with a
# positive control:
#
#     SAFE_OPENING = 4     3/400 failed   (0.8 percent)
#     SAFE_OPENING = 6     0/400
#     SAFE_OPENING = 8     0/400
#
# EIGHT WAS TRIED AND REJECTED. It did not lower the residual (a 32-config
# sweep found stragglers at both six and eight, around 0.3 percent of seeds in
# whichever configurations the sample happened to hit), and it broke
# TestCasualLogic.test_anti_air_is_required_from_the_first_spores: with eight
# locations to open, bootstrap_opening pre-places enough progression that
# anti-air is already in the state, so a check that casual logic should gate was
# reachable. That is the bootstrap SCRIPTING the opening, which its own docstring
# says it must not do. Six is the most slack this world can take before the
# opening stops being the player's to discover.
#
# The residual is therefore NOT a slack problem, and its cause is the one named in
# bootstrap_opening: the fill cannot see that a requirement naming two items is a
# PAIR, so a lone one of them opens nothing. The Greenar Refinery and Factory were
# the worked case and that pair has since been merged away, which measurably
# helped - but requiring greenar on three more missions widened the shape again. A
# pair-aware bootstrap is the real fix; slack only outlasts the trap rather than
# removing it.
#
# The cost is bounded: SAFE_OPENING is read only by bootstrap_opening, and
# needs_bootstrap also requires this world to be alone in the multiworld, so a
# real multiworld never pays for it.
#
# A single 100-seed sample said 0/100 at SAFE_OPENING = 4 while another said
# 4/100. At rates this low, one sample of a hundred cannot tell a fix from luck -
# use tools/audit/fillrate.py with N in the hundreds before believing any change
# here.
SAFE_OPENING = 6

# Below this, the opening cannot absorb a single wasted placement and the world
# steps in. At or above it, Archipelago's fill is left alone.
SAFE_OPENING_MIN = 2


def bootstrap_threshold(world) -> int:
    """The opening width below which the world widens it itself.

    CASUAL NEEDS ONE MORE THAN THE REST, and this is measured rather than
    cautious. CI hit a FillError in TestCasualLogic, and sampling the exact
    configuration on Archipelago 0.6.7 - with a positive control, because an
    unverified zero is worthless - put the rate at roughly 0.2 to 1 percent of
    casual seeds. Present in the apworld both before and after the filler work,
    so it is long-standing rather than new.

    Why casual specifically: rules._casual_defense ADDS a defensive requirement
    from CASUAL_DEFENSE_FROM onward, so casual is the HARSHER setting despite
    the name. At the default two starters the opening is two locations wide,
    which clears SAFE_OPENING_MIN and is still far below the SAFE_OPENING of 4
    this module treats as slack - and every extra requirement raises the chance
    that the fill's next progression item opens nothing. bootstrap_opening's own
    docstring is the description of that failure: "one item that opens nothing
    ends the seed".

    Deliberately +1 and not more. Bootstrapping costs the cross-game placements
    that make a narrow opening interesting (see World.needs_bootstrap), so it
    buys exactly enough slack for casual's extra requirement and no more.
    """
    return SAFE_OPENING_MIN + 1 if is_casual(world) else SAFE_OPENING_MIN
def force_early_mission(world) -> None:
    """Unlock one more mission early, so the opening widens past the starters.

    PREFERS a mission a weapon can actually open several checks on.

    It used to choose uniformly from STARTER_ELIGIBLE, which was fine while most
    of that set had weapon-openable checks. It stopped being fine on 2026-09-03:
    Miner became a logic requirement on Not My Mars and Ruins Repurposed and
    Nullifier gated every Reclaim, which left 7 of the 10 starter-eligible
    missions with a waived cache and nothing else a weapon can reach. The chance
    of a default two-starter seed opening entirely onto such missions went from
    22 to 47 percent, and the world's own test configurations began failing to
    fill in roughly 1 to 3 percent of seeds.

    Widening the bootstrap instead was tried and rejected: needs_bootstrap also
    governs the early-weapon guarantee and the pool accounting, so making it
    unconditional disabled force_early_weapon and broke the
    pool-exactly-fills-locations invariant on every solo seed. This is the
    surgical version - it changes WHICH mission is granted, nothing else, and
    only when the starters cannot already carry the opening themselves.
    """

    if opening_width(world) < bootstrap_threshold(world):
        return  # bootstrap_opening owns these slots
    roster = set(world.mission_roster)
    options = [n for n in STARTER_ELIGIBLE
               if n not in world.starter_missions and n in roster]
    if not options:
        # NOTHING STARTER-ELIGIBLE LEFT IN THE SEED, which cannot happen on a
        # campaign seed (all ten are always present) and happened on 1.1 percent
        # of mixed ones, where it silently granted nothing at all. Fall back to
        # the rest of the roster rather than returning: what this method is FOR
        # is a mission a weapon can open several checks on, and the filter below
        # is what actually decides that - starter-eligibility is a proxy for it,
        # not the requirement.
        options = [n for n in roster if n not in world.starter_missions]
    if not options:
        return
    casual = is_casual(world)
    # If a starter already opens up under a weapon, the opening is not fragile
    # and the choice stays free - a uniform pick keeps openings varied.
    starters_carry = any(weapon_breadth(n, casual) > 0
                         for n in world.starter_missions)
    if not starters_carry:
        broad = [n for n in options if weapon_breadth(n, casual) > 0]
        if broad:
            options = broad
    # ALL_MISSION_TITLES, not MISSION_TITLES: the fallback above can pick a SPAN
    # mission, and the campaign-only table raises KeyError on one.
    name = f"Mission Unlock: {ALL_MISSION_TITLES[world.random.choice(options)]}"
    # LOCAL early items, per the Archipelago FAQ's first remedy for a
    # restrictive start (docs/apworld_dev_faq.md, "My game has a restrictive
    # start that leads to fill errors"). early_items may be satisfied in ANOTHER
    # player's world (Fill.py splits them into early_local_prog_items and
    # early_local_rest_items), which does nothing for an
    # opening that needs OUR locations to chain. Identical for a solo seed;
    # correct for a multiworld.
    world.multiworld.local_early_items[world.player][name] = 1


def opening_width(world) -> int:
    """How many locations are collectable from a standing start, holding nothing.

    This is what decides whether the seed can afford more than one early item.
    Imported late for the same reason `classification` does it: locations imports
    items, so a top-level import of either here would be a cycle.
    """
    casual = is_casual(world)
    return sum(
        1
        for mission in world.starter_missions
        for name in location_names_for_mission(mission)
        if not location_requirements(name, mission, casual)
    )


def force_early_weapon(world) -> None:
    """Guarantee which of the two offense weapons arrives first.

    OFFENSE is an OR group, so a seed only ever needs one of Cannon and Mortar -
    and the one it does not need becomes logically redundant and can turn up
    anywhere. Which one wins was previously the fill's business, measured at 13
    Cannon to 7 Mortar over 20 seeds, with the reversed-order control proving that
    a coin flip rather than an ordering bias.

    early_items is the right tool and the only honest one. Logic CANNOT express
    this preference: a rule saying a mission needs a mortar specifically would be
    false wherever a cannon also works, and logic is not the place to record a
    taste in pacing. Placement is.

    The same mechanism already forces a second mission unlock early, so a seed can
    ask for two early items; if the opening is narrow enough that there are not
    two free locations to hold them, Archipelago places what it can and the rest
    fall where they fall.
    """
    # "random" needs no branch here: Archipelago resolves it while parsing the
    # yaml, so by now the option holds mortar or cannon either way.
    opt = world.options.early_weapon
    name = "Mortar" if opt.value == opt.option_mortar else "Cannon"

    # If the opening is one location wide, that location must hold a MISSION
    # UNLOCK, and the weapon has to wait. This is not a preference, it is the
    # difference between a seed that generates and one that does not.
    #
    # Measured at starter_missions: 1, 60 seeds per setting:
    #
    #     early items requested        seeds that failed to generate
    #     mission unlock only          0 of 60
    #     nothing                      2 of 60
    #     both (unlock and weapon)     7 of 60
    #     weapon only                  13 of 60
    #
    # The reason is what each item opens. An unlock chains: it turns the one free
    # cache into another mission with a free cache, and so on, which is the
    # intended shape of a narrow opening. A weapon widens the mission you are
    # already in but adds no new MISSIONS, so the other nineteen unlocks all have
    # to thread through one mission's locations, and fill_restrictive runs out of
    # legal spots - always with exactly one item left over, typically a mission
    # whose own locations are the only ones still empty.
    #
    # Archipelago was already dropping one of the two here and saying so ("Ran out
    # of early locations for early items"), so the guarantee was never being kept
    # at this width. This makes which one gets dropped deterministic, and picks
    # the one that does not break generation.
    #
    # AND THE OPPOSITE IS TRUE AT TWO STARTERS. Dropping the early WEAPON
    # request there - on the theory that a weapon opens no new mission and was
    # competing with the unlock for two slots - made things 24 TIMES WORSE:
    # 71 failures in 6000 seeds against 3. Measured 2026-09-03. The width-1
    # numbers above do NOT generalise: at two slots the early weapon is doing
    # most of the work, presumably because it opens the rest of whichever
    # starter mission can use it. Do not remove this request.
    if opening_width(world) < bootstrap_threshold(world):
        # bootstrap_opening handles this width instead, and can still honour the
        # weapon - see there. Requesting it here would only claim the single
        # sphere-0 location and leave the chain nowhere to go.
        world.early_weapon = name
        return

    world.early_weapon = name
    # LOCAL, for the same reason as force_early_mission above.
    world.multiworld.local_early_items[world.player][name] = 1


def bootstrap_opening(world) -> list:
    """Widen a one-location opening before the general fill runs.

    WHY THIS EXISTS. At `starter_missions: 1` exactly one check is reachable
    holding nothing, because every starter-eligible mission has exactly one cache
    collectable with a rift lab and a single tower. Archipelago's fill places
    progression items one at a time without looking ahead, which is fine when
    there is slack and fatal when there is none: one item that opens nothing ends
    the seed. Measured, that killed 12 percent of one-starter seeds before any of
    this, and 1.3 percent after the early-items half of the fix.

    A worked example, seed 20100, which is what this is written against:

        We Know Nothing - Cache 1         -> Mission Unlock: Somewhere in Spacetime
        Somewhere in Spacetime - Cache 1  -> Cannon
        Somewhere in Spacetime - Reclaim  -> Factory      <- opens nothing

    At the time, Totems wanted Greenar Refinery AND Factory, so a lone Factory was
    half a pair and unlocked nothing; 29 items were left with nowhere to go.
    Archipelago cannot see that two items are a pair, and it has no reason to.

    THAT PARTICULAR PAIR IS GONE - the Refinery was retired into the Factory, and
    the merge is one of the few changes measured to make generation better. The
    SHAPE is not gone: any requirement naming two items does the same thing, and
    a dud pick at width one still ends the seed. The example is kept because it is
    the clearest recorded instance of the failure, not because it can recur.

    WHAT THIS DOES NOT DO is script the opening. The item is drawn at random from
    everything that would actually open something, and the location from every
    reachable empty one, so two seeds with the same starter still differ. It only
    refuses to pick an item that opens nothing while the opening is too narrow to
    survive a dud, and it stops as soon as there is slack - after that the fill
    is on its own, exactly as everywhere else.

    The chosen early weapon gets first refusal, so `early_weapon` still means
    something at this width whenever the weapon is one of the productive choices.
    On eight of the ten possible starters a weapon opens between one and five
    checks; on We Know Nothing and The Experiment it opens none, and there it will
    not be picked, because picking it would end the seed.
    """
    from BaseClasses import CollectionState

    mw = world.multiworld
    player = world.player

    def open_empty(state):
        return [loc for loc in mw.get_locations(player)
                if loc.item is None and loc.address is not None and loc.can_reach(state)]

    state = CollectionState(mw)
    placed = []
    for _ in range(SAFE_OPENING + 2):
        empty = open_empty(state)
        if len(empty) >= SAFE_OPENING:
            break
        # One candidate per NAME - the pool holds duplicates and they are
        # interchangeable here.
        seen = {}
        for item in mw.itempool:
            if item.player == player and item.advancement:
                seen.setdefault(item.name, item)
        productive = []
        for item in seen.values():
            trial = state.copy()
            trial.collect(item, prevent_sweep=False)
            if len(open_empty(trial)) > len(empty):
                productive.append(item)
        if not productive or not empty:
            break
        # The weapon the player asked for goes first when it is a real choice.
        wanted = [i for i in productive if i.name == getattr(world, "early_weapon", "")]
        item = wanted[0] if wanted else world.random.choice(productive)
        location = world.random.choice(empty)
        location.place_locked_item(item)
        mw.itempool.remove(item)
        state.collect(item, prevent_sweep=False)
        placed.append((location.name, item.name))
    return placed
