using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Characterisation tests for the LEAVE half of <see cref="SessionLifecycle"/> (spec item 9.1, step 9.1c, phase 2):
/// they pin what the closing teardown, the restore from a backup, the save-conflict banner (10a) and the lost write
/// lock did when they lived in <c>MainWindow</c> — the prompts the user sees (title, text, buttons, in order), the
/// order of the host and surface calls, the outcome, and the files left on disk — so a later step that changes the
/// behaviour changes these tests visibly and on purpose.
///
/// <para>They share the fixture of <see cref="SessionLifecycleTestBase"/> with the open/switch tests
/// (<see cref="SessionLifecycleTests"/>): real <see cref="CheckoutService"/>, <see cref="WriteLockService"/> and
/// <see cref="DatabaseInstanceGuard"/> on temp folders, the scripted prompts, the fake host, the ordered trace.
/// What this file adds is the fake lost-lock surface (<see cref="FakeLockLossSurface"/>, reached through
/// <see cref="FakeSessionHost.LockLossSurface"/>), the host hooks (<see cref="FakeSessionHost.OnDisposeServices"/>,
/// <see cref="FakeSessionHost.OnRelaunch"/> and the like) and the wait-dialog script step
/// (<see cref="ScriptedPrompts.ExpectWait"/>). No test here ever starts a process: the fake host only RECORDS that the
/// lifecycle asked for a relaunch.</para>
///
/// <para><b>Faults</b> use the existing internal seams only: <see cref="CheckoutService.BeforeTmpVerifyAsync"/> (a
/// save that fails with <see cref="SaveOutcome.CopyError"/>), <see cref="CheckoutService.RestoreFaultPlan"/>, and
/// ordinary files (an external write to D, a lock file written over ours, a garbage "backup").</para>
///
/// <para><b>Known-bad behaviour is pinned on purpose</b> and named with the finding id from
/// <c>designs/data-persistence-review-2026-10.md</c> section 3 (for example <c>Pins_F10</c>; F6 was fixed by item 28, so its tests are named with the <c>_F6Fixed</c> suffix).</para>
/// </summary>
public sealed class SessionLifecycleLeaveTests : SessionLifecycleTestBase
{
    // ── Texts the tests pin (literal, so a wording change shows up here) ──────

    /// <summary>The shutdown checkpoint line the teardown logs last.</summary>
    private const string CheckpointLine = "[Shutdown] Managed teardown complete; closing main window.";

    /// <summary>The "Changes Not Saved" message shown at close when the final save failed.</summary>
    private const string ChangesNotSavedAtClose =
        "Your latest changes couldn't be saved to the database. They're kept on this computer. " +
        "TermPoint will offer to add them to the database the next time you open it for editing. " +
        "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy.";

    /// <summary>The context the teardown logs an unexpected exception under.</summary>
    private const string ShutdownErrorContext = "Error during shutdown — some changes may not have been saved";

