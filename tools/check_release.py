"""The release must not be able to lie about itself.

    py -3.13 tools/check_release.py
    py -3.13 tools/check_release.py --apworld dist/cw4.apworld --zip dist/CW4Archipelago-v0.2.0.zip

WHY THIS EXISTS. An audit of the build and release path on 2026-09-17 found that
of the thirteen validations in package-release.ps1, all five CI jobs and both
bump paths, exactly THREE touched a file that actually ships - and two of those
three checked one line of the yaml. The consequence was measurable: 41 percent of
the released .apworld was the test suite, and nobody could have known, because
nothing anywhere asserted anything about that file's contents.

WHAT IT CHECKS, and why each rule earned its place rather than being a tidy idea:

  versions agree        Four tools already check this. It moves here so there is
                        one implementation instead of an inline `sed` in ci.yml,
                        a regex in package-release.ps1 and two more elsewhere.

  ap pin matches        minimum_ap_version had exactly ONE programmatic reader
                        (package-release.ps1) while ci.yml hardcoded the same
                        number in two jobs. Raising it would have left CI
                        silently testing the old version - the exact bug ci.yml's
                        own comment boasts of having caught, back when the pin
                        was derived rather than frozen.

  changelog section     The release notes ARE the CHANGELOG section for the
                        version. Nothing read the file, so a release could ship
                        with its top section naming a different version. The
                        version bump opens the next section as "Unreleased" so
                        main always has one; packaging (--zip) refuses that
                        title, and a section with no notes in it.

  dev plugin versions   CW4Archipelago.Debug and CW4DevTools each state their
                        version twice - csproj and [BepInPlugin]. Both now read
                        the same constant, as the shipping plugin already did;
                        this rule is what keeps them that way. Neither ships, so
                        it is low severity and listed last.

  artifact rules        Only with --apworld / --zip. These are the ones the audit
                        was really about: the .apworld must declare this repo's
                        version, and the zip must actually CONTAIN the mod. That
                        last one is not hypothetical - Copy-Item with a wildcard
                        matching nothing is a silent no-op even under
                        ErrorActionPreference=Stop, so a build whose output path
                        moved would have produced an empty zip and printed
                        success.

  exact file lists      Both artifacts are compared against an ALLOWLIST, not a
                        blocklist. The blocklists ("no test/", "no *Debug.dll")
                        each caught one known leak and nothing else: the apworld
                        builder walks the filesystem, so any untracked file in
                        apworld/cw4 - a .bak, a scratch .py - would have shipped.
                        Adding a file to a release is now a deliberate edit here.

  no debug-only names   The mod's DLLs once carried counters, readbacks and
                        overrides that only the debug plugin or the tests ever
                        used (removed 2026-10-20; see docs/developing.md, "Which
                        code ships"). Their names are read out of the shipped
                        assemblies' metadata and must stay absent. A regression
                        guard for those specific names, not a general detector -
                        the rule that stops new ones is the one in developing.md.

Exit 0 when everything agrees. Every failure names the file, what it found and
what it expected.
"""
import argparse
import io
import json
import os
import re
import struct
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CSPROJ = os.path.join(REPO, "src", "CW4Archipelago", "CW4Archipelago.csproj")
PLUGIN = os.path.join(REPO, "src", "CW4Archipelago", "Plugin.cs")
MANIFEST = os.path.join(REPO, "apworld", "cw4", "archipelago.json")
CHANGELOG = os.path.join(REPO, "CHANGELOG.md")
CI = os.path.join(REPO, ".github", "workflows", "ci.yml")

# Each dev plugin states its version twice; check_dev_plugins holds the pair
# together.
DEV_PLUGINS = [
    ("CW4Archipelago.Debug", os.path.join(REPO, "src", "CW4Archipelago.Debug",
                                          "CW4Archipelago.Debug.csproj"),
     os.path.join(REPO, "src", "CW4Archipelago.Debug", "Plugin.cs")),
    ("CW4DevTools", os.path.join(REPO, "src", "CW4DevTools", "CW4DevTools.csproj"),
     os.path.join(REPO, "src", "CW4DevTools", "Plugin.cs")),
]

