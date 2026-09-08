#!/bin/bash
# Time the reconnect backoff against a dead port, for as long as it takes.
#
# offline-test asserts a SECOND attempt gets scheduled - the assertion that
# caught the retry chain dying - but it has to bound its wait, and a bound is a
# guess unless somebody has measured the real interval. Once that guess is too
# tight the harness reports the give-up bug on a client that is behaving
# perfectly, which is the same misleading-verdict trap as an unusable premise.
#
# This measures instead: launch offline against a port nothing is listening on,
# then print a timestamped line for every reconnect the mod schedules until the
# window closes. Read the gaps off the output and set the harness bound from
# them, with headroom.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
WATCH="${1:-420}"          # seconds to observe

listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }

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
  taskkill //IM CW4.exe //F >/dev/null 2>&1
  if [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ]; then cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"; fi
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

taskkill //IM CW4.exe //F >/dev/null 2>&1
DEAD="$(find_free_port 38451 38500)" || { echo "ABORT: no free port"; exit 1; }
rm -rf "$STORE_DIR/slots" 2>/dev/null
cat > "$CFG" <<CFGEOF
[Connection]
Host = localhost
Port = $DEAD
Slot = DrohaCW4
Password =
AutoConnect = true

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF
sleep 2

echo "retry-interval: dead port $DEAD, watching ${WATCH}s"
rm -f "$CW4/BepInEx/cw4ap-commands.txt"

alive() { tasklist //FI "IMAGENAME eq CW4.exe" 2>/dev/null | grep -qi "CW4.exe"; }

# The previous game must be GONE before the log is touched, or its buffered
# output gets flushed into the file we just emptied and every "is this a fresh
# log" test is answered by the old run.
for i in $(seq 1 30); do alive || break; sleep 1; done
alive && { echo "ABORT: a CW4 process would not die"; exit 1; }
: > "$L"

cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
START=$SECONDS

# Wait for the process to APPEAR before ever asking whether it has gone. Polling
# liveness straight after the launch reported GAME EXITED three seconds into a
# fourteen-second startup.
for i in $(seq 1 40); do alive && break; sleep 1; done
alive || { echo "ABORT: the game never started"; exit 1; }
echo "  process up at t+$((SECONDS - START))s"

# And the log is genuinely this run's, so this gate genuinely waits.
for i in $(seq 1 60); do
  grep -q "ModCore initialized" "$L" 2>/dev/null && break
  sleep 1
done
grep -q "ModCore initialized" "$L" 2>/dev/null || { echo "ABORT: the mod never loaded"; exit 1; }
echo "  mod up at t+$((SECONDS - START))s; counting from here"
SEEN=0
LAST=$((SECONDS - START))
while [ $((SECONDS - START)) -lt "$WATCH" ]; do
  # LIVENESS. A game that has died logs nothing more, which is indistinguishable
  # from a retry chain that has stopped - and the first run of this script
  # reported "1 attempt in 420s" with no way to tell those apart. Silence is
  # only evidence if the process is still there to be silent.
  if ! alive; then
    echo "  t+$((SECONDS - START))s  GAME EXITED - the run proves nothing about the backoff"
    echo "---"
    echo "retry-interval: ABORTED, game died after $SEEN attempt line(s)"
    exit 1
  fi
  # No `|| echo 0` here. grep -c already prints a zero when nothing
  # matches, and it ALSO exits non-zero, so the fallback ran as well and N
  # ended up as a TWO-LINE string reading zero, zero. That turns the
  # comparison below from a test into a hard error - the run printed
  # "integer expression expected" three times and skipped the branch, so
  # nothing was counted until the first match existed.
  N=$(grep -c "AP RECONNECT: attempt" "$L" 2>/dev/null); N=${N:-0}
  if [ "${N:-0}" -gt "$SEEN" ]; then
    T=$((SECONDS - START))
    LINE=$(grep "AP RECONNECT: attempt" "$L" 2>/dev/null | tail -1 | sed 's/^.*AP RECONNECT/AP RECONNECT/')
    echo "  t+${T}s  (gap $((T - LAST))s)  $LINE"
    SEEN=$N
    LAST=$T
  fi
  sleep 2
done
echo "---"
echo "retry-interval: $SEEN attempt line(s) in ${WATCH}s"
grep "AP RECONNECT: attempt" "$L" 2>/dev/null | sed 's/^.*AP RECONNECT/    AP RECONNECT/'

# Preserve the log. A chain that stalls can only be diagnosed from what came
# after the last attempt, and the next harness to run replaces this file.
KEEP="$(cd "$(dirname "$0")/.." && pwd)/.aptest/retry-interval.log"
mkdir -p "$(dirname "$KEEP")"; cp "$L" "$KEEP" 2>/dev/null
echo "log kept at $KEEP"
echo "  tail after the last attempt:"
awk '/AP RECONNECT: attempt/ { last = NR } { line[NR] = $0 } END {
       for (i = last; i <= NR && i <= last + 12; i++) print "    " line[i] }' "$KEEP" 2>/dev/null
