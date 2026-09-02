"""Full audit of the CW4 randomizer: generation, reachability, locations, items.

Run from inside the Archipelago clone (it imports AP and shells out to Generate.py).
Every finding is MEASURED here, not recited from docs.
"""
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__)))  # overridden below
AP = os.getcwd()
REPO = os.path.abspath(os.path.join(AP, ".."))
# Artifacts go somewhere gitignored, NOT next to the script - these used to land
# in the scratchpad, and dropping them into tools/audit/ would put generated json
# into the repo where it could be staged by accident.
OUT = os.path.join(REPO, ".aptest", "audit")
os.makedirs(OUT, exist_ok=True)

FAIL = []
WARN = []


def fresh_dirs(tag):
    """A clean, UNIQUE player/output pair for one generation.

    Reusing one output directory bit hard: rmtree(ignore_errors=True) leaves the
    folder behind whenever a file is locked, glob()[0] then returned the PREVIOUS
    run's archive, and every config in the matrix silently reported the first
    config's numbers - "no traps" and "all traps" both claimed 95 traps. A unique
    directory per generation cannot do that, and the caller asserts it finds
    exactly one archive.
    """
    pd = os.path.join(AP, f"_audit_p_{tag}")
    od = os.path.join(AP, f"_audit_o_{tag}")
    for d in (pd, od):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d, exist_ok=True)
    return pd, od


def one_archive(od, label):
    """The single archive in od, or None with a recorded failure."""
    zips = glob.glob(os.path.join(od, "AP_*.zip"))
    if len(zips) == 1:
        return zips[0]
    check(False, f"exactly one archive: {label}", f"found {len(zips)}")
    return None


def check(ok, label, detail=""):
    if ok:
        print(f"    PASS  {label}", flush=True)
    else:
        FAIL.append(f"{label}: {detail}")
        print(f"    FAIL  {label} -- {detail}", flush=True)


def warn(label, detail):
    WARN.append(f"{label}: {detail}")
    print(f"    NOTE  {label} -- {detail}", flush=True)


# ---------------------------------------------------------------- PART 1
print("-- PART 1/6: static tables --", flush=True)
sys.path.insert(0, AP)
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")

from worlds.cw4 import items as I, locations as L, rules as R, groups as G  # noqa: E402
from worlds.cw4 import options as O  # noqa: E402

print(f"[1/6] step 1/6: location table", flush=True)
loc_names = list(L.LOCATION_NAME_TO_ID)
check(len(loc_names) == len(set(loc_names)), "location names unique")
ids = list(L.LOCATION_NAME_TO_ID.values())
check(len(ids) == len(set(ids)), "location ids unique")
check(sorted(ids) == list(range(min(ids), min(ids) + len(ids))), "location ids contiguous")

kinds = {"Cache": 0, "Totem": 0, "Nullify": 0, "Reclaim": 0, "Custom": 0, "Mission Complete": 0}
per_mission = {n: 0 for n in range(1, 21)}
title_to_n = {t: n for n, t in I.MISSION_TITLES.items()}
unparsed = []
for name in loc_names:
    title, _, tail = name.partition(" - ")
    if title not in title_to_n:
        unparsed.append(name)
        continue
    per_mission[title_to_n[title]] += 1
    base = tail.rsplit(" ", 1)[0] if tail.rsplit(" ", 1)[-1].isdigit() else tail
    if base in kinds:
        kinds[base] += 1
    else:
        unparsed.append(name)
check(not unparsed, "every location name parses to a mission and kind", str(unparsed[:4]))
print(f"      total locations: {len(loc_names)}", flush=True)
for k, v in kinds.items():
    print(f"        {k:<18} {v}", flush=True)

print(f"[1/6] step 2/6: counts match INSTANCE_COUNTS", flush=True)
expect_cache = sum(c for c, _, _ in L.INSTANCE_COUNTS.values())
expect_totem = sum(t for _, t, _ in L.INSTANCE_COUNTS.values())
expect_null = sum(u for _, _, u in L.INSTANCE_COUNTS.values())
check(kinds["Cache"] == expect_cache, "cache count", f"{kinds['Cache']} vs {expect_cache}")
check(kinds["Totem"] == expect_totem, "totem count", f"{kinds['Totem']} vs {expect_totem}")
check(kinds["Nullify"] == expect_null, "nullify count", f"{kinds['Nullify']} vs {expect_null}")
check(kinds["Reclaim"] == len(L.RECLAIM_MISSIONS), "reclaim count")
check(kinds["Custom"] == len(L.CUSTOM_MISSIONS), "custom count")
check(kinds["Mission Complete"] == 19, "mission-complete count (20 minus the finale)",
      str(kinds["Mission Complete"]))

