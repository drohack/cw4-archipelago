#!/bin/bash
# Regression battery v2 - per-boot log markers, no stale-line races.
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/probe-unlocks.txt"
S="${TEMP:-/tmp}"
PASS=0; FAIL=0
MARK=0

mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }

since() {
  local cur; cur=$(wc -l < "$L" 2>/dev/null || echo 0)
  if [ "$cur" -lt "$MARK" ]; then MARK=0; fi   # log was truncated by game relaunch
  tail -n +"$((MARK+1))" "$L" 2>/dev/null
}

send() { printf "%b" "$1" > "$CMD"; sleep 2; }

wait_since() { # pattern, iters
  for i in $(seq 1 "$2"); do
    since | grep -q "$1" && return 0
    sleep 2
  done
  return 1
}

assert_check() { # name, expected buttons
  mark
  send "check\n"; sleep 1
  local line; line=$(since | grep "CHECK:" | tail -1)
  if echo "$line" | grep -q "structActive=True .*structButtons=$2"; then
    echo "PASS: $1 [$line]"; PASS=$((PASS+1))
  else
    echo "FAIL: $1 [$line]"; FAIL=$((FAIL+1))
  fi
}

boot_and_verify() { # name, mission
  echo "[battery] $1: booting $2"
  mark
  send "boot:$2\n"
  if ! wait_since "New GameSpace" 45; then
    echo "FAIL: $1 mission never loaded"; FAIL=$((FAIL+1)); return 1
  fi
  if wait_since "REVEAL OK\|REVEAL FAILED" 45; then
    if since | grep -q "REVEAL OK"; then echo "PASS: $1 reveal"; PASS=$((PASS+1));
    else echo "FAIL: $1 reveal"; FAIL=$((FAIL+1)); fi
  else
    echo "FAIL: $1 reveal timeout"; FAIL=$((FAIL+1))
  fi
  send "ada:close\n"
  return 0
}

shot() { powershell -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/screenshot.ps1" "$S/b2-$1.png" >/dev/null 2>&1; }

echo "[battery] launching game"
mark
echo "autoboot:story7" > "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12
powershell -NoProfile -Command "(New-Object -ComObject WScript.Shell).AppActivate('Creeper World 4')" >/dev/null 2>&1

if wait_since "New GameSpace" 60 && wait_since "REVEAL OK\|REVEAL FAILED" 45; then
  if since | grep -q "REVEAL OK"; then echo "PASS: boot1 reveal"; PASS=$((PASS+1));
  else echo "FAIL: boot1 reveal"; FAIL=$((FAIL+1)); fi
else
  echo "FAIL: boot1 load/reveal timeout"; FAIL=$((FAIL+1))
fi
send "ada:close\n"
assert_check "boot1 default struct(2)" 2
shot "boot1"

send "miner\ngreenarrefinery\n"
assert_check "combo A struct(4)" 4
send "lock:pylon\n"
assert_check "combo B struct(3)" 3
send "reset\n"
assert_check "combo C struct(2)" 2

mark
send "limit:tower:2\nlimit:cannon:1\n"; sleep 1
if since | grep "LIMIT SET" | grep -q "'tower' = 2 (readback 2)" && since | grep "LIMIT SET" | grep -q "'cannon' = 1 (readback 1)"; then
  echo "PASS: limits readback"; PASS=$((PASS+1))
else
  echo "FAIL: limits readback [$(since | grep 'LIMIT SET' | tr '\n' ' ')]"; FAIL=$((FAIL+1))
fi

if boot_and_verify "boot2" "story1"; then
  assert_check "boot2 default struct(2)" 2
  shot "boot2"
  send "miner\ngreenarrefinery\nterp\nporter\n"
  assert_check "combo D struct(6)" 6
fi

if boot_and_verify "boot3" "story7"; then
  assert_check "boot3 persisted struct(6)" 6
  shot "boot3"
fi

mark
send "flags\n"; sleep 1
if since | grep -q "FIGHTING"; then echo "FAIL: flag fights"; FAIL=$((FAIL+1)); else echo "PASS: no flag fights"; PASS=$((PASS+1)); fi

ERRS=$(grep -cE "Probe tick failed|Fatal" "$L")
if [ "$ERRS" -eq 0 ]; then echo "PASS: zero plugin errors"; PASS=$((PASS+1)); else echo "FAIL: $ERRS plugin errors"; FAIL=$((FAIL+1)); fi

echo "[battery] COMPLETE: $PASS passed, $FAIL failed"
