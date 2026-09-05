# Creeper World 4 + Archipelago: Feasibility Research

**Started 2026-08-24; appended to through 2026-08-31 across six sessions.** Every
fact was verified against the installed game on this machine, not recalled from
documentation - but "verified" means verified ON ITS DATE. Later sessions overturn
earlier conclusions in a dozen places.

**How to read it.** This is an append-only log, and its chronology is NOT
monotonic: the newest material sits at both the front and the back with older
material in between, so position does not tell you recency. Where a later finding
overturns an earlier one, the earlier one carries a `SUPERSEDED` or `CORRECTED`
marker pointing forward. If you find a confident claim with no such marker and it
disagrees with the code, trust the code and add the marker.

**Current verdict per topic** - the answer, and where the reasoning is:

| Topic | Current answer |
|---|---|
| Unit naming | FOUR name spaces. Build-pane keys, unit names, data names, and BUTTON object names. `porter` = `DeliveryPad` + `DeliveryDrone`. See "Unit naming" and "A fourth name space" |
| Killing a unit | Health is one of FOUR paths. `impervious` + `DESTROY_ON_UNEVEN_TERRAIN` covers damage and terrain, but **not nullification** - see "Nullification is a fourth removal path" |
| Energy | The store IS `commandBase.ammo`, the ceiling IS `MAX_AMMO`. `energyProduction` and `statEnergy*` are recomputed summaries: writing them is cosmetic |
| Mission map | Event-driven off `Span.Start` and `SpanNetworkPlanet.Refresh`. Marker colour is `_color` (lowercase); glyph shape is the material, selected by writing `objective`. Refresh APPENDS markers |
| Cache collection | `mustCollect` shrinking is the signal. `InfoCache.Retrieved` is NOT on the pickup path; a collected cache is destroyed |
| Power zones | None in the campaign, and the reader is verified by positive control |
| Save archiving | `saves/farsite` is moved per slot. **mcs.dat is left alone** - Steam Cloud restores it |
| Build panes | Five separate stacked GameObjects, not one shared strip |

## Environment

- Game install: `G:\Games\Steam\steamapps\common\Creeper World 4`
- User data:    `C:\Users\droha\Documents\My Games\creeperworld4`
- Unity 2019.4.23f1, **IL2CPP** (`GameAssembly.dll`, 42 MB)
- `global-metadata.dat`: 11.8 MB, magic `af1bb1fa`, version 24, **unencrypted**

## 4RPL (the official scripting language)

698 commands total. Scanned the complete command database.

- **No file read. No network. No sockets. No HTTP.**
- `Print` writes to `RPL.txt` in the game root, live, truncated on map load.
  This is the only outbound channel from a running map.
- `GetMCSEntries` / `DeleteMCSEntry` read and modify `mcs.dat`.
- `SendMVerseMsg` + `RegisterForMSG` carry arbitrary data over CW4's
  multiplayer layer. Real bidirectional networking, but the wire protocol is
  proprietary and undocumented. Rejected as an option.

### Unlock primitives

`SetUnitCanBuild(unitType, bool)` and `SetUnitBuildLimit(unitType, n)` work at
runtime and drive the build pane directly. The 15 buildable types:

    riftlab  factory  ernportal  tower  pylon  miner  greenarrefinery
    terp  porter  cannon  mortar  sprayer  sniper  missilelauncher  nullifier

Mission flow: `AcquireMissionObjective`, `SetMissionObjectiveEnabled`,
`SetMissionObjectiveRequired`, `IsMissionComplete`, `EndMission`.

## File formats (all reverse-engineered and confirmed)

Every CW4 data file is `[uint32 LE uncompressed size][gzip stream]`.
Inside is a tagged binary tree:

| Tag    | Meaning | Payload |
|--------|---------|---------|
| `0x0A` | node    | uint16 name length + name |
| `0x01` | int32   | uint16 name length + name, then 4 bytes |
| `0x03` | string  | uint16 name length + name, then uint16 length + data |

Applies to `.cw4` maps, `mcs.dat`, `achievements.dat`.
`LastMetaData` is plain gzipped XML.

**4RPL scripts are stored as plain UTF-8 text inside `.cw4` files**, under
nodes named like `Player.4rpl`. So Colonies maps can be read, injected, and
rewritten programmatically.

## The finding that decided the architecture

**Official campaign maps are not loose files.** They are baked inside
`CW4_Data/data.unity3d`, a 391 MB UnityFS bundle. No `.cw4` files exist in the
install directory.

Since the project targets the official campaign only, script injection into
map files does not apply. That removes 4RPL as the primary mechanism and
points to a BepInEx plugin instead.

## Chosen architecture

- **Python apworld** - item/location definitions, logic, generation.
  Required; this is Archipelago's core and cannot be another language.
- **C# BepInEx IL2CPP plugin** - hooks the game directly and opens a
  websocket to the Archipelago server via `Archipelago.MultiClient.Net`.

No third-party launcher, no `RPL.txt` tailing, no file IPC, no synthetic
keystrokes, no focus stealing. The BepInEx bundle ships
`System.Net.WebSockets.dll`, so the plugin can talk to AP directly.

## Prior art

None. No Creeper World Archipelago apworld, randomizer, or BepInEx plugin
exists publicly. This would be the first.

## Useful symbols found in game metadata

`ernPortalAvailable` (with `get_`/`set_`), `buildErnPortal`, `SetBuildLimit`,
`MissionPanelUnlock`, `GalaxyMissionPanel`, `mission21`, `CompleteFarsiteStory`.

## Unit naming: build-pane keys vs unit names (2026-08-28)

**Three different name spaces, and mixing them up costs hours.**

1. **Build-pane keys** - the `BuildUnitManager` availability flags:
   `cannonAvailable`, `pylonAvailable`, `minerAvailable`, `riftLabAvailable`...
   26 of them. `Core.UnitRules.ItemToUnit` maps AP items to these. This is all
   `UnitGate` needs, and it is correct for that purpose.
2. **Unit names** - the keys of `UnitData.unitConstants`, 88 entries, PascalCase:
   `Cannon`, `Tower`, `TowerBridge`, `Collector`, `CommandBase`, `Emitter`...
   This is what `UnitManager.CreateUnitAtPosition(name, pos)` accepts and what
   the `<Name>BuildGhost` objects are named after.
3. **Data names** - what `UnitManager.GetDataName()` returns at runtime: the
   unit name, lowercased (`cannon`, `towerbridge`, `collector`), except the rift
   lab which returns `CommandBase` with capitals.

**The trap: several build-pane keys do not exist as unit names at all.**

| Build-pane key | Actual unit name |
|---|---|
| `riftlab` | `CommandBase` |
| `pylon` | `TowerBridge` |
| `miner` | `Collector` (+ `CollectorPanel3`, `CollectorPanel5`) |
| `ernportal` | `ERNInterface` |
| `porter` | the DELIVERY family - see "A fourth name space" below |
| `airship`, `bertha`, `sweeper` | **not found** - see below |
| everything else | direct case-insensitive match |

### Airship, bertha and sweeper are CMOD units (2026-08-28, CORRECTED)

An earlier version of this section concluded these three "do not exist", because
they are absent from all three name sources (`unitConstants`, build ghosts,
`LeftPane.BuildUnitX`). **That conclusion was wrong.** They exist as **CMOD
units** - CW4's custom-unit system - which is why the `CustomUnitBuildPane` looked
empty and why they are missing from every name list.

A CMOD unit's `GetDataName()` returns a **GUID**, never a name, so no name
whitelist can ever match one. Confirmed in-game:

    cmods: 3 player-buildable: AIRSHIP[ca8dfbe4] BERTHA[b2d47782] SWEEPER[c5b44bd0]
    cmods: 8 with no player menu name (map/editor only)

Those three GUIDs are exactly the ones that showed up as "building but not in the
player list" while airship, bertha and sweeper kept building at normal speed with
InstantBuild on.

**The ownership test for CMOD units** is `GameSpace.cmods[guid].playerMenuUnitName`:
non-empty means the unit is offered in the PLAYER's build menu. This is
data-driven, so new custom units are handled without code changes, and it cleanly
separates the 3 player units from the 8 map/editor-only ones. Implemented as
`IsPlayerCmod` in both mods.

**CMODs register LAZILY, and measuring before that fooled a whole investigation
(2026-09-01).** `GameSpace.cmods` reports 0 player-buildable on every campaign
mission at load. Turn the availability flags on - by god mode's AllBuildings, or
by the randomizer granting the items, both work identically - and it becomes:

    DEVTOOLS cmods: 3 player-buildable: AIRSHIP[ca8dfbe4] BERTHA[b2d47782] SWEEPER[c5b44bd0]

So a scan of all 20 missions reading zero does NOT mean the campaign has no
custom units; it means nothing had asked for them yet. That reading, plus
`pane:dump`'s unreliable ON/off, led to a confident and wrong conclusion that
Airship, Bertha and Sweeper were dead items in the pool. They are not. Their
buttons are the CMODUNITBUTTON slots - one in the AIR pane, two in SPECIAL -
which is three buttons for three units, and they are absent from
CustomUnitBuildPane entirely.

