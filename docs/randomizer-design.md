# CW4 Archipelago Randomizer Design

Status: IMPLEMENTED and current as of 2026-08-31. This top half describes what the
randomizer actually does; the dated sections further down are the trail of how it
got here, kept because the reasoning behind several decisions is the only record
of why they are not the obvious thing.

Read this front matter as authoritative. It was rewritten on 2026-08-31 after an
audit found it still described the original 2026-08-25 draft - per-type locations,
a planet-graph region chain, SC2-style logic predicates - while later sections
corrected each of those a hundred lines below. A reader starting at the top was
reliably misled.

See also: [AP feature comparison + recommendations](design/2026-08-26-ap-feature-comparison.md)
- what other AP randomizers do and the designer's decisions on what to adopt.

## Scope

- Official Farsite Expedition campaign only: story1..story20 (story0 tutorial
  exempt and hidden). SPAN Experiments (26 missions) is a stretch goal - it
  shares the SpanNetworkPlanet map system.
- **Goal: beat story19, Founders.** Its completion is the Victory event
  (`VICTORY_EVENT` in `apworld/cw4/locations.py`, `FINAL_MISSION = 19`).
- The finale is additionally gated on a COUNT of other missions completed -
  `missions_for_finale`, default 12, maximum 19, 0 to disable. Reaching Founders
  is not enough; the mission is made genuinely unwinnable until the count is met.
- **story20, Ever After, is an ordinary mission, not the finale.** The campaign
  reaches it through a cutscene after Founders and it is not on the map at all;
  the mod places it beside Wallis so it can be played like any other.

## Items

| Category | Count | Notes |
|---|---|---|
| `Mission Unlock: <Title>` | 20 minus `starter_missions` | An item exists for ALL 20 missions; the starters are simply not put in the pool. Default 2 starters, so 18 |
| Unit unlocks | 21 | Every key in `UnitRules.ItemToUnit` except the three bonus units. **Pylon is an item** - only `riftlab` and `tower` are always available |
| Bonus units | 3 | Airship, Bertha, Sweeper. These are CMOD units: `GetDataName()` returns a GUID, never a name |
| `Progressive ERN` | `progressive_erns`, default 4 | Range 0-40. Never required to finish a mission, so this is purely pool budget |
| `Build Limit +1 (<Unit>)` | **not generated** | Every building starts at the "unlimited" sentinel of -1, so there is no limit to raise and the item does nothing. Ids kept, pool entry removed - see the build-limits note below |
| Energy storage / base generation | filler | Two items, applied to the rift lab. See "Energy items" below |
| Traps | 6 | Share of the non-progression slots set by `trap_percentage`, default 50. Seven effects exist; Emitter Overdrive is deliberately not generated - see the traps section |

There is no "ERN Spawning" item. An earlier draft proposed one; it was never
built, and the deny-sweep idea it depended on was explicitly ruled out - see
"Degradation over failure".

## Locations

**236 locations, one per INSTANCE.** Every totem, every nullifiable structure and
every info cache is its own check - not one check per objective TYPE, which is
what the first draft did and what produced only 58.

| Kind | Count | Name |
|---|---|---|
| Per-instance counted objectives | 203 | `<Title> - Cache N`, `<Title> - Totem N`, `<Title> - Nullify N` |
| Reclaim | 11 | `<Title> - Reclaim` |
| Custom | 3 | `<Title> - Custom` (missions 1, 19, 20) |
| Mission Complete | 19 | `<Title> - Mission Complete`, every mission but the finale |

Three things about this that are easy to get wrong:

- **The instance PREFIX is not the type word.** Objective slot 4 is `Collect`, but
  its locations are named `Cache N`. `MissionRules.InstanceKind` owns that
  mapping, and building the type-shaped name instead matched nothing - which
  silently disabled the map's glyph colouring once already.
- **Optional objectives count.** A mission's nullify targets are locations whether
  or not the mission requires nullifying them.
- **Instances are numbered by ACTIVATION ORDER.** The game cannot tell one totem
  from another, so the Nth activation sends the Nth check.

The per-mission counts live in `INSTANCE_COUNTS` (`apworld/cw4/locations.py`), not
in this document, so they cannot drift. `REQUIRED_OBJECTIVES` no longer drives the
location table - it only feeds mission-completion requirements.

Client/apworld contract (slot_data requirement groups, persistence, tracker
colors, save archiving): see design/2026-08-25-mod-wiring-design.md.

## Regions and access

**Missions are OPEN.** Every mission region connects directly from Menu and is
gated on one thing: its `Mission Unlock` item. There are no planet-graph edges -
the campaign's linear chain is display-only.

Unit logic is NOT on the region edge. It sits on the LOCATIONS inside each
mission, which is what lets a player enter a mission they cannot yet finish and
still collect the checks they can reach. That distinction is load-bearing for the
finale: Founders holds 24 checks, and gating entry would put all of them behind
the mission count.

Spheres are emergent: Archipelago computes them from these rules during fill.

## Unit logic

The rules are explicit per-mission tables in `apworld/cw4/rules.py`, NOT semantic
predicates. An earlier draft used SC2-style `has_offense` / `has_economy` /
`has_antiair` categories derived from the vanilla unlock schedule; that was
abandoned because it disagreed with how the missions actually play. The tables are
`OFFENSE`, `GREENAR_CHAIN`, `PREREQUISITES`, `MISSION_EXTRA`, `OBJECTIVE_OWN`,
`WAIVES_MISSION_REQUIREMENTS` and `DEFENSIVE`, and the sections below describe
each.

Four points the abandoned predicates got wrong, spelled out because they are the
mistakes most likely to be re-introduced:

- **Offense is Cannon OR Mortar. Sprayer is NOT offense** - it burns bluite, and
  every bluite map was judged short of the resource for a sprayer-only run.
- **Economy is not in logic at all.** No Miner, no ERN, no Factory-for-storage
  general rule. Tower energy carries every map, tested on the one mission that
  looked like an exception - see "Cross-mission questions, answered".
- **Anti-air is not required in the standard tier.** Sniper and Missile Launcher
  gate nothing except The Compound. Under `logic_difficulty: casual` they become
  a required pair from mission 6 onward.
- **Logic is NOT bounded by the vanilla unlock schedule.** The draft claimed logic
  "never requires MORE than vanilla, only equivalents". That is false, and
  deliberately so: mission 11 needs Porter or Platform (vanilla gives them at 12
  and 18), mission 12 needs Shield (vanilla: 13), mission 15 needs Chronat
  (vanilla: 16). What a mission REQUIRES is a fact about the mission, not about
  the order the campaign hands out units.

## Client behavior (proven by probe)

- Locks/unlocks: per-frame whitelist enforcement + LeftPane refresh recipe.
  Unit gating is genuinely per-frame; the mission MAP is not - it is driven by
  `Span.Start` and `SpanNetworkPlanet.Refresh`, see docs/developing.md.
- Live delivery: works mid-mission (SetEnabledButtons path).
- Location sends: per-instance, from patches on `Totem.totemComplete` and
  `InfoCache.DestroyUnit` plus a once-a-second safety poll. Nullification has no
  hook and relies on the poll.
- Mission gating: OnLaunch + OnLoad prefixes; map shows native "?" for locked
  missions; LockedPlanet visual + marker recoloring for tracker states.
  **mcs.dat is NOT rewritten** - an early design did that and Steam Cloud
  restores it. The mod owns the display from AP state and leaves the file alone.
  Save isolation moves `saves/farsite` per slot instead.
- Palette: FOUR colours - red not accessible, yellow reachable but not in logic,
  green reachable and in logic, grey finished. There is no orange: `Partial`
  exists as a TrackerStatus but maps to green, so do not document an orange.
- Menu: chronom/markV/colonies/editor hidden; AP login panel functional.

