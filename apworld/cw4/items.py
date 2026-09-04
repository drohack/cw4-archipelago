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

# EVERY mission's unlock exists as an item name, including the ones that end up
# starting unlocked. Item ids must not depend on which missions a seed happens to
# start with - excluding starters here would shift every later id whenever a
# player changed the option.
MISSION_UNLOCK_ITEMS = [f"Mission Unlock: {MISSION_TITLES[n]}" for n in range(1, 21)]


def starter_missions(world) -> list:
    """The missions this seed starts with, chosen at random from the eligible
    set. Deterministic for a seed because it draws from the world's random."""
    count = min(world.options.starter_missions.value, len(STARTER_ELIGIBLE))
    return sorted(world.random.sample(list(STARTER_ELIGIBLE), count))

# Vanilla-schedule units that become items. Only riftlab and tower are always
# available - without a base and energy a mission cannot be started at all.
# Pylon is an unlockable (towers relay on their own).
UNIT_ITEMS = [
    "Cannon", "Mortar", "Nullifier", "Miner", "Factory", "Greenar Refinery",
    "Missile Launcher", "Sprayer", "Terp", "ERN Portal", "Sniper", "Porter",
    "Pylon", "Bomber Pad", "Runway", "Shield", "AC Bomber Pad", "Chronat",
    "Microrift", "Platform", "Rocket Pad",
]

# Never unlocked in vanilla - pure bonus items.
BONUS_UNIT_ITEMS = ["Airship", "Bertha", "Sweeper"]

# Names that still EXIST but are no longer generated.
#
# "there's litterlay no reason to have a refinery without the factory"
# (designer, 2026-09-03), and as two items they were the campaign's biggest
# generation hazard: totems on 14 missions need both, as do Chronat, Platform
# and Rocket Pad, so a lone one of the pair opened nothing and Archipelago's
# fill - one item at a time, blind to pairs - could strand a seed on it. The
# Factory item now unlocks the refinery as well (see UnitRules.ItemAlsoUnlocks
# in the plugin).
#
# The NAME has to stay in UNIT_ITEMS: item ids are positional, so removing it
# would renumber every id after it and break every existing seed. Retiring
# rather than deleting is what the Build Limit items did.
RETIRED_ITEMS = {"Greenar Refinery"}

PROGRESSIVE_ERN = "Progressive ERN"
PROGRESSIVE_ERN_COUNT = 4

# Every name that HAS an id. Like Emitter Overdrive these are deliberately still
# here and deliberately not generated - see POOL_FILLER_KINDS below. Dropping the
# names outright would renumber every id after them, and ids must not move.
BUILD_LIMIT_ITEMS = [
    "Build Limit +1 (Tower)",
    "Build Limit +1 (Cannon)",
    "Build Limit +1 (Mortar)",
]

# Energy upgrades, both applied to the rift lab - measured as the only real
# levers the game exposes (docs/research-findings.md, "Energy: the store is the
# rift lab's ammo"). Storage raises its MAX_AMMO, generation adds to its ammo
# each tick.
#
# The names carry NO amounts. The amounts are yaml options and travel in
# slot_data, because ITEM_NAME_TO_ID has to be identical across every yaml - a
# name like "Energy Storage +50" would change ids whenever a player retuned an
# option and break the client.
ENERGY_STORAGE_ITEM = "Progressive Energy Storage"
BASE_GENERATION_ITEM = "Progressive Base Generation"

# ERN port upgrade items. Two per upgrade, six upgrades, twelve names.
#
# Names must match CW4Archipelago.Core.ErnUpgradeRules exactly - the mod counts
# received items by these strings - and a test pins both halves.
#
# The two axes are deliberately separate, and measurement is why. An earlier
# version had the cap also double the fill rate, which left the rate item with
# nothing to sell; see docs/ern-upgrade-measurements.md.
#
#     Progressive ERN Efficiency Rate: <upgrade>   fills faster, 4x at four copies
#     Progressive ERN Efficiency Cap:  <upgrade>   reaches higher, 200% (150% Build Speed)
#
# Measured effect at the cap, all six confirmed in game:
#
#     Mine Production    3.00x production
#     Move Speed         about 2.8x
#     Fire Rate          2.00x rate (reload 8 -> 4)
#     Build Speed        1.88x (its own lower ceiling)
#     Energy Production  1.63x
#     Fire Range         1.50x range (cannon 9 -> 13 cells)
ERN_UPGRADE_NAMES_ORDER = [
    "Energy Production", "Mine Production", "Build Speed",
    "Move Speed", "Fire Range", "Fire Rate",
]
ERN_RATE_PREFIX = "Progressive ERN Efficiency Rate: "
ERN_CAP_PREFIX = "Progressive ERN Efficiency Cap: "

