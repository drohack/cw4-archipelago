#!/bin/bash
# Set the game up so a PERSON can drive the boon demo.
#
# The automated version kept measuring things nobody could see - spawned units
# that never appeared as built weapons - so this does the setup only: launch,
# unlock everything, boot a mission, and get out of the way. You build what you
# want, and the effects get fired on your word.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
MISSION="${1:-story2}"

wait_for() { local pat="$1" secs="${2:-30}" i; for i in $(seq 1 "$secs"); do grep -q "$pat" "$L" && return 0; sleep 1; done; return 1; }
send() {
  printf "%s\n" "$1" > "$CMD"
  if [ -n "${2:-}" ]; then wait_for "$2" 25 || echo "  WARNING: no ack for '$1'"; else sleep 3; fi
}

echo "[setup] clean slate"
taskkill //IM CW4.exe //F >/dev/null 2>&1
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
AutoConnect = false

[Debug]
DebugCommands = true
CFGEOF
: > "$CMD"

echo "[setup] launching"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: never reached the menu"; exit 1; }
sleep 3

echo "[setup] unlocking every building"
send "item:Mission Unlock: Home" "DEBUG fake item"
for u in Cannon Mortar Nullifier Sprayer Miner Factory "Greenar Refinery" \
         Pylon Terp "ERN Portal" Sniper "Missile Launcher" Shield Porter; do
  send "item:$u" "DEBUG fake item: $u"
done

echo "[setup] booting $MISSION"
send "boot:$MISSION" "LocationWatcher: mission"
send "ada:close"

cat <<'READY'

========================================================================
READY. The game is yours.

  1. Place the rift lab and build whatever you want - cannons and mortars
     for the ammo test, and let them fire so their ammo actually drops.
  2. Say the word and the effects get fired one at a time.

Nothing is automated from here. Each effect is a single command:

     boon:ammo          refill every weapon
     boon:energy 0.5    add 50 percent of the energy cap to the store

and the traps, if a before/after helps:

     trap:drain         empty every weapon
     trap:energy        empty the energy store
========================================================================
READY
