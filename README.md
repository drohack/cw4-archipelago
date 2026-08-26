# Creeper World 4 Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer for
[Creeper World 4](https://knucklecracker.com/creeperworld4/cw4.php)'s
Farsite Expedition campaign.

Mission access, unit unlocks (cannon, mortar, terp, and the rest), and ERNs
are shuffled into the multiworld item pool. Completing mission objectives and
missions sends checks to other players; their checks send you your units.
Missions are open: any mission whose unlock you hold is playable in any
order. The goal is to beat the final mission, Ever After.

## Status

In development, and functional end to end. The mod connects to an Archipelago
server from the main menu (with auto-connect), receives items and applies them
live (unit unlocks appear mid-mission, ERNs spawn, missions unlock), sends
location checks as you complete objectives and missions, and colors the
mission map with the Archipelago tracker convention. Logic content (the exact
per-mission requirements) is still being finalized - see the design doc.

Covered so far:
- Connect / auto-connect / reconnect with a per-slot offline cache
- Items applied live: unit unlocks, mission unlocks, progressive ERNs, build limits
- Location checks: per-objective and per-mission, with the finale as the goal
- Mission gating: locked missions cannot be launched or save-loaded
- Mission map tracker: red / yellow / green / orange / grey per Archipelago
  convention, plus the native "?" for locked planets (which can't be clicked
  into a dead popup)
- Main menu slimmed to Farsite (SPAN hidden behind a config toggle) with the
  connection panel shown only on the menu
- Per-slot save isolation (a save from one seed never appears in another)
- Server messages (item sends/receives, chat) appear in a scrollable,
  semi-transparent message box in the bottom-left during a mission, colored
  with the Archipelago palette; it scales with the UI Scale setting
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

Details and troubleshooting: [docs/installation.md](docs/installation.md).

## Connect

Launch the game and use the Archipelago panel on the main menu: server
address and port, slot name, password. Mission availability follows your
received items.

## Repository layout

- `src/CW4Archipelago/` - the BepInEx mod (ships in releases)
- `src/CW4APProbe/` - development probe used to verify game mechanisms
- `apworld/cw4/` - the Archipelago world (Python)
- `docs/` - design and research documentation
- `tools/` - test batteries and release packaging

Contributing and building: [docs/developing.md](docs/developing.md).
Randomizer design: [docs/randomizer-design.md](docs/randomizer-design.md).

## License

MIT - see [LICENSE](LICENSE).