The lesson is the one this file keeps relearning: a zero is not a finding until
something has been made non-zero on purpose. See "Power zones" for the same
shape, done right.

**The CMOD ownership fix is VERIFIED (2026-09-01, hands-on).** With an airship,
a bertha and a sweeper built on Founders, all three trap effects that filter on
ownership reached them, and the two controls both held:

    TRAP drain: emptied 3 weapon(s), 550 ammo removed
    TRAP stun: 3 player unit(s) stunned; 10 cannot be stunned, 74 not the player's
    TRAP spore: aim=PlayerBuilding onto 13 candidate building(s)

    CModUnitManager/ca8dfbe4-...=MINE     AIRSHIP
    CModUnitManager/b2d47782-...=MINE     BERTHA
    CModUnitManager/c5b44bd0-...=MINE     SWEEPER
    CModUnitManager/0c43b01a-...=other x4 the map's own
    CModUnitManager/e76d9994-...=other x4 the map's own
    CModUnitManager/abe9d7ea-...=other    the neutron reactor

The negative control is what makes this meaningful: the map's own custom units
have GUID data names too, and a fix that accepted anything GUID-shaped would have
claimed all ten. Confirmed visually at the same time - ammo vanishing from the
airship and bertha, the sweeper running dry mid-fire, spores aimed at the sweeper
and a stun on the bertha.

Note the histogram in `trap:status` is capped at the 14 commonest types, so units
with a count of 1 - which is every hand-built one - do not appear in it. Read
`playerUnits=` and `withAmmo=` instead, or the units-on-map dump. That cap cost an
hour of confusion here.

NOTE 2026-08-31: this said "both mods" from the start but was only true of
CW4DevTools for three days - the randomizer's `GameUtil.IsPlayerUnit` had no
CMOD branch at all, so trap stun, ammo drain and spore targeting silently
skipped the player's airship, bertha and sweeper. Found by a documentation
audit, not by play. It is now genuinely in both.

Verified: spawning `c5b44bd0-...` (SWEEPER) now logs `instant-built`, where before
it was skipped entirely.

Also corrected: **enemy units DO build.** `Pod` appeared as "building but not in
the player list", so "enemy structures do not build" - used earlier to justify a
diagnostic - is false. The whitelist is what keeps enemies out, not that.

`porter` is resolved as of 2026-08-31 - see "A fourth name space" below. It was
never a registry name at all.

Note the list in both mods contains the literal string `"porter"`, which makes a
naive audit report it as covered. It is not: `GetDataName()` can only ever
return a registry name, so that entry can never match. `DevTools.ReportSkippedBuild`
exists to catch precisely this - it warns when a unit is under construction but
fails the player filter, which is what a placed porter should trigger.

### A fourth name space: BUTTON object names (2026-08-31)

The porter went unresolved for so long because there are **four** name spaces, not
three, and the fourth matches neither of the others. Build-button GameObject names
are historical and internal.

Derived by granting one item at a time and watching `DEBUG UNITS: structButtons`
go 1 -> 2 -> 3, then reading the struct pane's own on-screen labels against the
GameObject list from `pane:dump` (six labels, six buttons, same order, and three
of the six align by name which pins the ordering):

| Pane label | Button GameObject | Build-pane key |
|---|---|---|
| TOWER | `TowerButton` | `tower` |
| PYLON | **`SuperTowerButton`** | `pylon` |
| MINER | **`ReactorButton`** | `miner` |
| REFINERY | `GreenarRefineryButton` | `greenarrefinery` |
| TERP | `TerpButton` | `terp` |
| PORTER | **`DeliveryPadButton`** | `porter` |

**This resolves the contradiction with "Corrections and panel-flag results
(v0.23 final)" further down this file.** That entry said *"'Reactor' and
'DeliveryPad' are INTERNAL names for the MINER and PORTER buttons"* - correct
about the BUTTONS. The mapping table above it says `miner -> Collector` - correct
about the UNITS. Both are right; they are describing different name spaces, and
`SuperTowerButton` for PYLON is the third of the same kind.

So the porter is the DELIVERY family, and `DeliveryPad`, `DeliveryDrone`,
`StoragePad` and `Stash` were **already** in both mods' whitelists - per-unit
effects were covering porters, and the "STILL UNRESOLVED" note overstated the
risk. `spawn:Porter` places nothing, consistent with "porter" being a build-pane
key only.

**CONFIRMED by building one, same day.** A button's object name does not prove
which prefab it places - PYLON's button is `SuperTowerButton` while its unit is
`TowerBridge` - so the last step was a hand-placed porter. It dumps as

    DeliveryPad/DeliveryPad=MINEx1   DeliveryDrone/DeliveryDrone=MINEx1

Both were already whitelisted, both read `=MINE`, and
`DevTools.ReportSkippedBuild` said nothing about them - its only complaints in
that session were `Pod` and `Shot`, an enemy and a projectile, both correctly
excluded. **So `porter` is `DeliveryPad` + `DeliveryDrone`, and there was never a
coverage gap.** Note `Shot` is not in the 88-name registry at all, which is worth
remembering if a projectile ever needs classifying.

**Two oracles that do NOT work, so nobody re-tries them:**

- **The build-ghost dump.** `DevTools.DumpUnits` claims ghost -> prefab ->
  `GetDataName()` is "the decisive mapping with no guessing". It is not: every one
  of the ~60 ghosts reports `(no UnitManager)`, so the prefab carries no
  `UnitManager` to read a name from. The ghost NAMES are still useful (they are
  registry names, and there is no `PorterBuildGhost` at all, which is itself the
  clue that porter is not a unit name).
- **`pane:dump`'s ON/off for availability.** It reads `activeInHierarchy`, which
  on the struct tab reflects PAGING, not availability - all six buttons read `=ON`
  with zero items granted. `DEBUG UNITS: structButtons=N` is the reliable count.

### The 88-name registry, dumped (2026-08-31)

Recorded here because it was nowhere in the repo and every naming question needs
it. `DEVTOOLS ENEMY=false (88)`, alphabetical:

    ACBomber, ACBomberPad, ActivationAntenna, AirSac, AirSacBubble,
    AirSacCauldron, Blob, BlobNest, BlueFab, Bomber, BomberPad, Cannon, Chronat,
    Collector, CollectorPanel3, CollectorPanel5, CommandBase, Conversion,
    Crazonium, Crystal, CytocreepLauncher, Damper, DeliveryDrone, DeliveryPad,
    Denier, Driver, Emitter, ERN, ERNInterface, Fabricator, Fabricator2, Factory,
    FatMan, Flope, Forb, GrayFab, GreenarDrone, GreenarMother, GreenarRefinery,
    InfoCache, Max, Microrift, MissileLauncher, Monolith, Mortar, Nullifier,
    Payload, PayloadPad, Platform, Pod, PowerZone, Pterosaur, PterosaurNest,
    Rain, RainDrop, Reactor, RedFab, ResourceBlue, ResourceRed, Rocket,
    RocketPad, Runway, Shield, Shrapnel, Singularity, SkimmerFactory, Sniper,
    Sparker, Spore, SporeLauncher, Sprayer, Stash, StoragePad, Strafer,
    StraferPad, Strider, SuperTower, SurviveBase, Terp, TerpDrone, Totem, Tower,
    TowerBridge, Transformer, Ultrac, VineRoot, Wall, Workall

No `Porter`. `UnitConstants.ENEMY` reads false for all 88, so the split the dump
prints is not a player/enemy answer - see the discriminators above.

`Strider`, `Workall`, `Transformer` and `Max` - the "unverified candidates" a
comment once offered for the porter - all spawn successfully and read `=other`.
They are map/editor units, not the player's.

Verified: `CreateUnitAtPosition("pylon")` and `("miner")` return **null**;
`("TowerBridge")` and `("Collector")` place successfully.

Consequence, and the bug this caused: any code comparing a build-pane key
against `GetDataName()` silently skips those units. In the randomizer that meant
trap stun, weapon drain and spore targeting passed over pylons, miners and ERN
portals; in CW4DevTools it meant instant build / infinite resources /
indestructible ignored them. Fixed by carrying the real names as aliases
(`GameUtil.IsPlayerUnit`, `DevTools.PlayerKeys`).

Also present in the registry and absent from `UnitRules` entirely: `SuperTower`,
`Reactor`, `DeliveryPad`, `StoragePad`, `Stash`, `TerpDrone`, `GreenarDrone`.
`SuperTower` is ambiguous - it has a player build button but also appears
pre-placed on maps, so classifying it as the player's is a judgement call.

**CAUTION added 2026-08-31.** These were called "player-buildable" on the
strength of a button existing, and the fourth-name-space finding shows that
reasoning is unsafe: `ReactorButton` is the MINER's button and places a
`Collector`, so nothing here demonstrates the registry unit `Reactor` is ever
placed by the player. `Reactor`, `StoragePad` and `Stash` sit in both mods'
whitelists on this unverified basis. Harmless - a whitelist that is slightly
too generous costs nothing, whereas missing one of the player's buildings is a
visible bug - but do not cite this list as proof that a unit is buildable.

