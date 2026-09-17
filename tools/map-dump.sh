#!/usr/bin/env bash
# Dump terrain + unit cells for a list of missions, for reachability analysis.
#
# Usage:
#   tools/map-dump.sh                       # the Farsite control set
#   tools/map-dump.sh "story19 story11"     # explicit Farsite missions
#   SPAN=1 tools/map-dump.sh "knucracker1"  # SPAN maps, by guid
#
# THE CONTROL SET IS CHOSEN, not arbitrary. Whether a reachability analysis is
# measurement or a dressed-up guess is decided by whether it reproduces mover
# requirements that are already known from play:
#
#   story19  Founders  totems 2,3,4 need Platform-or-Terp; 1 and 5 do not
#                      (rules.OBJECTIVE_INSTANCE_EXTRA) - a WITHIN-MAP split,
#                      which is the strongest possible control: a analysis that
#                      just says "big map, needs a mover" cannot fake it
#   story11  Shattered totem 3 needs Porter-or-Platform; 1 and 2 do not
#   story2   Home      no mover requirement anywhere - the negative control
#
# Output: .aptest/maps/<mission>.map
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

CMD="$DEV_CMD"
OUTDIR="$REPO/.aptest/maps"
MISSIONS="${1:-story19 story11 story2}"
SPAN="${SPAN:-0}"

send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }

RESTORE=0
restore() {
  [ "$RESTORE" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in CW4Archipelago CW4ApDebug; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored)"
}
trap restore EXIT
for d in CW4Archipelago CW4ApDebug; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"; rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE=1
  fi
done

taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
mkdir -p "$OUTDIR"
rm -f "$GAME_LOG" "$CMD"
( cd "$GAME_DIR" && ./CW4.exe >/dev/null 2>&1 & )
for i in $(seq 1 120); do
  grep -q "Dev Tools loaded" "$GAME_LOG" 2>/dev/null && break
  sleep 2
done
echo "  ready"

for m in $MISSIONS; do
  echo "  $m"
  mark
  if [ "$SPAN" = "1" ]; then send "span:boot $m" 8; else send "boot:$m" 12; fi
  live=1
  for i in $(seq 1 15); do
    send "obj:dump" 3
    since | grep -q "DEVOBJ mission=" && { live=0; break; }
    sleep 2
  done
  if [ "$live" != "0" ]; then echo "    NOT BOOTED"; continue; fi
  send "ada:close" 2
  # LET THE SIM TICK FIRST. At load no player unit exists at all - story2 dumps
  # zero of them - so the rift lab has not spawned and a reachability flood fill
  # would have no source. A few seconds of sim is the difference between
  # measuring the map and measuring an empty stage.
  send "sim:run" 2
  sleep 8
  send "sim:pause" 2
  # Windows path - Unity's File.WriteAllText will not resolve a git-bash one.
  # Totem ware demand in the same pass. Campaign totems want liftic, which is
  # why the randomizer requires the Factory - but the wanted ware is authored
  # PER MAP, so on SPAN it has to be measured rather than inherited.
  send "totems:dump" 4
  # What the ware indices actually mean, in the game's own words.
  send "wares:names" 3
  send "map:dump $GAME_DIR/mapdump.txt" 8
  if [ -f "$GAME_DIR/mapdump.txt" ]; then
    mv "$GAME_DIR/mapdump.txt" "$OUTDIR/$m.map"
    echo "    $(since | grep -oE 'DEVMAP wrote.*' | tail -1)"
  else
    echo "    NO FILE WRITTEN"
    since | grep -E "map:dump|DEVMAP" | tail -2
  fi
done

grep -E "DEVTOTEM" "$GAME_LOG" >> "$REPO/.aptest/totems.txt" 2>/dev/null
grep -E "DEVWARE " "$GAME_LOG" | sort -u >> "$REPO/.aptest/warenames.txt" 2>/dev/null

taskkill //F //IM CW4.exe >/dev/null 2>&1
echo ""
echo "  totem ware lines: $(grep -c DEVTOTEM "$REPO/.aptest/totems.txt" 2>/dev/null || echo 0)"
echo "  wrote:"
ls -la "$OUTDIR" | tail -n +2
