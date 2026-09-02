# Randomizer audit

A repeatable measurement of what the randomizer actually generates, kept so that
progress is visible over time rather than re-derived from memory each session.

The presentable version is `docs/audit-report.html`, published as an Archipelago
Code artifact. The HTML is the source of that page and is versioned here so it can
be republished after a change; this file is the methodology and the results log.

## Running it

Prerequisites: an Archipelago clone at the repo root, with the apworld synced into
it (`powershell -File tools/ap-sync.ps1`). Every script is run from INSIDE the
clone, because they import Archipelago itself.

    cd Archipelago
    python ../tools/audit/audit.py                       # the whole battery
    python ../tools/audit/derive.py                      # arithmetic vs generated
    python ../tools/audit/depth.py     <label> <seeds> ["opt: val, ..."]
    python ../tools/audit/funnel.py    <seeds> <starters>
    python ../tools/audit/mixed.py     <label> <seeds>
    python ../tools/audit/repro.py     <seed> [starters]

Send stderr to its own file rather than merging it - Archipelago logs warnings
that will otherwise sit on top of the progress lines:

    python ../tools/audit/audit.py 2> audit.err

## What each script measures

| script | measures |
|---|---|
| `audit.py` | Static tables, cross-language agreement with the mod, in-process reachability, a 13-configuration generation matrix, pool quantities, and a weapon-timing sweep |
| `derive.py` | The pool computed in CLOSED FORM from `create_all_items`, checked against what really generated. This is the important one - see below |
| `depth.py` | True placement depth of named items, via `MultiWorld.get_spheres()`. Reports by item and by ROLE (opening weapon vs second weapon) |
| `funnel.py` | Hunts fill failures at a given `starter_missions`, then diagnoses the shape of the opening: what is reachable with nothing, with everything, and whether the goal is satisfiable |
| `mixed.py` | A multiworld with another game in it (ChecksFinder), to separate "our funnel" from "the fill's problem" |
| `repro.py` | One seed, with the whole `FillError` printed rather than its first line |

## Derived, sampled, and proven - they are not the same

Most of the pool is NOT an empirical question, and treating it as one wastes
effort and hides errors. `create_all_items` is straight-line arithmetic:

    real      = (20 - starters) + 21 units + 3 bonus + erns
    ernUpg    = 12 names * min(ern_upgrade_copies, 4)
    remaining = 236 - real - ernUpg
    traps     = remaining * trap_percentage // 100
    filler    = remaining - traps

The ERN upgrade block comes off the top like the real items, not out of the
filler remainder, because it is generated as FIXED counts before the trap split.
It has to be fixed: a fifth copy of a capped item does nothing, and the weighted
filler draw picks with replacement, so it could hand out nine copies of one name
and none of another.

`derive.py` runs that against all 12 single-player configurations and compares it
with real generated seeds. Currently 12 of 12 match exactly. Generation is the
CHECK on the arithmetic, not the source of it.

What genuinely has to be sampled:

- the split of filler between the two energy upgrades, and of traps between the
  six kinds - weighted draws, so expectation is computable but the draw is not
- the sphere any item lands in, which is the output of a randomized fill
- whether the fill SUCCEEDS

And one thing that is neither: reachability is checked exhaustively over the
region graph, every location against an all-items state. That is a proof for that
property, not a sample, and it should not be reported as "N seeds looked fine".

## A zero from an unverified harness is not a measurement

CI hit one `FillError` in `TestCasualLogic.test_fill`. Two things about that test
matter and neither is obvious: it draws a FRESH RANDOM SEED every run, so the
commit that passed before it proves nothing, and casual is the HARSHER setting
despite the name - `rules._casual_defense` ADDS a requirement from mission 6 on.

Hunting the rate produced the instructive part. A first sampling harness
reported **0/200** and "under 0.5 percent". Adding a positive control - force a
`FillError` and confirm the loop reports it - turned the same configuration into
**1/100**. Two independent reasons that zero was worthless:

- the harness had never been shown capable of reporting a failure at all, so
  "0/200" and "this loop does not run the test" look identical
