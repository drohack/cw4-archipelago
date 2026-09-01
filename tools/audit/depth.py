"""Where does each weapon actually LAND, in true logical spheres?

The playthrough in a spoiler only lists items needed to beat the game, so the
redundant half of the Cannon/Mortar pair shows up as absent rather than late -
which is precisely the thing we need a number for. MultiWorld.get_spheres()
computes the sphere of every FILLED location, redundant items included, so this
generates in-process and asks Archipelago directly.

Usage:  python depth.py <label> <seeds> ["opt: val, opt: val"]
Run from inside the Archipelago clone.
"""
import io
import os
import shutil
import sys

AP = os.getcwd()
LABEL = sys.argv[1] if len(sys.argv) > 1 else "run"
SEEDS = int(sys.argv[2]) if len(sys.argv) > 2 else 20
OPTS = sys.argv[3] if len(sys.argv) > 3 else ""
BODY = "\n".join("  " + o.strip() for o in OPTS.split(",") if o.strip()) or "  {}"

os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

import Generate  # noqa: E402
from Main import main as ERmain  # noqa: E402

TRACK = ["Cannon", "Mortar", "Sprayer", "Missile Launcher", "Nullifier", "Terp", "Sniper", "Miner"]

print(f"-- depth sweep '{LABEL}': {SEEDS} seeds, options [{OPTS or 'defaults'}] --",
      flush=True)

rows = {w: [] for w in TRACK}
totals = []
opener = {}

for i in range(1, SEEDS + 1):
    pd = os.path.join(AP, f"_dp_p_{i}")
    od = os.path.join(AP, f"_dp_o_{i}")
    for d in (pd, od):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d, exist_ok=True)
    with io.open(os.path.join(pd, "p.yaml"), "w", encoding="utf-8") as f:
        f.write("name: D1\ngame: Creeper World 4\nCreeper World 4:\n" + BODY + "\n")

    # Let Generate build its own Namespace. Hand-rolling one means silently
    # missing whichever field it grew last (allow_quantity was the first).
    saved_argv = sys.argv
    sys.argv = ["Generate.py", "--player_files_path", pd, "--outputpath", od,
                "--seed", str(9000 + i), "--spoiler", "1", "--log_level", "error"]
    try:
        erargs, seed = Generate.main()
        mw = ERmain(erargs, seed)
    except Exception as e:  # noqa: BLE001
        print(f"[{LABEL}] seed {i}/{SEEDS}: FAILED {type(e).__name__}: {e}", flush=True)
        shutil.rmtree(pd, ignore_errors=True)
        shutil.rmtree(od, ignore_errors=True)
        continue
    finally:
        sys.argv = saved_argv

    depth = {}
    for n, sphere in enumerate(mw.get_spheres(), start=1):
        if not sphere:
            break
        for loc in sphere:
            if loc.item is not None and loc.item.name in TRACK and loc.item.name not in depth:
                depth[loc.item.name] = n
        total = n
    totals.append(total)
    for w in TRACK:
        if w in depth:
            rows[w].append((depth[w], total))
    pair = [(depth[w], w) for w in ("Cannon", "Mortar") if w in depth]
    if pair:
        opener[min(pair)[1]] = opener.get(min(pair)[1], 0) + 1
    print(f"[{LABEL}] seed {i}/{SEEDS}: {total} spheres, "
          + ", ".join(f"{w}={depth.get(w, '?')}" for w in ("Cannon", "Mortar", "Sprayer")),
          flush=True)

    del mw
    shutil.rmtree(pd, ignore_errors=True)
    shutil.rmtree(od, ignore_errors=True)

print(f"[{LABEL}] SUMMARY over {len(totals)} seeds, mean depth "
      f"{sum(totals) / max(1, len(totals)):.1f} spheres", flush=True)
for w in TRACK:
    pairs = rows[w]
    if not pairs:
        print(f"[{LABEL}]   {w:<10} never placed?", flush=True)
        continue
    d = sorted(x for x, _ in pairs)
    frac = sorted(x / t for x, t in pairs)
    late = sum(1 for x, t in pairs if x / t > 0.6)
    last = sum(1 for x, t in pairs if x == t)
    print(f"[{LABEL}]   {w:<10} sphere min {d[0]} median {d[len(d) // 2]} max {d[-1]} "
          f"| median position {frac[len(frac) // 2]:.0%} of the seed "
          f"| last-40% {late}/{len(pairs)} | final sphere {last}/{len(pairs)}",
          flush=True)
print(f"[{LABEL}] OPENER: "
      + ", ".join(f"{k}={v}" for k, v in sorted(opener.items(), key=lambda kv: -kv[1])),
      flush=True)

# BY ROLE, not by name. Comparing "Cannon unforced" against "Cannon when Mortar is
# forced" compares an item that opens half the time against one that never opens,
# so it looks like a regression even if nothing moved. The honest comparison is
# opening weapon vs opening weapon, second weapon vs second weapon.
roles = {"opening": [], "second": []}
pairs = list(zip(rows["Cannon"], rows["Mortar"]))
for (cd, ct), (md, _mt) in pairs:
    roles["opening"].append((min(cd, md), ct))
    roles["second"].append((max(cd, md), ct))
for role in ("opening", "second"):
    v = sorted(x for x, _ in roles[role])
    fr = sorted(x / t for x, t in roles[role])
    late = sum(1 for x, t in roles[role] if x / t > 0.6)
    last = sum(1 for x, t in roles[role] if x == t)
    print(f"[{LABEL}]   {role:<8} weapon: sphere min {v[0]} median {v[len(v) // 2]} "
          f"max {v[-1]} | median position {fr[len(fr) // 2]:.0%} "
          f"| last-40% {late}/{len(v)} | final sphere {last}/{len(v)}", flush=True)
for w in ("Cannon", "Mortar"):
    if rows[w]:
        print(f"[{LABEL}]   {w} depth/total: "
              + " ".join(f"{x}/{t}" for x, t in rows[w]), flush=True)
print(f"Done: {LABEL}", flush=True)
