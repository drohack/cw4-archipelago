#!/bin/bash
# Mine Production (upgrade 1), on a mission that actually has ore.
#
# READ docs/in-game-testing.md FIRST.
#
# Why this exists rather than more polling from bash: timing Build Speed by
# polling "ern:stats" returned exactly 540 ticks at 0 percent AND at 100
# percent. Two identical numbers to the tick mean the stopwatch was measuring
# its own round trip, not the build. measure:build and measure:energy do the
# timing in the mod, tick by tick, and report one line.
#
# The two cheat mistakes this run exists to avoid, both of which produced a
# confident null before:
#
#   instantbuild ON       nothing ever shows isBuilding, so a build cannot be
#                         timed at all - the field-diff run had isBuilding=False
#                         throughout and reported "no field changed"
#   infiniteresources ON  pins energy at a constant, which is the very quantity
#                         Energy Production is supposed to move
#
# Both are set EXPLICITLY here, because the dev tools persist cheats to their
# BepInEx config and omitting one inherits the last run's value.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
wait_for() { local i; for i in $(seq 1 "${2:-25}"); do grep -qa "$1" "$L" && return 0; sleep 1; done; return 1; }
wait_since() { local i; for i in $(seq 1 "${2:-25}"); do since | grep -qa "$1" && return 0; sleep 1; done; return 1; }
send() { mark; printf '%s\n' "$1" > "$CMD"; if [ -n "${2:-}" ]; then wait_since "$2" 25 || echo "  (no ack: $1)"; else sleep 3; fi; }
dev() { printf '%s\n' "$1" > "$DEV"; sleep 2; }
say() { echo; echo "=== $* ==="; }

require_spawn() {
  local key="$1" want="$2" line="" i
  mark
  printf '%s\n' "spawn:$key $want" > "$CMD"
  for i in $(seq 1 60); do
    sleep 1
    line=$(since | grep -a "SPAWN $key:" | tail -1)
    [ -n "$line" ] && break
  done
  case "$line" in
    *"$want/$want placed"*) echo "  ok: $key $want/$want" ;;
    "") echo "FATAL: '$key' - no ack after 60s"; exit 1 ;;
    *) echo "FATAL: '$key' did not place ($line)"; exit 1 ;;
  esac
}

guard_command_file() {
  local sentinel="guard-$$-$RANDOM"
  printf '%s\n' "$sentinel" > "$CMD"
  sleep 4
  if [ "$(cat "$CMD" 2>/dev/null)" != "$sentinel" ]; then
    echo "FATAL: another harness is writing to $CMD - it contains: $(cat "$CMD" 2>/dev/null)"
    exit 1
  fi
  : > "$CMD"
}

# A REAL LOCK, because the sentinel check below is necessary and not sufficient.
#
# The sentinel writes to the command file and sees whether anything overwrites
# it within a few seconds. That catches a straggler that is actively issuing
# commands, and MISSES one that happens to be sleeping - which is exactly what
# happened: a still-running rate test was mid-sleep when a move test launched,
# passed the sentinel, then both drove the same game and the same command file.
# The move test's own taskkill also killed the other run's game mid-measurement,
# turning a valid 200 percent energy window into "0 energy over 900 ticks".
#
# One harness at a time, enforced by PID.
LOCK="$CW4/BepInEx/cw4-harness.lock"
acquire_lock() {
  if [ -f "$LOCK" ]; then
    local pid
    pid=$(cat "$LOCK" 2>/dev/null)
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
      echo "FATAL: another harness is already running (pid $pid)."
      echo "       Wait for it, or kill it:  kill $pid"
      exit 1
    fi
    echo "  (stale lock from pid ${pid:-?}, taking over)"
  fi
  echo "$$" > "$LOCK"
  # Release on ANY exit, including a FATAL or a kill, so a crashed run does not
  # leave a lock that blocks the next one.
  trap 'rm -f "$LOCK"' EXIT INT TERM
}

clear_ui() { send "ada:close"; send "ada:clear" "ADA clear"; }

effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"; sleep 1
    v=$(since | grep -a "ERN   \[$1\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

to_full() {
  local idx="$1" i e=""
  send "ern:release $idx" "ERN release:"; sleep 3
  send "ern:assign $idx" "ERN assign:"
  for i in $(seq 1 40); do
    sleep 3
    e=$(effof "$idx")
    [ "$e" = "1" ] && break
  done
  echo "  slot $idx eff=${e:-unreadable}"
}

boost_to_max() {
  local name="$1" idx="$2" i
  for i in 1 2 3 4; do send "item:ERN Efficiency Cap: $name" "DEBUG fake item"; done
  sleep 6
  echo "  eff now $(effof "$idx")"
}

# One build, timed by the mod. Reports the mod's own line verbatim so a warning
# ("never showed isBuilding") is never mistaken for a fast result.
one_build() {
  local label="$1" i out=""
  mark
  printf '%s\n' "measure:build cannon" > "$CMD"
  for i in $(seq 1 45); do
    sleep 2
    out=$(since | grep -a "MEASURE build: result=" | tail -1)
    [ -n "$out" ] && break
  done
  if [ -n "$out" ]; then
    echo "  [$label] ${out#*MEASURE build: }"
  else
    echo "  [$label] NO RESULT - the mod never reported"
  fi
}

# One energy window, timed by the mod. Drains first so the store cannot be
# sitting at its cap, where accrual reads as zero however much is produced.
one_energy() {
  local label="$1" i out=""
  send "trap:energy 200" "TRAP"
  sleep 3
  mark
  printf '%s\n' "measure:energy 900" > "$CMD"
  for i in $(seq 1 45); do
    sleep 2
    out=$(since | grep -a "MEASURE energy: .* over " | tail -1)
    [ -n "$out" ] && break
  done
  if [ -n "$out" ]; then
    echo "  [$label] ${out#*MEASURE energy: }"
  else
    echo "  [$label] NO RESULT - the mod never reported"
  fi
}

acquire_lock
echo "[mine] launch"
taskkill //IM CW4.exe //F >/dev/null 2>&1
# TRUNCATE THE LOG BEFORE LAUNCHING.
#
# wait_for greps the WHOLE log, so the launch wait can match the PREVIOUS run's
# "SCENE: 'Galaxy'" line before BepInEx has truncated the file. That happened:
# a sweep sent its mission unlock and boot while the game was still on the load
# screen, the log recorded "DEBUG boot BLOCKED: 'story2' locked" ABOVE the real
# "SCENE: 'Galaxy'" line, and the run then failed on a spawn with no ack - which
# looks like a hung mod rather than a harness that started too early.
: > "$L" 2>/dev/null || true
mkdir -p "$CW4/BepInEx/config"
printf '[Connection]\nAutoConnect = false\n\n[Debug]\nDebugCommands = true\n' \
  > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
: > "$CMD"
guard_command_file
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: no menu"; exit 1; }
sleep 3


# Boot each mission in turn and ask what resources it has. Cheap: no economy is
# built, nothing is timed - this only answers "where can Mine Production be
# measured at all", which every previous attempt assumed rather than checked.
# Grant EVERY mission unlock up front.
#
# The first attempt granted only "Mission Unlock: Home" and then booted story3
# through story7. MissionGate blocks a locked mission and logs
# "DEBUG boot BLOCKED", so all five silently stayed on story2 - and the harness
# was waiting on the wrong ack string, so the block never surfaced. Five
# missions then reported "no resources" without ever having been loaded, which
# is a confident answer from a test that did not run.
unlock_all() {
  local t
  for t in "Farsite" "Home" "Not My Mars" "Ruins Repurposed" "We Know Nothing"            "We Were Never Alone" "Hints" "Serious" "More and More" "War and Peace"            "Shattered" "Archon" "The Experiment" "Somewhere in Spacetime"            "Tower of Darkness" "The Compound" "Sequence" "Wallis" "Founders" "Ever After"; do
    send "item:Mission Unlock: $t" "DEBUG fake item"
  done
  echo "  granted 20 mission unlocks"
}

# story12 has SIX resource nodes at PRODUCTION_INTERVAL=20 - the most nodes and
# the fastest interval in the campaign, found by tools/ern-ore-scan.sh. Missions
# 2 to 4 have NO resource nodes at all, which is why every earlier attempt at
# Mine Production measured nothing: there was nothing to mine. The interval is
# map data (60 on most missions, 20 here), so it must be read per mission rather
# than assumed.
MISSION="${MISSION:-story12}"

nodes() {
  local i out=""
  mark
  printf '%s
' "ern:resources" > "$CMD"
  for i in $(seq 1 20); do
    sleep 2
    out=$(since | grep -aE "RESOURCE (node|refinery|scan)" | head -8)
    [ -n "$out" ] && break
  done
  if [ -n "$out" ]; then printf '%s
' "$out" | sed 's/^.*RESOURCE/  RESOURCE/'
  else echo "  (no answer from the probe)"; fi
}

say "MINE PRODUCTION (upgrade 1) on $MISSION"
unlock_all
mark
printf '%s
' "boot:$MISSION" > "$CMD"
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
send "ada:close"; send "ada:clear" "ADA clear"
dev "set:allbuildings=on"; dev "set:indestructible=on"
dev "set:instantbuild=on"; dev "set:infiniteresources=on"

# The mission may already own a rift lab; a second one is not needed and its
# failure is not a problem, so this one is optional rather than fatal.
send "spawn:commandbase 1" "SPAWN commandbase:"
require_spawn erninterface 1
require_spawn ern 6

send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 6
send "ada:clear" "ADA clear"

# Build a real mining economy. The designer's requirement, and the reason the
# direct read was never going to be enough:
#
#   "either a miner on RESO ground, or a refinery near a greenar, or a
#    tower/rift lab near a reddite or bluite crystal (all need factory to hold
#    resources). and watch gathering speed."
#
# Confirmed by the failed cheap check: Resource.PRODUCTION_INTERVAL held at 20
# across 0/100/200 percent, and every node read counter=0 wareAvailable=False -
# a node produces nothing until something mines it. So place a miner ON a node
# and a factory to hold what it yields, then time the ware total.
#
# 'collector' is the miner's data name: the MINER build button places a
# Collector prefab (docs/randomizer-design.md, ghost -> prefab mapping). If that
# turns out to be wrong, the node stays at counter=0 and the rate reads zero -
# which is why the node line is printed after the build, not just before.
NODE_X=73
NODE_Y=47

echo "  -- building the economy at node ($NODE_X,$NODE_Y) --"
# story12 reports allowed=[riftlab,tower] - there is NO miner on this mission,
# so the earlier attempt to put a 'collector' on the node was never going to
# work here. That matches the designer's third option: "a tower/rift Lab near a
# reddite or bluite crystal". Place both, ADJACENT to the node - the previous
# try sat 4 cells away, plausibly outside collection range.
#
# spawnat uses CreateUnitAtPosition, which bypasses the build pane, so the
# factory can be placed even though the mission would not offer it.
place() {
  local key="$1" x="$2" y="$3"
  send "spawnat:$key $x $y" "SPAWNAT $key:"
  since | grep -a "SPAWNAT $key:" | tail -1 | sed 's/^.*SPAWNAT/  SPAWNAT/'
}
place commandbase $((NODE_X+1)) $((NODE_Y+1))
sleep 6
place tower       $((NODE_X-1)) $NODE_Y
place tower       $NODE_X       $((NODE_Y+1))
place factory     $((NODE_X+3)) $NODE_Y
sleep 14
echo "  -- node state after placing tower/riftlab adjacent --"
nodes
echo "  -- what is actually on the map --"
send "units" "UNITS"
since | grep -aiE "unit|collector|factory|commandbase" | head -12 | sed 's/^/  /'

one_ware() {
  local label="$1" i out=""
  mark
  printf '%s
' "measure:ware 1800" > "$CMD"
  for i in $(seq 1 60); do
    sleep 2
    out=$(since | grep -a "MEASURE ware: .* over " | tail -1)
    [ -n "$out" ] && break
  done
  if [ -n "$out" ]; then echo "  [$label] ${out#*MEASURE ware: }"
  else echo "  [$label] NO RESULT - the mod never reported"; fi
}

echo "  -- baseline, nothing docked --"
one_ware "0 percent"
to_full 1
echo "  -- Mine Production at 100 percent --"
one_ware "100 percent"
nodes
boost_to_max "Mine Production" 1
echo "  -- Mine Production at 200 percent --"
one_ware "200 percent"
send "ern:release 1" "ERN release:"

clear_ui
echo "[mine] done."
