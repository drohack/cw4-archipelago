# Creeper World 4 Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer for
[Creeper World 4](https://knucklecracker.com/creeperworld4/cw4.php)'s
Farsite Expedition campaign.

Mission access, unit unlocks (cannon, mortar, terp, and the rest), ERNs, energy
and storage upgrades, and optional traps are shuffled into the multiworld item
pool. Completing objectives and missions sends checks to other
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
- Connect / auto-connect / reconnect with a per-slot offline cache, and
  offline play from that cache when no server is reachable (see
  [docs/design/2026-09-04-offline-and-disconnects.md](docs/design/2026-09-04-offline-and-disconnects.md))
- Items applied live: unit unlocks, mission unlocks, progressive ERNs, energy
  storage and base generation, and six optional traps
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
4. **Archipelago host** (only if you are generating the multiworld): install
   [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   0.6.7 or newer, put `cw4.apworld` (same release) into its `custom_worlds/`
   folder, and `Creeper World 4.yaml` into `Players/`.

Details, the yaml options, and troubleshooting:
[docs/installation.md](docs/installation.md).

## Connect

Launch the game and use the Archipelago panel on the main menu: server
address and port, slot name, password. Mission availability follows your
received items.

## The other two plugins (build them yourself)

The repo holds three BepInEx plugins. Only the randomizer is in a release; the
other two are built locally, and neither shares code with the randomizer - run
any combination of them, or none.

| Plugin | What it does | Ships? |
|---|---|---|
| `CW4Archipelago` | the randomizer | yes, in releases |
| `CW4DevTools` | cheats and survey tools for looking at a mission | no - build it |
| `CW4Archipelago.Debug` | file-command channel for driving the game from scripts | no, by design |

### Why you have to build them

They compile against the IL2CPP interop assemblies that BepInEx generates from
YOUR copy of the game. Those are derived from Creeper World 4 itself, so they
cannot be committed here or shipped - which is also why CI cannot build any
plugin in this repo.

### Building

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (net6.0 target;
   any SDK 6 or newer works).
2. Do steps 1 and 2 of the player install above: BepInEx into the game folder,
   then launch the game once so the interop assemblies exist.
3. Copy `src/GameDir.props.example` to `src/GameDir.props` and point it at your
   game folder.
4. **The game must be CLOSED** - the build deploys straight into
   `BepInEx/plugins/` and cannot overwrite a loaded DLL.

```
dotnet build src/CW4DevTools              # cheats and survey tools
dotnet build src/CW4Archipelago.Debug     # scripted-testing channel
```

Each deploys to its own folder (`BepInEx/plugins/CW4DevTools`,
`BepInEx/plugins/CW4ApDebug`). Add `-p:SkipDeploy=true` to compile without
installing. To remove one, delete its folder - renaming it does not work,
BepInEx scans subfolders recursively, so move it out of `plugins/` entirely.

### Using CW4DevTools

Cheats are hotkeys held with a modifier (Left Ctrl by default) and are also
config entries in `BepInEx/config/com.droha.cw4devtools.cfg`, editable while the
game runs. An on-screen strip lists what is active, so a mission played with
cheats cannot be mistaken for a vanilla one.

| Key | Does |
|---|---|
| Ctrl+F5 | instant build, no cost |
| Ctrl+F6 | every building available, ignoring the campaign unlock schedule |
| Ctrl+F7 | infinite energy, ammo and wares |
| Ctrl+F8 | your units cannot be destroyed |
| Ctrl+F9 | freeze creeper - it stops flowing, so a map can be inspected |
| Ctrl+F10 | cycle forced game speed: off, 2, 4, 8, 16 |
| Ctrl+F11 | reveal the whole map on fog missions |
| Ctrl+End | mark every mission objective complete, to leave a mission early |
| Ctrl+Home | dump the game's unit-name registry to the log |

It is a research and debugging aid, not a companion to the randomizer: it will
happily hand you buildings the randomizer has locked.

### Using CW4Archipelago.Debug

Its presence IS the switch - there is no config flag. Installed, it watches
`BepInEx/cw4ap-commands.txt` and runs one command per write, logging results to
`BepInEx/LogOutput.log`. That is how the `tools/*.sh` batteries drive the game
unattended:

```
echo "boot:story2"     > "$CW4/BepInEx/cw4ap-commands.txt"
echo "item:Cannon"     > "$CW4/BepInEx/cw4ap-commands.txt"
echo "glyphs:dump Home" > "$CW4/BepInEx/cw4ap-commands.txt"
```

Commands cover connecting, granting items, sending checks, booting missions,
loading saves, and dumping tracker, objective, unit and UI state. See
[docs/developing.md](docs/developing.md) for the full list.

## Repository layout

- `src/CW4Archipelago/` - the BepInEx mod (ships in releases)
- `src/CW4Archipelago.Core/` - the mod's rules and state, pure C# with no Unity
  dependency, so all of it is unit-testable
- `src/CW4Archipelago.Core.Tests/` - those unit tests
- `src/CW4DevTools/` - a separate cheat and survey plugin used to research the
  game. Deliberately not part of the randomizer, and installed separately
- `src/CW4Archipelago.Debug/` - the file-command test channel and measurement
  probes, as their own plugin so no release contains them
- `apworld/cw4/` - the Archipelago world (Python)
- `docs/` - design and research documentation
- `tools/` - test batteries, probes and release packaging

Contributing and building: [docs/developing.md](docs/developing.md).
Randomizer design: [docs/randomizer-design.md](docs/randomizer-design.md).

## License

MIT - see [LICENSE](LICENSE).
