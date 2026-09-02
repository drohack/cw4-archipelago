# ERN port upgrades: what each one actually does

Measured in game, not derived. Every number below reproduced at least twice
except where the table says otherwise.

The question this answers: the two Archipelago filler items act on CW4's own ERN
port upgrades, so their value depends entirely on what those upgrades do and
whether a boosted ceiling above 100 percent reaches the simulation at all.

## The mechanism

An ERN port (`ERNInterface`) has six upgrades. Docking an ERN ramps one from 0
to 1 over `EFFICIENCY_TIME` (3600 ticks), tracked as a TIMESTAMP in
`dockedTimes[]` - efficiency is `(tickCount - dockedTimes[i]) / EFFICIENCY_TIME`.

    Progressive ERN Efficiency Rate: <upgrade>   fills faster   -> retreat dockedTimes[i]
    Progressive ERN Efficiency Cap:  <upgrade>   higher ceiling -> scale the efficiency accessor

**The bug that cost the most time.** `ERNInterface` exposes efficiency twice:

    GetEff(int)          instance, per port   -> feeds the port UI
    GetEfficiency(int)   STATIC               -> what the sim reads

Only `GetEff` was patched at first. Every probe also called `GetEff`, so the
boost read back as a clean 1.25 / 1.50 / 1.75 / 2.00 - reproduced four separate
times - while the sim went on calling the untouched static and no cannon's range
ever moved. The measurement was self-confirming: it read back exactly the value
it had written. Both are patched now, sharing one `ErnCeiling.Scale`.

## Results

| upgrade | observable | 0% | 100% | 200% | runs |
|---|---|---|---|---|---|
| Fire Range | `Cannon.MYRANGE` | 9 | 11 | 13 | 2 |
| | `Mortar.MYRANGE` | 13 | 16 | 19 | 1 |
| | `Sniper.MYRANGE` | 30 | 37 | 45 | 1 |
| | `MissileLauncher.MYRANGE` | 40 | 50 | 60 | 1 |
| Fire Rate | `Cannon.COOL_DOWN` | 8 | 6 | 4 | 2 |
| Energy Production | energy/tick | 0.0533 | 0.0700 | 0.0867 | 3, identical |
| Move Speed | ticks per 12 cells | 192 | 129 | 108 | 2 |
| Build Speed | ticks to build a cannon | 363 | 186 | 99 | 3, exact |
| Mine Production | factory rate, bluite | 2.1 | 4.2 | 6.4 | 1 |
| | factory rate, reddite | 2.0 | 4.0 | 6.0 | 1 |

Fire Range follows `floor(RANGE * (1 + 0.25 * eff))` exactly, for every weapon
type; `UPGRADE_RANGE_BOOST` is 0.25 on all of them.

Energy Production is dead linear: +31.25 percent per 100 percent efficiency, so
+62.5 percent at the ceiling.

Move Speed fits `time = overhead + distance/speed` with about 59 ticks of fixed
takeoff and landing and a multiplier near `1 + 0.9 * eff`, i.e. roughly 2.8x
actual movement speed at 200 percent.

**Build Speed is NOT linear**, and needed its own ceiling. At the usual 2.0 it
measured 33 ticks - about 11x base - which made construction near instant and
dwarfed the +62.5 percent that four copies of Energy Production buy.

Designer's target: "double the 100 percent", i.e. 93 ticks. Swept with
`tools/ern-buildcap-test.sh`, every level twice in one session:

| ceiling | build ticks | vs the 100 percent rate |
|---|---|---|
| 1.40 | 99 | 1.88x |
| **1.50** | **99** | **1.88x  <- shipped** |
| 1.60 | 78 | 2.38x |
| 1.70 | 54 | 3.44x |
| 1.80 | 33 | 5.64x |
| 2.00 | 33 | 5.64x, the curve floors out |

The game QUANTIZES build time, so 93 is not attainable - the real choice is 99
or 78. 1.50 is nearest the target, errs conservative, gives a clean +12.5
percent per copy, and sits on the 1.4-1.5 plateau so it is not sensitive to
small changes. Confirmed against the shipped table (override untouched): eff
1.5, 99 ticks, three times.