- at a 1 percent rate, drawing zero from 200 happens about 13 percent of the
  time anyway

So: **before believing a zero, prove the harness can produce a one.**
`tools/audit/casualrate.py` now forces a failure before sampling and refuses to
print a rate if the control does not fire.

And for a sub-percent rate, sampling is the weak instrument. 0/800 after the fix
is only about 90 percent confidence; `tools/audit/bootstrapcheck.py` asks the
structural question instead - is the threshold 3 under casual and 2 otherwise,
does `needs_bootstrap` now fire, did `bootstrap_opening` place anything - and
answers it in two seeds rather than thousands.

## Measurement traps

Every one of these produced a confidently wrong number first.

0. **A cached measurement is not a measurement.** `derive.py` reads the
   generated side from `audit-quantities.json`, which a PREVIOUS `audit.py` run
   wrote. It therefore reported **12 of 12 match on the very run where 48 new
   items had appeared** and its own arithmetic had not been updated for them -
   comparing today's formula against last week's generation.

   It now models the ERN block and refuses to pass when the cache predates it
   ("STALE CACHE - rerun audit.py"). The same shape of bug bit the mod side the
   same week: a ceiling patch was verified four times by a probe that read the
   one accessor it had patched, while the game read a different one.

   Generally: if the thing you compare against can be older than the thing you
   changed, the comparison can pass for the wrong reason.

1. **The spoiler's Playthrough lists only items needed to WIN.** Counting weapon
   arrivals from it cannot see the redundant half of an OR pair at all, and
   reported the Cannon/Mortar opener as 13-7 when true placement depth says 10-10.
   Use `MultiWorld.get_spheres()`, which covers every filled location.

2. **Splitting a spoiler line on the LAST colon.** Location and item are separated
   by the FIRST one, and items contain colons - `Mission Unlock: Home`. The wrong
   split silently reported zero mission unlocks.

3. **One output directory plus `glob(...)[0]`.** `rmtree(ignore_errors=True)`
   leaves the folder when a file is locked, so the next read picked up the
   PREVIOUS run's archive: every configuration in the matrix reported the first
   configuration's numbers, and "no traps" and "all traps" both claimed 95 traps.
   Use a unique directory per generation and assert exactly one archive.

4. **Comparing by item NAME across configurations.** "Cannon with no forcing" opens
   half the seeds; "Cannon when Mortar is forced" never does. Comparing them shows
   a large regression where nothing moved. Compare by ROLE.

5. **`collect(item, prevent_sweep=False)` sweeps.** It picks up every other locked
   item that has become reachable, so measuring items one at a time silently
   double-counts. Pass `prevent_sweep=True` when the question is what a single
   item opens.

6. **Look at the screen.** An hour went into an ERN measurement that was running
   behind a full-screen A.D.A. Log modal - no map, no units, nothing visible -
   while the logs read like a slow ramp. One `shot:` screenshot showed it
   instantly. The mod has had a screenshot command the whole time. Reading a log
   is not observing the game.

7. **Start from a harness that already works.** `tools/cmod-traptest.sh` sets up
   the dev-tools cheats, waits for log acks, and boots cleanly. A new probe
   written from memory instead rediscovered every one of those as a bug: ghost
   units with no instant build, a paused sim, `spawn:riftlab` placing nothing
   because `riftlab` is the build-pane key and the data name is `commandbase`.

8. **Never edit a script bash is executing.** Bash reads a script by byte offset,
   so an edit mid-run makes it resume at garbage - "unexpected EOF" from a file
   that passes `bash -n`. Killing the GAME does not stop the SCRIPT either: a
   stopped-looking run kept writing into the shared command file and fired traps
   into the next session. `TaskStop` first, then edit.

9. **A quoted heredoc still mangles backslash escapes.** A backslash-n written
   through `python - <<'EOF'` has come out as a literal newline inside a string
   three times, each one a syntax error in a file that had just been reported as
   successfully patched. Use the Edit tool for anything containing escapes.
   This very entry was mangled the same way while being written, which is either
   the best possible evidence for it or the worst.

