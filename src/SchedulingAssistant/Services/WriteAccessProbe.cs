using System;
using System.IO;

namespace SchedulingAssistant.Services;

/// <summary>
/// Helpers for diagnosing why writing to a chosen folder failed. Pure BCL — no UI and no
/// SQLite dependency — so it is safe to call from the data layer and reusable across every
/// save/export site if the write-failure handling is broadened later.
///
/// The motivating case is Windows Defender <b>Controlled Folder Access</b>, which silently
/// blocks applications it does not yet trust (e.g. unsigned builds) from creating or modifying
/// files inside protected "known folders" such as Documents and Desktop. Reads succeed, so the
/// failure only appears on the first write (for SQLite, when the <c>-journal</c> sidecar is
/// created) and surfaces as a generic IO/SQLite error. These helpers let callers tell that case
/// apart from a genuine lock, corruption, or bad-path problem.
/// </summary>
public static class WriteAccessProbe
{
    /// <summary>
    /// Attempts to create and immediately delete a uniquely-named temporary file in
    /// <paramref name="directory"/>. Returns <c>true</c> when the folder accepts new files, and
    /// <c>false</c> on access/IO failures (Controlled Folder Access, a read-only ACL, read-only
    /// media, or a missing directory). Never throws.
    /// </summary>
    /// <param name="directory">The folder to test for write access.</param>
    /// <returns><c>true</c> if a file can be created in the folder; otherwise <c>false</c>.</returns>
    public static bool CanCreateFileIn(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var probe = Path.Combine(directory, $".sa-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(0);
            }
            File.Delete(probe);
            return true;
        }
        catch
        {
            // Any failure — UnauthorizedAccessException (CFA / ACL), IOException, or a missing
            // parent directory — means we cannot rely on writing here.
            try { if (File.Exists(probe)) File.Delete(probe); } catch { /* best-effort cleanup */ }
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> is at or under a Windows "known folder"
    /// that Controlled Folder Access protects by default: Documents, Desktop, Pictures, Videos,
    /// Music, or Favorites. <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
    /// resolves OneDrive Known-Folder-Move redirection, so redirected folders are covered too.
    /// </summary>
    /// <param name="path">A file or directory path to classify.</param>
    /// <returns><c>true</c> if the path lives inside a protected known folder.</returns>
    public static bool IsProtectedKnownFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }

        Environment.SpecialFolder[] protectedFolders =
        {
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.Favorites,
        };

        foreach (var folder in protectedFolders)
        {
            var known = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(known)) continue;
            if (IsAtOrUnder(full, known)) return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the user-facing wording shown when a folder rejects writes, so every entry point
    /// (database open, the startup wizard, future export sites) phrases it identically.
    /// </summary>
    /// <param name="directory">The folder that could not be written to.</param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item><description><c>userMessage</c> — neutral, location-focused guidance safe for any
    ///   audience. Deliberately omits app-lifecycle phrasing (e.g. "will now close") so it reads
    ///   correctly both in a dialog and inline in the wizard.</description></item>
    ///   <item><description><c>itDetail</c> — the Controlled Folder Access explanation for IT,
    ///   non-null <b>only</b> when <paramref name="directory"/> is a protected known folder.</description></item>
    /// </list>
    /// </returns>
    public static (string userMessage, string? itDetail) DescribeWriteBlock(string? directory)
    {
        var where = string.IsNullOrWhiteSpace(directory) ? "" : $"\n\n{directory}";
        var userMessage =
            "This folder doesn't allow the application to save changes, so the database can't be " +
            $"used here:{where}\n\n" +
            "Please choose a different location — a shared network folder, or a folder such as " +
            "C:\\Schedules.";

        var itDetail = IsProtectedKnownFolder(directory)
            ? "This folder is one of Windows' protected locations (Documents, Desktop, Pictures, " +
              "etc.). Windows Defender 'Controlled Folder Access' blocks applications it does not yet " +
              "trust from creating or changing files there, which prevents the database's journal file " +
              "from being created. Either store the database outside these folders (a shared network " +
              "drive works well), or add this application under Windows Security → Virus & threat " +
              "protection → Ransomware protection → Allow an app through Controlled folder access."
            : null;

        return (userMessage, itDetail);
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> equals <paramref name="ancestor"/> or is nested
    /// beneath it. Comparison is case-insensitive (Windows paths) and separator-normalized.
    /// </summary>
    private static bool IsAtOrUnder(string fullPath, string ancestor)
    {
        string a;
        try { a = Path.GetFullPath(ancestor); }
        catch { return false; }

        a = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var p = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
