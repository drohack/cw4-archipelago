#!/usr/bin/env bash
# Tower of Darkness, set up as the WORST CASE a Miner-less seed can hand you.
#
# The one gap in the randomizer's logic that points the dangerous way: Miner
# appears in no logic table at all. Tower energy carries most maps, and story15
# is the mission the worksheet flagged - "RESO at start for mining, but you might
# need it for energy at the start since there's not a lot of land for towers."
# Too-loose logic strands a player; too-tight only makes seeds duller. So this
# has to be answered by playing it, not reasoned about.
#
# What is granted is EXACTLY what logic requires for story15 and nothing else:
#   Cannon        offense (logic accepts Cannon OR Mortar)
#   Nullifier     the Nullify objective
#   Chronat       (15, "Nullify") - this is the "beacon of light" that lifts the
#                 darkness; the worksheet left "which unit?" open and the rules
#                 table answers it
#   Greenar Refinery + Factory    (15, "Totems"), and Chronat's prerequisites
#
# NOT granted, deliberately: Miner, Pylon, Platform, Terp, Sniper, Missile
# Launcher, and NO energy or storage filler items. Logic has to hold in the worst
# case, so if the mission works with zero energy items it is safe. UnitGate
# enforces the list, so a Miner cannot be built even by accident - the test is
# faithful without relying on self-discipline.
#
# The dev-tools cheats stay OFF. The strip at the bottom reads all grey, which is
# what makes the notes trustworthy.
#
# Usage: tools/story15-handtest.sh      (game must be CLOSED)
#        Then play. Answer the questions in
#        docs/design/2026-08-31-open-questions-worksheet.md
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEVCMD="$CW4/BepInEx/cw4dev-commands.txt"
SAMPLE=15          # seconds between samples; the energy story goes on record
send() { printf "%s\n" "$1" > "$CMD"; sleep "${2:-2}"; }
devsend() { printf "%s\n" "$1" > "$DEVCMD"; sleep "${2:-2}"; }

echo "[setup 1/5] game closed, randomizer on, cheats off"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD" "$DEVCMD"
if [ -d "$CW4/BepInEx/plugins-disabled/CW4Archipelago" ] \
   && [ ! -d "$CW4/BepInEx/plugins/CW4Archipelago" ]; then
  mv "$CW4/BepInEx/plugins-disabled/CW4Archipelago" "$CW4/BepInEx/plugins/CW4Archipelago"
fi
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
Host = localhost
Port = 38281
Slot = DrohaCW4
Password =
AutoConnect = false

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF

echo "[setup 2/5] launching"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 16

echo "[setup 3/5] cheats OFF, so the run is vanilla"
for c in instantbuild allbuildings infiniteresources indestructible freezecreeper; do
  devsend "set:$c=off"
done

echo "[setup 4/5] granting exactly story15's logic requirements"
send "item:Mission Unlock: Tower of Darkness"
for u in Cannon Nullifier Chronat "Greenar Refinery" Factory; do
  send "item:$u"
done
send "units" 3
echo "  allowed set the mod will enforce:"
grep "DEBUG UNITS:" "$L" | tail -1 | sed 's/.*DEBUG UNITS: /    /'

echo "[setup 5/5] booting story15"
send "boot:story15" 28
for i in $(seq 1 30); do grep -q "LocationWatcher: mission 15" "$L" && break; sleep 2; done
if ! grep -q "LocationWatcher: mission 15" "$L"; then
  echo "  WARNING: story15 did not report loading - check the log"
fi
send "ada:close" 3
send "units" 3
echo "  in-mission check (structButtons should be small, and NO miner):"
grep "DEBUG UNITS:" "$L" | tail -1 | sed 's/.*DEBUG UNITS: /    /'

echo "----------------------------------------------------------------"
echo "READY. Press play and try to get Tower of Darkness moving."
echo ""
echo "You have: rift lab, tower, cannon, nullifier, chronat, refinery, factory."
echo "You do NOT have: miner, pylon, platform, terp, sniper, missile launcher,"
echo "and no energy or storage items. That is the worst case logic must survive."
echo ""
echo "The question is NOT 'can you win'. It is:"
echo "  did ENERGY become the blocker in a way a Miner would have fixed?"
echo ""
echo "Leave this running - it samples the energy story every ${SAMPLE}s."
echo "Quit whenever you know the answer, then fill in the worksheet."
echo "----------------------------------------------------------------"

start=$SECONDS
while :; do
  el=$((SECONDS - start))
  if ! tasklist //FI "IMAGENAME eq CW4.exe" 2>/dev/null | grep -qi CW4.exe; then
    echo "[watch ${el}s] game closed - stopping."
    break
  fi
  send "counts:dump" 1
  send "resources:dump" 1
  c=$(grep "COUNTS:" "$L" | tail -1 | grep -o "mustCollect=[0-9]*/[0-9]*")
  o=$(grep "COUNTS:" "$L" | tail -1 | grep -o "objectives\[[^]]*\]")
  echo "[watch ${el}s] $c $o"
  sleep "$SAMPLE"
done

echo "--- energy and objective samples are in the log:"
echo "    grep -E 'COUNTS:|RESOURCES:' \"$L\""
echo "Done. Log: $L"
