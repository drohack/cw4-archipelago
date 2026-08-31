# Developing

## Repository layout

- `src/CW4Archipelago/` - the real mod. Ships in releases.
- `src/CW4APProbe/` - the research probe: a file-command-driven harness that
  proved every game mechanism. Kept for answering "can the game do X"
  questions. Never shipped.
- `apworld/cw4/` - the Archipelago world (Python source).
- `tools/` - test batteries and packaging scripts.
- `docs/randomizer-design.md` - the design source of truth (items, locations,
  logic rules, campaign survey data).
- `docs/research-findings.md` - proven recipes and crash rules for modding
  CW4 under IL2CPP. Read this before touching plugin code.

## Build setup (C# mod)

1. Install the .NET SDK (net6.0 target; any SDK >= 6 works).
2. Install BepInEx 6.0.0-pre.2 (IL2CPP win-x64) into your Creeper World 4
   folder and launch the game once - this generates
   `BepInEx/interop/*.dll`, which the projects reference. These assemblies
   are derived from the game and must never be committed or redistributed.
3. Copy `src/GameDir.props.example` to `src/GameDir.props` and set your game
   path.
4. `dotnet build` in `src/CW4Archipelago` (or `src/CW4APProbe`). Building
   deploys the plugin into the game's `BepInEx/plugins/` automatically.
   **The game must be closed when building** - the deploy step cannot
   overwrite a loaded DLL (MSB3021).

## apworld development

The `Archipelago/` folder (gitignored) is expected to be a clone of
https://github.com/ArchipelagoMW/Archipelago at the repo root. It serves as
the local generator/server for testing:

- `tools/ap-sync.ps1` copies `apworld/cw4/` into the clone's `worlds/`.
- Generate a test multiworld from the clone:
  `python Generate.py --player_files_path <dir with a cw4 yaml>`
- Host locally: `python MultiServer.py <generated .archipelago file>`

## Testing

Three tiers:

1. **Core unit tests** (no game): `dotnet test src/CW4Archipelago.Core.Tests`.
   Pure C# logic - slot state, rules, tracker colors, persistence.
2. **apworld tests** (in the Archipelago clone): `tools/ap-sync.ps1` then
   `python -m unittest discover -s worlds/cw4/test -t .` from the clone.
3. **Game integration batteries**:
   - `tools/apbattery.sh` - connect / live items / unit gate / location checks
     / tracker colors / mission gating / offline queue-and-flush.
   - `tools/apbattery2.sh` - goal on the finale, save-load gate decision, live
     tracker update while the page is open, build-limit items, ERN items, plus
     save archiving, the message-box receive path, and the menu-entry
     auto-connect. Both write their own hermetic BepInEx config and start a
     local server from the clone.

## CW4 Dev Tools (separate plugin)

`src/CW4DevTools/` is a SEPARATE BepInEx plugin for surveying missions (see
[the requirements worksheet](design/mission-requirements-worksheet.md)). It
shares no code with the randomizer in either direction, has its own GUID,
config and plugin folder, and ships in no release. Run either, both, or
neither.

Cheats, all hot-toggleable and all scoped to the PLAYER's units - enemies,
creeper, terrain and objectives are never touched, so a mission still requires
whatever it required:

| Setting | Key | Effect |
|---|---|---|
| `InstantBuild` | F5 | Buildings finish on placement, free |
| `AllBuildings` | F6 | Every building placeable; restores the mission's own set when switched off |
| `InfiniteResources` | F7 | Energy production lifted and deficit cleared; ammo and ALL ware slots filled |
| `Indestructible` | F8 | `impervious` set, `DESTROY_ON_UNEVEN_TERRAIN` lifted, health held at MAX_HEALTH; all three restored on release |
| `FreezeCreeper` | F9 | `SetCreeperTransferMultiplier(0)` - nothing flows |
| `GameSpeed` | F10 | Forces `GameSpace.GAME_SPEED`; cycles off/2/4/8/16. The in-game buttons cap at 4x, this does not |
| reveal fog | F11 | One-shot defog of the whole map |
| complete objectives | End | One-shot `AcquireMissionObjective` on every slot, to leave early |

`ShowOverlay` draws a three-row strip at the bottom centre listing EVERY option
with the key that toggles it, green when on and grey when off, plus a dim row of
one-shots. Always visible: all-grey reads as "vanilla" just as clearly as a
hidden panel and additionally shows what is available.

