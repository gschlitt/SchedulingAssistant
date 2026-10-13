using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="DatabaseSwitchPrecheck"/> (spec item 4e, hand-test finding): the decision
/// made at the very start of <c>MainWindow.SwitchDatabaseAsync</c>, BEFORE anything is torn down.
///
/// <para>The reported failures: opening a held-open database from the main window's Recent… menu
/// released the current database first and then failed with a technical message, leaving a leaked
/// write lock; and a Recent… entry whose file had moved silently created and opened an EMPTY
/// database at the old path. Now only File → New (<c>createIfMissing: true</c>) may create, and every
/// other target is validated first with the same validator the chooser uses.</para>
///
/// <para>The window itself is UI-bound and not constructed here; everything it decides lives in
/// <see cref="DatabaseSwitchPrecheck"/>, including the "update the settings and recent list only when
/// the target passed" rule (<see cref="DatabaseSwitchPrecheck.CheckAndRecordAsync"/>). Tests that touch
/// <see cref="AppSettings.Current"/> wrap themselves in a <see cref="SettingsFileGuard"/>. Held-open
/// files use <c>FileShare.None</c>, so those tests are Windows-only.</para>
/// </summary>
public sealed class DatabaseSwitchPrecheckTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public DatabaseSwitchPrecheckTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"switch_precheck_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>Full path of <paramref name="name"/> inside the temp directory.</summary>
    private string PathOf(string name) => Path.Combine(_tempDir, name);

    /// <summary>Creates a healthy database at <paramref name="name"/> and returns its path.</summary>
    private string HealthyDb(string name = "healthy.db")
    {
        var path = PathOf(name);
        DbFileTestHelpers.CreateHealthyDatabase(path);
        return path;
    }

    // ── Decide: the pure mapping ──────────────────────────────────────────────

    /// <summary>
    /// Opening an existing database (not File → New): an Ok database proceeds.
    /// </summary>
    [Fact]
    public void Decide_Open_Ok_Proceeds()
    {
        var result = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Ok, @"C:\db\x.db", createIfMissing: false);

        Assert.True(result.CanProceed);
    }

    /// <summary>
    /// A missing file is refused with the "not found" wording — the old behaviour was to create an empty
    /// database there — unless the caller is File → New, for which "missing" means "create it".
    /// </summary>
    [Fact]
    public void Decide_Missing_RefusesUnlessCreating()
    {
        var path = @"C:\db\moved.db";

        var refused = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Missing, path, createIfMissing: false);
        var create  = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Missing, path, createIfMissing: true);

        Assert.False(refused.CanProceed);
        Assert.Equal("Database Not Found", refused.Title);
        Assert.Equal(
            "The database file could not be found:\nC:\\db\\moved.db\n\nIt may have been moved, renamed or deleted.",
            refused.Message);
        Assert.True(create.CanProceed);
    }

    /// <summary>
    /// Opening: a held-open / unreadable file is refused with the chooser's "can't read it right now"
    /// wording — never "damaged".
    /// </summary>
    [Fact]
    public void Decide_Open_Unreadable_RefusesWithRetryWording_NotDamaged()
    {
        var result = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Unreadable, @"C:\db\x.db", createIfMissing: false);

        Assert.False(result.CanProceed);
        Assert.Equal("Database Can't Be Read", result.Title);
        Assert.Equal(
            "TermPoint can't read that database right now. Another program may have it open, " +
            "or you may not have permission to read it. Try again in a moment.",
            result.Message);
        Assert.Equal(DatabaseChooserViewModel.UnreadableDatabaseMessage, result.Message);
        Assert.DoesNotContain("damaged", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opening: only a genuine integrity failure says "damaged".</summary>
    [Fact]
    public void Decide_Open_Corrupt_RefusesWithDamagedWording()
    {
        var result = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Corrupt, @"C:\db\x.db", createIfMissing: false);

        Assert.False(result.CanProceed);
        Assert.Equal("Database Integrity Check Failed", result.Title);
        Assert.Equal("That database failed its integrity check and may be damaged.", result.Message);
    }

    /// <summary>
    /// File → New must never adopt an existing file: a file that is already there — healthy, damaged or
    /// merely held open — is refused with the "already exists" wording, naming the path. (The
    /// configuration transfer that follows a switch would otherwise insert duplicates into it.)
    /// </summary>
    [Theory]
    [InlineData(DatabaseValidationResult.Ok)]
    [InlineData(DatabaseValidationResult.Corrupt)]
    [InlineData(DatabaseValidationResult.Unreadable)]
    public void Decide_Creating_ExistingFile_RefusesAsAlreadyExists(DatabaseValidationResult validation)
    {
        var result = DatabaseSwitchPrecheck.Decide(validation, @"C:\db\taken.db", createIfMissing: true);

        Assert.False(result.CanProceed);
        Assert.Equal("Database Already Exists", result.Title);
        Assert.Equal(
            "A database file already exists at:\nC:\\db\\taken.db\n\n" +
            "It was not changed. Choose a different name, or open that database instead.",
            result.Message);
    }

    /// <summary>
    /// The "already exists" text is one constant: the pre-check and <c>SwitchDatabaseAsync</c>'s
    /// "created while we were building ours" path both use it.
    /// </summary>
    [Fact]
    public void AlreadyExistsMessage_IsSharedAndNamesThePath()
    {
        Assert.Equal(
            "A database file already exists at:\nC:\\db\\a.db\n\nIt was not changed. Choose a different name, or open that database instead.",
            DatabaseSwitchPrecheck.AlreadyExistsMessage(@"C:\db\a.db"));
        Assert.Equal("Database Already Exists", DatabaseSwitchPrecheck.AlreadyExistsTitle);
    }


    /// <summary>An unreachable location is refused with the standard network message.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_Unreachable_RefusesWithNetworkMessage(bool createIfMissing)
    {
        var result = DatabaseSwitchPrecheck.Decide(DatabaseValidationResult.Unreachable, @"\\server\share\x.db", createIfMissing);

        Assert.False(result.CanProceed);
        Assert.Equal("Network Unreachable", result.Title);
        Assert.Equal(NetworkFileOps.UnreachableMessage, result.Message);
    }

    // ── RunAsync: the real validator on real files ────────────────────────────

    /// <summary>A healthy database proceeds.</summary>
    [Fact]
    public async Task Run_HealthyDatabase_Proceeds()
    {
        var result = await DatabaseSwitchPrecheck.RunAsync(HealthyDb(), createIfMissing: false);

        Assert.True(result.CanProceed);
    }

    /// <summary>
    /// A path where nothing exists (a moved/deleted Recent… entry) is refused — and NOT created. Only
    /// File → New (createIfMissing) proceeds, and even then this check itself creates nothing.
    /// </summary>
    [Fact]
    public async Task Run_MissingFile_RefusedUnlessCreating_AndNothingIsCreated()
    {
        var path = PathOf("moved.db");

        var refused = await DatabaseSwitchPrecheck.RunAsync(path, createIfMissing: false);
        var create  = await DatabaseSwitchPrecheck.RunAsync(path, createIfMissing: true);

        Assert.False(refused.CanProceed);
        Assert.Equal(DatabaseSwitchPrecheck.MissingMessage(path), refused.Message);
        Assert.True(create.CanProceed);
        Assert.Empty(Directory.GetFileSystemEntries(_tempDir));
    }

    /// <summary>A file with a trashed page is refused as damaged.</summary>
    [Fact]
    public async Task Run_CorruptFile_RefusedAsDamaged()
    {
        var path = HealthyDb("corrupt.db");
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);

        var result = await DatabaseSwitchPrecheck.RunAsync(path, createIfMissing: false);

        Assert.False(result.CanProceed);
        Assert.Equal(DatabaseChooserViewModel.DamagedDatabaseMessage, result.Message);
    }

    /// <summary>
    /// THE BUG at the front door: a healthy database another program holds exclusively is refused with
    /// the Unreadable wording (not "damaged"), the bytes are untouched, and after release it proceeds.
    /// </summary>
    [FactWindowsOnly]
    public async Task Run_HeldOpenDatabase_RefusedAsUnreadable_ThenProceedsAfterRelease()
    {
        var path = HealthyDb();
        var before = DbFileTestHelpers.HashOf(path);

        using (DbFileTestHelpers.LockExclusively(path))
        {
            var result = await DatabaseSwitchPrecheck.RunAsync(path, createIfMissing: false);

            Assert.False(result.CanProceed);
            Assert.Equal(DatabaseChooserViewModel.UnreadableDatabaseMessage, result.Message);
            Assert.DoesNotContain("damaged", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, DbFileTestHelpers.HashOf(path));
        Assert.True((await DatabaseSwitchPrecheck.RunAsync(path, createIfMissing: false)).CanProceed);
    }

    // ── CheckAndRecordAsync: settings change only when the target passed ──────

    /// <summary>Puts known values into the in-memory settings so a later change is detectable.</summary>
    private static (string Path, List<string> Recents) SeedSettings()
    {
        const string baseline = @"C:\keep\current.db";
        AppSettings.Current.DatabasePath = baseline;
        AppSettings.Current.RecentDatabases = new List<string> { baseline, @"C:\keep\other.db" };
        return (baseline, AppSettings.Current.RecentDatabases.ToList());
    }

    /// <summary>
    /// A refused switch (missing file) leaves <see cref="AppSettings.DatabasePath"/> and the recent
    /// list exactly as they were — the old code wrote both before checking anything, so a dead Recent…
    /// entry became "the current database" and was re-promoted to the top of the list.
    /// </summary>
    [Fact]
    public async Task CheckAndRecord_MissingFile_LeavesSettingsAndRecentsUntouched()
    {
        using var settingsGuard = new SettingsFileGuard();
        var (baselinePath, baselineRecents) = SeedSettings();

        var result = await DatabaseSwitchPrecheck.CheckAndRecordAsync(
            PathOf("moved.db"), createIfMissing: false, AppSettings.Current);

        Assert.False(result.CanProceed);
        Assert.Equal(baselinePath, AppSettings.Current.DatabasePath);
        Assert.Equal(baselineRecents, AppSettings.Current.RecentDatabases);
    }

    /// <summary>A refused switch (held-open database) likewise changes no setting.</summary>
    [FactWindowsOnly]
    public async Task CheckAndRecord_HeldOpenDatabase_LeavesSettingsAndRecentsUntouched()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        var (baselinePath, baselineRecents) = SeedSettings();

        using (DbFileTestHelpers.LockExclusively(path))
        {
            var result = await DatabaseSwitchPrecheck.CheckAndRecordAsync(path, createIfMissing: false, AppSettings.Current);

            Assert.False(result.CanProceed);
        }

        Assert.Equal(baselinePath, AppSettings.Current.DatabasePath);
        Assert.Equal(baselineRecents, AppSettings.Current.RecentDatabases);
    }

    /// <summary>
    /// A passing target is recorded: it becomes <see cref="AppSettings.DatabasePath"/> and moves to the
    /// front of the recent list (normalised, as <see cref="AppSettings.AddRecentDatabase"/> does).
    /// </summary>
    [Fact]
    public async Task CheckAndRecord_HealthyDatabase_RecordsItAsCurrentAndMostRecent()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = HealthyDb();
        SeedSettings();

        var result = await DatabaseSwitchPrecheck.CheckAndRecordAsync(path, createIfMissing: false, AppSettings.Current);

        Assert.True(result.CanProceed);
        Assert.Equal(path, AppSettings.Current.DatabasePath);
        Assert.Equal(Path.GetFullPath(path), AppSettings.Current.RecentDatabases[0]);
    }

    /// <summary>File → New: a missing target proceeds and is recorded (the caller then creates it).</summary>
    [Fact]
    public async Task CheckAndRecord_MissingFile_WhenCreating_ProceedsAndRecords()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = PathOf("brand-new.db");
        SeedSettings();

        var result = await DatabaseSwitchPrecheck.CheckAndRecordAsync(path, createIfMissing: true, AppSettings.Current);

        Assert.True(result.CanProceed);
        Assert.Equal(path, AppSettings.Current.DatabasePath);
        Assert.False(File.Exists(path), "The pre-check itself must not create the file.");
    }
}
