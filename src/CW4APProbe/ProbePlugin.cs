// THROWAWAY PROBE v0.14
//
// Structure rule learned the hard way: the IL2CPP-injected MonoBehaviour is a
// THIN SHIM. All state and logic live in ProbeCore, a plain C# static class.
// (Static state/methods added directly to the injected class correlated with
// EXCEPTION_STACK_OVERFLOW during mission StartMission; v0.3-style layout
// works. Mechanism unconfirmed - keep injected classes minimal.)
//
// File commands (BepInEx/probe-unlocks.txt):
//   <unit>          add unit to whitelist live
//   lock:<unit>     remove unit live
//   reset           restore defaults
//   boot:<name>     LoadingScreen.LoadGame launch now
//   autoboot:<name> queue boot for 10s after Galaxy scene arrives
//   pane:<cmd>      refresh|setenabled|toggle|show experiments
//   dump            log every BuildButton state

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CW4APProbe;

[BepInPlugin("com.droha.cw4ap.probe", "CW4 AP Probe", "0.56.0")]
public class ProbePlugin : BasePlugin
{
    public override void Load()
    {
        ProbeCore.Logger = Log;
        Log.LogInfo("CW4 AP Probe v0.56 (ern:grant pre-mission queue) loading");
        ClassInjector.RegisterTypeInIl2Cpp<ProbeBehaviour>();
        AddComponent<ProbeBehaviour>();
        Harmony.CreateAndPatchAll(typeof(GatePatches), "com.droha.cw4ap.probe");
        Log.LogInfo($"CW4 AP Probe loaded - watching {ProbeCore.UnlockFilePath}");
    }
}

// Mission launch gating: block missions not in the allowed set.
// (Prefixes on GalaxyMissionPanel are proven safe - v0.3 ran them fine.)
public static class GatePatches
{
    [HarmonyPatch(typeof(GalaxyMissionPanel), nameof(GalaxyMissionPanel.OnLaunch))]
    [HarmonyPrefix]
    public static bool OnLaunchPrefix(string fileName)
    {
        if (ProbeCore.MissionAllowed(fileName))
            return true;
        ProbeCore.Logger.LogInfo($"MISSION BLOCKED: '{fileName}' not in allowed set");
        return false;
    }

    // Close the gate bypass: loading a SAVE of a gated mission skipped
    // OnLaunch entirely. The row's parent box knows the mission specifier.
    [HarmonyPatch(typeof(MissionPanelLoadBoxRow), nameof(MissionPanelLoadBoxRow.OnLoad))]
    [HarmonyPrefix]
    public static bool OnLoadPrefix(MissionPanelLoadBoxRow __instance)
    {
        string spec = null;
        try { spec = __instance.missionPanelLoadBox?.specifier; } catch { }
        if (string.IsNullOrEmpty(spec) || ProbeCore.MissionAllowed(spec))
            return true;
        ProbeCore.Logger.LogInfo($"SAVE LOAD BLOCKED: '{spec}' not in allowed set");
        return false;
    }
}

// Injected class: keep it as thin as possible. No statics, no logic.
public class ProbeBehaviour : MonoBehaviour
{
    public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ProbeCore.SafeTick();
    }

    private void LateUpdate()
    {
        ProbeCore.ApplySnpoTints();
    }
}

// Plain C# class - safe home for all state and logic.
public static class ProbeCore
{
    internal static ManualLogSource Logger;

    internal static string UnlockFilePath =>
        System.IO.Path.Combine(Paths.GameRootPath, "BepInEx", "probe-unlocks.txt");

    private static readonly string[] DefaultAllowed = { "riftlab", "tower", "pylon", "cannon" };

    private static readonly Dictionary<string, Action<BuildUnitManager, bool>> Setters =
        new()
        {
            ["riftlab"] = (b, v) => b.riftLabAvailable = v,
            ["factory"] = (b, v) => b.factoryAvailable = v,
            ["ernportal"] = (b, v) => b.ernPortalAvailable = v,
            ["tower"] = (b, v) => b.towerAvailable = v,
            ["pylon"] = (b, v) => b.pylonAvailable = v,
            ["miner"] = (b, v) => b.minerAvailable = v,
            ["greenarrefinery"] = (b, v) => b.greenarRefineryAvailable = v,
            ["terp"] = (b, v) => b.terpAvailable = v,
            ["porter"] = (b, v) => b.porterAvailable = v,
            ["cannon"] = (b, v) => b.cannonAvailable = v,
            ["mortar"] = (b, v) => b.mortarAvailable = v,
            ["sprayer"] = (b, v) => b.sprayerAvailable = v,
            ["sniper"] = (b, v) => b.sniperAvailable = v,
            ["missilelauncher"] = (b, v) => b.missileLauncherAvailable = v,
            ["nullifier"] = (b, v) => b.nullifierAvailable = v,
            ["runway"] = (b, v) => b.runwayAvailable = v,
            ["bomberpad"] = (b, v) => b.bomberPadAvailable = v,
            ["acbomberpad"] = (b, v) => b.acBomberPadAvailable = v,
            ["rocketpad"] = (b, v) => b.rocketPadAvailable = v,
            ["platform"] = (b, v) => b.platformAvailable = v,
            ["shield"] = (b, v) => b.shieldAvailable = v,
            ["microrift"] = (b, v) => b.microRiftAvailable = v,
            ["chronat"] = (b, v) => b.chronatAvailable = v,
            ["airship"] = (b, v) => b.airshipAvailable = v,
            ["bertha"] = (b, v) => b.berthaAvailable = v,
            ["sweeper"] = (b, v) => b.sweeperAvailable = v,
        };

    private static readonly Dictionary<string, Func<BuildUnitManager, bool>> Getters =
        new()
        {
            ["riftlab"] = b => b.riftLabAvailable,
            ["factory"] = b => b.factoryAvailable,
            ["ernportal"] = b => b.ernPortalAvailable,
            ["tower"] = b => b.towerAvailable,
            ["pylon"] = b => b.pylonAvailable,
            ["miner"] = b => b.minerAvailable,
            ["greenarrefinery"] = b => b.greenarRefineryAvailable,
            ["terp"] = b => b.terpAvailable,
            ["porter"] = b => b.porterAvailable,
            ["cannon"] = b => b.cannonAvailable,
            ["mortar"] = b => b.mortarAvailable,
            ["sprayer"] = b => b.sprayerAvailable,
            ["sniper"] = b => b.sniperAvailable,
            ["missilelauncher"] = b => b.missileLauncherAvailable,
            ["nullifier"] = b => b.nullifierAvailable,
            ["runway"] = b => b.runwayAvailable,
            ["bomberpad"] = b => b.bomberPadAvailable,
            ["acbomberpad"] = b => b.acBomberPadAvailable,
            ["rocketpad"] = b => b.rocketPadAvailable,
            ["platform"] = b => b.platformAvailable,
            ["shield"] = b => b.shieldAvailable,
            ["microrift"] = b => b.microRiftAvailable,
            ["chronat"] = b => b.chronatAvailable,
            ["airship"] = b => b.airshipAvailable,
            ["bertha"] = b => b.berthaAvailable,
            ["sweeper"] = b => b.sweeperAvailable,
        };

    private static readonly HashSet<string> Allowed = new(DefaultAllowed);
    private static bool _enforce = true;
    private static IntPtr _lastGameSpace = IntPtr.Zero;
    private static DateTime _lastFileWrite = DateTime.MinValue;
    private static int _paneRefreshCountdown = -1;
    private static int _filePollCountdown = 0;
    private static string _lastScene = "";
    private static string _autoBoot = null;
    private static int _autoBootCountdown = -1;
    private static readonly Dictionary<int, Color> SnpoTints = new();
    private static bool _paneHidden = false;
    private static int _paneRevealCountdown = -1;
    private static HashSet<string> _allowedMissions = null;   // null = all allowed
    private static bool[] _lastObjectiveState = null;
    private static bool _lastMissionComplete = false;
    private static bool _lastGameComplete = false;

    internal static bool MissionAllowed(string fileName)
    {
        if (_allowedMissions == null) return true;
        return _allowedMissions.Contains(fileName.Trim().ToLowerInvariant());
    }

