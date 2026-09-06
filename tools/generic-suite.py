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

The way to get scoping on 0.6.7 is to DELETE the other worlds from a throwaway
checkout, which is what CI does. Measured for this world: 322 tests in 239s
becomes 208 tests in about one second, and the 114 lost tests were other
people's games - ours keep test_fill, test_ids, test_reachability and
test_world_manifest.

Do NOT reach for `unittest -k "Creeper World 4"` instead. It looks equivalent
and is a trap: only the manifest tests generate a class per world, so -k matches
3 tests of 322 and silently skips test_fill, test_ids and test_reachability,
which loop over worlds inside the test body.

So the mode is DETECTED rather than assumed:

* PRUNED (CI): only our world and the suite's fixtures are present, so EVERY
  failure is ours - including nameless ones like test_no_failed_world_loads,
  which is precisely the case an unpruned run has to throw away.
* UNPRUNED, no scoping flag (a full 0.6.7 tree): all 91 worlds run and only
  failure lines naming our game count. A nameless failure is dropped here, so
  prefer a pruned run when it matters.
* SCOPED via AP_TEST_WORLDS (a tree newer than 0.6.7): kept for the day the
  minimum moves up.

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


# The suite's own fixture worlds, plus the shared underscore packages some
# worlds import. 0.6.8 names the first two in worlds/__init__._SUITE_FIXTURE_WORLDS.
FIXTURE_WORLDS = {"generic", "apquest"}


def checkout_is_pruned() -> bool:
    """Is this checkout down to our world and the fixtures?

    If so, nothing else can fail, so failures need no attribution - and the
    nameless ones an unpruned run must discard become meaningful.
    """
    try:
        present = {
            name for name in os.listdir("worlds")
            if os.path.isdir(os.path.join("worlds", name))
            and not name.startswith(("_", "."))
            and name != "__pycache__"
        }
    except OSError:
        return False
    return bool(present) and present <= (FIXTURE_WORLDS | {WORLD})


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
    pruned = checkout_is_pruned()
    scoped = pruned or scoping_supported()
    env = dict(os.environ)
    if scoped and not pruned:
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

    if pruned:
        mode = "pruned checkout - every failure is ours"
    elif scoped:
        mode = "scoped to %s via AP_TEST_WORLDS" % WORLD
    else:
        mode = ("unpruned, no AP_TEST_WORLDS - counting only failures naming %s"
                % GAME)
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
