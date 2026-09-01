"""Derive the pool composition from the code, then check it against the seeds.

The point: most of these numbers are not empirical at all. create_all_items is
straight-line arithmetic, so the pool for any option set can be computed in closed
form. Generating seeds is the CHECK on the arithmetic, not the source of it.

What is genuinely not derivable is marked as such at the bottom.
"""
import io
import json
import os
import sys

AP = os.getcwd()
REPO = os.path.abspath(os.path.join(AP, ".."))
# Gitignored, and shared with audit.py, which is what writes the file read below.
OUT = os.path.join(REPO, ".aptest", "audit")
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

from worlds.cw4 import items as I, locations as L  # noqa: E402

# ---------------------------------------------------------------- derivation
LOCATIONS = (sum(c for c, _, _ in L.INSTANCE_COUNTS.values())
             + sum(t for _, t, _ in L.INSTANCE_COUNTS.values())
             + sum(u for _, _, u in L.INSTANCE_COUNTS.values())
             + len(L.RECLAIM_MISSIONS) + len(L.CUSTOM_MISSIONS)
             + (20 - 1))          # mission complete on all but the finale

DEFAULTS = {"starter_missions": 2, "progressive_erns": 4, "trap_percentage": 50,
            "traps_off": False, "filler_off": False}


def derive(**o):
    """The pool create_all_items will build, computed rather than observed."""
    opt = dict(DEFAULTS, **o)
    starters = min(opt["starter_missions"], len(I.STARTER_ELIGIBLE))
    unlocks = len(I.MISSION_UNLOCK_ITEMS) - starters
    real = unlocks + len(I.UNIT_ITEMS) + len(I.BONUS_UNIT_ITEMS) + opt["progressive_erns"]
    remaining = max(0, LOCATIONS - real)
    traps = remaining * opt["trap_percentage"] // 100
    if opt["traps_off"]:            # every trap weight zero -> slots become filler
        traps = 0
    filler = remaining - traps
    return {"unlocks": unlocks, "units": len(I.UNIT_ITEMS),
            "bonus": len(I.BONUS_UNIT_ITEMS), "erns": opt["progressive_erns"],
            "traps": traps, "filler": filler,
            "total": real + traps + filler}


CASES = [
    ("defaults", {}),
    ("casual logic", {}),
    ("no traps", {"trap_percentage": 0}),
    ("all traps", {"trap_percentage": 100}),
    ("no erns", {"progressive_erns": 0}),
    ("max erns", {"progressive_erns": 40}),
    ("one starter", {"starter_missions": 1}),
    ("five starters", {"starter_missions": 5}),
    ("finale open", {}),
    ("finale maxed", {}),
    ("filler weights all zero", {}),
    ("trap weights all zero", {"traps_off": True}),
]

print(f"Locations, derived from the tables: {LOCATIONS}", flush=True)
print(f"Item names with ids, derived:       {len(I.ITEM_NAME_TO_ID)}", flush=True)
print("", flush=True)
print(f"{'config':<26}{'traps':>7}{'filler':>8}{'total':>7}   {'measured':>18}   verdict",
      flush=True)

quantities = os.path.join(OUT, "audit-quantities.json")
if not os.path.exists(quantities):
    print("", flush=True)
    print(f"No generated seeds to check against: {quantities} is missing.", flush=True)
    print("Run audit.py first - it writes that file. The derivation above stands on", flush=True)
    print("its own; this script's job is to confirm it against real generation.", flush=True)
    raise SystemExit(0)
measured = json.load(io.open(quantities, encoding="utf-8"))["matrix"]

bad = 0
for label, opts in CASES:
    d = derive(**opts)
    m = measured.get(label, {})
    m_traps = sum(v for k, v in m.items() if k in I.TRAP_ITEMS)
    m_filler = sum(v for k, v in m.items()
                   if k in (I.ENERGY_STORAGE_ITEM, I.BASE_GENERATION_ITEM))
    m_total = sum(m.values())
    ok = (d["traps"] == m_traps and d["filler"] == m_filler and d["total"] == m_total)
    bad += 0 if ok else 1
    print(f"{label:<26}{d['traps']:>7}{d['filler']:>8}{d['total']:>7}   "
          f"{m_traps:>6}{m_filler:>6}{m_total:>6}   {'match' if ok else 'MISMATCH'}",
          flush=True)

print("", flush=True)
print(f"Derived vs generated: {len(CASES) - bad}/{len(CASES)} match, {bad} mismatched",
      flush=True)

# What the arithmetic CANNOT tell you, stated explicitly so the split is honest.
print("", flush=True)
print("Not derivable, genuinely sampled:", flush=True)
print("  - the split of filler between the two energy upgrades (a weighted draw;", flush=True)
print("    expectation 50/50 at default weights, observed 41/54 in one seed)", flush=True)
print("  - which trap kinds fill the trap slots (same reason)", flush=True)
print("  - the SPHERE any item lands in: that is the output of Archipelago's", flush=True)
print("    randomized fill, and has no closed form", flush=True)
print("  - that fill SUCCEEDS: reachability is provable, fill terminating is not", flush=True)
