"""Run Archipelago's generic world tests against OUR world, and judge the result.

`docs/tests.md` calls these "generic tests are run on every World to ensure basic
functionality" - they are the closest thing Archipelago has to a spec compliance
check. Nothing in this repo ran them until 2026-09-04, when a single manual run
immediately found a real violation.

AP_TEST_WORLDS would limit auto-loading to our world alone, but **it does not
exist in 0.6.7** - our declared minimum, what CI pins, and what the dev clone is
checked out at. It arrived after that release. Setting it on 0.6.7 does nothing
at all, silently, which is how this script's first CI run failed: unscoped, it
picked up `test_no_failed_world_loads` - another world in the checkout failing
to import - and reported it as our spec violation.

So the mode is DETECTED from worlds/__init__.py rather than assumed:

* UNSCOPED (0.6.7, and therefore CI): all 91 worlds load, all 322 tests run,
  about three and a half minutes. Nothing is skipped - the script ignores
  failure lines that do not name our game. That drops nameless failures such as
  test_no_failed_world_loads; if OUR world were the one failing to import it
  would also take all 225 world tests with it, so it cannot pass unnoticed.
* SCOPED (a tree new enough to have the flag): 217 tests in under a second. We
  do not keep such a tree, so this path is for the day the minimum moves up.

We have one KNOWN violation, in EXPECTED below. It is allowed, but an allow-list
that quietly outlives its bug is rot - so an expected failure that STOPS failing
is also an error here, meaning the entry should be deleted.

Usage, from inside an Archipelago checkout with the world synced in:
    python ../tools/generic-suite.py
Exit 0 clean, 1 on any unexpected result.
"""
import io
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


def scoping_supported() -> bool:
    """Does this Archipelago honour AP_TEST_WORLDS?

    Asked of the source rather than assumed, because setting an unsupported
    environment variable fails silently - the suite simply runs everything, and
    then someone else's broken world looks like our bug.
    """
    try:
        with io.open(os.path.join("worlds", "__init__.py"), encoding="utf-8") as fh:
            return "AP_TEST_WORLDS" in fh.read()
    except OSError:
        return False


def main() -> int:
    scoped = scoping_supported()
    env = dict(os.environ)
    if scoped:
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
    if not scoped:
        # Everything ran, so a failure is only ours if it says so. A failure
        # with no game in its parameters belongs to the checkout, not to us.
        headers = [h for h in headers if GAME in h]

    failed = {}
    for h in headers:
        m = re.match(r"(\w+)", h)
        if m:
            failed.setdefault(m.group(1), []).append(h)

    unexpected = sorted(n for n in failed if n not in EXPECTED)
    stale = sorted(n for n in EXPECTED if n not in failed)

    mode = ("scoped to %s via AP_TEST_WORLDS" % WORLD if scoped
            else "unscoped (no AP_TEST_WORLDS here) - counting only failures naming %s" % GAME)
    print(f"generic suite [{mode}]: {ran.group(1)} tests, "
          f"{len(headers)} failure line(s) attributable to us")
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
