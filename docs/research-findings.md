# Creeper World 4 + Archipelago: Feasibility Research

Date: 2026-08-24. All facts below were verified against the installed game
on this machine, not recalled from documentation.

## Environment

- Game install: `G:\Games\Steam\steamapps\common\Creeper World 4`
- User data:    `C:\Users\droha\Documents\My Games\creeperworld4`
- Unity 2019.4.23f1, **IL2CPP** (`GameAssembly.dll`, 42 MB)
- `global-metadata.dat`: 11.8 MB, magic `af1bb1fa`, version 24, **unencrypted**

## 4RPL (the official scripting language)

698 commands total. Scanned the complete command database.

- **No file read. No network. No sockets. No HTTP.**
- `Print` writes to `RPL.txt` in the game root, live, truncated on map load.
  This is the only outbound channel from a running map.
- `GetMCSEntries` / `DeleteMCSEntry` read and modify `mcs.dat`.
- `SendMVerseMsg` + `RegisterForMSG` carry arbitrary data over CW4's
  multiplayer layer. Real bidirectional networking, but the wire protocol is
  proprietary and undocumented. Rejected as an option.

### Unlock primitives

`SetUnitCanBuild(unitType, bool)` and `SetUnitBuildLimit(unitType, n)` work at
runtime and drive the build pane directly. The 15 buildable types:

    riftlab  factory  ernportal  tower  pylon  miner  greenarrefinery
    terp  porter  cannon  mortar  sprayer  sniper  missilelauncher  nullifier

Mission flow: `AcquireMissionObjective`, `SetMissionObjectiveEnabled`,
`SetMissionObjectiveRequired`, `IsMissionComplete`, `EndMission`.

## File formats (all reverse-engineered and confirmed)

Every CW4 data file is `[uint32 LE uncompressed size][gzip stream]`.
Inside is a tagged binary tree:

| Tag    | Meaning | Payload |
|--------|---------|---------|
| `0x0A` | node    | uint16 name length + name |
| `0x01` | int32   | uint16 name length + name, then 4 bytes |
| `0x03` | string  | uint16 name length + name, then uint16 length + data |

Applies to `.cw4` maps, `mcs.dat`, `achievements.dat`.
`LastMetaData` is plain gzipped XML.

**4RPL scripts are stored as plain UTF-8 text inside `.cw4` files**, under
nodes named like `Player.4rpl`. So Colonies maps can be read, injected, and
rewritten programmatically.

## The finding that decided the architecture

**Official campaign maps are not loose files.** They are baked inside
`CW4_Data/data.unity3d`, a 391 MB UnityFS bundle. No `.cw4` files exist in the
install directory.

Since the project targets the official campaign only, script injection into
map files does not apply. That removes 4RPL as the primary mechanism and
points to a BepInEx plugin instead.

## Chosen architecture

- **Python apworld** - item/location definitions, logic, generation.
  Required; this is Archipelago's core and cannot be another language.
- **C# BepInEx IL2CPP plugin** - hooks the game directly and opens a
  websocket to the Archipelago server via `Archipelago.MultiClient.Net`.

No third-party launcher, no `RPL.txt` tailing, no file IPC, no synthetic
keystrokes, no focus stealing. The BepInEx bundle ships
`System.Net.WebSockets.dll`, so the plugin can talk to AP directly.

## Prior art

None. No Creeper World Archipelago apworld, randomizer, or BepInEx plugin
exists publicly. This would be the first.

## Useful symbols found in game metadata

`ernPortalAvailable` (with `get_`/`set_`), `buildErnPortal`, `SetBuildLimit`,
`MissionPanelUnlock`, `GalaxyMissionPanel`, `mission21`, `CompleteFarsiteStory`.

## Open questions

- Which campaign missions require which units (needed for AP logic spheres).
- Totems and pre-placed structures are outside `SetUnitCanBuild`; deferred.
- ERN portal as unlock plus individual ERNs as progressive items: needs
  investigation of how ERNs are represented.

