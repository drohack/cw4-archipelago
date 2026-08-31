# AP randomizer feature comparison + recommendations (2026-08-26)

> **Status note added 2026-08-31.** The comparison table and its verdicts are a
> 2026-08-26 snapshot. Several "Ours:" rows have since been done and are marked
> DONE inline; read the rest as history, not as the current gap list.


## Purpose

Compare our CW4 Archipelago mod against seven established, officially-supported
BepInEx-based AP randomizers to decide what (if anything) we should adopt. This
is a reference + recommendation doc, kept separate from the CW4-specific logic
design ([randomizer-design.md](../randomizer-design.md)) because most of it is
about *other* games and only the "Recommendation" section is a CW4 decision.

Games surveyed (apworld + client mod each): Inscryption, DLC Quest, Hylics 2,
TUNIC, Subnautica, Bomb Rush Cyberfunk, Overcooked! 2.

## Recommendation + designer decisions (2026-08-26)

Verdicts below are the DESIGNER's calls (user, 2026-08-26), which override the
initial recommendation. "Now (non-logic)" = build/test soon, independent of the
randomizer logic. "Logic pass" = part of the mission/unit logic work. "Later" /
"Future" = deferred. "No" = not doing it.

| Feature | Verdict | Notes / reasoning |
|---|---|---|
| **Star/token gating currency** | **No (use simple mission-unlock items)** | CW4 is linear and we already open missions in a random order via "Mission Unlock: X" items. A spendable star/token currency (Overcooked! 2 style) would have to be built from scratch in CW4 and adds no real value over per-mission unlock locations - the order is randomized either way. Noted as a possible future alternative only. |
| **Logic difficulty tiers** | **Logic pass** | Covered in the logic work (casual/normal). |
| **item_name_groups / location_name_groups** | **Now (non-logic)** | Cheap, improves `!hint`/plando. Proposed groups below. |
| **Idempotent item replay (verify effect)** | **Now (non-logic)** | Verify each received item actually took effect and re-apply on reconnect; throttle a reconnect batch so it does not lag-spike. Hardens the reconnect we already have. |
| **Seed binding** | **Now (non-logic)** | Store the AP seed in the save so a save cannot be loaded against the wrong seed. (The interactive "mismatch prompt" UX is optional; the binding/guard is the part we want.) |
| **Message box: user-relevant only** | **DONE (2026-08-26)** | Box filters to messages relevant to THIS player by default (Core.MessageRelevance + ItemSend/PlayerSpecific IsRelatedToActivePlayer); a Me/All header toggle reveals everything retroactively. Verified by tools/msgfilter.sh (2-player seed) + MessageRelevanceTests. |
| **In-game text input (chat/commands)** | **DONE (2026-08-26)** | Added an always-on input row to the box: ApClient.Say sends chat and !commands; server echoes back. An InputManager.HandleInput Harmony prefix suppresses game hotkeys/wheel while the box is focused or hovered (so typing/scrolling does not drive the game). Did not build a separate client - the input row on the existing box covers it. |
| **Traps** | **SPIKED + 7 IN BUILD (2026-08-26)** | Seven effects implemented and tuned against the game's own values: **spore strike (scatter), targeted spore strike (random PLAYER building), creeper surge, energy drain, emitter burst, unit stun, weapon drain**. Only re-fog dropped (fog missions only, where lifting the darkness is the objective). Emitter burst kept with a caveat: no-ops without emitters (absent on story1/5/8 of the first 8). Numbers, the spore-targeting measurements and the coordinate/filter findings: [traps spike](2026-08-26-traps-spike.md). Still no AP wiring - trap items are a separate step. |
| **Meaningful weighted filler** | **Consider** | Replace no-op build-limit filler; depends on the ERN/build-limit decision - resolve that first. |
| **Goal variants (yaml Choice)** | **Deferred (2026-08-26)** | Low-effort once an options dataclass exists (e.g. beat M20 vs all-20). |
| **Tiered per-mission objective checks** | **Deferred (2026-08-26)** | More locations per mission; needs the per-mission objective survey. |
| **DeathLink** | **Later** | Fun, but needs a design decision on what "death" means in CW4 - restart the mission? delete all buildings but leave creeper/enemies as-is? something else. Revisit after core logic. |
| **Enemy randomization** | **Future** | Randomize which enemies/emitters a mission fields. Interesting, but a large future feature. |
| **Universal Tracker support** | **Later** | Worth it once we know more about what we are building. |
| **Player-facing piecemeal commands** | **Folded into text-client question** | Not doing a piecemeal command set; if we build the full text client it covers this. |
| **Entrance / mission-order randomization** | **No** | Not needed. |
| **Data-driven slot_data logic, Gifting/link features, co-op handshake** | **No (v1)** | Stretch/architectural; revisit only if the mod gets traction. |

