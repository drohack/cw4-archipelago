# Changelog

Versions follow semantic versioning. The plugin and the apworld share one number,
so a release is a matched pair - if you update one, update the other.

## v0.2.0 - The SPAN Experiments, experimental and off by default

### Build and release audit

An audit of the build, version and release path found sixteen defects, of which
three could reach a player. They are fixed here; the notes below are for anyone
who wonders why a release behaves differently than it used to.

- **The shipped apworld no longer carries the test suite.** Measured from the
  v0.1.10 artifact, `cw4/test/` was 96,709 of 237,705 uncompressed bytes - 41
  percent of the download, to run code that only ever runs in CI. Nothing had
  ever asserted anything about that file's contents.
- **The mod tells you when it does not match the seed.** The plugin and the
  apworld ship as a matched pair and three documents say so, but nothing checked
  it at play time, so a mismatched pair connected cleanly and desynchronised in
  silence. It warns now; it does not refuse, and it says nothing for a seed
  generated before this release.
- **Cutting a pre-release no longer leaves the repository in a broken state.**
  The automatic version bump was skipped for pre-releases while the rule it
  depends on was not, so publishing one left every subsequent push failing CI.
- Packaging can no longer produce an empty mod, ship a stale apworld, overwrite
  your installed copy of the mod, or leave a mixed set of assets behind; and the
  version can no longer be rewritten backwards onto a number that has shipped.

None of this changes how a seed generates or plays. `tools/check-release.py` is
the new gate, and every one of its rules was proved to fail on a deliberately
broken tree before being trusted.


**This is a pre-release.** The feature below is off unless you turn it on, and a
seed that leaves it off plays exactly as v0.1.10 did: the campaign's 236 location
ids did not move, a test now fails if they ever do, and the roster of a
campaign-only seed is still story1 to story20 in order, so the level-select
retarget is a no-op there.

One thing DOES change for every seed: the world's own progression fill retries up
to 8 times rather than 5 (see the last entry). That can only turn a failure into
a success, and no released seed ever hit the old limit.

- **`span_missions`: the game's 26 SPAN Experiment maps can be mixed into a
  seed.** The level select still holds 20 planets, the goal is still Founders,
  and the other 19 are now drawn from the 19 remaining campaign missions and the
  26 SPAN maps together. Locked slots still show the native "?" and still cannot
  be clicked into a dead popup.

  The planets themselves are retargeted rather than added: each of the twenty
  authored positions in the spiral is pointed at whichever mission the seed drew
  for that slot, which keeps the map people already know, keeps the lines
  between the planets, and keeps every tracker and gate working by title exactly
  as they did.

  Founders keeps the nineteenth position it has in the untouched campaign, and
  everything else fills the spiral in mission order. That is cosmetic - missions
  are open, so any unlocked one is playable in any order - but a plain sort put
  the goal in the middle with ten maps drawn after it, because the SPAN maps
  number 21 to 46 and all sort past Founders at 19.

  **The logic for those 26 maps was DERIVED, not played,** and that is the whole
  reason this ships off by default. Every campaign requirement in this
  randomizer traces to somebody finishing the mission and writing down what they
  needed. For SPAN, three detectors were built instead - one for whether an
  objective needs a mover, one for whether a cache is buried, one for what a
  totem is authored to want - and each was validated against the campaign first,
  28 assertions with 0 failures. That found 23 of the 26 maps need the Factory
  for their totems, 3 need a mover, and exactly one map in the whole set has an
  item cache at all.

  What no measurement can see: creeper advancing over a route that was open at
  tick zero, reach problems that are not about terrain (Archon is 100 percent
  land and still needs a Pylon), and environmental hazards (Archon again - it
  rains). So the logic over-requires rather than under-requires, and a seed
  should be harder than it needs to be rather than impossible.

  If you play these maps, `docs/design/span-requirements-worksheet.md` is
  pre-filled with every measurement and blank where only play can answer.
- **The finale now counts SPAN completions**, and locked SPAN maps really are
  locked. `MissionGate` used to let anything that was not a `storyN` specifier
  through untouched, so a SPAN map in a seed would have been launchable whatever
  Archipelago said; a map the seed does NOT contain stays freely playable,
  because it is the game's own content rather than part of the randomizer.
