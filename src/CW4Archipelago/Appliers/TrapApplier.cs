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

        if (!TrapRules.IsTrap(item))
            return;

        ModCore.Log.LogInfo($"TRAP: {item}");
        Fire(item);
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
