"""Estimate the casual-logic fill failure RATE, cheaply.

CI hit one FillError in TestCasualLogic.test_fill. Twenty-five runs of that
exact configuration - old apworld on Archipelago 0.6.7 - all passed, so the
failure is rare rather than systematic, and a rare failure needs a RATE before
anyone can say whether it matters.

Runs the real test class in-process so a sample of hundreds is affordable;
spawning `python -m unittest` per seed costs about four seconds each and makes
anything past twenty runs impractical.

Run from inside an Archipelago clone (any version):

    python ../tools/audit/casualrate.py            # 200 seeds
    N=500 python ../tools/audit/casualrate.py

Reports the failure count and the seeds that failed, so a failing one can be
handed to repro_casual.py.
"""
import os
import sys
import unittest

sys.argv = ["casualrate"]
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, os.getcwd())

import Utils  # noqa: E402
from worlds.cw4.test import test_options  # noqa: E402

N = int(os.environ.get("N", "200"))

print(f"Archipelago {Utils.version_tuple}", flush=True)

# POSITIVE CONTROL, before believing any zero.
#
# A harness that silently is not running the test produces exactly the same
# "0/200" as a genuinely clean run. So force a FillError once and confirm this
# loop reports it; if the control does not fire, the sample below means nothing
# and the script says so instead of printing a reassuring number.
import Fill  # noqa: E402

_real = Fill.distribute_items_restrictive


def _always_fails(*a, **k):
    raise Fill.FillError("positive control")


Fill.distribute_items_restrictive = _always_fails
_probe = test_options.TestCasualLogic("test_fill")
_res = unittest.TestResult()
_probe.run(_res)
Fill.distribute_items_restrictive = _real
if not (_res.errors or _res.failures):
    print("", flush=True)
    print("CONTROL FAILED: a forced FillError was NOT detected by this loop.", flush=True)
    print("Any zero below would be meaningless. Fix the harness first.", flush=True)
    raise SystemExit(1)
print("control ok: a forced FillError is detected", flush=True)

print(f"Sampling {N} seeds of TestCasualLogic.test_fill", flush=True)
print("", flush=True)

fails = []
for i in range(N):
    case = test_options.TestCasualLogic("test_fill")
    result = unittest.TestResult()
    case.run(result)
    if result.errors or result.failures:
        # The seed is in the subtest description the base attaches, and failing
        # that, in the multiworld it built.
        seed = None
        try:
            seed = case.multiworld.seed
        except Exception:                            # noqa: BLE001
            pass
        fails.append(seed)
        blob = "".join(t for _, t in (result.errors + result.failures))
        first = next((ln for ln in blob.splitlines() if "FillError" in ln), "")
        print(f"  seed {seed}: {first.strip()[:120]}", flush=True)
    if (i + 1) % 25 == 0:
        print(f"  ...{i + 1}/{N} done, {len(fails)} failed", flush=True)

print("", flush=True)
pct = 100.0 * len(fails) / N if N else 0.0
print(f"RESULT: {len(fails)}/{N} FillError ({pct:.1f} percent)", flush=True)
if fails:
    print("", flush=True)
    print("Reproduce one with:", flush=True)
    print(f"    python ../tools/audit/repro_casual.py {fails[0]}", flush=True)
    print("NOTE repro_casual forces casual AFTER generate_early, so its RNG", flush=True)
    print("stream differs from the test base and a seed may not reproduce", flush=True)
    print("there. This script is the faithful one.", flush=True)
