using System;
using CW4Archipelago.Core;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Makes the finale genuinely unbeatable until enough missions have been
/// completed, and says so on screen.
///
/// Why not simply hold the goal back when the player wins? Because finishing a
/// mission and being told it did not count is a miserable way to learn a rule.
/// The mission is instead visibly unwinnable: its final objective cannot be
/// completed, the objective panel says why, and the planet reads as locked.
///
/// Why not lock the whole mission instead? Because Founders holds 24 checks.
/// Gating entry would put all of them behind the count, which is poor
/// multiworld design - those checks hold OTHER players' items, and they should
/// be collectable as soon as the player can fight their way to them. Only
/// WINNING is gated.
///
/// HOW the lock works: Founders' custom objective ("End the Beginning") needs
/// the four obelisk reactors nullified and then the neutron reactor. Removing
/// the neutron reactor from what a nullifier may target stops that last step,
/// and nothing else.
///
/// Measured on the real mission (docs/research-findings.md):
///   - `impervious` does NOT prevent nullification. Those reactors ship
///     impervious already and are still nullifiable - nullifying is not damage.
///   - Writing `CAN_NULLIFY = false` on the unit DOES work, but the sim resets
///     it every tick, so it needs rewriting every frame, and worse: the unit
///     leaves GameSpace.nullifiableUnits and NEVER comes back within the
///     mission. That is a soft-lock waiting to happen, and it also makes the
///     per-instance nullify counter register a phantom nullification.
///
/// So the lock is a Harmony filter on the targeting call instead. It mutates no
/// game state, costs nothing per frame, lifts the instant the gate opens, and
/// leaves the nullify counter alone.
/// </summary>
public sealed class FinaleLock
{
    /// <summary>The neutron reactor's data name. CMOD units report a GUID rather
    /// than a name, so this prefix IS its identity - read off the live mission
    /// with CW4DevTools "null:list". If a game update changes it, the lock
    /// degrades to doing nothing rather than breaking the mission.</summary>
    public const string NeutronReactorGuid = "abe9d7ea";

    /// <summary>Read by the Harmony patch. Static because a patch has no
    /// instance, and cheap to check.</summary>
    internal static bool Active;

    private IntPtr _lastGameSpace = IntPtr.Zero;
    private int _mission;
    /// <summary>Set by Invalidate; the only per-frame cost besides the mission
    /// pointer compare.</summary>
    private volatile bool _dirty = true;

    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null || GameSpace.editMode)
        {
            Active = false;
            Message = "";
            _lastGameSpace = IntPtr.Zero;
            return;
        }

        // Resolve the mission once per mission, not once per frame: it means
        // parsing a string, and it cannot change without the GameSpace changing.
        if (gs.Pointer != _lastGameSpace)
        {
            _lastGameSpace = gs.Pointer;
            _dirty = true;   // new mission: recompute
            _mission = 0;
            try { MissionRules.TryParseSpecifier(gs.specifier, out _mission); } catch { }
        }

        if (_mission != MissionRules.FinalMission)
        {
            Active = false;
            Message = "";
            return;
        }

        // Recompute only when Archipelago state changes - the only thing that can
        // move this gate. MissionsBeaten builds a location name per mission to
        // test it, so recomputing every frame meant nineteen string allocations
        // a frame for an answer that changes a few times an hour.
        if (!_dirty)
            return;
        _dirty = false;

        var state = ModCore.Client.State;
        bool open = MissionRules.FinaleCounts(state);
        Active = !open;
        Message = open ? "" : BuildMessage(state);
    }

    /// <summary>Recompute the gate on the next tick. Safe from any thread: sets
    /// a flag only.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>The line shown on the finale's custom objective while locked.
    /// Empty when there is nothing to say.</summary>
    internal static string Message = "";

    private static string BuildMessage(SlotState state)
    {
        int need = state.Hints.MissionsForFinale - MissionRules.MissionsBeaten(state);
        if (need < 1)
            return "";
        return need == 1
            ? "UNBEATABLE - BEAT 1 MORE LEVEL"
            : $"UNBEATABLE - BEAT {need} MORE LEVELS";
    }

}

/// <summary>
/// Hides the neutron reactor from nullifier targeting while the finale is
/// locked. A postfix filter rather than a state write: see FinaleLock.
/// </summary>
[HarmonyPatch(typeof(Nullifier), nameof(Nullifier.GetNullifierTargets))]
public static class NullifierTargetPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref List<UnitManager> __result)
    {
        if (!FinaleLock.Active || __result == null)
            return;
        try
        {
            // Collect first, then remove: mutating the list while iterating it
            // through the interop wrapper is not safe.
            var drop = new System.Collections.Generic.List<UnitManager>();
            foreach (var u in __result)
            {
                if (u == null) continue;
                string name;
                try { name = (u.GetDataName() ?? "").ToLowerInvariant(); }
                catch { continue; }
                if (name.StartsWith(FinaleLock.NeutronReactorGuid))
                    drop.Add(u);
            }
            foreach (var u in drop)
                __result.Remove(u);
        }
        catch { }
    }
}

/// <summary>
/// Puts the reason on the finale's custom objective row.
///
/// A POSTFIX on the row's own LateUpdate, not a write to the objective data.
/// Writing MissionObjectiveData.customName does stick in the data and never
/// reaches the screen: the row rebuilds its label every frame from a localized
/// name. Hooking that rebuild is both cheaper than fighting it and free of any
/// change to game state, so nothing can leak into a save.
/// </summary>
[HarmonyPatch(typeof(ObjectiveRow), nameof(ObjectiveRow.LateUpdate))]
public static class ObjectiveRowPatch
{
    /// <summary>Slot 5 is Custom - the objective the lock actually blocks.</summary>
    private const int CustomSlot = 5;

    /// <summary>The tracker convention's "not reachable" red.</summary>
    private static readonly Color Blocked = new(0.90f, 0.32f, 0.30f, 1f);

    [HarmonyPostfix]
    public static void Postfix(ObjectiveRow __instance)
    {
        if (!FinaleLock.Active || FinaleLock.Message.Length == 0)
            return;
        try
        {
            if (__instance.objective != CustomSlot)
                return;
            var label = __instance.text;
            if (label != null && label.text != FinaleLock.Message)
            {
                label.text = FinaleLock.Message;
                // The row is sized for names like "End the Beginning" and clips
                // anything longer - the first attempt showed "UNBEATABLE - BEAT
                // 1 ..." with the rest cut off. Let it wrap and shrink to fit
                // rather than silently losing the number, which is the one part
                // the player needs.
                try
                {
                    label.enableWordWrapping = true;
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 8f;
                    label.overflowMode = TMPro.TextOverflowModes.Overflow;
                }
                catch { }
            }

            // Red icon and red text, so the row reads as blocked at a glance
            // rather than as just another unfinished objective. Red is the
            // Archipelago tracker's "not reachable" colour, which is exactly
            // what this objective is.
            if (label != null && label.color != Blocked)
                label.color = Blocked;
            var icon = __instance.image;
            if (icon != null && icon.color != Blocked)
                icon.color = Blocked;
        }
        catch { }
    }
}
