# Mod wiring design (2026-08-25)

> **How to read this (note added 2026-08-31).** This is the wiring design as
> agreed on 2026-08-25 and it remains the reference for the client/apworld
> CONTRACT. Several specifics have changed since; the ones a reader would
> otherwise take as current are corrected inline below. `docs/developing.md` and
> `docs/randomizer-design.md` carry current behaviour.


Approved design for turning `src/CW4Archipelago` from a load-only skeleton into a
playable Archipelago client: connect/auto-connect, receive items live, send
location checks, drive the main menu and mission map, per-slot save archiving.
Every game mechanism used here was proven in the research probe (deleted
2026-09-03; see git history) and is written up
in `research-findings.md`; the item/location/logic content comes from
`randomizer-design.md`.

## Decisions

- **Offline behavior: cached state + queued checks.** A per-slot local cache
  keeps the last-known received items and checked locations. Offline, those
  unlocks stay in force; checks made offline are queued and flushed on
  reconnect. With no connection and no cache: starter missions and base units
  only.
- **Tracker colors follow the Archipelago/PopTracker convention**, not CW4's
  own palette (verified against the PopTracker README):
  - red - not accessible (mission unlock not held)
  - yellow - reachable but not in logic
  - green - reachable and in logic
  - orange - partial (some remaining checks in logic, some not).
    **CORRECTION: no glyph is ever orange.** `TrackerStatus.Partial` exists but
    `StatusColor` maps it to GREEN, so the map shows four colours, not five.
  - grey - finished (PopTracker hides cleared locations; planets cannot hide)
  - blue (visible but unobtainable) has no CW4 equivalent and is unused
- **Logic lives once, in the apworld.** The apworld ships requirement groups in
  slot_data; the C# client only evaluates lists. It never encodes rules.
- **Layered architecture**: network -> pure-C# slot state -> per-effect
  appliers, each applier one proven probe recipe.
- **Config-gated debug command channel** kept in the real mod so the test
  batteries can drive it hands-free. Off by default; players never see it.

## Architecture

```
apworld/cw4 (Python)          slot_data: requirement groups, starter missions
        |  (AP server)
        v
src/CW4Archipelago.Core (net6.0 class lib, NO Unity/interop refs, unit-tested)
   SlotState      received items (name->count), checked + pending locations,
                  hints; raises ItemsChanged / LocationsChanged
   UnitRules      item name -> game unit key / limit increment; AllowedUnits, Limits
   MissionRules   storyN <-> title <-> unlock item; starter set
   ErnRules       Progressive ERN count -> ERNs to grant per mission
   TrackerRules   per-location + per-mission status (Red/Yellow/Green/Orange/Grey)
                  from state + requirement groups
   SlotStore      JSON persistence per (seed, slot)
        ^
        |
src/CW4Archipelago (BepInEx plugin)
   Plugin         BasePlugin: config, registers thin ModBehaviour shim
   ModBehaviour   injected MonoBehaviour: Update->ModCore.Tick, LateUpdate->tints
   ModCore        scene tracking, main-thread dispatch queue drain, wiring
   ApClient       MultiClient.Net session: connect/login(+slot data), ItemReceived,
                  CompleteLocationChecks, SetGoalAchieved, reconnect backoff,
                  marshals every callback onto the main thread via the queue
   Appliers (one per proven probe recipe, each subscribes to SlotState):
     UnitGate         whitelist + limits + no-flash reveal state machine
     MissionGate      Harmony prefixes: GalaxyMissionPanel.OnLaunch,
                      MissionPanelLoadBoxRow.OnLoad
     LocationWatcher  objective/mission-complete transitions -> SlotState.MarkChecked
     ErnGranter       grant queue, waits for GameSpace.commandBase, spawns "ern"
     TrackerView      Harmony prefix SpanNetworkPlanet.FakeIsMissionObjectiveComplete
                      -> AP state; LockedPlanet/forceUnlocked; glyph `_color`;
                      Refresh() on state change while map open
     MenuUi           hide chronom/markV/colonies/editor; AP login panel (TMP)
     SaveArchiver     per-slot archive/restore of mcs.dat story entries + saves
     DebugChannel     file commands, only when config DebugCommands=true
                      **CORRECTION (2026-09-03): moved out of this
                      assembly into src/CW4Archipelago.Debug, a separate
                      plugin. No config flag - installing it enables it,
                      and no release contains it.**
```

