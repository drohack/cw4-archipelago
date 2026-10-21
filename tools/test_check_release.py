"""Tests for tools/check_release.py's artifact rules.

    py -3.13 -m unittest discover -s tools -p "test_check_release.py"

The allowlist rules are exercised on synthetic artifacts, so they run anywhere.
The metadata rule needs a real managed assembly, and the only ones to hand are
the mod's own build output - so those tests run where the mod has been built
(a maintainer's machine) and skip in CI, which cannot build it without the game.
"""
import io
import json
import os
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check_release as cr  # noqa: E402

BUILT = os.path.join(cr.REPO, "src", "CW4Archipelago", "bin", "Release")


def read_bytes(path):
    with open(path, "rb") as f:
        return f.read()


def write_zip(path, entries):
    with zipfile.ZipFile(path, "w") as zf:
        for name, data in entries.items():
            zf.writestr(name, data)


class Base(unittest.TestCase):
    def setUp(self):
        cr.FAILURES.clear()
        self.tmp = tempfile.TemporaryDirectory()
        self._out = sys.stdout
        sys.stdout = io.StringIO()     # the PASS/FAIL lines are not the test's output

    def tearDown(self):
        sys.stdout = self._out
        self.tmp.cleanup()

    def rules_failed(self):
        return {rule for rule, _ in cr.FAILURES}


class ApworldAllowlist(Base):
    def build(self, extra=(), drop=()):
        entries = {}
        for name in cr.APWORLD_FILES - set(drop):
            entries[name] = (json.dumps({"world_version": "9.9.9"})
                             if name.endswith("archipelago.json") else "# stub\n")
        for name in extra:
            entries[name] = "# stub\n"
        path = os.path.join(self.tmp.name, "cw4.apworld")
        write_zip(path, entries)
        return path

    def test_exact_set_passes(self):
        cr.check_apworld(self.build(), "9.9.9")
        self.assertEqual(cr.FAILURES, [])

    def test_a_stray_file_fails(self):
        # The case the old "no test/" blocklist could not see: an untracked
        # file in apworld/cw4 that the builder picks up from the filesystem.
        cr.check_apworld(self.build(extra=["cw4/scratch.py"]), "9.9.9")
        self.assertIn("apworld contents", self.rules_failed())

    def test_the_test_suite_still_fails(self):
        cr.check_apworld(self.build(extra=["cw4/test/test_access.py"]), "9.9.9")
        self.assertIn("apworld contents", self.rules_failed())

    def test_a_missing_module_fails(self):
        cr.check_apworld(self.build(drop=["cw4/rules.py"]), "9.9.9")
        self.assertIn("apworld contents", self.rules_failed())

    def test_directory_entries_are_ignored(self):
        cr.check_apworld(self.build(extra=["cw4/docs/"]), "9.9.9")
        self.assertEqual(cr.FAILURES, [])


class ZipAllowlist(Base):
    def build(self, extra=(), drop=()):
        entries = {"BepInEx/plugins/": ""}
        for name in cr.ZIP_FILES - set(drop):
            entries[name] = b"not a real assembly"
        for name in extra:
            entries[name] = b"x"
        path = os.path.join(self.tmp.name, "mod.zip")
        write_zip(path, entries)
        return path

    def test_exact_set_passes_the_allowlist(self):
        cr.check_zip(self.build())
        # The stub DLLs are not assemblies, so the metadata rule fails on them;
        # the allowlist itself must be clean.
        self.assertNotIn("zip contents", self.rules_failed())

    def test_the_debug_plugin_fails(self):
        cr.check_zip(self.build(extra=[cr.ZIP_DIR + "CW4Archipelago.Debug.dll"]))
        self.assertIn("zip contents", self.rules_failed())

    def test_any_other_extra_fails(self):
        cr.check_zip(self.build(extra=[cr.ZIP_DIR + "notes.txt"]))
        self.assertIn("zip contents", self.rules_failed())

    def test_an_empty_mod_fails(self):
        cr.check_zip(self.build(drop=cr.ZIP_FILES))
        self.assertIn("zip contents", self.rules_failed())


