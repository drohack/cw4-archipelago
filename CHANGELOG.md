# Changelog

Versions follow semantic versioning. The plugin and the apworld share one number,
so a release is a matched pair - if you update one, update the other.

## v0.1.1 - Archipelago conventions

No gameplay changes; the mod DLL is functionally identical to v0.1.0. This adds
the "encouraged features" from Archipelago's own `adding games.md` that the world
was missing, all of which are things a PLAYER touches:

- **Item and location name groups.** A group name works anywhere an item or
  location name does, so `!hint Traps` now works, and a yaml can say
  `non_local_items: [Units]` or `exclude_locations: [Tower of Darkness]` instead
  of listing two dozen names. Groups: Mission Unlocks, Units, Weapons, Economy,
  Traps, Build Limits, Upgrades; and per mission, plus Caches, Totems, Nullify
  Targets, Reclaim, Custom Objectives and Mission Completions.
- **Option groups.** Eighteen options in one flat list is a wall. The webhost now
  shows Goal and Logic first, with Traps, Item Pool and Energy Upgrades collapsed.
- **Option presets**: No traps, Relaxed, Short campaign. Each is a complete
  answer you can generate from, not a hint.
- **A bug report link** on the webhost page.

Versions stay matched: use the v0.1.1 apworld with the v0.1.1 mod.

## v0.1.0 - first public release

The first release of the Creeper World 4 Archipelago randomizer. Everything below
is new, so rather than a change list this is what the release actually does.

### The randomizer

- The 20-mission Farsite Expedition campaign, opened up. Any mission whose unlock
  you hold is playable in any order, and you can enter a mission you cannot yet
  finish and still collect the checks you can reach.
- **236 locations.** Every info cache, totem and nullifiable structure is its own
  check - 203 of those - plus reclaim objectives, custom objectives and mission
  completions. Optional objectives count.
- **Goal: beat Founders.** Reaching it is not enough: the finale is unwinnable
  until you have completed a number of other missions, 12 of 19 by default. Its
  objective panel says so on screen and the planet reads as locked.
- **Ever After is playable.** The campaign hides its twentieth mission behind a
  cutscene and never puts it on the map; the mod places it beside Wallis.
- Per-mission logic derived from a manual playthrough of the campaign, with a
  `casual` tier that brings snipers and missile launchers forward.
- Starter missions are random, drawn from the missions whose cache can be
  collected with no weapon.

### Items

Mission unlocks, unit unlocks (including Airship, Bertha and Sweeper, which the
campaign never grants), progressive ERNs, build-limit increases, energy storage
and base generation upgrades, and six traps.

Traps are 50 percent of the filler pool by default, which is a lot in a solo
game - set `trap_percentage` lower if they grate. Every trap is temporary and
recoverable by design; none can make a mission unwinnable.

### In game

- Connect from the main menu, with auto-connect and reconnect. Checks made while
  disconnected are re-sent when the server comes back.
- Items apply live: unit unlocks appear mid-mission, ERNs spawn, missions unlock.
- The mission map is coloured with the Archipelago tracker convention - red not
  reachable, yellow reachable but out of logic, green in logic, grey done - and
  locked planets keep the game's own "?" instead of opening a dead popup.
- Server messages and chat appear in a scrollable box during a mission, filtered
  to what concerns you by default, with an input row for chat and `!commands`.
- Saves are isolated per slot, so a save from one seed never appears in another.

### Known limitations

- **No full playthrough of a generated seed has been done yet.** Everything is
  verified in slices - 104 unit tests, 114 world tests, in-game batteries and
  hands-on checks - but trap frequency, energy-item pacing and how early the
  casual tier lands are unproven in practice. Feedback on those is the most
  useful thing you can send.
- Emitter Overdrive exists as an effect but is not generated: it does nothing on
  missions without emitters, and a trap that silently does nothing is worse than
  no trap.
- SPAN Experiments (26 missions) are not included.
- Windows only, and tested against the current Steam build.

### Installing

See the README, or `docs/installation.md` for the long version. In short: BepInEx
6.0.0-pre.2 into the game folder, run the game once, unzip the mod, and put
`cw4.apworld` wherever your Archipelago install keeps its custom worlds.
