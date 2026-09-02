#!/bin/bash
# Do the one-shot boons do what they claim, and ONLY that?
#
# READ docs/in-game-testing.md FIRST.
#
# "And only that" is the point. Two of these effects were doing more than they
# advertised, in opposite directions, and neither was visible from its own log:
#
#   Ammo Resupply  also filled the RIFT LAB, whose "ammo" is the energy store -
#                  so it was a silent free full-energy refill on top of the ammo
#   trap:drain     the same bug with the opposite sign: it emptied the whole
#                  economy to zero, which "Ammo Drain" does not claim to do
#
# So every check here reads the thing the effect should move AND the thing it
# should not.
#
#   1  Ammo Resupply   weapon ammo rises, rift-lab ammo does NOT
#   2  Energy Cache    cb.ammo rises, clamps at max, no-op when full
#   3  Field Shield    impervious false -> true -> false, map content untouched
#
# Energy is read from cb.ammo, never gs.energyStore: that one is a summary the
# sim recomputes every tick, so writing it moves the HUD and delivers nothing -
# which is also why trap:energy is NOT used to set up the Energy Cache test.
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
echo "[boon] launch"
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
# EVERY cheat set EXPLICITLY, and two of them deliberately OFF. The dev tools
# persist cheats to their BepInEx config, so omitting a line inherits whatever
# the last harness left on - and the first run of this script measured nothing
# for exactly that reason:
#
#   infiniteresources ON  pegged the store at 100000 AND re-topped weapon ammo
#                         instantly, so trap:drain read 10 instead of 0 and
#                         "BOON resupply: filled 0 weapon(s)" looked like a
#                         broken effect rather than a full one
#   indestructible ON     had impervious=True BEFORE the shield went up, so the
#                         snapshot captured True, the restore put back True, and
#                         the expiry test could never show False
#
# Both are the quantities under test here. instantbuild stays ON so spawned
# units are real weapons rather than ghosts.
dev "set:allbuildings=on"
dev "set:instantbuild=on"
dev "set:infiniteresources=off"
dev "set:indestructible=off"

echo "[boon] place"
# STATE selects what infrastructure exists. That is the whole experiment.
#
#   A  portal + ERN docked      the control - the proven path
#   B  portal, no ERN           does the surge need something DOCKED?
#   C  no portal at all         does the surge need a PORTAL?
#
# ern:cap cannot substitute for any of this: CeilingOverride is only read
# inside ComputeEffective, which runs only in the per-port loop for a docked
# slot, so with no portal it does nothing at all.
STATE="${STATE:-C}"   # C: no portal needed for these
require_spawn commandbase 1
case "$STATE" in
  A) require_spawn erninterface 1; require_spawn ern 6 ;;
  B) require_spawn erninterface 1 ;;
  C) echo "  (no erninterface, no ern - the point of state C)" ;;
  *) echo "FATAL: STATE must be A, B or C"; exit 1 ;;
esac
require_spawn cannon 3
require_spawn mortar 1

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

# ern:dumpall prints every primitive property, bools included, so impervious and
# DESTROY_ON_UNEVEN_TERRAIN need no new probe code.
dumpone() {
  local what="$1"
  send "ern:dumpall $what" "DUMPALL "
  sleep 2
  since | grep -a "DUMPALL   " | sed 's/^.*DUMPALL   //'
}
field() {
  printf '%s\n' "$2" | grep -a "\.$1=" | head -1 | cut -d= -f2
}
cannonammo() {
  send "ern:stats" "STATS energy:"
  since | grep -a "STATS unit cannon" | head -1 | grep -oE "ammo=[0-9.]+" | cut -d= -f2
}

say "0. CHEAT STATE - assert before measuring"
# The first run of this harness was invalid because two cheats were on. Reading
# them back is cheaper than another wasted run.
LABC=$(dumpone commandbase)
echo "  rift lab ammo=$(field ammo "$LABC") max=$(field MAX_AMMO "$LABC")"
echo "  cannon impervious=$(field impervious "$(dumpone cannon)")"
echo "  a max of 100000 means infiniteresources is STILL ON - stop and fix it"
echo "  impervious=True here means indestructible is STILL ON - same"

