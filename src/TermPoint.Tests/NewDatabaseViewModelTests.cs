using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the File → New flyout's switch-then-apply logic (spec item 4e review finding).
///
/// <para><b>The bug:</b> <see cref="NewDatabaseViewModel"/> switched databases and then ALWAYS applied
/// the transferred configuration (campuses, block patterns, an academic year, semesters) through
/// <c>App.Services</c> into whatever database was open. Once <c>SwitchDatabaseAsync</c> can REFUSE a
/// target and leave the previous database open and live (the pre-check), a File → New aimed at a held-open,
/// damaged, unreachable or already-existing file would have written the "new" configuration into the
/// user's CURRENT database. The switch delegate now reports whether the new database actually opened,
/// and the configuration is applied only on <c>true</c>.</para>
///
/// <para><b>Seam:</b> the real apply step writes through <c>App.Services</c> and cannot run in a unit
/// test, so the view model exposes it as the internal <c>ApplyConfigAction</c>; the tests replace it with
/// a recorder. A non-null configuration is built directly (taking one from the current database would
/// need a live <c>ShareViewModel</c>), so the decision is tested through
/// <c>SwitchAndApplyAsync</c>; the full <c>CreateDatabaseCommand</c> is exercised with no transfer
/// requested to check the user-visible state (Create button, error text, backup-folder setting).
/// Settings are touched in memory only, inside a <see cref="SettingsFileGuard"/>.</para>
/// </summary>
public sealed class NewDatabaseViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a view model whose switch delegate is <paramref name="switchDelegate"/>. A null
    /// <c>ShareViewModel</c> is fine: it is used only to snapshot a configuration, which these tests
    /// build themselves. The assessor is a fake with no real roots so nothing probes the machine.
    /// </summary>
    /// <param name="switchDelegate">What "switch to the new database" does and returns.</param>
    private static NewDatabaseViewModel BuildVm(Func<string, Task<bool>> switchDelegate)
    {
        var assessor = new FolderAssessor(
            Array.Empty<string>(), Array.Empty<CloudSyncRoot>(), writabilityProbe: _ => true);
        return new NewDatabaseViewModel(null!, new SemesterContext(), assessor)
        {
            SwitchDatabaseAsync = switchDelegate
        };
    }

    /// <summary>Fills in the minimum the Create button needs (name, database folder, backup folder).</summary>
    /// <param name="vm">The view model to fill in.</param>
    /// <param name="backupFolder">The backup folder the user "chose" for the new database.</param>
    private static void FillInValidInputs(NewDatabaseViewModel vm, string backupFolder)
    {
        vm.DbName       = "Fresh";
        vm.DbFolder     = Path.GetTempPath();
        vm.BackupFolder = backupFolder;
    }

    // ── SwitchAndApplyAsync: the decision ─────────────────────────────────────

    /// <summary>
    /// THE BUG. The switch was refused (returned false): the old database is still open, so the
    /// configuration must NOT be applied — and the result tells the caller the new database is not open.
    /// </summary>
    [Fact]
    public async Task SwitchAndApply_SwitchRefused_DoesNotApplyConfig()
    {
        var applied = new List<(TpConfigData Config, string AyName)>();
        string? switchedTo = null;
        var vm = BuildVm(path => { switchedTo = path; return Task.FromResult(false); });
        vm.ApplyConfigAction = (config, ay) => applied.Add((config, ay));

        var opened = await vm.SwitchAndApplyAsync(@"C:\db\new-TT.db", new TpConfigData(), "2025-2026");

        Assert.False(opened);
        Assert.Empty(applied);
        Assert.Equal(@"C:\db\new-TT.db", switchedTo);   // the switch was attempted with the right path
    }

    /// <summary>The switch succeeded: the configuration is applied exactly once, with the snapshot and year name.</summary>
    [Fact]
    public async Task SwitchAndApply_SwitchSucceeded_AppliesConfigOnce()
    {
        var applied = new List<(TpConfigData Config, string AyName)>();
        var vm = BuildVm(_ => Task.FromResult(true));
        vm.ApplyConfigAction = (config, ay) => applied.Add((config, ay));
        var snapshot = new TpConfigData();

        var opened = await vm.SwitchAndApplyAsync(@"C:\db\new-TT.db", snapshot, "2025-2026");

        Assert.True(opened);
        var call = Assert.Single(applied);
        Assert.Same(snapshot, call.Config);
        Assert.Equal("2025-2026", call.AyName);
    }

    /// <summary>No transfer requested (null snapshot): a successful switch applies nothing and still reports success.</summary>
    [Fact]
    public async Task SwitchAndApply_NoConfigRequested_AppliesNothing()
    {
        var applied = 0;
        var vm = BuildVm(_ => Task.FromResult(true));
        vm.ApplyConfigAction = (_, _) => applied++;

        var opened = await vm.SwitchAndApplyAsync(@"C:\db\new-TT.db", config: null, ayName: string.Empty);

        Assert.True(opened);
        Assert.Equal(0, applied);
    }

    // ── CreateDatabaseCommand: user-visible state ─────────────────────────────

    /// <summary>
    /// A refused switch (a dialog already told the user why): Create becomes available again, no extra
    /// error text is added, the backup-folder edit made for the attempt is undone (no settings were
    /// recorded), and the delegate was given the path built from the name and folder.
    /// </summary>
    [Fact]
    public async Task Create_SwitchRefused_ResetsCreateButton_AndRestoresBackupFolder()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = @"C:\old-backups";
        string? switchedTo = null;
        var vm = BuildVm(path => { switchedTo = path; return Task.FromResult(false); });
        FillInValidInputs(vm, backupFolder: @"C:\new-backups");
        Assert.True(vm.CanCreate);

        await vm.CreateDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(vm.DbFullPath, switchedTo);
        Assert.False(vm.IsCreating);
        Assert.True(vm.CanCreate);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.Equal(@"C:\old-backups", AppSettings.Current.BackupFolderPath);
    }

    /// <summary>
    /// If the switch returned false only AFTER it had recorded the new database in the settings (it had
    /// already closed the old one), the new database's backup folder is kept — restoring the old one
    /// would point the settings at a mismatched pair.
    /// </summary>
    [Fact]
    public async Task Create_SwitchFailedAfterSettingsRecorded_KeepsNewBackupFolder()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.DatabasePath     = @"C:\old\old-TT.db";
        AppSettings.Current.BackupFolderPath = @"C:\old-backups";
        var vm = BuildVm(path =>
        {
            AppSettings.Current.DatabasePath = path;   // what a switch that got past the pre-check does
            return Task.FromResult(false);
        });
        FillInValidInputs(vm, backupFolder: @"C:\new-backups");

        await vm.CreateDatabaseCommand.ExecuteAsync(null);

        Assert.False(vm.IsCreating);
        Assert.Equal(@"C:\new-backups", AppSettings.Current.BackupFolderPath);
    }

    /// <summary>
    /// A successful switch keeps the new backup folder, and (as before) leaves IsCreating set — the
    /// flyout is already gone after the window's data context swap.
    /// </summary>
    [Fact]
    public async Task Create_SwitchSucceeded_KeepsNewBackupFolder()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.BackupFolderPath = @"C:\old-backups";
        var vm = BuildVm(_ => Task.FromResult(true));
        FillInValidInputs(vm, backupFolder: @"C:\new-backups");

        await vm.CreateDatabaseCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\new-backups", AppSettings.Current.BackupFolderPath);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }
}
