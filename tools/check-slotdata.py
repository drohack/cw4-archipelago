"""Print a slot_data key out of a generated .archipelago multidata.

Usage (run from inside the Archipelago clone, which owns the pickle's classes):
    python ../tools/check-slotdata.py <multidata> required_objectives story2

Exists because a harness reused whatever seed was newest in .aptest/server/ and
that one predated `required_objectives`. The key was simply absent, so the
feature under test could not fire and the run reported a product failure while
measuring a seed incapable of exercising it. A harness that depends on a slot
data field should assert the field is there first.

Exits 0 and prints the value when present, exits 1 when not.
"""
import os
import pickle
import sys
import zlib

# The multidata pickle references Archipelago's own classes (NetUtils and
# friends), so the clone has to be importable. Running from inside it is not
# enough on its own: the working directory is not on sys.path for a script
# invoked by absolute path.
sys.path.insert(0, os.getcwd())


def main() -> int:
    if len(sys.argv) < 3:
        print("usage: check-slotdata.py <multidata> <key> [subkey]")
        return 2
    path, key = sys.argv[1], sys.argv[2]
    subkey = sys.argv[3] if len(sys.argv) > 3 else None

    with open(path, "rb") as fh:
        raw = fh.read()
    # Multidata is a one-byte version prefix followed by a zlib'd pickle.
    data = pickle.loads(zlib.decompress(raw[1:]))

    for slot, slot_data in (data.get("slot_data") or {}).items():
        if not isinstance(slot_data, dict):
            continue
        if key not in slot_data:
            continue
        value = slot_data[key]
        if subkey is not None:
            if not isinstance(value, dict) or subkey not in value:
                continue
            value = value[subkey]
        print(f"OK slot={slot} {key}" + (f"[{subkey}]" if subkey else "") + f"={value}")
        return 0

    print(f"MISSING {key}" + (f"[{subkey}]" if subkey else ""))
    return 1


if __name__ == "__main__":
    sys.exit(main())