- **Checks send from SPAN maps.** The watcher resolved a running mission by
  parsing `storyN` out of the live specifier and returned 0 for anything else,
  and a mission of 0 makes it return immediately - the map would have played
  perfectly and sent nothing.
- **A mixed roster never opens narrower than the campaign does.** Found by
  measurement rather than by reasoning, and it was a real defect: with the retry
  cap raised to 25 so the true tail was visible, 1 seed in 10,000 needed SEVEN
  attempts at the world's own progression fill, against a shipped cap of 5 and a
  campaign worst case of 4 in 20,000.

  Correlating retry depth against seed shape over 4,000 more seeds found exactly
  one driver, and it was not the one expected: not the opening width, but
  WEAPON BREADTH - how many of the whole roster's checks the first weapon opens.

  | retry depth | seeds | mean weapon breadth |
  |---|---|---|
  | 1 | 3,888 | 10.5 |
  | 2 | 98 | 8.9 |
  | 3 | 13 | 8.0 |
  | 4 | 1 | 5.0 |

  The campaign's breadth is 9, concentrated in five missions, and a campaign
  seed always contains all five. SPAN carries 16 more across five maps, so a
  mixed roster averages MORE than the campaign - the problem is variance:
  drawing 19 from 45 can miss nearly all ten, and 6 of those 4,000 seeds had
  zero breadth, meaning the first weapon opened nothing anywhere in the seed.

  Two fixes. The roster now swaps breadth in until it reaches the campaign's own
  9, stated as "never narrower than the campaign" rather than as an invented
  constant. And the early mission grant, which used to draw only from the
  starter-eligible set and silently granted nothing on 1.1 percent of mixed
  seeds when that set was empty inside the roster, now falls back to the rest of
  the roster.

  Re-measured over 50,000 seeds with the cap at 25: 0 failures, the outlier
  gone, and SPAN sitting exactly where the campaign sits. The worst
  configuration still reaches depth 5, so **the retry cap goes from 5 to 8** -
  a cap equal to the deepest thing observed is no margin at all, and the extra
  attempts cost nothing because an attempt only happens when the last one
  failed. Full numbers in
  [docs/design/2026-09-14-fill-reliability.md](docs/design/2026-09-14-fill-reliability.md).
- **Known gap: the shipped cache-cell table does not cover SPAN.** One map in
  the 26 has a cache at all (Far York Farm), and the watcher falls back to
  counting for it, which is exactly right when there is only one cache to tell
  apart. A SPAN map with two would need its cells added to `MapCells`; none has
  two.
- **The opening always keeps a campaign mission.** One map in the whole SPAN
  roster has a cache, so SPAN cannot reliably supply an opening at all, and
  whether creep covers that one cache is unverified. The first starter is
  therefore always drawn from the campaign.

## v0.1.10 - Founders opens up, and ERN upgrades stop arriving dead

- **Founders: a Terp crosses the chasm, not just a Platform.** The enemy builds
  a land bridge to the starting island; the only thing wrong with it is that it
  is too bumpy to place weapons on, and a Terp fixes that. Every Founders rule
  that meant "you have to leave the starting island" now accepts either route -
  the nullify targets, the three totems across the chasm, and the custom
  objective.

  The item cache dropped its Platform requirement entirely. It already needs a
  Terp to dig the item up, so once a Terp does the crossing the Platform clause
  can never fail: anyone who can dig it out can get to it.

  A Terp route does not pay for the greenar chain a Platform needs, which the
  generated logic table now shows as `Factory or Terp`.