Two earlier designs were worse and the reasons are worth keeping. A vertical
list at top-left covered the GEN/USE/STORE readout and the build buttons.
Replacing it with "only the active cheats, plus a shared F5-F10 label" fixed the
overlap but was unreadable - no per-key mapping, no way to see the other
options, and off was indistinguishable from absent. Legibility and staying clear
of the HUD are both requirements, not a trade-off. Geometry is measured against
the 1920-wide HUD: the creeper readout ends near x=670 and the minimap starts
near x=1480, so the panel is 780 wide centred at x=1060.

The background is sized to the text (`FitPanel`), not fixed. It used to be a
constant 780x78 measured against the longest row, so every shorter row trailed a
wide band of empty black across the map. Use `TMP_Text.GetPreferredValues`
rather than the `preferredWidth`/`preferredHeight` properties: those measure
against the CURRENT rect, so on the first pass they report against the
provisional size and the last row renders outside the box.

Its position is `OverlayX` / `OverlayY` in the config rather than a constant,
and is re-read every frame, so it can be nudged while the game runs. That is
not gold-plating: "the bottom centre is free" is only true of the PERMANENT
HUD, and tool-specific panels appear there too. The terraform bar was the one
that caught it - measured at 74 reference units tall, which is where the default
`OverlayY = 80` comes from.

### Checking the overlay against a HUD mode

The terraform bar only exists while terraform mode is open, so a log cannot show
the collision and neither can a screenshot taken from the default view. The loop
that works:

    powershell -File tools/game-key.ps1 -vk 76     # 76 = 'L' = Terraform
    echo "shot:C:\temp\shot.png" > "$GAME/BepInEx/cw4dev-commands.txt"

CW4's own keybinds are in `~/Documents/My Games/creeperworld4/settings/controls.xml`
as decimal keycodes - `<Terraform>` is 108 (`l`). `game-key.ps1` needs its
AttachThreadInput dance because Windows refuses `SetForegroundWindow` from a
background process: without it the call silently fails, focus stays where it was
and the keystroke lands in the wrong window (it went to VS Code the first time).
The script now checks the foreground process and reports FAILED rather than
pretending it sent the key.

Take the shot with the in-engine `shot:` command, not a desktop capture: the
game renders at 3840x2160 here while the canvas is authored against 1920x1080,
so measurements off the PNG need halving to get reference units.

Two traps in that one line, both of which cost a round trip. Give `shot:` a
WINDOWS path - a git-bash `/c/Users/...` path is not absolute to the game and
the file lands somewhere else entirely. And never put the path in a `printf`
FORMAT string: `printf 'shot:%s\path\to.png'` turns the backslash-t into a tab
and the game writes a file with a tab in its name. Pass it as an argument
(`printf '%s' "shot:$P"`) or use `echo`.

CW4DevTools has its OWN file-command channel at
`<game>/BepInEx/cw4dev-commands.txt`: `boot:storyN`, `ada:close`,
`sim:run [speed]` / `sim:pause`, `spawn:<RealUnitName> [n]`, `shot:<path>`,
`dump`, `story:open`, `planets:dump`, `span:goto <guid>`, `set:<cheat>=on|off`.

`story:open` invokes the Farsite button's own `onClick`, and it exists because
synthetic mouse input does NOT reach CW4's UI - `SetCursorPos` plus `mouse_event`
moves the cursor and the game ignores the click, so the menus cannot be driven
from outside the process. `planets:dump` then reports every planet with its
unlock state AND its world/screen position, because "active and unlocked" does
not mean visible: that is how story20 was found parked off the map (see
docs/research-findings.md). `span:goto <guid>` then pans the level-select camera
to centre a planet, which is how "is it reachable?" gets answered separately
from "does it have a position?" - it moves `Camera.main`, because the Farsite
view is panned by `Span`, and `SpanMissionNetwork` (the one class that does carry
drag clamps) belongs to a different screen and is absent here. It exists so survey work never has to enable the
randomizer just to boot a mission or take a screenshot - doing that repeatedly
is how the Archipelago layer kept getting left switched on. No AP commands here
by design.

Both csprojs accept `-p:SkipDeploy=true` to compile without installing into the
game. Without it, every build re-creates the plugin folder, which silently
re-enabled the randomizer in the middle of a vanilla test session.

