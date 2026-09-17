"""Which objectives need a MOVER to reach, computed from the terrain.

    python tools/reachability.py                 # the Farsite control set
    python tools/reachability.py story19         # one map

THE MODEL, from the designer's rules (2026-09-15):

    Towers can only be built so far apart, so gaps need bridging. Pylons
    provide a longer reach but have a bigger base. Porters can go any distance
    but need a safe space to land. Platforms have a big base and need liftic,
    but can be placed on open space only, which nothing else can be built on.

So "does this objective need a mover" is: is it in the same TOWER-CONNECTED
COMPONENT as where you land. Two land cells are in the same component when a
chain of towers can step between them, each step no longer than the tower
placement range. A gap wider than that splits the map, and crossing it needs a
pylon (longer reach), a porter (any distance, needs a landing spot) or a
platform (open space, and the greenar chain).

WHY COMPONENTS RATHER THAN A FLOOD FILL FROM THE RIFT LAB, which is what the
plan originally said: there IS no rift lab at mission load. Measured - story2
dumps zero player units even after the sim ticks, so the player places it. A
flood fill needs a source and there is none. Components need no source: they
describe the map's own connectivity, and the landing site only decides which
component is "free".

WHAT DECIDES WHETHER THIS IS MEASUREMENT OR A DRESSED-UP GUESS: Founders. Its
five totems are split by rules.OBJECTIVE_INSTANCE_EXTRA - 2, 3 and 4 need
Platform-or-Terp, 1 and 5 do not - and that split is WITHIN one map. An analysis
that merely says "big awkward map, assume a mover" gets the map-level answer
right and still fails this. It has to put 1 and 5 in one component and 2, 3 and
4 outside it.

THE RESULT IS ONE-DIRECTIONAL, AND THIS IS THE MOST IMPORTANT LINE IN THE FILE.
The designer, 2026-09-16: "creep increases as the game plays so some places that
are reachable in the first tic are not later as the enemy advances." Terrain
connectivity is therefore an OPTIMISTIC bound - it describes the map at its
emptiest, before the creeper has taken anything.

So the two answers are not symmetric:

    SPLIT     -> a mover IS needed. Terrain says the gap exists and no amount
                 of play closes it (barring a map that grows its own bridge,
                 which Founders does and no other campaign map does).
    CONNECTED -> a mover MAY STILL BE NEEDED. The route exists on an empty map
                 and the creeper may own it by the time you get there.

A THIRD BLIND SPOT, and Archon is the worked example: ENVIRONMENTAL HAZARDS.
Archon is the only campaign map with creeper raining constantly, so one of its
caches sits inside the starting shield and the other does not. That requirement
is real, and this analysis cannot see any part of it - the map is 100 percent
land in a single connected component. Terrain says "walk anywhere"; the map says
"not without a shield".

Use this to ADD requirements, never to remove them. That is also exactly the
conservative policy chosen for SPAN: require rather than assume, because a
wrong loosening reads green when it should read red.
"""
import io
import os
import sys
from collections import deque

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(REPO, '.aptest', 'maps')

# THE BASELINE, extracted from rules.py. This tool is checked against those
# tables, never the other way round.
#
# TWO KINDS OF REQUIREMENT live in them and only ONE is about reaching. A Terp
# on a Collect objective usually means the cache is BURIED (m16, m18, m19); a
# Chronat usually means DARKNESS (m15, m17). Counting those as reach would be a
# false positive, so they are excluded - this analysis is only asked about
# bridging a gap.
#
# EXPECT_SINGLE IS THE IMPORTANT HALF. Fifteen missions require no bridging at
# all, so every objective on them must fall in ONE component. That is the test
# an over-eager analysis fails: a map it wrongly splits shows up here, instead
# of quietly hardening the logic months later.
#
# TERRAIN IS STATIC, WITH ONE EXCEPTION. The designer, 2026-09-16: "founders is
# odd as it builds a land bridge as the level plays, which is why the TERP
# method is valid", and "no other map changes shape as the level plays (in the
# main campaign)". So a measurement taken at mission start is the whole truth
# for 19 of 20 Farsite maps, and for Founders it is the truth at t=0 - which is
# exactly when the mover question is asked. NOTHING GUARANTEES THE SAME FOR
# SPAN, and a map that grows its own bridge would read as needing a mover here
# and not need one in play.
EXPECT_SINGLE = {
    1: 'Farsite', 2: 'Home', 4: 'Ruins Repurposed', 5: 'We Know Nothing',
    6: 'We Were Never Alone', 7: 'Hints', 8: 'Serious', 9: 'More and More',
    10: 'War and Peace', 12: 'Archon', 13: 'The Experiment',
    14: 'Somewhere in Spacetime',
    15: 'Tower of Darkness', 16: 'The Compound', 18: 'Wallis',
    20: 'Ever After',
}

