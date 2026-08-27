# Traps feasibility spike - results (2026-08-26)

## What this was

A spike, not a feature. The question was whether the mod can produce hostile
in-mission effects through the CW4 API at all, and which of them feel worth
shipping as optional Archipelago trap items. The deliverable is this report plus
the working `trap:` debug commands; turning any effect into a real AP item (yaml
option, item pool, apply-on-receive, weighting) is a separate decision.

Verdict up front: **all seven effects work.** Seven traps are in the build -
spore strike (scatter), targeted spore strike (random building), creeper surge,
energy drain, emitter burst, unit stun, weapon drain.
Only re-fog was dropped, recorded as a comment block at the end of
`TrapEffects.cs` so nobody re-derives it. The decision and the final numbers
are at the bottom.

Design principle carried in from other AP games (ULTRAKILL's 15s Stamina
Limiter, DLC Quest's timed spikes): a good trap is temporary and recoverable.
Nothing here may make a mission unwinnable. Terrain deformation was ruled out up
front for exactly that reason and was not implemented.

## Results per effect

All verified in-game on the Farsite campaign, windowed 1920x1080, via the
config-gated `trap:` commands, with in-engine screenshots and log readback.

| # | Trap | Works | API used | Evidence |
|---|---|---|---|---|
| 1 | Spore strike | Yes | `SporeLauncher.CreateSpore(Spore.TARGET_BEHAVIOR.RANDOM, Vector2.zero, payload, pos)` (static) | 12/12 launched on story1; screenshot shows the arcing flight paths and a spore in flight |
| 2 | Creeper surge | Yes | `world.AddCreeper(int x, int y, int amt)` | story1: centre cell 0 -> 16,000,000 over 49 cells |
| 3 | Energy drain | Yes | `gs.energyStore` (writable float) | store 100 -> 95, reflected in the HUD STORE readout |
| 4 | Emitter burst | Yes | `gs.emitters` -> `Emitter.productionBaseAmt` / `productionInterval` (+ the `2` variants) | story3: 40,000,000 -> 160,000,000, then auto-restored to 40,000,000 after the 15s window |
| 5 | Unit stun | Yes | `UnitManager.SetStunCount(int ticks)` per unit | story15: cannon stun 900 -> 420 -> 300 over ~16s, so the sim counts it down by itself |
| 6 | Ammo drain | Yes | `UnitManager.ammo` (virtual float, settable) | story3: 1056 ammo removed from 2 units |
| 7 | Re-fog map | Yes | `world.SetDeFogTerrain(x, y, 0)` where `GetFogTerrain(x,y) > 0` | story15: defogged 0 -> 7845 -> 0, and the screenshots go dark -> revealed -> dark |

Also confirmed, and useful beyond traps:

- `UnitManager.StunUnitsInRangeS(int cx, int cy, int range, bool enemy, bool suppressMVerse = false)`
  exists as a static area version of the stun. Not used - the per-unit
  `SetStunCount` gives an exact duration, which an area call does not.
- `UnitManager.GetCellX(float x)` / `GetCellY(float y)` convert `pos.x` / `pos.z`
  to cells, as expected.
- `World.WORLD_CELL_WIDTH` / `WORLD_CELL_HEIGHT` are **per mission**, not
  global: story1 is 180x110, story15 is 224x224. Any full-map scan must read
  them each time.

## Calibration

**Creeper is fixed-point at 1,000,000 per displayed unit of depth.** Two
independent readings agree: `World.DIGITALIS_CREEPER_DEPTH` is 4,000,000 (the
in-game digitalis threshold is depth 4), and story3's emitters carry
`productionBaseAmt = 40,000,000` at `productionInterval = 15`, which is the
"40 per half second" those emitters show in the editor.

The spike's default of `DIGITALIS_CREEPER_DEPTH * 4` = 16,000,000 is therefore
**depth 16, which is far too much** - that is an instant loss, not a sting. A
shippable creeper trap wants depth 1 to 3 (1,000,000 to 3,000,000) over radius
2 to 4, and should land away from the rift lab rather than on top of it.

**The sim runs at 30 ticks/second**, confirmed by the stun countdown
(900 ticks -> 420 in roughly 16 seconds). Durations convert as
`ticks = seconds * 30`.

**Energy needs a percentage, not a flat number.** Draining 5 from a store of
100 refilled almost immediately - it was invisible. A real trap should take a
fraction of the current store (say 50% to 100%), which scales with the mission
instead of being trivial late and fatal early.

## Two findings worth keeping

### `UnitManager.enemy` is not a player/enemy discriminator

This is the trap-relevant bug the spike caught. On story3, `enemy` is `true`
for `Emitter` only - `Pod`, `Ultrac` and `SuperTower`, all hostile, report
`enemy = false`. A debuff filtered on `!u.enemy` would have **stunned and
disarmed enemy units, helping the player**, which is the exact opposite of a
trap.

The fix, in `TrapEffects.IsPlayerUnit`, filters on the authoritative list
instead: `UnitManager.GetDataName()` matched against
`Core.UnitRules.AlwaysAvailable + ItemToUnit.Values + "ern"` - the keys the
player can actually build. Verified on story15: 8 player units (6 ERN, 2
spawned cannons) against 21 correctly excluded (BlobNest, Totem, SporeLauncher,
SkimmerFactory, GreenarMother, Denier, ActivationAntenna, ResourceRed,
InfoCache).

Any future effect that touches "the player's units" must use this filter.

### `isFogTerrain` is derived state, not the fog definition

`World` carries three fog layers: `fogTerrain` (the map's fog **definition**),
`defogTerrain` (how much has been revealed), and `isFogTerrain` (the derived
"currently dark" flag). The first implementation keyed the re-fog scan off
`GetIsFogTerrain`, which silently found nothing to re-fog the moment anything
was revealed - it reported "this mission has no fog cells" on a mission with
7845 of them. Keying off `GetFogTerrain(x,y) > 0` is correct and the round trip
then works in both directions.

A full-map fog scan is cheap: 224x224 in 4-5ms, one-shot.

## Mission coverage (the caveat for two traps)

Not every Farsite mission can host every trap. Measured:

| Mission | Size | Emitters | Fog cells |
|---|---|---|---|
| story1 Farsite | 180x110 | 0 | 0 |
| story3 Not My Mars | - | 2 | 0 |
| story5 We Know Nothing | - | 0 | 0 |
| story15 Tower of Darkness | 224x224 | 0 (2 spore launchers) | 7845 |

So the **emitter burst and the re-fog only fire on some missions**.

This is a disqualifier, not a footnote. **A trap item that silently does nothing
is a bad item**: the player spends a check, receives a trap, and nothing happens
- which is worse than not having the trap, because it makes the trap pool feel
broken and it wastes a slot that a working trap could have used. An effect only
belongs in the pool if it fires on essentially every mission, or if it carries a
fallback effect for missions that cannot host it.

The four recommended traps (spore, creeper, energy, stun) all pass this test:
each depends only on things every mission has - the world grid, the energy
store, and the player's own units.

## Decision (2026-08-26) and final numbers

Seven traps ship (the two spore variants count separately). Only **re-fog** was
dropped: it applies to fog missions alone,
and on those the darkness IS the objective, so re-fogging reads as progress loss
rather than a setback.

The **emitter burst** is kept with a known caveat: it no-ops where a mission
ships no emitters. Measured across the first 8 Farsite missions - present on
story2 (1), story3 (2), story4 (4), story6 (3), story7 (2); absent on story1,
story5, story8. So roughly a third of missions would see nothing. That is a
weighting/balance problem for the AP item, not a bug in the effect.

Numbers tuned against the game's own values, read live with `trap:status`:

| Knob | Value | Where the number comes from |
|---|---|---|
| Spore payload | 20 depth (20,000,000) | **Exactly what CW4's own spores carry** - story7's launchers report `payload = 20000000`. A trap spore hits precisely as hard as a natural one. |
| Spore count | 2 | story7 fields 2 launchers at `sporeProductionInterval = 3600` (one spore each per 2 min), so 2 at once is already well above natural pressure - and targeted spores actually land. |
| Spore targeting | two traps: Scatter and PlayerBuilding (default) | Both built on the game's `LOCATION` behaviour with cells we choose. See the targeting section below. |
| Creeper depth | 2 depth per cell | An emitter pulse is 40-50 depth into one cell every 15 ticks (story3: 40,000,000; story7: 50,000,000). 2 depth over 49 cells is a couple of pulses spread wide - it flows and threatens without drowning a base. |
| Creeper radius / offset | r=3, 12 cells off the rift lab | Landing ON an undefended base is a near-instant loss, so it lands nearby and the player must react. Falls back to a random cell before the rift lab exists. |
| Energy drain | 100% of the store | **Proportional, not flat.** A flat amount is invisible late and fatal early: 5 off a store of 100 refilled almost instantly. Verified 100 -> 0 at 100% and 77 -> 38.5 at 50%. Energy regenerates, so it is a setback. |
| Emitter burst | x3 for 20s | Verified 50,000,000 -> 150,000,000 -> restored to 50,000,000, `burstActive` 2 -> 0. |
| Stun | **each unit's own `STUN_TIME`** | `UnitManager.STUN_TIME` is a per-type setting and the rift lab reports 300 ticks = **10s** - the duration the game itself applies when something stuns a unit. The earlier 15s was 1.5x that, i.e. a number I invented; using STUN_TIME makes the trap exactly one natural stun. `CAN_STUN` is also honoured, which is why ERNs shrug it off. Override with `trap:set stun=<seconds>`. Verified counting down on its own (300 -> 210). |
| Weapon drain | no knob | "Every weapon goes quiet until the packet network refills it" is the whole effect. Verified emptying and refilling by itself (49 removed, back to 3 within seconds). |

Settable live in depth units / percent:
`trap:set spores=2 payload=20 target=player depth=2 radius=3 offset=12 energy=100 emitmul=3 emitsec=20 stun=0`
(`stun=0` means per-unit `STUN_TIME`).

### Spore targeting: two traps, and why neither uses the game's STRUCTURE mode

`Spore.TARGET_BEHAVIOR` has three values. Measured with `trap:aim`, which fires a
wave and reports each live spore's real `targetPosition` as a distance to the
nearest player structure AND to the nearest building of any owner:

| Game mode | Aims at | Dist to player building | Dist to any building |
|---|---|---|---|
| `RANDOM` | arbitrary map points | ~75 | large |
| `STRUCTURE` | a random building of **any** owner | ~80 | **0 across 12 spores** |
| `LOCATION` | exactly the cell it is given | **0** | 0 |

`STRUCTURE` does pick real buildings - every one of 12 targets sat exactly on a
unit. But it picks from the whole map and is **not steerable**, so on story1
(player owned 1 of 36 buildings) it mostly hit scenery and enemy structures. An
earlier reading of this test concluded "STRUCTURE does not target the player",
which was wrong: distance-to-player is the misleading statistic on a lopsided
map, and distance-to-any-building is the one that answers the question.

Since a trap should threaten the PLAYER, both shipped variants are built on
`LOCATION`, choosing the cell ourselves:

- **Spore Strike** (`SporeAim.Scatter`) - game `RANDOM`, arbitrary map points.
  Exactly what CW4's own launchers do, so it is the fair, authentic version and
  may land somewhere harmless.
- **Targeted Spore Strike** (`SporeAim.PlayerBuilding`, the default) - every
  spore independently picks a random building **of the player's** and aims at it
  with `LOCATION`. Verified: with 5 player buildings, 12 spores distributed
  across (88,54), (68,48), (80,48), (72,48) and each measured `dPlayer = 0`.
  Scales with how built-up the player is, which is the good kind of scaling - it
  bites hardest late when there is something to lose. Spreading across buildings
  rather than always hitting the rift lab keeps it threatening without being
  surgical.

`SporeAim.RiftLab` aims every spore at the base. Available via
`trap:set target=riftlab`, not shipped - it is surgical enough to feel unfair.

Both targeted modes fall back to a real scatter when there is nothing to aim at
(no player buildings, or no rift lab), rather than dumping the strike at (0,0)
in a map corner. Verified: `aim=Scatter` with a log line saying why.

The mod uses its own `TrapEffects.SporeAim` enum for this rather than the game's,
precisely because the game's names do not mean what a trap needs.

### Two coordinate findings that were silently breaking things

**World coordinates map 1:1 to cells.** `GetCellX(0)=0`, `GetCellX(50)=50`,
`GetCellX(100)=100`, and story1's rift lab sits at world `(41,7,33)` = cell
`(41,33)`. Only the height needs looking up (`UnitManager.GetMinHeight`).

**`World.GetCreeperVertex(cellX, cellY)` is NOT a cell-to-world converter.** It
returns mesh-local coordinates: `GetCreeperVertex(50,50)` gives `(18,6,18)`.
Using it as a world position launched spores from the wrong place and put spawned
test units at NEGATIVE cells, which quietly invalidated a whole round of
targeting measurements before the `trap:aim` structure-cell dump exposed it.
`TrapEffects.CellToWorld` is now the one place that conversion happens.

### The player-unit filter

**`GetDataName()` returns the build-pane key for every buildable except the rift
lab, which reports `CommandBase` rather than `riftlab`.** `IsPlayerUnit` was
therefore excluding the player's own base, so stun and drain silently spared it.
Checked against the rest of the set - `cannon`, `tower`, `mortar`, `sniper`,
`sprayer`, `terp` all match their `UnitRules` keys exactly - so `CommandBase` is
the lone alias, now in `ExtraPlayerKeys` alongside `ern`.

Separately, **`UnitManager.enemy` is not a player/enemy discriminator**: story3
reports `Pod`, `Ultrac` and `SuperTower` (all hostile) with `enemy = false`, and
only `Emitter` with `enemy = true`. Filtering a debuff on `!u.enemy` would have
stunned and disarmed enemy units, i.e. helped the player.

Both were only visible because `Status()` prints a `P:` / `-:` type histogram.
That histogram is the guard against the whole class of bug where a trap runs,
logs success, and affects nothing. Keep it.

## What is in the build

Config-gated behind `DebugCommands` (off for players), so this is dormant code,
not yet a player feature - no trap is wired to an AP item.

- `src/CW4Archipelago/Appliers/TrapEffects.cs` - the effects; the tuning
  constants in one block at the top; `CellToWorld`; the `IsPlayerUnit` filter;
  `Status()` for readback, `Set()` for live tuning, and the two spike
  experiments `Aim()` and `Coord()`; and a closing comment block recording
  re-fog. Five effects are fire-and-forget; only the emitter burst carries
  state, and `ModCore.Tick` drives its restore (it also drops the snapshot if
  the mission changes mid-burst, so it never writes through stale IL2CPP
  pointers).
- `src/CW4Archipelago/Appliers/DebugChannel.cs` - `trap:<name> [args]`, plus two
  pieces of test scaffolding this spike needed: `sim:run [speed]` / `sim:pause`
  (clears every entry in `GameSpace.pauseOwner` so a battery can run the sim
  without a human pressing play) and `spawn:<unitKey> [n]` (places units beside
  the rift lab, or at map centre before it exists, so the debuffs have targets;
  `spawn:CommandBase` works and is how a test base gets placed).
- `src/CW4Archipelago/CW4Archipelago.csproj` - added the `Il2CppSystem.Core`
  reference, needed for `Il2CppSystem.Collections.Generic.HashSet<T>`
  (`gs.units`, `gs.emitters`).

Commands: `trap:spore [count] [payloadDepth]` (configured default),
`trap:scatter` (random map points), `trap:building` (random player buildings),
`trap:creep [radius] [depth]`,
`trap:energy [fraction]`, `trap:emit [seconds] [multiplier]`,
`trap:stun [seconds]`, `trap:drain`, `trap:status`, `trap:set k=v ...`, and the
diagnostics `trap:aim [mode]` and `trap:coord`. Omitted or zero arguments mean
"use the tuned default".

## Not done

Deliberately out of scope for a spike, and unchanged from the plan:

- No AP wiring: no trap items, no yaml option, no weighting, no apply-on-receive.
- No terrain deformation (`LowerTerrain` / `SetTerrain`) - permanent, can
  soft-lock.
- Re-fog is not in the build; its working implementation is recorded as a
  comment in `TrapEffects.cs`.
- No `CreateDigitalis`, `CREATEEGG` / `CreateAirSac`, `DAMAGEUNIT` /
  `DESTROYUNIT`, or `CmdSetGameSpeed`. These were candidates from the metadata
  grep; the seven above were enough to answer the question, and the destructive
  ones conflict with "recoverable".
- Balance beyond first-order sanity. The numbers above are calibrated against
  the game's own content, not playtested for difficulty - that wants a real
  run through several missions.
