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
                        with its top section naming a different version.

  dev plugin versions   CW4Archipelago.Debug and CW4DevTools each state their
                        version twice - csproj and [BepInPlugin]. Both now read
                        the same constant, as the shipping plugin already did;
                        this rule is what keeps them that way. Neither ships, so
                        it is low severity and listed last.

  artifact rules        Only with --apworld / --zip. These are the ones the audit
                        was really about: the .apworld must carry no test/ and
                        must declare this repo's version, and the zip must
                        actually CONTAIN the mod. That last one is not
                        hypothetical - package-release.ps1's only zip assertion
                        is a one-pattern blocklist, and Copy-Item with a wildcard
                        matching nothing is a silent no-op even under
                        ErrorActionPreference=Stop, so a build whose output path
                        moved would have produced an empty zip, passed the
                        blocklist and printed success.

Exit 0 when everything agrees. Every failure names the file, what it found and
what it expected.
"""
import argparse
import io
import json
import os
import re
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CSPROJ = os.path.join(REPO, "src", "CW4Archipelago", "CW4Archipelago.csproj")
PLUGIN = os.path.join(REPO, "src", "CW4Archipelago", "Plugin.cs")
MANIFEST = os.path.join(REPO, "apworld", "cw4", "archipelago.json")
CHANGELOG = os.path.join(REPO, "CHANGELOG.md")
CI = os.path.join(REPO, ".github", "workflows", "ci.yml")

# Each dev plugin states its version twice and nothing checks the pair.
DEV_PLUGINS = [
    ("CW4Archipelago.Debug", os.path.join(REPO, "src", "CW4Archipelago.Debug",
                                          "CW4Archipelago.Debug.csproj"),
     os.path.join(REPO, "src", "CW4Archipelago.Debug", "Plugin.cs")),
    ("CW4DevTools", os.path.join(REPO, "src", "CW4DevTools", "CW4DevTools.csproj"),
     os.path.join(REPO, "src", "CW4DevTools", "Plugin.cs")),
]

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


def check_changelog(version):
    if version is None:
        return
    text = read(CHANGELOG)
    if re.search(r"^##\s+v%s\b" % re.escape(version), text, re.M):
        ok("changelog section", "## v%s present" % version)
    else:
        fail("changelog section",
             "no '## v%s' heading in CHANGELOG.md - the release notes are that section"
             % version)


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
        tests = [n for n in names if re.search(r"(^|/)test/", n)]
        if tests:
            fail("apworld contents",
                 "%d test file(s) inside the artifact, e.g. %s - add them to "
                 "apworld/cw4/.apignore" % (len(tests), tests[0]))
        else:
            ok("apworld contents", "%d files, no test/" % len(names))

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
    """The zip must CONTAIN the mod, not merely lack the debug assembly."""
    if not os.path.exists(path):
        fail("zip contents", "no such file: %s" % path)
        return
    with zipfile.ZipFile(path) as zf:
        names = zf.namelist()
    required = ["CW4Archipelago.dll", "CW4Archipelago.Core.dll",
                "Archipelago.MultiClient.Net.dll"]
    missing = [r for r in required if not any(n.endswith(r) for n in names)]
    if missing:
        fail("zip contents", "missing from the zip: %s" % ", ".join(missing))
    else:
        ok("zip contents", "%d entries, all required assemblies present" % len(names))
    leaked = [n for n in names if n.endswith("Debug.dll")]
    if leaked:
        fail("zip contents", "debug assembly leaked: %s" % ", ".join(leaked))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apworld", help="built cw4.apworld to inspect")
    ap.add_argument("--zip", dest="zip_path", help="built mod zip to inspect")
    args = ap.parse_args()

    print("check-release: source rules", flush=True)
    version = check_versions_agree()
    check_ap_pin()
    check_changelog(version)
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
