using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CW4ApDebug;

/// <summary>
/// The file-command channel and the in-loop measurement probes, as a plugin of
/// their own. Test scaffolding only - the release zip never contains this DLL,
/// so a player installation simply has no debug channel.
///
/// Installing it IS enabling it. There used to be a `DebugCommands` config flag
/// as well, which meant the shipped mod carried 2,657 lines of scaffolding that
/// a config edit could switch on. Presence of the DLL is now the whole switch;
/// the harnesses in tools/ that still write `DebugCommands = true` into the
/// .cfg are harmless, because BepInEx ignores an unknown key.
///
/// BepInDependency makes BepInEx load this after the mod, so ModCore is
/// initialised before the first tick. If the mod is absent this plugin does not
/// load at all - BepInEx reports it as a missing hard dependency.
/// </summary>
[BepInPlugin("com.droha.cw4apdebug", "CW4 Archipelago Debug", "0.1.0")]
[BepInDependency("com.droha.cw4archipelago", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<DebugBehaviour>();
            AddComponent<DebugBehaviour>();
        }
        catch (Exception e)
        {
            Log.LogError($"FATAL: could not install the debug behaviour: {e.Message}");
            return;
        }

        DebugTick.Init(Log);
        Log.LogInfo($"CW4 Archipelago Debug loaded, watching {CW4Archipelago.Appliers.DebugChannel.FilePath}");
    }
}

/// <summary>
/// Injected MonoBehaviour, kept as a thin shim with no statics - statics on
/// injected IL2CPP types have correlated with stack-overflow crashes during
/// mission load. All work lives in DebugTick.
/// </summary>
public class DebugBehaviour : MonoBehaviour
{
    public DebugBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update() => DebugTick.SafeTick();
}

/// <summary>Drives the two things that used to hang off ModCore.Tick.</summary>
public static class DebugTick
{
    private static BepInEx.Logging.ManualLogSource _log = null!;
    private static readonly CW4Archipelago.Appliers.DebugChannel Channel = new();

    public static void Init(BepInEx.Logging.ManualLogSource log) => _log = log;

    public static void SafeTick()
    {
        // Two independent try blocks: a throw in the command channel must not
        // stop the stopwatches, and vice versa. Same shape as ModCore.SafeTick.
        try { CW4Archipelago.Appliers.MeasureProbe.Tick(); }
        catch (Exception e) { _log.LogError($"measure tick failed: {e.Message}"); }

        try { Channel.Tick(); }
        catch (Exception e) { _log.LogError($"debug tick failed: {e.Message}"); }
    }
}