ERN_RATE_ITEMS = [ERN_RATE_PREFIX + u for u in ERN_UPGRADE_NAMES_ORDER]
ERN_CAP_ITEMS = [ERN_CAP_PREFIX + u for u in ERN_UPGRADE_NAMES_ORDER]
ERN_UPGRADE_ITEMS = ERN_RATE_ITEMS + ERN_CAP_ITEMS

# A FIFTH COPY DOES NOTHING, so four is the whole supply of each name.
#
# This is why they cannot go through filler_sequence: that draws with
# replacement by weight, so it could hand a player nine copies of one name -
# five of them inert - and none of another. An item that does nothing is the
# exact defect that got build limits pulled from the pool, so these are
# generated as FIXED counts instead.
ERN_UPGRADE_MAX_COPIES = 4

# One-shot BENEFICIAL items - the mirror of the traps, and the pool's only
# honest padding.
#
# Every cumulative filler kind now has a ceiling: the ERN upgrades stop at four
# copies each and the energy upgrades stop at the count that reaches their
# maximum. A one-shot effect has no ceiling, because each firing is independent
# and the tenth copy is worth what the first was - the same reason a trap does
# not saturate.
#
# Names must match CW4Archipelago.Core.BoonRules; the audit pins both sides.
ERN_SURGE_PREFIX = "ERN Surge: "
ERN_SURGE_ITEMS = [ERN_SURGE_PREFIX + u for u in ERN_UPGRADE_NAMES_ORDER]

# Nine names, so no single one dominates the padding.
#
#   Ammo Resupply / Energy Cache / Field Shield   one-shot, no infrastructure
#   Resource Cache                                needs a factory, else whiffs
#   ERN Surge: <upgrade>  x6                      that upgrade at the game's own
#                                                 100 percent for a while, with
#                                                 no portal and no docked ERN
#
# The surges are capped at 100 percent on purpose, so the permanent
# "Progressive ERN Efficiency Cap" items stay strictly better - a surge is a
# taste of an upgrade, not a substitute for owning it.
BOON_ITEMS = (["Ammo Resupply", "Energy Cache", "Field Shield", "Resource Cache"]
              + ERN_SURGE_ITEMS)

