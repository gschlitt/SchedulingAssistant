using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="DatabaseSwitchRecovery"/> (spec item 4d): the rules for what a switch that
/// did not open the requested database does next, and the chooser loop that follows a failure.
/// <c>MainWindow</c> cannot be constructed in a test, so everything decidable without a window lives
/// here as pure code and is covered; what stays in the window (covering it, showing the chooser,
/// calling the cleanup) is thin glue verified by inspection and by hand with the
/// <c>TERMPOINT_DEBUG_FAIL_CHECKOUT</c> fault injection.
/// </summary>
public sealed class DatabaseSwitchRecoveryTests
{
    // ── Decide ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A refusal BEFORE anything is torn down, with a database open, keeps the user on it — today's
    /// behaviour, which must not change.
    /// </summary>
    [Fact]
    public void Decide_RefusalBeforeTeardown_WithDatabaseOpen_StaysOnTheCurrentDatabase()
    {
        Assert.Equal(SwitchResult.StayedOnCurrentDatabase,
            DatabaseSwitchRecovery.Decide(SwitchPhase.BeforeTeardown, databaseOpen: true));
    }

    /// <summary>
    /// A refusal with NO database open (retrying from the chooser, the post-wizard open) has nothing to
    /// stay on, so it must lead to the chooser — never to a dead end.
    /// </summary>
    [Fact]
    public void Decide_RefusalBeforeTeardown_WithNoDatabaseOpen_GoesToTheChooser()
    {
        Assert.Equal(SwitchResult.NoDatabaseOpen,
            DatabaseSwitchRecovery.Decide(SwitchPhase.BeforeTeardown, databaseOpen: false));
    }

    /// <summary>
    /// A failure AFTER the previous database was closed always goes to the chooser, whatever the
    /// "database open" flag says — the old database is gone either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_FailureAfterTeardown_AlwaysGoesToTheChooser(bool databaseOpen)
    {
        Assert.Equal(SwitchResult.NoDatabaseOpen,
            DatabaseSwitchRecovery.Decide(SwitchPhase.AfterTeardown, databaseOpen));
    }

    /// <summary>Decide describes failures only: it never reports a successful open.</summary>
    [Theory]
    [InlineData(SwitchPhase.BeforeTeardown, true)]
    [InlineData(SwitchPhase.BeforeTeardown, false)]
    [InlineData(SwitchPhase.AfterTeardown, true)]
    [InlineData(SwitchPhase.AfterTeardown, false)]
    public void Decide_NeverReportsOpened(SwitchPhase phase, bool databaseOpen)
    {
        Assert.NotEqual(SwitchResult.Opened, DatabaseSwitchRecovery.Decide(phase, databaseOpen));
    }

    // ── RunChooserLoopAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Exit (or Create New, which hands over to the wizard) ends the loop at once: no open is attempted
    /// and the result is false.
    /// </summary>
    [Fact]
    public async Task ChooserLoop_UserLeaves_EndsWithoutOpeningAnything()
    {
        var shown = new List<string>();
        var opened = new List<string>();

        var result = await DatabaseSwitchRecovery.RunChooserLoopAsync(
            @"C:\db\failed.db",
            path => { shown.Add(path); return Task.FromResult<string?>(null); },
            path => { opened.Add(path); return Task.FromResult(true); });

        Assert.False(result);
        Assert.Equal([@"C:\db\failed.db"], shown);
        Assert.Empty(opened);
    }

    /// <summary>A pick that opens ends the loop with true; the chooser is not shown again.</summary>
    [Fact]
    public async Task ChooserLoop_PickOpens_EndsWithTrue()
    {
        var shown = new List<string>();

        var result = await DatabaseSwitchRecovery.RunChooserLoopAsync(
            @"C:\db\failed.db",
            path => { shown.Add(path); return Task.FromResult<string?>(@"C:\db\good.db"); },
            path => Task.FromResult(path == @"C:\db\good.db"));

        Assert.True(result);
        Assert.Single(shown);
    }

    /// <summary>
    /// "If opening the chosen database fails again, the chooser comes back": each failed pick returns to
    /// the chooser, which is then told the path that JUST failed (the first visit is told the original
    /// failure), until a pick opens.
    /// </summary>
    [Fact]
    public async Task ChooserLoop_FailedPicksComeBackToTheChooser_UntilOneOpens()
    {
        var shown = new List<string>();
        var picks = new Queue<string>(["B.db", "C.db", "D.db"]);
        var attempts = new List<string>();

        var result = await DatabaseSwitchRecovery.RunChooserLoopAsync(
            "A.db",
            path => { shown.Add(path); return Task.FromResult<string?>(picks.Dequeue()); },
            path => { attempts.Add(path); return Task.FromResult(path == "D.db"); });

        Assert.True(result);
        Assert.Equal(["B.db", "C.db", "D.db"], attempts);
        Assert.Equal(["A.db", "B.db", "C.db"], shown);
    }

