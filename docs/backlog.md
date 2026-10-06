# Backlog

Reports and loose ends worth looking into later. Each entry says where it came
from, what is known, and what is only suspected. Move an entry out (to a fix, a
design doc, or a "won't fix" note) once it is resolved.

---

## Universal Tracker shows "Home - Cache 1" in logic without Home unlocked

**Source:** a player comment while starting a multiworld (reported 2026-10-04).
Their words:

> I'm starting up a multiworld to try things out, Universal Tracker is saying
> Home - Cache 1 is available, despite not having the Home mission unlocked.
> Not sure if that's UT being wrong, or a logic error.

**What we have:** only that comment. No logs, no seed, no yaml, and no list of
which missions they had unlocked.

**Generator side looks right.** Every location is created in its own mission's
region (`apworld/cw4/locations.py`, `create_all_locations`), and that region is
connected from Menu with no rule only when the mission is a starter; otherwise
it needs `Mission Unlock: <title>` (`apworld/cw4/regions.py`). So the real seed
can only have "Home - Cache 1" in logic before the Home unlock if Home is one
of that seed's starter missions, and a starter mission is unlocked in game.

**Likely cause: Universal Tracker re-rolls the starters.** UT works by
regenerating the world on the player's side. Without an `interpret_slot_data`
hook it gets no state from the real seed, so it runs `generate_early` with its
own random:

- `roster.starter_missions` draws the starters from `STARTER_ELIGIBLE` with
  `world.random` (`apworld/cw4/roster.py`). Home (mission 2) is eligible, so
  UT's copy can easily start with Home while the real seed does not.
- With `span_missions` on, `roster.mission_roster` is also random, so UT could
  be tracking a different set of 20 missions as well.
- The apworld has no `interpret_slot_data` or `ut_can_gen_without_yaml` today.
  `docs/design/2026-08-26-ap-feature-comparison.md` already recorded "no UT"
  and filed UT support under "Later".

The values UT would need are already in slot data: `starter_missions` and
`mission_roster` (as map guids), plus `mission_titles`. That is unverified as a
diagnosis until it is reproduced.

**To look into:**

1. Reproduce: generate a seed, connect UT, and compare what UT marks reachable
   against the seed's spoiler log. Check whether UT's starters match the
   spoiler's.
2. If they differ, add UT support: `interpret_slot_data` returning the
   starters and roster, and have `generate_early` use those instead of rolling
   when UT passes them back (`re_gen_passthrough`). Look at how other
   apworlds do it (TUNIC is noted in the comparison doc) and check the
   current UT docs for the hook names, rather than going from memory.
3. Check whether anything else in `generate_early` is random and matters to
   logic: `opening.force_early_mission` / `force_early_weapon` and the
   bootstrap path in `needs_bootstrap` only affect item placement, which UT
   reads from the server, but confirm that.
4. If the starters do match, it is a real logic bug: find which rule lets the
   Home region in without its unlock.
5. Until UT support lands, the README could say that UT is not supported yet
   and may show wrong reachability.

---

## All-CW4 multiworld: `non_local_items` on CW4 progression cannot generate

**Source:** design review of the early-items fix, 2026-10-06. Found by reading
the code; not reproduced.

When every player is Creeper World 4, `opening.place_own_progression` places all
of a player's progression into that player's own locations. Archipelago's
locality rules forbid a `non_local_items` item from its own world, so a player
who lists, say, `Cannon` under `non_local_items` in an all-CW4 multiworld would
make every one of the 8 attempts fail and generation stop with a FillError.
Excluding every location reachable at the start fails the same way.

**To look into:** reproduce with two CW4 yamls, one listing `non_local_items:
[Cannon]`. If it fails, have the own fill leave a player's non-local items in
the pool for Archipelago's fill, or stand down for that player.

---

## Audit scripts that no longer measure what they say

**Source:** exploration for `tools/audit/earlysweep.py`, 2026-10-06.

- `tools/audit/realfillrate.py` and `tools/audit/bootstrapcheck.py` set options
  AFTER `generate_early` has run, so anything `generate_early` reads
  (`logic_difficulty`, `starter_missions`, `span_missions`, `early_weapon`) is
  at its default during the early-item and bootstrap decisions. The "casual"
  variant of realfillrate makes those decisions under standard logic.
- `tools/audit/mixed.py` and `tools/audit/repro.py` still default to
  `starter_missions: 1`, which is below the option's range since 2026-09-03,
  so every seed fails before measuring anything. `earlysweep.py` covers what
  mixed.py was for.
- `tools/audit/funnel.py` prints `early_items`; this world only writes
  `local_early_items`.
- `realfillrate.py` calls `fulfills_accessibility()` bare, which raises a
  FillError under `__debug__` instead of returning False, so one stranded seed
  would end the run rather than be counted.
