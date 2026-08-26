#!/bin/bash
# Message-box behavior battery: the box builds and anchors to the minimap,
# ingests server messages (item receive/send) and connection lines with AP
# colors, and history survives a second mission boot.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"; MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-msgbox-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-msgbox-srv.in"
PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[msgbox] PASS: $2"; else FAIL=$((FAIL+1)); echo "[msgbox] FAIL: $2"; fi; }
MARK=0
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { for i in $(seq 1 "$2"); do since|grep -q "$1"&&return 0; sleep 2; done; return 1; }
kill_servers() { for pid in $(netstat -ano 2>/dev/null|grep -E ':38281[[:space:]]'|grep -i listening|awk '{print $NF}'|sort -u); do taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "[msgbox] FATAL: no multidata"; exit 1; }
echo "[msgbox] step 0: clean slate + config"
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
sleep 2

echo "[msgbox] step 1: server + launch + connect"
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port 38281 --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
for i in $(seq 1 20); do grep -q "Hosting game at" "$SRV_LOG"&&break; sleep 1; done
rm -f "$CMD"; cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12; MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "[msgbox] step 2: boot mission -> box anchors to minimap"
send "boot:story1"; wait_since "New GameSpace" 45; sleep 4
send "ada:close"; sleep 1
wait_since "MSGBOX: anchored to minimap" 20; verdict $? "message box anchored to minimap"
since | grep "MSGBOX: anchored" | sed 's/^.*MSGBOX:/[msgbox]   MSGBOX:/'

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
wait_since "MSGBOX: anchored to minimap" 20; verdict $? "box rebuilt in second mission"

echo "[msgbox] step 7: zero plugin errors"
ERR=$(grep -cE "\[Error :CW4 Archipelago\]|tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "[msgbox] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