# Everything a player's .apworld may contain. The builder writes its own
# archipelago.json; test/, player.yaml and __pycache__ are excluded by the
# world's .apignore and Archipelago's global one.
APWORLD_FILES = {
    "cw4/__init__.py",
    "cw4/archipelago.json",
    "cw4/docs/en_Creeper World 4.md",
    "cw4/docs/setup_en.md",
    "cw4/groups.py",
    "cw4/items.py",
    "cw4/locations.py",
    "cw4/opening.py",
    "cw4/options.py",
    "cw4/regions.py",
    "cw4/roster.py",
    "cw4/rules.py",
    "cw4/span_data.py",
}

# Everything the mod zip may contain: the mod, its rules library, and the two
# NuGet assemblies the client needs. Directory entries are ignored.
ZIP_DIR = "BepInEx/plugins/CW4Archipelago/"
ZIP_FILES = {ZIP_DIR + n for n in (
    "CW4Archipelago.dll",
    "CW4Archipelago.Core.dll",
    "Archipelago.MultiClient.Net.dll",
    "Newtonsoft.Json.dll",
)}

# Names that existed in the shipped assemblies only for the debug plugin or the
# tests, and were moved out on 2026-10-20. Each is distinctive enough not to
# collide with a game or library name; generic ones (Status, Set, Aim) are left
# out on purpose rather than risk a false alarm.
DEBUG_ONLY_NAMES = {
    "TrackerDiag", "PlanetUnlockedSetPatch", "TrackerRecolours", "SetShowAll",
    "TotemPokes", "CachePokes", "CeilingOverride", "RaiseLocationsChanged",
    "SporeTargeting", "RiftLab", "AllItemNames", "UsefulCopies", "ForMission",
    "MaxRateMultiplier", "StepPerCopy", "LimitIncrements", "TryParseLimitItem",
    "MissionTitles", "ErnPerItem", "RenderedLines",
}

FAILURES = []


def fail(rule, detail):
    FAILURES.append((rule, detail))
    print("  FAIL  %-22s %s" % (rule, detail), flush=True)


def ok(rule, detail):
    print("  PASS  %-22s %s" % (rule, detail), flush=True)


def read(path):
    return io.open(path, encoding="utf-8").read()


def csproj_version(path):
    m = re.search(r"<Version>([^<]+)</Version>", read(path))
    return m.group(1) if m else None


def repo_version():
    """The one version, read the way every other tool reads it."""
    csproj = csproj_version(CSPROJ)
    m = re.search(r'const string Version = "([^"]+)"', read(PLUGIN))
    plugin = m.group(1) if m else None
    world = json.loads(read(MANIFEST)).get("world_version")
    return csproj, plugin, world


def check_versions_agree():
    csproj, plugin, world = repo_version()
    if None in (csproj, plugin, world):
        fail("versions agree", "could not read one of them: csproj=%s plugin=%s world=%s"
             % (csproj, plugin, world))
        return None
    if not (csproj == plugin == world):
        fail("versions agree",
             "csproj %s, Plugin.cs %s, archipelago.json %s" % (csproj, plugin, world))
        return None
    ok("versions agree", csproj)
    return csproj


def check_ap_pin():
    """ci.yml must test against the Archipelago the world declares.

    The pins are `ref: <version>` on the Archipelago checkout steps. Any other
    `ref:` in the file belongs to a different action and is ignored, so this
    looks only at the ones that follow a checkout of ArchipelagoMW/Archipelago.
    """
    declared = json.loads(read(MANIFEST)).get("minimum_ap_version")
    if not declared:
        fail("ap pin matches", "minimum_ap_version missing from archipelago.json")
        return
    text = read(CI)
    pins = re.findall(r"repository:\s*ArchipelagoMW/Archipelago\s*\n\s*ref:\s*([0-9.]+)", text)
    if not pins:
        fail("ap pin matches", "found no Archipelago checkout pins in ci.yml to compare")
        return
    wrong = [p for p in pins if p != declared]
    if wrong:
        fail("ap pin matches",
             "archipelago.json declares %s, ci.yml pins %s" % (declared, ", ".join(sorted(set(pins)))))
    else:
        ok("ap pin matches", "%s in archipelago.json and all %d ci.yml pin(s)" % (declared, len(pins)))


