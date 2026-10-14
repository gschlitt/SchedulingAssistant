using Moq;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the restore gating of <see cref="SaveAndBackupViewModel"/> (spec item 5, step 5b): Restore is
/// available to the write-lock holder only — the command's CanExecute follows the lock, and the command itself
/// refuses a non-writer (second line of defence) without ever showing the confirmation or calling back. Also
/// the pure naming helper behind the confirmation text.
///
/// <para>Settings are touched in memory only, inside a <see cref="SettingsFileGuard"/>, with the backup folder
/// cleared so the view model's constructor lists nothing from the developer's real backup folder.</para>
/// </summary>
public sealed class SaveAndBackupViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>No-op logger: the backup service needs one, and nothing here should write a log file.</summary>
    private sealed class NullLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }

    /// <summary>
    /// Builds the view model over a real <see cref="WriteLockService"/> (not a writer until a test says so),
    /// a backup service with no repositories (never used here) and a mocked dialog service.
    /// </summary>
    /// <param name="lockService">The lock service the view model watches.</param>
    /// <param name="dialog">The mocked dialog service, so a test can see what was shown.</param>
    private static SaveAndBackupViewModel Build(out WriteLockService lockService, out Mock<IDialogService> dialog)
    {
        var db = new Mock<IDatabaseContext>();
        db.SetupGet(d => d.DatabasePath).Returns("unused.db");
        var backup = new BackupService(db.Object, null!, null!, null!, null!, null!, null!, null!, null!, new NullLogger());

        lockService = new WriteLockService();
        dialog = new Mock<IDialogService>();
        return new SaveAndBackupViewModel(backup, dialog.Object, lockService);
    }

    /// <summary>A backup entry as the panel lists it.</summary>
    private static BackupEntryViewModel Entry(SaveAndBackupViewModel vm)
        => new(new BackupEntry { DbPath = Path.Combine(Path.GetTempPath(), "dept_2026-10-13_13-00-00.db"), Timestamp = new DateTime(2026, 10, 13, 13, 0, 0) },
               vm.RestoreCommand);

    // ── CanExecute follows the lock ───────────────────────────────────────────

    /// <summary>
    /// A session that is not the writer (a reader, or a read-only licence) cannot restore: the command and so
    /// every row's button is disabled, and the panel reports restore as unavailable.
    /// </summary>
    [Fact]
    public void RestoreCommand_WhenNotTheWriter_CannotExecute()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = null;

        var vm = Build(out var lockService, out _);

        Assert.False(lockService.IsWriter);
        Assert.False(vm.IsWriteEnabled);
        Assert.False(vm.RestoreCommand.CanExecute(Entry(vm)));
    }

    /// <summary>The write-lock holder can restore: IsWriteEnabled is true and the command can execute.</summary>
    [Fact]
    public void RestoreCommand_WhenTheWriter_CanExecute()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = null;

        var vm = Build(out var lockService, out _);
        lockService.AcquireDemo();   // grants IsWriter without a lock file

        Assert.True(vm.IsWriteEnabled);
        Assert.True(vm.RestoreCommand.CanExecute(Entry(vm)));
    }

    /// <summary>The panel's texts are the agreed wording, taken from <see cref="RestorePrompt"/>.</summary>
    [Fact]
    public void RestoreTexts_AreTheRestorePromptWording()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = null;

        var vm = Build(out _, out _);

        Assert.Equal(RestorePrompt.PanelDescription, vm.RestoreDescription);
        Assert.Equal(RestorePrompt.ReadOnlyNote, vm.RestoreUnavailableNote);
    }

    // ── Second line of defence ────────────────────────────────────────────────

    /// <summary>
    /// Even when the command is run directly (it is disabled for a non-writer, but Execute does not check
    /// CanExecute), a non-writer is told so, is never asked to confirm, and the restore callback is not called.
    /// </summary>
    [Fact]
    public async Task Restore_WhenNotTheWriter_ShowsTheRefusal_AndNeverConfirmsOrCallsBack()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = null;

        var vm = Build(out _, out var dialog);
        string? calledWith = null;
        vm.RestoreCallback = path => { calledWith = path; return Task.CompletedTask; };

        await vm.RestoreCommand.ExecuteAsync(Entry(vm));

        dialog.Verify(d => d.ShowError(
            "Only the person editing this database can restore it. Your database is unchanged."), Times.Once);
        dialog.Verify(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Null(calledWith);
    }

    // ── Database name in the confirmation ─────────────────────────────────────

    /// <summary>The confirmation names the open database by its file name without the extension.</summary>
    [Fact]
    public void DatabaseNameFor_UsesTheOpenDatabasesFileNameWithoutExtension()
    {
        var source = Path.Combine(Path.GetTempPath(), "share", "History Dept.db");

        Assert.Equal("History Dept", SaveAndBackupViewModel.DatabaseNameFor(source, "ignored.db"));
    }

    /// <summary>With nothing open, the configured database path is used; with neither, a generic word.</summary>
    [Theory]
    [InlineData("", "Configured.db", "Configured")]
    [InlineData(null, "Configured.db", "Configured")]
    [InlineData("", null, "database")]
    [InlineData("   ", "", "database")]
    public void DatabaseNameFor_FallsBackToTheConfiguredPath_ThenToAGenericWord(string? source, string? fallback, string expected)
    {
        Assert.Equal(expected, SaveAndBackupViewModel.DatabaseNameFor(source, fallback));
    }
}
