# Creeper World 4 Setup Guide

## Requirements

- Creeper World 4 (Steam, Windows)
- BepInEx 6 (IL2CPP, win-x64) - exact tested build linked in the mod's README
- The CW4Archipelago release zip and cw4.apworld from the mod's releases page

## Installation

1. Install BepInEx: unzip the BepInEx 6 IL2CPP x64 archive into the Creeper
   World 4 install folder (the one containing CW4.exe).
2. Launch the game once and wait for the main menu, then quit. The first
   launch takes noticeably longer - BepInEx is generating interop assemblies.
3. Unzip the CW4Archipelago release zip into the same game folder. It adds
   BepInEx/plugins/CW4Archipelago/.
4. Launch the game. The main menu now shows the Archipelago connect panel.

## Joining a MultiWorld game

1. Enter the server address and port (e.g. archipelago.gg:38281), your slot
   name, and the room password (if any) in the in-game panel.
2. Press Connect. Mission availability updates to match your received items.

## Uninstalling

Delete winhttp.dll, doorstop_config.ini, .doorstop_version, changelog.txt,
and the BepInEx and dotnet folders from the game directory.
