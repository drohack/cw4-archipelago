using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CW4DevTools;

/// <summary>
/// File-command channel so the dev tools can be driven without a keyboard:
/// write a line to &lt;game&gt;/BepInEx/cw4dev-commands.txt and it runs on the
/// next tick.
///
/// This exists because the survey work previously borrowed the RANDOMIZER's
/// debug channel to boot missions and take screenshots - which meant enabling
/// the whole Archipelago layer just to test a cheat, and that layer then stayed
/// enabled by accident. Anything needed to drive or diagnose the dev tools
/// belongs here, in the plugin it belongs to.
///
/// Deliberately no Archipelago commands: no connect, no items, no checks. If a
/// test needs those, it is a randomizer test and belongs in its batteries.
/// </summary>
public sealed class DevCommands
{
    private static string FilePath => System.IO.Path.Combine(Paths.GameRootPath, "BepInEx", "cw4dev-commands.txt");

    private readonly ManualLogSource _log;
    private DateTime _lastWrite = DateTime.MinValue;
    private int _pollCountdown;

    public DevCommands(ManualLogSource log) => _log = log;

    public void Tick()
    {
        CapacityProbeTick();
        NullifyWatchTick();
        WatchTick();   // every frame: a readback must not wait on the poll

        if (--_pollCountdown > 0) return;
        _pollCountdown = 30;   // ~twice a second; this is a test hook, not a hot path

        var path = FilePath;
        if (!File.Exists(path)) return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); } catch { return; }
        if (stamp == _lastWrite) return;
        _lastWrite = stamp;

        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try { Handle(line); }
            catch (Exception e) { _log.LogWarning($"DEVCMD '{line}' failed: {e.Message}"); }
        }
    }

    private void Handle(string line)
    {
        var lower = line.ToLowerInvariant();

        if (lower.StartsWith("boot:")) { Boot(line.Substring(5).Trim()); return; }
        if (lower == "ada:close") { CloseAda(); return; }
        if (lower.StartsWith("sim:")) { Sim(line.Substring(4).Trim()); return; }
        if (lower.StartsWith("spawn:")) { Spawn(line.Substring(6).Trim()); return; }
        if (lower.StartsWith("build:")) { Build(line.Substring(6).Trim()); return; }
        if (lower.StartsWith("shot:")) { Shot(line.Substring(5).Trim()); return; }
        if (lower == "dump") { DevTools.DumpUnitsNow(); return; }
        if (lower == "story:open") { StoryOpen(); return; }
        if (lower == "planets:dump") { PlanetsDump(); return; }
        if (lower == "obj:dump") { ObjectiveDump(); return; }
        if (lower == "buildings:dump") { DevTools.DumpAvailability(); return; }
        if (lower.StartsWith("map:dump")) { MapDump(line.Substring(8).Trim()); return; }
        if (lower == "totems:dump") { TotemWares(); return; }
        if (lower == "wares:names") { WareNames(); return; }
        if (lower.StartsWith("null:")) { Nullify(line.Substring(5).Trim()); return; }
        if (lower.StartsWith("energy:")) { Energy(line.Substring(7).Trim()); return; }
        if (lower == "span:open") { SpanOpen(); return; }
        if (lower == "span:list") { SpanList(); return; }
        if (lower.StartsWith("span:swap")) { SpanSwap(line.Substring(9).Trim()); return; }
        if (lower.StartsWith("span:icons")) { SpanIcons(line.Substring(10).Trim()); return; }
        if (lower.StartsWith("span:play")) { SpanPlay(line.Substring(9).Trim()); return; }
        if (lower == "save:auto") { SaveAuto(); return; }
        if (lower.StartsWith("span:boot")) { SpanBoot(line.Substring(9).Trim()); return; }
        if (lower.StartsWith("span:goto")) { SpanGoto(line.Substring(9).Trim()); return; }
        if (lower.StartsWith("set:")) { Set(line.Substring(4).Trim()); return; }
        if (lower == "overlay:dump") { OverlayDump(); return; }

        _log.LogWarning($"DEVCMD unknown: {line} " +
                        "(boot:storyN | ada:close | sim:run [speed] | sim:pause | " +
                        "spawn:<UnitName> [n] | build:[unit] <x> <y> | build:scan <unit> [x y] | build:chain [x y [hop]] | " +
                        "shot:<path> | dump | story:open | " +
                        "planets:dump | obj:dump | buildings:dump | map:dump <path> | " +
                        "totems:dump | wares:names | " +
                        "overlay:dump | " +
                        "null:* | energy:* | " +
                        "span:open | span:list | span:boot <guid> | " +
                        "span:swap <planet> <spanguid> | span:icons <planet> <slots> | " +
                        "span:play <planet> | span:goto | " +
                        "set:<cheat>=on|off)");
    }

    /// <summary>Places a unit the way a PLAYER does, at cell x,y.
    ///
    /// <para>This is not a second <c>spawn:</c>. They use different game code and
    /// only one of them produces a unit the simulation treats as real:</para>
    ///
    /// <list type="bullet">
    /// <item><c>spawn:</c> calls <c>CreateUnitAtPosition</c>, which makes a unit
    /// the sim never adopts. Measured on Home 2026-09-18: a rift lab and a tower
    /// spawned five cells from the info cache, on clear height-3 ground, placed
    /// while paused with instant build on, still read energyProduction=0 and
    /// never collected the cache. It is not creep, burial, distance or ghost
    /// state - the unit claims no land, so there is no network.</item>
    /// <item><c>build:</c> drives the ghost the game itself builds from:
    /// <c>SetPosition</c> then <c>Build()</c>, the same two calls a mouse click
    /// makes. <c>Build()</c> calls <c>CreateUnit</c> from inside the game's own
    /// path, so whatever adoption a hand-placed unit gets, this gets.</item>
    /// </list>
    ///
    /// <para>Two forms, because the rift lab arrives differently from everything
    /// else. <c>build:&lt;x&gt; &lt;y&gt;</c> places what is ALREADY in the
    /// player's hand - which at a mission's landing prompt is the rift lab, held
    /// in <c>InputManager.unitToBuild</c>. That is the landing click, and it is
    /// why this command exists: <c>boot:</c> leaves a mission at that prompt, and
    /// synthetic mouse input does not reach CW4's UI.
    /// <c>build:&lt;unit&gt; &lt;x&gt; &lt;y&gt;</c> first fills the hand by
    /// invoking the left pane's own button handler (<c>BuildUnitTower</c> and
    /// friends), then places it.</para>
    ///
    /// <para>Reports what it ACHIEVED, not that it ran: the legality verdict, the
    /// Build() return, and whether GameSpace.commandBase is non-null afterwards.
    /// Every outcome logs a line starting DEVCMD build, so a harness greps one
    /// anchor and a failure never reads like a hung mod.</para>
    /// </summary>
    private void Build(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length >= 1 && tok[0].Equals("chain", StringComparison.OrdinalIgnoreCase))
        {
            int cx2 = -1, cy2 = -1, hop = 0;
            if (tok.Length >= 3) { int.TryParse(tok[1], out cx2); int.TryParse(tok[2], out cy2); }
            if (tok.Length >= 4) int.TryParse(tok[3], out hop);
            Chain(cx2, cy2, hop);
            return;
        }
        if (tok.Length >= 2 && tok[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
        {
            int tx = -1, ty = -1;
            if (tok.Length >= 4) { int.TryParse(tok[2], out tx); int.TryParse(tok[3], out ty); }
            Scan(tok[1], tx, ty);
            return;
        }

        string? unit = null;
        int x, y;
        if (tok.Length >= 3) { unit = tok[0]; }
        else if (tok.Length != 2) { _log.LogWarning("DEVCMD build: need <x> <y> or <unit> <x> <y>"); return; }
        if (!int.TryParse(tok[tok.Length - 2], out x) || !int.TryParse(tok[tok.Length - 1], out y))
        { _log.LogWarning("DEVCMD build: x and y must be whole cells"); return; }

        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD build: no GameSpace"); return; }

        InputManager? im = null;
        try { im = gs.inputManager; } catch { }
        if (im == null) { _log.LogWarning("DEVCMD build: no InputManager"); return; }

        if (unit != null && !FillHand(unit)) return;

        UnitBuildGhost? ubg = null;
        try { ubg = im.unitToBuild; } catch { }
        if (ubg == null)
        {
            _log.LogWarning(unit == null
                ? "DEVCMD build: nothing in hand - at a landing prompt the rift lab should be there, so this means the mission is past it"
                : $"DEVCMD build {unit}: the left pane handler ran but put nothing in hand");
            return;
        }

        bool legal = false;
        try { legal = ubg.IsLegal(x, y); } catch (Exception e) { _log.LogWarning($"DEVCMD build: IsLegal threw: {e.Message}"); }

        bool built = false;
        try
        {
            ubg.SetPosition(x, y, true);
            built = ubg.Build();
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD build: {e.Message}"); return; }

        bool haveLab = false;
        try { haveLab = gs.commandBase != null; } catch { }

        _log.LogInfo($"DEVCMD build {unit ?? "inhand"} at ({x},{y}): legal={legal} built={built} commandBase={(haveLab ? 1 : 0)}");

        // An illegal cell is the common failure and the least self-explanatory:
        // the ghost knows why, and says nothing. A footprint needs room and flat
        // ground, so the cell you picked off a terrain dump is often a cell or
        // two from one that works. Reporting the nearest legal cell turns a dead
        // end into the next command to send, and costs one scan only when the
        // build has already failed.
        if (!built && !legal) ReportNearestLegal(ubg, x, y);
    }

    /// <summary>Lands the rift lab and walks a tower line to a target cell, so a
    /// harness reaches an objective in one command instead of a hand-tuned list
    /// of coordinates per map.
    ///
    /// <para>With no arguments it aims at the first outstanding info cache,
    /// which is what every cache test wants and removes the last per-map
    /// constant from the harness.</para>
    ///
    /// <para>Two things here were learned the hard way and are why the hops are
    /// short and the lab is not placed where asked:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Tower range is 3D.</b> Eleven cells is the horizontal figure;
    /// height counts too, so a hop that measures nine on a flat map dump is over
    /// range wherever the ground steps. Hops of three keep every link inside
    /// range no matter what the terrain does, at the cost of a few more towers -
    /// which cost nothing here.</item>
    /// <item><b>The lab needs a footprint.</b> On Home the nearest cell the game
    /// will accept a lab on is twenty away from the cache, so asking for one next
    /// to the target simply fails. The scan finds the nearest legal cell and the
    /// chain starts from wherever that turns out to be.</item>
    /// </list>
    ///
    /// <para>Leaves the towers UNBUILT on purpose - the caller decides whether to
    /// cheat them up with instantbuild or let the lab build them, and a lab that
    /// builds them is itself the proof they are connected.</para>
    /// </summary>
    private void Chain(int tx, int ty, int hop)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD build chain: no GameSpace"); return; }

        if (tx < 0 || ty < 0)
        {
            try
            {
                foreach (var u in gs.mustCollect)
                {
                    if (u == null) continue;
                    tx = u.cellX; ty = u.cellY; break;
                }
            }
            catch { }
            if (tx < 0 || ty < 0) { _log.LogWarning("DEVCMD build chain: nothing left to collect, and no target given"); return; }
            _log.LogInfo($"DEVCMD build chain: aiming at the outstanding cache ({tx},{ty})");
        }

        InputManager? im = null;
        try { im = gs.inputManager; } catch { }
        if (im == null) { _log.LogWarning("DEVCMD build chain: no InputManager"); return; }

        // LAND AT MOST ONE LAB. The rift lab button builds a lab every time it is
        // pressed, so a chain run on a mission that already has one - a second
        // call, or a mission that starts with one placed - quietly produces two.
        int lx, ly;
        bool already = false;
        try { already = gs.commandBase != null; } catch { }
        if (already)
        {
            lx = -1; ly = -1;
            try { lx = gs.commandBase.cellX; ly = gs.commandBase.cellY; } catch { }
            _log.LogInfo($"DEVCMD build chain: a lab is already down at ({lx},{ly}), not landing another");
        }
        else
        {
            if (!FillHand("riftlab")) return;
            UnitBuildGhost? lab = null;
            try { lab = im.unitToBuild; } catch { }
            if (lab == null) { _log.LogWarning("DEVCMD build chain: no lab ghost in hand"); return; }

            if (!NearestLegal(lab, tx, ty, out lx, out ly))
            { _log.LogWarning("DEVCMD build chain: the map has no legal cell for a rift lab"); return; }
            bool labBuilt = false;
            try { lab.SetPosition(lx, ly, true); labBuilt = lab.Build(); } catch (Exception e) { _log.LogWarning($"DEVCMD build chain: lab: {e.Message}"); return; }
            if (!labBuilt) { _log.LogWarning($"DEVCMD build chain: the lab refused ({lx},{ly})"); return; }
        }

        if (!FillHand("Tower")) return;
        UnitBuildGhost? tw = null;
        try { tw = im.unitToBuild; } catch { }
        if (tw == null) { _log.LogWarning("DEVCMD build chain: no tower ghost in hand"); return; }

        // Hop size is a trade, and the first version got it backwards by being
        // timid. Towers reach 11 cells horizontally and range is 3D, so 6 is
        // still comfortably inside it over any terrain step - and HALVES the
        // tower count. That matters more than the margin does: with instant
        // build off the lab has to send packets to every one of them, and its
        // GEN is small, so a chain of tiny hops spends its energy budget
        // crawling rather than arriving. The distance that decides the pickup
        // is the LAST tower's distance from the item, which the loop does not
        // control at all - the final placement below is what controls it.
        int HOP = hop > 0 ? hop : 6;
        int cx = lx, cy = ly, towers = 0;
        for (int guard = 0; guard < 200; guard++)
        {
            int dx = tx - cx, dy = ty - cy;
            int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
            // Stop as soon as the last tower covers the target. Stopping at 1
            // instead ran the guard out at 200 towers on every map: inside one
            // hop the waypoint rounds onto ground the ghost refuses, the nearest
            // legal cell is beside it rather than nearer, and the loop places
            // tower after tower without ever closing the gap.
            if (dist <= HOP) break;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int wx = cx + (int)Math.Round((double)dx * HOP / steps);
            int wy = cy + (int)Math.Round((double)dy * HOP / steps);
            if (wx == cx && wy == cy) break;
            if (!NearestLegal(tw, wx, wy, out int px, out int py)) break;
            bool ok = false;
            try { tw.SetPosition(px, py, true); ok = tw.Build(); } catch { }
            if (!ok) break;
            towers++;
            int moved = Math.Max(Math.Abs(tx - px), Math.Abs(ty - py));
            cx = px; cy = py;

            // A hop that does not get closer is not the end of the road, it is a
            // hop that was too long. Tower legality tightens once a lab is down
            // - the cell must be reachable from the network - so a six-cell
            // waypoint over creeper or a gap resolves to the nearest legal cell
            // BEHIND it, and the first version read that as a dead end and
            // stopped 16 cells short of Far York Farm's cache. Shorten and try
            // again; only a stall at hop 1 is genuinely blocked.
            if (moved >= dist)
            {
                if (HOP > 1) { HOP = HOP > 2 ? HOP / 2 : 1; continue; }
                _log.LogWarning($"DEVCMD build chain: blocked at ({px},{py}), {moved} from target");
                break;
            }
            // The hand empties on a single-build ghost, so refill before the
            // next hop. Without this the chain silently stops after one tower.
            try { if (im.unitToBuild == null) { FillHand("Tower"); tw = im.unitToBuild; } } catch { }
            if (tw == null) break;
        }

        // FINISH ON THE TARGET, not wherever the hops ran out. The loop stops
        // once it is within one hop, which leaves the last tower up to three
        // cells short - and then the nearest legal cell to that waypoint can
        // drift further still, in any direction. On Home that was the
        // difference between collecting the cache and ending beside it: the
        // run that first worked finished two cells out.
        //
        // So place one more tower at the legal cell nearest the TARGET itself.
        // It is within a hop of the last one by construction, so it is in range.
        if (tw != null && NearestLegal(tw, tx, ty, out int ex, out int ey)
            && (ex != cx || ey != cy))
        {
            bool okEnd = false;
            try { tw.SetPosition(ex, ey, true); okEnd = tw.Build(); } catch { }
            if (okEnd) { towers++; cx = ex; cy = ey; }
        }

        // Leave the hand EMPTY. A ghost left held renders as a unit that is not
        // there, which reads as an extra building in a screenshot and is how a
        // "there are two rift labs" turns out to be one lab and one ghost.
        try
        {
            var pane = UnityEngine.Object.FindObjectOfType<LeftPane>();
            if (pane != null) pane.ClearSelected();
            im.unitToBuild = null;
        }
        catch { }

        int left = Math.Max(Math.Abs(tx - cx), Math.Abs(ty - cy));
        _log.LogInfo($"DEVCMD build chain: lab ({lx},{ly}), {towers} towers, ends ({cx},{cy}), {left} from ({tx},{ty})");
    }

    /// <summary>First cell the ghost accepts, searched outward from x,y. Shared by
    /// the chain and by the failure report, so both agree on what "nearest"
    /// means.</summary>
    private bool NearestLegal(UnitBuildGhost ubg, int x, int y, out int fx, out int fy)
    {
        fx = fy = -1;
        for (int r = 0; r <= 40; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (r > 0 && Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    bool ok = false;
                    try { ok = ubg.IsLegal(x + dx, y + dy); } catch { }
                    if (!ok) continue;
                    fx = x + dx; fy = y + dy;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Rings outward from a refused cell and names the first legal one.
    /// Silent if nothing within twelve cells works, which is itself the answer:
    /// the problem is the area, not the cell.</summary>
    private void ReportNearestLegal(UnitBuildGhost ubg, int x, int y)
    {
        for (int r = 1; r <= 12; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    bool ok = false;
                    try { ok = ubg.IsLegal(x + dx, y + dy); } catch { }
                    if (!ok) continue;
                    _log.LogInfo($"DEVCMD build nearest legal cell: ({x + dx},{y + dy}), {r} from ({x},{y})");
                    return;
                }
            }
        }
        _log.LogInfo($"DEVCMD build nearest legal cell: none within 12 of ({x},{y})");
    }

    /// <summary>Asks the ghost for every cell on the map it would accept.
    ///
    /// <para>Written because "no legal cell within twelve" is an answer that
    /// cannot be acted on: it does not distinguish a bad neighbourhood from a
    /// ghost that refuses everywhere. A whole-map count settles which, and when
    /// there IS a legal area it names where, so the next command is known rather
    /// than guessed. Twenty thousand IsLegal calls cost a frame and run only
    /// when asked.</para></summary>
    private void Scan(string unit, int tx, int ty)
    {
        var gs = GameSpace.instance;
        InputManager? im = null;
        try { im = gs?.inputManager; } catch { }
        if (im == null) { _log.LogWarning("DEVCMD build scan: no InputManager"); return; }
        if (!FillHand(unit)) return;

        UnitBuildGhost? ubg = null;
        try { ubg = im.unitToBuild; } catch { }
        if (ubg == null) { _log.LogWarning($"DEVCMD build scan {unit}: nothing in hand"); return; }

        int w = World.WORLD_CELL_WIDTH, h = World.WORLD_CELL_HEIGHT;
        int n = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        int bestX = -1, bestY = -1, bestD = int.MaxValue;
        var first = "";
        for (int cx = 0; cx < w; cx++)
        {
            for (int cy = 0; cy < h; cy++)
            {
                bool ok = false;
                try { ok = ubg.IsLegal(cx, cy); } catch { }
                if (!ok) continue;
                n++;
                if (n <= 5) first += $" ({cx},{cy})";
                if (tx >= 0)
                {
                    // Chebyshev, because that is the metric tower range uses -
                    // a cell eleven away diagonally is in range, so ranking by
                    // Euclidean distance would recommend the wrong cell.
                    int d = Math.Max(Math.Abs(cx - tx), Math.Abs(cy - ty));
                    if (d < bestD) { bestD = d; bestX = cx; bestY = cy; }
                }
                if (cx < minX) minX = cx;
                if (cy < minY) minY = cy;
                if (cx > maxX) maxX = cx;
                if (cy > maxY) maxY = cy;
            }
        }
        _log.LogInfo(n == 0
            ? $"DEVCMD build scan {unit}: 0 legal cells on the whole {w}x{h} map"
            : $"DEVCMD build scan {unit}: {n} legal cells, box ({minX},{minY})-({maxX},{maxY}), first{first}");
        if (tx >= 0 && bestX >= 0)
            _log.LogInfo($"DEVCMD build scan {unit}: nearest legal to ({tx},{ty}) is ({bestX},{bestY}), {bestD} away");
    }

    /// <summary>Puts <paramref name="unit"/> in the player's hand by invoking the
    /// left pane's own click handler, the <c>story:open</c> trick rather than a
    /// faked mouse event.
    ///
    /// <para>The handlers are named <c>BuildUnit&lt;Thing&gt;</c> - one per unit,
    /// forty of them - so this resolves by name instead of carrying a forty-case
    /// switch that would rot the first time a unit is added. The name wanted is
    /// the CAMEL one from the pane (<c>Tower</c>, <c>Collector</c>,
    /// <c>TowerBridge</c>), matched case-insensitively so a harness can write
    /// <c>tower</c>.</para>
    /// </summary>
    private bool FillHand(string unit)
    {
        // The rift lab is not on the left pane - it has a button of its own, and
        // at a landing prompt that button is what the player presses first. So
        // it is NOT already in hand when boot: returns, which is why the first
        // version of this command found unitToBuild null and reported the
        // mission was past its prompt. It was not; nobody had pressed the button.
        if (unit.Equals("riftlab", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("commandbase", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var btn = GameSpace.instance?.commandBaseButtonMgmt?.buildCommandBaseButton;
                if (btn == null) { _log.LogWarning("DEVCMD build: no rift lab button"); return false; }
                btn.onClick.Invoke();
                return true;
            }
            catch (Exception e) { _log.LogWarning($"DEVCMD build riftlab: {e.Message}"); return false; }
        }

        LeftPane? pane = null;
        try { pane = UnityEngine.Object.FindObjectOfType<LeftPane>(); } catch { }
        if (pane == null) { _log.LogWarning("DEVCMD build: no LeftPane - is a mission actually loaded?"); return false; }

        var want = "BuildUnit" + unit;
        foreach (var m in typeof(LeftPane).GetMethods())
        {
            if (m.GetParameters().Length != 0) continue;
            if (!string.Equals(m.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
            try { m.Invoke(pane, null); return true; }
            catch (Exception e) { _log.LogWarning($"DEVCMD build {unit}: {m.Name} threw: {e.Message}"); return false; }
        }
        _log.LogWarning($"DEVCMD build: no left-pane handler {want} - use the pane's own name, e.g. Tower, Collector, TowerBridge");
        return false;
    }

    /// <summary>Loads a mission directly. Unlike the randomizer's boot, there is
    /// no gate to satisfy - the dev tools never lock anything.</summary>
    private void Boot(string specifier)
    {
        GameSpace.specifierToApply = specifier;
        GameSpace.titleToApply = specifier;
        GameSpace.guidToApply = "";
        LoadingScreen.LoadGame(specifier, true, false, GameSpace.CATEGORY.FARSITE, -1);
        _log.LogInfo($"DEVCMD boot: {specifier}");
    }

    private void CloseAda()
    {
        int closed = 0;
        try
        {
            // FindObjectsOfTypeAll, not FindObjectsOfType: the log can be
            // inactive and still needs closing.
            var logs = Resources.FindObjectsOfTypeAll<ADAMessageLog>();
            if (logs != null)
                foreach (var lg in logs)
                {
                    if (lg == null) continue;
                    try { lg.Close(); closed++; } catch { }
                }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD ada:close: {e.Message}"); }
        _log.LogInfo($"DEVCMD ada:close: closed {closed} message log(s)");
    }

    /// <summary>Unpause by clearing every pause owner - a mission that is still
    /// showing its intro holds several, so setting one flag is not enough.</summary>
    private void Sim(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD sim: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "run";

        if (what == "pause") { gs.Pause("cw4dev", true); _log.LogInfo("DEVCMD sim: paused"); return; }

        var owners = new System.Collections.Generic.List<string>();
        try { foreach (var o in gs.pauseOwner) owners.Add(o); } catch { }
        foreach (var o in owners)
        {
            try { gs.Pause(o, false); } catch { }
        }
        if (tok.Length > 1 && int.TryParse(tok[1], out var sp)) gs.GAME_SPEED = sp;
        _log.LogInfo($"DEVCMD sim: cleared [{string.Join(",", owners)}] paused={gs.paused} speed={gs.GAME_SPEED}");
    }

    /// <summary>Places units for testing. Takes the game's REAL unit name, not a
    /// build-pane key: "TowerBridge" not "pylon", "Collector" not "miner",
    /// "CommandBase" not "riftlab". See docs/research-findings.md, "Unit
    /// naming" - passing a build-pane key returns null and places nothing.</summary>
    private void Spawn(string arg)
    {
        var tok = arg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0) { _log.LogWarning("DEVCMD spawn: need a unit name"); return; }
        var name = tok[0];
        int count = tok.Length > 1 && int.TryParse(tok[1], out var c) ? c : 1;

        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD spawn: no GameSpace"); return; }

        Vector3 anchor;
        CommandBase? cb = null;
        try { cb = gs.commandBase; } catch { }
        if (cb != null) anchor = cb.transform.position;
        else
        {
            // World coordinates are 1:1 with cells; only the height needs a
            // lookup. Do NOT use World.GetCreeperVertex - it is mesh-local and
            // returns positions that land off the map.
            int cx = World.WORLD_CELL_WIDTH / 2, cy = World.WORLD_CELL_HEIGHT / 2;
            float y = 0f;
            try { y = UnitManager.GetMinHeight(new Vector3(cx, 0f, cy), 0f, 0, false, false, false); }
            catch { }
            anchor = new Vector3(cx, y, cy);
        }

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var pos = anchor + new Vector3(-8f - 4f * i, 0f, -6f);
            try { if (UnitManager.CreateUnitAtPosition(name, pos) != null) made++; }
            catch (Exception e) { _log.LogWarning($"DEVCMD spawn '{name}': {e.Message}"); break; }
        }
        _log.LogInfo(made == 0
            ? $"DEVCMD spawn {name}: 0/{count} - is that the REAL unit name? build-pane keys do not work"
            : $"DEVCMD spawn {name}: {made}/{count} placed");
    }

    /// <summary>What the cheat strip currently says, and how many times it has
    /// been redrawn. Colour tags are left in: green means on, and asserting on
    /// the tag is how a test reads the strip without a screenshot.</summary>
    private void OverlayDump()
    {
        _log.LogInfo($"DEVCMD overlay: redraws={DevOverlay.Redraws} text='{DevOverlay.LastText}'");
    }

    /// <summary>Open the Farsite Expedition level select from the main menu.
    ///
    /// Needed because synthetic mouse input does not reach CW4's UI: SetCursorPos
    /// plus mouse_event moves the OS cursor and the game ignores the click, so
    /// the menu cannot be driven from outside. Invoking the button's own onClick
    /// is what actually works. (The randomizer has the same command; this is a
    /// deliberate copy, since the dev tools must not depend on it.)</summary>
    private void StoryOpen()
    {
        try
        {
            var gg = GameGalaxy.instance;
            var btn = gg?.farsiteButton?.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) { _log.LogWarning("DEVCMD story:open: no farsite button (are you on the main menu?)"); return; }
            btn.onClick.Invoke();
            _log.LogInfo("DEVCMD story:open");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD story:open: {e.Message}"); }
    }

    /// <summary>Nullify experiments, for the in-game finale gate.
    ///
    /// The question: can a specific structure be made UN-nullifiable, so the
    /// mission cannot be completed until the Archipelago gate opens? Two
    /// candidate levers, both per-instance on UnitManager:
    ///   CAN_NULLIFY   whether a nullifier may target it at all
    ///   impervious    whether it can be harmed
    ///
    ///   null:list              every nullifiable unit, with an index
    ///   null:protect &lt;i|all&gt;   clear CAN_NULLIFY only - impervious is NOT touched
    ///   null:allow &lt;i|all&gt;     set CAN_NULLIFY back to true
    ///   null:kill &lt;i&gt;          spawn a fed nullifier beside unit i
    /// </summary>
    private void Nullify(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD null: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "list";
        string target = tok.Length > 1 ? tok[1].ToLowerInvariant() : "";

        var units = new System.Collections.Generic.List<UnitManager>();
        try { foreach (var u in gs.nullifiableUnits) if (u != null) units.Add(u); } catch { }
        // Sorted by data name then position so the listing is stable to read.
        // NEVER address these by index across commands: the source is a HashSet
        // and its order changes between calls, which silently pointed an earlier
        // version of this probe at the wrong unit.
        units.Sort((a, b) =>
        {
            string na = "", nb = "";
            try { na = a.GetDataName() ?? ""; } catch { }
            try { nb = b.GetDataName() ?? ""; } catch { }
            int c = string.CompareOrdinal(na, nb);
            if (c != 0) return c;
            try { return a.transform.position.x.CompareTo(b.transform.position.x); } catch { return 0; }
        });

        // Match by data-name prefix - CMOD units report a GUID, so "abe9d7ea" is
        // enough to name the neutron reactor.
        System.Collections.Generic.List<UnitManager> Match(string prefix)
        {
            var hits = new System.Collections.Generic.List<UnitManager>();
            foreach (var u in units)
            {
                string nm = "";
                try { nm = (u.GetDataName() ?? "").ToLowerInvariant(); } catch { }
                if (prefix.Length > 0 && (prefix == "all" || nm.StartsWith(prefix)))
                    hits.Add(u);
            }
            return hits;
        }

        if (what == "list")
        {
            _log.LogWarning($"DEVNULL count={units.Count}");
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                string name = "?"; var pos = Vector3.zero;
                bool can = false, imp = false;
                try { name = u.GetDataName() ?? u.GetIl2CppType().Name; } catch { }
                try { pos = u.transform.position; } catch { }
                try { can = u.CAN_NULLIFY; } catch { }
                try { imp = u.impervious; } catch { }
                _log.LogWarning($"DEVNULL [{i}] {name} CAN_NULLIFY={can} impervious={imp} " +
                                $"pos=({pos.x:0},{pos.z:0})");
            }
            return;
        }

        if (what == "protect" || what == "allow")
        {
            bool allow = what == "allow";
            var hits = Match(target);
            foreach (var u in hits)
            {
                // A one-shot write; the sim restores it within a tick. Kept only
                // to demonstrate that, which is why the real lock in the
                // randomizer filters the targeting call instead.
                // Only CAN_NULLIFY is touched. impervious is left alone: these
                // structures are ALREADY impervious in vanilla and still
                // nullifiable, which is how we know nullifying is not damage.
                try { u.CAN_NULLIFY = allow; } catch { }
            }
            _log.LogWarning($"DEVNULL {what} '{target}': {hits.Count} unit(s) -> CAN_NULLIFY={allow}");
            return;
        }

        if (what == "targets")
        {
            // Ask the GAME what a nullifier would consider a valid target at
            // this spot. Better than trying to make a spawned nullifier fire -
            // those never join the network, so they never arm.
            var picked = Match(target);
            if (picked.Count == 0)
            {
                _log.LogWarning($"DEVCMD null:targets: nothing matches '{target}'");
                return;
            }
            try
            {
                var pos = picked[0].transform.position;
                int cx = UnitManager.GetCellX(pos.x), cy = UnitManager.GetCellY(pos.z);
                var found = Nullifier.GetNullifierTargets(cx, cy, 8);
                int n = found == null ? 0 : found.Count;
                _log.LogWarning($"DEVNULL targets near '{target}' at cell ({cx},{cy}): {n}");
                if (found != null)
                    foreach (var f in found)
                    {
                        if (f == null) continue;
                        string nm = "?"; bool can = false;
                        try { nm = f.GetDataName() ?? f.GetIl2CppType().Name; } catch { }
                        try { can = f.CAN_NULLIFY; } catch { }
                        _log.LogWarning($"DEVNULL   target {nm} CAN_NULLIFY={can}");
                    }
            }
            catch (Exception e) { _log.LogWarning($"DEVNULL targets: {e.Message}"); }
            return;
        }

        if (what == "kill")
        {
            if (!int.TryParse(target, out var idx) || idx < 0 || idx >= units.Count)
            {
                _log.LogWarning($"DEVCMD null:kill: index 0..{units.Count - 1}");
                return;
            }
            var u = units[idx];
            Vector3 pos;
            try { pos = u.transform.position; } catch { return; }

            // A nullifier has to sit within range. Place it right beside the
            // target, finish it, feed it, and make it indestructible so the
            // creeper cannot eat it before it fires.
            UnitManager? nul = null;
            try { nul = UnitManager.CreateUnitAtPosition("Nullifier", pos + new Vector3(3f, 0f, 0f)); }
            catch (Exception e) { _log.LogWarning($"DEVNULL kill: {e.Message}"); return; }
            if (nul == null) { _log.LogWarning("DEVNULL kill: could not place a Nullifier"); return; }
            try { nul.CompleteTheBuild(true); } catch { }
            try { nul.ammo = nul.MAX_AMMO; } catch { }
            try { nul.impervious = true; } catch { }
            try { gs.RefreshCollectors(); } catch { }

            string name = "?"; try { name = u.GetDataName() ?? ""; } catch { }
            bool can = false; try { can = u.CAN_NULLIFY; } catch { }
            _log.LogWarning($"DEVNULL kill: nullifier placed beside [{idx}] {name} " +
                            $"(CAN_NULLIFY={can}); watch the count");
            _watchNullCountdown = 900;
            return;
        }

        _log.LogWarning("DEVCMD null: expected list | protect <name> | allow <name> | targets <name> | kill <i>");
    }

    private int _watchNullCountdown;

    private void NullifyWatchTick()
    {
        if (_watchNullCountdown <= 0) return;
        _watchNullCountdown--;
        if (_watchNullCountdown % 120 != 0) return;
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            int n = 0;
            foreach (var u in gs.nullifiableUnits) if (u != null) n++;
            _log.LogWarning($"DEVNULL watch: nullifiable remaining = {n}");
        }
        catch { }
    }

    /// <summary>Objective slots and the live counters behind them.
    ///
    /// Answers what per-instance location checks need to know: whether
    /// MissionObjectiveData.count really is live progress, and what the target
    /// totals are. The totals come from the game's own sets - gs.totems,
    /// gs.nullifiableUnits, gs.mustCollect/maxMustCollect - not from a table we
    /// maintain.</summary>
    private void ObjectiveDump()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD obj:dump: no GameSpace"); return; }
        var world = gs.world;
        if (world == null) { _log.LogWarning("DEVCMD obj:dump: no World"); return; }

        string spec = ""; try { spec = gs.specifier ?? ""; } catch { }

        int totems = 0, totemsOn = 0;
        try
        {
            foreach (var t in gs.totems)
            {
                if (t == null) continue;
                totems++;
                try { if (t.totemComplete) totemsOn++; } catch { }
            }
        }
        catch { }

        int nullifiable = 0;
        try { foreach (var u in gs.nullifiableUnits) { if (u != null) nullifiable++; } } catch { }

        // WHERE the nullify targets are. Caches got cells first because that was
        // the objective under test, but across the 26 SPAN Experiments there is
        // exactly ONE cache and 163 nullifiable units - so for SPAN this is the
        // objective that matters, and a count cannot be aimed at any more than
        // the cache count could.
        try
        {
            foreach (var u in gs.nullifiableUnits)
            {
                if (u == null) continue;
                int creepN = -1, terrN = -1;
                try { creepN = world.GetCreeper(u.cellX, u.cellY); } catch { }
                try { terrN = world.GetTerrain(u.cellX, u.cellY); } catch { }
                _log.LogWarning($"DEVOBJ nullifiable {u.name} at cell ({u.cellX},{u.cellY}) " +
                                $"cellHeight {u.cellHeight} terrain {terrN} creeper {creepN}");
            }
        }
        catch { }
        int mustCollect = 0;
        try { foreach (var u in gs.mustCollect) { if (u != null) mustCollect++; } } catch { }
        int caches = 0;
        try { foreach (var c in gs.infocaches) { if (c != null) caches++; } } catch { }
        int maxCollect = 0; try { maxCollect = gs.maxMustCollect; } catch { }

        // WHERE the caches are, not just how many. A count cannot be aimed at.
        // The static map data said Home's cache was at 145,91; a network built
        // around that cell claimed land all round it and collected nothing,
        // which is exactly what a wrong coordinate looks like from the outside.
        // Ask the live units instead - they are the only authority on this.
        try
        {
            foreach (var u in gs.mustCollect)
            {
                if (u == null) continue;
                _log.LogWarning($"DEVOBJ mustCollect {u.name} at cell ({u.cellX},{u.cellY}) height {u.cellHeight}");
            }
        }
        catch { }
        try
        {
            foreach (var c in gs.infocaches)
            {
                if (c == null) continue;
                // Creeper on the cell, because that is the standing theory for
                // why a network ringing this cache still supplies it nothing:
                // creeper denies the claim, and a frozen creeper preserves
                // whatever was already there rather than clearing it.
                int creep = -1;
                try { creep = world.GetCreeper(c.cellX, c.cellY); } catch { }
                // BURIED is a terrain fact, not a creeper one, and the two get
                // confused constantly. A buried cache sits BELOW the ground at
                // its own cell and needs a Terp to dig out; a flooded one sits
                // on open ground under creeper. Print both numbers so a harness
                // can tell which map it is looking at instead of assuming.
                int terr = -1;
                try { terr = world.GetTerrain(c.cellX, c.cellY); } catch { }
                bool buried = terr >= 0 && terr > c.cellHeight;
                _log.LogWarning($"DEVOBJ infocache {c.name} at cell ({c.cellX},{c.cellY}) " +
                                $"cellHeight {c.cellHeight} terrain {terr} buried {buried} " +
                                $"creeper {creep} retrieved {c.retrieved}");
            }
        }
        catch { }

        // IS THE MISSION ALREADY WON? The SPAN survey found 25 of 26 maps with
        // no REQUIRED objective, which raises a specific danger: if
        // IsMissionComplete() is vacuously true when nothing is required, the
        // randomizer would mark those maps complete the moment they load.
        // Measure it rather than reason about it.
        string won = "?";
        try { won = world.IsMissionComplete().ToString(); } catch (Exception e) { won = "!" + e.Message; }
        string persisted = "?";
        try { persisted = MissionCompletionStats.IsMissionComplete(spec).ToString(); } catch (Exception e) { persisted = "!" + e.Message; }

        _log.LogWarning($"DEVOBJ mission={spec} totems={totemsOn}/{totems} nullifiable={nullifiable} " +
                        $"mustCollect={mustCollect} maxMustCollect={maxCollect} infocaches={caches} " +
                        $"IsMissionComplete={won} persistedComplete={persisted}");

        try
        {
            var objs = world.missionObjectives;
            if (objs == null) { _log.LogWarning("DEVOBJ no missionObjectives"); return; }
            for (int i = 0; i < objs.Length; i++)
            {
                var o = objs[i];
                if (o == null) { _log.LogWarning($"DEVOBJSLOT {i} null"); continue; }
                int count = -1, extra = -1; bool req = false, en = false, done = false; string custom = "";
                try { count = o.count; } catch { }
                try { extra = o.extra; } catch { }
                try { req = o.required; } catch { }
                try { en = o.enabled; } catch { }
                try { custom = o.customName ?? ""; } catch { }
                try { done = world.IsMissionObjectiveComplete(i); } catch { }
                _log.LogWarning($"DEVOBJSLOT {i} enabled={en} required={req} count={count} extra={extra} " +
                                $"complete={done} custom='{custom}'");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD obj:dump: {e.Message}"); }
    }

    // Energy probe state: capacity is measured by over-setting the store and
    // reading what the sim clamps it back to, which takes a tick.
    private int _capCountdown;
    private float _capSaved;

    /// <summary>Probes for the energy levers an AP "storage"/"generation" item
    /// would need. Nothing here ships - it exists to find out whether those
    /// items can do anything before they are added to the pool.
    ///
    ///   energy:cap            measure current storage capacity
    ///   energy:supply &lt;n&gt;     add n to SUPPLY on the player's units, then measure
    ///   energy:const &lt;n&gt;      add n to the Tower/CommandBase SUPPLY constant, then measure
    ///   energy:eff &lt;mult&gt;     multiply Tower.efficiency, then report production
    /// </summary>
    private void Energy(string arg)
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVCMD energy: no GameSpace"); return; }
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var what = tok.Length > 0 ? tok[0].ToLowerInvariant() : "cap";
        float amt = tok.Length > 1 && float.TryParse(tok[1], out var a) ? a : 0f;

        if (what == "cap") { BeginCapacityProbe(gs); return; }

        if (what == "supply")
        {
            int touched = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { u.SUPPLY = u.SUPPLY + (int)amt; touched++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY supply +{(int)amt} on {touched} unit(s)");
            BeginCapacityProbe(gs);
            return;
        }

        if (what == "const")
        {
            // Per-INSTANCE SUPPLY writes did nothing to capacity (measured), so
            // try the per-TYPE constant the instances are built from.
            var ud = gs.unitData;   // UnitData is per-GameSpace, not static
            if (ud == null) { _log.LogWarning("DEVENERGY const: no UnitData"); return; }
            string[] names = { "Tower", "CommandBase" };
            foreach (var n in names)
            {
                try
                {
                    var c = ud.GetUnitContants(n);
                    if (c == null) { _log.LogWarning($"DEVENERGY const: no constants for '{n}'"); continue; }
                    int before = c.SUPPLY;
                    c.SUPPLY = before + (int)amt;
                    ud.SetUnitConstants(n, c);
                    _log.LogWarning($"DEVENERGY const {n}.SUPPLY {before} -> {c.SUPPLY}");
                }
                catch (Exception e) { _log.LogWarning($"DEVENERGY const {n}: {e.Message}"); }
            }
            // Capacity looks cached rather than recomputed every tick - it moved
            // when units were added and then stopped responding. Poke the
            // refresh flag so the change has a chance to be picked up.
            try { gs.shouldRefreshCollectors = true; _log.LogWarning("DEVENERGY const: requested collector refresh"); }
            catch (Exception e) { _log.LogWarning($"DEVENERGY const refresh: {e.Message}"); }
            BeginCapacityProbe(gs);
            return;
        }

        if (what == "eff")
        {
            int towers = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    var t = u.TryCast<Tower>();
                    if (t == null) continue;
                    try { t.efficiency = t.efficiency * (amt <= 0f ? 1f : amt); towers++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY efficiency x{amt} on {towers} tower(s); " +
                            $"production now {gs.energyProduction:0.00} store {gs.energyStore:0.0}");
            return;
        }

        if (what == "scan")
        {
            // Log every numeric energy-ish field on GameSpace and World at once.
            // Run it before and after adding a tower: whichever value moves by
            // the per-tower storage amount IS the capacity. Searching by name
            // has failed twice, so search by behaviour instead.
            var w = gs.world;
            void F(string name, float v) => _log.LogWarning($"DEVSCAN {name}={v:0.###}");
            try { F("gs.energyStore", gs.energyStore); } catch { }
            try { F("gs.energyProduction", gs.energyProduction); } catch { }
            try { F("gs.energyProductionUnClamped", gs.energyProductionUnClamped); } catch { }
            try { F("gs.energyUse", gs.energyUse); } catch { }
            try { F("gs.energyDeficit", gs.energyDeficit); } catch { }
            try { F("gs.avg_energyUse", gs.avg_energyUse); } catch { }
            try { F("gs.avg_energyDeficit", gs.avg_energyDeficit); } catch { }
            try { F("gs.lifticStore", gs.lifticStore); } catch { }
            try { F("gs.argStore", gs.argStore); } catch { }
            try { F("gs.anticreeperStore", gs.anticreeperStore); } catch { }
            try { F("gs.ultracStore", gs.ultracStore); } catch { }
            try { F("gs.treeProduction", gs.treeProduction); } catch { }
            if (w != null)
            {
                try { F("world.statEnergyStore", w.statEnergyStore); } catch { }
                try { F("world.statEnergyGeneration", w.statEnergyGeneration); } catch { }
                try { F("world.statEnergyUse", w.statEnergyUse); } catch { }
                try { F("world.statEnergyBonus", w.statEnergyBonus); } catch { }
                try { F("world.statEnergyEco", w.statEnergyEco); } catch { }
            }
            // The real economy, found after energyStore turned out to be a
            // PERCENTAGE: supplyUsed/supplyMax are the absolute energy and its
            // capacity, and MAX_GEN_RATE caps production. The per-unit SUPPLY
            // constant feeds supplyMax, which is why it is named that.
            try { _log.LogWarning($"DEVSCAN gs.supplyUsed={gs.supplyUsed} gs.supplyMax={gs.supplyMax} gs.MAX_GEN_RATE={gs.MAX_GEN_RATE:0.###}"); }
            catch (Exception e) { _log.LogWarning($"DEVSCAN supply: {e.Message}"); }

            // Energy is held ON the units - the HUD STORE is the network total.
            // So the capacity lever should be per-unit ammo, not a global field.
            try
            {
                float ammo = 0f, maxAmmo = 0f;
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { ammo += u.ammo; maxAmmo += u.MAX_AMMO; } catch { }
                }
                _log.LogWarning($"DEVSCAN sum(unit.ammo)={ammo:0.###} sum(unit.MAX_AMMO)={maxAmmo:0.###}");
                var cb = gs.commandBase;
                if (cb != null)
                    _log.LogWarning($"DEVSCAN riftlab ammo={cb.ammo:0.###} MAX_AMMO={cb.MAX_AMMO:0.###} SUPPLY={cb.SUPPLY}");
            }
            catch (Exception e) { _log.LogWarning($"DEVSCAN ammo: {e.Message}"); }

            int units = 0, towers = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    units++;
                    if (u.TryCast<Tower>() != null) towers++;
                }
            }
            catch { }
            _log.LogWarning($"DEVSCAN units={units} towers={towers}");
            return;
        }

        if (what == "gen")
        {
            // Sustained generation: add energy to the rift lab every frame. The
            // base has no production-rate field, so this IS the lever - and it
            // is indistinguishable from the rift lab producing more, because the
            // store is its ammo.
            DevTools.GenerationBonus = amt;
            _log.LogWarning($"DEVENERGY generation bonus set to {amt:0.###}/sec");
            _watchCountdown = 900;
            return;
        }

        if (what == "build")
        {
            // Place a unit close to the rift lab and leave it UNFINISHED, so it
            // has to draw energy to build. This is how "does the store actually
            // get spent" gets answered - a bigger number proves nothing on its
            // own, as the generation display already taught us.
            string unit = tok.Length > 1 ? tok[1] : "Cannon";
            var cb = gs.commandBase;
            if (cb == null) { _log.LogWarning("DEVENERGY build: no command base"); return; }
            _addIndex++;
            float ang = _addIndex * 1.05f;
            var pos = cb.transform.position + new Vector3(Mathf.Cos(ang) * 5f, 0f, Mathf.Sin(ang) * 5f);
            UnitManager? made = null;
            try { made = UnitManager.CreateUnitAtPosition(unit, pos); } catch { }
            try { gs.RefreshCollectors(); } catch { }
            bool building = false;
            try { building = made != null && made.isBuilding; } catch { }
            _log.LogWarning($"DEVENERGY build {unit}: placed={made != null} isBuilding={building} " +
                            $"riftlab ammo={cb.ammo:0.#}/{cb.MAX_AMMO:0.#}");
            _watchCountdown = 900;
            return;
        }

        if (what == "give")
        {
            // The store IS the rift lab's ammo, so adding to that ammo is real
            // energy - unlike writing gs.energyStore, which is only a mirror the
            // sim recomputes.
            try
            {
                var cb = gs.commandBase;
                if (cb == null) { _log.LogWarning("DEVENERGY give: no command base"); return; }
                float was = cb.ammo;
                cb.ammo = Mathf.Min(cb.ammo + amt, cb.MAX_AMMO);
                _log.LogWarning($"DEVENERGY give: riftlab ammo {was:0.###} -> {cb.ammo:0.###} (max {cb.MAX_AMMO:0.###})");
                _watchCountdown = 600;
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY give: {e.Message}"); }
            return;
        }

        if (what == "ammo")
        {
            // Raise the energy capacity held on the player's units.
            int n = 0;
            try
            {
                foreach (var u in AllPlayerUnits(gs))
                {
                    try { u.MAX_AMMO = u.MAX_AMMO + amt; n++; } catch { }
                }
            }
            catch { }
            _log.LogWarning($"DEVENERGY MAX_AMMO +{amt:0.###} on {n} unit(s)");
            return;
        }

        if (what == "refresh")
        {
            // Units from CreateUnitAtPosition never join the packet network, so
            // every unit-based energy test has measured nothing. RefreshCollectors
            // is the game's own rebuild - if it adopts them, those tests become
            // possible.
            try { gs.RefreshCollectors(); _log.LogWarning("DEVENERGY RefreshCollectors() called"); }
            catch (Exception e) { _log.LogWarning($"DEVENERGY refresh: {e.Message}"); }
            return;
        }

        if (what == "max")
        {
            // Write the real capacity rather than the percentage mirror.
            try
            {
                int was = gs.supplyMax;
                gs.supplyMax = was + (int)amt;
                _log.LogWarning($"DEVENERGY supplyMax {was} -> {gs.supplyMax}");
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY max: {e.Message}"); }
            return;
        }

        if (what == "rate")
        {
            try
            {
                float was = gs.MAX_GEN_RATE;
                gs.MAX_GEN_RATE = was + amt;
                _log.LogWarning($"DEVENERGY MAX_GEN_RATE {was:0.###} -> {gs.MAX_GEN_RATE:0.###}");
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY rate: {e.Message}"); }
            return;
        }

        if (what == "set")
        {
            // Decisive test: is a one-shot write to energyStore kept, clamped to
            // a capacity, or discarded outright? Watch the value over several
            // seconds rather than reading it once.
            try
            {
                float was = gs.energyStore;
                gs.energyStore = amt;
                _log.LogWarning($"DEVENERGY set store {was:0.0} -> {amt:0.0}; watching");
                _watchCountdown = 600;   // ~10s at 60fps
            }
            catch (Exception e) { _log.LogWarning($"DEVENERGY set: {e.Message}"); }
            return;
        }

        if (what == "add")
        {
            // Spawn one unit of a named type and report the capacity delta, so
            // the per-unit storage contribution is measured directly.
            string unit = tok.Length > 1 ? tok[1] : "Tower";
            float before = MeasureNow(gs);
            var cb = gs.commandBase;
            // Spread them out. Towers collect from the land they claim, so
            // stacking them on one spot adds no generation at all - which is
            // exactly what made an earlier run read GEN 1 with three towers.
            // Close to the rift lab, spread by ANGLE only. A tower has to be
            // within the base's connection range to join the network, and it
            // collects from the land it claims - so an earlier version that grew
            // the radius each time put them out of range, and one before that
            // stacked them on a single spot. Both produced exactly nothing.
            _addIndex++;
            float ang = _addIndex * 1.05f;
            const float rad = 5f;
            var pos = cb != null
                ? cb.transform.position + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad)
                : Vector3.zero;
            UnitManager? created = null;
            try { created = UnitManager.CreateUnitAtPosition(unit, pos); } catch { }
            string kind = "null";
            bool building = false;
            if (created != null)
            {
                try { kind = created.GetIl2CppType().Name; } catch { }
                try { building = created.isBuilding; } catch { }
                // Finish it and rebuild the network, or it produces nothing.
                try { created.CompleteTheBuild(true); } catch { }
            }
            try { gs.RefreshCollectors(); } catch { }
            _log.LogWarning($"DEVENERGY add {unit}: type={kind} wasBuilding={building} store={before:0.0}");
            BeginCapacityProbe(gs);
            return;
        }

        _log.LogWarning("DEVCMD energy: expected cap | supply <n> | const <n> | eff <mult> | add <unit>");
    }

    /// <summary>Capacity right now, by over-setting the store and letting the
    /// sim clamp it back within the same call is not possible - this returns the
    /// last known value instead, for logging a before/after pair.</summary>
    private static float MeasureNow(GameSpace gs)
    {
        try { return gs.energyStore; } catch { return -1f; }
    }

    private void BeginCapacityProbe(GameSpace gs)
    {
        try
        {
            _capSaved = gs.energyStore;
            gs.energyStore = 1_000_000f;   // the sim clamps this to capacity
            _capCountdown = 15;            // ~a quarter second of ticks
            _log.LogWarning($"DEVENERGY capacity probe: store was {_capSaved:0.0}, over-set; reading back shortly");
        }
        catch (Exception e) { _log.LogWarning($"DEVENERGY capacity probe: {e.Message}"); }
    }

    private int _addIndex;
    private int _watchCountdown;

    private void WatchTick()
    {
        if (_watchCountdown <= 0) return;
        _watchCountdown--;
        if (_watchCountdown % 120 != 0) return;   // log about every 2 seconds
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            var w = gs.world;
            float hud = -1f; try { if (w != null) hud = w.statEnergyStore; } catch { }
            _log.LogWarning($"DEVENERGY watch t-{_watchCountdown / 60}s store={gs.energyStore:0.0} HUDstore={hud:0.0}");
        }
        catch { }
    }

    private void CapacityProbeTick()
    {
        if (_capCountdown <= 0) return;
        if (--_capCountdown > 0) return;
        try
        {
            var gs = GameSpace.instance;
            if (gs == null) return;
            // Report the HUD's numbers alongside the raw ones. The GEN/USE/STORE
            // readout does not necessarily show absolute energy - if STORE reads
            // 100 when full it is a percentage, and the absolute cap is what
            // energyStore clamps to.
            float sStore = -1f, sGen = -1f, sUse = -1f, sBonus = -1f, sEco = -1f;
            var w = gs.world;
            if (w != null)
            {
                try { sStore = w.statEnergyStore; } catch { }
                try { sGen = w.statEnergyGeneration; } catch { }
                try { sUse = w.statEnergyUse; } catch { }
                try { sBonus = w.statEnergyBonus; } catch { }
                try { sEco = w.statEnergyEco; } catch { }
            }
            _log.LogWarning($"DEVENERGY measured cap={gs.energyStore:0.0} " +
                            $"prod={gs.energyProduction:0.00} use={gs.energyUse:0.00} | " +
                            $"HUD store={sStore:0.0} gen={sGen:0.00} use={sUse:0.00} " +
                            $"bonus={sBonus:0.00} eco={sEco:0.00}");
        }
        catch { }
    }

    private static System.Collections.Generic.IEnumerable<UnitManager> AllPlayerUnits(GameSpace gs)
    {
        foreach (var u in gs.units) if (u != null) yield return u;
        var fly = gs.flyingUnits;
        if (fly != null) foreach (var f in fly) if (f != null) yield return f;
    }

    /// <summary>Terrain, unit cells and reach constants for the live mission,
    /// written to a file for offline reachability analysis.
    ///
    /// WHAT IT IS FOR. "Does this objective need a Terp, Platform, Porter or
    /// Pylon to reach?" is a connectivity question: can a tower chain get from
    /// the rift lab to the objective, and if not, which bridge works. That needs
    /// the terrain, the objective cells, and each unit's reach - none of which
    /// anything in this repo has ever read.
    ///
    /// A FILE, not the log: a 200x150 map is 30,000 cells.
    ///
    /// GetTerrain(x, y) is used rather than the raw World.terrain array on
    /// purpose. That array has a sectored layout, and indexing it wrongly would
    /// yield a plausible but scrambled map - a silent failure of exactly the
    /// kind that survives review. 30,000 accessor calls is the cheaper mistake.
    ///
    /// THREE terrain signals are dumped because which one means "buildable" is
    /// unverified, and picking one here would bake in a guess. The Farsite
    /// control decides it: those missions' mover requirements are already known
    /// from play, so whichever signal reproduces them is the right one.</summary>
    private void MapDump(string path)
    {
        if (path.Length == 0) { _log.LogWarning("DEVCMD map:dump: need a path"); return; }
        var gs = GameSpace.instance;
        var w = gs?.world;
        if (w == null) { _log.LogWarning("DEVCMD map:dump: no world - boot a mission first"); return; }

        try
        {
            int W = World.WORLD_CELL_WIDTH, H = World.WORLD_CELL_HEIGHT;
            var sb = new System.Text.StringBuilder();
            string spec = ""; try { spec = GameSpace.specifierToApply ?? ""; } catch { }

            sb.Append("mission=").Append(spec).Append('\n');
            sb.Append("cells=").Append(W).Append('x').Append(H).Append('\n');
            sb.Append("maxLandHeight=").Append(World.MAX_LAND_HEIGHT).Append('\n');
            try { sb.Append("towerPlacementRange=").Append(Tower.PLACEMENT_RANGE).Append('\n'); }
            catch (Exception e) { sb.Append("towerPlacementRange=!").Append(e.Message).Append('\n'); }

            // Every unit on the map with its cell and its reach. The rift lab,
            // the totems, the caches and the nullify targets are all in here;
            // which is which is the analyser's problem, not this command's.
            int units = 0;
            try
            {
                foreach (var u in gs.units)
                {
                    if (u == null) continue;
                    string nm = ""; try { nm = u.GetDataName() ?? ""; } catch { }
                    int cx = -1, cy = -1, cr = -1, rng = -1;
                    bool conn = false, mine = false, nullifiable = false;
                    float wy = 0f;
                    try { cx = u.cellX; cy = u.cellY; } catch { }
                    try { cr = UnitManager.CONNECT_RANGE; } catch { }
                    try { rng = u.RANGE; } catch { }
                    try { conn = u.CONNECTABLE; } catch { }
                    try { mine = DevTools.IsPlayerUnit(u); } catch { }
                    // Whether this is a NULLIFY objective. Without it the
                    // reachability analysis could only cover totems, and nullify
                    // targets are the larger class by some margin.
                    try { nullifiable = u.CAN_NULLIFY; } catch { }
                    // Elevation, for the BURIED question. What makes a cache
                    // buried is sitting below the terrain above it, which the
                    // column height alone cannot express.
                    try { wy = u.transform.position.y; } catch { }
                    var held = new System.Text.StringBuilder();
                    try
                    {
                        var wh = u.waresHeld;
                        if (wh != null)
                            foreach (var kv in wh)
                            {
                                if (kv.Value <= 0) continue;
                                if (held.Length > 0) held.Append(',');
                                held.Append('w').Append(kv.Key).Append('x').Append(kv.Value);
                            }
                    }
                    catch { }
                    // WHAT A POD CONTAINS. The designer, 2026-09-16: "pods can
                    // have different resources in them". So "this map has a Pod"
                    // does NOT mean "this map hands you liftic" - the resource
                    // has to be read, not inferred from the unit's name. Pod
                    // carries it on resourceType; waresHeld is empty at load.
                    string pod = "";
                    try
                    {
                        var pd = u.TryCast<Pod>();
                        if (pd != null) pod = pd.resourceType.ToString();
                    }
                    catch { }
                    sb.Append("unit ").Append(nm).Append(' ').Append(cx).Append(',').Append(cy)
                      .Append(" connect=").Append(cr).Append(" range=").Append(rng)
                      .Append(" connectable=").Append(conn).Append(" mine=").Append(mine)
                      .Append(" nullifiable=").Append(nullifiable)
                      .Append(" worldY=").Append(wy.ToString("0.00"))
                      .Append(" holds=").Append(held.Length == 0 ? "-" : held.ToString())
                      .Append(" podWare=").Append(pod.Length == 0 ? "-" : pod)
                      .Append('\n');
                    units++;
                }
            }
            catch (Exception e) { sb.Append("units=!").Append(e.Message).Append('\n'); }

            // Three grids, one row per y, one character per x.
            sb.Append("terrain\n");
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int v = 0;
                    try { v = w.GetTerrain(x, y); } catch { v = -1; }
                    // 0-9 then A-K, so the full 0..MAX_LAND_HEIGHT (20) range
                    // survives. This used to clamp at 9, which was harmless for
                    // land-versus-void but silently flattened every column above
                    // 9 - and that broke the buried-cache test, where a cache at
                    // elevation 15 under a height-15 column looked like it was
                    // sitting 6 units ABOVE a height-9 one.
                    sb.Append(v < 0 ? '?' : (v < 10 ? (char)('0' + v) : (char)('A' + v - 10)));
                }
                sb.Append('\n');
            }

            sb.Append("platform\n");
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    bool v = false;
                    try { v = w.GetPlatform(x, y); } catch { }
                    sb.Append(v ? '1' : '0');
                }
                sb.Append('\n');
            }

            sb.Append("legal\n");
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    bool v = false;
                    try { v = w.GetLegalUnitCellIfSet(x, y); } catch { }
                    sb.Append(v ? '1' : '0');
                }
                sb.Append('\n');
            }

            System.IO.File.WriteAllText(path, sb.ToString());
            _log.LogWarning($"DEVMAP wrote '{path}' mission={spec} cells={W}x{H} units={units}");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD map:dump: {e.Message}"); }
    }

    /// <summary>What each ware index is called, as the game itself names it.
    ///
    /// Totem demand is reported as a ware INDEX (w29, w30), which says nothing
    /// about what the player is being asked for. The campaign only ever uses
    /// one of them, so the index alone could not distinguish "this map wants
    /// liftic like every other" from "this map wants something else entirely" -
    /// and four SPAN maps turned out to want ware29.
    ///
    /// DeliveryPadControls.GetWareName is the lookup the delivery pad UI uses,
    /// so it gives the same word a player sees on hover. An earlier attempt
    /// recovered the names from the IL2CPP string heap instead
    /// (Anticreeper/Arg/Liftic/Resistium/Fluxygen/Tuffium) but that heap is
    /// unordered, so pairing them with indices would have been a guess.</summary>
    private void WareNames()
    {
        try
        {
            DeliveryPadControls? dpc = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<DeliveryPadControls>())
            {
                if (c != null) { dpc = c; break; }
            }
            if (dpc == null)
            {
                _log.LogWarning("DEVWARE: no DeliveryPadControls in the scene - boot a mission first");
                return;
            }
            for (int w = 0; w < 48; w++)
            {
                string nm;
                try { nm = dpc.GetWareName(w) ?? ""; } catch (Exception e) { nm = "!" + e.Message; }
                if (nm.Length == 0) continue;
                _log.LogWarning($"DEVWARE {w} = {nm}");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD wares:names: {e.Message}"); }
    }

    /// <summary>What every totem on this map wants, per totem, with its cell.
    ///
    /// WHY IT MATTERS. Campaign totems all want liftic, which is why the
    /// randomizer's totem rules demand the Factory - liftic comes from the
    /// greenar chain. But the wanted ware is authored PER MAP, and a map that
    /// wants something else would be gated behind an item it does not need.
    /// With 95 totems across the SPAN roster, guessing this wrong is expensive.
    ///
    /// The cell is included so demand can be tied to the mod's instance
    /// numbering, which orders structures by (cellY, cellX) ascending.
    /// GetAmmoWareWanted is probed across all 16 ware slots rather than assuming
    /// which one liftic is - the point is to find out.</summary>
    private void TotemWares()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVTOTEM: no GameSpace - boot a mission first"); return; }

        string spec = ""; try { spec = GameSpace.specifierToApply ?? ""; } catch { }
        int totems = 0;
        var tally = new System.Collections.Generic.Dictionary<int, int>();

        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try { if (u.GetIl2CppType().Name != "Totem") continue; } catch { continue; }
            totems++;
            int cx = -1, cy = -1;
            try { cx = u.cellX; cy = u.cellY; } catch { }

            // AUTHORED demand. AMMO_WARES is the ware-type-to-amount map the
            // mission designer set; GetAmmoWareWanted is only the CURRENT
            // shortfall, which is zero on an inactive totem and reported
            // wares[NONE] across all 17 campaign missions.
            var authored = new System.Text.StringBuilder();
            try
            {
                var aw = u.AMMO_WARES;
                if (aw != null)
                {
                    foreach (var kv in aw)
                    {
                        if (authored.Length > 0) authored.Append(',');
                        authored.Append('w').Append(kv.Key).Append('x').Append(kv.Value);
                        tally[kv.Key] = tally.TryGetValue(kv.Key, out var c)
                            ? c + kv.Value : kv.Value;
                    }
                }
            }
            catch (Exception e) { authored.Append('!').Append(e.Message); }

            // Kept alongside so the difference between the two is visible in
            // the data rather than only asserted in a comment.
            var wanted = new System.Text.StringBuilder();
            for (int w = 0; w < 16; w++)
            {
                int amt;
                try { amt = u.GetAmmoWareWanted(w); } catch { continue; }
                if (amt <= 0) continue;
                if (wanted.Length > 0) wanted.Append(',');
                wanted.Append('w').Append(w).Append('x').Append(amt);
            }

            _log.LogWarning($"DEVTOTEM mission={spec} cell={cx},{cy} " +
                            $"authored={(authored.Length == 0 ? "NONE" : authored.ToString())} " +
                            $"wantedNow={(wanted.Length == 0 ? "NONE" : wanted.ToString())}");
        }

        var totals = new System.Text.StringBuilder();
        foreach (var kv in tally)
        {
            if (totals.Length > 0) totals.Append(',');
            totals.Append("ware").Append(kv.Key).Append('=').Append(kv.Value);
        }
        _log.LogWarning($"DEVTOTEMS mission={spec} count={totems} " +
                        $"wares[{(totals.Length == 0 ? "NONE" : totals.ToString())}]");
    }

    /// <summary>Open the SPAN Experiments grid, the way story:open opens Farsite.
    ///
    /// The randomizer hides this button by default (ModConfig.ShowSpan), and a
    /// click on an inactive GameObject does nothing and reports nothing - so
    /// check and SAY so rather than logging a success that did not happen.</summary>
    private void SpanOpen()
    {
        try
        {
            var gg = GameGalaxy.instance;
            var go = gg?.spanButton;
            if (go == null) { _log.LogWarning("DEVCMD span:open: no span button (are you on the main menu?)"); return; }
            if (!go.activeInHierarchy)
            {
                _log.LogWarning("DEVCMD span:open: the span button is HIDDEN - the randomizer " +
                                "plugin hides it unless Missions/ShowSpan is true. Park the " +
                                "randomizer out of plugins/, or set ShowSpan=true.");
                return;
            }
            var btn = go.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) { _log.LogWarning("DEVCMD span:open: span button has no Button component"); return; }
            btn.onClick.Invoke();
            _log.LogInfo("DEVCMD span:open");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:open: {e.Message}"); }
    }

    /// <summary>Enumerate the SPAN Experiments grid: every tile, its address,
    /// its difficulty, and which objective types it advertises.
    ///
    /// WHY THIS EXISTS. SPAN maps are not storyN-addressed and cannot be listed
    /// offline - data.unity3d is compressed and the names appear nowhere in the
    /// repo. But the grid itself knows: SpanTile carries (x, y, page), a
    /// difficulty grade and one indicator GameObject per objective type, so the
    /// roster and a first cut of every map's shape can be read from the MENU
    /// without booting anything.
    ///
    /// Tiles are found with FindObjectsOfTypeAll rather than by walking
    /// SpanSector.spanTiles0/1/2: the off-page arrays belong to pages that are
    /// not active, and this avoids depending on the element type of an
    /// Il2CppReferenceArray.
    ///
    /// ARGUMENT ORDER IS DELIBERATELY NOT ASSUMED. SpanSector.GetLoc and GetGUID
    /// both take three ints and the interop metadata does not say whether they
    /// are (x, y, page) or (page, x, y). BOTH orders are printed so the data
    /// settles it - guessing would silently address the wrong map, and a wrong
    /// guid is exactly the kind of error that looks like it worked.</summary>
    private void SpanList()
    {
        try
        {
            // WHICH SCREEN ARE WE ON. The first run found the grid object but
            // no instantiated tiles, which can mean "wrong screen" or "grid not
            // yet enabled" and those need different fixes. Report the active
            // state of everything Span-ish rather than inferring it.
            try
            {
                foreach (var t2 in Resources.FindObjectsOfTypeAll<TheSpanSector>())
                {
                    if (t2 == null) continue;
                    int names = -1;
                    try { names = TheSpanSector.planetNames == null ? -1 : TheSpanSector.planetNames.Count; } catch { }
                    try
                    {
                        var pn = TheSpanSector.planetNames;
                        if (pn != null)
                            for (int i = 0; i < pn.Count; i++)
                                _log.LogWarning($"DEVSPANNAME {i}: {pn[i]}");
                    }
                    catch (Exception e) { _log.LogWarning($"DEVSPANNAME: {e.Message}"); }
                    _log.LogWarning($"DEVSPANSYS '{t2.gameObject.name}' active={t2.gameObject.activeInHierarchy} " +
                                    $"systems={TheSpanSector.SYSTEM_COUNT} sections={TheSpanSector.SECTION_COUNT} " +
                                    $"planetNames={names} complete={TheSpanSector.GetCompleteCount()}");
                }
            }
            catch (Exception e) { _log.LogWarning($"DEVSPANSYS: {e.Message}"); }

            try
            {
                int cur = MissionCompletionStats.GetCurrentSpanSystem();
                int items = -1;
                try { items = SpanItems.spanItems == null ? -1 : SpanItems.spanItems.Count; } catch { }
                _log.LogWarning($"DEVSPANPROGRESS currentSystem={cur} initialized={SpanItems.initialized} " +
                                $"spanItems={items}");
            }
            catch (Exception e) { _log.LogWarning($"DEVSPANPROGRESS: {e.Message}"); }

            try
            {
                foreach (var sp in Resources.FindObjectsOfTypeAll<Span>())
                {
                    if (sp == null) continue;
                    _log.LogWarning($"DEVSPANVIEW '{sp.gameObject.name}' category={sp.category} " +
                                    $"active={sp.gameObject.activeInHierarchy}");
                }
            }
            catch (Exception e) { _log.LogWarning($"DEVSPANVIEW: {e.Message}"); }

            var tiles = Resources.FindObjectsOfTypeAll<SpanTile>();
            if (tiles == null || tiles.Length == 0)
            {
                _log.LogWarning("DEVCMD span:list: no tiles - open the grid first (span:open)");
                return;
            }

            foreach (var s in Resources.FindObjectsOfTypeAll<SpanSector>())
            {
                if (s == null) continue;
                try
                {
                    _log.LogWarning($"DEVSPANSECTOR '{s.gameObject.name}' category={s.category} " +
                                    $"W={SpanSector.WIDTH} H={SpanSector.HEIGHT} maxPage={SpanSector.MAXPAGE} " +
                                    $"page={s.page} itemCount={s.itemCount} " +
                                    $"active={s.gameObject.activeInHierarchy}");
                }
                catch (Exception e) { _log.LogWarning($"DEVSPANSECTOR: {e.Message}"); }
            }

            _log.LogWarning($"DEVSPANTILES count={tiles.Length}");
            foreach (var tl in tiles)
            {
                if (tl == null) continue;
                int x = -1, y = -1, page = -1, diff = -1;
                try { x = tl.x; y = tl.y; page = tl.page; diff = tl.difficulty; } catch { }

                string act;
                try
                {
                    act = $"nullify={Act(tl.objectiveNullify)} totem={Act(tl.objectiveTotem)} " +
                          $"reclaim={Act(tl.objectiveReclaim)}";
                }
                catch (Exception e) { act = "objectives=" + e.Message; }

                string inter = ""; try { inter = tl.interactable.ToString(); } catch { }
                string sector = ""; try { sector = tl.sector?.gameObject.name ?? ""; } catch { }

                string locA = "", locB = "", guidA = "", guidB = "";
                try { locA = SpanSector.GetLoc(x, y, page) ?? ""; } catch (Exception e) { locA = "!" + e.Message; }
                try { locB = SpanSector.GetLoc(page, x, y) ?? ""; } catch (Exception e) { locB = "!" + e.Message; }
                try { guidA = SpanSector.GetGUID(x, y, page) ?? ""; } catch (Exception e) { guidA = "!" + e.Message; }
                try { guidB = SpanSector.GetGUID(page, x, y) ?? ""; } catch (Exception e) { guidB = "!" + e.Message; }

                _log.LogWarning($"DEVSPANTILE page={page} x={x} y={y} diff={diff} " +
                                $"interactable={inter} {act} sector='{sector}' " +
                                $"loc(x,y,page)='{locA}' loc(page,x,y)='{locB}' " +
                                $"guid(x,y,page)='{guidA}' guid(page,x,y)='{guidB}'");
            }

            // What the panel currently holds for the selected tile - the title,
            // specifier and guid a boot would need. If this is empty, selecting a
            // tile is a required step before a map can be identified.
            foreach (var gmp in Resources.FindObjectsOfTypeAll<GalaxyMissionPanel>())
            {
                if (gmp == null) continue;
                try
                {
                    var d = gmp.gmd;
                    if (d == null)
                    {
                        _log.LogWarning($"DEVSPANGMP '{gmp.gameObject.name}' category={gmp.category} gmd=null");
                        continue;
                    }
                    _log.LogWarning($"DEVSPANGMP '{gmp.gameObject.name}' category={gmp.category} " +
                                    $"title='{d.missionTitle}' specifier='{d.specifier}' " +
                                    $"guid='{d.guid}' map={d.mapWidth}x{d.mapHeight}");
                }
                catch (Exception e) { _log.LogWarning($"DEVSPANGMP: {e.Message}"); }
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:list: {e.Message}"); }
    }

    private static string Act(GameObject? go) => go == null ? "null" : (go.activeSelf ? "ON" : "off");

    /// <summary>Launch a SPAN Experiment by its planet guid, e.g.
    /// span:boot knucracker1.
    ///
    /// The Farsite boot above hardcodes specifier=storyN, guid="" and
    /// embeddedLoad=true. At least two of those are wrong for SPAN: its maps are
    /// not in the embedded story assets, and its identity is a guid rather than
    /// a storyN string. Rather than guess the replacements - a wrong guid would
    /// load the wrong map and still look like it worked - this does what a click
    /// does: select the planet, let the game fill GalaxyMissionPanel.gmd, and
    /// read the specifier and guid back out of it. IsEmbeddedCategory settles
    /// the third argument.
    ///
    /// Requires the SPAN screen to be open (span:open).</summary>
    private void SpanBoot(string guid)
    {
        if (guid.Length == 0) { _log.LogWarning("DEVCMD span:boot: need a planet guid, e.g. knucracker1"); return; }
        try
        {
            SpanNetworkPlanet? planet = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) { planet = p; break; }
            }
            if (planet == null)
            {
                // DIRECT ROUTE. No SPAN screen, so no GalaxyMissionData to read:
                // assume specifier == guid, which is what the screen-based route
                // measured on Forgotten Fortress. Labelled UNVERIFIED because a
                // map whose specifier differs would load the wrong thing and
                // look successful - obj:dump reports the mission that actually
                // loaded, so the caller can catch it.
                bool emb;
                try { emb = GalaxyMissionPanel.IsEmbeddedCategory(GameSpace.CATEGORY.SPAN); } catch { emb = true; }
                _log.LogWarning($"DEVSPANBOOT guid='{guid}' route=DIRECT-UNVERIFIED " +
                                $"specifier='{guid}' embedded={emb}");
                GameSpace.specifierToApply = guid;
                GameSpace.titleToApply = guid;
                GameSpace.guidToApply = guid;
                LoadingScreen.LoadGame(guid, emb, false, GameSpace.CATEGORY.SPAN, -1);
                _log.LogWarning($"DEVCMD span:boot: {guid} (direct)");
                return;
            }

            // Select it the way a click would, so the panel populates.
            Span? view = null;
            foreach (var sp in UnityEngine.Object.FindObjectsOfType<Span>())
            {
                if (sp == null) continue;
                if (sp.category == GameSpace.CATEGORY.SPAN) { view = sp; break; }
            }
            if (view == null) { _log.LogWarning("DEVCMD span:boot: no active SPAN view"); return; }
            try { view.SelectPlanet(planet); } catch (Exception e) { _log.LogWarning($"DEVCMD span:boot SelectPlanet: {e.Message}"); }
            try { view.SetMission(planet); } catch (Exception e) { _log.LogWarning($"DEVCMD span:boot SetMission: {e.Message}"); }

            var gmp = view.gmp;
            var d = gmp?.gmd;
            if (d == null)
            {
                _log.LogWarning($"DEVCMD span:boot: selecting '{guid}' did not populate a mission record");
                return;
            }

            bool embedded = true;
            try { embedded = GalaxyMissionPanel.IsEmbeddedCategory(GameSpace.CATEGORY.SPAN); } catch { }

            _log.LogWarning($"DEVSPANBOOT guid='{guid}' route=PANEL title='{d.missionTitle}' " +
                            $"specifier='{d.specifier}' gmdGuid='{d.guid}' " +
                            $"map={d.mapWidth}x{d.mapHeight} embedded={embedded}");

            GameSpace.specifierToApply = d.specifier ?? "";
            GameSpace.titleToApply = d.missionTitle ?? "";
            GameSpace.guidToApply = d.guid ?? "";
            LoadingScreen.LoadGame(d.specifier, embedded, false, GameSpace.CATEGORY.SPAN, -1);
            _log.LogWarning($"DEVCMD span:boot: {guid} ({d.missionTitle})");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:boot: {e.Message}"); }
    }

    /// <summary>Retarget a Farsite level-select planet at a SPAN map.
    ///
    ///     span:swap story5 knucracker1
    ///
    /// Sets every field that identifies the mission behind a planet, because
    /// which one the launch path reads is exactly what this is testing:
    /// planetGUID, map_guid, map_title, map_desc, map_width, map_height and
    /// map_objectives. The last is the objective bitmask the map draws its icons
    /// from - the randomizer already rebuilds that icon set in TrackerView, so
    /// this only has to make the underlying data right.
    ///
    /// Prints before and after so a swap that silently did nothing is visible
    /// rather than inferred from the launch failing later.</summary>
    private void SpanSwap(string arg)
    {
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 2)
        {
            _log.LogWarning("DEVCMD span:swap: need <planetGuid> <spanGuid>, e.g. span:swap story5 knucracker1");
            return;
        }
        string from = tok[0], to = tok[1];
        try
        {
            SpanNetworkPlanet? target = null;
            foreach (var p in Resources.FindObjectsOfTypeAll<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, from, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            }
            if (target == null) { _log.LogWarning($"DEVCMD span:swap: no planet '{from}' - open the map first (story:open)"); return; }

            string beforeTitle = "", beforeMapGuid = "";
            int beforeObj = -1;
            try { beforeTitle = target.map_title ?? ""; } catch { }
            try { beforeMapGuid = target.map_guid ?? ""; } catch { }
            try { beforeObj = target.map_objectives; } catch { }
            _log.LogWarning($"DEVSWAP before planet='{from}' mapGuid='{beforeMapGuid}' " +
                            $"title='{beforeTitle}' objectives={beforeObj}");

            try { target.planetGUID = to; } catch (Exception e) { _log.LogWarning($"  planetGUID: {e.Message}"); }
            try { target.map_guid = to; } catch (Exception e) { _log.LogWarning($"  map_guid: {e.Message}"); }
            try { target.map_title = to; } catch (Exception e) { _log.LogWarning($"  map_title: {e.Message}"); }
            try { if (target.title != null) target.title.text = to; } catch { }
            try { target.forceUnlocked = true; target.unlocked = true; target.unlockedSet = true; } catch { }
            // THE OBJECTIVE BITMASK, which drives the icons the map draws.
            // Omitting it left the swapped planet showing the ORIGINAL
            // mission's icons - visible in a screenshot and invisible in every
            // log line, which is why the first version of this looked like it
            // had worked. Bit k = objective slot k (0 Nullify, 1 Totems,
            // 2 Reclaim, 3 Hold, 4 Collect, 5 Custom).
            if (tok.Length >= 3 && int.TryParse(tok[2], out var objMask))
            {
                try { target.map_objectives = (byte)objMask; }
                catch (Exception e) { _log.LogWarning($"  map_objectives: {e.Message}"); }
            }

            string afterTitle = "", afterMapGuid = "", afterPlanet = "";
            try { afterTitle = target.map_title ?? ""; } catch { }
            try { afterMapGuid = target.map_guid ?? ""; } catch { }
            try { afterPlanet = target.planetGUID ?? ""; } catch { }
            _log.LogWarning($"DEVSWAP after  planet='{afterPlanet}' mapGuid='{afterMapGuid}' " +
                            $"title='{afterTitle}'");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:swap: {e.Message}"); }
    }

    /// <summary>Spacing between objective icons, in the container's local units.
    /// Measured off the shipped map: icon k sits at x = 0.55 * k.</summary>
    private const float GlyphSpacing = 0.55f;

    /// <summary>Rebuild a planet's objective icons to an arbitrary slot set.
    ///
    ///     span:icons knucracker1 0,1,2      Nullify, Totems, Reclaim
    ///
    /// Setting SpanNetworkPlanet.map_objectives does NOT do this: that byte is
    /// read when the map is built, so writing it afterwards leaves the icons
    /// already on screen untouched. The icons are real GameObjects and have to
    /// be reconfigured.
    ///
    /// Mirrors TrackerView.ReconcileGlyphs in the randomizer, including three
    /// details that each cost something to learn there: search with
    /// includeInactive (a locked planet's whole container is deactivated), HIDE
    /// surplus markers rather than destroy them (they are the game's), and clone
    /// the material when cloning a marker (Instantiate shares it, so one recolour
    /// would hit every copy).</summary>
    private void SpanIcons(string arg)
    {
        var tok = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 2)
        {
            _log.LogWarning("DEVCMD span:icons: need <planetGuid> <slots>, e.g. span:icons knucracker1 0,1,2");
            return;
        }
        var want = new System.Collections.Generic.List<int>();
        foreach (var s in tok[1].Split(','))
        {
            if (int.TryParse(s.Trim(), out var v) && v >= 0 && v < 6) want.Add(v);
        }
        if (want.Count == 0) { _log.LogWarning("DEVCMD span:icons: no valid slots"); return; }

        try
        {
            SpanNetworkPlanet? target = null;
            foreach (var p in Resources.FindObjectsOfTypeAll<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, tok[0], StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            }
            if (target == null) { _log.LogWarning($"DEVCMD span:icons: no planet '{tok[0]}'"); return; }

            Transform container;
            try { container = target.objectiveContainer; } catch { _log.LogWarning("DEVICONS: no objectiveContainer"); return; }
            if (container == null) { _log.LogWarning("DEVICONS: objectiveContainer is null"); return; }

            // includeInactive MUST be true - a locked planet has its whole
            // container deactivated, so an active-only walk finds nothing.
            var markers = container.GetComponentsInChildren<SpanNetworkPlanetObjective>(true);
            if (markers == null || markers.Length == 0) { _log.LogWarning("DEVICONS: no markers found"); return; }

            var live = new System.Collections.Generic.List<SpanNetworkPlanetObjective>();
            foreach (var m in markers) if (m != null) live.Add(m);
            int before = live.Count;

            for (int k = 0; k < want.Count; k++)
            {
                SpanNetworkPlanetObjective slot;
                if (k < live.Count) slot = live[k];
                else
                {
                    SpanNetworkPlanetObjective? made = null;
                    try
                    {
                        var go = UnityEngine.Object.Instantiate(live[0].gameObject, container, false);
                        go.name = "AddedGlyph";
                        made = go.GetComponent<SpanNetworkPlanetObjective>();
                        // Instantiate SHARES the material - clone it or a later
                        // recolour of one icon repaints all of them.
                        try { var mr = go.GetComponent<MeshRenderer>(); mr.material = new Material(mr.material); } catch { }
                    }
                    catch (Exception e) { _log.LogWarning($"DEVICONS clone: {e.Message}"); }
                    if (made == null) break;
                    slot = made; live.Add(made);
                }
                try { if (!slot.gameObject.activeSelf) slot.gameObject.SetActive(true); } catch { }
                try { slot.objective = want[k]; } catch (Exception e) { _log.LogWarning($"DEVICONS objective: {e.Message}"); }
                try { slot.transform.localPosition = new Vector3(GlyphSpacing * k, 0f, 0f); } catch { }
            }
            // Surplus markers are HIDDEN, never destroyed - they are the game's.
            for (int k = want.Count; k < live.Count; k++)
            {
                try { live[k].gameObject.SetActive(false); } catch { }
            }

            _log.LogWarning($"DEVICONS planet='{tok[0]}' markers {before} -> {live.Count}, " +
                            $"showing [{string.Join(",", want)}]");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:icons: {e.Message}"); }
    }

    /// <summary>Select a level-select planet and launch it, the way a click
    /// would - so a swapped planet is tested through the REAL path rather than
    /// through span:boot, which bypasses the map entirely.
    ///
    /// Reports the panel's category and the mission record it produced before
    /// launching, because those are what decide whether a SPAN map loads at all
    /// and which save folder it lands in.</summary>
    private void SpanPlay(string guid)
    {
        if (guid.Length == 0) { _log.LogWarning("DEVCMD span:play: need a planet guid"); return; }
        try
        {
            SpanNetworkPlanet? target = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            }
            if (target == null) { _log.LogWarning($"DEVCMD span:play: no planet '{guid}'"); return; }

            var view = target.span;
            if (view == null) { _log.LogWarning("DEVCMD span:play: planet has no Span view"); return; }
            try { view.SelectPlanet(target); } catch (Exception e) { _log.LogWarning($"  SelectPlanet: {e.Message}"); }
            try { view.SetMission(target); } catch (Exception e) { _log.LogWarning($"  SetMission: {e.Message}"); }

            var gmp = view.gmp;
            var d = gmp?.gmd;
            _log.LogWarning($"DEVPLAY planet='{guid}' panelCategory={(gmp == null ? "?" : gmp.category.ToString())} " +
                            $"gmd={(d == null ? "null" : $"title='{d.missionTitle}' specifier='{d.specifier}' guid='{d.guid}'")}");
            if (gmp == null) return;
            try { gmp.OnPlay(); _log.LogWarning("DEVPLAY: OnPlay invoked"); }
            catch (Exception e) { _log.LogWarning($"DEVPLAY OnPlay: {e.Message}"); }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:play: {e.Message}"); }
    }

    /// <summary>Force an autosave, and say whether autosave is even on.
    ///
    /// The swap test could not tell "saves do not work for a swapped mission"
    /// from "20 seconds is shorter than the autosave interval" - and those need
    /// very different responses. Calling GameSpace.AutoSave() directly removes
    /// the timing question, and GameSettings.noAutoSave removes the other
    /// obvious confound.</summary>
    private void SaveAuto()
    {
        var gs = GameSpace.instance;
        if (gs == null) { _log.LogWarning("DEVSAVE: no GameSpace - boot a mission first"); return; }
        bool off = false;
        try { off = GameSettings.noAutoSave; } catch { }
        string spec = ""; try { spec = GameSpace.specifierToApply ?? ""; } catch { }
        _log.LogWarning($"DEVSAVE mission='{spec}' noAutoSave={off} - calling AutoSave()");
        try { gs.AutoSave(); _log.LogWarning("DEVSAVE: AutoSave() returned"); }
        catch (Exception e) { _log.LogWarning($"DEVSAVE: {e.Message}"); }
    }

    /// <summary>Pan the level select so a named planet sits at the middle of the
    /// view: "span:goto story20".
    ///
    /// This is what a player does by dragging, done precisely. It exists to
    /// answer whether an off-cluster planet is REACHABLE - the map is drag-panned
    /// with a clamp, so "it has a position" and "you can get to it" are separate
    /// questions and only the second one matters.</summary>
    private void SpanGoto(string guid)
    {
        if (guid.Length == 0) { _log.LogWarning("DEVCMD span:goto: need a planet guid, e.g. story20"); return; }
        try
        {
            SpanNetworkPlanet? target = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>())
            {
                if (p == null) continue;
                string g = ""; try { g = p.planetGUID ?? ""; } catch { }
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            }
            if (target == null) { _log.LogWarning($"DEVCMD span:goto: no planet '{guid}'"); return; }

            // Move the CAMERA, not a UI container: the Farsite view is panned by
            // Span (panMouseDown / mainCamera), and SpanMissionNetwork - which
            // does carry drag clamps - belongs to a different screen and is not
            // present here.
            var cam = Camera.main;
            if (cam == null) { _log.LogWarning("DEVCMD span:goto: no main camera"); return; }

            var tw = target.transform.position;
            // The world point currently at the centre of the view: cast the
            // camera's centre ray onto the plane the planets sit on.
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
            float denom = ray.direction.y;
            if (Mathf.Abs(denom) < 0.0001f) { _log.LogWarning("DEVCMD span:goto: camera is not looking at the plane"); return; }
            float t = (tw.y - ray.origin.y) / denom;
            var centre = ray.origin + ray.direction * t;

            var before = cam.transform.position;
            var delta = new Vector3(tw.x - centre.x, 0f, tw.z - centre.z);
            cam.transform.position = before + delta;
            _log.LogWarning($"DEVCMD span:goto {guid}: centre was ({centre.x:0.0},{centre.z:0.0}), " +
                            $"planet at ({tw.x:0.0},{tw.z:0.0}); camera moved by ({delta.x:0.0},{delta.z:0.0})");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD span:goto: {e.Message}"); }
    }

    /// <summary>Every planet on the level select, with its unlock state.
    ///
    /// The question this answers: a mission that exists and boots fine can still
    /// be absent or locked on the map, and no other view shows that. Reports the
    /// planet's own title rather than any list we maintain, so a title we got
    /// wrong shows up as a mismatch rather than agreeing with itself.</summary>
    private void PlanetsDump()
    {
        try
        {
            var planets = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanet>();
            if (planets == null || planets.Length == 0)
            {
                _log.LogWarning("DEVCMD planets:dump: no planets - open the level select first (story:open)");
                return;
            }
            _log.LogWarning($"DEVPLANETS count={planets.Length}");

            // Where the connecting lines actually live. SpanNetworkPlanet.lines
            // reads 0 on every planet even though lines are plainly drawn, so
            // enumerate the components themselves and report their parentage.
            try
            {
                // Where the lines really are: NOT SpanNetworkPlanet.lines, which
                // reads empty on every planet. Each line is a child of a
                // planet's lineContainer, drawn in LOCAL space from the origin
                // to the neighbour's offset. Counting them is also how a MISSING
                // link shows up - vanilla has 19 lines for 20 connections,
                // because Founders -> story20 is never drawn.
                var ls = UnityEngine.Object.FindObjectsOfType<SpanNetworkPlanetLine>();
                _log.LogWarning($"DEVLINES count={(ls == null ? 0 : ls.Length)}");
                if (ls != null)
                    foreach (var l in ls)
                    {
                        if (l == null) continue;
                        string parent = "";
                        try { parent = l.transform.parent?.name ?? ""; } catch { }
                        string gp = "";
                        try { gp = l.transform.parent?.parent?.name ?? ""; } catch { }
                        var lr = l.lineRenderer;
                        if (lr == null) { _log.LogWarning($"DEVLINE parent={gp}/{parent} noRenderer"); continue; }
                        var a = lr.GetPosition(0);
                        var b = lr.GetPosition(lr.positionCount - 1);
                        _log.LogWarning($"DEVLINE parent={gp}/{parent} n={lr.positionCount} " +
                                        $"space={(lr.useWorldSpace ? "world" : "local")} " +
                                        $"({a.x:0.0},{a.y:0.0},{a.z:0.0})->({b.x:0.0},{b.y:0.0},{b.z:0.0})");
                    }
            }
            catch (Exception e) { _log.LogWarning($"DEVLINES: {e.Message}"); }

            // The level select is drag-panned but CLAMPED (SpanMissionNetwork
            // min/maxDrag). Whether a planet can be reached at all depends on
            // those limits, not on its position alone, so report both.
            try
            {
                // FindObjectsOfTypeAll: FindObjectsOfType returned nothing here,
                // and an inactive or not-yet-enabled container still carries the
                // clamp values we need.
                foreach (var n in Resources.FindObjectsOfTypeAll<SpanMissionNetwork>())
                {
                    if (n == null) continue;
                    var lp = n.transform.localPosition;
                    _log.LogWarning($"DEVSPANNET '{n.gameObject.name}' localPos=({lp.x:0.0},{lp.y:0.0},{lp.z:0.0}) " +
                                    $"dragX={n.minDragX:0.0}..{n.maxDragX:0.0} dragY={n.minDragY:0.0}..{n.maxDragY:0.0}");
                }
            }
            catch (Exception e) { _log.LogWarning($"DEVSPANNET: {e.Message}"); }
            foreach (var p in planets)
            {
                if (p == null) continue;
                string title = "";
                try { if (p.title != null) title = (p.title.text ?? "").Trim(); } catch { }
                if (title.Length == 0) { try { title = (p.map_title ?? "").Trim(); } catch { } }
                string guid = ""; try { guid = p.planetGUID ?? ""; } catch { }
                bool unlocked = false; try { unlocked = p.unlocked; } catch { }
                bool forced = false; try { forced = p.forceUnlocked; } catch { }
                bool set = false; try { set = p.unlockedSet; } catch { }
                int links = 0; string linkTo = "";
                try
                {
                    var cg = p.connectedPlanetGUIDS;
                    if (cg != null)
                    {
                        links = cg.Length;
                        var parts = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < cg.Length; i++) parts.Add(cg[i] ?? "");
                        linkTo = string.Join(",", parts);
                    }
                }
                catch { }
                bool active = false; try { active = p.gameObject.activeInHierarchy; } catch { }
                // World AND screen position: "active and unlocked" does not mean
                // visible, and a planet parked outside the framed view looks
                // exactly like a missing one.
                var wp = Vector3.zero; try { wp = p.transform.position; } catch { }
                var lpos = Vector3.zero; try { lpos = p.transform.localPosition; } catch { }
                var sp = Vector3.zero; bool onScreen = false;
                try
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        sp = cam.WorldToScreenPoint(wp);
                        onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                    }
                }
                catch { }
                _log.LogWarning($"DEVPLANET '{title}' guid={guid} unlocked={unlocked} forceUnlocked={forced} " +
                                $"unlockedSet={set} links={links}->[{linkTo}] active={active} " +
                                $"world=({wp.x:0},{wp.y:0},{wp.z:0}) local=({lpos.x:0.0},{lpos.y:0.0},{lpos.z:0.0}) " +
                                $"screen=({sp.x:0},{sp.y:0}) onScreen={onScreen}");
            }
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD planets:dump: {e.Message}"); }
    }

    /// <summary>In-engine capture. Desktop screenshots only grab a crop when the
    /// game renders above the reported desktop resolution, and they miss the
    /// overlay layer entirely.</summary>
    private void Shot(string path)
    {
        if (path.Length == 0) { _log.LogWarning("DEVCMD shot: need a path"); return; }
        try
        {
            ScreenCapture.CaptureScreenshot(path);
            _log.LogInfo($"DEVCMD shot: {path}");
        }
        catch (Exception e) { _log.LogWarning($"DEVCMD shot: {e.Message}"); }
    }

    /// <summary>"set:instantbuild=off" - the hotkeys with no keyboard.</summary>
    private void Set(string arg)
    {
        var kv = arg.Split('=');
        if (kv.Length != 2) { _log.LogWarning("DEVCMD set: expected <cheat>=on|off"); return; }
        bool on = kv[1].Trim().ToLowerInvariant() != "off";
        switch (kv[0].Trim().ToLowerInvariant())
        {
            case "instantbuild": DevConfig.InstantBuild.Value = on; break;
            case "allbuildings": DevConfig.AllBuildings.Value = on; break;
            case "infiniteresources": DevConfig.InfiniteResources.Value = on; break;
            case "indestructible": DevConfig.Indestructible.Value = on; break;
            case "freezecreeper": DevConfig.FreezeCreeper.Value = on; break;
            case "overlay": DevConfig.ShowOverlay.Value = on; break;
            default: _log.LogWarning($"DEVCMD set: unknown cheat '{kv[0]}'"); return;
        }
        _log.LogInfo($"DEVCMD set: {kv[0]}={(on ? "on" : "off")}");
    }
}
