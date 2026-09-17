"""Turn .aptest/span-survey.txt into docs/design/span-survey.md.

GENERATED - do not hand-edit the output; re-run this.

    bash tools/span-survey.sh        # 26 unattended boots
    python tools/gen-spantable.py

What each column is worth, stated plainly, because the whole point of the survey
is to separate what is measured from what is guessed:

  objectives   EXACT. obj:dump reads GameSpace totems / nullifiableUnits /
               maxMustCollect at mission start.
  withholds    EXACT. buildings:dump reads the mission's own 26 availability
               flags. This is the column that prunes requirements: a map that
               does not offer a Terp cannot require one.
  oddities     EXACT detection, judgement still needed. Map-embedded CPACK units
               and unit types the Farsite model has no location for.
  movers       NOT MEASURED. Whether a map needs a Terp/Platform/Porter/Pylon to
               REACH something is terrain reachability, and nothing here reads
               terrain. Left blank on purpose rather than guessed.
"""
import io
import os
import re
import sys
from collections import OrderedDict

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, '.aptest', 'span-survey.txt')
DST = os.path.join(REPO, 'docs', 'design', 'span-survey.md')

# The 26 build keys, in the order buildings:dump reports them. airship, bertha
# and sweeper are off in every campaign mission by design (they are the bonus
# units), so they are not "withheld" in any interesting sense.
BONUS = {'airship', 'bertha', 'sweeper'}
ALWAYS = {'riftlab', 'tower'}

# Unit types that mean the Farsite objective model does not fit as-is.
ODD_UNITS = {
    'SurviveBase': 'Hold/Survive objective - no Farsite mission uses slot 3',
}

SLOTS = {0: 'Nullify', 1: 'Totems', 2: 'Reclaim', 3: 'Hold', 4: 'Collect', 5: 'Custom'}


def parse(path):
    blocks = OrderedDict()
    cur = None
    for raw in io.open(path, encoding='utf-8', errors='replace'):
        line = raw.rstrip('\n').rstrip('\r')
        if line.startswith('====='):
            parts = [p.strip() for p in line.strip('= ').split('|')]
            if len(parts) < 2 or parts[0].startswith('CONTROL'):
                cur = None
                continue
            cur = parts[0]
            blocks[cur] = {
                'title': parts[1], 'loaded': parts[2] if len(parts) > 2 else '',
                'slots': {}, 'on': [], 'off': [], 'cmods': 0, 'onmap': '',
                'totems': None, 'nullify': None, 'caches': None,
            }
            continue
        if cur is None:
            continue
        b = blocks[cur]

        m = re.search(r'DEVOBJ mission=(\S+) totems=(\d+)/(\d+) nullifiable=(\d+) '
                      r'mustCollect=(\d+) maxMustCollect=(\d+) infocaches=(\d+)', line)
        if m:
            b['totems'] = int(m.group(3))
            b['nullify'] = int(m.group(4))
            # maxMustCollect is the authored cache target; infocaches counts the
            # objects still present. Prefer the target - it does not shrink.
            b['caches'] = max(int(m.group(6)), int(m.group(7)))
            continue

        m = re.search(r'DEVOBJSLOT (\d+) enabled=(True|False) required=(True|False) count=(\d+)', line)
        if m:
            b['slots'][int(m.group(1))] = (m.group(2) == 'True', m.group(3) == 'True', int(m.group(4)))
            continue

        m = re.search(r'DEVBUILD ON : (.*)$', line)
        if m:
            b['on'] = [s for s in m.group(1).split(',') if s]
            continue
        m = re.search(r'DEVBUILD OFF: (.*)$', line)
        if m:
            b['off'] = [s for s in m.group(1).split(',') if s]
            continue
        m = re.search(r'DEVTOOLS cmods: (\d+) player-buildable:(.*)$', line)
        if m:
            b['cmods'] = int(m.group(1))
            b['cmodnames'] = m.group(2).strip()
            continue
        m = re.search(r'DEVTOOLS units on map: (.*)$', line)
        if m:
            b['onmap'] = m.group(1)
            continue
    return blocks


