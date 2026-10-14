namespace TermPoint.Services;

/// <summary>
/// Where the database chooser offers to restore a backup to (spec item 5c). Pure path logic with no I/O, so it is
/// unit-tested without touching a disk; the chooser probes the answer separately.
///
/// <para><b>Why the rules are what they are.</b> The old chooser restored into the BACKUP's folder under the last
/// database's name, so it usually created a second database next to the backups instead of repairing the shared
/// one. The rules now are:</para>
/// <list type="bullet">
/// <item><description><b>Name</b> — taken from the backup file itself, because the backup knows which database it
/// is a backup of: <c>&lt;name&gt;_&lt;yyyy-MM-dd_HH-mm-ss&gt;.db</c> (the periodic backup's naming) gives
/// <c>&lt;name&gt;.db</c>, and a pre-restore copy (<c>&lt;name&gt; (before restore …).&lt;ext&gt;</c>, which is
/// what a user restores to undo a restore) gives <c>&lt;name&gt;.&lt;ext&gt;</c>. A file named some other way
/// falls back to the last database's name, then to the backup's own.</description></item>
/// <item><description><b>Folder</b> — the last database's folder, but ONLY when the last database has that same
/// name (compared case-insensitively). Otherwise the backup's own folder. So a backup of a different database can
/// never replace the last database by default; the user must deliberately choose that folder.</description></item>
/// <item><description><b>Never onto itself</b> — restoring a file onto itself would destroy it
/// (<see cref="IsSameFile"/>); the chooser disables Continue then.</description></item>
/// </list>
///
/// <para>Desktop-only (it reads <see cref="BackupService"/> and <see cref="DatabaseRestore"/> naming), like the
/// chooser that uses it.</para>
/// </summary>
public static class RestoreDestination
{
    /// <summary>
    /// The extension a backup's derived database name gets: the periodic backup always writes <c>.db</c>, whatever
    /// extension the database it copied had.
    /// </summary>
    private const string BackupDatabaseExtension = ".db";

    /// <summary>
    /// Builds the default full destination path for restoring <paramref name="backupPath"/>: the name from
    /// <see cref="SuggestFileName"/>, in the last database's folder when that database has the same name, else in
    /// the backup's own folder.
    /// </summary>
    /// <param name="backupPath">Full path of the chosen backup file.</param>
    /// <param name="lastKnownPath">
    /// The database path the chooser was opened about (the one that failed to open, in recovery), or null/blank.
    /// Preferred over <paramref name="settingsPath"/> as "the last database".
    /// </param>
    /// <param name="settingsPath">The configured database path (<c>AppSettings.DatabasePath</c>), or null/blank.</param>
    /// <returns>The suggested full destination path.</returns>
    public static string SuggestRestorePath(string backupPath, string? lastKnownPath, string? settingsPath)
    {
        var lastDatabase = FirstNonBlank(lastKnownPath, settingsPath);
        var name         = SuggestFileName(backupPath, lastDatabase);

        var lastName   = lastDatabase is null ? null : Path.GetFileName(lastDatabase);
        var lastFolder = lastDatabase is null ? null : Path.GetDirectoryName(lastDatabase);

        // The last database's folder only for a database of the same name; otherwise beside the backup.
        var useLastFolder = !string.IsNullOrEmpty(lastFolder)
                            && string.Equals(lastName, name, StringComparison.OrdinalIgnoreCase);
        var folder = useLastFolder ? lastFolder! : Path.GetDirectoryName(backupPath) ?? string.Empty;

        return Path.Combine(folder, name);
    }

    /// <summary>
    /// The file name to restore under: derived from the backup's own name when it is a periodic backup
    /// (<c>name_yyyy-MM-dd_HH-mm-ss.db</c>) or a pre-restore copy; otherwise the last database's file name;
    /// otherwise the backup's file name.
    /// </summary>
    /// <param name="backupPath">Full path (or just the name) of the chosen backup file.</param>
    /// <param name="lastDatabasePath">The last database's path, or null when there is none.</param>
    /// <returns>A file name with extension, never a path.</returns>
    public static string SuggestFileName(string backupPath, string? lastDatabasePath)
    {
        if (BackupService.TryGetDatabaseNameFromBackupFilename(backupPath) is { } backupName)
            return backupName + BackupDatabaseExtension;

        if (DatabaseRestore.TryGetOriginalNameOfPreRestoreCopy(backupPath) is { } originalName)
            return originalName;

        var lastName = string.IsNullOrWhiteSpace(lastDatabasePath) ? null : Path.GetFileName(lastDatabasePath);
        return string.IsNullOrEmpty(lastName) ? Path.GetFileName(backupPath) : lastName;
    }

    /// <summary>
    /// Moves a destination to another folder, keeping its file name (what "Change folder…" does: the name stays
    /// the derived one and only the folder changes).
    /// </summary>
    /// <param name="destinationPath">The current destination path; only its file name is used.</param>
    /// <param name="folder">The folder to restore into.</param>
    /// <returns>The new full destination path.</returns>
    public static string WithFolder(string destinationPath, string folder)
        => Path.Combine(folder, Path.GetFileName(destinationPath));

    /// <summary>
    /// True when two paths name the same file: their full, normalised paths are equal, ignoring case (Windows and
    /// the default macOS volumes are case-insensitive, and refusing too much here is the safe direction). A
    /// path that cannot be normalised is compared as written.
    /// </summary>
    /// <param name="first">One path; null or blank is never the same file as anything.</param>
    /// <param name="second">The other path.</param>
    /// <returns>True when restoring onto <paramref name="second"/> would overwrite <paramref name="first"/> (or the reverse).</returns>
    public static bool IsSameFile(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        return string.Equals(Normalise(first), Normalise(second), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the full path of <paramref name="path"/>, or the text as written when it cannot be normalised
    /// (illegal characters, too long).
    /// </summary>
    /// <param name="path">The path to normalise.</param>
    private static string Normalise(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>Returns the first argument that is not null or blank, or null when both are.</summary>
    /// <param name="first">Preferred value.</param>
    /// <param name="second">Fallback value.</param>
    private static string? FirstNonBlank(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;
}
