namespace TermPoint.Services;

/// <summary>
/// The command-line argument a relaunch after a Save &amp; Backup restore passes to the new copy, so it reopens the
/// restored database instead of showing the database chooser (spec 1.2.4 item 4.3, design
/// <c>designs/restore-relaunch-1.2.4.md</c>, change C). Nothing is stored: the argument covers one start only.
/// </summary>
public static class RestartArguments
{
    /// <summary>The flag; the next argument is the full path of the restored database.</summary>
    public const string ReopenAfterRestoreFlag = "--reopen-after-restore";

    /// <summary>The arguments that ask the new copy to reopen <paramref name="databasePath"/>.</summary>
    /// <param name="databasePath">The restored (shared) database's full path.</param>
    /// <returns>The flag followed by the path, as separate arguments (the caller quotes nothing).</returns>
    public static string[] ForReopen(string databasePath) => [ReopenAfterRestoreFlag, databasePath];

    /// <summary>
    /// True when this start should open <paramref name="savedDatabasePath"/> directly, as the "Always open the most
    /// recent database" setting would, once: <paramref name="args"/> carry the flag and a path, and that path is the
    /// saved most-recent database (compared as full paths, ignoring case). Any mismatch, a missing or blank path, or
    /// a path that can't be compared answers false, and the start goes on as it would have anyway.
    /// </summary>
    /// <param name="args">This copy's command-line arguments.</param>
    /// <param name="savedDatabasePath">The most-recent database from the settings (<c>AppSettings.DatabasePath</c>).</param>
    /// <returns>True to open the saved database directly this once. Never throws.</returns>
    public static bool ShouldReopen(IReadOnlyList<string> args, string? savedDatabasePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(savedDatabasePath)) return false;

            var at = -1;
            for (var i = 0; i < args.Count; i++)
                if (string.Equals(args[i], ReopenAfterRestoreFlag, StringComparison.Ordinal)) { at = i; break; }
            if (at < 0 || at + 1 >= args.Count || string.IsNullOrWhiteSpace(args[at + 1])) return false;

            return string.Equals(Path.GetFullPath(args[at + 1]), Path.GetFullPath(savedDatabasePath),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // an unparseable path: carry on as without the argument
        }
    }
}
