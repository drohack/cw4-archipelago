#!/usr/bin/env bash
# Winning a mission must send EVERY required objective's checks, including the
# counted ones - nullify, totems, collect - which are one location per instance.
#
# Reported from a v0.1.6 playthrough: Home was completed with every objective
# cleared and required, and "Home - Nullify 1" never arrived. The backfill that
# exists for exactly this asked for "Home - Nullify", which is not a location,
# so IsLocation rejected it and the loop skipped. It could only ever fire for
# Reclaim and Custom - the two objectives that genuinely are single checks.
# Home requires [0, 1, 4], all three counted, so the net was useless there.
#
# This needs a REAL SERVER: the required-objective list arrives in slot data, so
# an offline harness cannot exercise the path at all. That is why the bug
# survived an offline reproduction that looked thorough.
#
# Usage: tools/objective-backfill-test.sh      (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
STORE="$HOME/Documents/My Games/creeperworld4/archipelago"
MULTIDATA=""    # generated below, from the CURRENT apworld
GENDIR="$REPO/.aptest/backfill-seed"
SRV_IN="$REPO/.aptest/backfill-srv-in"
SRV_LOG="$REPO/.aptest/backfill-srv.log"
LOGDIR="$REPO/.aptest/backfill-logs"
SLOT="DrohaCW4"
PORT=""


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
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }
save_log() { mkdir -p "$LOGDIR"; [ -f "$L" ] && cp "$L" "$LOGDIR/$1.log"; }
kill_game() { save_log "${1:-phase}"; taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 3; }
stop_server() {
  srv "/exit" 2>/dev/null; sleep 2
  local pid
  for pid in $(pids_on_port "$PORT"); do taskkill //PID "$pid" //F >/dev/null 2>&1; done
}

echo "step 0/4: generate a seed from the CURRENT apworld"
# NOT a leftover seed from .aptest/server: the newest one there was generated
# before required_objectives existed, so its slot data has no such key and this
# harness silently measured a seed that could not exercise the feature at all.
rm -rf "$GENDIR"; mkdir -p "$GENDIR/players" "$GENDIR/out"
printf 'name: %s
game: Creeper World 4
Creeper World 4: {}
' "$SLOT"   > "$GENDIR/players/cw4.yaml"
( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python Generate.py     --player_files_path "$GENDIR/players" --outputpath "$GENDIR/out" --seed 20260905     > "$GENDIR/generate.log" 2>&1 )
# Generate.py writes a .zip holding the .archipelago plus the spoiler; the
# server wants the .archipelago itself.
( cd "$GENDIR/out" && unzip -o -q ./*.zip 2>/dev/null )
MULTIDATA="$(ls -t "$GENDIR/out/"*.archipelago 2>/dev/null | head -1)"
[ -n "$MULTIDATA" ]; verdict $? "generated a seed from the current apworld"
[ -n "$MULTIDATA" ] || { tail -5 "$GENDIR/generate.log"; exit 1; }

# The premise, asserted rather than assumed. With no required_objectives in slot
# data there is no backfill to exercise, and every assertion below would pass or
# fail for the wrong reason.
( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python "$REPO/tools/check-slotdata.py"     "$MULTIDATA" required_objectives story2 ) > "$GENDIR/slotdata.txt" 2>&1
grep -q "^OK" "$GENDIR/slotdata.txt"; verdict $? "the seed carries required_objectives for Home"
sed 's/^/  /' "$GENDIR/slotdata.txt" | head -1

echo "step 0b/4: clean slate on a free port"
kill_game pre
rm -f "$CMD"
rm -rf "$STORE/slots" "$STORE/last-session.json"
PORT="$(find_free_port 38401 38450)" || { echo "ABORT: no free port"; exit 1; }
echo "  serving on $PORT"
rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 \
  python MultiServer.py "$MULTIDATA" --port "$PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && break; sleep 1; done
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up"

mkdir -p "$(dirname "$CFG")"
printf '[Connection]\nHost = localhost\nPort = %s\nSlot = %s\nPassword =\nAutoConnect = true\n\n[Missions]\nShowSpan = false\n' \
  "$PORT" "$SLOT" > "$CFG"

echo "step 1/4: connect"
( cd "$CW4" && ./CW4.exe > /dev/null 2>&1 & ); sleep 16
MARK=0
wait_since "ModCore initialized" 30; verdict $? "the mod loaded (control)"
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "step 2/4: Home requires the three COUNTED objectives"
mark
srv "/send $SLOT Mission Unlock: Home"
wait_since "AP ITEM RECEIVED: Mission Unlock: Home" 25; verdict $? "received the Home unlock"

echo "step 3/4: win Home WITHOUT nullifying anything"
send "boot:story2"
wait_since "New GameSpace" 45 || echo "  (story2 slow to load)"
send "ada:close"
sleep 4
mark
# `win` marks the objectives complete; it does not remove nullifiable units, so
# the live per-instance counter cannot have sent the nullify check. Only the
# completion backfill can. That is the whole point of this run.
send "win"; sleep 6
wait_since "OBJDUMP: mission 2 complete" 30; verdict $? "the mission completed"
echo "  --- objective dump ---"
since | grep "OBJDUMP:" | sed 's/^.*OBJDUMP/  OBJDUMP/'

echo "step 4/4: every required objective's instances were sent"
sleep 4
since | grep -q "INFERRED 'Home - Nullify 1'"; verdict $? "the nullify instance was inferred and sent"
since | grep -q "LOCATION CHECK: Home - Nullify 1"; verdict $? "and left as a real check"
sleep 3
grep -q "Home - Nullify 1" "$SRV_LOG"; verdict $? "the server recorded it"
echo "  --- what the backfill sent ---"
since | grep "INFERRED" | sed 's/^.*LocationWatcher: /  /'

echo "---"
echo "objective-backfill: $PASS passed, $FAIL failed"
kill_game post
stop_server
echo "objective-backfill: cleaned up"