IL2CPP rules that shape this (see research-findings "Crash lessons"): the
injected MonoBehaviour stays a thin shim with no statics; all state lives in
plain static classes; never patch ReadData or UnitBuildPane.OnEnable/Start;
compare interop wrappers by `.Pointer`, never `==`.

MultiClient.Net raises its events on the socket thread. `ApClient` never
touches game state from a callback; every callback enqueues an action that
`ModCore.Tick` drains on the Unity main thread.

## Slot data contract (apworld -> client)

```json
{
  "starter_missions": ["story1"],
  "mission_requirements": { "story2": [["Cannon", "Mortar"]] },
  "location_requirements": {
    "Not My Mars - Objective 1": [["Cannon", "Mortar"], ["Nullifier"]]
  },
  "ern_per_item": 1
}
```

A requirement is a list of any-of groups of item names; it is satisfied iff
every group has at least one held item. Mission reachability = its unlock item
is held (starter missions are free). A location is in logic when its mission's
requirements and its own requirements are both satisfied. `rules.py` builds its
access rules from the same table it exports to slot_data, so logic and hints
cannot drift.

## Item name -> game mapping (Core.UnitRules)

| Item | Unit key | | Item | Unit key |
|---|---|---|---|---|
| Cannon | cannon | | Bomber Pad | bomberpad |
| Mortar | mortar | | Runway | runway |
| Nullifier | nullifier | | Shield | shield |
| Miner | miner | | AC Bomber Pad | acbomberpad |
| Factory | factory | | Chronat | chronat |
| Greenar Refinery | greenarrefinery | | Microrift | microrift |
| Missile Launcher | missilelauncher | | Platform | platform |
| Sprayer | sprayer | | Rocket Pad | rocketpad |
| Terp | terp | | Airship | airship |
| ERN Portal | ernportal | | Bertha | bertha |
| Sniper | sniper | | Sweeper | sweeper |
| Porter | porter | | | |

Always available: riftlab, tower. **CORRECTION 2026-08-31: pylon is NOT always
available** - it is an unlockable AP item, and it is load-bearing progression for
Archon's buried caches. "Build Limit +1 (X)" adds one to the
game's default limit for unit x. There are no limit-0 items.

## Persistence and offline

- Config (BepInEx `com.droha.cw4archipelago.cfg`): Host, Port, Slot, Password,
  AutoConnect, DebugCommands, and ShowSpan. Panel edits write config.
  **CORRECTION (2026-09-03): DebugCommands is gone. The debug channel is
  its own plugin now and its presence is the switch.**
  AutoConnect connects at Galaxy scene entry and reconnects with backoff.
  **CORRECTION: 3 attempts at 5s, 10s, 15s** - no 20s step and no 60s cap.
- Slot cache: `<Documents>/My Games/creeperworld4/archipelago/slots/<seed>-<slot>.json`
  holding received item names in server index order, checked locations, pending
  checks, and a slot_data snapshot. Loaded on connect attempt and at launch when
  AutoConnect is set, so offline play uses last-known state.
- Pending checks flush on (re)connect via CompleteLocationChecks; the server's
  AllLocationsChecked reconciles the cache.
- Goal: **CORRECTION - story19 (Founders), not story20.** Ever After plays as an
  epilogue rather than a climax, so it is an ordinary mission. The goal is also
  gated on a count of missions beaten (`missions_for_finale`, default 12).
  SetGoalAchieved, queued if offline.

## Mission-page display ownership + per-slot save isolation

Two separate concerns, handled separately:

- **Display** is owned by TrackerView: every planet's lock state
  (`forceUnlocked` + `lockedPlanet` + `planet`/`objectiveContainer` visibility),
  objective glyph colors, and completion (via the `FakeIsMissionObjective
  Complete` patch) come purely from AP state, regardless of `mcs.dat`. So the
  mission page reflects the multiworld, not the local vanilla save, with no
  `mcs.dat` manipulation. `PlanetClickPatch` also swallows clicks on locked
  planets so no dead popup appears.
