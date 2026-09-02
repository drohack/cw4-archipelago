#!/bin/bash
# Does a boosted ceiling SHOW in the ERN port panel?
#
# READ docs/in-game-testing.md FIRST.
#
# The mechanism is settled: patching the static ERNInterface.GetEfficiency makes
# a 200 percent ceiling reach the sim, and a cannon's MYRANGE goes 9 -> 11 -> 13.
# This asks the separate question of whether the PLAYER can see it, which is
# where the designer expected the item to read visually.
#
# Two display surfaces per upgrade row, and they can disagree:
#
#   efficiencyText   a Text, free to say anything
#   efficiencyBar    a Unity Image, whose fillAmount is CLAMPED to 0..1 - so a
#                    200 percent value CANNOT overfill it
#
# Reads both, plus the raw accessor, at 0 / 100 / 200 percent.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"
SLOT=4                                  # Fire Range, the one already proven

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
uirows() { send "ern:ui" "ERN ui:"; since | grep -a "ERN ui:" | sed 's/^.*ERN ui:/ERN ui:/'; }
rangerow() { send "ern:stats" "STATS energy:"; since | grep -a "STATS unit cannon" | head -1 | sed 's/^.*STATS/STATS/'; }
effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"; sleep 1
    v=$(since | grep -a "ERN   \[$SLOT\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

acquire_lock
echo "[ui] launch"
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

echo "[ui] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn cannon 1

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 4
clear_ui

# Sample the ramp instead of only its endpoints.
#
# THE QUESTION THIS ANSWERS: the ceiling is applied as a MULTIPLY on a value the
# game ramps from 0 to 1. So a boosted slot should climb continuously to 200
# percent rather than snapping there - but if it reaches 200 in the same wall
# time that an unboosted slot reaches 100, then Boost is also doubling the FILL
# RATE, which is meant to be ERN Efficiency Rate's job. Printing tickCount beside each
# sample is what makes the two cases distinguishable.
sample_ramp() {
  local label="$1" i e t
  send "ern:release $SLOT" "ERN release:"; sleep 4
  send "ern:assign $SLOT" "ERN assign:"
  echo "  [$label] ramp samples (tick, eff, uiText, barFill, cannon MYRANGE):"
  for i in $(seq 1 18); do
    sleep 5
    t=$(send "ern:dump" "ERN sim:"; since | grep -a "ERN sim:" | tail -1 | grep -oE "tickCount=[0-9]+" | cut -d= -f2)
    e=$(effof)
    UIL=$(send "ern:ui" "ERN ui:"; since | grep -a "ERN ui: \[$SLOT\]" | tail -1 | grep -oE 'text="[^"]*" barFill=[0-9.-]+')
    MR=$(send "ern:stats" "STATS energy:"; since | grep -a "STATS unit cannon" | head -1 | grep -oE "MYRANGE=[0-9]+" | cut -d= -f2)
    echo "    tick=${t:-?} eff=${e:-?} ${UIL:-ui?} MYRANGE=${MR:-?}"
    [ "$e" = "2" ] && { echo "    (reached ceiling)"; break; }
  done
}

say "A. nothing docked"
uirows
rangerow

say "B. Fire Range docked, NO boosts - the reference ramp"
sample_ramp "no boosts"
uirows
rangerow

say "C. four ERN Efficiency Cap: Fire Range added WHILE ALREADY FULL"
echo "  (this is the case that would look like a flat jump to 200)"
for i in 1 2 3 4; do send "item:ERN Efficiency Cap: Fire Range" "DEBUG fake item"; done
sleep 6
echo "  eff=$(effof)"
uirows
rangerow

say "D. re-dock with the boosts ALREADY HELD - does it count up to 200?"
echo "  Compare the tick span here against section B. Same span to reach 200 as B"
echo "  took to reach 100 means Boost is doubling the fill rate as a side effect."
sample_ramp "4 boosts held"
uirows
rangerow

clear_ui
rm -f "$CW4/ap_shot.png"; send "shot:" "SHOT:"; sleep 4
echo "[ui] done. Screenshot: $CW4/ap_shot.png"
