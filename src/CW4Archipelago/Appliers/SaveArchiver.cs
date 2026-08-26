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
            {
                // Already this slot - no folder move needed, but still ensure
                // the live folder carries the seed stamp (it may be missing on a
                // reconnect to the same slot, or after a manual save restore).
                WriteStamp(seed, slot);
                return;
            }

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
            WriteStamp(seed, slot);
            log($"SAVE ARCHIVE: switched saves/farsite from '{active}' to '{target}'");
        }
        catch (Exception e)
        {
            log($"SAVE ARCHIVE failed (saves left as-is): {e.Message}");
        }
    }

    // A seed stamp written inside saves/farsite makes the binding explicit and
    // auditable, and travels with the folder if it is ever copied. Used by
    // SeedMatches to detect a save set that does not belong to the connected
    // seed/slot (e.g. after offline play or manual file moves).
    private static string StampFile() => SIO.Path.Combine(SavesFarsite(), "archipelago-seed.txt");

    private static void WriteStamp(string seed, string slot)
    {
        try
        {
            SIO.Directory.CreateDirectory(SavesFarsite());
            SIO.File.WriteAllText(StampFile(), $"{seed}|{slot}");
        }
        catch { /* stamp is best-effort; isolation already done via archive */ }
    }

    /// <summary>
    /// True if the live saves/farsite folder is stamped for this seed+slot (or
    /// has no stamp yet, i.e. nothing to contradict). False only on a definite
    /// mismatch - caller should warn the player they are on the wrong seed.
    /// </summary>
    public static bool SeedMatches(string seed, string slot)
    {
        try
        {
            if (!SIO.File.Exists(StampFile()))
                return true;   // unstamped legacy/vanilla saves - do not false-alarm
            var parts = SIO.File.ReadAllText(StampFile()).Split('|');
            return parts.Length == 2 && parts[0] == seed && parts[1] == slot;
        }
        catch { return true; }
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
