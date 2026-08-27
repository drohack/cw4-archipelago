using CW4Archipelago.Core;
using Xunit;

public class MessageRelevanceTests
{
    [Theory]
    // Always shown regardless of player relation.
    [InlineData(ApMessageKind.Chat, false, true)]
    [InlineData(ApMessageKind.ServerChat, false, true)]
    [InlineData(ApMessageKind.CommandResult, false, true)]
    [InlineData(ApMessageKind.AdminCommandResult, false, true)]
    [InlineData(ApMessageKind.Countdown, false, true)]
    [InlineData(ApMessageKind.Tutorial, false, true)]
    [InlineData(ApMessageKind.Other, false, true)]
    // Player-specific: shown only when related to the active player.
    [InlineData(ApMessageKind.ItemSend, true, true)]
    [InlineData(ApMessageKind.ItemSend, false, false)]
    [InlineData(ApMessageKind.Hint, true, true)]
    [InlineData(ApMessageKind.Hint, false, false)]
    [InlineData(ApMessageKind.ItemCheat, false, false)]
    [InlineData(ApMessageKind.Collect, true, true)]
    [InlineData(ApMessageKind.Collect, false, false)]
    [InlineData(ApMessageKind.Release, false, false)]
    [InlineData(ApMessageKind.Goal, false, false)]
    [InlineData(ApMessageKind.Goal, true, true)]
    [InlineData(ApMessageKind.Join, false, false)]
    [InlineData(ApMessageKind.Leave, false, false)]
    [InlineData(ApMessageKind.TagsChanged, false, false)]
    public void IsRelevant_Matches(ApMessageKind kind, bool related, bool expected)
    {
        Assert.Equal(expected, MessageRelevance.IsRelevant(kind, related));
    }
}
