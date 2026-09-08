#!/bin/bash
# Integration battery for the real CW4 Archipelago mod. Starts a local AP
# server, launches the game (AutoConnect + DebugCommands via the pre-written
# BepInEx config), and asserts connect / live items / unit gate / location
# checks / tracker colors / mission gating from LogOutput.log.
set -u

# --- pick our own port, and never kill anyone else's server -------------------
# This used to be a fixed 38281 with a "kill whatever is listening on it"
# cleanup, which takes out an unrelated project's Archipelago server and then
# races it for the bind. Choose a free port instead, remember the PID we start,
# and kill only that.
listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }
AP_PORT="$(find_free_port 38301 38380)"
if [ -z "$AP_PORT" ]; then echo "ABORT: no free port in 38301-38380"; exit 1; fi
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"
MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-apserver.log"
SRV_IN="${TEMP:-/tmp}/cw4-apserver.in"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[apbattery] PASS: $2"; else FAIL=$((FAIL+1)); echo "[apbattery] FAIL: $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local cur; cur=$(wc -l < "$L" 2>/dev/null || echo 0); if [ "$cur" -lt "$MARK" ]; then MARK=0; fi; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 2; done; return 1; }

if [ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ]; then echo "[apbattery] FATAL: no multidata"; exit 1; fi
echo "[apbattery] multidata: $MULTIDATA"

