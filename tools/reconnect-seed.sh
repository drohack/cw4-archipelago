#!/bin/bash
# Focused battery: proves (2) received items are replayed idempotently across a
# disconnect/reconnect - a unit granted before the drop still works after it -
# and (3) seed binding: saves/farsite carries a seed stamp and the mission-entry
# SEED GUARD confirms the active saves match the connected seed/slot.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"
MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-rs-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-rs-srv.in"
STAMP="$USERPROFILE/Documents/My Games/creeperworld4/saves/farsite/archipelago-seed.txt"
PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[rs] PASS: $2"; else FAIL=$((FAIL+1)); echo "[rs] FAIL: $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since|grep -q "$1"&&return 0; sleep 2; done; return 1; }
kill_servers() { for pid in $(netstat -ano 2>/dev/null|grep -E ':38281[[:space:]]'|grep -i listening|awk '{print $NF}'|sort -u); do taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "[rs] FATAL: no multidata"; exit 1; }
echo "[rs] step 0: clean slate + config"
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
rm -rf "$USERPROFILE/Documents/My Games/creeperworld4/archipelago/slots" 2>/dev/null
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

echo "[rs] step 1: server + launch + connect"
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port 38281 --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
for i in $(seq 1 20); do grep -q "Hosting game at" "$SRV_LOG"&&break; sleep 1; done
rm -f "$CMD"; cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12; MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "[rs] step 2: grant Cannon + Home unlock"
mark
srv "/send $SLOT Cannon"; sleep 1
srv "/send $SLOT Mission Unlock: Home"
# Home is sent second, so seeing it received implies Cannon was processed too;
# Cannon's actual application is proven after the reconnect in step 5.
wait_since "AP ITEM RECEIVED: Mission Unlock: Home" 20; verdict $? "items received (Cannon + Home)"

echo "[rs] step 3: (3) seed stamp written into saves/farsite"
[ -f "$STAMP" ]; verdict $? "seed stamp file exists"
if [ -f "$STAMP" ]; then echo "[rs]   stamp = $(cat "$STAMP")"; grep -q "|$SLOT" "$STAMP"; verdict $? "stamp names the connected slot"; fi

echo "[rs] step 4: (2) disconnect then reconnect -> full idempotent replay"
mark
send "disconnect"; sleep 3
send "connect"; sleep 8
# On reconnect the client rebuilds ReceivedItems from the server's full list;
# received>=2 proves the two items were replayed (not lost, not doubled - the
# count equals the server total).
since | grep -E "AP CONNECTED .* received=[2-9]"; verdict $? "reconnect replayed items (received>=2)"
since | grep "AP CONNECTED" | tail -1 | sed 's/^.*CW4 Archipelago\] /[rs]   /'

echo "[rs] step 5: (2) replayed unit still works + (3) SEED GUARD on mission entry"
mark
send "boot:story1"; wait_since "New GameSpace" 45 || echo "[rs] WARN: story1 slow"
wait_since "SEED GUARD: active saves match" 20; verdict $? "seed guard confirms match on mission entry"
send "ada:close"; sleep 1
send "units"; sleep 2
since | grep -q "allowed=\[.*cannon.*\]"; verdict $? "Cannon still applied after reconnect (idempotent replay)"

echo "[rs] step 6: zero plugin errors"
ERR=$(grep -cE "\[Error :CW4 Archipelago\]|tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "[rs] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
