#!/bin/bash
# Battery for #4 (relevance filter) + #5 (Say). Uses a 2-player seed so the
# "other player" path exists. Connect as DrohaCW4; a self item is relevant=1,
# an item to Player2CW4 is relevant=0 (filtered by default); showall reveals it;
# say: sends chat that the server receives and echoes back (relevant=1).
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"; OTHER="Player2CW4"
MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server2/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-mf-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-mf-srv.in"
PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[mf] PASS: $2"; else FAIL=$((FAIL+1)); echo "[mf] FAIL: $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since|grep -q "$1"&&return 0; sleep 2; done; return 1; }
kill_servers() { for pid in $(netstat -ano 2>/dev/null|grep -E ':38281[[:space:]]'|grep -i listening|awk '{print $NF}'|sort -u); do taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "[mf] FATAL: no 2-player multidata"; exit 1; }
echo "[mf] multidata: $MULTIDATA"
echo "[mf] step 0: clean slate + config"
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

echo "[mf] step 1: server + launch + connect"
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port 38281 --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
for i in $(seq 1 20); do grep -q "Hosting game at" "$SRV_LOG"&&break; sleep 1; done
rm -f "$CMD"; cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12; MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "[mf] step 2: boot mission so the box exists"
send "boot:story1"; wait_since "New GameSpace" 45 || echo "[mf] WARN slow"
send "ada:close"; sleep 2

echo "[mf] step 3: self item -> relevant=1"
mark
srv "/send $SLOT Cannon"; sleep 3
since | grep -q "MSGBOX APPEND relevant=1"; verdict $? "self item classified relevant=1"

echo "[mf] step 4: another player joins -> relevant=0 (filtered by default)"
# A real second client connecting produces a Join notice for the other player,
# which is a guaranteed relevant=0 event for us. (A cheat /send to an OFFLINE
# slot is only a ServerChat 'Cheat console' notice = relevant=1, so it cannot
# exercise this path - hence a real client.)
mark
python3 "$REPO/tools/ap_player2.py" 2 2>&1 | sed 's/^/[mf]   p2: /'
sleep 2
since | grep "MSGBOX APPEND" | sed 's/^.*CW4 Archipelago\] /[mf]   /'
since | grep -q "MSGBOX APPEND relevant=0 shown=0"; verdict $? "other-player event relevant=0 hidden by default"

echo "[mf] step 5: showall:on -> other-player event now shown"
mark
send "showall:on"; sleep 1
since | grep -q "MSGBOX SHOWALL=1"; verdict $? "showall toggled on"
mark
python3 "$REPO/tools/ap_player2.py" 0 2>&1 | sed 's/^/[mf]   p2: /'
sleep 2
since | grep -q "MSGBOX APPEND relevant=0 shown=1"; verdict $? "other-player event shown when showall on"

echo "[mf] step 6: say -> server receives chat + echo relevant=1"
send "showall:off"; sleep 1
mark
send "say:hello from cw4"; sleep 3
since | grep -q "SAY: hello from cw4"; verdict $? "Say invoked"
grep -qi "hello from cw4" "$SRV_LOG"; verdict $? "server received the chat"

echo "[mf] step 7: zero plugin errors"
ERR=$(grep -cE "\[Error :CW4 Archipelago\]|tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "[mf] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
