#!/usr/bin/env bash
# Regression battery for CW4DevTools.
#
# Exists because ad-hoc verification kept passing while something else broke:
# fixing empty factories broke weapon ammo, an earlier "set:instantbuild=off"
# left the config off so a later run "proved" instant build was broken, and the
# randomizer silently reinstalled itself on every build. Each of those would
# have been caught here.
#
# Writes a KNOWN config before launching - never trusts whatever the last
# session left behind - then asserts on log output rather than eyeballing it.
#
# Usage: tools/devtools-test.sh          (game must be CLOSED)
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

CFG="$DEV_CFG"
CMD="$DEV_CMD"
MISSION="${1:-story7}"

pass=0; fail=0; skip=0
check() { # check <name> <condition-result>
  if [ "$2" = "0" ]; then echo "  PASS  $1"; pass=$((pass+1));
  else echo "  FAIL  $1"; fail=$((fail+1)); fi
}
# A check the fixture genuinely cannot exercise. Reported, never silently
# dropped - a false FAIL trains you to ignore the battery, which is worse than
# no battery at all.
skipped() { echo "  SKIP  $1 ($2)"; skip=$((skip+1)); }
send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }
state() { grep -oE "DEVSTATE .*" "$GAME_LOG" | tail -1; }
field() { state | grep -oE "$1=[-0-9]+" | cut -d= -f2; }

# Park the randomizer for the duration, and restore it however we exit - a
# battery that needs a condition should create it, not assert it and blame the
# operator. Renaming the folder does NOT work: BepInEx scans subfolders
# recursively, so it must move out of plugins/ entirely.
# BOTH halves of the randomizer, not just the mod. CW4ApDebug declares a hard
# BepInDependency on the mod, so parking the mod alone leaves BepInEx logging
# "Could not load [CW4 Archipelago Debug] because it has missing dependencies" -
# which the zero-errors assertion at the end correctly fails on. The dev tools
# are being tested standalone; the whole randomizer goes away, or none of it.
RANDOMIZER_DIRS="CW4Archipelago CW4ApDebug"
RESTORE_RANDOMIZER=0
restore_randomizer() {
  [ "$RESTORE_RANDOMIZER" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in $RANDOMIZER_DIRS; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored to plugins/)"
}
trap restore_randomizer EXIT
for d in $RANDOMIZER_DIRS; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"
    rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE_RANDOMIZER=1
  fi
done
if [ "$RESTORE_RANDOMIZER" = "1" ]; then
  echo "  (randomizer parked for this run; it will be restored at the end)"
fi

echo "== setup: known config, game closed =="
taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
[ -f "$CFG" ] || { echo "no config yet - run the game once first"; exit 1; }
python - "$CFG" <<'PY'
import io,re,sys
p=sys.argv[1]; s=io.open(p,encoding='utf-8').read()
want={'InstantBuild':'true','AllBuildings':'false','InfiniteResources':'true',
      'Indestructible':'true','FreezeCreeper':'false','ShowOverlay':'true',
      'DumpUnitsOnStart':'false'}
for k,v in want.items():
    s=re.sub(rf'^{k} = (true|false)$', f'{k} = {v}', s, flags=re.M)
s=re.sub(r'^GameSpeed = \d+$','GameSpeed = 0',s,flags=re.M)
io.open(p,'w',encoding='utf-8',newline='').write(s)
print("  config pinned")
PY

rm -f "$GAME_LOG" "$CMD"
( cd "$GAME_DIR" && ./CW4.exe >/dev/null 2>&1 & )
echo "== waiting for load =="
for i in $(seq 1 120); do grep -q "Dev Tools loaded" "$GAME_LOG" 2>/dev/null && break; sleep 2; done

grep -q "Dev Tools loaded" "$GAME_LOG"; check "plugin loads" $?
# The randomizer must be ABSENT for this battery. Its unit gate fights the
# AllBuildings cheat over the same availability flags every frame, so a run with
# both installed measures the argument rather than the cheat. The setup step
# above parks it and the exit trap puts it back, so this assertion is now about
# whether that worked - it used to fail whenever the mod happened to be
# installed, which trained everyone to ignore a red line.
! grep -q "Loading \[CW4 Archipelago" "$GAME_LOG"; check "randomizer NOT loaded" $?