### Proposed item / location groups (for review)

Item name groups (functional; refine against the logic categories in
randomizer-design.md):
- **Offense**: Cannon, Mortar, Bertha, Sniper, Missile Launcher
- **Air**: Bomber Pad, AC Bomber Pad, Runway, Rocket Pad, Airship
- **Anti-Creeper**: Sprayer, Sweeper, Nullifier
- **Economy**: Miner, Factory, Greenar Refinery, Platform, ERN Portal
- **Utility**: Terp, Porter, Shield, Microrift, Chronat
- **Bonus** (never vanilla-unlocked): Airship, Bertha, Sweeper
- **Mission Unlocks**: all "Mission Unlock: X"
- **ERN**: Progressive ERN
- **Build Limits**: the "Build Limit +1 (...)" filler
- **Units**: all unit items (umbrella group)

Location name groups:
- Per-mission: one group per mission title (e.g. "Farsite" = all its checks).
- By objective type: "Mission Complete", "Totems", "Nullify", "Collect", etc.

## Where CW4 already matches or beats the field
- **Auto-reconnect (3 retries):** most surveyed clients have NO auto-reconnect
  (BRC, Hylics 2, Subnautica rely on manual reconnect + replaying stored
  checks). We are ahead. Keep the "replay checks on connect" pattern as the net.
- **In-mission scrollable colored message box + mission-map tracker:** BRC/OC2
  only show transient toasts. Ours is on par or better.
- **Per-slot save isolation + mission gating (launch and save-load):** standard
  across the field; we have it.
- **Harmony-patch resilience (per-patch guards):** Hylics 2 and BRC have none.

## Findings by feature (reference)

### YAML options depth
- TUNIC: deepest - goal (Hexagon Quest), ability/entrance shuffle, multi-level
  trick logic (off/easy/medium/hard), fool_traps tiers, plando connections,
  presets. Grouped via `option_groups`.
- Overcooked! 2: `stars_to_win`, `star_threshold_scale`, `shuffle_level_order`,
  `location_balancing` (disabled/compromise/full), `include_dlcs`, graded
  `deathlink`, many QoL toggles - all shipped in slot_data (data-driven client).
- Inscryption: `goal` (in-order/any-order/first-act), deck/sigil randomization,
  `epitaph_pieces_randomization`, `painting_checks_balancing`, `death_link` +
  `act_1_death_link_behaviour`.
- DLC Quest: `campaign` (basic/lfod/both), `coinsanity` + bundle size,
  `item_shuffle`, `ending_choice`, `death_link`; `option_groups` for the web UI.
- Hylics 2: party/gesture/medallion shuffle, `StartLocation`, `ExtraLogic`,
  `death_link`. Subnautica: 4-way `goal`, `swim_rule` tiers, `creature_scans`,
  weighted `filler_items_distribution`, `death_link`.
- Ours: **DONE** - 18 game options now live in `apworld/cw4/options.py`
  (`missions_for_finale`, `logic_difficulty`, `starter_missions`,
  `trap_percentage` and seven trap weights, `progressive_erns`, four energy
  tunables, three filler weights). Was: empty `CW4Options(PerGameCommonOptions)`
  - only the inherited common
  options; no game-specific options yet.

