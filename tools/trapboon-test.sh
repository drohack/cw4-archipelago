#!/usr/bin/env bash
# Traps and boons ARRIVING: sent by a real server as items, received by the mod,
# dispatched by TrapApplier, and applied in a mission.
#
# Why this exists. Every other harness fires effects through the debug plugin's
# "trap:" and "boon:" commands, which call the effect methods directly - so the
# path a player actually takes (server -> ApClient -> SlotState -> TrapApplier ->
# effect) had no in-game test at all. The 2026-10-20 cleanup changed that path:
# Emitter Overdrive's case left TrapApplier and the Resource Cache call lost its
# arguments. This is the test for it, and for what must NOT happen:
#
#   - every generated trap and every boon kind fires once, with its effect line
#   - the retired items (Emitter Overdrive Trap, Build Limit +1, Greenar
#     Refinery) arrive and do nothing - no effect, no unlock, no error
#   - a reconnect re-delivers the whole received list and fires NOTHING again
#
# The dev tools' cheats are pinned OFF except instant build: infinite resources
# would refill what the drains take, and indestructible would pre-empt the
# shield. Instant build only makes the spawned units finished, so the drains and
# the resupply have real weapons to act on.
#
# Usage: tools/trapboon-test.sh      (game must be CLOSED; needs CW4DevTools
#        and CW4ApDebug installed beside the mod)
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

