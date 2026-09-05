# Changelog

Versions follow semantic versioning. The plugin and the apworld share one number,
so a release is a matched pair - if you update one, update the other.

## v0.1.7 - checks that actually arrive

Six fixes, all found by playing. Two of them were losing checks silently, which
is the worst way for a randomizer to be wrong: nothing errors, you just quietly
do not get your items.

- **Nullify checks never worked.** Not on a fresh mission, not on a loaded save,
  not in any version. Every nullify target you destroyed should have been a
  check and none of them were. The mod watched the game's list of nullifiable
  structures and waited for it to shrink; that list never shrinks, and a
  nullified structure is not destroyed - it is marked SUPPRESSED and stays
  exactly where it was. Progress is counted from that mark now. If you have
  nullified targets in an existing save, loading it sends the checks you are
  owed.
- **Fixed: completing a mission did not send its counted checks.** If the live
  count missed something, a safety net was meant to send every required
  objective on completion - but it looked up the wrong name for nullify, totems
  and caches ("Home - Nullify" instead of "Home - Nullify 1"), so it silently
  skipped all three and only ever worked for Reclaim and Custom.
- **Fixed: granted ERNs spawned inside the terrain.** They were placed beside
  the rift lab at the LAB's height rather than the ground's, so anywhere the
  ground rises they ended up buried.
- **Fixed: the connect button said DISCONNECT while it was failing to connect**,
  offering to end something that had never started. It now reads CONNECT, CANCEL
  while connecting or retrying, and DISCONNECT only when actually connected -
  and CANCEL stops the retries, which previously you could only escape by
  quitting the game.
- **Fixed: pressing CONNECT could do nothing at all.** A retry attempt already
  in flight made the button ignore the click for up to 40 seconds. It never
  ignores it now, and a slow attempt can no longer reconnect you after you
  pressed DISCONNECT.
- The objective dump in the log reported no locations for every counted
  objective, which was the same wrong-name bug making the diagnostic lie in the
  exact case it exists to explain.

Nothing about generation changed, and no item or location names changed, so a
v0.1.6 seed keeps working.

## v0.1.6 - offline play, and disconnects that behave

Disconnects, offline play, and what survives reconnecting. Four bugs, all of
which lost or duplicated progress silently, and none of which any test could
see - the decision they lived in sat in the plugin where nothing could reach it.
It is now `SessionReconcile` in Core with nine tests, and `tools/offline-test.sh`
covers the whole area in game (25 assertions).

Written up with the Archipelago citations behind each rule in
[docs/design/2026-09-04-offline-and-disconnects.md](docs/design/2026-09-04-offline-and-disconnects.md).

- **Fixed: checks could cross from one multiworld into another.** The guard on
  replaying queued checks compared the slot NAME only, with no seed. Location
  names are identical across seeds, so a check earned in one multiworld was
  accepted by the next one you joined under the same name as a genuine check
  there. Archipelago's reference client keys a session on (seed, slot) for
  exactly this reason.
- **Fixed: beating the finale while disconnected never counted.** The goal was
  queued and saved correctly, then discarded on the next connect and never
  sent. It is now replayed, and it says so in the log when it queues.
- **Fixed: every trap and boon fired again on every connect and every launch.**
  Connecting re-delivers your whole received-items list, and the high-water
  mark that stops it replaying was missing from the save file entirely. At the
  default 50 percent trap share, one reconnect could mean dozens of traps.
- **Fixed: DISCONNECT did not stay disconnected.** The mod reconnected a few
  seconds later, because the socket-close event arrives more than once and only
  the first was recognised as intentional.
- **The game is playable with the server down.** It comes up on the last slot
  you played, using its cache, and the missions you have unlocked are playable.
  Checks queue and are sent when you next connect. Previously an unreachable
  server meant every mission locked and nothing playable at all, with a
  complete cache sitting on disk.