## Probe result (2026-08-24): VALIDATED

A throwaway BepInEx plugin (`cw4-probe/`) proved the core mechanism live:

- Whitelisted riftLab/tower/pylon/cannon, locked all other 22 types, enforced
  every frame in `MonoBehaviour.Update`.
- On a late story mission (full tech tree normally), the build pane collapsed
  to exactly the whitelist. Locking works.
- Cannon was forced available by code and appeared in the weapons tab even
  where the mission would provide it - forcing units ON works too.
- Zero errors; mission scripts could not fight the plugin.

### Key API surface (interop `Assembly-CSharp.dll`)

- `GameSpace.instance` (static) -> `.buildUnitManager` -> `BuildUnitManager`
- **26 availability flags** (not just the 15 4RPL exposes):
  riftLab factory ernPortal tower pylon miner greenarRefinery terp porter
  cannon mortar sprayer sniper missileLauncher nullifier runway bomberPad
  acBomberPad rocketPad platform shield microRift chronat airship bertha
  sweeper - each as `<name>Available` bool property.
  The extra 11 are the special per-mission units (bertha, airship, bombers...)
  which means they can be items too.
- `BuildUnitManager.SetBuildCountLimit(string, int)` / `GetBuildCountLimit` -
  build limits as progressive items confirmed possible.
- `UnitBuildPane.Refresh()` (find via `FindObjectOfType`) rebuilds the pane;
  the pane is sectioned (structures vs weapons tabs).
- `GameSpace.editMode` (static) - skip the map editor.
- New map load detected by comparing `GameSpace.instance.Pointer`.

### Build notes

- .NET SDK 10.0.400 builds the `net6.0` plugin fine.
- References: `BepInEx/core/{BepInEx.Core,BepInEx.Unity.IL2CPP,Il2CppInterop.Runtime}.dll`
  plus `BepInEx/interop/{Assembly-CSharp,Il2Cppmscorlib,UnityEngine.CoreModule}.dll`.
- Injected MonoBehaviour needs `ClassInjector.RegisterTypeInIl2Cpp<T>()` and a
  `public T(IntPtr) : base(ptr)` constructor.
- csproj has an AfterTargets=Build copy straight into `BepInEx/plugins`.
- Game ships `Mirror.dll` (MVerse is open-source Mirror) and `BestHTTP.dll`.

### Deployment target

End state must be: download a zip, extract into the game folder, done.
BepInEx installs are pure file-copy, so ship BepInEx + plugin together.
First run generates interop assemblies automatically (takes ~1 min).

## Probe v0.11 (2026-08-24): programmatic mission launch VALIDATED

Full hands-free chain works: game launch -> autoboot command -> mission
running with locks enforced. Zero UI interaction.

### The correct launch API

    GameSpace.specifierToApply = "story7";  // also titleToApply, guidToApply=""
    LoadingScreen.LoadGame("story7", true, false, GameSpace.CATEGORY.FARSITE, -1);

- Story missions are internally "story1".."story20" (tutorial not counted;
  story7 = "Hints"). Mission title mapping observable via OnLaunch hook.
- Scene flow: GameLoad (init) -> Galaxy (menu) -> LoadingScreen -> Game.
- LoadingScreen has its own statics (fileToLoad/embeddedLoad/category/...)
  and an async load coroutine; LoadGame() is the static entry the UI uses.
- Booting on Galaxy's first frame crashes natively; waiting ~10s after the
  Galaxy scene arrives works. Readiness signal TBD.

### Crash lessons (cost several hours - do not repeat)

1. NEVER call SceneManager.LoadScene("Game") directly with GameSpace statics
   set - silent native crash during world creation. Use LoadingScreen.LoadGame.
2. NEVER Harmony-patch BuildUnitManager.ReadData - calling the 26 availability
   setters during map deserialization kills the process with no managed
   exception, no crash log. This broke ALL mission loads (manual included).
