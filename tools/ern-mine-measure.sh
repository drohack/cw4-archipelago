#!/bin/bash
# Measure Mine Production against a mining economy someone else built.
#
# Run tools/ern-mine-setup.sh, build the mining units by hand, then run this. It
# does NOT launch or reboot the game - it attaches to the running one, because
# the whole point is to measure a set-up the harness could not create. Killing
# the game would destroy the thing being measured, so nothing here rebuilds the
# mod either.
#
# READ docs/in-game-testing.md FIRST.
#
# WHAT IS READ, AND WHY IT IS THE UI. Two earlier observables failed:
#
#   Resource.PRODUCTION_INTERVAL   never moves - the upgrade is not written
#                                  into the node, unlike Fire Rate's COOL_DOWN
#   total ware held (measure:ware) read a flat 0 -> 0 even while the nodes were
#                                  visibly producing (counter=24), so
#                                  GetWareHeld does not see the factory's
#                                  contents. It would ALSO have saturated once
#                                  the factory filled, which the designer
#                                  spotted before it wasted a run.
#
# The factory build button shows a live RATE, and a rate does not saturate when
# storage fills:
#
#   SingletonUnits/Buttons/Factory/Amts/BlueProduction   "+2.9"
#   SingletonUnits/Buttons/Factory/Amts/RedProduction    "+2.8"
#
# That is also exactly the number the designer reads on screen, so there is no
# translation between what is measured and what is observed.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"
SLOT=1                        # Mine Production

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
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
  trap 'rm -f "$LOCK"' EXIT INT TERM
}

# One reading of both production rates, as "blue red".
read_rate() {
  local out b r
  mark
  printf '%s\n' "ui:text +" > "$CMD"
  sleep 3
  out=$(since | grep -a "UI text" | grep -aE "Production\]")
  b=$(printf '%s\n' "$out" | grep -a "BlueProduction" | tail -1 | grep -oE '"\+?-?[0-9.]+"' | tr -d '"+')
  r=$(printf '%s\n' "$out" | grep -a "RedProduction"  | tail -1 | grep -oE '"\+?-?[0-9.]+"' | tr -d '"+')
  printf '%s %s' "${b:-}" "${r:-}"
}

# Wait for the rate to STOP MOVING before believing it.
#
# This is not optional: sampled immediately after the setup the rate read 2.1,
# then 2.3, then 2.9, then 3.0 as the economy spun up. Comparing a level taken
# during that climb against one taken after it would show a difference that has
# nothing to do with the upgrade - the single most likely way to fake a result
# here.
stable_rate() {
  local label="$1" i cur last="" same=0
  for i in $(seq 1 40); do
    cur=$(read_rate)
    case "$cur" in " "|"") echo "  [$label] could not read the rate"; return 1 ;; esac
    if [ "$cur" = "$last" ]; then
      same=$((same+1))
      if [ "$same" -ge 3 ]; then
        echo "  [$label] STABLE at blue=$(echo "$cur" | cut -d' ' -f1) red=$(echo "$cur" | cut -d' ' -f2)"
        return 0
      fi
    else
      same=0
    fi
    last="$cur"
  done
  echo "  [$label] NEVER SETTLED - last read blue/red = $last (treat as unusable)"
  return 1
}

effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"; sleep 1
    v=$(since | grep -a "ERN   \[$SLOT\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}

nodes() {
  local i out=""
  mark
  printf '%s\n' "ern:resources" > "$CMD"
  for i in $(seq 1 20); do
    sleep 2
    out=$(since | grep -aE "RESOURCE (node|refinery|scan)" | head -4)
    [ -n "$out" ] && break
  done
  printf '%s\n' "${out:-  (no answer)}" | sed 's/^.*RESOURCE/  RESOURCE/'
}

acquire_lock

# infiniteresources tops up held wares. It does not affect the RATE readout, but
# it is re-asserted off so the run is comparable with the earlier ones.
dev "set:infiniteresources=off"
sleep 2

say "IS IT MINING?"
nodes
echo "  counter moving means yes. It read 0 on every automated attempt and 24"
echo "  once the units were placed by hand."

say "BASELINE - Mine Production released"
send "ern:release $SLOT" "ERN release:"
sleep 5
stable_rate "0 percent"

say "100 PERCENT - ERN docked and ramped to full"
send "ern:assign $SLOT" "ERN assign:"
E=""
for i in $(seq 1 60); do
  sleep 3
  E=$(effof)
  [ "$E" = "1" ] && break
done
echo "  eff=${E:-unreadable}"
stable_rate "100 percent"

say "200 PERCENT - four Progressive ERN Efficiency Cap: Mine Production"
for i in 1 2 3 4; do send "item:Progressive ERN Efficiency Cap: Mine Production" "DEBUG fake item"; done
sleep 5
# The cap now EXTENDS the ramp rather than steepening it, so 200 percent takes a
# second full 3600 ticks to arrive. Wait for it instead of assuming.
E=""
for i in $(seq 1 80); do
  sleep 3
  E=$(effof)
  [ "$E" = "2" ] && break
done
echo "  eff=${E:-unreadable}"
stable_rate "200 percent"

send "ern:release $SLOT" "ERN release:"
say "DONE"
echo "  The game is left running and the mining set-up untouched."
