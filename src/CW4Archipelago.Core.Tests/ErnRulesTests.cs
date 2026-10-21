using System.Collections.Generic;
using CW4Archipelago.Core;
using Xunit;

public class ErnRulesTests
{
    [Fact]
    public void ErnCount_IsOnePerItem()
    {
        var s = new SlotState();
        s.ApplyReceivedItems(new[] { "Progressive ERN", "Progressive ERN", "Cannon" });
        Assert.Equal(2, ErnRules.ErnCount(s));
        // Every seed ever generated sent ern_per_item = 1, and the key is no
        // longer read: an old seed that still sends it changes nothing.
        s.Hints = SlotData.FromJson("{\"ern_per_item\":2}");
        Assert.Equal(2, ErnRules.ErnCount(s));
    }
}
