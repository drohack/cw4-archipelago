#!/usr/bin/env bash
# Sets up the ONE check no script can send, then watches for it.
#
# WHAT IS ACTUALLY UNSCRIPTABLE, because the old wording here misled a reader
# into repeating that no script can send this check, which is not true.
#
# The CHECK is scripted already: cache:destroy calls DestroyUnit, mustCollect
# loses its member and the location follows. eventdriven-test.sh and
# instance-dump.sh both drive it, and instance-dump asserts it as a control.
#
# What cannot be scripted is the REAL PICKUP - the game collecting the cache
# because your network reached it. Measured 2026-09-17, and the blocker is
# narrower than "the UI": spawnat: CAN place a tower on the cache cell (proved
# on Home, whose cache MapCells records at 145,91 - it placed), but boot: leaves
# a mission at its LANDING PROMPT and there is no command to put the rift lab
# down. spawnat:riftlab is refused. No rift lab means no network, so the tower
# sits inert and the Collect slot never moves. Landing is a click on the map,
# and synthetic mouse input does not reach CW4's UI.
#
# So a land: command that placed the rift lab at a cell would make this whole
# harness automatable - everything after landing already works.
#
# Separately: InfoCache.Retrieved is NOT the pickup path. It sets the cache's
# own flag and moves neither mustCollect nor the Collect count, and a real
# pickup proved it is never called - see docs/research-findings.md.
#
# This script does everything either side of that: it puts the game in "Home"
# with the cache registered as an Archipelago location and every unit unlocked,
# then polls until the check fires, recording BOTH cache signals so a
# disagreement between them is visible rather than inferred.
#
# Usage: tools/cache-handtest.sh          (game must be CLOSED)
#        Then play, connect your network to the info cache, and read the result.
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

CMD="$AP_CMD"
DEADLINE=1800    # 30 minutes is far longer than the mission needs

send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }

echo "[setup 1/5] game closed, randomizer enabled, debug channel on"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD"
# The build target deploys here; if the mod was parked, bring it back.
if [ -d "$GAME_DIR/BepInEx/plugins-disabled/CW4Archipelago" ] \
   && [ ! -d "$GAME_DIR/BepInEx/plugins/CW4Archipelago" ]; then
  mv "$GAME_DIR/BepInEx/plugins-disabled/CW4Archipelago" "$GAME_DIR/BepInEx/plugins/CW4Archipelago"
fi
mkdir -p "$GAME_DIR/BepInEx/config"
cat > "$GAME_DIR/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
[Connection]
Host = localhost
Port = 38281
Slot = DrohaCW4
Password =
AutoConnect = false

[Missions]
ShowSpan = false

[Debug]
DebugCommands = true
CFGEOF

echo "[setup 2/5] launching"
cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 &
sleep 16

echo "[setup 3/5] unlocking Home and every unit (no server: local fake items)"
send "item:Mission Unlock: Home"
# Every buildable, so the mission plays normally and the test is about the cache
# and nothing else. Build LIMITS are untouched - those are the game's defaults.
for u in Cannon Mortar Nullifier Miner Factory "Greenar Refinery" "Missile Launcher" \
         Sprayer Terp "ERN Portal" Sniper Porter Pylon "Bomber Pad" Runway Shield \
         "AC Bomber Pad" Chronat Microrift Platform "Rocket Pad" Airship Bertha Sweeper; do
  send "item:$u"
done

echo "[setup 4/5] registering Home's locations"
for loc in "Home - Cache 1" "Home - Totem 1" "Home - Totem 2" "Home - Nullify 1" \
           "Home - Mission Complete"; do
  send "loc:add $loc"
done

echo "[setup 5/5] booting story2 (Home)"
send "boot:story2"
for i in $(seq 1 30); do grep -q "LocationWatcher: mission 2" "$GAME_LOG" && break; sleep 2; done
send "ada:close"

echo "----------------------------------------------------------------"
echo "READY. Press play and collect the info cache in Home."
echo "Nothing else is needed - leave this running and it will report."
echo "----------------------------------------------------------------"

start=$SECONDS
last_counts=""
while :; do
  el=$((SECONDS - start))
  if grep -q "LOCATION CHECK: Home - Cache 1" "$GAME_LOG"; then
    send "counts:dump"; sleep 2
    last_counts=$(grep "COUNTS:" "$GAME_LOG" | tail -1)
    echo "[watch ${el}s] PASS: 'Home - Cache 1' was sent"
    echo "  $last_counts"
    n=$(grep -c "LOCATION CHECK: Home - Cache 1" "$GAME_LOG")
    if [ "$n" = 1 ]; then echo "  PASS  sent exactly once"
    else echo "  FAIL  sent $n times - the patch and the safety poll are double-sending"; fi
    break
  fi
  if [ "$el" -ge "$DEADLINE" ]; then
    send "counts:dump"; sleep 2
    echo "[watch ${el}s] TIMEOUT: no cache check in $((DEADLINE / 60)) minutes"
    echo "  $(grep "COUNTS:" "$GAME_LOG" | tail -1)"
    break
  fi
  # Both cache signals, every 30s: mustCollect/max is what LocationWatcher
  # counts, and objective 4's count is the game's own tally. They should agree,
  # and a disagreement is the interesting failure.
  send "counts:dump"; sleep 2
  c=$(grep "COUNTS:" "$GAME_LOG" | tail -1 | grep -o "mustCollect=[0-9]*/[0-9]*")
  o=$(grep "COUNTS:" "$GAME_LOG" | tail -1 | grep -o "4:[a-z]*/count=[0-9]*/[A-Za-z]*")
  echo "[watch ${el}s/${DEADLINE}s] waiting for a cache pickup in Home: $c objective$o"
  sleep 26
done

echo "Done. Log: $GAME_LOG"