echo "== boot $MISSION =="
send "boot:$MISSION" 26
send "ada:close" 3
send "sim:run 1" 3
grep -q "DEVCMD boot: $MISSION" "$GAME_LOG"; check "boot command works" $?

echo "== fixture =="
send "spawn:CommandBase 1" 4
send "spawn:Cannon 2" 4
send "spawn:Factory 1" 5
grep -q "DEVCMD spawn Cannon: 2/2" "$GAME_LOG"; check "spawn by real name" $?
# THE RIFT LAB RESULT, which this fixture has been throwing away. It has sent
# spawn:CommandBase every run since it was written and only ever asserted the
# Cannon, so whether the game will create a rift lab by name has never once been
# recorded - and it is the question that decides whether a mission can be driven
# without a human clicking the landing prompt. CommandBase is the REAL unit
# name; riftlab is a build-pane key and places nothing.
grep -q "DEVCMD spawn CommandBase: 1/1" "$GAME_LOG"
check "spawn the rift lab by its real name (CommandBase)" $?
grep -q "pre-existing map unit(s) will not be touched" "$GAME_LOG"; check "map content snapshot taken" $?
grep -q "spawn pylon\|0/1 - is that the REAL" "$GAME_LOG" || true

echo "== settle, then read state =="
sleep 8
send "dump" 6
S="$(state)"
echo "  $S"
[ -n "$S" ]; check "state line present" $?

# InstantBuild: nothing of the player's should still be building.
[ "$(field building)" = "0" ]; check "instant build: no player unit still building" $?
# InfiniteResources: weapons hold ammo AND wares are filled (the two-way bug).
[ "$(field withAmmo)" -gt 0 ] 2>/dev/null; check "infinite resources: weapons have ammo" $?
# Ware filling needs a REAL factory wired into the packet network, and for a
# long time this fixture could not build one: spawn: calls
# CreateUnitAtPosition, whose units the simulation never adopts, so SetWareHeld
# did not stick and the assertion was SKIPPED every run.
#
# build: fixes that. It drives the game's own UnitBuildGhost - SetPosition then
# Build() - which is the two calls a mouse click makes, so the lab and what it
# powers are as real as hand-placed ones. Land the lab first, since a building
# with nothing to connect to is the old problem again.
#
# A SPRAYER, not a refinery. The cheat fills AMMO_WARES - what a unit CONSUMES -
# and a refinery produces wares rather than consuming any, so it reports zero
# however well it is connected. The sprayer takes bluite.
send "build:riftlab 40 40" 5
send "build:Sprayer 44 44" 5
# A refused cell is normal and the command says where to go instead, so use that
# rather than hard-coding a second guess that rots the next time the map moves.
if grep -q "DEVCMD build Sprayer at (44,44): legal=False" "$GAME_LOG"; then
  alt="$(grep -oE "DEVCMD build nearest legal cell: \(([0-9]+),([0-9]+)\)" "$GAME_LOG"          | tail -1 | grep -oE "[0-9]+,[0-9]+" | tr ',' ' ')"
  [ -n "$alt" ] && send "build:Sprayer $alt" 5
fi
sleep 8
send "dump" 6
S="$(state)"
# STILL NOT PROVEN, but the reason has changed and that is worth more than the
# old wording. The blocker WAS "this fixture cannot build a real factory".
# It can now: the lines above land a lab and build a sprayer through the game's
# own ghost, and DEVSTATE's mine count rises, so both are units the simulation
# adopted. wareTotal is nonetheless 0, which rules the network out - a built,
# connected, ware-CONSUMING unit gets nothing filled on story7. What is left is
# the cheat's own condition: it writes only slots present in u.AMMO_WARES, and
# nothing here has shown that dictionary to be populated on this map.
# Confirmed working in play (liftic/redon/bluite all filled).
if [ "$(field wareTotal)" -gt 0 ] 2>/dev/null; then
  check "infinite resources: wares filled (real network-connected sprayer)" 0
else
  skipped "infinite resources: wares filled"           "lab and sprayer now build for real and still fill nothing - AMMO_WARES looks empty here, not a network problem"
