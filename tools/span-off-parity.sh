#!/usr/bin/env bash
# With span_missions OFF, does this apworld generate the SAME seed the old one did?
#
# "The tests still pass" and "a span-off seed is unchanged" are different claims,
# and only the second is what a player who leaves the option alone cares about. A
# change that shifted every placement by one would pass every test in the repo
# and still hand them a different game.
#
# So this generates the same seeds twice - once from the apworld as it was before
# the SPAN work, extracted from git, once from the working tree - and diffs the
# real multidata: location ids, every placement, and slot_data.
#
# Usage: tools/span-off-parity.sh [base-commit] [seed-count]
set -u

. "$(dirname "${BASH_SOURCE[0]}")/lib.sh" \
  || { echo "FATAL: cannot source tools/lib.sh" >&2; exit 1; }

BASE="${1:-0fe2bef}"
SEEDS="${2:-5}"
WORK="$REPO/.aptest/parity"
WORLD="$AP/worlds/cw4"

pass=0; fail=0
verdict() { if [ "$1" = 0 ]; then pass=$((pass+1)); echo "  PASS  $2";
            else fail=$((fail+1)); echo "  FAIL  $2"; fi; }

echo "span-off parity: $BASE vs the working tree, $SEEDS seeds"

# The working tree's apworld has to go back when this is done, whatever happens -
# the synced copy is what every other harness and test run uses.
restore() {
  rm -rf "$WORLD"
  cp -r "$REPO/apworld/cw4" "$WORLD"
  echo "  (working-tree apworld restored into the clone)"
}
trap restore EXIT

rm -rf "$WORK/old" "$WORK/out-old" "$WORK/out-new" "$WORK/players"
mkdir -p "$WORK/old" "$WORK/out-old" "$WORK/out-new" "$WORK/players"

# DEFAULTS, and nothing else. The option is off by default, so the default yaml
# is exactly the configuration this is about.
printf 'name: ParityTester\ngame: Creeper World 4\nCreeper World 4: {}\n' \
  > "$WORK/players/cw4.yaml"

git -C "$REPO" archive "$BASE" apworld/cw4 | tar -x -C "$WORK/old"
[ -d "$WORK/old/apworld/cw4" ]; verdict $? "extracted the apworld as it was at $BASE"
[ -d "$WORK/old/apworld/cw4" ] || exit 1

# A PREMISE WORTH ASSERTING: the old copy must not already know about SPAN, or
# this compares the change against itself and passes for the wrong reason.
if grep -rq "span_missions" "$WORK/old/apworld/cw4" 2>/dev/null; then
  verdict 1 "the base commit predates span_missions"
  exit 1
fi
verdict 0 "the base commit predates span_missions"

generate() {   # $1 = apworld dir, $2 = output dir
  rm -rf "$WORLD"
  cp -r "$1" "$WORLD"
  local s
  for s in $(seq 1 "$SEEDS"); do
    ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python Generate.py \
        --player_files_path "$WORK/players" --outputpath "$2/$s" --seed "$s" \
        > "$2/$s.log" 2>&1 )
    ( cd "$2/$s" 2>/dev/null && unzip -o -q ./*.zip 2>/dev/null )
  done
}

echo "generating $SEEDS seeds from $BASE"
generate "$WORK/old/apworld/cw4" "$WORK/out-old"
echo "generating $SEEDS seeds from the working tree"
generate "$REPO/apworld/cw4" "$WORK/out-new"

for s in $(seq 1 "$SEEDS"); do
  o="$(ls "$WORK/out-old/$s/"*.archipelago 2>/dev/null | head -1)"
  n="$(ls "$WORK/out-new/$s/"*.archipelago 2>/dev/null | head -1)"
  if [ -z "$o" ] || [ -z "$n" ]; then
    verdict 1 "seed $s generated on both sides"
    [ -z "$o" ] && tail -5 "$WORK/out-old/$s.log" 2>/dev/null | sed 's/^/        old: /'
    [ -z "$n" ] && tail -5 "$WORK/out-new/$s.log" 2>/dev/null | sed 's/^/        new: /'
    continue
  fi
  echo "-- seed $s"
  ( cd "$AP" && SKIP_REQUIREMENTS_UPDATE=1 python "$REPO/tools/parity-check.py" "$o" "$n" )
  verdict $? "seed $s is unchanged"
done

echo
echo "span-off parity: $pass passed, $fail failed"
[ "$fail" = 0 ]
