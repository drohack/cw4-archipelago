#!/usr/bin/env bash
# The map's own per-objective completion answer must come from the multiworld.
#
# The game asks SpanNetworkPlanet.FakeIsMissionObjectiveComplete(guid, obj)
# when it draws a planet, and the mod's FakeCompletePatch answers it from AP
# state. It looked the objective up by its type-shaped name ("Home - Nullify"),
# which per-instance locations ("Home - Nullify 1") no longer have - so for
# Nullify, Totems and Collect it let the game answer from the LOCAL SAVE, and
# only Reclaim and Custom ever reflected the multiworld. Nothing in the log
# showed it: glyph colouring is a different code path, and it was right.
#
# This puts the map into a known AP state, then reads the answer back with the
# debug plugin's "fake:dump", which calls the patched method itself. It asserts
# BOTH directions, because the local save is whatever is on this machine: where
# it says a mission is done, the unchecked and half-done objectives below catch
# the bug (measured 2026-10-21: the old patch read Home's Nullify and Totems as
# True from a completed local save); where it says not done, the checked ones
# do.
#
# Usage: tools/objective-complete-test.sh      (game must be CLOSED; needs
#        CW4ApDebug installed beside the mod)
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

CMD="$AP_CMD"
PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
# Wait up to $2 seconds for a line matching $1 since the last mark.
wait_for() { local i; for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 1; done; return 1; }
# One planet's answer for one objective slot, from the newest fake:dump line.
answer() { since | grep "DEBUG FAKE: $1 obj" | tail -1 | grep -o "obj$2=[A-Za-z()]*" | cut -d= -f2; }
expect() {   # $1 planet, $2 slot, $3 expected, $4 description
  local got; got=$(answer "$1" "$2")
  verdict $([ "$got" = "$3" ] && echo 0 || echo 1) "$1 $4 (obj$2=$3, got ${got:-none})"
}

echo "step 1/5: clean slate + known config"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD"
# Hermetic: with no server reachable the mod comes up on the LAST SLOT PLAYED,
# and that slot's checks would answer these queries instead of the ones below.
rm -rf "$HOME/Documents/My Games/creeperworld4/archipelago/slots" \
       "$HOME/Documents/My Games/creeperworld4/archipelago/last-session.json"
mkdir -p "$GAME_DIR/BepInEx/config"
cat > "$GAME_DIR/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
Host = localhost
Port = 38281
Slot = DrohaCW4
Password =
AutoConnect = false

[Missions]
ShowSpan = false
CFGEOF

echo "step 2/5: launch"
cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 &
MARK=0   # BepInEx truncates the log on launch
sleep 5
if ! wait_for "CW4 Archipelago Debug loaded" 60; then
  echo "FATAL: the debug plugin never loaded - is CW4ApDebug installed?"
  taskkill //IM CW4.exe //F >/dev/null 2>&1; exit 1
fi
sleep 8

echo "step 3/5: a known AP state"
send "item:Mission Unlock: Home"
send "item:Mission Unlock: Not My Mars"
for l in "Home - Cache 1" "Home - Totem 1" "Home - Totem 2" "Home - Nullify 1"; do send "loc:add $l"; done
# Home: the cache and ONE of two totems checked, nullify open.
send "check:Home - Cache 1"
send "check:Home - Totem 1"
# Not My Mars: every location checked.
for l in "Not My Mars - Cache 1" "Not My Mars - Totem 1" "Not My Mars - Totem 2" \
         "Not My Mars - Totem 3" "Not My Mars - Totem 4" "Not My Mars - Nullify 1" \
         "Not My Mars - Nullify 2"; do send "loc:add $l"; send "check:$l"; done
send "story:open"
sleep 6

echo "step 4/5: the map's answers"
mark
send "fake:dump"
wait_for "DEBUG FAKE: Home obj" 10 || echo "  NOTE  no fake:dump line for Home"
since | grep "DEBUG FAKE: \(Home\|Not My Mars\) obj"
# Slots: 0 Nullify, 1 Totems, 4 Collect.
expect "Home" 4 True "Collect, its cache checked"
expect "Home" 1 False "Totems, 1 of 2 checked"
expect "Home" 0 False "Nullify, unchecked"
expect "Not My Mars" 0 True "Nullify, all checked"
expect "Not My Mars" 1 True "Totems, all checked"
expect "Not My Mars" 4 True "Collect, all checked"

# The second totem completes the objective.
send "check:Home - Totem 2"
sleep 3
mark
send "fake:dump Home"
wait_for "DEBUG FAKE: Home obj" 10
expect "Home" 1 True "Totems, 2 of 2 checked"

echo "step 5/5: shut down"
ERR=$(grep -cE "$MOD_ERROR_RE" "$GAME_LOG" 2>/dev/null); ERR=${ERR:-0}
verdict $([ "$ERR" = 0 ] && echo 0 || echo 1) "no mod errors in the log (got $ERR)"
taskkill //IM CW4.exe //F >/dev/null 2>&1
echo "---"
echo "objective-complete: $PASS passed, $FAIL failed"
[ "$FAIL" = 0 ]
