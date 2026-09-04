from test.bases import WorldTestBase

from .. import items


class CW4TestBase(WorldTestBase):
    game = "Creeper World 4"

    # Our own progression fill is OFF for these tests by default.
    #
    # World.pre_fill places this world's progression itself and retries on
    # failure (see items.place_own_progression). That is the right behaviour for
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

    def setUp(self) -> None:
        self._own_fill_attempts = items.OWN_FILL_ATTEMPTS
        if not self.own_fill:
            items.OWN_FILL_ATTEMPTS = 0
        try:
            super().setUp()
        finally:
            items.OWN_FILL_ATTEMPTS = self._own_fill_attempts
