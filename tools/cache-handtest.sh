#!/usr/bin/env bash
# Sets up the ONE check no script can send, then watches for it.
#
# Collecting an info cache is unscriptable here: synthetic mouse input does not
# reach CW4's UI, and InfoCache.Retrieved (the only hook) sets the cache's own
# flag without moving GameSpace.mustCollect or the Collect objective's count -
# measured, see docs/research-findings.md. So the cache branch of LocationWatcher
# needs a human to pick one up.
#
# This script does everything either side of that: it puts the game in "Home"
# with the cache registered as an Archipelago location and every unit unlocked,
# then polls until the check fires, recording BOTH cache signals so a
# disagreement between them is visible rather than inferred.
#
# Usage: tools/cache-handtest.sh          (game must be CLOSED)
#        Then play, connect your network to the info cache, and read the result.
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
DEADLINE=1800    # 30 minutes is far longer than the mission needs

send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }

echo "[setup 1/5] game closed, randomizer enabled, debug channel on"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD"
# The build target deploys here; if the mod was parked, bring it back.
if [ -d "$CW4/BepInEx/plugins-disabled/CW4Archipelago" ] \
   && [ ! -d "$CW4/BepInEx/plugins/CW4Archipelago" ]; then
  mv "$CW4/BepInEx/plugins-disabled/CW4Archipelago" "$CW4/BepInEx/plugins/CW4Archipelago"
fi
mkdir -p "$CW4/BepInEx/config"
cat > "$CW4/BepInEx/config/com.droha.cw4archipelago.cfg" <<CFGEOF
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
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
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
for i in $(seq 1 30); do grep -q "LocationWatcher: mission 2" "$L" && break; sleep 2; done
send "ada:close"

echo "----------------------------------------------------------------"
echo "READY. Press play and collect the info cache in Home."
echo "Nothing else is needed - leave this running and it will report."
echo "----------------------------------------------------------------"

start=$SECONDS
last_counts=""
while :; do
  el=$((SECONDS - start))
  if grep -q "LOCATION CHECK: Home - Cache 1" "$L"; then
    send "counts:dump"; sleep 2
    last_counts=$(grep "COUNTS:" "$L" | tail -1)
    echo "[watch ${el}s] PASS: 'Home - Cache 1' was sent"
    echo "  $last_counts"
    n=$(grep -c "LOCATION CHECK: Home - Cache 1" "$L")
    if [ "$n" = 1 ]; then echo "  PASS  sent exactly once"
    else echo "  FAIL  sent $n times - the patch and the safety poll are double-sending"; fi
    break
  fi
  if [ "$el" -ge "$DEADLINE" ]; then
    send "counts:dump"; sleep 2
    echo "[watch ${el}s] TIMEOUT: no cache check in $((DEADLINE / 60)) minutes"
    echo "  $(grep "COUNTS:" "$L" | tail -1)"
    break
  fi
  # Both cache signals, every 30s: mustCollect/max is what LocationWatcher
  # counts, and objective 4's count is the game's own tally. They should agree,
  # and a disagreement is the interesting failure.
  send "counts:dump"; sleep 2
  c=$(grep "COUNTS:" "$L" | tail -1 | grep -o "mustCollect=[0-9]*/[0-9]*")
  o=$(grep "COUNTS:" "$L" | tail -1 | grep -o "4:[a-z]*/count=[0-9]*/[A-Za-z]*")
  echo "[watch ${el}s/${DEADLINE}s] waiting for a cache pickup in Home: $c objective$o"
  sleep 26
done

echo "Done. Log: $L"
