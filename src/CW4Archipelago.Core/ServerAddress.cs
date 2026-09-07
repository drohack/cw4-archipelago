namespace CW4Archipelago.Core;

/// <summary>
/// Splitting "host:port" as typed into the login panel.
///
/// Lived in MenuUi, where nothing could test it, and gave an EMPTY host for
/// empty input. The panel writes whatever it parses straight into the config on
/// connect, so clearing the server field left a config with no host at all -
/// while the field's placeholder still read "archipelago.gg:38281", so it looked
/// set. Empty now falls back to the default rather than to nothing.
/// </summary>
public static class ServerAddress
{
    /// <summary>Where a player is overwhelmingly likely to be playing, and the
    /// same value ModConfig defaults Host to.</summary>
    public const string DefaultHost = "archipelago.gg";

    /// <summary>Archipelago's own default port.</summary>
    public const int DefaultPort = 38281;

    /// <summary>The host part, or the default if none was given.
    ///
    /// LastIndexOf, not IndexOf: an IPv6 literal is full of colons and the port
    /// is after the last one.
    /// </summary>
    public static string Host(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0)
            return DefaultHost;
        var i = s.LastIndexOf(':');
        var host = (i > 0 ? s.Substring(0, i) : s).Trim();
        return host.Length == 0 ? DefaultHost : host;
    }

    /// <summary>The port part, or the default when absent or unparseable.</summary>
    public static int Port(string? text)
    {
        var s = (text ?? "").Trim();
        var i = s.LastIndexOf(':');
        if (i <= 0)
            return DefaultPort;
        return int.TryParse(s.Substring(i + 1).Trim(), out var p) && p > 0 && p <= 65535
            ? p
            : DefaultPort;
    }
}
