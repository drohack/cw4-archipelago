namespace CW4Archipelago.Core;

/// <summary>
/// Kinds of Archipelago server log messages we distinguish for the in-game
/// message box's relevance filter. ApClient maps each concrete
/// MultiClient.Net LogMessage subtype to one of these, which keeps the AP
/// (and Unity) types out of this pure, unit-tested project.
/// </summary>
public enum ApMessageKind
{
    ItemSend,
    Hint,
    ItemCheat,
    Chat,
    ServerChat,
    CommandResult,
    AdminCommandResult,
    Collect,
    Release,
    Goal,
    Join,
    Leave,
    TagsChanged,
    Countdown,
    Tutorial,
    Other,
}

/// <summary>
/// Decides whether a server message concerns the local player, for the box's
/// default relevant-only view. Player-specific events (item swaps, hints,
/// collects, joins, ...) show only when they involve the active player; chat,
/// server notices, command results and unknown kinds always show (so nothing
/// is silently hidden). The show-all toggle in the box bypasses this entirely.
/// </summary>
public static class MessageRelevance
{
    public static bool IsRelevant(ApMessageKind kind, bool relatedToActivePlayer) => kind switch
    {
        // Communication and server/command output are always shown.
        ApMessageKind.Chat => true,
        ApMessageKind.ServerChat => true,
        ApMessageKind.CommandResult => true,
        ApMessageKind.AdminCommandResult => true,
        ApMessageKind.Countdown => true,
        ApMessageKind.Tutorial => true,
        ApMessageKind.Other => true,

        // Player-specific events: only when they involve the active player.
        ApMessageKind.ItemSend => relatedToActivePlayer,
        ApMessageKind.Hint => relatedToActivePlayer,
        ApMessageKind.ItemCheat => relatedToActivePlayer,
        ApMessageKind.Collect => relatedToActivePlayer,
        ApMessageKind.Release => relatedToActivePlayer,
        ApMessageKind.Goal => relatedToActivePlayer,
        ApMessageKind.Join => relatedToActivePlayer,
        ApMessageKind.Leave => relatedToActivePlayer,
        ApMessageKind.TagsChanged => relatedToActivePlayer,

        _ => true,
    };
}