### Per-frame work: audit, then events, 2026-08-31

CW4 recomputes a lot of state every tick, so it is tempting to answer everything
with a per-frame write. Most of the time that is the wrong tool, and the audit
found four places where it was.

The audit's first pass THROTTLED and CACHED those four. That cut the cost and
left the architecture polling, which was the wrong answer to the right question:
the question is not how cheap a poll is, it is whether the work belongs in a
frame at all. A second pass replaced the polling with the game's own lifecycle
events, and the tables below record both passes - the throttles are gone.

One honest limit: Unity offers no way to run main-thread code without a frame
hook, so a per-frame CHECK cannot be removed. The goal is that the check is a
single bool test and the per-frame WORK is zero.

**Genuinely per-frame** (leave alone):

| Work | Why |
|---|---|
| Adding energy to the rift lab | "Energy per second" has to be delivered per tick |
| The storage ceiling | One float compare; the sim resets the value, so it must be re-asserted |
| Instant build / indestructible unit pass | The player expects both to act immediately |

**Fixed, and what each one became:**

- **The mission map was a full scene scan per frame.** `TrackerView.ApplyTints`
  ran `FindObjectsOfType<SpanNetworkPlanet>()` in LateUpdate - a whole-scene
  search about sixty times a second for twenty-one objects that never change
  while the map is open - and then a `GetComponentsInChildren` per planet and a
  material write per marker.
  It polled for two reasons, and both turned out to have hooks. The planets
  appear when a PANEL opens, not on a scene change: `Span.Start` is that moment,
  so a postfix there says "the map is open" instead of searching to find out. And
  the game's own `SpanNetworkPlanet.Refresh` overwrites our visuals, which the
  mod used to win by re-asserting them every frame: a postfix on `Refresh`
  re-applies that one planet instead. `ApplyTints` is now `if (!_dirty) return;`.
- **The finale lock allocated nineteen strings a frame.** It called
  `MissionsBeaten`, which builds a location name per mission to test it, and
  parsed the mission specifier, both every frame - for an answer that changes
  when a mission is completed. Now recomputed on `ApClient.StateChanged`. One
  pointer compare remains, to notice a mission change; that has no event the mod
  already consumes, and it is a single integer comparison.
- **Location progress walked three sets a frame.** Nullify, totem and cache
  progress each iterate one of the game's sets. Two of the three now have hooks:
  postfixes on `Totem.set_totemComplete` and `InfoCache.DestroyUnit` mark the
  watcher dirty. (The cache hook was `InfoCache.Retrieved` first. It was applied,
  it was on a real method with a plausible name, and a real pickup proved it never
  fired once - collecting a cache destroys the unit and never calls Retrieved.
  See [research-findings.md](research-findings.md).) **A once-a-second poll of all three stays, deliberately.** A
  Harmony patch on a private method or a property setter can silently fail to
  apply under IL2CPP, and checks that stop firing entirely would be far worse
  than checks that arrive a second late. The patches give the response; the poll
  guarantees the correctness. Nullification has no hook at all - nothing on
  `UnitManager` or `GameSpace` is named for it, and `Nullifier.FireAtUnit` is
  private and fires repeatedly while the beam is up rather than once on success -
  so it rides the poll.
- **The dev overlay built its string every frame** just to discover it had not
  changed. The first pass compared an integer signature of the toggles instead;
  that was cheap but had to be updated by hand whenever a displayed option was
  added, and forgetting would silently stop the strip updating. It now subscribes
  to BepInEx's `ConfigFile.SettingChanged`, which cannot drift out of step.
  Hotkey handling stays per frame: Unity input has no event model.

**The hooks, and one that does not exist:**

| Hook | Kind | Replaced |
|---|---|---|
| `Span.Start` | public | Polling to notice the mission map opened |
| `SpanNetworkPlanet.Refresh` | public | Re-asserting visuals every frame after the game repaints |
| `Totem.set_totemComplete` | property setter | Polling `gs.totems` |
| `InfoCache.DestroyUnit` | public | Polling `gs.mustCollect` |
| `ApClient.StateChanged` | the mod's own | Recomputing tracker/gate state every frame |
| `ConfigFile.SettingChanged` | BepInEx | The dev overlay's hand-maintained signature |

