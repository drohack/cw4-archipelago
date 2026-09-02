using CW4Archipelago.Core;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Fires a trap when its item arrives.
///
/// Two rules shape this:
///
/// FIRE ONCE. Reconnecting re-delivers the whole received-items list, so firing
/// on the receive event alone would replay every trap the player has ever been
/// sent. Progress is a persisted high-water mark over the received list instead,
/// which also survives quitting and reloading.
///
/// FIRE IN A MISSION. A trap received at the menu has nothing to act on - a
/// spore strike with no map is simply lost. Those wait, and go off on the next
/// mission, one per tick so a backlog does not arrive as a single unsurvivable
/// wall.
/// </summary>
public sealed class TrapApplier
{
    public void Tick()
    {
        var gs = GameSpace.instance;
        if (gs == null || GameSpace.editMode)
            return;                      // no mission: hold the queue
        if (gs.commandBase == null)
            return;                      // mission still materialising

        var state = ModCore.Client.State;
        if (state.TrapsApplied >= state.ReceivedItems.Count)
            return;

        // One per tick. A backlog should sting repeatedly, not all at once.
        var item = state.ReceivedItems[state.TrapsApplied];
        state.TrapsApplied++;

        // Boons share this counter deliberately. Both traps and boons must fire
        // EXACTLY once and must not replay when a reconnect re-delivers the
        // whole received list, and that is the only hard part - so there is one
        // mechanism for it rather than two that can drift.
        //
        // Checked BEFORE the trap test, because a boon is not a trap and the
        // trap test returns. That return is what made every boon dead code:
        // FireBoon existed with no caller for a whole session while 63 of 236
        // pool items quietly did nothing.
        if (BoonRules.IsBoon(item))
        {
            ModCore.Log.LogInfo($"BOON: {item}");
            FireBoon(item);
            return;
        }

        if (!TrapRules.IsTrap(item))
            return;

        ModCore.Log.LogInfo($"TRAP: {item}");
        Fire(item);
    }

    private static void FireBoon(string item)
    {
        // Surges are prefix-matched rather than listed, because there is one
        // per upgrade and the upgrade order lives in ErnUpgradeRules.
        int surge = BoonRules.SurgeIndex(item);
        if (surge >= 0) { ErnUpgrades.StartSurge(surge); return; }

        switch (item)
        {
            case BoonRules.AmmoResupply: BoonEffects.Resupply(); return;
            // 0 means "use the tuned default fraction", same convention the
            // traps use for their amounts.
            case BoonRules.EnergyCache: BoonEffects.EnergyCache(0f); return;
            case BoonRules.FieldShield: BoonEffects.Shield(); return;
            // "all" grants every ware the factory has a channel for; see
            // BoonRules.ResourceCache for why this is one item, not three.
            case BoonRules.ResourceCache: BoonEffects.ResourceCache("all", 0, false); return;
        }
    }

    private static void Fire(string item)
    {
        // 0 means "use the tuned default", which is what the feasibility spike
        // calibrated against the game's own values.
        switch (item)
        {
            case TrapRules.SporeStrike: TrapEffects.SporeStrikeBuilding(0, 0); return;
            case TrapRules.SporeScatter: TrapEffects.SporeStrikeScatter(0, 0); return;
            case TrapRules.CreeperSurge: TrapEffects.Creep(0, 0); return;
            case TrapRules.EnergyDrain: TrapEffects.Energy(0f); return;
            case TrapRules.EmitterOverdrive: TrapEffects.Emit(0f, 0f); return;
            case TrapRules.UnitStun: TrapEffects.Stun(0f); return;
            case TrapRules.AmmoDrain: TrapEffects.Drain(); return;
        }
    }
}
