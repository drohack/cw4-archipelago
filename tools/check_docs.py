"""The documentation and the comments must not lie, and nothing else checks them.

    py -3.13 tools/check_docs.py

WHY THIS EXISTS. An audit on 2026-09-17 found that this repo has exactly ONE
document that cannot lie - docs/randomizer-logic.md, because
tools/audit/logictable.py regenerates it from rules.py. Everything else is prose
that passes CI, passes compliance and passes the release gate no matter what it
claims. What that allowed, all of it live at the time:

  - docs/installation.md published three WRONG option defaults, and the
    arithmetic derived from them. It is the page a player reads before
    generating a seed.
  - Five separate comments and docs cited a test BY NAME as their proof. Not one
    of those five tests existed, and three of them propped up a claim the real
    test contradicts.
  - The comment on OWN_FILL_SOLO_ONLY described the opposite of the constant
    beneath it, and had since the commit that introduced it.

WHAT IT CHECKS, and why each rule earned its place rather than being a tidy idea:

1. Every relative markdown link resolves. Two forms that look broken on disk are
   allowed because both are correct where they are actually rendered: GitHub's
   `../../releases` from the repo-root README, and the Archipelago webhost's
   `../player-options` from a world's docs folder.

2. EVERY TEST NAME CITED IN A COMMENT OR A DOC RESOLVES. This is the rule the
   audit most wanted: five dead citations, and a reader trusting any of them
   believes a guardrail exists that does not. Only names in citing form are
   matched - a snake_case word carrying the test prefix, or a CamelCase one -
   and each is checked against every def/class in the two test suites. (Spelling
   those two forms out as examples here would cite two tests that do not exist,
   and this rule would rightly fail its own file.)

3. No `file.ext:NNN` line citation in a source comment. BANNED rather than
   re-resolved: a checker that merely verifies the numbers goes green the moment
   someone corrects them, and they rot again on the next edit. Name the symbol
   or the heading instead - those move with the thing they name. (Citations into
   the vendored Archipelago/ tree are the worst case, since it is gitignored and
   unpinned, so nothing here could verify them even in principle.)

4. No C# member carries two <summary> elements. It is invalid doc XML - only the
   first binds - and it happens mechanically when a member is inserted and the
   doc comment above it is left on whatever is now underneath.

5. Every generated file declares its generator in its header. Three of the four
   generated docs did; docs/design/span-requirements-worksheet.md did not, and
   it is the one players are told to fill in, so the next generator run would
   silently destroy their work.

6. Option defaults quoted in prose match options.py. This is the one that
   reaches players.

Exit 0 when everything agrees. Every failure names the file, the line, what was
found and what was expected.
"""
import ast
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP_DIRS = {".git", "Archipelago", "bin", "obj", "__pycache__", ".aptest",
             "dist", "node_modules"}

# Rendered elsewhere than on disk, and correct there.
LINK_ALLOW = ("../../releases", "../player-options")

# ARCHIPELAGO'S OWN TESTS, which several docs legitimately cite. They live in the
# vendored Archipelago/ tree, which is gitignored and may be absent entirely, so
# they are listed rather than discovered - a rule that silently passes when a
# directory is missing is worse than one that needs a list kept.
UPSTREAM_TESTS = {
    "test_ids", "test_names", "test_reachability", "test_fill", "test_options",
    "test_world_manifest", "test_itempool_not_modified", "test_all_state_can_reach_everything",
    "test_no_failed_world_loads", "test_entrance_connections",
}

FAILURES = []


def fail(rule, detail):
    FAILURES.append((rule, detail))
    print("  FAIL  %-20s %s" % (rule, detail), flush=True)


def ok(rule, detail):
    print("  PASS  %-20s %s" % (rule, detail), flush=True)


def walk(exts):
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if name.endswith(exts):
                yield os.path.join(root, name)


def read(path):
    return io.open(path, encoding="utf-8", errors="replace").read()


def rel(path):
    return os.path.relpath(path, REPO).replace("\\", "/")


# ---------------------------------------------------------------- rule 1
LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")


def check_links():
    bad = 0
    total = 0
    for path in walk((".md",)):
        text = read(path)
        for target in LINK.findall(text):
            target = target.split("#")[0].strip()
            if not target or target.startswith(("http://", "https://", "mailto:")):
                continue
            if target in LINK_ALLOW:
                continue
            total += 1
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), target))
            if not os.path.exists(resolved):
                fail("links resolve", "%s -> %s does not exist" % (rel(path), target))
                bad += 1
    if not bad:
        ok("links resolve", "%d relative link(s)" % total)


