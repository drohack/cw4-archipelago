# Mod wiring design (2026-08-25)

Approved design for turning `src/CW4Archipelago` from a load-only skeleton into a
playable Archipelago client: connect/auto-connect, receive items live, send
location checks, drive the main menu and mission map, per-slot save archiving.
Every game mechanism used here was proven in `src/CW4APProbe` and is written up
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
  - orange - partial (some remaining checks in logic, some not)
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

Always available: riftlab, tower, pylon. "Build Limit +1 (X)" adds one to the
game's default limit for unit x. There are no limit-0 items.

## Persistence and offline

- Config (BepInEx `com.droha.cw4archipelago.cfg`): Host, Port, Slot, Password,
  AutoConnect, DebugCommands. Panel edits write config. AutoConnect connects at
  Galaxy scene entry and reconnects with backoff (5s, 10s, 20s, cap 60s).
- Slot cache: `<Documents>/My Games/creeperworld4/archipelago/slots/<seed>-<slot>.json`
  holding received item names in server index order, checked locations, pending
  checks, and a slot_data snapshot. Loaded on connect attempt and at launch when
  AutoConnect is set, so offline play uses last-known state.
- Pending checks flush on (re)connect via CompleteLocationChecks; the server's
  AllLocationsChecked reconciles the cache.
- Goal: story20 mission complete -> SetGoalAchieved (queued if offline).

## Mission-page ownership (supersedes save archiving)

Original plan was to archive mcs.dat + saves per slot for a clean page.
Resolved during implementation (research-findings "Steam Cloud PIVOT"): the
mod OWNS the mission-select display instead. TrackerView drives every planet's
lock state (`forceUnlocked` + `lockedPlanet`), objective glyph colors, and
completion (via the `FakeIsMissionObjectiveComplete` patch) purely from AP
state, regardless of mcs.dat contents. MissionGate blocks launching and
save-loading locked missions. So the player's vanilla campaign progress is
neither shown nor reachable through the randomizer, with no file manipulation
and no risk to user data.

Physical archiving (the proven same-length storyN -> xtoryN byte-rename plus a
`saves/farsite` move) remains available if a future need arises, but is NOT
part of this milestone - display-ownership makes it unnecessary.

## Test tiers

1. Core unit tests (`src/CW4Archipelago.Core.Tests`, xunit): pure C#, no game.
2. apworld tests (`apworld/cw4/test`, AP WorldTestBase) run in the local
   Archipelago clone after `tools/ap-sync.ps1`.
3. Game integration battery (`tools/apbattery.sh`): local AP server from the
   clone (MultiServer.py with piped stdin for `/send` console commands), game
   launched with AutoConnect + DebugCommands, log assertions for connect, live
   item receipt, checks reaching the server, tracker colors, gating, archive
   round trip, offline queue and flush, zero plugin errors.
4. Manual smoke with a real player at the end of the milestone.
