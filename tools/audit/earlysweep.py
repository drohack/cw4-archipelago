"""Does what a seed asks to place EARLY actually land early - in every shape?

WHY THIS EXISTS. `early_weapon` and the early mission unlock are requests in
multiworld.local_early_items. Every test of them checked that the REQUEST was
recorded, never where the item LANDED, and on 2026-10-06 that turned out to
hide two holes:

  - on a seed where every player is Creeper World 4, our own progression fill
    placed every CW4 item before Archipelago's early-items step ran, so the
    request was dropped without a word (the weapon reached sphere 0 on 27
    percent of solo seeds, the unlock on 11);
  - in a multiworld with another game and a narrow opening, the request was
    skipped in favour of a bootstrap that only runs when every player is CW4,
    so nothing placed the weapon early at all.

This measures the thing itself: generate through the player's real path
(Generate.main then Main.main, so start inventory, locality, plando and
progression balancing all run), then look at where each requested item is.

SHAPES vary the multiworld, and the CW4 options are ROLLED per seed from yaml
weights by Generate's own seeded random, so one run covers the option space and
any seed can be regenerated exactly from its number.

"EARLY" IS COMPUTED THREE WAYS, and they must agree on every CW4 player:
  (a) Archipelago's own definition, the one distribute_early_items uses:
      CollectionState(mw) swept through event locations only.
  (b) The first sphere of mw.get_spheres().
  (c) The rule tables: the starter missions' locations with no requirements.
A disagreement is reported, not resolved - it means the checker is wrong.

CONTROLS, because a checker that quietly says "early" proves nothing:
  - a forced FillError must be counted as a failure;
  - on the first seed and every Nth, the early weapon is SWAPPED with a late
    item of the same player and the checker must flag it, then swapped back;
  - Timespinner makes its own local early-item request, measured the same way;
  - run on unfixed code, solo seeds must reproduce the bug.

Run from inside the Archipelago clone, after tools/ap-sync.ps1:

    PYTHONUNBUFFERED=1 PYTHONHASHSEED=0 py -3.13 -u ../tools/audit/earlysweep.py \\
        --shape solo --seeds 2000 --out <dir>

Shapes: solo, cw4x2, cw4x4, cw4+1, cw4+4, cw4x2+2, cw4+1-narrow.
--logic standard|casual|easy pins logic_difficulty instead of rolling it, for a
run that measures one tier; --early-weapon cannon|mortar|random does the same
for early_weapon.
Exit 0 when every seed generated, every requested item was early, the three
definitions agreed and every control was detected; 1 otherwise; 2 when the
harness itself is broken (config error, non-deterministic, control missed).
"""
import argparse
import collections
import gc
import hashlib
import itertools
import json
import logging
import os
import random
import shutil
import sys
import tempfile
import time

AP = os.getcwd()
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

import ModuleUpdate  # noqa: E402
ModuleUpdate.update_ran = True  # never pip install mid-run

import Generate  # noqa: E402
import Main  # noqa: E402
from BaseClasses import CollectionState  # noqa: E402
from Fill import FillError  # noqa: E402

GAME = "Creeper World 4"
WEAPONS = ("Cannon", "Mortar")

# Every CW4 player rolls these per seed. Equal weights, explicit values - a
# "default" key would hide which value was actually used.
CW4_WEIGHTS = {
    "early_weapon": "{cannon: 1, mortar: 1, random: 1}",
    "logic_difficulty": "{standard: 1, casual: 1, easy: 1}",
    "starter_missions": "{2: 1, 3: 1, 4: 1, 6: 1}",
    # Quoted: unquoted on/off are YAML 1.1 booleans.
    "span_missions": "{'false': 1, 'true': 1}",
    "trap_percentage": "{0: 1, 50: 1, 100: 1}",
    "missions_for_finale": "{12: 1, 19: 1}",
}
# The narrow shape exists for the mixed-multiworld hole: two starters, where
# casual's threshold sits above the width and SPAN can make the opening 1 wide.
CW4_NARROW = dict(CW4_WEIGHTS, starter_missions="2")
ROLLED = list(CW4_WEIGHTS)

PARTNERS = ["ChecksFinder", "VVVVVV", "Meritous", "Timespinner"]

