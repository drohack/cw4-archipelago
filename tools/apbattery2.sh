#!/bin/bash
# Extended integration battery: goal/victory, save-load gate decision, live
# tracker update while the page is open, build-limit items, ERN items, plus the
# new mechanics (save archiving, reconnect-on-menu, server-message toasts).
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"
MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-apserver2.log"
SRV_IN="${TEMP:-/tmp}/cw4-apserver2.in"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[ab2] PASS: $2"; else FAIL=$((FAIL+1)); echo "[ab2] FAIL: $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 2; done; return 1; }
kill_servers() { for pid in $(netstat -ano 2>/dev/null | grep -E ':38281[[:space:]]' | grep -i listening | awk '{print $NF}' | sort -u); do taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "[ab2] FATAL: no multidata"; exit 1; }
echo "[ab2] multidata: $MULTIDATA"

echo "[ab2] step 0: clean slate + test config"
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
rm -rf "$USERPROFILE/Documents/My Games/creeperworld4/archipelago/slots" 2>/dev/null
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
Host = localhost
Port = 38281
Slot = $SLOT
Password =
AutoConnect = true

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF
sleep 2

echo "[ab2] step 1: start server"
rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port 38281 --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
sleep 8
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up"

echo "[ab2] step 2: launch + connect"
rm -f "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12
MARK=0   # BepInEx truncates LogOutput.log on launch; index from its start
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"
# save archiving: active.txt must name this slot (whether it switched this run
# or was already active from a prior run - the invariant is per-slot isolation).
sleep 2
ACTIVE=$(cat "$USERPROFILE/Documents/My Games/creeperworld4/archipelago/save-archive/active.txt" 2>/dev/null)
echo "[ab2]   save-archive active='$ACTIVE'"
echo "$ACTIVE" | grep -q "$SLOT"; verdict $? "saves isolated to this slot (active=$ACTIVE)"

# --- (A) save-load gate DECISION (same check the OnLoad patch uses) ---
echo "[ab2] step 3: save-load gate decision"
mark
send "gatecheck:story3"; sleep 1        # locked
send "gatecheck:story1"; sleep 1        # starter, unlocked
since | grep -q "GATECHECK: 'story3' allowed=False"; verdict $? "locked mission load denied"
since | grep -q "GATECHECK: 'story1' allowed=True"; verdict $? "unlocked mission load allowed"

# --- (B) live tracker update while the level-select page is open ---
echo "[ab2] step 4: live tracker update while viewing"
mark
send "story:open"; sleep 6
send "tracker:dump"; sleep 2
since | grep -q "TRACKER: story10 'War and Peace' status=Locked"; verdict $? "story10 locked before item"
mark
srv "/send $SLOT Mission Unlock: War and Peace"
wait_since "AP ITEM RECEIVED: Mission Unlock: War and Peace" 20
srv "/send $SLOT Cannon"
sleep 4
send "tracker:dump"; sleep 2
since | grep -qE "TRACKER: story10 'War and Peace' status=(InLogic|Partial)"; verdict $? "story10 unlocked live while page open"

# --- (C) server-message toasts (receive path) ---
echo "[ab2] step 5: server message received (toast path)"
mark
srv "/send $SLOT Terp"
wait_since "AP MESSAGE:" 20; verdict $? "server message received for toast"

# --- (D) build-limit item: unlimited stays unlimited; limited unit increases ---
echo "[ab2] step 6: build-limit item"
mark
send "boot:story10"
if ! wait_since "New GameSpace" 45; then echo "[ab2] FATAL: story10 load failed"; fi
sleep 4
send "ada:close"; sleep 1
# cannon is unlimited (base -1); a +1 must NOT turn it into a cap of 0.
mark
send "limit:cannon"; sleep 2
CB=$(since | grep -oE "DEBUG LIMIT: cannon=-?[0-9]+" | tail -1 | grep -oE "\-?[0-9]+$")
srv "/send $SLOT Build Limit +1 (Cannon)"
wait_since "AP ITEM RECEIVED: Build Limit" 20
mark
send "limit:cannon"; sleep 2
CN=$(since | grep -oE "DEBUG LIMIT: cannon=-?[0-9]+" | tail -1 | grep -oE "\-?[0-9]+$")
echo "[ab2]   cannon limit $CB -> $CN (unlimited must be unchanged)"
[ "${CN:-0}" -eq "${CB:--1}" ]; verdict $? "unlimited unit unchanged by +1 (no cap-to-zero bug)"
# tower: report its base and, if it has a real cap, verify +1 raises it.
mark
send "limit:tower"; sleep 2
TB=$(since | grep -oE "DEBUG LIMIT: tower=-?[0-9]+" | tail -1 | grep -oE "\-?[0-9]+$")
echo "[ab2]   tower base limit=$TB"
srv "/send $SLOT Build Limit +1 (Tower)"
wait_since "AP ITEM RECEIVED: Build Limit +1 (Tower)" 20
mark
send "limit:tower"; sleep 2
TN=$(since | grep -oE "DEBUG LIMIT: tower=-?[0-9]+" | tail -1 | grep -oE "\-?[0-9]+$")
echo "[ab2]   tower limit $TB -> $TN"
if [ "${TB:--1}" -ge 0 ]; then
  [ "${TN:-0}" -eq "$(( ${TB:-0} + 1 ))" ]; verdict $? "limited unit (tower) +1 applied ($TB -> $TN)"
else
  echo "[ab2] NOTE: tower is unlimited by default too - build-limit items are no-ops; apworld filler should be reconsidered."
fi

# --- (E) ERN item drives the granter (spawn-beside-rift-lab proven live;
#         the automated boot has no rift lab placed, so it correctly waits) ---
echo "[ab2] step 7: progressive ERN item"
mark
srv "/send $SLOT Progressive ERN"
wait_since "AP ITEM RECEIVED: Progressive ERN" 20
sleep 3
since | grep -qE "ERN: target [1-9] this mission"; verdict $? "ERN item registered with granter"

# --- (F) goal: beat the finale ---
echo "[ab2] step 8: goal on finale"
srv "/send $SLOT Mission Unlock: Ever After"
sleep 2
mark
send "boot:story20"
if ! wait_since "New GameSpace" 45; then echo "[ab2] FATAL: story20 load failed"; fi
sleep 4
send "win"; sleep 3
since | grep -q "AP GOAL ACHIEVED sent"; verdict $? "goal sent on finale completion"
sleep 3
grep -qiE "completed (their|its) goal|has completed|goal" "$SRV_LOG"; verdict $? "server recorded goal"

# --- (G) reconnect-on-menu path fires (composite of menu-entry autoconnect +
#         flush-on-reconnect; the flush half is proven in apbattery.sh) ---
echo "[ab2] step 9: menu-entry auto-connect path"
# The startup connection went through the Galaxy-scene auto-connect, which now
# fires on EVERY menu entry (not just the first). Confirm that path logged.
grep -q "AUTOCONNECT: attempting (menu entry)" "$L"; verdict $? "menu-entry auto-connect path fires"

echo "[ab2] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1
kill_servers
echo "[ab2] cleaned up"