A constraint worth recording: `ApClient` REPLACES the `SlotState` object on
connect, so subscribing once to `State.ItemsChanged` would leave a dead
subscription. `State` is a property whose setter re-wires the events and forwards
them to `StateChanged`, so one client-level subscription serves every consumer.

**Removed rather than optimised:** the dev tools' per-frame `CAN_NULLIFY` hold.
It worked, but it permanently removed the unit from `nullifiableUnits` for the
rest of the mission - a soft-lock waiting to happen, and it made the per-instance
nullify counter register a phantom nullification. The randomizer does the same
job with a Harmony filter on `Nullifier.GetNullifierTargets`, which mutates
nothing and lifts instantly.

### The audit was measured, not just reasoned

Worth recording, because the static audit got one of its own claims wrong.

`TrackerView` logs a line whenever it searches the scene for planets, and the
debug channel's `perf` command reports how many recolour passes have run. That
turned "the caching should help" into a number:

| | Scene scans | Recolour passes |
|---|---|---|
| 20s on the main menu, polling | 2256 | - |
| 20s on the main menu, throttled | ~10/sec | - |
| 20s on the main menu, event-driven | **0** | - |
| opening the map, event-driven | **1** | 11 |
| after the finale gate opened | 0 | 13 |

The caching worked for the map, but the first measurement showed 2256 scans in
twenty seconds and nearly all of them returned NOTHING: the Galaxy scene is both
the main menu and the mission map, and the planets only exist once the map is
open. So an empty result is the normal state on the menu, and retrying every
frame there was a whole-scene search about a hundred times a second while the
player read the menu. That is pre-existing behaviour the code review missed and
only the measurement found.

With `Span.Start` driving it instead, the menu does no scans at all and opening
the map does exactly one. The recolour counts prove the cache does not go stale:
they advance when Archipelago state changes and not otherwise, and the finale's
glyph still flips red to green when the gate opens.

**The refactor's own bug, and why it was nearly invisible.** Making the tracker
repaint on `ApClient.StateChanged` broke the colouring, because `StateChanged`
was raised only when an ITEM arrived or the connection status moved - never when
a location was CHECKED. So the finale's glyph stayed red after twelve missions
were beaten. The per-frame poll had covered that gap for as long as it existed,
by rebuilding a signature that happened to include `CheckedLocations.Count`.

Two things came out of that. `ApClient.State` became a property that forwards the
state's own `ItemsChanged` and `LocationsChanged` to `StateChanged`, so every
mutation propagates rather than the two that were remembered. And the colouring
became ASSERTABLE: `glyphs:dump` reads the colour back off each glyph's material
and names it, because the only previous way to check it was a screenshot and a
human eye. The colouring had by then broken silently twice - once when locations
became per-instance and `ColorGlyphs` started building names that matched
nothing, and once here. `tools/eventdriven-test.sh` covers both.

**The general rule this settles.** If the game recomputes a value, do not fight
it with a per-frame write - patch the thing that recomputes it. Two cases in the
finale lock make the point: `MissionObjectiveData.customName` accepted a write
that never reached the screen because `ObjectiveRow` rebuilds its label every
frame, and `CAN_NULLIFY` accepted a write the sim undid within a tick. Both
became one-line Harmony postfixes on the game's own update.

**And the corollary the refactor added, which then caught a real bug.** A patch
being APPLIED is not the same as a patch FIRING. A postfix can attach to a method
IL2CPP never calls, or to a method that is simply not on the path you think it is,
and a safety poll will then cover for it silently forever. So `perf` reports
`totemPokes` and `cachePokes`, and the battery asserts both are non-zero.

That is not hypothetical: `cachePokes` was **0 after a human collected a cache**.
The cache hook was on `InfoCache.Retrieved`, which is never called on the pickup
path - the poll had been doing all the work since the hook was written. Without
the counter the feature looked fine, because it WAS fine, just for a different
reason than the code claimed. The hook is now on `InfoCache.DestroyUnit`.

The rule: every event hook gets a fired-counter, and something has to trigger the
real event at least once. "The patch applied" proves very little on its own.

### The flashing planet: one hook was not enough

