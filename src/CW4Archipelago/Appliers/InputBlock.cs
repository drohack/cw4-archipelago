using HarmonyLib;

namespace CW4Archipelago.Appliers;

/// <summary>
/// While the message box has keyboard/wheel focus, skip the game's per-frame
/// input handling so typed keys (WASD/hotkeys) and the mouse wheel drive the
/// box, not the game. InputManager.enabled does NOT gate this - the game invokes
/// HandleInput directly each frame - so we prefix the handlers and skip them
/// while Blocked. Applied via Plugin.TryPatch, so if a game update renames these
/// the feature degrades but the mod keeps running.
/// </summary>
[HarmonyPatch]
public static class InputBlock
{
    public static bool Blocked;

    [HarmonyPatch(typeof(InputManager), "HandleInput")]
    [HarmonyPrefix]
    public static bool HandleInputPrefix() => !Blocked;

    [HarmonyPatch(typeof(InputManager), "HandleInputEarly")]
    [HarmonyPrefix]
    public static bool HandleInputEarlyPrefix() => !Blocked;
}