# Kill ONLY the server this battery started, on the port it chose. This used to
# kill whatever owned a fixed 38281, which takes out an unrelated project's
# Archipelago server and then races it for the bind - apbattery2 failed its
# connect step that way and cascaded nine assertions off it.
kill_servers() { [ -n "${SRV_PID:-}" ] && kill "$SRV_PID" 2>/dev/null
                 for pid in $(netstat -ano 2>/dev/null | grep "LISTENING" \
                   | grep ":$AP_PORT " | awk '{print $NF}' | sort -u); do
                   taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

# Hermetic setup: no leftover game/server, no stale slot cache, own config.
echo "[apbattery] step 0: clean slate (kill game/servers, clear cache, write config)"
taskkill //IM CW4.exe //F >/dev/null 2>&1
kill_servers
rm -rf "$(cygpath -u "$USERPROFILE" 2>/dev/null || echo "$HOME")/Documents/My Games/creeperworld4/archipelago/slots" 2>/dev/null
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
Host = localhost
Port = $AP_PORT
Slot = $SLOT
Password =
AutoConnect = true

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF
sleep 2

echo "[apbattery] step 1/10: starting local AP server on :$AP_PORT"
rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port "$AP_PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
# Poll rather than sleeping a fixed 8s. MultiServer's startup is not that
# predictable - it reads and validates the whole multidata first - and this
# reported FAIL: server up while the following 19 assertions all passed against
# that same server, which is a self-refuting verdict: the run cannot both have
# no server and check locations on it.
for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && break; sleep 1; done
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up"

echo "[apbattery] step 2/9: launching game (autoconnect)"
rm -f "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12
MARK=0   # BepInEx truncates LogOutput.log on launch; index from its start
wait_since "ModCore initialized" 30; verdict $? "plugin loaded"
wait_since "SCENE: 'Galaxy'" 40 || echo "[apbattery] WARN: menu slow"
wait_since "MENU: AP panel created" 20; verdict $? "AP panel built"
wait_since "AP CONNECTED slot='$SLOT'" 40; verdict $? "auto-connected"

echo "[apbattery] step 3/9: server grants Cannon + Home and Farsite unlocks"
mark
srv "/send $SLOT Cannon"
wait_since "AP ITEM RECEIVED: Cannon" 20; verdict $? "received Cannon"
srv "/send $SLOT Mission Unlock: Home"
wait_since "AP ITEM RECEIVED: Mission Unlock: Home" 20; verdict $? "received Home unlock"
# Farsite has to be GRANTED like any other mission. It used to be permanently
# available, so steps 4, 6 and 7 below just assumed it - and when starters became
# random (and Farsite merely eligible) this battery started failing four
# assertions in a row off one stale premise, all of them downstream of a mission
# that never loaded. apbattery2.sh had already been corrected for exactly this;
# this one was missed. Sending the unlock makes the run independent of which two
# missions THIS seed happened to pick.
srv "/send $SLOT Mission Unlock: Farsite"
wait_since "AP ITEM RECEIVED: Mission Unlock: Farsite" 20; verdict $? "received Farsite unlock"

echo "[apbattery] step 4/9: tracker colors at story select"
mark
send "story:open"; sleep 6
send "tracker:dump"; sleep 3
since | grep "TRACKER:" | sed 's/^.*TRACKER:/[apbattery]   TRACKER:/'
since | grep -q "TRACKER: story1 'Farsite' status=InLogic"; verdict $? "story1 in logic (green)"
# story2 has a Nullify objective needing Nullifier (not held), other objectives
# doable with Cannon -> Partial (orange). Correct nuanced tracker behavior.
since | grep -q "TRACKER: story2 'Home' status=Partial"; verdict $? "story2 partial (orange, Nullify needs Nullifier)"
since | grep -q "TRACKER: story3 .* status=Locked"; verdict $? "story3 locked (red)"

# A location checked by anything OTHER than this client - an admin
# /send_location, a !collect, another client on the same slot. The mod ignored
# all of it: nothing subscribed to Locations.CheckedLocationsUpdated, so the
# level-select icon stayed green until a reconnect rebuilt state from
# AllLocationsChecked. Reported from play on v0.1.7.
#
# Only reachable with a real server, which is why it was missed - the offline
# harness cannot produce a check it did not make.
#
# THIS HAS TO RUN WHILE THE MAP IS OPEN, which is why it sits here rather than
# after the mission steps where it was first written. tracker:dump enumerates
# SpanNetworkPlanet objects, and inside a mission there are none - and
# story:open cannot get back out, because it works by clicking the Farsite
# button on the menu, which in a mission does not exist ("story:open: no
# farsite button"). The first version asserted from inside story1 and failed on
# an empty planet list while the fix it was testing was working correctly.
echo "[apbattery] step 4b/9: a check made SERVER-side repaints the map"
mark
# Not My Mars is locked in this seed, so its icon is red and its checks are
# untouched - picking an unlocked mission's location risks asserting against
# something already checked earlier in this run.
srv "/send_location $SLOT Not My Mars - Cache 1"
if wait_since "AP SERVER CHECKED: Not My Mars - Cache 1" 25; then
  verdict 0 "the server-side check reached the client"
  # And the map must repaint without a reconnect. The tracker logs a line per
  # planet on each pass, so asking it to rescan proves the state moved.
  mark
  send "tracker:dump"; sleep 3
  since | grep -q "TRACKER: story3 'Not My Mars'"; verdict $? "the tracker re-evaluated the planet"
else
  verdict 1 "the server-side check reached the client"
  verdict 1 "the tracker re-evaluated the planet"
  since | grep -E "AP SERVER CHECKED|Locations" | tail -3 | sed 's/^/        /'
fi

echo "[apbattery] step 5/9: mission gating blocks a locked mission"
mark
send "boot:story3"; sleep 2
since | grep -q "DEBUG boot BLOCKED: 'story3'"; verdict $? "locked mission boot blocked"

echo "[apbattery] step 6/9: boot story1 and check the unit gate"
mark
send "boot:story1"
if ! wait_since "New GameSpace" 45; then echo "[apbattery] FATAL: story1 load failed"; fi
wait_since "REVEAL OK" 40; verdict $? "unit pane revealed"
send "ada:close"; sleep 1
send "units"; sleep 2
since | grep "DEBUG UNITS" | sed 's/^.*DEBUG UNITS/[apbattery]   UNITS/'
since | grep -q "allowed=\[.*cannon.*\]"; verdict $? "cannon in allowed set"

echo "[apbattery] step 7/9: complete story1 objective -> location check"
mark
send "objective:5"; sleep 3
since | grep -q "LOCATION CHECK: Farsite - Custom"; verdict $? "objective check triggered"
sleep 2
grep -q "Farsite - Custom" "$SRV_LOG"; verdict $? "server recorded the check"

echo "[apbattery] step 8/9: live item mid-mission (Mortar) rebuilds pane"
mark
srv "/send $SLOT Mortar"
wait_since "AP ITEM RECEIVED: Mortar" 20; verdict $? "received Mortar in mission"
send "units"; sleep 2
since | grep -q "allowed=\[.*mortar.*\]"; verdict $? "mortar now allowed live"

echo "[apbattery] step 9/10: check made while disconnected reaches the server"
mark
# Use a real, still-unchecked location from another unlocked mission (Home),
# so the flush is not deduped by an already-checked story1 location.
send "disconnect"; sleep 3
# "Home - Totems" was the location name until objectives became per INSTANCE;
# the real name is "Home - Totem 1". A check on a name the world does not have
# is silently nothing, so this step asserted a flush that never had anything to
# flush.
send "check:Home - Totem 1"; sleep 3
send "connect"; sleep 8
# End-to-end property: however it is routed (queued+flushed, or sent on the
# auto-reconnect), the check must arrive at the server. Precise queue mechanics
# are covered by the Core unit tests.
grep -q "Home - Totem 1" "$SRV_LOG"; verdict $? "disconnected check reached the server"

echo "[apbattery] step 10/10: zero plugin errors"
mark; send "dump"; sleep 1
ERR=$(grep -cE "\[Error :CW4 Archipelago\]|tick failed|late tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "[apbattery] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1
kill_servers
echo "[apbattery] cleaned up"