**Two player/enemy discriminators that do NOT work:**

- `UnitManager.enemy` (per instance): hostile `Pod`, `Ultrac` and `SuperTower`
  all report `false`; only `Emitter` reports `true`.
- `UnitConstants.ENEMY` (per type, in `UnitData`): reads `false` for **all 88**
  types, so it is a default template, not per-map truth.

Hence both mods use an explicit name whitelist. A whitelist rather than a
blacklist on purpose: missing one of the player's buildings is a visible
annoyance, but missing one hostile type would make an emitter indestructible.

**Build completion:** `UnitManager.isBuilding` is the only correct signal.
`HasBuildBar` / `BuildBarCubes` describe the BAR (5 cubes on everything), not
remaining progress - using them as a fallback flags every finished building.
`CompleteTheBuild(force: true)` finishes a unit and skips its remaining cost.

## Ever After (story20) is parked off the galaxy map (2026-08-29)

The player reported not being able to find "Ever After" on the Farsite level
select, even after beating Founders. It is not missing and it is not locked:

    DEVPLANET 'Founders'   guid=story19 unlocked=True links=1 world=(0,59,13)   screen=(1920,890)  onScreen=True
    DEVPLANET 'Wallis'     guid=story18 unlocked=True links=1 world=(4,59,14)   screen=(2337,994)  onScreen=True
    DEVPLANET 'Ever After' guid=story20 unlocked=True links=0 world=(36,59,-67) screen=(-9432,23230) onScreen=False

All 21 planets (story0..story20) exist on the level select and every one reports
(**NOTE: a later section calls them 'SpanNetworkPlanet (0..19)', which is 20
objects. The two cannot both be right. `planets:dump` prints `DEVPLANETS count=`
and `DEVLINES count=`, so this is one command away from being settled; it has
not been. Nothing in the mod depends on the total - it resolves planets by
title - so this is untidiness rather than a live hazard.**)
`unlocked=True`. The whole campaign spiral sits inside roughly x -10..4, z 13..19;
story20 sits at x=36, z=-67 - about 90 units away, far outside the framed view
and unreachable by "Center View". It is also the terminal node (`links=0`), so
the line that would lead the eye to it runs off-screen as well.

It renders correctly once the camera is there - `span:goto story20` centres the
view on it and the planet appears fully unlocked, green ring and objective icons
and all, alone in empty space. So this is not a rendering fault and not a lock:
the planet is simply placed about 82 units from a campaign that spans roughly
20x16 units, with no visible line leading to it. The Farsite view (`Span`) pans
by free drag. **CORRECTION: there ARE clamp fields** - `SpanMissionNetwork`
carries `minDragX`/`maxDragX`/`minDragY`/`maxDragY`, which `planets:dump` reads
and reports, so whether a planet can be reached depends on those limits and not
on its position alone. This section's conclusion ("unsignposted, not
unreachable") was therefore never established. It stopped mattering because the
mod MOVES Ever After onto the map next to Wallis rather than relying on a drag,
but do not cite the no-clamp claim. Original text: a player CAN drag there - across a
screen of empty starfield, with nothing indicating a direction.

The connection is real in the data: every planet links to the next, `story19 ->
story20`, and story20 is the terminal node with no outgoing link.

story20 itself boots and plays normally - it is the epilogue, opening on "Let's
start eternity by doing some good" and "WE ARE THE FOUNDERS!", with 241 map
units. Finishing Founders plays a cutscene that says the story continues later,
which fits: the epilogue appears to be intended as a story continuation rather
than a map selection.

**There is also no line to it.** The map holds 19 line objects for 20
connections, and none of them is the ~82-unit one Founders -> story20 would
need: that link is simply never drawn. Note that `SpanNetworkPlanet.lines` reads
empty on EVERY planet and is not where the lines live - each line is a child of
a planet's `lineContainer`, a `SpanNetworkPlanetLine` whose `LineRenderer` runs
in LOCAL space from the origin to the neighbour's offset. `SetEnd` takes that
local offset.

**Fixed in the randomizer** (`Appliers/FinalePlacement.cs`): on entering the map,
story20 is moved beside Founders and the missing line is created. The spot is
chosen by sampling directions around Founders at the map's own
nearest-neighbour spacing and taking the one with the most clearance, so it is
deterministic and does not collide with the spiral; the new line copies its
appearance from a line the game built rather than hard-coding colours. All
cosmetic - no unlock, objective or mission content changes, and clicking the
planet boots story20 exactly as before. Verified in game: the planet lands at
local (6.8, -1.7) and the line is indistinguishable from its neighbours.

Re-derive with `story:open`, then `planets:dump` and `span:goto story20`.

## Energy: the store is the rift lab's ammo (2026-08-30)

Investigated because an Archipelago "+energy" / "+storage" item needs a lever.
There is one, and it is a single writable value for each.

    energy store    = GameSpace.commandBase.ammo
    store capacity  = GameSpace.commandBase.MAX_AMMO

Measured directly: `gs.energyStore=63` against `riftlab ammo=62.999`, and the
store sat at exactly 100 - the rift lab's `MAX_AMMO` - until that ceiling was
raised, after which it climbed straight past:

    at cap                  store=100  riftlab ammo=100  MAX_AMMO=100
    after MAX_AMMO += 400   store=125  riftlab ammo=125  MAX_AMMO=500

Granting ammo directly persists and the store keeps rising naturally from the
new level (`ammo 23 -> 423`, then 427, 430, 434), so it is real energy rather
than a display value.

**So: `+generation` adds to `commandBase.ammo` per tick; `+storage` adds to
`commandBase.MAX_AMMO`.** Both are single fields, both stick, and both restore.

**And the energy is genuinely SPENT, not just displayed.** That check matters
because the generation display passed a casual look and turned out to be
cosmetic. With a cannon left unfinished next to the rift lab, the store drains
as it builds:

    build Cannon: isBuilding=True  riftlab ammo=65/100
    watch store=63 -> 59 -> 56 -> 52

So the ceiling is the buffer construction draws from: raising `MAX_AMMO` gives a
player more sustained building before they stall, which is exactly what a
"+storage" item should do.

**Generation scales the same way, and is measurable in the fill rate:**

| | store over three samples | rate |
|---|---|---|
| baseline | 107, 111, 115 | ~1/sec, matching GEN 1 |
| +20/sec bonus added to `commandBase.ammo` | 167, 252, 336 | ~21/sec |

The base has NO production-rate field (`PACKET_REQUEST_RATE` governs requests,
not output), so adding to the rift lab's ammo per tick IS the generation lever -
indistinguishable from the base producing more, because the store is its ammo.

Note the HUD GEN figure will not move: it is computed from the network. The
energy is real regardless, which is the opposite of the old bug where the figure
moved and the energy was not.

### What is NOT the energy economy

These look like the levers and are not - they are summaries the sim RECOMPUTES
every tick from the network, so writes to them change the readout and nothing
else:

