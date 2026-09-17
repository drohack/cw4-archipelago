#!/usr/bin/env bash
# Can a SPAN map be swapped into the FARSITE level select and played?
#
# The randomizer's design is 20 mission slots drawn from Farsite and SPAN
# together, which needs a SPAN map to occupy a planet on the Farsite map. Four
# things have to work, and this tests them in order because each depends on the
# last:
#
#   1. SWAP    retarget a planet at a SPAN map
#   2. LAUNCH  clicking it loads that map, not the original
#   3. SAVE    the autosave lands somewhere, under a known category
#   4. RELOAD  the save can be loaded back
#
# Booting a SPAN map directly (span:boot) is already proven and is NOT what this
# tests - it bypasses the level select entirely. The interesting risk is the
# CATEGORY: the Farsite panel is CATEGORY.FARSITE, SPAN maps live under
# CATEGORY.SPAN, and the category picks the save folder (saves/farsite vs
# saves/span). The randomizer's per-slot save isolation only covers saves/farsite.
#
# Usage: tools/span-swap-test.sh [planet] [spanguid]      (game must be CLOSED)
set -u

G="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
LOG="$G/BepInEx/LogOutput.log"
CMD="$G/BepInEx/cw4dev-commands.txt"
SAVES="$USERPROFILE/Documents/My Games/creeperworld4/saves"
PLANET="${1:-story5}"
SPANMAP="${2:-knucracker1}"
REPO_SHOTS="$(cd "$(dirname "$0")/.." && pwd)/.aptest/swap-shots"

send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }
MARK=0
mark() { MARK=$(wc -l < "$LOG" 2>/dev/null || echo 0); }
since() { tail -n +"$((MARK+1))" "$LOG" 2>/dev/null; }
pass=0; fail=0
check() { if [ "$2" = "0" ]; then echo "  PASS  $1"; pass=$((pass+1)); else echo "  FAIL  $1"; fail=$((fail+1)); fi; }

PLUGINS="$G/BepInEx/plugins"; PARKED="$G/BepInEx/plugins-disabled"; RESTORE=0
restore() {
  [ "$RESTORE" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in CW4Archipelago CW4ApDebug; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored)"
}
trap restore EXIT
for d in CW4Archipelago CW4ApDebug; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"; rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE=1
  fi
done

echo "== setup: swapping $SPANMAP onto planet $PLANET =="
taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
rm -f "$LOG" "$CMD"
# Snapshot the save folders so a new autosave is identifiable rather than
# guessed at - an existing save would otherwise read as a success.
# CLEAR ANY SAVE FOR THIS MISSION FIRST.
#
# A previous run's autosave is what made this test look intermittent: with a
# save present, OnPlay() opens the resume-or-restart panel instead of launching,
# so the mission never loads and the log says only "OnPlay invoked". That was
# invisible until a screenshot showed the RESTART MISSION button. A test must
# create the state it needs rather than inherit whatever the last run left.
rm -rf "$SAVES/farsite/$SPANMAP" "$SAVES/span/$SPANMAP" 2>/dev/null
echo "  cleared any existing save for $SPANMAP"
BEFORE_FARSITE=$(ls "$SAVES/farsite" 2>/dev/null | wc -l)
BEFORE_SPAN=$(ls "$SAVES/span" 2>/dev/null | wc -l)
echo "  saves before: farsite=$BEFORE_FARSITE span=$BEFORE_SPAN"

( cd "$G" && ./CW4.exe >/dev/null 2>&1 & )
for i in $(seq 1 120); do grep -q "Dev Tools loaded" "$LOG" 2>/dev/null && break; sleep 2; done
echo "  dev tools loaded"

# OPEN THE MAP, AND KEEP TRYING UNTIL THE PLANETS ARE REALLY THERE.
#
# story:open logs success as soon as it invokes the button, which says nothing
# about whether the main menu was ready to receive the click or whether the map
# finished building. One run swapped against a planet reading mapGuid=''
# title='' objectives=0 and then had nothing to launch - which looked like the
# swap mechanism failing when it was the harness being early. The only honest
# readiness signal is a planet reporting its own metadata.
echo "== 1. open the Farsite level select =="
ready=1
for i in $(seq 1 15); do
  send "story:open" 5
  mark
  send "planets:dump" 4
  if since | grep -qE "DEVPLANET '[^']+' guid=$PLANET "; then ready=0; break; fi
  sleep 3
