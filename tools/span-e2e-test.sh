#!/usr/bin/env bash
# A SPAN seed, end to end, with the randomizer installed and a real server.
#
# tools/span-swap-test.sh proved the SWAP is POSSIBLE with the randomizer
# disabled - it drives CW4DevTools directly. This is the other half: does the
# shipped mod actually do it, off a seed the generator produced?
#
# Eight things, in order, because each depends on the last:
#
#   1. GENERATE  a span_missions seed, and confirm its slot data carries the
#                roster - the premise for everything below
#   2. RETARGET  the spiral shows THIS SEED's missions, by name, with the
#                objective icons each one really has
#   3. GATE      a mission in the seed with no unlock is refused; a SPAN map the
#                seed does NOT contain is left alone, because that one is the
#                game's own content rather than part of the randomizer
#   4. LAUNCH    an unlocked SPAN map boots from the spiral
#   5. CHECK     finishing an objective on it sends the right location
#   6. SAVE      its autosave lands under saves/farsite, where SaveArchiver
#                already isolates per slot
#
# SCREENSHOTS ARE TAKEN AND KEPT. Every icon bug found in this area was
# invisible in the log and obvious in a picture: map_objectives was written
# correctly and the icons on screen did not move, twice.
#
# Usage: tools/span-e2e-test.sh       (game must be CLOSED)
set -u

CW4="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"; AP="$REPO/Archipelago"
L="$CW4/BepInEx/LogOutput.log"
CMD="$CW4/BepInEx/cw4ap-commands.txt"
# CW4DevTools' own channel, used for one thing only: forcing an autosave in step
# 6. That plugin is dev-only and not part of a release; if it is not installed,
# step 6 degrades to a SKIP rather than a failure.
DEVCMD="$CW4/BepInEx/cw4dev-commands.txt"
CFG="$CW4/BepInEx/config/com.droha.cw4archipelago.cfg"
STORE="$HOME/Documents/My Games/creeperworld4/archipelago"
SAVES="$HOME/Documents/My Games/creeperworld4/saves"
GENDIR="$REPO/.aptest/span-e2e"
SHOTS="$GENDIR/shots"; LOGDIR="$GENDIR/logs"
# UNITY NEEDS A WINDOWS PATH. ScreenCapture.CaptureScreenshot takes whatever
# string it is handed and fails silently on the MSYS form that `pwd` produces,
# so "shot:/c/Users/..." logs a cheerful SHOT line and writes nothing at all.
SHOTS_WIN="$(cygpath -w "$SHOTS" 2>/dev/null || echo "$SHOTS")"
# PER-PROCESS, not per-directory. The server reads its console from SRV_IN, and
# two overlapping runs sharing one path means the older run's cleanup writes
# "/exit" into the NEWER run's server: it shut down mid-test and every assertion
# after it failed with "cannot reach server", which reads as a product bug.
SRV_IN="$GENDIR/srv-in.$$"; SRV_LOG="$GENDIR/srv.log"
SLOT="SpanTester"
PORT=""; SRV_PID=""; MULTIDATA=""

PASS=0; FAIL=0
verdict() { if [ "$1" = 0 ]; then PASS=$((PASS+1)); echo "  PASS  $2";
            else FAIL=$((FAIL+1)); echo "  FAIL  $2"; fi; }
MARK=0
mark() { MARK=$(wc -l < "$L" 2>/dev/null || echo 0); }
since() { local c; c=$(wc -l < "$L" 2>/dev/null || echo 0); [ "$c" -lt "$MARK" ] && MARK=0;
          tail -n +"$((MARK+1))" "$L" 2>/dev/null; }
send() { printf "%s\n" "$1" > "$CMD"; sleep "${2:-3}"; }
srv() { printf "%s\n" "$1" >> "$SRV_IN"; sleep 3; }
wait_since() { local pat="$1" n="${2:-20}" i; for i in $(seq 1 "$n"); do
                 since | grep -q "$pat" && return 0; sleep 1; done; return 1; }
listening() { netstat -ano | grep "LISTENING" | grep -q ":$1 "; }
pids_on_port() { netstat -ano | grep "LISTENING" | grep ":$1 " | awk '{print $NF}' | sort -u; }
find_free_port() { local p; for p in $(seq "$1" "$2"); do
                     listening "$p" || { echo "$p"; return 0; }; done; return 1; }
save_log() { mkdir -p "$LOGDIR"; [ -f "$L" ] && cp "$L" "$LOGDIR/$1.log"; }
kill_game() { save_log "${1:-phase}"; taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 3; }