class MetadataNames(Base):
    def test_rejects_what_is_not_an_assembly(self):
        with self.assertRaises(ValueError):
            cr.metadata_strings(b"not a PE file at all")

    @unittest.skipUnless(os.path.exists(os.path.join(BUILT, "CW4Archipelago.dll")),
                         "needs the mod built (src/CW4Archipelago/bin/Release)")
    def test_reads_real_names_out_of_the_mod(self):
        names = cr.metadata_strings(read_bytes(os.path.join(BUILT, "CW4Archipelago.dll")))
        # A type and a method the mod certainly declares: proves the reader
        # finds the heap rather than returning an empty set that passes.
        self.assertIn("TrapApplier", names)
        self.assertIn("SporeStrikeScatter", names)

    @unittest.skipUnless(os.path.exists(os.path.join(BUILT, "CW4Archipelago.dll")),
                         "needs the mod built (src/CW4Archipelago/bin/Release)")
    def test_the_built_mod_carries_no_debug_only_names(self):
        for dll in ("CW4Archipelago.dll", "CW4Archipelago.Core.dll"):
            cr.check_assembly_names(dll, read_bytes(os.path.join(BUILT, dll)))
        self.assertEqual(cr.FAILURES, [])

    def test_a_name_hidden_in_a_getter_is_still_found(self):
        # The compiler stores a property's name only as the tail of its
        # getter's; a whole-string match would miss it.
        real = cr.metadata_strings
        cr.metadata_strings = lambda data: {"get_RenderedLines", "TrapApplier"}
        try:
            cr.check_assembly_names("stub.dll", b"")
        finally:
            cr.metadata_strings = real
        self.assertIn("no debug-only names", self.rules_failed())

    def test_a_longer_identifier_is_not_a_match(self):
        real = cr.metadata_strings
        cr.metadata_strings = lambda data: {"TrackerDiagnosticsOverlay", "IsRiftLabAlive"}
        try:
            cr.check_assembly_names("stub.dll", b"")
        finally:
            cr.metadata_strings = real
        self.assertEqual(cr.FAILURES, [])


class ChangelogRule(Base):
    """A section for the version on every push; a WRITTEN one to release."""

    OPENED = "# Changelog\n\n## v9.9.9 - Unreleased\n\n## v9.9.8 - Old\n\n- old notes\n"
    WRITTEN = "# Changelog\n\n## v9.9.9 - A title\n\n- a note\n\n## v9.9.8 - Old\n\n- old\n"

    def check(self, text, releasing):
        path = os.path.join(self.tmp.name, "CHANGELOG.md")
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        real = cr.CHANGELOG
        cr.CHANGELOG = path
        try:
            cr.check_changelog("9.9.9", releasing=releasing)
        finally:
            cr.CHANGELOG = real
        return self.rules_failed()

    def test_missing_section_fails_on_every_push(self):
        self.assertIn("changelog section", self.check("# Changelog\n\n## v9.9.8 - Old\n", False))

    def test_the_bump_placeholder_passes_between_releases(self):
        # What the post-release bump writes, so main is green after a release.
        self.assertEqual(self.check(self.OPENED, False), set())

    def test_the_placeholder_cannot_be_released(self):
        self.assertIn("changelog section", self.check(self.OPENED, True))

    def test_a_titled_but_empty_section_cannot_be_released(self):
        text = "# Changelog\n\n## v9.9.9 - A title\n\n## v9.9.8 - Old\n\n- old\n"
        self.assertIn("changelog section", self.check(text, True))

    def test_a_written_section_releases(self):
        self.assertEqual(self.check(self.WRITTEN, True), set())

    def test_crlf_does_not_hide_the_placeholder(self):
        self.assertIn("changelog section",
                      self.check(self.OPENED.replace("\n", "\r\n"), True))


if __name__ == "__main__":
    unittest.main()
