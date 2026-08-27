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

The real mod exposes a config-gated file-command channel (enable
`DebugCommands` in `BepInEx/config/com.droha.cw4archipelago.cfg`): write
commands to `<game>/BepInEx/cw4ap-commands.txt`, read results from
`LogOutput.log`. Commands: connect, disconnect, dump, units, item:<name>,
check:<location>, boot:<storyN>, objective:<n>, win, ada:close, tracker:dump,
story:open, clickplanet:<storyN>, limit:<unit>, ern:status, gatecheck:<storyN>,
say:<text>, showall:on|off, shot:<path>, msgbox:set, msgbox:dump, canvas:dump,
hud:dump, minimap:dump, menu:dump, toast:<text>.

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

The probe (`src/CW4APProbe`) keeps its own file-command protocol
(`probe-unlocks.txt`) and older batteries (`tools/battery2.sh`,
`tools/erntest.sh`, `tools/survey.sh`) for game-mechanism research.

Scripts read the game location from `CW4_DIR` (defaults to the maintainer's
path) and write outputs under `$TEMP`.

## Release checklist

1. Game closed; `dotnet build` clean for both projects.
2. `tools/battery2.sh` passes 13/13.
3. Manual smoke: launch, main menu shows the AP panel and slimmed buttons,
   boot two missions, verify unit whitelist and mission locks.
4. Bump `<Version>` in `src/CW4Archipelago/CW4Archipelago.csproj` and
   `world_version` in `apworld/cw4/archipelago.json`.
5. `tools/package-release.ps1` - writes `dist/CW4Archipelago-vX.Y.Z.zip`
   and `dist/cw4.apworld`.
6. Test-install the zip into a clean game folder; check
   `BepInEx/LogOutput.log` for the mod + MultiClient.Net load lines.
7. Create the GitHub release with both artifacts.

## Decompiling game code for reference

Interop assemblies are stubs (signatures only). To inspect them:
`ilspycmd -t <TypeName> "<game>/BepInEx/interop/Assembly-CSharp.dll"`
(`dotnet tool install -g ilspycmd`). Do not commit decompiled output.
