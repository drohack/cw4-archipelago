#!/bin/bash
# Measure CW4's ERN port upgrades, and what our two items do to them.
#
# READ docs/in-game-testing.md BEFORE CHANGING THIS. Every guard below was added
# after a run produced confident numbers from a game that was paused, empty, or
# hidden behind a modal.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEV="$CW4/BepInEx/cw4dev-commands.txt"
SLOT="${SLOT:-4}"                 # 4 = Fire Range
POLL="${POLL:-3}"

MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() {
  local c; c=$(wc -l < "$L" 2>/dev/null || echo 0)
  [ "$c" -lt "$MARK" ] && MARK=0
  tail -n +"$((MARK+1))" "$L" 2>/dev/null
}

# Whole-log search. ONLY correct for the first command of its kind - after that
# it matches the previous run's line and returns instantly.
wait_for() { local i; for i in $(seq 1 "${2:-25}"); do grep -qa "$1" "$L" && return 0; sleep 1; done; return 1; }

# Search only what arrived after the mark. This is the one to use for anything
# repeated: the whole-log version made every poll return on a stale line, so the
# read that followed found nothing and a live ramp was reported as eff=0.
wait_since() { local i; for i in $(seq 1 "${2:-25}"); do since | grep -qa "$1" && return 0; sleep 1; done; return 1; }

# Marks FIRST, so "since" after a send means "the output of this command".
send() {
  mark
  printf "%s\n" "$1" > "$CMD"
  if [ -n "${2:-}" ]; then wait_since "$2" 25 || echo "  (no ack: $1)"; else sleep 3; fi
}
dev() { printf "%s\n" "$1" > "$DEV"; sleep 2; }
say() { echo; echo "=== $* ==="; }

# A spawn that places 0/1 MUST stop the run. Two whole measurements were invalid
# because a silent 0/1 meant no rift lab (so the mission never started) or no ERN
# port (so there was nothing to measure), and the script carried on reporting
# zeros that looked like real data.
require_spawn() {
  local key="$1" want="$2" line
  send "spawn:$key $want" "SPAWN $key:"
  line=$(since | grep -a "SPAWN $key:" | tail -1)
  case "$line" in
    *"$want/$want placed"*) echo "  ok: $key $want/$want" ;;
    *) echo "FATAL: '$key' did not place ($line)"; exit 1 ;;
  esac
}

# ADA panels reopen as the mission fires story messages, and opening the log
# PAUSES the sim. Clear them, then sim:hold keeps it running regardless.
clear_ui() { send "ada:close"; send "ada:clear" "ADA clear"; }

# Retries, and returns EMPTY rather than 0 when it cannot read. The log lands a
# frame behind the ack, so a read occasionally catches it mid-flush.
# UNKNOWN AND ZERO MUST NEVER BE THE SAME VALUE - conflating them reported a
# ramp sitting at 0.294 as "eff=0".
effof() {
  local v i
  for i in 1 2 3; do
    send "ern:dump" "ERN dump:"
    sleep 1
    v=$(since | grep -a "ERN   \[$SLOT\]" | tail -1 | grep -oE "eff=[0-9.]+" | cut -d= -f2)
    [ -n "$v" ] && { printf "%s" "$v"; return 0; }
  done
  printf ""
}
slotline() { send "ern:dump" "ERN dump:"; since | grep -a "ERN   \[$SLOT\]" | tail -1 | sed 's/^.*ERN   /ERN /'; }
simline() { send "ern:dump" "ERN sim:"; since | grep -a "ERN sim:" | tail -1 | sed 's/^.*ERN/ERN/'; }

echo "[ern] launch"
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
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
wait_for "SCENE: 'Galaxy'" 90 || { echo "FATAL: never reached the menu"; exit 1; }
sleep 3

echo "[ern] boot"
send "item:Mission Unlock: Home" "DEBUG fake item"
send "boot:story2" "LocationWatcher: mission 2"
sleep 6
dev "set:instantbuild=on"; dev "set:infiniteresources=on"
dev "set:allbuildings=on"; dev "set:indestructible=on"

echo "[ern] place"
require_spawn commandbase 1
require_spawn erninterface 1
require_spawn ern 6
require_spawn cannon 2

clear_ui
send "sim:hold on" "SIM hold"
send "sim:run 3" "SIM run:"
sleep 3
say "sim state"
simline
case "$(simline)" in *"paused=True"*) echo "FATAL: still paused"; exit 1 ;; esac

# Ramp the slot from zero to a plateau. Waits for efficiency to LEAVE zero before
# timing, because an assigned ERN flies to the port first and 0 reads the same as
# finished. Unreadable samples are skipped, never counted as zero or as stable.
ramp() {
  local label="$1" t=0 now="" started=0 last="" stable=0 i v
  send "ern:release $SLOT" "ERN release:"; sleep 4
  send "ern:assign $SLOT" "ERN assign:"
  for i in $(seq 1 40); do
    sleep "$POLL"; t=$((t+POLL))
    now=$(effof)
    [ -n "$now" ] && [ "$now" != "0" ] && { started=1; break; }
  done
  if [ "$started" != "1" ]; then
    echo "  [$label] NEVER STARTED after ${t}s - $(slotline)"
    return 1
  fi
  echo "    [$label] started at eff=$now (${t}s)"
  for i in $(seq 1 80); do
    sleep "$POLL"; t=$((t+POLL))
    v=$(effof)
    [ -z "$v" ] && continue
    if [ "$v" = "$last" ]; then
      stable=$((stable+1))
      [ "$stable" -ge 2 ] && { now="$v"; break; }
    else
      stable=0
    fi
    last="$v"; now="$v"
  done
  echo "  [$label] PLATEAU eff=$now after ${t}s"
}

say "A. baseline ramp, no items"
ramp baseline

say "B. fill rate: 4x ERN Efficiency Rate (expect roughly half the time)"
for i in 1 2 3 4; do send "item:Progressive ERN Efficiency Rate: Fire Range" "DEBUG fake item"; done
ramp charged

say "C. ceiling: ERN Efficiency Cap added one at a time, from the plateau above"
for i in 1 2 3 4; do
  send "item:Progressive ERN Efficiency Cap: Fire Range" "DEBUG fake item"
  sleep 5
  echo "  boost x$i -> eff=$(effof)"
done

say "D. does the GAME move, or only the number?"
send "upgrade:units cannon" "UPGRADE unit"
since | grep -a "UPGRADE unit" | sed 's/^.*UPGRADE/UPGRADE/' | head -2
echo "  (unboosted cannon RANGE was 9 with rangeBoost 0.25)"

clear_ui
rm -f "$CW4/ap_shot.png"; send "shot:" "SHOT:"; sleep 4
echo "[ern] done. Screenshot: $CW4/ap_shot.png"
