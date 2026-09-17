"""Print a generated seed's mission roster as "specifier<TAB>title".

    python ../tools/span_roster.py <multidata>

Run from inside the Archipelago clone, which owns the pickle's classes -
running from inside it is not enough on its own, since the working directory is
not on sys.path for a script invoked by absolute path.

Exists because tools/span-e2e-test.sh has to assert against what the SEED
actually drew. A harness that hard-codes a mission name measures whichever seed
happens to contain it and silently passes on every other one.
"""
import os
import pickle
import sys
import zlib

sys.path.insert(0, os.getcwd())


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: span_roster.py <multidata>", file=sys.stderr)
        return 2
    with open(sys.argv[1], "rb") as fh:
        raw = fh.read()
    # Multidata is a one-byte version prefix followed by a zlib'd pickle.
    data = pickle.loads(zlib.decompress(raw[1:]))

    for _slot, slot_data in (data.get("slot_data") or {}).items():
        if not isinstance(slot_data, dict) or "mission_roster" not in slot_data:
            continue
        titles = slot_data.get("mission_titles") or {}
        for specifier in slot_data["mission_roster"]:
            print("%s\t%s" % (specifier, titles.get(specifier, "")))
        return 0

    print("no mission_roster in any slot's slot_data - the seed predates it, "
          "or span_missions was not set", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