print(f"[1/6] step 3/6: item table", flush=True)
item_names = list(I.ITEM_NAME_TO_ID)
check(len(item_names) == len(set(item_names)), "item names unique")
iids = list(I.ITEM_NAME_TO_ID.values())
check(len(iids) == len(set(iids)), "item ids unique")
check(sorted(iids) == list(range(min(iids), min(iids) + len(iids))), "item ids contiguous")
CATS = {
    "Mission unlocks": I.MISSION_UNLOCK_ITEMS,
    "Unit unlocks": I.UNIT_ITEMS,
    "Bonus units": I.BONUS_UNIT_ITEMS,
    "Progressive ERN": [I.PROGRESSIVE_ERN],
    "Build limits (ids only)": I.BUILD_LIMIT_ITEMS,
    "Energy upgrades": [I.ENERGY_STORAGE_ITEM, I.BASE_GENERATION_ITEM],
    "Traps (ids)": I.TRAP_ITEMS,
    "ERN upgrade rate": I.ERN_RATE_ITEMS,
    "ERN upgrade cap": I.ERN_CAP_ITEMS,
    "Boons (one-shot)": I.BOON_ITEMS,
}
tot = 0
for k, v in CATS.items():
    print(f"        {k:<26} {len(v)}", flush=True)
    tot += len(v)
check(tot == len(item_names), "categories cover the id table", f"{tot} vs {len(item_names)}")
print(f"      total item NAMES (ids): {len(item_names)}", flush=True)
print(f"      poolable filler kinds: {len(I.POOL_FILLER_KINDS)}  "
      f"poolable traps: {len(I.POOL_TRAP_ITEMS)}", flush=True)

print(f"[1/6] step 4/6: rules reference only real items", flush=True)
logic_names = R.logic_item_names()
bad = [n for n in logic_names if n not in I.ITEM_NAME_TO_ID]
check(not bad, "every item named by logic exists", str(bad))
print(f"      items logic can require: {len(logic_names)}", flush=True)

print(f"[1/6] step 5/6: every location has a requirement entry", flush=True)
missing_rule = []
for name in loc_names:
    title, _, _ = name.partition(" - ")
    n = title_to_n[title]
    try:
        req = R.location_requirements(name, n)
    except Exception as e:  # noqa: BLE001
        missing_rule.append(f"{name}: {e}")
        continue
    for group in req:
        for item in group:
            if item not in I.ITEM_NAME_TO_ID:
                missing_rule.append(f"{name} requires missing item {item}")
check(not missing_rule, "every location rule resolves to real items", str(missing_rule[:3]))

print(f"[1/6] step 6/6: name groups", flush=True)
poolable = (set(I.MISSION_UNLOCK_ITEMS) | set(I.UNIT_ITEMS) | set(I.BONUS_UNIT_ITEMS)
            | {I.PROGRESSIVE_ERN} | set(I.POOL_FILLER_KINDS) | set(I.POOL_TRAP_ITEMS))
dead = {g: sorted(set(v) - poolable) for g, v in G.ITEM_NAME_GROUPS.items()
        if set(v) - poolable}
check(not dead, "no item group names an ungeneratable item", str(dead))
badloc = {g: sorted(set(v) - set(L.LOCATION_NAME_TO_ID))
          for g, v in G.LOCATION_NAME_GROUPS.items() if set(v) - set(L.LOCATION_NAME_TO_ID)}
check(not badloc, "no location group names a missing location", str(badloc))

# ---------------------------------------------------------------- PART 2
print("-- PART 2/6: cross-language consistency (apworld vs the game mod) --", flush=True)
mr = open(os.path.join(REPO, "src/CW4Archipelago.Core/MissionRules.cs"), encoding="utf-8").read()

