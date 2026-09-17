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


# EVERY mission's unlock exists as an item name, including the ones that end up
# starting unlocked. Item ids must not depend on which missions a seed happens to
# start with - excluding starters here would shift every later id whenever a
# player changed the option.
from .span_data import SPAN_MISSIONS  # noqa: E402  (after MISSION_TITLES)

MISSION_UNLOCK_ITEMS = [f"Mission Unlock: {MISSION_TITLES[n]}" for n in range(1, 21)]

# THE SPAN EXPERIMENTS, missions 21..46.
#
# A SEPARATE LIST, APPENDED AT THE TAIL of _all_names - never folded into
# MISSION_UNLOCK_ITEMS above. That list sits FIRST in _all_names, so growing it
# in place would renumber every item id from Cannon onward and break every seed
# already in flight. Keeping them apart is what makes this change additive.
#
# The names exist unconditionally, exactly as the Farsite unlocks do even for a
# mission that starts unlocked: item ids must not depend on a yaml option, and
# span_missions is a yaml option.
SPAN_MISSION_TITLES = {n: title for n, (_guid, title) in SPAN_MISSIONS.items()}
SPAN_MISSION_UNLOCK_ITEMS = [f"Mission Unlock: {SPAN_MISSION_TITLES[n]}"
                             for n in sorted(SPAN_MISSION_TITLES)]

# Every mission, Farsite and SPAN, for naming. Location names are built from
# titles, so this is what locations.py keys on.
ALL_MISSION_TITLES = dict(MISSION_TITLES)
ALL_MISSION_TITLES.update(SPAN_MISSION_TITLES)

# A title is an ID KEY - two missions sharing one would collide their locations
# and silently merge two missions' checks. Checked here rather than trusted,
# because the SPAN titles come from the game and could change under a patch.
_dupes = sorted(set(MISSION_TITLES.values()) & set(SPAN_MISSION_TITLES.values()))
if _dupes:
    raise AssertionError(f"SPAN titles collide with Farsite titles: {_dupes}")


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

# Ten names, so no single one dominates the padding.
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
# introduced deliberately, which is the more likely route - reopening means adding
# the name to PAD_ITEMS, which is what filler_sequence actually draws from.
#
# NOT this list. POOL_FILLER_KINDS IS READ BY NO GENERATION CODE - only by two
# tests and tools/audit/audit.py, where it is the declaration of what SHOULD be
# poolable. This comment used to say "putting build_limit back in this list is the
# whole change", and docs/randomizer-design.md repeated it; following either would
# have generated nothing while turning the item-group test green, because that
# test derives its expectation from this same constant.
POOL_FILLER_KINDS = [ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM]

# Traps. Every effect is temporary and recoverable by design - a trap may sting,
# but none may make a mission unwinnable, which is why permanent terrain
# deformation was dropped during the feasibility spike. Names must match
# CW4Archipelago.Core.TrapRules exactly; a test pins that.
# Every trap that HAS an id. Emitter Overdrive is deliberately still here and
# deliberately not in POOL_TRAP_ITEMS below: dropping the name outright would
# renumber every id after it, and ids are the one thing that must not move.
TRAP_ITEMS = [
    "Spore Strike Trap",
    "Spore Scatter Trap",
    "Rift Breach Trap",
    "Energy Drain Trap",
    "Emitter Overdrive Trap",
    "Unit Stun Trap",
    "Ammo Drain Trap",
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
POOL_TRAP_ITEMS = [t for t in TRAP_ITEMS if t != "Emitter Overdrive Trap"]

# APPENDED, never inserted. Item ids are positional, and the client's
# ITEM_NAME_TO_ID has to match across every yaml - inserting a name anywhere but
# the end renumbers everything after it.
_all_names = (
    MISSION_UNLOCK_ITEMS + UNIT_ITEMS + BONUS_UNIT_ITEMS
    + [PROGRESSIVE_ERN] + FILLER_ITEMS + TRAP_ITEMS
    + ERN_UPGRADE_ITEMS + BOON_ITEMS
    # SPAN last, so ids +0..+78 are untouched.
    + SPAN_MISSION_UNLOCK_ITEMS
)
ITEM_NAME_TO_ID = {name: BASE_ID + i for i, name in enumerate(_all_names)}


class CW4Item(Item):
    game = "Creeper World 4"


def classification(name: str) -> ItemClassification:
    # Mission unlocks gate their region (see regions.py), so they are always
    # progression even though they never appear in an access RULE.
    if name in MISSION_UNLOCK_ITEMS or name in SPAN_MISSION_UNLOCK_ITEMS:
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
    # Only the missions this seed CONTAINS get an unlock in the pool. A mission
    # that is not in the roster has no region, no locations and nothing to
    # unlock, so its item would be pure dead weight.
    roster = set(world.mission_roster)
    starters = {f"Mission Unlock: {ALL_MISSION_TITLES[n]}" for n in world.starter_missions}
    in_roster = {f"Mission Unlock: {ALL_MISSION_TITLES[n]}" for n in roster}
    for name in MISSION_UNLOCK_ITEMS + SPAN_MISSION_UNLOCK_ITEMS:
        if name in starters or name not in in_roster:
            continue
        pool.append(create_item(world, name))
    for name in UNIT_ITEMS + BONUS_UNIT_ITEMS:
        if name in RETIRED_ITEMS:
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
        "Spore Strike Trap": o.trap_weight_spore_strike.value,
        "Spore Scatter Trap": o.trap_weight_spore_scatter.value,
        "Rift Breach Trap": o.trap_weight_creeper_surge.value,
        "Energy Drain Trap": o.trap_weight_energy_drain.value,
        # Read but unused while Emitter Overdrive is out of the pool - see
        # POOL_TRAP_ITEMS. Kept so the option keeps working if it goes back in,
        # and so an existing yaml naming it is not an error.
        "Emitter Overdrive Trap": o.trap_weight_emitter_overdrive.value,
        "Unit Stun Trap": o.trap_weight_unit_stun.value,
        "Ammo Drain Trap": o.trap_weight_ammo_drain.value,
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


# WHAT PADS THE POOL, and why this is a placeholder rather than a design.
#
# Every filler kind now has a CAP: the ERN upgrades stop at 4 copies each, and
# the two energy upgrades stop at whatever count reaches their maximum (20 each
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
