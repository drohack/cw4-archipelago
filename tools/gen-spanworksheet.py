"""The community worksheet for the SPAN Experiments.

    python tools/gen-spanworksheet.py   # -> docs/design/span-requirements-worksheet.md

GENERATED, and pre-filled from the measurements, so a player is CONFIRMING
rather than starting from a blank page. Every pre-filled line says where it came
from; every blank line says why only play can settle it.

Reads the same sources as gen-spanreqs.py (.aptest/maps, .aptest/totems.txt) for
the map shape, and apworld/cw4/span_data.py for what the randomizer actually
believes. When the dumps are absent the shape lines are simply omitted - the
sheet is still complete and still correct.
"""
import io
import os
import re
import runpy
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(REPO, '.aptest', 'maps')
TOTEMS = os.path.join(REPO, '.aptest', 'totems.txt')
DST = os.path.join(REPO, 'docs', 'design', 'span-requirements-worksheet.md')
SETUP_SRC = os.path.join(REPO, 'docs', 'design', 'mission-requirements-worksheet.md')

span = runpy.run_path(os.path.join(REPO, 'apworld', 'cw4', 'span_data.py'))
MISSIONS = span['SPAN_MISSIONS']
COUNTS = span['SPAN_INSTANCE_COUNTS']
SLOTS = span['SPAN_OBJECTIVE_SLOTS']
FACTORY = span['SPAN_TOTEMS_NEED_FACTORY']
MOVER = span['SPAN_NEEDS_MOVER']
NOTES = span['SPAN_NOTES']

SLOT_NAMES = {0: 'Nullify', 1: 'Totems', 2: 'Reclaim', 3: 'Hold', 4: 'Collect', 5: 'Custom'}
WARE = {29: 'Arg', 30: 'Liftic'}


def setup_section():
    """The CW4DevTools table, lifted from the campaign worksheet rather than
    rewritten - it is the same plugin and the same keys, and two copies that
    drift is worse than one copy quoted."""
    if not os.path.exists(SETUP_SRC):
        return ['(setup section unavailable: mission-requirements-worksheet.md not found)']
    text = io.open(SETUP_SRC, encoding='utf-8').read()
    start = text.find('## Setup: CW4 Dev Tools')
    end = text.find('**How to use:**', start)
    if start < 0 or end < 0:
        return ['(setup section unavailable: markers not found)']
    return text[start:end].rstrip().split('\n')


def totem_wares():
    out = {}
    if not os.path.exists(TOTEMS):
        return out
    for line in io.open(TOTEMS, encoding='utf-8', errors='replace'):
        m = re.search(r'DEVTOTEM mission=(\S+) cell=(\d+),(\d+) authored=w(\d+)x', line)
        if m:
            out.setdefault(m.group(1), []).append(int(m.group(4)))
    return out


def map_shape(guid):
    """(width, height, components) or None when the dumps are not present."""
    path = os.path.join(MAPS, guid + '.map')
    if not os.path.exists(path):
        return None
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from reachability import load, components  # noqa: E402
    head, _units, grids = load(path)
    w, h = [int(v) for v in head['cells'].split('x')]
    reach = int(head.get('towerPlacementRange', '11'))
    ter = grids['terrain']
    land = [[c not in '0?' for c in row.ljust(w)[:w]] for row in ter]
    _comp, sizes = components(land, w, h, reach)
    return w, h, len(sizes)


WARES = totem_wares()

HEAD = """# SPAN Experiments: requirements worksheet

**This sheet is for people who PLAY these maps.** The randomizer can already mix
the 26 SPAN Experiments into a seed (the `span_missions` yaml toggle), and it
does so on requirements that were DERIVED rather than played. Derived logic is a
floor, not the truth. Filling in a block below is how a guess becomes a fact.

## What is already known, and how

Every campaign requirement in this randomizer came from somebody finishing the
mission and writing down what they needed. Nothing like that exists for SPAN, so
three detectors were built and each was VALIDATED AGAINST THE CAMPAIGN before
being pointed at a SPAN map - 28 assertions, 0 failures:

| what | how it is measured | how it was validated |
|---|---|---|
| does an objective need a mover | tower-connected components over land, at `Tower.PLACEMENT_RANGE` = 11 | 26 campaign totem assertions, plus Shattered's 4 and Founders' 17 nullify targets, all correctly outside the free component |
| is a cache buried | `worldY` below the terrain column at that cell | 10 of 10 classified campaign caches, including Sequence's pair where one is buried and one is not |
| what a totem wants | the ware the totem is authored for, named by the game's own `DeliveryPadControls.GetWareName`; free only if a Pod on the map carries it | the campaign's 17-of-17 Factory verdict reproduced exactly |

That is why each block below arrives with its objective counts, its totem ware
and its Factory verdict already filled in. **Those lines are measurements. If one
disagrees with what you saw in play, that is a finding worth reporting - say so.**

## What NO measurement can see, which is why this sheet exists

Three whole classes of requirement are invisible to every detector above, and
the campaign proves each class is real:

1. **Creep advance.** Every measurement is taken on an empty map at tick zero. A
   route that exists then can be gone by the time you need it. Two campaign
   missions (More and More, Tower of Darkness) need a weapon purely because
   creep covers a cache that is otherwise free.
2. **Reach that is not about terrain.** Archon is 100 percent land in one
   component, and still needs a Pylon to get to its second cache. A terrain
   analysis says that map needs nothing.
3. **Environmental hazards.** Archon again: it rains creeper constantly, so one
   cache sits inside the starting shield and the other does not, and the mission
   needs Shields and a Factory for reasons the map's shape never mentions.

So the randomizer's SPAN logic is deliberately conservative - it over-requires
rather than under-requires - and the failure mode is a seed that is HARDER than
it needs to be, not one that cannot be finished. Your answers make it accurate
instead of merely safe.

## How to fill this in

Play the map and answer the questions in its block. Terse is fine: `cannon
only`, `needed terp for the ridge`, `porter mandatory`. **Leave a field blank
rather than guessing - blank is information, a wrong guess is not.**

The goal is the MINIMUM to finish, not what is comfortable. Where something only
made it easier, put it under "Helped but not needed": that feeds the difficulty
tiers rather than core logic.

Known-good defaults, so you only note deviations:

- Offense is cannon or mortar. If cannon alone sufficed, just write `cannon`.
- Nullify objectives need the Nullifier.
- Reclaim needs the Nullifier too - it is "clear the map", and nothing is clear
  while an emitter is still producing.
- The randomizer merges Refinery and Factory into ONE item, so "needs the
  factory" covers the whole refine-and-convert chain.

Send a filled-in block (or the whole file) to
<https://github.com/drohack/cw4-archipelago/issues>.

"""

