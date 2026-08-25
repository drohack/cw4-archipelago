#!/bin/bash
# ERN spawn/deny probe test on story10 (War and Peace).
# Asserts: ERN unit spawnable, portal unit spawnable, deny destroys free
# ERNs and holds available count at 0, allow restores spawning.
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/probe-unlocks.txt"
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() {
  local cur; cur=$(wc -l < "$L" 2>/dev/null || echo 0)
  if [ "$cur" -lt "$MARK" ]; then MARK=0; fi
  tail -n +"$((MARK+1))" "$L" 2>/dev/null
}
send() { printf "%b" "$1" > "$CMD"; sleep 2; }
wait_since() { for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 2; done; return 1; }
PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "[erntest] PASS: $2"; else FAIL=$((FAIL+1)); echo "[erntest] FAIL: $2"; fi; }

echo "[erntest] step 1/8: launching game, autoboot story10"
mark
rm -f "$CMD"
echo "autoboot:story10" > "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12
wait_since "v0.55" 30; verdict $? "v0.55 plugin loaded"
if ! wait_since "New GameSpace" 60; then echo "[erntest] FATAL: story10 never loaded"; exit 1; fi
sleep 6
send "ada:close\n"
send "enforce:off\n"

echo "[erntest] step 2/8: baseline ern:status"
mark
send "ern:status\n"; sleep 2
since | grep "ERNSTATUS" | sed 's/^/[erntest]   /'
BASE=$(since | grep -oE "availableCount=-?[0-9]+" | head -1 | cut -d= -f2)
echo "[erntest] baseline availableCount=$BASE"

echo "[erntest] step 3/8: spawn raw ERN (ern:make)"
mark
send "ern:make\n"; sleep 3
since | grep -q "ERN SPAWN OK"; verdict $? "raw ERN unit spawned"
send "ern:status\n"; sleep 2
C1=$(since | grep -oE "availableCount=-?[0-9]+" | tail -1 | cut -d= -f2)
echo "[erntest] after ern:make availableCount=$C1"
[ "${C1:--1}" -gt "${BASE:-0}" ]; verdict $? "available count rose ($BASE -> $C1)"

echo "[erntest] step 4/8: spawn ERN portal (ern:portal)"
mark
send "ern:portal\n"; sleep 3
since | grep "ERN SPAWN" | sed 's/^/[erntest]   /'
since | grep -q "ERN SPAWN OK"; verdict $? "portal unit spawned"

echo "[erntest] step 5/8: watch 90s for portal ERN production"
mark
for i in $(seq 1 9); do
  sleep 10
  echo "[erntest] step 5/8: waited $((i*10))s/90s (count changes logged by plugin)"
done
since | grep "ERN COUNT" | sed 's/^/[erntest]   /'

echo "[erntest] step 6/8: ern:deny - free ERNs must be destroyed"
mark
send "ern:deny\n"; sleep 5
since | grep -q "ERN DENIED: destroyed"; verdict $? "deny destroyed free ERN(s)"
send "ern:status\n"; sleep 2
C2=$(since | grep -oE "availableCount=-?[0-9]+" | tail -1 | cut -d= -f2)
echo "[erntest] under deny availableCount=$C2"
[ "${C2:--1}" = 0 ]; verdict $? "available count is 0 under deny"

echo "[erntest] step 7/8: respawn attempt under deny must be swept"
mark
send "ern:make\n"; sleep 6
send "ern:status\n"; sleep 2
C3=$(since | grep -oE "availableCount=-?[0-9]+" | tail -1 | cut -d= -f2)
[ "${C3:--1}" = 0 ]; verdict $? "count still 0 after respawn under deny ($C3)"

echo "[erntest] step 8/8: ern:allow then ern:make restores spawning"
mark
send "ern:allow\n"
send "ern:make\n"; sleep 3
send "ern:status\n"; sleep 2
C4=$(since | grep -oE "availableCount=-?[0-9]+" | tail -1 | cut -d= -f2)
[ "${C4:--1}" -gt 0 ]; verdict $? "ERN spawnable again after allow (count=$C4)"

echo "[erntest] DONE: $PASS passed, $FAIL failed"