# shape -> list of player lineups; seed i uses lineup i % len(lineups).
# Each lineup is a list of (game, weights) with weights None for a partner.
SHAPES = {
    "solo": [[(GAME, CW4_WEIGHTS)]],
    "cw4x2": [[(GAME, CW4_WEIGHTS)] * 2],
    "cw4x4": [[(GAME, CW4_WEIGHTS)] * 4],
    "cw4+1": [[(GAME, CW4_WEIGHTS), (p, None)] for p in PARTNERS],
    "cw4+4": [[(GAME, CW4_WEIGHTS)] + [(p, None) for p in PARTNERS]],
    "cw4x2+2": [[(GAME, CW4_WEIGHTS)] * 2 + [(a, None), (b, None)]
                for a, b in itertools.combinations(PARTNERS, 2)],
    "cw4+1-narrow": [[(GAME, CW4_NARROW), (p, None)] for p in PARTNERS],
}

# Archipelago's own early-item warnings: an independent signal that it could not
# keep a request, counted per seed.
AP_WARNINGS = {
    "could_not_fulfill": "Could not fulfill rules of early items",
    "ran_out": "Ran out of early locations",
    "not_all_placed": "Not all items placed",
    "cw4_early_unplaced": "CW4: could not place early",
}


class SeedLog(logging.Handler):
    """Buffers WARNING+ records for one seed. Installed on the root logger, so
    Python's last-resort stderr handler never fires and the output stays one
    readable line per event."""

    def __init__(self):
        super().__init__(logging.WARNING)
        self.messages = []

    def emit(self, record):
        try:
            self.messages.append(record.getMessage())
        except Exception:  # noqa: BLE001
            self.messages.append(str(record.msg))


def write_lineups(shape, root, pins=None):
    """One directory of yamls per lineup, written once and reused every seed.
    `pins` overrides CW4 weights by key, for a run that measures one value."""
    dirs = []
    for li, lineup in enumerate(SHAPES[shape]):
        d = os.path.join(root, f"lineup{li}")
        os.makedirs(d, exist_ok=True)
        for pi, (game, weights) in enumerate(lineup, 1):
            # Files are read in casefold order, so the prefix fixes player order.
            short = "CW" if game == GAME else game[:4]
            with open(os.path.join(d, f"p{pi:02d}.yaml"), "w", encoding="utf-8") as f:
                f.write(f"name: {short}{pi}\ngame: {game}\n")
                if weights is None:
                    f.write(f"{game}: {{}}\n")
                else:
                    f.write(f"{game}:\n")
                    for key, value in dict(weights, **(pins or {})).items():
                        f.write(f"  {key}: {value}\n")
        dirs.append(d)
    return dirs


def generate(player_dir, out_dir, seed):
    """One real generation. Returns (multiworld or None, erargs, error string)."""
    argv = ["--player_files_path", player_dir, "--outputpath", out_dir,
            "--seed", str(seed), "--multi", "1", "--skip_output",
            "--spoiler", "0", "--log_level", "warning"]
    try:
        args = Generate.mystery_argparse(argv)
        assert not args.race, "race mode would make the seed non-reproducible"
        erargs, seed = Generate.main(args)
    except Exception as exc:  # noqa: BLE001
        # A yaml or option error is the HARNESS being wrong, not the world.
        print(f"HARNESS ERROR in Generate.main: {type(exc).__name__}: "
              f"{str(exc).splitlines()[0] if str(exc) else ''}", flush=True)
        raise SystemExit(2)
    try:
        mw = Main.main(erargs, seed)
    except Exception as exc:  # noqa: BLE001
        first = str(exc).splitlines()[0] if str(exc) else ""
        return None, erargs, f"{type(exc).__name__}: {first[:300]}"
    return mw, erargs, None


def rolled_options(erargs, player):
    out = {}
    for key in ROLLED:
        opt = getattr(erargs, key, {}).get(player)
        if opt is None:
            out[key] = None
        else:
            # A Range has a name_lookup too, but only for its special values, so
            # current_key raises KeyError on an ordinary number.
            out[key] = (opt.current_key if opt.value in getattr(opt, "name_lookup", {})
                        else opt.value)
    return out