    /// <summary>The value the session writes as its unsaved edit.</summary>
    private const string SessionEdit = "session-edit";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a backup file that the restore routine accepts as a TermPoint database: the core tables it requires,
    /// plus a <c>_test</c> row carrying <paramref name="value"/> so a test can tell the backup's content from D's.
    /// It lives in a <c>backups</c> sub-folder of the share.
    /// </summary>
    /// <param name="value">The row stored in the backup's <c>_test</c> table.</param>
    /// <param name="name">The backup's file name.</param>
    /// <returns>The full path of the backup.</returns>
    private string NewBackup(string value, string name = "backup.db")
    {
        var folder = Path.Combine(_shareDir, "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        InsertValue(path, value);
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = string.Concat(DatabaseRestore.RequiredTables.Select(t => $"CREATE TABLE {t} (key TEXT, data TEXT); "));
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    /// <summary>
    /// Opens <paramref name="db"/> as a READER through the startup path (observer mode, so no lock is taken and the
    /// session works on the read-only snapshot D''), asserts it opened, and clears the recorded calls.
    /// </summary>
    /// <param name="db">The database to open.</param>
    private async Task OpenAsReaderAsync(string db)
    {
        AppSettings.Current.OpenInReaderMode = true;
        Assert.Null(await StartupAsync(db));
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        AssertScriptConsumed();
        ResetRecords();
    }

    /// <summary>
    /// Brings the open writer session into a pending save conflict (spec item 10, step 10a): the session makes an
    /// unsaved edit, a colleague changes D, and the save is refused as <see cref="SaveOutcome.SourceModified"/>.
    /// Clears the recorded calls afterwards.
    /// </summary>
    /// <param name="db">D, already opened for writing.</param>
    private async Task ForceSaveConflictAsync(string db)
    {
        _host.Edit(SessionEdit);
        InsertValue(db, ColleagueValue);
        Assert.Equal(SaveOutcome.SourceModified, await _checkout.SaveAsync());
        Assert.True(_checkout.HasSaveConflict);
        ResetRecords();
    }

    /// <summary>Makes the next save fail with <see cref="SaveOutcome.CopyError"/> (D.tmp vanishes just before it is verified).</summary>
    private void MakeSavesFail()
        => _checkout.BeforeTmpVerifyAsync = path => { File.Delete(path); return Task.CompletedTask; };

    /// <summary>Lets saves work again.</summary>
    private void MakeSavesWork() => _checkout.BeforeTmpVerifyAsync = null;

    /// <summary>
    /// Whether the checkout's autosave timer is running. The timer is a private field and there is no property for
    /// it, so the test reads the field — test code only; production is untouched.
    /// </summary>
    private bool IsAutoSaveRunning()
        => typeof(CheckoutService)
            .GetField("_autoSaveTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_checkout) is not null;

    /// <summary>The single "(before restore …)" copy a restore left beside D.</summary>
    /// <param name="db">D.</param>
    private string PreRestoreCopyOf(string db)
        => Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(db)!, Path.GetFileNameWithoutExtension(db) + " (before restore *.db"));

    /// <summary>
    /// Runs a restore that succeeds, with the prompts it shows, and returns the pre-restore copy it made. The wait
    /// dialog and the "Backup Restored" message are scripted; the caller asserts whatever else it cares about.
    /// </summary>
    /// <param name="db">D, opened for writing.</param>
    /// <param name="backup">The backup to restore.</param>
    /// <returns>The path of the pre-restore copy.</returns>
    private async Task<string> RestoreSucceedsAsync(string db, string backup)
    {
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage("Backup Restored");
        await _lifecycle.RestoreFromBackupAsync(backup);
        AssertScriptConsumed();
        return PreRestoreCopyOf(db);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CLOSE — TeardownForCloseAsync
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The ordinary close of a writer with unsaved edits, the save succeeding: the periodic backup is stopped first,
    /// then D' is saved to D (which releases the lock), then the secondary windows are closed, then the services are
    /// disposed (D' still on disk at that moment), and only then is the working copy deleted along with its marker.
    /// The single-instance guard is given back, nothing is asked, no relaunch is requested, the window is not closed
    /// by the lifecycle (the caller does that), and the shutdown checkpoint is logged.
    /// </summary>
    [Fact]
    public async Task Close_WriterWithUnsavedEdits_SaveSucceeds_TearsDownInOrder_AndCleansUp()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-at-close");
        ResetRecords();
        _host.OnDisposeServices = () => _trace.Add($"state:atDispose:workingExists={File.Exists(working)}");

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Empty(_prompts.Calls);
        AssertInOrder(_trace,
            "host:StopBackupSession",
            "checkout:SaveCompleted",
            "host:CloseSecondaryWindows",
            "host:DisposeServices(built=True, connection=True)",
            "state:atDispose:workingExists=True");
        Assert.DoesNotContain("host:RelaunchApplication", _trace);
        Assert.DoesNotContain("host:CloseWindow", _trace);
        Assert.Equal("edit-at-close", NewestValue(db));
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Contains(CheckpointLine, _logger.Infos);
        Assert.Empty(_logger.Errors);
        AssertScriptConsumed();
    }

    /// <summary>
    /// F6 (fixed by item 28): the final save fails (a transient copy error). The services are disposed, then "Changes
    /// Not Saved" is shown and says what really happens: the changes are kept on this computer, offered back at the
    /// next open for editing, and savable as a separate copy if someone else changes the database first.
    /// D' and its marker are KEPT (the only copy of the edit), D is unchanged, and the lock file is left in place
    /// (parked, <see cref="WriteLockService.Suspend"/>) so nobody writes under the kept changes until it goes stale.
    /// The guard is still given back and the checkpoint still logged.
    /// </summary>
    [Fact]
    public async Task Close_WriterWithUnsavedEdits_SaveFails_ChangesNotSaved_KeepsWorkingCopyAndParksLock_F6Fixed()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-at-close");
        MakeSavesFail();
        ResetRecords();
        _prompts.ExpectMessage("Changes Not Saved", ChangesNotSavedAtClose);

        await _lifecycle.TeardownForCloseAsync();
        MakeSavesWork();

        AssertPrompts("Message:Changes Not Saved");
        Assert.Equal("OK", _prompts.Calls.Single().Buttons);
        AssertInOrder(_trace,
            "host:StopBackupSession",
            "host:CloseSecondaryWindows",
            "host:DisposeServices(built=True, connection=True)",
            "prompt:Message:Changes Not Saved");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.DoesNotContain("host:RelaunchApplication", _trace);
        Assert.Equal("original", NewestValue(db));
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Equal("edit-at-close", NewestValue(working));
        Assert.True(File.Exists(LockOf(db)), "the lock is parked, not released, after a failed final save");
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Closing a READER (observer mode, working on the read-only snapshot): nothing is saved and nothing is asked;
    /// the snapshot D'' is deleted, D is untouched and there is no lock to give back.
    /// </summary>
    [Fact]
    public async Task Close_Reader_SavesNothing_DeletesTheSnapshot()
    {
        var db = NewDb();
        await OpenAsReaderAsync(db);
        var snapshot = _checkout.ReadOnlyWorkingPath!;
        Assert.True(File.Exists(snapshot));
        var dHash = DbFileTestHelpers.HashOf(db);

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Empty(_prompts.Calls);
        AssertInOrder(_trace, "host:StopBackupSession", "host:CloseSecondaryWindows", "host:DisposeServices(built=True, connection=True)");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.False(File.Exists(snapshot));
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Closing after a CONFIRMED restore: the session is stale, so nothing is saved; the lock is released, the
    /// secondary windows closed, the services disposed, and D' and its marker are always deleted (the restored D
    /// supersedes them). The single-instance guard is given back BEFORE the relaunch is requested, because a copy
    /// started earlier would be refused as "already open".
    /// </summary>
    [Fact]
    public async Task Close_AfterConfirmedRestore_EndsTheStaleSessionWithoutSaving_ThenRelaunchesAfterTheGuardIsReleased()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-before-restore");
        var preRestore = await RestoreSucceedsAsync(db, NewBackup("from-backup"));
        _host.Edit("late-edit");                       // a marker exists again: the teardown must delete it too
        Assert.True(File.Exists(MarkerOf(working)));
        ResetRecords();
        _host.OnRelaunch = () => _trace.Add($"state:atRelaunch:guardHeld={_guard.IsHeld(db)}");

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Empty(_prompts.Calls);
        AssertInOrder(_trace,
            "host:StopBackupSession",
            "host:CloseSecondaryWindows",
            "host:DisposeServices(built=True, connection=True)",
            "host:RelaunchApplication",
            "state:atRelaunch:guardHeld=False");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.Equal("from-backup", NewestValue(db));
        Assert.Equal("edit-before-restore", NewestValue(preRestore));     // saved first, so it is in the pre-restore copy
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Closing after a restore whose final rename could not be confirmed: the same stale-session teardown, but NO
    /// relaunch (the user is told to reopen TermPoint themselves once the connection is back).
    /// </summary>
    [Fact]
    public async Task Close_AfterRenameUnconfirmedRestore_EndsTheStaleSession_WithoutRelaunch()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-before-restore");
        _checkout.RestoreFaultPlan = RestoreFaultPlan.Parse("rename");
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage("Restore Not Confirmed");
        await _lifecycle.RestoreFromBackupAsync(NewBackup("from-backup"));
        AssertScriptConsumed();
        ResetRecords();

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Empty(_prompts.Calls);
        AssertInOrder(_trace, "host:StopBackupSession", "host:CloseSecondaryWindows", "host:DisposeServices(built=True, connection=True)");
        Assert.DoesNotContain("host:RelaunchApplication", _trace);
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
    }

    /// <summary>
    /// A host call inside the teardown throws (the container's disposal): the exception is logged as "Error during
    /// shutdown…" and swallowed so the window can still close. The steps after the failing call are skipped (the
    /// working copy is NOT cleaned up, so a stale D' is left behind — harmless, the final save had succeeded and the
    /// next start sweeps it), but the guard is still released and the checkpoint still logged.
    /// </summary>
    [Fact]
    public async Task Close_ADisposeFails_LogsTheError_StillReleasesTheGuard_AndLogsTheCheckpoint()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-at-close");
        ResetRecords();
        var failure = new InvalidOperationException("the container would not dispose");
        _host.DisposeServicesFailure = failure;

        await _lifecycle.TeardownForCloseAsync();     // must not throw
        SettleLocks();

        var error = Assert.Single(_logger.Errors);
        Assert.Same(failure, error.Ex);
        Assert.Equal(ShutdownErrorContext, error.Context);
        Assert.Empty(_prompts.Calls);
        Assert.Equal("edit-at-close", NewestValue(db));            // the save came first and had succeeded
        Assert.True(File.Exists(working));                         // cleanup was skipped with the failing call
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
        AssertScriptConsumed();
    }

