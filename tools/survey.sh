#!/bin/bash
# Campaign survey: graph + per-mission natural flags, objectives, enemy census.
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/probe-unlocks.txt"
OUT="${TEMP:-/tmp}/cw4-survey-raw.log"
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() {
  local cur; cur=$(wc -l < "$L" 2>/dev/null || echo 0)
  if [ "$cur" -lt "$MARK" ]; then MARK=0; fi
  tail -n +"$((MARK+1))" "$L" 2>/dev/null
}
send() { printf "%b" "$1" > "$CMD"; sleep 2; }
wait_since() { for i in $(seq 1 "$2"); do since | grep -q "$1" && return 0; sleep 2; done; return 1; }

: > "$OUT"
echo "[survey] launching game"
mark
rm -f "$CMD"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 12
if ! wait_since "SCENE: 'GameLoad' -> 'Galaxy'" 40; then echo "[survey] game never reached menu"; exit 1; fi
sleep 8
send "enforce:off\n"
send "story:open\n"; sleep 2
mark
send "graph\n"; sleep 3
since | grep "GRAPH" >> "$OUT"
echo "[survey] graph captured: $(grep -c GRAPH "$OUT") lines"

for n in $(seq 1 20); do
  M="story$n"
  mark
  send "boot:$M\n"
  if ! wait_since "New GameSpace" 45; then echo "[survey] $M LOAD FAILED"; continue; fi
  sleep 6
  send "ada:close\n"; sleep 1
  mark
  send "natflags\nobjdump\ncensus\n"; sleep 4
  {
    echo "=== $M ==="
    since | grep -E "NATFLAGS|OBJDUMP|CENSUS"
  } >> "$OUT"
  echo "[survey] $M done"
done
echo "[survey] COMPLETE -> $OUT"
