# Testing against the live game

Everything here was learned by getting it wrong first, usually while reporting
confident numbers. The short version: **look at the screen, and start from a
script that already works.**

## The setup recipe

Do not write a new harness from memory. Start from `tools/cmod-traptest.sh` or
`tools/apbattery2.sh` - they already encode all of this - and change what the new
test needs.

    1. taskkill the game, write a known BepInEx config, empty the command file
    2. launch, and WAIT for "SCENE: 'Galaxy'" in the log
    3. grant the mission unlock, boot the mission
    4. turn on the dev-tools cheats
    5. spawn what the test acts on, asserting each placement
    6. sim:run to unpause
    7. clear the ADA panels
    8. only now start measuring

### 4. The dev-tools cheats

Written to a SEPARATE file, `cw4dev-commands.txt`, not the Archipelago one:

    set:instantbuild=on        spawned units finish; without it they are ghosts
    set:infiniteresources=on   nothing waits on energy
    set:indestructible=on      nothing under test dies mid-measurement
    set:allbuildings=on        replaces a dozen "item:" grants and their popups

`instantbuild` is not a nicety. A spawned unit without it is a blue outline: a
real UnitManager with a real MAX_AMMO that is not a weapon. An ammo-refill effect
once reported "filled 5 weapons, 54 ammo added" against five of them.

### 5. Spawning uses DATA NAMES, not build-pane keys

This project has four name spaces and this is the one that keeps biting:

| you want | build-pane key | spawn key (data name) |
|---|---|---|
| rift lab | `riftlab` | **`commandbase`** |
| ERN port | `ernportal` | **`erninterface`** |

`spawn:riftlab` reports `0/1 placed` and carries on. Always assert:

    case "$line" in
      *"$want/$want placed"*) ;;
      *) echo "FATAL: did not place"; exit 1 ;;
    esac

A silent 0/1 on the rift lab is what invalidated an entire ERN measurement run:
no base means the mission never starts.

### 6. Placing a base is not starting the mission

CW4 holds the sim paused with a pause owner called `main`. `sim:run [speed]`
clears every owner and sets the game speed. Until then `tickCount` does not move,
ERNs fly forever without arriving, and a frozen game reads exactly like a slow
one. Prove it rather than assume it:

    T1=$(ticks); sleep 6; T2=$(ticks)
    [ "$T1" = "$T2" ] && { echo "FATAL: sim not advancing"; exit 1; }

`sim:run 3` also makes long ramps three times faster in wall clock. Ratios between
measurements are unaffected.

**Unpausing once is not enough - use `sim:hold on`.** Opening the A.D.A. log
pauses the sim, and a mission keeps firing story messages that open it, so a run
that unpauses at the start drifts back into being paused partway through. Two
attempts were lost to reordering `sim:run` against `clear_ui` before the real fix
landed: `sim:hold on` force-clears the pause owners every frame and holds until
`sim:hold off` or `sim:pause`.

The symptom is deliberately hard to spot - `tickCount` kept advancing while
`GameSpace.paused` read true - so print both, and trust the flag.

### 7. The A.D.A. panels come back

`ada:close` at boot closes nothing, because the story panels appear once the
mission is under way. Clear them AFTER starting, and again before any screenshot:

    ada:close        closes the A.D.A. Log window
    ada:clear        ClearAllRevealedMessages
    ui:hide <text>   disables the object owning a piece of on-screen text

`ui:text [filter]` lists every visible UI string with its object path, which is
how to identify a panel instead of guessing at type names in the assembly.

## Acknowledgements must be scoped to the command

Waiting for a log line by grepping the WHOLE log only works for the first command
of its kind. Every repeated command after that matches the previous run's line and
returns instantly, so the read that follows finds nothing.

That is how a live ramp got reported as `eff=0`: the poll sent `ern:dump`, matched
an older dump, read no new output, and the empty string was coerced to zero. The
setup steps looked fine because each of those commands ran once.