# ---------------------------------------------------------------- rule 2
TEST_NAME = re.compile(r"\b(test_[a-z0-9_]+|Test[A-Z][A-Za-z0-9]*)\b")


def known_test_names():
    names = set()
    for path in walk((".py", ".cs")):
        r = rel(path)
        if "/test" not in r.lower() and not r.endswith("Tests.cs"):
            continue
        for m in re.finditer(r"(?:def|class)\s+(test_[a-z0-9_]+|Test[A-Za-z0-9]*)",
                             read(path)):
            names.add(m.group(1))
    return names


def docstring_lines(text):
    """Line numbers covered by a DOCSTRING, or by any bare string statement.

    Found the hard way: the first version of this checker scanned only lines
    beginning with `#`, and two deliberate breaks planted in module docstrings
    went through it green. This repo puts most of its prose in docstrings -
    rules.py and items.py open with several hundred words each - so a rule that
    cannot see them is a rule that cannot see where the rot actually lives.

    ast rather than a quote-counting scan: it distinguishes a docstring from a
    string that is DATA, and only the first is prose to be held to account.
    A file that does not parse contributes nothing rather than failing the run -
    a syntax error is not this checker's business to report.
    """
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return set()
    covered = set()
    for node in ast.walk(tree):
        if (isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant)
                and isinstance(node.value.value, str)):
            end = getattr(node, "end_lineno", node.lineno) or node.lineno
            covered.update(range(node.lineno, end + 1))
    return covered


def comment_and_doc_text(path):
    """The parts of a file where a citation can appear: comments and docstrings,
    or all of a .md."""
    text = read(path)
    if path.endswith(".md"):
        return [(i + 1, line) for i, line in enumerate(text.split("\n"))]
    docs = docstring_lines(text) if path.endswith(".py") else set()
    out = []
    for i, line in enumerate(text.split("\n")):
        stripped = line.lstrip()
        if (stripped.startswith("#") or stripped.startswith("//")
                or stripped.startswith("///") or (i + 1) in docs):
            out.append((i + 1, line))
    return out


def check_test_citations():
    known = known_test_names()
    if len(known) < 50:
        fail("test citations", "only found %d test names - the scan is broken, "
                               "not the repo" % len(known))
        return
    bad = 0
    checked = 0
    for path in walk((".py", ".cs", ".md")):
        r = rel(path)
        if "/test" in r.lower() or r.endswith("Tests.cs"):
            continue          # a test file naming its own neighbours
        for lineno, line in comment_and_doc_text(path):
            for name in TEST_NAME.findall(line):
                # "Test" alone, or a class-shaped word that is not a test.
                if name in ("TestBase", "Test") or name in UPSTREAM_TESTS:
                    continue
                checked += 1
                if name not in known:
                    fail("test citations",
                         "%s:%d cites %s, which does not exist" % (r, lineno, name))
                    bad += 1
    if not bad:
        ok("test citations", "%d citation(s), all resolve" % checked)


# ---------------------------------------------------------------- rule 3
# A citation into a source file, in a comment. Deliberately narrow: an extension
# followed by a colon and digits.
FILE_LINE = re.compile(r"\b[\w./-]+\.(?:py|cs|md|ps1|sh|yml|json|csproj):\d+")


def check_file_line_citations():
    bad = 0
    for path in walk((".py", ".cs")):
        r = rel(path)
        for lineno, line in comment_and_doc_text(path):
            for hit in FILE_LINE.findall(line):
                fail("no file:line",
                     "%s:%d cites %s - name the symbol instead, line numbers rot"
                     % (r, lineno, hit))
                bad += 1
    if not bad:
        ok("no file:line", "no line-number citations in source comments")


# ---------------------------------------------------------------- rule 4
def check_double_summary():
    bad = 0
    for path in walk((".cs",)):
        lines = read(path).split("\n")
        opens = 0
        start = 0
        for i, line in enumerate(lines):
            if "<summary>" in line:
                if opens == 0:
                    start = i + 1
                opens += 1
            if "</summary>" in line:
                continue
            # A non-comment, non-blank line ends the doc block.
            stripped = line.strip()
            if stripped and not stripped.startswith("///"):
                if opens > 1:
                    fail("one summary each",
                         "%s:%d has %d <summary> elements on one member - "
                         "only the first binds" % (rel(path), start, opens))
                    bad += 1
                opens = 0
    if not bad:
        ok("one summary each", "no member carries two <summary> elements")


