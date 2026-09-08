#!/bin/bash
# Dump every counted-objective INSTANCE with its map cell, for the missions whose
# LOGIC is keyed to instance number.
#
# Checks are moving from "the Nth one you did" to "this specific structure". Four
# apworld tables are keyed to instance number and their comments say they mean
# ACTIVATION ORDER (OBJECTIVE_TIERS, WAIVES_INSTANCE), so the numbering switch
# and the logic re-derivation have to land together. This produces the input for
# the second half: which index each described structure actually gets.
#
# NO SERVER. `item:` fake-receives an unlock locally, which is all MissionGate
# needs, so this runs entirely offline - no port to free, nothing of the
# player's to collide with. The config and slot cache are still snapshotted,
# because the run has to write a known config to enable the debug channel.
#
# Missions that matter, and why:
#   story1  Farsite   WAIVES_INSTANCE (1,"Collect",1) - which cache is free
#   story11 Shattered OBJECTIVE_TIERS totems - which totem needs the mover
#   story12 Archon    two caches, no per-cache logic - cache identity control
#   story17 Sequence  OBJECTIVE_TIERS nullify (4 groups) + a per-cache Terp split
#   story18 Wallis    OBJECTIVE_TIERS nullify (being flattened)
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
OUT="$REPO/.aptest/instance-dump.txt"

# mission id | title (the unlock item is "Mission Unlock: <title>"). Newline
# separated and read with `read`, NOT a whitespace-split list: the five missions
# here happen to be single-word, but 13 of the 20 titles contain spaces ("Not My
# Mars", "Tower of Darkness"), and a word-split list would silently boot the
# wrong thing the moment this is pointed at one of them.
MISSION_LIST="${1:-}"
if [ -z "$MISSION_LIST" ]; then
  MISSION_LIST="story1|Farsite
story11|Shattered
story12|Archon
story17|Sequence
story18|Wallis"
fi

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
wait_since() { local pat="$1" n="${2:-20}" i; for i in $(seq 1 "$n"); do
                 since | grep -q "$pat" && return 0; sleep 1; done; return 1; }

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
  taskkill //IM CW4.exe //F >/dev/null 2>&1
  if [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ]; then cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"; fi
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

echo "instance-dump: offline, no server"
taskkill //IM CW4.exe //F >/dev/null 2>&1
rm -rf "$STORE_DIR/slots" 2>/dev/null
cat > "$CFG" <<CFGEOF
[Connection]
Host = localhost
Port = 38281
Slot =
Password =
AutoConnect = false

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF
mkdir -p "$(dirname "$OUT")"; : > "$OUT"
sleep 2

rm -f "$CMD"; cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 14; MARK=0
wait_since "ModCore initialized" 40; verdict $? "the mod loaded (control)"

dump_one() {   # $1 = storyN, $2 = Title, $3 = label for the output file
  local spec="$1" title="$2" label="$3"
  echo "-- $spec ($title) --"
  mark
  send "item:Mission Unlock: $title"
  send "boot:$spec"
  if since | grep -q "boot BLOCKED"; then
    verdict 1 "$spec booted (gate blocked it even with the unlock)"
    return 1
  fi
  if ! wait_since "New GameSpace" 60; then
    verdict 1 "$spec booted"
    return 1
  fi
  send "ada:close"; sleep 3
  mark
  send "inst:dump"; sleep 3
  local got
  got=$(since | grep -E "INST (DUMP|NULLIFY|TOTEM|CACHE|INFOCACHE)")
  if [ -z "$got" ]; then
    verdict 1 "$spec dumped its instances"
    return 1
  fi
  verdict 0 "$spec dumped its instances"
  { echo "===== $label $spec ($title)"; echo "$got" | sed 's/^\[[^]]*\] *//'; echo; } >> "$OUT"
  # The cells have to be real. (-1,-1) is the "could not read it" marker in the
  # dump, so a run full of them means the member lookup was wrong and every
  # index derived from it would be garbage.
  local bad
  bad=$(echo "$got" | grep -c "cell=(-1,-1)")
  [ "${bad:-0}" -eq 0 ]; verdict $? "$spec cells all read ($bad unreadable)"
  return 0
}

while IFS='|' read -r spec title; do
  # Strip CR. A mission list produced by anything Windows-flavoured arrives
  # CRLF, `read` keeps the CR on the last field, and it then ends up INSIDE the
  # echoed block header - which Python's splitlines() treats as a line break, so
  # every header parsed as two lines and the generator attributed all 22 caches
  # to one mission.
  spec=$(printf "%s" "$spec" | tr -d "\r")
  title=$(printf "%s" "$title" | tr -d "\r")
  [ -z "$spec" ] && continue
  dump_one "$spec" "$title" "first"
done <<MLEOF
$MISSION_LIST
MLEOF

# Determinism control. The sets are HashSets, so raw iteration order proves
# nothing - but the SORTED cell list must be identical on a second boot, or no
# index derived from it can be stable and the whole approach is unusable.
echo "-- determinism: boot Shattered again and compare sorted cells --"
dump_one story11 Shattered "second" >/dev/null 2>&1
A=$(awk '/^===== first story11/,/^$/' "$OUT" | grep -oE "cell=\([0-9-]+,[0-9-]+\)" | sort)
B=$(awk '/^===== second story11/,/^$/' "$OUT" | grep -oE "cell=\([0-9-]+,[0-9-]+\)" | sort)
# Assert the PREMISE separately. Comparing against an empty list reported a
# bare FAIL with a blank "first:" line when this was run with a mission list
# that did not include story11 - a harness artifact that reads exactly like a
# real determinism failure, which is the worst kind of misleading verdict.
if [ -z "$A" ] || [ -z "$B" ]; then
  verdict 1 "HARNESS PREMISE: both story11 dumps present, so there is something to compare"
  echo "        (run with no mission-list argument, or include story11 in it)"
