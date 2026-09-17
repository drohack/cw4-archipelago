"""Per-objective requirement baseline for the SPAN Experiments.

    bash tools/map-dump.sh ...        # dump the maps
    python tools/reachability.py      # MUST pass the campaign control first
    python tools/gen_spanreqs.py      # -> docs/design/span-requirements.md

GENERATED. Do not hand-edit the output; re-run this.

THREE DETECTORS, each validated against the campaign before being pointed here:

  movement  Tower-connected components over land, at Tower.PLACEMENT_RANGE.
            Validated on 26 totem assertions plus Shattered's 4 and Founders'
            17 nullify targets, all correctly outside the free component.
  buried    worldY < terrain column height at the same cell. Validated 10/10 on
            classified campaign caches, including Sequence's within-map pair
            (cache 1 buried, cache 2 not).
  resource  The ware each totem is authored to want, named by the game's own
            DeliveryPadControls.GetWareName. Liftic is ware30, Arg is ware29.
            A ware is FREE if a Pod on the map carries it (Pod.resourceType) and
            must otherwise be made in the Factory. Read, not inferred: "pods can
            have different resources in them", so the presence of a Pod says
            nothing by itself. All ten pods across the campaign's story2/3/4 and
            SPAN's three Pod maps carry Liftic, which is why those six read as
            loose-liftic.

            A DISTINCTION THAT DOES NOT CHANGE THE REQUIREMENT, recorded so the
            next reader does not rediscover it as a bug (designer, 2026-09-16):
            greenar crystals need the REFINERY before the factory, while bluite
            and redite crystals need only a connection to the tower/pylon grid -
            but all three still need the FACTORY to convert. The randomizer
            merged refinery and factory into a single item, so every route lands
            on the same requirement. If those are ever split again, this is the
            line that has to change.

WHAT THE MOVEMENT COLUMN CAN AND CANNOT SAY. It reports which objectives share a
component. Turning that into "needs a mover" takes one more fact - which
component you land in - and there IS no rift lab at mission load to read it
from. So a map whose objectives span several components definitely needs a mover
for some of them; WHICH ones depends on the landing site. A map whose objectives
all share one component is NOT thereby cleared: creep advance, non-gap reach
(Archon) and environmental hazards are all invisible here.

Use this to ADD requirements, never to remove them.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAPS = os.path.join(REPO, '.aptest', 'maps')
TOTEMS = os.path.join(REPO, '.aptest', 'totems.txt')
DST = os.path.join(REPO, 'docs', 'design', 'span-requirements.md')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reachability import (load, components, nearest_component,  # noqa: E402
                          terrain_height, SPAN_GUIDS)

NAMES = {
    'knucracker1': 'Forgotten Fortress', 'knucracker2': 'Four Pieces',
    'knucracker3': 'Neuron', 'knucracker4': 'Creeper++', 'knucracker5': 'Turtle',
    'knucracker6': 'Valley of the Shadow of Death', 'knucracker7': 'Parasite',
    'knucracker8': 'Cheap Construction', 'knucracker9': 'Sector L',
    'knucracker10': 'Gort', 'knucracker11': 'Creepers Pieces',
    'knucracker12': 'Special', 'knucracker13': 'Highway to helheim',
    'knucracker14': 'Creeperpeace', 'knucracker15': 'Islands',
    'knucracker16': 'Enchanted Forest', 'knucracker17': 'The Dark Side',
    'knucracker18': 'Far York Farm', 'knucracker19': 'Chanson',
    'knucracker20': 'Invasion', 'knucrackerbonus0': 'Razor',
    'knucrackerbonus1': 'Holdem 2', 'demobonus': 'Mark V Sample',
    'demobonus2': 'Before Time', 'demobonus3': 'Day of Infamy',
    'demobonus4': 'Shaka',
}
WARE = {29: 'Arg', 30: 'Liftic'}


def totem_wares():
    """cell -> ware index, per mission, from the totems dump."""
    out = {}
    if not os.path.exists(TOTEMS):
        return out
    for line in io.open(TOTEMS, encoding='utf-8', errors='replace'):
        m = re.search(r'DEVTOTEM mission=(\S+) cell=(\d+),(\d+) authored=w(\d+)x', line)
        if m:
            out.setdefault(m.group(1), {})[(int(m.group(2)), int(m.group(3)))] = int(m.group(4))
    return out


def analyse(guid, wares):
    path = os.path.join(MAPS, guid + '.map')
    if not os.path.exists(path):
        return None
    head, units, grids = load(path)
    w, h = [int(v) for v in head['cells'].split('x')]
    reach = int(head.get('towerPlacementRange', '11'))
    ter = grids['terrain']
    land = [[c not in '0?' for c in row.ljust(w)[:w]] for row in ter]
    comp, sizes = components(land, w, h, reach)

    def comp_of(u):
        cid, _d = nearest_component(comp, land, w, h, u['x'], u['y'])
        return cid

    rows = []
    for kind, sel in (('Totem', lambda u: u['name'] == 'Totem'),
                      ('Nullify', lambda u: u.get('nullifiable')),
                      ('Cache', lambda u: u['name'] == 'InfoCache')):
        group = sorted([u for u in units if sel(u)], key=lambda u: (u['y'], u['x']))
        for i, u in enumerate(group, 1):
            ware = wares.get(guid, {}).get((u['x'], u['y'])) if kind == 'Totem' else None
            th = terrain_height(ter, u['x'], u['y'])
            wy = u.get('worldY')
            buried = (kind == 'Cache' and wy is not None and th >= 0 and wy < th)
            rows.append({'kind': kind, 'i': i, 'x': u['x'], 'y': u['y'],
                         'comp': comp_of(u), 'ware': ware, 'buried': buried,
                         'delta': (wy - th) if (wy is not None and th >= 0) else None})
    # WHICH WARES ARE HANDED OVER. A Pod supplies its own resourceType, and
    # "pods can have different resources in them" (designer, 2026-09-16) - so a
    # Pod on the map does NOT mean liftic is free. Read the type.
    podwares = set()
    for u in units:
        if u['name'] == 'Pod' and u.get('podWare') is not None:
            podwares.add(u['podWare'])
    return {'guid': guid, 'rows': rows, 'sizes': sizes, 'w': w, 'h': h,
            'land': sum(sizes), 'greenar': sum(1 for u in units
                                               if u['name'] == 'GreenarMother'),
            'pods': sum(1 for u in units if u['name'] == 'Pod'),
            'podwares': podwares}


def main():
    wares = totem_wares()
    out = []
    out.append('# SPAN Experiments: per-objective requirements')
    out.append('')
    out.append('GENERATED by `tools/gen_spanreqs.py` from `.aptest/maps`. '
               'Do not hand-edit - re-run the generator.')
    out.append('')
    out.append('Every column here comes from a detector validated against the')
    out.append('campaign first; see the module docstring for what each was checked')
    out.append('against. **Use this to ADD requirements, never to remove them** -')
    out.append('creep advance, non-gap reach and environmental hazards are all')
    out.append('invisible to a terrain measurement, and Archon is the worked example')
    out.append('of the last one.')
    out.append('')

    split_maps = []
    for g in SPAN_GUIDS:
        r = analyse(g, wares)
        if r is None:
            continue
        comps_used = sorted({x['comp'] for x in r['rows'] if x['comp'] >= 0})
        if len(comps_used) > 1:
            split_maps.append(g)

        tot = [x for x in r['rows'] if x['kind'] == 'Totem']
        arg = [x for x in tot if x['ware'] == 29]
        lif = [x for x in tot if x['ware'] == 30]
        # THE RULE, and it is the same for every ware (designer, 2026-09-16):
        # a resource is free if a Pod on the map carries it, otherwise it must
        # be made in the Factory. Arg comes from redite crystals plus a Factory
        # exactly as liftic comes from greenar plus one; the raw-resource step
        # differs (bluite and redite need only a grid connection, greenar needs
        # the refinery) but the randomizer merged refinery and factory into one
        # item, so the requirement is identical either way.
        want = sorted({x['ware'] for x in tot if x['ware']})
        freebies = [w for w in want if w in r['podwares']]
        needed = [w for w in want if w not in r['podwares']]
        if not want:
            factory = 'no totems'
        elif not needed:
            factory = 'no - %s supplied by Pod' % '/'.join(WARE.get(w, 'w%d' % w) for w in freebies)
        elif not freebies:
            factory = 'yes - %s must be refined' % '/'.join(WARE.get(w, 'w%d' % w) for w in needed)
        else:
            factory = ('yes for %s; %s is Pod-supplied'
                       % ('/'.join(WARE.get(w, 'w%d' % w) for w in needed),
                          '/'.join(WARE.get(w, 'w%d' % w) for w in freebies)))

        out.append('## %s - %s' % (NAMES.get(g, g), g))
        out.append('')
        out.append('%dx%d, %d land cells, %d component(s). Totems %d (%s), '
                   'nullify %d, caches %d.'
                   % (r['w'], r['h'], r['land'], len(r['sizes']),
                      len(tot),
                      ', '.join(filter(None, [
                          '%d Liftic' % len(lif) if lif else '',
                          '%d Arg' % len(arg) if arg else ''])) or 'ware unknown',
                      len([x for x in r['rows'] if x['kind'] == 'Nullify']),
                      len([x for x in r['rows'] if x['kind'] == 'Cache'])))
        out.append('')
        out.append('- **Factory for totems:** %s' % factory)
        out.append('- **Objectives span components:** %s%s'
                   % (comps_used,
                      ' - a mover is needed for some of these'
                      if len(comps_used) > 1 else ' - no gap found'))
        buried = [x for x in r['rows'] if x['buried']]
        if buried:
            out.append('- **Buried:** %s'
                       % ', '.join('%s %d (delta %.0f)' % (x['kind'], x['i'], x['delta'])
                                   for x in buried))
        out.append('')
        if len(comps_used) > 1:
            out.append('| objective | cell | component |')
            out.append('|---|---|---|')
            for x in r['rows']:
                out.append('| %s %d | %d,%d | %s |'
                           % (x['kind'], x['i'], x['x'], x['y'], x['comp']))
            out.append('')

    # After the preamble paragraph, not inside it.
    out.insert(11, 'Maps whose objectives span more than one component, and so '
                  'definitely need a mover: **%s**.\n' % (', '.join(
                      NAMES.get(g, g) for g in split_maps) or 'none'))
    io.open(DST, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print('wrote %s (%d maps, %d split)' % (DST, len(SPAN_GUIDS), len(split_maps)))


if __name__ == '__main__':
    main()