def check_changelog(version, releasing=False):
    """A section for the version must exist on every push. When RELEASING it
    must also be written: not the bump's "Unreleased" placeholder, and not
    empty, because it is pasted as the release notes."""
    if version is None:
        return
    text = read(CHANGELOG)
    m = re.search(r"^##\s+v%s\b(.*)$" % re.escape(version), text, re.M)
    if not m:
        fail("changelog section",
             "no '## v%s' heading in CHANGELOG.md - the release notes are that section"
             % version)
        return
    if not releasing:
        ok("changelog section", "## v%s present" % version)
        return
    title = m.group(1).strip().strip("-").strip()
    rest = text[m.end():]
    nxt = re.search(r"^## ", rest, re.M)
    body = (rest[:nxt.start()] if nxt else rest).strip()
    if not title or title.lower() == "unreleased":
        fail("changelog section",
             "## v%s is still titled '%s' - give the release its title" % (version, title or ""))
    elif not body:
        fail("changelog section", "## v%s has no notes under it - they are the release notes" % version)
    else:
        ok("changelog section", "## v%s - %s (%d lines of notes)" % (version, title, len(body.splitlines())))


def check_dev_plugins():
    for name, csproj, plugin in DEV_PLUGINS:
        if not (os.path.exists(csproj) and os.path.exists(plugin)):
            fail("dev plugin versions", "%s: missing csproj or Plugin.cs" % name)
            continue
        declared = csproj_version(csproj)
        m = re.search(r"\[BepInPlugin\(\s*\"[^\"]*\"\s*,\s*\"[^\"]*\"\s*,\s*([^)]+)\)\]",
                      read(plugin))
        if not m:
            fail("dev plugin versions", "%s: no [BepInPlugin] found" % name)
            continue
        third = m.group(1).strip()
        # A bare literal must equal the csproj. A reference (the shipping
        # plugin's fix) cannot drift and is accepted as-is.
        if third.startswith('"'):
            literal = third.strip('"')
            if literal != declared:
                fail("dev plugin versions",
                     "%s: csproj %s but [BepInPlugin] literal %s" % (name, declared, literal))
            else:
                ok("dev plugin versions", "%s %s (literal, agrees)" % (name, declared))
        else:
            ok("dev plugin versions", "%s uses %s - cannot drift" % (name, third))


def check_apworld(path, version):
    """The .apworld is what a player installs. Until this existed, nothing
    anywhere asserted a single thing about it."""
    if not os.path.exists(path):
        fail("apworld contents", "no such file: %s" % path)
        return
    with zipfile.ZipFile(path) as zf:
        names = zf.namelist()
        files = {n for n in names if not n.endswith("/")}
        extra = sorted(files - APWORLD_FILES)
        missing = sorted(APWORLD_FILES - files)
        if extra:
            fail("apworld contents",
                 "%d file(s) not on the allowlist, e.g. %s - exclude it in "
                 "apworld/cw4/.apignore, or add it to APWORLD_FILES if it "
                 "really ships" % (len(extra), extra[0]))
        if missing:
            fail("apworld contents", "missing from the artifact: %s" % ", ".join(missing))
        if not extra and not missing:
            ok("apworld contents", "exactly the %d allowed files" % len(files))

        manifest = [n for n in names if n.endswith("archipelago.json")]
        if not manifest:
            fail("apworld manifest", "no archipelago.json inside the artifact")
            return
        data = json.loads(zf.read(manifest[0]))
        shipped = data.get("world_version")
        if version and shipped != version:
            fail("apworld manifest",
                 "artifact declares world_version %s, repo declares %s - the "
                 "artifact is stale" % (shipped, version))
        else:
            ok("apworld manifest", "world_version %s" % shipped)