- **ERN upgrades: half as many, and no weaker.** 45 percent of ERN upgrade
  items were arriving before the ERN Portal that makes them work - the portal
  gates nothing in logic, so it is placed at random and landed at median sphere
  11 of about 18. Measured over 40 seeds that was 21.6 dead-on-arrival items per
  seed out of 48, and a worst seed of 47.

  `ern_upgrade_copies` drops from 4 to 2, so 48 items become 24, and the freed
  slots go to the energy upgrades - `energy_storage_copies` and
  `base_generation_copies` rise from 8 to 20 each, turning 16 items into 40.
  The pool is the same size; 24 items that might do nothing for half the run
  became 24 that pay out the moment they arrive. Dead-on-arrival falls to 10.3
  per seed, worst case 23.

  **The ERN maxima did not move.** The copy count now behaves like the energy
  one: the per-copy step is the maximum divided by the count, so two copies is
  two bigger steps rather than half the power - 200 percent efficiency in steps
  of 50, 400 percent rate in steps of 150.

  This is a fix, not just a rebalance. The plugin used to divide by a hardcoded
  4 because the pool's copy count was never sent to it, so lowering this option
  silently lowered the CEILING instead of coarsening the steps: two copies
  capped efficiency at 150 percent and rate at 250. The count now travels in
  slot data. **A seed generated before this release is unaffected** - it sends
  no such key and the plugin falls back to 4, which is what those seeds were
  built with.
- **The ERN Portal itself is, and always was, in every seed.** Confirmed after a
  report of never seeing one: it is an ordinary unit unlock, one copy per seed,
  never retired and never precollected. Two playthroughs that did not reach it
  had it behind a check that was not done.
- **Fixed: generation errors in CI.** The test suite could fail with
  `FillError: No more spots to place 25 items` on roughly 1 seed in 1,000. No
  seed was ever unfillable, and no released seed was affected - the tests were
  generating down a path no player uses.

  The world places its own progression during `pre_fill` and retries, because
  Archipelago's fill is greedy and caps backtracking at two swaps per item, so
  it can give up on an arrangement that exists. The test base switches that off
  so access assertions keep working against an untouched pool, and it was doing
  so for the inherited `test_fill` as well - the one test that is about
  generating rather than about the rules.

  Measured over the same seeds: 0 failures in 16,000 with the world's own fill
  on, 3 in 3,000 with it off. All three of those failures had the same shape, a
  two-location opening that did not chain, and all three were recovered on the
  first attempt when re-run with the fill on. A mixed multiworld, which takes
  neither defence, was clean over 1,000 seeds - our progression can live in the
  other world's locations, which is slack a solo seed does not have.

  `test_fill` now generates the way a real seed generates, and a new test fails
  if that is ever switched back.

## v0.1.9 - the log reaches the bottom, and two missions read true

- **Fixed: the message log opened part-way up.** Third time asked, first time
  actually fixed. Scrolling the log switched off its own auto-scroll: the
  scrollbar treats any movement as you dragging it, and it moves by itself
  whenever the log grows - so on a full log the mod concluded you had scrolled
  away and stopped following, leaving the view in the middle. It now keeps up
  with the layout for as long as the layout is moving, however long that takes.

  The two previous attempts both guessed how many frames the text takes to lay
  out, and both guesses were far too short once the log is full. The test that
  passed them was running against a log with eight lines in it; it now fills it
  to 150 first.
- **Not My Mars is re-graded.** v0.1.8 demanded a Pylon, Porter or Platform and
  nothing could satisfy it, so the whole planet went red the moment it unlocked.
  It now reads:

  | held | colour |
  |---|---|
  | weapon only | red |
  | weapon + Miner | yellow |
  | weapon + Miner + Platform | yellow |
  | weapon + Miner + Pylon or Porter | green |

  The Miner is required outright - without one there is no power on the far
  island. Crossing the gap is not: the rift-lab hover gets you over, and so does
  a Platform, but both are hard enough that logic will not assume them, so they
  show yellow rather than green. Its cache is still free, so the mission can
  still open a seed.
- **Fixed: Founders showed green with no way to cross the chasm.** Only the two
  totems on the right-hand starting island can be reached with the factory
  alone; the other three need a platform, and the totems were the one objective
  on that map not asking for one - so the planet read green while holding no
  Platform, Porter or Pylon at all. The platform is now required on those three
  and not on the other two.

The message-log fix is in the mod, so updating the mod is enough. The **Not My
Mars and Founders** fixes are apworld logic and take effect in **newly generated
seeds** only - an existing seed carries its requirements in its slot data and
will keep showing the old colours.