10. **Git Bash `/tmp` is not Windows Python's `/tmp`.** A backup written by `cp` was
   invisible to the script that read it, and four "different" experiment arms all
   silently ran the unmodified code and returned identical results.

## Results log

| date | commit | measurement | result |
|---|---|---|---|
| 2026-09-01 | `9406452` | Pool arithmetic vs generated, 12 configurations | 12/12 exact |
| 2026-09-01 | `9406452` | Locations, reachability, goal | 236 locations, all reachable, goal satisfiable |
| 2026-09-01 | `9406452` | Cross-language agreement with the mod | titles, final mission, objective slots, trap names all agree |
| 2026-09-01 | `9406452` | Default pool | 18 unlocks, 21 units, 3 bonus, 4 ERN, 95 traps, 95 energy, 0 build limits |
| 2026-09-01 | `9406452` | Classification | 31 progression, 110 useful, 95 trap, 0 filler |
| 2026-09-01 | `9406452` | Opening weapon, 100 seeds | 51 Mortar / 49 Cannon - a clean coin flip |
| 2026-09-01 | `9406452` | `starter_missions: 1` generation | **12 percent FillError** |
| 2026-09-01 | `4d9ce94` | `starter_missions: 1` after `bootstrap_opening` | **0 failures in 300**, 0 in 120 more after gating |
| 2026-09-01 | `4d9ce94` | Two CW4 players, 1 starter each | 0 failures in 40, bootstrap ran for all 80 player-worlds |
| 2026-09-01 | `4d9ce94` | CW4 + ChecksFinder | 0 failures in 60, 8 foreign items in CW4's opening (bootstrap stands down) |
| 2026-09-01 | `4d9ce94` | `early_weapon` honoured at one starter | requested weapon went first in 31 of 40 |
| 2026-09-01 | `4d9ce94` | Item arrival, 20 seeds, mean depth 13.6 | Cannon med 4 (29%), Mortar med 6 (35%), Sniper med 10 (67%), Terp med 9 (62%), Sprayer med 9 (75%), **Missile Launcher med 9 (75%)** |
| 2026-09-01 | `4d9ce94` | Energy Storage, 41 copies | 99 percent of its 250 ceiling by copy 21 - about 20 copies are dead |
| 2026-09-01 | `4d9ce94` | Base Generation, 54 copies | **313 energy/sec** against a natural 3-4/s. The ramp is the wrong shape at this count |
| 2026-09-02 | `7ef049c`+ | ERN upgrade items added: 12 names, 4 copies each | 48 of 236, fixed counts, every copy useful |
| 2026-09-02 | `7ef049c`+ | Pool arithmetic vs generated, 14 configurations | 14/14 exact, including both ends of `ern_upgrade_copies` |
| 2026-09-02 | `7ef049c`+ | Default pool | 20 unlocks, 21 units, 3 bonus, 1 ERN, 48 ERN upgrades, 71 traps, 71 energy |
| 2026-09-02 | `7ef049c`+ | `derive.py` stale-cache guard | caught its own false 12/12 pass |
| 2026-09-02 | `4637887` | CASUAL logic fill, 0.6.7, control-verified | **1/100 then 1/600 FillError** - long-standing, not new |
| 2026-09-02 | working tree | Same after `bootstrap_threshold` +1 for casual | 0/800, and the bootstrap provably engages |

## Open, from the numbers above

- The two energy upgrades are now 30 percent of the pool rather than 40, since
  the ERN block took 48 slots. Both curves are still wrong - storage saturates
  at copy 21 of 41, generation ramps to the wrong shape - so the rework still
  stands.
- ERN upgrade items are classified FILLER, not useful, because they do nothing
  until the ERN Portal unlock arrives and a portal is built with an ERN docked.
  Held loosely: Mine Production triples production once live, which is a large
  effect for something the fill treats as padding.
- Missile Launcher is the latest-arriving item in the game, because it appears in
  one rule that only applies under casual logic and is an OR with Sniper. Sprayer
  is second-latest for the same reason: it appears in no rule at all.
- No full playthrough of a generated seed has been done. Everything here is
  structural; none of it says whether the result is fun.