# Missions whose rules DO require bridging. Where rules.py names instances, the
# expectation is per-totem; otherwise it is only "this map must split".
EXPECT_SPLIT_TOTEMS = {
    11: {'note': 'Shattered: totem 3 needs Porter-or-Platform; 1 and 2 do not',
         'needs': {3: True, 1: False, 2: False}},
    19: {'note': 'Founders: totems 2,3,4 need Platform-or-Terp; 1 and 5 do not '
                 '(and this map grows a land bridge as it plays)',
         'needs': {2: True, 3: True, 4: True, 1: False, 5: False}},
}

EXPECT_SPLIT_ONLY = {
    3: 'Not My Mars: MISSION_SOFT Pylon-or-Porter to cross the gap',
    17: 'Sequence: Reclaim and nullify 12-14 need a mover',
}

# ARCHON MOVED TO EXPECT_SINGLE, and the reason is worth being honest about.
#
# It used to sit here because (12, "Collect") required a Pylon, and this tool
# disagreed - Archon is 100 percent land in one component, so nothing there can
# need a Pylon to REACH it. The disagreement was investigated and the TABLE was
# wrong: the worksheet only ever said a pylon helps with one of the two caches,
# and the designer's corrected spec (2026-09-16) makes the near-centre cache
# Terp-only and the far one weapon-plus-shield-and-factory.
#
# So this tool no longer disagrees with rules.py on any campaign mission. That
# is NOT twenty-six independent confirmations: on this one mission the target
# moved because the tool complained. It is still evidence - the complaint was
# correct and a real logic bug came out of it - but it is a corrected
# disagreement, not a prediction that held.

# BURIED CACHES - a second, separate question. The designer, 2026-09-16: "item
# caches can be burried, which need a terp to get to." That is why a Terp on a
# Collect objective is excluded from the reach baseline above: it means DIG, not
# bridge.
#
# But it may be measurable in its own right. A buried cache sits under terrain,
# so the terrain height AT its cell should separate the buried ones from the
# exposed ones. rules.py already classifies them, so the hypothesis can be
# tested rather than assumed - and if the heights do not separate, that is a
# real answer and the idea gets dropped.
#
# Sequence is the valuable case: its two caches DIFFER (instance 1 is the buried
# right-side one, instance 2 is reachable with a weapon), so it is a within-map
# control of the same kind Founders gives for reach.
KNOWN_BURIED = {
    16: {'all': True, 'note': 'The Compound: (16, Collect) needs Terp'},
    18: {'all': True, 'note': 'Wallis: (18, Collect) needs Terp'},
    19: {'all': True, 'note': 'Founders: (19, Collect) needs Terp + Chronat'},
    17: {'per': {1: True, 2: False},
         'note': 'Sequence: cache 1 buried, cache 2 weapon-only'},
    1: {'all': False, 'note': 'Farsite: caches are free'},
    2: {'all': False, 'note': 'Home: cache is free'},
    5: {'all': False, 'note': 'We Know Nothing: cache is free'},
    13: {'all': False, 'note': 'The Experiment: cache is free'},
}

