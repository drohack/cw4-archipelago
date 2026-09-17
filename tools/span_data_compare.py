"""Compare a span-data-check log against apworld/cw4/span_data.py.

    py -3.13 tools/span_data_compare.py .aptest/span-data-check/log.txt

Reads the DEVOBJ / DEVOBJSLOT lines CW4DevTools writes for each booted map and
checks four things per map against the frozen table:

  totems        gs.totems                  -> SPAN_INSTANCE_COUNTS[n][1]
  nullify       gs.nullifiableUnits        -> SPAN_INSTANCE_COUNTS[n][2]
  caches        gs.infocaches              -> SPAN_INSTANCE_COUNTS[n][0]
  slots         the objectives the map ENABLES -> SPAN_OBJECTIVE_SLOTS[n]

SLOT 3, HOLD, IS EXCLUDED ON BOTH SIDES. No Farsite mission uses it, there is no
location for it, and span_data drops it by construction - so a map that enables
it is expected to differ here and the maps that do are listed in SPAN_NOTES.

TWO DIFFERENT QUESTIONS, depending on which log you point it at, and it is worth
being clear which one a green run has answered:

  .aptest/span-survey.txt        the dump span_data.py was GENERATED from. A pass
                                 says the generator did not garble anything -
                                 useful, and cheap, but it cannot notice the game
                                 changing underneath, because both sides trace to
                                 the same 2026 measurement.
  a fresh tools/span-data-check.sh run
                                 26 maps booted again, now. A pass says the game
                                 still agrees. This is the one that catches a
                                 patch adding an emitter to a map.

Exit 0 when every map matches, 1 otherwise. An empty or truncated log fails
loudly ("never booted, so nothing was checked") rather than passing by finding
nothing - the failure mode that makes a log-reading check worthless.
"""
import io
import os
import re
import runpy
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
span = runpy.run_path(os.path.join(REPO, 'apworld', 'cw4', 'span_data.py'))
MISSIONS = span['SPAN_MISSIONS']
COUNTS = span['SPAN_INSTANCE_COUNTS']
SLOTS = span['SPAN_OBJECTIVE_SLOTS']
NOTES = span['SPAN_NOTES']

BY_GUID = {guid: n for n, (guid, _title) in MISSIONS.items()}

DEVOBJ = re.compile(
    r'DEVOBJ mission=(\S+) totems=\d+/(\d+) nullifiable=(\d+) '
    r'mustCollect=\d+ maxMustCollect=\d+ infocaches=(\d+)')
DEVSLOT = re.compile(r'DEVOBJSLOT (\d+) enabled=(True|False)')


def parse(path):
    """guid -> {'totems':.., 'nullify':.., 'caches':.., 'slots':[..]}

    The slot lines follow their DEVOBJ header, so the current map is whatever
    header was seen last. A map booted twice keeps its LAST reading, which is
    what a re-run of one map should mean.
    """
    out = {}
    current = None
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = DEVOBJ.search(line)
        if m:
            current = m.group(1)
            out[current] = {'totems': int(m.group(2)),
                            'nullify': int(m.group(3)),
                            'caches': int(m.group(4)),
                            'slots': []}
            continue
        m = DEVSLOT.search(line)
        if m and current in out:
            if m.group(2) == 'True':
                out[current]['slots'].append(int(m.group(1)))
    return out


def main():
    if len(sys.argv) < 2:
        print('usage: span_data_compare.py <log>', file=sys.stderr)
        return 2
    live = parse(sys.argv[1])

    bad = 0
    missing = []
    for n in sorted(MISSIONS):
        guid, title = MISSIONS[n]
        if guid not in live:
            missing.append('%s (%s)' % (title, guid))
            continue
        got = live[guid]
        caches, totems, nullify = COUNTS[n]
        want_slots = [s for s in SLOTS[n] if s != 3]
        got_slots = [s for s in got['slots'] if s != 3]

        problems = []
        if got['totems'] != totems:
            problems.append('totems %d, table says %d' % (got['totems'], totems))
        if got['nullify'] != nullify:
            problems.append('nullify %d, table says %d' % (got['nullify'], nullify))
        if got['caches'] != caches:
            problems.append('caches %d, table says %d' % (got['caches'], caches))
        if sorted(got_slots) != sorted(want_slots):
            problems.append('slots %s, table says %s' % (sorted(got_slots), sorted(want_slots)))

        if problems:
            bad += 1
            print('  FAIL  %-32s %s' % (title, '; '.join(problems)), flush=True)
        else:
            note = '  (%s)' % NOTES[n] if n in NOTES else ''
            print('  PASS  %-32s totems %d, nullify %d, caches %d, slots %s%s'
                  % (title, totems, nullify, caches, sorted(want_slots), note), flush=True)

    if missing:
        print('  FAIL  never booted, so nothing was checked: %s'
              % ', '.join(missing), flush=True)
        bad += len(missing)

    print('Done: %d of %d maps match the frozen table, %d do not'
          % (len(MISSIONS) - bad, len(MISSIONS), bad), flush=True)
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
