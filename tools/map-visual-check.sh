#!/usr/bin/env bash
# Puts the mission map into a KNOWN state and screenshots it, so the map can be
# checked with an eye rather than only from log lines.
#
# Why this exists: every automated check here reads the log, and the log was
# happy while the map was visibly wrong twice - a planet flashing between its
# sphere and its locked "?", and a planet showing a confident green icon for an
# objective that is not a check in this slot. Neither is visible in a log line.
#
# EXPECTED RESULT (assert this against the screenshot):
#   Farsite       sphere, 2 icons, both GREEN: collect then custom (a skull)
#                 (vanilla draws ONE icon here and it is a TOTEMS icon, on a
#                  mission with no totems at all - it has 2 caches and a custom
#                  objective, measured live. The mod reconciles the icon set to
#                  the objectives that actually have checks: the totems icon is
#                  retextured to collect, and the custom icon is added. No stray
#                  quad may sit at the container origin, and the two must be
#                  spaced like every other planet's.)
#   Home          sphere, 3 icons: nullify GREEN, totems GREEN, collect GREY
#                 (its Cache 1 is checked, the rest are open)
#   Not My Mars   sphere, 3 icons, ALL GREY (everything checked)
#   every other planet   the locked "?" and no icons
#
# Yellow (reachable, out of logic) cannot be produced here: it needs logic hints
# from slot data, which only a real generated seed provides.
#
# Usage: tools/map-visual-check.sh      (game must be CLOSED)
#        The screenshot path is printed at the end. Delete it after reading.
set -u
CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
SHOT="$CW4/ap_map.png"
send() { printf "%s\n" "$1" > "$CMD"; sleep 2; }
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$CMD" "$SHOT"
cd "$CW4" && ./CW4.exe > /dev/null 2>&1 &
sleep 15

# A KNOWN state, so the screenshot has a right answer.
# Unlocked: Farsite, Home, Not My Mars. Everything else locked.
send "item:Mission Unlock: Farsite"
send "item:Mission Unlock: Home"
send "item:Mission Unlock: Not My Mars"
# Farsite: two caches and a custom objective, untouched -> both GREEN.
# This is the mission whose authored icon set is WRONG (the map draws a totems
# icon and the mission has no totems), so registering both kinds is what
# exercises the icon reconcile: one marker retextured, one added.
send "loc:add Farsite - Cache 1"
send "loc:add Farsite - Cache 2"
send "loc:add Farsite - Custom"
# Home: cache done, totems and nullify open     -> Cache GREY, others GREEN
for l in "Home - Cache 1" "Home - Totem 1" "Home - Totem 2" "Home - Nullify 1"; do send "loc:add $l"; done
send "check:Home - Cache 1"
# Not My Mars: everything done                  -> all GREY
for l in "Not My Mars - Cache 1" "Not My Mars - Totem 1" "Not My Mars - Totem 2" \
         "Not My Mars - Totem 3" "Not My Mars - Totem 4" "Not My Mars - Nullify 1" \
         "Not My Mars - Nullify 2"; do send "loc:add $l"; send "check:$l"; done

send "story:open"
sleep 6
send "diag:watch 15"
sleep 4
send "glyphs:dump"
sleep 3
send "shot:$SHOT"
sleep 5
echo "--- glyph colours"
grep "DEBUG GLYPHS:" "$L" | tail -25
echo "--- flashes during the still frame"
grep -c "DIAG FLASH" "$L"
echo "--- shot"
ls -l "$SHOT" 2>/dev/null || echo "NO SHOT"
taskkill //IM CW4.exe //F >/dev/null 2>&1