Reported from play: "Not My Mars" alternating between its planet sphere and its
locked "?". The diagnostic (`diag:watch`, which samples in Update - before our own
LateUpdate can correct anything, so it sees what the player saw) caught it:

    DIAG FLASH: frame=812 story4 'Ruins Repurposed' wantUnlocked=False
                sphereOn=True forceUnlocked=False unlocked=True refreshes=22
    ... twenty-plus consecutive frames, refreshes=22 throughout ...

The Refresh counter never moved during the wrong frames, which rules out "the
game refreshed that planet and we lost the race". The cause is that refreshing
one planet changes its NEIGHBOURS - the map reveals connected planets - so
unlocking "Not My Mars" turned on the sphere of "Ruins Repurposed", whose own
Refresh was never called. A per-instance repaint could not see it, and it stayed
wrong until some unrelated state change happened to trigger a full repaint.

Fix: a Refresh postfix repaints the whole map, not just `__instance`. It
terminates because the sweep only calls Refresh again when a planet's
forceUnlocked actually changes, and the recursion guard stops a nested Refresh
scheduling a further sweep. Measured: 41 wrong frames before, 2 single
non-consecutive frames after, 0 on a still map.

The general lesson, and it is a correction to the section above: "hook the thing
that overwrites you" is right, but the thing that overwrites you is not always
the object you are looking at.

### Icons that were not ours: the map's marker set can disagree with the mission

Also reported from play: planets showing up with icons that were not one of the
four tracker colours. `glyphs:dump` named the colour as `OTHER(0.00,1.00,0.00)` -
the game's own bright green, which the mod had left alone.

Farsite is the case. Its map marker is objective type 1 (Totems), and the mission
has no totems at all - measured live, `totems=0 infoCaches=2` with only objective
slot 5 (Custom) enabled. So that marker maps to no Archipelago location,
`ColorGlyphs` skipped it, and it kept vanilla's green - which in this map's
language now means "reachable and in logic". A confident green icon for something
that is not a check.

The location table is not the problem: `counts:dump` on story1, story3 and story7
matched `INSTANCE_COUNTS` exactly (2/0/0, 1/4/2, 1/3/4 caches/totems/nullify).
Vanilla's marker set is simply not the mission's objective set.

An unmatched marker now takes the MISSION's overall status, so every visible icon
is one of the four colours and none of them lies about reachability. The icon's
SHAPE stays vanilla's mistake; fixing that means building the marker set
ourselves rather than colouring the game's, which is a bigger change and is not
done.

### The icon set, not just its colour

The map draws one icon per objective in the MAP FILE's authored list, and that is
not always the mission's real objective set. Tabulated across all twenty planets:
19 agree, Farsite does not. It draws a Totems icon on a mission with no totems,
while its two caches and its custom objective got no icon at all - so their
status could not be read off the map. Vanilla does the same, confirmed by
screenshotting the base game with the randomizer parked, so this is the game's
data rather than a regression.

`TrackerView.ReconcileGlyphs` now makes the set follow the LOCATIONS, via
`MissionRules.ExpectedObjectiveIndices`. Generic rather than a special case for
mission 1: a no-op wherever the game already agrees, and self-correcting if a
game update changes the map data.

**It does not require a connection, and the first version wrongly did.** Driving
the icons purely from the AP location list meant that opening the game
unconnected showed vanilla's wrong icon, because that list is empty until a
server sends one - and the map still displays Farsite, since it is the default
starter. Which objectives are CHECKS is a per-seed question; which objectives a
mission HAS is not. `MissionRules.MissionObjectives` is the measured answer to
the second, used as the fallback; locations win once known, because a seed may
exclude some. `MissionObjectivesTests` pins that table against what the game's
own markers draw, so 19 agreeing and Farsite disagreeing is asserted rather than
remembered.

Three measurements made it cheap (details in
[research-findings.md](research-findings.md)): writing `objective` re-textures
the marker, so re-pointing an icon needs no donor and no prefab hunt; the layout
is `x = 0.55 * ordinal` with nothing else varying, so an added icon goes exactly
where the game would have put it; and a clone keeps the SOURCE's position, so the
position must always be written.

It also absorbed a bug nobody had noticed: **Refresh appends markers without
clearing the container**, so every `forceUnlocked` flip left another exact copy
stacked on the previous one. Invisible, because they overlap perfectly. The
reconcile hides the surplus.

Idle cost is zero: 1,250 frames on an open map with the Refresh, paint and
recolour counters unmoved, and one reconcile per planet.

