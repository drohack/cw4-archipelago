using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CW4DevTools;

/// <summary>
/// A development aid for surveying missions quickly. Deliberately a SEPARATE
/// plugin from the randomizer: it shares no code, ships in no release, and the
/// two never reference each other. Run either, both, or neither.
///
/// To play the base game, just do not install this and disable the randomizer.
/// Note: disabling means MOVING the plugin folder out of BepInEx/plugins -
/// renaming it ".disabled" does nothing, BepInEx scans subfolders recursively.
///
/// The Home key dumps the game's unit-name registry and how the cheats classify
/// every unit on the map. That diagnostic is what uncovered the build-pane-key
/// vs unit-name split written up in docs/research-findings.md ("Unit naming"),
/// which had been making the cheats skip pylons and miners entirely.
/// </summary>
[BepInPlugin("com.droha.cw4devtools", "CW4 Dev Tools", "0.1.0")]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        DevConfig.Init(Config);

        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<DevBehaviour>();
            AddComponent<DevBehaviour>();
        }
        catch (Exception e)
        {
            Log.LogError($"FATAL: could not install the dev behaviour: {e.Message}");
            return;
        }

        DevTools.Init(Log);
        Log.LogInfo("CW4 Dev Tools loaded. " + DevConfig.Summary());
    }
}

/// <summary>
/// Injected MonoBehaviour, kept as a thin shim with no statics - statics on
/// injected IL2CPP types have correlated with stack-overflow crashes during
/// mission load. All work lives in DevTools.
/// </summary>
public class DevBehaviour : MonoBehaviour
{
    public DevBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update() => DevTools.SafeTick();
}

/// <summary>Config plus the hotkeys that toggle each switch mid-mission.</summary>
public static class DevConfig
{
    public static ConfigEntry<bool> InstantBuild = null!;
    public static ConfigEntry<bool> AllBuildings = null!;
    public static ConfigEntry<bool> InfiniteResources = null!;
    public static ConfigEntry<bool> Indestructible = null!;

    public static ConfigEntry<bool> FreezeCreeper = null!;
    public static ConfigEntry<int> GameSpeed = null!;
    public static ConfigEntry<bool> ShowOverlay = null!;
    public static ConfigEntry<int> OverlayX = null!;
    public static ConfigEntry<int> OverlayY = null!;
    public static ConfigEntry<bool> DumpUnitsOnStart = null!;
    public static ConfigEntry<KeyCode> HotkeyModifier = null!;

    public static ConfigEntry<KeyCode> KeyInstantBuild = null!;
    public static ConfigEntry<KeyCode> KeyAllBuildings = null!;
    public static ConfigEntry<KeyCode> KeyInfiniteResources = null!;
    public static ConfigEntry<KeyCode> KeyIndestructible = null!;
    public static ConfigEntry<KeyCode> KeyFreezeCreeper = null!;
    public static ConfigEntry<KeyCode> KeyGameSpeed = null!;
    public static ConfigEntry<KeyCode> KeyRevealFog = null!;
    public static ConfigEntry<KeyCode> KeyWinMission = null!;
    public static ConfigEntry<KeyCode> KeyDumpUnits = null!;

