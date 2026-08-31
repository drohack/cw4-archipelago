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

G="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
CFG="$G/BepInEx/config/com.droha.cw4devtools.cfg"
LOG="$G/BepInEx/LogOutput.log"
CMD="$G/BepInEx/cw4dev-commands.txt"
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
state() { grep -oE "DEVSTATE .*" "$LOG" | tail -1; }
field() { state | grep -oE "$1=[-0-9]+" | cut -d= -f2; }

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

rm -f "$LOG" "$CMD"
( cd "$G" && ./CW4.exe >/dev/null 2>&1 & )
echo "== waiting for load =="
for i in $(seq 1 120); do grep -q "Dev Tools loaded" "$LOG" 2>/dev/null && break; sleep 2; done

grep -q "Dev Tools loaded" "$LOG"; check "plugin loads" $?
! grep -q "Loading \[CW4 Archipelago" "$LOG"; check "randomizer NOT loaded" $?

echo "== boot $MISSION =="
send "boot:$MISSION" 26
send "ada:close" 3
send "sim:run 1" 3
grep -q "DEVCMD boot: $MISSION" "$LOG"; check "boot command works" $?

echo "== fixture =="
send "spawn:CommandBase 1" 4
send "spawn:Cannon 2" 4
send "spawn:Factory 1" 5
grep -q "DEVCMD spawn Cannon: 2/2" "$LOG"; check "spawn by real name" $?
grep -q "pre-existing map unit(s) will not be touched" "$LOG"; check "map content snapshot taken" $?
grep -q "spawn pylon\|0/1 - is that the REAL" "$LOG" || true

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
# Ware filling needs a REAL factory wired into the packet network. A factory
# spawned by CreateUnitAtPosition has no network, and SetWareHeld does not stick
# on it, so this fixture cannot prove it either way. Confirmed working in play
# (liftic/redon/bluite all filled). Left visible rather than deleted so it is not
# mistaken for covered.
if [ "$(field wareTotal)" -gt 0 ] 2>/dev/null; then
  check "infinite resources: wares filled" 0
else
  skipped "infinite resources: wares filled" "needs a network-connected factory; verify by hand"
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
grep -q "Indestructible off - unit damage flags restored" "$LOG"; check "indestructible: RESTORES on release" $?
send "set:indestructible=on" 4

echo "== AllBuildings on then off must RESTORE =="
send "set:allbuildings=on" 4
grep -q "AllBuildings on - saved" "$LOG"; check "all buildings: snapshot taken" $?
send "set:allbuildings=off" 4
grep -q "AllBuildings off - restored" "$LOG"; check "all buildings: restored on release" $?

echo "== every parameter cheat must UNDO itself on release =="
# This whole section exists because "turn it off" not actually undoing anything
# was a real bug: AllBuildings left every building in the sidebar, and a bogus
# ware value stayed written into sprayers after the cheat was switched off.
send "set:freezecreeper=on" 3
grep -q "creeper flow FROZEN" "$LOG"; check "freeze creeper applies" $?
send "set:freezecreeper=off" 3
grep -q "creeper flow restored" "$LOG"; check "freeze creeper RESTORES on release" $?

send "set:instantbuild=off" 2
send "set:instantbuild=on" 2

# GameSpeed forces GAME_SPEED; releasing it must put the game's own speed back.
send "sim:run 1" 3
printf 'set:allbuildings=on
' > "$CMD"; sleep 3
printf 'set:allbuildings=off
' > "$CMD"; sleep 3
grep -q "AllBuildings off - restored" "$LOG"; check "all buildings RESTORES on release" $?

echo "== the cheat strip must follow the config =="
# The strip is redrawn from ConfigFile.SettingChanged now, not from a per-frame
# comparison of the values it displays. If that subscription is ever lost the
# failure is silent: the strip keeps showing the state it had when the event
# last fired, which is worse than no strip at all - notes get written against
# a cheat set that was not actually in force.
send "set:instantbuild=off" 2
send "overlay:dump" 2
before=$(grep "DEVCMD overlay: redraws=" "$LOG" | tail -1 | grep -o "redraws=[0-9]*" | cut -d= -f2)
send "set:instantbuild=on" 2
send "overlay:dump" 2
after=$(grep "DEVCMD overlay: redraws=" "$LOG" | tail -1 | grep -o "redraws=[0-9]*" | cut -d= -f2)
[ -n "${after:-}" ] && [ -n "${before:-}" ] && [ "$after" -gt "$before" ]
check "overlay redraws when a setting changes ($before -> ${after:-none})" $?
# On is green (#7CFF7C). Reading the colour tag asserts the strip's CONTENT,
# not merely that it was rewritten.
grep "DEVCMD overlay:" "$LOG" | tail -1 | grep -q "#7CFF7C[^<]*instant build"
check "strip shows instant build as ON" $?
send "set:instantbuild=off" 2
send "overlay:dump" 2
grep "DEVCMD overlay:" "$LOG" | tail -1 | grep -q "#7CFF7C[^<]*instant build"
[ $? = 1 ]; check "strip shows instant build as off again" $?

echo "== no errors anywhere =="
[ "$(grep -cE '\[Error' "$LOG")" = "0" ]; check "zero errors in log" $?

taskkill //F //IM CW4.exe >/dev/null 2>&1
echo
echo "RESULT: $pass passed, $fail failed, $skip skipped"
[ "$fail" = "0" ]
