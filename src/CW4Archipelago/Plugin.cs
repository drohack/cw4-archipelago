using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace CW4Archipelago;

[BepInPlugin("com.droha.cw4archipelago", "CW4 Archipelago", Plugin.Version)]
public class Plugin : BasePlugin
{
    /// <summary>The one place this plugin's version is written.
    ///
    /// It used to appear here twice as a literal AND in the csproj AND in
    /// apworld/cw4/archipelago.json, and after v0.1.1 shipped, main kept
    /// calling itself 0.1.1 through twelve commits - including an item rename.
    /// A build and a seed could then both say "0.1.1" and disagree about what
    /// items are called. package-release.ps1 now refuses to build unless this,
    /// the csproj Version and world_version all match.</summary>
    public const string Version = "0.1.3";

    public override void Load()
    {
        Log.LogInfo($"CW4 Archipelago v{Version} loading");

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
        TryPatch("ERN efficiency", typeof(Appliers.ErnEffPatch));
        // The static one is what the SIM reads; the instance one above only
        // feeds the port's UI. Registering just one of the pair is why the
        // ceiling raised the displayed number and moved nothing in the game.
        TryPatch("ERN efficiency (static)", typeof(Appliers.ErnEfficiencyPatch));
        // Replace polling with the game's own lifecycle events. A failure
        // here is logged and the mod continues - but the map would then
        // never colour, so the log is worth reading after a game update.
        TryPatch("map opened", typeof(Appliers.SpanStartPatch));
        TryPatch("planet refresh", typeof(Appliers.PlanetRefreshPatch));
        TryPatch("totem complete", typeof(Appliers.TotemCompletePatch));
        TryPatch("cache destroyed", typeof(Appliers.CacheDestroyedPatch));
        TryPatch("planet unlocked set", typeof(Appliers.PlanetUnlockedSetPatch));
        // Refuse a mission's grant of a locked unit at source, rather than
        // rebuilding the build strip to remove a button that should never have
        // been created.
        TryPatch("unit grant", typeof(Appliers.UnitGrantPatch));

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
