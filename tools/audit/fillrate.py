"""Estimate the fill failure RATE for any of the world's test configurations.

casualrate.py samples exactly one class (TestCasualLogic), because casual was
the configuration CI had failed on. Tightening logic can starve any of them, so
this takes the class as an argument and defaults to sweeping all of them.

Every sweep runs a POSITIVE CONTROL first: a harness that silently is not
running the test prints the same reassuring "0 failed" as a genuinely clean one,
so a forced FillError has to be seen before any zero here means anything.

Run from inside an Archipelago clone:

    python ../tools/audit/fillrate.py                    # 60 seeds of each class
    N=200 python ../tools/audit/fillrate.py TestNoTraps  # one class, deeper
"""
import os
import sys
import unittest

sys.argv_backup = list(sys.argv)
WANTED = [a for a in sys.argv[1:] if not a.startswith("-")]
sys.argv = ["fillrate"]
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, os.getcwd())

import Utils                                             # noqa: E402
import Fill                                              # noqa: E402
from worlds.cw4.test import test_options                  # noqa: E402

N = int(os.environ.get("N", "60"))

CLASSES = WANTED or [
    name for name in dir(test_options)
    if name.startswith("Test") and hasattr(getattr(test_options, name), "test_fill")
]

print(f"Archipelago {Utils.version_tuple}", flush=True)

# POSITIVE CONTROL, before believing any zero.
_real = Fill.distribute_items_restrictive


def _always_fails(*a, **k):
    raise Fill.FillError("positive control")


probe_class = getattr(test_options, CLASSES[0])
Fill.distribute_items_restrictive = _always_fails
_res = unittest.TestResult()
probe_class("test_fill").run(_res)
Fill.distribute_items_restrictive = _real
if not (_res.errors or _res.failures):
    print("", flush=True)
    print("CONTROL FAILED: a forced FillError was NOT detected by this loop.", flush=True)
    print("Any zero below would be meaningless. Fix the harness first.", flush=True)
    raise SystemExit(1)
print("control ok: a forced FillError is detected", flush=True)
print(f"Sampling {N} seeds each of: {', '.join(CLASSES)}", flush=True)
print("", flush=True)

total_fail = 0
worst = []
for cls_name in CLASSES:
    cls = getattr(test_options, cls_name)
    fails = []
    for i in range(N):
        result = unittest.TestResult()
        case = cls("test_fill")
        case.run(result)
        if result.errors or result.failures:
            seed = None
            try:
                seed = case.multiworld.seed
            except Exception:                            # noqa: BLE001
                pass
            fails.append(seed)
        if (i + 1) % 50 == 0:
            print(f"  {cls_name}: {i + 1}/{N} done, {len(fails)} failed", flush=True)
    pct = 100.0 * len(fails) / N if N else 0.0
    flag = "" if not fails else "   <-- REGRESSION"
    print(f"  {cls_name:24} {len(fails):3}/{N}  ({pct:.1f} percent){flag}", flush=True)
    if fails:
        print(f"      first failing seed: {fails[0]}", flush=True)
        worst.append(cls_name)
    total_fail += len(fails)

print("", flush=True)
print(f"Done: {total_fail} failure(s) across {len(CLASSES)} configuration(s)"
      + (f" -- {', '.join(worst)}" if worst else " -- all clean"), flush=True)
raise SystemExit(1 if total_fail else 0)
