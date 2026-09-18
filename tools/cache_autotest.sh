#!/usr/bin/env bash
# Collects an info cache FOR REAL, without a human, on every campaign mission
# that has one.
#
# This is the test cache-handtest.sh could not be. The check itself was always
# scriptable - cache:destroy calls DestroyUnit and the location follows - but
# that fakes the consequence. What could not be scripted was the PICKUP: the
# game collecting a cache because your network reached it, which is the thing
# the randomizer's Collect locations actually depend on.
#
# It works now, and four separate wrong theories had to die first. Recorded here
# because each one looks reasonable and each cost a run:
#
#   "the units are ghosts"        No. spawn:/spawnat: place units the SIMULATION
#                                 NEVER ADOPTS - no land claimed, no network, at
#                                 any distance, on clear ground, paused, with
#                                 instant build on. build: drives the game's own
#                                 UnitBuildGhost (SetPosition then Build), which
#                                 is the two calls a mouse click makes, and the
#                                 resulting units are real.
#   "the cache is buried"         No. World.GetCreeper on Home's cache cell
#                                 reads 0 at tick zero.
#   "the sim is running"          It was not. A mission is paused by owner main,
#                                 and ADA's opening messages then re-pause it as
#                                 owner gamemessage. Six minutes of game time
#                                 took two minutes of wall clock only once both
#                                 were cleared REPEATEDLY.
#   "eleven cells is the range"   Horizontally. Tower range is 3D and height
#                                 counts, so nine-cell hops that measure fine on
#                                 a flat map dump are over range wherever the
#                                 ground steps. build:chain hops three.
#
# The creeper is frozen and units indestructible on purpose. Home's cache sits
# in a basin that floods early, and an unfrozen run lost towers faster than the
# poll loop ran - mine went 4, 2, 1, 0. This measures whether a network reaches
# a cache, not whether it survives; survival is a different test.
#
# Usage: tools/cache_autotest.sh [--instant] [storyN ...]   (game must be CLOSED)
#
#   --instant   finish every tower the moment it is placed. OFF by default,
#               because an instantly-finished tower can collect a cache in the
#               one frame before the creeper kills it - which is a pickup no
#               player gets. With it off the lab has to send packets, and a
#               tower that cannot be built in creep simply never finishes.
#               Worth running both ways when a map's verdict is surprising.
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

# WHAT THIS HARNESS CAN AND CANNOT TELL YOU. Read this before believing a PASS.
#
# It freezes the creeper, makes units indestructible and force-places a lab on
# whatever cell the game will accept - which are exactly the constraints a real
# playthrough has to satisfy. So a PASS means "a network that reaches this cache
# collects it". It does NOT mean "a player can reach this cache", and randomizer
# logic must never be derived from it.
#
# The line falls in a specific and measurable place, and the two halves are
# constantly confused:
#
#   BURIED under terrain   The game refuses collection outright, unaided. A Terp
#                          is genuinely required. obj:dump prints buried=True by
#                          comparing the cache's cellHeight against
#                          World.GetTerrain at its own cell.
#   COVERED in creep       A lab and a line of towers cannot take these, because
#                          you cannot hold ground in creep without fighting it
#                          back. obj:dump prints the creeper depth on the cell.
#
# THE CHEATS ARE THE WHOLE DIFFICULTY, which is why two of them are off. An
# earlier version froze the creeper and made units indestructible, and collected
# Tower of Darkness' cache - which sits in creep - with nothing but a rift lab
# and towers. That is not possible in play, and a harness that reports it as a
# pickup is worse than no harness. Instant build stays on: it skips the wait,
# not the reachability.
#
# WHAT THE QUESTION ACTUALLY IS, once the cheats are off: "is this cache
# trivially reachable at tick zero, with nothing but a rift lab and towers?"
#
# The chain lands the lab on the nearest cell the game will ACCEPT one, which is
# a footprint test and not a safety one - build:scan finds legal cells spread
# right across the map, so on a hostile map it will happily land next to the
# cache and be eaten. That is the correct answer rather than a flaw: a player
# lands somewhere safe and fights across, and a cache that needs that is a cache
# whose logic requirements matter. A PASS here means the opposite - no items
# needed, the cache is simply sitting there.
#
# Sequence is the shape to keep in mind: one cache under creep, the other under
# creep AND land, and no lab site near either. Both must fail.
#
# Every campaign mission whose REQUIRED objectives include Collect, from
# apworld/cw4/locations.py REQUIRED_OBJECTIVES. story1 and story6 have no
# Collect requirement and are not here.
DEFAULT_MISSIONS="story2 story3 story4 story5 story7 story9 story10 story11 \
story12 story13 story14 story15 story16 story17 story18 story19"
INSTANT=off
if [ "${1:-}" = "--instant" ]; then INSTANT=on; shift; fi
MISSIONS="${*:-$DEFAULT_MISSIONS}"

