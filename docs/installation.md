# Installation

## Requirements

- Creeper World 4 (Steam, Windows). Tested against the current Steam build
  (Unity 2019.4.23f1, IL2CPP).
- BepInEx 6, IL2CPP, win-x64, version 6.0.0-pre.2. This exact build is what
  the mod is developed and crash-tested against:
  https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2
  (file: `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip`)

## Step 1: Install BepInEx

Unzip the BepInEx archive directly into the Creeper World 4 install folder -
the folder containing `CW4.exe`. For Steam:
right-click the game -> Manage -> Browse local files.

After unzipping, the game folder contains `winhttp.dll`,
`doorstop_config.ini`, and a `BepInEx` folder next to `CW4.exe`.

## Step 2: First launch

Start the game normally and wait until the main menu appears, then quit.

The first launch takes noticeably longer than usual (up to a few minutes):
BepInEx is generating interop assemblies for the game. This happens once.

## Step 3: Install the mod

Unzip `CW4Archipelago-vX.Y.Z.zip` (from the releases page) into the same
game folder. It adds `BepInEx/plugins/CW4Archipelago/` with the mod and its
libraries.

Launch the game. The main menu now shows the Archipelago connection panel;
menus not relevant to the randomizer (Chronom, Mark V, Colonies, editor) are
hidden while the mod is active.

## Step 4: Archipelago host setup

Whoever generates the multiworld needs `cw4.apworld` (from the same release)
in their Archipelago installation's `custom_worlds/` folder, and each CW4
player needs a yaml with `game: Creeper World 4`.

## Connecting

Use the panel on the main menu: server address and port
(e.g. `archipelago.gg:38281`), slot name, password if the room has one.
On connect, mission availability and unit unlocks update to match your
received items. Progress made while playing is sent live.

## Troubleshooting

- **Game starts but no panel / nothing changed**: check
  `BepInEx/LogOutput.log` in the game folder for a line containing
  `CW4 Archipelago ... loading`. If the file does not exist, BepInEx is not
  installed (step 1); if the line is missing, the plugin folder is misplaced
  (step 3).
- **Antivirus complains about winhttp.dll**: this is BepInEx's standard
  loader shim; allow it or install to a whitelisted folder.

## Uninstalling

Delete from the game folder: `winhttp.dll`, `doorstop_config.ini`,
`.doorstop_version`, `changelog.txt`, and the `BepInEx` and `dotnet`
folders. The game is then fully vanilla; verify files via Steam if unsure.
Your saves and campaign progress live elsewhere
(`Documents/My Games/creeperworld4`) and are not touched by uninstalling.
