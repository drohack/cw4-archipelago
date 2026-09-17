"""Generate real seeds in bulk and report what the fill actually did.

WHY THIS EXISTS. The apworld suite proves the rules are consistent; it does not
prove a seed generates. Generation failure is a tail event - the campaign's rate
was about 1 in 18,000 before place_own_progression absorbed it - so the only
honest way to talk about it is to run thousands of seeds and count.

WHAT IT MEASURES, per configuration:

  failures        seeds where generation raised. Any non-zero number is a bug,
                  not a tuning result.
  retry depth     how deep place_own_progression had to go. It is capped at
                  items.OWN_FILL_ATTEMPTS, so the margin that matters is
                  (cap - deepest observed). A run whose deepest attempt equals
                  the cap has NO margin left and the next unlucky seed fails.
  roster shape    with SPAN on, how many of the 20 missions came from SPAN, and
                  how wide the opening was. Both move per seed, and the opening
                  is the thing most likely to break.

Run from inside an Archipelago checkout with the world synced in:

    powershell -File tools/ap-sync.ps1
    cd Archipelago
    PYTHONUNBUFFERED=1 py -3.13 -u ../tools/seed_battery.py --seeds 20000

Exit 0 when every configuration had zero failures, 1 otherwise.
"""
import argparse
import collections
import os
import random
import sys
import time
import traceback

sys.path.insert(0, os.getcwd())

import ModuleUpdate  # noqa: E402
ModuleUpdate.update_ran = True  # do not let it pip install mid-run

from BaseClasses import MultiWorld, CollectionState  # noqa: E402
from worlds import AutoWorldRegister  # noqa: E402
from Fill import distribute_items_restrictive  # noqa: E402
from Options import Accessibility  # noqa: E402

GAME = "Creeper World 4"

# Each configuration is (label, yaml option overrides). The default row is what
# most players generate; the rest are the corners the opening can break in.
CONFIGS = [
    ("span off, default", {}),
    ("span ON,  default", {"span_missions": 1}),
    # The NARROWEST legal opening. starter_missions bottoms out at 2, and 2 is
    # where the campaign's own fill failures used to live, so it is the row that
    # would show a SPAN roster making the opening worse.
    ("span off, min starters", {"starter_missions": 2}),
    ("span ON,  min starters", {"span_missions": 1, "starter_missions": 2}),
    # Every mission required for the finale, so the finale sits behind the whole
    # roster rather than a subset - the arrangement with the least slack.
    ("span ON,  all for finale", {"span_missions": 1, "missions_for_finale": 19}),
]


def build(seed, overrides):
    world_type = AutoWorldRegister.world_types[GAME]
    multiworld = MultiWorld(1)
    multiworld.game[1] = GAME
    multiworld.player_name = {1: "Tester"}
    multiworld.set_seed(seed)
    args = argparse.Namespace()
    for name, option in world_type.options_dataclass.type_hints.items():
        setattr(args, name, {1: option.from_any(overrides.get(name, option.default))})
    multiworld.set_options(args)
    multiworld.state = CollectionState(multiworld)
    multiworld.worlds[1].options.accessibility.value = Accessibility.option_full
    return multiworld


def generate(seed, overrides):
    multiworld = build(seed, overrides)
    world = multiworld.worlds[1]
    world.generate_early()
    multiworld.worlds[1].create_regions()
    multiworld.worlds[1].create_items()
    multiworld.worlds[1].set_rules()
    multiworld.worlds[1].connect_entrances()
    multiworld.worlds[1].generate_basic()
    multiworld.worlds[1].pre_fill()
    distribute_items_restrictive(multiworld)
    return world


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=2000)
    ap.add_argument("--start", type=int, default=1)
    # RAISE THE CAP TO SEE THE TAIL. At the shipped cap the depth histogram is
    # censored: a seed that would have needed 7 attempts is recorded as a failure
    # at 5, and a run with no failures says only "at most 5", which cannot tell a
    # comfortable margin from a seed away from disaster. Raising the cap turns
    # P(fail at cap 5) into the directly observable P(needed more than 5).
    # See docs/design/2026-09-14-fill-reliability.md.
    ap.add_argument("--cap", type=int, default=0,
                    help="override items.OWN_FILL_ATTEMPTS (0 keeps the shipped value)")
    ap.add_argument("--only", default="",
                    help="substring: run only configurations whose label contains it")
    args = ap.parse_args()

    configs = [c for c in CONFIGS if args.only in c[0]]
    if not configs:
        print(f"no configuration matches '{args.only}'", flush=True)
        return 1

    from worlds.cw4 import items as cw4_items
    if args.cap:
        cw4_items.OWN_FILL_ATTEMPTS = args.cap
    cap = cw4_items.OWN_FILL_ATTEMPTS
    print(f"retry cap is {cap}; {args.seeds} seeds x {len(configs)} configs",
          flush=True)

    bad = 0
    for ci, (label, overrides) in enumerate(configs, 1):
        depths = collections.Counter()
        span_counts = collections.Counter()
        failures = []
        started = time.time()
        for i in range(args.seeds):
            seed = random.Random(f"{label}:{args.start + i}").getrandbits(48)
            try:
                world = generate(seed, overrides)
            except Exception as exc:
                failures.append((seed, f"{type(exc).__name__}: {exc}"))
                if len(failures) == 1:
                    traceback.print_exc()
            else:
                depths[getattr(world, "own_fill_attempts", 0)] += 1
                span_counts[sum(1 for n in world.mission_roster if n >= 21)] += 1
            if (i + 1) % 100 == 0 or i + 1 == args.seeds:
                rate = (i + 1) / max(1e-9, time.time() - started)
                deepest = max(depths) if depths else 0
                print(f"[config {ci}/{len(configs)} {label}] seed {i + 1}/{args.seeds}: "
                      f"{len(failures)} failures, deepest retry {deepest}/{cap}, "
                      f"{rate:.1f} seeds/s", flush=True)

        deepest = max(depths) if depths else 0
        spread = " ".join(f"{d}:{n}" for d, n in sorted(depths.items()))
        print(f"-- {label}: {len(failures)} failures in {args.seeds}; "
              f"retry depth {spread}; deepest {deepest} of cap {cap}", flush=True)
        if span_counts:
            lo, hi = min(span_counts), max(span_counts)
            mean = sum(k * v for k, v in span_counts.items()) / sum(span_counts.values())
            print(f"   SPAN missions per roster: {lo} to {hi}, mean {mean:.1f}",
                  flush=True)
        for seed, why in failures[:5]:
            print(f"   FAILED seed {seed}: {why}", flush=True)
        bad += len(failures)

    print(f"Done: {bad} generation failures across "
          f"{args.seeds * len(configs)} seeds", flush=True)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