def load(path):
    text = io.open(path, encoding='utf-8', errors='replace').read().split('\n')
    head = {}
    units = []
    grids = {}
    i = 0
    while i < len(text):
        line = text[i]
        if line in ('terrain', 'platform', 'legal'):
            h = int(head['cells'].split('x')[1])
            grids[line] = text[i + 1:i + 1 + h]
            i += 1 + h
            continue
        if line.startswith('unit '):
            parts = line.split()
            cell = parts[2].split(',')
            units.append({'name': parts[1], 'x': int(cell[0]), 'y': int(cell[1]),
                          'mine': 'mine=True' in line,
                          'nullifiable': 'nullifiable=True' in line,
                          'worldY': (float(line.split('worldY=')[1].split()[0])
                                     if 'worldY=' in line else None),
                          'podWare': (int(line.split('podWare=')[1].split()[0])
                                      if 'podWare=' in line
                                      and line.split('podWare=')[1].split()[0] != '-'
                                      else None)})
        elif '=' in line and line:
            k, v = line.split('=', 1)
            head[k] = v
        i += 1
    return head, units, grids


def terrain_height(ter, x, y):
    """Column height at a cell. Encoded 0-9 then A-K so 0..20 all survive."""
    if not (0 <= y < len(ter)) or x >= len(ter[y]):
        return -1
    c = ter[y][x]
    if c.isdigit():
        return int(c)
    if 'A' <= c <= 'K':
        return 10 + ord(c) - ord('A')
    return -1


def components(land, w, h, reach):
    """Label land cells; two are joined when within `reach` cells of each other.

    Chebyshev distance, i.e. a square neighbourhood - a tower's placement range
    in game is a radius, so this is slightly GENEROUS at the diagonals. Erring
    generous is the safe direction here: it merges components that a stricter
    metric would split, so it under-reports mover requirements rather than
    inventing them, and an under-report shows up as a control failure instead of
    silently hardening the logic.
    """
    comp = [[-1] * w for _ in range(h)]
    sizes = []
    for sy in range(h):
        for sx in range(w):
            if not land[sy][sx] or comp[sy][sx] >= 0:
                continue
            cid = len(sizes)
            n = 0
            q = deque([(sx, sy)])
            comp[sy][sx] = cid
            while q:
                x, y = q.popleft()
                n += 1
                for ny in range(max(0, y - reach), min(h, y + reach + 1)):
                    row = land[ny]
                    crow = comp[ny]
                    dy2 = (ny - y) * (ny - y)
                    for nx in range(max(0, x - reach), min(w, x + reach + 1)):
                        if not row[nx] or crow[nx] >= 0:
                            continue
                        # EUCLIDEAN, not Chebyshev. Chebyshev lets a diagonal
                        # step reach reach*sqrt(2) - about 15.6 cells at range
                        # 11 - which merged components that are really apart. It
                        # cost two control failures (m3, m12) in the direction
                        # that matters: under-reporting a mover requirement is
                        # what produces a false green.
                        if (nx - x) * (nx - x) + dy2 > reach * reach:
                            continue
                        crow[nx] = cid
                        q.append((nx, ny))
            sizes.append(n)
    return comp, sizes


def nearest_component(comp, land, w, h, x, y, limit=14):
    """The component an objective belongs to.

    Objectives sit ON terrain, but a totem or an emitter occupies its cell, and
    a cell can read as void at the exact centre. So search outward a little
    rather than demanding the centre cell be land - and report how far it had to
    look, because a large radius means the answer is soft.
    """
    if 0 <= x < w and 0 <= y < h and land[y][x]:
        return comp[y][x], 0
    for r in range(1, limit + 1):
        for ny in range(max(0, y - r), min(h, y + r + 1)):
            for nx in range(max(0, x - r), min(w, x + r + 1)):
                if max(abs(nx - x), abs(ny - y)) != r:
                    continue
                if land[ny][nx]:
                    return comp[ny][nx], r
    return -1, limit + 1


