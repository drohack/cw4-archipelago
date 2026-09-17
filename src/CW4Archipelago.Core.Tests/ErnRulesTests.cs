using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

public class ErnRulesTests
{
    [Fact]
    public void ErnCount_MultipliesByHint()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Progressive ERN", "Progressive ERN", "Cannon" });
        Assert.Equal(2, ErnRules.ErnCount(s));
        s.Hints = SlotData.FromJson("{\"ern_per_item\":2}");
        Assert.Equal(4, ErnRules.ErnCount(s));
    }
}