LEGEND = """
---

## Legend

- **Min units to complete** - the smallest set you could have won with. Assume
  rift lab and tower are always available; they are, in the randomizer too.
- **Openable with nothing?** - could you take ANY objective on this map holding
  no items at all, with just a rift lab and towers? This is the single most
  valuable answer on the sheet: a map that can open a seed is what lets the
  generator start, and no measurement can answer it because creep coverage is
  invisible to all of them. Exactly one SPAN map even has a cache.
- **Does it rain, or any other hazard?** - anything on the map that damages or
  disables your units on its own, without an enemy doing it. Archon is the
  worked example and the reason this question exists.
- **Does the map change shape as it plays?** - Founders grows a land bridge to
  the starting island as the mission runs, which is why a Terp substitutes for a
  Platform there. No other campaign map does this and NOTHING is known about
  SPAN. If a route appeared or closed while you played, say so.
- **Required for an OBJECTIVE only** - needed for one objective but not to
  finish the mission.
- **Helped but not needed** - difficulty-tier material.
- **Blockers** - anything that made the map unwinnable, or nearly so, without a
  specific unit.

---
"""


def block(n):
    guid, title = MISSIONS[n]
    caches, totems, nullify = COUNTS[n]
    lines = ['## %s - `%s`' % (title, guid), '']

    shape = map_shape(guid)
    measured = []
    if shape:
        w, h, comps = shape
        measured.append('- Map: %dx%d, %d tower-connected land component(s)' % (w, h, comps))
    objectives = ', '.join(SLOT_NAMES[s] for s in SLOTS[n])
    measured.append('- Objectives the map enables: %s' % (objectives or 'none'))
    measured.append('- Counted objectives: %d nullify target(s), %d totem(s), %d cache(s)'
                    % (nullify, totems, caches))

    wares = WARES.get(guid, [])
    if totems:
        if wares:
            kinds = sorted({WARE.get(w, 'ware%d' % w) for w in wares})
            measured.append('- Totems want: %s' % ', '.join(kinds))
        measured.append('- **Factory required for totems: %s** %s'
                        % ('YES' if FACTORY[n] else 'no',
                           '(no Pod on the map carries that ware)' if FACTORY[n]
                           else '(a Pod on the map carries it, so it is free)'))
    if n in MOVER:
        measured.append('- **A mover is required**: the objectives do not all share one '
                        'tower-connected component, so some of them cannot be reached by '
                        'towers alone. WHICH ones depends on where you land, which is why '
                        'the randomizer requires it for the whole map.')
    else:
        measured.append('- No mover found necessary from the terrain alone. '
                        '**This is the weakest line in the block** - it cannot see creep, '
                        'and Archon needs a Pylon on a map this test calls clear.')
    if n in NOTES:
        measured.append('- Note: %s' % NOTES[n])

    lines += ['### Measured', ''] + measured + ['', '### To fill in', '']
    lines += [
        '- Openable with nothing (rift lab + towers only)?',
        '- Min units to complete:',
        '- Required for an OBJECTIVE only:',
        '- Does it rain, or any other hazard?',
        '- Does the map change shape as it plays?',
        '- Helped but not needed:',
        '- Blockers:',
        '- Anything the measurements above got wrong:',
        '- Notes:',
        '',
    ]
    return lines


out = [HEAD.rstrip(), '']
out += setup_section()
out += [LEGEND.rstrip(), '']
for n in sorted(MISSIONS):
    out += block(n)

text = '\n'.join(out).rstrip() + '\n'
bad = sorted({ord(c) for c in text if ord(c) > 126})
if bad:
    sys.exit('non-ascii in the worksheet: %r' % bad[:6])

with io.open(DST, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write(text)
print('wrote %s: %d maps, %d lines' % (os.path.relpath(DST, REPO), len(MISSIONS),
                                       text.count('\n')))
