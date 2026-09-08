#!/bin/bash
# The end-to-end assertion for per-structure identity: completing a PARTICULAR
# structure must send THAT structure's location.
#
# This is the assertion that would have caught the original bug. Every existing
# harness asserted only that "a location fired" - `totem 1 sent exactly once` and
# friends - which a high-water mark satisfies perfectly while attaching the check
# to the wrong structure. So the shape here is deliberately different: complete
# one totem, find out from the game WHICH cell became done, compute that cell's
# rank independently in this script, and require the mod to have sent exactly
# that instance.
#
# Shattered is the fixture because its three totems are far apart and its cells
# sort to a useful order - (54,48)=1, (201,141)=2, (18,185)=3 - so the totem the
# game happens to complete first is very unlikely to be rank 1. Under the old
# code every first completion sent "Totem 1", so a mismatch here is exactly the
# regression this replaces.
#
# A SERVER IS REQUIRED, unlike instance-dump.sh: a check is only sent for a name
# in SlotState.AllLocations, which comes from slot data, so an offline run would
# pass by never sending anything at all.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"; CMD="$CW4/BepInEx/cw4ap-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
SLOT="DrohaCW4"; MULTIDATA="${1:-$(ls -t "$REPO"/.aptest/server/*.archipelago 2>/dev/null | head -1)}"
SRV_LOG="${TEMP:-/tmp}/cw4-identity-srv.log"; SRV_IN="${TEMP:-/tmp}/cw4-identity-srv.in"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null||echo 0); [ "$c" -lt "$MARK" ]&&MARK=0; tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { local pat="$1" n="${2:-20}" i; for i in $(seq 1 "$n"); do
                 since | grep -q "$pat" && return 0; sleep 1; done; return 1; }

listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
pids_on_port() { netstat -ano | grep "LISTENING" | grep ":$1 " | awk '{print $NF}' | sort -u; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }
SRV_PID=""; PORT=""
stop_server() {
  if [ -n "$SRV_PID" ]; then printf "/exit\n" >> "$SRV_IN" 2>/dev/null; sleep 2; kill "$SRV_PID" 2>/dev/null; fi
  if [ -n "$PORT" ]; then
    for pid in $(pids_on_port "$PORT"); do taskkill //PID "$pid" //F >/dev/null 2>&1; done
  fi
  SRV_PID=""
}

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
  if [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ]; then cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"; fi
  rm -rf "$STORE_DIR/slots"; rm -f "$STORE_DIR/last-session.json"
  if [ -n "$STORE_BAK" ] && [ -d "$STORE_BAK" ]; then
    [ -d "$STORE_BAK/slots" ] && cp -r "$STORE_BAK/slots" "$STORE_DIR/slots"
    [ -f "$STORE_BAK/last-session.json" ] && cp "$STORE_BAK/last-session.json" "$STORE_DIR/"
    rm -rf "$STORE_BAK"
  fi
  echo "  (config and slot cache restored)"
}

[ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ] && { echo "FATAL: no multidata"; exit 1; }
save_env
trap restore_env EXIT

echo "instance-identity: $(basename "$MULTIDATA")"
echo "step 0/5: clean slate on a free port"
taskkill //IM CW4.exe //F >/dev/null 2>&1
PORT="$(find_free_port 38351 38400)" || { echo "ABORT: no free port"; exit 1; }
echo "  serving on $PORT"
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

echo "step 1/5: server + connect"
: > "$SRV_LOG"; rm -f "$SRV_IN"; : > "$SRV_IN"
tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python MultiServer.py "$MULTIDATA" --port "$PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
SRV_PID=$!
for i in $(seq 1 25); do grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && break; sleep 1; done
grep -q "Hosting game at" "$SRV_LOG"; verdict $? "server up on $PORT"
rm -f "$CMD"; cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 14; MARK=0
wait_since "AP CONNECTED slot='$SLOT'" 60; verdict $? "connected"