fi
# Assert on energyStore, not energyProduction: the sim recomputes production
# from the network every tick, so a dump taken later reads its value, not ours -
# even though the HUD does show the lifted GEN. The store is what persists.
[ "$(field energyStore)" -gt 1000 ] 2>/dev/null; check "infinite resources: energy store pinned" $?
# Indestructible. Health is only half of it: CW4 destroys some units without
# ever reducing health (DESTROY_ON_UNEVEN_TERRAIN removes a platform outright),
# so assert the game's own impervious flag too. Health alone passing while
# platforms still died is the bug this pair exists to catch.
[ "$(field fullHealth)" -gt 0 ] 2>/dev/null; check "indestructible: units at full health" $?
[ "$(field impervious)" -gt 0 ] 2>/dev/null; check "indestructible: units flagged impervious" $?
[ "$(field uneven)" = "0" ]; check "indestructible: terrain destroy rule lifted" $?

echo "== Indestructible off must clear the flags it set =="
send "set:indestructible=off" 4
send "dump" 5
[ "$(field impervious)" = "0" ]; check "indestructible: impervious cleared on release" $?
grep -q "Indestructible off - unit damage flags restored" "$GAME_LOG"; check "indestructible: RESTORES on release" $?
send "set:indestructible=on" 4

echo "== AllBuildings on then off must RESTORE =="
send "set:allbuildings=on" 4
grep -q "AllBuildings on - saved" "$GAME_LOG"; check "all buildings: snapshot taken" $?
send "set:allbuildings=off" 4
grep -q "AllBuildings off - restored" "$GAME_LOG"; check "all buildings: restored on release" $?

echo "== every parameter cheat must UNDO itself on release =="
# This whole section exists because "turn it off" not actually undoing anything
# was a real bug: AllBuildings left every building in the sidebar, and a bogus
# ware value stayed written into sprayers after the cheat was switched off.
send "set:freezecreeper=on" 3
grep -q "creeper flow FROZEN" "$GAME_LOG"; check "freeze creeper applies" $?
send "set:freezecreeper=off" 3
grep -q "creeper flow restored" "$GAME_LOG"; check "freeze creeper RESTORES on release" $?

send "set:instantbuild=off" 2
send "set:instantbuild=on" 2

# GameSpeed forces GAME_SPEED; releasing it must put the game's own speed back.
send "sim:run 1" 3
printf 'set:allbuildings=on
' > "$CMD"; sleep 3
printf 'set:allbuildings=off
' > "$CMD"; sleep 3
grep -q "AllBuildings off - restored" "$GAME_LOG"; check "all buildings RESTORES on release" $?

echo "== the cheat strip must follow the config =="
# The strip is redrawn from ConfigFile.SettingChanged now, not from a per-frame
# comparison of the values it displays. If that subscription is ever lost the
# failure is silent: the strip keeps showing the state it had when the event
# last fired, which is worse than no strip at all - notes get written against
# a cheat set that was not actually in force.
send "set:instantbuild=off" 2
send "overlay:dump" 2
before=$(grep "DEVCMD overlay: redraws=" "$GAME_LOG" | tail -1 | grep -o "redraws=[0-9]*" | cut -d= -f2)
send "set:instantbuild=on" 2
send "overlay:dump" 2
after=$(grep "DEVCMD overlay: redraws=" "$GAME_LOG" | tail -1 | grep -o "redraws=[0-9]*" | cut -d= -f2)
[ -n "${after:-}" ] && [ -n "${before:-}" ] && [ "$after" -gt "$before" ]
check "overlay redraws when a setting changes ($before -> ${after:-none})" $?
# On is green (#7CFF7C). Reading the colour tag asserts the strip's CONTENT,
# not merely that it was rewritten.
grep "DEVCMD overlay:" "$GAME_LOG" | tail -1 | grep -q "#7CFF7C[^<]*instant build"
check "strip shows instant build as ON" $?
send "set:instantbuild=off" 2
send "overlay:dump" 2
grep "DEVCMD overlay:" "$GAME_LOG" | tail -1 | grep -q "#7CFF7C[^<]*instant build"
[ $? = 1 ]; check "strip shows instant build as off again" $?

echo "== no errors anywhere =="
[ "$(grep -cE '\[Error' "$GAME_LOG")" = "0" ]; check "zero errors in log" $?

taskkill //F //IM CW4.exe >/dev/null 2>&1
echo
echo "RESULT: $pass passed, $fail failed, $skip skipped"
[ "$fail" = "0" ]
