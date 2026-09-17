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


# THE FAILURES ARE NOT ABOUT THE OPENING'S SHAPE. Measured 2026-09-03 over 9000
# seeds, bucketed by how many of the two starter missions a weapon can open:
#
#     0 broad starters     2 / 5576   0.036 percent
#     1 broad starter      2 / 3223   0.062 percent
#     2 broad starters     0 /  196
#
# and two of the four failing pairs CONTAINED a broad mission - (Home,
# Shattered) and (Home, War and Peace). So constraining the starter draw to
# include Farsite or Home fixes nothing; an earlier "0 in 12,600" for that idea
# was two events against zero, which is noise, and it was retracted.
#
# The reason is the one the designer gave: an opening CHAINS. Ten missions have
# a cache reachable with a rift lab and one tower, so a thin starter's free
# cache can hold a mission unlock that opens another free cache, and since the
# greenar merge a Factory plus a weapon opens totems on fourteen missions. There
# was never a two-slot cliff; weapon_breadth measures what a weapon opens on ONE
# mission and says nothing about whether a seed can get going.
#
# What remains is the solver's own incompleteness, uncorrelated with anything we
# choose at draw time: fill_restrictive allows each item at most TWO swaps
# ("if swap_count > 1: continue") and caches three swap states, so a solvable
# instance can still defeat it. Nothing in the rules or the draw can forbid
# that, which is why the only measured fixes are ones that make the pool easier
# (the greenar merge, 4x) or the opening wider (starter_missions 3, measured
# 0/4800 against 8/4800 - declined by the designer).
#
# DO NOT TRY TO HELP ARCHIPELAGO'S FILL. Six interventions were measured on
# 2026-09-03 and every single one made generation WORSE or did nothing:
#
#     engage bootstrap_opening for standard      7x worse
#     pre-place one guaranteed broad unlock      8x worse
#     drop the early weapon request             24x worse
#     a second/third early mission unlock            worse
#     fill_hook: order locations deepest first   3.4x worse (0.111 vs 0.033)
#     fill_hook: order locations shallowest first 3x worse (0.100 vs 0.033)
#
# The shuffle Archipelago applies before filling is LOAD-BEARING: its swap and
# backtrack heuristics work across a random spread, and any order we impose
# creates correlated structure they handle worse. The same goes for taking its
# locations or its slots away from it.
#
# What DID work was changing the PROBLEM rather than the search - merging the
# greenar pair so fewer items open nothing (4x better), and refusing the
# one-starter option that cannot be filled reliably. Both came from the
# designer, not from reasoning about the fill.
#
# DO NOT PRE-PLACE ANYTHING INTO A TWO-SLOT OPENING. The first three rows of the
# table above are that experiment, and the mechanism took all three to see:
# with two starter missions there
# are exactly two locations reachable holding nothing, and Archipelago's fill
# needs BOTH of them free to run its own search and swapping. Spending one to
# guarantee something - even something as useful as a mission unlock that
# chains - costs more than the guarantee buys.
#
# This is also why a BROAD STARTER works where "reach a broad mission early"
# does not: a starter mission is open for free and consumes no slot, whereas
# arriving at one later means spending a slot on its unlock. Same destination,
# opposite effect on the fill.
#
# WHY STANDARD LOGIC IS STILL LEFT ALONE, measured 2026-09-03.
#
# After the late-mission logic review, a 32-configuration sweep found fill
# failures in about 0.06 percent of seeds (5 in 8000), all in STANDARD-logic
# configurations - the ones where the line above returns SAFE_OPENING_MIN, so
# opening_width (2 at two starters) is not LESS than the threshold (2) and the
# bootstrap never runs at all. Two fixes were tried and measured:
#
# 1. ENGAGE THE BOOTSTRAP FOR STANDARD TOO (return SAFE_OPENING_MIN + 1
#    unconditionally). MEASURED HARMFUL - it makes the failure SEVEN TIMES more
#    likely, 58 failures in 4800 seeds against 8 for the current code. It was
#    written up here as "the known remedy" on the strength of helping CASUAL,
#    which was an inference and never measured on standard. It does not
#    transfer: pre-placing consumes the two reachable opening slots, so the fill
#    inherits an opening with LESS room than it started with, and a random
#    productive choice paints it into a corner more often than its own search
#    would. It also costs:
#      - 21 tests fail, and not because they are wrong. Pre-placing removes
#        items from the pool, so "the pool holds every item" and "pool length
#        equals location count" stop being true, and access tests that call
#        collect_all_but no longer see the pre-placed items.
#      - The churn is INHERENT TO PRE-PLACING, not to how much: with the target
#        cut to 3, placements averaged 2.75 per seed and 20 tests still failed.
#      - At SAFE_OPENING = 6 it places EIGHT items on every solo standard seed,
#        which is bootstrap_opening scripting the opening - the thing its own
#        docstring promises not to do.
#
# 2. ASK FOR MORE EARLY ITEMS (a second and third early mission unlock, which
#    pre-places nothing). MEASURABLY WORSE, 300 seeds per config across five
#    configurations:
#
#        1 early unlock (current)      0 failures in 1500 seeds
#        2 early unlocks              17 failures in 1500 seeds
#        3 early unlocks              11 failures in 1500 seeds
#
#    This agrees with the numbers already in force_early_weapon: asking for more
#    early items than the opening can hold strands them.
#
# WHAT DOES WORK: a WIDER OPENING. The defect is that two starter missions give
# exactly two reachable locations, so two dud placements end the seed. Three
# starters give three, and the same 4800-seed sweep goes to ZERO failures. That
# is the fix to reach for, and it needs no pre-placing and no test churn - see
# the StarterMissions option default.
#
# For the record, the failure is also a LOUD one: generation stops with a
# FillError and the player re-rolls. That is a different class from the silent
# soft-locks this review removed, where a seed generated happily and could not
# be finished. It is still worth fixing, but it never costs a playthrough.


