using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for how <see cref="DatabaseChooserViewModel"/> reports a database that can't be read
/// right now (spec item 4e). A healthy file that another program holds open used to be shown as
/// "failed its integrity check and may be damaged", inviting the user to restore a backup over a
/// good, current database. It must now get its own wording, and only a genuinely corrupt file may
/// be called damaged.
///
/// <para>Real files in a temp folder are validated through the real
/// <see cref="DatabaseValidator"/> — nothing is mocked and no real database is touched. The
/// view model reads and (for the "always open recent" checkbox) writes
/// <see cref="AppSettings.Current"/>, so every test wraps itself in a
/// <see cref="SettingsFileGuard"/> that restores the developer's real settings file.</para>
///
/// <para>Held-open files use <c>FileShare.None</c>, so those tests are Windows-only
/// (<see cref="FactWindowsOnlyAttribute"/>). The startup-mapping tests
/// (<see cref="DatabaseChooserViewModel.RecoveryReasonFor"/>) are pure and run everywhere.</para>
/// </summary>
public sealed class DatabaseChooserViewModelTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public DatabaseChooserViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"chooser_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a healthy multi-page database file in the temp folder and returns its path.</summary>
    private string HealthyDb(string name = "healthy.db")
    {
        var path = Path.Combine(_tempDir, name);
        DbFileTestHelpers.CreateHealthyDatabase(path);
        return path;
    }

    /// <summary>
    /// Builds a chooser whose recent list is exactly <paramref name="recentPaths"/>. Call only inside
    /// a <see cref="SettingsFileGuard"/>: it edits the in-memory settings (never saved) and the
    /// view model's constructor may save the "always open recent" flag.
    /// </summary>
    /// <param name="recentPaths">The recent-database paths, most recent first (null = none).</param>
    /// <param name="mode">Normal chooser or recovery.</param>
    /// <param name="reason">Recovery reason (recovery mode only).</param>
    /// <param name="lastKnownPath">The path the recovery banner is about.</param>
    private static DatabaseChooserViewModel BuildChooser(
        IEnumerable<string>? recentPaths = null,
        ChooserMode mode = ChooserMode.Normal,
        RecoveryReason? reason = null,
        string? lastKnownPath = null)
    {
        AppSettings.Current.AlwaysOpenMostRecentDatabase = false;
        AppSettings.Current.RecentDatabases = (recentPaths ?? []).ToList();
        return new DatabaseChooserViewModel(mode, reason, lastKnownPath);
    }

    // ── Open recent (double-click) ────────────────────────────────────────────

    /// <summary>
    /// Double-clicking a recent database that another program holds open shows the Unreadable
    /// wording — not "damaged" — and does not resolve; after the lock is released the same item opens.
    /// </summary>
    [FactWindowsOnly]
    public async Task OpenRecent_LockedFile_ShowsUnreadableMessage_NotDamaged()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        var vm = BuildChooser([path]);
        var item = vm.RecentDatabases[0];

        using (DbFileTestHelpers.LockExclusively(path))
        {
            await vm.OpenRecentDatabaseCommand.ExecuteAsync(item);

            Assert.Equal(DatabaseChooserViewModel.UnreadableDatabaseMessage, vm.RecentError);
            Assert.DoesNotContain("damaged", vm.RecentError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("integrity", vm.RecentError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ChooserOutcome.None, vm.Outcome);
            Assert.Null(vm.ResolvedPath);
        }

        await vm.OpenRecentDatabaseCommand.ExecuteAsync(item);

        Assert.Null(vm.RecentError);
        Assert.Equal(ChooserOutcome.Resolved, vm.Outcome);
        Assert.Equal(path, vm.ResolvedPath);
    }

    /// <summary>A genuinely corrupt recent database is still called damaged, with the existing wording.</summary>
    [Fact]
    public async Task OpenRecent_CorruptFile_StillSaysDamaged()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb("corrupt.db");
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);
        var vm = BuildChooser([path]);

        await vm.OpenRecentDatabaseCommand.ExecuteAsync(vm.RecentDatabases[0]);

        Assert.Equal("That database failed its integrity check and may be damaged.", vm.RecentError);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
    }

    /// <summary>A recent database whose file has gone is still "could not be found" (Unreadable didn't swallow it).</summary>
    [Fact]
    public async Task OpenRecent_MissingFile_StillSaysNotFound()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = BuildChooser([Path.Combine(_tempDir, "gone.db")]);

        await vm.OpenRecentDatabaseCommand.ExecuteAsync(vm.RecentDatabases[0]);

        Assert.Equal("That database could not be found — it may have been moved or deleted.", vm.RecentError);
    }

    // ── Continue button ───────────────────────────────────────────────────────

    /// <summary>
    /// Selecting a locked recent database and pressing Open/Continue shows the same Unreadable
    /// wording as double-click, not "damaged".
    /// </summary>
    [FactWindowsOnly]
    public async Task Continue_LockedRecentFile_ShowsUnreadableMessage_NotDamaged()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        var vm = BuildChooser([path]);
        vm.SelectedRecentDatabase = vm.RecentDatabases[0];

        using (DbFileTestHelpers.LockExclusively(path))
        {
            await vm.ContinueCommand.ExecuteAsync(null);

            Assert.Equal(DatabaseChooserViewModel.UnreadableDatabaseMessage, vm.RecentError);
            Assert.DoesNotContain("damaged", vm.RecentError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ChooserOutcome.None, vm.Outcome);
        }

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(ChooserOutcome.Resolved, vm.Outcome);
        Assert.Equal(path, vm.ResolvedPath);
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Browsing to a locked file shows the Unreadable wording (not "does not appear to be a valid
    /// database") and does not accept it; browsing to it again after release accepts it.
    /// </summary>
    [FactWindowsOnly]
    public async Task Browse_LockedFile_ShowsUnreadableMessage_NotInvalid()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        var vm = BuildChooser();
        vm.PickDatabaseFileAsync = () => Task.FromResult<string?>(path);

        using (DbFileTestHelpers.LockExclusively(path))
        {
            await vm.BrowseForDatabaseCommand.ExecuteAsync(null);

            Assert.Equal(DatabaseChooserViewModel.UnreadableDatabaseMessage, vm.BrowseError);
            Assert.DoesNotContain("valid database", vm.BrowseError, StringComparison.OrdinalIgnoreCase);
            Assert.Null(vm.BrowsedPath);
        }

        await vm.BrowseForDatabaseCommand.ExecuteAsync(null);

        Assert.Null(vm.BrowseError);
        Assert.Equal(path, vm.BrowsedPath);
    }

    // ── Restore from backup ───────────────────────────────────────────────────

    /// <summary>
    /// Picking a backup another program has open shows the dedicated backup wording and does not
    /// reveal step 2; a genuinely bad backup keeps the existing "could not be opened as a valid
    /// database backup" message.
    /// </summary>
    [FactWindowsOnly]
    public async Task RestorePick_LockedBackup_ShowsUnreadableBackupMessage()
    {
        using var settingsGuard = new SettingsFileGuard();
        var locked = HealthyDb("backup-locked.db");
        var corrupt = HealthyDb("backup-corrupt.db");
        DbFileTestHelpers.TrashPage(corrupt, pageNumber: 2);
        var vm = BuildChooser();
        string? picked = locked;
        vm.PickBackupFileAsync = () => Task.FromResult(picked);

        using (DbFileTestHelpers.LockExclusively(locked))
        {
            await vm.BrowseForBackupCommand.ExecuteAsync(null);

            Assert.Equal(DatabaseChooserViewModel.UnreadableBackupMessage, vm.RestoreError);
            Assert.False(vm.IsBackupStepTwoVisible);
            Assert.Null(vm.BackupPath);
        }

        picked = corrupt;
        await vm.BrowseForBackupCommand.ExecuteAsync(null);

        Assert.Equal("That file could not be opened as a valid database backup.", vm.RestoreError);
        Assert.False(vm.IsBackupStepTwoVisible);
    }

    // ── Startup recovery mapping ──────────────────────────────────────────────

    /// <summary>
    /// The mapping both startup branches in <c>MainWindow</c> use: Unreadable gets its own reason and
    /// is never folded into Corrupt or NotFound; the other results keep their existing reasons.
    /// </summary>
    [Theory]
    [InlineData(DatabaseValidationResult.Unreadable, RecoveryReason.Unreadable)]
    [InlineData(DatabaseValidationResult.Corrupt, RecoveryReason.Corrupt)]
    [InlineData(DatabaseValidationResult.Unreachable, RecoveryReason.Unreachable)]
    [InlineData(DatabaseValidationResult.Missing, RecoveryReason.NotFound)]
    public void RecoveryReasonFor_MapsEachValidationResult(DatabaseValidationResult validation, RecoveryReason expected)
    {
        Assert.Equal(expected, DatabaseChooserViewModel.RecoveryReasonFor(validation));
    }

    /// <summary>
    /// The startup banner for an Unreadable most-recent database is the exact agreed wording, says
    /// nothing about damage or integrity, and the path stays in the recent list so the user can retry it.
    /// </summary>
    [Fact]
    public void RecoveryBanner_Unreadable_HasOwnWording_AndKeepsPathInRecentList()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = Path.Combine(_tempDir, "recent.db");

        var vm = BuildChooser([path], ChooserMode.Recovery, RecoveryReason.Unreadable, path);

        Assert.Equal(
            "TermPoint couldn't read your most recently used database just now. " +
            "Another program may have it open (for example a backup or sync tool), " +
            "or you may not have permission to read it. " +
            "Try again in a moment, or choose another database.",
            vm.ProblemDescription);
        Assert.DoesNotContain("damaged", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("integrity", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.RecentDatabases, r => r.Path == path);
    }

    /// <summary>
    /// Contrast with Unreachable, which does hide the known-bad path from the recent list (the
    /// banner already names it): that behaviour is unchanged.
    /// </summary>
    [Fact]
    public void RecoveryBanner_Unreachable_StillHidesPathFromRecentList()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = Path.Combine(_tempDir, "recent.db");

        var vm = BuildChooser([path], ChooserMode.Recovery, RecoveryReason.Unreachable, path);

        Assert.DoesNotContain(vm.RecentDatabases, r => r.Path == path);
    }
}
