#!/usr/bin/env bash
# The HAND route to a real cache pickup. Superseded for regression purposes by
# tools/cache_autotest.sh, which now does it unattended - kept because a human
# playing the mission is still the only check on whether the automated route is
# measuring the same thing.
#
# WHAT CHANGED, 2026-09-18. This file used to say a real pickup could not be
# scripted, because boot: leaves a mission at its landing prompt and synthetic
# mouse input does not reach CW4's UI. Both halves were true and the conclusion
# was still wrong: you do not need mouse input, you need the ghost the game
# builds from.
#
#   InputManager.unitToBuild : UnitBuildGhost   the ghost in the player's hand
#     ubg.IsLegal(x, y) -> bool                 will the game accept this cell
#     ubg.SetPosition(x, y, true)               where a click would put it
#     ubg.Build()       -> bool                 calls CreateUnit inside the game
#
# Filling the hand needs no mouse either: every unit has its own left-pane
# handler (LeftPane.BuildUnitTower and forty siblings) and the rift lab has
# GameSpace.commandBaseButtonMgmt.buildCommandBaseButton. Clicking that and
# calling Build() IS the landing click. CW4DevTools build: is those calls.
#
# spawn:/spawnat: remain useless for this, and that is the one part of the old
# text that held up: CreateUnitAtPosition makes units the simulation never
# adopts - no land claimed, no network, at any distance, on clear ground,
# paused, with instant build on.
#
# Separately, and still true: InfoCache.Retrieved is NOT the pickup path. It
# sets the cache's own flag and moves neither mustCollect nor the Collect count
# - see docs/research-findings.md.
#
# This script puts the game in "Home" with the cache registered as an
# Archipelago location and every unit unlocked, then polls until the check
# fires, recording BOTH cache signals so a disagreement is visible.
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
