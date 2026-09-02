#!/bin/bash
# Build Speed and Energy Production, timed INSIDE the game loop.
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
  for i in 1 2 3 4; do send "item:Progressive ERN Efficiency Cap: $name" "DEBUG fake item"; done
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
echo "[rate] launch"
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
dev "set:allbuildings=on"; dev "set:indestructible=on"
dev "set:instantbuild=on"; dev "set:infiniteresources=on"

echo "[rate] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn collector 6

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 8
clear_ui

# ENERGY FIRST, BUILD SECOND - the order is load-bearing.
#
# Running build first left several cannons standing, each requesting ammo
# packets, and their consumption came straight off the energy store. The
# store delta is production MINUS use, so the energy rate went from a clean
# 0.053/0.070/0.087 in one run to 0.043/0.021/0.033 in the next with nothing
# about the upgrade changed. Measure energy while the base is quiet.

# -------------------------------------------------------- Energy Production
say "ENERGY PRODUCTION (upgrade 0)"
dev "set:instantbuild=on"
dev "set:infiniteresources=off"     # or there is nothing to measure
sleep 4
one_energy "0 percent"
to_full 0
one_energy "100 percent"
boost_to_max "Energy Production" 0
one_energy "200 percent"
send "ern:release 0" "ERN release:"

# ------------------------------------------------------------- Build Speed
say "BUILD SPEED (upgrade 2)"
dev "set:instantbuild=off"
dev "set:infiniteresources=on"      # energy must NOT be the limiter here
sleep 3
# TWICE per level. A single build per level gave 363 / 186 / 33 ticks, and the
# 33 was an artifact of every build reusing one cell - repeats are what make an
# outlier visible instead of quotable.
one_build "0 percent a"; one_build "0 percent b"
to_full 2
one_build "100 percent a"; one_build "100 percent b"
boost_to_max "Build Speed" 2
one_build "200 percent a"; one_build "200 percent b"
send "ern:release 2" "ERN release:"
sleep 3

clear_ui
echo "[rate] done."