### Screenshots are part of verifying the map

Both of the above were invisible to every log-reading test and were reported by a
player, not caught here. `tools/map-visual-check.sh` puts the map into a known
state - three planets unlocked with a deliberate mix of done and open checks -
screenshots it, and documents the expected result icon by icon so the picture has
a right answer to be compared against.

### Two invariants the cheats must keep

Both were learned the hard way and both are covered by the battery:

1. **Toggling a cheat off undoes it.** Anything that CHANGES a game parameter
   snapshots the original on first apply and restores it on release -
   AllBuildings, GameSpeed, FreezeCreeper, and the energy store. Snapshots are
   dropped on mission change so one mission's values are never restored into
   another.
   Cheats that GRANT something are one-way and are NOT undone: a finished
   building, a healed unit, a filled magazine. You cannot un-build a tower, and
   reverting a player's ammo mid-fight would read as a bug. That is acceptable
   only because what they grant is VALID - which it was not once, when ware slots
   were filled with an invented 1000 instead of the unit's own capacity, leaving
   sprayers convinced they were full so they ignored porter deliveries. Switching
   the cheat off did not help, because the bad value was already in the units.
   **Grant the game's own values, never plausible-looking constants.**

2. **Only touch units the player created this mission.** The name list cannot
   distinguish a player's SuperTower or Stash from one the MAP placed - both
   dumped as "MINE" - and an indestructible map object can make a mission
   unwinnable. So the unit pointers present ~2s after mission load are recorded
   as map content and skipped by every cheat; anything appearing later is the
   player's. This closes the whole class rather than patching names one by one.

Hotkeys require a held modifier (default `Ctrl`) because F5-F12 are Creeper World
hotkeys too - bare keys made every toggle also fire a game action.

Four bugs worth remembering, all found by playtest rather than by reading code:

- **Health is not the only way a unit dies.** Indestructible originally just
  held `health` at `MAX_HEALTH`, which looks complete and is not: CW4 has
  destroy paths that never reduce health. `UnitManager.DESTROY_ON_UNEVEN_TERRAIN`
  removes a unit outright when the ground under it stops being flat, and
  `Platform` overrides `DestroyUnit`, so platforms kept dying at full health with
  the cheat on. The game has its own switch for this - the per-unit
  `impervious` bool - and using it is both simpler and complete. The clamp is
  still applied on top, because `impervious` only stops NEW damage; a unit
  already hurt when the cheat went on would otherwise stay hurt and read as the
  cheat not working. The general lesson: when the game already models the thing
  a cheat wants, drive its model instead of simulating the symptom.

- **Energy has two limits.** `energyStore` is clamped by the network's storage
  CAPACITY, so writing a huge number still displayed `STORE 100` - full, just a
  small buffer. Production is the separate limit: `GEN 1 / USE 2` runs a deficit
  no matter how full the store is. Fix pins `energyProduction`,
  `energyProductionUnClamped` and clears `energyDeficit` as well.
- **A forced flag must be un-forced.** `AllBuildings` wrote all 26 availability
  flags true; switching it off merely stopped writing, leaving every building in
  the sidebar. It now snapshots the mission's values on the first force, restores
  them on release, and calls `LeftPane.RefreshUnitBuildPanes()` so the strip
  actually re-renders. The snapshot is dropped on mission change so one mission's
  flags never leak into another.
- **Filling only non-empty slots fills nothing.** Ware top-up originally skipped
  slots holding 0, so an empty factory never received bluite/redon/liftic - the
  entire point of the cheat.

**Regression battery: `tools/devtools-test.sh`** (game closed; optional mission
arg, default story7). It pins a KNOWN config before launching, boots, builds a
fixture and asserts on log output - 13 checks covering plugin load, randomizer
absence, boot, spawn-by-real-name, instant build, weapon ammo, energy, health,
the AllBuildings save/restore pair, freeze creeper, and a zero-error log.

It exists because ad-hoc checking kept passing while something else broke. In its
first two runs it caught three things eyeballing had missed: instant build
silently off (an earlier `set:instantbuild=off` had persisted into the config),
an assertion on `energyProduction` that the sim recomputes after our write, and
the ware/ammo interaction below. Any change to the cheats should re-run it.

