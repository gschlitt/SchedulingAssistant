using System.Globalization;
using System.IO.Compression;

namespace TermPoint.Services;

/// <summary>One daily log file the Support panel can offer to save.</summary>
/// <param name="Path">Full path of the log file.</param>
/// <param name="Day">The day the log covers (from its file name, <c>app-yyyy-MM-dd.log</c>).</param>
/// <param name="SizeBytes">The file's size when it was listed.</param>
public sealed record SupportLogFile(string Path, DateTime Day, long SizeBytes);

/// <summary>
/// The file work behind the Support panel's "Save logs…" (Dev Tools → Support): listing TermPoint's daily log files
/// and packing the chosen ones into one zip the user can attach to an email. Desktop only (the browser build has no
/// log files). Nothing here touches a database.
/// </summary>
public static class SupportLogs
{
    /// <summary>The prefix and extension of a daily log file's name, as <see cref="FileAppLogger"/> writes it.</summary>
    private const string LogFilePrefix = "app-";
    private const string LogFileExtension = ".log";

    /// <summary>The date format inside a daily log file's name.</summary>
    private const string LogFileDateFormat = "yyyy-MM-dd";

    /// <summary>The folder TermPoint writes its logs to (the same one <see cref="FileAppLogger"/> uses).</summary>
    public static string LogFolder => FileAppLogger.LogDirectory;

    /// <summary>
    /// Lists the daily log files in <paramref name="folder"/>, newest day first. Files whose names aren't
    /// <c>app-yyyy-MM-dd.log</c> are left out. Never throws: a missing or unreadable folder gives an empty list.
    /// </summary>
    /// <param name="folder">The log folder; normally <see cref="LogFolder"/>.</param>
    /// <returns>The log files, newest first.</returns>
    public static IReadOnlyList<SupportLogFile> List(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<SupportLogFile>();

            var logs = new List<SupportLogFile>();
            foreach (var path in Directory.GetFiles(folder, LogFilePrefix + "*" + LogFileExtension))
            {
                if (TryParseDay(System.IO.Path.GetFileName(path), out var day))
                    logs.Add(new SupportLogFile(path, day, new FileInfo(path).Length));
            }
            return logs.OrderByDescending(l => l.Day).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<SupportLogFile>();
        }
    }

    /// <summary>
    /// Reads the day out of a daily log file's name (<c>app-2026-11-11.log</c> → 11 November 2026).
    /// </summary>
    /// <param name="fileName">The file name, without a folder.</param>
    /// <param name="day">The day, when the name has the expected form.</param>
    /// <returns>True when the name is a daily log file's name.</returns>
    internal static bool TryParseDay(string fileName, out DateTime day)
    {
        day = default;
        if (!fileName.StartsWith(LogFilePrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(LogFileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        var datePart = fileName.Substring(LogFilePrefix.Length,
            fileName.Length - LogFilePrefix.Length - LogFileExtension.Length);
        return DateTime.TryParseExact(datePart, LogFileDateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out day);
    }

    /// <summary>The name the Save dialog suggests for the zip, e.g. <c>TermPoint logs 2026-11-11.zip</c>.</summary>
    /// <param name="today">Today's date.</param>
    /// <returns>The suggested file name.</returns>
    public static string SuggestedZipName(DateTime today)
        => $"TermPoint logs {today.ToString(LogFileDateFormat, CultureInfo.InvariantCulture)}.zip";

    /// <summary>
    /// Writes the log files in <paramref name="logPaths"/> into a new zip at <paramref name="zipPath"/> (replacing a
    /// file already there, which the Save dialog has confirmed), each under its own file name. Today's log may still
    /// be in use by the logger, so each file is read with shared access. If writing fails, the partly written zip is
    /// deleted.
    /// </summary>
    /// <param name="logPaths">Full paths of the log files to include; at least one.</param>
    /// <param name="zipPath">Full path of the zip to write.</param>
    /// <exception cref="ArgumentException"><paramref name="logPaths"/> is empty.</exception>
    /// <exception cref="IOException">A log could not be read, or the zip could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The zip's folder can't be written to.</exception>
    public static void WriteZip(IReadOnlyList<string> logPaths, string zipPath)
    {
        if (logPaths.Count == 0)
            throw new ArgumentException("At least one log file is needed.", nameof(logPaths));

        try
        {
            using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            foreach (var logPath in logPaths)
            {
                var entry = archive.CreateEntry(System.IO.Path.GetFileName(logPath), CompressionLevel.Optimal);
                using var source = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var target = entry.Open();
                source.CopyTo(target);
            }
        }
        catch
        {
            try { File.Delete(zipPath); } catch { /* leave it; the caller reports the failure */ }
            throw;
        }
    }
}