    /// <summary>
    /// A failed pick followed by Exit ends the loop: false, with the failed pick attempted once and
    /// nothing opened after the user left.
    /// </summary>
    [Fact]
    public async Task ChooserLoop_FailedPickThenExit_EndsWithFalse()
    {
        var picks = new Queue<string?>(["B.db", null]);
        var attempts = new List<string>();
        var shown = 0;

        var result = await DatabaseSwitchRecovery.RunChooserLoopAsync(
            "A.db",
            _ => { shown++; return Task.FromResult(picks.Dequeue()); },
            path => { attempts.Add(path); return Task.FromResult(false); });

        Assert.False(result);
        Assert.Equal(["B.db"], attempts);
        Assert.Equal(2, shown);
    }

    // ── Refusal by the single-instance guard (spec item 4b) ───────────────────

    /// <summary>
    /// <c>SwitchCoreAsync</c> takes the guard for the target before the pre-check and before anything is
    /// closed, and a refusal is returned through <see cref="DatabaseSwitchRecovery.Decide"/> with
    /// <see cref="SwitchPhase.BeforeTeardown"/>. With a database open the user therefore STAYS on it
    /// (nothing was touched; the dialog has told them why).
    /// </summary>
    [Fact]
    public void GuardRefusal_WithDatabaseOpen_StaysOnTheCurrentDatabase()
    {
        Assert.Equal(SwitchResult.StayedOnCurrentDatabase,
            DatabaseSwitchRecovery.Decide(SwitchPhase.BeforeTeardown, databaseOpen: true));
    }

    /// <summary>
    /// With NO database open (the 4d no-database state: post-wizard open, chooser retry) a guard refusal
    /// goes to the chooser, so the user can pick another database or exit — never a dead end.
    /// </summary>
    [Fact]
    public void GuardRefusal_WithNoDatabaseOpen_GoesToTheChooser()
    {
        Assert.Equal(SwitchResult.NoDatabaseOpen,
            DatabaseSwitchRecovery.Decide(SwitchPhase.BeforeTeardown, databaseOpen: false));
    }

    /// <summary>
    /// In the chooser loop a database that is refused by the guard comes straight back to the chooser (the
    /// pick "failed"), and picking the same one again is refused again — the loop only ends when a
    /// database opens or the user leaves. (The guard-specific startup loop is covered in
    /// <see cref="DatabaseInstanceGuardTests"/>.)
    /// </summary>
    [Fact]
    public async Task ChooserLoop_GuardRefusedPick_ComesBackToTheChooser_UntilSomethingOpens()
    {
        var picks = new Queue<string?>(["busy.db", "busy.db", "free.db"]);
        var refused = new List<string>();

        var result = await DatabaseSwitchRecovery.RunChooserLoopAsync(
            "busy.db",
            _ => Task.FromResult(picks.Dequeue()),
            path => { if (path == "busy.db") { refused.Add(path); return Task.FromResult(false); } return Task.FromResult(true); });

        Assert.True(result);
        Assert.Equal(["busy.db", "busy.db"], refused);
    }

    // ── The recovery banner ───────────────────────────────────────────────────

    /// <summary>
    /// The chooser's banner after a failed switch is the exact agreed wording, names the path that failed,
    /// says nothing about damage (the failure may have been momentary), and keeps that path in the recent
    /// list so the user can simply retry it.
    /// </summary>
    [Fact]
    public void SwitchFailedBanner_HasTheAgreedWording_AndKeepsThePathInTheRecentList()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = Path.Combine(Path.GetTempPath(), "failed-switch-target.db");
        AppSettings.Current.AlwaysOpenMostRecentDatabase = false;
        AppSettings.Current.RecentDatabases = [path];

        var vm = new DatabaseChooserViewModel(ChooserMode.Recovery, RecoveryReason.SwitchFailed, path);

        Assert.True(vm.IsRecoveryMode);
        Assert.Equal(
            "TermPoint couldn't open the database you chose, and the database you had open has been closed. " +
            "Choose a database to continue.",
            vm.ProblemDescription);
        Assert.Equal(path, vm.LastKnownPath);
        Assert.DoesNotContain("damaged", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("integrity", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.RecentDatabases, r => r.Path == path);
    }

    /// <summary>
    /// The chooser's banner after the single-instance guard refused a database (spec item 4b) is the exact
    /// agreed wording — not the "integrity check" fallback that any unmapped reason would show — says
    /// nothing about damage or a closed database, and keeps the path in the recent list so the user can pick
    /// it again once the other window is closed.
    /// </summary>
    [Fact]
    public void AlreadyOpenBanner_HasTheAgreedWording_AndKeepsThePathInTheRecentList()
    {
        using var settingsGuard = new SettingsFileGuard();
        var path = Path.Combine(Path.GetTempPath(), "already-open-target.db");
        AppSettings.Current.AlwaysOpenMostRecentDatabase = false;
        AppSettings.Current.RecentDatabases = [path];

        var vm = new DatabaseChooserViewModel(ChooserMode.Recovery, RecoveryReason.AlreadyOpen, path);

        Assert.True(vm.IsRecoveryMode);
        Assert.Equal(
            "That database is already open in another TermPoint window on this computer. " +
            "Choose a different database, or exit.",
            vm.ProblemDescription);
        Assert.Equal(path, vm.LastKnownPath);
        Assert.DoesNotContain("damaged", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("integrity", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("closed", vm.ProblemDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.RecentDatabases, r => r.Path == path);
    }
}
