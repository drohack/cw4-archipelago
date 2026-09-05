#!/usr/bin/env bash
# Reproduce two bugs reported from a real playthrough on v0.1.6, BEFORE fixing
# either, so the fix is measured against a failure that was actually observed.
#
#   1. Home fully beaten, but the level-select totem icon is still GREEN.
#      Both "Home - Totem 1" and "Home - Totem 2" were checked (seen in the
#      player's log), so that marker should be GREY.
#   2. An ERN granted next to the rift lab spawns INSIDE the terrain.
#      ErnGranter offsets X and Z from the lab but keeps the lab's Y, and CW4
#      terrain has height - so wherever the ground is higher than the lab, the
#      unit is buried.
#
# Usage: tools/playtest-repro.sh      (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
STORE="$HOME/Documents/My Games/creeperworld4/archipelago"


# --- restore the player's environment on exit ---------------------------------
# A harness overwrites the BepInEx config (hermetic settings) and clears the AP
# slot cache (so the offline-start feature cannot inherit a previous battery's
# slot). Both belong to whoever plays this install next. Leaving a test config
# behind pointed a real session at a dead localhost port with a test slot name,
# which reads in game as "connecting... timed out... disconnected" and no items.
STORE_DIR="$HOME/Documents/My Games/creeperworld4/archipelago"
CFG_BAK=""; STORE_BAK=""
save_env() {
  if [ -f "$CFG" ]; then CFG_BAK="$(mktemp)"; cp "$CFG" "$CFG_BAK"; fi
  if [ -d "$STORE_DIR/slots" ] || [ -f "$STORE_DIR/last-session.json" ]; then
    STORE_BAK="$(mktemp -d)"
    [ -d "$STORE_DIR/slots" ] && cp -r "$STORE_DIR/slots" "$STORE_BAK/slots"
    [ -f "$STORE_DIR/last-session.json" ] && cp "$STORE_DIR/last-session.json" "$STORE_BAK/"
  fi
}
restore_env() {
  if [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ]; then
    cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"
  fi
  rm -rf "$STORE_DIR/slots"; rm -f "$STORE_DIR/last-session.json"
  if [ -n "$STORE_BAK" ] && [ -d "$STORE_BAK" ]; then
    [ -d "$STORE_BAK/slots" ] && cp -r "$STORE_BAK/slots" "$STORE_DIR/slots"
    [ -f "$STORE_BAK/last-session.json" ] && cp "$STORE_BAK/last-session.json" "$STORE_DIR/"
    rm -rf "$STORE_BAK"
  fi
  echo "  (config and slot cache restored)"
}
save_env
trap restore_env EXIT

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0;
          tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
wait_since() { local pat="$1" n="${2:-20}" i; for i in $(seq 1 "$n"); do
                 since | grep -q "$pat" && return 0; sleep 1; done; return 1; }

echo "step 0: clean slate (no server, no cached slot)"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 3
rm -f "$CMD"
rm -rf "$STORE/slots" "$STORE/last-session.json"
mkdir -p "$(dirname "$CFG")"
printf '[Connection]\nHost = localhost\nPort = 38499\nSlot = repro\nPassword =\nAutoConnect = false\n\n[Missions]\nShowSpan = false\n' > "$CFG"

echo "step 1: launch"
( cd "$CW4" && ./CW4.exe > /dev/null 2>&1 & ); sleep 16
MARK=0
wait_since "ModCore initialized" 30 || { echo "FATAL: mod did not load"; exit 1; }
echo "  mod loaded"

echo "step 2: build Home's location set, as a server would have sent it"
send "item:Mission Unlock: Home"
for loc in "Home - Cache 1" "Home - Totem 1" "Home - Totem 2" "Home - Nullify 1" \
           "Home - Mission Complete"; do
  send "loc:add $loc"
done

echo "step 3: BEFORE - nothing checked (control: the totem marker should be GREEN)"
# The glyphs only exist while the mission MAP is open. Without this the dump
# is silently empty and the run looks like it proved something.
send "story:open"; sleep 4
mark
send "glyphs:dump Home"; sleep 3
echo "  --- glyphs before ---"
since | grep "DEBUG GLYPHS: story2 " | sed 's/^.*DEBUG GLYPHS/  GLYPH/'

echo "step 4: check exactly what the player checked (both totems, cache, complete)"
for loc in "Home - Cache 1" "Home - Totem 1" "Home - Totem 2" "Home - Mission Complete"; do
  send "check:$loc"
done
# Home - Nullify 1 is deliberately left unchecked: the player's log shows they
# never took it, so the nullify marker SHOULD stay coloured.

echo "step 5: AFTER - both totems checked (the totem marker should now be GREY)"
mark
send "glyphs:dump Home"; sleep 3
echo "  --- glyphs after ---"
since | grep "DEBUG GLYPHS: story2 " | sed 's/^.*DEBUG GLYPHS/  GLYPH/'

echo "step 6: ERN height beside the rift lab"
send "item:Progressive ERN"
send "boot:story2"
wait_since "New GameSpace" 45 || echo "  (story2 slow to load)"
send "ada:close"
# No rift lab, no ERN: ErnGranter waits for GameSpace.commandBase, and a
# mission starts with the lab in the player's hand rather than placed. This is
# exactly the moment the player described - "when I spawned in my rift Lab".
send "spawn:CommandBase"; sleep 4
wait_since "ERN granted near rift lab" 40 || echo "  WARNING: no ERN was granted"
sleep 4
mark
send "ern:status"; sleep 3
echo "  --- ERN positions (dy < 0 means buried) ---"
since | grep "DEBUG ERN" | sed 's/^.*DEBUG /  /'

# One map proves nothing: the old code only buried the ERN where the ground
# beside the lab was HIGHER than the lab, so a flat start looks fine either way.
for m in story4 story8 story11 story15; do
  echo "  -- $m --"
  send "boot:$m"
  wait_since "New GameSpace" 45 || echo "     (slow to load)"
  send "ada:close"
  send "spawn:CommandBase"; sleep 4
  wait_since "ERN granted near rift lab" 40 || echo "     no ERN granted"
  sleep 3
  mark
  send "ern:status"; sleep 3
  since | grep "DEBUG ERNPOS" | sed 's/^.*DEBUG /     /'
done

echo "step 7: shut down"
taskkill //IM CW4.exe //F >/dev/null 2>&1
echo "done - read the two glyph dumps and the ERN dy above"
