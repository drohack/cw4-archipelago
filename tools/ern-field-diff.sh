#!/bin/bash
# Which FIELD does each unresolved ERN upgrade move? Dump everything, diff.
#
# READ docs/in-game-testing.md FIRST.
#
# Guessing the field has been wrong three times, and every wrong guess reads as
# "the upgrade does nothing":
#
#   Fire Range  moves MYRANGE, not RANGE, and MYRANGE is per weapon type
#   Fire Rate   moves COOL_DOWN - a SHOUTING-CASE name that looks like an
#               immutable base constant and is where the effective reload lands
#   Energy      does NOT move gs.energyProduction, which sits at 0 while
#               gs.energyStore visibly climbs
#
# So this stops guessing: ern:dumpall prints every primitive property of a unit
# or of GameSpace, and the diff between 0 percent and 200 percent names the
# field. Build Speed, Move Speed and Energy Production are the three still open.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"
OUT="${OUT_DIR:-.}"

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

# Capture one dump to a file so two of them can be diffed.
snap() {
  local what="$1" file="$2"
  send "ern:dumpall $what" "DUMPALL "
  sleep 2
  since | grep -a "DUMPALL   " | sed 's/^.*DUMPALL   //' | sort > "$file"
  echo "  captured $(wc -l < "$file") values -> $file"
}

# The whole point: report ONLY what changed, and say so explicitly when nothing
# did. A silent empty diff is indistinguishable from a broken capture.
showdiff() {
  local label="$1" a="$2" b="$3"
  echo "  -- $label: fields that CHANGED --"
  if diff "$a" "$b" > /dev/null 2>&1; then
    echo "     (nothing changed - $(wc -l < "$a") values compared)"
  else
    diff "$a" "$b" | grep -E "^[<>]" | sed 's/^/     /'
  fi
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
  sleep 6
  echo "  eff now $(effof "$2")"
}

acquire_lock
echo "[diff] launch"
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
dev "set:instantbuild=on"; dev "set:infiniteresources=off"

echo "[diff] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn cannon 2
require_spawn collector 4

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 6
clear_ui

# Each upgrade: snapshot at 0 percent, take it to 200 percent, snapshot again.
# Both a unit and GameSpace, because an economy upgrade may land on either.
probe_upgrade() {
  local idx="$1" name="$2" subject="$3"
  say "UPGRADE $idx: $name  (subject: $subject)"
  snap "$subject" "$OUT/d${idx}_unit_0.txt"
  snap "gs"       "$OUT/d${idx}_gs_0.txt"
  to_full "$idx"
  boost_to_max "$name" "$idx"
  snap "$subject" "$OUT/d${idx}_unit_2.txt"
  snap "gs"       "$OUT/d${idx}_gs_2.txt"
  showdiff "$name on $subject" "$OUT/d${idx}_unit_0.txt" "$OUT/d${idx}_unit_2.txt"
  showdiff "$name on GameSpace" "$OUT/d${idx}_gs_0.txt" "$OUT/d${idx}_gs_2.txt"
  send "ern:release $idx" "ERN release:"
  sleep 3
}

probe_upgrade 0 "Energy Production" collector
probe_upgrade 2 "Build Speed"       cannon
probe_upgrade 3 "Move Speed"        cannon

# Fire Rate is already settled (COOL_DOWN 8 -> 6 -> 4); included as the CONTROL.
# A diff run that cannot reproduce a known-good result is not to be trusted for
# the unknown ones.
probe_upgrade 5 "Fire Rate"         cannon

clear_ui
echo "[diff] done."
