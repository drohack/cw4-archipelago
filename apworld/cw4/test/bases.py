from test.bases import WorldTestBase

from .. import items, opening


class CW4TestBase(WorldTestBase):
    game = "Creeper World 4"

    # Our own progression fill is OFF for these tests by default.
    #
    # World.pre_fill places this world's progression itself and retries on
    # failure (see opening.place_own_progression). That is the right behaviour for
    # a real seed and the wrong one for an ACCESS test: assertAccessDependency
    # and collect_all_but work by removing items from the POOL, and a pre-placed
    # item is not in the pool, so those assertions silently stop testing
    # anything. Twenty-seven of them broke the moment the fill was added.
    #
    # Access tests are about the RULES, so they run against an untouched pool.
    # The fill itself is covered where it belongs - test_fill generates for real,
    # and TestOwnProgressionFill exercises the retry directly.
    #
    # Set `own_fill = True` on a subclass to test with it enabled.
    own_fill = False

    # ...EXCEPT for the inherited tests that are about GENERATING rather than
    # about the rules. Those must run the path a real seed takes, and switching
    # our pre_fill off is not a neutral simplification - it removes the world's
    # only defence against a known Archipelago limitation, and so tests a
    # configuration that never ships.
    #
    # This was not theoretical. CI failed test_fill on TestAllWeightsZero with
    # "No more spots to place 25 items", and measuring the two paths over the
    # same seeds separated them cleanly:
    #
    #     our pre_fill ON      0 failures / 16,000 seeds
    #     our pre_fill OFF     3 failures /  3,000 seeds
    #
    # All three failures had the identical shape - a two-location opening that
    # did not chain - and all three were RECOVERED ON THE FIRST ATTEMPT when the
    # same seed was re-run with our fill on. So no seed was unfillable; the
    # arrangement existed every time and Archipelago's capped backtracking (each
    # item swapped at most twice) could not find it. That is precisely the
    # limitation place_own_progression exists to absorb, and precisely what
    # turning it off in the test re-exposed.
    #
    # Keyed on the test NAME rather than a class flag because it varies per
    # test, not per class: TestAllWeightsZero needs an untouched pool for its
    # own assertions and the shipped fill for the inherited test_fill.
    FILL_TESTS = frozenset({"test_fill"})

    def setUp(self) -> None:
        self._own_fill_attempts = opening.OWN_FILL_ATTEMPTS
        if not (self.own_fill or self._testMethodName in self.FILL_TESTS):
            opening.OWN_FILL_ATTEMPTS = 0
        try:
            super().setUp()
        finally:
            opening.OWN_FILL_ATTEMPTS = self._own_fill_attempts
