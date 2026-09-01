"""Does the one-location funnel matter in a multiworld with OTHER games in it?

The premise to test: CW4's opening is only a hard constraint when CW4's own
locations are the only place its progression can live. Put another game in the
multiworld and the fill can park CW4's unlocks in that game's world, and fill
CW4's first check with that game's item - so the funnel stops being a funnel.

If that holds, the bootstrap should stand down in a mixed multiworld and leave the
shuffle alone.

Usage:  python mixed.py <label> <seeds>
"""
import io
import os
import shutil
import sys

AP = os.getcwd()
LABEL = sys.argv[1] if len(sys.argv) > 1 else "mixed"
SEEDS = int(sys.argv[2]) if len(sys.argv) > 2 else 40

os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

import Generate  # noqa: E402
from Main import main as ERmain  # noqa: E402
from BaseClasses import CollectionState  # noqa: E402

print(f"-- {LABEL}: CW4 (starter_missions 1) + ChecksFinder, {SEEDS} seeds --",
      flush=True)

failures = 0
foreign_in_opening = 0
cw4_items_abroad = []
checked = 0
for i in range(1, SEEDS + 1):
    pd = os.path.join(AP, f"_mx_p_{i}")
    od = os.path.join(AP, f"_mx_o_{i}")
    for d in (pd, od):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d, exist_ok=True)
    with io.open(os.path.join(pd, "a.yaml"), "w", encoding="utf-8") as f:
        f.write("name: CW\ngame: Creeper World 4\n"
                "Creeper World 4:\n  starter_missions: 1\n")
    with io.open(os.path.join(pd, "b.yaml"), "w", encoding="utf-8") as f:
        f.write("name: CF\ngame: ChecksFinder\nChecksFinder: {}\n")
    saved = sys.argv
    sys.argv = ["Generate.py", "--player_files_path", pd, "--outputpath", od,
                "--seed", str(31000 + i), "--log_level", "error"]
    try:
        erargs, seed = Generate.main()
        mw = ERmain(erargs, seed)
    except Exception as e:  # noqa: BLE001
        failures += 1
        print(f"[{LABEL}] seed {31000 + i}: FAILED "
              f"{type(e).__name__}: {str(e).splitlines()[0]}", flush=True)
        sys.argv = saved
        shutil.rmtree(pd, ignore_errors=True)
        shutil.rmtree(od, ignore_errors=True)
        continue
    sys.argv = saved

    cw4 = next(p for p in mw.player_ids if mw.worlds[p].game == "Creeper World 4")
    empty = CollectionState(mw)
    opening = [l for l in mw.get_locations(cw4)
               if l.address is not None and l.can_reach(empty)]
    checked += 1
    for l in opening:
        if l.item is not None and l.item.player != cw4:
            foreign_in_opening += 1
    abroad = [it.name for p in mw.player_ids if p != cw4
              for it in [loc.item for loc in mw.get_locations(p) if loc.item]
              if it.player == cw4 and it.advancement]
    cw4_items_abroad.append(len(abroad))

    del mw
    shutil.rmtree(pd, ignore_errors=True)
    shutil.rmtree(od, ignore_errors=True)

for d in os.listdir(AP):
    if d.startswith("_mx_"):
        shutil.rmtree(os.path.join(AP, d), ignore_errors=True)

print(f"[{LABEL}] failures: {failures}/{SEEDS}", flush=True)
if checked:
    avg = sum(cw4_items_abroad) / len(cw4_items_abroad)
    print(f"[{LABEL}] CW4 opening checks holding ANOTHER game's item: "
          f"{foreign_in_opening} across {checked} seeds", flush=True)
    print(f"[{LABEL}] CW4 progression items placed in the other world: "
          f"{avg:.1f} per seed (min {min(cw4_items_abroad)}, "
          f"max {max(cw4_items_abroad)})", flush=True)
print(f"Done: {LABEL}", flush=True)