SHOTS="$REPO/.aptest/cache-autotest"
rm -rf "$SHOTS"; mkdir -p "$SHOTS"

pass=0; fail=0
# A FAIL gets a screenshot, because the log cannot show what the board looks
# like and every wrong turn in building this was diagnosed from one: a ghost
# still in hand reading as a second rift lab, ADA's dialogs holding the sim at
# Time 0:05 while the harness happily polled for two minutes, and a tower line
# that stopped beside the cache instead of on it. Needs a WINDOWS path - shot:
# with an MSYS one logs a cheerful line and writes nothing.
check() { if [ "$2" = "0" ]; then echo "  PASS  $1"; pass=$((pass+1));
          else echo "  FAIL  $1"; fail=$((fail+1))
               apsend "shot:$(cygpath -w "$SHOTS")\\$m.png" 6
               echo "        board: $SHOTS/$m.png"; fi }
apsend()  { printf '%s\n' "$1" > "$AP_CMD";  sleep "${2:-3}"; }
devsend() { printf '%s\n' "$1" > "$DEV_CMD"; sleep "${2:-3}"; }
collected() { grep -oE "mustCollect=[0-9]+/[0-9]+" "$GAME_LOG" | tail -1; }

# ONE HARNESS AT A TIME, enforced rather than remembered. Two runs share one
# game and one command file, and the result does not look like a crash - it
# looks like wrong data: a run booted story2, another booted story18 over the
# top of it, and the harness cheerfully reported Home's cache as buried at
# (95,120), which is a different map's cache entirely. mkdir is the atomic test
# in POSIX sh; the trap releases it however this exits.
LOCK="$REPO/.aptest/cache-autotest.lock"
mkdir -p "$REPO/.aptest"
if ! mkdir "$LOCK" 2>/dev/null; then
  echo "FATAL: another cache_autotest is running (or died holding $LOCK)" >&2
  exit 1
fi
trap 'rmdir "$LOCK" 2>/dev/null; taskkill //IM CW4.exe //F >/dev/null 2>&1' EXIT

taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2
rm -f "$AP_CMD" "$DEV_CMD"

# ASSERT THE DEPLOYED PLUGIN IS CURRENT. dotnet cannot overwrite the DLL while
# the game holds it, and the copy failure is a build error that scrolls past -
# so a run can silently exercise the previous build and blame the change under
# test. That happened, twice, and cost more than this check ever will.
built="$REPO/src/CW4DevTools/bin/Debug/CW4DevTools.dll"
live="$GAME_DIR/BepInEx/plugins/CW4DevTools/CW4DevTools.dll"
if [ -f "$built" ] && ! cmp -s "$built" "$live"; then
  echo "FATAL: $live is not the plugin you just built - rebuild with the game closed" >&2
  exit 1
fi
cat > "$AP_CFG" <<CFGEOF
[Connection]
Host = localhost
Port = 38999
Slot = CacheAuto
Password =
AutoConnect = false
[Missions]
ShowSpan = false
[Debug]
DebugCommands = true
CFGEOF

( cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 & )
sleep 17

# Set every cheat EXPLICITLY. set: writes the DevTools config, which persists
# across runs, so an unset cheat is whatever the last session happened to leave
# - that is how a previous probe "proved" infinite resources was off while the
# store sat at 100000.
devsend "set:instantbuild=$INSTANT" 2
devsend "set:infiniteresources=off" 2
devsend "set:indestructible=off" 2
devsend "set:freezecreeper=off" 2

