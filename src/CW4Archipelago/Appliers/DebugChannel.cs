using System;
using System.IO;
using System.Linq;
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

    /// <summary>While true, the sim is force-unpaused every frame.
    ///
    /// Test scaffolding, and it exists because unpausing ONCE is not enough: a
    /// mission keeps firing story messages, each of which opens the A.D.A. log
    /// and pauses the game again. A timed measurement that unpauses at the start
    /// silently spends half its samples frozen, and the symptom is subtle -
    /// tickCount keeps advancing while GameSpace.paused reads true.</summary>
    public static bool HoldRunning;

    private void HoldSim()
    {
        if (!HoldRunning) return;
        var gs = GameSpace.instance;
        if (gs == null || GameSpace.editMode) return;
        try
        {
            if (!gs.paused) return;
            var owners = new System.Collections.Generic.List<string>();
            foreach (var o in gs.pauseOwner) owners.Add(o);
            foreach (var o in owners) gs.Pause(o, false);
        }
        catch { }
    }

    public void Tick()
    {
        HoldSim();
        KeyWatch();
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
        System.DateTime stamp;
        try { stamp = System.IO.File.GetLastWriteTimeUtc(path); }
        catch { return; }                       // being written right now
        if (stamp == _lastWrite)
            return;

        // READ FIRST, MARK SECOND, and share the handle.
        //
        // The old order set _lastWrite before reading, so a read that lost the
        // race with a harness write left the file marked as already-seen and
        // the channel silently stopped processing commands - one logged
        // "tick failed: ... being used by another process" and then nothing
        // ever ran again. A harness sending 24 items back to back hit it
        // reliably, and the symptom was a later command "never acknowledged",
        // which reads like the mod hanging rather than one lost read.
        string[] lines;
        try
        {
            using var fs = new System.IO.FileStream(
                path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            using var sr = new System.IO.StreamReader(fs);
            var all = sr.ReadToEnd();
            lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
        catch (System.IO.IOException)
        {
            // Leave _lastWrite alone so the very next tick tries again.
            return;
        }
        _lastWrite = stamp;

        foreach (var raw in lines)
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
        if (lower.StartsWith("say:")) { ModCore.Client.Say(line.Substring(4).Trim()); return; }
        if (lower.StartsWith("showall:")) { ModCore.SetShowAll(line.Substring(8).Trim() == "on"); return; }
        if (lower == "canvas:dump") { CanvasDump(); return; }
        if (lower == "ui:input") { InputDump(); return; }
        if (lower.StartsWith("grant:")) { Grant(line.Substring(6).Trim()); return; }
        if (lower == "items:clear")
        {
            // Wall-testing needs item sets that SHRINK - "mortar + nullifier +
            // terp" is not "the previous set plus terp". item: only ever adds,
            // so without this every candidate run meant restarting the game.
            var st = ModCore.Client.State;
            int had = st.ReceivedItems.Count;
            st.ApplyReceivedItems(new string[0]);
            ModCore.Log.LogInfo($"DEBUG items:clear - dropped {had} received item(s)");
            return;
        }
        if (lower.StartsWith("ui:keys")) { _keyWatch = line.Substring(7).Trim() != "off"; 
            ModCore.Log.LogInfo($"KEYWATCH: {(_keyWatch ? "on" : "off")}"); return; }
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
        if (lower.StartsWith("glyphs:dump")) { GlyphDump(line.Substring(11).Trim()); return; }
        if (lower == "diag:span") { SpanDiag(); return; }
        if (lower.StartsWith("diag:refresh")) { DiagRefresh(line.Substring(12).Trim()); return; }
        if (lower.StartsWith("diag:watch"))
        {
            var arg = line.Substring(10).Trim();
            int secs = int.TryParse(arg, out var v) ? v : 10;
            TrackerDiag.WatchFrames = secs * 60;
            ModCore.Log.LogInfo($"DEBUG diag:watch: armed for ~{secs}s");
            return;
        }
        if (lower == "totem:complete") { TotemComplete(); return; }
        if (lower == "cache:destroy") { CacheDestroy(); return; }
        if (lower == "units") { UnitsDump(); return; }
        if (lower == "story:open") { StoryOpen(); return; }
        if (lower.StartsWith("clickplanet:")) { ClickPlanet(line.Substring(12).Trim()); return; }
        if (lower.StartsWith("toast:")) { ModCore.EnqueueToast(line.Substring(6).Trim()); return; }
        if (lower.StartsWith("limit:")) { LimitDump(line.Substring(6).Trim()); return; }
        if (lower == "ern:status") { ErnStatus(); return; }
        if (lower.StartsWith("finale:")) { Finale(line.Substring(7).Trim()); return; }
        if (lower == "perf")
        {
            ModCore.Log.LogInfo(
                $"DEBUG PERF: tracker recolours={ModCore.TrackerRecolours} " +
                $"totemPokes={LocationWatcher.TotemPokes} cachePokes={LocationWatcher.CachePokes}");
            return;
        }
        if (lower.StartsWith("loc:add ")) { LocAdd(line.Substring(8).Trim()); return; }
        if (lower.StartsWith("gatecheck:")) { GateCheck(line.Substring(10).Trim()); return; }

        if (lower.StartsWith("trap:")) { Trap(line.Substring(5).Trim()); return; }
        if (lower.StartsWith("sim:")) { Sim(line.Substring(4).Trim()); return; }
        if (lower.StartsWith("spawnat:")) { SpawnAt(line.Substring(8).Trim()); return; }
        if (lower.StartsWith("measure:ware")) { MeasureProbe.TimeWares(
            int.TryParse(line.Substring(12).Trim(), out var mw) ? mw : 1800); return; }
        if (lower.StartsWith("spawn:")) { Spawn(line.Substring(6).Trim()); return; }
        // --- ERN port upgrades and the temporary-filler effects ---------
        if (lower == "ern:dump") { UpgradeProbe.Dump(); return; }
        if (lower == "ern:erns") { UpgradeProbe.Erns(); return; }
        if (lower == "ern:stats") { UpgradeProbe.Stats(); return; }
        if (lower == "ern:ui") { UpgradeProbe.Ui(); return; }
        if (lower.StartsWith("ern:cap"))
        {
            var a = line.Substring(7).Trim();
            if (a.Length == 0 || a == "off")
            {
                CW4Archipelago.Core.ErnUpgradeRules.CeilingOverride = null;
                ModCore.Log.LogInfo("ERN cap: override cleared, using the per-upgrade table");
            }
            else if (float.TryParse(a, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var cv))
            {
                CW4Archipelago.Core.ErnUpgradeRules.CeilingOverride = cv;
                ModCore.Log.LogInfo($"ERN cap: override set to {cv:0.###}");
            }
            else ModCore.Log.LogWarning($"ERN cap: cannot parse '{a}'");
            return;
        }

        if (lower == "ern:resources") { UpgradeProbe.Resources(); return; }

        if (lower.StartsWith("measure:move")) { MeasureProbe.TimeMove(
            float.TryParse(line.Substring(12).Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mv) ? mv : 10f); return; }
        if (lower.StartsWith("measure:build")) { MeasureProbe.TimeBuild(line.Substring(13).Trim()); return; }
        if (lower.StartsWith("measure:energy")) { MeasureProbe.TimeEnergy(
            int.TryParse(line.Substring(14).Trim(), out var mt) ? mt : 900); return; }
        if (lower.StartsWith("ern:dumpall")) { UpgradeProbe.DumpAll(line.Substring(11).Trim()); return; }
        if (lower == "ern:free") { UpgradeProbe.FreeErns(); return; }
        if (lower == "ada:clear") { UpgradeProbe.ClearAda(); return; }
        if (lower.StartsWith("ui:text")) { UpgradeProbe.UiText(line.Substring(7).Trim()); return; }
        if (lower.StartsWith("ui:hide ")) { UpgradeProbe.UiHide(line.Substring(8).Trim()); return; }
        if (lower.StartsWith("upgrade:units")) { UpgradeProbe.Units(line.Substring(13).Trim()); return; }
        if (lower.StartsWith("ern:assign "))
        {
            if (int.TryParse(line.Substring(11).Trim(), out var ai)) UpgradeProbe.Assign(ai);
            else ModCore.Log.LogWarning("DEBUG usage: ern:assign <upgrade index 0-5>");
            return;
        }
        if (lower.StartsWith("ern:release "))
        {
            if (int.TryParse(line.Substring(12).Trim(), out var ri)) UpgradeProbe.Release(ri);
            else ModCore.Log.LogWarning("DEBUG usage: ern:release <upgrade index 0-5>");
            return;
        }
        if (lower == "boon:ammo") { BoonEffects.Resupply(); return; }
        if (lower == "boon:shield") { BoonEffects.Shield(); return; }
        if (lower.StartsWith("boon:cache"))
        {
            var t = line.Substring(10).Trim().Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var which = t.Length > 0 ? t[0].ToLowerInvariant() : "bluite";
            int amt = t.Length > 1 && int.TryParse(t[1], out var ca) ? ca : 0;
            // "create" selects CreateProducedWares over SetProducedWares,
            // so a run can measure which write the sim honours.
            bool create = t.Length > 2 && t[2] == "create";
            BoonEffects.ResourceCache(which, amt, create);
            return;
        }
        // Starts a surge WITHOUT going through the received-items path, so the
        // effect can be tested independently of the dispatch that carries it -
        // which is the whole reason this exists: FireBoon sat uncalled for a
        // session and no test could have told the difference.
        if (lower.StartsWith("boon:surge"))
        {
            var a = line.Substring(10).Trim();
            if (int.TryParse(a, out var si) && CW4Archipelago.Core.ErnUpgradeRules.IsValidIndex(si))
                ErnUpgrades.StartSurge(si);
            else
                ModCore.Log.LogWarning(
                    $"boon:surge needs an upgrade index 0-{CW4Archipelago.Core.ErnUpgradeRules.UpgradeNames.Length - 1}"
                    + $" (got '{a}')");
            return;
        }
        if (lower.StartsWith("boon:energy"))
        {
            float.TryParse(line.Substring(11).Trim(), out var frac);
            BoonEffects.EnergyCache(frac);
            return;
        }

        if (lower == "resources:dump") { ResourceDump(); return; }
        if (lower == "resources:zonetest") { ZoneTest(); return; }
        if (lower == "pane:dump") { PaneDump(); return; }
        if (lower == "totems:dump") { TotemDump(); return; }
        if (lower == "counts:dump") { CountsDump(); return; }

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

    /// <summary>Complete one live totem by driving the game's own property.
    ///
    /// The point is that this goes through Totem.set_totemComplete, which is what
    /// TotemCompletePatch hooks - so it exercises the real event path rather than
    /// a stand-in for it, and the totem also genuinely becomes complete, so the
    /// safety poll's count moves too. Between them that is what makes a
    /// double-send observable if one exists.
    ///
    /// Synthetic mouse input does not reach CW4's UI, so capturing a totem by
    /// hand is the one thing no script can do. This is as close as automation
    /// gets; a hands-on run is still the final word.</summary>
    private static void TotemComplete()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("totem:complete: no game space"); return; }
        try
        {
            foreach (var t in gs.totems)
            {
                if (t == null) continue;
                bool done;
                try { done = t.totemComplete; } catch { continue; }
                if (done) continue;
                t.totemComplete = true;
                ModCore.Log.LogInfo("DEBUG totem:complete: one totem completed");
                return;
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"totem:complete failed: {e.Message}"); }
        ModCore.Log.LogInfo("DEBUG totem:complete: no incomplete totem left");
    }

    /// <summary>Who is repainting the mission map, and how often.
    ///
    /// For the flashing-planet problem: a planet alternating between its sphere
    /// and its locked "?" means something writes those visuals by a route the mod
    /// does not hook. The counters say which route and at what rate; the
    /// per-planet lines say what state each planet is actually in right now.</summary>
    private static void SpanDiag()
    {
        ModCore.Log.LogInfo(
            $"DIAG SPAN: refreshes={TrackerDiag.Refreshes} unlockedSets={TrackerDiag.UnlockedSets} " +
            $"paints={TrackerDiag.PaintCalls} visualFixes={TrackerDiag.VisualFixes} " +
            $"frame={UnityEngine.Time.frameCount}");
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null) return;
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            var title = TrackerView.TitleOf(p);
            var mission = TrackerView.MissionByTitle(title);
            if (mission == 0) continue;
            bool want = CW4Archipelago.Core.MissionRules.IsUnlocked(ModCore.Client.State, mission);
            string sphere = "?", locked = "?", objs = "?";
            try { sphere = p.planet == null ? "null" : (p.planet.gameObject.activeSelf ? "ON" : "off"); } catch { }
            try { locked = p.lockedPlanet == null ? "null" : (p.lockedPlanet.gameObject.activeSelf ? "ON" : "off"); } catch { }
            try { objs = p.objectiveContainer == null ? "null" : (p.objectiveContainer.gameObject.activeSelf ? "ON" : "off"); } catch { }
            bool fu = false, un = false;
            try { fu = p.forceUnlocked; } catch { }
            try { un = p.unlocked; } catch { }
            ModCore.Log.LogInfo(
                $"DIAG SPAN: story{mission} '{title}' wantUnlocked={want} forceUnlocked={fu} unlocked={un} " +
                $"sphere={sphere} lockedQ={locked} objectives={objs}");
        }
    }

    /// <summary>Destroy one live info cache, which is what collecting it does.
    ///
    /// This replaced a "cache:take" that called InfoCache.Retrieved instead. That
    /// was measurably the wrong method and is worth remembering: it set the
    /// cache's own `retrieved` flag, moved neither GameSpace.mustCollect nor the
    /// Collect objective, and - proven by a real pickup - is not called on the
    /// pickup path at all. The hook built on it never once fired in play.
    ///
    /// DestroyUnit is the real thing: mustCollect loses its member, so the check
    /// follows, and CacheDestroyedPatch fires (watch cachePokes in "perf").
    /// It is still not a PICKUP - it skips whatever the game does with the
    /// message - so a hands-on collection stays the final word.</summary>
    private static void CacheDestroy()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("cache:destroy: no game space"); return; }
        try
        {
            foreach (var u in gs.mustCollect)
            {
                if (u == null) continue;
                InfoCache? cache = null;
                try { cache = u.GetComponent<InfoCache>(); } catch { }
                if (cache == null) continue;
                cache.DestroyUnit(true);
                ModCore.Log.LogInfo("DEBUG cache:destroy: one cache destroyed");
                return;
            }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"cache:destroy failed: {e.Message}"); }
        ModCore.Log.LogInfo("DEBUG cache:destroy: no collectable cache left");
    }

    /// <summary>Call the game's own Refresh on one planet and report its
    /// objective child count either side.
    ///
    /// This is how a surprising fact was measured and how to re-measure it:
    /// Refresh APPENDS its authored objective markers and never clears the
    /// container, so consecutive calls gave 3 -> 4 -> 5 children. The duplicates
    /// overlap their originals exactly, so they are invisible; TrackerView's
    /// reconcile hides the surplus.
    ///
    /// Note this command therefore ADDS markers as a side effect - it is a
    /// measurement, not something to leave running.</summary>
    private static void DiagRefresh(string title)
    {
        var planet = PlanetByTitle(title);
        if (planet == null) { ModCore.Log.LogWarning($"diag:refresh: no planet '{title}'"); return; }
        try
        {
            var container = planet.objectiveContainer;
            int before = container == null ? -1 : container.childCount;
            planet.Refresh();
            int after = container == null ? -1 : container.childCount;
            ModCore.Log.LogInfo($"DIAG REFRESH: '{title}' objective children {before} -> {after}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"diag:refresh failed: {e.Message}"); }
    }

    private static SpanNetworkPlanet? PlanetByTitle(string title)
    {
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null) return null;
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            if (TrackerView.TitleOf(p).Equals(title, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    /// <summary>Report the ACTUAL colour written onto each objective glyph, read
    /// back off the material.
    ///
    /// Exists because the only previous way to check the tracker's colouring was
    /// a screenshot, which is slow, needs a human eye, and cannot be asserted on
    /// in the battery. The colouring has now broken silently twice - once when
    /// locations became per-instance and ColorGlyphs started building names that
    /// matched nothing, and once when the repaint became event-driven and no
    /// event fired for a location check. Both were invisible to every automated
    /// test and both would have failed this one.
    ///
    /// Optional argument filters by planet title, e.g. "glyphs:dump Ever After".</summary>
    private static void GlyphDump(string filter)
    {
        var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
        if (planets == null) { ModCore.Log.LogInfo("DEBUG GLYPHS: no planets"); return; }
        foreach (var p in planets)
        {
            if (!GameUtil.IsAlive(p)) continue;
            var title = TrackerView.TitleOf(p);
            if (filter.Length > 0 && !title.Equals(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            var mission = TrackerView.MissionByTitle(title);
            if (mission == 0) continue;
            Transform container;
            try { container = p.objectiveContainer; } catch { continue; }
            if (container == null) continue;
            var markers = container.GetComponentsInChildren<SpanNetworkPlanetObjective>(true);
            if (markers == null) continue;
            foreach (var m in markers)
            {
                if (!GameUtil.IsAlive(m)) continue;
                int obj;
                try { obj = m.objective; } catch { continue; }
                string col = "?";
                try { col = NameColor(m.GetComponent<MeshRenderer>().material.GetColor("_color")); }
                catch { }
                // Everything needed to tell a HIDDEN marker from an ABSENT one,
                // and to work out the layout rule the game uses - there is no
                // Unity layout component here, these are world-space quads with
                // hand-set positions, and nothing had ever recorded the spacing.
                string name = "?", act = "?", pos = "?", scale = "?", mat = "?", tex = "?";
                try { name = m.gameObject.name; } catch { }
                try { act = m.gameObject.activeSelf ? "ON" : "off"; } catch { }
                try
                {
                    var lp = m.transform.localPosition;
                    pos = $"({lp.x:0.###},{lp.y:0.###},{lp.z:0.###})";
                }
                catch { }
                try
                {
                    var ls = m.transform.localScale;
                    scale = $"({ls.x:0.###},{ls.y:0.###},{ls.z:0.###})";
                }
                catch { }
                try
                {
                    var mr = m.GetComponent<MeshRenderer>();
                    mat = mr.material.name;
                    var t = mr.material.GetTexture("_MainTexture");
                    tex = t == null ? "none" : t.name;
                }
                catch { }
                ModCore.Log.LogInfo(
                    $"DEBUG GLYPHS: {CW4Archipelago.Core.MissionRules.Specifier(mission)} '{title}' " +
                    $"obj={obj} color={col} name='{name}' active={act} pos={pos} scale={scale} " +
                    $"mat='{mat}' tex='{tex}'");
            }
        }
    }

    /// <summary>Name the tracker colours so the log is assertable, rather than
    /// printing floats a test would have to compare with a tolerance.</summary>
    private static string NameColor(UnityEngine.Color c)
    {
        // Named by COLOUR, not by status: two statuses share green, so a status
        // name would be a guess where the colour is a fact.
        var known = new (string Name, CW4Archipelago.Core.TrackerStatus Status)[]
        {
            ("RED", CW4Archipelago.Core.TrackerStatus.Locked),
            ("YELLOW", CW4Archipelago.Core.TrackerStatus.OutOfLogic),
            ("GREY", CW4Archipelago.Core.TrackerStatus.Done),
            ("GREEN", CW4Archipelago.Core.TrackerStatus.InLogic),
        };
        foreach (var k in known)
        {
            var t = TrackerView.StatusColor(k.Status);
            if (Mathf.Abs(t.r - c.r) < 0.01f && Mathf.Abs(t.g - c.g) < 0.01f
                && Mathf.Abs(t.b - c.b) < 0.01f)
                return k.Name;
        }
        return $"OTHER({c.r:0.00},{c.g:0.00},{c.b:0.00})";
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

    /// <summary>
    /// Why can a player not type into the login panel? A TMP_InputField needs
    /// three things the panel does not currently verify: an active EventSystem,
    /// an input module driving it, and a GraphicRaycaster on the canvas it was
    /// parented to. BuildPanel takes the FIRST root canvas FindObjectsOfType
    /// returns, which is not a documented order - so the panel can land on a
    /// canvas that renders but cannot be clicked.
    /// </summary>
    /// <summary>Call BuildUnitManager.SetAvailable the way a mission script
    /// does, so UnitGrantPatch can be tested without finding the story beat
    /// that triggers a real grant. Farsite's cannon grant is driven by player
    /// progress, and five minutes of sim with no player actions never reached
    /// it.</summary>
    private static void Grant(string key)
    {
        if (key.Length == 0) { ModCore.Log.LogWarning("usage: grant:<unit key>"); return; }
        var bum = GameSpace.instance?.buildUnitManager;
        if (bum == null) { ModCore.Log.LogWarning("grant: no buildUnitManager"); return; }
        try
        {
            bum.SetAvailable(key, true);
            ModCore.Log.LogInfo($"DEBUG grant: called SetAvailable('{key}', true)");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"grant failed: {e.Message}"); }
    }

    private static bool _keyWatch;

    /// <summary>Does a keystroke reach Unity at ALL?
    ///
    /// Needed because an injected-key test that changes nothing is ambiguous:
    /// the key may never have reached the process (Windows refuses foreground
    /// to a background console), or it may have reached it and been dropped by
    /// the input field. Input.inputString is upstream of every uGUI widget, so
    /// a line here means the key arrived and the field is at fault; silence
    /// means the injection failed and the test proves nothing.</summary>
    private static void KeyWatch()
    {
        if (!_keyWatch) return;
        try
        {
            var s = Input.inputString;
            if (!string.IsNullOrEmpty(s))
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                ModCore.Log.LogInfo($"KEYWATCH: inputString='{s}' " +
                    $"selected={(es == null || es.currentSelectedGameObject == null ? "none" : es.currentSelectedGameObject.name)}");
            }
        }
        catch { }
    }

    private static void InputDump()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        ModCore.Log.LogInfo($"INPUTDUMP: EventSystem.current=" +
            (es == null ? "NULL" : es.gameObject.name));
        if (es != null)
            ModCore.Log.LogInfo($"INPUTDUMP:   esActive={es.isActiveAndEnabled} " +
                $"module={(es.currentInputModule == null ? "NULL" : es.currentInputModule.GetIl2CppType().Name)} " +
                $"selected={(es.currentSelectedGameObject == null ? "none" : es.currentSelectedGameObject.name)}");

        foreach (var m in UnityEngine.Object.FindObjectsOfType<UnityEngine.EventSystems.BaseInputModule>())
            if (m != null)
                ModCore.Log.LogInfo($"INPUTDUMP:   module '{m.gameObject.name}' " +
                    $"{m.GetIl2CppType().Name} active={m.isActiveAndEnabled}");

        foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (cv == null || !cv.isRootCanvas) continue;
            GraphicRaycaster? gr = null;
            try { gr = cv.GetComponent<GraphicRaycaster>(); } catch { }
            ModCore.Log.LogInfo($"INPUTDUMP:   rootCanvas '{cv.gameObject.name}' " +
                $"raycaster={(gr != null)} rcEnabled={(gr != null && gr.enabled)} " +
                $"order={cv.sortingOrder} active={cv.isActiveAndEnabled}");
        }

        var panel = GameObject.Find("CW4ApPanel");
        ModCore.Log.LogInfo($"INPUTDUMP: panel={(panel == null ? "NULL" : "found")}");
        if (panel != null)
        {
            var pc = panel.GetComponentInParent<Canvas>();
            GraphicRaycaster? pgr = null;
            try { if (pc != null) pgr = pc.rootCanvas.GetComponent<GraphicRaycaster>(); } catch { }
            ModCore.Log.LogInfo($"INPUTDUMP:   hostCanvas=" +
                $"'{(pc != null ? pc.rootCanvas.gameObject.name : "null")}' " +
                $"hostRaycaster={(pgr != null)} panelActive={panel.activeInHierarchy}");
            foreach (var f in panel.GetComponentsInChildren<TMPro.TMP_InputField>(true))
            {
                if (f == null) continue;
                Image? img = null;
                try { img = f.GetComponent<Image>(); } catch { }
                ModCore.Log.LogInfo($"INPUTDUMP:   field '{f.gameObject.name}' " +
                    $"interactable={f.interactable} focused={f.isFocused} " +
                    $"raycastTarget={(img != null && img.raycastTarget)} " +
                    $"text='{f.text}'");
            }
        }
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

    /// <summary>Set the finale's mission-count gate without a server, so the
    /// in-game lock can be tested: "finale:need 12", "finale:need 0" to lift it.
    /// Also reports how the gate currently evaluates.</summary>
    private static void Finale(string arg)
    {
        var state = ModCore.Client.State;
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length >= 2 && tok[0].Equals("need", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(tok[1], out var n))
        {
            state.Hints.MissionsForFinale = n;
        }
        else if (tok.Length >= 2 && tok[0].Equals("beat", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(tok[1], out var b))
        {
            // Mark that many missions complete, so the countdown and the map
            // colours can be exercised without playing them.
            state.CheckedLocations.RemoveWhere(
                l => l.EndsWith(" - Mission Complete", StringComparison.Ordinal));
            int added = 0;
            for (int m = 1; m <= 20 && added < b; m++)
            {
                if (m == CW4Archipelago.Core.MissionRules.FinalMission) continue;
                state.CheckedLocations.Add(CW4Archipelago.Core.MissionRules.MissionCompleteLocation(m));
                added++;
            }
        }

        // Both branches write state behind the normal paths - one edits the
        // checked set directly, the other edits the slot hints, and neither
        // raises a change event. Announce it, or the listeners that now drive
        // the map and the lock never hear about a change this command exists to
        // make.
        state.RaiseLocationsChanged();

        ModCore.Log.LogInfo(
            $"DEBUG FINALE: need={state.Hints.MissionsForFinale} " +
            $"beaten={CW4Archipelago.Core.MissionRules.MissionsBeaten(state)} " +
            $"counts={CW4Archipelago.Core.MissionRules.FinaleCounts(state)}");
    }

    /// <summary>Add a location name to this slot's list, as if the server had
    /// sent it. The map's glyph colouring reads AllLocations, which is empty
    /// until a connection exists - so without this there is no way to exercise
    /// the tracker offline.</summary>
    private static void LocAdd(string name)
    {
        if (name.Length == 0) return;
        var state = ModCore.Client.State;
        if (!state.AllLocations.Contains(name))
            state.AllLocations.Add(name);
        // Which locations exist decides which glyphs are tracked, so this is a
        // change the map has to hear about.
        state.RaiseLocationsChanged();
        ModCore.Log.LogInfo($"DEBUG LOC ADD: '{name}' (total {state.AllLocations.Count})");
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

    /// <summary>Traps feasibility spike (docs/design/2026-08-26-traps-spike.md).
    /// "trap:&lt;name&gt; [args]" - each effect is fire-and-forget or self-restoring;
    /// nothing here may make a mission unwinnable.</summary>
    /// <summary>Test scaffolding: unpause the sim (clearing every pause owner)
    /// and optionally set the game speed, so a battery can watch an effect play
    /// out without a human pressing play. "sim:run [speed]" / "sim:pause".</summary>
    private void Sim(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("sim: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "run";

        var owners = new System.Collections.Generic.List<string>();
        try { foreach (var o in gs.pauseOwner) owners.Add(o); } catch { }

        if (what == "pause") { HoldRunning = false; gs.Pause("cw4ap", true); ModCore.Log.LogInfo("SIM paused"); return; }
        if (what == "hold")
        {
            HoldRunning = tok.Length < 2 || tok[1].ToLowerInvariant() != "off";
            ModCore.Log.LogInfo($"SIM hold={(HoldRunning ? "on" : "off")}");
            if (!HoldRunning) return;
        }

        foreach (var o in owners)
        {
            try { gs.Pause(o, false); } catch (Exception e) { ModCore.Log.LogWarning($"sim: unpause '{o}': {e.Message}"); }
        }
        if (tok.Length > 1 && int.TryParse(tok[1], out var sp)) gs.GAME_SPEED = sp;
        ModCore.Log.LogInfo($"SIM run: cleared owners [{string.Join(",", owners)}], paused={gs.paused} speed={gs.GAME_SPEED}");
    }

    /// <summary>Test scaffolding: place N of a unit in a row beside the rift lab
    /// so effects that need player units (stun, ammo) have targets.
    /// "spawn:cannon 3".</summary>
    private void Spawn(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0) { ModCore.Log.LogWarning("spawn: need a unit key"); return; }
        var key = tok[0].ToLowerInvariant();
        int count = tok.Length > 1 && int.TryParse(tok[1], out var c) ? c : 1;

        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("spawn: no GameSpace"); return; }
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }

        // Before the rift lab is placed (most missions start that way) fall back
        // to the middle of the map so the rift lab itself can be spawned.
        Vector3 anchor;
        if (cb != null && GameUtil.IsAlive(cb)) anchor = cb.transform.position;
        else if (gs.world != null) anchor = TrapEffects.CellToWorld(World.WORLD_CELL_WIDTH / 2, World.WORLD_CELL_HEIGHT / 2);
        else { ModCore.Log.LogWarning("spawn: no anchor"); return; }

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            // Take the TERRAIN height at the target, not the anchor's. Reusing
            // the rift lab's Y buried a spawned ERN inside a rise next to the
            // base - the unit existed, was selectable in dumps, and could not be
            // seen or used. GetMinHeight is what CellToWorld already uses.
            var flat = anchor + new Vector3(-8f - 4f * i, 0f, -6f);
            float ground = flat.y;
            try { ground = UnitManager.GetMinHeight(new Vector3(flat.x, 0f, flat.z), 0f, 0, false, false, false); }
            catch { }
            var pos = new Vector3(flat.x, ground, flat.z);
            try { if (UnitManager.CreateUnitAtPosition(key, pos) != null) made++; }
            catch (Exception e) { ModCore.Log.LogWarning($"spawn '{key}': {e.Message}"); break; }
        }
        ModCore.Log.LogInfo($"SPAWN {key}: {made}/{count} placed");
    }

    /// <summary>Place a unit at an ABSOLUTE map cell: "spawnat:collector 73 47".
    ///
    /// spawn: places relative to the command base, which is fine for a weapon
    /// under test and useless for mining: resource nodes sit at fixed map
    /// coordinates (story12 has six, at 73,47 and 95,32 and four more) and a
    /// miner only works ON one.</summary>
    private void SpawnAt(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 3
            || !int.TryParse(tok[1], out var cx)
            || !int.TryParse(tok[2], out var cy))
        {
            ModCore.Log.LogWarning("spawnat: need 'key x y'");
            return;
        }
        var key = tok[0].ToLowerInvariant();
        float ground = 0f;
        try { ground = UnitManager.GetMinHeight(new Vector3(cx, 0f, cy), 0f, 0, false, false, false); }
        catch { }
        try
        {
            var made = UnitManager.CreateUnitAtPosition(key, new Vector3(cx, ground, cy));
            ModCore.Log.LogInfo(
                $"SPAWNAT {key}: {(made != null ? "placed" : "FAILED")} at ({cx},{cy}) y={ground:0.##}");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"SPAWNAT {key}: threw {e.Message}"); }
    }

    private void Trap(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var name = tok.Length > 0 ? tok[0].ToLowerInvariant() : "";
        // 0 means "use the tuned default in TrapEffects"; amounts are in depth units.
        float A(int i, float dflt) =>
            tok.Length > i && float.TryParse(tok[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : dflt;

        switch (name)
        {
            // Two distinct spore traps: a fair scatter, and a strike on a
            // random building. "trap:spore" uses the configured default.
            case "spore":    TrapEffects.SporeStrike((int)A(1, 0f), (int)(A(2, 0f) * 1_000_000)); return;
            case "scatter":  TrapEffects.SporeStrikeScatter((int)A(1, 0f), (int)(A(2, 0f) * 1_000_000)); return;
            case "building": TrapEffects.SporeStrikeBuilding((int)A(1, 0f), (int)(A(2, 0f) * 1_000_000)); return;
            case "creep":  TrapEffects.Creep((int)A(1, 0f), (int)(A(2, 0f) * 1_000_000)); return;
            case "energy": TrapEffects.Energy(A(1, 0f)); return;
            case "emit":   TrapEffects.Emit(A(1, 0f), A(2, 0f)); return;
            case "stun":   TrapEffects.Stun(A(1, 0f)); return;
            case "drain":  TrapEffects.Drain(); return;
            case "status": TrapEffects.Status(); return;
            case "set":    TrapEffects.Set(tok.Skip(1).ToArray()); return;
            case "aim":    TrapEffects.Aim(tok.Length > 1 ? tok[1] : ""); return;
            case "coord":  TrapEffects.Coord(); return;
            default:
                ModCore.Log.LogWarning(
                    $"trap: unknown effect '{name}' - expected spore|scatter|building|creep|energy|emit|stun|drain|status|set|aim");
                return;
        }
    }

    /// <summary>Settle whether powerZoneCells reading 0 on all twenty missions is
    /// the truth or a broken read.
    ///
    /// It has to be settled rather than assumed, because this project has already
    /// made exactly this mistake once: the re-fog scan keyed off GetIsFogTerrain
    /// (the DERIVED "currently dark" flag) instead of GetFogTerrain (the map's
    /// definition) and confidently reported "no fog cells" on a mission with 7845
    /// of them. A uniform zero is what that failure looks like from the outside.
    ///
    /// Three checks, because one reader agreeing with itself proves nothing:
    ///   1. Count via World.GetPowerZone(x, y), which is what ResourceDump uses.
    ///   2. Count via the raw World.powerZone array, an INDEPENDENT reader - it is
    ///      an Il2CppStructArray of int, i.e. the terrain layer itself.
    ///   3. A POSITIVE CONTROL: write a zone into three cells with SetPowerZone,
    ///      re-count both ways, then put the old values back. If a written cell
    ///      does not show up, the reader is wrong and the survey's zeros mean
    ///      nothing. If it does, the reader works and the campaign genuinely has
    ///      no power zones.
    ///
    /// Not to be confused with World.desiredPowerZone, which is an array of
    /// HashSets rather than a terrain layer - player/UI intent, not the map.
    ///
    /// This MUTATES the world for a moment. It is a debug command in a throwaway
    /// session, it restores what it changed, and nothing is saved.</summary>
    private static void ZoneTest()
    {
        var gs = GameSpace.instance;
        var w = gs?.world;
        if (w == null) { ModCore.Log.LogWarning("zonetest: no world"); return; }

        int width = World.WORLD_CELL_WIDTH, height = World.WORLD_CELL_HEIGHT;

        int ByAccessor()
        {
            int n = 0;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (w.GetPowerZone(x, y) > 0) n++;
            return n;
        }

        // The raw layer. Length is reported too: if it is not width*height the
        // grid assumption behind every full-map scan in the mod is wrong.
        int rawLen = -1;
        int ByArray()
        {
            try
            {
                var arr = w.powerZone;
                if (arr == null) { rawLen = -2; return -1; }
                rawLen = arr.Length;
                int n = 0;
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] > 0) n++;
                return n;
            }
            catch (Exception e)
            {
                ModCore.Log.LogWarning($"zonetest: raw array read failed: {e.Message}");
                return -1;
            }
        }

        int before = ByAccessor(), beforeRaw = ByArray();
        ModCore.Log.LogInfo(
            $"ZONETEST: grid={width}x{height} (cells={width * height}) rawLen={rawLen} " +
            $"accessor={before} rawArray={beforeRaw}");

        // Three cells at the centre, so the write cannot land off-grid.
        var cells = new (int X, int Y)[] { (width / 2, height / 2), (width / 2 + 1, height / 2), (width / 2, height / 2 + 1) };
        var saved = new int[cells.Length];
        try
        {
            for (int i = 0; i < cells.Length; i++)
            {
                saved[i] = w.GetPowerZone(cells[i].X, cells[i].Y);
                w.SetPowerZone(cells[i].X, cells[i].Y, 1);
            }
            int after = ByAccessor(), afterRaw = ByArray();
            bool accessorSaw = after >= before + cells.Length;
            bool arraySaw = afterRaw >= beforeRaw + cells.Length;
            ModCore.Log.LogInfo(
                $"ZONETEST: wrote {cells.Length} cells -> accessor={after} rawArray={afterRaw} " +
                $"accessorSawTheWrite={accessorSaw} arraySawTheWrite={arraySaw}");
            ModCore.Log.LogInfo(accessorSaw && arraySaw
                ? "ZONETEST: VERDICT reader WORKS - a zero count is the map's real answer"
                : "ZONETEST: VERDICT reader is WRONG - a written cell did not read back");
        }
        catch (Exception e) { ModCore.Log.LogWarning($"zonetest: write failed: {e.Message}"); }
        finally
        {
            try
            {
                for (int i = 0; i < cells.Length; i++)
                    w.SetPowerZone(cells[i].X, cells[i].Y, saved[i]);
                ModCore.Log.LogInfo($"ZONETEST: restored, accessor back to {ByAccessor()}");
            }
            catch (Exception e) { ModCore.Log.LogWarning($"zonetest: restore failed: {e.Message}"); }
        }
    }

    /// <summary>Per-mission resource survey for the logic pass: which raw
    /// resources a map actually ships. Sprayers need bluite, so "does this
    /// mission have bluite at all" decides whether sprayer can count as offense
    /// here; miner only matters where there is something to mine.
    ///
    /// Counts map deposits (ResourceBlue / ResourceRed / GreenarMother) AND
    /// POWER ZONE cells - the bright blue ground the player builds Reactors on.
    /// A reactor swaps between extra energy and producing bluite, so a map with
    /// power zones is a bluite source with zero deposits. Counting live Reactor
    /// units instead would always read 0 at mission start, because the PLAYER
    /// builds them; the terrain is the thing that is fixed per map.</summary>
    private static void ResourceDump()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("resources: no GameSpace"); return; }

        int blue = 0, red = 0, greenar = 0, reactors = 0, powerZoneUnits = 0;
        var reactorWares = new System.Collections.Generic.Dictionary<int, int>();
        var other = new System.Collections.Generic.Dictionary<string, int>();

        foreach (var u in gs.units)
        {
            if (u == null) continue;
            string type;
            try { type = u.GetIl2CppType().Name; } catch { continue; }
            switch (type)
            {
                case "ResourceBlue": blue++; break;
                case "ResourceRed": red++; break;
                case "GreenarMother": greenar++; break;
            // A second, independent reader for power zones. The 88-name registry
            // contains a PowerZone type and there is a PowerZoneBuildGhost, so
            // zones exist as OBJECTS - counting only the terrain layer would miss
            // a map that carries them some other way.
            case "PowerZone": powerZoneUnits++; break;
                case "Reactor":
                    reactors++;
                    try
                    {
                        var r = u.TryCast<Reactor>();
                        if (r != null)
                        {
                            int w = r.GetWareType();
                            reactorWares[w] = reactorWares.TryGetValue(w, out var c) ? c + 1 : 1;
                        }
                    }
                    catch { }
                    break;
                default:
                    if (type.StartsWith("Resource"))
                        other[type] = other.TryGetValue(type, out var o) ? o + 1 : 1;
                    break;
            }
        }

        // Power-zone cells: where Reactors can be built at all.
        int zoneCells = 0;
        try
        {
            var w = gs.world;
            if (w != null)
                for (int x = 0; x < World.WORLD_CELL_WIDTH; x++)
                    for (int y = 0; y < World.WORLD_CELL_HEIGHT; y++)
                        if (w.GetPowerZone(x, y) > 0) zoneCells++;
        }
        catch (Exception e) { ModCore.Log.LogWarning($"resources: powerZone scan failed: {e.Message}"); }

        var wares = string.Join(",", reactorWares.Select(kv => $"ware{kv.Key}x{kv.Value}"));
        var extra = other.Count == 0 ? "" : " otherResource:" + string.Join(",", other.Select(kv => $"{kv.Key}x{kv.Value}"));
        ModCore.Log.LogInfo(
            $"RESOURCES: bluite={blue} redon={red} greenar={greenar} powerZoneCells={zoneCells} " +
        $"powerZoneUnits={powerZoneUnits} reactors={reactors}" +
            (wares.Length > 0 ? $" ({wares})" : "") + extra);
    }

    /// <summary>Lists every build-pane button the game actually offers, with the
    /// unit key each one builds. Settles which buildables the AP whitelist
    /// covers: UnitGate drives 26 BuildUnitManager availability flags, so any
    /// button whose key is not one of those 26 is a building the mod cannot
    /// gate. Reactor is the open question.</summary>
    private static void PaneDump()
    {
        var panes = GameUtil.AllPanes(true);
        if (panes == null || panes.Count == 0) { ModCore.Log.LogWarning("pane: no build panes"); return; }

        int n = 0;
        foreach (var pn in panes)
        {
            if (pn == null) continue;
            var names = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var b in pn.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    if (b == null) continue;
                    var go = b.gameObject;
                    // activeInHierarchy separates "exists in the prefab" from
                    // "actually offered to the player" - the availability flags
                    // work by toggling these on and off.
                    if (go != null) names.Add(go.name + (go.activeInHierarchy ? "=ON" : "=off"));
                }
            }
            catch (Exception e) { ModCore.Log.LogWarning($"pane dump: {e.Message}"); }
            ModCore.Log.LogInfo($"PANE[{n++}] '{pn.gameObject.name}' buttons({names.Count}): {string.Join(",", names)}");
        }
    }

    /// <summary>What this mission's totems actually demand. Totem.ammoWares is a
    /// ware-type -> amount map authored PER MAP, so "the Totems objective needs
    /// greenar" is a per-mission fact, not a global rule - this reads it.
    /// Ware indices are reported raw; GetWareName is not reachable from here, so
    /// they are correlated against the deposit survey instead.</summary>
    private static void TotemDump()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("totems: no GameSpace"); return; }

        int totems = 0;
        var tally = new System.Collections.Generic.Dictionary<int, int>();
        var detail = new System.Collections.Generic.List<string>();
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try { if (u.GetIl2CppType().Name != "Totem") continue; } catch { continue; }
            totems++;
            var wants = new System.Collections.Generic.List<string>();
            for (int w = 0; w < 16; w++)
            {
                int amt;
                try { amt = u.GetAmmoWareWanted(w); } catch { continue; }
                if (amt <= 0) continue;
                wants.Add($"w{w}x{amt}");
                tally[w] = tally.TryGetValue(w, out var c) ? c + amt : amt;
            }
            if (detail.Count < 6) detail.Add(wants.Count == 0 ? "[none]" : "[" + string.Join(",", wants) + "]");
        }

        var totals = string.Join(",", tally.OrderBy(kv => kv.Key).Select(kv => $"ware{kv.Key}={kv.Value}"));
        ModCore.Log.LogInfo(
            $"TOTEMS: count={totems} wares[{(totals.Length == 0 ? "NONE" : totals)}] {string.Join("", detail)}");
    }

    /// <summary>Counts the per-mission things that could each become their own
    /// AP location, instead of collapsing into one objective check:
    ///   mustCollect      - GameSpace's authoritative collect-target set (the
    ///                      caches you connect your network to). This is what
    ///                      the "Collect" objective completes on.
    ///   nullifiableUnits - enemy structures that can be nullified; the
    ///                      "Nullify" objective completes on these.
    ///   InfoCache        - message/lore caches, each with a `retrieved` flag.
    ///   Totem            - totems, fed wares to activate.
    /// The current design gives ONE location per objective type per mission, so
    /// these counts are the ceiling if each individual one became a check.</summary>
    private static void CountsDump()
    {
        var gs = GameSpace.instance;
        if (gs == null) { ModCore.Log.LogWarning("counts: no GameSpace"); return; }

        int mustCollect = -1, nullifiable = -1, maxMustCollect = -1;
        try { mustCollect = gs.mustCollect?.Count ?? -1; } catch { }
        try { maxMustCollect = gs.maxMustCollect; } catch { }
        try { nullifiable = gs.nullifiableUnits?.Count ?? -1; } catch { }

        // What the objectives THEMSELVES report. LocationWatcher infers cache
        // progress from mustCollect shrinking, so whether that set tracks the
        // Collect objective is load-bearing and worth printing next to it.
        var objState = new System.Collections.Generic.List<string>();
        try
        {
            var w = gs.world;
            var slots = w?.missionObjectives;
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                {
                    bool done = false;
                    try { done = w!.IsMissionObjectiveComplete(i); } catch { }
                    int count = -1;
                    try { count = slots[i].count; } catch { }
                    bool en = false;
                    try { en = slots[i].enabled; } catch { }
                    objState.Add($"{i}:{(en ? "on" : "off")}/count={count}/{(done ? "DONE" : "open")}");
                }
        }
        catch { }

        // Is the rift lab already on the map, or must the player place it?
        // Missions that ship a placed base are the only ones playable if the
        // Rift Lab itself becomes an unlockable item - a natural starter set.
        bool basePlaced = false;
        try
        {
            var cb = gs.commandBase;
            basePlaced = cb != null && GameUtil.IsAlive(cb);
        }
        catch { }

        int caches = 0, retrieved = 0, totems = 0;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            string t;
            try { t = u.GetIl2CppType().Name; } catch { continue; }
            if (t == "Totem") { totems++; continue; }
            if (t != "InfoCache") continue;
            caches++;
            try { if (u.TryCast<InfoCache>()?.retrieved == true) retrieved++; } catch { }
        }

        // The AUTHORED objective targets. This is the number that matters for
        // sizing locations: MissionObjectiveData.count is what the objective
        // panel shows as "0/N", so it already accounts for units that spawn
        // during play rather than existing at load. The live counts above are
        // only a start-of-mission floor.
        var objs = new System.Collections.Generic.List<string>();
        try
        {
            var w = gs.world;
            var mo = w?.missionObjectives;
            if (mo != null)
                for (int i = 0; i < mo.Length; i++)
                {
                    var o = mo[i];
                    if (o == null) continue;
                    string kind = i < CW4Archipelago.Core.MissionRules.ObjectiveTypes.Length
                        ? CW4Archipelago.Core.MissionRules.ObjectiveTypes[i] : $"slot{i}";
                    var nm = string.IsNullOrEmpty(o.customName) ? "" : $"'{o.customName}'";
                    objs.Add($"{kind}{nm}:en={(o.enabled ? 1 : 0)},req={(o.required ? 1 : 0)},n={o.count},t={o.time}");
                }
        }
        catch (Exception e) { ModCore.Log.LogWarning($"counts: objectives failed: {e.Message}"); }

        ModCore.Log.LogInfo($"OBJECTIVES: {string.Join(" | ", objs)}");
        ModCore.Log.LogInfo(
            $"COUNTS: riftLabPreplaced={(basePlaced ? 1 : 0)} mustCollect={mustCollect}/{maxMustCollect} " +
            $"objectives[{string.Join(" ", objState)}] nullifiable={nullifiable} " +
            $"infoCaches={caches} (retrieved={retrieved}) totems={totems}");
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
