# Changelog

Versions follow semantic versioning. The plugin and the apworld share one number,
so a release is a matched pair - if you update one, update the other.

## Unreleased

- **New option: `early_weapon`.** Cannon and Mortar are interchangeable in logic,
  so which one opens a seed was decided by the fill - a genuine coin flip,
  measured 10-10 over 20 seeds. This makes it a choice: `mortar` for a slower
  opening, `cannon` for a brisk one, or `random` (the default) to decide per seed.
  Whichever is chosen is guaranteed to arrive in the very first sphere - unless
  the opening is a single location, where the slot goes to a mission unlock
  instead. See the fix below; that is not a nicety, it is what stops the seed
  failing to generate.
- **Fixed: `starter_missions: 1` often failed to generate.** About 12 percent of
  one-starter seeds died with `FillError: No more spots to place 1 items`. Every
  one of them was winnable, so this was the fill giving up rather than a logic
  error. The world was asking for two early items - a mission unlock and a weapon
  - when a one-location opening can only hold one, and Archipelago was picking
  between them arbitrarily. An unlock chains to the next mission; a weapon does
  not, leaving nineteen unlocks to thread through a single mission. Requesting
  only the unlock at that width removed that failure shape, but left a rarer one
  at 1.3 percent where the fill spent a scarce slot on an item that opens nothing -
  a lone Factory, which is half of the Greenar pair.
  Both are fixed by `bootstrap_opening`: while the opening is too narrow to
  survive a wasted placement, the world places items itself, drawn at random from
  those that actually open something. **0 failures in 300 one-starter seeds**, and
  0 in 100 at the default. The opening stays random - only items that open nothing
  are excluded, and only while it is dangerous - and `early_weapon` is honoured
  here too, going first in 31 of 40 seeds.
  It runs only where it is needed: solo, or a multiworld where every player is
  playing Creeper World 4. With another game in the multiworld the funnel is not a
  single point of failure - the fill can park CW4 unlocks in that world and put
  that game's items in CW4's opening - so the bootstrap stands down and leaves the
  cross-game shuffle alone.
  What it changes is smaller than it first looks. The SECOND weapon lands about
  two thirds of the way into a seed whatever you pick - that is a property of an
  OR pair, and was already true before this option. Forcing buys an opening weapon
  in the first sphere instead of somewhere in the first four; the only real cost is
  the second weapon reaching the final sphere in 2 or 3 seeds of 20 rather than 0.
  `unforced` reproduces the old distribution exactly.

- **Build limit items are no longer generated.** Every building in CW4 starts at
  the game's "unlimited" sentinel, so there was no limit for a "+1" to raise and
  the item did nothing - on any unit, on any mission. At the default weights that
  was 24 items in a 256-item seed, roughly one check in ten paying out nothing.
  The three names keep their ids, so existing seeds and clients are unaffected,
  and `filler_build_limit_weight` is still accepted in a yaml; it just has no
  effect. The "Build Limits" item group is gone, because a group that matches
  nothing is worse than a name that does not exist - a yaml naming it would
  appear to work.
  Setting a limit does work and is enforced; only raising an unlimited one does
  not. If limits are ever introduced deliberately, the item comes straight back.
- **Farsite can open a seed again.** Mission 1 had been excluded from the starter
  set because its two caches have different requirements - the first is free, the
  second needs a weapon - and requirements were per objective TYPE, which could
  not express that. They are now per instance where needed, so Farsite is
  eligible without claiming its second cache is free.
- The mod logs the seed's shape on connect (`AP SEED SHAPE: starters=[...]`).
  Which missions start unlocked is decided per seed and was previously invisible,
  so "why can I only play these two?" had no answer anywhere.

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
