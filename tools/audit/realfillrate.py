"""Measure generation the way generation actually happens.

fillrate.py drives the world's UNIT TESTS, which is fine for comparing rule
changes but stopped telling the truth once World.pre_fill began placing this
world's own progression: CW4TestBase switches that off so access tests keep
working, so every test_fill in that sweep exercises the OLD path. A 20,000-seed
sweep looked unchanged for exactly that reason.

This runs the real pipeline instead - every generation step, then
distribute_items_restrictive - so whatever pre_fill does is included.

Run from inside an Archipelago clone:

    python ../tools/audit/realfillrate.py                  # 2000 seeds, defaults
    N=5000 python ../tools/audit/realfillrate.py
    OWN_FILL=0 python ../tools/audit/realfillrate.py       # with our fill disabled
    OPTIONS=casual python ../tools/audit/realfillrate.py
"""
import os
import sys

sys.argv = ["realfillrate"]
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, os.getcwd())

from test.general import setup_solo_multiworld              # noqa: E402
import Fill                                              # noqa: E402
from Fill import FillError                                # noqa: E402
from worlds.cw4 import CW4World, items as itemsmod          # noqa: E402

N = int(os.environ.get("N", "2000"))
OWN_FILL = os.environ.get("OWN_FILL", "1") != "0"
VARIANT = os.environ.get("OPTIONS", "default")

STEPS = ("create_regions", "create_items", "set_rules", "connect_entrances",
         "generate_basic", "pre_fill")

VARIANTS = {
    "default": {},
    "casual": {"logic_difficulty": 1},
    "notraps": {"trap_percentage": 0},
    "alltraps": {"trap_percentage": 100},
    "noerns": {"progressive_erns": 0},
}
if VARIANT not in VARIANTS:
    sys.exit(f"unknown OPTIONS variant {VARIANT}; pick from {sorted(VARIANTS)}")

if not OWN_FILL:
    itemsmod.OWN_FILL_ATTEMPTS = 0

print(f"variant={VARIANT}  own_fill={'on' if OWN_FILL else 'OFF'}  "
      f"attempts={itemsmod.OWN_FILL_ATTEMPTS}", flush=True)


def build_and_fill():
    """One full generation. Returns the world, or raises FillError."""
    mw = setup_solo_multiworld(CW4World, ("generate_early",))
    world = mw.worlds[1]
    for name, value in VARIANTS[VARIANT].items():
        getattr(world.options, name).value = value
    for step in STEPS:
        getattr(world, step)()
    # Called through the MODULE, not a name imported at the top: the positive
    # control below monkeypatches Fill.distribute_items_restrictive, and a
    # directly-imported reference would keep pointing at the real function - so
    # the control would silently pass while testing nothing. It caught exactly
    # that on the first run of this file.
    Fill.distribute_items_restrictive(mw)
    return world


# POSITIVE CONTROL. A harness that silently is not generating prints the same
# clean zero as a genuinely clean run, so force a failure and see it detected.
_real = Fill.distribute_items_restrictive


def _always_fails(*a, **k):
    raise FillError("positive control")


Fill.distribute_items_restrictive = _always_fails
try:
    build_and_fill()
except FillError:
    detected = True
except Exception:                                            # noqa: BLE001
    detected = False
else:
    detected = False
finally:
    Fill.distribute_items_restrictive = _real
if not detected:
    print("CONTROL FAILED: a forced FillError was not detected. Any zero below "
          "would be meaningless.", flush=True)
    raise SystemExit(1)
print("control ok: a forced FillError is detected", flush=True)
print(f"generating {N} seeds for real", flush=True)

fails = 0
retried = 0
inaccessible = 0
unbeatable = 0
for i in range(N):
    try:
        world = build_and_fill()
    except FillError:
        fails += 1
    else:
        if getattr(world, "own_fill_attempts", 1) > 1:
            retried += 1
        # ACCESSIBILITY AND BEATABILITY, both of which real generation enforces
        # and a bare distribute_items_restrictive does not. Main.py submits
        # multiworld.fulfills_accessibility and aborts on a false result
        # (Main.py:239 and :365), so a fill that strands locations shows up
        # there rather than here unless this asks.
        #
        # It matters for this world specifically: placing our own progression
        # skips the priority pass and accessibility_corrections that
        # distribute_items_restrictive runs, so "did we leave anything
        # unreachable" is a real question, not a formality.
        mw = world.multiworld
        if not mw.fulfills_accessibility():
            inaccessible += 1
        if not mw.can_beat_game():
            unbeatable += 1
    if (i + 1) % 500 == 0:
        print(f"  ...{i + 1}/{N}, {fails} failed", flush=True)

rate = 100.0 * fails / N if N else 0.0
print("", flush=True)
print(f"RESULT: {fails}/{N} failed to generate ({rate:.4f} percent)", flush=True)
if OWN_FILL:
    print(f"        our own fill needed a retry on {retried} seed(s)", flush=True)
print(f"        seeds with UNREACHABLE locations: {inaccessible}", flush=True)
print(f"        seeds that are UNBEATABLE:        {unbeatable}", flush=True)
raise SystemExit(1 if (fails or inaccessible or unbeatable) else 0)
