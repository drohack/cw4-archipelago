"""The shipped player yaml must stay in step with the options.

player.yaml is a release asset - one of the three files a player is handed - and
it is written by hand, so it is the file that rots. A yaml that quietly omits an
option is not obviously broken: generation succeeds on the default, and the
player never learns the setting exists. A yaml naming an option that has been
REMOVED is worse, because Archipelago rejects it and the player has no idea
which line to delete. So it is pinned to CW4Options here, in both directions.

It replaced the template Archipelago generates from the option docstrings
(2026-10-06, droha: "there's way too much info in the comments"). The same
arrangement as alttl-archipelago, whose player.yaml this follows. It lives in
the package so this test can find it, and .apignore keeps it out of the built
.apworld.
"""
import json
import pathlib
import unittest

from Options import PerGameCommonOptions, Visibility

from ..options import CW4Options

HERE = pathlib.Path(__file__).resolve().parent.parent
YAML = HERE / "player.yaml"
GAME = "Creeper World 4"


def _load() -> dict:
    # Archipelago's own reader, the one Generate uses, so a quirk in how it
    # reads a value (on/off, a leading BOM) is a quirk this test sees too.
    from Utils import parse_yamls
    docs = list(parse_yamls(YAML.read_text(encoding="utf-8-sig")))
    assert len(docs) == 1, f"player.yaml holds {len(docs)} documents, not 1"
    return docs[0]


def _shown(option) -> bool:
    """Whether Archipelago offers this option in a template at all."""
    return bool(option.visibility & Visibility.template)


class TestPlayerYaml(unittest.TestCase):
    def setUp(self) -> None:
        self.doc = _load()
        self.section = self.doc[GAME]
        common = set(PerGameCommonOptions.type_hints)
        ours = {name: opt for name, opt in CW4Options.type_hints.items()
                if name not in common}
        self.shown = {name for name, opt in ours.items() if _shown(opt)}
        self.hidden = set(ours) - self.shown
        self.common = common

    def test_the_root_is_what_archipelago_expects(self) -> None:
        # The root keys the Advanced YAML Guide lists, and nothing else.
        self.assertEqual({"name", "description", "game", "requires", GAME},
                         set(self.doc))
        self.assertEqual(GAME, self.doc["game"])

    def test_it_requires_the_archipelago_we_support(self) -> None:
        manifest = json.loads((HERE / "archipelago.json").read_text(encoding="utf-8"))
        self.assertEqual(str(manifest["minimum_ap_version"]),
                         str(self.doc["requires"]["version"]))

    def test_every_option_is_offered(self) -> None:
        """A missing option is a setting the player never finds out about."""
        missing = sorted(self.shown - set(self.section))
        self.assertFalse(missing, f"player.yaml does not mention {missing}")

    def test_nothing_is_offered_that_does_nothing(self) -> None:
        """An unknown key makes Archipelago reject the yaml, and a hidden one
        is an option that currently has no effect."""
        extra = sorted(set(self.section) - self.shown - self.common)
        self.assertFalse(extra, f"player.yaml offers {extra}, which is unknown or hidden")

    def test_the_values_are_the_defaults(self) -> None:
        """Untouched, it must generate the seed the website's defaults would."""
        for name in self.shown:
            option = CW4Options.type_hints[name]
            written = self.section[name]
            with self.subTest(option=name):
                if isinstance(option.default, str):
                    # early_weapon's default is the string "random", which
                    # from_any would RESOLVE - so compare the spelling instead.
                    self.assertEqual(option.default, written)
                else:
                    self.assertEqual(option.from_any(option.default).value,
                                     option.from_any(written).value)

    def test_it_reads_in_the_same_order_as_the_options_page(self) -> None:
        """The website and this file are one order, so a player moving between
        them is never hunting.

        Archipelago's own get_option_groups is the source, because it is what
        the options page and the official template are built from. It puts the
        options no group claims - progression_balancing and accessibility - in
        a "Game Options" group FIRST, then ours in option_groups order, then
        its "Item & Location Options", which this file leaves out. The first
        version of this file put the two Archipelago options at the bottom and
        a test checking only our groups let it through; droha caught it.
        """
        from Options import get_option_groups
        from .. import CW4World
        page = [name for group in get_option_groups(CW4World).values() for name in group]
        written = list(self.section)
        self.assertEqual([name for name in page if name in self.section], written)
