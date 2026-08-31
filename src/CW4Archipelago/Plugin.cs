using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace CW4Archipelago;

[BepInPlugin("com.droha.cw4archipelago", "CW4 Archipelago", "0.1.0")]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("CW4 Archipelago v0.1.0 loading");

        var mcnet = typeof(Archipelago.MultiClient.Net.ArchipelagoSessionFactory).Assembly.GetName();
        Log.LogInfo($"Archipelago.MultiClient.Net {mcnet.Version} available");

        var config = new ModConfig(Config);
        ModCore.Init(Log, config);

        // Each patch is applied independently and guarded: if a future game
        // update changes one patched method, that feature degrades but the
        // rest of the mod (connection, items, checks) still loads.
        TryPatch("MissionGate", typeof(Appliers.MissionGate));
        TryPatch("FakeComplete", typeof(Appliers.FakeCompletePatch));
        TryPatch("PlanetClick", typeof(Appliers.PlanetClickPatch));
        TryPatch("InputBlock", typeof(Appliers.InputBlock));
        TryPatch("nullifier targets", typeof(Appliers.NullifierTargetPatch));
        TryPatch("objective row", typeof(Appliers.ObjectiveRowPatch));
        // Replace polling with the game's own lifecycle events. A failure
        // here is logged and the mod continues - but the map would then
        // never colour, so the log is worth reading after a game update.
        TryPatch("map opened", typeof(Appliers.SpanStartPatch));
        TryPatch("planet refresh", typeof(Appliers.PlanetRefreshPatch));
        TryPatch("totem complete", typeof(Appliers.TotemCompletePatch));
        TryPatch("cache destroyed", typeof(Appliers.CacheDestroyedPatch));
        TryPatch("planet unlocked set", typeof(Appliers.PlanetUnlockedSetPatch));

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<ModBehaviour>();
            AddComponent<ModBehaviour>();
        }
        catch (Exception e)
        {
            Log.LogError($"FATAL: could not install the mod behaviour: {e.Message}");
            return;
        }

        Log.LogInfo("CW4 Archipelago loaded");
    }

    private void TryPatch(string name, Type patchType)
    {
        try
        {
            Harmony.CreateAndPatchAll(patchType, "com.droha.cw4archipelago");
        }
        catch (Exception e)
        {
            Log.LogError($"Harmony patch '{name}' failed (feature disabled, mod continues): {e.Message}");
        }
    }
}