# How many times to re-attempt our own progression fill before the error is
# allowed out. Each attempt reshuffles, and every observed failure is the same
# shape - the opening does not chain in the first few placements, which a
# different order almost always fixes.
#
# WHY 5 WAS ENOUGH BEFORE SPAN, MEASURED (2026-09-14, 20,000 default seeds,
# cap raised to 25 so the depth each seed NEEDED could be recorded). Superseded
# by the SPAN sweep further down, which is why the constant is 8:
#
#     1 attempt   19262   96.310 percent
#     2 attempts    708    3.540
#     3 attempts     29    0.145
#     4 attempts      1    0.005
#     5 or more       0
#
# Read the tail, not the zero. Each level is about 3.7 percent of the one above
# it (708/19262 = 3.68, 29/708 = 4.1, 1/29 = 3.4), which is the same as the
# per-attempt failure rate p = 0.0369. That geometric decay is the evidence that
# attempts really are near-independent - the assumption this whole design rests
# on, and one nothing had actually checked. Because it holds, p**5 is a
# trustworthy estimate and not hand-waving: about 1 seed in 14.6 million,
# against 1 in 1,000 with no retry at all.
#
# Measuring DEPTH rather than failures is what makes this answerable. A blind
# sweep can only report "no failure yet", which at a 1-in-a-million rate is
# almost no information; roughly 4 percent of seeds retry, so the tail is
# estimable from 20,000 seeds instead of needing millions.
#
# OPENING WIDTH IS THE ONLY DRIVER WITHIN THE CAMPAIGN. A further 20,000 seeds
# across five selectable configurations: casual (bootstraps to a 6-wide opening)
# and starter_missions 6 (6-wide directly) each retried ZERO times in 4,000,
# while all-traps, no-traps and no-progressive-ERNs sat at the default 3.6 to
# 4.6 percent. Pool composition barely registers.
#
# IT IS NOT THE DRIVER ONCE THE ROSTER CAN VARY. The SPAN Experiments made the
# mission set itself a per-seed draw, and the quantity that then predicts retry
# depth is WEAPON BREADTH - see MIN_ROSTER_BREADTH, which is the fix. Opening
# width sat at 2.00 across every depth in that measurement while breadth fell
# from 10.5 to 5.0. Both statements are true; the first is about a fixed roster.
#
# WHY 8 AND NOT 5 (2026-09-16, 50,000 seeds, cap raised to 25 again). With the
# breadth floor in place the worst SPAN configuration still reaches depth 5:
#
#     configuration            1      2    3   4   5   deepest
#     span off, default      9729    253   17   1   -     4
#     span ON,  default      9758    222   18   2   -     4
#     span off, min starters 9765    223   12   -   -     3
#     span ON,  min starters 9782    202   14   1   1     5
#     span ON,  all finale   9793    188   16   3   -     4
#
# Zero failures in all 50,000, and the shape is geometric again - the one seed
# at 5 is the tail, not a separate population. But a cap of 5 against an
# observed depth of 5 is ZERO margin, and the campaign's own cap was chosen when
# the deepest observed was 4. The honest response to a measurement that moved is
# to move the cap with it.
#
# BEFORE the breadth floor, the same measurement found a seed needing SEVEN
# attempts in 10,000 - it would have failed outright at a cap of 5. That is what
# this number exists to absorb, and why it is set from the tail rather than from
# a round figure.
#
# The cost of the extra attempts is nothing: an attempt only happens when the
# previous one failed, which is 2 percent of seeds at depth 2 and effectively
# never past 5.
OWN_FILL_ATTEMPTS = 8

