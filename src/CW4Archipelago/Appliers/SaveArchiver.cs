using System;
using SIO = System.IO;

namespace CW4Archipelago.Appliers;

/// <summary>
/// Isolates the game's Farsite save files per Archipelago slot so a save from
/// one seed can never appear in (or be loaded into) another. On connecting to a
/// slot different from the last active one, the current saves/farsite folder is
/// moved into archive/&lt;previous&gt;/ and the new slot's archived saves are
/// restored. Moving saves/farsite is Steam-Cloud-safe (proven: not restored by
/// the cloud). Nothing is deleted; every switch is reversible.
///
/// The mission map's completion display is owned by TrackerView (driven by AP
/// state), so mcs.dat is left untouched - only the .cw4 save files need slotting.
/// </summary>
public static class SaveArchiver
{
    private const string Vanilla = "vanilla";

    private static string GameDataRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return SIO.Path.Combine(docs, "My Games", "creeperworld4");
    }

    private static string SavesFarsite() => SIO.Path.Combine(GameDataRoot(), "saves", "farsite");
    private static string ArchiveRoot() => SIO.Path.Combine(GameDataRoot(), "archipelago", "save-archive");
    private static string ActiveFile() => SIO.Path.Combine(ArchiveRoot(), "active.txt");

    private static string SlotKey(string seed, string slot)
    {
        var key = $"{seed}-{slot}";
        foreach (var c in SIO.Path.GetInvalidFileNameChars())
            key = key.Replace(c, '_');
        return key;
    }

    /// <summary>Switch the live saves/farsite folder to the given slot's set.</summary>
    public static void SwitchTo(string seed, string slot, Action<string> log)
    {
        try
        {
            var target = SlotKey(seed, slot);
            var active = ReadActive();
            if (active == target)
                return;   // already this slot

            SIO.Directory.CreateDirectory(ArchiveRoot());
            var farsite = SavesFarsite();

            // Stash the current live saves under the previously-active slot.
            var activeArchive = SIO.Path.Combine(ArchiveRoot(), active);
            MoveDirContents(farsite, activeArchive, log);

            // Restore the target slot's saves (if it has any archived).
            var targetArchive = SIO.Path.Combine(ArchiveRoot(), target);
            SIO.Directory.CreateDirectory(farsite);
            MoveDirContents(targetArchive, farsite, log);

            WriteActive(target);
            log($"SAVE ARCHIVE: switched saves/farsite from '{active}' to '{target}'");
        }
        catch (Exception e)
        {
            log($"SAVE ARCHIVE failed (saves left as-is): {e.Message}");
        }
    }

    private static string ReadActive()
    {
        try { return SIO.File.Exists(ActiveFile()) ? SIO.File.ReadAllText(ActiveFile()).Trim() : Vanilla; }
        catch { return Vanilla; }
    }

    private static void WriteActive(string key)
    {
        SIO.Directory.CreateDirectory(ArchiveRoot());
        SIO.File.WriteAllText(ActiveFile(), key);
    }

    // Move every file/subdir from src into dst (creating dst), leaving src empty.
    private static void MoveDirContents(string src, string dst, Action<string> log)
    {
        if (!SIO.Directory.Exists(src))
            return;
        SIO.Directory.CreateDirectory(dst);
        foreach (var file in SIO.Directory.GetFiles(src))
        {
            var to = SIO.Path.Combine(dst, SIO.Path.GetFileName(file));
            if (SIO.File.Exists(to)) SIO.File.Delete(to);
            SIO.File.Move(file, to);
        }
        foreach (var dir in SIO.Directory.GetDirectories(src))
        {
            var to = SIO.Path.Combine(dst, SIO.Path.GetFileName(dir));
            if (SIO.Directory.Exists(to)) SIO.Directory.Delete(to, true);
            SIO.Directory.Move(dir, to);
        }
    }
}
