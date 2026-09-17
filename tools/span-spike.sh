#!/usr/bin/env bash
# Phase 0 spike: can the SPAN Experiments roster be enumerated from a script?
#
# SPAN maps are not storyN-addressed. data.unity3d is compressed, so the level
# list cannot be extracted offline, and nothing in this repo has ever touched
# SPAN. Before any randomizer work is planned around them, one question has to
# be answered: can a script list them and read each one's identity?
#
# The grid itself knows. SpanTile carries (x, y, page), a difficulty grade and
# one indicator GameObject per objective type, and GalaxyMissionPanel.gmd
# carries the title/specifier/guid a boot would need - so the roster should be
# readable from the MENU without booting 26 maps.
#
# Runs on the DEV TOOLS channel with the randomizer parked, for two reasons:
# the randomizer hides the SPAN button (ModConfig.ShowSpan defaults false), and
# its UnitGate rewrites the availability flags every frame, which is what the
# later build-list survey needs out of the way.
#
# Usage: tools/span-spike.sh          (game must be CLOSED)
set -u

G="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
LOG="$G/BepInEx/LogOutput.log"
CMD="$G/BepInEx/cw4dev-commands.txt"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$REPO/.aptest/span-spike.txt"

send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }

# Park the randomizer and restore it however we exit. Renaming does NOT work -
# BepInEx scans subfolders recursively, so it must leave plugins/ entirely.
# Both halves go: CW4ApDebug hard-depends on the mod, so parking the mod alone
# leaves a missing-dependency error in the log.
PLUGINS="$G/BepInEx/plugins"
PARKED="$G/BepInEx/plugins-disabled"
RANDOMIZER_DIRS="CW4Archipelago CW4ApDebug"
RESTORE_RANDOMIZER=0
restore_randomizer() {
  [ "$RESTORE_RANDOMIZER" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in $RANDOMIZER_DIRS; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored to plugins/)"
}
trap restore_randomizer EXIT
for d in $RANDOMIZER_DIRS; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"
    rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE_RANDOMIZER=1
  fi
done
[ "$RESTORE_RANDOMIZER" = "1" ] && echo "  (randomizer parked; restored at the end)"

echo "== setup =="
taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
mkdir -p "$(dirname "$OUT")"
rm -f "$LOG" "$CMD" "$OUT"

( cd "$G" && ./CW4.exe >/dev/null 2>&1 & )

echo "== waiting for the dev tools to load =="
ok=1
for i in $(seq 1 120); do
  grep -q "Dev Tools loaded" "$LOG" 2>/dev/null && { ok=0; break; }
  sleep 2
done
if [ "$ok" != "0" ]; then echo "FAIL: dev tools never loaded"; exit 1; fi
echo "  dev tools loaded"

# The randomizer must genuinely be absent, or the span button is hidden and the
# whole run measures the wrong thing.
if grep -q "Loading \[CW4 Archipelago" "$LOG"; then
  echo "FAIL: the randomizer is still loaded - the SPAN button will be hidden"
  exit 1
fi
echo "  randomizer absent, as required"

# A baseline BEFORE opening anything. If the grid object and its tiles look the
# same here as they do afterwards, the button did nothing and that is the
# finding - without this, "no tiles" is ambiguous.
echo "== baseline (main menu, nothing opened) =="
send "span:list" 5
base=$(grep -c "DEVSPANTILE " "$LOG" 2>/dev/null || true)
echo "  tiles visible before opening: ${base:-0}"

echo "== opening the SPAN grid =="
opened=1
for i in $(seq 1 15); do
  send "span:open" 4
  if grep -q "DEVCMD span:open$" "$LOG"; then opened=0; break; fi
  if grep -q "span:open: the span button is HIDDEN" "$LOG"; then
    echo "FAIL: span button hidden - the randomizer config is in play"; exit 1
  fi
