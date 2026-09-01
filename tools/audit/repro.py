"""Reproduce one failing seed and print the whole FillError, not just its head."""
import io
import os
import shutil
import sys

AP = os.getcwd()
SEED = int(sys.argv[1])
STARTERS = sys.argv[2] if len(sys.argv) > 2 else "1"
os.environ.setdefault("SKIP_REQUIREMENTS_UPDATE", "1")
sys.path.insert(0, AP)

import Generate  # noqa: E402
from Main import main as ERmain  # noqa: E402

pd = os.path.join(AP, "_rp_p")
od = os.path.join(AP, "_rp_o")
for d in (pd, od):
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d, exist_ok=True)
with io.open(os.path.join(pd, "p.yaml"), "w", encoding="utf-8") as f:
    f.write("name: R1\ngame: Creeper World 4\nCreeper World 4:\n"
            f"  starter_missions: {STARTERS}\n")

sys.argv = ["Generate.py", "--player_files_path", pd, "--outputpath", od,
            "--seed", str(SEED), "--log_level", "error"]
try:
    erargs, seed = Generate.main()
    ERmain(erargs, seed)
    print("generated fine")
except Exception as e:  # noqa: BLE001
    text = str(e)
    print(f"{type(e).__name__}")
    for section in ("Unplaced items:", "Unfilled locations:", "Already placed"):
        if section in text:
            head, _, tail = text.partition(section)
            if section == "Unplaced items:":
                print("  UNPLACED:", tail.split("Unfilled locations:")[0].strip()[:900])
            elif section == "Already placed":
                block = tail.split("All Placements:")[0]
                print("  ALREADY PLACED:", block.strip()[:1200])
    # The full placement list says what the fill actually managed.
    if "All Placements:" in text:
        pl = text.split("All Placements:")[1].strip()
        print("  PLACEMENTS:", pl[:1500])
finally:
    shutil.rmtree(pd, ignore_errors=True)
    shutil.rmtree(od, ignore_errors=True)