One check reports SKIP: ware filling needs a factory wired into the packet
network, and a factory spawned by `CreateUnitAtPosition` has none, so the fixture
cannot exercise it. It is verified by hand instead. Reported rather than deleted -
a false FAIL trains you to ignore the battery, and a silent deletion hides a gap.

The ware/ammo interaction took four attempts and is worth reading in
`TopUpUnit`: every rule that fixed factories broke weapons or vice versa, because
weapon ammo is ware-backed AND a factory's storage is registered in its
`AMMO_WARES`. The working version fills every slot and then sets `u.ammo` LAST,
so it needs no rule distinguishing the two at all.

Deliberately NOT included: "place anywhere" (`UnitBuildGhost.alwaysLegal`).
Whether a location is reachable without air or a porter is exactly what the
survey is trying to establish, so that cheat would corrupt the findings.

`DumpUnits` (Home, or `DumpUnitsOnStart`) logs the game's unit-name registry
and, per unit on the map, its type, data name and whether the cheats consider it
yours. That diagnostic found the cause of pylons and miners ignoring
`InstantBuild`: **the build-pane keys are not unit names**, so those units failed
the player filter and every cheat skipped them. See "Unit names" in
[randomizer-design.md](randomizer-design.md) for the mapping - the same bug was
present in the randomizer's `GameUtil.IsPlayerUnit` and is fixed there too.

`isBuilding` is the correct and only build signal. A "does it have a build bar"
fallback was tried and removed: `HasBuildBar`/`BuildBarCubes` describe the BAR
(5 cubes on everything), not remaining progress, so it flagged every finished
building - including ones instant-built a frame earlier. `FinishBuild` instead
logs one line per unit type it completes, which is how the pylon/miner fix was
proven: `instant-built 'towerbridge'` and `'collector'` now appear where before
the name fix they never did.

**Disabling a plugin: move its folder out of `BepInEx/plugins/`.** Renaming the
folder to `.disabled` does NOT work - BepInEx scans subfolders recursively and
loads the DLL anyway. `BepInEx/plugins-disabled/` is outside the scanned tree.
(Renaming a loose `.dll` to `.dll.disabled` does work, since only `.dll` is
scanned.)

The real mod exposes a config-gated file-command channel (enable
`DebugCommands` in `BepInEx/config/com.droha.cw4archipelago.cfg`): write
commands to `<game>/BepInEx/cw4ap-commands.txt`, read results from
`LogOutput.log`. Commands: connect, disconnect, dump, units, item:<name>,
check:<location>, boot:<storyN>, objective:<n>, win, ada:close, tracker:dump,
story:open, clickplanet:<storyN>, limit:<unit>, ern:status, gatecheck:<storyN>,
say:<text>, showall:on|off, shot:<path>, msgbox:set, msgbox:dump, canvas:dump,
hud:dump, minimap:dump, menu:dump, toast:<text>, finale:need <n>, finale:beat
<n>, loc:add <location>, perf, glyphs:dump [planet title], totem:complete,
cache:destroy, counts:dump, totems:dump, pane:dump, resources:dump,
resources:zonetest, diag:span, diag:watch [seconds], diag:refresh <planet title>.

Four of those exist for the event-driven work and are worth knowing:
`glyphs:dump` reads each objective glyph back off the live object - colour named
RED/YELLOW/GREEN/GREY, plus name, activeSelf, localPosition, material and
`_MainTexture` - so both the colouring AND the icon set can be asserted from a
log instead of eyeballed in a screenshot, and a HIDDEN marker can be told from an
ABSENT one; `diag:refresh` calls the game's Refresh on one planet and reports its
objective child count either side, which is how "Refresh appends markers" was
measured (and it adds markers as a side effect, so it is a measurement, not
something to leave running); `perf` reports recolour passes plus how
many times each location patch has fired; `totem:complete` drives
`Totem.totemComplete` on one live totem, which really completes it and really
goes through the patched setter; `cache:destroy` destroys one info cache, which is
the effect a pickup has. A real pickup is still unscriptable - mouse input does
not reach CW4's UI - so `tools/cache-handtest.sh` sets up both sides of one and
watches for the check.

The dev tools have `overlay:dump`, which reports the cheat strip's text and how
many times it has been redrawn - the strip is event-driven now, and a lost
subscription would otherwise leave it quietly showing stale cheat state.