AP_PORT="$(find_free_port 38601 38650)"
if [ -z "$AP_PORT" ]; then echo "[tb] ABORT: no free port in 38601-38650"; exit 1; fi
CMD="$AP_CMD"
SLOT="DrohaCW4"
WORK="$REPO/.aptest/trapboon"
SRV_LOG="$WORK/server.log"
SRV_IN="$WORK/server.in"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[tb] PASS: $2"; else FAIL=$((FAIL+1)); echo "[tb] FAIL: $2"; fi; }
send() { printf "%s\n" "$1" > "$CMD"; sleep "${2:-3}"; }
dev() { printf "%s\n" "$1" > "$DEV_CMD"; sleep "${2:-3}"; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 2; }
wait_since() { local i; for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 2; done; return 1; }
kill_servers() { [ -n "${SRV_PIPE:-}" ] && kill "$SRV_PIPE" 2>/dev/null
                 for pid in $(netstat -ano 2>/dev/null | grep "LISTENING" \
                   | grep ":$AP_PORT " | awk '{print $NF}' | sort -u); do
                   taskkill //PID "$pid" //F >/dev/null 2>&1; done; }

echo "[tb] step 0/7: clean slate, fresh seed, pinned configs"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -rf "$WORK"; mkdir -p "$WORK/players" "$WORK/out"
rm -rf "$HOME/Documents/My Games/creeperworld4/archipelago/slots" \
       "$HOME/Documents/My Games/creeperworld4/archipelago/last-session.json"
# The shipped player yaml, so the seed is what a player would generate.
sed "s/^name: .*/name: $SLOT/" "$REPO/apworld/cw4/player.yaml" > "$WORK/players/$SLOT.yaml"
( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 py -3.13 Generate.py --player_files_path "$WORK/players" \
    --outputpath "$WORK/out" --seed 20261021 > "$WORK/generate.log" 2>&1 )
MULTIDATA_ZIP="$(ls "$WORK"/out/AP_*.zip 2>/dev/null | head -1)"
[ -n "$MULTIDATA_ZIP" ]; verdict $? "seed generated from the shipped player yaml"
( cd "$WORK/out" && py -3.13 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('.')" "$MULTIDATA_ZIP" )
MULTIDATA="$(ls "$WORK"/out/*.archipelago 2>/dev/null | head -1)"

mkdir -p "$GAME_DIR/BepInEx/config"
cat > "$AP_CFG" <<CFGEOF
[Connection]
Host = localhost
Port = $AP_PORT
Slot = $SLOT
Password =
AutoConnect = true

[Missions]
ShowSpan = false
CFGEOF
if [ -f "$DEV_CFG" ]; then
  py -3.13 - "$DEV_CFG" <<'PY'
import io, re, sys
p = sys.argv[1]; s = io.open(p, encoding='utf-8').read()
want = {'InstantBuild': 'true', 'AllBuildings': 'false', 'InfiniteResources': 'false',
        'Indestructible': 'false', 'FreezeCreeper': 'false'}
for k, v in want.items():
    s = re.sub(rf'^{k} = (true|false)$', f'{k} = {v}', s, flags=re.M)
s = re.sub(r'^GameSpeed = \d+$', 'GameSpeed = 0', s, flags=re.M)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
PY
fi

echo "[tb] step 1/7: server on :$AP_PORT, launch, auto-connect"
: > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 py -3.13 MultiServer.py "$MULTIDATA" \
    --port "$AP_PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PIPE=$!
for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && break; sleep 1; done
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up"
rm -f "$CMD" "$DEV_CMD"
( cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 & )
sleep 12
MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 45; verdict $? "auto-connected"

echo "[tb] step 2/7: into a mission with a rift lab and units"
mark
srv "/send $SLOT Mission Unlock: Farsite"
wait_since "AP ITEM RECEIVED: Mission Unlock: Farsite" 20
send "boot:story1"
wait_since "New GameSpace" 45; verdict $? "story1 loaded"
send "ada:close"
# Traps hold until the mission has a rift lab (TrapApplier waits on
# gs.commandBase), and Farsite starts with the lab in the player's hand.
dev "spawn:CommandBase 1" 4
dev "spawn:Cannon 3" 4
dev "spawn:Factory 1" 4
send "sim:run"
sleep 3
since | grep -E "DEVCMD spawn" | sed 's/^.*DEVCMD/[tb]   DEVCMD/'

echo "[tb] step 3/7: each generated trap arrives and fires"
# name|effect line it must produce
for spec in \
  "Spore Strike Trap|TRAP spore: launched" \
  "Spore Scatter Trap|TRAP spore: launched [0-9]*/[0-9]* aim=Scatter" \
  "Rift Breach Trap|TRAP creep: +" \
  "Energy Drain Trap|TRAP energy: store" \
  "Unit Stun Trap|TRAP stun: [1-9][0-9]* player unit" \
  "Ammo Drain Trap|TRAP drain: emptied"; do
  name="${spec%%|*}"; effect="${spec#*|}"
  mark
  srv "/send $SLOT $name"
  wait_since "TRAP: $name" 20; verdict $? "$name dispatched"
  wait_since "$effect" 10; verdict $? "$name took effect"
  since | grep -E "TRAP (spore|creep|energy|stun|drain)" | tail -1 | sed 's/^.*\] /[tb]     /'
done

echo "[tb] step 4/7: each boon kind arrives and fires"
for spec in \
  "Ammo Resupply|BOON resupply: filled" \
  "Energy Cache|BOON energy cache:" \
  "Field Shield|BOON shield: [1-9][0-9]* unit" \
  "Resource Cache|BOON resource cache:" \
  "ERN Surge: Fire Rate|BOON ern surge: Fire Rate"; do
  name="${spec%%|*}"; effect="${spec#*|}"
  mark
  srv "/send $SLOT $name"
  wait_since "BOON: $name" 20; verdict $? "$name dispatched"
  wait_since "$effect" 10; verdict $? "$name took effect"
  since | grep -E "BOON (resupply|energy cache|shield|resource cache|ern surge)" | tail -1 | sed 's/^.*\] /[tb]     /'
done

echo "[tb] step 5/7: retired items arrive and do nothing"
mark
for name in "Emitter Overdrive Trap" "Build Limit +1 (Tower)" "Greenar Refinery"; do
  srv "/send $SLOT $name"
  wait_since "AP ITEM RECEIVED: $name" 20; verdict $? "$name received"
done
sleep 5
! since | grep -qE "TRAP: Emitter Overdrive|TRAP emit"; verdict $? "Emitter Overdrive fired nothing"
send "units"
since | grep "DEBUG UNITS" | tail -1 | sed 's/^.*DEBUG UNITS/[tb]   UNITS/'
since | grep "DEBUG UNITS" | tail -1 | grep -q "allowed=\[" \
  && ! since | grep "DEBUG UNITS" | tail -1 | grep -q "greenarrefinery"
verdict $? "Greenar Refinery unlocked nothing (no Factory held)"

echo "[tb] step 6/7: a reconnect re-delivers everything and fires nothing again"
mark
send "disconnect" 4
send "connect" 2
wait_since "AP CONNECTED slot='$SLOT'" 30; verdict $? "reconnected"
sleep 10
REFIRED=$(since | grep -cE "TRAP: |BOON: ")
[ "$REFIRED" -eq 0 ]; verdict $? "no trap or boon fired again after reconnect ($REFIRED)"

echo "[tb] step 7/7: totals and errors"
TRAPS=$(grep -c "TRAP: " "$GAME_LOG"); BOONS=$(grep -c "BOON: " "$GAME_LOG")
[ "$TRAPS" -eq 6 ]; verdict $? "exactly 6 traps fired in the run ($TRAPS)"
[ "$BOONS" -eq 5 ]; verdict $? "exactly 5 boons fired in the run ($BOONS)"
ERR=$(grep -cE "\[Error|\[Fatal|Exception" "$GAME_LOG"); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no errors or exceptions in the log ($ERR)"

echo "[tb] DONE: $PASS passed, $FAIL failed"
srv "/exit" 2>/dev/null
taskkill //IM CW4.exe //F >/dev/null 2>&1
kill_servers
rm -f "$CMD" "$DEV_CMD"
[ "$FAIL" -eq 0 ]
