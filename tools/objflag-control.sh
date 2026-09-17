#!/usr/bin/env bash
# What does MissionObjectiveData.required actually MEAN?
#
# The SPAN survey read "required=False on 25 of 26 maps" as "these maps have no
# win condition", which is not a believable reading of a shipped game. The
# survey had no baseline: it dumped buildings for a Farsite control but never
# dumped OBJECTIVES for one, so the flag was interpreted against nothing.
#
# This is that baseline. The apworld already knows which slots each Farsite
# mission requires (locations.REQUIRED_OBJECTIVES), from a manual playthrough:
#
#     story2  -> [0, 1, 4]     Nullify, Totems, Collect
#     story6  -> [2]           Reclaim
#     story19 -> [4, 5]        Collect, Custom      (Founders, the finale)
#     story20 -> [0, 1, 2, 5]  Nullify, Totems, Reclaim, Custom
#
# So if `required` means "required to win", those slots read True and the rest
# read False. If Farsite ALSO reports required=False everywhere, the flag means
# something else and the SPAN conclusion is wrong.
#
# Usage: tools/objflag-control.sh          (game must be CLOSED)
set -u

G="${CW4_DIR:-G:/Games/Steam/steamapps/common/Creeper World 4}"
LOG="$G/BepInEx/LogOutput.log"
CMD="$G/BepInEx/cw4dev-commands.txt"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$REPO/.aptest/objflag-control.txt"

send() { printf '%s\n' "$1" > "$CMD"; sleep "${2:-3}"; }
MARK=0
mark() { MARK=$(wc -l < "$LOG" 2>/dev/null || echo 0); }
since() { tail -n +"$((MARK+1))" "$LOG" 2>/dev/null; }

PLUGINS="$G/BepInEx/plugins"
PARKED="$G/BepInEx/plugins-disabled"
RANDOMIZER_DIRS="CW4Archipelago CW4ApDebug"
RESTORE=0
restore() {
  [ "$RESTORE" = "1" ] || return 0
  mkdir -p "$PLUGINS"
  for d in $RANDOMIZER_DIRS; do
    [ -d "$PARKED/$d" ] && mv "$PARKED/$d" "$PLUGINS/$d" 2>/dev/null
  done
  echo "  (randomizer restored)"
}
trap restore EXIT
for d in $RANDOMIZER_DIRS; do
  if [ -d "$PLUGINS/$d" ]; then
    mkdir -p "$PARKED"; rm -rf "$PARKED/$d"
    mv "$PLUGINS/$d" "$PARKED/$d" && RESTORE=1
  fi
done

taskkill //F //IM CW4.exe >/dev/null 2>&1; sleep 3
mkdir -p "$(dirname "$OUT")"
rm -f "$LOG" "$CMD" "$OUT"
( cd "$G" && ./CW4.exe >/dev/null 2>&1 & )
for i in $(seq 1 120); do
  grep -q "Dev Tools loaded" "$LOG" 2>/dev/null && break
  sleep 2
done
echo "  ready"

probe() { # probe <bootcmd> <label>
  mark
  send "$1" 10
  for i in $(seq 1 12); do
    send "obj:dump" 3
    since | grep -q "DEVOBJ mission=" && break
    sleep 2
  done
  send "ada:close" 2
  send "obj:dump" 3
  {
    echo "===== $2"
    since | grep -E "DEVOBJ" | tail -8
  } >> "$OUT"
  echo "  $2"
  since | grep -E "DEVOBJSLOT" | tail -6 | sed 's/.*DEVOBJSLOT/    slot/'
}

# Farsite, where the required set is already known from play.
probe "boot:story2"  "story2 (expect required slots 0,1,4)"
probe "boot:story6"  "story6 (expect required slot 2)"
probe "boot:story20" "story20 (expect required slots 0,1,2,5)"

# SPAN, for the same reading side by side.
probe "span:boot knucracker1"  "knucracker1 Forgotten Fortress"
probe "span:boot knucracker19" "knucracker19 Chanson (the one with required=True)"

taskkill //F //IM CW4.exe >/dev/null 2>&1
echo ""
echo "  wrote $OUT"