## v0.1.8 - checks that belong to the right structure

**Checks now belong to the structure you completed, not to how many you have
completed.** Nullify the hard enemy first and you used to get `Nullify 1`; the
Nth thing finished simply sent the Nth check. That covered 203 of the 236
locations. Each structure is now identified by its position on the map, so
`Nullify 3` is always the same enemy, `Totem 2` always the same totem, and
`Cache 1` always the same cache.

This matters beyond tidiness, because different instances carry different
requirements - and those requirements had been attached to whichever structure
happened to be finished in that slot. Re-derived from the map with the designer:

- **Sequence**'s 14 nullify targets split four ways: the two top-left creep
  emitters need only a weapon, the five under the dark tower need the beacon, the
  four bottom-right ones are tower-reachable, and the three top-right ones take
  the beacon or a mover. The two easy ones turn out to be instances **6 and 11** -
  in the middle - which is exactly what the old "the first N are easy" table could
  never express.
- **Sequence**'s two caches are no longer both treated as buried: only the
  right-hand one needs the Terp, and the top-left-middle one needs just a weapon.
- **Shattered**'s mover requirement is pinned to the actual top-left totem beside
  the nullify targets.
- **Farsite**'s free cache is pinned to the one nearest the rift lab.
- **Wallis** needs a **Cannon** specifically - Mortar-and-Sniper is not a
  beatable seed there any more - and its Miner is now "reachable but not
  promised" rather than required. Its old "the first 2, maybe 4, are easier"
  tier is withdrawn; all nine targets share one rule.

Caches needed one extra step. Collecting one destroys it, and units carry no id,
so the game cannot be asked afterwards which cache you took. But a cache's
position is fixed map data, so the mod now simply knows where all 20 of them are
and works out which is missing - meaning even a save from before this update
identifies its caches correctly.

If you are mid-mission on an existing seed nothing breaks: location names and ids
are unchanged, and a check you already banked stays banked.

**The traps are renamed, and old seeds will not fire them.** Every trap now ends
in `Trap` - the convention 26 of the 36 trap-using worlds follow - and `Creeper
Surge` is now **Rift Breach Trap**, because a surge sounds like emitters ramping
up and what it actually does is drop a slab of creeper next to the rift lab.
There are no aliases: a seed generated before this sends the old name, the mod
does not recognise it, and its traps quietly do nothing. Regenerate to keep
traps working. Item ids did not move, and the yaml keys did not change either -
`trap_weight_creeper_surge` still sets the Rift Breach weight, so existing yamls
stay valid.

- **Fixed: an unreachable server stopped being retried after about four
  attempts, silently.** Measured against a dead port: the backoff fired four
  times and then went quiet for good, so a player who started the game before
  their server was up would sit disconnected forever with nothing in the log to
  explain it. Three things were wrong - each failed attempt leaked its
  connection, the retry competed with itself for thread-pool threads, and a
  single hung connect could end the chain. It now runs indefinitely (verified to
  nine attempts over seven minutes) and abandons any attempt that hangs rather
  than waiting on it.
- **Fixed: a second cause of the same thing.** The retry had two pieces of state
  and a connect re-armed only one, so a connect landing while a retry was still
  waiting - which the menu-entry auto-connect does on its own - left the client
  failed with nothing scheduled. Reintroduced in v0.1.6 by the guard that fixed
  the swallowed CONNECT click.
- The log now says when a retry actually FIRES, and why it did not. It only ever
  recorded what had been scheduled, which is why a chain that had stopped looked
  exactly like one still waiting.
- **Fixed: a location checked by someone else did not repaint the map.** An admin
  `/send_location`, a `!collect`, or another client on your slot only showed up
  after a reconnect. Reported from play on v0.1.7.
- **Fixed: the message log opened part-way up** instead of on the newest line
  when you entered a mission with history behind you.
- Two missions showed checks as reachable that were not: **Not My Mars** needs a
  Pylon, Porter or Platform to move liftic to the totems and reach the enemies,
  and **Shattered**'s third totem needs a Porter or Platform to cross space.
  Platforms and beacons also run on liftic, so both now require the refinery and
  factory before they count towards anything.

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
