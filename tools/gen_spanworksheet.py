"""The community reporting form for the SPAN Experiments, and its reference sheet.

    python tools/gen_spanworksheet.py

Emits TWO files, both generated so neither can drift from span_data.py:

    .github/ISSUE_TEMPLATE/span-map-report.yml   the form a player fills in
    docs/design/span-requirements-worksheet.md   what is already known

WHY IT WAS REWRITTEN (2026-09-17). The previous sheet was 742 lines and asked a
stranger to copy a 23-line markdown block into a free-form issue. Measured
against itself it was mostly repetition: 601 of those lines were per-map blocks
containing 80 distinct non-blank lines, and two maps' blocks were character
identical. Specifically:

  - 141 lines of preamble before the first map, 40 of them a CW4DevTools cheat
    table. The sheet said "for people who PLAY these maps" and then taught them
    instant build, all buildings, infinite resources and indestructible - which
    make the question it asks, "what is the MINIMUM to finish", unanswerable.
  - The same Archon sentence on 23 of 26 blocks, 600 lines from the paragraph
    that explains it, naming a campaign mission a SPAN player need never have
    seen.
  - "Blockers" and "Min units to complete" asked the same question. The proof
    was next door: in the hand-filled campaign worksheet the designer filled in
    Notes 20 times out of 20 and Blockers 0 times out of 20. The SPAN sheet led
    with the field that scored zero.
  - It asked 26 times for corrections to measurements that have nowhere to
    land. Five of the six SPAN tables - INSTANCE_COUNTS, OBJECTIVE_SLOTS,
    TOTEMS_NEED_FACTORY, NEEDS_MOVER, NOTES - are emitted by gen_spandata.py
    and overwritten on its next run. Only roster.SPAN_STARTER_ELIGIBLE is
    hand-written.

So the form asks the four things a human can answer and the code can act on, and
the sheet is a reference table rather than 26 near-identical questionnaires.

Reads .aptest/maps and .aptest/totems.txt for map shape and totem wares. When
those dumps are absent the affected columns say "not measured" and the rest is
still correct.
"""
import io
import os
import runpy
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(REPO, '.aptest', 'maps')
TOTEMS = os.path.join(REPO, '.aptest', 'totems.txt')
SHEET = os.path.join(REPO, 'docs', 'design', 'span-requirements-worksheet.md')
FORM = os.path.join(REPO, '.github', 'ISSUE_TEMPLATE', 'span-map-report.yml')

span = runpy.run_path(os.path.join(REPO, 'apworld', 'cw4', 'span_data.py'))
MISSIONS = span['SPAN_MISSIONS']
COUNTS = span['SPAN_INSTANCE_COUNTS']
SLOTS = span['SPAN_OBJECTIVE_SLOTS']
FACTORY = span['SPAN_TOTEMS_NEED_FACTORY']
MOVER = span['SPAN_NEEDS_MOVER']
NOTES = span['SPAN_NOTES']

SLOT_NAMES = {0: 'Nullify', 1: 'Totems', 2: 'Reclaim', 3: 'Hold', 4: 'Collect', 5: 'Custom'}
WARE = {29: 'Arg', 30: 'Liftic'}

ISSUES = 'https://github.com/drohack/cw4-archipelago/issues'


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


def objectives(n):
    """One cell: what the map has, with counts. The count implies the slot, so
    this is one column rather than the two lines it used to be."""
    caches, totems, nullify = COUNTS[n]
    bits = []
    if nullify:
        bits.append('%d nullify' % nullify)
    if totems:
        bits.append('%d totems' % totems)
    if caches:
        bits.append('%d cache%s' % (caches, '' if caches == 1 else 's'))
    for s in SLOTS[n]:
        if s in (2, 3, 5):          # Reclaim, Hold, Custom - uncounted
            bits.append(SLOT_NAMES[s])
    return ', '.join(bits) or 'none'


def totem_cell(n):
    caches, totems, nullify = COUNTS[n]
    if not totems:
        return '-'
    guid = MISSIONS[n][0]
    kinds = sorted({WARE.get(w, 'ware%d' % w) for w in WARES.get(guid, [])})
    ware = '/'.join(kinds) if kinds else '?'
    return '%s, Factory %s' % (ware, 'REQUIRED' if FACTORY[n] else 'not needed')


def shape_cell(n):
    shape = map_shape(MISSIONS[n][0])
    if not shape:
        return 'not measured'
    w, h, comps = shape
    return '%dx%d, %d part%s' % (w, h, comps, '' if comps == 1 else 's')