print(f"[2/6] step 1/4: mission titles agree", flush=True)
cs_titles = dict((int(a), b) for a, b in re.findall(r'\[(\d+)\]\s*=\s*"([^"]+)"', mr))
check(cs_titles == I.MISSION_TITLES, "the mod's 20 titles equal the apworld's",
      str({k: (cs_titles.get(k), I.MISSION_TITLES.get(k)) for k in set(cs_titles) | set(I.MISSION_TITLES)
           if cs_titles.get(k) != I.MISSION_TITLES.get(k)}))

print(f"[2/6] step 2/4: final mission agrees", flush=True)
cs_final = int(re.search(r"FinalMission\s*=\s*(\d+)", mr).group(1))
check(cs_final == L.FINAL_MISSION, "FinalMission", f"mod {cs_final} vs apworld {L.FINAL_MISSION}")

print(f"[2/6] step 3/4: objective slots agree", flush=True)
block = mr[mr.index("MissionObjectives"):]
block = block[:block.index("};")]
cs_obj = {}
for m, body in re.findall(r"\[(\d+)\]\s*=\s*new\[\]\s*\{([^}]*)\}", block):
    cs_obj[int(m)] = sorted(int(x) for x in re.findall(r"\d+", body))
ap_obj = {}
for n in range(1, 21):
    c, t, u = L.INSTANCE_COUNTS[n]
    s = []
    if u:
        s.append(0)
    if t:
        s.append(1)
    if n in L.RECLAIM_MISSIONS:
        s.append(2)
    if c:
        s.append(4)
    if n in L.CUSTOM_MISSIONS:
        s.append(5)
    ap_obj[n] = s
diff = {n: (cs_obj.get(n), ap_obj[n]) for n in range(1, 21) if cs_obj.get(n) != ap_obj[n]}
check(not diff, "the mod's per-mission objective slots equal the apworld's location set", str(diff))

print(f"[2/6] step 4/4: trap names agree", flush=True)
tr = open(os.path.join(REPO, "src/CW4Archipelago.Core/TrapRules.cs"), encoding="utf-8").read()
cs_traps = set(re.findall(r'"((?:Spore|Creeper|Energy|Emitter|Unit|Ammo)[^"]*)"', tr))
missing = set(I.TRAP_ITEMS) - cs_traps
check(not missing, "every apworld trap name exists in the mod's TrapRules", str(sorted(missing)))
print(f"      apworld traps: {len(I.TRAP_ITEMS)} names, {len(I.POOL_TRAP_ITEMS)} generated", flush=True)

print(f"[2/6] step 5/5: ERN upgrade and energy item names agree", flush=True)
# The mod counts received items by these exact strings, so a rename on one side
# only would stop the upgrades applying without failing anywhere. Pinned here
# rather than trusting the two literals to stay in step: the apworld's own test
# compares its list against a hardcoded copy, which cannot see the C# side at
# all, so a rename that touched only Python would still pass it.
eur = open(os.path.join(REPO, "src/CW4Archipelago.Core/ErnUpgradeRules.cs"), encoding="utf-8").read()
cs_rate = re.search(r'RatePrefix\s*=\s*"([^"]*)"', eur)
cs_cap = re.search(r'CapPrefix\s*=\s*"([^"]*)"', eur)
check(cs_rate is not None and cs_rate.group(1) == I.ERN_RATE_PREFIX,
      "the mod's ERN rate prefix equals the apworld's",
      f"{cs_rate.group(1) if cs_rate else None!r} vs {I.ERN_RATE_PREFIX!r}")
check(cs_cap is not None and cs_cap.group(1) == I.ERN_CAP_PREFIX,
      "the mod's ERN cap prefix equals the apworld's",
      f"{cs_cap.group(1) if cs_cap else None!r} vs {I.ERN_CAP_PREFIX!r}")

# The six upgrade names are addressed BY INDEX on the mod side, so their ORDER
# matters as much as their spelling.
cs_names = re.search(r'UpgradeNames\s*=\s*\{(.*?)\};', eur, re.S)
cs_list = re.findall(r'"([^"]+)"', cs_names.group(1)) if cs_names else []
check(cs_list == I.ERN_UPGRADE_NAMES_ORDER,
      "the mod's ERN upgrade order equals the apworld's",
      f"{cs_list} vs {I.ERN_UPGRADE_NAMES_ORDER}")