**Measure, do not fit.** A linear fit of the 0/100/200 points predicted a 1.64
ceiling for 93 ticks. The real value at 1.70 is 54. The fit was badly wrong
near the top and would have shipped an item twice as strong as intended.

So the ceiling is PER UPGRADE, and the per-copy step is derived from it rather
than fixed at 0.25 - the fourth copy lands exactly on the ceiling and no copy is
ever a no-op, which is the failure that got build limits removed from the pool.

## The ramp shape - FIXED

### The bug

The ceiling used to be a MULTIPLY on the game's clamped 0-to-1 ramp, and
multiplying a curve steepens it. So a capped slot climbed at double rate and
crossed EVERY level twice as fast - which is ERN Efficiency Rate's entire job,
handed over for free. The two items were not separate at all.

| | ramp rate | ticks to its ceiling |
|---|---|---|
| unboosted | 1/3600 | 3600 to reach 100% |
| 4 cap items, BEFORE | 2/3600 | 3600 to reach 200% |

### The fix

Take elapsed time UNCLAMPED and cap it at the ceiling, which LENGTHENS the ramp
instead of steepening it. Measured after the change:

| | ramp rate | ticks to 100% | ticks to 200% |
|---|---|---|---|
| no cap items | 0.700 eff / 2520 ticks = 1/3600 | 3600 | n/a |
| 4 cap items | 1.625 eff / 5940 ticks = 1/3600 | 3600 | 7200 |

Identical fill rate; both cross 100 percent after the same 3600 ticks, and the
capped slot then climbs a second full ramp to 200. Panel text tracked it 37, 59,
82, 104, 129, 157, 179, 200 percent, with the cannon's MYRANGE going
9, 10, 11 (at 100), 12, 13 (at 200).

Efficiency Rate still speeds this up, because it works by retreating
`dockedTimes` and that makes elapsed grow faster - so the two items compose, and
holding both is what reaches the ceiling quickly. That is the point of there
being two.

Implementation note: computed in `ErnUpgrades.ComputeEffective` and read by the
patches, because the STATIC `ERNInterface.GetEfficiency` has no port to read
`dockedTimes` from. It is applied as a MAX so our arithmetic can never drag the
game's own efficiency down, and with no cap item held the override stays -1 and
vanilla behaviour is byte-identical.

Adding cap items to a slot that is ALREADY full still snaps it to 200 percent,
because elapsed time is already past the ceiling. An ERN that has been parked a
while pays off immediately; a freshly docked one has to earn it.

## What the player sees

The port panel row (`UpgradeItem`) has two display surfaces and they disagree:

- `efficiencyText` shows the real value and counts up past 100 correctly:
  33% -> 78% -> 128% -> 178% -> 200%
- `efficiencyBar` is a Unity `Image`, whose `fillAmount` is CLAMPED to 0..1, so
  a 200 percent value cannot overfill the bar

Designer's call: the number is the easier of the two to read, so the text is the
surface that matters and the bar is left alone. (The bar read `fillAmount=1` at
every value including 0 percent in these runs, but the panel was closed
throughout, so treat that as unmeasured rather than broken.)

Adding boosts to a slot that is ALREADY full snaps it 100 -> 200 instantly.
That is intended: an item arriving should pay off immediately.

## Mine Production: measured, and the three observables it took

Result: **+100 percent production per 100 percent efficiency**, exactly
`1 + eff`. Both resources agree - bluite 2.1 / 4.2 / 6.4 and reddite
2.0 / 4.0 / 6.0 at 0 / 100 / 200 percent. It is the strongest linear upgrade of
the six.

Three observables were tried. The first two were dead ends and each one would
have reported a confident zero:

1. **`Resource.PRODUCTION_INTERVAL`** holds at its map value (20 or 60) across
   all three levels, so unlike Fire Rate's `COOL_DOWN` the upgrade is not
   written into the node.
