#!/bin/bash
# Message-box behavior battery: the box builds and hosts itself on a screen-space
# canvas, ingests server messages (item receive/send) and connection lines with AP
# colors, history survives a second mission boot, and a mission entered with a
# history behind you opens the log at the BOTTOM.
#
# This harness was the last one still carrying three defects the others had
# already been fixed for, and all three were found by finally running it:
#
#   1. It called `mark` in four places without ever DEFINING it. Every other
#      harness in tools/ defines it. `MARK` therefore stayed 0 and `since`
#      returned the whole log every time, so each assertion could be satisfied
#      by a line printed in an earlier step. It failed loose, not tight, which
#      is why nothing looked wrong until the stderr was read.
#   2. It waited on "MSGBOX: anchored to minimap", a string the mod does not
#      emit. The real line is "MSGBOX: built on '<canvas>'". Two assertions
#      could not pass at all, whatever the box did.
#   3. It hardcoded port 38281 and killed whatever was LISTENING there - the
#      default Archipelago port, i.e. the player's own server for another game.
#      It also overwrote the BepInEx config and deleted the AP slot cache with
#      no restore, which is what left a real session pointed at a dead test
#      port under a test slot name. Free port plus snapshot/restore now,
#      matching offline-test.sh.
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game
CMD="$AP_CMD"
CFG="$AP_CFG"
SLOT="DrohaCW4"; MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-msgbox-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-msgbox-srv.in"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[msgbox] PASS: $2";
            else FAIL=$((FAIL+1)); echo "[msgbox] FAIL: $2"; fi; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since|grep -q "$1"&&return 0; sleep 2; done; return 1; }

listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
pids_on_port() { netstat -ano | grep "LISTENING" | grep ":$1 " | awk '{print $NF}' | sort -u; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }
SRV_PID=""; PORT=""
stop_server() {
  if [ -n "$SRV_PID" ]; then printf "/exit\n" >> "$SRV_IN" 2>/dev/null; sleep 2; kill "$SRV_PID" 2>/dev/null; fi
  # Only what is listening on the port THIS run chose. A blanket kill on 38281
  # took out the player's own MultiServer for a different game.
  if [ -n "$PORT" ]; then
    for pid in $(pids_on_port "$PORT"); do taskkill //PID "$pid" //F >/dev/null 2>&1; done
  fi
  SRV_PID=""
}

# --- restore the player's environment on exit ---------------------------------
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
  stop_server
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

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "[msgbox] FATAL: no multidata"; exit 1; }
save_env
trap restore_env EXIT

echo "[msgbox] step 0: clean slate + config"
taskkill //IM CW4.exe //F >/dev/null 2>&1
PORT="$(find_free_port 38301 38350)" || { echo "[msgbox] ABORT: no free port in 38301-38350"; exit 1; }
echo "[msgbox]   serving on $PORT"
rm -rf "$STORE_DIR/slots" 2>/dev/null
cat > "$CFG" <<CFGEOF
[Connection]
Host = localhost
Port = $PORT
Slot = $SLOT
Password =
AutoConnect = true

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF
sleep 2

echo "[msgbox] step 1: server + launch + connect"
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port "$PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PID=$!
for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && break; sleep 1; done
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up on $PORT"
rm -f "$CMD"; cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 &
sleep 12; MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "[msgbox] step 2: boot mission -> the box builds"
# Unlock what we are about to boot. Starters are RANDOM per seed, and this
# harness used to assume story1 was bootable - on a seed where it was not, the
# boot was refused and three assertions failed downstream (the box never built,
# so no objective could complete and the box then built for the first time in
# story2, tripping the do-not-rebuild check). One locked mission, three
# confusing failures, none of them about the message box.
srv "/send $SLOT Mission Unlock: Farsite"
srv "/send $SLOT Mission Unlock: Home"
mark
send "boot:story1"
if since | grep -q "boot BLOCKED"; then
  verdict 1 "PREMISE: story1 is bootable (the gate refused it)"
else
  verdict 0 "PREMISE: story1 is bootable"
fi
wait_since "New GameSpace" 45; sleep 4
send "ada:close"; sleep 1
wait_since "MSGBOX: built on" 20; verdict $? "message box built"
since | grep "MSGBOX: built on" | tail -1 | sed 's/^.*MSGBOX:/[msgbox]   MSGBOX:/'

echo "[msgbox] step 3: item receive -> colored line"
mark
srv "/send $SLOT Cannon"
wait_since "AP MESSAGE: .*Cannon" 20; verdict $? "item receive message ingested"

echo "[msgbox] step 4: check completion -> item send line"
mark
send "objective:5"; sleep 3
wait_since "AP MESSAGE:" 15; verdict $? "objective completion produced a server message"

