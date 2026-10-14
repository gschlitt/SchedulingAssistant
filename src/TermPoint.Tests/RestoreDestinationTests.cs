using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="RestoreDestination"/> — where the database chooser offers to restore a backup to (spec item
/// 5c) — and for the two file-name parsers it is built on (<see cref="BackupService.TryGetDatabaseNameFromBackupFilename"/>
/// and <see cref="DatabaseRestore.TryGetOriginalNameOfPreRestoreCopy"/>). Pure path logic: nothing here touches a disk.
///
/// <para>The rules proved: the NAME comes from the backup itself (periodic-backup naming, or a pre-restore copy — which
/// is how a user undoes a restore), falling back to the last database's name and then to the backup's own; the FOLDER
/// is the last database's only when that database has the same name (so a backup of a different database can never
/// replace the last database by default), otherwise the backup's own folder; and a file is never restored onto itself.</para>
/// </summary>
public sealed class RestoreDestinationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A folder that stands in for "the shared folder" holding the databases; built for the current OS.</summary>
    private static readonly string Share = Path.Combine(Path.GetTempPath(), "rd-tests", "share");

    /// <summary>A folder that stands in for the backup folder.</summary>
    private static readonly string Backups = Path.Combine(Path.GetTempPath(), "rd-tests", "backups");

    /// <summary>A path inside the share.</summary>
    /// <param name="name">File name.</param>
    private static string InShare(string name) => Path.Combine(Share, name);

    /// <summary>A path inside the backup folder.</summary>
    /// <param name="name">File name.</param>
    private static string InBackups(string name) => Path.Combine(Backups, name);

    // ── Name: from the backup's own name ──────────────────────────────────────

    /// <summary>A periodic backup <c>name_yyyy-MM-dd_HH-mm-ss.db</c> is a backup of <c>name.db</c>.</summary>
    [Theory]
    [InlineData("BIOL-TT_2026-10-13_14-30-00.db", "BIOL-TT")]
    [InlineData("Computer Science_2026-03-20_14-30-00.db", "Computer Science")]
    [InlineData("my_dept_2026-03-20_14-30-00.db", "my_dept")]
    [InlineData("a_2026-03-20_14-30-00.db", "a")]
    public void BackupName_PeriodicBackup_GivesTheDatabaseName(string backupName, string expected)
    {
        Assert.Equal(expected, BackupService.TryGetDatabaseNameFromBackupFilename(InBackups(backupName)));
    }

    /// <summary>Names that are not periodic-backup names give no database name.</summary>
    [Theory]
    [InlineData("BIOL-TT.db")]
    [InlineData("BIOL-TT_backup.db")]
    [InlineData("BIOL-TT_2026-10-13.db")]
    [InlineData("BIOL-TT 2026-10-13_14-30-00.db")]     // space, not underscore, before the timestamp
    [InlineData("_2026-10-13_14-30-00.db")]            // nothing before the timestamp
    [InlineData("2026-10-13_14-30-00.db")]             // only the timestamp
    [InlineData("BIOL-TT_2026-13-45_99-99-99.db")]     // not a real date
    public void BackupName_NotAPeriodicBackup_GivesNothing(string fileName)
    {
        Assert.Null(BackupService.TryGetDatabaseNameFromBackupFilename(InBackups(fileName)));
    }

    /// <summary>
    /// A pre-restore copy (the file a user restores to undo a restore) is a copy of the original database, with or
    /// without the " (n)" of a second copy in the same minute.
    /// </summary>
    [Theory]
    [InlineData("dept (before restore 2026-10-13 14-05).db", "dept.db")]
    [InlineData("dept (before restore 2026-10-13 14-05) (2).db", "dept.db")]
    [InlineData("dept (before restore 2026-10-13 14-05) (20).db", "dept.db")]
    [InlineData("my dept.v2 (before restore 2026-10-13 14-05).db", "my dept.v2.db")]
    [InlineData("a (before restore 2026-10-13 14-05).sqlite", "a.sqlite")]
    [InlineData("noext (before restore 2026-10-13 14-05)", "noext")]
    public void PreRestoreName_GivesTheOriginalDatabaseName(string fileName, string expected)
    {
        Assert.Equal(expected, DatabaseRestore.TryGetOriginalNameOfPreRestoreCopy(InShare(fileName)));
    }

    /// <summary>The parser inverts the builder: whatever <see cref="DatabaseRestore.PreRestoreCopyName"/> makes, the original name comes back.</summary>
    [Theory]
    [InlineData("BIOL-TT.db", 1)]
    [InlineData("BIOL-TT.db", 2)]
    [InlineData("my.dept.db", 3)]
    [InlineData("noext", 1)]
    public void PreRestoreName_IsTheInverseOfPreRestoreCopyName(string original, int attempt)
    {
        var made = DatabaseRestore.PreRestoreCopyName(InShare(original), new DateTime(2026, 10, 13, 14, 5, 0), attempt);

        Assert.Equal(original, DatabaseRestore.TryGetOriginalNameOfPreRestoreCopy(made));
    }

    /// <summary>Names that are not pre-restore copies give nothing.</summary>
    [Theory]
    [InlineData("dept.db")]
    [InlineData("dept (before restore).db")]
    [InlineData("dept (before restore 2026-10-13).db")]
    [InlineData("dept before restore 2026-10-13 14-05.db")]
    [InlineData(" (before restore 2026-10-13 14-05).db")]
    public void PreRestoreName_NotAPreRestoreCopy_GivesNothing(string fileName)
    {
        Assert.Null(DatabaseRestore.TryGetOriginalNameOfPreRestoreCopy(InShare(fileName)));
    }

    // ── SuggestFileName ───────────────────────────────────────────────────────

    /// <summary>The name is derived from a periodic backup, whatever the last database was called.</summary>
    [Fact]
    public void SuggestFileName_PeriodicBackup_UsesTheBackupsDatabaseName()
    {
        Assert.Equal("BIOL-TT.db",
            RestoreDestination.SuggestFileName(InBackups("BIOL-TT_2026-10-13_14-30-00.db"), InShare("MATH-TT.db")));
    }

    /// <summary>The name is derived from a pre-restore copy, keeping its extension.</summary>
    [Fact]
    public void SuggestFileName_PreRestoreCopy_UsesTheOriginalName()
    {
        Assert.Equal("dept.db",
            RestoreDestination.SuggestFileName(InShare("dept (before restore 2026-10-13 14-05) (2).db"), InShare("other.db")));
    }

    /// <summary>A backup not named either way falls back to the last database's name…</summary>
    [Fact]
    public void SuggestFileName_UnknownName_FallsBackToTheLastDatabasesName()
    {
        Assert.Equal("MATH-TT.db", RestoreDestination.SuggestFileName(InBackups("copy of stuff.db"), InShare("MATH-TT.db")));
    }

    /// <summary>…and, with no last database, to the backup's own name.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SuggestFileName_UnknownName_NoLastDatabase_UsesTheBackupsOwnName(string? last)
    {
        Assert.Equal("copy of stuff.db", RestoreDestination.SuggestFileName(InBackups("copy of stuff.db"), last));
    }

    // ── SuggestRestorePath: the name ──────────────────────────────────────────

    /// <summary>The backup-pattern name, in the last database's folder when that database has the same name.</summary>
    [Fact]
    public void Suggest_BackupPatternName_SameNameAsLastDatabase_IsTheLastDatabasesPath()
    {
        var path = RestoreDestination.SuggestRestorePath(
            InBackups("BIOL-TT_2026-10-13_14-30-00.db"), lastKnownPath: InShare("BIOL-TT.db"), settingsPath: null);

        Assert.Equal(InShare("BIOL-TT.db"), path);
    }

    /// <summary>A pre-restore copy is offered back to the database it was a copy of (with and without " (2)").</summary>
    [Theory]
    [InlineData("dept (before restore 2026-10-13 14-05).db")]
    [InlineData("dept (before restore 2026-10-13 14-05) (2).db")]
    public void Suggest_PreRestoreCopy_IsOfferedBackToTheOriginalDatabase(string preRestoreName)
    {
        var path = RestoreDestination.SuggestRestorePath(InShare(preRestoreName), lastKnownPath: InShare("dept.db"), settingsPath: null);

        Assert.Equal(InShare("dept.db"), path);
    }

    /// <summary>An unknown backup name takes the last database's name AND folder (the same name, trivially).</summary>
    [Fact]
    public void Suggest_UnknownName_TakesTheLastDatabasesNameAndFolder()
    {
        var path = RestoreDestination.SuggestRestorePath(InBackups("whatever.db"), InShare("MATH-TT.db"), null);

        Assert.Equal(InShare("MATH-TT.db"), path);
    }

    /// <summary>An unknown backup name and no last database: the backup's own name in the backup's own folder.</summary>
    [Fact]
    public void Suggest_UnknownName_NoLastDatabase_IsTheBackupsOwnPath()
    {
        var path = RestoreDestination.SuggestRestorePath(InBackups("whatever.db"), null, null);

        Assert.Equal(InBackups("whatever.db"), path);
    }

    // ── SuggestRestorePath: the folder ────────────────────────────────────────

    /// <summary>
    /// A backup of a DIFFERENT database never defaults onto the last database: the name is the backup's database's,
    /// and the folder is the backup's own, so nothing the user has open is a candidate for replacement.
    /// </summary>
    [Fact]
    public void Suggest_BackupOfADifferentDatabase_NeverTargetsTheLastDatabase()
    {
        var path = RestoreDestination.SuggestRestorePath(
            InBackups("BIOL-TT_2026-10-13_14-30-00.db"), lastKnownPath: InShare("MATH-TT.db"), settingsPath: null);

        Assert.Equal(InBackups("BIOL-TT.db"), path);
        Assert.NotEqual(InShare("MATH-TT.db"), path);
    }

    /// <summary>The file-name comparison that decides the folder ignores case.</summary>
    [Fact]
    public void Suggest_NameMatchIsCaseInsensitive()
    {
        var path = RestoreDestination.SuggestRestorePath(
            InBackups("biol-tt_2026-10-13_14-30-00.db"), lastKnownPath: InShare("BIOL-TT.DB"), settingsPath: null);

        // The folder is the last database's; the name is the one derived from the backup.
        Assert.Equal(Share, Path.GetDirectoryName(path));
        Assert.Equal("biol-tt.db", Path.GetFileName(path));
    }

    /// <summary>With no last database at all, even a backup-pattern name goes beside the backup.</summary>
    [Fact]
    public void Suggest_NoLastDatabase_BackupPatternName_GoesBesideTheBackup()
    {
        var path = RestoreDestination.SuggestRestorePath(InBackups("BIOL-TT_2026-10-13_14-30-00.db"), null, null);

        Assert.Equal(InBackups("BIOL-TT.db"), path);
    }

    // ── SuggestRestorePath: which "last database" ─────────────────────────────

    /// <summary>The chooser's known path wins over the settings path.</summary>
    [Fact]
    public void Suggest_LastKnownPathWinsOverTheSettingsPath()
    {
        var other = Path.Combine(Path.GetTempPath(), "rd-tests", "elsewhere");

        var path = RestoreDestination.SuggestRestorePath(
            InBackups("BIOL-TT_2026-10-13_14-30-00.db"),
            lastKnownPath: InShare("BIOL-TT.db"),
            settingsPath: Path.Combine(other, "BIOL-TT.db"));

        Assert.Equal(InShare("BIOL-TT.db"), path);
    }

    /// <summary>Without a known path, the settings path is the last database.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Suggest_NoKnownPath_UsesTheSettingsPath(string? lastKnown)
    {
        var path = RestoreDestination.SuggestRestorePath(
            InBackups("BIOL-TT_2026-10-13_14-30-00.db"), lastKnown, settingsPath: InShare("BIOL-TT.db"));

        Assert.Equal(InShare("BIOL-TT.db"), path);
    }

    /// <summary>A last database whose name differs is not used for the folder even when it is the settings path.</summary>
    [Fact]
    public void Suggest_SettingsPathOfADifferentName_IsNotUsedForTheFolder()
    {
        var path = RestoreDestination.SuggestRestorePath(
            InBackups("BIOL-TT_2026-10-13_14-30-00.db"), null, settingsPath: InShare("MATH-TT.db"));

        Assert.Equal(InBackups("BIOL-TT.db"), path);
    }

    // ── WithFolder ────────────────────────────────────────────────────────────

    /// <summary>"Change folder…" keeps the derived name and only changes the folder.</summary>
    [Fact]
    public void WithFolder_KeepsTheNameAndChangesTheFolder()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "rd-tests", "elsewhere");

        Assert.Equal(Path.Combine(elsewhere, "BIOL-TT.db"), RestoreDestination.WithFolder(InShare("BIOL-TT.db"), elsewhere));
    }

    // ── IsSameFile ────────────────────────────────────────────────────────────

    /// <summary>The same path, in any case, or written with a redundant segment, is the same file.</summary>
    [Fact]
    public void IsSameFile_SamePath_IgnoringCaseAndRedundantSegments()
    {
        Assert.True(RestoreDestination.IsSameFile(InShare("a.db"), InShare("a.db")));
        Assert.True(RestoreDestination.IsSameFile(InShare("a.db"), InShare("A.DB")));
        Assert.True(RestoreDestination.IsSameFile(InShare("a.db"), Path.Combine(Share, "sub", "..", "a.db")));
    }

    /// <summary>Different files, or no path at all, are never "the same file".</summary>
    [Fact]
    public void IsSameFile_DifferentOrMissingPaths_AreNotTheSame()
    {
        Assert.False(RestoreDestination.IsSameFile(InShare("a.db"), InShare("b.db")));
        Assert.False(RestoreDestination.IsSameFile(InShare("a.db"), InBackups("a.db")));
        Assert.False(RestoreDestination.IsSameFile(null, InShare("a.db")));
        Assert.False(RestoreDestination.IsSameFile(InShare("a.db"), null));
        Assert.False(RestoreDestination.IsSameFile("", ""));
    }

    /// <summary>A path that cannot be normalised does not throw; it compares as written.</summary>
    [Fact]
    public void IsSameFile_AnIllegalPath_DoesNotThrow()
    {
        var bad = "bad\0path.db";

        Assert.True(RestoreDestination.IsSameFile(bad, bad));
        Assert.False(RestoreDestination.IsSameFile(bad, InShare("a.db")));
    }
}