FILLER_ITEMS = BUILD_LIMIT_ITEMS + [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Build limits are NOT generated (designer, 2026-09-01).
#
# Every building starts at the game's "unlimited" sentinel of -1, so there is no
# limit to raise. UnitGate.ApplyLimits already refuses to touch those, and
# correctly: writing base+1 over an unlimited unit would CAP something that had no
# cap, turning a bonus item into a penalty. The consequence is that a build-limit
# item does nothing, on every unit, on every mission - not "nothing yet", nothing
# at all.
#
# That is the same rule Emitter Overdrive was removed under, only more so: it fails
# on the whole campaign rather than a third of it. At the default weights this was
# 24 of 256 items in a seed, so roughly one check in ten paid out a message with
# nothing behind it, and there was no in-game signal to notice.
#
# HELD LOOSELY, and cheap to reopen. Nothing else was removed: the ids, the
# UnitRules mapping, UnitGate's base-capture and increment, and the yaml weight all
# still work. If a mission is found that ships a real limit - or if limits are ever
# introduced deliberately, which is the more likely route - putting "build_limit"
# back in this list is the whole change.
POOL_FILLER_KINDS = [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Traps. Every effect is temporary and recoverable by design - a trap may sting,
# but none may make a mission unwinnable, which is why permanent terrain
# deformation was dropped during the feasibility spike. Names must match
# CW4Archipelago.Core.TrapRules exactly; a test pins that.
# Every trap that HAS an id. Emitter Overdrive is deliberately still here and
# deliberately not in POOL_TRAP_ITEMS below: dropping the name outright would
# renumber every id after it, and ids are the one thing that must not move.
TRAP_ITEMS = [
    "Spore Strike",
    "Spore Scatter",
    "Creeper Surge",
    "Energy Drain",
    "Emitter Overdrive",
    "Unit Stun",
    "Ammo Drain",
]

# Emitter Overdrive is NOT generated (designer, 2026-08-31).
#
# The traps spike set the rule that admits an effect to the pool: it must fire on
# essentially every mission, or carry a fallback for the ones it cannot. Its own
# reasoning for that rule is that "a trap item that silently does nothing is a bad
# item" - the player spends a check, receives a trap, nothing happens, and the
# whole trap pool starts to feel broken.
#
# Emitter Overdrive meets neither half. It no-ops where a mission ships no
# emitters, logging "no emitters on this map - trap had no effect", and it has no
# fallback. Emitters are present at mission START on 11 of 20 missions, so it is
# dead on roughly a quarter to a third of the campaign. Every other trap depends
# only on things every mission has: the world grid, the energy store, and the
# player's own units.
#
# HELD LOOSELY, and here is exactly what would reopen it. The 11-of-20 figure
# counts emitters at mission start only; enemies arrive during play, so real
# coverage is better and possibly much better. If someone measures emitter counts
# a few minutes in and it is more like 16 of 20, "essentially every mission"
# becomes arguable and this decision should be revisited. Nothing else was
# removed to make that easy: the effect, the applier mapping, the trap: debug
# command and the yaml weight all still work, so putting the name back in this
# list is the whole change.
POOL_TRAP_ITEMS = [t for t in TRAP_ITEMS if t != "Emitter Overdrive"]

# APPENDED, never inserted. Item ids are positional, and the client's
# ITEM_NAME_TO_ID has to match across every yaml - inserting a name anywhere but
# the end renumbers everything after it.
_all_names = (
    MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS
    + [PROGRESSIVE_ERN] + FILLER_ITEMS + TRAP_ITEMS
    + ERN_UPGRADE_ITEMS + BOON_ITEMS
)
ITEM_NAME_TO_ID = {name: BASE_ID + i for i, name in enumerate(_all_names)}


class CW4Item(Item):
    game = "Creeper World 4"


def classification(name: str) -> ItemClassification:
    # Mission unlocks gate their region (see regions.py), so they are always
    # progression even though they never appear in an access RULE.
    if name in MISSION_UNLOCK_ITEMS:
        return ItemClassification.progression

    if name in UNIT_ITEMS:
        # Only buildings that actually gate something are progression. The rest
        # are real benefits nobody is blocked on - marking them progression would
        # have the fill prioritise them and progression balancing drag them into
        # early spheres, crowding out the items that genuinely open the game.
        #
        # Imported late: locations.py imports this module, so a top-level import
        # of rules would be a cycle.
        from .rules import logic_item_names
        return (ItemClassification.progression if name in logic_item_names()
                else ItemClassification.useful)
    if name in BONUS_UNIT_ITEMS or name == PROGRESSIVE_ERN:
        return ItemClassification.useful
    # Energy upgrades are a real, measurable benefit - more building before you
    # stall - so they are useful rather than filler.
    if name in (ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM):
        return ItemClassification.useful
    if name in TRAP_ITEMS:
        return ItemClassification.trap
    # ERN upgrades are FILLER rather than useful, unlike the energy upgrades.
    #
    # The difference is that they do nothing at all until the player has been
    # given the ERN Portal unlock, built one, and docked an ERN in the matching
    # slot. That is real optional infrastructure, so an ERN upgrade arriving
    # early can sit dead for a long time, where an energy upgrade always pays
    # out immediately.
    #
    # HELD LOOSELY: the effects are large once live (Mine Production triples
    # production), so if these end up feeling like real rewards rather than
    # padding, promoting them to useful is a one-line change.
    if name in ERN_UPGRADE_ITEMS:
        return ItemClassification.filler
    # A one-shot benefit, and genuinely filler: welcome when it lands, never
    # something anyone is blocked on.
    if name in BOON_ITEMS:
        return ItemClassification.filler
    return ItemClassification.filler


def create_item(world, name: str) -> CW4Item:
    return CW4Item(name, classification(name), ITEM_NAME_TO_ID[name], world.player)




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
# The residual is therefore NOT a slack problem, and its cause is named in
# bootstrap_opening: the fill cannot see that Greenar Refinery and Factory are a
# PAIR, so a lone one of them opens nothing. Requiring greenar on three more
# missions made that trap more common. A pair-aware bootstrap is the real fix;
# slack only outlasts the trap rather than removing it.
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
    from .rules import is_casual
    return SAFE_OPENING_MIN + 1 if is_casual(world) else SAFE_OPENING_MIN


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
# DO NOT TRY TO HELP ARCHIPELAGO'S FILL. Five interventions were measured on
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
# DO NOT PRE-PLACE ANYTHING INTO A TWO-SLOT OPENING. Measured three separate
# ways on 2026-09-03, and every one made generation WORSE:
#
#     bootstrap_opening for standard        7x worse   (1.208 vs 0.167 percent)
#     one guaranteed broad mission unlock   8x worse   (0.278 vs 0.033 percent)
#     a second/third early mission unlock   worse      (17 and 11 vs 0 failures)
#
# The mechanism, which took all three to see: with two starter missions there
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


def weapon_breadth(mission: int, casual: bool = False) -> int:
    """How many of a mission's checks a WEAPON alone opens, beyond the free ones.

    The opening's real capacity is not how many checks are free - it is how many
    the fill's first progression item can unlock. A mission whose only early
    check is its waived cache contributes one location and then nothing, however
    many objectives it has.

    Measured, not listed, so it tracks the rules: when Miner became a logic
    requirement on Not My Mars and Ruins Repurposed, this began reporting 0 for
    them without anything else being edited.
    """
    from .locations import location_names_for_mission
    from .rules import OFFENSE, location_requirements
    held = {OFFENSE[0]}
    n = 0
    for name in location_names_for_mission(mission):
        reqs = location_requirements(name, mission, casual)
        if not reqs:
            continue                       # free already, not weapon-opened
        if all(any(item in held for item in group) for group in reqs):
            n += 1
    return n


# How many times to re-attempt our own progression fill before the error is
# allowed out. Each attempt reshuffles, so attempts are near-independent: one
# attempt fails about 1 seed in 18,000, and every observed failure is the same
# shape - the opening does not chain in the first few placements, which a
# different order almost always fixes.
OWN_FILL_ATTEMPTS = 5

# Every seed, or only solo ones?
#
# The designer chose every seed (2026-09-03), reasoning that a multiworld can
# fail too. The retry LOOP costs nothing when nothing fails, but the fill itself
# runs every time, and that is what a multiworld pays: our progression is placed
# into our own locations, so it can no longer live in another player's world.
# World.needs_bootstrap records the measurement of what that is worth - "4 CW4
# progression items per seed living in the other world" over 40 seeds. Set to
# True to make this solo-only.
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
    options = [n for n in STARTER_ELIGIBLE if n not in world.starter_missions]
    if not options:
        return
    from .rules import is_casual
    casual = is_casual(world)
    # If a starter already opens up under a weapon, the opening is not fragile
    # and the choice stays free - a uniform pick keeps openings varied.
    starters_carry = any(weapon_breadth(n, casual) > 0
                         for n in world.starter_missions)
    if not starters_carry:
        broad = [n for n in options if weapon_breadth(n, casual) > 0]
        if broad:
            options = broad
    name = f"Mission Unlock: {MISSION_TITLES[world.random.choice(options)]}"
    # LOCAL early items, per the Archipelago FAQ's first remedy for a
    # restrictive start (docs/apworld_dev_faq.md, "My game has a restrictive
    # start that leads to fill errors"). early_items may be satisfied in ANOTHER
    # player's world (Fill.py:426-470 splits the two), which does nothing for an
    # opening that needs OUR locations to chain. Identical for a solo seed;
    # correct for a multiworld.
    world.multiworld.local_early_items[world.player][name] = 1


def opening_width(world) -> int:
    """How many locations are collectable from a standing start, holding nothing.

    This is what decides whether the seed can afford more than one early item.
    Imported late for the same reason `classification` does it: locations imports
    items, so a top-level import of either here would be a cycle.
    """
    from .locations import location_names_for_mission
    from .rules import is_casual, location_requirements
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

    Totems want Greenar Refinery AND Factory, so a lone Factory is half a pair and
    unlocks nothing; 29 items were left with nowhere to go. Archipelago cannot see
    that the two are a pair, and it has no reason to.

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


def energy_useful_copies(world) -> tuple:
    """How many of each energy upgrade to generate.

    The copy count IS the setting: the per-copy value is the maximum divided by
    it, so the last copy lands exactly on the maximum and there is no such thing
    as a spare. Generating more would put dead items in the pool, which is the
    defect that got build limits removed.

    Mirrors EnergyRules.UsefulCopies, which is the identity for the same reason.
    """
    o = world.options
    return o.energy_storage_copies.value, o.base_generation_copies.value


def create_all_items(world) -> None:
    pool = []
    # No weapon is granted. Every weapon is a real check, so cannon, mortar and
    # sprayer each arrive when the multiworld decides rather than all at once.
    #
    # A starter mission's unlock is not in the pool - the player already has it -
    # but the ITEM still exists, so ids are unaffected.
    starters = {f"Mission Unlock: {MISSION_TITLES[n]}" for n in world.starter_missions}
    for name in MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS:
        if name in starters or name in RETIRED_ITEMS:
            continue
        pool.append(create_item(world, name))
    for _ in range(world.options.progressive_erns.value):
        pool.append(create_item(world, PROGRESSIVE_ERN))

    # ERN port upgrades, as FIXED counts before any weighted padding.
    #
    # Not part of filler_sequence on purpose: that draws with replacement, so it
    # could hand out nine copies of one name (five of them inert, since a fifth
    # copy does nothing) and none of another. Fixed counts guarantee every copy
    # generated is a copy that works.
    #
    # Clamped to what is actually left. At the default 4 copies this is 48
    # items, which fits comfortably, but a seed with few locations - a small
    # missions_for_finale, or heavy starter_missions - must not overflow its
    # own location count.
    unfilled = len(world.multiworld.get_unfilled_locations(world.player))
    ern_copies = world.options.ern_upgrade_copies.value
    for name in ERN_UPGRADE_ITEMS:
        for _ in range(min(ern_copies, ERN_UPGRADE_MAX_COPIES)):
            if len(pool) >= unfilled:
                break
            pool.append(create_item(world, name))

    # Energy upgrades, as FIXED counts for the same reason as the ERN block:
    # both curves are capped, so only maximum/step copies of each do anything.
    #
    # This is a large change from the old behaviour, where these two names
    # absorbed every leftover slot - about 142 of them - and the copies past the
    # useful count were inert. See the note on PAD_KIND below for what now fills
    # the gap they leave.
    storage_copies, generation_copies = energy_useful_copies(world)
    for name, count in ((ENERGY_STORAGE_ITEM, storage_copies),
                        (BASE_GENERATION_ITEM, generation_copies)):
        for _ in range(count):
            if len(pool) >= unfilled:
                break
            pool.append(create_item(world, name))

    # Pad to the number of unfilled locations, drawing filler by the player's
    # weights. Round-robin used to do this, which ignored the weights entirely
    # and made the mix depend on how many real items happened to precede it.
    remaining = max(0, unfilled - len(pool))

    # Split what is left between traps and useful upgrades.
    traps = remaining * world.options.trap_percentage.value // 100
    for name in trap_sequence(world, traps):
        pool.append(create_item(world, name))
    for name in filler_sequence(world, remaining - traps):
        pool.append(create_item(world, name))
    world.multiworld.itempool += pool


def trap_weights(world) -> dict:
    o = world.options
    return {
        "Spore Strike": o.trap_weight_spore_strike.value,
        "Spore Scatter": o.trap_weight_spore_scatter.value,
        "Creeper Surge": o.trap_weight_creeper_surge.value,
        "Energy Drain": o.trap_weight_energy_drain.value,
        # Read but unused while Emitter Overdrive is out of the pool - see
        # POOL_TRAP_ITEMS. Kept so the option keeps working if it goes back in,
        # and so an existing yaml naming it is not an error.
        "Emitter Overdrive": o.trap_weight_emitter_overdrive.value,
        "Unit Stun": o.trap_weight_unit_stun.value,
        "Ammo Drain": o.trap_weight_ammo_drain.value,
    }


def trap_sequence(world, count: int) -> list:
    """`count` trap names drawn by the configured weights.

    If a player zeroes every trap weight while still asking for traps, the slots
    become useful items rather than failing generation - an unfillable
    preference should degrade, not break the seed.
    """
    if count <= 0:
        return []
    weights = {k: v for k, v in trap_weights(world).items()
               if v > 0 and k in POOL_TRAP_ITEMS}
    if not weights:
        return filler_sequence(world, count)
    kinds = list(weights)
    return world.random.choices(kinds, weights=[weights[k] for k in kinds], k=count)


def filler_weights(world) -> dict:
    """Kept for yaml compatibility, no longer used to size anything.

    The energy upgrades have their own copy-count options now, because both
    curves are capped and the count that reaches the cap is the only count worth
    generating. An existing yaml naming these weights stays valid; it just does
    not change the pool. Same treatment build limits got.
    """
    return {
        ENERGY_STORAGE_ITEM: world.options.filler_energy_storage_weight.value,
        BASE_GENERATION_ITEM: world.options.filler_base_generation_weight.value,
        # Read but unused while build limits are out of the pool - see
        # POOL_FILLER_KINDS. Kept so the option keeps working if they go back in,
        # and so an existing yaml naming it is not an error.
        "build_limit": world.options.filler_build_limit_weight.value,
    }


# WHAT PADS THE POOL, and why this is a placeholder rather than a design.
#
# Every filler kind now has a CAP: the ERN upgrades stop at 4 copies each, and
# the two energy upgrades stop at whatever count reaches their maximum (8 each
# by default). Capping them was the point - a copy that does nothing is the
# defect that got build limits pulled - but it leaves a hole:
#
#     236 locations
#      46 real items (unlocks, units, bonus, progressive ERN)
#      48 ERN upgrades      (12 names x 4)
#      16 energy upgrades   (8 + 8 at the defaults)
#      63 traps             (50 percent of what is left)
#     ---
#      63 slots with no capped item left to put in them
#
# The boons pad them, because a one-shot effect is the only shape that can
# absorb an arbitrary count honestly - the thirtieth copy refills weapons
# exactly as well as the first.
#
# All nine share the load evenly, which is why there are nine: it cuts how
# many of any one name a player sees to about a ninth.
#
# Progressive ERN was tried first and was wrong: padding with it meant a player
# who set progressive_erns to 0 still received sixty-six of them, which
# overrides an explicit option. A padder must not be something anyone can ask
# for less of.
#
# STILL NOT A FINISHED DESIGN. About thirty copies of each is a lot, and a
# backlog arriving while ammo and energy are both full fires them all for
# nothing. The real fix is more item KINDS to spread the leftover across, which
# is the same gap the ERN upgrades were added to close.
PAD_ITEMS = BOON_ITEMS


def filler_sequence(world, count: int) -> list:
    """`count` filler item names.

    The player's filler weights still choose between the two energy upgrades
    where there is a choice, but their COUNTS are fixed now, so this is only
    reached for the leftover slots described above - and those go to PAD_ITEM.
    """
    if count <= 0:
        return []
    # Alternating rather than a weighted draw: the counts should be even and
    # deterministic, not a sample that happens to favour one name.
    return [PAD_ITEMS[i % len(PAD_ITEMS)] for i in range(count)]


def get_filler_item_name(world) -> str:
    return filler_sequence(world, 1)[0]
