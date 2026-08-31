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
| `Build Limit +1 (<Unit>)` | filler | Increments over the game's own default limits |
| Energy storage / base generation | filler | Two items, applied to the rift lab. See "Energy items" below |
| Traps | 7 | Share of the non-progression slots set by `trap_percentage`, default 50 |

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

`Energy Storage Upgrade` and `Base Generation Upgrade`, both applied to the rift
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

### Degradation over failure

An unfillable preference must not fail generation: zeroing every filler weight
falls back to build limits, and zeroing every trap weight turns those slots into
useful items. Both are tested.

### Verified

108 world tests and 104 C# tests as of 2026-08-31. The 80-generated-seed sweep
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