# --- restore the player's environment on exit --------------------------------
# The config and the slot cache belong to whoever plays this install next. A
# test config left behind points a real session at a dead localhost port, which
# reads in game as "connecting... timed out" and no items.
CFG_BAK=""; STORE_BAK=""
save_env() {
  [ -f "$CFG" ] && { CFG_BAK="$(mktemp)"; cp "$CFG" "$CFG_BAK"; }
  if [ -d "$STORE/slots" ]; then STORE_BAK="$(mktemp -d)"; cp -r "$STORE/slots" "$STORE_BAK/slots"; fi
}
cleanup() {
  [ -n "$SRV_PID" ] && { srv "/exit" 2>/dev/null; sleep 2; kill "$SRV_PID" 2>/dev/null; }
  if [ -n "$PORT" ]; then
    for pid in $(pids_on_port "$PORT"); do taskkill //PID "$pid" //F >/dev/null 2>&1; done
  fi
  kill_game final
  [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ] && { cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"; }
  rm -rf "$STORE/slots"
  [ -n "$STORE_BAK" ] && [ -d "$STORE_BAK/slots" ] && { cp -r "$STORE_BAK/slots" "$STORE/slots"; rm -rf "$STORE_BAK"; }
  echo "  (config and slot cache restored)"
}
save_env
trap cleanup EXIT

start_server() {
  rm -f "$SRV_IN"; : > "$SRV_IN"
  tail -n +1 -f "$SRV_IN" | ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 \
    python MultiServer.py "$MULTIDATA" --port "$PORT" --disable_save > "$SRV_LOG" 2>&1 ) &
  SRV_PID=$!
  local i; for i in $(seq 1 25); do
    grep -q "Hosting game at" "$SRV_LOG" 2>/dev/null && return 0; sleep 1; done
  return 1
}
write_cfg() {
  mkdir -p "$(dirname "$CFG")"
  printf '[Connection]\nHost = localhost\nPort = %s\nSlot = %s\nPassword =\nAutoConnect = true\n\n[Missions]\nShowSpan = false\n' \
    "$PORT" "$SLOT" > "$CFG"
}
launch() { ( cd "$CW4" && ./CW4.exe > /dev/null 2>&1 & ); sleep 16; MARK=0; }

# A STRAY HARNESS IS THE MOST EXPENSIVE FAILURE HERE, and it does not announce
# itself: another run still writing to the command file clobbers ours, the game
# executes whichever line survived, and our assertions find nothing while the log
# plainly shows the feature working. That reads as a product regression and costs
# an afternoon. TaskStop does NOT reliably kill a bash child, so this is a direct
# test of the actual hazard rather than a hope. See docs/in-game-testing.md,
# "Two ways a run silently corrupts itself".
guard_command_file() {
  local target="$1" sentinel="guard-$$-$RANDOM"
  printf '%s
' "$sentinel" > "$target"
  sleep 4
  if [ "$(cat "$target" 2>/dev/null)" != "$sentinel" ]; then
    echo "FATAL: another harness is writing to $target"
    echo "       it now contains: $(cat "$target" 2>/dev/null)"
    echo "       find the straggler and kill it, then rerun:"
    echo "       Get-CimInstance Win32_Process -Filter \"Name='bash.exe'\" | Select ProcessId,CommandLine"
    exit 1
  fi
  : > "$target"
}


# ------------------------------------------------------------------- 1
echo "step 1/8: generate a SPAN seed from the CURRENT apworld"
rm -rf "$GENDIR"; mkdir -p "$GENDIR/players" "$GENDIR/out" "$SHOTS" "$LOGDIR"
printf 'name: %s\ngame: Creeper World 4\nCreeper World 4:\n  span_missions: 1\n  starter_missions: 4\n' \
  "$SLOT" > "$GENDIR/players/cw4.yaml"
( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python Generate.py \
    --player_files_path "$GENDIR/players" --outputpath "$GENDIR/out" --seed 20260916 \
    > "$GENDIR/generate.log" 2>&1 )
