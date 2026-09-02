#!/bin/bash
# A watchable demo of the three temporary filler effects.
#
# Run it, then WATCH THE GAME WINDOW - the point is to see what each one feels
# like, not to read a log. Every step announces itself here and pauses so there
# is time to look, and every step prints the numbers it moved.
#
# Each effect is shown as a controlled before/after: the matching TRAP runs first
# so there is a visible hole for the boon to fill, on the same map and the same
# units, rather than waiting on whatever the mission happens to do.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
PAUSE="${PAUSE:-6}"          # seconds to watch each effect; PAUSE=15 to linger

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
wait_for() { local pat="$1" secs="${2:-25}" i; for i in $(seq 1 "$secs"); do grep -q "$pat" "$L" && return 0; sleep 1; done; return 1; }
send() {
  printf "%s\n" "$1" > "$CMD"
  if [ -n "${2:-}" ]; then wait_for "$2" 25 || echo "  WARNING: no ack for '$1'"; else sleep 3; fi
}
banner() { echo; echo "############################################################"; echo "# $*"; echo "############################################################"; }

echo "[demo] step 0: clean slate"
taskkill //IM CW4.exe //F >/dev/null 2>&1
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
AutoConnect = false

[Debug]
DebugCommands = true
CFGEOF
: > "$CMD"

echo "[demo] step 1: launch"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: never reached the menu"; exit 1; }
sleep 3

echo "[demo] step 2: grant the buildings and boot a mission"
send "item:Mission Unlock: Home" "DEBUG fake item"
for u in Cannon Mortar Nullifier Sprayer Miner Factory "Greenar Refinery" Pylon Terp; do
  send "item:$u" "DEBUG fake item: $u"
done
send "boot:story2" "LocationWatcher: mission 2"
send "ada:close"
sleep 6

echo "[demo] step 3: put a rift lab and some weapons on the map"
# A campaign mission starts with the rift lab in your HAND, not placed, so
# gs.commandBase is null until the player drops it. Both the energy cache and the
# cache needs it, because it writes that unit's store - without this the demo
# reports "no rift lab" instead of doing anything.
# commandbase, NOT riftlab - riftlab is the build-pane key and places nothing.
send "spawn:commandbase 1" "SPAWN commandbase:"
sleep 2
send "spawn:cannon 3" "SPAWN cannon:"
send "spawn:mortar 2" "SPAWN mortar:"
sleep 3

banner "WATCH THE GAME WINDOW from here on."
echo "Three cannons and two mortars are placed near the rift lab."
echo "Each effect below is shown as a before/after: the matching TRAP runs first"
echo "so there is a visible hole for the boon to fill."
sleep "$PAUSE"

banner "1/2  AMMO RESUPPLY - every weapon refilled at once"
mark
send "trap:drain" "TRAP drain"
since | grep -E "TRAP drain" | sed 's/^.*TRAP/TRAP/'
echo "  (weapons just emptied on purpose, so the refill is visible)"
sleep 3
mark
send "boon:ammo" "BOON resupply"
since | grep "BOON resupply" | sed 's/^.*BOON/BOON/'
sleep "$PAUSE"

banner "2/2  ENERGY CACHE - a slug of energy straight into the store"
mark
send "trap:energy" "TRAP energy"
since | grep "TRAP energy" | sed 's/^.*TRAP/TRAP/'
echo "  (store drained first, again so the grant is visible)"
sleep 3
mark
send "boon:energy 0.5" "BOON energy cache"
since | grep "BOON energy" | sed 's/^.*BOON/BOON/'
sleep "$PAUSE"

banner "Done. Game left running."
echo "Fire any of them again by hand:"
echo "    echo 'boon:ammo'        > '$CMD'"
echo "    echo 'boon:energy 0.5'  > '$CMD'"
echo "Log: $L"
