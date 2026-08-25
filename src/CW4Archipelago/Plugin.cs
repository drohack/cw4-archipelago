using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace CW4Archipelago;

[BepInPlugin("com.droha.cw4archipelago", "CW4 Archipelago", "0.1.0")]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("CW4 Archipelago v0.1.0 loading");

        // Touch Archipelago.MultiClient.Net so a broken dependency deploy
        // fails loudly at startup instead of at first connect.
        var mcnet = typeof(Archipelago.MultiClient.Net.ArchipelagoSessionFactory).Assembly.GetName();
        Log.LogInfo($"Archipelago.MultiClient.Net {mcnet.Version} available");

        Log.LogInfo("CW4 Archipelago loaded (skeleton - no features ported yet)");
    }
}
