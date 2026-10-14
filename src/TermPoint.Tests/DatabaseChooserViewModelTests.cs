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

    // ═════════════════════════════════════════════════════════════════════════
    // Restore from backup (spec item 5, step 5c)
    //
    // The destination probe, the Step 2 status, Continue's gating, and the restore flow with FAKE dialogs and a FAKE
    // restore operation (the real one is covered by RestoreIntoTests). Real files in a temp folder stand in for the
    // share and the backups.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Creates a healthy backup file in a "backups" sub-folder of the temp folder and returns its path.</summary>
    /// <param name="name">The backup's file name.</param>
    private string MakeBackup(string name = "dept_2026-10-13_13-00-00.db")
    {
        var folder = Path.Combine(_tempDir, "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        DbFileTestHelpers.CreateHealthyDatabase(path);
        return path;
    }

    /// <summary>The path of "the shared database" in a "share" sub-folder (the folder is created; the file is not).</summary>
    /// <param name="name">The database's file name.</param>
    private string SharePath(string name = "dept.db")
    {
        var folder = Path.Combine(_tempDir, "share");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name);
    }

    /// <summary>
    /// Builds a chooser whose last known database is <paramref name="lastKnown"/>, with the restore option selected
    /// and <paramref name="backup"/> picked, and waits for the destination probe to answer. Call only inside a
    /// <see cref="SettingsFileGuard"/>.
    /// </summary>
    /// <param name="lastKnown">The chooser's last known database path (may be null).</param>
    /// <param name="backup">The backup to pick.</param>
    private async Task<DatabaseChooserViewModel> ChooserWithBackupAsync(string? lastKnown, string backup)
    {
        AppSettings.Current.DatabasePath = null;   // never let the developer's real setting leak into a test
        var vm = BuildChooser(lastKnownPath: lastKnown);
        vm.PickBackupFileAsync = () => Task.FromResult<string?>(backup);
        vm.SelectOptionRestoreCommand.Execute(null);
        await vm.BrowseForBackupCommand.ExecuteAsync(null);
        await vm.RestoreProbeTask;
        return vm;
    }

    /// <summary>A restore operation that records its arguments and returns <paramref name="result"/>.</summary>
    private sealed class FakeRestore
    {
        private readonly RestoreResult _result;
        private readonly Action? _whileRunning;

        /// <summary>Creates the fake.</summary>
        /// <param name="result">What the operation returns.</param>
        /// <param name="whileRunning">Runs while the operation is "in progress" (to observe the view model mid-restore); may be null.</param>
        public FakeRestore(RestoreResult result, Action? whileRunning = null)
        {
            _result       = result;
            _whileRunning = whileRunning;
        }

        /// <summary>How many times the operation was started.</summary>
        public int Calls { get; private set; }

        /// <summary>The backup path of the last call.</summary>
        public string? Backup { get; private set; }

        /// <summary>The destination of the last call.</summary>
        public string? Target { get; private set; }

        /// <summary>The allowReplace argument of the last call.</summary>
        public bool? AllowReplace { get; private set; }

        /// <summary>The operation (assign to <c>RestoreOperation</c>).</summary>
        public async Task<RestoreResult> RunAsync(string backup, string target, bool allowReplace)
        {
            Calls++;
            Backup       = backup;
            Target       = target;
            AllowReplace = allowReplace;
            _whileRunning?.Invoke();
            await Task.Yield();
            return _result;
        }
    }

    // ── Default destination ───────────────────────────────────────────────────

    /// <summary>
    /// Picking a periodic backup of the last database proposes that database's own path — in the shared folder,
    /// not the backups folder — and, because a file is there, the status says it will be replaced and where the
    /// current file is kept.
    /// </summary>
    [Fact]
    public async Task RestoreStep2_BackupOfTheLastDatabase_ProposesThePathAndSaysItReplacesIt()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);

        var vm = await ChooserWithBackupAsync(shared, MakeBackup());

        Assert.Equal(shared, vm.RestorePath);
        Assert.Equal(RestoreDestinationState.Exists, vm.RestoreDestinationState);
        Assert.Equal(
            "This replaces the database at this location for everyone who uses it. " +
            "The current file is kept beside it as \"dept (before restore …)\".",
            vm.RestoreStatusText);
        Assert.False(vm.IsRestoreStatusProblem);
        Assert.True(vm.CanContinue);
        Assert.True(vm.IsBackupStepTwoVisible);
        Assert.Equal("Your backup file will not be modified.", vm.BackupNotModifiedNote);
    }

    /// <summary>When nothing is at the proposed destination the status says a new database will be created, and Continue is enabled.</summary>
    [Fact]
    public async Task RestoreStep2_NothingAtTheDestination_SaysANewDatabaseWillBeCreated()
    {
        using var settingsGuard = new SettingsFileGuard();

        var vm = await ChooserWithBackupAsync(SharePath(), MakeBackup());

        Assert.Equal(RestoreDestinationState.Missing, vm.RestoreDestinationState);
        Assert.Equal("A new database will be created here from the backup.", vm.RestoreStatusText);
        Assert.True(vm.CanContinue);
    }

    /// <summary>A backup of a different database is proposed beside the backup, never onto the last database.</summary>
    [Fact]
    public async Task RestoreStep2_BackupOfADifferentDatabase_IsProposedBesideTheBackup()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath("MATH-TT.db");
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var backup = MakeBackup("BIOL-TT_2026-10-13_13-00-00.db");

        var vm = await ChooserWithBackupAsync(shared, backup);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(backup)!, "BIOL-TT.db"), vm.RestorePath);
        Assert.NotEqual(shared, vm.RestorePath);
    }

    /// <summary>"Change folder…" keeps the derived name and only changes the folder, and the new destination is probed.</summary>
    [Fact]
    public async Task ChangeRestoreFolder_KeepsTheNameAndProbesTheNewDestination()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath(), MakeBackup());
        var elsewhere = Path.Combine(_tempDir, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        DbFileTestHelpers.CreateHealthyDatabase(Path.Combine(elsewhere, "dept.db"));
        vm.PickRestoreFolderAsync = () => Task.FromResult<string?>(elsewhere);
        Assert.Equal(RestoreDestinationState.Missing, vm.RestoreDestinationState);

        await vm.ChangeRestoreFolderCommand.ExecuteAsync(null);
        await vm.RestoreProbeTask;

        Assert.Equal(Path.Combine(elsewhere, "dept.db"), vm.RestorePath);
        Assert.Equal(RestoreDestinationState.Exists, vm.RestoreDestinationState);
    }

    // ── The probe ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A probe whose answer the test releases by hand: the view model is given this as its destination probe, and
    /// each probed path gets a source the test completes when it wants.
    /// </summary>
    private sealed class HeldProbes
    {
        private readonly Dictionary<string, TaskCompletionSource<FileProbeResult>> _held = new();

        /// <summary>The probe to give the view model.</summary>
        /// <param name="path">The path being probed.</param>
        public Task<FileProbeResult> ProbeAsync(string path)
        {
            var source = new TaskCompletionSource<FileProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _held[path] = source;
            return source.Task;
        }

        /// <summary>Releases the answer for <paramref name="path"/>.</summary>
        /// <param name="path">The path that was probed.</param>
        /// <param name="answer">What the probe finds.</param>
        public void Answer(string path, FileProbeResult answer) => _held[path].SetResult(answer);
    }

    /// <summary>
    /// While the probe is out the status reads "Checking this location…" and Continue is disabled; when it answers,
    /// the status and Continue follow.
    /// </summary>
    [Fact]
    public async Task RestoreProbe_ShowsCheckingUntilItAnswers_AndContinueWaits()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath("other.db"), MakeBackup("x_2026-10-13_13-00-00.db"));
        var probes = new HeldProbes();
        vm.DestinationProbe = probes.ProbeAsync;
        var destination = SharePath();

        vm.RestorePath = destination;

        Assert.Equal(RestoreDestinationState.Checking, vm.RestoreDestinationState);
        Assert.Equal("Checking this location…", vm.RestoreStatusText);
        Assert.False(vm.IsRestoreStatusProblem);
        Assert.False(vm.CanContinue);

        probes.Answer(destination, FileProbeResult.Exists);
        await vm.RestoreProbeTask;

        Assert.Equal(RestoreDestinationState.Exists, vm.RestoreDestinationState);
        Assert.True(vm.CanContinue);
    }

    /// <summary>
    /// An answer that arrives after the destination changed again is ignored: two probes are started, the newer
    /// one answers first and the stale one after it, and the state still describes the LATEST destination.
    /// </summary>
    [Fact]
    public async Task RestoreProbe_AStaleAnswer_IsIgnored()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath("other.db"), MakeBackup("x_2026-10-13_13-00-00.db"));
        var probes = new HeldProbes();
        vm.DestinationProbe = probes.ProbeAsync;
        var first = SharePath("first.db");
        var second = SharePath("second.db");

        vm.RestorePath = first;
        var firstProbe = vm.RestoreProbeTask;
        vm.RestorePath = second;
        var secondProbe = vm.RestoreProbeTask;

        probes.Answer(second, FileProbeResult.Missing);   // the answer that counts arrives first…
        await secondProbe;
        probes.Answer(first, FileProbeResult.Exists);     // …and the stale one afterwards
        await firstProbe;

        Assert.Equal(RestoreDestinationState.Missing, vm.RestoreDestinationState);
        Assert.Equal("A new database will be created here from the backup.", vm.RestoreStatusText);
    }

    /// <summary>
    /// Picking the same backup again changes neither the backup nor the proposed destination, yet the destination may
    /// have changed in the meantime: it is probed again, so the status never goes stale.
    /// </summary>
    [Fact]
    public async Task RestoreStep2_PickingTheSameBackupAgain_ProbesTheDestinationAgain()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath(), MakeBackup());
        Assert.Equal(RestoreDestinationState.Missing, vm.RestoreDestinationState);
        DbFileTestHelpers.CreateHealthyDatabase(SharePath());   // someone creates the database meanwhile

        await vm.BrowseForBackupCommand.ExecuteAsync(null);
        await vm.RestoreProbeTask;

        Assert.Equal(RestoreDestinationState.Exists, vm.RestoreDestinationState);
    }

    /// <summary>The probe's three answers map to the three states: present, absent, no answer.</summary>
    [Theory]
    [InlineData(FileProbeResult.Exists, RestoreDestinationState.Exists, true)]
    [InlineData(FileProbeResult.Missing, RestoreDestinationState.Missing, true)]
    [InlineData(FileProbeResult.Unreachable, RestoreDestinationState.Unreachable, false)]
    public async Task RestoreProbe_MapsEachAnswerToAState(FileProbeResult answer, RestoreDestinationState expected, bool canContinue)
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath("other.db"), MakeBackup("x_2026-10-13_13-00-00.db"));
        vm.DestinationProbe = _ => Task.FromResult(answer);

        vm.RestorePath = SharePath();
        await vm.RestoreProbeTask;

        Assert.Equal(expected, vm.RestoreDestinationState);
        Assert.Equal(canContinue, vm.CanContinue);
    }

    /// <summary>
    /// A destination that cannot be probed (an illegal path stands in for an unresponsive share, which the probe
    /// maps the same way) reads "This location isn't responding right now.", shown as a problem, and Continue stays disabled.
    /// </summary>
    [Fact]
    public async Task RestoreProbe_Unreachable_ShowsTheProblem_AndDisablesContinue()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = await ChooserWithBackupAsync(SharePath(), MakeBackup());

        vm.RestorePath = "bad\0path.db";
        await vm.RestoreProbeTask;

        Assert.Equal(RestoreDestinationState.Unreachable, vm.RestoreDestinationState);
        Assert.Equal("This location isn't responding right now.", vm.RestoreStatusText);
        Assert.True(vm.IsRestoreStatusProblem);
        Assert.False(vm.CanContinue);
    }

    /// <summary>A destination that is the backup file itself is refused at once: the explanation is shown as a problem and Continue is disabled.</summary>
    [Fact]
    public async Task RestoreProbe_DestinationIsTheBackup_IsRefused()
    {
        using var settingsGuard = new SettingsFileGuard();
        var backup = MakeBackup();
        var vm = await ChooserWithBackupAsync(SharePath(), backup);

        vm.RestorePath = backup.ToUpperInvariant().Replace(".DB", ".db");

        Assert.Equal(RestoreDestinationState.IsBackupFile, vm.RestoreDestinationState);
        Assert.Equal("That's the backup file itself — choose a different folder.", vm.RestoreStatusText);
        Assert.True(vm.IsRestoreStatusProblem);
        Assert.False(vm.CanContinue);
    }

    // ── Restore flow ──────────────────────────────────────────────────────────

    /// <summary>
    /// Replacing an existing database: the user is asked to confirm (title, the agreed message naming backup, database
    /// and folder, the "Restore" label); the operation then runs with replacing allowed, and WHILE it runs the chooser
    /// is busy (restoring, Continue and Exit unavailable); afterwards "Backup Restored" is shown and the chooser
    /// resolves with the destination.
    /// </summary>
    [Fact]
    public async Task Restore_ReplacingAnExistingDatabase_ConfirmsRestoresAnnouncesAndResolves()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var backup = MakeBackup();
        var vm = await ChooserWithBackupAsync(shared, backup);

        var preRestore = Path.Combine(Path.GetDirectoryName(shared)!, "dept (before restore 2026-10-13 14-05).db");
        bool? busyWhileRunning = null, continueWhileRunning = null;
        var fake = new FakeRestore(new RestoreResult(RestoreOutcome.Restored, preRestore), () =>
        {
            busyWhileRunning     = vm.IsRestoring;
            continueWhileRunning = vm.CanContinue;
            vm.ExitCommand.Execute(null);   // the window disables Exit; the view model ignores it too
        });
        vm.RestoreOperation = fake.RunAsync;

        (string Title, string Message, string Label)? confirmed = null;
        vm.ConfirmAsync = (title, message, label) => { confirmed = (title, message, label); return Task.FromResult(true); };
        (string Title, string Message)? shown = null;
        vm.ShowMessageAsync = (title, message) => { shown = (title, message); return Task.CompletedTask; };
        var closeRequests = 0;
        vm.CloseRequested += () => closeRequests++;

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.NotNull(confirmed);
        Assert.Equal("Restore from Backup", confirmed!.Value.Title);
        Assert.Equal(RestorePrompt.BuildChooserConfirmMessage(Path.GetFileName(backup), shared), confirmed.Value.Message);
        Assert.Equal("Restore", confirmed.Value.Label);

        Assert.Equal(1, fake.Calls);
        Assert.Equal((backup, shared, true), (fake.Backup, fake.Target, fake.AllowReplace!.Value));
        Assert.True(busyWhileRunning);
        Assert.False(continueWhileRunning);
        Assert.False(vm.IsRestoring);

        Assert.Equal("Backup Restored", shown?.Title);
        Assert.Equal(
            "The backup was restored. The database as it was just before the restore is kept beside it as " +
            "\"dept (before restore 2026-10-13 14-05).db\". It will open now.",
            shown?.Message);

        Assert.Equal(ChooserOutcome.Resolved, vm.Outcome);
        Assert.Equal(shared, vm.ResolvedPath);
        Assert.Equal(1, closeRequests);   // only the resolve — the Exit pressed mid-restore did nothing
        Assert.Null(vm.RestoreError);
    }

    /// <summary>Declining the confirmation does nothing: no restore, no lock, the chooser stays open and unresolved.</summary>
    [Fact]
    public async Task Restore_ConfirmationDeclined_DoesNothing()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var vm = await ChooserWithBackupAsync(shared, MakeBackup());
        var fake = new FakeRestore(new RestoreResult(RestoreOutcome.Restored));
        vm.RestoreOperation = fake.RunAsync;
        vm.ConfirmAsync = (_, _, _) => Task.FromResult(false);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.Calls);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
        Assert.Null(vm.ResolvedPath);
        Assert.False(vm.IsRestoring);
        Assert.Null(vm.RestoreError);
    }

    /// <summary>With no way to ask the user, an existing database is never replaced silently.</summary>
    [Fact]
    public async Task Restore_NoConfirmationCallback_NeverReplaces()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var vm = await ChooserWithBackupAsync(shared, MakeBackup());
        var fake = new FakeRestore(new RestoreResult(RestoreOutcome.Restored));
        vm.RestoreOperation = fake.RunAsync;
        vm.ConfirmAsync = null;

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.Calls);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
    }

    /// <summary>
    /// Creating a new database needs no confirmation and shows no dialog: the operation runs with replacing NOT
    /// allowed (so a database that appears meanwhile is refused, not replaced unconfirmed) and the chooser resolves.
    /// </summary>
    [Fact]
    public async Task Restore_CreatingANewDatabase_AsksNothing_ShowsNothing_AndResolves()
    {
        using var settingsGuard = new SettingsFileGuard();
        var target = SharePath();
        var vm = await ChooserWithBackupAsync(target, MakeBackup());
        var fake = new FakeRestore(new RestoreResult(RestoreOutcome.Created, CreatePath: true));
        vm.RestoreOperation = fake.RunAsync;
        var asked = false;
        var shown = false;
        vm.ConfirmAsync = (_, _, _) => { asked = true; return Task.FromResult(true); };
        vm.ShowMessageAsync = (_, _) => { shown = true; return Task.CompletedTask; };

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.False(shown);
        Assert.Equal(false, fake.AllowReplace);
        Assert.Equal(target, fake.Target);
        Assert.Equal(ChooserOutcome.Resolved, vm.Outcome);
        Assert.Equal(target, vm.ResolvedPath);
        Assert.False(vm.IsRestoring);
    }

    /// <summary>
    /// A refused restore stays in the chooser with the message shown inline (and the controls available again): here
    /// the person editing is named. The destination is probed again afterwards.
    /// </summary>
    [Fact]
    public async Task Restore_LockHeld_ShowsTheHolderInline_AndStaysInTheChooser()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var vm = await ChooserWithBackupAsync(shared, MakeBackup());
        var holder = new TermPoint.Models.LockFileData { Username = "jsmith", Machine = "LAPTOP-42" };
        var result = new RestoreResult(RestoreOutcome.LockHeld, Holder: holder);
        vm.RestoreOperation = new FakeRestore(result).RunAsync;
        vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        var shown = false;
        vm.ShowMessageAsync = (_, _) => { shown = true; return Task.CompletedTask; };

        await vm.ContinueCommand.ExecuteAsync(null);
        await vm.RestoreProbeTask;

        Assert.Equal(RestorePrompt.Describe(result, RestoreContext.Chooser).Message, vm.RestoreError);
        Assert.StartsWith("jsmith on LAPTOP-42 is editing this database", vm.RestoreError);
        Assert.False(shown);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
        Assert.False(vm.IsRestoring);
        Assert.Equal(RestoreDestinationState.Exists, vm.RestoreDestinationState);
        Assert.True(vm.CanContinue);
    }

    /// <summary>An unconfirmed rename is shown inline and the chooser stays: the database must not be opened on a guess.</summary>
    [Fact]
    public async Task Restore_RenameUnconfirmed_IsShownInline_AndDoesNotResolve()
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        DbFileTestHelpers.CreateHealthyDatabase(shared);
        var vm = await ChooserWithBackupAsync(shared, MakeBackup());
        var preRestore = Path.Combine(Path.GetDirectoryName(shared)!, "dept (before restore 2026-10-13 14-05).db");
        vm.RestoreOperation = new FakeRestore(new RestoreResult(RestoreOutcome.RenameUnconfirmed, preRestore)).RunAsync;
        vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);
        var shown = false;
        vm.ShowMessageAsync = (_, _) => { shown = true; return Task.CompletedTask; };

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Contains("can't tell whether the restore finished", vm.RestoreError);
        Assert.Contains("\"dept (before restore 2026-10-13 14-05).db\"", vm.RestoreError);
        Assert.False(shown);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
        Assert.False(vm.IsRestoring);
    }

    /// <summary>On the create path a failure says "Nothing was created."; on the replace path "Your database is unchanged."</summary>
    [Theory]
    [InlineData(false, "Missing", "Nothing was created.")]
    [InlineData(true, "Exists", "Your database is unchanged.")]
    public async Task Restore_Failure_UsesTheWordingOfItsPath(bool destinationExists, string expectedState, string expectedSentence)
    {
        using var settingsGuard = new SettingsFileGuard();
        var shared = SharePath();
        if (destinationExists) DbFileTestHelpers.CreateHealthyDatabase(shared);
        var vm = await ChooserWithBackupAsync(shared, MakeBackup());
        Assert.Equal(expectedState, vm.RestoreDestinationState.ToString());
        vm.RestoreOperation = new FakeRestore(new RestoreResult(RestoreOutcome.BackupDamaged, CreatePath: !destinationExists)).RunAsync;
        vm.ConfirmAsync = (_, _, _) => Task.FromResult(true);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Contains("That backup is damaged", vm.RestoreError);
        Assert.EndsWith(expectedSentence, vm.RestoreError);
        Assert.Equal(ChooserOutcome.None, vm.Outcome);
    }

    /// <summary>The Exit command does nothing while a restore runs, and works again afterwards.</summary>
    [Fact]
    public async Task Exit_IsIgnoredWhileRestoring_AndWorksAfterwards()
    {
        using var settingsGuard = new SettingsFileGuard();
        var vm = BuildChooser();
        var closes = 0;
        vm.CloseRequested += () => closes++;

        vm.IsRestoring = true;
        vm.ExitCommand.Execute(null);
        Assert.Equal(0, closes);

        vm.IsRestoring = false;
        vm.ExitCommand.Execute(null);
        Assert.Equal(1, closes);
        await Task.CompletedTask;
    }

    /// <summary>Nothing can be started while a restore runs: Continue is disabled whatever is selected.</summary>
    [Fact]
    public void CanContinue_IsFalseWhileRestoring()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        var vm = BuildChooser([path]);
        vm.SelectedRecentDatabase = vm.RecentDatabases[0];
        Assert.True(vm.CanContinue);

        vm.IsRestoring = true;

        Assert.False(vm.CanContinue);
    }
}