en = open(os.path.join(REPO, "src/CW4Archipelago.Core/EnergyRules.cs"), encoding="utf-8").read()
cs_store = re.search(r'StorageItem\s*=\s*"([^"]*)"', en)
cs_gen = re.search(r'GenerationItem\s*=\s*"([^"]*)"', en)
check(cs_store is not None and cs_store.group(1) == I.ENERGY_STORAGE_ITEM,
      "the mod's energy storage item name equals the apworld's",
      f"{cs_store.group(1) if cs_store else None!r} vs {I.ENERGY_STORAGE_ITEM!r}")
check(cs_gen is not None and cs_gen.group(1) == I.BASE_GENERATION_ITEM,
      "the mod's base generation item name equals the apworld's",
      f"{cs_gen.group(1) if cs_gen else None!r} vs {I.BASE_GENERATION_ITEM!r}")
br = open(os.path.join(REPO, "src/CW4Archipelago.Core/BoonRules.cs"), encoding="utf-8").read()
cs_boons = set(re.findall(r'=\s*"([^"]+)"', br))
# The six surge names are BUILT from a prefix plus the upgrade order, so match
# them that way rather than looking for six literals that do not exist.
cs_prefix = re.search(r'SurgePrefix\s*=\s*"([^"]*)"', br)
if cs_prefix:
    for u in I.ERN_UPGRADE_NAMES_ORDER:
        cs_boons.add(cs_prefix.group(1) + u)
missing_boons = set(I.BOON_ITEMS) - cs_boons
check(not missing_boons, "every apworld boon name exists in the mod's BoonRules",
      str(sorted(missing_boons)))
print(f"      progressive names checked: 12 ERN upgrades + 2 energy", flush=True)
print(f"      boon names checked: {len(I.BOON_ITEMS)}", flush=True)

# ---------------------------------------------------------------- PART 3
print("-- PART 3/6: in-process logic (default options) --", flush=True)
from test.general import setup_solo_multiworld  # noqa: E402
from worlds.AutoWorld import AutoWorldRegister  # noqa: E402

wt = AutoWorldRegister.world_types["Creeper World 4"]
print(f"[3/6] step 1/3: building a solo multiworld", flush=True)
mw = setup_solo_multiworld(wt)
player = 1
all_locs = mw.get_locations(player)
print(f"      regions: {len(mw.get_regions(player))}  locations: {len(all_locs)}", flush=True)
check(len([l for l in all_locs if l.address is not None]) == len(loc_names),
      "the multiworld carries every table location",
      f"{len([l for l in all_locs if l.address is not None])} vs {len(loc_names)}")

print(f"[3/6] step 2/3: reachability with every item collected", flush=True)
state = mw.get_all_state(False)
unreachable = [l.name for l in all_locs if not l.can_reach(state)]
check(not unreachable, "every location is reachable once all items are held",
      f"{len(unreachable)} unreachable, e.g. {unreachable[:5]}")

print(f"[3/6] step 3/3: goal is satisfiable", flush=True)
check(bool(mw.completion_condition[player](state)), "completion condition satisfiable with all items")
pool = mw.itempool
prog = [i for i in pool if i.advancement]
print(f"      itempool {len(pool)}: progression {len(prog)}, "
      f"useful {len([i for i in pool if i.useful and not i.advancement])}, "
      f"trap {len([i for i in pool if i.trap])}, "
      f"filler {len([i for i in pool if i.filler and not i.trap])}", flush=True)