done
if [ "$opened" != "0" ]; then
  echo "FAIL: could not open the SPAN grid in 15 tries"
  grep -E "DEVCMD span:open" "$LOG" | tail -3
  exit 1
fi
echo "  SPAN button clicked"

# Retry rather than sleeping once. The first run found the grid object but no
# instantiated tiles, which can mean the grid had not populated yet OR that the
# button lands on a system chooser with the grid one level deeper. Retrying
# tells those apart without a longer blind sleep.
echo "== enumerating =="
for i in $(seq 1 6); do
  send "span:list" 5
  n=$(grep -c "DEVSPANTILE " "$LOG" 2>/dev/null || true)
  echo "  attempt $i: ${n:-0} tile line(s) in the log so far"
  [ "${n:-0}" -gt $(( ${base:-0} + 3 )) ] && break
done

# A log is not an observation - see docs/in-game-testing.md.
send "planets:dump" 6
send "shot:$G/span-screen.png" 10

# THE EXIT CRITERION: one SPAN map booted unattended. Enumeration alone proves
# the roster is readable; it does not prove the maps can be loaded, and the
# existing Farsite boot arguments are known to be wrong for SPAN.
echo "== booting one SPAN map =="
BOOTGUID="${SPAN_BOOT_GUID:-knucracker1}"
send "span:boot $BOOTGUID" 6
# Probe with obj:dump rather than waiting for "New GameSpace" - that line comes
# from the randomizer's UnitGate, which this harness deliberately parks, so it
# can never appear here. obj:dump prints DEVOBJ only when a mission is live.
booted=1
for i in $(seq 1 20); do
  sleep 3
  send "obj:dump" 3
  if grep -q "DEVOBJ mission=" "$LOG" 2>/dev/null; then booted=0; break; fi
done
if [ "$booted" = "0" ]; then
  echo "  BOOTED: $BOOTGUID is live"
  send "ada:close" 3
  send "obj:dump" 4
  send "counts:dump" 4
  send "dump" 5
  send "shot:$G/span-boot.png" 10
else
  echo "  NOT BOOTED: obj:dump never reported a live mission after span:boot $BOOTGUID"
fi
grep -E "DEVSPANBOOT|DEVCMD span:boot" "$LOG" | tail -3

grep -E "DEVSPANSECTOR|DEVSPANTILES|DEVSPANTILE |DEVSPANGMP|DEVSPANSYS|DEVSPANPROGRESS|DEVSPANVIEW|DEVSPANNAME|DEVPLANET|DEVSPANNET|DEVSPANBOOT|DEVOBJ|DEVTOOLS" \
  "$LOG" > "$OUT" 2>/dev/null

echo ""
echo "== result =="
echo "  wrote $OUT"
echo ""
echo "  screen state:"
grep -E "DEVSPANSYS|DEVSPANPROGRESS|DEVSPANVIEW|DEVSPANSECTOR" "$OUT" | tail -12
echo ""
echo "  planet names (the candidate roster):"
grep "DEVSPANNAME" "$OUT" | tail -40
echo ""
echo "  tiles:"
grep "DEVSPANTILE " "$OUT" | tail -6
echo ""
echo "  mission panel records:"
grep "DEVSPANGMP" "$OUT" | tail -4
echo ""
echo "  SPAN planets (the real roster, if any):"
grep -E "DEVPLANETS|DEVPLANET " "$OUT" | head -40

taskkill //F //IM CW4.exe >/dev/null 2>&1

echo ""
echo "  boot evidence:"
grep -E "DEVSPANBOOT|DEVOBJSLOT|DEVOBJ " "$OUT" | head -12

names=$(grep -c "DEVSPANNAME" "$OUT" 2>/dev/null || true)
tiles=$(grep -c "DEVSPANTILE " "$OUT" 2>/dev/null || true)
echo ""
echo "SPIKE RESULT: ${names:-0} planet name(s), ${tiles:-0} tile line(s)"