echo "step 2/5: boot Shattered"
mark
srv "/send $SLOT Mission Unlock: Shattered"
wait_since "AP ITEM RECEIVED: Mission Unlock: Shattered" 25; verdict $? "received the Shattered unlock"
send "boot:story11"
wait_since "New GameSpace" 60; verdict $? "Shattered loaded"
send "ada:close"; sleep 4

# The premise: all three totems present and none done, or "which one became
# done" below has nothing to measure.
mark; send "inst:dump"; sleep 3
BEFORE=$(since | grep "INST TOTEM raw=")
NDONE=$(echo "$BEFORE" | grep -c "done=True")
NTOT=$(echo "$BEFORE" | grep -c "raw=")
echo "$BEFORE" | sed 's/^.*INST TOTEM/    INST TOTEM/'
[ "${NTOT:-0}" -eq 3 ] && [ "${NDONE:-9}" -eq 0 ]
verdict $? "three totems, none complete (premise: $NTOT present, $NDONE done)"

echo "step 3/5: complete ONE totem and see which location fires"
mark
send "totem:complete"; sleep 5
AFTER=$(since | grep "INST TOTEM raw=")
send "inst:dump"; sleep 3
AFTER=$(since | grep "INST TOTEM raw=")
CHECKS=$(since | grep "LOCATION CHECK: Shattered - Totem")
echo "$AFTER" | sed 's/^.*INST TOTEM/    INST TOTEM/'
echo "$CHECKS" | sed 's/^.*LOCATION CHECK/    LOCATION CHECK/'

