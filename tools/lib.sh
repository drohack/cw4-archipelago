# shellcheck shell=bash
#
# Shared setup for the in-game harnesses. SOURCED, never executed - no shebang,
# no execute bit.
#
# WHY THIS EXISTS. The game's install path was written out in 22 harnesses, and
# the repo root was re-derived in 16 of them with a hard-coded `..` depth. Both
# are now in one place. The second is the more dangerous of the two: a harness
# that computes the wrong REPO does not fail, it writes its output somewhere
# else, so the assertion below turns a silent wrong answer into a loud one.
#
# THE NAMES. The game directory used to be `G` in five harnesses and `CW4` in
# seventeen, and the log `LOG` in five and `L` in seventeen - one concept, two
# names each, explained nowhere. `G` was the worst of them, because the default
# path happens to live on the G: drive and the variable reads as if it were
# named after a drive letter that is specific to one machine. It never was.
# GAME_DIR and GAME_LOG say what they hold.
#
# CW4_DIR is deliberately NOT renamed. It is the documented user-facing override
# (see docs/developing.md), so it stays the input and GAME_DIR is the resolved
# value.
#
# WHAT IS NOT HERE, and why:
#
#   CMD   Seventeen harnesses mean cw4ap-commands.txt by it and seven mean
#         cw4dev-commands.txt, and three use BOTH channels in one run. A shared
#         CMD would silently post commands to the file the wrong plugin reads,
#         and the harness would sit waiting for a reply that never comes. Each
#         harness says which it means: CMD="$AP_CMD" or CMD="$DEV_CMD". The same
#         goes for CFG, which names the randomizer's config in eight harnesses
#         and the dev tools' in one.
#
#   send  Twenty-one definitions with at least six different behaviours - the
#         sleep is 2, 3, or an argument, and cmod-traptest.sh waits for a log
#         acknowledgement instead of sleeping at all, because (its own comment
#         records) an earlier version slept 2s per command and lost five of six:
#         the debug channel polls every 30 FRAMES. A unified send() would
#         silently retime every harness, and the failure mode is a harness that
#         passes because it never sent anything.
#
#   verdict  Eleven definitions with five different label prefixes. Unifying
#         changes the output a human reads to judge a game run, for no gain.
#
# This file must stay safe to source under `set -u` (every harness sets it, none
# sets -e), must not cd, and must print nothing when it works.

CW4AP_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# The one copy of the game path. Override with CW4_DIR.
GAME_DIR="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"

REPO="$(cd "$CW4AP_LIB_DIR/.." && pwd)"
# The assertion that makes a future move survivable. Nothing else in this repo
# checks that REPO is the repo root, and a wrong one is invisible: the harness
# runs, writes to a path that does not exist, and reports whatever it found.
if [ ! -f "$REPO/apworld/cw4/__init__.py" ]; then
  echo "FATAL: tools/lib.sh computed REPO=$REPO, which is not the repo root." >&2
  echo "       If tools/ moved, this is the line to fix." >&2
  exit 1
fi
AP="$REPO/Archipelago"

GAME_LOG="$GAME_DIR/BepInEx/LogOutput.log"
AP_CMD="$GAME_DIR/BepInEx/cw4ap-commands.txt"
DEV_CMD="$GAME_DIR/BepInEx/cw4dev-commands.txt"
AP_CFG="$GAME_DIR/BepInEx/config/com.droha.cw4archipelago.cfg"
DEV_CFG="$GAME_DIR/BepInEx/config/com.droha.cw4devtools.cfg"
PLUGINS="$GAME_DIR/BepInEx/plugins"
PARKED="$GAME_DIR/BepInEx/plugins-disabled"

# Log tailing. mark() remembers where the log is now; since() prints what has
# been appended since.
#
# THE REWIND GUARD IS LOAD-BEARING, and four harnesses did not have it. map-dump,
# objflag-control, span-survey and span-swap-test used a bare
# `tail -n +"$((MARK+1))"`, and all four taskkill and relaunch CW4 two or three
# times per run - which rotates LogOutput.log. After a relaunch the new log is
# shorter than MARK, so tail printed NOTHING and every assertion after the first
# relaunch was reading an empty string. Ten other harnesses already had this
# guard; those four now do too.
MARK=0
mark() { MARK=$(wc -l < "$GAME_LOG" 2>/dev/null || echo 0); }
since() {
  local cur
  cur=$(wc -l < "$GAME_LOG" 2>/dev/null || echo 0)
  [ "$cur" -lt "$MARK" ] && MARK=0
  tail -n +"$((MARK+1))" "$GAME_LOG" 2>/dev/null
}

# Picking a server port, and NOT killing somebody else's server.
#
# THIS HAS GONE WRONG TWICE. A harness that hard-codes Archipelago's default
# port 38281 and cleans up by killing "whatever is listening on 38281" takes out
# an unrelated project's server - the maintainer's own, on the same machine -
# and then races it for the bind. apbattery.sh and apbattery2.sh were fixed for
# this and carry the comment; msgfilter.sh was not, and killed a live server
# again on 2026-09-17 during a full sweep.
#
# So the helpers live here rather than in two harnesses out of three. Pick a
# free port from your own range, remember the PID you started, and kill only
# that. Ranges in use: apbattery 38301-38380, offline-test 38401-38500,
# msgfilter 38551-38600.
listening() { netstat -ano 2>/dev/null | grep "LISTENING" | grep -q ":$1 "; }
find_free_port() {
  local p
  for p in $(seq "$1" "$2"); do
    listening "$p" || { echo "$p"; return 0; }
  done
  return 1
}

# Called by the harnesses that drive the game. NOT called on sourcing, because
# span-off-parity.sh and refactor-parity.sh need the repo and the clone and no
# game at all.
require_game() {
  [ -d "$GAME_DIR" ] && return 0
  echo "FATAL: no Creeper World 4 install at $GAME_DIR" >&2
  echo "       Set CW4_DIR to your install, e.g." >&2
  echo "       CW4_DIR='D:/Steam/steamapps/common/Creeper World 4' $0" >&2
  exit 1
}
