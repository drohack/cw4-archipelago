"""Run Archipelago's generic world tests against OUR world, and judge the result.

`docs/tests.md` calls these "generic tests are run on every World to ensure basic
functionality" - they are the closest thing Archipelago has to a spec compliance
check. Nothing in this repo ran them until 2026-09-04, when a single manual run
immediately found a real violation.

Scoped with AP_TEST_WORLDS, which Archipelago provides for exactly this: it
limits auto-loading to the named worlds (plus the `generic` and `apquest`
fixtures, which are not themselves under test). That turns 340 tests over 91
worlds in 97 seconds into 217 tests in under a second, and removes the chance of
another world's failure breaking our build.

We have one KNOWN violation, in EXPECTED below. It is allowed, but an allow-list
that quietly outlives its bug is rot - so an expected failure that STOPS failing
is also an error here, meaning the entry should be deleted.

Usage, from inside an Archipelago checkout with the world synced in:
    python ../tools/generic-suite.py
Exit 0 clean, 1 on any unexpected result.
"""
import os
import re
import subprocess
import sys

WORLD = "cw4"
GAME = "Creeper World 4"

# Known, deliberate, documented. Each entry needs a reason and a pointer.
EXPECTED = {
    "test_itempool_not_modified":
        "place_own_progression removes 31 of 236 pool items during pre_fill, "
        "which adding games.md prohibits. The test exempts Ocarina of Time and "
        "SMZ3 for the same behaviour. Fixable via get_pre_fill_items since the "
        "placed set is seed-invariant, but it is the v0.1.3 change behind 0 "
        "failures in 16,000 seeds, so changing it needs a realfillrate re-run. "
        "See docs/design/2026-09-04-offline-and-disconnects.md.",
}


def main() -> int:
    env = dict(os.environ)
    env["AP_TEST_WORLDS"] = WORLD
    env.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")

    proc = subprocess.run(
        [sys.executable, "-m", "unittest", "discover", "-s", "test/general", "-t", "."],
        capture_output=True, text=True, env=env,
    )
    output = proc.stdout + proc.stderr

    ran = re.search(r"^Ran (\d+) tests", output, re.M)
    if not ran:
        print("could not parse the test output - the suite did not run:")
        print(output[-2000:])
        return 1

    headers = re.findall(r"^(?:FAIL|ERROR): (.+)$", output, re.M)
    failed = {}
    for h in headers:
        m = re.match(r"(\w+)", h)
        if m:
            failed.setdefault(m.group(1), []).append(h)

    unexpected = sorted(n for n in failed if n not in EXPECTED)
    stale = sorted(n for n in EXPECTED if n not in failed)

    print(f"generic suite ({GAME} only): {ran.group(1)} tests, "
          f"{len(headers)} failure line(s)")
    for lines in failed.values():
        for h in lines:
            print("  " + h)

    if unexpected:
        print("\nUNEXPECTED - a spec violation that was not there before:")
        for n in unexpected:
            print("  " + n)
    if stale:
        print("\nNO LONGER FAILING - delete these from EXPECTED in this script:")
        for n in stale:
            print(f"  {n}\n    was: {EXPECTED[n]}")

    if unexpected or stale:
        return 1

    for n in sorted(EXPECTED):
        print(f"\nknown and allowed: {n}\n  {EXPECTED[n]}")
    print("\nOK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