| Field | Reality |
|---|---|
| `GameSpace.energyStore` | mirror of the rift lab's ammo |
| `GameSpace.energyProduction`, `energyProductionUnClamped` | recomputed each tick |
| `World.statEnergy*` | HUD display mirrors |
| `UnitManager.SUPPLY`, `UnitConstants.SUPPLY`, `GameSpace.supplyMax` | the BUILD supply system, unrelated to energy (`supplyMax` equals the base's SUPPLY of 8, while energy capacity is 100) |
| `Tower.efficiency` | no measurable effect on generation |

The proof that the summaries are cosmetic: with `statEnergyGeneration` pinned at
3,000,001 the store still filled at the same rate as it did at GEN 1.
(That rate is the ~1/sec measured in the table above; an earlier version of
this line said ~2/sec, which contradicted its own baseline. The rate is not
the point - UNCHANGED is - but a proof that cites two different numbers for
one measurement is a weaker proof than it needs to be.)

**This was a live bug in CW4DevTools.** InfiniteResources (F7) wrote
`energyStore` and `energyProduction`, so it showed millions of GEN and delivered
no energy whatsoever. Now fixed to raise `commandBase.MAX_AMMO` and keep `ammo`
topped up, restoring the ceiling on release.

### Two traps that cost hours here

**Read ordering.** `DevCommands.Tick()` runs BEFORE the cheats are applied in the
same frame, so any probe reading a value the cheats write sees the PREVIOUS
frame's post-sim value. Several "the write did not stick" conclusions were this,
not the game.

**Units placed by `CreateUnitAtPosition` never join the energy network.** They
can be completed with `CompleteTheBuild(true)` and survive with Indestructible
on, and they still claim no land, so they generate nothing - a tower has to claim
land to produce, and the claim also takes time to grow. Any energy experiment
built on spawned towers measures nothing. Real towers, placed by hand, do
produce (GEN 1 -> 1.6).

## How a unit dies: health is only one of the paths (2026-08-29)

Found because platforms kept being destroyed with CW4DevTools' Indestructible
on, which held `health` at `MAX_HEALTH` every frame.

`UnitManager` carries the per-unit damage model as plain fields (mirrored from
`UnitData.unitConstants`, so they are readable AND writable per instance):

| Field | Type | Meaning |
|---|---|---|
| `impervious` | bool | The game's own indestructibility switch |
| `MAX_HEALTH` / `health` | float | The ordinary damage path |
| `CREEPER_DAMAGES` / `ANTICREEPER_DAMAGES` | bool | Whether fluid hurts this unit |
| `CREEPER_DAMAGES_ONLY_ON_HEIGHT` | bool | Fluid only hurts it above a height |
| `CREEPER_DAMAGE_AMT` | float | How fast fluid hurts it |
| `DESTROY_ON_UNEVEN_TERRAIN` | bool | **Removed outright when the ground under it stops being flat - health is never consulted** |
| `PLAYER_CAN_DESTROY` | bool | Whether the player may scrap it |

`DESTROY_ON_UNEVEN_TERRAIN` is the one that matters, and `Platform` additionally
overrides `DestroyUnit`. Together they mean **a unit can vanish at full health**,
so no health clamp can ever be a complete "indestructible".

Consequence for both mods: to make something unkillable against DAMAGE and
TERRAIN, set `impervious = true` and clear `DESTROY_ON_UNEVEN_TERRAIN` rather than
fighting the health bar. That is not the whole story - see the next section. To
make something killable in a hostile effect, the same fields are the levers -
note that a trap which set `DESTROY_ON_UNEVEN_TERRAIN` would be permanent and
therefore off-limits under the traps design rule (temporary and recoverable
only).

## Nullification is a fourth removal path, and `impervious` does not stop it (2026-08-29)

Recorded here because `Appliers/FinaleLock.cs` and `DevCommands.cs` both cite this
file for these facts and, until 2026-08-31, none of them were in it. A dangling
citation is worse than no citation: the reader concludes the recipe above is
complete.

**Nullifying is not damage.** The obelisk reactors on Founders ship `impervious`
already and are still nullifiable. So the "unkillable" recipe in the section above
- `impervious` plus clearing `DESTROY_ON_UNEVEN_TERRAIN` - leaves a unit fully
removable by a nullifier.

The switch is `UnitManager.CAN_NULLIFY`, and it has two traps:

1. **The sim resets it every tick**, so a write only holds for a frame. Holding it
   means writing every frame.
2. **Worse, the unit leaves `GameSpace.nullifiableUnits` and never comes back
   within the mission.** That is a soft-lock waiting to happen if the unit was an
   objective, and it makes the per-instance nullify counter register a phantom
   nullification, because progress is measured by that set shrinking.

Both are why `CW4DevTools` dropped its per-frame `CAN_NULLIFY` hold, and why the
randomizer's finale lock does NOT write unit state at all. It filters
`Nullifier.GetNullifierTargets` with a Harmony postfix instead: no state is
mutated, nothing can leak into a save, the lock lifts the instant the gate opens,
and the nullify counter is untouched.

`null:protect` in CW4DevTools writes `CAN_NULLIFY` for experiments and touches
`impervious` deliberately NOT at all - these structures are already impervious in
vanilla and still nullifiable, which is how the whole thing was measured.

**There is no hook for nullification.** Nothing on `UnitManager` or `GameSpace` is
named for it, and `Nullifier.FireAtUnit` is private and fires repeatedly while the
beam is up rather than once on success. So nullify PROGRESS is the one counted
objective with no event, and `LocationWatcher` finds it by polling
`nullifiableUnits` about once a second.

**CORRECTION (2026-09-05): a nullified structure is marked by
`UnitManager.IsSuppressed()`, and `nullifiableUnits` NEVER shrinks.** The
sentence above and the one in the CAN_NULLIFY section - "progress is measured by
that set shrinking" - were both wrong, and they cost two failed fixes before
anyone measured it. What was measured, on We Were Never Alone:

| | all 9 nullified (loaded save) | fresh start |
|---|---|---|
| `nullifiableUnits.Count` | 9 | 9 |
| units alive in the scene | 9 | 9 |
| `IsSuppressed()` | **true** on all 9 | **false** on all 9 |
| `dead` / `_enabled` / `CAN_NULLIFY` / `health` | identical | identical |
| `IsMissionObjectiveComplete(0)` | true | false |

Nullifying neither removes the unit from the set nor destroys it, so ANY rule
that counts what is left reports zero forever - which is why no nullify check
had ever been sent by the poll, on a fresh mission or a resumed one. Count the
SUPPRESSED units instead (`NullifyRules`), and keep
`IsMissionObjectiveComplete` as a second source, since it is the only signal
that survives a reload.

Reproduce it with the debug plugin: `null:dump` prints per-unit state for every
nullify target, and `loadsave:<storyN>` reaches the resumed case that `boot:`
cannot.

One more measured oddity from the same work, also cited by `LocationWatcher` and
also missing here: **`MissionObjectiveData.enabled` and `count` are unreliable.**
Farsite's Collect slot reads `enabled=False` with `count=0` while two collectable
caches sit on the map, and on a real cache pickup the Collect objective flipped to
DONE while its `count` never moved off 0. Use the live sets (`mustCollect`,
`gs.totems`, `nullifiableUnits`), not the objective fields.


`UnitConstants` also carries `IMPERVIOUS` as a per-TYPE default; the per-instance
`UnitManager.impervious` is what actually gates damage at runtime.

**How to re-derive all of this:** `CW4DevTools`, Home key (or
`DumpUnitsOnStart`). It logs the full `unitConstants` registry, every
`<Name>BuildGhost`, and each unit on the map with its type, data name and
whether the player filter accepts it.

## Open questions (2026-08-24) - ALL ANSWERED, kept for the record

Every question below has since been answered: per-mission unit requirements by the
manual playthrough (`docs/design/mission-requirements-worksheet.md`), the ERN
representation by `Core/ErnRules.cs` and `Appliers/ErnGranter.cs`, and the three
questions that outlived those by
`docs/design/2026-08-31-open-questions-worksheet.md`, whose title is "ANSWERED,
all three closed". Do not treat this section as a to-do list.

- Which campaign missions require which units (needed for AP logic spheres).
- Totems and pre-placed structures are outside `SetUnitCanBuild`; deferred.
- ERN portal as unlock plus individual ERNs as progressive items: needs
  investigation of how ERNs are represented.

## Probe result (2026-08-24): VALIDATED

A throwaway BepInEx plugin (`cw4-probe/`) proved the core mechanism live:

- Whitelisted riftLab/tower/pylon/cannon, locked all other 22 types, enforced
  every frame in `MonoBehaviour.Update`.
- On a late story mission (full tech tree normally), the build pane collapsed
  to exactly the whitelist. Locking works.
- Cannon was forced available by code and appeared in the weapons tab even
  where the mission would provide it - forcing units ON works too.
- Zero errors; mission scripts could not fight the plugin.

### Key API surface (interop `Assembly-CSharp.dll`)

- `GameSpace.instance` (static) -> `.buildUnitManager` -> `BuildUnitManager`
- **26 availability flags** (not just the 15 4RPL exposes):
  riftLab factory ernPortal tower pylon miner greenarRefinery terp porter
  cannon mortar sprayer sniper missileLauncher nullifier runway bomberPad
  acBomberPad rocketPad platform shield microRift chronat airship bertha
  sweeper - each as `<name>Available` bool property.
  The extra 11 are the special per-mission units (bertha, airship, bombers...)
  which means they can be items too.
- `BuildUnitManager.SetBuildCountLimit(string, int)` / `GetBuildCountLimit` -
  build limits as progressive items confirmed possible.
- `UnitBuildPane.Refresh()` (find via `FindObjectOfType`) rebuilds the pane;
  the pane is sectioned (structures vs weapons tabs).
- `GameSpace.editMode` (static) - skip the map editor.
- New map load detected by comparing `GameSpace.instance.Pointer`.

### Build notes

- .NET SDK 10.0.400 builds the `net6.0` plugin fine.
- References: `BepInEx/core/{BepInEx.Core,BepInEx.Unity.IL2CPP,Il2CppInterop.Runtime}.dll`
  plus `BepInEx/interop/{Assembly-CSharp,Il2Cppmscorlib,UnityEngine.CoreModule}.dll`.
- Injected MonoBehaviour needs `ClassInjector.RegisterTypeInIl2Cpp<T>()` and a
  `public T(IntPtr) : base(ptr)` constructor.
- csproj has an AfterTargets=Build copy straight into `BepInEx/plugins`.
- Game ships `Mirror.dll` (MVerse is open-source Mirror) and `BestHTTP.dll`.

### Deployment target

End state must be: download a zip, extract into the game folder, done.
BepInEx installs are pure file-copy, so ship BepInEx + plugin together.
First run generates interop assemblies automatically (takes ~1 min).

## Probe v0.11 (2026-08-24): programmatic mission launch VALIDATED

Full hands-free chain works: game launch -> autoboot command -> mission
running with locks enforced. Zero UI interaction.

### The correct launch API

    GameSpace.specifierToApply = "story7";  // also titleToApply, guidToApply=""
    LoadingScreen.LoadGame("story7", true, false, GameSpace.CATEGORY.FARSITE, -1);

- Story missions are internally "story1".."story20" (tutorial not counted;
  story7 = "Hints"). Mission title mapping observable via OnLaunch hook.
- Scene flow: GameLoad (init) -> Galaxy (menu) -> LoadingScreen -> Game.
- LoadingScreen has its own statics (fileToLoad/embeddedLoad/category/...)
  and an async load coroutine; LoadGame() is the static entry the UI uses.
- Booting on Galaxy's first frame crashes natively; waiting ~10s after the
  Galaxy scene arrives works. Readiness signal TBD.

### Crash lessons (cost several hours - do not repeat)

1. NEVER call SceneManager.LoadScene("Game") directly with GameSpace statics
   set - silent native crash during world creation. Use LoadingScreen.LoadGame.
2. NEVER Harmony-patch BuildUnitManager.ReadData - calling the 26 availability
   setters during map deserialization kills the process with no managed
   exception, no crash log. This broke ALL mission loads (manual included).
3. Harmony prefixes on UnitBuildPane.OnEnable/Start throw NullReferenceException
   spam from the DMD at boot. Unity lifecycle methods on this type do not
   detour cleanly. Patching GalaxyMissionPanel.OnLaunch/OnPlay works fine.
4. Diagnostic that cracked it: manual launch crashing too = the passive hook,
   not the launch path, was guilty.

### Build pane behavior (confirmed)

- BuildButton visibility is DYNAMIC (each button re-checks flags per frame):
  live LOCKING works instantly.
- Button CREATION is static at pane build: live UNLOCKING needs a pane
  rebuild - UnitBuildPane.Refresh() is NOT enough. **ANSWERED below:
  `UnitBuildPane.SetEnabledButtons()`, which CREATES missing buttons.** Was: TBD
  (candidates: SetEnabledButtons, gameObject toggle, Show; test via pane:
  commands in probe v0.11).
- Units available at pane-creation time show correctly (mortar test passed
  after mission restart).

### Probe file-command protocol (BepInEx/probe-unlocks.txt)

  <unitname>      add unit to whitelist live
  lock:<unit>     remove unit from whitelist live
  reset           restore default whitelist
  load:<name>     launch via GalaxyMissionPanel.OnLaunch (NRE from main menu)
  boot:<name>     launch via LoadingScreen.LoadGame (works)
  autoboot:<name> queue boot for 10s after Galaxy scene arrives
  pane:<cmd>      refresh|setenabled|onenable|start|toggle|show experiments
  dump            log every BuildButton state

### Launch/focus notes

- Game window spawned from background shell does not take focus; fix with
  WScript.Shell AppActivate('Creeper World 4') ~12s after launch.
- The launching shell exits immediately (Steam-style detach); watch the
  process list or BepInEx log, not the launcher exit.

## Crash investigation round 2 (2026-08-24 late)

The v0.11 "success" report was premature: the mission reached StartMission
(Player.log: "Unpersist time: NN" then "StartMission") then the process died.
Manual launches crashed identically, so the boot path was NOT the cause.
Quarantining the story7 autosave did not help either.

**Real evidence from minidumps** (C:/Users/droha/AppData/Local/CrashDumps,
parsed with python `minidump` package): 7 of 8 crashes tonight are
EXCEPTION_STACK_OVERFLOW with ~19,900 return addresses into a single
non-module JIT-code region, coreclr.dll/clrjit.dll frames present. That is a
managed method (our plugin is the only managed code in-process) recursing to
stack exhaustion during StartMission.

Binary-search in progress: v0.12 = v0.11 minus ALL Harmony patching.

Analysis recipe for future crashes:
    py -3.13 -m pip install minidump
    MinidumpFile.parse(dump) -> .exception.exception_records[0]
    scan crashing thread stack for values inside module ranges;
    heavy repetition of one non-module region = managed recursion.

## RESOLUTION (2026-08-24 ~22:00): all probe goals achieved

### The crash: cause isolated to code structure

Byte-exact v0.3 redeploy WORKED while bisect variants crashed. The only
actively-executing diff in the crashing builds: the whitelist was made a
static field and a static ApplyTo() method was added ON THE IL2CPP-INJECTED
MonoBehaviour class. Fix that ended the crashes: extract ALL state and logic
into a plain (non-injected) static class ProbeCore; the injected
ProbeBehaviour is a one-line Update() shim.

**RULE: IL2CPP-injected classes get an (IntPtr) ctor and Unity messages,
NOTHING else. No static state, no helper methods, no logic.**
(Mechanism unconfirmed - correlation was decisive across 8+ runs. Crash
signature: EXCEPTION_STACK_OVERFLOW, ~20k frames in one JIT region, dies at
StartMission during mission load.)

Cleared suspects: LoadingScreen.LoadGame (fine), autosave corruption (red
herring), scene tracking per frame (fine), Harmony patches on
GalaxyMissionPanel (fine).

### Live unlock: SOLVED including button creation

- Locking: flags checked per frame by BuildButton - instant.
- Unlocking mid-mission: set flag, then UnitBuildPane.SetEnabledButtons()
  CREATES missing buttons, then Refresh(). Confirmed visually: mortar
  removed and re-added live, mid-mission, no restart.
- GetBuildButtons() returns only the ACTIVE TAB's buttons (structures vs
  weapons are separate button sets).

