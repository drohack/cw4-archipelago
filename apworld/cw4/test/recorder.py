"""What the World's pre_fill placed, recorded from OUTSIDE the World.

The World keeps none of this. A player's generation has no use for which items
the bootstrap placed or how many attempts the own fill needed, so those used to
be attributes on the World that only tests and the audit tools read - test code
shipped in every .apworld. They are recorded here instead, by wrapping the two
module functions World.pre_fill calls.

That only works because pre_fill calls them through the MODULE
(`opening.bootstrap_opening(self)`), so replacing the module attribute is what
it finds. A `from .opening import bootstrap_opening` in __init__.py would bypass
this silently and every record would read as "nothing placed".

    from worlds.cw4.test import recorder
    recorder.install()
    ...generate...
    rec = recorder.of(world)
    rec.bootstrapped, rec.own_placements, rec.own_fill_attempts

Lives under test/ so it never ships; the tools in tools/ import it from the
Archipelago clone, where tools/ap-sync.ps1 puts it.
"""
import weakref

from .. import opening


class PreFillRecord:
    def __init__(self) -> None:
        # (location, item) names bootstrap_opening placed, in placement order.
        self.bootstrapped = []
        # (location, item) names place_own_progression placed.
        self.own_placements = []
        # How many attempts place_own_progression made, counted as calls of its
        # main fill. 0 when it stood down (another game present) or its attempt
        # cap was 0; a seed that needed one retry reads 2.
        self.own_fill_attempts = 0


_records = weakref.WeakKeyDictionary()
_installed = False


def of(world) -> PreFillRecord:
    """The record for this world - empty if pre_fill placed nothing."""
    return _records.get(world) or PreFillRecord()


def _record(world) -> PreFillRecord:
    rec = _records.get(world)
    if rec is None:
        rec = _records[world] = PreFillRecord()
    return rec


def install() -> None:
    """Wrap opening.bootstrap_opening and opening.place_own_progression.

    Idempotent, and permanent for the process: the wrappers only record, so
    leaving them in place changes nothing about what is generated.
    """
    global _installed
    if _installed:
        return
    _installed = True
    real_bootstrap = opening.bootstrap_opening
    real_own = opening.place_own_progression

    def bootstrap_opening(world):
        placed = real_bootstrap(world)
        _record(world).bootstrapped = list(placed)
        return placed

    def place_own_progression(world):
        # Counted at Fill.fill_restrictive, which place_own_progression imports
        # inside its own body - so the module attribute set here is the one it
        # gets. Each attempt runs the main fill exactly once; the early-items
        # fill is named differently and allow_partial, so it never ends one.
        import Fill
        real_fill = Fill.fill_restrictive
        calls = 0

        def counting(*args, **kwargs):
            nonlocal calls
            if kwargs.get("name") == "CW4 own progression":
                calls += 1
            return real_fill(*args, **kwargs)

        Fill.fill_restrictive = counting
        try:
            placed = real_own(world)
        finally:
            Fill.fill_restrictive = real_fill
        rec = _record(world)
        rec.own_placements = list(placed)
        rec.own_fill_attempts = calls
        return placed

    opening.bootstrap_opening = bootstrap_opening
    opening.place_own_progression = place_own_progression