say "1. AMMO RESUPPLY - fills weapons, and must NOT fill the rift lab"
# The rift lab's "ammo" IS the energy store, so a resupply that touches it is a
# silent free energy refill. That was the bug; this is the regression test.
LAB0=$(dumpone commandbase)
echo "  rift lab before: ammo=$(field ammo "$LAB0") / MAX_AMMO=$(field MAX_AMMO "$LAB0")"
send "trap:drain" "TRAP drain:"
since | grep -a "TRAP drain:" | tail -1 | sed 's/^.*TRAP/  TRAP/'
LAB1=$(dumpone commandbase)
echo "  rift lab after drain: ammo=$(field ammo "$LAB1")   (drain must not empty it either)"
echo "  cannon ammo after drain: $(cannonammo)   (expect 0)"
send "boon:ammo" "BOON resupply:"
since | grep -a "BOON resupply:" | tail -1 | sed 's/^.*BOON/  BOON/'
LAB2=$(dumpone commandbase)
echo "  cannon ammo after resupply: $(cannonammo)   (expect full)"
echo "  rift lab after resupply: ammo=$(field ammo "$LAB2")"
echo "  VERDICT: lab ammo must be ~unchanged across all three reads"

say "2. ENERGY CACHE - measured on the REAL field, not the summary"
# cb.ammo is the store. gs.energyStore is a summary the sim recomputes every
# tick, which is why trap:energy is NOT used to set up this test - writing that
# field moves the HUD and delivers nothing, so a drain-then-fire test would
# have been measuring its own no-op.
#
# No drain is needed anyway: a mission's store starts below its ceiling, so the
# gain is visible directly.
E0=$(dumpone commandbase)
A0=$(field ammo "$E0"); M0=$(field MAX_AMMO "$E0")
echo "  store before: ammo=$A0 / max=$M0"
send "boon:energy 0.25" "BOON energy cache:"
since | grep -a "BOON energy cache:" | tail -1 | sed 's/^.*BOON/  BOON/'
E1=$(dumpone commandbase)
echo "  store after +25 percent: ammo=$(field ammo "$E1") / max=$(field MAX_AMMO "$E1")"
echo "  VERDICT: ammo must have RISEN, and never above max"
send "boon:energy 1.0" "BOON energy cache:"
since | grep -a "BOON energy cache:" | tail -1 | sed 's/^.*BOON/  BOON/'
E2=$(dumpone commandbase)
echo "  store after +100 percent: ammo=$(field ammo "$E2") / max=$(field MAX_AMMO "$E2")"
echo "  VERDICT: clamped at max, and the log should say so"
send "boon:energy 0.5" "BOON energy cache:"
since | grep -a "BOON energy cache:" | tail -1 | sed 's/^.*BOON/  BOON/'
echo "  (fired again while full: this is the known weakness - a no-op)"

say "3. FIELD SHIELD - impervious on, then off again"
S0=$(dumpone cannon)
echo "  before: impervious=$(field impervious "$S0") uneven=$(field DESTROY_ON_UNEVEN_TERRAIN "$S0")"
send "boon:shield" "BOON shield:"
since | grep -a "BOON shield:" | tail -1 | sed 's/^.*BOON/  BOON/'
S1=$(dumpone cannon)
echo "  during: impervious=$(field impervious "$S1") uneven=$(field DESTROY_ON_UNEVEN_TERRAIN "$S1")"
echo "  (expect impervious=True, uneven=False)"

say "3b. AND DOES IT SURVIVE A REAL ATTACK?"
send "trap:building 0 3" "TRAP"
sleep 6
send "ern:stats" "STATS energy:"
echo "  units still present after a spore strike on a building:"
since | grep -ac "STATS unit" | sed 's/^/    /'

say "3c. AND DOES IT EXPIRE? (900 sim ticks)"
for i in $(seq 1 10); do
  sleep 8
  S2=$(dumpone cannon)
  IMP=$(field impervious "$S2")
  echo "  t+$((i*8))s impervious=$IMP"
  [ "$IMP" = "False" ] && { echo "  restored cleanly"; break; }
done

say "4. MAP CONTENT MUST BE UNTOUCHED"
echo "  the shield log above reports how many non-player objects were left"
echo "  alone; an indestructible map object can make a mission unwinnable."
since | grep -a "BOON shield:" | tail -1 | grep -oE "[0-9]+ non-player object\(s\) left alone" | sed 's/^/    /'

clear_ui
echo "[boon] done."