### No-flash (v0.15)

Hide pane GameObject at first sight in a mission, wait 60 frames while
enforcement clamps the mission's own grants, SetEnabledButtons + Refresh +
SetActive(true). Avoids the crash-prone lifecycle/deserialization hooks.

### Autoboot timing

Game boot to menu ~20-30s (unavoidable). Our settle delay at Galaxy before
LoadGame: 10s, conservative, shrinkable later; only needed for automated
testing - real players launch from the (AP-gated) galaxy UI.

## FINAL PROBE STATE (v0.16) - everything verified

- There are FIVE UnitBuildPane instances per mission: StructUnitBuildPane,
  WeaponUnitBuildPane, AirUnitBuildPane, SpecialUnitBuildPane,
  CustomUnitBuildPane. ALL pane operations must iterate all of them
  (Resources.FindObjectsOfTypeAll). The v0.15 miner/refinery leak was caused
  by rebuilding only all[0].
- flags diagnostic (actual vs wanted per unit): all 26 matched, no write
  battle from mission scripts - per-frame enforcement wins cleanly.
- Visual verification is self-service: PowerShell CopyFromScreen screenshot
  (scratchpad/screenshot.ps1) + Read the PNG. No human eyes needed for pane
  checks.
- Verified via screenshot: mission start shows ONLY whitelisted units.

### Probe command protocol v0.16 (BepInEx/probe-unlocks.txt)
  <unit> | lock:<unit> | reset | boot:<name> | autoboot:<name>
  pane:refresh|setenabled|toggle|show | dump | flags

### What the real mod inherits from the probe
- ProbeCore structure (thin injected shim + plain core class)
- Whitelist enforcement loop + AllPanes rebuild for live item delivery
- LoadingScreen.LoadGame for mission gating/launch
- The AP client replaces the file-command channel with the websocket

## UI refresh recipe - FINAL (v0.20, user-verified)

**SUPERSEDED - see "Correction to the shared button strip theory" below, which
overturns the central claim of this section. The heading says FINAL and
user-verified; it is neither on this point.**

The five UnitBuildPanes share one physical button strip, managed by LeftPane
(fields: structUnitBuildPane..customUnitBuildPane, structTab..customTab
toggles, RefreshUnitBuildPanes(), PickActiveTab(), HideAll()).

- On mission reveal (after no-flash hide): SetActive(true) on all panes,
  LeftPane.RefreshUnitBuildPanes(), then force a REAL tab-change cycle
  (weaponTab.isOn=true; structTab.isOn=true). PickActiveTab alone does NOT
  rebuild the strip when the default toggle is already on (no change event),
  which left the weapon pane's buttons visible on the struct tab.
- On live item change: LeftPane.RefreshUnitBuildPanes() ONLY. Never
  PickActiveTab - it yanks the player's selected tab.
- Only unit changes trigger a refresh; tab/ada commands must not.
- ADA log dismissal: Resources.FindObjectsOfTypeAll<ADAMessageLog>()[0].Close().
- Tab switching: LeftPane.<name>Tab.isOn = true.
- Autoboot settle delay: 4s after Galaxy scene is stable (10s was overkill).
  TODO: replace timer with a menu-readiness signal.

Verified end-to-end by automated screenshot sweep (scratchpad screenshots) and
by the user: initial load clean, live unlock lands in correct tab, no leaks,
no tab yank, no flash.

## CORRECTION + full test matrix (v0.21-v0.23)

