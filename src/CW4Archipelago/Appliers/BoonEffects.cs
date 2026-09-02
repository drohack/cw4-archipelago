using System;
using System.Collections.Generic;
using CW4Archipelago.Core;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// The helpful counterparts to TrapEffects: filler items that do something for
/// the mission you are in right now, rather than a permanent stat.
///
/// These exist because the filler pool was 95 of two permanent upgrades - about
/// 40 percent of every seed spent on "your numbers are slightly bigger", with
/// nothing that lands as a moment. Each effect here is deliberately the mirror of
/// a trap that already works, so it reuses a proven path into the game rather
/// than a new guess:
///
///     Ammo Drain      -> Resupply
///     Energy Drain    -> EnergyCache
///
/// A Creeper Purge was built here and REMOVED (designer, 2026-09-01). The effect
/// worked; the targeting had no answer. An Archipelago item fires when the server
/// sends it, with no chance for the player to aim, and every automatic choice was
/// wrong: the rift lab is rarely what is threatened, creeper is rarely touching a
/// unit because weapons engage at range, and following the player's fire is a lot
/// of machinery for one filler slot. Scrapped rather than shipped unaimed.
///
/// Pure side effects on live game objects, so there are no unit tests here; the
/// numbers are measured in game with the "boon:" debug commands.
/// </summary>
public static class BoonEffects
{
    /// <summary>Default energy granted by one Energy Cache, as a fraction of the
    /// rift lab's CURRENT ceiling. A fraction rather than a flat number so it
    /// stays meaningful after storage upgrades raise the cap.</summary>
    public static float CacheFraction = 0.5f;

    /// <summary>Is this the rift lab, whose "ammo" is the ENERGY STORE rather
    /// than a weapon's magazine?
    ///
    /// Shared by Resupply and by the Drain trap so the mirror pair cannot
    /// disagree - they did, and in opposite directions: Resupply handed out a
    /// free full energy refill and Drain emptied the store to zero, neither
    /// documented and neither intended.
    ///
    /// TryCast is the established test in this codebase (UpgradeProbe uses it
    /// for ERNInterface, ERN and Reactor) and works even before
    /// GameSpace.commandBase is populated - a campaign mission starts with the
    /// lab in the player's hand, so that property is null until it is placed.</summary>
    public static bool IsEnergyStore(UnitManager u)
    {
        try { if (u.TryCast<CommandBase>() != null) return true; }
        catch { }
        try
        {
            var cb = GameSpace.instance?.commandBase;
            if (cb != null && cb.Pointer == u.Pointer) return true;
        }
        catch { }
        return false;
    }

    private static GameSpace? Live(string what)
    {
        var gs = GameSpace.instance;
        if (gs == null) ModCore.Log.LogWarning($"BOON {what}: no GameSpace");
        return gs;
    }