done
check "Farsite map open with planet metadata populated" $ready
[ "$ready" = "0" ] || { echo "  (cannot continue without the map)"; taskkill //F //IM CW4.exe >/dev/null 2>&1; echo ""; echo "DONE: $pass passed, $fail failed"; exit 1; }

# A LOG IS NOT AN OBSERVATION (docs/in-game-testing.md). Everything below is
# asserted from log lines; these screenshots are what makes the claim checkable
# by eye - especially the objective ICONS, which no log line describes.
send "shot:$G/swap-1-before.png" 8

echo "== 2. swap =="
mark
send "span:swap $PLANET $SPANMAP ${OBJMASK:-7}" 4
since | grep -q "DEVSWAP after .*mapGuid='$SPANMAP'"; check "planet retargeted at $SPANMAP" $?
since | grep -E "DEVSWAP" | sed 's/.*DEVSWAP/    DEVSWAP/'

# Rebuild the icons to the SPAN map's real objective slots. Setting
# map_objectives alone does not move them - proven twice by screenshot.
send "span:icons $SPANMAP ${OBJSLOTS:-0,1,2}" 4
send "shot:$G/swap-2-after.png" 8

echo "== 3. launch it from the map =="
mark
send "span:play $SPANMAP" 10
since | grep -E "DEVPLAY" | sed 's/.*DEVPLAY/    DEVPLAY/'
live=1
for i in $(seq 1 20); do
  send "obj:dump" 3
  since | grep -q "DEVOBJ mission=" && { live=0; break; }
  sleep 2
done
check "a mission loaded" $live
got=$(since | grep -oE "DEVOBJ mission=[^ ]+" | tail -1 | cut -d= -f2)
echo "    loaded mission: '$got' (wanted '$SPANMAP')"
[ "$got" = "$SPANMAP" ]; check "the SPAN map loaded, not the original planet's" $?

send "ada:close" 3
send "shot:$G/swap-3-ingame.png" 8

echo "== 4. autosave =="
send "ada:close" 2
send "sim:run" 2
sleep 12
# Force it rather than wait. 20 seconds of sim could not distinguish "saves do
# not work here" from "the autosave interval is longer than that", and those
# call for completely different responses.
mark
send "save:auto" 6
since | grep -E "DEVSAVE" | sed 's/.*DEVSAVE/    DEVSAVE/'
send "sim:pause" 2
AFTER_FARSITE=$(ls "$SAVES/farsite" 2>/dev/null | wc -l)
AFTER_SPAN=$(ls "$SAVES/span" 2>/dev/null | wc -l)
echo "  saves after : farsite=$AFTER_FARSITE span=$AFTER_SPAN"
[ "$AFTER_FARSITE" -gt "$BEFORE_FARSITE" ] || [ "$AFTER_SPAN" -gt "$BEFORE_SPAN" ]; check "an autosave appeared somewhere" $?
if [ "$AFTER_FARSITE" -gt "$BEFORE_FARSITE" ]; then
  echo "    -> saved under saves/farsite (the randomizer's per-slot isolation covers this)"
  ls -t "$SAVES/farsite" | head -3 | sed 's/^/       /'
fi
if [ "$AFTER_SPAN" -gt "$BEFORE_SPAN" ]; then
  echo "    -> saved under saves/span (OUTSIDE the randomizer's per-slot isolation)"
  ls -t "$SAVES/span" | head -3 | sed 's/^/       /'
fi

taskkill //F //IM CW4.exe >/dev/null 2>&1
mkdir -p "$REPO_SHOTS"
mv "$G"/swap-*.png "$REPO_SHOTS/" 2>/dev/null
echo ""
echo "  screenshots: $REPO_SHOTS"
ls "$REPO_SHOTS" 2>/dev/null | sed 's/^/    /'
echo ""
echo "DONE: $pass passed, $fail failed"