SHEET_HEAD = """# SPAN Experiments: requirements worksheet

GENERATED by `tools/gen_spanworksheet.py` from `apworld/cw4/span_data.py`.
Do not hand-edit - re-run the generator.

**This sheet is for people who PLAY these maps.** The randomizer can already mix
the 26 SPAN Experiments into a seed (the `span_missions` yaml toggle, off by
default), on requirements DERIVED from the map files rather than observed in
play. Derived logic is a floor, not the truth. Filling in a block is how a guess
becomes a fact.

**To send answers**, either
[open a report](%s/new?template=span-map-report.yml) - same four questions, no
copying - or paste a filled-in block into
[a new issue](%s/new). Do not edit this file: the next generator run rewrites it
and anything typed here is lost.

## Before you start

Each block opens with what was MEASURED from the map file. Those numbers are
reliable; three whole classes of requirement are invisible to them, and the
campaign proves each one is real:

- **Creep.** Every measurement is taken on an empty map at tick zero. A route
  open then can be gone by the time you need it.
- **Reach that is not about terrain.** One campaign map is a single unbroken
  landmass and still needs a Pylon to reach its second cache.
- **Hazards.** A map that rains creeper needs Shields for reasons its shape
  never mentions.

So **the mover verdict is the weakest line in every block.** A **mover** is a
Pylon, Porter, Platform or Terp - anything that reaches where towers cannot. A
map can be in several pieces and still need none, if everything you must reach
sits on one of them.

The logic over-requires rather than under-requires, so a wrong guess makes a
seed harder than it needs to be rather than impossible.

## The four questions

- **Needed to finish** - the smallest set you could have won with. Assume a rift
  lab and towers are always available; they are in the randomizer too. Terse is
  fine: `cannon only`, `needed a terp for the ridge`.
- **Needed for one objective only** - a unit only one totem or one cache
  demanded, where you could have skipped that objective and still won.
- **Surprising** - did it rain or damage your units on its own? Did the map
  change shape as it played? Could you take any objective holding NOTHING at
  all? Did a measured line look wrong?
- **Helped but not needed** - feeds the difficulty tiers rather than core logic.

**Leave a field blank rather than guessing - blank is information, a wrong guess
is not.** Known-good defaults, so you only note deviations: offense is cannon or
mortar; nullify objectives need the Nullifier; reclaim needs it too; the
Refinery and Factory are ONE item, so "needs the factory" covers the whole
greenar chain.

---
""" % (ISSUES, ISSUES)


def measured(n):
    """The whole measured picture on one line, instead of six bullets."""
    mover = 'MOVER REQUIRED' if n in MOVER else 'no mover needed'
    bits = [objectives(n)]
    tot = totem_cell(n)
    if tot != '-':
        bits.append('totems want ' + tot)
    bits.append(mover)
    bits.append(shape_cell(n))
    line = ' | '.join(bits)
    if n in NOTES:
        line += '\n\n  Note: ' + NOTES[n]
    return line


def sheet():
    out = [SHEET_HEAD.rstrip(), '']
    for n in sorted(MISSIONS):
        guid, title = MISSIONS[n]
        out += ['## %s - `%s`' % (title, guid),
                '',
                'Measured: %s' % measured(n),
                '',
                '- Needed to finish:',
                '- Needed for one objective only:',
                '- Surprising:',
                '- Helped but not needed:',
                '']
    out += ['---', '',
            'Send answers: <%s/new?template=span-map-report.yml>' % ISSUES, '']
    return '\n'.join(out)


FORM_HEAD = """# GENERATED by tools/gen_spanworksheet.py from apworld/cw4/span_data.py.
# Do not hand-edit - re-run the generator.
#
# The map list is generated so it cannot drift from the randomizer's own data.
name: SPAN map report
description: Tell us what a SPAN Experiment map actually needed to finish
title: "[SPAN] "
labels: ["span-requirements"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for playing one of these. The randomizer guesses what each SPAN
        map needs by reading the map file, which cannot see creeper, hazards, or
        anything that changes as the mission runs - so what you actually needed
        is worth more than everything measured so far.

        Answer what you know and leave the rest blank. **A blank is information;
        a guess is not.**

        One question worth singling out: **could you have taken any objective
        holding no items at all**, with just a rift lab and towers? That is what
        lets a seed start, and no measurement can answer it. Only one SPAN map
        (Far York Farm) has a collectable cache, so on the others the answer is
        almost certainly no - but if you managed it, that is the single most
        useful thing you can tell us. Put it under Anything surprising.

        What is already measured for each map is in
        [the reference table](../blob/main/docs/design/span-requirements-worksheet.md).
  - type: dropdown
    id: map
    attributes:
      label: Which map?
      options:
"""

FORM_TAIL = """    validations:
      required: true
  - type: textarea
    id: needed
    attributes:
      label: What did you need to FINISH it?
      description: >-
        The smallest set you could have won with. Assume a rift lab and towers
        are always available - they are in the randomizer too. Terse is fine:
        "cannon only", "needed a terp for the ridge", "porter mandatory".
      placeholder: cannon, nullifier, terp
    validations:
      required: true
  - type: textarea
    id: objective_only
    attributes:
      label: Anything you needed for ONE objective but not to finish?
      description: >-
        A unit that only one totem or one cache demanded, where you could have
        walked away from that objective and still won.
      placeholder: needed a pylon to reach the far totem, nothing else used it
  - type: textarea
    id: surprises
    attributes:
      label: Anything surprising?
      description: >-
        Did it rain creeper or damage your units on its own? Did the map change
        shape as it played - a route opening or closing? Could you take any
        objective holding nothing at all? Did anything in the reference table
        look wrong? All of that is worth more than a tidy answer.
      placeholder: the whole south side floods by ten minutes, so the cache there is gone
  - type: input
    id: difficulty
    attributes:
      label: Anything that just HELPED, without being needed?
      description: Feeds the difficulty tiers rather than the core logic.
      placeholder: snipers made it much calmer but I finished without them
"""


def form():
    out = [FORM_HEAD.rstrip('\n')]
    for n in sorted(MISSIONS):
        guid, title = MISSIONS[n]
        out.append('        - "%s (%s)"' % (title, guid))
    out.append(FORM_TAIL.rstrip('\n'))
    return '\n'.join(out) + '\n'


def write(path, text):
    bad = sorted({ord(c) for c in text if ord(c) > 126})
    if bad:
        sys.exit('non-ascii in %s: %r' % (os.path.basename(path), bad[:6]))
    parent = os.path.dirname(path)
    if not os.path.isdir(parent):
        os.makedirs(parent)
    io.open(path, 'w', encoding='utf-8', newline='\n').write(text)
    print('wrote %s (%d lines)' % (os.path.relpath(path, REPO), text.count('\n')),
          flush=True)


write(SHEET, sheet())
write(FORM, form())