def analyse(mission):
    path = os.path.join(MAPS, mission + '.map')
    if not os.path.exists(path):
        print('  no dump for %s' % mission)
        return None
    head, units, grids = load(path)
    w, h = [int(v) for v in head['cells'].split('x')]
    reach = int(head.get('towerPlacementRange', '11'))
    ter = grids['terrain']
    land = [[c not in '0?' for c in row.ljust(w)[:w]] for row in ter]

    comp, sizes = components(land, w, h, reach)
    order = sorted(range(len(sizes)), key=lambda i: -sizes[i])
    big = order[:6]

    print('== %s == %dx%d reach=%d land=%d comps=%d'
          % (mission, w, h, reach, sum(sizes), len(sizes)))
    print('   largest: %s' % ', '.join('#%d=%d' % (c, sizes[c]) for c in big))

    tot, cache = objectives(units)
    rows = []
    for i, u in enumerate(tot, 1):
        cid, dist = nearest_component(comp, land, w, h, u['x'], u['y'])
        rows.append((i, u['x'], u['y'], cid, sizes[cid] if cid >= 0 else 0, dist))
    cachecomps = []
    cacheheights = []
    for u in cache:
        cid, _d = nearest_component(comp, land, w, h, u['x'], u['y'])
        cachecomps.append(cid)
        # BURIED = the cache sits BELOW the terrain column above it.
        #
        # The first version compared the column HEIGHT against a classification
        # and did not separate - buried caches sat at heights 8-9, exposed ones
        # spanned 1-9. Two things were wrong: height is not the question (a tall
        # column with the cache on top is exposed), and the dump clamped every
        # height above 9, so a cache at elevation 15 under a height-15 column
        # looked like it sat 6 units ABOVE a height-9 one.
        #
        # Elevation minus column height separates cleanly: exposed caches read
        # exactly 0, buried ones -5 to -9, on all ten classified campaign caches
        # including Sequence's within-map pair.
        ch = terrain_height(ter, u['x'], u['y'])
        cacheheights.append((u['x'], u['y'], u.get('worldY'), ch))
    return {'mission': mission, 'totems': rows, 'cachecomps': cachecomps,
            'cacheheights': cacheheights, 'sizes': sizes, 'comps': len(sizes)}


def nullify_control():
    """Do nullify targets land where rules.py says a mover is needed?

    The component analysis had only ever been run on TOTEMS, because the map
    dump did not say which units were nullifiable. It does now (CAN_NULLIFY),
    and nullify targets are the larger class - 163 of SPAN's objectives against
    95 totems - so this is the bigger half of the question.

    Two campaign missions require a mover for EVERY nullify target:
        m11 Shattered  (11, "Nullify") = [["Porter", "Platform"]]
        m19 Founders   (19, "Nullify") = [["Platform", "Terp"]]
    On both, the free component is the one holding the totems that need no
    mover. So the test is: no nullify target may sit in that component.
    """
    import re
    cases = {
        11: {'free_totems': [1, 2], 'note': 'Shattered: all nullify need Porter-or-Platform'},
        19: {'free_totems': [1, 5], 'note': 'Founders: all nullify need Platform-or-Terp'},
    }
    passed = failed = 0
    print('== nullify targets vs the free component ==')
    for n, case in sorted(cases.items()):
        spec = 'story%d' % n
        path = os.path.join(MAPS, spec + '.map')
        if not os.path.exists(path):
            print('  %s no dump' % spec)
            continue
        head, units, grids = load(path)
        w, h = [int(v) for v in head['cells'].split('x')]
        reach = int(head.get('towerPlacementRange', '11'))
        ter = grids['terrain']
        land = [[c not in '0?' for c in row.ljust(w)[:w]] for row in ter]
        comp, sizes = components(land, w, h, reach)

        tot = sorted([u for u in units if u['name'] == 'Totem'],
                     key=lambda u: (u['y'], u['x']))
        free = set()
        for i, u in enumerate(tot, 1):
            if i in case['free_totems']:
                cid, _d = nearest_component(comp, land, w, h, u['x'], u['y'])
                free.add(cid)

        nul = [u for u in units if u.get('nullifiable')]
        inside = 0
        for u in nul:
            cid, _d = nearest_component(comp, land, w, h, u['x'], u['y'])
            if cid in free:
                inside += 1
        ok = (inside == 0) and len(nul) > 0
        passed += ok
        failed += (not ok)
        print('  m%-3d %s' % (n, case['note']))
        print('       %s %d nullify target(s), free component(s)=%s, %d inside'
              % ('PASS' if ok else 'FAIL', len(nul), sorted(free), inside))
    return passed, failed