Test scaffolding and the traps spike (see
[traps spike](design/2026-08-26-traps-spike.md)):
`sim:run [speed]` / `sim:pause` clears every `GameSpace.pauseOwner` entry so a
battery can run the sim without a human pressing play; `spawn:<unitKey> [n]`
places units beside the rift lab (or at map centre before it exists) so
unit-targeting effects have targets (`spawn:CommandBase` places a test base);
`trap:<name> [args]` fires one trap effect - `scatter` (spores at random
points), `building` (spores at random player buildings), `spore` (whichever is
configured), `creep`, `energy`, `emit`, `stun`, `drain` - plus `status` for a readback (including the
player/non-player unit histogram that catches a trap silently affecting
nothing), `set k=v` for live tuning in depth units, and the diagnostics `aim`
(where spores actually aim) and `coord` (cell/world mapping). Omitted or zero
arguments use the tuned defaults in `TrapEffects.cs`. These are dormant; no trap
is wired to an AP item yet.

Two oracles that LOOK authoritative and are not - both cost time before being
caught, so they are worth knowing about before reaching for them:

- **The build-ghost dump** (`DEVTOOLS build ghosts`) claims ghost -> prefab ->
  `GetDataName()` is "the decisive mapping with no guessing". Every one of the ~60
  ghosts actually reports `(no UnitManager)`. The ghost NAMES are still useful -
  they are registry names, and the absence of a `PorterBuildGhost` was itself the
  clue that "porter" is not a unit name.
- **`pane:dump`'s `=ON`/`=off`** reads `activeInHierarchy`, which on the struct tab
  reflects PAGING, not availability: all six buttons read `=ON` with zero items
  granted. `DEBUG UNITS: structButtons=N` is the count that actually tracks the
  availability flags - watching it go 1 -> 2 -> 3 while granting one item at a
  time is what mapped the buttons to their flags.

`resources:zonetest` is the pattern to copy for any "the count reads zero"
question: two independent readers, a bounds check, and a POSITIVE CONTROL that
writes a known value and reads it back. It closed the power-zone question that a
year of zeros could not, and the reason it was needed is in
[research-findings.md](research-findings.md) - the fog scan once reported "no fog
cells" on a mission with 7845 of them.

The probe (`src/CW4APProbe`) keeps its own file-command protocol
(`probe-unlocks.txt`) and older batteries (`tools/battery2.sh`,
`tools/erntest.sh`, `tools/survey.sh`) for game-mechanism research.

`tools/names-probe.sh` dumps the game's naming: the 88-entry registry, the build
ghosts, the CMOD GUIDs, and a `spawn:` pass over candidate names (spawn is a name
oracle - it says outright when a name is not real). `tools/story15-handtest.sh`
sets up a hands-on test with EXACTLY one mission's logic requirements granted and
nothing else, which is how a "is this mission possible without X" question gets
asked faithfully - `UnitGate` enforces the list, so the tester cannot cheat by
accident.

Scripts read the game location from `CW4_DIR` (defaults to the maintainer's
path) and write outputs under `$TEMP`.

## Release checklist

1. Game closed; `dotnet build` clean for both projects.
2. `tools/battery2.sh` passes 13/13.
3. `tools/eventdriven-test.sh` passes 22/22 and `tools/devtools-test.sh` has no
   failures - between them they cover the event hooks, which fail silently.
4. `tools/map-visual-check.sh` and READ the screenshot against the expected
   result in its header. Two map bugs reached a player because every check here
   read the log instead of looking.
5. Manual smoke: launch, main menu shows the AP panel and slimmed buttons,
   boot two missions, verify unit whitelist and mission locks.
6. Bump `<Version>` in `src/CW4Archipelago/CW4Archipelago.csproj` and
   `world_version` in `apworld/cw4/archipelago.json`.
7. `tools/package-release.ps1` - writes `dist/CW4Archipelago-vX.Y.Z.zip`
   and `dist/cw4.apworld`.
8. Test-install the zip into a clean game folder; check
   `BepInEx/LogOutput.log` for the mod + MultiClient.Net load lines.
9. Create the GitHub release with both artifacts.

## Decompiling game code for reference

Interop assemblies are stubs (signatures only). To inspect them:
`ilspycmd -t <TypeName> "<game>/BepInEx/interop/Assembly-CSharp.dll"`
(`dotnet tool install -g ilspycmd`). Do not commit decompiled output.