# SOLO SEEDS ONLY. place_own_progression returns immediately in any multiworld
# that contains another game.
#
# WHAT A MULTIWORLD WOULD PAY, which is why it is off there: our progression
# would be placed into our OWN locations, so it could no longer live in another
# player's world. World.needs_bootstrap measures what that is worth - "4 CW4
# progression items per seed living in the other world" over 40 seeds - and that
# cross-game placement is most of the point of a multiworld. The retry loop
# itself costs nothing when nothing fails; the loss is where the items end up.
#
# Set to False to run the fill on every seed.
#
# (This comment used to say the opposite - "the designer chose every seed" - and
# ended "Set to True to make this solo-only" directly above a constant that was
# already True. It was wrong from the commit that introduced it and survived
# every audit until 2026-09-17. A reader would have concluded that a multiworld
# exercises the retry path, which it does not.)
OWN_FILL_SOLO_ONLY = True


def place_own_progression(world) -> list:
    """Place OUR progression into OUR locations, retrying on failure.

    WHY. Archipelago's main fill is greedy and its backtracking is hard-capped -
    each item may be swapped at most twice (Fill.py: "if swap_count > 1:
    continue") behind a three-entry state cache - so a SOLVABLE arrangement can
    still defeat it. Ours defeated it about once in 18,000 seeds, always the
    same way: the first few placements fail to chain and it stops with the world
    still empty. A captured failure had 231 of 236 locations unfilled and 15
    mission unlocks still in hand.

    A world cannot catch or retry the MAIN fill. It can place its own items and
    retry, which is exactly what oot does for songs (6 attempts) and
    pokemon_emerald for badges and HMs. This is that idiom.

    Two things distribute_items_restrictive does around fill_restrictive that we
    must therefore do ourselves:
      - EXCLUDED locations must never take progression, or a player's
        exclude_locations option is silently ignored.
      - PRIORITY locations should be used first. fill_restrictive takes the
        first VALID location in list order, so putting them at the front is
        enough to honour priority_locations.
    """
    from BaseClasses import LocationProgressType
    from Fill import FillError, fill_restrictive, sweep_from_pool

    mw = world.multiworld
    if OWN_FILL_SOLO_ONLY and not all(mw.worlds[p].game == world.game
                                      for p in mw.player_ids):
        return []

    ours = [item for item in mw.itempool
            if item.player == world.player and item.advancement]
    if not ours:
        return []

    priority, default = [], []
    for loc in mw.get_unfilled_locations(world.player):
        if loc.address is None:
            continue                     # event locations already hold their item
        if loc.progress_type == LocationProgressType.EXCLUDED:
            continue                     # the player asked for no progression here
        (priority if loc.progress_type == LocationProgressType.PRIORITY
         else default).append(loc)

    if OWN_FILL_ATTEMPTS < 1:
        return []            # switched off - see test/bases.py CW4TestBase

    placed = []
    for attempt in range(1, OWN_FILL_ATTEMPTS + 1):
        world.random.shuffle(priority)
        world.random.shuffle(default)
        locations = priority + default
        pool = list(ours)
        world.random.shuffle(pool)
        filled = []
        try:
            fill_restrictive(mw, sweep_from_pool(mw.state), locations, pool,
                             single_player_placement=True, lock=False,
                             on_place=filled.append, name="CW4 own progression")
            placed = [(loc.name, loc.item.name) for loc in filled]
            # Recorded so a run can show how often the retry actually fires and
            # whether it recovers - the only direct evidence that the retry, and
            # not luck, is what removed the failures.
            world.own_fill_attempts = attempt
            break
        except FillError:
            # Undo the attempt completely before reshuffling, the way
            # pokemon_emerald does, or the next attempt inherits half a fill.
            for loc in filled:
                if loc.item is not None:
                    loc.item.location = None
                    loc.item = None
                loc.locked = False
            if attempt == OWN_FILL_ATTEMPTS:
                raise

    for _loc_name, item_name in placed:
        for item in mw.itempool:
            if item.player == world.player and item.name == item_name:
                mw.itempool.remove(item)
                break
    return placed
