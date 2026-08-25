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

The probe supports a file-command protocol: write commands to
`<game>/BepInEx/probe-unlocks.txt` and read results from
`<game>/BepInEx/LogOutput.log`. The batteries in `tools/` drive full
game-launch regression runs on top of it (bash, e.g. via Git Bash):

- `tools/battery2.sh` - 13-assert regression: whitelist enforcement, build
  limits, mission gating, no-flash pane reveal, save/resume.
- `tools/erntest.sh` - ERN spawn/grant/deny battery.
- `tools/survey.sh` - campaign data survey (objectives, flags, enemy census).

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