    internal static void SafeTick()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            if (Time.frameCount % 60 == 0)
                Logger.LogError($"Probe tick failed: {e.Message}");
        }
    }

    private static void Tick()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (sceneName != _lastScene)
        {
            Logger.LogInfo($"SCENE: '{_lastScene}' -> '{sceneName}'");
            _lastScene = sceneName;
            if (sceneName == "Game")
            {
                // Hide the pane the moment the mission scene activates -
                // BEFORE GameSpace exists. On the resume-from-save path the
                // UI draws from the save's stale flags a few frames before
                // GameSpace.instance is available; this beats the render.
                foreach (var pn in AllPanes(true))
                    pn.gameObject.SetActive(false);
                _paneHidden = true;
                _paneRevealCountdown = -1;  // armed by New GameSpace detection
                Logger.LogInfo("Panes hidden at Game scene entry (pre-GameSpace)");
            }
            if (_autoBoot != null && sceneName == "Galaxy")
            {
                _autoBootCountdown = 240;
                Logger.LogInfo($"AUTOBOOT: Galaxy scene up - booting '{_autoBoot}' in 4s");
            }
        }

        if (_autoBootCountdown > 0 && --_autoBootCountdown == 0 && _autoBoot != null)
        {
            var m = _autoBoot;
            _autoBoot = null;
            BootMission(m);
        }

        if (--_filePollCountdown <= 0)
        {
            _filePollCountdown = 30;
            PollUnlockFile();
        }

        var gs = GameSpace.instance;
        if (gs == null)
        {
            _lastGameSpace = IntPtr.Zero;
            return;
        }
        if (GameSpace.editMode)
            return;

        var bum = gs.buildUnitManager;
        if (bum == null)
            return;

        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            Logger.LogInfo($"New GameSpace - enforcing whitelist: {string.Join(", ", Allowed)}");
            _lastObjectiveState = null;
            _lastMissionComplete = false;
            _lastGameComplete = false;
            _paneRefreshCountdown = -1;
            if (_paneHidden)
            {
                _paneRevealCountdown = 30;   // scene-entry hide already active
            }
            else
            {
                _paneHidden = false;         // fall back to first-sight hide
                _paneRevealCountdown = -1;
            }
        }

        if (_enforce)
            foreach (var kv in Setters)
                kv.Value(bum, Allowed.Contains(kv.Key));

        if (_paneRefreshCountdown > 0 && --_paneRefreshCountdown == 0)
        {
            if (!RefreshPane())
                _paneRefreshCountdown = 30;
        }

        WatchLocations(gs);

        EnforceErnDeny();
        ProcessErnGrants(gs);

        // No-flash: hide all panes at first sight; after ~1s of enforcement
        // rebuild every pane's buttons and reveal.
        if (!_paneHidden)
        {
            var early = AllPanes(false);
            if (early.Count > 0)
            {
                foreach (var pn in early) pn.gameObject.SetActive(false);
                _paneHidden = true;
                _paneRevealCountdown = 30;
                Logger.LogInfo($"{early.Count} pane(s) hidden at first sight (no-flash)");
            }
        }
        else if (_paneRevealCountdown > 0 && --_paneRevealCountdown == 0)
        {
            _revealPhase = 1;   // start the multi-frame reveal state machine
            _revealAttempts = 0;
        }

        RunRevealStateMachine();
    }

    // Multi-frame reveal: activate, let a few frames pass so OnEnable/Start
    // run, refresh, force real toggle events, enforce single-active, VERIFY,
    // and retry the whole dance if the struct pane came up empty.
    private static int _revealPhase = 0;
    private static int _revealWait = 0;
    private static int _revealAttempts = 0;

    private static void RunRevealStateMachine()
    {
        if (_revealPhase == 0)
            return;
        if (_revealWait > 0) { _revealWait--; return; }

        var lp = FindLeftPane();
        if (lp == null)
            return;

        switch (_revealPhase)
        {
            case 1: // activate everything so components initialize
                foreach (var pn in AllPanes(true))
                    pn.gameObject.SetActive(true);
                _revealPhase = 2; _revealWait = 5;
                break;
            case 2: // native refresh while panes are alive
                NativeRefresh("reveal", true);
                _revealPhase = 3; _revealWait = 3;
                break;
            case 3: // clear ALL toggles (they are NOT in a ToggleGroup -
                    // code-set isOn leaves multiple true; the game's click
                    // handler manages exclusivity, not Unity)
                if (lp.weaponTab != null) lp.weaponTab.isOn = false;
                if (lp.airTab != null) lp.airTab.isOn = false;
                if (lp.specialTab != null) lp.specialTab.isOn = false;
                if (lp.customTab != null) lp.customTab.isOn = false;
                if (lp.structTab != null) lp.structTab.isOn = false;
                _revealPhase = 4; _revealWait = 3;
                break;
            case 4: // set struct ON - a real false->true change event
                if (lp.structTab != null) lp.structTab.isOn = true;
                _revealPhase = 5; _revealWait = 3;
                break;
            case 5: // enforce single-active pane, explicitly struct
                ResyncStrip(lp, lp.structUnitBuildPane);
                _revealPhase = 6; _revealWait = 5;
                break;
            case 6: // VERIFY: struct pane active with at least one button
                bool ok = false;
                var sp = lp.structUnitBuildPane;
                if (sp != null)
                {
                    try
                    {
                        var btns = sp.GetBuildButtons();
                        // activeSelf, not activeInHierarchy: the game hides
                        // the whole pane container while the ADA log is open,
                        // which is not a failure of OUR state.
                        ok = sp.gameObject.activeSelf && btns != null && btns.Length > 0;
                    }
                    catch { }
                }
                if (ok)
                {
                    Logger.LogInfo($"REVEAL OK (attempt {_revealAttempts + 1})");
                    _revealPhase = 0;
                }
                else if (++_revealAttempts < 5)
                {
                    Logger.LogWarning($"REVEAL VERIFY FAILED - retrying (attempt {_revealAttempts + 1})");
                    _revealPhase = 1; _revealWait = 10;
                }
                else
                {
                    Logger.LogError("REVEAL FAILED after 5 attempts - leaving panes as-is");
                    _revealPhase = 0;
                }
                break;
        }
    }

    // Use the game's own LeftPane machinery to rebuild panes and re-sync the
    // active tab - avoids the shared-button-strip desync caused by calling
    // SetEnabledButtons on every pane manually.
    // After LoadGame destroys the previous mission scene,
    // FindObjectsOfTypeAll can return DESTROYED instances from the old
    // scene. Everything we touch must be liveness-checked.
    private static bool IsAlive(Component c)
    {
        try
        {
            if (c == null) return false;
            var go = c.gameObject;
            if (go == null) return false;
            return go.scene.IsValid();
        }
        catch { return false; }
    }

    private static LeftPane FindLeftPane()
    {
        // Authoritative: the current mission's GameSpace holds the live
        // LeftPane. Resources scans can return destroyed instances from the
        // previous mission after a LoadGame transition.
        try
        {
            var lp = GameSpace.instance?.leftPane;
            if (IsAlive(lp)) return lp;
        }
        catch { }
        var all = Resources.FindObjectsOfTypeAll<LeftPane>();
        if (all == null) return null;
        foreach (var lp in all)
            if (IsAlive(lp)) return lp;
        return null;
    }

    private static void NativeRefresh(string why, bool pickTab)
    {
        var lp = FindLeftPane();
        if (lp == null)
        {
            Logger.LogWarning($"NativeRefresh ({why}): no LeftPane found");
            return;
        }
        lp.RefreshUnitBuildPanes();
        if (pickTab)
            lp.PickActiveTab();
        Logger.LogInfo($"LeftPane refresh done ({why}, pickTab={pickTab})");
    }

    private static void SwitchTab(string name)
    {
        var lp = FindLeftPane();
        if (lp == null)
        {
            Logger.LogWarning("tab: no LeftPane found");
            return;
        }
        UnityEngine.UI.Toggle t = name switch
        {
            "struct" => lp.structTab,
            "weapon" => lp.weaponTab,
            "air" => lp.airTab,
            "special" => lp.specialTab,
            "custom" => lp.customTab,
            _ => null,
        };
        if (t == null)
        {
            Logger.LogWarning($"tab: unknown tab '{name}'");
            return;
        }
        t.isOn = true;
        Logger.LogInfo($"TAB SWITCHED: {name}");
    }

    // Poll objective completion + mission/game completion; log every
    // transition. These log lines are what the AP client will turn into
    // LocationChecks packets.
    private static void WatchLocations(GameSpace gs)
    {
        var world = gs.world;
        if (world == null)
            return;

        var objs = world.missionObjectives;
        if (objs != null)
        {
            if (_lastObjectiveState == null || _lastObjectiveState.Length != objs.Length)
                _lastObjectiveState = new bool[objs.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                bool done = false;
                try { done = world.IsMissionObjectiveComplete(i); } catch { }
                if (done && !_lastObjectiveState[i])
                {
                    _lastObjectiveState[i] = true;
                    string title = "?";
                    try { title = objs[i].customName; } catch { }
                    Logger.LogInfo($"LOCATION TRIGGER: objective {i} complete ('{title}')");
                }
            }
        }

        bool mc = false;
        try { mc = world.IsMissionComplete(); } catch { }
        if (mc && !_lastMissionComplete)
        {
            _lastMissionComplete = true;
            Logger.LogInfo("LOCATION TRIGGER: MISSION COMPLETE (all required objectives)");
        }

        bool gc = false;
        try { gc = gs.gameComplete; } catch { }
        if (gc && !_lastGameComplete)
        {
            _lastGameComplete = true;
            Logger.LogInfo("LOCATION TRIGGER: gameComplete=true (victory sequence)");
        }
    }

    internal static void ApplySnpoTints()
    {
        if (SnpoTints.Count == 0) return;
        var snposT = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
        if (snposT == null) return;
        foreach (var kv in SnpoTints)
        {
            if (kv.Key < 0 || kv.Key >= snposT.Length) continue;
            try { snposT[kv.Key].GetComponent<MeshRenderer>().material.color = kv.Value; }
            catch { }
        }
    }

    private static List<UnitBuildPane> AllPanes(bool includeInactive)
    {
        // Prefer the live LeftPane's own five references; fall back to a
        // global scan only if no LeftPane is reachable.
        var result = new List<UnitBuildPane>();
        var lp = FindLeftPane();
        if (lp != null)
        {
            foreach (var pn in new[] { lp.structUnitBuildPane, lp.weaponUnitBuildPane,
                                       lp.airUnitBuildPane, lp.specialUnitBuildPane,
                                       lp.customUnitBuildPane })
                if (IsAlive(pn) && (includeInactive || pn.gameObject.activeInHierarchy))
                    result.Add(pn);
            return result;
        }
        var all = includeInactive
            ? Resources.FindObjectsOfTypeAll<UnitBuildPane>()
            : UnityEngine.Object.FindObjectsOfType<UnitBuildPane>();
        if (all != null)
            foreach (var pobj in all)
                if (IsAlive(pobj)) result.Add(pobj);
        return result;
    }

    private static void DumpFlags()
    {
        var gs = GameSpace.instance;
        var bum = gs?.buildUnitManager;
        if (bum == null)
        {
            Logger.LogWarning("flags - no BuildUnitManager");
            return;
        }
        foreach (var kv in Getters)
        {
            bool actual = false;
            try { actual = kv.Value(bum); } catch { }
            bool wanted = Allowed.Contains(kv.Key);
            var mark = actual == wanted ? "" : "  <-- FIGHTING US";
            Logger.LogInfo($"FLAG: {kv.Key} actual={actual} wanted={wanted}{mark}");
        }
    }

    private static void PollUnlockFile()
    {
        var path = UnlockFilePath;
        if (!File.Exists(path))
            return;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (stamp == _lastFileWrite)
            return;
        _lastFileWrite = stamp;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            var lower = line.ToLowerInvariant();

            if (lower.StartsWith("boot:"))
            {
                BootMission(line.Substring(5).Trim());
                continue;
            }
            if (lower.StartsWith("autoboot:"))
            {
                _autoBoot = line.Substring(9).Trim();
                Logger.LogInfo($"AUTOBOOT queued: '{_autoBoot}'");
                continue;
            }
            if (lower.StartsWith("lock:"))
            {
                var lname = lower.Substring(5).Trim();
                if (Allowed.Remove(lname))
                {
                    Logger.LogInfo($"LIVE LOCK: {lname}");
                    _paneRefreshCountdown = 1;
                }
                continue;
            }
            if (lower == "enforce:off") { _enforce = false; Logger.LogInfo("ENFORCE off"); continue; }
            if (lower == "enforce:on") { _enforce = true; Logger.LogInfo("ENFORCE on"); continue; }
            if (lower == "natflags")
            {
                var bumN = GameSpace.instance?.buildUnitManager;
                if (bumN != null)
                {
                    var onList = new List<string>();
                    foreach (var kv in Getters)
                    {
                        bool v = false;
                        try { v = kv.Value(bumN); } catch { }
                        if (v) onList.Add(kv.Key);
                    }
                    Logger.LogInfo($"NATFLAGS: {string.Join(",", onList)}");
                }
                continue;
            }
            if (lower == "ern:status") { ErnStatus(); continue; }
            if (lower == "ern:portal") { ErnSpawn(new[] { "ernportal", "ERNPortal", "ErnPortal", "ERNPORTAL" }); continue; }
            if (lower == "ern:make") { ErnSpawn(new[] { "ern", "ERN", "Ern" }); continue; }
            if (lower == "ern:iface") { ErnSpawn(new[] { "erninterface", "ERNInterface", "ErnInterface" }); continue; }
            if (lower.StartsWith("ern:grant:"))
            {
                if (int.TryParse(line.Substring(10).Trim(), out var eg) && eg > 0)
                {
                    _ernPending += eg;
                    Logger.LogInfo($"ERN GRANT: {eg} queued ({_ernPending} pending) - spawns once the rift lab exists");
                }
                continue;
            }
            if (lower == "ern:deny") { _ernDeny = true; Logger.LogInfo("ERN DENY ON"); continue; }
            if (lower == "ern:allow") { _ernDeny = false; Logger.LogInfo("ERN DENY OFF"); continue; }
            if (lower == "census")
            {
                try
                {
                    var units = UnityEngine.Object.FindObjectsOfType<UnitManager>();
                    var counts = new Dictionary<string, int>();
                    if (units != null)
                        foreach (var u in units)
                        {
                            string tn = "?";
                            try { tn = u.GetIl2CppType().Name; } catch { }
                            counts[tn] = counts.TryGetValue(tn, out var c0) ? c0 + 1 : 1;
                        }
                    var parts = new List<string>();
                    foreach (var kv in counts) parts.Add($"{kv.Key}:{kv.Value}");
                    parts.Sort();
                    Logger.LogInfo($"CENSUS: {string.Join(" ", parts)}");
                }
                catch (Exception e) { Logger.LogWarning($"census failed: {e.Message}"); }
                continue;
            }
            if (lower == "graph")
            {
                var planetsG = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
                Logger.LogInfo($"GRAPH: {(planetsG == null ? 0 : planetsG.Length)} planets");
                if (planetsG != null)
                    foreach (var pg in planetsG)
                    {
                        string ttl = "?", guid = "?";
                        try { ttl = pg.title?.text?.Trim(); } catch { }
                        try { guid = pg.planetGUID; } catch { }
                        string conns = "";
                        try { conns = string.Join(",", pg.connectedPlanetGUIDS); } catch { }
                        Logger.LogInfo($"GRAPH: '{pg.gameObject.name}' guid={guid} title='{ttl}' -> [{conns}]");
                    }
                continue;
            }
            if (lower == "reset")
            {
                Allowed.Clear();
                foreach (var d in DefaultAllowed) Allowed.Add(d);
                Logger.LogInfo("whitelist RESET to defaults");
                _paneRefreshCountdown = 1;
                continue;
            }
            if (lower.StartsWith("pane:"))
            {
                PaneExperiment(lower.Substring(5).Trim());
                continue;
            }
            if (lower == "check")
            {
                var lpc = FindLeftPane();
                var spc = lpc?.structUnitBuildPane;
                int nbtn = -1; bool act = false; bool actH = false;
                if (spc != null)
                {
                    try { act = spc.gameObject.activeSelf; } catch { }
                    try { actH = spc.gameObject.activeInHierarchy; } catch { }
                    try { var bb = spc.GetBuildButtons(); nbtn = bb == null ? -1 : bb.Length; } catch { }
                }
                string tsel = "?";
                if (lpc != null)
                {
                    if (lpc.structTab != null && lpc.structTab.isOn) tsel = "struct";
                    else if (lpc.weaponTab != null && lpc.weaponTab.isOn) tsel = "weapon";
                    else if (lpc.airTab != null && lpc.airTab.isOn) tsel = "air";
                    else if (lpc.specialTab != null && lpc.specialTab.isOn) tsel = "special";
                    else if (lpc.customTab != null && lpc.customTab.isOn) tsel = "custom";
                }
                Logger.LogInfo($"CHECK: tab={tsel} structActive={act} structVisible={actH} structButtons={nbtn} " +
                               $"allowed=[{string.Join(",", Allowed)}]");
                continue;
            }
            if (lower == "dump")
            {
                DumpButtons();
                continue;
            }
            if (lower == "flags")
            {
                DumpFlags();
                continue;
            }
            if (lower.StartsWith("tab:"))
            {
                SwitchTab(lower.Substring(4).Trim());
                continue;
            }
            if (lower.StartsWith("limit:"))
            {
                var parts = line.Substring(6).Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var amt))
                {
                    var gs2 = GameSpace.instance;
                    var bum2 = gs2?.buildUnitManager;
                    if (bum2 != null)
                    {
                        bum2.SetBuildCountLimit(parts[0].Trim(), amt);
                        int rb = -999;
                        try { rb = bum2.GetBuildCountLimit(parts[0].Trim()); } catch { }
                        Logger.LogInfo($"LIMIT SET: '{parts[0].Trim()}' = {amt} (readback {rb})");
                        _paneRefreshCountdown = 1;
                    }
                    else Logger.LogWarning("limit: no BuildUnitManager");
                }
                else Logger.LogWarning($"limit: bad syntax '{line}'");
                continue;
            }
            if (lower.StartsWith("missions:"))
            {
                var list = lower.Substring(9).Trim();
                if (list == "all")
                {
                    _allowedMissions = null;
                    Logger.LogInfo("MISSION GATE: all missions allowed");
                }
                else
                {
                    _allowedMissions = new HashSet<string>();
                    foreach (var m in list.Split(','))
                        if (m.Trim().Length > 0) _allowedMissions.Add(m.Trim());
                    Logger.LogInfo($"MISSION GATE: allowed = {string.Join(", ", _allowedMissions)}");
                }
                continue;
            }
            if (lower.StartsWith("objective:"))
            {
                if (int.TryParse(lower.Substring(10).Trim(), out var objIdx))
                {
                    var w = GameSpace.instance?.world;
                    if (w != null)
                    {
                        w.AcquireMissionObjective(objIdx, true);
                        Logger.LogInfo($"AcquireMissionObjective({objIdx}) called");
                    }
                    else Logger.LogWarning("objective: no world");
                }
                continue;
            }
            if (lower == "objdump")
            {
                var w = GameSpace.instance?.world;
                if (w?.missionObjectives != null)
                {
                    var objs2 = w.missionObjectives;
                    Logger.LogInfo($"OBJDUMP: {objs2.Length} objectives");
                    for (int i = 0; i < objs2.Length; i++)
                    {
                        string t = "?"; bool c2 = false, req = false;
                        try { t = objs2[i].customName; } catch { }
                        try { req = objs2[i].required; } catch { }
                        try { c2 = w.IsMissionObjectiveComplete(i); } catch { }
                        Logger.LogInfo($"OBJDUMP:   [{i}] '{t}' required={req} complete={c2}");
                    }
                    Logger.LogInfo($"OBJDUMP: IsMissionComplete={w.IsMissionComplete()}");
                }
                else Logger.LogWarning("objdump: no world/objectives");
                continue;
            }
            if (lower == "win")
            {
                var w = GameSpace.instance?.world;
                if (w?.missionObjectives != null)
                {
                    for (int i = 0; i < w.missionObjectives.Length; i++)
                        w.AcquireMissionObjective(i, true);
                    Logger.LogInfo("WIN: all objectives acquired");
                }
                else Logger.LogWarning("win: no world/objectives");
                continue;
            }
            if (lower == "galaxy:dump")
            {
                GalaxyDump();
                continue;
            }
            if (lower == "story:open")
            {
                var gg = GameGalaxy.instance;
                var btn = gg?.farsiteButton;
                if (btn != null)
                {
                    var b = btn.GetComponent<UnityEngine.UI.Button>();
                    if (b != null) { b.onClick.Invoke(); Logger.LogInfo("story:open - farsite button clicked"); }
                    else Logger.LogWarning("story:open - no Button component");
                }
                else Logger.LogWarning("story:open - no GameGalaxy/farsiteButton");
                continue;
            }
            if (lower == "galaxy:snpo")
            {
                var snpos = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                Logger.LogInfo($"SNPO: {(snpos == null ? 0 : snpos.Length)} instances");
                if (snpos != null)
                    for (int i = 0; i < snpos.Length && i < 12; i++)
                    {
                        var sn = snpos[i];
                        var pnt = sn.transform.parent;
                        var comps = sn.GetComponents<Component>();
                        var cn = new List<string>();
                        foreach (var cm in comps) if (cm != null) cn.Add(cm.GetIl2CppType().Name);
                        Logger.LogInfo($"SNPO: [{i}] objective={sn.objective} complete={sn.complete} " +
                                       $"pos={sn.transform.position} parent='{pnt?.name}' comps=[{string.Join(",", cn)}] children={sn.transform.childCount}");
                        for (int c = 0; c < sn.transform.childCount; c++)
                        {
                            var ch = sn.transform.GetChild(c);
                            string extra = "";
                            try { var tmp = ch.GetComponent<TMPro.TextMeshPro>(); if (tmp != null) extra += $" TMP(text='{tmp.text}',color={tmp.color})"; } catch { }
                            try { var sr2 = ch.GetComponent<SpriteRenderer>(); if (sr2 != null) extra += $" SR(sprite={sr2.sprite?.name},color={sr2.color})"; } catch { }
                            Logger.LogInfo($"SNPO:     child '{ch.name}' active={ch.gameObject.activeSelf}{extra}");
                        }
                    }
                continue;
            }
            if (lower.StartsWith("galaxy:snpoflip:"))
            {
                // galaxy:snpoflip:<index> - toggle complete on instance #index
                if (int.TryParse(line.Substring(16).Trim(), out var si))
                {
                    var snpos = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                    if (snpos != null && si >= 0 && si < snpos.Length)
                    {
                        snpos[si].complete = !snpos[si].complete;
                        Logger.LogInfo($"SNPO: flipped [{si}] -> complete={snpos[si].complete}");
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:snpotint:"))
            {
                // galaxy:snpotint:<index>:<r>,<g>,<b>
                var pp = line.Substring(16).Split(':');
                if (pp.Length == 2)
                {
                    var rgb = pp[1].Split(',');
                    if (int.TryParse(pp[0], out var si2) && rgb.Length == 3 &&
                        float.TryParse(rgb[0], out var r2) && float.TryParse(rgb[1], out var g2) && float.TryParse(rgb[2], out var b2))
                    {
                        SnpoTints[si2] = new Color(r2, g2, b2, 1f);
                        Logger.LogInfo($"SNPO: continuous tint [{si2}] = ({r2},{g2},{b2})");
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:planettint:"))
            {
                // galaxy:planettint:<index>:<r>,<g>,<b>
                var pp2 = line.Substring(18).Split(':');
                if (pp2.Length == 2)
                {
                    var rgb2 = pp2[1].Split(',');
                    if (int.TryParse(pp2[0], out var pi) && rgb2.Length == 3 &&
                        float.TryParse(rgb2[0], out var r3) && float.TryParse(rgb2[1], out var g3) && float.TryParse(rgb2[2], out var b3))
                    {
                        var ss4 = FindStorySector();
                        var cont = ss4?.planets?.transform.GetChild(0);
                        if (cont != null && pi >= 0 && pi < cont.childCount)
                        {
                            try
                            {
                                var mr2 = cont.GetChild(pi).GetComponent<MeshRenderer>();
                                mr2.material.color = new Color(r3, g3, b3, 1f);
                                Logger.LogInfo($"PLANET: tinted [{pi}] to ({r3},{g3},{b3})");
                            }
                            catch (Exception e) { Logger.LogWarning($"planettint failed: {e.Message}"); }
                        }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:mat:"))
            {
                if (int.TryParse(line.Substring(11).Trim(), out var mi))
                {
                    var snposM = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                    if (snposM != null && mi >= 0 && mi < snposM.Length)
                    {
                        try
                        {
                            var mr = snposM[mi].GetComponent<MeshRenderer>();
                            var m = mr.material;
                            Logger.LogInfo($"MAT[{mi}]: name='{m.name}' shader='{m.shader?.name}'");
                            foreach (var prop in new[] { "_Color", "_BaseColor", "_EmissionColor", "_TintColor", "_MainColor", "_GlowColor" })
                            {
                                try
                                {
                                    if (m.HasProperty(prop))
                                        Logger.LogInfo($"MAT[{mi}]:   {prop} = {m.GetColor(prop)}");
                                }
                                catch { }
                            }
                            var sm = mr.sharedMaterial;
                            Logger.LogInfo($"MAT[{mi}]: shared='{sm?.name}' sameAsInstance={(sm != null && m != null && sm.Pointer == m.Pointer)}");
                        }
                        catch (Exception e) { Logger.LogWarning($"galaxy:mat failed: {e.Message}"); }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:matset:"))
            {
                // galaxy:matset:<i>:<prop>:<r>,<g>,<b>
                var mp = line.Substring(14).Split(':');
                if (mp.Length == 3)
                {
                    var rgbm = mp[2].Split(',');
                    if (int.TryParse(mp[0], out var mi2) && rgbm.Length == 3 &&
                        float.TryParse(rgbm[0], out var mr1) && float.TryParse(rgbm[1], out var mg1) && float.TryParse(rgbm[2], out var mb1))
                    {
                        var snposM2 = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                        if (snposM2 != null && mi2 >= 0 && mi2 < snposM2.Length)
                        {
                            try
                            {
                                snposM2[mi2].GetComponent<MeshRenderer>().material.SetColor(mp[1], new Color(mr1, mg1, mb1, 1f));
                                Logger.LogInfo($"MATSET[{mi2}]: {mp[1]} = ({mr1},{mg1},{mb1})");
                            }
                            catch (Exception e) { Logger.LogWarning($"matset failed: {e.Message}"); }
                        }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:recolor:"))
            {
                // galaxy:recolor:<i>:<r>,<g>,<b>  white-texture + tintable shader
                var rp = line.Substring(15).Split(':');
                if (rp.Length == 2)
                {
                    var rgbr = rp[1].Split(',');
                    if (int.TryParse(rp[0], out var ri) && rgbr.Length == 3 &&
                        float.TryParse(rgbr[0], out var rr1) && float.TryParse(rgbr[1], out var rg1) && float.TryParse(rgbr[2], out var rb1))
                    {
                        var snposR = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                        if (snposR != null && ri >= 0 && ri < snposR.Length)
                        {
                            try
                            {
                                var sn = snposR[ri];
                                sn.complete = false;   // white glyph texture
                                var mr = sn.GetComponent<MeshRenderer>();
                                var m = mr.material;
                                Texture tex = null;
                                try { tex = m.mainTexture; } catch { }
                                var sh = Shader.Find("Sprites/Default");
                                if (sh != null) m.shader = sh;
                                if (tex != null) m.mainTexture = tex;
                                m.color = new Color(rr1, rg1, rb1, 1f);
                                Logger.LogInfo($"RECOLOR[{ri}]: shader={m.shader?.name} tex={(tex == null ? "NULL" : tex.name)} color=({rr1},{rg1},{rb1})");
                            }
                            catch (Exception e) { Logger.LogWarning($"recolor failed: {e.Message}"); }
                        }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:planetgrey:"))
            {
                // galaxy:planetgrey:<index>:<brightness 0-1>
                var pg = line.Substring(18).Split(':');
                if (pg.Length == 2 && int.TryParse(pg[0], out var gi) && float.TryParse(pg[1], out var gb))
                {
                    var ssG = FindStorySector();
                    var contG = ssG?.planets?.transform.GetChild(0);
                    if (contG != null && gi >= 0 && gi < contG.childCount)
                    {
                        try
                        {
                            var mrG = contG.GetChild(gi).GetComponent<MeshRenderer>();
                            var mG = mrG.material;
                            Texture texG = null;
                            try { texG = mG.mainTexture; } catch { }
                            var shG = Shader.Find("Sprites/Default");
                            if (shG != null) mG.shader = shG;
                            if (texG != null) mG.mainTexture = texG;
                            mG.color = new Color(gb, gb, gb, 1f);
                            Logger.LogInfo($"PLANETGREY[{gi}]: tex={(texG == null ? "NULL" : texG.name)} brightness={gb}");
                        }
                        catch (Exception e) { Logger.LogWarning($"planetgrey failed: {e.Message}"); }
                    }
                }
                continue;
            }
            if (lower == "galaxy:rings")
            {
                var lrs = UnityEngine.Object.FindObjectsOfType<LineRenderer>();
                Logger.LogInfo($"RINGS: {(lrs == null ? 0 : lrs.Length)} LineRenderers");
                if (lrs != null)
                    for (int i = 0; i < lrs.Length && i < 10; i++)
                        Logger.LogInfo($"RINGS: LR[{i}] '{lrs[i].gameObject.name}' parent='{lrs[i].transform.parent?.name}' start={lrs[i].startColor}");
                var srsR = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
                int shown = 0;
                Logger.LogInfo($"RINGS: {(srsR == null ? 0 : srsR.Length)} SpriteRenderers");
                if (srsR != null)
                    foreach (var sr in srsR)
                    {
                        if (shown >= 12) break;
                        Logger.LogInfo($"RINGS: SR '{sr.gameObject.name}' sprite={sr.sprite?.name} color={sr.color} parent='{sr.transform.parent?.name}'");
                        shown++;
                    }
                continue;
            }
            if (lower.StartsWith("galaxy:shaderprops:"))
            {
                // galaxy:shaderprops:snpo:<i>  or  galaxy:shaderprops:planet:<i>
                var sp2 = line.Substring(19).Split(':');
                if (sp2.Length == 2 && int.TryParse(sp2[1], out var pi3))
                {
                    Material mat = null;
                    try
                    {
                        if (sp2[0] == "snpo")
                        {
                            var arr = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetObjective>();
                            if (arr != null && pi3 < arr.Length) mat = arr[pi3].GetComponent<MeshRenderer>().material;
                        }
                        else
                        {
                            var contP = FindStorySector()?.planets?.transform.GetChild(0);
                            if (contP != null && pi3 < contP.childCount) mat = contP.GetChild(pi3).GetComponent<MeshRenderer>().material;
                        }
                    }
                    catch { }
                    if (mat == null) { Logger.LogWarning("shaderprops: no material"); continue; }
                    var shd = mat.shader;
                    int cnt = 0;
                    try { cnt = shd.GetPropertyCount(); } catch { }
                    Logger.LogInfo($"SHADERPROPS: mat='{mat.name}' shader='{shd?.name}' props={cnt}");
                    for (int i = 0; i < cnt; i++)
                    {
                        try
                        {
                            var pname = shd.GetPropertyName(i);
                            var ptype = shd.GetPropertyType(i);
                            string val = "";
                            if (ptype == UnityEngine.Rendering.ShaderPropertyType.Color) val = mat.GetColor(pname).ToString();
                            else if (ptype == UnityEngine.Rendering.ShaderPropertyType.Texture) val = mat.GetTexture(pname)?.name ?? "null";
                            else if (ptype == UnityEngine.Rendering.ShaderPropertyType.Float || ptype == UnityEngine.Rendering.ShaderPropertyType.Range) val = mat.GetFloat(pname).ToString();
                            Logger.LogInfo($"SHADERPROPS:   [{i}] {pname} ({ptype}) = {val}");
                        }
                        catch (Exception e) { Logger.LogInfo($"SHADERPROPS:   [{i}] err {e.Message}"); }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:planetprop:"))
            {
                // galaxy:planetprop:<i>:<prop>:<float>
                var ppr = line.Substring(18).Split(':');
                if (ppr.Length == 3 && int.TryParse(ppr[0], out var ppi) && float.TryParse(ppr[2], out var ppv))
                {
                    var contPP = FindStorySector()?.planets?.transform.GetChild(0);
                    if (contPP != null && ppi < contPP.childCount)
                    {
                        try
                        {
                            contPP.GetChild(ppi).GetComponent<MeshRenderer>().material.SetFloat(ppr[1], ppv);
                            Logger.LogInfo($"PLANETPROP[{ppi}]: {ppr[1]} = {ppv}");
                        }
                        catch (Exception e) { Logger.LogWarning($"planetprop failed: {e.Message}"); }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:linecolor:"))
            {
                // galaxy:linecolor:<i>:<r>,<g>,<b>
                var lc = line.Substring(17).Split(':');
                if (lc.Length == 2 && int.TryParse(lc[0], out var li))
                {
                    var rgbL = lc[1].Split(',');
                    if (rgbL.Length == 3 && float.TryParse(rgbL[0], out var lr) && float.TryParse(rgbL[1], out var lg) && float.TryParse(rgbL[2], out var lb))
                    {
                        var lrs2 = UnityEngine.Object.FindObjectsOfType<LineRenderer>();
                        if (lrs2 != null && li < lrs2.Length)
                        {
                            var col = new Color(lr, lg, lb, 1f);
                            lrs2[li].startColor = col;
                            lrs2[li].endColor = col;
                            Logger.LogInfo($"LINECOLOR[{li}] = ({lr},{lg},{lb})");
                        }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:find:"))
            {
                var needle = line.Substring(12).Trim().ToLowerInvariant();
                var alls = Resources.FindObjectsOfTypeAll<Transform>();
                int hits = 0;
                if (alls != null)
                    foreach (var t in alls)
                    {
                        if (hits >= 15) break;
                        string nm = null;
                        try { nm = t.name; } catch { continue; }
                        if (nm != null && nm.ToLowerInvariant().Contains(needle) && IsAlive(t))
                        {
                            Logger.LogInfo($"FIND: '{nm}' parent='{t.parent?.name}' active={t.gameObject.activeInHierarchy} pos={t.position}");
                            hits++;
                        }
                    }
                Logger.LogInfo($"FIND: {hits} hits for '{needle}'");
                continue;
            }
            if (lower.StartsWith("galaxy:planetmats:"))
            {
                if (int.TryParse(line.Substring(18).Trim(), out var pmi))
                {
                    var contM = FindStorySector()?.planets?.transform.GetChild(0);
                    if (contM != null && pmi < contM.childCount)
                    {
                        try
                        {
                            var mats = contM.GetChild(pmi).GetComponent<MeshRenderer>().materials;
                            Logger.LogInfo($"PLANETMATS[{pmi}]: {mats.Length} materials");
                            foreach (var mm in mats)
                                Logger.LogInfo($"PLANETMATS:   '{mm.name}' shader='{mm.shader?.name}'");
                        }
                        catch (Exception e) { Logger.LogWarning($"planetmats failed: {e.Message}"); }
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:matfind:"))
            {
                var sub = line.Substring(15).Trim().ToLowerInvariant();
                var matsAll = Resources.FindObjectsOfTypeAll<Material>();
                int mh = 0;
                if (matsAll != null)
                    foreach (var ma in matsAll)
                    {
                        if (mh >= 15) break;
                        string mn = null;
                        try { mn = ma.name; } catch { continue; }
                        if (mn != null && mn.ToLowerInvariant().Contains(sub))
                        {
                            Logger.LogInfo($"MATFIND: '{mn}' shader='{ma.shader?.name}'");
                            mh++;
                        }
                    }
                Logger.LogInfo($"MATFIND: {mh} hits for '{sub}'");
                continue;
            }
            if (lower.StartsWith("galaxy:planetswap:"))
            {
                // galaxy:planetswap:<i>:<material name substring>
                var psw = line.Substring(18).Split(':');
                if (psw.Length == 2 && int.TryParse(psw[0], out var swi))
                {
                    var contS = FindStorySector()?.planets?.transform.GetChild(0);
                    Material found = null;
                    var matsAll2 = Resources.FindObjectsOfTypeAll<Material>();
                    if (matsAll2 != null)
                        foreach (var ma in matsAll2)
                        {
                            try { if (ma.name.ToLowerInvariant().Contains(psw[1].ToLowerInvariant())) { found = ma; break; } }
                            catch { }
                        }
                    if (contS != null && swi < contS.childCount && found != null)
                    {
                        try
                        {
                            contS.GetChild(swi).GetComponent<MeshRenderer>().material = found;
                            Logger.LogInfo($"PLANETSWAP[{swi}]: -> '{found.name}'");
                        }
                        catch (Exception e) { Logger.LogWarning($"planetswap failed: {e.Message}"); }
                    }
                    else Logger.LogWarning($"planetswap: planet or material not found ('{psw[1]}')");
                }
                continue;
            }
            if (lower.StartsWith("galaxy:renderers:"))
            {
                var rsub = line.Substring(17).Trim().ToLowerInvariant();
                var mrs2 = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                int rh = 0;
                if (mrs2 != null)
                    foreach (var r in mrs2)
                    {
                        if (rh >= 12) break;
                        string mn2 = null;
                        try { mn2 = r.material?.name; } catch { continue; }
                        if (mn2 != null && mn2.ToLowerInvariant().Contains(rsub))
                        {
                            Logger.LogInfo($"RENDERER: go='{r.gameObject.name}' parent='{r.transform.parent?.name}' mat='{mn2}' pos={r.transform.position}");
                            rh++;
                        }
                    }
                Logger.LogInfo($"RENDERER: {rh} hits for '{rsub}'");
                continue;
            }
            if (lower.StartsWith("galaxy:tree2:"))
            {
                // galaxy:tree2:<exact object name>
                var tname = line.Substring(13).Trim();
                var allT = Resources.FindObjectsOfTypeAll<Transform>();
                Transform hit = null;
                if (allT != null)
                    foreach (var t in allT)
                    {
                        try { if (t.name == tname && IsAlive(t)) { hit = t; break; } } catch { }
                    }
                if (hit == null) { Logger.LogWarning($"tree2: '{tname}' not found"); continue; }
                DumpTree2(hit, 0, 3);
                continue;
            }
            if (lower == "menu:hide")
            {
                var ggm = GameGalaxy.instance;
                if (ggm == null) { Logger.LogWarning("menu:hide - no GameGalaxy"); continue; }
                int hid = 0;
                foreach (var go in new[] { ggm.chronomButton, ggm.markVButton, ggm.coloniesButton, ggm.editorButton })
                {
                    if (go != null) { go.SetActive(false); hid++; }
                }
                Logger.LogInfo($"MENU: hid {hid} buttons (chronom, markV, colonies, editor)");
                continue;
            }
            if (lower == "menu:panel")
            {
                try { BuildApPanel(); }
                catch (Exception e) { Logger.LogWarning($"menu:panel failed: {e.Message}"); }
                continue;
            }
            if (lower == "galaxy:icons")
            {
                // scene-wide hunt for objects using Icon_* sprites
                int found = 0;
                var srs = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
                if (srs != null)
                    foreach (var sr in srs)
                    {
                        string sn = null;
                        try { sn = sr.sprite?.name; } catch { }
                        if (sn != null && sn.StartsWith("Icon_"))
                        {
                            var parent = sr.transform.parent;
                            Logger.LogInfo($"ICON-SR: '{sr.gameObject.name}' sprite={sn} color={sr.color} parent='{parent?.name}' gparent='{parent?.parent?.name}'");
                            found++;
                        }
                    }
                var imgs = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>();
                if (imgs != null)
                    foreach (var im in imgs)
                    {
                        string sn = null;
                        try { sn = im.sprite?.name; } catch { }
                        if (sn != null && sn.StartsWith("Icon_"))
                        {
                            var parent = im.transform.parent;
                            Logger.LogInfo($"ICON-IMG: '{im.gameObject.name}' sprite={sn} color={im.color} parent='{parent?.name}' gparent='{parent?.parent?.name}'");
                            found++;
                        }
                    }
                Logger.LogInfo($"ICON HUNT: {found} Icon_* objects");
                continue;
            }
            if (lower == "galaxy:comps")
            {
                var ss3 = FindStorySector();
                var pl = ss3?.planets;
                if (pl != null)
                {
                    var container = pl.transform.GetChild(0);
                    for (int i = 0; i < container.childCount && i < 3; i++)
                    {
                        var c = container.GetChild(i);
                        var comps = c.GetComponents<Component>();
                        var names = new List<string>();
                        foreach (var cm in comps)
                            if (cm != null) names.Add(cm.GetIl2CppType().Name);
                        Logger.LogInfo($"COMPS: '{c.name}': {string.Join(", ", names)}");
                    }
                }
                continue;
            }
            if (lower.StartsWith("galaxy:tree"))
            {
                // galaxy:tree[:depth]
                int depth = 4;
                var idx = line.IndexOf(':', 7);
                if (idx > 0) int.TryParse(line.Substring(idx + 1), out depth);
                var ss2 = FindStorySector();
                if (ss2?.planets != null)
                    DumpTree(ss2.planets.transform, 0, depth);
                else
                    Logger.LogWarning("galaxy:tree - no StorySector/planets");
                continue;
            }
            if (lower.StartsWith("galaxy:tint:"))
            {
                // galaxy:tint:<objectiveIndex>:<r>,<g>,<b>  (0-1 floats)
                var parts = line.Substring(12).Split(':');
                if (parts.Length == 2)
                {
                    var rgb = parts[1].Split(',');
                    if (int.TryParse(parts[0], out var oi) && rgb.Length == 3 &&
                        float.TryParse(rgb[0], out var rr) && float.TryParse(rgb[1], out var gg) && float.TryParse(rgb[2], out var bb))
                        TintObjectiveIcon(oi, new Color(rr, gg, bb, 1f));
                }
                continue;
            }
            if (lower == "ada:close")
            {
                var logs = Resources.FindObjectsOfTypeAll<ADAMessageLog>();
                bool closed = false;
                if (logs != null)
                    foreach (var lg in logs)
                        if (IsAlive(lg)) { lg.Close(); closed = true; }
                if (closed)
                    Logger.LogInfo("ADA log closed");
                else
                    Logger.LogWarning("ada:close - no ADAMessageLog found");
                continue;
            }

            if (!Setters.ContainsKey(lower))
            {
                Logger.LogWarning($"Unknown unit in unlock file: '{lower}'");
                continue;
            }
            if (Allowed.Add(lower))
            {
                Logger.LogInfo($"LIVE UNLOCK: {lower}");
                _paneRefreshCountdown = 1;
            }
        }
    }

    private static void BootMission(string fileName)
    {
        if (!MissionAllowed(fileName))
        {
            Logger.LogInfo($"MISSION BLOCKED: boot of '{fileName}' denied by gate");
            return;
        }
        Logger.LogInfo($"BOOT: LoadingScreen.LoadGame('{fileName}')");
        GameSpace.specifierToApply = fileName;
        GameSpace.titleToApply = fileName;
        GameSpace.guidToApply = "";
        LoadingScreen.LoadGame(fileName, true, false, GameSpace.CATEGORY.FARSITE, -1);
        Logger.LogInfo("BOOT: LoadGame called");
    }

    private static StorySector FindStorySector()
    {
        var all = Resources.FindObjectsOfTypeAll<StorySector>();
        if (all == null) return null;
        foreach (var s in all)
            if (IsAlive(s)) return s;
        return null;
    }

    private static void GalaxyDump()
    {
        var ss = FindStorySector();
        if (ss == null)
        {
            Logger.LogWarning("galaxy:dump - no StorySector found (be at the story select screen)");
            return;
        }
        Logger.LogInfo($"GALAXY: planetShown={ss.planetShown} title='{ss.missionTitle?.text}' " +
                       $"completeText={(ss.completeText != null && ss.completeText.activeSelf)}");
        var objs = ss.objectives;
        if (objs != null)
        {
            Logger.LogInfo($"GALAXY: {objs.Length} objective icon slots");
            for (int i = 0; i < objs.Length; i++)
            {
                var go = objs[i];
                if (go == null) { Logger.LogInfo($"GALAXY:   [{i}] null"); continue; }
                string comps = "";
                try
                {
                    var img = go.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) comps += $" Image(color={img.color}, sprite={img.sprite?.name})";
                }
                catch { }
                Logger.LogInfo($"GALAXY:   [{i}] '{go.name}' active={go.activeSelf}{comps}");
            }
        }
        var planets = ss.planets;
        if (planets != null)
        {
            var t = planets.transform;
            Logger.LogInfo($"GALAXY: planets container '{planets.name}' children={t.childCount}");
            for (int i = 0; i < t.childCount && i < 25; i++)
            {
                var c = t.GetChild(i);
                Logger.LogInfo($"GALAXY:   planet[{i}] '{c.name}' active={c.gameObject.activeSelf}");
            }
        }
    }

    // Interactive Archipelago login panel - real input fields + clickable
    // button (still not wired to any network; proves UI interactivity).
    private static GameObject _apPanel;
    private static TMPro.TextMeshProUGUI _apStatus;
    private static TMPro.TMP_InputField _apServer, _apSlot, _apPass;

    private static void BuildApPanel()
    {
        if (_apPanel != null) { _apPanel.SetActive(!_apPanel.activeSelf); return; }
        Canvas host = null;
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
        if (canvases != null)
            foreach (var cv in canvases)
                if (cv != null && cv.isRootCanvas) { host = cv; break; }
        if (host == null) { Logger.LogWarning("menu:panel - no host canvas found"); return; }
        Logger.LogInfo($"MENU: using host canvas '{host.gameObject.name}'");

        TMPro.TMP_FontAsset fontAsset = null;
        var fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
        if (fonts != null && fonts.Length > 0) fontAsset = fonts[0];

        _apPanel = new GameObject("APPanel");
        _apPanel.transform.SetParent(host.transform, false);
        _apPanel.transform.SetAsLastSibling();
        var img = _apPanel.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.02f, 0.08f, 0.15f, 0.92f);
        var rt = _apPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(30f, 120f);
        rt.sizeDelta = new Vector2(360f, 330f);

        TMPro.TextMeshProUGUI MakeText(Transform parent, string txt, float size, Color c)
        {
            var go = new GameObject("APText");
            go.transform.SetParent(parent, false);
            var t2 = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (fontAsset != null) t2.font = fontAsset;
            t2.text = txt;
            t2.fontSize = size;
            t2.color = c;
            t2.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            return t2;
        }

        void Place(RectTransform r, float y, float h)
        {
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-30f, h);
        }

        TMPro.TMP_InputField MakeInput(string placeholder, float y, string initial)
        {
            var box = new GameObject("APInput");
            box.transform.SetParent(_apPanel.transform, false);
            box.SetActive(false);   // defer Awake until fully wired (caret setup)
            var bi = box.AddComponent<UnityEngine.UI.Image>();
            bi.color = new Color(0.05f, 0.18f, 0.3f, 1f);
            Place(box.GetComponent<RectTransform>(), y, 32f);
            var field = box.AddComponent<TMPro.TMP_InputField>();

            var area = new GameObject("TextArea");
            area.transform.SetParent(box.transform, false);
            var art = area.AddComponent<RectTransform>();
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(10f, 4f); art.offsetMax = new Vector2(-10f, -4f);
            area.AddComponent<UnityEngine.UI.RectMask2D>();

            var ph = MakeText(area.transform, placeholder, 16f, new Color(0.5f, 0.62f, 0.72f, 1f));
            var phr = ph.GetComponent<RectTransform>();
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = Vector2.zero; phr.offsetMax = Vector2.zero;

            var txt = MakeText(area.transform, "", 16f, new Color(0.9f, 0.96f, 1f, 1f));
            var txr = txt.GetComponent<RectTransform>();
            txr.anchorMin = Vector2.zero; txr.anchorMax = Vector2.one;
            txr.offsetMin = Vector2.zero; txr.offsetMax = Vector2.zero;

            field.textViewport = art;
            field.textComponent = txt;
            field.placeholder = ph;
            field.text = initial;
            if (fontAsset != null) field.fontAsset = fontAsset;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = new Color(0.9f, 0.96f, 1f, 1f);
            field.caretBlinkRate = 0.85f;
            field.selectionColor = new Color(0.2f, 0.5f, 0.9f, 0.5f);
            field.interactable = true;
            box.SetActive(true);    // now Awake/OnEnable run with viewport assigned
            return field;
        }

        var title = MakeText(_apPanel.transform, "ARCHIPELAGO", 24f, new Color(0.4f, 0.8f, 1f, 1f));
        Place(title.GetComponent<RectTransform>(), -12f, 34f);
        var tr = title.GetComponent<RectTransform>();
        tr.anchoredPosition = new Vector2(15f, -12f);

        _apServer = MakeInput("server:port", -52f, "archipelago.gg:38281");
        _apSlot = MakeInput("slot name", -94f, "");
        _apPass = MakeInput("password", -136f, "");
        _apPass.contentType = TMPro.TMP_InputField.ContentType.Password;

        var btn = new GameObject("APConnect");
        btn.transform.SetParent(_apPanel.transform, false);
        var bimg = btn.AddComponent<UnityEngine.UI.Image>();
        bimg.color = new Color(0.1f, 0.5f, 0.2f, 1f);
        Place(btn.GetComponent<RectTransform>(), -184f, 40f);
        var button = btn.AddComponent<UnityEngine.UI.Button>();
        var blabel = MakeText(btn.transform, "CONNECT", 20f, Color.white);
        blabel.alignment = TMPro.TextAlignmentOptions.Center;
        var blr = blabel.GetComponent<RectTransform>();
        blr.anchorMin = Vector2.zero; blr.anchorMax = Vector2.one;
        blr.offsetMin = Vector2.zero; blr.offsetMax = Vector2.zero;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)OnApConnectClicked);

        var auto = MakeText(_apPanel.transform, "[x] Auto-connect", 16f, new Color(0.75f, 0.85f, 0.95f, 1f));
        Place(auto.GetComponent<RectTransform>(), -238f, 30f);
        auto.GetComponent<RectTransform>().anchoredPosition = new Vector2(15f, -238f);

        _apStatus = MakeText(_apPanel.transform, "Status: not connected", 16f, new Color(1f, 0.7f, 0.3f, 1f));
        Place(_apStatus.GetComponent<RectTransform>(), -272f, 30f);
        _apStatus.GetComponent<RectTransform>().anchoredPosition = new Vector2(15f, -272f);

        Logger.LogInfo("MENU: interactive AP panel created");
    }

    private static void OnApConnectClicked()
    {
        var server = _apServer != null ? _apServer.text : "?";
        var slot = _apSlot != null ? _apSlot.text : "?";
        var pass = _apPass != null ? _apPass.text : "";
        Logger.LogInfo($"AP CONNECT CLICKED: server='{server}' slot='{slot}' pass={(pass.Length > 0 ? "***" : "(none)")}");
        if (_apStatus != null)
        {
            _apStatus.text = $"Status: connecting to {server} as {slot}... (mock)";
            _apStatus.color = new Color(0.5f, 0.9f, 0.5f, 1f);
        }
    }

    private static void DumpTree2(Transform t, int depth, int maxDepth)
    {
        if (t == null || depth > maxDepth) return;
        string info = "";
        try
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) info += $" MR(mat='{mr.material?.name}')";
        }
        catch { }
        try
        {
            var tmp = t.GetComponent<TMPro.TextMeshPro>();
            if (tmp != null) info += $" TMP('{tmp.text}',{tmp.color})";
        }
        catch { }
        var comps2 = t.GetComponents<Component>();
        var cn2 = new List<string>();
        foreach (var cm in comps2) if (cm != null) cn2.Add(cm.GetIl2CppType().Name);
        Logger.LogInfo($"TREE2: {new string(' ', depth * 2)}'{t.name}' active={t.gameObject.activeSelf} [{string.Join(",", cn2)}]{info}");
        for (int i = 0; i < t.childCount; i++)
            DumpTree2(t.GetChild(i), depth + 1, maxDepth);
    }

    private static void DumpTree(Transform t, int depth, int maxDepth)
    {
        if (t == null || depth > maxDepth) return;
        string info = "";
        try
        {
            var img = t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) info += $" Image(sprite={img.sprite?.name},color={img.color})";
        }
        catch { }
        try
        {
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null) info += $" SpriteRenderer(sprite={sr.sprite?.name},color={sr.color})";
        }
        catch { }
        try
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) info += $" MeshRenderer(mat={mr.material?.name})";
        }
        catch { }
        Logger.LogInfo($"TREE: {new string(' ', depth * 2)}'{t.name}' active={t.gameObject.activeSelf}{info}");
        for (int i = 0; i < t.childCount; i++)
            DumpTree(t.GetChild(i), depth + 1, maxDepth);
    }

    private static void TintObjectiveIcon(int index, Color color)
    {
        var ss = FindStorySector();
        var objs = ss?.objectives;
        if (objs == null || index < 0 || index >= objs.Length || objs[index] == null)
        {
            Logger.LogWarning($"galaxy:tint - bad index {index}");
            return;
        }
        try
        {
            var img = objs[index].GetComponent<UnityEngine.UI.Image>();
            if (img != null)
            {
                img.color = color;
                Logger.LogInfo($"GALAXY: tinted objective [{index}] to {color}");
            }
            else Logger.LogWarning($"galaxy:tint - no Image on [{index}]");
        }
        catch (Exception e) { Logger.LogWarning($"galaxy:tint failed: {e.Message}"); }
    }

    private static bool _ernDeny;
    private static int _ernDenyCountdown;
    private static int _lastErnCount = -1;

    private static void EnforceErnDeny()
    {
        if (--_ernDenyCountdown > 0)
            return;
        _ernDenyCountdown = 30;
        int cnt = -1;
        try { cnt = UnitManager.GetAvailableERNCount(); } catch { }
        if (cnt != _lastErnCount)
        {
            Logger.LogInfo($"ERN COUNT: {_lastErnCount} -> {cnt}");
            _lastErnCount = cnt;
        }
        if (!_ernDeny)
            return;
        try
        {
            var erns = UnityEngine.Object.FindObjectsOfType<ERN>();
            if (erns == null) return;
            foreach (var e in erns)
            {
                if (e == null) continue;
                bool docked = false;
                try { docked = e.IsDocked(); } catch { }
                if (docked) continue;
                string st = "?";
                try { st = e.state.ToString(); } catch { }
                try
                {
                    e.DestroyUnit(true);
                    Logger.LogInfo($"ERN DENIED: destroyed free ERN (state={st})");
                }
                catch (Exception ex) { Logger.LogWarning($"ERN deny destroy failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Logger.LogWarning($"ERN deny scan failed: {ex.Message}"); }
    }

    private static void ErnStatus()
    {
        var gs = GameSpace.instance;
        var bum = gs == null ? null : gs.buildUnitManager;
        bool portalAvail = false;
        try { portalAvail = bum != null && bum.ernPortalAvailable; } catch { }
        int cnt = -1;
        try { cnt = UnitManager.GetAvailableERNCount(); }
        catch (Exception e) { Logger.LogWarning($"GetAvailableERNCount: {e.Message}"); }
        Logger.LogInfo($"ERNSTATUS: portalAvailable={portalAvail} availableCount={cnt} deny={_ernDeny}");
        try
        {
            var erns = UnityEngine.Object.FindObjectsOfType<ERN>();
            Logger.LogInfo($"ERNSTATUS: {(erns == null ? 0 : erns.Length)} ERN unit(s)");
            if (erns != null)
                foreach (var e in erns)
                {
                    string st = "?", pos = "?";
                    bool buried = false, docked = false, avail = false;
                    try { st = e.state.ToString(); } catch { }
                    try { buried = e.IsBuried(); } catch { }
                    try { docked = e.IsDocked(); } catch { }
                    try { avail = e.IsAvailable(); } catch { }
                    try { pos = e.transform.position.ToString(); } catch { }
                    Logger.LogInfo($"ERNSTATUS:   state={st} buried={buried} docked={docked} available={avail} pos={pos}");
                }
        }
        catch (Exception e) { Logger.LogWarning($"ern:status ERN scan failed: {e.Message}"); }
        try
        {
            var ifaces = UnityEngine.Object.FindObjectsOfType<ERNInterface>();
            Logger.LogInfo($"ERNSTATUS: {(ifaces == null ? 0 : ifaces.Length)} ERNInterface unit(s)");
        }
        catch { }
    }

    private static int _ernPending;
    private static int _ernGrantCountdown;

    private static void ProcessErnGrants(GameSpace gs)
    {
        if (_ernPending <= 0)
            return;
        if (--_ernGrantCountdown > 0)
            return;
        _ernGrantCountdown = 30;
        CommandBase cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb == null || !IsAlive(cb))
        {
            Logger.LogInfo($"ERN GRANT: {_ernPending} pending - waiting for rift lab");
            return;
        }
        var pos = cb.transform.position + new Vector3(4f + 3f * _ernPending, 0f, 6f);
        try
        {
            var u = UnitManager.CreateUnitAtPosition("ern", pos);
            if (u != null)
            {
                _ernPending--;
                Logger.LogInfo($"ERN GRANT: spawned near rift lab at {pos}, {_ernPending} still pending");
            }
            else Logger.LogWarning("ERN GRANT: CreateUnitAtPosition returned null");
        }
        catch (Exception e) { Logger.LogWarning($"ERN GRANT failed: {e.Message}"); }
    }

    private static void ErnSpawn(string[] nameCandidates)
    {
        UnitManager anchor = null;
        try
        {
            var gsA = GameSpace.instance;
            var cbA = gsA == null ? null : gsA.commandBase;
            if (cbA != null && IsAlive(cbA)) anchor = cbA;
        }
        catch { }
        try
        {
            var units = UnityEngine.Object.FindObjectsOfType<UnitManager>();
            if (units != null)
                foreach (var u in units)
                {
                    if (u == null) continue;
                    string tn = "";
                    try { tn = u.GetIl2CppType().Name; } catch { }
                    if (tn.IndexOf("Rift", StringComparison.OrdinalIgnoreCase) >= 0) { anchor = u; break; }
                    if (anchor == null) anchor = u;
                }
        }
        catch { }
        if (anchor == null)
        {
            Logger.LogWarning("ERN SPAWN: no anchor unit found (not in a mission?)");
            return;
        }
        var pos = anchor.transform.position + new Vector3(8f, 0f, 8f);
        foreach (var nm in nameCandidates)
        {
            try
            {
                var u = UnitManager.CreateUnitAtPosition(nm, pos);
                if (u != null)
                {
                    string tn = "?";
                    try { tn = u.GetIl2CppType().Name; } catch { }
                    Logger.LogInfo($"ERN SPAWN OK: '{nm}' -> type={tn} pos={pos}");
                    return;
                }
                Logger.LogInfo($"ERN SPAWN: '{nm}' returned null");
            }
            catch (Exception e) { Logger.LogWarning($"ERN SPAWN '{nm}' failed: {e.Message}"); }
        }
        Logger.LogWarning("ERN SPAWN: all name candidates failed");
    }

    private static void PaneExperiment(string what)
    {
        var panes = AllPanes(true);
        Logger.LogInfo($"PANE EXPERIMENT: {what} on {panes.Count} pane(s)");
        foreach (var pane in panes)
        {
            switch (what)
            {
                case "refresh": pane.Refresh(); break;
                case "setenabled": pane.SetEnabledButtons(); break;
                case "show": pane.Show(true); break;
                case "toggle":
                    pane.gameObject.SetActive(false);
                    pane.gameObject.SetActive(true);
                    break;
                default:
                    Logger.LogWarning($"unknown pane experiment '{what}'");
                    return;
            }
        }
        Logger.LogInfo($"PANE EXPERIMENT: {what} done");
    }

    private static void DumpButtons()
    {
        var panes = AllPanes(true);
        Logger.LogInfo($"DUMP: {panes.Count} UnitBuildPane instance(s)");
        foreach (var pane in panes)
        {
            string pname = "?";
            bool pactive = false;
            try { pname = pane.gameObject.name; } catch { }
            try { pactive = pane.gameObject.activeInHierarchy; } catch { }
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BuildButton> buttons = null;
            try { buttons = pane.GetBuildButtons(); } catch { }
            var count = buttons == null ? 0 : buttons.Length;
            Logger.LogInfo($"DUMP: pane '{pname}' active={pactive} buttons={count}");
            if (buttons == null) continue;
            foreach (var b in buttons)
            {
                if (b == null) continue;
                string unit = "?";
                bool canBuild = false, enabled = false, active = false;
                try { unit = b.unit; } catch { }
                try { canBuild = b.CanBuild(); } catch { }
                try { enabled = b.IsBuildUnitEnabled(); } catch { }
                try { active = b.gameObject.activeSelf; } catch { }
                Logger.LogInfo($"DUMP:   unit='{unit}' active={active} canBuild={canBuild} enabled={enabled}");
            }
        }
    }

    private static bool RefreshPane()
    {
        var lp = FindLeftPane();
        if (lp == null)
            return false;
        NativeRefresh("item change", false);
        ResyncStrip(lp);
        return true;
    }

    // The five pane GameObjects are stacked in the same screen space; the
    // game keeps only the selected tab's pane active. Our no-flash reveal
    // activated ALL of them, so any pane that later gained buttons rendered
    // on top of the struct tab. Enforce the invariant: exactly one pane
    // active - the one matching the selected tab.
    private static void ResyncStrip(LeftPane lp, UnitBuildPane forcedTarget = null)
    {
        // Toggles are not group-managed; prefer struct on ambiguity.
        UnitBuildPane target = forcedTarget;
        if (target == null)
        {
            target = lp.structUnitBuildPane;
            if (lp.structTab != null && lp.structTab.isOn) target = lp.structUnitBuildPane;
            else if (lp.weaponTab != null && lp.weaponTab.isOn) target = lp.weaponUnitBuildPane;
            else if (lp.airTab != null && lp.airTab.isOn) target = lp.airUnitBuildPane;
            else if (lp.specialTab != null && lp.specialTab.isOn) target = lp.specialUnitBuildPane;
            else if (lp.customTab != null && lp.customTab.isOn) target = lp.customUnitBuildPane;
        }
        // Il2Cpp interop returns a FRESH wrapper per property access, so
        // managed reference equality between wrappers is ALWAYS false.
        // Compare native pointers instead.
        var targetPtr = target != null ? target.Pointer : System.IntPtr.Zero;
        foreach (var pn in new[] { lp.structUnitBuildPane, lp.weaponUnitBuildPane,
                                   lp.airUnitBuildPane, lp.specialUnitBuildPane,
                                   lp.customUnitBuildPane })
        {
            if (pn != null)
                pn.gameObject.SetActive(pn.Pointer == targetPtr);
        }
        Logger.LogInfo("Pane active-state resynced (single pane visible)");
    }
}
