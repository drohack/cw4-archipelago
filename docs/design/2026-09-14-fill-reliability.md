# Fill reliability: how to measure it, and what it currently is

Written 2026-09-14, after CI failed a test with
`FillError: No more spots to place 25 items`.

The numbers here will go stale after the next logic change. **The method will
not**, and that is the reason this document exists - so the next person
re-measures rather than inheriting a verdict.

## The question, and why the obvious measurement cannot answer it

Archipelago's `fill_restrictive` is greedy and its backtracking is hard-capped:
each item may be swapped at most twice, behind a three-entry state cache. So it
can give up on an arrangement that genuinely exists. A world cannot catch or
retry the MAIN fill, but it can place its own progression and retry, which is
what `items.place_own_progression` does, and what oot does for songs and
pokemon_emerald for badges and HMs.

It retries `OWN_FILL_ATTEMPTS = 5` times. A seed therefore only dies if all five
attempts fail, which is rare - and that rarity is exactly what makes it hard to
measure. A blind sweep reports "no failure in N seeds", which at a rate near one
in a million is almost no information: it cannot distinguish a safe margin from
a lucky run. Running 100,000 seeds does not fix this, it just costs more.

**Measure the DEPTH, not the failures.**

    P(a seed fails at cap 5) = P(a seed needs more than 5 attempts)

Raise the cap far above 5, record how deep each seed actually goes, and read the
answer off the distribution. About 4 percent of seeds retry at all, so the tail
is estimable from 20,000 seeds instead of needing millions. A cap of 5 is safe
exactly when the tail beyond it is empty AND the shape is falling fast.

## What it currently is

20,000 default seeds, cap raised to 25:

| attempts needed | seeds | share |
|---|---|---|
| 1 | 19,262 | 96.310% |
| 2 | 708 | 3.540% |
| 3 | 29 | 0.145% |
| 4 | 1 | 0.005% |
| 5 or more | 0 | - |

Zero FillError, zero seeds with unreachable locations, zero unbeatable.

**The shape is the finding, not the zero.** Each level is about 3.7 percent of
the one above it - 708/19,262 = 3.68, 29/708 = 4.1, 1/29 = 3.4 - which is the
same as the per-attempt failure rate p = 0.0369. That geometric decay is direct
evidence that the retries are near-independent, which is the assumption the
whole design rests on and which nothing had previously checked. Because it
holds, `p**5` is trustworthy rather than hand-waving: **about 1 seed in 14.6
million**, against 1 in 1,000 with no retry at all.

## Opening width is the only driver

A further 20,000 seeds, 4,000 each across five configurations a player can
actually select:

| configuration | opening | retried | deepest | over cap | FillError |
|---|---|---|---|---|---|
| default | 2 | 738 / 20,000 | 4 | 0 | 0 |
| casual | 2 -> 6 (bootstrap) | 0 / 4,000 | 1 | 0 | 0 |
| all traps | 2 | 143 / 4,000 | 4 | 0 | 0 |
| no traps | 2 | 145 / 4,000 | 3 | 0 | 0 |
| no progressive ERNs | 2 | 183 / 4,000 | 4 | 0 | 0 |
| six starters | 6 | 0 / 4,000 | 1 | 0 | 0 |

The two zero-retry rows carry the explanation. Casual reaches a 6-wide opening
through `bootstrap_opening` and six starters reaches it directly, and both retry
exactly zero times. Pool composition barely registers - traps on versus off
moves the rate by 0.05 points.

Two consequences worth stating plainly, because both invert an assumption that
seemed obvious:

- **Casual is the SAFER tier, not the harsher one.** Its
  `bootstrap_threshold` is `SAFE_OPENING_MIN + 1 = 3` against an opening of 2,
  so `needs_bootstrap` fires on every casual seed and widens the opening to 6
  before our fill runs. Measured: 4,000 of 4,000 casual seeds bootstrapped, 0 of
  4,000 default seeds did. The `+1` added after a CI failure in TestCasualLogic
  is doing precisely its job.
- **The default two starters is the worst case a player can select.**
  `StarterMissions.range_start` is 2; one starter was measured at 0.25 percent
  failure and the floor was raised deliberately. So there is no narrower opening
  to test, and the 20,000-seed default run above IS the worst case.

## The test path is not the shipped path

`CW4TestBase` switches our own fill off so access assertions work against an
untouched pool, and for a long time that applied to the inherited `test_fill`
too - the one inherited test that is about generating rather than about the
rules. Over the same seeds:

| path | failures |
|---|---|
| our pre_fill ON (what players generate with) | 0 / 16,000 |
| our pre_fill OFF (what CI was testing) | 3 / 3,000 |

All three off-path failures had the identical shape - a two-location opening
that did not chain - and all three were recovered on the FIRST attempt when the
same seed was re-run with our fill on. Nothing was unfillable. See
`CW4TestBase.FILL_TESTS` and `TestFillRunsTheShippedPath`.

A mixed multiworld gets neither defence (`OWN_FILL_SOLO_ONLY`, and
`needs_bootstrap` requires every world to be ours) and was clean over 1,000
seeds against a deliberately tiny partner: our progression can live in the other
world's locations, which is slack a solo seed does not have.

## Harness traps, both of which bit during this work

**Options set after `generate_early` silently do nothing.** The natural shape,
copied from `tools/audit/realfillrate.py`, is
`setup_solo_multiworld(World, ("generate_early",))` and then assigning to
`world.options.<name>.value`. That works for an option read late
(`logic_difficulty`, `trap_percentage`) and is a silent no-op for one read
DURING `generate_early` - which `starter_missions` is. A "one starter" variant
measured plain defaults and would have been reported as a clean harsh case.
`setup_multiworld` applies its `options` argument before any step runs, so that
is the only correct seam. `realfillrate.py` is correct today only because all of
its variants are late-read options.

**Every variant must assert its own premise.** The fix for the above is not to
be more careful, it is to make the run refuse to measure a configuration it has
not proved: check `is_casual(world)`, `len(world.starter_missions)`, the option
value itself. The assertion is what catches this class of bug; care does not.

And the standing rule, inherited from `realfillrate.py`: **a positive control
first.** A harness that has silently stopped generating prints the same clean
zero as a genuinely clean run. Force a `FillError` and confirm it is detected
before believing any number below it.

## Reproducing

From inside the Archipelago clone, with `apworld/cw4` synced into
`worlds/cw4`:

```
python ../tools/audit/realfillrate.py                 # shipped path, pass/fail
OWN_FILL=0 python ../tools/audit/realfillrate.py      # the no-retry path
```

The depth measurement is the one this document is about: set
`items.OWN_FILL_ATTEMPTS` to 25, generate N seeds, and histogram
`world.own_fill_attempts`, checking `fulfills_accessibility()` and
`can_beat_game()` per seed as well - our fill skips the priority pass and
`accessibility_corrections` that `distribute_items_restrictive` would otherwise
run, so "did we strand anything" is a real question rather than a formality.
