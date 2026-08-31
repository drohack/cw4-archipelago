# CW4 Archipelago Randomizer Design (draft)

Status: draft, 2026-08-25. Data tables below are filled by the automated
campaign survey (probe v0.54, scratchpad/survey.sh).

See also: [AP feature comparison + recommendations](design/2026-08-26-ap-feature-comparison.md)
- what other AP randomizers do and the designer's decisions on what to adopt
  (name groups, idempotent replay, seed binding, and a traps feasibility test
  next; logic tiers in this doc; star/token gating declined).

## Scope

- Official Farsite Expedition campaign only: story1..story20 (story0
  tutorial exempt, always available). SPAN Experiments (26 missions) is a
  stretch goal - it shares the SpanNetworkPlanet map system.
- Goal: complete story20 ("Founders"?) - hard-locked as the finale; its
  Mission Unlock item exists but logic requires a configurable number of
  completed missions before it is reachable (SC2-style).

## Items

| Category | Count | Notes |
|---|---|---|
| Mission Unlock: story2..story20 | 19 | story1 (or a configurable starter set) starts unlocked |
| Unit unlocks | ~20 | cannon, mortar, sprayer, sniper, missilelauncher, nullifier, miner, greenarrefinery, terp, porter, factory, ernportal, runway, bomberpad, acbomberpad, rocketpad, platform, shield, microrift, chronat, airship, bertha, sweeper (exact list per survey; riftlab/tower/pylon always available) |
| Progressive ERN | ~4-8 | ernportal building is an item; ERNs to slot are progressive |
| ERN Spawning | 1 | separate late-game unlock: whether the ERN portal may actually spawn/produce ERNs. Gates supercharging units. Never mission-required, so it doubles as strong filler |
| Build limit increases | filler | e.g. +N tower/cannon limit as useful filler |

## Locations

- CW4's six objective slots are fixed by TYPE (index 0 Nullify, 1 Totems,
  2 Reclaim, 3 Hold, 4 Collect, 5 Custom - confirmed by the survey). One
  location per REQUIRED objective, named "<Title> - <Type>" (e.g.
  "Home - Nullify"); the client maps objective index -> type directly.
- One "<Title> - Mission Complete" location per mission except the finale,
  whose completion is the Victory event.
- Total: 39 objective + 19 mission-complete = 58 locations (see
  apworld/cw4/locations.py REQUIRED_OBJECTIVES for the per-mission table).

Client/apworld contract (slot_data requirement groups, persistence, tracker
colors, save archiving): see design/2026-08-25-mod-wiring-design.md.

## Regions and access

- Region per mission; Menu -> story1 (or starter missions).
- Edges follow the planet graph (survey `graph` dump:
  connectedPlanetGUIDS). Entering region storyN requires:
  1. `Mission Unlock: storyN`
  2. Unit logic for that mission (below)
- Spheres are emergent: AP computes them from these rules during fill.

## Unit logic (SC2-style semantic predicates)

- has_offense: any of cannon/mortar/sprayer (creeper clearing)
- has_antiair: sniper or missilelauncher (required where census shows
  spores/airsacs/skimmers)
- has_economy: miner (+ greenarrefinery for late missions)
- has_mobility/support: terp/porter/platform etc. where terrain demands
- Baseline rule per mission: the vanilla availability schedule (natflags
  survey) is the reference: a mission is in-logic when the player's unit
  set covers the semantic needs that the vanilla loadout covered.
- All missions were designed to be beatable with their vanilla loadout;
  logic never requires MORE than vanilla, only equivalents.

## Client behavior (proven by probe)

- Locks/unlocks: per-frame whitelist enforcement + LeftPane refresh recipe.
- Live delivery: works mid-mission (SetEnabledButtons path).
- Location sends: objective/mission-complete transitions via World polling.
- Mission gating: OnLaunch + OnLoad prefixes; map shows native "?" for
  locked missions (mcs rewrite gives clean slate per slot; display owned
  by mod thereafter); LockedPlanet visual + marker recoloring for tracker
  states. Palette settled: red not accessible / yellow reachable but not in
  logic / green reachable and in logic / orange partial / grey finished,
  per the Archipelago-PopTracker convention in the wiring design.
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

### Derived logic categories

- has_offense = cannon OR mortar OR sprayer  (needed from story2 on)
- has_nullify = nullifier                     (nullify objectives, story3+)
- has_antiair = missilelauncher OR sniper     (story6+ where spores present)
- has_economy = miner                         (story4+), + factory/ern for
  late-mission energy scale (story10+ heuristic)
- has_terraform = terp                        (specific terrain missions)
- Baseline in-logic rule for storyN: player's set covers every category the
  vanilla cumulative loadout at N covered (never stricter than vanilla).


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
  14 missions have one: 2,3,4,5,7,8,9,10,12,13,14,15,16,20.
- **Nullify objective requires the Nullifier** (4 missions: 2,11,15,20).
  Already encoded; missions themselves never require it.
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
| 16 The Compound | Sniper | saw blades die only to snipers, "no way to do any objectives without" |

The Compound is the ONLY mission where a sniper is in logic.

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
unchanged at 58. The mod's off-map placement now hangs Ever After off Wallis and
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

71 world tests, 80 C# tests, and 80 generated seeds with no failures across
defaults, ERNs at 0 and 40, generation-only filler, traps at 0 and 100 percent, a
single starter mission, and 4-player multiworlds.

### Open design options for more locations/items (user, 2026-08-27)

Raised while sizing the pool (59 locations vs 47 real items = 12 filler slots):

1. **Caches as individual locations.** The "blocks you connect the network to"
   are InfoCaches, and they ARE the Collect objective - `GameSpace.mustCollect`
   equals the InfoCache count on every mission measured. Today all of a
   mission's caches collapse into ONE "Collect" check; each could be its own
   location instead. Counts are in the table below.
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
   `riftLabPreplaced` in the table below is measured from `GameSpace.commandBase`
   existing at mission load.

CAVEAT on all counts below: they are measured at MISSION START. Enemies, totems
and nullifiable structures can appear during play - story2 has both a Nullify
and a Totems objective yet reports 0 nullifiable and 0 totems at load. Treat
these as a floor, not a total.

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

### Buildings the mod does not model (2026-08-26)

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