echo "[msgbox] step 5: disconnect -> connection status line"
mark
send "disconnect"; sleep 2
since | grep -q "STATUS TOAST: Archipelago:"; verdict $? "connection status line appended"

echo "[msgbox] step 6: history survives a second mission boot"
# FILL THE BOX FIRST. The scroll assertion below is the whole point of this
# harness, and until 2026-09-10 it ran against a box holding eight lines - which
# settles its layout almost instantly. The bug it was meant to catch only
# appears once there is enough text that TMP needs many frames to measure it,
# and the box holds up to 200 lines. An eight-line fixture cannot fail, so it
# passed while the mod shipped the bug twice.
send "msgbox:fill 150"; sleep 4
since | grep -oE "MSGBOX FILL: added [0-9]+ line\(s\), history=[0-9]+" | tail -1 | sed 's/^/[msgbox]   /'
send "connect"; sleep 6
send "msgbox:dump"; sleep 2
H1=$(since | grep -oE "MSGBOX DUMP: history=[0-9]+" | tail -1 | grep -oE "[0-9]+$")
echo "[msgbox]   history before reboot=$H1"
mark
send "boot:story2"; wait_since "New GameSpace" 45; sleep 4
send "ada:close"; sleep 1
send "msgbox:dump"; sleep 2
H2=$(since | grep -oE "MSGBOX DUMP: history=[0-9]+" | tail -1 | grep -oE "[0-9]+$")
echo "[msgbox]   history after reboot=$H2"
[ "${H2:-0}" -ge "${H1:-1}" ] && [ "${H2:-0}" -gt 0 ]; verdict $? "history retained across missions ($H1 -> $H2)"
# Rebuilding on a mission change is CORRECT, and this used to assert the
# opposite. The box hosts itself on AchievementCanvas, which the scene change
# destroys, so IsAlive() goes false and TryBuild() makes a new one - the log
# shows exactly one "built on" per SCENE: 'Game'. The earlier assertion was
# written from a single run where the canvas happened to survive, and it then
# failed on every seed where it did not, while the box was working perfectly.
#
# What actually matters is that there is ONE box, not that it is the same one:
# a second build without the first being gone would orphan a box and double
# every line. So count builds against mission entries rather than forbidding
# them, and let the render and scroll assertions below prove it works.
BUILDS=$(grep -c "MSGBOX: built on" "$GAME_LOG")
ENTRIES=$(grep -c "New GameSpace" "$GAME_LOG")
echo "[msgbox]   box builds=$BUILDS, mission entries=$ENTRIES"
[ "${BUILDS:-0}" -le "${ENTRIES:-0}" ] && [ "${BUILDS:-0}" -ge 1 ]
verdict $? "one box per mission entry, none orphaned ($BUILDS builds / $ENTRIES entries)"

# Entering a mission with a history behind you must open the log at the BOTTOM,
# on the newest line. Reported from play on v0.1.7: "when i load into a level the
# text client starts in the middle."
#
# The render did call ScrollToBottom, but the content uses a VerticalLayoutGroup
# with a PreferredSize ContentSizeFitter and TMP recomputes its metrics in its
# own pass, so the position was applied against a height still smaller than
# final. Canvas.ForceUpdateCanvases does not cover TMP. It re-pins for a few
# frames now.
#
# The rendered-line check is the control: an empty box reports scroll 0 whatever
# happens, so without it this assertion would pass on a box showing nothing.
# rendered=-1 scroll=-1.000 is a THIRD outcome and reads differently again: the
# internals return -1 for a null content/scroll rect, so that pair means the box
# was never built, not that it scrolled wrong.
DUMP=$(since | grep "MSGBOX DUMP:" | tail -1)
RENDERED=$(echo "$DUMP" | grep -oE "rendered=[0-9-]+" | cut -d= -f2)
SCROLL=$(echo "$DUMP" | grep -oE "scroll=[0-9.-]+" | cut -d= -f2)
echo "[msgbox]   $DUMP"
# A FULL box, not merely a non-empty one. Two lines scroll to the bottom no
# matter what the code does; the failure needs enough text to make the layout
# take its time.
[ "${RENDERED:-0}" -ge 100 ]; verdict $? "the box is genuinely full ($RENDERED lines, want >=100)"
awk -v s="${SCROLL:--1}" 'BEGIN { exit !(s >= 0 && s <= 0.02) }'
verdict $? "the log opens at the bottom, not part-way up (scroll=$SCROLL)"

echo "[msgbox] step 7: zero plugin errors"
ERR=$(grep -cE "\[Error :CW4 Archipelago\]|tick failed" "$GAME_LOG" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "[msgbox] DONE: $PASS passed, $FAIL failed"
