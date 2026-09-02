"""Reproduce the exact CI FillError seed under casual logic.

CI, at commit 4637887, on Archipelago 0.6.7:

    ERROR: test_fill (worlds.cw4.test.test_options.TestCasualLogic.test_fill)
           (game='Creeper World 4', seed=47184655139301313347)
    Fill.FillError: No more spots to place 28 items.

Forty local runs (20 on that commit's apworld, 20 on the current one) all
passed, so the failure is either RARE or specific to Archipelago 0.6.7 - which
this script settles by pinning the seed rather than sampling.

If the seed passes here, the difference is the Archipelago version and the fix
belongs in whatever 0.6.7's Fill.py does differently. If it fails, the world has
a genuine opening too narrow for casual logic and `items.bootstrap_opening`
needs to cover casual the way it already covers `starter_missions: 1`.

Run from inside the Archipelago clone:

    python ../tools/audit/repro_casual.py [seed]
"""
import os
import sys

sys.argv = ["repro_casual"]
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, os.getcwd())

from test.general import setup_solo_multiworld  # noqa: E402
from worlds.cw4 import CW4World  # noqa: E402
from Fill import distribute_items_restrictive, FillError  # noqa: E402

SEED = int(sys.argv[1]) if len(sys.argv) > 1 else 47184655139301313347

STEPS = ("generate_early", "create_regions", "create_items", "set_rules",
         "connect_entrances", "generate_basic", "pre_fill")

print(f"Archipelago at: {os.getcwd()}", flush=True)
try:
    import Utils
    print(f"Archipelago version: {Utils.version_tuple}", flush=True)
except Exception:                                    # noqa: BLE001
    pass
print(f"Seed: {SEED}", flush=True)
print("", flush=True)

# NOTE: setup_solo_multiworld applies DEFAULT options, so this reproduces the
# default configuration at that seed. TestCasualLogic sets
# logic_difficulty: casual, which is applied below the same way the test base
# does it - after generate_early would be too late, so it is set before the
# steps run by constructing in two stages.
mw = setup_solo_multiworld(CW4World, ("generate_early",), SEED)
world = mw.worlds[1]
opt = world.options.logic_difficulty
try:
    opt.value = 1                                    # casual
    print(f"logic_difficulty forced to casual (value={opt.value})", flush=True)
except Exception as e:                               # noqa: BLE001
    print(f"could not force casual: {e}", flush=True)

for step in STEPS[1:]:
    getattr(world, step)() if hasattr(world, step) else None

try:
    distribute_items_restrictive(mw)
    print("", flush=True)
    print("FILL SUCCEEDED at this seed.", flush=True)
    print("So the CI failure is not reproducible on this Archipelago; the", flush=True)
    print("difference is the pinned 0.6.7 in .github/workflows/ci.yml.", flush=True)
except FillError as e:
    print("", flush=True)
    print("FILL FAILED - reproduced.", flush=True)
    print(str(e)[:400], flush=True)
except Exception as e:                               # noqa: BLE001
    print("", flush=True)
    print(f"Unexpected {type(e).__name__}: {e}", flush=True)
    print("(the two-stage construction above may not match the test base;", flush=True)
    print(" prefer running the unittest itself if this is unreliable)", flush=True)
