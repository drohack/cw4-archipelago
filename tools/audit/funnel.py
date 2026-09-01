"""Find a starter_missions: 1 seed that fails to fill, then ask WHY.

The funnel itself is fine and intended - the first mission has one free check, it
can hand you an unlock to another mission with one free check, and so on until a
weapon turns up. The question is whether a failing seed is
  (a) genuinely unsolvable, which would be a logic bug, or
  (b) solvable but abandoned by fill_restrictive, which is a fill-fragility bug
      the world has to help with.

Reachability with every item collected answers that: if all-state reaches every
location and satisfies the goal, the seed is winnable and the fill simply failed.

Run from inside the Archipelago clone.
"""
import io
import os
import shutil
import subprocess
import sys

AP = os.getcwd()
SEEDS = int(sys.argv[1]) if len(sys.argv) > 1 else 60
STARTERS = sys.argv[2] if len(sys.argv) > 2 else "1"

os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

import Generate  # noqa: E402
from Main import main as ERmain  # noqa: E402

print(f"-- hunting fill failures at starter_missions: {STARTERS}, {SEEDS} seeds --",
      flush=True)

failures = []
for i in range(1, SEEDS + 1):
    pd = os.path.join(AP, f"_fn_p_{i}")
    od = os.path.join(AP, f"_fn_o_{i}")
    for d in (pd, od):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d, exist_ok=True)
    with io.open(os.path.join(pd, "p.yaml"), "w", encoding="utf-8") as f:
        f.write("name: F1\ngame: Creeper World 4\nCreeper World 4:\n"
                f"  starter_missions: {STARTERS}\n")
    saved = sys.argv
    sys.argv = ["Generate.py", "--player_files_path", pd, "--outputpath", od,
                "--seed", str(20000 + i), "--log_level", "error"]
    try:
        erargs, seed = Generate.main()
        ERmain(erargs, seed)
        ok, msg = True, ""
    except Exception as e:  # noqa: BLE001
        ok, msg = False, f"{type(e).__name__}: {str(e).splitlines()[0]}"
    finally:
        sys.argv = saved
    if not ok:
        failures.append((20000 + i, msg))
        print(f"[hunt] seed {20000 + i}: {msg}", flush=True)
    if i % 10 == 0:
        print(f"[hunt] {i}/{SEEDS} done, {len(failures)} failures", flush=True)
    shutil.rmtree(pd, ignore_errors=True)
    shutil.rmtree(od, ignore_errors=True)

print(f"[hunt] {len(failures)} failures in {SEEDS} seeds", flush=True)
if not failures:
    print("Done: no failures to analyse", flush=True)
    raise SystemExit(0)

# ---------------------------------------------------------------- diagnosis
from test.bases import WorldTestBase  # noqa: E402


class Probe(WorldTestBase):
    game = "Creeper World 4"
    options = {"starter_missions": int(STARTERS)}
    run_default_tests = False

    def runTest(self):
        pass


print("", flush=True)
print("-- diagnosing the shape of the opening (any seed, same structure) --", flush=True)
t = Probe()
t.setUp()
mw = t.multiworld
world = t.world
player = t.player

from worlds.cw4 import items as I  # noqa: E402

print(f"  starter missions: {world.starter_missions} "
      f"({', '.join(I.MISSION_TITLES[n] for n in world.starter_missions)})", flush=True)
print(f"  early items requested: {dict(mw.early_items[player])}", flush=True)

from BaseClasses import CollectionState  # noqa: E402

empty = CollectionState(mw)
free = [l.name for l in mw.get_locations(player)
        if l.address is not None and l.can_reach(empty)]
print(f"  locations reachable with NOTHING: {len(free)} -> {free}", flush=True)

allst = mw.get_all_state(False)
locs = [l for l in mw.get_locations(player) if l.address is not None]
unreachable = [l.name for l in locs if not l.can_reach(allst)]
print(f"  locations reachable with EVERYTHING: {len(locs) - len(unreachable)}/{len(locs)}",
      flush=True)
print(f"  goal satisfiable with everything: {bool(mw.completion_condition[player](allst))}",
      flush=True)

# How wide does the funnel get, step by step, if you always take the best item?
print("", flush=True)
print("  widening the funnel, collecting everything reachable each round:", flush=True)
state = CollectionState(mw)
seen = set()
for step in range(1, 9):
    reach = [l for l in locs if l.can_reach(state)]
    new = [l for l in reach if l.name not in seen]
    if not new:
        break
    for l in new:
        seen.add(l.name)
    print(f"    round {step}: {len(reach)} reachable ({len(new)} new)", flush=True)
    # Collect the whole item pool's worth of progression that sits in reach -
    # we have no placement yet, so approximate by collecting every progression
    # item once, which is the upper bound on what this round could yield.
    if step == 1:
        for item in mw.itempool:
            if item.advancement and item.player == player:
                state.collect(item, prevent_sweep=True)
        state.sweep_for_advancements()

print("", flush=True)
print(f"Done: {len(failures)} failures, first was seed {failures[0][0]}", flush=True)