def check_zip(path):
    """The zip must contain exactly the mod - not merely lack the debug
    assembly - and the mod must carry none of the debug-only names."""
    if not os.path.exists(path):
        fail("zip contents", "no such file: %s" % path)
        return
    with zipfile.ZipFile(path) as zf:
        names = zf.namelist()
        files = {n for n in names if not n.endswith("/")}
        extra = sorted(files - ZIP_FILES)
        missing = sorted(ZIP_FILES - files)
        if extra:
            fail("zip contents", "not on the allowlist: %s" % ", ".join(extra))
        if missing:
            fail("zip contents", "missing from the zip: %s" % ", ".join(missing))
        if not extra and not missing:
            ok("zip contents", "exactly the %d allowed assemblies" % len(files))

        for dll in ("CW4Archipelago.dll", "CW4Archipelago.Core.dll"):
            entry = ZIP_DIR + dll
            if entry in files:
                check_assembly_names(dll, zf.read(entry))


def metadata_strings(data):
    """Every identifier in a .NET assembly's #Strings heap: type, member,
    namespace and assembly-reference names.

    Read straight from the PE file, so it needs no .NET runtime and runs the
    same in CI as on Windows. Raises ValueError for anything that is not a
    managed assembly."""
    def u16(o):
        return struct.unpack_from("<H", data, o)[0]

    def u32(o):
        return struct.unpack_from("<I", data, o)[0]

    if data[:2] != b"MZ":
        raise ValueError("not a PE file")
    pe = u32(0x3C)
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("no PE signature")
    coff = pe + 4
    sections, opt_size = u16(coff + 2), u16(coff + 16)
    opt = coff + 20
    dirs = opt + (96 if u16(opt) == 0x10B else 112)
    cli_rva = u32(dirs + 14 * 8)
    if not cli_rva:
        raise ValueError("no CLI header - not a managed assembly")

    spans = []
    for i in range(sections):
        sh = opt + opt_size + i * 40
        spans.append((u32(sh + 12), u32(sh + 8), u32(sh + 20)))

    def offset(rva):
        for va, size, raw in spans:
            if va <= rva < va + size:
                return raw + rva - va
        raise ValueError("RVA 0x%x is in no section" % rva)

    root = offset(u32(offset(cli_rva) + 8))
    if u32(root) != 0x424A5342:
        raise ValueError("bad metadata signature")
    p = root + 16 + u32(root + 12) + 2
    streams = u16(p)
    p += 2
    for _ in range(streams):
        off, size = u32(p), u32(p + 4)
        end = data.index(b"\0", p + 8)
        name = data[p + 8:end].decode("ascii")
        p = (end + 4) & ~3
        if name == "#Strings":
            heap = data[root + off:root + off + size]
            return {s.decode("utf-8", "replace") for s in heap.split(b"\0") if s}
    raise ValueError("no #Strings heap")


def check_assembly_names(label, data):
    try:
        names = metadata_strings(data)
    except ValueError as e:
        fail("no debug-only names", "%s: cannot read metadata (%s)" % (label, e))
        return
    # Whole-word, not whole-string: the compiler merges a string into the tail
    # of a longer one, so a property's own name can exist only as the end of
    # its getter's ("get_RenderedLines"), and a generated name wraps it
    # ("<AllItemNames>d__12").
    found = sorted(n for n in DEBUG_ONLY_NAMES
                   if any(re.search(r"(?<![A-Za-z0-9])%s(?![A-Za-z0-9])" % re.escape(n), s)
                          for s in names))
    if found:
        fail("no debug-only names",
             "%s carries %s - debug and test code belongs in "
             "src/CW4Archipelago.Debug or the test projects" % (label, ", ".join(found)))
    else:
        ok("no debug-only names", "%s (%d names read)" % (label, len(names)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apworld", help="built cw4.apworld to inspect")
    ap.add_argument("--zip", dest="zip_path", help="built mod zip to inspect")
    args = ap.parse_args()

    print("check-release: source rules", flush=True)
    version = check_versions_agree()
    check_ap_pin()
    check_changelog(version, releasing=bool(args.zip_path))
    check_dev_plugins()

    if args.apworld or args.zip_path:
        print("check-release: artifact rules", flush=True)
        if args.apworld:
            check_apworld(args.apworld, version)
        if args.zip_path:
            check_zip(args.zip_path)
    else:
        print("  NOTE  artifact rules skipped - pass --apworld and/or --zip", flush=True)

    print("Done: %d failure(s)" % len(FAILURES), flush=True)
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
