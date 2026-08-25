using System;
using System.IO;
using BepInEx;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Config-gated file-command channel for hands-free testing (the batteries
/// write to BepInEx/cw4ap-commands.txt). Off by default; never enabled for
/// players. Runs on the main thread (called from ModCore.Tick).
/// </summary>
public sealed class DebugChannel
{
    public static string FilePath => System.IO.Path.Combine(Paths.GameRootPath, "BepInEx", "cw4ap-commands.txt");

    private DateTime _lastWrite = DateTime.MinValue;
    private int _pollCountdown;

    public void Tick()
    {
        if (--_pollCountdown > 0)
            return;
        _pollCountdown = 30;
        Poll();
    }

    private void Poll()
    {
        var path = FilePath;
        if (!System.IO.File.Exists(path))
            return;
        var stamp = System.IO.File.GetLastWriteTimeUtc(path);
        if (stamp == _lastWrite)
            return;
        _lastWrite = stamp;

        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            try { Handle(line); }
            catch (Exception e) { ModCore.Log.LogWarning($"debug '{line}' failed: {e.Message}"); }
        }
    }

    private void Handle(string line)
    {
        var lower = line.ToLowerInvariant();

        if (lower == "connect") { ModCore.Connect(); return; }
        if (lower == "disconnect") { ModCore.Client.Disconnect(); return; }
        if (lower == "dump") { Dump(); return; }

        if (lower.StartsWith("item:"))
        {
            // Local fake-receive to exercise appliers without a server.
            var name = line.Substring(5).Trim();
            ModCore.Client.State.ReceiveItem(name);
            ModCore.Log.LogInfo($"DEBUG fake item: {name}");
            return;
        }
        if (lower.StartsWith("check:"))
        {
            var loc = line.Substring(6).Trim();
            var was = ModCore.Client.State.MarkChecked(loc, ModCore.Client.Connected);
            if (was)
                ModCore.Client.SendChecks(new[] { loc });
            ModCore.Log.LogInfo($"DEBUG check: {loc} ({(ModCore.Client.Connected ? "sent" : "queued")})");
            return;
        }
        if (lower.StartsWith("boot:")) { Boot(line.Substring(5).Trim()); return; }
        if (lower.StartsWith("objective:")) { AcquireObjective(line.Substring(10).Trim()); return; }
        if (lower == "win") { Win(); return; }
        if (lower == "ada:close") { CloseAda(); return; }
        if (lower == "tracker:dump") { TrackerDump(); return; }
        if (lower == "units") { UnitsDump(); return; }
        if (lower == "story:open") { StoryOpen(); return; }
        if (lower.StartsWith("clickplanet:")) { ClickPlanet(line.Substring(12).Trim()); return; }

        ModCore.Log.LogWarning($"DEBUG unknown command: {line}");
    }

    private static void Boot(string specifier)
    {
        if (!MissionGate.Allowed(specifier))
        {
            ModCore.Log.LogInfo($"DEBUG boot BLOCKED: '{specifier}' locked");
            return;
        }
        GameSpace.specifierToApply = specifier;
        GameSpace.titleToApply = specifier;
        GameSpace.guidToApply = "";
        LoadingScreen.LoadGame(specifier, true, false, GameSpace.CATEGORY.FARSITE, -1);
        ModCore.Log.LogInfo($"DEBUG boot: {specifier}");
    }

    private static void AcquireObjective(string arg)
    {
        if (!int.TryParse(arg, out var idx)) return;
        var w = GameSpace.instance?.world;
        if (w == null) { ModCore.Log.LogWarning("objective: no world"); return; }
        w.AcquireMissionObjective(idx, true);
        ModCore.Log.LogInfo($"DEBUG objective {idx} acquired");
    }

    private static void Win()
    {
        var w = GameSpace.instance?.world;
        if (w?.missionObjectives == null) { ModCore.Log.LogWarning("win: no world"); return; }
        for (int i = 0; i < w.missionObjectives.Length; i++)
            w.AcquireMissionObjective(i, true);
        ModCore.Log.LogInfo("DEBUG win: all objectives acquired");
    }

    private static void CloseAda()
    {
        var logs = UnityEngine.Resources.FindObjectsOfTypeAll<ADAMessageLog>();
        if (logs == null) return;
        foreach (var lg in logs)
            if (GameUtil.IsAlive(lg))
            {
                try { lg.Close(); ModCore.Log.LogInfo("DEBUG ada closed"); }
                catch { }
            }
    }

    private static void TrackerDump()
    {
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        int count = planets == null ? 0 : planets.Length;
        ModCore.Log.LogInfo($"TRACKER: {count} planets found");
        if (planets == null) return;
        var state = ModCore.Client.State;
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            var title = TrackerView.TitleOf(p);
            var mission = TrackerView.MissionByTitle(title);
            if (mission == 0) continue;
            var st = CW4Archipelago.Core.TrackerRules.MissionStatus(state, mission);
            ModCore.Log.LogInfo($"TRACKER: {CW4Archipelago.Core.MissionRules.Specifier(mission)} '{title}' status={st}");
        }
    }

    private void Dump()
    {
        var s = ModCore.Client.State;
        ModCore.Log.LogInfo($"DEBUG DUMP: status={ModCore.Client.StatusText} seed='{s.Seed}' slot='{s.Slot}' " +
                            $"items={s.ReceivedItems.Count} checked={s.CheckedLocations.Count} pending={s.PendingChecks.Count}");
    }

    private static void UnitsDump()
    {
        var allowed = CW4Archipelago.Core.UnitRules.AllowedUnits(ModCore.Client.State);
        int structButtons = -1;
        try
        {
            var lp = GameUtil.FindLeftPane();
            var sp = lp?.structUnitBuildPane;
            if (sp != null && sp.gameObject.activeSelf)
            {
                var b = sp.GetBuildButtons();
                structButtons = b == null ? 0 : b.Length;
            }
        }
        catch { }
        ModCore.Log.LogInfo($"DEBUG UNITS: allowed=[{string.Join(",", allowed)}] structButtons={structButtons}");
    }

    private static void StoryOpen()
    {
        var gg = GameGalaxy.instance;
        var btn = gg?.farsiteButton?.GetComponent<UnityEngine.UI.Button>();
        if (btn != null) { btn.onClick.Invoke(); ModCore.Log.LogInfo("DEBUG story:open"); }
        else ModCore.Log.LogWarning("story:open: no farsite button");
    }

    // Invoke a planet's click handler to test the locked-click block. Reports
    // whether a mission popup is showing afterwards.
    private static void ClickPlanet(string arg)
    {
        if (!CW4Archipelago.Core.MissionRules.TryParseSpecifier(arg, out var mission))
        {
            ModCore.Log.LogWarning($"clickplanet: bad specifier '{arg}'");
            return;
        }
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null) { ModCore.Log.LogWarning("clickplanet: no planets"); return; }
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            var title = TrackerView.TitleOf(p);
            var m = TrackerView.MissionByTitle(title);
            if (m != mission) continue;
            bool before = PopupShowing();
            try { p.OnPointerClick(null); } catch (Exception e) { ModCore.Log.LogWarning($"clickplanet invoke: {e.Message}"); }
            bool after = PopupShowing();
            ModCore.Log.LogInfo($"CLICKPLANET {arg} '{title}' unlocked={CW4Archipelago.Core.MissionRules.IsUnlocked(ModCore.Client.State, m)} popupBefore={before} popupAfter={after}");
            return;
        }
        ModCore.Log.LogWarning($"clickplanet: mission {arg} not found");
    }

    private static bool PopupShowing()
    {
        try
        {
            var gmp = UnityEngine.Object.FindObjectOfType<GalaxyMissionPanel>();
            return gmp != null && gmp.gameObject.activeInHierarchy;
        }
        catch { return false; }
    }
}