3. Harmony prefixes on UnitBuildPane.OnEnable/Start throw NullReferenceException
   spam from the DMD at boot. Unity lifecycle methods on this type do not
   detour cleanly. Patching GalaxyMissionPanel.OnLaunch/OnPlay works fine.
4. Diagnostic that cracked it: manual launch crashing too = the passive hook,
   not the launch path, was guilty.

### Build pane behavior (confirmed)

- BuildButton visibility is DYNAMIC (each button re-checks flags per frame):
  live LOCKING works instantly.
- Button CREATION is static at pane build: live UNLOCKING needs a pane
  rebuild - UnitBuildPane.Refresh() is NOT enough. Rebuild mechanism TBD
  (candidates: SetEnabledButtons, gameObject toggle, Show; test via pane:
  commands in probe v0.11).
- Units available at pane-creation time show correctly (mortar test passed
  after mission restart).

### Probe file-command protocol (BepInEx/probe-unlocks.txt)

  <unitname>      add unit to whitelist live
  lock:<unit>     remove unit from whitelist live
  reset           restore default whitelist
  load:<name>     launch via GalaxyMissionPanel.OnLaunch (NRE from main menu)
  boot:<name>     launch via LoadingScreen.LoadGame (works)
  autoboot:<name> queue boot for 10s after Galaxy scene arrives
  pane:<cmd>      refresh|setenabled|onenable|start|toggle|show experiments
  dump            log every BuildButton state

### Launch/focus notes

- Game window spawned from background shell does not take focus; fix with
  WScript.Shell AppActivate('Creeper World 4') ~12s after launch.
- The launching shell exits immediately (Steam-style detach); watch the
  process list or BepInEx log, not the launcher exit.

## Crash investigation round 2 (2026-08-24 late)

The v0.11 "success" report was premature: the mission reached StartMission
(Player.log: "Unpersist time: NN" then "StartMission") then the process died.
Manual launches crashed identically, so the boot path was NOT the cause.
Quarantining the story7 autosave did not help either.

**Real evidence from minidumps** (C:/Users/droha/AppData/Local/CrashDumps,
parsed with python `minidump` package): 7 of 8 crashes tonight are
EXCEPTION_STACK_OVERFLOW with ~19,900 return addresses into a single
non-module JIT-code region, coreclr.dll/clrjit.dll frames present. That is a
managed method (our plugin is the only managed code in-process) recursing to
stack exhaustion during StartMission.

Binary-search in progress: v0.12 = v0.11 minus ALL Harmony patching.

Analysis recipe for future crashes:
    py -3.13 -m pip install minidump
    MinidumpFile.parse(dump) -> .exception.exception_records[0]
    scan crashing thread stack for values inside module ranges;
    heavy repetition of one non-module region = managed recursion.

## RESOLUTION (2026-08-24 ~22:00): all probe goals achieved

### The crash: cause isolated to code structure

Byte-exact v0.3 redeploy WORKED while bisect variants crashed. The only
actively-executing diff in the crashing builds: the whitelist was made a
static field and a static ApplyTo() method was added ON THE IL2CPP-INJECTED
MonoBehaviour class. Fix that ended the crashes: extract ALL state and logic
into a plain (non-injected) static class ProbeCore; the injected
ProbeBehaviour is a one-line Update() shim.

**RULE: IL2CPP-injected classes get an (IntPtr) ctor and Unity messages,
NOTHING else. No static state, no helper methods, no logic.**
(Mechanism unconfirmed - correlation was decisive across 8+ runs. Crash
signature: EXCEPTION_STACK_OVERFLOW, ~20k frames in one JIT region, dies at
StartMission during mission load.)

Cleared suspects: LoadingScreen.LoadGame (fine), autosave corruption (red
herring), scene tracking per frame (fine), Harmony patches on
GalaxyMissionPanel (fine).

### Live unlock: SOLVED including button creation

- Locking: flags checked per frame by BuildButton - instant.
- Unlocking mid-mission: set flag, then UnitBuildPane.SetEnabledButtons()
  CREATES missing buttons, then Refresh(). Confirmed visually: mortar
  removed and re-added live, mid-mission, no restart.
