# Open questions worksheet (2026-08-31) - ANSWERED, all three closed

**Outcome, recorded before the questions so this reads as a result rather than a
form.** All three are closed. Power zones and the porter's button mapping were
settled without the designer; the porter's UNIT was settled by building one, and
Tower of Darkness by playing it.

- **Miner: not required.** "Yes very doable with no miners on Tower of Darkness.
  not a requirement. the snipers are a little more important." Then: "snipers are
  not needed, but nice to haves. you can beat the level without them." Economy
  stays out of logic; snipers stay difficulty-tier, and explicitly not a hedge.
  Pinned by
  `test_miner_gates_nothing` and `test_sniper_on_tower_of_darkness_is_casual_only`.
- **Porter is `DeliveryPad` + `DeliveryDrone`**, both already whitelisted, both
  reading `=MINE`, with `ReportSkippedBuild` silent about them. No coverage gap
  ever existed.
- **Power zones: none in the campaign**, proven by positive control.

The questions as asked follow, for the record.

---


Fill this in and hand it back. Same rules as the per-mission worksheet: terse is
fine, and **leave a field blank rather than guessing - blank is information, a
wrong guess is not.**

Three questions were open before the full-seed playthrough. Two are now closed
without you; one needs you to play, and one needs a single mouse click.

## Already closed - no action needed

- **Power zones.** `powerZoneCells` read 0 on all 20 missions and was distrusted
  because a uniform zero is exactly what a broken reader looks like - this project
  shipped that bug once already with fog. Settled with a positive control: writing
  three cells with `SetPowerZone` makes two independent readers both report three,
  then restores. **The reader works, so the zeros are real** - the campaign has no
  power zones. The "bright blue reactor ground" in the old notes was almost
  certainly RESO: the MINER's button object is named `ReactorButton`.
- **The porter's name**, mostly. There are FOUR name spaces, not three - button
  object names match neither the build-pane key nor the unit name:
  PYLON is `SuperTowerButton`, MINER is `ReactorButton`, PORTER is
  `DeliveryPadButton`. So the porter is the delivery family, which both mods
  already whitelist; per-unit effects were covering porters all along. Question 1
  below closes the last 5% of it.

---

## 1. The porter, final confirmation (one click, any mission)

A button's object name does not strictly prove which unit it places - PYLON's
button is `SuperTowerButton` but its unit is `TowerBridge`. So this wants one
observation.

**What to do:** any mission, with the dev tools on. Press Ctrl+F6 (all buildings)
and Ctrl+F5 (instant build), then **build one PORTER**. Then press Ctrl+Home.

**What decides it:** whether the log warns
`'<name>' ... is building but is not in the player list`.

- Did that warning appear after you built the porter?
- If it did, what name did it print?
- Anything else odd about the porter (did it build at all, did it work):

**Decision this drives:** if no warning, the whitelist is complete and the note in
`GameUtil.cs` can be closed outright. If a warning, the name it prints is the
missing one and goes straight into both mods' whitelists.

---

## 2. Tower of Darkness without a Miner (the one that needs playing)

Run `tools/story15-handtest.sh`. It boots story15 granting **exactly** what logic
requires and nothing else: rift lab, tower, cannon, nullifier, chronat, refinery,
factory. No miner, no pylon, no platform, no terp, no sniper, no missile launcher,
and no energy or storage items. Cheats are off. The mod enforces the list, so you
cannot build a miner by accident.

That is the worst case a real seed can hand you, which is what logic has to
survive.

**The question is not "can you win".** It is whether ENERGY became the blocker in
a way a Miner would have fixed.

- Could you get the opening going on tower energy alone:
- Did energy ever stall you outright, or just slow you down:
- If it stalled you, roughly when and where (the start, or after a corner):
- Would a Miner have fixed it, or was the problem something else (land, creep
  pressure, the darkness):
- Did you finish the mission:
- If you did not finish, was that energy or something unrelated:
- Was it POSSIBLE but miserable, or actually not possible:
- Anything the setup got wrong (a unit you needed that you did not have, or one
  you had that you should not have):
- Notes:

**Decision this drives:** three outcomes, and I want to be able to tell them
apart.
- *Fine on towers alone* -> Miner stays out of logic, and the flag in
  `randomizer-design.md` gets closed with your answer as the evidence.
- *Impossible without mining* -> `MISSION_EXTRA[15]` gains `[["Miner"]]`.
- *Possible but miserable* -> a hedged call, exactly like Archon's two entries
  (both hedged, both treated as required). Your "miserable or not" answer is what
  I would quote in the comment.

---

## 3. Anything else you noticed

Free text. If the setup itself looked wrong - a build pane offering something it
should not, an objective that did not behave - that is worth more than the answers
above, because it means a test was measuring the wrong thing.

-

---

## Why these three could not be derived

Question 1 needs a unit placed by a real build action: the ghost-prefab oracle
that looked like it would answer it turns out to report `(no UnitManager)` for
every button, and `pane:dump`'s ON/off reflects paging rather than availability.
Question 2 needs judgement about whether a mission is reasonable, which no log
line contains. Synthetic mouse input does not reach CW4's UI, so neither can be
scripted.