# ---------------------------------------------------------------- rule 5
# SEVEN artifacts, not four. The rule started on the generated DOCS because that
# is where a hand-edit gets silently destroyed, but three generated CODE and DATA
# files have exactly the same contract and had no gate at all: MapCells.cs is
# compiled and span_data.py ships. All three already declared their generator
# correctly, so the gate was free to add - and it is what would catch a rename
# campaign that missed one of them.
GENERATORS = {
    "docs/randomizer-logic.md": "tools/audit/logictable.py",
    "docs/design/span-requirements.md": "tools/gen_spanreqs.py",
    "docs/design/span-survey.md": "tools/gen_spantable.py",
    "docs/design/span-requirements-worksheet.md": "tools/gen_spanworksheet.py",
    "src/CW4Archipelago.Core/MapCells.cs": "tools/gen_mapcells.py",
    "src/CW4Archipelago.Core/SpanMissionTable.g.cs": "tools/gen_spancsharp.py",
    "apworld/cw4/span_data.py": "tools/gen_spandata.py",
    ".github/ISSUE_TEMPLATE/span-map-report.yml": "tools/gen_spanworksheet.py",
}


def check_generated_declared():
    bad = 0
    for doc, generator in GENERATORS.items():
        path = os.path.join(REPO, doc)
        if not os.path.exists(path):
            fail("generated declared", "%s is listed as generated but is missing" % doc)
            bad += 1
            continue
        head = "\n".join(read(path).split("\n")[:12])
        if os.path.basename(generator) not in head:
            fail("generated declared",
                 "%s does not name %s in its first 12 lines - a hand-edit would "
                 "be destroyed by the next run" % (doc, generator))
            bad += 1
    if not bad:
        ok("generated declared", "%d generated doc(s) name their generator" % len(GENERATORS))


# ---------------------------------------------------------------- rule 6
OPTION_ROW = re.compile(r"^\|\s*`(\w+)`\s*\|\s*([^|]+?)\s*\|", re.M)


def option_defaults():
    """name -> default, read from the option classes' `default =` lines."""
    text = read(os.path.join(REPO, "apworld", "cw4", "options.py"))
    classes = dict(re.findall(r"^class (\w+)\(", text, re.M) and [] or [])
    out = {}
    # Map dataclass field -> class, then class -> default.
    fields = re.findall(r"^\s{4}(\w+):\s*(\w+)", text, re.M)
    defaults = {}
    for m in re.finditer(r"^class (\w+)\(.*?\n(.*?)(?=\n class |\nclass |\Z)", text,
                         re.M | re.S):
        d = re.search(r"^\s+default = (.+)$", m.group(2), re.M)
        if d:
            defaults[m.group(1)] = d.group(1).strip()
    for field, cls in fields:
        if cls in defaults:
            out[field] = defaults[cls]
    return out


def check_option_defaults():
    defaults = option_defaults()
    if len(defaults) < 15:
        fail("option defaults", "only parsed %d defaults - the scan is broken" % len(defaults))
        return
    doc = os.path.join(REPO, "docs", "installation.md")
    text = read(doc)
    bad = 0
    checked = 0
    for name, stated in OPTION_ROW.findall(text):
        if name not in defaults:
            continue
        real = defaults[name].strip('"').strip("'")
        stated = stated.strip()
        checked += 1
        # Ranges and Toggles render as words; compare only where both are numeric.
        if stated.isdigit() and real.isdigit() and stated != real:
            fail("option defaults",
                 "installation.md says %s defaults to %s, options.py says %s"
                 % (name, stated, real))
            bad += 1
    if not bad:
        ok("option defaults", "%d numeric default(s) agree with options.py" % checked)


# ---------------------------------------------------------------- rules 7, 8
# A PATH NAMED IN PROSE IS A CLAIM, and nothing checked one. Rule 1 validates
# markdown LINKS, of which there are 37; there are 236 backticked slash-bearing
# tokens across 21 documents, and a rename campaign rots every one of them
# silently. Four were already wrong when this rule was written.
#
# ONLY THESE PREFIXES ARE CLAIMS ABOUT THIS REPO. That single decision disposes
# of three whole classes of false positive with no allow-list at all: game paths
# (BepInEx/..., saves/...), upstream Archipelago paths (worlds/, Players/,
# custom_worlds/), and the eight third-party GitHub repos named in the feature
# comparison.
REPO_PREFIXES = ("tools/", "docs/", "src/", "apworld/", ".github/")

# Paths that LOOK like ours and belong to the vendored Archipelago clone, which
# is gitignored and may be absent. Listed rather than discovered, for the same
# reason as UPSTREAM_TESTS above.
UPSTREAM_PATHS = {
    "docs/apworld_dev_faq.md", "docs/tests.md", "docs/world api.md",
}

