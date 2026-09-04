#!/usr/bin/env bash
# Verifies the mods react to EVENTS rather than polling, and that nothing the
# polling used to cover has been lost.
#
# Exists because the polling-to-events refactor broke the map's colouring in a
# way no test could see: the tracker repainted on ApClient.StateChanged, and
# StateChanged was not raised when a location was CHECKED. The old per-frame
# poll had hidden that gap for as long as it existed. Every assertion below is
# read out of the log, so the next such gap fails a test instead of needing an
# eye on a screenshot.
#
# Usage: tools/eventdriven-test.sh      (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0;
          tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
# Pull one "key=number" field out of the newest perf line. grep -o rather than a
# sed backreference: the perf line has grown a field twice already, and a
# "match to the last =" sed silently returned the wrong number both times.
perf_field() { since | grep "DEBUG PERF:" | tail -1 | grep -o "$1=[0-9]*" | cut -d= -f2; }

echo "step 0/8: clean slate + known config"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD"
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

echo "step 1/8: launch"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 14
MARK=0   # BepInEx truncates the log on launch

# 1. Every new patch must actually be applied. A silently unapplied patch is
#    the whole risk of this refactor: the map would simply never colour.
echo "step 2/8: patches applied"
for pat in "map opened" "planet refresh" "totem complete" "cache destroyed" \
           "nullifier targets" "objective row"; do
  if grep -q "Harmony patch '$pat' failed" "$L"; then r=1; else r=0; fi
  verdict $r "patch applied: $pat"
done

# 2. On the MENU the tracker must scan ZERO times. This was 2256 scans in
#    twenty seconds before the refactor - a whole-scene FindObjectsOfType per
#    frame, finding nothing, because the menu and the map share a scene.
echo "step 3/8: no polling on the menu (20s)"
mark; sleep 20
n=$(since | grep -c "TRACKER: scanned the map")
verdict $([ "$n" = 0 ] && echo 0 || echo 1) "menu scans = 0 (got $n)"

# 3. Opening the map is an EVENT (Span.Start), so it should produce exactly the
#    scan the menu did not.
echo "step 4/8: opening the map scans once"
mark
send "story:open"
sleep 4
n=$(since | grep -c "TRACKER: scanned the map")
verdict $([ "$n" -ge 1 ] && echo 0 || echo 1) "map open scans >= 1 (got $n)"

# 4. THE REGRESSION. The finale's custom glyph must be RED while the
#    mission-count gate is shut and GREEN once it opens - driven by a location
#    check, which is exactly the change that raised no event.
echo "step 5/8: finale gate flips the glyph colour"
send "item:Mission Unlock: Founders"
send "loc:add Founders - Custom"
send "finale:need 12"
mark
send "finale:beat 0"
sleep 2
send "glyphs:dump Founders"
sleep 2
since | grep -q "DEBUG GLYPHS: story19 .* obj=5 color=RED"; r=$?
verdict $r "glyph RED while gated (need 12, beat 0)"
# Ask for the baseline rather than scraping one out of the log - nothing has
# printed a perf line yet at this point, and an empty baseline made this
# assertion pass or fail on whether the shell liked comparing "".
mark
send "perf"
sleep 2
before=$(perf_field recolours)
mark
send "finale:beat 12"
sleep 2
send "glyphs:dump Founders"
sleep 2
since | grep -q "DEBUG GLYPHS: story19 .* obj=5 color=GREEN"; r=$?
verdict $r "glyph GREEN once the gate opens (beat 12)"
since | grep -q "DEBUG FINALE: need=12 beaten=12 counts=True"; r=$?
verdict $r "gate reports open at beat 12"

# 5. A repaint must have HAPPENED, not merely produced the right colour by
#    having never been wrong. Without this the test above would pass on a
#    tracker that painted once and froze.
mark
send "perf"
sleep 2
after=$(perf_field recolours)
if [ -n "${after:-}" ] && [ -n "${before:-}" ] && [ "$after" -gt "$before" ]; then r=0; else r=1; fi
verdict $r "recolours advanced on the state change ($before -> ${after:-none})"

# 6. In a real mission: each instance must send exactly ONE check, and the
#    event patch and the once-a-second safety poll must not both send it.
#    "Home" has one cache, two totems and one nullifiable target, so it
#    exercises all three counted paths in the smallest mission that has them.
# The icon SET, not just its colour. The map draws one icon per objective in the
# map file's authored list, and on Farsite that list is wrong: a totems icon on a
# mission with no totems, and nothing for its two caches or its custom objective.
# The reconcile makes the set follow the locations, so this planet must end up
# with exactly Collect and Custom and no active totems icon.
echo "step 6/8: the icon set follows the locations"
send "item:Mission Unlock: Farsite"
for loc in "Farsite - Cache 1" "Farsite - Cache 2" "Farsite - Custom"; do
  send "loc:add $loc"