## Survey data (probe v0.54, automated, 2026-08-25)

### Mission graph

Linear chain: story0 (09 Leo, 266 - tutorial) -> story1 -> ... -> story20.
Each planet's connectedPlanetGUIDS points to exactly the next mission;
story20 terminates. Titles:
Farsite, Home, Not My Mars, Ruins Repurposed, We Know Nothing, We Were
Never Alone, Hints, Serious, More and More, War and Peace, Shattered,
Archon, The Experiment, Somewhere in Spacetime, Tower of Darkness, The
Compound, Sequence, Wallis, Founders, Ever After.

### Vanilla unlock schedule + enemies (NEW units per mission, cumulative)

| # | Title | NEW units | Req obj | Enemies at start |
|---|---|---|---|---|
| 1 | Farsite | riftlab, tower | 1/6 | - |
| 2 | Home | cannon | 3/6 | Emitter x1 |
| 3 | Not My Mars | mortar, nullifier | 2/6 | Emitter x2 |
| 4 | Ruins Repurposed | miner, pylon | 2/6 | Emitter x4 |
| 5 | We Know Nothing | factory, greenarrefinery | 2/6 | - |
| 6 | We Were Never Alone | missilelauncher | 1/6 | Emitter x3, Spore x6 |
| 7 | Hints | sprayer | 2/6 | Emitter x2, Spore x2 |
| 8 | Serious | terp | 1/6 | Spore x1 |
| 9 | More and More | - | 2/6 | BlobNest x3, Emitter, Spore x2 |
| 10 | War and Peace | ernportal | 2/6 | BlobNest, Emitter, Spore x3 |
| 11 | Shattered | sniper | 2/6 | BlobNest, Emitter, Spore x2 |
| 12 | Archon | porter | 2/6 | Spore x2 |
| 13 | The Experiment | bomberpad, runway, shield | 2/6 | BlobNest, Emitter x5, Spore x2 |
| 14 | Somewhere in Spacetime | - | 2/6 | +SkimmerFactory x3 |
| 15 | Tower of Darkness | - | 3/6 | BlobNest x4, Skimmer x2, Spore x2 |
| 16 | The Compound | acbomberpad, chronat, microrift | 2/6 | BlobNest x2, Emitter x5, Skimmer x2, Spore |
| 17 | Sequence | - | 1/6 | BlobNest x2, Emitter x7, Skimmer x2, Spore x2 |
| 18 | Wallis | platform | 1/6 | AirSacBubble x22 + full mix |
| 19 | Founders | - | 2/6 | AirSacCauldron x2 + full mix |
| 20 | Ever After | rocketpad | 4/6 | BlobNest, Spore |

Notes:
- airship, bertha, sweeper NEVER unlock in the campaign (SPAN/bonus units) -
  free extra AP items, already proven grantable.
- Anti-air need begins exactly at story6 (first spores) = vanilla grants
  missilelauncher there. Skimmers from 14, AirSacs from 18.
- nullifier arrives at story3 - the nullify-objective win condition unit.
- Objective slots are always 6; required counts 1-4 (story20 requires 4).

### Derived logic categories - SUPERSEDED, kept as the discarded approach

These were the first attempt: semantic categories inferred from the vanilla unlock
schedule. **None of it is in the code.** It is kept because knowing what was tried
and why it failed is worth more than a clean page - the failure being that a
category derived from unlock ORDER says nothing about what a mission needs, and
the designer's playthrough disagreed with it mission after mission.

    has_offense = cannon OR mortar OR sprayer   (sprayer is NOT offense)
    has_nullify = nullifier                      (now type-wide, all 20)
    has_antiair = missilelauncher OR sniper      (casual tier only)
    has_economy = miner                          (not in logic at all)
    has_terraform = terp                         (per-objective, not per-mission)
    Baseline: never stricter than vanilla        (false - see Unit logic above)

Every line above is wrong in the way noted beside it. The real rules are the
explicit tables in `apworld/cw4/rules.py`.


## Logic corrections from the designer (user, 2026-08-25) - AUTHORITATIVE

Mission order: **open missions** - any mission whose Unlock item is held is
playable (unit logic permitting). The linear chain affects map display only.

Real unit requirements (looser than vanilla schedule):
- **Cannons can do most anything.** Mortars: same but a little harder.
  Sprayers additionally require blueite (resource availability per mission).
  -> has_offense = cannon OR mortar (sprayer only counts where blueite
  is obtainable).
- **Snipers / missile launchers are nice-to-have**, defensive/survival aids,
  NOT win requirements. May soften difficulty on spore-heavy missions but
  logic must not require them... except possibly survival-objective cases.
- **Nullifier is ONLY needed for "nullify enemies" objectives** - not for
  general mission completion. Nullify-objective locations require it;
  missions themselves usually don't.
- **Air units** (runway/bomber/acbomber) need enough energy + space;
  **rockets are their own thing** (rocketpad).
- **Refinery (greenarrefinery) is required for: rocket, platform, and
  beacon (chronat)** - prerequisite chain in item logic.
- **Factory may be needed to hold greenar/blueite/redon** before use
  (verify in-game which resources need factory storage).
- **Miners can mine blueite only on missions that allow it / have ground
  for it** - per-mission resource table needed (future survey pass:
  resource deposits per map).
- **Some missions might need porter to complete** - identify which
  (per-mission review needed).

Revised baseline rule sketch per mission:
  in_logic(storyN) = MissionUnlock(N)
                     AND has_offense (cannon|mortar) [story2+]
                     AND has_economy(miner) [storyN where energy demands]
                     AND porter [only the porter-required missions]
  objective-level rules:
    nullify objective -> nullifier
    collect/custom objectives -> per-mission review
  difficulty options can add sniper/missile "recommended" tiers (like SC2
  logic levels: casual requires defenses, hard does not).

Addendum (user, 2026-08-26) - objective requirements:
- **Totems objective requires greenar**: totems are activated by feeding them
  greenar, so the greenar chain (Greenar Refinery) is required wherever a
  Totems objective is a location. Encoded in rules.objective_requirements.
  CORRECTED: **11** missions carry the chain - 5, 7, 8, 9, 10, 12, 13, 14, 15,
  16, 20. Missions 2, 3 and 4 require NOTHING for their totems: they run off
  loose liftic caches, so there is deliberately no type-wide Totems rule.
- **Nullify objective requires the Nullifier.** CORRECTED: this is now
  **type-wide on all 20 missions**, not four. Encoding it only on the missions
  that require nullifying left every OPTIONAL nullify target reachable
  bare-handed once those targets became checks - roughly 120 locations. The
  missions themselves still never require it, only their nullify locations do.
Caveat: Totem.ammoWares is authored PER MAP (ware type -> amount), so a map
could in principle demand a different ware. GetAmmoWareWanted reads 0 for all
wares at mission start, so the live requirement was not readable - the blanket
greenar rule is the designer's, not measured.

## Per-mission logic, derived from the playthrough (2026-08-30) - IMPLEMENTED

The worksheet (design/mission-requirements-worksheet.md) is filled in, and
`apworld/cw4/rules.py` is built from it, mission by mission.

**Written as an explicit per-mission table, not derived from objective type.**
The type is not what decides: missions 2, 3 and 4 power their totems from liftic
lying on the ground while mission 8 needs the whole greenar chain. A first
version derived rules from the type and was wrong in both directions - it also
missed that most missions let the player take their cache with nothing but a rift
lab and one tower.

Reading rules: only what is genuinely REQUIRED becomes logic ("helped but not
needed" is difficulty-tier material, so snipers and missiles are out, with one
exception); and where the worksheet hedges, require rather than not, since
too-tight logic only makes seeds linear while too-loose can make them unwinnable.
Hedged calls are marked HEDGED in the code with the note they came from.