# Rank the completed cell INDEPENDENTLY, by the documented rule: (cellY, cellX)
# ascending, 1-based. Computed here rather than read from the mod, so the mod
# cannot mark its own homework.
EXPECT=$(CELLS="$AFTER" python -c "
import os, re
rows = []
for line in os.environ['CELLS'].splitlines():
    m = re.search(r'cell=\((\d+),(\d+)\) done=(True|False)', line)
    if m:
        rows.append((int(m.group(1)), int(m.group(2)), m.group(3) == 'True'))
ranked = sorted(rows, key=lambda r: (r[1], r[0]))
done = [i for i, r in enumerate(ranked, 1) if r[2]]
print(' '.join(str(d) for d in done))
")
echo "    expected instance(s) done, by cell rank: [$EXPECT]"
[ -n "$EXPECT" ]; verdict $? "the dump says a totem became complete"

OK=1
for n in $EXPECT; do
  echo "$CHECKS" | grep -q "LOCATION CHECK: Shattered - Totem $n" || OK=0
done
[ "$OK" = 1 ] && [ -n "$EXPECT" ]; verdict $? "the location sent matches the completed structure's rank"

# And the negative half. If the completed totem is NOT rank 1, then "Totem 1"
# must not have been sent - that is precisely what the old high-water mark did
# on every first completion, so this is the assertion that separates the two
# implementations.
if [ -n "$EXPECT" ] && ! echo "$EXPECT" | grep -qw 1; then
  if echo "$CHECKS" | grep -q "LOCATION CHECK: Shattered - Totem 1"; then
    verdict 1 "Totem 1 was NOT sent for a totem that is not rank 1"
  else
    verdict 0 "Totem 1 was NOT sent for a totem that is not rank 1"
  fi
else
  echo "  SKIP  Totem 1 was NOT sent (the game completed rank 1 this run, so there is nothing to distinguish)"
fi

echo "step 3b/5: collecting a PARTICULAR cache sends THAT cache's check"
# The hard case, and the one worth proving. A collected cache is destroyed, so
# unlike a nullified structure or a finished totem there is nothing left in the
# scene to identify - the index comes from the known map cells instead. Farsite
# has two, at (66,51) and (150,63), sorting to instances 1 and 2.
mark
send "item:Mission Unlock: Farsite"
send "boot:story1"
if wait_since "New GameSpace" 60; then
  send "ada:close"; sleep 4
  mark; send "inst:dump"; sleep 3
  CBEFORE=$(since | grep -oE "INST CACHE raw=[0-9]+ cell=\([0-9]+,[0-9]+\)" | grep -oE "[0-9]+,[0-9]+" | sort)
  NC=$(printf "%s" "$CBEFORE" | grep -c ",")
  [ "${NC:-0}" -eq 2 ]; verdict $? "two caches present to tell apart (premise: ${NC:-0})"

  mark
  send "cache:destroy"; sleep 5
  send "inst:dump"; sleep 3
  CAFTER=$(since | grep -oE "INST CACHE raw=[0-9]+ cell=\([0-9]+,[0-9]+\)" | grep -oE "[0-9]+,[0-9]+" | sort)
  CHECKS=$(since | grep -oE "LOCATION CHECK: Farsite - Cache [0-9]+")
  echo "    remaining before: $(printf "%s" "$CBEFORE" | tr "\n" " ")"
  echo "    remaining after : $(printf "%s" "$CAFTER" | tr "\n" " ")"
  echo "    checks sent     : $(printf "%s" "$CHECKS" | tr "\n" " ")"

  # The instance is computed HERE from the cell that vanished, by the documented
  # rule - (cellY, cellX) ascending - so the mod is not asked to confirm its own
  # answer.
  EXPECTC=$(BEFORE="$CBEFORE" AFTER="$CAFTER" python -c "
import os
def cells(v):
    out = []
    for line in v.splitlines():
        line = line.strip()
        if not line:
            continue
        x, y = line.split(',')
        out.append((int(x), int(y)))
    return out
before = cells(os.environ['BEFORE'])
after = set(cells(os.environ['AFTER']))
order = sorted(before, key=lambda c: (c[1], c[0]))
gone = [i for i, c in enumerate(order, 1) if c not in after]
print(' '.join(str(g) for g in gone))
")
  echo "    expected instance by cell rank: [$EXPECTC]"
  [ -n "$EXPECTC" ]; verdict $? "exactly one cache disappeared"

  OKC=1
  for n in $EXPECTC; do
    printf "%s" "$CHECKS" | grep -q "Farsite - Cache $n" || OKC=0
  done
  [ "$OKC" = 1 ] && [ -n "$EXPECTC" ]; verdict $? "the check sent names the cache that was taken"

  # The negative half: the OTHER instance must not have been sent. A
  # count-based rule sends "Cache 1" for whichever cache goes first, so this is
  # what separates the two implementations.
  OTHER=1
  for n in $EXPECTC; do [ "$n" = 1 ] && OTHER=2; done
  if printf "%s" "$CHECKS" | grep -q "Farsite - Cache $OTHER"; then
    verdict 1 "the untouched cache (Cache $OTHER) was NOT checked"
  else
    verdict 0 "the untouched cache (Cache $OTHER) was NOT checked"
  fi
else
  verdict 1 "two caches present to tell apart (Farsite did not load)"
  verdict 1 "exactly one cache disappeared"
  verdict 1 "the check sent names the cache that was taken"
  verdict 1 "the untouched cache was NOT checked"
fi

echo "step 4/5: the server recorded it"
sleep 2
FOUND=0
for n in $EXPECT; do
  grep -q "Shattered - Totem $n" "$SRV_LOG" && FOUND=1
done
[ "$FOUND" = 1 ]; verdict $? "the server recorded the same instance"

echo "step 5/5: zero plugin errors"
ERR=$(grep -cE "\[Error  :CW4 Archipelago\]|\[Error   :CW4 Archipelago\]|tick failed" "$L" 2>/dev/null); ERR=${ERR:-0}
[ "$ERR" -eq 0 ]; verdict $? "no plugin errors ($ERR)"

echo "---"
echo "instance-identity: $PASS passed, $FAIL failed"