def objectives(units):
    """Totems and caches, in the (cellY, cellX) order the mod numbers by.

    Only these two kinds: they are unambiguously identifiable by name. Nullify
    targets would need a nullifiable flag the dump does not carry, and guessing
    which unit types count would put an assumption inside the control.
    """
    tot = sorted([u for u in units if u['name'] == 'Totem'],
                 key=lambda u: (u['y'], u['x']))
    cache = sorted([u for u in units if u['name'] == 'InfoCache'],
                   key=lambda u: (u['y'], u['x']))
    return tot, cache


SPAN_GUIDS = [
    'knucracker1', 'knucracker2', 'knucracker3', 'knucracker4', 'knucracker5',
    'knucracker6', 'knucracker7', 'knucracker8', 'knucracker9', 'knucracker10',
    'knucracker11', 'knucracker12', 'knucracker13', 'knucracker14',
    'knucracker15', 'knucracker16', 'knucracker17', 'knucracker18',
    'knucracker19', 'knucracker20', 'knucrackerbonus0', 'knucrackerbonus1',
    'demobonus', 'demobonus2', 'demobonus3', 'demobonus4',
]


def span_report():
    """Run the validated analysis over SPAN and report what it finds.

    ADDITIVE ONLY. The campaign control passes 25 of 26, and the one failure
    (Archon) is a map that is 100 percent land - so a mover requirement that is
    not about a gap is invisible to this. Combined with creep advance, a map
    reported as one component may STILL need a mover. A split is evidence; a
    non-split is not evidence of absence.
    """
    print('== SPAN: component structure ==')
    print('%-18s %-10s %-6s %-8s %s' % ('map', 'size', 'land%', 'comps', 'totem components'))
    flagged = []
    for g in SPAN_GUIDS:
        r = analyse_quiet(g)
        if r is None:
            print('  %-18s no dump' % g)
            continue
        comps = sorted({c for _i, _x, _y, c, _s, _d in r['totems'] if c >= 0})
        if len(comps) > 1:
            flagged.append((g, comps, r))
        print('%-18s %-10s %-6s %-8s %s'
              % (g, r['dims'], r['landpct'], r['comps'], comps))
    print('')
    print('== maps whose totems are SPLIT across components (a mover IS needed) ==')
    if not flagged:
        print('  none')
    for g, comps, r in flagged:
        print('  %s: totems in components %s' % (g, comps))
        for i, x, y, cid, size, _d in r['totems']:
            print('     totem %-2d (%3d,%3d) comp=%-3s compsize=%d' % (i, x, y, cid, size))
    return 0


