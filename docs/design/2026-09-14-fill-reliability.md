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

## Opening width is the only driver - while the roster is fixed

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

## Once the ROSTER varies, weapon breadth is the driver instead (2026-09-16)

The SPAN Experiments made the mission set a per-seed draw: 19 of the 20 missions
come from a pool of 45 rather than being the campaign every time. That breaks
the finding above, and it broke it in a way a blind sweep would have missed.

**10,000 SPAN seeds at the shipped cap of 5 reported zero failures.** Re-run
with the cap at 25, the same configuration produced this:

| attempts needed | seeds |
|---|---|
| 1 | 9,709 |
| 2 | 259 |
| 3 | 27 |
| 4 | 4 |
| 5 | 0 |
| 6 | 0 |
| 7 | 1 |

The geometric part is intact and the last row is not part of it: a seed needing
seven attempts, with nothing at five or six. **At the shipped cap that seed
fails**, so the true rate was about 1 in 10,000 against the campaign's 1 in 14.6
million - and the capped run said "zero failures" because a censored histogram
cannot tell a comfortable margin from a seed one step from disaster.

### What was different about the deep seeds

4,000 further SPAN seeds, recording the shape of each alongside its depth:

| depth | seeds | mean weapon breadth | mean opening | mean early candidates |
|---|---|---|---|---|
| 1 | 3,888 | 10.5 | 1.89 | 3.20 |
| 2 | 98 | 8.9 | 2.00 | 3.06 |
| 3 | 13 | 8.0 | 2.00 | 2.77 |
| 4 | 1 | 5.0 | 2.00 | 3.00 |

Opening width does not move at all. **Weapon breadth** - how many of the whole
roster's checks the first weapon opens, `items.weapon_breadth` summed over the
seed - falls monotonically. Six of the 4,000 seeds had breadth ZERO: the first
weapon opened nothing anywhere in the seed.

The cause is variance, not a worse average. The campaign's breadth is 9, held by
five missions (Farsite 3, Home 2, More and More 1, Tower of Darkness 1,
Sequence 2), and a campaign seed contains all five every time. SPAN adds 16
across five more, 14 of it in the three Pod maps whose loose liftic means their
totems need no factory. So a mixed roster averages 10.5 - more than the campaign
- and can still draw almost none of the ten.

### Two fixes, and a third thing the fixes taught

1. `MIN_ROSTER_BREADTH`, set to the campaign's own 9 rather than to an invented
   constant, so the rule reads "a mixed seed never opens narrower than the
   campaign does". A roster below it swaps breadth in rather than being
   re-rolled: re-rolling would bias the whole roster, a swap changes only what
   it must.
2. `force_early_mission` drew only from `STARTER_ELIGIBLE` inside the roster,
   and on 1.1 percent of mixed seeds that set was empty - so it returned having
   granted nothing at all, silently. It now falls back to the rest of the
   roster. (Its own docstring already said the point was a mission a WEAPON can
   open checks on; starter-eligibility was a proxy for that, not the
   requirement.)
3. That fix shipped a `KeyError` on its first measurement, because the fallback
   can pick a SPAN mission and the item name was still being built from the
   campaign-only title table. 2.5 percent of seeds, found in a 200-seed run, and
   **the unit suite passed throughout** - whether a seed reaches the fallback
   depends on its draw, and no fixture seed did. The test that pins it now
   constructs the condition instead of hoping for it.

### After

50,000 seeds, cap 25, five configurations:

| configuration | 1 | 2 | 3 | 4 | 5 | deepest | failures |
|---|---|---|---|---|---|---|---|
| span off, default | 9,729 | 253 | 17 | 1 | - | 4 | 0 |
| span ON, default | 9,758 | 222 | 18 | 2 | - | 4 | 0 |
| span off, min starters | 9,765 | 223 | 12 | - | - | 3 | 0 |
| span ON, min starters | 9,782 | 202 | 14 | 1 | 1 | 5 | 0 |
| span ON, all for finale | 9,793 | 188 | 16 | 3 | - | 4 | 0 |

The outlier is gone and SPAN now sits where the campaign sits. The worst
configuration still reaches 5, which against a cap of 5 is zero margin, so
`OWN_FILL_ATTEMPTS` went from 5 to 8. An attempt only happens when the previous
one failed, so the extra three cost nothing measurable.

Reproduce with `tools/seed_battery.py` (`--cap 25` to see the tail,
`--only "span ON"` to pick configurations).

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
