using CW4Archipelago.Core;
using BepInEx.Configuration;

namespace CW4Archipelago;

/// <summary>BepInEx-backed connection settings, editable from the in-game panel.</summary>
public sealed class ModConfig
{
    public ConfigEntry<string> Host { get; }
    public ConfigEntry<int> Port { get; }
    public ConfigEntry<string> Slot { get; }
    public ConfigEntry<string> Password { get; }
    public ConfigEntry<bool> AutoConnect { get; }
    public ConfigEntry<bool> ShowSpan { get; }

    public ModConfig(ConfigFile file)
    {
        // Defaults come from Core so the config, the panel's placeholder and the
        // parser's fallback cannot disagree about them.
        Host = file.Bind("Connection", "Host", ServerAddress.DefaultHost,
            "Archipelago server host. archipelago.gg for a room hosted on the website.");
        Port = file.Bind("Connection", "Port", ServerAddress.DefaultPort,
            "Archipelago server port. A website room gives you its own port; this is the default.");
        Slot = file.Bind("Connection", "Slot", "",
            "Your slot (player) name - the `name:` from your yaml. Blank until you set it.");
        Password = file.Bind("Connection", "Password", "", "Room password, if any.");
        AutoConnect = file.Bind("Connection", "AutoConnect", false, "Connect automatically at the main menu.");
        ShowSpan = file.Bind("Missions", "ShowSpan", false,
            "Show the SPAN Experiments button. The randomizer covers the 20 Farsite missions; " +
            "SPAN is a future expansion and is hidden by default.");
        // No Debug section: the file-command test channel is a separate
        // plugin now (src/CW4Archipelago.Debug), and installing it is what
        // enables it. Harnesses that still write DebugCommands into the .cfg
        // are harmless - BepInEx ignores an unknown key.
    }
}