Mark the log position BEFORE writing the command, and search only after it:

    send() { mark; printf "%s
" "$1" > "$CMD"; wait_since "$2"; }
    wait_since() { for i in $(seq 1 25); do since | grep -qa "$1" && return 0; sleep 1; done; return 1; }

This is the same shape as the stale-archive bug in `docs/randomizer-audit.md`:
a check that passes on data left over from the previous iteration.

## Verify the FILE, not the success message

A patch script that prints "patched" has proved what it did in memory, not what
is on disk. Twice a patch was reported as applied and the running script was the
old one - once because a stopped task's `cat > file` heredoc completed AFTER the
edit and quietly restored the previous version, wiping two separate fixes.

After editing, grep the file for a string only the new version contains:

    grep -c "PLATEAU" tools/ern-upgrade-test.sh    # expect 1, not 0

and confirm no other task is alive that could rewrite it.

## Take screenshots

`shot:` writes a PNG (default `<game>/ap_shot.png`), and it costs one command.

An hour of ERN measurement ran behind a full-screen A.D.A. Log modal - no map, no
units, nothing visible - while the log output read like a plausible slow ramp. One
screenshot showed it immediately. **A log is not an observation.** Take a shot at
the start of a run, after the setup, and whenever a number looks surprising.

## "It reported success" is not "it worked"

Two examples from one afternoon, both of which read as green:

- `ada:clear` logged "1 story panel(s) hidden" while the banner the designer had
  complained about twice was still on screen. It had hidden a DIFFERENT panel.
  The owner sits four levels above its text (`MessageArea/ControlRow/Buttons/
  Button/Text`) and the parent-walk stopped one short.
- A patch script printed "patched" while the file on disk was still the old
  version, because a stopped task's heredoc rewrote it afterwards.

A count, a log line, and an exit code all describe what the code THOUGHT it did.
For anything with a visible result, check the visible result.

## Driving the game unattended

Prefer a debug command over asking a human to click. `ern:assign <slot>` calls the
game's own `AssignERN` and removes six drag-and-drops plus several minutes of ramp
from every iteration. If a test needs a human step, that step will be skipped and
the test will rot.

## Two ways a run silently corrupts itself

- **Editing a script bash is executing.** Bash reads a script by byte offset, so
  an edit mid-run resumes at garbage: "unexpected EOF" from a file that passes
  `bash -n`. `TaskStop` first, then edit.
- **Killing the game does not kill the script.** A run whose game has been killed
  keeps writing to the shared command file, and those commands land in the NEXT
  session - stray creeper, cleared ammo, a spawn overwritten mid-flight. One such
  zombie caused a `FATAL: cannon did not place` in an unrelated run. Stop the
  task, then check nothing else can write to `cw4ap-commands.txt`.
- **`TaskStop` does not reliably kill the bash child either.** A stopped sweep
  went on running for another ten minutes: its `ern:assign 1` landed in the
  middle of the REPLACEMENT run's spawn phase and made a healthy run report
  `FATAL: 'ern' did not place`, and both runs appended to the same redirect file
  so the output interleaved. A sweep of the process table then found zombie
  harnesses from several EARLIER SESSIONS still alive.

  Two defences, both now in `tools/ern-all-upgrades.sh`:

      guard_command_file    write a sentinel to cw4ap-commands.txt before
                            launching, sleep, and abort if anything overwrote
                            it - a direct test of the actual hazard

  and give every run its own output file rather than reusing one name, so a
  straggler's output can never be mistaken for this run's.

  To audit by hand:

      Get-CimInstance Win32_Process -Filter "Name='bash.exe'" |
        Select-Object ProcessId, CommandLine

## Reading state back

| command | reports |
|---|---|
| `ern:dump` | per-slot efficiency from BOTH accessors, docked timestamp, plus `paused` / `tickCount` / `GAME_SPEED` |
| `ern:erns` | every ERN: state (WAITING, BURIED, MOVING_TO_ASSIGNMENT, DOCKING, DOCKED, PARKING), available, docked |
| `ern:stats` | the observable each of the six upgrades should move, for a before/after diff |
| `upgrade:units [filter]` | per-unit MYRANGE (effective) and RANGE (base), build cost, ammo, ernDocked |
| `trap:status` | player unit count, units with ammo, energy, emitters |
| `ui:text [filter]` | visible UI strings and their object paths |

Sim state is printed on every `ern:dump` deliberately: a measurement without it
cannot be distinguished later from one taken while paused.

## Things that are timestamps, not counters

`ERNInterface.dockedTimes[i]` is the tick the ERN docked, and efficiency is
`(tickCount - dockedTimes[i]) / EFFICIENCY_TIME`. It does not tick upward, and an
implementation that adds to it each frame both never fires and, if it did, would
slow the ramp down. Verified in game: efficiency rose 0.219 to 0.494 across 990
ticks, exactly 990/3600, while `dockedTimes` never moved.

Before writing to a game field on a schedule, check whether it is an accumulator
or a stamp. The names do not tell you.

## Patch the accessor the SIM reads, not the one your probe reads

`ERNInterface` exposes efficiency twice, and the pair is easy to miss because the
names are near-synonyms:

    GetEff(int)          instance, one port    <- the port's UI
    GetEfficiency(int)   STATIC                <- what the units read

The ceiling item was patched onto `GetEff` only. Every probe also called
`GetEff`, so the boost looked like it worked perfectly - efficiency went to 2.0
on command, reproduced four times - while the sim went on calling the untouched
static and no cannon's range ever moved. The measurement was self-confirming: it
read back exactly the value it had written.

**A test that only reads the value it patched proves the patch applied, not that
the game uses it.** Find every accessor for a quantity before patching any of
them, and log them side by side so a disagreement is visible. `ern:dump` now
prints both.

## Effective values are declared per type, not on the base class

`UnitManager.RANGE` is the BASE range and is constant by design. The effective
value is `MYRANGE`, and it is declared separately on `Cannon`, `Mortar`,
`Sniper`, `MissileLauncher`, `Sprayer`, `Terp`, `Nullifier` - there is no shared
base-class property, so one `TryCast` finds nothing and a probe written against
`CModUnitManager` silently reports `n/a` for a cannon.

That combination produced a wrong conclusion twice over: the probe printed no
effective range, the test fell back to `RANGE`, and the run reported "the Fire
Range upgrade does nothing" from a number that could not have changed. Reading a
ladder of casts is ugly and correct.

**When an upgrade appears to do nothing, first prove the observable you are
watching is capable of moving at all.**

## Finding the right member without guessing

Guessing member names against a 40 MB IL2CPP assembly does not converge. Reflect
over the interop assembly's metadata instead - no game launch, no execution:

    MetadataLoadContext + PathAssemblyResolver over BepInEx/interop/*.dll

`tools/reflect` does this. It takes a needle and prints `STATIC`/`FIELD`/`PROP`
with the declaring type:

    dotnet run --project tools/reflect -- GetEff
    dotnet run --project tools/reflect -- MYRANGE
    dotnet run --project tools/reflect -- Cannon type

Listing every method matching `GetEff` found the static/instance pair in one
command, after an afternoon of guessing had not. Same for `MYRANGE`, which showed
immediately as a per-weapon-type property.

Do NOT try this with `Assembly.LoadFrom` in PowerShell - the interop assemblies
pull in a resolve cascade that stack-overflows. Metadata-only loading has no such
problem because nothing is executed.

## Make only the units under test fatal

`require_spawn` correctly stops a run when the unit being MEASURED is missing.
Applying it to a nice-to-have unit is a different failure: a wrong data name on a
supporting unit killed a 20-minute sweep at setup, after the game had booted
perfectly. Supporting units get `optional_spawn`, which warns and lets their
observable read `n/a`.

## Traps and boons have DIFFERENT admission rules

A trap that whiffs feels broken. The player spends a check, receives a
punishment, nothing happens, and the whole trap pool starts to feel suspect -
which is why Emitter Overdrive was pulled from the pool for being dead on
roughly a third of the campaign.

A boon that whiffs is a shrug. Designer's ruling, verbatim: *"needs a factory
down, but if they don't have one it just wiffs, which is fine."*

So a boon may depend on infrastructure the player might not have. What it may
NOT do is whiff for a reason the player cannot see or act on. That distinction
killed the three per-ware resource items: a factory only holds wares the MISSION
gives it a channel for, so "Bluite Cache" would have been structurally dead on
most maps with nothing the player could do about it. One "Resource Cache" that
grants whatever the factory can hold pays out wherever a factory exists.

Corollary for the logs: **an effect that whiffs must SAY WHY.** A silent
"0 -> 0" is indistinguishable from a broken write - that exact line appeared for
three wares before the channel check was added, and it read like a bug.

## Read the thing that should NOT move, too

Two effects were doing more than they advertised, in opposite directions, and
neither was visible from its own log line:

    Ammo Resupply   also filled the RIFT LAB, whose "ammo" is the energy store,
                    so it was a silent free full-energy refill
    trap:drain      the same bug with the opposite sign - it emptied the whole
                    economy to zero, which "Ammo Drain" does not claim

`"filled 5 weapons, 54 ammo added"` was true and complete while the effect was
also refilling the entire economy. A test that checks only the advertised
quantity confirms the advertisement, not the behaviour.

Worse, the obvious check does not settle it either: energy MOVES ON ITS OWN as
weapons draw packets, so the store falling after a drain is equally consistent
with the bug and with normal play. The fix was to make the filter report itself
- `[energy stores skipped: 1]` can only be non-zero if the filter ran. When a
quantity has other reasons to move, measure something that can only mean one
thing.

## A wiring edit that reports success is not a wiring edit

`FireBoon` sat with NO CALLERS for an entire session while 63 of 236 pool items
did nothing. The edit meant to add its branch was a string replace whose anchor
did not match, and the script printed "boon dispatch wired" anyway.

Two habits, both cheap:

- **After wiring anything, read the file back** - not the tool's success line.
- **Make the edit fail loudly.** A helper that exits non-zero when the anchor is
  missing turns this whole class of bug into an immediate stop. `sed`/`replace`
  silently doing nothing is the hazard; the Edit tool erroring is the fix.

And when one silent failure is found, **re-verify every other wiring claim from
the same session**, because the same script wrote them all.
