# CW4 Archipelago Randomizer Design (draft)

Status: draft, 2026-08-25. Data tables below are filled by the automated
campaign survey (probe v0.54, scratchpad/survey.sh).

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
  states (grey locked / red nothing / yellow out-of-logic / green in-logic
  or done - final palette TBD).
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