def early_sets(mw, cw4_players):
    """The three independent definitions of 'early', per CW4 player."""
    from worlds.cw4.locations import location_names_for_mission
    from worlds.cw4.rules import is_casual, location_requirements

    state = CollectionState(mw)
    state.sweep_for_advancements(
        locations=(loc for loc in mw.get_filled_locations() if loc.address is None))
    first_sphere = next(iter(mw.get_spheres()), set())
    out = {}
    for p in mw.player_ids:
        a = {loc for loc in mw.get_locations(p)
             if loc.address is not None and loc.can_reach(state)}
        b = {loc for loc in first_sphere if loc.player == p and loc.address is not None}
        c = None
        if p in cw4_players:
            world = mw.worlds[p]
            casual = is_casual(world)
            c = {mw.get_location(name, p)
                 for m in world.starter_missions
                 for name in location_names_for_mission(m)
                 if not location_requirements(name, m, casual)}
        out[p] = (a, b, c)
    return out


def holders(mw):
    """(player, item name) -> every location holding that item."""
    index = collections.defaultdict(list)
    for loc in mw.get_filled_locations():
        if loc.item is not None:
            index[(loc.item.player, loc.item.name)].append(loc)
    return index


def where(index, sets, player, name):
    """Where one requested item is, and whether that spot is early by each
    definition. Early is judged in the HOLDING world, so an item that fell back
    into another player's world is visible as such rather than as late."""
    locs = index.get((player, name), [])
    if len(locs) != 1:
        return {"copies": len(locs), "own": None, "a": False, "b": False, "c": None}
    loc = locs[0]
    a, b, c = sets[loc.player]
    return {"copies": 1, "own": loc.player == player,
            "a": loc in a, "b": loc in b, "c": (loc in c) if c is not None else None}


def swap_control(mw, cw4_players, index, sets):
    """Move an early weapon somewhere late and require the checker to notice.
    Returns True (detected), False (missed) or None (nothing to swap)."""
    for p in cw4_players:
        a, _b, _c = sets[p]
        for name in WEAPONS:
            locs = index.get((p, name), [])
            if len(locs) != 1 or locs[0] not in a:
                continue
            early_loc = locs[0]
            late = next((loc for loc in mw.get_locations(p)
                         if loc.address is not None and loc.item is not None
                         and loc not in a and not loc.item.advancement), None)
            if late is None:
                continue
            w, x = early_loc.item, late.item
            early_loc.item, late.item = x, w
            w.location, x.location = late, early_loc
            try:
                new_sets = early_sets(mw, cw4_players)
                r = where(holders(mw), new_sets, p, name)
                return not (r["a"] or r["b"] or r["c"])
            finally:
                early_loc.item, late.item = w, x
                w.location, x.location = early_loc, late
    return None


def full_spheres(mw, wanted):
    """Sphere index of each (player, name) in wanted. Only called on the rows
    that need an ORDER rather than an early/late answer - it is the costly part."""
    found = {}
    for i, sphere in enumerate(mw.get_spheres()):
        for loc in sphere:
            if loc.item is not None:
                key = (loc.item.player, loc.item.name)
                if key in wanted and key not in found:
                    found[key] = i
        if len(found) == len(wanted):
            break
    return found


