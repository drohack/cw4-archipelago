#!/bin/bash
# Set up a playable mission with RESO nodes, then GET OUT OF THE WAY.
#
# READ docs/in-game-testing.md FIRST.
#
# Four automated attempts failed to wake a resource node (see
# docs/ern-upgrade-measurements.md), so this stops trying: it boots a mission
# that has nodes, turns on the cheats that make building painless, places the
# ERN port and ERNs (the test's side of the job), reports where the nodes are,
# and LEAVES THE GAME RUNNING for the designer to lay down the mining units.
#
# Then say the word and tools/ern-mine-measure.sh takes the reading.
#
# THE ONE CHEAT THAT MUST STAY OFF: infiniteresources. The dev tools describe it
# as "units that hold wares (factory greenar/redon/bluite, weapon ammo) stay
# topped up" - it would peg the very total the measurement reads. Every other
# harness turns it on, so it is set explicitly OFF here rather than omitted,
# because the dev tools persist cheats across launches.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"

# story6 has four nodes in a tight cluster - see tools/ern-ore-scan.sh, which
# found nodes in story5 (1), story6 (4), story7 (1), story8 (1), story10 (1),
# story12 (6) and story15 (3). Missions 2 to 4 have none at all.
MISSION="${MISSION:-story6}"

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
wait_for() { local i; for i in $(seq 1 "${2:-25}"); do grep -qa "$1" "$L" && return 0; sleep 1; done; return 1; }
wait_since() { local i; for i in $(seq 1 "${2:-25}"); do since | grep -qa "$1" && return 0; sleep 1; done; return 1; }
send() { mark; printf '%s\n' "$1" > "$CMD"; if [ -n "${2:-}" ]; then wait_since "$2" 25 || echo "  (no ack: $1)"; else sleep 3; fi; }
dev() { printf '%s\n' "$1" > "$DEV"; sleep 2; }
say() { echo; echo "=== $* ==="; }

LOCK="$CW4/BepInEx/cw4-harness.lock"
acquire_lock() {
  if [ -f "$LOCK" ]; then
    local pid
    pid=$(cat "$LOCK" 2>/dev/null)
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
      echo "FATAL: another harness is already running (pid $pid). kill $pid"
      exit 1
    fi
  fi
  echo "$$" > "$LOCK"
  # Released on exit - the GAME stays up, but the lock must not, or the
  # measurement script that follows would refuse to start.
  trap 'rm -f "$LOCK"' EXIT INT TERM
}

acquire_lock
echo "[setup] launch"
taskkill //IM CW4.exe //F >/dev/null 2>&1
# See the other harnesses: wait_for greps the whole log, so a stale
# "SCENE: 'Galaxy'" from the previous run makes the launch wait return early.
# The sleep matters: taskkill returns before Windows releases the handle, and
# truncating too early fails with "Device or resource busy" - which left a
# previous run reading a stale log and reporting a boot that never acked.
sleep 4
: > "$L" 2>/dev/null || echo "  (could not truncate the log - it is still held)"
mkdir -p "$CW4/BepInEx/config"
printf '[Connection]\nAutoConnect = false\n\n[Debug]\nDebugCommands = true\n' \
  > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
: > "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: no menu"; exit 1; }
sleep 3

echo "[setup] unlocking every mission"
for T in "Farsite" "Home" "Not My Mars" "Ruins Repurposed" "We Know Nothing" \
         "We Were Never Alone" "Hints" "Serious" "More and More" "War and Peace" \
         "Shattered" "Archon" "The Experiment" "Somewhere in Spacetime" \
         "Tower of Darkness" "The Compound" "Sequence" "Wallis" "Founders" "Ever After"; do
  send "item:Mission Unlock: $T" "DEBUG fake item"
done

# GRANT EVERY UNIT UNLOCK TOO.
#
# The first run of this script reported allowed=[riftlab,tower] and no miner -
# which was OUR OWN randomizer gating the build pane, not the mission. Miner,
# Factory and Greenar Refinery are all Archipelago unlock items
# (UnitRules.ItemToUnit), so without them the designer is handed a mission with
# nothing to mine with. set:allbuildings does not cover this: it is the dev
# tools' cheat, and the mod's own gating runs on top of it.
echo "[setup] unlocking every buildable"
for U in "Cannon" "Mortar" "Nullifier" "Miner" "Factory" "Greenar Refinery"          "Missile Launcher" "Sprayer" "Terp" "ERN Portal" "Sniper" "Porter"          "Pylon" "Bomber Pad" "Runway" "Shield" "AC Bomber Pad" "Chronat"          "Microrift" "Platform" "Rocket Pad" "Airship" "Bertha" "Sweeper"; do
  send "item:$U" "DEBUG fake item"