- GetBuildButtons() returns only the ACTIVE TAB's buttons (structures vs
  weapons are separate button sets).

### No-flash (v0.15)

Hide pane GameObject at first sight in a mission, wait 60 frames while
enforcement clamps the mission's own grants, SetEnabledButtons + Refresh +
SetActive(true). Avoids the crash-prone lifecycle/deserialization hooks.

### Autoboot timing

Game boot to menu ~20-30s (unavoidable). Our settle delay at Galaxy before
LoadGame: 10s, conservative, shrinkable later; only needed for automated
testing - real players launch from the (AP-gated) galaxy UI.

## FINAL PROBE STATE (v0.16) - everything verified

- There are FIVE UnitBuildPane instances per mission: StructUnitBuildPane,
  WeaponUnitBuildPane, AirUnitBuildPane, SpecialUnitBuildPane,
  CustomUnitBuildPane. ALL pane operations must iterate all of them
  (Resources.FindObjectsOfTypeAll). The v0.15 miner/refinery leak was caused
  by rebuilding only all[0].
- flags diagnostic (actual vs wanted per unit): all 26 matched, no write
  battle from mission scripts - per-frame enforcement wins cleanly.
- Visual verification is self-service: PowerShell CopyFromScreen screenshot
  (scratchpad/screenshot.ps1) + Read the PNG. No human eyes needed for pane
  checks.
- Verified via screenshot: mission start shows ONLY whitelisted units.

### Probe command protocol v0.16 (BepInEx/probe-unlocks.txt)
  <unit> | lock:<unit> | reset | boot:<name> | autoboot:<name>
  pane:refresh|setenabled|toggle|show | dump | flags

### What the real mod inherits from the probe
- ProbeCore structure (thin injected shim + plain core class)
- Whitelist enforcement loop + AllPanes rebuild for live item delivery
- LoadingScreen.LoadGame for mission gating/launch
- The AP client replaces the file-command channel with the websocket

## UI refresh recipe - FINAL (v0.20, user-verified)

The five UnitBuildPanes share one physical button strip, managed by LeftPane
(fields: structUnitBuildPane..customUnitBuildPane, structTab..customTab
toggles, RefreshUnitBuildPanes(), PickActiveTab(), HideAll()).

- On mission reveal (after no-flash hide): SetActive(true) on all panes,
  LeftPane.RefreshUnitBuildPanes(), then force a REAL tab-change cycle
  (weaponTab.isOn=true; structTab.isOn=true). PickActiveTab alone does NOT
  rebuild the strip when the default toggle is already on (no change event),
  which left the weapon pane's buttons visible on the struct tab.
- On live item change: LeftPane.RefreshUnitBuildPanes() ONLY. Never
  PickActiveTab - it yanks the player's selected tab.
- Only unit changes trigger a refresh; tab/ada commands must not.
- ADA log dismissal: Resources.FindObjectsOfTypeAll<ADAMessageLog>()[0].Close().
- Tab switching: LeftPane.<name>Tab.isOn = true.
- Autoboot settle delay: 4s after Galaxy scene is stable (10s was overkill).
  TODO: replace timer with a menu-readiness signal.

Verified end-to-end by automated screenshot sweep (scratchpad screenshots) and
by the user: initial load clean, live unlock lands in correct tab, no leaks,
no tab yank, no flash.

## CORRECTION + full test matrix (v0.21-v0.23)

