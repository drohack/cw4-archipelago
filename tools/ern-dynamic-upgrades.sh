#!/bin/bash
# The five ERN upgrades that a STATIC snapshot cannot measure.
#
# READ docs/in-game-testing.md FIRST.
#
# tools/ern-all-upgrades.sh settled Fire Range, because range is the one upgrade
# with an effective-value property to read (Cannon.MYRANGE and friends). It
# reported the other five as "moved nothing", which was a limit of the test, not
# a result: a snapshot cannot see build speed with nothing building, move speed
# with nothing moving, or fire rate with nothing firing. A metadata search
# confirmed there is no MYBUILDRATE / MYSPEED / MYFIRERATE anywhere - those
# upgrades are applied inline in native code - so each one has to be TIMED.
#
#   Energy Production  gs.energyProduction, with collectors and no energy cheat
#   Build Speed        ticks for isBuilding to go true -> false
#   Move Speed         ticks for an assigned ERN to reach its slot
#   Fire Rate          the largest Cannon.coolDown seen while shooting creeper
#   Mine Production    NOT COVERED - needs an ore deposit, see the end
#
# Each is measured at 0 percent and again at a 200 percent ceiling, so the
# comparison is within one run and one mission.
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
send() { mark; printf "%s\n" "$1" > "$CMD"; if [ -n "${2:-}" ]; then wait_since "$2" 25 || echo "  (no ack: $1)"; else sleep 3; fi; }
dev() { printf "%s\n" "$1" > "$DEV"; sleep 2; }
say() { echo; echo "=== $* ==="; }

# NO ACK and PLACED-ZERO are different failures and must not share a branch.
# The 25s ack window is not always enough - the game hitches while a mission
# finishes loading - so a spawn that genuinely succeeded reported
# "FATAL: 'erninterface' did not place ()" with EMPTY parens, while the log had
# "SPAWN erninterface: 1/1 placed" three lines further on. Poll for the ack
# rather than waiting a fixed time, and say which of the two happened.
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
    "") echo "FATAL: '$key' - no ack after 60s (game hung, or the mod is not reading commands)"; exit 1 ;;
    *) echo "FATAL: '$key' did not place ($line)"; exit 1 ;;
  esac
}