    /// <summary>
    /// A save conflict is pending (D was changed by someone else and a save was refused) and the user closes the
    /// window: the exit save is refused again, so "Changes Not Saved" is shown (F6, fixed by item 28: its text names
    /// both outcomes, "added next time" and "a separate copy if the database changed"), D' and its marker are kept,
    /// D keeps the colleague's content, and the lock is released (a refusal, not a copy error).
    /// </summary>
    [Fact]
    public async Task Close_WithASaveConflictPending_SaveRefusedAgain_ChangesNotSaved_KeepsTheWorkingCopy_F6Fixed()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        _prompts.ExpectMessage("Changes Not Saved", ChangesNotSavedAtClose);

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        AssertPrompts("Message:Changes Not Saved");
        AssertInOrder(_trace,
            "host:StopBackupSession",
            "host:CloseSecondaryWindows",
            "host:DisposeServices(built=True, connection=True)",
            "prompt:Message:Changes Not Saved");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.Equal(ColleagueValue, NewestValue(db));
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Equal(SessionEdit, NewestValue(working));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.Contains(CheckpointLine, _logger.Infos);
        AssertScriptConsumed();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // RESTORE — RestoreFromBackupAsync
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Only the writer may restore. A reader is told so (title "Restore Not Started", the read-only note and "Your
    /// database is unchanged.") and nothing else happens: no wait dialog, no host call, no window close, D untouched.
    /// </summary>
    [Fact]
    public async Task Restore_NotTheWriter_ShowsTheNotStartedMessage_AndDoesNothingElse()
    {
        var db = NewDb();
        await OpenAsReaderAsync(db);
        var backup = NewBackup("from-backup");
        var dHash = DbFileTestHelpers.HashOf(db);
        _prompts.ExpectMessage("Restore Not Started", "Only the person editing this database can restore it. Your database is unchanged.");

        await _lifecycle.RestoreFromBackupAsync(backup);

        AssertPrompts("Message:Restore Not Started");
        Assert.Empty(_trace.Where(t => !t.StartsWith("prompt:")));
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.Empty(Directory.GetFiles(_shareDir, "dept (before restore *"));
        Assert.False(_lifecycle.IsRestoreInProgress);
        AssertScriptConsumed();
    }

    /// <summary>
    /// The save that must precede the restore fails: the restore is never started. The wait dialog is shown, then
    /// "Restore Not Started" with the save-failed text; D, D' and the marker are untouched, the window is not closed,
    /// the periodic backup is not stopped, and autosave (which the restore stopped) is started again because the user
    /// has it switched on.
    /// </summary>
    [Fact]
    public async Task Restore_TheSaveBeforeItFails_RestoreNotStarted_SessionContinues_AutoSaveResumes()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-before-restore");
        var backup = NewBackup("from-backup");
        AppSettings.Current.AutoSaveEnabled = true;
        MakeSavesFail();
        ResetRecords();
        Assert.False(IsAutoSaveRunning());
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage(RestorePrompt.NotStartedTitle, RestorePrompt.SaveFailedMessage);

        await _lifecycle.RestoreFromBackupAsync(backup);
        MakeSavesWork();

        AssertPrompts("Wait:Restoring the Backup", "Message:Restore Not Started");
        Assert.Equal(
            "Your latest changes couldn't be saved, so the restore wasn't started. Your database is unchanged.",
            _prompts.Calls[1].Text);
        Assert.DoesNotContain("host:StopBackupSession", _trace);
        Assert.DoesNotContain("host:CloseWindow", _trace);
        Assert.Equal("original", NewestValue(db));
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Empty(Directory.GetFiles(_shareDir, "dept (before restore *"));
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.False(_lifecycle.IsRestoreInProgress);
        Assert.True(IsAutoSaveRunning(), "autosave was stopped for the restore and is started again");
        AssertScriptConsumed();
    }

