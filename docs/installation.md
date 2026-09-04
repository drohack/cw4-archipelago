# Installation

## What to download

Three files, and every step below says which one it needs.

| file | where |
|---|---|
| `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip` | [BepInEx 6.0.0-pre.2 release](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2) |
| `CW4Archipelago-vX.Y.Z.zip` (the mod) | [this project's releases](https://github.com/drohack/cw4-archipelago/releases/latest) |
| `cw4.apworld` and `Creeper World 4.yaml` | same release as the mod |

Plus [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
itself, **0.6.7 or newer**, if you are the one generating the multiworld. A
player who is only joining someone else's game does not need it.

## Requirements

- Creeper World 4 (Steam, Windows). Tested against the current Steam build
  (Unity 2019.4.23f1, IL2CPP).
- BepInEx **6**, IL2CPP, win-x64, version 6.0.0-pre.2.

  **Why 6 and not 5:** Creeper World 4 is an IL2CPP build of Unity, and
  BepInEx 5 only supports Mono games. IL2CPP support arrives in the BepInEx 6
  pre-releases, so 5 will not load at all here - it is not a matter of
  preferring the newer one.

  **Why that exact pre-release:** 6.0.0-pre.2 is what the mod is developed and
  crash-tested against. Later pre-releases change the Il2CppInterop surface the
  mod compiles against, so mixing versions tends to fail at load with a type
  error rather than anything self-explanatory.

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

Unzip `CW4Archipelago-vX.Y.Z.zip` - from
[this project's releases](https://github.com/drohack/cw4-archipelago/releases/latest) -
into the same game folder. It adds `BepInEx/plugins/CW4Archipelago/` with the mod and its
libraries.

Launch the game. The main menu now shows the Archipelago connection panel;
menus not relevant to the randomizer (Chronom, Mark V, Colonies, editor) are
hidden while the mod is active.

## Step 4: Archipelago host setup

This step is only for whoever GENERATES the multiworld. If you are joining a
game someone else generated, skip to "Connecting".

1. Install [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases/latest)
   **0.6.7 or newer** - that is the version the world is tested against in CI,
   and the one its `archipelago.json` declares as its minimum.
2. Put `cw4.apworld` (from the same release as the mod) into the Archipelago
   installation's `custom_worlds/` folder. Create it if it is not there.

   **Which folder is that?** Archipelago uses its install folder when that is
   writable, and `%USERPROFILE%/Archipelago` when it is not - so an install
   under `Program Files` puts your files in the second one while a portable or
   source copy keeps them alongside the program. Rather than guess: the folder
   holding `Players/` is the one that also wants `custom_worlds/`.
3. Put `Creeper World 4.yaml` (same release) into `Players/`, and edit `name:`
   to your slot name. Every option has a default, so you can change nothing
   else and it will generate.
4. Run `ArchipelagoGenerate.exe` (or `python Generate.py`). It writes a seed
   archive to `output/` containing a `.archipelago` multidata file - that is
   what gets hosted.

To check the world loaded, the generator prints a line per game; look for
`Creeper World 4` with its version and location count.

## Yaml options

Every option has a default, so a yaml that names none of them generates a
sensible seed. The ones most worth setting:

| Option | Default | What it does |
|---|---|---|
| `missions_for_finale` | 12 | How many other missions must be completable before the finale can be won. 0 disables the gate. Maximum 19 |
| `logic_difficulty` | standard | `standard` assumes only what is needed to WIN. `casual` also assumes a sniper or missile launcher from We Were Never Alone onward, so anti-air arrives earlier |
| `starter_missions` | 2 | How many missions start unlocked, drawn from those whose cache needs no weapon. Range 2 to 6. The minimum was 1 until 2026-09-03: one starter is a one-location opening, and about one seed in 400 failed to generate from it, so the floor is now 2 |
| `early_weapon` | random | Which of Cannon and Mortar is guaranteed to arrive first, in the very first sphere. `mortar` is the slower opening, `cannon` the brisk one, `random` picks per seed. It does not affect when the OTHER weapon arrives - that is about two thirds of the way in either way |
| `trap_percentage` | 50 | Share of the non-progression slots that are traps. **50 is a lot in a solo game** - lower it if they grate. 0 removes them |
| `progressive_erns` | 4 | How many Progressive ERN items go in the pool. ERNs are never required, so this is purely pool budget. Range 0 to 40 |

Finer tuning, all optional: seven `trap_weight_*` options (default 100 each)
set the relative frequency of the individual traps; `energy_storage_step` and
`energy_storage_decay` shape the storage upgrades; `base_generation_start` and
`base_generation_ramp` shape the generation upgrades; and the `filler_*_weight`
options split the leftover slots between energy storage and base generation.
(`filler_build_limit_weight` is still accepted but does nothing - build limits are
not generated, because every building starts unlimited so there is no limit for a
"+1" to raise.)

Fractional values travel as TENTHS because Archipelago ranges are integers - so
`base_generation_start: 5` means +0.5 energy per second. Each option's own
description in the yaml template says which unit it uses.

## Connecting

Use the panel on the main menu: server address and port
(e.g. `archipelago.gg:38281`), slot name, password if the room has one.
On connect, mission availability and unit unlocks update to match your
received items. Progress made while playing is sent live. The panel appears
only on the main menu; the level-select screen shows a small connection line.

## Configuration

Settings live in `BepInEx/config/com.droha.cw4archipelago.cfg` (created on
first launch) and are also editable from the in-game panel:

- `[Connection]` Host, Port, Slot, Password, AutoConnect - connection details;
  AutoConnect joins automatically at the main menu.
- `[Missions]` ShowSpan - show the SPAN Experiments button (off by default;
  the randomizer covers the 20 Farsite missions).
- `[Debug]` DebugCommands - a file-command channel for testing; leave off.

Your received items and checked locations are cached per slot under
`Documents/My Games/creeperworld4/archipelago/`, so a brief disconnect keeps
your unlocks and re-sends any checks when the server returns.

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

**Your Farsite saves need one extra step, because the mod moves them.** To keep
one seed's saves from showing up in another, the mod treats
`Documents/My Games/creeperworld4/saves/farsite` as a slot-specific folder: on
connecting to a different slot it moves the current contents into
`Documents/My Games/creeperworld4/archipelago/save-archive/<slot>/` and restores
that slot's set. Your original, pre-mod campaign saves are archived under the key
`vanilla`.

So if you uninstall while an Archipelago slot is active, `saves/farsite` holds
that slot's saves and your vanilla campaign is sitting in the archive. To get it
back, move the contents of `archipelago/save-archive/vanilla/` into
`saves/farsite/`. Nothing is ever deleted and every switch is reversible - but
the archive folder is the only place to look for saves that seem to have
vanished.

Campaign PROGRESS (`mcs.dat`) is genuinely untouched: the mod drives the mission
map's display from Archipelago state instead of editing it.
