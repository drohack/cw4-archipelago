#!/usr/bin/env bash
# Survey all 26 SPAN Experiments: what objectives each has, and what it lets
# you build. Phase 1 of adding SPAN to the randomizer - no logic is written
# from this until the designer has read it.
#
# WHY IT RUNS ON THE DEV TOOLS CHANNEL, with the randomizer parked:
#   - the randomizer hides the SPAN button (ModConfig.ShowSpan defaults false)
#   - its UnitGate rewrites all 26 availability flags EVERY FRAME, so with it
#     installed buildings:dump reports the AP-allowed set instead of the
#     mission's own, and every map would look identical
#
# The consequence is that the randomizer's richer dumps (inst:dump, counts:dump,
# totems:dump, resources:dump) are NOT available here. That is fine for this
# phase: obj:dump already gives totem / nullify / cache counts, which is what
# the roster table needs. Per-instance map cells come later, when MapCells is
# regenerated for SPAN.
#
# Output: .aptest/span-survey.txt, one block per map.
# Usage: tools/span-survey.sh          (game must be CLOSED)
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }
require_game

CMD="$DEV_CMD"
OUT="$REPO/.aptest/span-survey.txt"
SHOTS="$GAME_DIR"

send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }

# The 26 maps, guid|title. Captured from the DEVPLANET lines of the phase-0
# spike, which has since been retired; span:list in CW4DevTools is how to
# regenerate this table.
read -r -d '' MAPS <<'EOF' || true
knucracker1|Forgotten Fortress
knucracker2|Four Pieces
knucracker3|Neuron
knucracker4|Creeper++
knucracker5|Turtle
knucracker6|Valley of the Shadow of Death
knucracker7|Parasite
knucracker8|Cheap Construction
knucracker9|Sector L
knucracker10|Gort
knucracker11|Creepers Pieces
knucracker12|Special
knucracker13|Highway to helheim
knucracker14|Creeperpeace
knucracker15|Islands
knucracker16|Enchanted Forest
knucracker17|The Dark Side
knucracker18|Far York Farm
knucracker19|Chanson
knucracker20|Invasion
knucrackerbonus0|Razor
knucrackerbonus1|Holdem 2
demobonus|Mark V Sample
demobonus2|Before Time
demobonus3|Day of Infamy
demobonus4|Shaka
EOF

# Park the randomizer and restore it however we exit. Renaming does NOT work -
# BepInEx scans subfolders recursively. Both halves go: CW4ApDebug hard-depends
# on the mod.
RANDOMIZER_DIRS="CW4Archipelago CW4ApDebug"
RESTORE_RANDOMIZER=0
restore_randomizer() {
  [ "$RESTORE_RANDOMIZER" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in $RANDOMIZER_DIRS; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored to plugins/)"
}
trap restore_randomizer EXIT
for d in $RANDOMIZER_DIRS; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"; rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE_RANDOMIZER=1
  fi
done
[ "$RESTORE_RANDOMIZER" = "1" ] && echo "  (randomizer parked; restored at the end)"

echo "== setup =="
taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
mkdir -p "$(dirname "$OUT")"
rm -f "$GAME_LOG" "$CMD" "$OUT"
( cd "$GAME_DIR" && ./CW4.exe >/dev/null 2>&1 & )

echo "== waiting for the dev tools =="
ok=1
for i in $(seq 1 120); do
  grep -q "Dev Tools loaded" "$GAME_LOG" 2>/dev/null && { ok=0; break; }
  sleep 2
done
[ "$ok" = "0" ] || { echo "FAIL: dev tools never loaded"; exit 1; }
if grep -q "Loading \[CW4 Archipelago" "$GAME_LOG"; then
  echo "FAIL: randomizer still loaded - buildings:dump would report the AP set"
  exit 1
fi
echo "  ready"

# POSITIVE CONTROL. buildings:dump is new and its whole value is the NEGATIVE
# ("this map does not offer a Terp"), so a dump that silently reported
# everything-false, or everything-true, would look like data. Check it against a
# FARSITE mission whose availability is already known from the worksheet before
# trusting it on 26 maps nobody has played.
echo "== control: a Farsite mission, where the answer is already known =="
send "boot:story1" 22
send "ada:close" 3
mark
send "buildings:dump" 4
ctl=$(since | grep "DEVBUILD ON" | tail -1)
echo "  story1 $ctl"
if [ -z "$ctl" ]; then echo "FAIL: buildings:dump produced nothing on a known mission"; exit 1; fi
if since | grep -q "forcedByCheat=True"; then
  echo "FAIL: AllBuildings is forcing the flags - every map would look identical"
  exit 1
fi
{
  echo "===== CONTROL story1 (Farsite) ====="
  since | grep -E "DEVBUILD|DEVOBJ"
} >> "$OUT"

n=0
total=$(printf '%s\n' "$MAPS" | grep -c .)
while IFS='|' read -r guid title; do
  [ -n "$guid" ] || continue
  guid=$(printf '%s' "$guid" | tr -d '\r')
  title=$(printf '%s' "$title" | tr -d '\r')
  n=$((n+1))
  echo "[$n/$total] $guid ($title)"

  mark
  send "span:boot $guid" 8

  live=1
  for i in $(seq 1 15); do
    send "obj:dump" 3
    if since | grep -q "DEVOBJ mission="; then live=0; break; fi
    sleep 2
  done
  if [ "$live" != "0" ]; then
    echo "    NOT BOOTED"
    { echo "===== $guid | $title | NOT BOOTED"; since | grep -E "DEVSPANBOOT|span:boot"; } >> "$OUT"
    continue
  fi

  send "ada:close" 2
  send "obj:dump" 3
  send "buildings:dump" 3
  send "dump" 5
  send "shot:$SHOTS/span-$guid.png" 6

  # The mission obj:dump reports is the one that ACTUALLY loaded. The direct
  # boot route assumes specifier == guid; this is where a wrong assumption shows
  # up instead of being silently surveyed as the wrong map.
  got=$(since | grep -oE "DEVOBJ mission=[^ ]+" | tail -1 | cut -d= -f2)
  if [ "$got" != "$guid" ]; then
    echo "    MISMATCH: asked for '$guid', loaded '$got'"
  fi
  echo "    $(since | grep -oE 'DEVOBJ mission=.*infocaches=[0-9]+' | tail -1)"

  {
    echo "===== $guid | $title | loaded=$got"
    since | grep -E "DEVSPANBOOT|DEVOBJ|DEVBUILD|DEVTOOLS ENEMY|DEVTOOLS cmods|DEVTOOLS units on map|DEVSTATE"
  } >> "$OUT"
done <<< "$MAPS"

taskkill //F //IM CW4.exe >/dev/null 2>&1
mkdir -p "$REPO/.aptest/span-shots"
mv "$SHOTS"/span-*.png "$REPO/.aptest/span-shots/" 2>/dev/null

echo ""
echo "== done =="
echo "  survey: $OUT"
echo "  shots : $REPO/.aptest/span-shots/"
echo "  blocks: $(grep -c '^=====' "$OUT" 2>/dev/null || echo 0)"
echo "  not booted: $(grep -c 'NOT BOOTED' "$OUT" 2>/dev/null || echo 0)"