( cd "$GENDIR/out" && unzip -o -q ./*.zip 2>/dev/null )
MULTIDATA="$(ls -t "$GENDIR/out/"*.archipelago 2>/dev/null | head -1)"
[ -n "$MULTIDATA" ]; verdict $? "a span_missions seed generated"
[ -n "$MULTIDATA" ] || { tail -20 "$GENDIR/generate.log"; exit 1; }

# THE PREMISE, asserted rather than assumed. With no roster in slot data the
# plugin retargets nothing and every assertion below would pass or fail for the
# wrong reason. The reader also writes the roster out as "specifier<TAB>title",
# which is what the assertions compare against.
# tr -d '\r', because python's print() on Windows writes CRLF. Without it every
# title read back out of this file carries a trailing carriage return, and every
# grep for one fails against a log that plainly contains it - which read as "all
# 20 missions missing" from a map that had all 20 of them.
( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python "$REPO/tools/span-roster.py" \
    "$MULTIDATA" ) 2>&1 | tr -d '\r' > "$GENDIR/roster.txt"
grep -q "knucracker\|demobonus" "$GENDIR/roster.txt"; verdict $? "the roster drew at least one SPAN map"
echo "        roster:"; sed 's/^/          /' "$GENDIR/roster.txt"

SPAN_SPEC="$(grep -E 'knucracker|demobonus' "$GENDIR/roster.txt" | head -1 | cut -f1)"
SPAN_TITLE="$(grep -E 'knucracker|demobonus' "$GENDIR/roster.txt" | head -1 | cut -f2)"
echo "        testing with $SPAN_SPEC ($SPAN_TITLE)"

# ------------------------------------------------------------------- 2
echo "step 2/8: the spiral shows this seed's roster"
kill_game pre
rm -f "$CMD"; rm -rf "$STORE/slots"
guard_command_file "$CMD"
PORT="$(find_free_port 38501 38550)" || { echo "ABORT: no free port"; exit 1; }
start_server; verdict $? "server up on $PORT"
write_cfg
launch
# Positive control FIRST: every assertion below reads the log, so if the game
# never came up they would all pass by finding nothing.
wait_since "ModCore initialized" 30; verdict $? "the mod loaded (control)"
wait_since "AP CONNECTED" 40; verdict $? "connected to the seed"

mark; send "story:open" 8
send "shot:$SHOTS_WIN\1-spiral.png" 6
# The retarget logs how many planets it moved. Zero is only a legitimate answer
# for an all-campaign roster, which step 1 already ruled out.
wait_since "AP: retargeted" 20; verdict $? "the spiral was retargeted"
since | grep -E "AP: retargeted" | sed 's/^/        /'

mark; send "glyphs:dump" 6
since | grep -E "DEBUG GLYPH" > "$GENDIR/glyphs.txt" 2>/dev/null
MISSING=""
# cut, not `while IFS=$(printf '\t') read`: command substitution strips the tab
# it was asked to produce, so IFS ends up empty and the split silently does not
# happen. That read as "all 20 missions missing" on a map that had all 20.
cut -f2 "$GENDIR/roster.txt" | while read -r title; do
  [ -n "$title" ] || continue
  grep -qF "$title" "$GENDIR/glyphs.txt" || echo "$title"
done > "$GENDIR/missing.txt"
MISSING="$(tr '\n' ' ' < "$GENDIR/missing.txt")"
[ -z "${MISSING// /}" ]; verdict $? "every roster mission is on the map${MISSING:+ (missing: $MISSING)}"

# ------------------------------------------------------------------- 3
echo "step 3/8: locked means locked, out-of-seed means hands off"
mark; send "gatecheck:$SPAN_SPEC" 3
since | grep -q "GATECHECK: '$SPAN_SPEC' allowed=False"
verdict $? "a SPAN map in the seed with no unlock is refused"

# A SPAN map the seed does NOT contain must stay launchable. MissionGate used to
# fail open for every non-storyN specifier, which made the opposite mistake, and
# a naive fix would make this one.
OUTSIDE=""
for guid in knucracker1 knucracker2 knucracker3 demobonus demobonus2 demobonus3; do
  cut -f1 "$GENDIR/roster.txt" | grep -qx "$guid" || { OUTSIDE="$guid"; break; }
done
if [ -n "$OUTSIDE" ]; then
  mark; send "gatecheck:$OUTSIDE" 3
  since | grep -q "GATECHECK: '$OUTSIDE' allowed=True"
  verdict $? "a SPAN map outside the seed is left alone ($OUTSIDE)"
else
  echo "  SKIP  every candidate SPAN map is in this roster"
fi

# ------------------------------------------------------------------- 4
echo "step 4/8: an unlocked SPAN map launches from the spiral"
mark; srv "/send $SLOT Mission Unlock: $SPAN_TITLE"
wait_since "Mission Unlock: $SPAN_TITLE" 25; verdict $? "the unlock arrived"
mark; send "gatecheck:$SPAN_SPEC" 3
since | grep -q "GATECHECK: '$SPAN_SPEC' allowed=True"
verdict $? "the gate opens once the unlock is held"

# TWO STEPS, because a click is not a launch. Clicking a planet opens its
# mission panel; the panel's own button is what calls OnLaunch. The click is
# still worth asserting on its own - PlanetClickPatch SWALLOWS the click on a
# locked planet, so "the popup opened" is the observable difference between
# locked and unlocked.
mark; send "clickplanet:$SPAN_SPEC" 6
since | grep -q "CLICKPLANET $SPAN_SPEC .* popupAfter=True"
verdict $? "clicking the planet opens its panel"
since | grep -E "CLICKPLANET" | sed 's/^/        /'

# boot: goes through MissionGate and then LoadGame under CATEGORY.FARSITE - the
# same call the panel's launch button ends up making, and the one the swap test
# proved loads a SPAN map from a Farsite planet.
mark; send "boot:$SPAN_SPEC" 16
wait_since "LocationWatcher: mission" 30; verdict $? "a mission loaded"
since | grep -E "DEBUG boot|LocationWatcher: mission" | sed 's/^/        /'
# Mission 0 is "I could not work out what this map is", which makes Tick return
# immediately - the map plays perfectly and sends nothing.
if since | grep -q "LocationWatcher: mission 0 "; then verdict 1 "the map resolved to a real mission"
else verdict 0 "the map resolved to a real mission"; fi
send "shot:$SHOTS_WIN\2-inmission.png" 5

# ------------------------------------------------------------------- 5
echo "step 5/8: an objective on it sends a check"
mark; send "objective:0" 10
wait_since "LOCATION CHECK" 30; verdict $? "a check was sent from a SPAN map"
since | grep -E "LOCATION CHECK" | head -5 | sed 's/^/        /'
# The name has to be one the SERVER knows, or it is silently dropped there.
grep -q "$SPAN_TITLE" "$SRV_LOG" 2>/dev/null
verdict $? "the server recognised a location from that map"

# ------------------------------------------------------------------- 6
echo "step 6/8: the autosave lands where SaveArchiver isolates it"
# FORCE THE SAVE, do not wait for one. Waiting measured the harness's patience
# rather than the save path: a 25-second wait at 4x reported a missing autosave
# on a run where everything else worked, and the screenshot showed why - the
# mission was still sitting on its landing prompt, so no time had passed in it
# at all. CW4DevTools' save:auto calls GameSpace.AutoSave() directly, which is
# what the swap test uses for exactly this reason.
send "sim:run 4" 4
if [ -f "$DEVCMD" ] || [ -d "$CW4/BepInEx/plugins/CW4DevTools" ]; then
  printf '%s
' "save:auto" > "$DEVCMD"; sleep 8
  SAVED=1
  ls -d "$SAVES/farsite/$SPAN_SPEC" >/dev/null 2>&1 && SAVED=0
  verdict "$SAVED" "autosave is under saves/farsite/$SPAN_SPEC"
  if [ "$SAVED" != 0 ]; then
    echo "        saves/farsite holds:"; ls "$SAVES/farsite" 2>/dev/null | sed 's/^/          /'
    grep -E "DEVSAVE" "$L" 2>/dev/null | tail -3 | sed 's/^/        /'
  fi
else
  echo "  SKIP  autosave: CW4DevTools is not installed, so the save cannot be"
  echo "        forced and waiting for one on a mission at its landing prompt"
  echo "        would never succeed. tools/span-swap-test.sh covers this path."
fi
if ls -d "$SAVES/span/$SPAN_SPEC" >/dev/null 2>&1; then
  echo "        NOTE: a save also appeared under saves/span, which SaveArchiver does not isolate"
fi

# ------------------------------------------------------------------- 7
echo "step 7/8: every OTHER SPAN map in this roster, one boot each"
# ONE MAP IS NOT THE ROSTER. Steps 4 to 6 go deep on a single map; this goes
# wide. The 26 differ in shape - four have a Hold objective the model has no
# location for, one has the roster's only cache, one has a Custom objective, and
# three are supplied by Pods so their totems want no Factory - and a map whose
# shape breaks the watcher would be invisible to a test that only ever launches
# the first one drawn.
#
# What it asserts per map is deliberately narrow, because the deep pass already
# covers the rest: the gate opens, the running map RESOLVES (mission 0 is the
# silent failure that sends nothing), and a check goes out carrying that map's
# own title.
SWEPT=0
for row in $(cut -f1 "$GENDIR/roster.txt" | grep -E 'knucracker|demobonus'); do
  [ "$row" = "$SPAN_SPEC" ] && continue          # already covered in depth
  [ "$SWEPT" -ge 6 ] && break                    # six is enough to cover the shapes
  SWEPT=$((SWEPT + 1))
  title="$(grep -P "^$row	" "$GENDIR/roster.txt" 2>/dev/null | cut -f2)"
  [ -n "$title" ] || title="$(awk -v k="$row" -F'	' '$1==k{print $2}' "$GENDIR/roster.txt")"

  mark; srv "/send $SLOT Mission Unlock: $title"
  wait_since "Mission Unlock: $title" 25 || true
  mark; send "gatecheck:$row" 3
  since | grep -q "GATECHECK: '$row' allowed=True"
  verdict $? "$title: the gate opens with its unlock"

  mark; send "boot:$row" 16
  wait_since "LocationWatcher: mission" 25; LOADED=$?
  if since | grep -q "LocationWatcher: mission 0 "; then LOADED=1; fi
  verdict "$LOADED" "$title: loaded and resolved to a real mission"

  mark; send "objective:0" 8
  since | grep -q "LOCATION CHECK: $title - "
  verdict $? "$title: a check went out under its own name"
  since | grep -E "LOCATION CHECK: $title" | head -2 | sed 's/^/        /'
done
[ "$SWEPT" -gt 0 ] || echo "  SKIP  this roster drew only one SPAN map"

# ------------------------------------------------------------------- 8
echo "step 8/8: the SPAN save loads back, through the gate"
# THE RESUMED CASE IS ITS OWN PATH and has hidden two bugs before (see
# docs/in-game-testing.md): boot: always starts a mission FRESH, so nothing that
# only ever launches can reach it. A resumed SPAN map has to be recognised by the
# same guid, which means MissionGate's save-load prefix and the watcher both have
# to resolve it - and the watcher must NOT re-send the checks it already sent,
# because the structures it reads are already marked done.
if [ "$SAVED" = 0 ] 2>/dev/null; then
  # RELAUNCH FIRST. The mission panel only exists on the Galaxy, and by this
  # point the sweep has left the game inside a mission - clickplanet finds no
  # planets and the whole step fails for a reason that has nothing to do with
  # saves. There is no return-to-menu command, and relaunching is the honest
  # shape anyway: quitting and coming back is exactly how a player reaches a
  # save. It also exercises the cold start and the reconnect on the way.
  kill_game presave
  launch
  wait_since "ModCore initialized" 30; verdict $? "the game came back up (control)"
  wait_since "AP CONNECTED" 40; verdict $? "it reconnected to the same seed"
  send "story:open" 8

  mark; send "clickplanet:$SPAN_SPEC" 6
  send "loadsave:$SPAN_SPEC" 16
  since | grep -q "loadsave: invoking OnLoad for '$SPAN_SPEC'"
  verdict $? "the save-load gate let the SPAN save through"
  wait_since "LocationWatcher: mission" 25; RESUMED=$?
  verdict "$RESUMED" "the resumed map loaded"
  since | grep -E "DEBUG loadsave|LocationWatcher: mission" | head -3 | sed 's/^/        /'
  # CONDITIONAL ON THE LOAD, because "no mission 0 line" is also true when
  # nothing loaded at all. This passed vacuously on a run where the resume had
  # plainly failed - the exact shape of loose pass that makes a log-reading
  # assertion worthless.
  if [ "$RESUMED" != 0 ]; then
    verdict 1 "the resumed map resolved to a real mission (nothing loaded)"
  elif since | grep -q "LocationWatcher: mission 0 "; then
    verdict 1 "the resumed map resolved to a real mission"
  else
    verdict 0 "the resumed map resolved to a real mission"
  fi
  # Re-sending is not merely noise: MarkChecked dedupes locally, so a resend that
  # DID happen would show here and nowhere else.
  mark; sleep 6
  if since | grep -q "LOCATION CHECK: $SPAN_TITLE"; then
    verdict 1 "no already-sent check is re-sent on resume"
    since | grep "LOCATION CHECK" | head -3 | sed 's/^/        /'
  else
    verdict 0 "no already-sent check is re-sent on resume"
  fi
else
  echo "  SKIP  no autosave was produced, so there is nothing to load back"
fi

echo
echo "span-e2e: $PASS passed, $FAIL failed. Screenshots in $SHOTS_WIN"
[ "$FAIL" = 0 ]