    /// <summary>Fill every player weapon's ammo to full, once.
    ///
    /// The exact inverse of TrapEffects.Drain, which empties them. Deliberately a
    /// one-shot: it helps a fight that is happening now and leaves no permanent
    /// change behind, which is the whole point of a temporary boon.</summary>
    public static void Resupply()
    {
        var gs = Live("resupply");
        if (gs == null) return;

        int n = 0, ghosts = 0, stores = 0; float added = 0f;
        // AllUnits, not gs.units: the airship is only in gs.flyingUnits, so
        // this effect silently skipped it until now.
        foreach (var u in AllUnits(gs))
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                // NOT THE RIFT LAB. Its "ammo" IS the energy store, so filling
                // it made this item a strictly-better Energy Cache by accident -
                // an undocumented free energy refill on top of the ammo.
                //
                // GameUtil.ExtraPlayerKeys contains "CommandBase", so
                // IsPlayerUnit accepts it, and a placed lab is not isBuilding
                // either - it passed every other filter.
                if (IsEnergyStore(u)) { stores++; continue; }
                // Skip anything still under construction. A spawned or
                // part-built unit is a real UnitManager with a real MAX_AMMO but
                // is not a weapon yet, and filling it does nothing except make
                // the log look like the effect worked - which is exactly what it
                // did the first time this was demonstrated: "filled 5 weapons,
                // 54 ammo added" against five blue ghosts.
                if (u.isBuilding) { ghosts++; continue; }
                float max = u.MAX_AMMO;
                if (max <= 0f) continue;          // not an ammo user
                float a = u.ammo;
                if (a >= max) continue;           // already full
                u.ammo = max;
                added += max - a;
                n++;
            }
            catch { }
        }
        // The skipped-store count is LOGGED because it is the only
        // unambiguous evidence the rift-lab filter fired. Inferring it from the
        // store's value does not work: energy moves on its own as weapons draw
        // packets, so a plausible number proves nothing either way.
        ModCore.Log.LogInfo($"BOON resupply: filled {n} weapon(s), {added:0.##} ammo added"
                    + (ghosts > 0 ? $" (skipped {ghosts} still under construction)" : "")
                    + $" [energy stores skipped: {stores}]");
    }

    /// <summary>Drop a slug of energy into the store immediately.
    ///
    /// The store IS commandBase.ammo and the ceiling is MAX_AMMO
    /// (docs/research-findings.md, "Energy: the store is the rift lab's ammo"),
    /// so this is the same field the Energy Drain trap empties. Clamped to the
    /// ceiling: energy above MAX_AMMO is not a thing the game keeps.</summary>
    public static void EnergyCache(float fraction)
    {
        var gs = Live("energy cache");
        if (gs == null) return;
        var cb = gs.commandBase;
        if (cb == null) { ModCore.Log.LogWarning("BOON energy cache: no rift lab"); return; }

        if (fraction <= 0f) fraction = CacheFraction;
        float before = cb.ammo;
        float grant = cb.MAX_AMMO * fraction;
        float after = Mathf.Min(before + grant, cb.MAX_AMMO);
        cb.ammo = after;
        ModCore.Log.LogInfo(
            $"BOON energy cache: +{fraction:P0} of {cb.MAX_AMMO:0.#} cap = {grant:0.#} granted; " +
            $"store {before:0.#} -> {after:0.#}" +
            (after < before + grant ? " (clamped at the ceiling)" : ""));
    }

    // --- Resource caches --------------------------------------------------
    //
    // WHERE MINED WARES ACTUALLY LIVE, which took three attempts to find:
    //
    //   UnitManager.waresHeld        a factory's AMMO_WARES INPUT slots, which
    //                                is what GetWareHeld reads - and why summing
    //                                it returned a flat 0 while a factory was
    //                                visibly filling
    //   Factory.producedWareCounts   the mined OUTPUT stock, reached through
    //                                GetProducedWares / SetProducedWares
    //
    // Both are real storage; they are just different storage. The earlier claim
    // that "GetWareHeld does not see the factory's contents" was too strong.

    /// <summary>Default stock one cache grants, per factory.</summary>
    public static int CacheWareAmount = 10;

    /// <summary>The ware ids, read from the game rather than hard-coded. They
    /// are assigned PER MISSION, so a literal would be wrong on some maps.
    /// Returns -1 for a ware this mission does not define.</summary>
    public static int WareId(string which)
    {
        try
        {
            switch (which)
            {
                case "bluite": return WaresManager.WARE_BLUITE;
                case "redon":  return WaresManager.WARE_REDON;
                case "liftic": return WaresManager.WARE_LIFTIC;
                case "greenar": return WaresManager.WARE_GREENAR;
            }
        }
        catch { }
        return -1;
    }

    /// <summary>Add stock of one ware to every player factory, once.
    ///
    /// WHIFFS HARMLESSLY with no factory, which the designer accepted
    /// explicitly. That is a looser rule than the traps get - a trap that does
    /// nothing feels broken, which is why Emitter Overdrive was pulled - but a
    /// boon that does nothing is only a let-down.
    ///
    /// <paramref name="useCreate"/> selects between the two write methods so a
    /// test can measure which one the sim honours instead of guessing. This
    /// repo has already been caught writing a recomputed summary
    /// (gs.energyStore) and moving nothing but the HUD.</summary>
    public static void ResourceCache(string which, int amount, bool useCreate)
    {
        var gs = Live("resource cache");
        if (gs == null) return;

        // "all" grants every ware the factory actually has a channel for, which
        // is what a single Resource Cache item should do.
        //
        // MEASURED, and the reason one item beats three: a factory only holds
        // wares the MISSION gives it a channel for. On story2 liftic had
        // channel 2 and bluite, redon and greenar had none at all - so a
        // per-ware item would whiff on most missions for a reason the player
        // cannot see or act on, which is much worse than "you have no factory".
        if (which == "all")
        {
            // ONE summary line, not one per ware. Firing the item logged four
            // separate "whiffed" lines for a single grant, which reads like
            // four failures instead of one item doing its job on the one ware
            // this mission happens to channel.
            var paid = new List<string>();
            var dry = new List<string>();
            foreach (var w in new[] { "bluite", "redon", "greenar", "liftic" })
            {
                int got = ResourceCacheOne(gs, w, amount, useCreate, quiet: true);
                if (got > 0) paid.Add($"{w} +{got}");
                else dry.Add(w);
            }
            if (paid.Count > 0)
                ModCore.Log.LogInfo(
                    $"BOON resource cache: {string.Join(", ", paid)}"
                    + (dry.Count > 0 ? $" (no channel this mission: {string.Join("/", dry)})" : ""));
            else
                ModCore.Log.LogInfo(
                    "BOON resource cache: whiffed - no factory, or none of "
                    + $"{string.Join("/", dry)} is channelled on this mission (allowed)");
            return;
        }
        ResourceCacheOne(gs, which, amount, useCreate, quiet: false);
    }

    /// <summary>One ware. Returns how much was actually granted, so the "all"
    /// caller can summarise instead of every ware logging for itself.</summary>
    private static int ResourceCacheOne(GameSpace gs, string which, int amount,
                                        bool useCreate, bool quiet)
    {

        int ware = WareId(which);
        if (ware < 0)
        {
            if (!quiet)
                ModCore.Log.LogWarning(
                    $"BOON resource cache: '{which}' is not a ware this mission defines");
            return 0;
        }
        if (amount <= 0) amount = CacheWareAmount;

        int cap = int.MaxValue;
        try { cap = Factory.MAX_WARES; } catch { }

        int factories = 0, granted = 0;
        foreach (var u in AllUnits(gs))
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
                var f = u.TryCast<Factory>();
                if (f == null) continue;
                factories++;

                // A factory only holds wares it has a CHANNEL for, and channels
                // are per mission. Without this the effect logged a silent
                // "0 -> 0" that reads exactly like a broken write - which is
                // how it looked for bluite, redon and greenar on story2 while
                // liftic worked fine on the same factory.
                int channel = -1;
                try { channel = f.GetChannelForWare(ware); } catch { }
                if (channel < 0)
                {
                    if (!quiet)
                        ModCore.Log.LogInfo(
                            $"BOON resource cache: {which} on factory {factories}: " +
                            "no channel for it on this mission (whiff, not a failure)");
                    continue;
                }

                int before = f.GetProducedWares(ware);
                int want = before + amount;
                if (want > cap) want = cap;
                if (want <= before)
                {
                    if (!quiet)
                        ModCore.Log.LogInfo(
                            $"BOON resource cache: {which} on factory {factories}: " +
                            $"already at {before} of cap {cap}, nothing to add");
                    continue;
                }

                if (useCreate) f.CreateProducedWares(ware, want - before);
                else f.SetProducedWares(ware, want);

                int after = f.GetProducedWares(ware);
                granted += after - before;
                // Read back, always. A write that does not land must be visible
                // as before == after rather than assumed to have worked - and
                // that line is logged even when quiet, because a failed write
                // is never noise.
                if (!quiet || after == before)
                    ModCore.Log.LogInfo(
                        $"BOON resource cache: {which} on factory {factories} ch{channel}: " +
                        $"{before} -> {after} (asked for {want}, cap {cap}, " +
                        $"{(useCreate ? "CreateProducedWares" : "SetProducedWares")})"
                        + (after == before ? "  <- WRITE DID NOT LAND" : ""));
            }
            catch (Exception e)
            {
                ModCore.Log.LogWarning($"BOON resource cache: factory threw {e.Message}");
            }
        }

        // Zero factories is a WHIFF, not a failure, and says so plainly - the
        // log must not read like a bug when the player simply has no factory.
        if (!quiet)
        {
            if (factories == 0)
                ModCore.Log.LogInfo(
                    $"BOON resource cache: {which} whiffed - no factory built (this is allowed)");
            else
                ModCore.Log.LogInfo(
                    $"BOON resource cache: {which} +{granted} across {factories} factory(ies)");
        }
        return granted;
    }

    // --- Field Shield: temporary invincibility ----------------------------
    //
    // Replaces an earlier "Field Repair" that set health to full. A health
    // write is not enough, because CW4 has FOUR kill paths and a clamp sees
    // only the first:
    //
    //     damage                        a health clamp catches this
    //     DESTROY_ON_UNEVEN_TERRAIN     removes the unit outright, health unread
    //     Platform.DestroyUnit override its own destroy path
    //     nullification (CAN_NULLIFY)   the sim resets it every tick
    //
    // So this copies CW4DevTools' MakeTough/ReleaseTough, which covers the
    // first three. CAN_NULLIFY is deliberately NOT touched: DevTools dropped
    // its per-frame hold because a nullified unit permanently leaves
    // GameSpace.nullifiableUnits, risking a soft-lock.

    /// <summary>How long a shield lasts, in SIM TICKS.
    ///
    /// Ticks, not wall-clock seconds: Time.time keeps running while the game is
    /// paused, so a player who pauses to think would lose the boon they were
    /// just given. About 30 seconds at the game's 30 ticks/sec.</summary>
    public const int ShieldTicks = 900;

    /// <summary>Per shielded unit, the two flags to put back.</summary>
    private static readonly Dictionary<IntPtr, (bool Impervious, bool Uneven)> Shielded = new();
    private static int _shieldExpiry = -1;
    private static IntPtr _shieldGameSpace = IntPtr.Zero;

    /// <summary>Both unit collections. FLYING UNITS ARE NOT IN gs.units - the
    /// airship lives only in gs.flyingUnits, which is why Resupply silently
    /// skipped it and DevTools had to add this.</summary>
    private static IEnumerable<UnitManager> AllUnits(GameSpace gs)
    {
        foreach (var u in gs.units) yield return u;
        var fly = gs.flyingUnits;
        if (fly != null)
            foreach (var f in fly) yield return f;
    }

    /// <summary>Make every player unit impervious for ShieldTicks.
    ///
    /// MAP CONTENT IS NEVER TOUCHED, and this is the requirement that can
    /// otherwise break a seed. DevTools refuses for a reason worth repeating:
    /// an indestructible map object can make a mission unwinnable. Only units
    /// GameUtil.IsPlayerUnit accepts are shielded.</summary>
    public static void Shield()
    {
        var gs = Live("shield");
        if (gs == null) return;

        int tick;
        try { tick = gs.tickCount; } catch { return; }

        // Re-casting while active restores FIRST, so the snapshot always holds
        // the units' original flags rather than an already-shielded unit's.
        if (Shielded.Count > 0) RestoreShield("recast");

        Shielded.Clear();
        _shieldGameSpace = gs.Pointer;
        _shieldExpiry = tick + ShieldTicks;

        int shielded = 0, skipped = 0;
        foreach (var u in AllUnits(gs))
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) { skipped++; continue; }
                var key = u.Pointer;
                if (Shielded.ContainsKey(key)) continue;
                Shielded[key] = (u.impervious, u.DESTROY_ON_UNEVEN_TERRAIN);
                if (!u.impervious) u.impervious = true;
                if (u.DESTROY_ON_UNEVEN_TERRAIN) u.DESTROY_ON_UNEVEN_TERRAIN = false;
                shielded++;
            }
            catch { }
        }

        ModCore.Log.LogInfo(
            $"BOON shield: {shielded} unit(s) impervious for {ShieldTicks} ticks " +
            $"(until tick {_shieldExpiry}); {skipped} non-player object(s) left alone");
    }

    /// <summary>Called every tick from ModCore. Expires the shield.</summary>
    public static void Tick()
    {
        if (Shielded.Count == 0) return;

        var gs = GameSpace.instance;
        if (gs == null || gs.Pointer != _shieldGameSpace)
        {
            // Mission torn down mid-shield: DROP the snapshot rather than
            // writing through stale IL2CPP pointers. The emitter burst makes
            // the same call for the same reason.
            ModCore.Log.LogInfo(
                $"BOON shield: mission changed, dropping {Shielded.Count} snapshot(s)");
            Shielded.Clear();
            _shieldExpiry = -1;
            return;
        }

        int tick;
        try { tick = gs.tickCount; } catch { return; }
        if (tick < _shieldExpiry) return;
        RestoreShield("expired");
    }

    /// <summary>Put back exactly what Shield changed.
    ///
    /// Iterates the LIVE unit list and looks each one up, rather than walking
    /// the snapshot: IL2CPP recycles pointers, so a dead unit's entry could
    /// otherwise be written onto a live one.
    ///
    /// No health is involved, so unlike DevTools' version there is nothing
    /// one-way here - the shield leaves nothing behind at all.</summary>
    private static void RestoreShield(string why)
    {
        var gs = GameSpace.instance;
        int restored = 0;
        if (gs != null)
        {
            foreach (var u in AllUnits(gs))
            {
                if (u == null) continue;
                try
                {
                    if (!Shielded.TryGetValue(u.Pointer, out var saved)) continue;
                    u.impervious = saved.Impervious;
                    u.DESTROY_ON_UNEVEN_TERRAIN = saved.Uneven;
                    restored++;
                }
                catch { }
            }
        }
        int lost = Shielded.Count - restored;
        Shielded.Clear();
        _shieldExpiry = -1;
        ModCore.Log.LogInfo(
            $"BOON shield {why}: {restored} unit(s) restored" +
            (lost > 0 ? $", {lost} no longer on the map" : ""));
    }
}
