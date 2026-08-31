# Creeper World 4 Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer for
[Creeper World 4](https://knucklecracker.com/creeperworld4/cw4.php)'s
Farsite Expedition campaign.

Mission access, unit unlocks (cannon, mortar, terp, and the rest), ERNs, build
limits, energy and storage upgrades, and optional traps are shuffled into the
multiworld item pool. Completing objectives and missions sends checks to other
players; their checks send you your units.

Missions are open: any mission whose unlock you hold is playable in any order.
The goal is to beat **Founders**, and it takes more than reaching it - the finale
is unwinnable until you have beaten a configurable number of other missions
(12 of 19 by default). Ever After is a twentieth mission the campaign hides
behind a cutscene; the mod places it on the map as an ordinary mission.

## Status

In development, and functional end to end. The mod connects to an Archipelago
server from the main menu (with auto-connect), receives items and applies them
live (unit unlocks appear mid-mission, ERNs spawn, missions unlock), sends
location checks as you complete objectives and missions, and colors the mission
map with the Archipelago tracker convention.

Not yet done: a full playthrough of a generated seed. Everything below is
verified in slices - unit tests, in-game batteries, and hands-on checks of
individual mechanisms - but nobody has played one seed start to finish, so trap
frequency, energy-item pacing and how early the casual logic tier lands are all
still unproven in practice.

Covered so far:
- Connect / auto-connect / reconnect with a per-slot offline cache
- Items applied live: unit unlocks, mission unlocks, progressive ERNs, build
  limits, energy storage and base generation, and seven optional traps
- **236 locations**: every cache, totem and nullify target is its own check
  (203 of those), plus reclaim, custom objectives and mission completions
- Per-mission logic derived from a manual playthrough, with a casual tier that
  brings snipers and missiles forward in the spheres
- Randomized starter missions - any mission with a collectible reachable without
  a weapon can open the game, not just Farsite
- Mission gating: locked missions cannot be launched or save-loaded
- Finale gate: the last mission is genuinely unwinnable until the count is met -
  its objective panel says so, and the planet reads as locked
- Mission map tracker: red / yellow / green / grey per Archipelago convention,
  plus the native "?" for locked planets (which can't be clicked into a dead
  popup). Objective icons are corrected to match the checks that actually exist
- Main menu slimmed to Farsite (SPAN hidden behind a config toggle) with the
  connection panel shown only on the menu
- Per-slot save isolation (a save from one seed never appears in another)
- Server messages (item sends/receives, chat) appear in a scrollable,
  semi-transparent message box in the bottom-left during a mission, colored
  with the Archipelago palette; it scales with the UI Scale setting. Filtered
  to messages relevant to you by default, with a Me/All toggle to show every
  player's activity, plus an always-on input row to chat and run !commands
  in-game (game hotkeys and map zoom are suppressed while it has focus)
- Reconnects on returning to the menu, re-syncing checks made offline

## Install (players)

1. **BepInEx**: unzip
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2)
   into the Creeper World 4 install folder (the one containing `CW4.exe`).
2. **First launch**: start the game, wait for the main menu, quit. The first
   launch is slow - BepInEx is generating interop assemblies.
3. **Mod**: unzip `CW4Archipelago-vX.Y.Z.zip` from the
   [releases page](../../releases) into the same game folder.
4. **Archipelago host**: put `cw4.apworld` (same release) into your
   Archipelago installation's `custom_worlds/` folder.

Details, the yaml options, and troubleshooting:
[docs/installation.md](docs/installation.md).

## Connect

Launch the game and use the Archipelago panel on the main menu: server
address and port, slot name, password. Mission availability follows your
received items.

## Repository layout

- `src/CW4Archipelago/` - the BepInEx mod (ships in releases)
- `src/CW4Archipelago.Core/` - the mod's rules and state, pure C# with no Unity
  dependency, so all of it is unit-testable
- `src/CW4Archipelago.Core.Tests/` - those unit tests
- `src/CW4DevTools/` - a separate cheat and survey plugin used to research the
  game. Deliberately not part of the randomizer, and installed separately
- `src/CW4APProbe/` - earlier development probe, kept for game-mechanism research
- `apworld/cw4/` - the Archipelago world (Python)
- `docs/` - design and research documentation
- `tools/` - test batteries, probes and release packaging

Contributing and building: [docs/developing.md](docs/developing.md).
Randomizer design: [docs/randomizer-design.md](docs/randomizer-design.md).

## License

MIT - see [LICENSE](LICENSE).