2. **Total ware held** (`measure:ware`, summing `GetWareHeld` over player
   units) read a flat `0 -> 0` while the nodes were visibly producing at
   `counter=24`. Two independent faults, and the first was misdiagnosed at the
   time: `GetWareHeld` reads `UnitManager.waresHeld`, which on a factory is its
   AMMO_WARES **input** slots - the mined **output** stock is
   `Factory.producedWareCounts`, reached through `GetProducedWares` /
   `SetProducedWares`. So it was the wrong dictionary, not a blind spot. (Both
   are real storage: `DevTools.TopUpUnit` fills a factory's inputs with
   `SetWareHeld` and that works.) AND a total saturates once the factory fills,
   which the designer spotted before it wasted a run.
3. **The factory build button's rate readout** - which works, because a rate
   does not saturate when storage fills, and it is the same number the player
   reads on screen:

       SingletonUnits/Buttons/Factory/Amts/BlueProduction   "+2.1"
       SingletonUnits/Buttons/Factory/Amts/RedProduction    "+2.0"

   Read with `ui:text +` and parsed by object name, so it needed no rebuild -
   which mattered, because rebuilding means killing the game and the mining
   economy could not be rebuilt by script.

### Why it could not be automated

Four automated attempts read `counter=0 wareAvailable=False` - a node produces
nothing until something mines it. Contributing causes, in the order they were
found:

- **Missions 2 to 4 have no resource nodes at all.** Every early attempt ran on
  story2. `tools/ern-ore-scan.sh` maps the campaign: nodes appear in story5 (1),
  story6 (4), story7 (1), story8 (1), story10 (1), story12 (6), story15 (3).
- **`allowed=[riftlab,tower]` was OUR OWN randomizer**, not the mission. Miner,
  Factory and Greenar Refinery are Archipelago unlock items
  (`UnitRules.ItemToUnit`), so without granting them the build pane has nothing
  that mines. `set:allbuildings` does not cover this - the mod's gating runs on
  top of the dev tools' cheat.
- Even with both fixed, placing a miner and factory by script did not wake a
  node. The economy was built BY HAND in the end
  (`tools/ern-mine-setup.sh` prepares the mission and stands aside,
  `tools/ern-mine-measure.sh` attaches to the running game and measures).

### Wait for the rate to settle

The rate readout climbs while an upgrade ramps, so a level sampled mid-climb
reads as a different level. Sampled straight after setup it gave 2.1, 2.3, 2.9,
3.0 - and the climb was the docked ERN ramping, not the economy spinning up.
`stable_rate` requires three consecutive identical reads before recording, and
reports a level as unusable rather than quoting a number from mid-ramp.

## Strength comparison, all six at their ceiling

| upgrade | per 100 percent | at its ceiling |
|---|---|---|
| Mine Production | +100 percent | 3.00x |
| Move Speed | about +90 percent | about 2.8x |
| Fire Rate | reload 8 -> 6 -> 4 | 2.00x rate |
| Build Speed | see its own ceiling | 1.88x (capped at 150 percent) |
| Fire Range | +25 percent | 1.50x range |
| Energy Production | +31.25 percent | 1.63x |

Mine Production and Move Speed are the strongest, Energy Production the weakest
by some way. Worth a balance pass now that all six are known.

## Tools

| tool | measures |
|---|---|
| `tools/ern-all-upgrades.sh` | static observables for all six, before/after |
| `tools/ern-rate-tests.sh` | Energy Production and Build Speed, timed in-loop |
| `tools/ern-move-test.sh` | Move Speed, by relocating a cannon |
| `tools/ern-ui-check.sh` | the ramp, and what the port panel displays |
| `tools/ern-field-diff.sh` | dump every primitive property and diff it |
| `tools/ern-ore-scan.sh` | which missions have resource nodes |
| `tools/ern-mine-test.sh` | Mine Production (incomplete, see above) |
| `tools/reflect` | find a member in the interop assembly, no game launch |

Debug commands added for this work: `ern:dump` (both accessors), `ern:stats`,
`ern:ui`, `ern:dumpall <unit|gs>`, `ern:resources`, `spawnat:<key> <x> <y>`,
`measure:build <key>`, `measure:move <cells>`, `measure:energy <ticks>`,
`measure:ware <ticks>`.

Method notes, including the traps that produced confident wrong numbers, are in
[in-game-testing.md](in-game-testing.md).