- **Save files** are isolated per slot by `SaveArchiver`: on connecting to a
  slot different from the last active one, the live `saves/farsite` folder is
  moved into `archipelago/save-archive/<previous>/` and the new slot's archived
  saves are restored (proven Steam-Cloud-safe: moving `saves/farsite` is not
  cloud-restored). This prevents a save from one seed appearing in another's
  load list. Nothing is deleted; every switch is reversible via `active.txt`.

Display ownership handles the visual; save isolation handles the actual files.
`mcs.dat` is left untouched (the tracker overrides its display role).

## Server-message + connection box (in-mission)

`ApClient` subscribes to `session.MessageLog.OnMessageReceived`. On the main
thread it raises `MessageReceived` (plain text) and `LineReceived` (per-part
colored spans, hex pulled from each `MessagePart.Color`). `ModCore` keeps a
rolling history (cap 200) and feeds `ApMessageBox`, a scrollable,
semi-transparent message box shown DURING A MISSION only (Game scene). It sits
in the bottom-left resting on top of the terrain/creeper/emit-mode readout
cluster, matches that cluster's width, colors each part with the Archipelago
dark palette, and scrolls via a scrollbar or the mouse wheel while hovering. It
reads the UI scale live from the always-present `BOTTOM` corner container, so it
tracks the in-game UI Scale setting and window size. Connection status
TRANSITIONS (disconnected / retrying / reconnected) also append lines, so a drop
or reconnect is visible without leaving the mission. Not shown on the
menu/level-select. (Superseded the earlier fading toasts.)

By default the box shows only messages relevant to the local player
(`Core.MessageRelevance` classifies each `LogMessage` via its subtype and
`IsRelatedToActivePlayer`); a Me/All header toggle reveals every player's
activity retroactively. An always-on input row at the bottom sends chat and
`!commands` through `ApClient.Say`; the server echoes them back through the
normal message path. A Harmony prefix on `InputManager.HandleInput` suppresses
the game's own hotkey/camera/wheel input while the box is focused or hovered
(`InputManager.enabled` does not gate it - the game calls HandleInput directly),
so typing and scrolling drive the box, not the game.

The menu shows connection status too: the main-menu panel status line and the
level-select compact label both reflect disconnected / connecting / retrying /
failed states from `ApClient.StatusText`.

## Reconnect: bounded auto-retry + menu fallback

On a detected drop the client auto-retries up to 3 times (5s, 10s, 15s apart),
then stops with a clear "save and return to the menu to retry" status. The
retry budget re-arms on a successful connect and on any manual/menu connect, so
returning to the menu (auto-connect fires on every Galaxy entry) is the
guaranteed fallback. On reconnect, queued checks flush and received items are
pulled.

Drop DETECTION: a graceful close fires `SocketClosed`; an ungraceful server
death is caught by a periodic `Socket.Connected` poll and by send failures
(a failed check re-queues and triggers the reconnect). Detection of an abrupt
drop can lag ~30s (websocket keepalive), after which the bounded retry runs.

## Graceful patch failure

Each Harmony patch is applied independently and guarded; if a future game
update changes one patched method, that feature logs an error and disables,
but the rest of the mod (connection, items, checks) still loads.

## Test tiers

1. Core unit tests (`src/CW4Archipelago.Core.Tests`, xunit): pure C#, no game.
2. apworld tests (`apworld/cw4/test`, AP WorldTestBase) run in the local
   Archipelago clone after `tools/ap-sync.ps1`.
3. Game integration battery (`tools/apbattery.sh`): local AP server from the
   clone (MultiServer.py with piped stdin for `/send` console commands), game
   launched with AutoConnect + the debug plugin installed (the battery still
   writes a DebugCommands key, which is now ignored), log assertions for
   connect, live
   item receipt, checks reaching the server, tracker colors, gating, archive
   round trip, offline queue and flush, zero plugin errors.
4. Manual smoke with a real player at the end of the milestone.