    public static void Init(ConfigFile file)
    {
        // Any setting change redraws the overlay. Covers the hotkeys, which
        // write config values, and hand edits to the .cfg while the game runs -
        // which is how the overlay position is tuned.
        file.SettingChanged += (_, __) => DevOverlay.Invalidate();

        InstantBuild = file.Bind("Cheats", "InstantBuild", false,
            "Your buildings finish the moment they are placed, at no cost.");
        AllBuildings = file.Bind("Cheats", "AllBuildings", false,
            "Every building is available to place, ignoring the campaign unlock schedule. " +
            "Off means you get exactly what the mission would normally offer.");
        InfiniteResources = file.Bind("Cheats", "InfiniteResources", false,
            "Energy stays full, and units that hold wares (factory greenar/redon/bluite, " +
            "weapon ammo) stay topped up.");
        Indestructible = file.Bind("Cheats", "Indestructible", false,
            "Your units cannot be destroyed. Sets the game's own 'impervious' flag, lifts " +
            "DESTROY_ON_UNEVEN_TERRAIN (which removes platforms outright, past any amount " +
            "of health) and holds health at maximum. Creeper still flows and enemies still " +
            "fire; your buildings just survive it.");

        FreezeCreeper = file.Bind("Cheats", "FreezeCreeper", false,
            "Creeper and anti-creeper stop flowing and spreading, so a map can be inspected " +
            "without dying. Emitters keep producing; nothing moves.");
        GameSpeed = file.Bind("Cheats", "GameSpeed", 0,
            "Force the simulation speed. 0 leaves the game alone. The in-game buttons cap at " +
            "4x; this does not, so 8 or 16 makes a long mission short. Very high values will " +
            "drop the frame rate.");
        ShowOverlay = file.Bind("Display", "ShowOverlay", true,
            "Show the on-screen list of active cheats. Worth leaving on: it prevents writing " +
            "up a mission as vanilla when it was played with the tools running.");

        // Tunable because the HUD is not static: the terp's terrain-height bar
        // appears at the bottom centre only while that tool is selected, so a
        // position that looks clear can be covered a minute later. Read every
        // frame, so editing the config while the game runs moves the strip
        // immediately - no restart, no rebuild.
        OverlayX = file.Bind("Display", "OverlayX", 0,
            "Horizontal offset of the overlay from the bottom centre, in 1920x1080 units. " +
            "Positive is right.");
        OverlayY = file.Bind("Display", "OverlayY", 80,
            "Height of the overlay above the bottom edge, in 1920x1080 units. The default " +
            "clears the terraform bar (measured at 74 units tall), which appears at the " +
            "bottom centre only while terraform mode is open. Lower it to about 12 to sit " +
            "right on the bottom edge.");

        DumpUnitsOnStart = file.Bind("Display", "DumpUnitsOnStart", false,
            "Diagnostic: run the unit-name dump automatically on entering a mission, as if " +
            "the DumpUnits hotkey were pressed. For when a cheat appears to skip a building.");

        HotkeyModifier = file.Bind("Hotkeys", "Modifier", KeyCode.LeftControl,
            "Key that must be HELD for any dev hotkey to fire. F5-F12 are also " +
            "Creeper World hotkeys, so without a modifier every toggle collides with " +
            "a game action. Set to None to use the keys bare (not recommended).");

        KeyInstantBuild = file.Bind("Hotkeys", "ToggleInstantBuild", KeyCode.F5,
            "Toggles InstantBuild in-mission (hold the Modifier). None disables it.");
        KeyAllBuildings = file.Bind("Hotkeys", "ToggleAllBuildings", KeyCode.F6,
            "Toggles AllBuildings in-mission.");
        KeyInfiniteResources = file.Bind("Hotkeys", "ToggleInfiniteResources", KeyCode.F7,
            "Toggles InfiniteResources in-mission.");
        KeyIndestructible = file.Bind("Hotkeys", "ToggleIndestructible", KeyCode.F8,
            "Toggles Indestructible in-mission.");
        KeyFreezeCreeper = file.Bind("Hotkeys", "ToggleFreezeCreeper", KeyCode.F9,
            "Toggles FreezeCreeper in-mission.");
        KeyGameSpeed = file.Bind("Hotkeys", "CycleGameSpeed", KeyCode.F10,
            "Cycles the forced speed: off, 2, 4, 8, 16.");
        KeyRevealFog = file.Bind("Hotkeys", "RevealFog", KeyCode.F11,
            "One-shot: reveals the whole map on fog missions.");
        KeyWinMission = file.Bind("Hotkeys", "CompleteObjectives", KeyCode.End,
            "One-shot: marks every mission objective complete, to leave a mission early once " +
            "you have learned what it needs. Deliberately not an F-key.");
        KeyDumpUnits = file.Bind("Hotkeys", "DumpUnits", KeyCode.Home,
            "Diagnostic: logs every unit name the game knows (UnitData.unitConstants) and, " +
            "for each unit on the map, its type, data name and whether the cheats consider it " +
            "yours. Use it when a cheat appears to skip a building.");
    }

    public static string Summary() =>
        $"InstantBuild={InstantBuild.Value} AllBuildings={AllBuildings.Value} " +
        $"InfiniteResources={InfiniteResources.Value} Indestructible={Indestructible.Value} " +
        $"FreezeCreeper={FreezeCreeper.Value} GameSpeed={(GameSpeed.Value > 0 ? GameSpeed.Value.ToString() : "game")}";
}