def analyse_quiet(mission):
    import io as _io
    path = os.path.join(MAPS, mission + '.map')
    if not os.path.exists(path):
        return None
    head, units, grids = load(path)
    w, h = [int(v) for v in head['cells'].split('x')]
    reach = int(head.get('towerPlacementRange', '11'))
    ter = grids['terrain']
    land = [[c not in '0?' for c in row.ljust(w)[:w]] for row in ter]
    comp, sizes = components(land, w, h, reach)
    tot, cache = objectives(units)
    rows = []
    for i, u in enumerate(tot, 1):
        cid, dist = nearest_component(comp, land, w, h, u['x'], u['y'])
        rows.append((i, u['x'], u['y'], cid, sizes[cid] if cid >= 0 else 0, dist))
    total = sum(sizes)
    return {'mission': mission, 'totems': rows, 'sizes': sizes,
            'comps': len(sizes), 'dims': '%dx%d' % (w, h),
            'landpct': '%d%%' % (100 * total // (w * h))}


def main():
    if len(sys.argv) > 1 and sys.argv[1] == '--span':
        return span_report()
    wanted = sys.argv[1:]
    missions = [int(a) for a in wanted] if wanted else sorted(
        list(EXPECT_SINGLE) + list(EXPECT_SPLIT_TOTEMS) + list(EXPECT_SPLIT_ONLY))

    passed = failed = missing = 0
    cached = {}
    for n in missions:
        spec = 'story%d' % n
        r = analyse(spec)
        if r is None:
            missing += 1
            continue
        cached[spec] = r
        comps_used = sorted({c for _i, _x, _y, c, _s, _d in r['totems'] if c >= 0}
                            | {c for c in r['cachecomps'] if c >= 0})

        if n in EXPECT_SINGLE:
            ok = len(comps_used) <= 1
            passed += ok
            failed += (not ok)
            print('  m%-3d %-22s %s expected ONE component, objectives in %s'
                  % (n, EXPECT_SINGLE[n], 'PASS' if ok else 'FAIL', comps_used))
        elif n in EXPECT_SPLIT_TOTEMS:
            known = EXPECT_SPLIT_TOTEMS[n]
            free = {c for i, _x, _y, c, _s, _d in r['totems']
                    if known['needs'].get(i) is False}
            print('  m%-3d %s' % (n, known['note']))
            for i, x, y, cid, size, _d in r['totems']:
                expect = known['needs'].get(i)
                if expect is None:
                    continue
                got = cid not in free
                ok = (got == expect)
                passed += ok
                failed += (not ok)
                print('       totem %d (%3d,%3d) comp=%-3s %s expected=%-5s got=%s'
                      % (i, x, y, cid, 'PASS' if ok else 'FAIL',
                         'mover' if expect else 'free', 'mover' if got else 'free'))
        else:
            ok = len(comps_used) > 1
            passed += ok
            failed += (not ok)
            print('  m%-3d %s %s objectives in %s'
                  % (n, EXPECT_SPLIT_ONLY[n], 'PASS' if ok else 'FAIL', comps_used))

    np_, nf_ = nullify_control()
    passed += np_
    failed += nf_

    print('')
    print('== buried caches: does terrain height at the cache cell separate them? ==')
    buried_h, exposed_h = [], []
    for n in missions:
        known = KNOWN_BURIED.get(n)
        if not known:
            continue
        r = cached.get('story%d' % n)
        if not r:
            continue
        for i, (x, y, wy, ch) in enumerate(r['cacheheights'], 1):
            if 'per' in known:
                exp = known['per'].get(i)
            else:
                exp = known['all']
            if exp is None:
                continue
            delta = None if (wy is None or ch < 0) else wy - ch
            (buried_h if exp else exposed_h).append((n, i, x, y, delta))
            print('  m%-3d cache %d (%3d,%3d) worldY=%s terrain=%s delta=%s  classified=%s'
                  % (n, i, x, y, wy, ch, delta, 'BURIED' if exp else 'exposed'))
    if buried_h and exposed_h:
        bset = sorted(d for _n, _i, _x, _y, d in buried_h if d is not None)
        eset = sorted(d for _n, _i, _x, _y, d in exposed_h if d is not None)
        sep = bool(bset) and bool(eset) and max(bset) < min(eset)
        print('  buried deltas=%s  exposed deltas=%s  -> %s'
              % (bset, eset, 'SEPARATES' if sep else 'DOES NOT separate'))
    else:
        print('  not enough classified caches in this run to test')

    print('')
    print('Done: %d passed, %d failed, %d map dump(s) missing' % (passed, failed, missing))
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
