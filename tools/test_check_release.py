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


if __name__ == "__main__":
    unittest.main()
