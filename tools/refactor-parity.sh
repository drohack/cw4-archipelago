#!/usr/bin/env bash
# Did this RESTRUCTURE change any seed? It must not change a single one.
#
# WHY A SECOND PARITY HARNESS. tools/span-off-parity.sh answers a different
# question and cannot be pointed at this one. It hard-exits unless the base
# commit PREDATES span_missions - a deliberate premise assertion, because for a
# feature it would otherwise compare the change against itself and pass for the
# wrong reason. A refactor wants the exact opposite: the base is HEAD, the two
# sides are supposed to be identical, and "identical" is the passing answer
# rather than the suspicious one.
#
# It also differs in what it tolerates. span-off-parity runs parity-check.py
# with its allow-lists live, because the SPAN change deliberately added
# slot_data keys and deliberately fixed Archon. A restructure is allowed to
# change NOTHING, so this sets CW4_PARITY_STRICT=1 and both allow-lists empty.
# The differences most likely to be introduced by accident while moving code are
# exactly the ones those allow-lists would wave through.
#
# And it runs TWO configurations. Span OFF is the default a player gets; span ON
# is the only one that exercises the roster draw, the breadth floor and the
# widening swap - which is most of the code this audit moves. Running only the
# default would leave the moved code almost entirely untested.
#
# Usage: tools/refactor-parity.sh [base-commit] [seed-count]
#        base defaults to HEAD, so this compares the committed tree against the
#        working tree - run it with uncommitted moves in place.
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }

BASE="${1:-HEAD}"
SEEDS="${2:-5}"
WORK="$REPO/.aptest/refactor-parity"
WORLD="$AP/worlds/cw4"

pass=0; fail=0
verdict() { if [ "$1" = 0 ]; then pass=$((pass+1)); echo "  PASS  $2";
            else fail=$((fail+1)); echo "  FAIL  $2"; fi; }

echo "refactor parity: $BASE vs the working tree, $SEEDS seeds x 2 configurations"

# The working tree's apworld has to go back when this is done, whatever happens -
# the synced copy is what every other harness and test run uses.
restore() {
  rm -rf "$WORLD"
  cp -r "$REPO/apworld/cw4" "$WORLD"
  echo "  (working-tree apworld restored into the clone)"
}
trap restore EXIT

rm -rf "$WORK"
mkdir -p "$WORK/old" "$WORK/players-off" "$WORK/players-on"

# Span OFF is the default, so the default yaml IS that configuration.
printf 'name: ParityTester\ngame: Creeper World 4\nCreeper World 4: {}\n' \
  > "$WORK/players-off/cw4.yaml"
# Span ON, which is what exercises mission_roster, the breadth floor and
# _widen_roster - the code most of this audit moves.
printf 'name: ParityTester\ngame: Creeper World 4\nCreeper World 4:\n  span_missions: true\n' \
  > "$WORK/players-on/cw4.yaml"

git -C "$REPO" archive "$BASE" apworld/cw4 | tar -x -C "$WORK/old"
[ -d "$WORK/old/apworld/cw4" ]; verdict $? "extracted the apworld as it was at $BASE"
[ -d "$WORK/old/apworld/cw4" ] || exit 1

generate() {   # $1 = apworld dir, $2 = output dir, $3 = players dir
  rm -rf "$WORLD"
  cp -r "$1" "$WORLD"
  mkdir -p "$2"
  local s
  for s in $(seq 1 "$SEEDS"); do
    ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python Generate.py \
        --player_files_path "$3" --outputpath "$2/$s" --seed "$s" \
        > "$2/$s.log" 2>&1 )
    ( cd "$2/$s" 2>/dev/null && unzip -o -q ./*.zip 2>/dev/null )
  done
}

for cfg in off on; do
  echo
  echo "=== span $cfg ==="
  generate "$WORK/old/apworld/cw4" "$WORK/out-old-$cfg" "$WORK/players-$cfg"
  generate "$REPO/apworld/cw4"     "$WORK/out-new-$cfg" "$WORK/players-$cfg"

  produced=0
  for s in $(seq 1 "$SEEDS"); do
    o="$(ls "$WORK/out-old-$cfg/$s/"*.archipelago 2>/dev/null | head -1)"
    n="$(ls "$WORK/out-new-$cfg/$s/"*.archipelago 2>/dev/null | head -1)"
    if [ -z "$o" ] || [ -z "$n" ]; then
      verdict 1 "span $cfg seed $s generated on both sides"
      [ -z "$o" ] && tail -5 "$WORK/out-old-$cfg/$s.log" 2>/dev/null | sed 's/^/        old: /'
      [ -z "$n" ] && tail -5 "$WORK/out-new-$cfg/$s.log" 2>/dev/null | sed 's/^/        new: /'
      continue
    fi
    produced=$((produced+1))
    echo "-- span $cfg, seed $s"
    ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 CW4_PARITY_STRICT=1 \
        python "$REPO/tools/parity-check.py" "$o" "$n" )
    verdict $? "span $cfg seed $s is unchanged"
  done

  # Zero seeds on BOTH sides is agreement of the useless kind. Say so loudly
  # rather than letting an empty run add nothing to the pass count.
  [ "$produced" != 0 ]; verdict $? "span $cfg produced seeds to compare at all"
done

echo
echo "refactor parity: $pass passed, $fail failed"
[ "$fail" = 0 ]
