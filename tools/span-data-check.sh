#!/usr/bin/env bash
# Re-derive span_data.py from the live game, for all 26 SPAN maps.
#
# WHY THIS EXISTS. Every SPAN requirement in the randomizer is computed from
# three numbers per map - how many totems, how many nullify targets, how many
# caches - plus which objective slots the map enables. Those numbers were
# measured once, during the survey, and then FROZEN into apworld/cw4/span_data.py
# with mission numbers 21..46 that can never be reordered because location ids
# are positional.
#
# Nothing has checked them since. A game patch that adds an emitter to one map,
# or a transcription slip in the generator, would change what a seed contains and
# show up as a player unable to finish a mission - with no test failing anywhere.
# This boots every map and asks the game again.
#
# It needs no Archipelago server and makes no checks: it drives CW4DevTools
# directly and compares the log against the table. The randomizer stays installed
# but is configured not to connect, so it sits idle rather than being uninstalled
# and put back.
#
# Usage: tools/span-data-check.sh      (game must be CLOSED)
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

DEVCMD="$DEV_CMD"
CFG="$AP_CFG"
OUT="$REPO/.aptest/span-data-check"

GUIDS="knucracker1 knucracker2 knucracker3 knucracker4 knucracker5 knucracker6
knucracker7 knucracker8 knucracker9 knucracker10 knucracker11 knucracker12
knucracker13 knucracker14 knucracker15 knucracker16 knucracker17 knucracker18
knucracker19 knucracker20 knucrackerbonus0 knucrackerbonus1 demobonus demobonus2
demobonus3 demobonus4"

send() { printf '%s\n' "$1" > "$DEVCMD"; sleep "${2:-3}"; }

# A STRAY HARNESS IS THE MOST EXPENSIVE FAILURE HERE, and it does not announce
# itself: another run still writing to the command file clobbers ours, the game
# executes whichever line survived, and our assertions find nothing while the log
# plainly shows the feature working. That reads as a product regression and costs
# an afternoon - it did. TaskStop does NOT reliably kill a bash child, so this is
# a direct test of the actual hazard rather than a hope. See
# docs/in-game-testing.md, "Two ways a run silently corrupts itself".
guard_command_file() {
  local target="$1" sentinel="guard-$$-$RANDOM"
  printf '%s\n' "$sentinel" > "$target"
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

# --- the player's config belongs to whoever plays this install next ----------
CFG_BAK=""
save_env() { [ -f "$CFG" ] && { CFG_BAK="$(mktemp)"; cp "$CFG" "$CFG_BAK"; }; }
cleanup() {
  taskkill //IM CW4.exe //F >/dev/null 2>&1
  [ -n "$CFG_BAK" ] && [ -f "$CFG_BAK" ] && { cp "$CFG_BAK" "$CFG"; rm -f "$CFG_BAK"; }
  echo "  (config restored)"
}
save_env
trap cleanup EXIT

echo "span-data-check: booting 26 SPAN maps and dumping their objectives"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 3
mkdir -p "$OUT"
rm -f "$GAME_LOG" "$DEVCMD"
guard_command_file "$DEVCMD"
# AutoConnect OFF. This measures the GAME, and a randomizer trying to reach a
# server it has not been given would only add noise to the log being read.
mkdir -p "$(dirname "$CFG")"
printf '[Connection]\nHost = localhost\nPort = 38999\nSlot = DataCheck\nPassword =\nAutoConnect = false\n\n[Missions]\nShowSpan = true\n' > "$CFG"

( cd "$GAME_DIR" && ./CW4.exe > /dev/null 2>&1 & )
sleep 16

n=0
total=$(printf '%s\n' $GUIDS | wc -w)
for guid in $GUIDS; do
  n=$((n + 1))
  echo "[span-data-check] map $n/$total: $guid"
  send "span:boot $guid" 11
  send "ada:close" 2
  send "obj:dump" 4
done

cp "$GAME_LOG" "$OUT/log.txt"
taskkill //IM CW4.exe //F >/dev/null 2>&1; sleep 2

echo "span-data-check: comparing against apworld/cw4/span_data.py"
py -3.13 "$REPO/tools/span-data-compare.py" "$OUT/log.txt"