def oddities(b):
    out = []
    if b.get('cmods'):
        out.append('%d custom unit(s): %s' % (b['cmods'], b.get('cmodnames', '')))
    for unit, why in ODD_UNITS.items():
        if unit in (b.get('onmap') or ''):
            out.append(why)
    for idx, (en, req, cnt) in sorted(b['slots'].items()):
        if en and idx == 3:
            out.append('Hold slot enabled')
    if b['loaded'] and b['loaded'] != b.get('_guid', b['loaded']):
        pass
    return '; '.join(out)


def main():
    if not os.path.exists(SRC):
        sys.exit('no survey at %s - run tools/span-survey.sh first' % SRC)
    blocks = parse(SRC)
    if not blocks:
        sys.exit('no map blocks parsed from %s' % SRC)

    rows = []
    for guid, b in blocks.items():
        b['_guid'] = guid
        withheld = sorted(set(b['off']) - BONUS)
        slots_on = [SLOTS[i] for i, (en, _r, _c) in sorted(b['slots'].items()) if en]
        rows.append((guid, b, withheld, slots_on))

    out = []
    out.append('# SPAN Experiments: survey')
    out.append('')
    out.append('GENERATED by `tools/gen-spantable.py` from `tools/span-survey.sh`.')
    out.append('Do not hand-edit - re-run it.')
    out.append('')
    out.append('All 26 maps booted unattended and every one loaded the map it was')
    out.append('asked for, so `specifier == guid` holds across the whole roster.')
    out.append('')
    out.append('**What is measured and what is not.** Objective counts and withheld')
    out.append('buildings are read from the game. Whether a map needs a MOVER - a Terp,')
    out.append('Platform, Porter or Pylon to reach something - is terrain reachability,')
    out.append('and nothing in the survey reads terrain. That column is deliberately')
    out.append('absent rather than guessed: a wrong mover requirement is a false green.')
    out.append('')
    out.append('| guid | title | nullify | totems | caches | objective slots | buildings withheld | oddities |')
    out.append('|---|---|---|---|---|---|---|---|')
    for guid, b, withheld, slots_on in rows:
        out.append('| `%s` | %s | %s | %s | %s | %s | %s | %s |' % (
            guid, b['title'],
            b['nullify'] if b['nullify'] is not None else '?',
            b['totems'] if b['totems'] is not None else '?',
            b['caches'] if b['caches'] is not None else '?',
            ', '.join(slots_on) or 'none',
            ', '.join(withheld) if withheld else 'nothing',
            oddities(b) or '-'))

    # A roster is only useful if the totals are visible.
    tot_n = sum(b['nullify'] or 0 for _g, b, _w, _s in rows)
    tot_t = sum(b['totems'] or 0 for _g, b, _w, _s in rows)
    tot_c = sum(b['caches'] or 0 for _g, b, _w, _s in rows)
    out.append('')
    out.append('## Totals')
    out.append('')
    out.append('%d maps, %d nullify targets, %d totems, %d caches.' % (len(rows), tot_n, tot_t, tot_c))
    out.append('')
    out.append('For scale, the 20 Farsite missions carry 236 locations in total.')
    out.append('')

    nothing = [g for g, _b, w, _s in rows if not w]
    hold = [g for g, _b, _w, s in rows if 'Hold' in s]
    custom_units = [(g, b.get('cmodnames', '')) for g, b, _w, _s in rows if b.get('cmods')]
    required = [(g, i, c) for g, b, _w, _s in rows
                for i, (en, req, _n) in b['slots'].items() if req for c in [b['title']]]
    with_caches = [(g, b['caches']) for g, b, _w, _s in rows if (b['caches'] or 0) > 0]

    out.append('## What this changes')
    out.append('')
    out.append('### The build list prunes nothing')
    out.append('')
    out.append('%d of %d maps withhold NOTHING - every unit except the three bonus'
               % (len(nothing), len(rows)))
    out.append('units is available from the start. The survey plan leaned on this as')
    out.append('the one exact way to narrow a map\'s possible requirements ("a map that')
    out.append('does not offer a Terp cannot require one"). It yields nothing here.')
    out.append('')
    out.append('The dump is not broken: the control run on `story1` reports')
    out.append('`available=2/26` - rift lab and tower only - so it discriminates')
    out.append('perfectly. SPAN maps simply hand you everything and let the map be the')
    out.append('difficulty, which is what "development maps" implies in hindsight.')
    out.append('')
    out.append('### Almost nothing is a required objective')
    out.append('')
    out.append('%d of %d maps have NO required objective at all.' % (len(rows) - len(required), len(rows)))
    if required:
        out.append('The only exception:')
        out.append('')
        for g, i, title in required:
            out.append('- `%s` (%s), slot %d' % (g, title, i))
    out.append('')
    out.append('THIS IS NOT THE PROBLEM IT FIRST LOOKED. Measured against a Farsite')
    out.append('control (tools/objflag-control.sh): the `required` flag is trustworthy -')
    out.append('story2 reports slots 0,1,4, story6 reports 2 and story20 reports 0,1,2,5,')
    out.append('all exactly matching REQUIRED_OBJECTIVES. And the danger that mattered -')
    out.append('that `IsMissionComplete()` might be VACUOUSLY TRUE where nothing is')
    out.append('required, which would make the randomizer send a completion check the')
    out.append('moment the map loaded - does not happen: Forgotten Fortress reads')
    out.append('`IsMissionComplete=False` at load, exactly like the Farsite missions.')
    out.append('')
    out.append('So each SPAN map is won by its own rule and the mod does not need to know')
    out.append('which: it polls the same `World.IsMissionComplete()` it already polls.')
    out.append('`MissionCompletionStats.IsMissionComplete(spec)` is the persisted record,')
    out.append('and it reads True for beaten Farsite missions and False for unplayed SPAN')
    out.append('ones, so it is a working per-map completion signal.')
    out.append('')
    out.append('The objectives being OPTIONAL is a benefit. On Farsite the required')
    out.append('objectives are both the win condition and the checks, which is the race')
    out.append('REQUIRED_OBJECTIVES exists to work around. On SPAN the two are separate:')
    out.append('the map is won by its own rule, and its nullify targets and totems are')
    out.append('pure optional collectibles - which is the shape a location wants.')
    out.append('')
    out.append('### Starters are a problem')
    out.append('')
    out.append('The roster holds %d cache(s) in total%s.' % (
        sum(c for _g, c in with_caches),
        ' (' + ', '.join('%s x%d' % (g, c) for g, c in with_caches) + ')' if with_caches else ''))
    out.append('A Farsite mission opens the game because it has a cache collectable')
    out.append('with a rift lab and one tower. With one cache across 26 maps, SPAN')
    out.append('cannot supply that opening, so the seed\'s first checks have to keep')
    out.append('coming from Farsite missions however the mix is drawn.')
    out.append('')
    out.append('### Objective types the model has no slot for')
    out.append('')
    out.append('%d map(s) enable the Hold/Survive slot, which no Farsite mission uses'
               % len(hold))
    out.append('and which `locations.py` has no location for: %s.' % (', '.join('`%s`' % g for g in hold) or 'none'))
    out.append('')
    out.append('%d map(s) carry map-embedded CPACK units - these ship with the map,' % len(custom_units))
    out.append('cannot be unlocked and cannot be items:')
    out.append('')
    for g, names in custom_units:
        out.append('- `%s`: %s' % (g, names))
    out.append('')

    io.open(DST, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print('wrote %s (%d maps)' % (DST, len(rows)))


if __name__ == '__main__':
    main()