**Correction to the "shared button strip" theory:** wrong. The five
UnitBuildPanes are separate stacked GameObjects in the same screen space; the
game keeps exactly ONE active (the selected tab's). Panes with zero buttons
render nothing even when active, which masked the bug: our reveal had
activated all five, so as soon as other panes gained buttons they drew on top
of the struct tab (and produced overlapping-button artifacts). Invariant to
maintain: after any pane manipulation, SetActive(true) only on the pane
matching the selected tab toggle (ResyncStrip in probe v0.23).

### Test matrix results (probe v0.21-v0.23, all verified)

- All 26 availability flags: unlocked live; buttons appear on correct tabs
  (struct: Tower/SuperTower/Miner/Reactor?/GreenarRefinery/Terp/DeliveryPad...,
  weapon: Cannon/Mortar/Sprayer/Sniper/MissileLauncher/Nullifier, air:
  Runway/BomberPad/ACBomberPad/RocketPad+custom, special:
  Platform/Shield/Microrift/Beacon(Chronat)/Bertha/Sweeper). Extra buildables
  exist beyond the 26 flags: Reactor, DeliveryPad, and mission-embedded CPACK
  units with GUID names - relevant to item pool design.
- Mixed enable/disable combinations: pane counts track exactly, flags never
  fought by mission scripts (flags diagnostic clean).
- Build limits: BuildUnitManager.SetBuildCountLimit/GetBuildCountLimit work -
  unit names must be LOWERCASE ('tower', 'cannon'; 'Tower' fails silently,
  readback -1). UI badge + behavioral cap still to verify in unpaused play.
- Mission gating: allowed-set + Harmony prefix on GalaxyMissionPanel.OnLaunch
  returning false blocks launch; BootMission also gated. Verified:
  boot:story2 denied while story1/story7 allowed.
- LOCATION TRIGGERS (the AP send side): World (via GameSpace.instance.world)
  exposes missionObjectives[] (customName, required, complete),
  IsMissionObjectiveComplete(i), IsMissionComplete(),
  AcquireMissionObjective(i, showPopup). Per-frame polling detects
  transitions reliably; objective popup + rift-jump availability confirmed
  in-game. Programmatic 'win' (acquire all) works -> MISSION COMPLETE fires.

### Probe commands added in v0.21+

  limit:<unit>:<n>   set build count limit (lowercase names)
  missions:<csv>|all mission launch gate
  objective:<n>      acquire objective n
  objdump            list objectives with required/complete
  win                acquire all objectives

### Corrections and panel-flag results (v0.23 final)

- 'Reactor' and 'DeliveryPad' are INTERNAL names for the MINER and PORTER
  buttons (display names differ). No extra buildables beyond the 26 flags
  plus mission-embedded CPACK units. Struct tab = exactly 6 buttons fully
  unlocked; no scroll overflow.
- factory and ernportal do not use pane buttons: each has a DEDICATED panel
  next to the build pane (Factory ware rows / ERN PORT avail+buried).
  Verified live in both directions: locking removes the panel, unlocking
  restores it, no restart, no artifacts.

Remaining to verify in actual (unpaused) play, one session covers all:
build-limit cap enforcement while building, mission gating via a real galaxy
click, victory/depart sequence and gameComplete transition.

### Real-play validation (user beat story7 with probe active)

Full authentic sequence captured: 4 individual objective triggers in
completion order (2 optional + 2 main), MISSION COMPLETE on the required
pair, then SCENE Game->Galaxy on rift jump (depart detectable). Whitelist
state persisted across missions (story7 -> story6) - correct AP semantics.
Note: the mission gate and build limits are in-memory plugin state and reset
on game restart; the real mod repopulates them from the AP server on connect.

## COMPLETE: full behavioral validation (2026-08-24 end of session)

User-verified in real gameplay:
- Build limit cap ENFORCED: limit:tower:3 showed a badge and the game refused
  the 4th tower.
- Mission gate blocks the REAL UI path: restart-mission clicks on story6
  denied 5/5 via the GalaxyMissionPanel.OnLaunch Harmony prefix.
- Victory flow: 4 objectives triggered individually in real play, MISSION
  COMPLETE fired, rift jump detected as SCENE Game->Galaxy.

Gaps found by user testing (real-mod TODO, not probe scope):
1. GATE BYPASS: loading an existing SAVE of a gated mission skips OnLaunch -
   must also gate the save-load path (MissionPanelLoadBox) or clear stale
   saves per AP slot.
2. Gated missions should be GREYED OUT on the galaxy map, not just refuse to
   launch (StorySelectionPanel / mission marker UI work).
3. Reveal ordering bug fixed in v0.24 (queued): panes must be active during
   refresh or buttons never build; enforce single-active only afterwards.

PROBE COMPLETE. Every mechanism the Archipelago mod needs is proven:
unlocks (26 units + factory/ernportal panels), live delivery both directions,
build limits, mission gating, location triggers (objectives + completion +
depart), programmatic mission launch, full state persistence across missions.
Next milestones: AP websocket client in the plugin, galaxy UI lock styling,
save-path gating, Python apworld with mission/unit logic spheres.

## Blank struct tab: SOLVED (2026-08-25, probe v0.30, battery 13/13)

The blank-pane-on-mission-entry bug had THREE stacked root causes:

1. **Tab toggles are NOT in a Unity ToggleGroup.** The game's click handler
   manages exclusivity. Setting toggle.isOn from code leaves multiple toggles
   true and fires no exclusivity logic. Fix: clear ALL five toggles to false,
   then set the target true (a real false->true change event).
2. **Il2Cpp wrapper reference equality is ALWAYS false.** Every interop
   property access returns a fresh wrapper, so `paneA == paneB` never matches
   even for the same native object - our single-active enforcement was
   deactivating every pane including the target. Fix: compare `.Pointer`.
3. **The game hides the whole pane container while the ADA log is open**, so
   `activeInHierarchy` reads false during mission intros regardless of our
   state. Fix: verify with `activeSelf`.

Plus robustness: the reveal is now a multi-frame state machine (activate ->
refresh -> toggle-cycle -> single-active resync -> VERIFY -> retry up to 5x),
and all UI objects resolve through `GameSpace.instance.leftPane` (Resources
scans can return destroyed instances from the previous mission after a
LoadGame transition - liveness-check everything).

**Regression battery** (scratchpad/battery2.sh): 13/13 pass - three mission
entries including mission->mission transitions, four whitelist combos, limit
readbacks, state persistence across missions, flag-fight scan, zero errors.
Log-marker sequencing per boot (the log truncates on game relaunch - reset
markers when the file shrinks).

## Full-campaign sweep (2026-08-25, probe v0.30): 20/20 CLEAN

Booted story1..story20 back-to-back in one session. Every mission:
reveal=OK, struct pane active+visible with exact whitelist (2 buttons),
ZERO flag fights - no story mission script contests the whitelist anywhere
in the campaign. The plugin has uncontested authority over unit
availability in all 20 missions.

Objective slots per mission: always 6. Required counts:
  story1:1 story2:3 story3:2 story4:2 story5:2 story6:1 story7:2 story8:1
  story9:2 story10:2 story11:2 story12:2 story13:2 story14:2 story15:3
  story16:2 story17:1 story18:1 story19:2 story20:4
(Locations table seed: enabled-objective flags per mission still to capture;
MissionObjectiveData.enabled exists.)

Still untested (need human clicks or future battery):
- Resume-from-save path (saves serialize BuildUnitManager state; also the
  known gate bypass)
- Pause-menu restart path (last exercised pre-reveal-machine)
- limit:0 semantics (unit owned but unbuildable - possible AP item design)

## Pane system: CLOSED (2026-08-25, probe v0.31)

- v0.31 hides panes at Game SCENE ENTRY (before GameSpace.instance exists),
  which kills the resume-from-save flash of stale serialized flags. Reveal
  delay halved to 30 frames.
- Regression battery 13/13 on v0.31.
- User-verified: fresh boots clean, restarts clean, resume-from-save clean
  (brief blank, then correct whitelist - no stale flash).
- limit:0 rejected by design (unintuitive); build limits are >=1 or absent.

Pane/unlock system is DONE. Next: Archipelago websocket client.

## Mission-select tracker research (2026-08-25, probe v0.32-v0.40)

### Campaign facts
- Full campaign = story0 (tutorial) + story1..story20; STORY PLANET_COUNT=20.
- SPAN experiments not in embedded story assets; CATEGORY.SPAN exists (stretch).

### Save-load gate CLOSED
Harmony prefix on MissionPanelLoadBoxRow.OnLoad reading
missionPanelLoadBox.specifier - same gate check as OnLaunch. (v0.32)

### Programmatic navigation
GameGalaxy.instance.farsiteButton -> GetComponent<Button>().onClick.Invoke()
opens the story sector screen from the main menu ("story:open").

### Mission-select anatomy (Sector: Farsite screen)
- StorySector: planets carousel; 20 'Planet (n)' bare meshes
  (StoryPlanetMaterial n), objectives array = 6 Image slots for the DETAIL
  panel (sprites Icon_Magic1/Money1/PieChart/Time/Diamond/Terror), all
  Image.color tintable.
- Overview per-planet markers = 63 SpanNetworkPlanetObjective instances
  (fields: objective type int, complete bool) under 'Objectives'; each a
  quad mesh. complete=true -> green material, false -> white material
  (SpanNetworkPlanetComplete<N>Material, Shader Forge/
  SimpleTextureTransparent, NO color property - color baked in texture,
  game swaps whole materials).
- GalaxyPlanet (colonies) has native STATUS enum
  {NONE,LOCKED,UNLOCKED,PARTIAL,COMPLETE} + 6 status materials - not used by
  story planets but proves the game's design language.

### Tracker recolor: PROVEN with caveat
Setting complete=false + swapping instance material shader to
Sprites/Default + .color => arbitrary colors (red/yellow/grey/blue shown
live on the map). Caveat: renders as solid squares - glyph texture sits in
a custom ShaderForge property; fix = carry texture across the swap
(GetTexture->SetTexture _MainTex) or pre-tint copies of the white texture.

### material.color lessons
- Marker/planet materials ignore .color (no such property) - silent no-op.
- The game repaints marker state per frame; one-shot flips of 'complete'
  do show, but material swaps happen on state change only.

## TRACKER VISUALS: SOLVED (2026-08-25, probe v0.42)

**Colored objective glyphs on the mission map, shapes intact, applied live:**
marker material property is `_color` (LOWERCASE - `_Color` misses silently).
`GetComponent<MeshRenderer>().material.SetColor("_color", c)` per
SpanNetworkPlanetObjective. Glyph texture lives in `_MainTexture`.
Verified on-screen: red skull, yellow I, blue I, grey X with all else green.

- Shader property discovery: shader.GetPropertyCount()/GetPropertyName(i)/
  GetPropertyType(i) - enumerate, never guess (case-sensitive).
- Connector lines: 19x 'SpanNetworkPlanetLine(Clone)' LineRenderers under
  'Lines'; startColor/endColor settable (currently green 0,0.859,0.255).
- Planet mesh shader = AmplifyStandard: NO color property; has _Contrast,
  _Emission, _Smoothness ranges (dimming candidates), textures per material.
  Planet greying still open (contrast/emission experiments, or overlay).
- Planet rings: not yet located (not sprites, not lines, not planet children).
- Live updates confirmed: all marker changes render same-frame with the
  page open; camera pan/zoom tracks correctly (world-space quads).

## Save archiving: Steam Cloud PIVOT

Archiving mcs.dat fails: Steam Cloud restores it on next launch (observed,
fresh timestamp). Design pivot: the mod OWNS the mission-select display -
AP state drives every marker color, planet state, and completeText,
regardless of mcs.dat contents; launch + save-load gates handle behavior.
No file fights, no user data loss. (If a truly clean page is ever needed,
in-game MCS deletion APIs exist - GetMCSEntries/DeleteMCSEntry - but
display-ownership makes it unnecessary.)

## MISSION MAP: COMPLETE ANATOMY (2026-08-25, probe v0.46)

The story map planets are 'SpanNetworkPlanet (0..19)' objects (the SPAN
network prefab system - story and SPAN experiments SHARE this UI; stretch
goal inherits everything). NOT StorySector.planets (that is a different,
non-map planet set - all earlier planet tints hit the wrong objects).

Per-planet subtree:
  SpanNetworkPlanet (n)   [SpanNetworkPlanet component, SphereCollider]
    Lines/SpanNetworkPlanetLine(Clone)   LineRenderer connector
    GameObject/LockedPlanet [inactive]   NATIVE locked-planet visual
                                          (SpanNetworkPlanetLockedMaterial)
    Planet                                sphere (SpanNetworkPlanet2Material)
    Title                                 TextMeshPro, color settable
    SelectionIndicator [inactive]         ring, SelectedMaterial (_color)
    CompletionIndicator [inactive]        ring, CompleteMaterial
    Objectives/SNPO(Clone) x N            objective markers

SpanNetworkPlanet API (all native):
- forceUnlocked bool, lockedPlanet, planet, title, completionIndicator,
  selectedIndicator transforms/objects
- activeLineColor0/1, inactiveLineColor0/1 (line state colors)
- completionBronze/Silver/GoldColor, incomplete/completeMaterial
- planetGUID, connectedPlanetGUIDS (the map graph)
- Refresh() - repaint this planet
- FakeIsMissionObjectiveComplete(guid, obj) - DISPLAY-TIME completion query.
  ** Harmony-patch this to answer from AP state and the whole map renders
  itself natively; call Refresh() per planet for live updates. **

Line colors verified live (red/grey shown). Save archiving verified:
byte-rename storyN->xtoryN inside mcs.dat (same length, content rewrite -
Steam Cloud syncs it instead of restoring), saves/farsite move works
(NOT cloud-restored), full round-trip proven including the game's native
fresh-campaign "?" display on clean state.

## Main menu editing + AP login panel (2026-08-25, probe v0.47-v0.49)

- Hiding menu buttons: GameGalaxy.instance has named GameObject refs
  (chronomButton, markVButton, coloniesButton, editorButton, farsiteButton,
  spanButton, recordingsButton...) - SetActive(false) works instantly.
  Verified: menu shows only FARSITE EXPEDITION + SPAN EXPERIMENTS.
- SPAN card shows "1 / 26" - confirms 26 SPAN missions for the stretch goal.
- Custom floating UI: parent a panel to the game's own root canvas
  (FindObjectsOfType<Canvas>, first isRootCanvas - 'AchievementCanvas' at
  menu). Text MUST be TextMeshProUGUI with a TMP_FontAsset borrowed via
  Resources.FindObjectsOfTypeAll<TMP_FontAsset>() - legacy UI.Text +
  Font.CreateDynamicFontFromOSFont throws interop constraint errors.
  A standalone ScreenSpaceOverlay canvas created from scratch did NOT render;
  the game-canvas parent works.
- Mock ARCHIPELAGO login panel rendered: server box, slot, password,
  CONNECT button, auto-connect, status line. Wiring = real-mod work
  (TMP_InputField for editing, Button.onClick -> AP client).

Screenshot lesson: verify CW4 actually took focus before CopyFromScreen -
AppActivate can fail and capture unrelated windows; delete such captures
immediately.

## AP login panel: INTERACTIVE (probe v0.50-v0.53, user-verified)

Full input functionality works in custom UI: typing, focus, caret,
selection, delete, copy/paste, clickable button updating status text,
password masking (TMP_InputField.ContentType.Password).

Construction rules for TMP_InputField in IL2CPP:
1. Parent to the game's root canvas (own overlay canvas won't render).
2. Text = TextMeshProUGUI + borrowed TMP_FontAsset.
3. Build the field on an INACTIVE GameObject and SetActive(true) only after
   textViewport/textComponent/placeholder are assigned - otherwise Awake
   runs unwired and the caret never renders.
4. Button wiring: onClick.AddListener((UnityEngine.Events.UnityAction)Method).