done
mark
send "glyphs:dump Farsite"
sleep 3
# Farsite draws its Custom check as the TOTEMS icon, so the marker's objective
# field reads 1 where the location is still "Farsite - Custom" - see
# MissionRules.IconAlias. Both assertions below asserted the pre-alias drawing
# and had to move with it: the alias is the feature, not a regression. Sorted,
# because the order is whatever order the glyph children come back in.
active=$(since | grep "DEBUG GLYPHS: story1 " | grep "active=ON" | grep -o "obj=[0-9]*" | cut -d= -f2 | sort -n | tr '\n' ',')
verdict $([ "$active" = "1,4," ] && echo 0 || echo 1) "Farsite icons are Collect and Custom-drawn-as-Totems (got ${active:-none})"
n=$(since | grep "DEBUG GLYPHS: story1 " | grep -c "active=ON.*tex='ObjTotem'")
verdict $([ "$n" = 1 ] && echo 0 || echo 1) "the aliased Custom icon uses the totem texture (got $n)"
n=$(since | grep -c "DEBUG GLYPHS: story1 .*active=ON.*pos=(0,0,0)")
verdict $([ "$n" = 1 ] && echo 0 || echo 1) "exactly one icon at the row origin (got $n)"

echo "step 7/8: per-instance checks, no double sends"
send "item:Mission Unlock: Home"
for loc in "Home - Totem 1" "Home - Totem 2" "Home - Cache 1" "Home - Nullify 1"; do
  send "loc:add $loc"
done
mark
send "boot:story2"
for i in $(seq 1 30); do since | grep -q "LocationWatcher: mission 2" && break; sleep 2; done
since | grep -q "LocationWatcher: mission 2"; r=$?
verdict $r "mission 2 loaded"
send "ada:close"

mark
send "totem:complete"
sleep 5           # past the once-a-second safety poll, so a double send shows up
n=$(since | grep -c "LOCATION CHECK: Home - Totem 1")
verdict $([ "$n" = 1 ] && echo 0 || echo 1) "totem 1 sent exactly once (got $n)"

mark
send "totem:complete"
sleep 5
n=$(since | grep -c "LOCATION CHECK: Home - Totem 2")
verdict $([ "$n" = 1 ] && echo 0 || echo 1) "totem 2 sent exactly once (got $n)"
n=$(since | grep -c "LOCATION CHECK: Home - Totem 1")
verdict $([ "$n" = 0 ] && echo 0 || echo 1) "totem 1 not re-sent (got $n)"

# The totem patch must have FIRED, not merely been applied - otherwise the
# safety poll is silently doing all the work and the event path is dead code.
mark
send "perf"
sleep 2
tp=$(perf_field totemPokes)
verdict $([ "${tp:-0}" -ge 2 ] && echo 0 || echo 1) "totem patch fired (totemPokes=${tp:-none})"

# The cache hook, which a real pickup proved was originally attached to the wrong
# method: InfoCache.Retrieved is never called when a cache is collected, so
# cachePokes stayed 0 through an actual pickup while the safety poll quietly did
# all the work. It is now on DestroyUnit, which IS what collecting does - so
# destroying a cache must both fire the patch and produce the check.
mark
send "cache:destroy"
sleep 5
send "perf"
sleep 2
cp=$(perf_field cachePokes)
verdict $([ "${cp:-0}" -ge 1 ] && echo 0 || echo 1) "cache patch fired (cachePokes=${cp:-none})"
n=$(since | grep -c "LOCATION CHECK: Home - Cache 1")
verdict $([ "$n" = 1 ] && echo 0 || echo 1) "cache 1 sent exactly once (got $n)"
echo "  NOTE  a real PICKUP is unscriptable (mouse input never reaches CW4's UI)."
echo "        Confirmed by hand on 2026-08-31: mustCollect 1->0, objective 4 DONE,"
echo "        'Home - Cache 1' sent exactly once. See tools/cache-handtest.sh."

echo "step 8/8: shut down"
taskkill //IM CW4.exe //F >/dev/null 2>&1
echo "---"
echo "eventdriven: $PASS passed, $FAIL failed"
[ "$FAIL" = 0 ]