### Completing a mission

Offense (Cannon OR Mortar) on all 20. Sprayers are excluded: they burn bluite and
every bluite map was judged short of the resource for a sprayer-only run. Two
missions add to it:

| Mission | Extra | Source |
|---|---|---|
| 12 Archon | Nullifier | HEDGED: an enemy shuts off energy production, "super hard to do anything without a nullifier" |
| 12 Archon | Shield | HEDGED: the map rains creeper, "doable/not super hard mode if you get SHIELDS as it creates a safe space" |
| 16 The Compound | Sniper | saw blades die only to snipers, "no way to do any objectives without" |

The Compound is the only mission where a sniper is in logic **in the standard
tier**. Under `logic_difficulty: casual`, a Sniper OR Missile Launcher is
required from mission 6 onward.

### Caches that need no weapon

Ten Collect checks need NOTHING - not a weapon, not the mission's extras. Nine are
the cache a player takes with the rift lab and a single tower before the creeper
arrives, stated mission by mission in the worksheet ("You can get the item
immediately with rift lab and single tower"): missions 2, 3, 4, 5, 7, 10, 11, 13,
14. Archon is the tenth for a different reason - its caches are buried, but "If
you have a pylon and a terp you can get the 2nd item (no weapons needed)", so a
terp and a pylon stand in for the weapon.

This is what makes a starting weapon unnecessary, and it is why cannon, mortar and
sprayer can all stay real checks rather than one being handed over up front.

Not waived, because the worksheet says the opposite: More and More ("No easy way
to get the item at the start ... it starts under creep"), Tower of Darkness ("No
easy way to get item. need to fight back creep"), The Compound, Sequence, Wallis,
Founders, Ever After.

### Per-objective requirements

| Objective | Requirement |
|---|---|
| Nullify on 2, 20 | Nullifier |
| Nullify on 11 Shattered | Nullifier + (Porter OR Platform) to cross space |
| Nullify on 15 Tower of Darkness | Nullifier + Chronat (beacon, to reach the centre) |
| Totems on 2, 3, 4 | nothing - powered from loose liftic caches |
| Totems on 5, 7, 8, 9, 10, 12, 13, 14, 15, 16, 20 | Greenar Refinery + Factory |
| Reclaim on 6 | HEDGED: Nullifier ("probably nessesary") |
| Collect on 12 Archon | Terp + Pylon (buried; no weapon needed) |
| Collect on 16, 17, 18 | Terp (buried) |
| Collect on 19 Founders | Terp + Chronat + Platform |
| Custom on 19 Founders | Nullifier + Platform (the obelisk reactors, then the neutron reactor) |

### Structural rules

**Mission Complete inherits its objectives.** A mission cannot end until its
required objectives are done, so its completion check carries the union of their
requirements plus the mission's own. A waived cache waives the weapon for ITS
check only.

**Prerequisite expansion.** Platform, Chronat and Rocket Pad need the greenar
chain to build, so any rule requiring one also requires Greenar Refinery and
Factory. Done in one place (`rules._expand`) so it cannot be forgotten, and
deliberately NOT applied to an any-of group like "Porter or Platform", where only
one branch needs it.

**slot_data contract.** Each `location_requirements` entry is COMPLETE. A
consumer must not AND it with the mission's entry - a mission's cache is often
collectable long before the mission is winnable, and combining them would hide
that.

### Starter missions are RANDOM, and there is no starting weapon

The only real constraint on the opening is that something must be reachable with
an empty inventory, or the generator has nowhere to place a first item. That
means a mission whose cache can be taken with the rift lab and a single tower.
Nothing requires the campaign to start at its beginning - missions are open - so
the starters are drawn at random from the nine that qualify (2, 3, 4, 5, 7, 10,
11, 13, 14) and every seed opens somewhere different. `starter_missions` sets how
many, default 2.

Farsite is NOT eligible despite being mission 1: its Custom objective and rift
jump both need a weapon, and although its first cache is free, its second is not
- instances of an objective share a rule, so that pair cannot be split. Archon is
excluded for a different reason: it waives the weapon but its caches are buried
behind a Terp and a Pylon.

`items.force_early_mission` additionally forces one more free-cache unlock that
is not already a starter, so the opening always widens. Without something like
it, generation failed on 1 of the first 20 seeds tested.

**Every mission's unlock has an item id, including ones that start unlocked.**
Ids must not shift with the starter set - building them from the non-starters
was harmless while the set was a constant and would have silently broken the
client the moment it became an option. Starters simply are not added to the pool.

### Founders is the finale, Ever After is a side branch

From the worksheet's mission 20 notes: "It's a hard map, but not good for a
finale. I would say currently lock Founders (19) as the finale, and have this as
an additional level ... have it connected to level 18 as a new branch and it can
be to the right of Wallis."

Implemented: `FINAL_MISSION = 19` in the apworld and `MissionRules.FinalMission`
in the mod, so finishing Founders sends the goal and Ever After became an
ordinary mission with its own Mission Complete check. The location count is
unchanged by that decision - though the per-instance rework has since taken it
from 58 to 236. The mod's off-map placement hangs Ever After off Wallis and
to its right.

### Cross-mission questions, answered

- **Greenar for totems**: missions 2, 3, 4 run off loose liftic, no refinery.
- **Porter**: never the only way; one appearance, as an alternative to Platform.
- **Terp**: buried caches on 12, 16, 17, 18, 19.
- **Sprayer / bluite**: never sufficient alone.
- **Air units**: never the only way to reach anything.
- **Miner / economy**: ANSWERED 2026-08-31, and it stays out of logic. Tower of
  Darkness was the one mission where mining "might" be needed, so it was played
  with exactly its logic requirements granted and no Miner, no Pylon, no Platform,
  no Terp and no energy items - the worst case a seed can hand a player.
  Designer's verdict: *"Yes very doable with no miners on Tower of Darkness. not a
  requirement. the snipers are a little more important."* Followed by the
  clarification that settles it: *"snipers are not needed, but nice to haves. you
  can beat the level without them."* That is difficulty-tier material and the
  casual tier already covers it from mission 6 onward. It is explicitly NOT a
  hedge - unlike Archon's two entries, which were hedges and were promoted on
  purpose - so it must not be promoted into `MISSION_EXTRA` later.
  `test_miner_gates_nothing` and
  `test_sniper_on_tower_of_darkness_is_casual_only` pin both halves.

### Still not in logic, deliberately

Factory-for-storage as a general rule, Miner, and anything about ERNs. The
worksheet does not establish them as required, and guessing would risk
unwinnable seeds in the one direction that matters.

Miner is no longer a guess: it was the one item this could have got wrong, and the
mission it could have got wrong is now played and reported above.

## Traps, energy items and the option set (2026-08-30) - IMPLEMENTED

### Traps

The seven effects from the feasibility spike are now items:
`Spore Strike`, `Spore Scatter`, `Creeper Surge`, `Energy Drain`,
`Emitter Overdrive`, `Unit Stun`, `Ammo Drain`. Each has a weight option, and
`trap_percentage` (default 50) sets what share of the non-progression slots they
take. Every effect is temporary and recoverable - permanent terrain deformation
was dropped during the spike precisely because it could strand a mission.

**Traps must fire exactly once, and only in a mission.** Two hazards, both
handled in `Appliers/TrapApplier.cs`:

- Reconnecting re-delivers the WHOLE received-items list. Firing on the receive
  event would replay every trap the player had ever been sent. Progress is a
  persisted high-water mark (`SlotState.TrapsApplied`) over that list instead, so
  it survives a reconnect and a restart.
- A trap received at the menu has nothing to act on. Those queue and fire on the
  next mission, one per tick, so a backlog stings repeatedly rather than landing
  as a single unsurvivable wall.

Trap names are pinned by tests on both sides. The mod dispatches on the exact
strings, so a rename would otherwise stop traps firing silently rather than fail.

### Energy items

`Progressive Energy Storage` and `Progressive Base Generation`, both applied to the rift
lab - the only real levers CW4 exposes (see research-findings.md, "Energy: the
store is the rift lab's ammo"). Storage has diminishing returns, generation
ramps, per the designer:

| Option | Default | Effect |
|---|---|---|
| `energy_storage_step` | 50 | first copy's capacity bonus |
| `energy_storage_decay` | 80 | percent of the previous copy, so 50, 40, 32, 25 |
| `base_generation_start` | 5 | tenths per second, so +0.5/sec |
| `base_generation_ramp` | 2 | tenths more per later copy, so 0.5, 0.7, 0.9 |

**Item names carry no amounts.** Ids must be identical across every yaml, so
`Energy Storage +50` would break the client whenever a player retuned an option.
The amounts travel in slot_data.

### The narrow opening, and the bootstrap that fixes it (2026-09-01)

Every starter-eligible mission has exactly ONE cache collectable with no items -
Farsite has two caches but only the first is free - so `starter_missions: 1` gives
a one-location opening. That shape is intended: the single free cache hands you an
unlock, that mission's free cache hands you the next thing, and the seed chains
until a weapon turns up.

It also used to fail to generate about **12 percent** of the time, and now fails
**0 times in 300 seeds**. Two separate causes, worth keeping apart.

**Cause one: two early items, one slot.** Archipelago's "early" means a location
reachable holding nothing, and there was exactly one. The world asked for both a
mission unlock and the `early_weapon`, so Archipelago dropped one arbitrarily and
logged that it had. Measured at 60 seeds per setting:

| early items requested | failed to generate |
|---|---|
| mission unlock only | 0 of 60 |
| nothing | 2 of 60 |
| both, as shipped | 7 of 60 |
| weapon only | 13 of 60 |

**Cause two: the fill spending a scarce slot on an item that opens nothing.** With
cause one fixed the rate was still 1.3 percent (4 in 300), all of them the funnel
collapsing at the start. Seed 20100:

    We Know Nothing - Cache 1         -> Mission Unlock: Somewhere in Spacetime
    Somewhere in Spacetime - Cache 1  -> Cannon
    Somewhere in Spacetime - Reclaim  -> Factory      <- opens nothing

Totems want Greenar Refinery AND Factory, so a lone Factory is half a pair;
nullify targets want a Nullifier. Twenty-nine items were left with nowhere to go.
Archipelago's fill places one item at a time without looking ahead and cannot know
the two are a pair - which is fine when there is slack, and fatal when there is
none.

**It only matters when this world is the only home for its own progression.**
Put another game in the multiworld and the funnel stops being a funnel: the fill
can park Creeper World 4's unlocks in that game's world and fill Creeper World 4's
opening with that game's items. Measured over 40 seeds of CW4 at one starter plus
ChecksFinder, with the bootstrap disabled: **zero** generation failures, 7 opening
checks holding a foreign item, 4 CW4 progression items per seed living abroad.

So the bootstrap stands down whenever another game is present, and that is not
just tidiness - running it there costs the thing a multiworld is for. The same 40
seeds WITH it forced on had **0** foreign items in the opening: it takes the
cross-game placements out of the only checks a narrow opening has. Gated, the
mixed case measures 0 failures in 60 seeds and 8 foreign items back in the
opening.

It DOES run for a Creeper World 4 only multiworld, because every player there has
the same narrow opening and there is no roomier world to lean on: two CW4 players
at one starter each, 40 seeds, 0 failures, the bootstrap running for all 80
player-worlds at an average of 2.6 placements.

KNOWN EDGE: a player who forces their own items local (`local_items`) inside a
mixed multiworld recreates the funnel and is not covered. Rare, and visible in the
yaml, so it is a re-roll rather than a heuristic guessing at intent.

**The fix is `bootstrap_opening`, in `pre_fill`.** While the opening is narrower
than `SAFE_OPENING`, the world places items itself, drawn at RANDOM from those
that actually open something, into a location drawn at random from those reachable
and empty. It stops as soon as there is slack, and the general fill takes over.
Only `starter_missions: 1` reaches it; at 2 or more it is a no-op.

It is not a script. The item and the location are both random draws - the only
thing forbidden is picking something that opens nothing while a dud would be
fatal. Bootstrap length across 40 seeds was 1 to 6 placements, most often 2 or 3,
and the first item varied across eight different items.

**`early_weapon` still works at this width**, which the previous attempt gave up
on. The requested weapon gets first refusal whenever it is one of the productive
choices, and it went first in **31 of 40** seeds. It is skipped only where a
weapon genuinely opens nothing - measured per starter, a weapon alone opens 5
checks on Not My Mars and Ruins Repurposed, 3 on Farsite and Shattered, 2 on Home,
1 on Hints, War and Peace and Somewhere in Spacetime, and **0 on We Know Nothing
and The Experiment**. Those last two are exactly the 2-in-10 that made
"weapon only" fail 13 times in 60.

### Which weapon opens the seed

`OFFENSE = ["Cannon", "Mortar"]` is one OR group, so logic needs either and never
both. Whichever lands early IS the opening weapon and the other is redundant for
the rest of that seed.

Measured over 20 default seeds: the opener is a genuine coin flip, **Cannon 10,
Mortar 10**. An earlier 13-7 sample looked like a bias and was not - reversing the
pair to `["Mortar", "Cannon"]` and regenerating the same seeds produced identical
results, seed for seed, so list order does not choose the winner.

`early_weapon` lets a player choose instead. It is placement, not logic: a rule
saying a mission needs a mortar specifically would be false wherever a cannon also
works, so the option uses Archipelago's `early_items`, the same mechanism that
already forces a second mission unlock early.

**What it costs, measured by ROLE.** The first version of this section compared
Cannon against Cannon and reported a large regression. That was an artefact:
under `unforced` Cannon opens half the seeds, so its median is dragged early,
while under `early_weapon: mortar` it never opens one. Comparing opening weapon
against opening weapon, and second against second, over 20 seeds each:

| | opening weapon | second weapon |
|---|---|---|
| no forcing (before the option) | median 2, range 1 to 4 | median 9, 67 percent in, final sphere 0 of 20 |
| `random` | median 1, always | median 10, 75 percent in, final sphere 3 of 20 |
| `mortar` | median 1, always | median 8, 67 percent in, final sphere 2 of 20 |

The second weapon arrives about two thirds of the way in regardless. That is a
property of an OR pair - the loser is redundant for the whole seed and nothing
pulls a redundant item forward - and it was true before this option existed. The
option does not cause it and cannot fix it.

What forcing buys is an opening weapon in the first sphere rather than somewhere
in the first four. The only real cost is the extreme tail: the second weapon
landing in the FINAL sphere goes from 0 seeds in 20 to 2 or 3.

The first row is history rather than a setting. An `unforced` value existed
briefly and was removed: it reproduced the old distribution exactly, but once the
by-role numbers showed the difference was a weapon in sphere 1 versus sphere 1 to
4 and nothing else, a fourth value earning its place in every yaml could not be
justified. Three values that a player can read at a glance beat four that need
this table to tell apart.

`random` is not defined by the world. Archipelago accepts it for any Choice and
resolves it per seed while parsing the yaml, and defining it would actually raise:
Options.py asserts "Choice option 'random' cannot be manually assigned". That is
why the default is the string `"random"` rather than a value - the same pattern
cv64, ffmq and alttp use.

There is no Archipelago primitive for "place this by sphere N". A world can force
an item into sphere 1 (`early_items`) or place it at a chosen location
(`place_locked_item`); nothing in between. Bounding the second weapon to mid-game
would therefore mean pinning it to a specific location, which takes it out of the
shuffle entirely and stops it ever travelling to another player in a multiworld.
That trade has not been made.

Sprayer is a third case and the latest-arriving of the three: it appears in NO
rule (sprayers burn bluite, and every bluite map was judged short of the resource
for a sprayer-only run), so it is never progression and never pulled forward.
Measured median sphere 9 of 13.6, in the last 40 percent of the seed in 14 of 20
seeds. Making it "early in logic" is not available without asserting something the
worksheet says is false; nudging it early as a non-logic item would be.

### Degradation over failure

An unfillable preference must not fail generation: zeroing every filler weight
falls back to energy storage, and zeroing every trap weight turns those slots into
useful items. Both are tested.

The filler fallback used to be build limits, which stopped being a safe default
the moment build limits left the pool - the degradation would have degraded to an
item that does nothing. Worth noting as a shape: a fallback names a specific item,
so removing any item means checking whether something falls back to it.

### Build limits are not generated (2026-09-01)

`SetBuildCountLimit` works, and the cap it writes is enforced - that was verified
in real play, where `limit:tower:3` produced a badge and the game refused a fourth
tower. What does not work is the item, because of the gap between setting a limit
and raising one.

Every building starts at -1, the game's "unlimited" sentinel. `UnitGate.ApplyLimits`
deliberately skips those, and has to: writing base+1 over an unlimited unit would
CAP a unit that had no cap, so a bonus item would arrive as a penalty. With every
base unlimited, every increment is skipped, and a `Build Limit +1` item does
nothing on any unit on any mission.

At the default weights that was 24 items in a 256-item seed - about one check in
ten paying out nothing, with no in-game signal that anything was missing. Same
rule that removed Emitter Overdrive, applied to a worse case: that trap is dead on
a third of the campaign, this item was dead on all of it.

Held loosely. The ids, the `UnitRules.ItemToUnit` mapping, `UnitGate`'s base
capture and increment, the `limit:` debug command and the yaml weight all still
work, so re-adding the name to `POOL_FILLER_KINDS` is the whole change. The likely
route back is limits being introduced deliberately rather than a mission being
found that ships one.

### Verified

132 world tests and 104 C# tests as of 2026-09-01. The 80-generated-seed sweep
below predates the per-instance rework and is NOT recorded in the repo, so treat
it as history rather than a current guarantee; it covered
defaults, ERNs at 0 and 40, generation-only filler, traps at 0 and 100 percent, a
single starter mission, and 4-player multiworlds.

### Open design options for more locations/items (user, 2026-08-27)

**Two of the three shipped.** Options 1 and 2 (caches and nullify targets as
individual locations) are the per-instance model now in use - that is where the
236 locations come from. Only option 3, the Rift Lab as an item, is still open.

Raised while sizing the pool (59 locations vs 47 real items = 12 filler slots):

1. **Caches as individual locations.** The "blocks you connect the network to"
   are InfoCaches, and they ARE the Collect objective - `GameSpace.mustCollect`
   equals the InfoCache count on every mission measured. Today all of a
   mission's caches collapse into ONE "Collect" check; each could be its own
   location instead. **SHIPPED** - see the Locations section at the top. The
   per-mission counts live in `INSTANCE_COUNTS` (`apworld/cw4/locations.py`);
   there is no table in this document (an earlier draft promised one).
2. **First-nullify as a location.** `GameSpace.nullifiableUnits` is the set the
   Nullify objective completes on, so per-structure or first-per-mission
   nullify checks are capturable.
3. **Rift Lab as an unlockable item.** A few missions ship with the rift lab
   already placed; most make the player place it. If the Rift Lab becomes an
   item, only the pre-placed missions are playable without it - a natural
   starter set and a strong early gate. Logic would read:
       playable(N) = MissionUnlock(N) AND (riftlab_held OR base_preplaced(N))
   Requires care: with no Rift Lab item and no pre-placed base, a mission is
   unstartable, which is the intended gate but must never be the ONLY reachable
   state (at least one pre-placed mission must be in the starter set).
   `riftLabPreplaced` is measured from `GameSpace.commandBase` by
   `counts:dump`. There is no table for it in this document; run the command
   existing at mission load.

CAVEAT on all counts below: they are measured at MISSION START. Enemies, totems
and nullifiable structures can appear during play - story2 has both a Nullify
and a Totems objective yet reports 0 nullifiable and 0 totems at load. Treat
these as a floor, not a total.

**CORRECTION 2026-08-31, and this one matters:** for the COUNTED objectives the
counts are now exact, because they define the location set. The survey confirmed
every required counting objective has a non-zero target at load, so nothing is
hidden behind mid-mission spawning, and story2 reports 2 totems and 1 nullifiable
rather than the zeros this caveat cites. Believing "a floor, not a total" now
would suggest the location set is incomplete when it is not.

### Per-mission resources (survey, 2026-08-26)

Map deposits at mission start. Feeds the sprayer/bluite and miner questions.

| Resource | Missions |
|---|---|
| bluite deposits | 5 (1), 14 (2), 16 (1), 18 (3), 19 (4) |
| redon | 6 (4), 12 (6), 13 (2), 15 (3), 17 (3), 18 (7), 19 (4), 20 (3), and 1 each on 7,8,9,10,11,16 |
| greenar (GreenarMother) | 1 each on 5,7,8,9,10,11,12,13,14,16,18,20; 2 on 15; 3 on 17,19 |
| none at all | 1, 2, 3, 4 |

KNOWN GAPS in this survey, do not treat as complete:
- **greenar is undercounted**: only GreenarMother units are counted, but the
  game also has greenar CRYSTALS (`greenarCrystal`, `greenarLocations`,
  `CreateGreenar`). Missions 2,3,4 have Totems objectives with zero counted
  greenar, which is most likely crystals rather than a contradiction.
- ~~**powerZoneCells read 0 on all 20 missions** and is UNVERIFIED~~ -
  **CLOSED 2026-08-31, the zeros are real.** See "Power zones: verified absent"
  in [research-findings.md](research-findings.md). Three checks: a second
  independent reader (the raw `World.powerZone` int array) agrees with
  `GetPowerZone`, `rawLen == width*height` on every map tried, and a positive
  control - writing three cells with `SetPowerZone` makes both readers report
  three, then restores. So the campaign genuinely has no power zones, and the
  "second bluite source" concern does not apply to it. `resources:zonetest`
  re-runs the whole check.

### Unit names: build-pane keys are NOT unit names (2026-08-28)

`UnitRules.ItemToUnit` holds build-pane keys, not the game's unit names: the
registry has no `pylon`, `miner`, `porter` or `riftlab`. Comparing those keys
against `GetDataName()` silently skipped exactly those buildings, which is why
trap stun, weapon drain and spore targeting passed over pylons and miners.

    riftlab -> CommandBase      pylon      -> TowerBridge
    miner   -> Collector        ernportal  -> ERNInterface
    porter  -> DeliveryPad (+ DeliveryDrone)

A FOURTH name space explains why porter took so long: build-BUTTON object names
match neither the key nor the unit. PYLON's button is `SuperTowerButton`, MINER's
is `ReactorButton`, PORTER's is `DeliveryPadButton`.

Full write-up - the three name spaces, the mapping table, the two player/enemy
discriminators that do NOT work, and how to re-derive it all with CW4DevTools -
is the canonical reference in
[research-findings.md](research-findings.md) under "Unit naming". Kept in one
place on purpose so the two copies cannot drift.

### Buildings the mod does not model (2026-08-26) - RESOLVED, it models all three

**This section's premise was a naming mistake and is kept only to explain it.**
`ReactorButton`, `SuperTowerButton` and `DeliveryPadButton` are not three
unmodelled buildings - they are the BUTTON OBJECT NAMES of the miner, the pylon
and the porter, all three of which the mod has always driven through
`minerAvailable`, `pylonAvailable` and `porterAvailable`. See "A fourth name
space" in [research-findings.md](research-findings.md).

The original text follows.


The build panes contain `ReactorButton`, `SuperTowerButton` and
`DeliveryPadButton`. None of the three is among the 26 BuildUnitManager
`xxxAvailable` flags UnitGate drives, and none is in UnitRules or the item
pool. They ARE gated by the game (with only Tower granted, all three read
`=off` while TowerButton reads `=ON`), so this is not an open hole in the
whitelist - but it is unresolved whether they ever become available in an AP
run, since per-slot save archiving resets campaign progression.

Addendum (user, 2026-08-25): add an **ERN Spawning** unlock - whether ERNs
can be spawned at all (for the ERN portal / supercharging units). Late-game
or filler; never required by logic.

PROVEN (probe v0.55, erntest 7/8 pass on story10, 2026-08-25). User
simplified the requirement to "just be able to spawn in an ERN". Verified
mechanics, all working even while the game is paused:
- ERN is a UnitManager subclass; campaign ERNs are map data (story10 has
  7: 6 buried, 1 free). ERNInterface (upgrade building) is separate.
- SPAWN: UnitManager.CreateUnitAtPosition("ern", pos) creates a live ERN;
  UnitManager.GetAvailableERNCount() rises immediately. This is the item
  delivery mechanism: a received ERN item = spawn an ERN near the rift lab.
- GRANT QUEUE (v0.56, user-verified live on story2): ern:grant:N queues
  ERNs even before the mission loads; they wait for the rift lab
  (GameSpace.commandBase is null until placed) and then spawn beside it,
  one per 30 frames. User docked them and supercharged units - fully
  functional. Docked ERNs leave the available pool as expected.
- Probe commands: ern:status / ern:make / ern:grant:N / ern:deny / ern:allow.
- Not needed: the ERN portal production path (portal prefab name unknown -
  "ernportal" etc. all null in CreateUnitAtPosition; irrelevant now).
FINAL DESIGN (user-approved 2026-08-25): Progressive ERN items are purely
additive - spawn N beside the rift lab at mission start for N items held,
plus live spawn on receipt, waiting for the rift lab if needed. Map-native
ERNs stay untouched; the deny sweep exists in the probe but is NOT part of
the randomizer (user: "i don't think i care about ern deny").

## Playthrough findings (user, 2026-09-03) - AUTHORITATIVE

Found by playing seeds with the map colours working, which is the first time
"logic says this is reachable" could be compared against a real attempt. Three
of these were discovered by being SOFT-LOCKED, so they are corrections to logic
that was too loose, not tuning.

### Implemented

- **Totems need the greenar chain everywhere except three missions.** Shattered,
  Wallis and Founders were added.
  - Founders was found by soft-lock: "the totems on founders are the only green
    icon on my map, but i can't get them as they need greenar
    (refinery/factory) to get, there's no liftic cache on that mission."
  - Shattered and Wallis were then checked by playing them with rift lab, tower
    and Mortar only: "No both Shattered and Wallis need refinery/factory to
    collect greenar and convert to liftic."
  - So Home, Not My Mars and Ruins Repurposed are the ONLY free-totem missions.
    `test_totems_need_greenar` names that exception list, so a new totem mission
    fails the suite until someone classifies it.

- **Reclaim needs a Nullifier on every mission.** It is "clear the map", which
  cannot finish while anything still produces creeper: "that's typically the
  last thing you do in a level as you need to lock down all the enemies".
  Encoded per-mission it had covered only We Were Never Alone, leaving nine of
  eleven Reclaim checks asking for a weapon alone.

- **Miner gates Not My Mars and Ruins Repurposed, SOFT.** Both are energy
  problems caused by map geography, not income appetite. Soft because neither is
  literally impossible - Not My Mars can be done by hovering the rift lab
  between islands, which the designer rejected as logic, and Ruins is "doable,
  but it's hard mode/out of logic". Logic requires the Miner so the generator
  never counts on a hard-mode run; the tracker shows yellow because the player
  CAN do it.

### The mortar + nullifier baseline

What a lone Mortar plus a Nullifier already clears, in the designer's words:

  "Anything Somewhere in Spacetime and above will also need more firepower than
   just the morter + nullifier to nullify stuff, and reclaim (including Ever
   After). below that it's doable (as long as there isn't another requirement,
   i.e. Archon, Shattered)."

  "Serious will need more than just morter and nullifier to do the
   nullify/reclaim. That's hard mode in that state, and i don't even know if its
   possible."

So, for Nullify and Reclaim specifically:

| missions | mortar + nullifier | notes |
|---|---|---|
| 1 to 7, 9, 10, 13 | ENOUGH | subject to each mission's own requirements |
| 8 Serious | NOT enough | "hard mode ... don't even know if its possible" |
| 11 Shattered, 12 Archon | blocked by their own rules | greenar / nullifier+shield |
| 14 to 20 | NOT enough | includes Ever After |

"Below that it's doable" is about Nullify and Reclaim only. Totems on those
missions still need the greenar chain, and a mission's own MISSION_EXTRA still
applies - the baseline is not a claim that missions 1 to 13 are winnable with a
mortar.

### Open: what clears the wall

NOT ENCODED, deliberately, because there is no data yet. Missions 8 and 14 to 20
currently carry no firepower requirement, so logic still overstates what a lone
mortar can do there.

The designer's account of why it is hard to state:

  "Miners help get resources, which does help, only on some missions does just
   miner + factory help if there's unique buildings on the map that are weapons.
   Terp can also help build natual pushing barracades, i kind of like that as an
   option. Another weapon helps as well, but Sprayers need Factory and Bluite to
   actually be weapons. Getting ERNs and bonus energy (not cap, and not ERN
   bonus unless you also have the ERN Port and ERNs to use). It's a delecate
   balance."

And a single ERN was observed to matter, without being decisive:

  "in this specific test I did get an early ERN (jsut one) and that did make
   doing the Morter + nullifier only a little easier to do, not trivial by any
   means, but it does show that those kinds of things can swing it possibly."

The requirement format is AND-of-OR-groups, so "any ONE of these clears the
wall" is directly expressible and "any TWO of these five" is not. The plan is
therefore to find items that each INDEPENDENTLY clear it, and put them in one
OR-group beside the Nullifier.

Test protocol agreed 2026-09-03: one benchmark mission (Serious), one run per
candidate, the harness granting the exact item set so only the play is manual.
Candidates: Cannon (the designer's prime suspect - "Cannons can do most
anything" from 2026-08-25 remains untested against this wall), Terp, Miner,
Miner + Factory, and 2 ERNs / bonus energy.

**Price of the ERN answer:** ERN and energy items are deliberately filler that
gates nothing. If an ERN count clears the wall it must become progression, which
changes item classification, the pool and the sphere structure. That cost is
worth naming before the test rather than discovering it after.

### Wall test results (Serious, benchmark mission)

Run 1, the CONTROL - rift lab, tower, Mortar, Nullifier:

  "Only Morters (i don't have enough power to use get to the single spore to use
   the nullifier, the single spore isn't really the issue, it's the generation
   ground). I can get just over 20 gen. but my static defence is using over
   that. meaning i don't have enough gen to have a group of units to push out.
   even erns wouldn't help here as it just make the morters shoot
   faster/farther, not more efficiently (as far as I know)."

VERDICT: not possible. And the reason is more useful than the verdict.

The wall is an ENERGY BUDGET, not firepower. Serious offers just over 20
generation because of how much ground can hold towers, and static defence spends
more than that, so there is never a surplus to field a push. The objective
itself - one spore - is not the obstacle.

That reframes what can clear it. A candidate has to either RAISE GENERATION or
LOWER THE COST OF HOLDING GROUND:

- **Terp** makes buildable ground, which is the constrained resource named here.
  On this diagnosis it looked like the strongest candidate, ahead of a second
  weapon - see run 2, which settled the question before Terp was reached.
- **Base generation items** add income directly. Their full set is +10 against a
  budget of ~20, so roughly a 50 percent increase - large enough to matter, and
  the reason the "is +5 energy good?" question cannot be answered without a
  mission's budget in hand.
- **Cannon** only helps if it holds the same line for less energy than a mortar.
  That is a per-unit constant and IS measurable, unlike map-level energy.
- **Miner** helps if it adds income independent of tower ground.
- **ERNs are ruled out by mechanism**, not by feel: they change rate and range,
  not efficiency, so they cannot create a surplus that does not exist. Run 6 was
  dropped for this reason.

Run 2 - the control set plus CANNON:

  "yes with cannons i could nullify and eventually reclaim. they can defend the
   slittle areas with less energy and do more consiste attacks to take over more
   space. there's a possiblity that you can do it only with morters, but in
   general i would say it outside of even possible logic (it's very hard mode)."

VERDICT: cleared. And it clears the wall for exactly the reason the wall exists -
a cannon holds the small chokes for LESS energy, which is what creates the
surplus that mortars never leave. The extra pressure to take ground is a bonus
on top, not the mechanism.

This confirms the designer's standing position from 2026-08-25 ("Cannons can do
most anything. Mortars: same but a little harder") and sharpens it: on a
generation-starved map the difference is not "a little harder", it is the
difference between having a surplus and not.

CONSEQUENCE FOR LOGIC. OFFENSE is an OR-group, Cannon or Mortar, and that is
wrong for this class of check: mortar-only here is "outside of even possible
logic (it's very hard mode)". So Serious' Nullify and Reclaim need CANNON
specifically, not either weapon. Whether Terp or Miner ALSO clears it decides
whether the encoded group is ["Cannon"] alone or ["Cannon", "Terp", ...] - and
that matters, because a group that is too narrow paints reachable checks red.
Runs 3 to 5 exist to answer exactly that and must not be skipped now that run 2
has a positive.

Run 3 - the control set plus TERP, no cannon:

  "Yes with terp it's doable, slow, but doable. you can close up the holes in
   the base, and start building out safe spots and walls to conserve energy and
   carve out more spots."

VERDICT: cleared, slowly.

Two independent clears now, and BOTH act on the energy budget rather than on
damage: the cannon spends less to hold a choke, the terp both conserves (walls
and sealed holes mean less line to defend) and produces (carved spots hold more
towers). Nothing so far clears this wall by shooting harder, which is why
"firepower" was the wrong name for it.

So the group has at least two members, ["Cannon", "Terp"], and the earlier worry
about a too-narrow group was justified: encoding run 2 alone would have painted
every Terp-only route red.

Run 4 - the control set plus MINER, no cannon and no terp:

  "Yes with miners it's doable with morters, it's slower than with cannons, but
   you get up to like 30, a little more generation which is enough overhea to
   build a hit squad to push out. (cannons are just that strong in comparison)."

VERDICT: cleared, slower than the cannon.

This is the run that put a NUMBER on the wall. Serious yields just over 20
generation from tower ground alone and static defence spends all of it; miners
take it to about 30, and that ~+10 of headroom is what pays for a push squad.

Two things follow.

RUN 5 WAS DROPPED. Miner + Factory is a superset of a set that already clears,
so it can only clear too - it cannot change the OR-group. The distinction the
designer drew between them ("only on some missions does just miner + factory
help if there's unique buildings on the map that are weapons") is about OTHER
missions, and would need those missions to test.

THE +10 IS THIS MAP'S NUMBER, NOT THE MINER'S. Corrected by the designer
immediately after the run:

  "remember that's just on this map, since you get that much RESO field
   available to you right away. it won't always be +10 on other maps"

So a miner is worth as much RESO field as the map hands you, early. It is not a
flat +10, and Serious happens to be generous. That kills the tempting shortcut
of treating "Miner" and "+10 generation" as interchangeable in logic: they are
equivalent HERE and need not be anywhere else.

What survives as a yardstick is narrower but still useful: on a mission whose
budget is ~20 and whose defence eats all of it, about +10 of headroom bought a
push squad. It says "+5 alone was probably not enough on Serious". It does not
say what +5 does on any other map, and the Progressive Base Generation set's +10
cap only matches Serious by coincidence.

Testing the energy items at all is therefore only worth it if the designer would
accept the price: they are deliberately filler that gates nothing, and any of
them entering logic makes them progression and reshapes the pool.

The OR-group after four runs: ["Cannon", "Terp", "Miner"]. Every member acts on
the energy economy - spend less holding ground, or produce more of it. None of
them clears the wall by shooting harder, so the requirement should be NAMED for
the economy rather than for firepower when it is encoded.

### Spot-check: Somewhere in Spacetime (m14), the late-mission wall

  "with morters and nullifiers it is doable. i'd say medium/hard, so out of
   logic, but not by much (yellow). There's no RESO so miners won't help here.
   because the map already has natual choke points TERPs only help a little, but
   probably not enough to move the needle. with only morters you definitly need
   nullifiers to help, if you don't then it's not possible (completely out of
   logic). Factories and sprayers would help (on top of morters and nullifiers).
   just cannons and nullifiers would be doable (in logic), morters and
   nullifiers, with snipers would move the needle to doable (the eggs, and blobs
   and skimmers are annoying enough that killing ahead of time makes it a bit
   easier, you don't need to build tower walls as defence). Some erns with
   morters and nullifiers might work. I think 2-3. Actually there's 6 burried
   ERNS on the map so TERPS would help with that and I would say that's doable
   as well."

This is a DIFFERENT wall from Serious, and the difference is the whole point of
having two layers:

- Serious with mortars only is "outside of even possible logic" -> HARD, red.
- m14 with mortars and nullifiers is "doable ... medium/hard, so out of logic,
  but not by much" -> SOFT, yellow. Possible; just not promised.

And the members that clear it are NOT the same set:

| item | Serious (m8) | Somewhere in Spacetime (m14) |
|---|---|---|
| Cannon | clears | clears ("in logic") |
| Terp | clears | clears, but for a MAP-SPECIFIC reason - 6 buried ERNs to dig up. Its usual value is low here because the map already has natural chokes |
| Miner | clears (+10 from RESO) | USELESS - "There's no RESO so miners won't help here" |
| Sniper | not tested | clears - eggs, blobs and skimmers killed early mean no tower walls needed for defence |

That is the predicted failure mode caught in the act: a shared
["Cannon", "Terp", "Miner"] group would have told a m14 player holding a Miner
that these checks were reachable. There is no RESO on that map. That is a
soft-lock, and it is exactly why the group has to be per-mission.

Nullifier is confirmed HARD here independently: without one "it's not possible
(completely out of logic)", which the type-wide Reclaim/Nullify rule already
covers.

TWO CANDIDATES DELIBERATELY LEFT OUT of m14's group:

- **Sprayer** ("Factories and sprayers would help"). Cannot be expressed
  soundly. An OR-group is satisfied by any ONE member, so listing "Sprayer"
  would claim a bare Sprayer suffices - and a sprayer needs a Factory and
  bluite to be a weapon at all. _expand only adds a prerequisite when EVERY
  member of the group shares it, which is correct and means it will not rescue
  a mixed group. Encoding this needs a nested requirement the format does not
  have.
- **ERNs** ("might work. I think 2-3"). Hedged, and it carries the price of
  turning filler into progression. Left for a deliberate decision rather than
  smuggled in on a maybe.

### The wall is a per-map generation THRESHOLD, and that changes the encoding

  "and 30 gen is also what looks to be what we need to only work with morters on
   that map as well. it'll be a different level on different maps"

So Serious' wall is not "you need a cannon" or "you need a miner". It is "reach
about 30 generation", and every clear so far is just a different route to it:

    Cannon   lowers the DEMAND  - holds the same choke for less
    Terp     both               - walls cut the line to defend, carving adds spots
    Miner    raises the SUPPLY  - worth as much RESO field as the map offers early

Logic cannot say "30 generation": it can only name items. So each mission's group
is the set of items that reach THAT map's threshold, and the threshold moves.

WHICH WAY TO ERR, AND WHY IT IS NOT SYMMETRIC. An OR-group is satisfied by any
one member, so:

- A group that is too NARROW understates: checks read red that a player could
  actually take, and the generator places items later than it needs to. Safe,
  merely conservative.
- A group that is too BROAD is dangerous: a Miner in the group for a map with no
  early RESO field makes logic call that check reachable when it is not, and
  that is a SOFT-LOCK - exactly the failure the 2026-09-03 playthrough kept
  hitting from the other direction.

Tonight's evidence is from ONE map, so a single shared group across missions 8
and 14 to 20 would be the broad, dangerous kind of guess.

PROPOSED SHAPE, pending the mission 14 spot-check:

- **Cannon is the safest shared member**, because it works on the demand side.
  It needs no RESO field and no terpable terrain, so it travels between maps in
  a way the supply-side items do not.
- **Terp and Miner get added per mission**, only where a run has verified them,
  since each depends on a map feature that may be absent.

That keeps the failure mode on the conservative side, at the price of some
checks reading red that a resourceful player could take.

### Energy cannot be answered by measurement alone

An earlier plan to settle the energy questions with sim probes was wrong, and
the reason is worth keeping:

  "each mission can require different energy demands. there's different usable
   land for Towers to occupy and produce energy for you. different weapons
   require different energy demands, enemies produce different amount of creep."

Global constants are measurable (a weapon's draw, a tower's output, what an ERN
does to reload). Whether the ceiling beats the pressure on a given map is not.

### Fixing the fill, measured (2026-09-03)

The late-mission logic review made the pool harder to fill: a 32-configuration
sweep found failures in about 0.17 percent of seeds on the configurations that
fail at all. The failure is structural, and worth stating precisely because two
plausible fixes made it WORSE.

WHY IT HAPPENS. Archipelago's fill places progression one item at a time into
currently-reachable locations, greedily. Two starter missions give exactly two
locations reachable with no items. And most of this world's progression items
open nothing on their own - totems needed Refinery AND Factory on 14 missions,
Chronat and Platform and Rocket Pad need that same chain, Sequence needs Sniper
AND Miner AND a mover. Mission unlocks are the only reliable openers. So the
fill has two slots and a pool of mostly duds: place two duds and the seed
strands.

MEASURED, six failure-prone configurations, hundreds of seeds each, always with
a positive control:

| change | 1 starter | 2 starters (default) |
|---|---|---|
| before | - | 0.167 percent |
| engage bootstrap_opening for standard | - | 1.208 percent (SEVEN TIMES WORSE) |
| a second/third early mission unlock | - | worse: 17 and 11 failures vs 0 |
| starter_missions default 3 | - | 0.000 percent |
| MERGE THE GREENAR PAIR | 0.25 percent | 0.014 percent (1 in 7200) |
| merge + force one broad starter | 0.056 percent | 0.000 percent |

WHAT WAS ADOPTED, and what was not:

- **The merge.** Factory unlocks the greenar refinery too, so the biggest dud
  class is gone. The designer's reason came first and the fill benefit second:
  "there's litterlay no reason to have a refinery without the factory". The
  refinery's NAME is retired rather than deleted, because item ids are
  positional.
- **starter_missions floor raised to 2.** One starter could not be made to
  generate reliably even with the merge and a forced broad starter.
- **NOT the default of 3.** It measured perfect and was declined: "I don't like
  widening to 3 levels at the start."
- **NOT the forced broad starter.** It bought nothing at two starters, and would
  have cost the varied openings that random starters exist for.
- **NOT the bootstrap for standard.** Actively harmful, and it had been written
  up in items.py as "the known remedy" on the strength of helping casual - an
  inference that was never measured on standard and turned out backwards.

### Generation reliability: we place our own progression (2026-09-03)

**The problem was never the rules.** Archipelago verifies every seed it writes -
it computes a playthrough and refuses to output one it cannot prove beatable -
so a `FillError` never ships a broken seed. It writes NO seed, and the human
re-runs generation.

But it happened about once in 18,000 solo seeds, and a randomizer whose seeds
sometimes fail to build is bad design.

WHY IT HAPPENED. `Fill.fill_restrictive` is greedy and its backtracking is
hard-capped - each item may be swapped at most twice ("if swap_count > 1:
continue") behind a three-entry state cache. It is not an exhaustive solver, so
a SOLVABLE arrangement can defeat it. Every captured failure had the same shape:
the opening failed to chain in the first few placements and it stopped with the
world still empty (one had 231 of 236 locations unfilled, holding 15 mission
unlocks).

WHAT DID NOT WORK, all measured, all recorded in apworld/cw4/items.py:
bootstrapping standard seeds (7x worse), pre-placing a guaranteed opener (8x),
dropping the early weapon request (24x), extra early unlocks (worse), and
ordering the fill's location list in EITHER direction (3x). Constraining the
starter draw to include a mission a weapon can open was measured at "0 in
12,600" and then RETRACTED - bucketing 9,000 seeds by starter category showed
failures spread evenly, and two of four failing pairs contained a broad mission.

WHAT WORKED. Two things, both about the problem rather than the search:

1. **Merging the greenar pair** into one item, so the campaign's biggest
   "opens nothing alone" class disappeared. 4x better, and a simplification the
   designer wanted anyway.
2. **Placing our own progression, with retries** - `items.place_own_progression`,
   called from `World.pre_fill`. A world cannot catch or retry the MAIN fill, but
   it can place its own items and retry, which is exactly what `oot` does for
   songs (6 attempts) and `pokemon_emerald` for badges and HMs.

MEASURED, on the real pipeline rather than through the unit tests:

| | result |
|---|---|
| attempts needed, 8,000 seeds | 7611 x1, 364 x2, 24 x3, 1 x4, **0 exhausted 5** |
| generation failures, with the fill | **0 in 16,000** |
| generation failures, without | 1 in 4,000 (0.025 percent) |
| unreachable locations / unbeatable | 0 and 0 in 1,500 |
| multiworld, CW4 + ChecksFinder | 0 failures in 200, our fill correctly never ran |
| CW4 progression in the other world | 4.0 items per seed - unchanged |

Our single attempt is far WORSE than Archipelago's orchestrated fill (about 5
percent against 0.025) because it skips the priority pass and
`accessibility_corrections`. It wins only by being allowed to roll again. That
is fine - and checked: 0 unreachable and 0 unbeatable seeds in 1,500.

SOLO ONLY (`items.OWN_FILL_SOLO_ONLY`). A multiworld does not need it - measured
0 failures in 200 - and applying it there would place all our progression
locally, costing the cross-game placements the design values. The designer
initially asked for it everywhere, then chose solo-only once it was clear the
retry LOOP is free but the fill itself runs every time.

TESTING NOTE. `CW4TestBase` switches this fill OFF by default, because access
tests assert reachability by removing items from the POOL and a pre-placed item
is not in the pool - 27 of them broke the moment it was added, and they would
have kept passing while testing nothing. The fill is covered directly by
`TestOwnProgressionFill`, and generation is measured by
`tools/audit/realfillrate.py`, which runs the real pipeline. `tools/audit/
fillrate.py` drives the unit tests and therefore does NOT exercise this - a
20,000-seed sweep with it looked unchanged for exactly that reason.
