using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace CW4Archipelago;

[BepInPlugin("com.droha.cw4archipelago", "CW4 Archipelago", "0.2.0")]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("CW4 Archipelago v0.2.0 loading");

        var mcnet = typeof(Archipelago.MultiClient.Net.ArchipelagoSessionFactory).Assembly.GetName();
        Log.LogInfo($"Archipelago.MultiClient.Net {mcnet.Version} available");

        var config = new ModConfig(Config);
        ModCore.Init(Log, config);

        Harmony.CreateAndPatchAll(typeof(Appliers.MissionGate), "com.droha.cw4archipelago");
        Harmony.CreateAndPatchAll(typeof(Appliers.FakeCompletePatch), "com.droha.cw4archipelago");
        Harmony.CreateAndPatchAll(typeof(Appliers.PlanetClickPatch), "com.droha.cw4archipelago");

        ClassInjector.RegisterTypeInIl2Cpp<ModBehaviour>();
        AddComponent<ModBehaviour>();

        Log.LogInfo("CW4 Archipelago loaded");
    }
}