# ---------------------------------------------------------------- PART 4
print("-- PART 4/6: real generation across an option matrix --", flush=True)
MATRIX = [
    ("defaults", {}),
    ("casual logic", {"logic_difficulty": "casual"}),
    ("no traps", {"trap_percentage": 0}),
    ("all traps", {"trap_percentage": 100}),
    ("no erns", {"progressive_erns": 0}),
    ("max erns", {"progressive_erns": 40}),
    # Both ends of the ERN upgrade block. derive.py checks the same two labels,
    # and a config it derives but this matrix never generates shows up there as
    # a MISMATCH against an empty measurement rather than as a real result.
    ("no ern upgrades", {"ern_upgrade_copies": 0}),
    ("one ern upgrade copy", {"ern_upgrade_copies": 1}),
    ("no energy upgrades", {"energy_storage_copies": 0, "base_generation_copies": 0}),
    ("max energy upgrades", {"energy_storage_copies": 36, "base_generation_copies": 36}),
    ("one starter", {"starter_missions": 1}),
    ("five starters", {"starter_missions": 5}),
    ("finale open", {"missions_for_finale": 0}),
    ("finale maxed", {"missions_for_finale": 19}),
    ("filler weights all zero", {"filler_energy_storage_weight": 0,
                                 "filler_base_generation_weight": 0,
                                 "filler_build_limit_weight": 0}),
    ("trap weights all zero", {"trap_weight_spore_strike": 0, "trap_weight_spore_scatter": 0,
                               "trap_weight_creeper_surge": 0, "trap_weight_energy_drain": 0,
                               "trap_weight_emitter_overdrive": 0, "trap_weight_unit_stun": 0,
                               "trap_weight_ammo_drain": 0}),
]
results = []
total = len(MATRIX) + 1
for idx, (label, opts) in enumerate(MATRIX, 1):
    PD, OD = fresh_dirs(f"m{idx}")
    body = "\n".join(f"  {k}: {v}" for k, v in opts.items())
    with open(os.path.join(PD, "p.yaml"), "w", encoding="utf-8") as f:
        f.write(f"name: A1\ngame: Creeper World 4\nCreeper World 4:\n{body or '  {}'}\n")
    print(f"[4/6] config {idx}/{total} '{label}': generating", flush=True)
    p = subprocess.run([sys.executable, "Generate.py", "--player_files_path", PD,
                        "--outputpath", OD, "--seed", str(1000 + idx)],
                       capture_output=True, text=True, cwd=AP)
    if p.returncode != 0:
        tail = (p.stderr or p.stdout).strip().splitlines()[-3:]
        check(False, f"generation: {label}", " | ".join(tail))
        results.append((label, None))
        continue
    archive = one_archive(OD, label)
    if not archive:
        results.append((label, None))
        continue
    zf = zipfile.ZipFile(archive)
    spoiler = [n for n in zf.namelist() if "Spoiler" in n]
    stats = {"locations": 0, "events": 0, "playthrough_spheres": 0}
    counts = {}
    if spoiler:
        lines = zf.read(spoiler[0]).decode("utf-8", "replace").splitlines()
        li = [i for i, l in enumerate(lines) if l.strip().startswith("Locations")]
        pi = [i for i, l in enumerate(lines) if l.strip().startswith("Playthrough")]
        end = pi[0] if pi else len(lines)
        for l in lines[li[0] + 1:end]:
            if ":" not in l or not l.strip():
                continue
            # Split on the FIRST colon: the LOCATION is on the left and the item
            # on the right, and the item can itself contain a colon
            # ("Mission Unlock: Home"). rsplit here silently turned every mission
            # unlock into a bare mission title and reported zero unlocks.
            loc, _, item = l.partition(":")
            loc, item = loc.strip(), item.strip()
            if not item:
                continue
            # Event locations (19 "Mission Beaten" plus the Victory event) are
            # listed here too and have no id. They are real placements to AP but
            # not checks a player can find, so they are counted separately.
            if loc in L.LOCATION_NAME_TO_ID:
                counts[item] = counts.get(item, 0) + 1
                stats["locations"] += 1
            else:
                stats["events"] += 1
        # Sphere headers look like "0: {", not "0:".
        stats["playthrough_spheres"] = sum(
            1 for l in lines[end:] if re.match(r"^\s*\d+:\s*\{\s*$", l))
    stats["counts"] = counts
    results.append((label, stats))
    bl = sum(v for k, v in counts.items() if "Build Limit" in k)
    tr_ = sum(v for k, v in counts.items() if k in I.TRAP_ITEMS)
    bonus = sum(v for k, v in counts.items() if k in I.BONUS_UNIT_ITEMS)
    print(f"      placements {stats['locations']} (+{stats['events']} events), traps {tr_}, "
          f"bonus units {bonus}, build limits {bl}, "
          f"playthrough spheres {stats['playthrough_spheres']}", flush=True)
    check(bl == 0, f"no build limits: {label}", str(bl))
    check(bonus == 3, f"all three bonus units placed: {label}", str(bonus))
    check(stats["locations"] == len(loc_names),
          f"every location filled: {label}",
          f"{stats['locations']} vs {len(loc_names)}")
    check(stats["playthrough_spheres"] > 0, f"playthrough computed (beatable): {label}")