elif [ "$A" = "$B" ]; then
  verdict 0 "the sorted cell list is identical across two boots"
else
  verdict 1 "the sorted cell list is identical across two boots"
  echo "        first:  $(echo "$A" | tr '\n' ' ')"
  echo "        second: $(echo "$B" | tr '\n' ' ')"
fi

# The cache-identity question, asked as a measurement rather than assumed.
#
# A collected cache LEAVES GameSpace.mustCollect, so the remaining set cannot
# name what was taken - which for the two-cache missions (Farsite, Archon,
# Sequence) would mean a collected cache has no identity after a reload. That
# used to be harmless because no per-cache logic existed; it is load-bearing now,
# because Farsite's free-cache waiver and Sequence's Terp split are both keyed to
# a SPECIFIC cache.
#
# InfoCache carries its own `retrieved` flag (InfoCache.retrieved : Boolean,
# confirmed by reflection). If the object survives collection then caches behave
# exactly like nullify targets, whose IsSuppressed() marks them in place, and no
# persisted state is needed. If it does not survive, identity has to be carried
# in SlotState instead. Take one and look.
echo "-- cache identity: does a COLLECTED cache stay identifiable? --"
mark
send "item:Mission Unlock: Farsite"
send "boot:story1"
if wait_since "New GameSpace" 60; then
  send "ada:close"; sleep 3
  mark; send "inst:dump"; sleep 3
  BEFORE_MC=$(since | grep -oE "INST CACHE: [0-9]+ still" | grep -oE "[0-9]+" | head -1)
  BEFORE_IC=$(since | grep -oE "INST INFOCACHE: [0-9]+ InfoCache" | grep -oE "[0-9]+" | head -1)
  mark; send "cache:destroy"; sleep 4
  since | grep -q "cache:destroy: one cache destroyed"; verdict $? "a cache was collected (control)"
  mark; send "inst:dump"; sleep 3
  AFTER_MC=$(since | grep -oE "INST CACHE: [0-9]+ still" | grep -oE "[0-9]+" | head -1)
  AFTER_IC=$(since | grep -oE "INST INFOCACHE: [0-9]+ InfoCache" | grep -oE "[0-9]+" | head -1)
  RETR=$(since | grep -c "INST INFOCACHE .*retrieved=True")
  { echo "===== cache identity (Farsite)";
    echo "mustCollect  $BEFORE_MC -> $AFTER_MC";
    echo "InfoCache    $BEFORE_IC -> $AFTER_IC (retrieved=True: ${RETR:-0})";
    since | grep -E "INST (CACHE|INFOCACHE)" | sed 's/^\[[^]]*\] *//'; echo; } >> "$OUT"
  echo "        mustCollect $BEFORE_MC -> $AFTER_MC; InfoCache $BEFORE_IC -> $AFTER_IC; retrieved=True x${RETR:-0}"
  # The premise: collecting must actually have moved mustCollect, or nothing
  # below is measuring what it claims to.
  [ "${AFTER_MC:-9}" -lt "${BEFORE_MC:-0}" ]; verdict $? "collecting removed it from mustCollect (premise)"
  # RECORDED, not asserted. Collecting a cache destroys the unit, so the answer
  # is "no" and always will be - this is a property of the game, not a defect,
  # and a standing expected FAIL would teach everyone to ignore this harness's
  # failures. What follows from it is that cache identity cannot come from live
  # state, which is why MapCells.cs exists (known map cells, generated by
  # tools/gen-mapcells.py) with the observed-set memory as a fallback.
  if [ "${AFTER_IC:-0}" -ge "${BEFORE_IC:-9}" ] && [ "${RETR:-0}" -ge 1 ]; then
    echo "  NOTE  a collected cache IS still in the scene with retrieved=True."
    echo "        That contradicts the 2026-09-08 measurement - re-check whether"
    echo "        MapCells is still needed at all."
  else
    echo "  NOTE  a collected cache leaves no trace in live state (unit destroyed,"
    echo "        no InfoCache, no id on UnitManager). Identity therefore comes"
    echo "        from MapCells.cs; see tools/instance-identity.sh for the"
    echo "        end-to-end assertion that the right instance is sent."
  fi
  # What CAN be asserted: the remaining cache's cell is one the known table
  # lists. If that ever fails, the table is stale and the mod will (correctly)
  # fall back rather than mislabel - but somebody needs to regenerate it.
  LEFTCELL=$(since | grep -oE "INST CACHE raw=[0-9]+ cell=\([0-9]+,[0-9]+\)" | grep -oE "[0-9]+,[0-9]+" | head -1)
  if [ -n "$LEFTCELL" ]; then
    grep -q "\"$LEFTCELL\"" "$(cd "$(dirname "$0")/.." && pwd)/src/CW4Archipelago.Core/MapCells.cs"
    verdict $? "the surviving cache's cell ($LEFTCELL) is in the known table"
  else
    verdict 1 "the surviving cache's cell is in the known table (no cell read)"
  fi
else
  verdict 1 "a cache was collected (control)"
  verdict 1 "collecting removed it from mustCollect (premise)"
  verdict 1 "a COLLECTED cache is still identifiable (InfoCache survives, retrieved=True)"
fi

echo "---"
echo "instance-dump: $PASS passed, $FAIL failed"
echo "dump written to $OUT"
