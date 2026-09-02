using System;
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

        int n = 0, ghosts = 0; float added = 0f;
        foreach (var u in gs.units)
        {
            if (u == null) continue;
            try
            {
                if (!GameUtil.IsPlayerUnit(u)) continue;
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
        ModCore.Log.LogInfo($"BOON resupply: filled {n} weapon(s), {added:0.##} ammo added"
                    + (ghosts > 0 ? $" (skipped {ghosts} still under construction)" : ""));
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
}