    /// <summary>The same refusal with autosave switched off: it is NOT started again.</summary>
    [Fact]
    public async Task Restore_TheSaveBeforeItFails_AutoSaveSwitchedOff_AutoSaveStaysStopped()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        _host.Edit("edit-before-restore");
        var backup = NewBackup("from-backup");
        AppSettings.Current.AutoSaveEnabled = false;
        MakeSavesFail();
        ResetRecords();
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage(RestorePrompt.NotStartedTitle, RestorePrompt.SaveFailedMessage);

        await _lifecycle.RestoreFromBackupAsync(backup);
        MakeSavesWork();

        Assert.False(IsAutoSaveRunning());
        AssertScriptConsumed();
    }

    /// <summary>
    /// The restore itself is refused with D unchanged (here: the "backup" is not a database at all). The user's
    /// changes were saved first (so D holds them), the message says the backup is damaged and the database is
    /// unchanged, the session simply continues — no relaunch, no window close, no pre-restore copy, no D.tmp — and
    /// autosave resumes.
    /// </summary>
    [Fact]
    public async Task Restore_RefusedWithDUnchanged_ShowsItsMessage_AndTheSessionContinues()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-before-restore");
        var garbage = Path.Combine(_shareDir, "garbage.db");
        File.WriteAllText(garbage, "this is not a database");
        AppSettings.Current.AutoSaveEnabled = true;
        ResetRecords();
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage("Restore Stopped", "That backup is damaged, so it can't be restored. Your database is unchanged.");

        await _lifecycle.RestoreFromBackupAsync(garbage);

        AssertPrompts("Wait:Restoring the Backup", "Message:Restore Stopped");
        AssertInOrder(_trace, "prompt:Wait:Restoring the Backup", "checkout:SaveCompleted", "prompt:Message:Restore Stopped");
        Assert.DoesNotContain("host:StopBackupSession", _trace);
        Assert.DoesNotContain("host:CloseWindow", _trace);
        Assert.Equal("edit-before-restore", NewestValue(db));            // saved first; the restore did not touch it
        Assert.Empty(Directory.GetFiles(_shareDir, "dept (before restore *"));
        Assert.False(File.Exists(db + ".tmp"));
        Assert.True(File.Exists(working));
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.True(_lockSvc.IsWriter);
        Assert.False(_lifecycle.IsRestoreInProgress);
        Assert.True(IsAutoSaveRunning());
        AssertScriptConsumed();
    }

    /// <summary>
    /// A confirmed restore, step by step: the wait dialog (title and text) runs the save and the restore, with
    /// <see cref="SessionLifecycle.IsRestoreInProgress"/> true for the whole of the work and false again by the time
    /// the message is shown; then the periodic backup is stopped (so a backup of the stale copy never looks newest),
    /// the "Backup Restored" message names the pre-restore file by NAME only, and the window is closed (which, in the
    /// real app, runs the close teardown). D now holds the backup; the replaced D is kept beside it with the user's
    /// saved edit; the session itself is untouched (D' still there, lock still ours) until the teardown ends it.
    /// </summary>
    [Fact]
    public async Task Restore_Confirmed_WaitDialog_StopsTheBackup_ShowsTheMessage_ClosesTheWindow()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit("edit-before-restore");
        var backup = NewBackup("from-backup");
        ResetRecords();
        bool? inProgressDuringWork = null, inProgressAtMessage = null;
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage,
                            before: () => inProgressDuringWork = _lifecycle.IsRestoreInProgress)
                .ExpectMessage("Backup Restored",
                               before: () => inProgressAtMessage = _lifecycle.IsRestoreInProgress);

        await _lifecycle.RestoreFromBackupAsync(backup);

        AssertPrompts("Wait:Restoring the Backup", "Message:Backup Restored");
        Assert.Equal(
            "Saving your changes and restoring the backup. This can take a minute on a slow network.",
            _prompts.Calls[0].Text);
        AssertInOrder(_trace,
            "prompt:Wait:Restoring the Backup",
            "checkout:SaveCompleted",
            "host:StopBackupSession",
            "prompt:Message:Backup Restored",
            "host:CloseWindow");
        Assert.True(inProgressDuringWork);
        Assert.False(inProgressAtMessage);
        Assert.False(_lifecycle.IsRestoreInProgress);

        var preRestore = PreRestoreCopyOf(db);
        Assert.Equal(
            "The backup was restored. The database as it was just before the restore is kept beside it as " +
            $"\"{Path.GetFileName(preRestore)}\". TermPoint will now restart.",
            _prompts.Calls[1].Text);
        Assert.DoesNotContain(_shareDir, _prompts.Calls[1].Text);
        Assert.Equal("from-backup", NewestValue(db));
        Assert.Equal("edit-before-restore", NewestValue(preRestore));
        Assert.True(File.Exists(working));
        Assert.True(_lockSvc.IsWriter);
        Assert.DoesNotContain("host:ReleaseServices(built=True, connection=True)", _trace);   // ending the session is the teardown's job
        AssertScriptConsumed();
    }

    /// <summary>
    /// A second restore requested while one is running is ignored (logged, no prompt, nothing started); the first
    /// one carries on and completes normally.
    /// </summary>
    [Fact]
    public async Task Restore_ASecondCallWhileOneRuns_IsIgnored_AndTheFirstCompletes()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        _host.Edit("edit-before-restore");
        var backup = NewBackup("from-backup");
        ResetRecords();
        Task? second = null;
        _prompts.ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage,
                            before: () => second = _lifecycle.RestoreFromBackupAsync(backup))
                .ExpectMessage("Backup Restored");

        await _lifecycle.RestoreFromBackupAsync(backup);

        Assert.NotNull(second);
        Assert.True(second!.IsCompletedSuccessfully, "the ignored call returns at once");
        Assert.Contains("MainWindow: RestoreFromBackupAsync ignored — a restore is already in progress.", _logger.Infos);
        AssertPrompts("Wait:Restoring the Backup", "Message:Backup Restored");
        Assert.Single(Directory.GetFiles(_shareDir, "dept (before restore *"));
        Assert.Equal("from-backup", NewestValue(db));
        Assert.Single(_trace, "host:CloseWindow");
        AssertScriptConsumed();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CONFLICT BANNER — ResolveSaveConflictAsync (spec item 10, step 10a)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Asserts that nothing about the session changed: no host call, the conflict still pending, still the writer, D' and its marker kept.</summary>
    /// <param name="db">D.</param>
    /// <param name="working">D'.</param>
    private void AssertConflictUntouched(string db, string working)
    {
        Assert.Empty(_trace.Where(t => !t.StartsWith("prompt:")));
        Assert.True(_checkout.HasSaveConflict);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.True(_lockSvc.IsWriter);
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Equal(SessionEdit, NewestValue(working));
        Assert.Equal(ColleagueValue, NewestValue(db));
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);
        Assert.True(_guard.IsHeld(db));
    }

    /// <summary>With no conflict pending the request is ignored: logged, no prompt, no host call.</summary>
    [Fact]
    public async Task Conflict_NoConflictPending_IsIgnored()
    {
        var db = NewDb();
        await OpenCleanAsync(db);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);
        await _lifecycle.ResolveSaveConflictAsync(saveCopy: false);

        Assert.Empty(_prompts.Calls);
        Assert.Empty(_trace);
        Assert.Equal(2, _logger.Infos.Count(i => i ==
            "MainWindow: ResolveSaveConflictAsync ignored — no conflict is pending, or one is already being resolved."));
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        AssertScriptConsumed();
    }

    /// <summary>
    /// An inline editor is open: an edit that has not been applied is not in the database at all, so the request is
    /// refused first with "Finish Editing First" and nothing changes — for Save a Copy and for Discard alike.
    /// </summary>
    [Fact]
    public async Task Conflict_AnInlineEditorIsOpen_FinishEditingFirst_NothingChanges()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        _host.InlineEditorOpen = true;
        _prompts.ExpectMessage("Finish Editing First", "Apply or cancel the open edit first.")
                .ExpectMessage("Finish Editing First", "Apply or cancel the open edit first.");

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);
        await _lifecycle.ResolveSaveConflictAsync(saveCopy: false);

        AssertPrompts("Message:Finish Editing First", "Message:Finish Editing First");
        AssertConflictUntouched(db, working);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Save a Copy, but the user cancels the save picker (the picker shows the recovered-copy title and a suggested
    /// name built from the database's name): nothing changes and the banner stays.
    /// </summary>
    [Fact]
    public async Task Conflict_SaveACopy_PickerCancelled_NothingChanges()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        _prompts.ExpectPick("Save Recovered Copy", null);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);

        AssertPrompts("PickSaveFile:Save Recovered Copy");
        Assert.Equal("dept (recovered).db|db", _prompts.Calls.Single().Text);
        AssertConflictUntouched(db, working);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Save a Copy, but the chosen name is already taken: the EARLY refusal shows "Choose a New Name" and returns to
    /// the banner with nothing changed (the existing file is not touched; the session is not ended).
    /// </summary>
    [Fact]
    public async Task Conflict_SaveACopy_NameTaken_ChooseANewName_NothingChanges()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        var taken = NewDb("taken.db", "someone-elses-file");
        var takenHash = DbFileTestHelpers.HashOf(taken);
        _prompts.ExpectPick("Save Recovered Copy", taken)
                .ExpectMessage("Choose a New Name", RecoveryConflictPrompt.AlreadyExistsMessage);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);

        AssertPrompts("PickSaveFile:Save Recovered Copy", "Message:Choose a New Name");
        Assert.Equal(
            "A file with that name already exists. Choose a new name — the recovered copy is never saved over an existing file.",
            _prompts.Calls[1].Text);
        Assert.Equal(takenHash, DbFileTestHelpers.HashOf(taken));
        AssertConflictUntouched(db, working);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Save a Copy works. The session is ended by the normal switch to the SAME database WITHOUT saving (the
    /// services are released first, nothing is saved to D); under the wait dialog the user's version is exported from
    /// D'; because the export was created, the kept changes are deleted and "Your Version Was Saved" names the file
    /// (its NAME only); then D is reopened from a fresh copy that holds the colleague's content, not the user's edit.
    /// </summary>
    [Fact]
    public async Task Conflict_SaveACopy_Works_ReleasesWithoutSaving_ExportsDeletesTheKeptChanges_AndReopens()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        var dHash = DbFileTestHelpers.HashOf(db);
        var export = Path.Combine(_shareDir, "my-version.db");
        _prompts.ExpectPick("Save Recovered Copy", export)
                .ExpectWait(SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage)
                .ExpectMessage("Your Version Was Saved", SaveConflictPrompt.SavedMessage("my-version.db"));

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);
        SettleLocks();

        AssertPrompts("PickSaveFile:Save Recovered Copy", "Wait:Saving Your Version", "Message:Your Version Was Saved");
        Assert.Equal("Saving your version…", _prompts.Calls[1].Text);
        Assert.Equal(
            "Your version was saved as \"my-version.db\". You can open it any time from Files → Open Database. " +
            "TermPoint will now reopen the current database.",
            _prompts.Calls[2].Text);
        Assert.DoesNotContain(_shareDir, _prompts.Calls[2].Text);
        AssertInOrder(_trace,
            "prompt:PickSaveFile:Save Recovered Copy",
            "host:ReleaseServices(built=True, connection=True)",
            "prompt:Wait:Saving Your Version",
            "prompt:Message:Your Version Was Saved",
            "host:OpenServices");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));                // released without saving
        Assert.Equal(SessionEdit, NewestValue(export));                   // the user's version, as a separate file
        Assert.Contains(
            "MainWindow: save conflict — the user's version was saved as a separate file; the kept changes were deleted.",
            _logger.Infos);

        // Reopened from a fresh copy of D: it holds the colleague's content and no kept changes.
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.False(_checkout.HasSaveConflict);
        Assert.Equal(working, _checkout.WorkingPath);
        Assert.Equal(ColleagueValue, NewestValue(_checkout.WorkingPath));
        Assert.False(File.Exists(MarkerOf(_checkout.WorkingPath)));
        Assert.True(_guard.IsHeld(db));
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Save a Copy, but the copy cannot be written after all (the name was taken between the picker and the export,
    /// so the export's own check refuses): "Choose a New Name" is shown and the kept changes are KEPT — nothing is
    /// deleted on any outcome but a created copy. The reopen's crash recovery then offers them again (D changed, so
    /// "Can't Restore Unsaved Changes" follows), and a second Save a Copy succeeds.
    /// </summary>
    [Fact]
    public async Task Conflict_SaveACopy_CopyFailsLate_KeepsTheChanges_AndTheReopenOffersThemAgain()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        var export  = Path.Combine(_shareDir, "my-version.db");
        var export2 = Path.Combine(_shareDir, "my-version-2.db");
        bool keptAtOffer = false, markerAtOffer = false;
        string? keptValueAtOffer = null;
        _prompts.ExpectPick("Save Recovered Copy", export)
                .ExpectWait(SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage,
                            before: () => File.WriteAllText(export, "a file that appeared meanwhile"))
                .ExpectMessage("Choose a New Name", RecoveryConflictPrompt.AlreadyExistsMessage)
                .ExpectChoice("Restore Unsaved Changes?", true,
                              before: () =>
                              {
                                  keptAtOffer      = File.Exists(working);
                                  markerAtOffer    = File.Exists(MarkerOf(working));
                                  keptValueAtOffer = NewestValue(working);
                              })
                .ExpectChoice("Can't Restore Unsaved Changes", true)
                .ExpectPick("Save Recovered Copy", export2);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);
        SettleLocks();

        AssertPrompts(
            "PickSaveFile:Save Recovered Copy", "Wait:Saving Your Version", "Message:Choose a New Name",
            "Choice:Restore Unsaved Changes?", "Choice:Can't Restore Unsaved Changes", "PickSaveFile:Save Recovered Copy");
        Assert.DoesNotContain("Message:Your Version Was Saved", _prompts.Calls.Select(c => $"{c.Kind}:{c.Title}"));
        Assert.Contains("MainWindow: save conflict — the copy was not saved; the changes are kept.", _logger.Infos);
        Assert.True(keptAtOffer);
        Assert.True(markerAtOffer);
        Assert.Equal(SessionEdit, keptValueAtOffer);
        Assert.Equal(SessionEdit, NewestValue(export2));
        Assert.True(_host.IsDatabaseOpen);
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);
        AssertScriptConsumed();
    }

    /// <summary>Discard Changes, but the user cancels the confirmation: the confirmation is asked with its texts and buttons, and nothing changes.</summary>
    [Fact]
    public async Task Conflict_Discard_ConfirmationCancelled_NothingChanges()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        _prompts.ExpectChoice("Discard Changes?", false,
            "Discard the changes you made since your last save? This can't be undone.");

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: false);

        AssertPrompts("Choice:Discard Changes?");
        Assert.Equal("Discard|Cancel", _prompts.Calls.Single().Buttons);
        AssertConflictUntouched(db, working);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Discard Changes, confirmed: the session is released without saving, the kept changes are deleted, and D is
    /// reopened from a fresh copy (the colleague's content). Nothing else is asked and nothing is exported.
    /// </summary>
    [Fact]
    public async Task Conflict_Discard_Confirmed_ReleasesWithoutSaving_DeletesTheKeptChanges_AndReopens()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        await ForceSaveConflictAsync(db);
        var dHash = DbFileTestHelpers.HashOf(db);
        _prompts.ExpectChoice("Discard Changes?", true);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: false);
        SettleLocks();

        AssertPrompts("Choice:Discard Changes?");
        AssertInOrder(_trace,
            "prompt:Choice:Discard Changes?",
            "host:ReleaseServices(built=True, connection=True)",
            "host:OpenServices");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.Contains("MainWindow: save conflict — the user's changes were discarded.", _logger.Infos);
        Assert.Empty(Directory.GetFiles(_shareDir, "*recovered*"));
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.False(_checkout.HasSaveConflict);
        Assert.Equal(working, _checkout.WorkingPath);
        Assert.Equal(ColleagueValue, NewestValue(_checkout.WorkingPath));
        Assert.False(File.Exists(MarkerOf(_checkout.WorkingPath)));
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);
        AssertScriptConsumed();
    }

    /// <summary>
    /// <see cref="SessionLifecycle.IsSaveConflictResolutionInProgress"/> (which makes the window refuse every close
    /// request) is true from the moment the choice is confirmed — while the services are released, during the export
    /// and while the "saved" message is up — and false again afterwards; a second request meanwhile is ignored.
    /// </summary>
    [Fact]
    public async Task Conflict_WhileResolving_TheCloseGuardIsOn_AndASecondRequestIsIgnored()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        await ForceSaveConflictAsync(db);
        var export = Path.Combine(_shareDir, "my-version.db");
        bool? atRelease = null, atWait = null, atMessage = null;
        Task? second = null;
        _host.OnReleaseServices = () => atRelease = _lifecycle.IsSaveConflictResolutionInProgress;
        _prompts.ExpectPick("Save Recovered Copy", export)
                .ExpectWait(SaveConflictPrompt.SavingTitle, SaveConflictPrompt.SavingMessage,
                            before: () =>
                            {
                                atWait = _lifecycle.IsSaveConflictResolutionInProgress;
                                second = _lifecycle.ResolveSaveConflictAsync(saveCopy: false);
                            })
                .ExpectMessage("Your Version Was Saved",
                               before: () => atMessage = _lifecycle.IsSaveConflictResolutionInProgress);
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);

        await _lifecycle.ResolveSaveConflictAsync(saveCopy: true);
        SettleLocks();

        Assert.True(atRelease);
        Assert.True(atWait);
        Assert.True(atMessage);
        Assert.False(_lifecycle.IsSaveConflictResolutionInProgress);
        Assert.NotNull(second);
        Assert.True(second!.IsCompletedSuccessfully);
        Assert.Contains(
            "MainWindow: ResolveSaveConflictAsync ignored — no conflict is pending, or one is already being resolved.",
            _logger.Infos);
        AssertPrompts("PickSaveFile:Save Recovered Copy", "Wait:Saving Your Version", "Message:Your Version Was Saved");
        AssertScriptConsumed();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LOST LOCK — HandleWriteLockLostAsync
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// F6 (fixed by item 28): another session's lock is written over ours while the user has unsaved edits; the next
    /// save notices (<see cref="WriteLockLostReason.TakenOver"/>, naming the new holder). The banner names that holder,
    /// says the changes are kept on this computer and what happens to them next (offered back at the next open for
    /// editing; a separate copy if someone else changes the database first), and how to edit again. The
    /// surface is used in this order: banner, then the connection is closed; D' and its marker are preserved and the
    /// read-only snapshot D'' is created; the connection is reopened on D''; the section list is reloaded. The
    /// foreign lock is left alone.
    /// </summary>
    [Fact]
    public async Task LostLock_TakenOver_WithUnsavedEdits_BannerNamesTheHolder_ThenDemotesInPlace_F6Fixed()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit(SessionEdit);
        var reasons = new List<WriteLockLostReason>();
        _checkout.WriteLockLost += reasons.Add;
        WriteExternalFreshLock(db);                                   // another session's lock written over ours
        Assert.Equal(SaveOutcome.LockLost, await _checkout.SaveAsync());
        var reason = Assert.Single(reasons);
        Assert.Equal(WriteLockLostReason.TakenOver, reason);
        ResetRecords();
        bool dKept = false, markerKept = false, snapshotMade = false;
        _host.LockLossSurface.OnReinitialize = path =>
        {
            dKept        = File.Exists(working);
            markerKept   = File.Exists(MarkerOf(working));
            snapshotMade = File.Exists(path);
        };

        await _lifecycle.HandleWriteLockLostAsync(reason);

        var snapshot = _checkout.WorkingPath;
        Assert.NotEqual(working, snapshot);
        var banner = Assert.Single(_host.LockLossSurface.SaveErrors);
        Assert.Equal(
            "other_user on OTHER-PC has taken over editing this database, so it's read-only for you now. " +
            "Your changes since your last save are kept on this computer. " +
            "TermPoint will offer to add them to the database the next time you open it for editing. " +
            "If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy. " +
            "To edit again once they've finished, close TermPoint and open it again.",
            banner);
        AssertInOrder(_trace,
            "host:CaptureLockLossSurface",
            "surface:ShowSaveError",
            "surface:CloseConnection",
            $"surface:ReinitializeConnection({Path.GetFileName(snapshot)})",
            "surface:ReloadSections");
        Assert.Equal(new[] { snapshot }, _host.LockLossSurface.ReinitializedPaths);
        Assert.True(dKept);
        Assert.True(markerKept);
        Assert.True(snapshotMade);
        Assert.Equal(SessionEdit, NewestValue(working));
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Equal("other_user", JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockOf(db)))!.Username);
        Assert.Empty(_prompts.Calls);
        Assert.Empty(_logger.Errors);
        AssertScriptConsumed();
    }

    /// <summary>
    /// The lock file is removed by another program and there are no unsaved edits: the banner says "removed or
    /// changed" by another program (F13: a damaged file is not "removed") and "no unsaved changes"; D' is deleted (nothing to keep), the snapshot D'' is created, the
    /// connection reopens on it and the sections reload.
    /// </summary>
    [Fact]
    public async Task LostLock_LockFileRemoved_NoUnsavedEdits_BannerSaysSo_AndDeletesTheWorkingCopy()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        File.Delete(LockOf(db));                                      // antivirus or sync software removed it
        Assert.False(File.Exists(MarkerOf(working)));

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        var snapshot = _checkout.WorkingPath;
        Assert.Equal(
            "Another program removed or changed the file TermPoint uses to mark this database as in use " +
            "(antivirus or cloud-sync software can do this), so it's read-only for you now. " +
            "There were no unsaved changes at the time. " +
            "To edit again, close TermPoint and open it again.",
            Assert.Single(_host.LockLossSurface.SaveErrors));
        AssertInOrder(_trace,
            "host:CaptureLockLossSurface",
            "surface:ShowSaveError",
            "surface:CloseConnection",
            $"surface:ReinitializeConnection({Path.GetFileName(snapshot)})",
            "surface:ReloadSections");
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.True(File.Exists(snapshot));
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Empty(_logger.Errors);
    }

    /// <summary>
    /// F4: the demotion marks the data connection read-only BEFORE it closes it (so an editor applied while the
    /// read-only copy is being made is refused with the plain message, not left to fail on a closed connection) and
    /// reopens it read-only on the new snapshot.
    /// </summary>
    [Fact]
    public async Task LostLock_MarksTheConnectionReadOnlyBeforeClosing_AndReopensItReadOnly()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        File.Delete(LockOf(db));

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        Assert.Equal(1, _host.LockLossSurface.MarkReadOnlyCalls);
        Assert.True(_host.LockLossSurface.WasReadOnlyWhenClosed);
        AssertInOrder(_trace,
            "surface:ShowSaveError",
            "surface:MarkReadOnly",
            "surface:CloseConnection",
            $"surface:ReinitializeConnection({Path.GetFileName(_checkout.WorkingPath)})",
            "surface:ReloadSections");
        Assert.Equal(new[] { true }, _host.LockLossSurface.ReinitializedReadOnly);
    }

    /// <summary>
    /// F4: when the read-only snapshot cannot be made, the connection is still reopened on what was left (the kept
    /// D', the user's only copy of unsaved work) — and it is reopened READ-ONLY, so nothing can write to that copy
    /// from now on. The section list is not reloaded (the demotion failed).
    /// </summary>
    [Fact]
    public async Task LostLock_WhenTheSnapshotCannotBeMade_StillReopensReadOnly_OnTheKeptWorkingCopy()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit(SessionEdit);
        _checkout.FaultPlan = CheckoutFaultPlan.Parse("snapshot");
        File.Delete(LockOf(db));

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        Assert.True(_host.LockLossSurface.WasReadOnlyWhenClosed);
        Assert.Equal(new[] { working }, _host.LockLossSurface.ReinitializedPaths);
        Assert.Equal(new[] { true }, _host.LockLossSurface.ReinitializedReadOnly);
        Assert.DoesNotContain("surface:ReloadSections", _trace);
        Assert.True(File.Exists(working));                            // the unsaved work is still there
    }

    /// <summary>
    /// Reopening the connection on the snapshot fails: the SECOND banner replaces the first ("could not be restored…
    /// close and reopen the database file"), the error is logged, and — pinned as it is today — the section reload is
    /// STILL attempted, because the failure is swallowed inside the reopen step and the demotion reports success.
    /// </summary>
    [Fact]
    public async Task LostLock_ReinitializeConnectionThrows_SecondBannerShown_ErrorLogged_ReloadStillAttempted()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        File.Delete(LockOf(db));
        var failure = new InvalidOperationException("the snapshot could not be opened");
        _host.LockLossSurface.ReinitializeFailure = failure;

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        Assert.Equal(2, _host.LockLossSurface.SaveErrors.Count);
        Assert.StartsWith("Another program removed or changed the file TermPoint uses to mark this database as in use", _host.LockLossSurface.SaveErrors[0]);
        Assert.Equal(
            "The database connection could not be restored after losing write access. " +
            "Please close and reopen the database file.",
            _host.LockLossSurface.SaveErrors[1]);
        var error = Assert.Single(_logger.Errors);
        Assert.Same(failure, error.Ex);
        Assert.Equal("OnWriteLockLost: ReinitializeConnection failed — database is unavailable until reopened", error.Context);
        AssertInOrder(_trace,
            "surface:ShowSaveError", "surface:CloseConnection", $"surface:ReinitializeConnection({Path.GetFileName(_checkout.WorkingPath)})",
            "surface:ShowSaveError", "surface:ReloadSections");
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
    }

    /// <summary>The section reload after the demotion throws: it is logged ("post-demote reload failed") and swallowed.</summary>
    [Fact]
    public async Task LostLock_TheReloadThrows_IsLoggedAndSwallowed()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        File.Delete(LockOf(db));
        var failure = new InvalidOperationException("the reload failed");
        _host.LockLossSurface.ReloadFailure = failure;

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);   // must not throw

        var error = Assert.Single(_logger.Errors);
        Assert.Same(failure, error.Ex);
        Assert.Equal("OnWriteLockLost: post-demote reload failed", error.Context);
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
    }

    /// <summary>
    /// No view model was captured (the window's data context was something else): the demotion still happens and
    /// the connection is still reopened, but the section list is not reloaded, and nothing throws.
    /// </summary>
    [Fact]
    public async Task LostLock_NoViewModelCaptured_DemotesWithoutReloading_AndDoesNotThrow()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        File.Delete(LockOf(db));
        _host.LockLossSurface.HasViewModel = false;

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        Assert.DoesNotContain("surface:ReloadSections", _trace);
        AssertInOrder(_trace,
            "surface:ShowSaveError", "surface:CloseConnection",
            $"surface:ReinitializeConnection({Path.GetFileName(_checkout.WorkingPath)})");
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Empty(_logger.Errors);
    }

    /// <summary>
    /// No data connection was captured: the demotion still happens and the sections still reload, but the
    /// connection is not reopened (there is nothing to reopen).
    /// </summary>
    [Fact]
    public async Task LostLock_NoConnectionCaptured_DemotesWithoutReopening_AndDoesNotThrow()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        File.Delete(LockOf(db));
        _host.LockLossSurface.HasConnection = false;

        await _lifecycle.HandleWriteLockLostAsync(WriteLockLostReason.LockFileRemoved);

        Assert.Empty(_host.LockLossSurface.ReinitializedPaths);
        AssertInOrder(_trace, "surface:ShowSaveError", "surface:CloseConnection", "surface:ReloadSections");
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Empty(_logger.Errors);
    }
}