# ONE RUN PER CACHE, not per mission. build:chain aims at whichever cache comes
# first out of GameSpace.mustCollect, so on a two-cache map the other one was
# never tested at all - and the two are routinely NOT alike. Sequence is the
# case that forced this: its far-right cache is buried under land with no lab
# site anywhere near it, and its top-left one sits under creep. A single
# per-mission verdict cannot say that, and rules.py already keys its
# requirements to the individual cache.
#
# Only one rift lab lands per mission, so each cache gets a fresh boot.
for m in $MISSIONS; do
  echo "== $m =="
  devsend "boot:$m" 22
  apsend "ada:close" 2
  devsend "sim:pause" 3

  mark
  devsend "obj:dump" 5

  # DID THE BOOT ACTUALLY TAKE? Asserting this is what turns "two harnesses are
  # fighting over one game" from a day of wrong numbers into one loud line. The
  # mission name comes from the same obj:dump the caches do, so the cells below
  # cannot belong to a different map than the one named here.
  on="$(since | grep -oE "DEVOBJ mission=[a-z0-9]+" | tail -1 | cut -d= -f2)"
  if [ "$on" != "$m" ]; then
    echo "  FAIL  $m: booted but the game is on '${on:-nothing}' - aborting the sweep" >&2
    fail=$((fail+1))
    break
  fi

  caches="$(since | grep -oE "DEVOBJ infocache .* creeper [0-9]+ retrieved False" | sed -E 's/.*cell \(([0-9]+),([0-9]+)\).*buried (True|False) creeper ([0-9]+).*/\1 \2 \3 \4/')"
  if [ -z "$caches" ]; then
    echo "  ----  $m has no cache to collect"
    continue
  fi

  n=0
  while read -r cxx cyy bur crp; do
    [ -z "${cxx:-}" ] && continue
    n=$((n+1))
    tag="$m cache $n at ($cxx,$cyy)"

    # Fresh boot per cache: the lab is already down from the previous one.
    if [ "$n" -gt 1 ]; then
      devsend "boot:$m" 22
      apsend "ada:close" 2
      devsend "sim:pause" 3
    fi

    mark
    apsend "counts:dump" 4
    before="$(collected)"

    mark
    devsend "build:chain $cxx $cyy" 8
    chain="$(since | grep -oE "DEVCMD build chain: lab .*" | tail -1)"
    # HOW CLOSE THE CHAIN ACTUALLY GOT. Judging reachability from the cache's own
    # cell is not enough and Far York Farm is the proof: its cache cell reads
    # creeper 0, but the whole map around it is drowned, the tower line stalled
    # 16 cells out, and the harness called that a failure to collect a "clear"
    # cache. The route is part of the question, so measure it.
    reach="$(printf '%s' "$chain" | grep -oE "[0-9]+ from" | grep -oE "[0-9]+")"
    [ -z "$reach" ] && reach=999
    if [ -z "$chain" ]; then
      echo "  $tag: build:chain said nothing - no lab site reachable?"
    else
      echo "  $tag: $chain"
    fi

    apsend "ada:close" 2
    devsend "sim:run 4" 3
    after="$before"
    for _ in 1 2 3 4 5 6; do
      sleep 8
      apsend "ada:close" 2
      devsend "sim:run 4" 2
      apsend "counts:dump" 3
      after="$(collected)"
      [ "$after" != "$before" ] && break
    done

    echo "  $tag: $before -> $after  (buried=$bur, creeper $crp, chain got within $reach)"
    if [ "$bur" = "True" ]; then
      [ "$after" = "$before" ]
      check "$tag: buried under land, correctly NOT collected without a Terp" $?
    elif [ "$crp" -gt 0 ]; then
      [ "$after" = "$before" ]
      check "$tag: sitting in creeper, correctly NOT collected by towers alone" $?
    elif [ "$reach" -gt 11 ]; then
      # Out of tower range means the chain never got there, so the cache was
      # never offered to the network. Not collecting is the only correct
      # outcome, and the interesting part is the number: the route is blocked.
      [ "$after" = "$before" ]
      check "$tag: route blocked, chain stalled $reach cells out, correctly NOT collected" $?
    else
      [ "$after" != "$before" ]
      check "$tag: clear ground and in reach, the network collected it" $?
    fi
  done <<CACHEEOF
$caches
CACHEEOF
done

echo
echo "cache_autotest: $pass passed, $fail failed (instantbuild=$INSTANT)"
[ "$fail" -eq 0 ]
