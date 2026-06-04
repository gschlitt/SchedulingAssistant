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
