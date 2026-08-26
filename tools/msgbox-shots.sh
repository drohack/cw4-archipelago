#!/bin/bash
# Visual + multi-resolution check WITHOUT executable args (avoids Steam's
# custom-args popup): set windowed mode + resolution via Unity's registry keys
# before each launch. Captures the main menu (AP panel vs buttons) and an
# in-mission shot (message box above the minimap) at each size, via PrintWindow
# targeting the Unity game window.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
SLOT="DrohaCW4"; MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-shots-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-shots-srv.in"
OUTDIR="${TEMP:-/tmp}/cw4-msgbox-shots"
PRINTCAP="C:/Users/droha/AppData/Local/Temp/claude/c--Users-droha-Workspace-cw4-archipelago/36c32d39-bf0b-48cf-9ad1-3e88282f7823/scratchpad/printcap.ps1"
REGBASE='HKCU:\Software\Knuckle Cracker\Creeper World 4'
KW='Screenmanager Resolution Width_h182942802'
KH='Screenmanager Resolution Height_h2627697771'
KM='Screenmanager Fullscreen mode_h3630240806'
KN='Screenmanager Resolution Use Native_h1405027254'
MARK=0
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 2; }
wait_since() { for i in $(seq 1 "$2"); do since|grep -q "$1"&&return 0; sleep 2; done; return 1; }
kill_servers() { for pid in $(netstat -ano 2>/dev/null|grep -E ':38281[[:space:]]'|grep -i listening|awk '{print $NF}'|sort -u); do taskkill //PID "$pid" //F >/dev/null 2>&1; done; }
setres() {  # W H -> windowed at WxH via registry (no exe args)
  powershell -NoProfile -Command "Set-ItemProperty -Path '$REGBASE' -Name '$KW' -Value ([int]$1); Set-ItemProperty -Path '$REGBASE' -Name '$KH' -Value ([int]$2); Set-ItemProperty -Path '$REGBASE' -Name '$KM' -Value 3; Set-ItemProperty -Path '$REGBASE' -Name '$KN' -Value 0" >/dev/null 2>&1
}
cap() { powershell -NoProfile -ExecutionPolicy Bypass -File "$PRINTCAP" "$1" 2>&1 | tr -d '\r'; }

mkdir -p "$OUTDIR"; rm -f "$OUTDIR"/*.png
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
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port 38281 --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
for i in $(seq 1 20); do grep -q "Hosting game at" "$SRV_LOG"&&break; sleep 1; done

shoot() {  # W H name
  local W="$1" H="$2" N="$3"
  echo "[shots] === ${W}x${H} (windowed) ==="
  taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
  setres "$W" "$H"
  rm -f "$CMD"
  cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
  sleep 14; MARK=0
  wait_since "AP CONNECTED" 60 || { echo "[shots] ${N}: never connected"; return; }
  # main menu shot (AP panel vs main buttons)
  sleep 2
  cap "$OUTDIR/menu-${N}.png"; echo "[shots] MENU ${N}"
  # in-mission shot (message box above minimap); story10 has a normal minimap
  send "boot:story10"; wait_since "New GameSpace" 45; sleep 5
  send "ada:close"; sleep 2
  srv "/send $SLOT Cannon"; send "objective:1"; srv "/send $SLOT Mortar"
  srv "/send $SLOT Mission Unlock: Home"; send "toast:Archipelago: connected as DrohaCW4"
  sleep 1
  cap "$OUTDIR/game-${N}.png"; echo "[shots] GAME ${N}"
}

shoot 1280 720 1280x720
shoot 1920 1080 1920x1080
shoot 1024 768 1024x768

echo "[shots] DONE -> $OUTDIR"
srv "/exit" 2>/dev/null; sleep 1
kill "$SRV_PIPE" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1; kill_servers
# restore a sane resolution (windowed 1920x1080)
setres 1920 1080