done

echo "[setup] booting $MISSION"
mark
printf '%s\n' "boot:$MISSION" > "$CMD"
for i in $(seq 1 25); do
  sleep 1
  ACK=$(since | grep -aE "DEBUG boot" | tail -1)
  [ -n "$ACK" ] && break
done
case "$ACK" in
  *BLOCKED*) echo "FATAL: $ACK"; exit 1 ;;
  "")        echo "FATAL: boot never acknowledged"; exit 1 ;;
esac
sleep 12

dev "set:allbuildings=on"
dev "set:instantbuild=on"
dev "set:indestructible=on"
dev "set:infiniteresources=off"      # MUST be off - it tops up held wares
sleep 2

send "ada:close"
send "ada:clear" "ADA clear"
send "sim:run 1" "SIM run:"
sleep 3
send "ada:clear" "ADA clear"

say "RESO NODES in $MISSION"
mark
printf '%s\n' "ern:resources" > "$CMD"
for i in $(seq 1 20); do
  sleep 2
  OUT=$(since | grep -aE "RESOURCE (node|refinery|scan)" | head -8)
  [ -n "$OUT" ] && break
done
printf '%s\n' "${OUT:-  (no answer from the probe)}" | sed 's/^.*RESOURCE/  RESOURCE/'

# Node coordinates, so the ERN port can go somewhere useful and the designer
# knows where to build.
NX=$(printf '%s\n' "$OUT" | grep -a "RESOURCE node" | head -1 | grep -oE "pos=\([0-9]+" | grep -oE "[0-9]+")
NY=$(printf '%s\n' "$OUT" | grep -a "RESOURCE node" | head -1 | grep -oE ",[0-9]+\)" | grep -oE "[0-9]+")

say "WHAT IS BUILDABLE (after unlocks)"
send "units" "DEBUG UNITS"
since | grep -a "DEBUG UNITS" | tail -1 | sed 's/^.*DEBUG UNITS/  /'
echo "  (miner and factory must appear here, or the pane is still gated)"

if [ -n "${NX:-}" ] && [ -n "${NY:-}" ]; then
  say "PLACING THE ERN PORT near the first node ($NX,$NY)"
  send "spawnat:erninterface $((NX+5)) $((NY+5))" "SPAWNAT erninterface:"
  since | grep -a "SPAWNAT erninterface:" | tail -1 | sed 's/^.*SPAWNAT/  SPAWNAT/'
  for i in 0 1 2 3 4 5; do
    send "spawnat:ern $((NX+7+i)) $((NY+7))" "SPAWNAT ern:"
  done
  send "ern:free" "ERN free:"
  since | grep -a "ERN free:" | tail -1 | sed 's/^.*ERN/  ERN/'
else
  echo "  (could not parse a node position - place the ERN port yourself)"
fi

send "ada:clear" "ADA clear"
rm -f "$CW4/ap_shot.png"; send "shot:" "SHOT:"; sleep 3

say "OVER TO YOU"
cat <<'NOTES'
  The game is RUNNING and left open. Cheats: allbuildings, instantbuild,
  indestructible ON; infiniteresources deliberately OFF so the ware total is
  real.

  An ERN port and six freed ERNs are placed near the first node.

  Please build whatever actually mines on this mission - a miner on the RESO
  ground, or a refinery near a greenar, or a tower/rift lab near the crystal -
  plus the factory to hold the resource.

  Then say so, and tools/ern-mine-measure.sh will:
    1. read the node state to confirm it is producing
    2. time the ware total with Mine Production at 0 percent
    3. dock an ERN, wait for 100 percent, time it again
    4. add four ERN Efficiency Cap: Mine Production, time it again
NOTES
echo "[setup] done. Screenshot: $CW4/ap_shot.png"
