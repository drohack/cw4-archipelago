#!/usr/bin/env bash
# Offline play, reconnect, and what survives a connect. End to end, real server.
# Every rule asserted here is cited in
# docs/design/2026-09-04-offline-and-disconnects.md.
#
# Exists because three bugs lived in this area and none was visible to the log
# or the unit tests: a goal silently not sent, every trap silently re-firing,
# and checks silently crossing from one multiworld into another. Two of them
# only happen on the SECOND connection, which is why no battery had caught them.
#
# TWO THINGS THIS HARNESS LEARNED ON ITS OWN FIRST RUN:
#
#  * It must ASSERT the port is free, not assume it. That run appeared to test
#    "no server" while something WAS listening on 38281 and answering
#    InvalidSlot - so every later step failed off a premise that was never true,
#    and the whole run read like a product regression.
#  * A log-reading assertion needs a positive control before it. "No cached
#    slot was loaded" passes just as happily when the game never started.
#
# Usage: tools/offline-test.sh      (game must be CLOSED; ports are chosen
#                                    automatically from 38401-38500)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
STORE="$HOME/Documents/My Games/creeperworld4/archipelago"
MULTIDATA="$(ls -t "$REPO/.aptest/server/"*.archipelago 2>/dev/null | head -1)"
SRV_IN="$REPO/.aptest/offline-srv-in"
SRV_LOG="$REPO/.aptest/offline-srv.log"
SLOT="DrohaCW4"
# Ports are CHOSEN AT RUN TIME, not fixed. The first run of this harness was
# invalidated by a MultiServer from an unrelated project sitting on 38281: a
# stray listener answered the connect with InvalidSlot, and the run read like a
# CW4 regression. A harness that hard-codes a well-known port is one shared
# machine away from measuring someone else's server.
PORT=""
DEAD_PORT=""