**Correction to the "shared button strip" theory:** wrong. The five
UnitBuildPanes are separate stacked GameObjects in the same screen space; the
game keeps exactly ONE active (the selected tab's). Panes with zero buttons
render nothing even when active, which masked the bug: our reveal had
activated all five, so as soon as other panes gained buttons they drew on top
of the struct tab (and produced overlapping-button artifacts). Invariant to
maintain: after any pane manipulation, SetActive(true) only on the pane
matching the selected tab toggle (ResyncStrip in probe v0.23).

### Test matrix results (probe v0.21-v0.23, all verified)

- All 26 availability flags: unlocked live; buttons appear on correct tabs
  (struct: Tower/SuperTower/Miner/Reactor?/GreenarRefinery/Terp/DeliveryPad...,
  weapon: Cannon/Mortar/Sprayer/Sniper/MissileLauncher/Nullifier, air:
  Runway/BomberPad/ACBomberPad/RocketPad+custom, special:
  Platform/Shield/Microrift/Beacon(Chronat)/Bertha/Sweeper). Extra buildables
  exist beyond the 26 flags: Reactor, DeliveryPad, and mission-embedded CPACK
  units with GUID names - relevant to item pool design.
- Mixed enable/disable combinations: pane counts track exactly, flags never
  fought by mission scripts (flags diagnostic clean).
- Build limits: BuildUnitManager.SetBuildCountLimit/GetBuildCountLimit work -
  unit names must be LOWERCASE ('tower', 'cannon'; 'Tower' fails silently,
  readback -1). UI badge + behavioral cap still to verify in unpaused play.
  UPDATE 2026-09-01: setting a limit works and is enforced, but RAISING one does
  not, because every building's base limit IS -1 (unlimited). UnitGate skips
  those on purpose - base+1 over unlimited would cap a unit that had no cap - so
  `Build Limit +1` items are inert everywhere and are no longer generated. Note
  the readback value -1 is overloaded here: it is both the "wrong case" failure
  above and the legitimate "unlimited" answer, which is why the two took a while
  to tell apart.
- Mission gating: allowed-set + Harmony prefix on GalaxyMissionPanel.OnLaunch
  returning false blocks launch; BootMission also gated. Verified:
  boot:story2 denied while story1/story7 allowed.
- LOCATION TRIGGERS (the AP send side): World (via GameSpace.instance.world)
  exposes missionObjectives[] (customName, required, complete),
  IsMissionObjectiveComplete(i), IsMissionComplete(),
  AcquireMissionObjective(i, showPopup). Per-frame polling detects
  transitions reliably; objective popup + rift-jump availability confirmed
  in-game. Programmatic 'win' (acquire all) works -> MISSION COMPLETE fires.

### Probe commands added in v0.21+

  limit:<unit>:<n>   set build count limit (lowercase names)
  missions:<csv>|all mission launch gate
  objective:<n>      acquire objective n
  objdump            list objectives with required/complete
  win                acquire all objectives

### Corrections and panel-flag results (v0.23 final)

- 'Reactor' and 'DeliveryPad' are INTERNAL names for the MINER and PORTER
  buttons (display names differ). No extra buildables beyond the 26 flags
  plus mission-embedded CPACK units. Struct tab = exactly 6 buttons fully
  unlocked; no scroll overflow.
  **RECONCILED 2026-08-31** - this is about BUTTON object names and it is
  correct; the `miner -> Collector` row near the top of this file is about UNIT
  names and is also correct. See "A fourth name space", which adds
  `SuperTowerButton` for PYLON and settles the porter.
- factory and ernportal do not use pane buttons: each has a DEDICATED panel
  next to the build pane (Factory ware rows / ERN PORT avail+buried).
  Verified live in both directions: locking removes the panel, unlocking
  restores it, no restart, no artifacts.

Remaining to verify in actual (unpaused) play, one session covers all:
build-limit cap enforcement while building, mission gating via a real galaxy
click, victory/depart sequence and gameComplete transition.

### Real-play validation (user beat story7 with probe active)

Full authentic sequence captured: 4 individual objective triggers in
completion order (2 optional + 2 main), MISSION COMPLETE on the required
pair, then SCENE Game->Galaxy on rift jump (depart detectable). Whitelist
state persisted across missions (story7 -> story6) - correct AP semantics.
Note: the mission gate and build limits are in-memory plugin state and reset
on game restart; the real mod repopulates them from the AP server on connect.

## COMPLETE: full behavioral validation (2026-08-24 end of session)

User-verified in real gameplay:
- Build limit cap ENFORCED: limit:tower:3 showed a badge and the game refused
  the 4th tower.
- Mission gate blocks the REAL UI path: restart-mission clicks on story6
  denied 5/5 via the GalaxyMissionPanel.OnLaunch Harmony prefix.
- Victory flow: 4 objectives triggered individually in real play, MISSION
  COMPLETE fired, rift jump detected as SCENE Game->Galaxy.

Gaps found by user testing (real-mod TODO, not probe scope):
1. GATE BYPASS: loading an existing SAVE of a gated mission skips OnLaunch -
   must also gate the save-load path (MissionPanelLoadBox) or clear stale
   saves per AP slot.
2. Gated missions should be GREYED OUT on the galaxy map, not just refuse to
   launch (StorySelectionPanel / mission marker UI work).
3. Reveal ordering bug fixed in v0.24 (queued): panes must be active during
   refresh or buttons never build; enforce single-active only afterwards.

PROBE COMPLETE. Every mechanism the Archipelago mod needs is proven:
unlocks (26 units + factory/ernportal panels), live delivery both directions,
build limits, mission gating, location triggers (objectives + completion +
depart), programmatic mission launch, full state persistence across missions.
Next milestones: AP websocket client in the plugin, galaxy UI lock styling,
save-path gating, Python apworld with mission/unit logic spheres.

## Blank struct tab: SOLVED (2026-08-25, probe v0.30, battery 13/13)

The blank-pane-on-mission-entry bug had THREE stacked root causes:

1. **Tab toggles are NOT in a Unity ToggleGroup.** The game's click handler
   manages exclusivity. Setting toggle.isOn from code leaves multiple toggles
   true and fires no exclusivity logic. Fix: clear ALL five toggles to false,
   then set the target true (a real false->true change event).
2. **Il2Cpp wrapper reference equality is ALWAYS false.** Every interop
   property access returns a fresh wrapper, so `paneA == paneB` never matches
   even for the same native object - our single-active enforcement was
   deactivating every pane including the target. Fix: compare `.Pointer`.
3. **The game hides the whole pane container while the ADA log is open**, so
   `activeInHierarchy` reads false during mission intros regardless of our
   state. Fix: verify with `activeSelf`.

Plus robustness: the reveal is now a multi-frame state machine (activate ->
refresh -> toggle-cycle -> single-active resync -> VERIFY -> retry up to 5x),
and all UI objects resolve through `GameSpace.instance.leftPane` (Resources
scans can return destroyed instances from the previous mission after a
LoadGame transition - liveness-check everything).

**Regression battery** (scratchpad/battery2.sh): 13/13 pass - three mission
entries including mission->mission transitions, four whitelist combos, limit
readbacks, state persistence across missions, flag-fight scan, zero errors.
Log-marker sequencing per boot (the log truncates on game relaunch - reset
markers when the file shrinks).

## Full-campaign sweep (2026-08-25, probe v0.30): 20/20 CLEAN

Booted story1..story20 back-to-back in one session. Every mission:
reveal=OK, struct pane active+visible with exact whitelist (2 buttons),
ZERO flag fights - no story mission script contests the whitelist anywhere
in the campaign. The plugin has uncontested authority over unit
availability in all 20 missions.

Objective slots per mission: always 6. Required counts:
  story1:1 story2:3 story3:2 story4:2 story5:2 story6:1 story7:2 story8:1
  story9:2 story10:2 story11:2 story12:2 story13:2 story14:2 story15:3
  story16:2 story17:1 story18:1 story19:2 story20:4
(Locations table seed: enabled-objective flags per mission still to capture;
MissionObjectiveData.enabled exists.)

Still untested (need human clicks or future battery):
- Resume-from-save path (saves serialize BuildUnitManager state; also the
  known gate bypass)
- Pause-menu restart path (last exercised pre-reveal-machine)
- limit:0 semantics (unit owned but unbuildable - possible AP item design)

## Pane system: CLOSED (2026-08-25, probe v0.31)

- v0.31 hides panes at Game SCENE ENTRY (before GameSpace.instance exists),
  which kills the resume-from-save flash of stale serialized flags. Reveal
  delay halved to 30 frames.
- Regression battery 13/13 on v0.31.
- User-verified: fresh boots clean, restarts clean, resume-from-save clean
  (brief blank, then correct whitelist - no stale flash).
- limit:0 rejected by design (unintuitive); build limits are >=1 or absent.

Pane/unlock system is DONE. Next: Archipelago websocket client.

## Mission-select tracker research (2026-08-25, probe v0.32-v0.40)

### Campaign facts
- Full campaign = story0 (tutorial) + story1..story20; STORY PLANET_COUNT=20.
- SPAN experiments not in embedded story assets; CATEGORY.SPAN exists (stretch).

### Save-load gate CLOSED
Harmony prefix on MissionPanelLoadBoxRow.OnLoad reading
missionPanelLoadBox.specifier - same gate check as OnLaunch. (v0.32)

### Programmatic navigation
GameGalaxy.instance.farsiteButton -> GetComponent<Button>().onClick.Invoke()
opens the story sector screen from the main menu ("story:open").

### Mission-select anatomy (Sector: Farsite screen)
- StorySector: planets carousel; 20 'Planet (n)' bare meshes
  (StoryPlanetMaterial n), objectives array = 6 Image slots for the DETAIL
  panel (sprites Icon_Magic1/Money1/PieChart/Time/Diamond/Terror), all
  Image.color tintable.
- Overview per-planet markers = 63 SpanNetworkPlanetObjective instances
  (**STALE: counted with `FindObjectsOfType`, which skips inactive objects, and
  the mod deactivates every locked planet's marker container. The measured
  per-mission set in `MissionObjectivesTests` sums to 66.**)
  (fields: objective type int, complete bool) under 'Objectives'; each a
  quad mesh. complete=true -> green material, false -> white material
  (SpanNetworkPlanetComplete<N>Material, Shader Forge/
  SimpleTextureTransparent, NO color property - color baked in texture,
  game swaps whole materials).
- GalaxyPlanet (colonies) has native STATUS enum
  {NONE,LOCKED,UNLOCKED,PARTIAL,COMPLETE} + 6 status materials - not used by
  story planets but proves the game's design language.

### Tracker recolor: PROVEN with caveat - SUPERSEDED, see "TRACKER VISUALS" below

The shader-swap and texture-carrying workaround described here was replaced by a
one-line `material.SetColor("_color", c)`, which is what `TrackerView` does.
Setting complete=false + swapping instance material shader to
Sprites/Default + .color => arbitrary colors (red/yellow/grey/blue shown
live on the map). Caveat: renders as solid squares - glyph texture sits in
a custom ShaderForge property; fix = carry texture across the swap
(GetTexture->SetTexture _MainTex) or pre-tint copies of the white texture.

### material.color lessons
- Marker/planet materials ignore Unity's `.color` SHORTHAND (which resolves
  `_Color`) - silent no-op. **This does NOT mean they have no colour property:**
  the marker property is `_color`, lowercase, and setting it works. See the next
  section. The original wording ("no such property") led to the opposite
  conclusion.
