"""Does the casual bootstrap actually ENGAGE? A structural check, not a sample.

The casual FillError rate was about 0.2 to 1 percent, and 0/800 after the fix is
only ~90 percent confidence on its own - at that rate, zero in 800 comes up
roughly one time in ten by luck. Proving a sub-percent rate by sampling needs
thousands of seeds.

Asking whether the MECHANISM turned on is much stronger and costs two seeds:
bootstrap_threshold should be 3 under casual and 2 otherwise, needs_bootstrap
should now be true for a casual solo seed at the default two starters, and
bootstrap_opening should have placed something.

Run from inside an Archipelago clone:

    python ../tools/audit/bootstrapcheck.py
"""
import os
import sys

sys.argv = ["bootstrapcheck"]
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, os.getcwd())

from test.general import setup_solo_multiworld  # noqa: E402
from worlds.cw4 import CW4World, items  # noqa: E402

STEPS = ("generate_early", "create_regions", "create_items", "set_rules",
         "connect_entrances", "generate_basic", "pre_fill")

fails = 0


def check(ok, label, detail=""):
    global fails
    print(f"  {'PASS' if ok else 'FAIL'}  {label}" + (f" -- {detail}" if detail else ""),
          flush=True)
    if not ok:
        fails += 1


def build(casual):
    """A solo world with logic_difficulty set BEFORE the gen steps run, so the
    option is in place for opening_width and needs_bootstrap."""
    mw = setup_solo_multiworld(CW4World, ("generate_early",))
    w = mw.worlds[1]
    w.options.logic_difficulty.value = 1 if casual else 0
    for step in STEPS[1:]:
        getattr(w, step)()
    return w


for casual in (False, True):
    label = "casual" if casual else "normal"
    print(f"{label} logic:", flush=True)
    w = build(casual)
    want = items.SAFE_OPENING_MIN + (1 if casual else 0)
    got = items.bootstrap_threshold(w)
    check(got == want, f"bootstrap_threshold is {want}", f"got {got}")

    width = items.opening_width(w)
    print(f"        opening_width = {width}, SAFE_OPENING = {items.SAFE_OPENING}", flush=True)

    engaged = width < got
    if casual:
        # This is the whole point of the fix: at the default two starters the
        # casual opening is too narrow for the fill to survive a dud pick, and
        # the world now widens it itself.
        check(engaged, "the bootstrap engages for casual", f"width {width} < {got}")
        placed = getattr(w, "bootstrapped", None)
        check(bool(placed), "bootstrap_opening placed something",
              f"{len(placed) if placed else 0} placement(s)")
        if placed:
            for loc, item in placed:
                print(f"          {loc} <- {item}", flush=True)
    else:
        # Normal logic must be UNCHANGED - the fix is meant to cost nothing
        # where there was no problem, because bootstrapping trades away the
        # cross-game placements a narrow opening makes interesting.
        check(not engaged, "normal logic is untouched", f"width {width} >= {got}")
    print("", flush=True)

print(f"{'ALL CHECKS PASSED' if not fails else str(fails) + ' CHECK(S) FAILED'}", flush=True)
raise SystemExit(1 if fails else 0)