# --- restore the player's environment on exit ---------------------------------
# A harness overwrites the BepInEx config (hermetic settings) and clears the AP
# slot cache (so the offline-start feature cannot inherit a previous battery's
# slot). Both belong to whoever plays this install next. Leaving a test config
# behind pointed a real session at a dead localhost port with a test slot name,
# which reads in game as "connecting... timed out... disconnected" and no items.
STORE_DIR="$HOME/Documents/My Games/creeperworld4/archipelago"
CFG_BAK=""; STORE_BAK=""
save_env() {
  if [ -f "$CFG" ]; then CFG_BAK="$(mktemp)"; cp "$CFG" "$CFG_BAK"; fi
  if [ -d "$STORE_DIR/slots" ] || [ -f "$STORE_DIR/last-session.json" ]; then
    STORE_BAK="$(mktemp -d)"
    [ -d "$STORE_DIR/slots" ] && cp -r "$STORE_DIR/slots" "$STORE_BAK/slots"
    [ -f "$STORE_DIR/last-session.json" ] && cp "$STORE_DIR/last-session.json" "$STORE_BAK/"
  fi
}
restore_env() {
  if [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ]; then
    cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"
  fi
  rm -rf "$STORE_DIR/slots"; rm -f "$STORE_DIR/last-session.json"
  if [ -n "$STORE_BAK" ] && [ -d "$STORE_BAK" ]; then
    [ -d "$STORE_BAK/slots" ] && cp -r "$STORE_BAK/slots" "$STORE_DIR/slots"
    [ -f "$STORE_BAK/last-session.json" ] && cp "$STORE_BAK/last-session.json" "$STORE_DIR/"
    rm -rf "$STORE_BAK"
  fi
  echo "  (config and slot cache restored)"
}
save_env
trap restore_env EXIT

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
refute() { # assert a pattern is ABSENT since the mark
  if since | grep -q "$1"; then verdict 1 "$2"; else verdict 0 "$2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0;
          tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { local pat="$1" n="${2:-20}" i; for i in $(seq 1 "$n"); do
                 since | grep -q "$pat" && return 0; sleep 1; done; return 1; }

listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
pids_on_port() { netstat -ano | grep "LISTENING" | grep ":$1 " | awk '{print $NF}' | sort -u; }
wait_port_free() { local i; for i in $(seq 1 20); do
                     listening "$1" || return 0; sleep 1; done; return 1; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }

SRV_PID=""
start_server() {
  rm -f "$SRV_IN"; : > "$SRV_IN"
  tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 \
    python MultiServer.py "$MULTIDATA" --port "$PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
  SRV_PID=$!
  local i
  for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && return 0; sleep 1; done
  return 1
}
stop_server() {
  [ -n "$SRV_PID" ] && srv "/exit" 2>/dev/null
  sleep 2
  [ -n "$SRV_PID" ] && kill "$SRV_PID" 2>/dev/null
  # Kill ONLY what is listening on the port we chose. This was a filter on every
  # python.exe on the machine, which would have taken out an unrelated project's
  # Archipelago server - one was found sitting on 38281 while this was written.
  local pid
  for pid in $(pids_on_port "$PORT"); do taskkill //PID "$pid" //F >/dev/null 2>&1; done
  wait_port_free "$PORT" || echo "  WARNING: port $PORT still held"
  SRV_PID=""
}
LOGDIR="$REPO/.aptest/offline-logs"
# BepInEx truncates LogOutput.log on EVERY launch and this harness launches
# four times, so a failure in a later phase is undiagnosable after the fact
# unless the log is kept. It was not, twice, and both times the next step was
# guesswork. Keep a copy per phase.
save_log() { mkdir -p "$LOGDIR"; [ -f "$L" ] && cp "$L" "$LOGDIR/$1.log"; }
kill_game() { save_log "${1:-phase}"; taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 3; }
write_cfg() {   # $1 = port, $2 = autoconnect
  mkdir -p "$(dirname "$CFG")"
  printf '[Connection]\nHost = localhost\nPort = %s\nSlot = %s\nPassword =\nAutoConnect = %s\n\n[Missions]\nShowSpan = false\n' \
    "$1" "$SLOT" "$2" > "$CFG"
}
launch() { ( cd "$CW4" && ./CW4.exe > /dev/null 2>&1 & ); sleep 14; MARK=0; }

[ -n "$MULTIDATA" ] || { echo "no multidata in .aptest/server - run tools/apbattery.sh first"; exit 1; }
echo "offline-test: multidata $MULTIDATA"

echo "step 0/6: clean slate, and the premises asserted"
kill_game
rm -f "$CMD"
rm -rf "$STORE/slots" "$STORE/last-session.json"
PORT="$(find_free_port 38401 38450)" || { echo "ABORT: no free port in 38401-38450"; exit 1; }
DEAD_PORT="$(find_free_port 38451 38500)" || { echo "ABORT: no free dead port"; exit 1; }
# Re-assert: step 1's whole premise is that DEAD_PORT stays closed.
listening "$PORT" && { echo "ABORT: $PORT became busy"; exit 1; }
listening "$DEAD_PORT" && { echo "ABORT: $DEAD_PORT became busy"; exit 1; }
echo "  serving on $PORT; using $DEAD_PORT as the unreachable one"

# ---------------------------------------------------------------- 1
echo "step 1/6: cold start, no server and no cache"
write_cfg "$DEAD_PORT" true
launch
# Positive control FIRST: every assertion below reads the log, so if the game
# never came up they would all pass by finding nothing.
wait_since "ModCore initialized" 30; verdict $? "the mod loaded (control)"
refute "AP OFFLINE: loaded cached slot" "no cached slot exists to load yet"
wait_since "cannot reach server" 40; verdict $? "an unreachable server is reported as such"
wait_since "AP RECONNECT: attempt 1" 20; verdict $? "and is retried, not given up on"
# The backoff is 5s then 10s, but each attempt has to TIME OUT before the next
# is scheduled, so the wall-clock gap is much larger than the delay. Measured
# on 2026-09-04: attempts 1, 2 and 3 logged within 150s of launch. 90s is the
# window for the second, not 40 - which is why this failed once while the
# product was behaving correctly.
if wait_since "AP RECONNECT: attempt 2" 120; then
  verdict 0 "the retry keeps going (attempt 2)"
else
  verdict 1 "the retry keeps going (attempt 2)"
  echo "        reconnect lines actually seen:"
  since | grep -E "AP RECONNECT|cannot reach server" | sed "s/^/          /"
fi
refute "AP LOGIN REFUSED" "an unreachable host is not mistaken for a refusal"

# ---------------------------------------------------------------- 2
echo "step 2/6: connect for real and earn state, including a fired trap"
kill_game step1-offline
start_server; verdict $? "server up on $PORT"
write_cfg "$PORT" true
launch
wait_since "ModCore initialized" 30; verdict $? "the mod loaded (control)"
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"
srv "/send $SLOT Mission Unlock: Farsite"
wait_since "AP ITEM RECEIVED: Mission Unlock: Farsite" 25; verdict $? "received a mission unlock"
srv "/send $SLOT Spore Strike"
wait_since "AP ITEM RECEIVED: Spore Strike" 25; verdict $? "received a trap"
send "boot:story1"
wait_since "New GameSpace" 45 || echo "  (story1 slow to load)"
send "ada:close"
# The trap must actually fire before its mark means anything: otherwise the
# replay assertion below would pass on a mark that never moved.
wait_since "TRAP:" 40; verdict $? "the trap fired once, in a mission"

# ---------------------------------------------------------------- 3
echo "step 3/6: reconnecting must not replay the trap"
mark
send "disconnect"; sleep 3
send "connect"; sleep 10
wait_since "AP CONNECTED slot='$SLOT'" 45; verdict $? "reconnected"
sleep 6                     # plenty of TrapApplier ticks to misbehave in
N=$(since | grep -c "TRAP:")
verdict $([ "$N" = 0 ] && echo 0 || echo 1) "no trap re-fired on the reconnect (got $N)"

# ---------------------------------------------------------------- 4
echo "step 3b/6: a deliberate disconnect must STAY disconnected"
mark
send "disconnect"; sleep 12
refute "disconnected - will retry" "a manual disconnect is not treated as a drop"
refute "AP CONNECTED slot=" "and it does not silently reconnect"
send "connect"; sleep 10
wait_since "AP CONNECTED slot='$SLOT'" 45; verdict $? "an explicit connect still works after it"

echo "step 4/6: a goal reached offline reaches the server on reconnect"
srv "/send $SLOT Mission Unlock: Founders"
wait_since "AP ITEM RECEIVED: Mission Unlock: Founders" 25
send "disconnect"; sleep 3
mark
# Lift the finale gate rather than faking 19 mission completions: the goal is
# gated on MissionRules.FinaleCounts, and `finale:beat` writes checked entries
# the server never had, which the next reconcile correctly discards. This is
# the sequence apbattery2 proves. Done AFTER the disconnect so nothing can
# rebuild it.
send "finale:need 0"; sleep 2
send "boot:story19"
wait_since "New GameSpace" 45 || echo "  (story19 slow to load)"
send "ada:close"
sleep 4                     # world must exist, or `win` bails with "no world"
send "win"; sleep 3
wait_since "AP GOAL QUEUED (offline)" 25; verdict $? "the goal is queued while disconnected"
mark
send "connect"; sleep 12
wait_since "AP CONNECTED slot='$SLOT'" 45; verdict $? "reconnected after the offline goal"
wait_since "AP GOAL ACHIEVED sent" 30; verdict $? "and the queued goal was sent"

# ---------------------------------------------------------------- 5
echo "step 5/6: launch with the server down, on the cached slot"
kill_game step2-4-connected
stop_server
write_cfg "$DEAD_PORT" false
launch
wait_since "ModCore initialized" 30; verdict $? "the mod loaded (control)"
wait_since "AP OFFLINE: loaded cached slot='$SLOT'" 30; verdict $? "came up on the cached slot"
mark; send "gatecheck:story1"; sleep 2
since | grep -q "DEBUG GATECHECK: 'story1' allowed=True"
verdict $? "a mission unlocked before the drop is playable offline"
# Dump the cached state first: `check:` goes through MarkChecked, which returns
# false for an ALREADY-checked location and then queues nothing - so this
# assertion is only meaningful on a location known to be unchecked. It flapped
# between two runs for exactly that reason.
mark; send "tracker:dump"; sleep 2
mark; send "check:Hints - Cache 1"; sleep 3
if since | grep -q "AP CHECKS QUEUED (offline)"; then
  verdict 0 "an offline check is queued, not dropped"
else
  verdict 1 "an offline check is queued, not dropped"
  echo "        (state dump kept in $LOGDIR; DEBUG check line follows)"
  since | grep "DEBUG check:" | tail -2 | sed "s/^/        /"
fi

# ---------------------------------------------------------------- 6
echo "step 6/6: zero plugin errors"
ERR=$(grep -cE "\[Error  :CW4 Archipelago\]|tick failed|late tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "---"
echo "offline-test: $PASS passed, $FAIL failed"
kill_game step5-offline
stop_server
echo "offline-test: cleaned up"