- The game repaints marker state per frame; one-shot flips of 'complete'
  do show, but material swaps happen on state change only.

## TRACKER VISUALS: SOLVED (2026-08-25, probe v0.42)

**Colored objective glyphs on the mission map, shapes intact, applied live:**
marker material property is `_color` (LOWERCASE - `_Color` misses silently).
`GetComponent<MeshRenderer>().material.SetColor("_color", c)` per
SpanNetworkPlanetObjective. Glyph texture lives in `_MainTexture`.
Verified on-screen: red skull, yellow I, blue I, grey X with all else green.

- Shader property discovery: shader.GetPropertyCount()/GetPropertyName(i)/
  GetPropertyType(i) - enumerate, never guess (case-sensitive).
- Connector lines: 19x 'SpanNetworkPlanetLine(Clone)' LineRenderers under
  'Lines'; startColor/endColor settable (currently green 0,0.859,0.255).
- Planet mesh shader = AmplifyStandard: NO color property; has _Contrast,
  _Emission, _Smoothness ranges (dimming candidates), textures per material.
  Planet greying still open (contrast/emission experiments, or overlay).
  **ANSWERED below: `GameObject/LockedPlanet` is the game's own locked visual,
  driven by `forceUnlocked`. No shader work was needed.**
- Planet rings: not yet located (not sprites, not lines, not planet children).
  **ANSWERED below: `SelectionIndicator` and `CompletionIndicator`, both
  inactive children of the planet.**
- Live updates confirmed: all marker changes render same-frame with the
  page open; camera pan/zoom tracks correctly (world-space quads).

## Save archiving: Steam Cloud PIVOT

Archiving mcs.dat fails: Steam Cloud restores it on next launch (observed,
fresh timestamp). Design pivot: the mod OWNS the mission-select display -
AP state drives every marker color, planet state, and completeText,
regardless of mcs.dat contents; launch + save-load gates handle behavior.
No file fights, no user data loss. (If a truly clean page is ever needed,
in-game MCS deletion APIs exist - GetMCSEntries/DeleteMCSEntry - but
display-ownership makes it unnecessary.)

## MISSION MAP: COMPLETE ANATOMY (2026-08-25, probe v0.46)

The story map planets are 'SpanNetworkPlanet (0..19)' objects (the SPAN
network prefab system - story and SPAN experiments SHARE this UI; stretch
goal inherits everything). NOT StorySector.planets (that is a different,
non-map planet set - all earlier planet tints hit the wrong objects).

Per-planet subtree:
  SpanNetworkPlanet (n)   [SpanNetworkPlanet component, SphereCollider]
    Lines/SpanNetworkPlanetLine(Clone)   LineRenderer connector
    GameObject/LockedPlanet [inactive]   NATIVE locked-planet visual
                                          (SpanNetworkPlanetLockedMaterial)
    Planet                                sphere (SpanNetworkPlanet2Material)
    Title                                 TextMeshPro, color settable
    SelectionIndicator [inactive]         ring, SelectedMaterial (_color)
    CompletionIndicator [inactive]        ring, CompleteMaterial
    Objectives/SNPO(Clone) x N            objective markers

SpanNetworkPlanet API (all native):
- forceUnlocked bool, lockedPlanet, planet, title, completionIndicator,
  selectedIndicator transforms/objects
- activeLineColor0/1, inactiveLineColor0/1 (line state colors)
- completionBronze/Silver/GoldColor, incomplete/completeMaterial
- planetGUID, connectedPlanetGUIDS (the map graph)
- Refresh() - repaint this planet
- FakeIsMissionObjectiveComplete(guid, obj) - DISPLAY-TIME completion query.
  ** Harmony-patch this to answer from AP state and the whole map renders
  itself natively; call Refresh() per planet for live updates. **

Line colors verified live (red/grey shown). Save archiving verified:
**SUPERSEDED: the mcs.dat rewrite below was abandoned.** Steam Cloud restores
the file, and the mod now owns the map's display from AP state and leaves
mcs.dat alone (`SaveArchiver`: "mcs.dat is left untouched"). Only the
`saves/farsite` move shipped. `grep xtory` finds nothing in the repo. Original
text: byte-rename storyN->xtoryN inside mcs.dat (same length, content rewrite -
Steam Cloud syncs it instead of restoring), saves/farsite move works
(NOT cloud-restored), full round-trip proven including the game's native
fresh-campaign "?" display on clean state.

## Main menu editing + AP login panel (2026-08-25, probe v0.47-v0.49)

- Hiding menu buttons: GameGalaxy.instance has named GameObject refs
  (chronomButton, markVButton, coloniesButton, editorButton, farsiteButton,
  spanButton, recordingsButton...) - SetActive(false) works instantly.
  Verified: menu shows only FARSITE EXPEDITION + SPAN EXPERIMENTS.
- SPAN card shows "1 / 26" - confirms 26 SPAN missions for the stretch goal.
- Custom floating UI: parent a panel to the game's own root canvas
  (FindObjectsOfType<Canvas>, first isRootCanvas - 'AchievementCanvas' at
  menu). Text MUST be TextMeshProUGUI with a TMP_FontAsset borrowed via
  Resources.FindObjectsOfTypeAll<TMP_FontAsset>() - legacy UI.Text +
  Font.CreateDynamicFontFromOSFont throws interop constraint errors.
  A standalone ScreenSpaceOverlay canvas created from scratch did NOT render;
  the game-canvas parent works.
- Mock ARCHIPELAGO login panel rendered: server box, slot, password,
  CONNECT button, auto-connect, status line. Wiring = real-mod work
  (TMP_InputField for editing, Button.onClick -> AP client).

Screenshot lesson: verify CW4 actually took focus before CopyFromScreen -
AppActivate can fail and capture unrelated windows; delete such captures
immediately.

## AP login panel: INTERACTIVE (probe v0.50-v0.53, user-verified)

Full input functionality works in custom UI: typing, focus, caret,
selection, delete, copy/paste, clickable button updating status text,
password masking (TMP_InputField.ContentType.Password).

Construction rules for TMP_InputField in IL2CPP:
1. Parent to the game's root canvas (own overlay canvas won't render).
2. Text = TextMeshProUGUI + borrowed TMP_FontAsset.
3. Build the field on an INACTIVE GameObject and SetActive(true) only after
   textViewport/textComponent/placeholder are assigned - otherwise Awake
   runs unwired and the caret never renders.
4. Button wiring: onClick.AddListener((UnityEngine.Events.UnityAction)Method).

## Collect objective: how a cache pickup actually works (2026-08-31, story2)

**Supersedes the section below, which had it wrong.** A real hands-on pickup in
"Home" settled it. Before / after collecting the one cache:

| | before | after a REAL pickup |
|---|---|---|
| `GameSpace.mustCollect` / `maxMustCollect` | 1/1 | **0/1** |
| Collect objective (slot 4) complete? | open | **DONE** |
| Collect objective (slot 4) `count` | 0 | **0** (unchanged) |
| `InfoCache` units on the map | 1 | **0** (unit destroyed) |
| `cachePokes` (did InfoCache.Retrieved fire?) | 0 | **0** |
| `LOCATION CHECK: Home - Cache 1` | - | sent, exactly once |