# 4-player multiworld
print(f"[4/6] config {total}/{total} '4-player multiworld': generating", flush=True)
PD, OD = fresh_dirs("mp")
for i in range(1, 5):
    with open(os.path.join(PD, f"p{i}.yaml"), "w", encoding="utf-8") as f:
        f.write(f"name: P{i}\ngame: Creeper World 4\nCreeper World 4: {{}}\n")
p = subprocess.run([sys.executable, "Generate.py", "--player_files_path", PD,
                    "--outputpath", OD, "--seed", "4242"],
                   capture_output=True, text=True, cwd=AP)
archive = one_archive(OD, "4-player") if p.returncode == 0 else None
check(bool(archive), "4-player multiworld generates",
      " | ".join((p.stderr or p.stdout).strip().splitlines()[-3:]) if not archive else "")
if archive:
    zf = zipfile.ZipFile(archive)
    sp = [n for n in zf.namelist() if "Spoiler" in n]
    if sp:
        lines = zf.read(sp[0]).decode("utf-8", "replace").splitlines()
        li = [i for i, l in enumerate(lines) if l.strip().startswith("Locations")][0]
        pi = [i for i, l in enumerate(lines) if l.strip().startswith("Playthrough")]
        body = lines[li + 1:pi[0] if pi else len(lines)]
        placed = [l.partition(":")[2].strip() for l in body if ":" in l and l.strip()]
        check(not [x for x in placed if "Build Limit" in x],
              "no build limits in a 4-player seed")
        check(len(placed) >= 4 * len(loc_names),
              "4-player seed fills every player's locations",
              f"{len(placed)} placements for 4 players")
        print(f"      4-player placements: {len(placed)}", flush=True)
shutil.rmtree(PD, ignore_errors=True)
shutil.rmtree(OD, ignore_errors=True)

# ---------------------------------------------------------------- PART 5
print("-- PART 5/6: quantities (defaults) --", flush=True)
base = dict(results)["defaults"]
if base:
    c = base["counts"]
    groups = {
        "Mission unlocks": sum(v for k, v in c.items() if k.startswith("Mission Unlock:")),
        "Unit unlocks": sum(v for k, v in c.items() if k in I.UNIT_ITEMS),
        "Bonus units": sum(v for k, v in c.items() if k in I.BONUS_UNIT_ITEMS),
        "Progressive ERN": c.get(I.PROGRESSIVE_ERN, 0),
        "Traps": sum(v for k, v in c.items() if k in I.TRAP_ITEMS),
        "Energy storage": c.get(I.ENERGY_STORAGE_ITEM, 0),
        "Base generation": c.get(I.BASE_GENERATION_ITEM, 0),
        "Build limits": sum(v for k, v in c.items() if "Build Limit" in k),
        "ERN upgrade rate": sum(v for k, v in c.items() if k in set(I.ERN_RATE_ITEMS)),
        "ERN upgrade cap": sum(v for k, v in c.items() if k in set(I.ERN_CAP_ITEMS)),
        "Boons": sum(v for k, v in c.items() if k in set(I.BOON_ITEMS)),
    }
    for k, v in groups.items():
        print(f"      {k:<20} {v}", flush=True)
    print(f"      {'TOTAL':<20} {sum(groups.values())}", flush=True)
    check(sum(groups.values()) == base["locations"], "categories cover every placement",
          f"{sum(groups.values())} vs {base['locations']}")

    with open(os.path.join(OUT, "audit-quantities.json"), "w", encoding="utf-8") as f:
        json.dump({"per_kind": kinds, "per_mission": per_mission,
                   "defaults_pool": groups, "matrix": {k: (v or {}).get("counts", {})
                                                       for k, v in results}}, f, indent=1)