# DELIBERATELY ABSENT FROM A CLONE. src/GameDir.props is per-machine and
# gitignored; three documents name it precisely because you have to create it.
#
# Found the way it should be: the first version of this rule passed on the
# working tree, where the file exists, and failed on a `git archive` export of
# the same commit - which is what CI checks out. Verifying a path rule against
# a tree that carries gitignored files proves nothing about a clone, so the
# export is now part of running it.
GITIGNORED_PATHS = {
    "src/GameDir.props",
}

# A CONVENTION, not an escape hatch: A LIVE THING IS NAMED BY REPO-RELATIVE
# PATH, A RETIRED THING BY BASENAME. docs/ern-upgrade-measurements.md already
# writes its eleven deleted harnesses as bare names and says why - they are
# "the record of HOW each number was taken, not paths to open". So the prefix
# rule exempts every obituary for free, and the rule stays enforceable because a
# writer has something to follow. If a genuine case ever needs a prefixed dead
# path it goes here, with a reason. It starts empty.
HISTORICAL_PATHS = set()

# Placeholders, not paths: anything carrying a substitution, a bracket or a space.
PLACEHOLDER = re.compile(r"[{}<>$%\s]")
BACKTICKED = re.compile(r"`([^`\n]+)`")
# Bare paths in source comments, which do not use backticks consistently.
BARE_PATH = re.compile(
    r"\b((?:tools|docs|src|apworld)/[\w./-]+\.(?:py|sh|ps1|cs|md|yml|json|csproj))")


def path_claim_is_good(cand):
    """None when the candidate is not a claim at all; else whether it resolves."""
    if not cand.startswith(REPO_PREFIXES):
        return None
    if cand in UPSTREAM_PATHS or cand in HISTORICAL_PATHS or cand in GITIGNORED_PATHS:
        return None
    if PLACEHOLDER.search(cand):
        return None
    target = cand.rstrip("/")
    if "*" in target or "?" in target:
        # Globs are RESOLVED rather than skipped, so `tools/*.sh` stays a real
        # claim and would fail if tools/ ever lost its harnesses.
        import glob
        return bool(glob.glob(os.path.join(REPO, target)))
    return os.path.exists(os.path.join(REPO, target))


def strip_fences(text):
    """The markdown outside fenced code blocks, as a list of lines.

    Fenced blocks are example commands and sample output, full of paths that are
    inputs and outputs rather than repo files. They are the single largest source
    of false positives, so they are removed wholesale rather than allow-listed.
    Lines are blanked rather than dropped, so line numbers stay true.
    """
    out, fenced = [], False
    for line in text.split("\n"):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            out.append("")
            continue
        out.append("" if fenced else line)
    return out


def check_doc_paths():
    bad = checked = 0
    for path in walk((".md",)):
        r = rel(path)
        # A changelog records what SHIPPED. Rewriting a path there to a name
        # introduced later makes it describe something that never existed under
        # that version, so it is history rather than a claim about the tree.
        if r == "CHANGELOG.md":
            continue
        for lineno, line in enumerate(strip_fences(read(path)), 1):
            for cand in BACKTICKED.findall(line):
                good = path_claim_is_good(cand.strip())
                if good is None:
                    continue
                checked += 1
                if not good:
                    fail("doc paths", "%s:%d names %s, which does not exist"
                         % (r, lineno, cand.strip()))
                    bad += 1
    if checked < 110:
        fail("doc paths", "only found %d path citations - the scan is broken, "
                          "not the repo" % checked)
    elif not bad:
        ok("doc paths", "%d path(s) named in prose all resolve" % checked)


def check_comment_paths():
    bad = checked = 0
    for path in walk((".py", ".cs", ".sh", ".ps1")):
        r = rel(path)
        for lineno, line in comment_and_doc_text(path):
            for cand in BARE_PATH.findall(line):
                good = path_claim_is_good(cand)
                if good is None:
                    continue
                checked += 1
                if not good:
                    fail("comment paths", "%s:%d names %s, which does not exist"
                         % (r, lineno, cand))
                    bad += 1
    if checked < 50:
        fail("comment paths", "only found %d path citations - the scan is "
                              "broken, not the repo" % checked)
    elif not bad:
        ok("comment paths", "%d path(s) named in comments all resolve" % checked)


def main():
    print("check-docs: %s" % REPO, flush=True)
    check_links()
    check_test_citations()
    check_file_line_citations()
    check_double_summary()
    check_generated_declared()
    check_option_defaults()
    check_doc_paths()
    check_comment_paths()
    print("Done: %d failure(s)" % len(FAILURES), flush=True)
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
