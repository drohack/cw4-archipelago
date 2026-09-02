#!/bin/bash
# What does each of the six ERN port upgrades actually MOVE?
#
# READ docs/in-game-testing.md FIRST.
#
# The question this answers, and the TWO separate mistakes that made an earlier
# run report "the Fire Range upgrade does nothing":
#
#   1. It watched UnitManager.RANGE, which is the BASE range and is constant by
#      design. The effective value is MYRANGE, declared on each weapon type
#      (Cannon.MYRANGE, Mortar.MYRANGE, ...) - not on the shared base class.
#   2. The ceiling was patched onto ERNInterface.GetEff, the per-port INSTANCE
#      accessor the UI reads, while the sim reads the STATIC
#      ERNInterface.GetEfficiency. Every probe read the patched one, so the
#      boost looked like it worked and the game never saw it.
#
# So this dumps a fixed set of observables before and after each upgrade and lets
# the DIFF say what moved, and ern:dump now prints BOTH efficiency accessors so
# they can never silently disagree again.
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
# For units the sweep would LIKE but can live without. A wrong data name in a
# nice-to-have unit killed a run at the setup stage after the game had already
# booted correctly; only the units an upgrade is measured ON deserve to be fatal.
optional_spawn() {
  local key="$1" want="$2" line
  send "spawn:$key $want" "SPAWN $key:"
  line=$(since | grep -a "SPAWN $key:" | tail -1)
  case "$line" in
    *"$want/$want placed"*) echo "  ok: $key $want/$want" ;;
    *) echo "  WARNING: '$key' did not place - its observable will read n/a" ;;
  esac
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
stats() { send "ern:stats" "STATS energy:"; since | grep -a "STATS " | sed 's/^.*STATS/STATS/'; }
effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"; sleep 1
    v=$(since | grep -a "ERN   \[$1\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

# A stray harness from an earlier run - or an earlier SESSION - keeps writing to
# the shared command file, and its commands land in the middle of this run's
# setup. That is not hypothetical: a stopped sweep's "ern:assign 1" arrived
# during this script's spawn phase and made a perfectly good run report
# "FATAL: 'ern' did not place".
#
# TaskStop does not reliably kill the bash child, so detect the hazard directly:
# write a sentinel and see whether anything else overwrites it. Runs BEFORE the
# game launches, so nothing is legitimately consuming the file yet.
guard_command_file() {
  local sentinel="guard-$$-$RANDOM"
  printf "%s\n" "$sentinel" > "$CMD"
  sleep 4
  if [ "$(cat "$CMD" 2>/dev/null)" != "$sentinel" ]; then
    echo "FATAL: another harness is writing to $CMD"
    echo "       it now contains: $(cat "$CMD" 2>/dev/null)"
    echo "       kill stray bash processes, then rerun:"
    echo "       Get-CimInstance Win32_Process -Filter \"Name='bash.exe'\" | Select ProcessId,CommandLine"
    exit 1
  fi
  : > "$CMD"
}

acquire_lock
echo "[all] launch"
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
guard_command_file
mkdir -p "$CW4/BepInEx/config"
printf '[Connection]\nAutoConnect = false\n\n[Debug]\nDebugCommands = true\n' \
  > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
: > "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: no menu"; exit 1; }
sleep 3

send "item:Mission Unlock: Home" "DEBUG fake item"
send "boot:story2" "LocationWatcher: mission 2"
sleep 6
# EVERY cheat is set EXPLICITLY, including the ones we want off.
#
# The dev tools persist their cheats to BepInEx/config/*devtools*.cfg, so they
# survive a relaunch. Simply OMITTING a cheat inherits whatever the last run
# left enabled. This run dropped the "set:infiniteresources=on" line and still
# measured energy pinned at store=100000 production=0 - the exact observable the
# Energy and Mine Production upgrades move - because an earlier run had already
# written InfiniteResources = true into the file.
#
# infiniteresources OFF: it is what makes energy unmeasurable.
# instantbuild OFF too, further down, when timing Build Speed.
dev "set:infiniteresources=off"
dev "set:instantbuild=on"
dev "set:allbuildings=on"; dev "set:indestructible=on"

echo "[all] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn cannon 2
require_spawn mortar 1
# 'collector', not 'miner': the miner BUTTON places a Collector prefab, and the
# build-pane key is not a data name at all. See docs/randomizer-design.md.
optional_spawn collector 2
optional_spawn sniper 1
optional_spawn missilelauncher 1

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 4

say "BASELINE, nothing docked"
stats

NAMES="Energy_Production Mine_Production Build_Speed Move_Speed Fire_Range Fire_Rate"
IDX=0
for NAME in $NAMES; do
  say "UPGRADE $IDX: $NAME"

  send "ern:release $IDX" "ERN release:"; sleep 3
  send "ern:assign $IDX" "ERN assign:"

  # Wait for this slot to reach full rather than a fixed sleep.
  for i in $(seq 1 40); do
    sleep 3
    E=$(effof "$IDX")
    [ "$E" = "1" ] && break
  done
  echo "  eff at full: ${E:-unreadable}"
  clear_ui
  echo "  -- with the upgrade at 100 percent --"
  stats

  # Now push the ceiling to 200 percent and look again.
  for i in 1 2 3 4; do send "item:ERN Efficiency Cap: $(echo "$NAME" | tr '_' ' ')" "DEBUG fake item"; done
  sleep 6
  echo "  eff with 4 boosts: $(effof "$IDX")"
  echo "  -- with the upgrade at 200 percent --"
  stats

  send "ern:release $IDX" "ERN release:"; sleep 3
  IDX=$((IDX+1))
done

clear_ui
rm -f "$CW4/ap_shot.png"; send "shot:" "SHOT:"; sleep 4
echo "[all] done. Screenshot: $CW4/ap_shot.png"
