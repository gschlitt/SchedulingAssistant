using System.Security.Cryptography;
using TermPoint.Services;
using TermPoint.ViewModels.Wizard;
using TermPoint.ViewModels.Wizard.Steps;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the wizard's "I already have a database" (join) path — step 1a — after spec item 4a:
/// the step must only VALIDATE the shared database file D. It must never open D with SQLite (which
/// rewrote the journal-mode header, so D's hash changed with no data written and the active
/// editor's next save failed "modified outside this session"), never initialise services against D,
/// and never take the write lock — the one real checkout happens afterwards, in the host's
/// <c>SwitchDatabaseAsync</c>.
///
/// <para>Uses a real database file (created with <see cref="NewDatabaseFile"/>) in a temp folder and
/// the shared wizard test services from <see cref="WizardRoutingTests.BuildTestServices"/>. Step 1a
/// persists <see cref="AppSettings"/>, so each test wraps itself in a <see cref="SettingsFileGuard"/>.</para>
/// </summary>
public sealed class WizardExistingDbJoinTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _localBuildDir;

    /// <summary>Creates isolated folders for the "shared" database and the helper's local build.</summary>
    public WizardExistingDbJoinTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _tempDir       = Path.Combine(Path.GetTempPath(), $"wiz_join_{id}");
        _localBuildDir = Path.Combine(Path.GetTempPath(), $"wiz_join_local_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_localBuildDir);
    }

    /// <summary>Removes both folders (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_localBuildDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>SHA-256 of a file's bytes, read with sharing (the hash CheckoutService compares).</summary>
    private static string HashOf(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    /// <summary>Creates a real TermPoint database file "shared.db" and returns its path.</summary>
    private async Task<string> CreateSharedDatabaseAsync()
    {
        var path = Path.Combine(_tempDir, "shared.db");
        var result = await NewDatabaseFile.CreateAsync(path, _localBuildDir);
        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        return path;
    }

    /// <summary>
    /// Builds a wizard on the standard test services, advanced to step 1a (existing-DB check), and
    /// records how often <c>InitializeServices</c> is called.
    /// </summary>
    /// <param name="initializeCalls">Incremented on every <c>InitializeServices</c> call.</param>
    /// <returns>The wizard and its step-1a view model.</returns>
    private static async Task<(StartupWizardViewModel Wizard, Step1aExistingDbViewModel Step, Func<int> InitCalls)>
        WizardAtStep1aAsync()
    {
        var initCalls = 0;
        var services  = WizardRoutingTests.BuildTestServices() with { InitializeServices = _ => initCalls++ };
        var wizard    = new StartupWizardViewModel(null!, services);

        await wizard.NextCommand.ExecuteAsync(null);   // welcome → license
        await wizard.NextCommand.ExecuteAsync(null);   // license → step 1a
        var step = Assert.IsType<Step1aExistingDbViewModel>(wizard.CurrentStep);
        return (wizard, step, () => initCalls);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The core guarantee. Joining an existing database leaves D byte-for-byte identical (so its
    /// hash — which the active editor compares on every save — does not change), creates no
    /// lock/WAL/journal files beside it, builds no services against it and takes no lock; yet the
    /// wizard completes and the settings point at D so the host can check it out.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Step1a_JoinExistingDb_DoesNotOpenOrChangeD(bool readerMode)
    {
        using var settingsGuard = new SettingsFileGuard();
        var db = await CreateSharedDatabaseAsync();
        var hashBefore = HashOf(db);
        var (wizard, step, initCalls) = await WizardAtStep1aAsync();

        step.IsExistingDbChoice = true;
        step.DbPath             = db;
        step.BackupFolder       = _tempDir;
        step.OpenInReaderMode   = readerMode;
        await wizard.NextCommand.ExecuteAsync(null);

        Assert.True(wizard.IsComplete, $"Wizard should finish. Error: {step.ErrorMessage}");
        Assert.Equal(string.Empty, step.ErrorMessage);
        Assert.Equal(hashBefore, HashOf(db));                                   // D untouched
        Assert.Equal(new[] { db }, Directory.GetFileSystemEntries(_tempDir));   // no .lock, -wal, -shm, -journal
        Assert.Equal(0, initCalls());                                           // no services built on D
        Assert.False(App.LockService.IsWriter);                                 // no lock taken

        Assert.Equal(db, AppSettings.Current.DatabasePath);
        Assert.Equal(readerMode, AppSettings.Current.OpenInReaderMode);
        Assert.True(AppSettings.Current.IsInitialSetupComplete);
    }

    /// <summary>
    /// A file that is not a SQLite database is rejected with a clear message, the wizard stays on
    /// step 1a, and the file is left alone.
    /// </summary>
    [Fact]
    public async Task Step1a_NonSqliteFile_ShowsErrorAndDoesNotFinish()
    {
        using var settingsGuard = new SettingsFileGuard();
        var notDb = Path.Combine(_tempDir, "notes.db");
        File.WriteAllText(notDb, "this is definitely not a SQLite database");
        var (wizard, step, initCalls) = await WizardAtStep1aAsync();

        step.IsExistingDbChoice = true;
        step.DbPath             = notDb;
        await wizard.NextCommand.ExecuteAsync(null);

        Assert.False(wizard.IsComplete);
        Assert.Same(step, wizard.CurrentStep);
        Assert.Equal("That file does not appear to be a valid database — please try another.", step.ErrorMessage);
        Assert.Equal(0, initCalls());
        Assert.Equal("this is definitely not a SQLite database", File.ReadAllText(notDb));
    }

    /// <summary>A path where no file exists is rejected with the existing wording and nothing is created.</summary>
    [Fact]
    public async Task Step1a_MissingFile_ShowsErrorAndDoesNotFinish()
    {
        using var settingsGuard = new SettingsFileGuard();
        var missing = Path.Combine(_tempDir, "nope.db");
        var (wizard, step, initCalls) = await WizardAtStep1aAsync();

        step.IsExistingDbChoice = true;
        step.DbPath             = missing;
        await wizard.NextCommand.ExecuteAsync(null);

        Assert.False(wizard.IsComplete);
        Assert.Same(step, wizard.CurrentStep);
        Assert.Equal("Please choose an existing database file.", step.ErrorMessage);
        Assert.Equal(0, initCalls());
        Assert.False(File.Exists(missing));
        Assert.Empty(Directory.GetFileSystemEntries(_tempDir));
    }
}