# See tools/ern-all-upgrades.sh - a stopped harness keeps writing to the shared
# command file and lands its commands in the middle of THIS run's setup.
guard_command_file() {
  local sentinel="guard-$$-$RANDOM"
  printf "%s\n" "$sentinel" > "$CMD"
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

# The sim clock, which is the only honest stopwatch here: wall-clock seconds
# include our own polling latency and stop meaning anything if the game pauses.
ticknow() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN sim:"; sleep 1
    v=$(since | grep -a "ERN sim:" | tail -1 | grep -oE "tickCount=[0-9]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"; sleep 1
    v=$(since | grep -a "ERN   \[$1\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

statlines() { send "ern:stats" "STATS energy:"; since | grep -a "STATS " | sed 's/^.*STATS/STATS/'; }

# Dock a slot and wait for it to reach full, so a measurement is never taken
# against a half-ramped upgrade.
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
  local name="$1" i
  for i in 1 2 3 4; do send "item:Progressive ERN Efficiency Cap: $name" "DEBUG fake item"; done
  sleep 5
}

acquire_lock
echo "[dyn] launch"
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

send "item:Mission Unlock: Home" "DEBUG fake item"
send "boot:story2" "LocationWatcher: mission 2"
sleep 6

# EXPLICIT, every one of them. The dev tools persist cheats to their BepInEx
# config, so omitting a line inherits whatever the previous run left on - which
# is how a run that had deliberately dropped "infiniteresources" still measured
# energy pinned at store=100000 production=0.
dev "set:allbuildings=on"; dev "set:indestructible=on"
dev "set:instantbuild=on"; dev "set:infiniteresources=on"

echo "[dyn] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn cannon 2

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 4
clear_ui

# ---------------------------------------------------------------- Energy
# Needs the energy cheat OFF, which is the whole reason it is measured here and
# not in the static sweep.
say "ENERGY PRODUCTION (upgrade 0)"
dev "set:infiniteresources=off"
sleep 3
require_spawn collector 4
sleep 12                      # let them build and join the network
echo "  -- baseline, nothing docked --"
statlines | grep -a "energy:"
to_full 0
echo "  -- upgrade 0 at 100 percent --"
statlines | grep -a "energy:"
boost_to_max "Energy Production"
echo "  eff now $(effof 0)"
echo "  -- upgrade 0 at 200 percent --"
statlines | grep -a "energy:"
send "ern:release 0" "ERN release:"
dev "set:infiniteresources=on"

# ---------------------------------------------------------------- Build speed
# instantbuild OFF or there is nothing to time. infiniteresources stays ON so
# what is measured is build SPEED and not energy starvation.
say "BUILD SPEED (upgrade 2)"
dev "set:instantbuild=off"
sleep 3

time_a_build() {
  local label="$1" t0 t1 i saw=0
  send "spawn:cannon 1" "SPAWN cannon:"
  t0=$(ticknow)
  for i in $(seq 1 40); do
    if statlines | grep -a "building=True" > /dev/null; then saw=1; break; fi
    sleep 2
  done
  if [ "$saw" != "1" ]; then echo "  [$label] never saw building=True - cannot time"; return 1; fi
  for i in $(seq 1 60); do
    sleep 2
    statlines | grep -a "building=True" > /dev/null || break
  done
  t1=$(ticknow)
  if [ -n "$t0" ] && [ -n "$t1" ]; then
    echo "  [$label] built in $((t1 - t0)) ticks"
  else
    echo "  [$label] tick read failed (t0=$t0 t1=$t1)"
  fi
}

time_a_build "build speed 0 percent"
to_full 2
time_a_build "build speed 100 percent"
boost_to_max "Build Speed"
echo "  eff now $(effof 2)"
time_a_build "build speed 200 percent"
send "ern:release 2" "ERN release:"
dev "set:instantbuild=on"

# ---------------------------------------------------------------- Move speed
# An assigned ERN flies from where it sits to the port, so its travel time is a
# movement measurement that needs no new command. Whether the MOVE_SPEED upgrade
# applies to ERNs at all is exactly the open question.
say "MOVE SPEED (upgrade 3)"
time_ern_travel() {
  local label="$1" t0 t1 i e
  send "ern:release 3" "ERN release:"; sleep 4
  t0=$(ticknow)
  send "ern:assign 3" "ERN assign:"
  for i in $(seq 1 40); do
    sleep 2
    e=$(effof 3)
    [ -n "$e" ] && [ "$e" != "0" ] && break
  done
  t1=$(ticknow)
  if [ -n "$t0" ] && [ -n "$t1" ]; then
    echo "  [$label] ERN reached its slot in $((t1 - t0)) ticks"
  else
    echo "  [$label] tick read failed"
  fi
}
time_ern_travel "move speed 0 percent"
boost_to_max "Move Speed"
time_ern_travel "move speed 200 percent"
send "ern:release 3" "ERN release:"

# ---------------------------------------------------------------- Fire rate
# LAST, because it puts creeper on the map. The observable is the cannon's
# reload: coolDown counts down after a shot, so the largest value sampled is the
# effective reload and COOL_DOWN beside it is the base.
say "FIRE RATE (upgrade 5)"
dev "set:infiniteresources=on"      # ammo stays topped up so it keeps shooting
send "trap:creep" "TRAP creep:"
sleep 6

max_cooldown() {
  local label="$1" i best=0 v
  for i in $(seq 1 12); do
    v=$(statlines | grep -a "coolDown=" | grep -oE "coolDown=[0-9]+" | cut -d= -f2 | sort -n | tail -1)
    if [ -n "$v" ] && [ "$v" -gt "$best" ] 2>/dev/null; then best="$v"; fi
    sleep 1
  done
  echo "  [$label] largest coolDown seen: $best"
  statlines | grep -a "coolDown=" | head -2
}

max_cooldown "fire rate 0 percent"
to_full 5
max_cooldown "fire rate 100 percent"
boost_to_max "Fire Rate"
echo "  eff now $(effof 5)"
max_cooldown "fire rate 200 percent"

say "MINE PRODUCTION (upgrade 1) - NOT COVERED"
echo "  Needs a miner sitting on an ore deposit, and the mission booted here has"
echo "  no deposit under the spawn point. Measuring it needs a mission chosen for"
echo "  its ore, or a deposit placed - neither is a spawn command today."

clear_ui
rm -f "$CW4/ap_shot.png"; send "shot:" "SHOT:"; sleep 4
echo "[dyn] done. Screenshot: $CW4/ap_shot.png"
