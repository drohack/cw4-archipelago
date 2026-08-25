namespace CW4Archipelago.Core;

public static class ErnRules
{
    public const string Item = "Progressive ERN";

    /// <summary>ERNs to grant the player in every mission given the items held.</summary>
    public static int ErnCount(SlotState state) => state.Count(Item) * state.Hints.ErnPerItem;
}