# ---------------------------------------------------------------- PART 6
# How early do the weapons land? Cannon and Mortar are interchangeable in most
# logic groups (["Cannon", "Mortar"]), so which one a seed hands you first is the
# filler's choice, not the logic's - which is exactly what this measures.
# Only PROGRESSION items appear in a playthrough, and all three weapons are
# progression, so the sphere they appear in is their real logical depth.
print("-- PART 6/6: weapon timing across seeds --", flush=True)
WEAPONS = ["Cannon", "Mortar", "Sprayer", "Nullifier", "Miner", "Terp", "Sniper"]
SEEDS = 20
spheres = {w: [] for w in WEAPONS}
first_weapon = {}
max_sphere = []
for i in range(1, SEEDS + 1):
    PD, OD = fresh_dirs(f"w{i}")
    with open(os.path.join(PD, "p.yaml"), "w", encoding="utf-8") as f:
        f.write("name: W1\ngame: Creeper World 4\nCreeper World 4: {}\n")
    p = subprocess.run([sys.executable, "Generate.py", "--player_files_path", PD,
                        "--outputpath", OD, "--seed", str(7000 + i)],
                       capture_output=True, text=True, cwd=AP)
    zips = glob.glob(os.path.join(OD, "AP_*.zip")) if p.returncode == 0 else []
    if len(zips) != 1:
        print(f"[6/6] seed {i}/{SEEDS}: GENERATION FAILED", flush=True)
        check(False, f"weapon-timing seed {i} generates", f"{len(zips)} archives")
        continue
    zf = zipfile.ZipFile(zips[0])
    sp = [n for n in zf.namelist() if "Spoiler" in n]
    if not sp:
        continue
    lines = zf.read(sp[0]).decode("utf-8", "replace").splitlines()
    pi = [n for n, l in enumerate(lines) if l.strip().startswith("Playthrough")]
    if not pi:
        continue
    cur = 0
    found = {}
    for l in lines[pi[0] + 1:]:
        m = re.match(r"^\s*(\d+):\s*\{\s*$", l)
        if m:
            cur = int(m.group(1))
            continue
        if ":" not in l or not l.strip():
            continue
        _loc, _, item = l.partition(":")
        item = re.sub(r"\s*\([^)]*\)\s*$", "", item.strip()).strip()
        if item in WEAPONS and item not in found:
            found[item] = cur
    max_sphere.append(cur)
    for w in WEAPONS:
        if w in found:
            spheres[w].append(found[w])
    ordered = sorted((v, k) for k, v in found.items() if k in ("Cannon", "Mortar", "Sprayer"))
    if ordered:
        first_weapon[ordered[0][1]] = first_weapon.get(ordered[0][1], 0) + 1
    print(f"[6/6] seed {i}/{SEEDS}: spheres {max_sphere[-1]}, "
          + ", ".join(f"{w}={found.get(w, '-')}" for w in ("Cannon", "Mortar", "Sprayer")),
          flush=True)

shutil.rmtree(PD, ignore_errors=True)
shutil.rmtree(OD, ignore_errors=True)
print(f"      seeds measured: {len(max_sphere)}, mean total spheres "
      f"{sum(max_sphere) / max(1, len(max_sphere)):.1f}", flush=True)
for w in WEAPONS:
    v = spheres[w]
    if not v:
        print(f"      {w:<12} never in a playthrough (not required by logic)", flush=True)
        continue
    v = sorted(v)
    print(f"      {w:<12} sphere min {v[0]}  median {v[len(v) // 2]}  max {v[-1]}  "
          f"(in {len(v)}/{len(max_sphere)} seeds)  sphere1: "
          f"{sum(1 for x in v if x == 1)}", flush=True)
print(f"      first of Cannon/Mortar/Sprayer: "
      + ", ".join(f"{k}={v}" for k, v in sorted(first_weapon.items(), key=lambda kv: -kv[1])),
      flush=True)

try:
    with open(os.path.join(OUT, "audit-weapons.json"), "w", encoding="utf-8") as f:
        json.dump({"spheres": spheres, "first_weapon": first_weapon,
                   "total_spheres": max_sphere}, f, indent=1)
except OSError:
    pass

# Sweep every per-generation directory, so the clone is left as it was found.
for d in glob.glob(os.path.join(AP, "_audit_p_*")) + glob.glob(os.path.join(AP, "_audit_o_*")):
    shutil.rmtree(d, ignore_errors=True)
left = glob.glob(os.path.join(AP, "_audit_*"))
if left:
    warn("audit directories left behind", str([os.path.basename(x) for x in left]))

print(f"Done: {len(FAIL)} failures, {len(WARN)} notes", flush=True)
if FAIL:
    for f_ in FAIL:
        print(f"  FAIL {f_}", flush=True)
    sys.exit(1)
