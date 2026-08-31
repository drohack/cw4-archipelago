#!/usr/bin/env bash
# Resolve the game's real unit names, and settle a contradiction in the docs.
#
# Three name spaces exist and mixing them up has cost this project hours:
# build-pane keys (porterAvailable -> "porter"), unit names (the 88 keys of
# UnitData.unitConstants), and data names (GetDataName(), lowercased). Several
# build-pane keys are not unit names at all, so code comparing one against
# GetDataName() silently skips those buildings - that is how trap stun, weapon
# drain and spore targeting passed over pylons, miners and ERN portals.
#
# "porter" is the one still unresolved, and the docs disagree with themselves
# about it: research-findings.md:626 says 'Reactor' and 'DeliveryPad' are the
# INTERNAL names of the MINER and PORTER buttons, while :107 maps
# miner -> Collector and :163 lists Reactor and DeliveryPad as separate
# buildables. One of those is wrong.
#
# The oracle is already in the dev tools: every build button owns a
# UnitBuildGhost which owns the PREFAB it places, so ghost -> prefab data name is
# the mapping with no guessing. It uses FindObjectsOfTypeAll, so nothing has to
# be built or even unlocked. Also captured: the full 88-name registry, which is
# recorded nowhere in the repo.
#
# Usage: tools/names-probe.sh          (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4dev-commands.txt"      # dev tools channel, not the randomizer's
OUT="${TEMP:-/tmp}/cw4-names-probe.txt"
send() { printf "%s\n" "$1" > "$CMD"; sleep "${2:-2}"; }

echo "[setup 1/3] game closed, dev tools channel"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD"

echo "[setup 2/3] launching"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
for i in $(seq 1 60); do grep -q "Dev Tools loaded" "$L" 2>/dev/null && break; sleep 2; done

# story12 is where Farsite grants the Porter, so its button and ghost are the
# most likely to exist. AllBuildings on so every ghost is loaded regardless.
echo "[setup 3/3] booting story12 (Archon - the mission that grants Porter)"
send "boot:story12" 26
send "ada:close" 3
send "set:allbuildings=on" 3
send "dump" 5

: > "$OUT"
echo "== the decisive mapping: build ghost -> real unit name =="
grep "DEVTOOLS build ghosts" "$L" | tail -1 | tr '  ' '\n' | grep -- "->" | sort | tee -a "$OUT"

echo
echo "== the 88-name registry (recorded nowhere in the repo until now) =="
grep "DEVTOOLS ENEMY=false" "$L" | tail -1 | tee -a "$OUT"

echo
echo "== CMOD units (airship/bertha/sweeper report GUIDs, not names) =="
grep "DEVTOOLS cmods" "$L" | tail -2 | tee -a "$OUT"

# Independent confirmation: spawn takes the REAL name and says so when it is not.
echo
echo "== spawn oracle: does each candidate name actually exist? =="
for n in DeliveryPad DeliveryDrone StoragePad Stash Reactor Collector Porter Strider Workall Transformer Max; do
  send "spawn:$n 1" 3
  line=$(grep "DEVCMD spawn $n:" "$L" | tail -1)
  echo "  ${line#*DEVCMD }" | tee -a "$OUT"
done

echo
echo "== units actually on the map (data name + whether the cheats own it) =="
send "dump" 4
grep "DEVTOOLS units on map" "$L" | tail -1 | tee -a "$OUT"

echo
echo "== anything building that the player filter rejects (names the gap) =="
grep -c "is building but is not in the player list" "$L"

taskkill //IM CW4.exe //F >/dev/null 2>&1
echo "Saved: $OUT"