### DeathLink
Present in Inscryption, Hylics 2, Subnautica, TUNIC, BRC, Overcooked! 2 (all but
DLC-less ones). TUNIC proves it can be purely client-side. OC2 grades it
(death_only vs death_and_overcook). Inscryption/Hylics 2 queue the incoming
death until a safe game state. Ours: none.

### Traps
Only DLC Quest ships real traps (Zombie Sheep, timed spikes, loading-screen,
name-change). TUNIC's fool_traps are opt-in tiers. BRC, OC2, Subnautica,
Inscryption, Hylics 2: none. Ours: **DONE** - seven trap items at
`trap_percentage` (default 50), each with its own frequency weight, fired on
receipt by `TrapApplier`. Note two shipped under different names than this doc
uses: "emitter burst" is `Emitter Overdrive` and "weapon drain" is `Ammo Drain`.

### Filler
Subnautica has weighted `filler_items_distribution`; BRC has tiered REP;
Inscryption/Hylics 2 use fixed multisets. None use per-yaml weight sliders.
Ours: **DONE** - filler is weighted across three kinds (energy storage, base
generation, build limits) by three yaml weights, and the energy pair has a
measured in-game effect. Was: build-limit items that are no-ops (no CW4 unit has a
default cap) - flagged in the logic design.

### Item/location groups, hints
TUNIC and BRC define rich `item_name_groups` (+ TUNIC location groups and hint
aliases). BRC does client-side hint scouting. Most others define none. Ours:
none.

### slot_data + Universal Tracker
All push their options into slot_data; OC2 is fully data-driven (unlock rules
and costs shipped to the client). Only TUNIC supports Universal Tracker
(`interpret_slot_data` + `ut_can_gen_without_yaml`). Ours: slot_data carries
requirement groups; no UT.

### Client robustness patterns
- Idempotent item application: Inscryption verifies each item's *effect* and
  re-applies ones that did not take (`itemsUnaccountedFor` + `VerifyAllItems`);
  DLC Quest / Subnautica keep a persistent processed-item set.
- Throttled application (~1 item / 0.2s) to avoid reconnect lag spikes
  (Inscryption, Hylics 2).
- Seed binding: Inscryption stores the AP seed in-save and prompts on mismatch;
  DLC Quest bakes seed into the save filename. BRC lacks any seed check (a
  documented gap).
- Safe-state application: Inscryption/Hylics 2 queue item/death effects until no
  animation/cutscene/pause is active.
- FailsafeCoroutine (Inscryption) wraps risky apply sequences so a mid-sequence
  exception cannot wedge the mod.
- Save isolation via Harmony save-path redirection (Subnautica `ArchipelagoSaves`
  folder; Inscryption per-save folders; Overcooked! 2 per-game save dir).
- Reconnect: most have NO auto-reconnect and lean on local-check replay; ours
  auto-retries 3x, which is ahead.
- Player commands: Hylics 2 exposes `/popups /deathlink /checked /respawn
  /airship(unstuck) /help` and `!` chat.

### Notable one-offs
- Overcooked! 2: stars-as-currency second gating axis; co-op-on-one-slot
  handshake; `location_balancing`; fully data-driven client from slot_data.
- DLC Quest: AP Gifting API + a custom "MoveLink"; graceful "fake ending"
  screen on disconnect; `AntiCrashes` defensive-patch package.
- BRC: an in-game "Encounter" app to re-trigger events early (soft-lock escape).

## Sources
apworlds under `ArchipelagoMW/Archipelago` `worlds/{inscryption, dlcquest,
hylics2, tunic, subnautica, bomb_rush_cyberfunk, overcooked2}`; client mods
`DrBibop/Archipelago_Inscryption`, `agilbert1412/DLCQuestipelago`,
`TRPG0/ArchipelagoHylics2`, `silent-destroyer/tunic-randomizer-archipelago`,
`Berserker66/ArchipelagoSubnauticaModSrc`, `TRPG0/BRC-Archipelago`,
`toasterparty/oc2-modding`.