Four things follow, and three of them corrected an earlier belief:

1. **`mustCollect` shrinking IS the pickup signal.** `LocationWatcher` reads it
   correctly, and the check fired end to end with no double-send from the patch
   plus the safety poll.
2. **The objective's `count` is NOT the collect tally.** It read 0 both before
   and after, while the objective itself flipped to DONE and the HUD showed 1/1.
   Do not use `MissionObjectiveData.count` for Collect progress.
3. **A collected cache is DESTROYED**, not flagged. The `InfoCache` count went to
   zero, so anything reading `retrieved` after the fact reads nothing.
4. **`InfoCache.Retrieved` is never called on the pickup path.** `cachePokes`
   stayed at 0 through a real collection, so that patch (then
   `CacheRetrievedPatch`, now `CacheDestroyedPatch` after the hook moved) had never
   fired once in play and the once-a-second poll had been doing all of the work,
   silently. The hook is now a postfix on `InfoCache.DestroyUnit`, which is what a
   pickup actually does.

That last one is the lesson worth keeping: the patch was APPLIED (the log said
so), it was on a real method with a plausible name, and it did nothing. Only a
counter on the postfix plus a human collecting a cache could tell the difference.
Every event hook in the mod now has such a counter, reported by `perf`.

### Earlier, incomplete version of the above (kept for the reasoning)

Measured while trying to test the cache branch of `LocationWatcher` without
playing the mission. Calling `InfoCache.Retrieved()` on a live cache in "Home":

| | before | after Retrieved() |
|---|---|---|
| `InfoCache.retrieved` | 0 | **1** |
| `GameSpace.mustCollect` / `maxMustCollect` | 1/1 | 1/1 |
| Collect objective (slot 4) `count` | 0 | 0 |
| Collect objective complete? | open | open |

So `Retrieved` sets that cache's own flag and moves nothing else. It is the
message/lore reveal, not the collection. Three consequences:

1. WRONG: "`CacheRetrievedPatch` is a hint, not the signal." It coincides with
   nothing - measured above, `Retrieved` is not called on a pickup at all. The
   guess that "the message pops when a cache is collected, so the hook very
   likely coincides with a pickup" was reasonable-sounding, which is exactly what
   made it dangerous.
2. SUPERSEDED: "`cache:take` cannot simulate a pickup." That command has since
   been REMOVED, not merely superseded. Replaced by
   `cache:destroy`, which drives `DestroyUnit` - the effect a pickup has.
3. **The cache path needs a hands-on pickup to confirm end to end.** This one
   held, and it is what caught the error: synthetic mouse input does not reach
   CW4's UI, so no script here can close this branch. `tools/cache-handtest.sh`
   sets up everything either side of the pickup and watches for the check.

Two live signals were expected to agree: `maxMustCollect - mustCollect.Count`
(what `LocationWatcher` uses) and the Collect objective's own
`MissionObjectiveData.count`. They do not - `count` never moves. `counts:dump`
prints both plus every objective's enabled/count/complete state, which is how
that was caught.

## Objective markers: layout, re-texturing, and Refresh's duplicates (2026-08-31)

Measured to make a planet's icon set match its actual checks. Four facts, all
from `glyphs:dump` (which now prints name, activeSelf, localPosition, localScale,
material and `_MainTexture` per marker) and `diag:refresh`.

**1. The layout is one rule, and there is no layout component.** Markers are
world-space quads under `Objectives`. Every planet on the map, without exception:

    marker k:  localPosition = (0.55 * k, 0, 0)    localScale = (0.7, 0.7, 0.7)

k is the ordinal position in the row, NOT the objective index, and the row runs
left to right in ascending objective index. So an added marker can be placed
exactly where the game would have put it - but the position must be written,
because a clone inherits the SOURCE marker's position (a clone of a 4th-in-row
donor landed at x=1.65).

**2. Writing `objective` re-textures the marker.** This is the useful one:

    tex before='ObjTotem'  afterObjectiveWrite='ObjCollect'

`SpanNetworkPlanetObjective` has no Awake, Start or Update, so the property
setter is doing it - it swaps in `SpanNetworkPlanetComplete{index}Material`,
whose texture is named for the objective TYPE at that index - 0 `ObjNullify`,
1 `ObjTotem`, 2 `ObjReclaim`, 4 `ObjCollect`, 5 `ObjCustom`. Index 3 (Hold) was
never observed because no campaign mission draws a Hold marker; do not read the
list as five consecutive indices. Superseded wording listed:
`ObjNullify` / `ObjTotem` / `ObjReclaim` / `ObjCollect` /
`ObjCustom` by index. Re-pointing an icon therefore needs no donor marker, no
prefab lookup and no material copying. (`spanNetworkPlanetObjectivePrefab` exists
in the game but its owning type was never confirmed, and this makes it moot.)

**3. `SpanNetworkPlanet.Refresh` APPENDS objective markers and never clears the
container.** Consecutive calls on Farsite: 3 -> 4 -> 5 children. Every Refresh
leaves another exact copy of the authored set stacked on the previous one. They
overlap perfectly so nothing looks wrong, which is why this went unnoticed - the
mod calls Refresh whenever a planet's `forceUnlocked` flips. `TrackerView`'s
reconcile hides the surplus, so the duplication is now absorbed rather than
accumulating visibly.

Do not assume Refresh is a rebuild. It is not; `DestroyChildren` exists on the
type but Refresh evidently does not call it for this container.

**4. `FindObjectsOfType` cannot see most markers.** It skips inactive objects,
and the mod deactivates the whole `objectiveContainer` of every locked planet -
so on a fresh slot almost every marker is invisible to it. The first version of
the donor search reported "no donor with objective=4" on a map that has fourteen
of them. Walk the planets and use
`GetComponentsInChildren<SpanNetworkPlanetObjective>(true)`.

### What this fixed

Farsite's marker set is the one that disagrees with its mission (19 of 20 agree,
tabulated 2026-08-31): the map draws a Totems icon, the mission has no totems,
and its two caches and custom objective had no icon at all. The map now shows
Collect then Custom, spaced like every other planet - verified from a screenshot,
and with a forced four-icon case to exercise the add path.

**It must not need a connection.** The first version drove the icon set purely
from the AP location list, and that list is empty until a server sends one - so
opening the game unconnected still showed vanilla's totems icon on Farsite, which
the map leaves unlocked because it is the default starter. Which objectives are
CHECKS is a per-seed question; which objectives a mission HAS is not.
`MissionRules.MissionObjectives` holds the latter, measured, and
`ExpectedObjectiveIndices` falls back to it when no locations are known. Locations
still win once the server has spoken, since a seed may exclude some.

The reconcile is driven by `MissionRules.ExpectedObjectiveIndices`, so it is
generic rather than a special case for mission 1: a no-op wherever the game
already agrees, and self-correcting if a game update changes the map data. It
runs from `Paint`, i.e. on map open, planet refresh and Archipelago state change.
Measured idle on an open map: 1,250 frames with the Refresh, paint and recolour
counters completely unmoved, and exactly one reconcile per planet.

## Power zones: verified absent from the campaign (2026-08-31)

`powerZoneCells` read 0 on all 20 missions and was distrusted, for a good reason:
this project had already shipped exactly this failure once. The re-fog scan keyed
off `GetIsFogTerrain` (the DERIVED "currently dark" flag) instead of
`GetFogTerrain` (the map's definition) and confidently reported "no fog cells" on
a mission with 7845 of them. A uniform zero is what that looks like from outside.

So it was settled with three checks rather than one, via `resources:zonetest`
(story19, story15, story5):

| Check | Result |
|---|---|
| `GetPowerZone(x, y)` loop | 0 |
| Raw `World.powerZone` int array, an INDEPENDENT reader | 0 |
| `rawLen` vs `width * height` | equal on all three (49152, 50176, 23040) |
| **Positive control:** `SetPowerZone` three cells, re-count | **both readers report 3** |
| Restore | back to 0 |
| `resources: powerZone scan failed` in the log | never |

**Verdict: the reader works, so the zeros are the map's real answer.** The
campaign has no power zones, and the "a Reactor can be swapped to produce bluite,
so this is a second bluite source" concern does not apply to it.

`World` also has a `desiredPowerZone` - an `Il2CppReferenceArray<HashSet<Int32>>`,
i.e. per-column sets rather than a terrain layer. That is player/UI intent, not
the map, and is NOT the definition/derived pair the fog bug taught us to look for.
There is no such pair here.

A second, unit-based reader was added to `resources:dump` at the same time
(`powerZoneUnits`), because the 88-name registry contains a `PowerZone` type and
there is a `PowerZoneBuildGhost` - so zones exist as objects and counting only
terrain could have missed a map that carries them another way. It also reads 0.

**Where the confusion came from, most likely.** The docs described power zones as
"the bright blue ground Reactors are built on". The MINER's button object is named
`ReactorButton` (see "A fourth name space"), and the ground miners work is what
the play notes call RESO throughout - never "power zone". A button name almost
certainly got read as a unit name, and RESO ground became "reactor ground".

