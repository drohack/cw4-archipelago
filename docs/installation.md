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
sensible seed. All 24 are listed below; the yaml template shipped with the
release carries each option's full description, and this table is the summary.

**The ones most worth setting:**

| Option | Default | Range | What it does |
|---|---|---|---|
| `missions_for_finale` | 12 | 0 to 19 | How many other missions must be completable before the finale can be won. 0 disables the gate |
| `logic_difficulty` | standard | standard, casual | `standard` assumes only what is needed to WIN. `casual` also assumes a sniper or missile launcher from We Were Never Alone onward, so anti-air arrives earlier |
| `starter_missions` | 2 | 2 to 6 | How many missions start unlocked, drawn from those whose cache needs no weapon. The minimum was 1 until 2026-09-03: one starter is a one-location opening, and about one seed in 400 failed to generate from it, so the floor is now 2 |
| `early_weapon` | random | mortar, cannon, random | Which of Cannon and Mortar is guaranteed to arrive first, in the very first sphere. `mortar` is the slower opening, `cannon` the brisk one. It does not affect when the OTHER weapon arrives - that is about two thirds of the way in either way |
| `trap_percentage` | 50 | 0 to 100 | Share of the non-progression slots that are traps. **50 is a lot in a solo game** - lower it if they grate. 0 removes them |
| `progressive_erns` | 4 | 0 to 40 | How many Progressive ERN items go in the pool. ERNs are never required, so this is purely pool budget |

**ERN port upgrades.** Percentages are whole percents. The defaults are
measured values, not guesses - see `docs/ern-upgrade-measurements.md`.

| Option | Default | Range | What it does |
|---|---|---|---|
| `ern_upgrade_copies` | 4 | 0 to 4 | Copies of each of the twelve ERN port upgrade items (a Rate and a Cap for each of six upgrades), so the default puts 48 in the pool. 4 is the ceiling, not a preference: the fourth copy is the one that lands on the maximum |
| `ern_rate_max` | 400 | 100 to 800 | What four Rate copies are worth as a percent of the game's own fill speed. 400 turns 3600 ticks to full efficiency into 900 |
| `ern_cap_max` | 200 | 100 to 400 | How far four Cap copies raise an upgrade's ceiling. 200 is double the game's own |
| `ern_cap_max_build_speed` | 150 | 100 to 400 | The Cap maximum for BUILD SPEED only, which needs its own value because the game shortens build time steeply and non-linearly |

**Energy upgrades.** Both curves are capped, so the copy count IS the number
generated - there is no spare copy, because the per-copy step is the maximum
divided by the count.

| Option | Default | Range | What it does |
|---|---|---|---|
| `energy_storage_max` | 200 | 0 to 900 | How much the rift lab's energy STORE grows at full stack. The lab's own store is about 100, so 900 is roughly 1000 total |
| `energy_storage_copies` | 8 | 0 to 36 | Progressive Energy Storage items in the pool. 200 over 8 is 25 each |
| `base_generation_max` | 10 | 0 to 100 | Energy per second the rift lab GENERATES at full stack - income, not store. CW4's own production is about 3 to 4/sec, so 10 roughly triples the economy |
| `base_generation_copies` | 8 | 0 to 36 | Progressive Base Generation items in the pool. 10 over 8 is 1.25 each |

**Trap weights.** Six options, `0 to 100`, all defaulting to 100, setting the
relative frequency of each trap within the `trap_percentage` share:
`trap_weight_spore_strike`, `trap_weight_spore_scatter`,
`trap_weight_creeper_surge`, `trap_weight_energy_drain`,
`trap_weight_unit_stun` and `trap_weight_ammo_drain`. A seventh,
`trap_weight_emitter_overdrive`, exists but is inert - see below.

**Four options are accepted but do nothing.** They are kept so that a yaml
naming them stays valid rather than erroring:

- `trap_weight_emitter_overdrive` - Emitter Overdrive is not generated. It does
  nothing on missions that have no emitters, which is a third of the campaign,
  and a trap that silently does nothing is worse than no trap.
- `filler_energy_storage_weight`, `filler_base_generation_weight` and
  `filler_build_limit_weight` - filler counts come from the `*_copies` options
  above, not from weights. Build limits are not generated at all, because every
  building starts unlimited so there is no limit for a "+1" to raise.

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
