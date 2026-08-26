using System;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

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
        if (lower == "minimap:dump") { MinimapDump(); return; }
        if (lower == "hud:dump") { HudDump(); return; }
        if (lower == "menu:dump") { MenuDump(); return; }
        if (lower.StartsWith("msgbox:set")) { MsgBoxSet(line.Substring(10).Trim()); return; }
        if (lower.StartsWith("shot:")) { Shot(line.Substring(5).Trim()); return; }
        if (lower == "canvas:dump") { CanvasDump(); return; }
        if (lower == "msgbox:dump")
        {
            ModCore.Log.LogInfo($"MSGBOX DUMP: history={ModCore.MessageHistory.Count}");
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
        if (lower.StartsWith("toast:")) { ModCore.EnqueueToast(line.Substring(6).Trim()); return; }
        if (lower.StartsWith("limit:")) { LimitDump(line.Substring(6).Trim()); return; }
        if (lower == "ern:status") { ErnStatus(); return; }
        if (lower.StartsWith("gatecheck:")) { GateCheck(line.Substring(10).Trim()); return; }

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

    private static void CanvasDump()
    {
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
        ModCore.Log.LogInfo($"CANVASDUMP: {(canvases == null ? 0 : canvases.Length)} canvases");
        if (canvases != null)
            foreach (var cv in canvases)
            {
                if (cv == null) continue;
                float alpha = -1f;
                try { var cg = cv.GetComponent<CanvasGroup>(); if (cg != null) alpha = cg.alpha; } catch { }
                ModCore.Log.LogInfo($"CANVASDUMP:   '{cv.gameObject.name}' mode={cv.renderMode} " +
                    $"root={cv.isRootCanvas} enabled={cv.isActiveAndEnabled} order={cv.sortingOrder} cgAlpha={alpha}");
            }
        // Where do the build panes live? (a canvas that definitely renders in-mission)
        try
        {
            var lp = GameUtil.FindLeftPane();
            var lpCanvas = lp != null ? lp.GetComponentInParent<Canvas>() : null;
            ModCore.Log.LogInfo($"CANVASDUMP: leftPane canvas='{(lpCanvas != null ? lpCanvas.rootCanvas.gameObject.name : "null")}'");
        }
        catch { }
    }

    private static void MinimapDump()
    {
        var mm = UnityEngine.Object.FindObjectOfType<MiniMap>();
        if (mm == null) { ModCore.Log.LogWarning("minimap:dump - no MiniMap found"); return; }
        try
        {
            // Walk from the MiniMap component's transform up to the canvas,
            // logging each RectTransform's on-screen corners, to find the
            // HUD panel anchored bottom-right (the on-screen minimap rect).
            var t = mm.transform;
            int depth = 0;
            var corners = new UnityEngine.Vector3[4];
            while (t != null && depth < 12)
            {
                var rt = t.TryCast<RectTransform>();
                string info = $"name='{t.gameObject.name}'";
                if (rt != null)
                {
                    rt.GetWorldCorners(corners);
                    info += $" anchoredPos={rt.anchoredPosition} sizeDelta={rt.sizeDelta}" +
                            $" anchorMin={rt.anchorMin} anchorMax={rt.anchorMax}" +
                            $" screenBL={corners[0]} screenTR={corners[2]}";
                }
                var cv = t.GetComponent<Canvas>();
                if (cv != null) info += $" [Canvas renderMode={cv.renderMode} root={cv.isRootCanvas}]";
                ModCore.Log.LogInfo($"MINIMAP[{depth}]: {info}");
                if (cv != null && cv.isRootCanvas) break;
                t = t.parent;
                depth++;
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"minimap:dump failed: {e.Message}"); }
    }

    // Screen-space rect of a RectTransform, computed via TransformPoint (the
    // Vector3[] GetWorldCorners marshalling returns zeros under IL2CPP).
    private static bool ScreenRect(RectTransform rt, Camera? cam, out Vector2 bl, out Vector2 tr)
    {
        bl = Vector2.zero; tr = Vector2.zero;
        if (rt == null) return false;
        var r = rt.rect;
        var wbl = rt.TransformPoint(new Vector3(r.xMin, r.yMin, 0f));
        var wtr = rt.TransformPoint(new Vector3(r.xMax, r.yMax, 0f));
        bl = RectTransformUtility.WorldToScreenPoint(cam, wbl);
        tr = RectTransformUtility.WorldToScreenPoint(cam, wtr);
        return true;
    }

    private static void LogRt(string tag, Transform t, Camera? cam)
    {
        var rt = t.TryCast<RectTransform>();
        string info = $"name='{t.gameObject.name}' active={t.gameObject.activeInHierarchy}";
        if (rt != null && ScreenRect(rt, cam, out var bl, out var tr))
            info += $" size={rt.sizeDelta} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax}" +
                    $" screen=({bl.x:F0},{bl.y:F0})..({tr.x:F0},{tr.y:F0}) w={(tr.x - bl.x):F0} h={(tr.y - bl.y):F0}";
        ModCore.Log.LogInfo($"{tag}: {info}");
    }

    private static void DumpSubtree(Transform t, Camera? cam, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            LogRt($"HUDDUMP{new string('.', depth)}", c, cam);
            DumpSubtree(c, cam, depth + 1, maxDepth);
        }
    }

    // Dump the menu canvas scaler + FARSITE button rect + AP panel rect, so the
    // login panel can be parented to the same scaler and positioned clear of it.
    private static void MenuDump()
    {
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (cv == null || !cv.isRootCanvas) continue;
            var sc = cv.GetComponent<CanvasScaler>();
            string s = sc != null
                ? $"mode={sc.uiScaleMode} ref={sc.referenceResolution} match={sc.matchWidthOrHeight:F2}"
                : "no-scaler";
            ModCore.Log.LogInfo($"MENUDUMP canvas '{cv.gameObject.name}' mode={cv.renderMode} scaleFactor={cv.scaleFactor:F3} {s}");
        }
        var gg = GameGalaxy.instance;
        if (gg != null && gg.farsiteButton != null)
        {
            var fc = gg.farsiteButton.GetComponentInParent<Canvas>();
            Camera? cam = (fc != null && fc.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? fc.rootCanvas.worldCamera : null;
            ModCore.Log.LogInfo($"MENUDUMP farsite canvas='{(fc != null ? fc.rootCanvas.gameObject.name : "?")}'");
            LogRt("MENUDUMP farsite", gg.farsiteButton.transform, cam);
            var frt = gg.farsiteButton.transform.TryCast<RectTransform>();
            if (frt != null)
                ModCore.Log.LogInfo($"MENUDUMP farsite local: anchoredPos={frt.anchoredPosition} sizeDelta={frt.sizeDelta} anchorMin={frt.anchorMin} anchorMax={frt.anchorMax}");
        }
        var panel = GameObject.Find("CW4ApPanel");
        if (panel != null)
        {
            var pc = panel.GetComponentInParent<Canvas>();
            Camera? cam = (pc != null && pc.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? pc.rootCanvas.worldCamera : null;
            ModCore.Log.LogInfo($"MENUDUMP panel canvas='{(pc != null ? pc.rootCanvas.gameObject.name : "?")}'");
            LogRt("MENUDUMP panel", panel.transform, cam);
        }
    }

    // Full-framebuffer screenshot from inside the engine (captures the overlay
    // layer AND the whole render, even parts a smaller monitor crops off).
    private static void Shot(string path)
    {
        if (string.IsNullOrEmpty(path))
            path = System.IO.Path.Combine(Paths.GameRootPath, "ap_shot.png");
        try
        {
            UnityEngine.ScreenCapture.CaptureScreenshot(path);
            ModCore.Log.LogInfo($"SHOT: {path}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"shot failed: {e.Message}"); }
    }

    // Live-tune the message box geometry: msgbox:set w=400 h=180 left=6 bottom=20 alpha=0.5
    private static void MsgBoxSet(string arg)
    {
        foreach (var tok in arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = tok.Split('=');
            if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            switch (kv[0].ToLowerInvariant())
            {
                case "w": case "width": ApMessageBox.WidthRef = v; break;
                case "h": case "height": ApMessageBox.BaseHeightRef = v; break;
                case "left": ApMessageBox.LeftInsetRef = v; break;
                case "bottom": ApMessageBox.BottomInsetRef = v; break;
                case "alpha": ApMessageBox.BgAlpha = v; break;
            }
        }
        ModCore.Log.LogInfo($"MSGBOX SET: w={ApMessageBox.WidthRef} h={ApMessageBox.BaseHeightRef} " +
            $"left={ApMessageBox.LeftInsetRef} bottom={ApMessageBox.BottomInsetRef} alpha={ApMessageBox.BgAlpha}");
    }

    // Enumerate the bottom HUD cluster (terrain height, creeper coverage, emit
    // mode) so the message box can be sized to their combined on-screen width.
    private static void HudDump()
    {
        Canvas? ui = null;
        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (cv != null && cv.gameObject.name == "UICanvas") { ui = cv; break; }
        if (ui == null) { ModCore.Log.LogWarning("hud:dump - no UICanvas"); return; }
        Camera? cam = ui.renderMode == RenderMode.ScreenSpaceOverlay ? null : ui.worldCamera;
        try
        {
            var uit = ui.transform;
            ModCore.Log.LogInfo($"HUDDUMP: UICanvas children={uit.childCount} cam={(cam != null ? cam.name : "null")}");
            for (int i = 0; i < uit.childCount; i++)
            {
                var c = uit.GetChild(i);
                LogRt("HUDDUMP child", c, cam);
                string nm = c.gameObject.name.ToUpperInvariant();
                if (nm.Contains("BOTTOM") || nm.Contains("LEFT"))
                    DumpSubtree(c, cam, 1, 4);
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"hud:dump failed: {e.Message}"); }
    }

    private static void LimitDump(string unit)
    {
        var bum = GameSpace.instance?.buildUnitManager;
        if (bum == null) { ModCore.Log.LogWarning("limit: no BuildUnitManager"); return; }
        int lim = -999;
        try { lim = bum.GetBuildCountLimit(unit); } catch { }
        ModCore.Log.LogInfo($"DEBUG LIMIT: {unit}={lim}");
    }

    private static void ErnStatus()
    {
        int avail = -1;
        try { avail = UnitManager.GetAvailableERNCount(); } catch { }
        int ernUnits = -1;
        try { var e = UnityEngine.Object.FindObjectsOfType<ERN>(); ernUnits = e == null ? 0 : e.Length; } catch { }
        ModCore.Log.LogInfo($"DEBUG ERN: availableCount={avail} ernUnits={ernUnits}");
    }

    private static void GateCheck(string spec)
    {
        // Same decision used by both the launch and the save-load gates.
        bool allowed = MissionGate.Allowed(spec);
        ModCore.Log.LogInfo($"DEBUG GATECHECK: '{spec}' allowed={allowed}");
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