- **Reconnect keeps trying.** It backs off 5, 10, 20, 40 then 60 seconds and
  stops only when you disconnect deliberately, matching the reference client
  (which is unbounded; the 60-second cap is ours). It used to give up after
  three tries in 30 seconds, so a host restart left you offline until you
  noticed.
- **A wrong slot name or password is reported instead of retried.** Retrying
  cannot fix an answer the server has already given.

## v0.1.5 - a smaller mod to install

No gameplay changes. This is a structural release: the mod players install got
30 percent smaller, and the yaml documentation now matches the options that
actually exist.

- **The debug and measurement channel is no longer part of the mod.** It was
  2,651 of 8,792 lines - a file-command channel and two measurement probes,
  used only by the test harnesses in `tools/`. It was gated at runtime, so it
  never did anything in a normal game, but it was still compiled into what
  players downloaded. It now lives in a separate plugin that ships in no
  release, and the `DebugCommands` config key is gone with it.
- **Fixed: the yaml options documentation.** `docs/installation.md` described 7
  of the 24 options and named four that do not exist - `energy_storage_step`,
  `energy_storage_decay`, `base_generation_start` and `base_generation_ramp`,
  all replaced by max/copies pairs some releases ago. Anyone copying that table
  got options the generator silently ignores. All 24 are now documented, with
  ranges and defaults read from the options themselves, and the four that are
  accepted but do nothing say so.
- Dead code removed: the throwaway research probe (1,897 lines), fourteen
  one-off measurement harnesses, and four unused symbols.

## v0.1.4 - release plumbing

Gameplay is identical to v0.1.3 - every logic, tracker and generation fix
shipped there. This release exists so the published artifacts match main
exactly.

- CI fails any push where the version has already shipped, so drift is caught on
  the push that causes it rather than at the next release. v0.1.1 and v0.1.2
  both drifted for days without anything noticing.
- Publishing a release now bumps the version automatically.
- The plugin logs the commit it was built from, so a released build and a local
  one that share a version number can be told apart from the log.

## v0.1.3 - honest map, tighter logic, seeds that always build

- **Generation.** Solo seeds used to fail to build about once in 18,000 -
  loudly, writing no seed at all. The world now places its own progression and
  retries on failure, the way oot and pokemon_emerald do. Measured 0 failures in
  16,000 seeds, with no unreachable or unbeatable seeds. Multiworld is
  untouched. This supersedes v0.1.2's `bootstrap_opening`.
- **The map tells the truth.** Red now means a check cannot be reached, yellow
  means reachable but out of logic, grey means done. Farsite's free first cache
  reads green as it always should have, and its skull is drawn as a totem,
  because lighting the totems is what the mission actually asks.
- **Logic, from playing the campaign.** Totems on Shattered, Wallis and Founders
  need the greenar chain. Reclaim needs a Nullifier everywhere. Not My Mars and
  Ruins Repurposed need a Miner. Serious, Sequence, Wallis and Ever After have
  real requirements they did not have before, some of which escalate across a
  mission's objectives. Two seeds were soft-locked before these corrections.
- Greenar Refinery is retired - the Factory now unlocks it too. There was never
  a reason to have one without the other.
- `starter_missions` now starts at 2. One starter could not be made to generate
  reliably.
- A mission's own grant of a locked unit is refused at source, rather than the
  build strip being rebuilt to remove a button that should never have existed.
- The CONNECT button becomes DISCONNECT once connected.

## v0.1.2 - matching mod and apworld

Use the mod and the apworld from this release together. v0.1.1's assets were
built before twelve commits that renamed every progressive item, so a v0.1.1
seed and a v0.1.2 mod disagree about what the items are called and those items
silently do nothing.

- ERN port upgrades: all six measured, the efficiency cap and the ramp fixed,
  and their magnitudes are yaml options.
- Every progressive item is named "Progressive ...", so trackers group them.
- Ten filler items (ammo, energy, resource caches, field shield, six ERN
  surges), each proven in game; five that did nothing are fixed.
- The login panel picks its canvas deterministically and logs which one it got.
- The release build refuses to package if the version disagrees across files.
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