def fingerprint(mw):
    rows = sorted((loc.player, loc.name, loc.item.player, loc.item.name)
                  for loc in mw.get_filled_locations() if loc.item is not None)
    return hashlib.sha256(repr(rows).encode("utf-8")).hexdigest()[:16]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--shape", required=True, choices=sorted(SHAPES))
    ap.add_argument("--seeds", type=int, default=2000)
    ap.add_argument("--start", type=int, default=1)
    ap.add_argument("--out", required=True, help="results directory (outside the repo)")
    ap.add_argument("--control-every", type=int, default=100)
    ap.add_argument("--logic", choices=["standard", "casual", "easy"],
                    help="pin logic_difficulty instead of rolling it")
    ap.add_argument("--early-weapon", choices=["cannon", "mortar", "random"],
                    help="pin early_weapon instead of rolling it")
    args = ap.parse_args()
    shape = args.shape
    pins = {}
    if args.logic:
        pins["logic_difficulty"] = args.logic
    if args.early_weapon:
        pins["early_weapon"] = args.early_weapon
    tag = f"[{'/'.join([shape] + list(pins.values()))}]"

    if os.environ.get("PYTHONHASHSEED") != "0":
        print(f"{tag} HARNESS ERROR: run with PYTHONHASHSEED=0, or partner worlds' "
              "set ordering makes a seed unreproducible in another process", flush=True)
        return 2

    root = logging.getLogger()
    for h in list(root.handlers):
        root.removeHandler(h)
    seedlog = SeedLog()
    root.addHandler(seedlog)
    root.setLevel(logging.WARNING)

    import worlds.cw4.opening as cw4_opening
    with open(cw4_opening.__file__, "rb") as f:
        code_sha = hashlib.sha256(f.read()).hexdigest()[:12]
    from settings import get_settings
    panic = get_settings().generator.panic_method
    print(f"{tag} code: {cw4_opening.__file__} sha256 {code_sha}; "
          f"own fill cap {cw4_opening.OWN_FILL_ATTEMPTS}; panic_method {panic}", flush=True)

    os.makedirs(args.out, exist_ok=True)
    work = tempfile.mkdtemp(prefix=f"earlysweep_{shape.replace('+', 'p')}_")
    lineup_dirs = write_lineups(shape, os.path.join(work, "players"), pins)
    out_dir = os.path.join(work, "output")
    rows_path = os.path.join(args.out, f"{shape.replace('+', 'p')}{'-' + args.logic if args.logic else ''}.jsonl")
    rows_file = open(rows_path, "w", encoding="utf-8")
    rows_file.write(json.dumps({"header": True, "shape": shape, "code_sha": code_sha,
                                "seeds": args.seeds, "start": args.start}) + "\n")

    def seed_for(i):
        return random.Random(f"{shape}:{i}").getrandbits(48)

    # CONTROL 1: determinism. The same seed twice must place identically.
    d0 = lineup_dirs[args.start % len(lineup_dirs)]
    fps = []
    for _ in range(2):
        mw, _e, err = generate(d0, out_dir, seed_for(args.start))
        fps.append(err or fingerprint(mw))
        del mw
    if fps[0] != fps[1]:
        print(f"{tag} HARNESS ERROR: seed {seed_for(args.start)} generated twice "
              f"differently ({fps[0]} vs {fps[1]})", flush=True)
        return 2
    print(f"{tag} control ok: generation is deterministic ({fps[0]})", flush=True)

    # CONTROL 2: a forced FillError must be counted, through the same call path.
    real = Main.distribute_items_restrictive

    def _forced(*_a, **_k):
        raise FillError("positive control")
    Main.distribute_items_restrictive = _forced
    try:
        mw, _e, err = generate(d0, out_dir, seed_for(args.start))
    finally:
        Main.distribute_items_restrictive = real
    if not (mw is None and err and "positive control" in err):
        print(f"{tag} HARNESS ERROR: a forced FillError was not detected", flush=True)
        return 2
    print(f"{tag} control ok: a forced FillError is counted as a failure", flush=True)
    seedlog.messages.clear()

    # Tallies.
    fails, inaccessible, unbeatable = [], 0, 0
    req = collections.Counter()          # weapon / unlock requested
    early = collections.Counter()        # weapon / unlock early when requested
    other_early = collections.Counter()  # the other weapon early, both honoured
    other_seen = 0
    disagree = []
    misses = []
    not_requested = collections.Counter()
    bootstrap_first = collections.Counter()
    by_option = collections.defaultdict(lambda: [0, 0])  # (opt, value) -> [req, early]
    depths = collections.Counter()
    controls = collections.Counter()
    ts_req, ts_early = 0, 0
    warn = collections.Counter()
    other_warn = collections.Counter()
    started = time.time()

    for n in range(args.start, args.start + args.seeds):
        k = n - args.start + 1
        li = n % len(lineup_dirs)
        seed = seed_for(n)
        seedlog.messages.clear()
        mw, erargs, err = generate(lineup_dirs[li], out_dir, seed)

        for msg in seedlog.messages:
            hit = [key for key, needle in AP_WARNINGS.items() if needle in msg]
            for key in hit:
                warn[key] += 1
            if not hit:
                other_warn[msg.splitlines()[0][:120]] += 1

        cw4_players = [p for p, g in erargs.game.items() if g == GAME]
        if mw is None:
            fails.append((seed, li, err, {p: rolled_options(erargs, p) for p in cw4_players}))
            rows_file.write(json.dumps({"seed": seed, "i": n, "lineup": li,
                                        "fail": err}) + "\n")
        else:
            try:
                ok_access = mw.fulfills_accessibility()
            except FillError:
                ok_access = False
            ok_beat = mw.can_beat_game()
            inaccessible += not ok_access
            unbeatable += not ok_beat

            sets = early_sets(mw, cw4_players)
            index = holders(mw)
            for p in cw4_players:
                world = mw.worlds[p]
                opts = rolled_options(erargs, p)
                asked = world.early_weapon
                other = "Mortar" if asked == "Cannon" else "Cannon"
                local = dict(mw.local_early_items[p])
                unlock = next((x for x in local if x.startswith("Mission Unlock:")), None)
                w_req = asked in local
                row = {"seed": seed, "i": n, "lineup": li, "player": p, **opts,
                       "early_weapon_resolved": asked,
                       "starters": list(world.starter_missions),
                       "needs_bootstrap": world.needs_bootstrap(),
                       "bootstrapped": len(getattr(world, "bootstrapped", []) or []),
                       "own_fill_attempts": getattr(world, "own_fill_attempts", None),
                       "weapon_requested": w_req, "unlock_requested": unlock}
                a, b, c = sets[p]
                if not (a == b and (c is None or a == c)):
                    disagree.append((seed, p, len(a), len(b), len(c or ())))
                row["early_slots"] = len(a)
                if row["own_fill_attempts"]:
                    depths[row["own_fill_attempts"]] += 1

                for kind, name, asked_for in (("weapon", asked, w_req),
                                              ("unlock", unlock, unlock is not None)):
                    if not asked_for:
                        continue
                    r = where(index, sets, p, name)
                    row[kind] = r
                    req[kind] += 1
                    ok = r["copies"] == 1 and r["own"] and r["a"]
                    early[kind] += bool(ok)
                    if kind == "weapon":
                        for key in ROLLED:
                            by_option[(key, opts[key])][0] += 1
                            by_option[(key, opts[key])][1] += bool(ok)
                    if not ok and len(misses) < 200:
                        misses.append((seed, n, li, p, kind, name, r, opts))

                if w_req:
                    ro = where(index, sets, p, other)
                    if unlock is not None and len(a) == 2:
                        other_seen += 1
                        other_early["at width 2"] += bool(ro["a"])
                    row["other_weapon_early"] = ro["a"]
                else:
                    reason = ("bootstrap" if row["needs_bootstrap"]
                              else "width 1" if len(a) < 2 else "NOT REQUESTED")
                    not_requested[reason] += 1
                    sph = full_spheres(mw, {(p, asked), (p, other)})
                    first = sph.get((p, asked), 99) <= sph.get((p, other), 99)
                    row["not_requested_reason"] = reason
                    row["asked_sphere"] = sph.get((p, asked))
                    row["other_sphere"] = sph.get((p, other))
                    bootstrap_first[(reason, "asked first")] += first
                    bootstrap_first[(reason, "total")] += 1
                rows_file.write(json.dumps(row) + "\n")

            # Timespinner's own request: an independent early-items control.
            for p, g in erargs.game.items():
                if g != "Timespinner":
                    continue
                for name in mw.local_early_items[p]:
                    r = where(index, sets, p, name)
                    ts_req += 1
                    ts_early += bool(r["copies"] == 1 and r["own"] and r["a"])

            if k == 1 or k % args.control_every == 0:
                verdict = swap_control(mw, cw4_players, index, sets)
                if verdict is not None:
                    controls["run"] += 1
                    controls["detected"] += verdict
            del sets, index
            del mw
        del erargs

        if k % 50 == 0:
            gc.collect()
        if k % 100 == 0 or k == args.seeds:
            rate = k / max(1e-9, time.time() - started)
            deepest = max(depths) if depths else 0
            print(f"{tag} seed {k}/{args.seeds}: fails {len(fails)}, unreachable "
                  f"{inaccessible}, unbeatable {unbeatable}, weapon early "
                  f"{early['weapon']}/{req['weapon']}, unlock early "
                  f"{early['unlock']}/{req['unlock']}, disagree {len(disagree)}, "
                  f"controls {controls['detected']}/{controls['run']}, deepest retry "
                  f"{deepest}/{cw4_opening.OWN_FILL_ATTEMPTS}, {rate:.2f} seeds/s", flush=True)
        if k % 500 == 0:
            print(f"{tag} memory: {len(gc.get_objects())} live objects", flush=True)
        rows_file.flush()

    rows_file.close()
    shutil.rmtree(work, ignore_errors=True)

    def pct(x, y):
        return f"{x}/{y} ({100.0 * x / y:.1f}%)" if y else f"{x}/0"

    print(f"{tag} ===== summary: {args.seeds} seeds, code {code_sha} =====", flush=True)
    print(f"{tag} generation failures {len(fails)}, unreachable {inaccessible}, "
          f"unbeatable {unbeatable}", flush=True)
    print(f"{tag} weapon early when requested: {pct(early['weapon'], req['weapon'])}", flush=True)
    print(f"{tag} unlock early when requested: {pct(early['unlock'], req['unlock'])}", flush=True)
    print(f"{tag} other weapon early (both requests honoured, width 2): "
          f"{pct(other_early['at width 2'], other_seen)}", flush=True)
    print(f"{tag} not requested: {dict(not_requested)}", flush=True)
    for reason in sorted({r for r, _ in bootstrap_first}):
        print(f"{tag}   {reason}: asked weapon first "
              f"{pct(bootstrap_first[(reason, 'asked first')], bootstrap_first[(reason, 'total')])}",
              flush=True)
    print(f"{tag} definitions disagree: {len(disagree)}", flush=True)
    print(f"{tag} swap control detected: {controls['detected']}/{controls['run']}", flush=True)
    if ts_req:
        print(f"{tag} Timespinner's own early item early: {pct(ts_early, ts_req)}", flush=True)
    print(f"{tag} own fill retry depth: "
          f"{' '.join(f'{d}:{c}' for d, c in sorted(depths.items())) or 'n/a'} "
          f"(cap {cw4_opening.OWN_FILL_ATTEMPTS})", flush=True)
    print(f"{tag} Archipelago early-item warnings: {dict(warn) or 'none'}", flush=True)
    for msg, count in other_warn.most_common(5):
        print(f"{tag}   other warning x{count}: {msg}", flush=True)
    print(f"{tag} weapon early by rolled option:", flush=True)
    for key in ROLLED:
        cells = [f"{value}={pct(v[1], v[0])}" for (k2, value), v in sorted(
            by_option.items(), key=lambda kv: str(kv[0][1])) if k2 == key]
        print(f"{tag}   {key}: " + ", ".join(cells), flush=True)
    for seed, li, err, opts in fails[:10]:
        print(f"{tag} FAILED seed {seed} lineup {li}: {err} options {opts}", flush=True)
    for seed, n, li, p, kind, name, r, opts in misses[:10]:
        print(f"{tag} MISS seed {seed} (i {n}, lineup {li}) P{p} {kind} {name}: {r} "
              f"options {opts}", flush=True)
    for d in disagree[:10]:
        print(f"{tag} DISAGREE seed {d[0]} P{d[1]}: a={d[2]} b={d[3]} c={d[4]}", flush=True)
    print(f"{tag} rows: {rows_path}", flush=True)

    bad_harness = controls["run"] and controls["detected"] != controls["run"]
    bad = (fails or inaccessible or unbeatable or disagree
           or early["weapon"] != req["weapon"] or early["unlock"] != req["unlock"]
           or not_requested.get("NOT REQUESTED"))
    verdict = "HARNESS BROKEN" if bad_harness else ("FAIL" if bad else "PASS")
    print(f"Done: {shape} {verdict} - {args.seeds} seeds, {len(fails)} failures, "
          f"weapon early {early['weapon']}/{req['weapon']}, "
          f"unlock early {early['unlock']}/{req['unlock']}", flush=True)
    return 2 if bad_harness else (1 if bad else 0)


if __name__ == "__main__":
    sys.exit(main())
