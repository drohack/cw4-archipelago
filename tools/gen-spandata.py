"""Generate apworld/cw4/span_data.py from the SPAN survey measurements.

    bash tools/span-survey.sh          # objective slots
    bash tools/map-dump.sh  (SPAN=1)   # unit cells, wares, terrain
    python tools/gen-spandata.py

GENERATED OUTPUT - do not hand-edit span_data.py; re-run this.

WHY GENERATED. Twenty-six missions times four tables is a hundred-odd numbers,
every one of which was measured. Typing them by hand would break the chain from
measurement to logic at exactly the point where a single transposed digit gates
the wrong check, and would make the tables impossible to re-derive when a map is
re-surveyed.

ORDER IS FROZEN. Mission numbers 21..46 are assigned in the SPAN_ORDER below and
that order must never change: location ids are positional, so reordering this
list silently renumbers every SPAN location and breaks seeds in flight. New maps
would have to be appended, never inserted - the same rule the Farsite tables
follow.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(REPO, '.aptest', 'maps')
SURVEY = os.path.join(REPO, '.aptest', 'span-survey.txt')
TOTEMS = os.path.join(REPO, '.aptest', 'totems.txt')
DST = os.path.join(REPO, 'apworld', 'cw4', 'span_data.py')

# FROZEN. See the module docstring - reordering renumbers every SPAN location.
SPAN_ORDER = [
    ('knucracker1', 'Forgotten Fortress'),
    ('knucracker2', 'Four Pieces'),
    ('knucracker3', 'Neuron'),
    ('knucracker4', 'Creeper++'),
    ('knucracker5', 'Turtle'),
    ('knucracker6', 'Valley of the Shadow of Death'),
    ('knucracker7', 'Parasite'),
    ('knucracker8', 'Cheap Construction'),
    ('knucracker9', 'Sector L'),
    ('knucracker10', 'Gort'),
    ('knucracker11', 'Creepers Pieces'),
    ('knucracker12', 'Special'),
    ('knucracker13', 'Highway to helheim'),
    ('knucracker14', 'Creeperpeace'),
    ('knucracker15', 'Islands'),
    ('knucracker16', 'Enchanted Forest'),
    ('knucracker17', 'The Dark Side'),
    ('knucracker18', 'Far York Farm'),
    ('knucracker19', 'Chanson'),
    ('knucracker20', 'Invasion'),
    ('knucrackerbonus0', 'Razor'),
    ('knucrackerbonus1', 'Holdem 2'),
    ('demobonus', 'Mark V Sample'),
    ('demobonus2', 'Before Time'),
    ('demobonus3', 'Day of Infamy'),
    ('demobonus4', 'Shaka'),
]
FIRST_SPAN_MISSION = 21

# Maps whose objectives fall in more than one tower-connected component, from
# tools/reachability.py --span. A mover is needed to reach SOME of them; which
# ones depends on where the player lands, and there is no rift lab at mission
# load to read that from. So the requirement is applied objective-WIDE rather
# than per instance - conservative, per the standing rule that a wrong loosening
# reads green when it should read red.
SPLIT_MAPS = ('knucracker11', 'knucracker13', 'knucracker20')

SLOT_NAMES = {0: 'Nullify', 1: 'Totems', 2: 'Reclaim', 3: 'Hold', 4: 'Collect', 5: 'Custom'}


def survey_slots():
    """guid -> {slot: enabled}, from the objective dump."""
    out = {}
    cur = None
    for line in io.open(SURVEY, encoding='utf-8', errors='replace'):
        line = line.rstrip('\n').rstrip('\r')
        if line.startswith('====='):
            parts = [p.strip() for p in line.strip('= ').split('|')]
            cur = parts[0] if parts and not parts[0].startswith('CONTROL') else None
            continue
        if cur is None:
            continue
        m = re.search(r'DEVOBJSLOT (\d+) enabled=(True|False)', line)
        if m:
            out.setdefault(cur, {})[int(m.group(1))] = (m.group(2) == 'True')
    return out


def map_facts(guid):
    """Counts and per-instance geometry straight off the map dump."""
    path = os.path.join(MAPS, guid + '.map')
    if not os.path.exists(path):
        return None
    text = io.open(path, encoding='utf-8', errors='replace').read()
    totems, nullify, caches, pods = [], [], [], set()
    greenar = 0
    for line in text.split('\n'):
        m = re.match(r'unit (\S+) (\d+),(\d+) .*', line)
        if not m:
            continue
        name, x, y = m.group(1), int(m.group(2)), int(m.group(3))
        if name == 'Totem':
            totems.append((y, x))
        if name == 'InfoCache':
            caches.append((y, x))
        if name == 'GreenarMother':
            greenar += 1
        if name == 'Pod':
            pm = re.search(r'podWare=(\d+)', line)
            if pm:
                pods.add(int(pm.group(1)))
        if 'nullifiable=True' in line:
            nullify.append((y, x))
    return {'totems': sorted(totems), 'nullify': sorted(nullify),
            'caches': sorted(caches), 'greenar': greenar, 'pods': pods}


def totem_wares():
    out = {}
    if not os.path.exists(TOTEMS):
        return out
    for line in io.open(TOTEMS, encoding='utf-8', errors='replace'):
        m = re.search(r'DEVTOTEM mission=(\S+) cell=(\d+),(\d+) authored=w(\d+)x', line)
        if m:
            out.setdefault(m.group(1), {})[(int(m.group(3)), int(m.group(2)))] = int(m.group(4))
    return out


def main():
    slots = survey_slots()
    wares = totem_wares()

    titles, counts, objslots, factory, notes = [], [], [], [], []
    missing = []
    for i, (guid, title) in enumerate(SPAN_ORDER):
        n = FIRST_SPAN_MISSION + i
        f = map_facts(guid)
        if f is None:
            missing.append(guid)
            continue
        titles.append((n, guid, title))
        counts.append((n, len(f['caches']), len(f['totems']), len(f['nullify'])))

        # Enabled objective slots, minus Hold: the location model has no Hold
        # location and inventing one here would be a silent model change.
        enabled = sorted(k for k, v in slots.get(guid, {}).items() if v and k != 3)
        objslots.append((n, enabled))

        # A ware is free if a Pod carries it; otherwise the Factory makes it.
        want = sorted({w for w in wares.get(guid, {}).values()})
        needed = [w for w in want if w not in f['pods']]
        factory.append((n, bool(needed), want, sorted(f['pods'])))

        had_hold = slots.get(guid, {}).get(3, False)
        if had_hold:
            notes.append((n, guid, 'Hold/Survive objective present; no location is created for it'))

    if missing:
        sys.exit('no map dump for: %s' % ', '.join(missing))

    out = []
    out.append('"""SPAN Experiment mission data - GENERATED by tools/gen-spandata.py.')
    out.append('')
    out.append('Do not hand-edit. Re-run the generator against the survey dumps.')
    out.append('')
    out.append('MISSION NUMBERS 21..46 ARE FROZEN. Location ids are positional, so')
    out.append('reordering SPAN_ORDER in the generator renumbers every SPAN location and')
    out.append('breaks seeds in flight. Append, never insert.')
    out.append('')
    out.append('Every number here was measured, not authored - see')
    out.append('docs/design/span-survey.md and docs/design/span-requirements.md for how,')
    out.append('and what each measurement can and cannot support.')
    out.append('"""')
    out.append('')
    out.append('FIRST_SPAN_MISSION = %d' % FIRST_SPAN_MISSION)
    out.append('')
    out.append('# mission -> (guid, title). The guid IS the launch specifier: verified by')
    out.append('# booting all 26, specifier == guid on every one.')
    out.append('SPAN_MISSIONS = {')
    for n, guid, title in titles:
        out.append('    %d: (%r, %r),' % (n, guid, title))
    out.append('}')
    out.append('')
    out.append('# mission -> (caches, totems, nullifiable), counted off the live map.')
    out.append('SPAN_INSTANCE_COUNTS = {')
    for n, c, t, nu in counts:
        out.append('    %d: (%d, %d, %d),' % (n, c, t, nu))
    out.append('}')
    out.append('')
    out.append('# mission -> enabled objective slots. Hold (slot 3) is DELIBERATELY')
    out.append('# dropped: no Farsite mission uses it and locations.py has no location')
    out.append('# for it, so creating one would be a silent model change.')
    out.append('SPAN_OBJECTIVE_SLOTS = {')
    for n, en in objslots:
        out.append('    %d: %r,  # %s' % (n, en, ', '.join(SLOT_NAMES[k] for k in en)))
    out.append('}')
    out.append('')
    out.append('# mission -> does the Factory gate its totems?')
    out.append('#')
    out.append('# A ware is free when a Pod on the map carries it and must otherwise be')
    out.append('# made in the Factory - the same rule for every ware. Liftic is ware30,')
    out.append('# Arg is ware29; the campaign only ever uses Liftic, so the Arg maps have')
    out.append('# no campaign precedent and are marked below.')
    out.append('SPAN_TOTEMS_NEED_FACTORY = {')
    for n, need, want, pods in factory:
        wl = '/'.join({29: 'Arg', 30: 'Liftic'}.get(w, 'w%d' % w) for w in want) or 'none'
        pl = '/'.join({29: 'Arg', 30: 'Liftic'}.get(w, 'w%d' % w) for w in pods) or 'no pods on the map'
        out.append('    %d: %s,  # wants %s, %s' % (n, need, wl, pl))
    out.append('}')
    out.append('')
    out.append('# mission -> a mover is needed for some of its objectives.')
    out.append('#')
    out.append('# Objective-WIDE, not per instance. The analysis says which objectives')
    out.append('# share a component; turning that into "this one needs a mover" needs')
    out.append('# the landing site, and no rift lab exists at mission load to read it')
    out.append('# from. Requiring it of the whole objective over-constrains rather than')
    out.append('# under-constrains, which is the safe direction.')
    out.append('SPAN_NEEDS_MOVER = {')
    for i, (guid, title) in enumerate(SPAN_ORDER):
        if guid in SPLIT_MAPS:
            out.append('    %d,  # %s' % (FIRST_SPAN_MISSION + i, title))
    out.append('}')
    out.append('')
    out.append('# Maps carrying something the model does not express. Recorded rather')
    out.append('# than silently dropped.')
    out.append('SPAN_NOTES = {')
    for n, guid, note in notes:
        out.append('    %d: %r,' % (n, note))
    out.append('}')
    out.append('')

    io.open(DST, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print('wrote %s (%d missions, %d..%d)'
          % (DST, len(titles), FIRST_SPAN_MISSION, FIRST_SPAN_MISSION + len(titles) - 1))


if __name__ == '__main__':
    main()
