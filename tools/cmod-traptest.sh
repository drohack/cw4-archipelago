#!/usr/bin/env bash
# Verify that trap effects recognise CMOD units as the player's.
#
# Airship, Bertha and Sweeper are CMOD units - CW4's custom-unit system - and a
# CMOD's GetDataName() returns a GUID rather than a name. GameUtil.IsPlayerUnit
# matched names, so all three fell through and Spore Strike, Unit Stun and Ammo
# Drain quietly SKIPPED them: a stun would freeze your cannons and leave your
# airship flying. Fixed 2026-08-31 by adding a CMOD branch; this is the test that
# the branch actually fires, which needs a real built unit and therefore a human.
#
# The control is the point. The MAP's own CMOD units have GUID data names too,
# but no playerMenuUnitName, so they must read as NOT yours. A fix that blanket-
# accepted anything GUID-shaped would pass the positive test and fail this one.
#
# Expected once you have built them:
#   P:CModUnitManager/<guid>   x3   your airship, bertha, sweeper
#   -:CModUnitManager/<guid>   xN   the map's own custom units
#   P:Tower/tower, P:Cannon/cannon  ordinary units, as a sanity check
#
# Usage: tools/cmod-traptest.sh      (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"
# Each send WAITS for the mod to acknowledge in the log rather than sleeping a
# guessed interval. The first version of this script slept 2s per command and
# lost five of six: the debug channel polls every 30 FRAMES, and while a mission
# loads the framerate is low enough that 30 frames takes longer than 2 seconds -
# so the next write overwrote the previous one before it was ever read. The only
# command that survived was the last one, and it failed because the unlock it
# depended on had been thrown away.
wait_for() {  # wait_for <grep pattern> <seconds>
  local pat="$1" secs="${2:-20}" i
  for i in $(seq 1 "$secs"); do
    grep -q "$pat" "$L" && return 0
    sleep 1
  done
  return 1
}
send() {  # send <command> [ack pattern]
  printf "%s\n" "$1" > "$CMD"
  if [ -n "${2:-}" ]; then
    wait_for "$2" 25 || echo "  WARNING: no ack for '$1' (looked for '$2')"
  else
    sleep 3
  fi
}
dev() { printf "%s\n" "$1" > "$DEV"; sleep 3; }

echo "[setup 1/4] game closed, both plugins, debug channel on"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD" "$DEV"
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

echo "[setup 2/4] launching"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
# Wait for the menu rather than guessing. Sending before the mod ticks means the
# writes are simply lost.
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: game never reached the menu"; exit 1; }
sleep 3

echo "[setup 3/4] granting the three CMOD units plus a cannon as a control"
send "item:Mission Unlock: Founders" "DEBUG fake item: Mission Unlock: Founders"
for u in Airship Bertha Sweeper Cannon Pylon; do
  send "item:$u" "DEBUG fake item: $u"
done
send "units" "DEBUG UNITS:"
allowed=$(grep "DEBUG UNITS:" "$L" | tail -1 | sed 's/.*DEBUG UNITS: //')
echo "  $allowed"
case "$allowed" in
  *airship*bertha*sweeper*) ;;
  *) echo "  FATAL: the three CMOD units are not in the allowed set"; exit 1 ;;
esac

echo "[setup 4/4] booting Founders, instant build ON so this is quick"
send "boot:story19" "LocationWatcher: mission 19"
send "ada:close"
# Instant build and free resources so building three units takes seconds.
# Infinite resources is turned OFF again before the traps fire - it tops ammo up
# every frame, which would undo Ammo Drain as fast as it lands.
dev "set:instantbuild=on" 2
dev "set:infiniteresources=on" 2
dev "sim:run 1" 2

echo "----------------------------------------------------------------"
echo "READY. Press play if it is paused, then build one of each:"
echo ""
echo "   AIRSHIP   - the AIR tab"
echo "   BERTHA    - the SPECIAL tab"
echo "   SWEEPER   - the SPECIAL tab"
echo "   a CANNON  - the WEAPON tab (the control: an ordinary named unit)"
echo ""
echo "Instant build and free resources are on, so they place immediately."
echo "Tell me when they are down and I will fire the traps."
echo "----------------------------------------------------------------"
echo "Log: $L"
