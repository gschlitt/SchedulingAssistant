using Microsoft.Data.Sqlite;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Lifecycle tests for the "Save Changes?" question (undo design, stories 5, 6, 8 and 9, and "The close question"):
/// what a close, a database switch and a restore do for each of Save, Don't Save and Cancel, and that a crash recovery
/// no longer saves what it restored. They use the real <see cref="CheckoutService"/> on temp files, the scripted
/// prompts and <see cref="FakeSessionHost.UnsavedState"/> to model unsaved changes.
/// </summary>
public sealed class SessionLifecycleUnsavedTests : SessionLifecycleTestBase
{
    /// <summary>The value the session writes as its unsaved edit.</summary>
    private const string SessionEdit = "session-edit";

    /// <summary>Opens <paramref name="db"/>, makes one unsaved edit and tells the fake host the database is unsaved.</summary>
    /// <param name="db">D.</param>
    /// <returns>The path of the working copy D'.</returns>
    private async Task<string> OpenWithUnsavedEditAsync(string db)
    {
        await OpenCleanAsync(db);
        var working = _checkout.WorkingPath;
        _host.Edit(SessionEdit);
        MarkUnsaved();
        ResetRecords();
        return working;
    }

    /// <summary>Makes the fake host report unsaved changes (as the real history does after a section edit).</summary>
    private void MarkUnsaved() => _host.UnsavedState = new UnsavedState(true, DateTime.Now, SavedThisSession: true, CardHeading: null);

    /// <summary>Creates a backup file the restore accepts as a TermPoint database, holding <paramref name="value"/> in its <c>_test</c> table.</summary>
    /// <param name="value">The row stored in the backup.</param>
    /// <returns>The backup's full path.</returns>
    private string NewBackup(string value)
    {
        var folder = Path.Combine(_shareDir, "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "backup.db");
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

    // ═════════════════════════════════════════════════════════════════════════
    // CLOSE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Close, Don't Save: the lock is released, D is byte-identical to before, and the working copy and its dirty marker
    /// are deleted (the changes are gone, the user's deliberate choice).
    /// </summary>
    [Fact]
    public async Task Close_DontSave_ReleasesTheLock_LeavesDUntouched_DeletesWorkingCopyAndMarker()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);
        _prompts.ExpectThreeChoice(HistoryText.CloseTitle, ThreeChoiceResult.Second);

        Assert.True(_lifecycle.NeedsCloseQuestion);
        Assert.True(await _lifecycle.AskBeforeCloseAsync());
        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.Equal("original", NewestValue(db));
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(db)));
        Assert.False(_guard.IsHeld(db));
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        AssertScriptConsumed();
    }

    /// <summary>Close, Save: today's save runs, so D holds the change and the working copy is cleaned up.</summary>
    [Fact]
    public async Task Close_Save_SavesAsToday()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        _prompts.ExpectThreeChoice(HistoryText.CloseTitle, ThreeChoiceResult.First);

        Assert.True(await _lifecycle.AskBeforeCloseAsync());
        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.Equal(SessionEdit, NewestValue(db));
        Assert.Contains("checkout:SaveCompleted", _trace);
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(db)));
        AssertScriptConsumed();
    }

    /// <summary>Close, Cancel: the close stops, nothing changes, and the question is still due on the next close request.</summary>
    [Fact]
    public async Task Close_Cancel_StopsAndChangesNothing()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);
        _prompts.ExpectThreeChoice(HistoryText.CloseTitle, ThreeChoiceResult.Third);

        Assert.False(await _lifecycle.AskBeforeCloseAsync());

        Assert.True(_lifecycle.NeedsCloseQuestion);
        Assert.False(_lifecycle.IsUnsavedQuestionShowing);
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.True(File.Exists(LockOf(db)));
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        AssertScriptConsumed();
    }

    /// <summary>The close question is not needed when nothing is unsaved, and not again once it has been answered.</summary>
    [Fact]
    public async Task NeedsCloseQuestion_FalseWhenNothingUnsaved_AndAfterAnAnswer()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        Assert.False(_lifecycle.NeedsCloseQuestion);                    // nothing unsaved

        _host.UnsavedState = new UnsavedState(false, DateTime.Now, true, null);
        Assert.False(_lifecycle.NeedsCloseQuestion);                    // state says clean

        MarkUnsaved();
        Assert.True(_lifecycle.NeedsCloseQuestion);
        _prompts.ExpectThreeChoice(HistoryText.CloseTitle, ThreeChoiceResult.First);
        Assert.True(await _lifecycle.AskBeforeCloseAsync());

        Assert.False(_lifecycle.NeedsCloseQuestion);                    // the second close request goes straight to the teardown
        AssertScriptConsumed();
    }

    /// <summary>An open editor with changes alone also makes the close question due.</summary>
    [Fact]
    public async Task NeedsCloseQuestion_TrueForAnEditorWithChangesAlone()
    {
        var db = NewDb();
        await OpenCleanAsync(db);
        _host.UnappliedEditor = new FakeUnappliedEditor();

        Assert.True(_lifecycle.NeedsCloseQuestion);
    }

    /// <summary>
    /// The lock is lost after the user answered Don't Save and before the teardown: nothing is discarded. The working
    /// copy and its marker are kept for the next open, exactly as a lost lock keeps them today.
    /// </summary>
    [Fact]
    public async Task Close_DontSave_ThenTheLockIsLost_KeepsTheWorkingCopyAndMarker()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        _prompts.ExpectThreeChoice(HistoryText.CloseTitle, ThreeChoiceResult.Second);
        Assert.True(await _lifecycle.AskBeforeCloseAsync());
        var reasons = new List<WriteLockLostReason>();
        _checkout.WriteLockLost += reasons.Add;
        WriteExternalFreshLock(db);
        Assert.Equal(SaveOutcome.LockLost, await _checkout.SaveAsync());
        await _lifecycle.HandleWriteLockLostAsync(Assert.Single(reasons));

        await _lifecycle.TeardownForCloseAsync();
        SettleLocks();

        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Equal(SessionEdit, NewestValue(working));
        Assert.Equal("original", NewestValue(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SWITCH
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Switch, Don't Save: D is unchanged and the old working copy and its marker are deleted; the target opens.</summary>
    [Fact]
    public async Task Switch_DontSave_LeavesDUntouched_DeletesTheOldWorkingCopyAndMarker()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        var working = await OpenWithUnsavedEditAsync(a);
        var before = DbFileTestHelpers.HashOf(a);
        _prompts.ExpectThreeChoice(HistoryText.SwitchTitle("a", "b"), ThreeChoiceResult.Second);

        var opened = await _lifecycle.SwitchDatabaseAsync(b);
        SettleLocks();

        Assert.True(opened);
        Assert.Equal(b, _checkout.SourcePath);
        Assert.Equal(before, DbFileTestHelpers.HashOf(a));
        Assert.False(File.Exists(working));
        Assert.False(File.Exists(MarkerOf(working)));
        Assert.False(File.Exists(LockOf(a)));
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        AssertScriptConsumed();
    }

    /// <summary>Switch, Save: the old database is saved (D holds the change) and the target opens.</summary>
    [Fact]
    public async Task Switch_Save_SavesTheOldDatabase_ThenOpensTheTarget()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        await OpenWithUnsavedEditAsync(a);
        _prompts.ExpectThreeChoice(HistoryText.SwitchTitle("a", "b"), ThreeChoiceResult.First);

        Assert.True(await _lifecycle.SwitchDatabaseAsync(b));
        SettleLocks();

        Assert.Equal(SessionEdit, NewestValue(a));
        Assert.Equal(b, _checkout.SourcePath);
        AssertScriptConsumed();
    }

    /// <summary>Switch, Cancel: the switch stops, the old database stays open, checked out and marked; nothing is deleted.</summary>
    [Fact]
    public async Task Switch_Cancel_StaysOnTheCurrentDatabase()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        var working = await OpenWithUnsavedEditAsync(a);
        _prompts.ExpectThreeChoice(HistoryText.SwitchTitle("a", "b"), ThreeChoiceResult.Third);

        Assert.False(await _lifecycle.SwitchDatabaseAsync(b));

        Assert.Equal(a, _checkout.SourcePath);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.True(File.Exists(working));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.True(_guard.IsHeld(a));
        Assert.False(_guard.IsHeld(b));
        Assert.Empty(HostTrace());
        AssertScriptConsumed();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // RESTORE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Restore, Don't Save: the restore goes ahead although the dirty marker exists (no save first); D becomes the backup
    /// and the pre-restore copy holds the old D without the discarded edit.
    /// </summary>
    [Fact]
    public async Task Restore_DontSave_GoesAheadDespiteTheMarker()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        Assert.True(File.Exists(MarkerOf(working)));
        var backup = NewBackup("from-backup");
        _prompts.ExpectThreeChoice(HistoryText.RestoreTitle("dept"), ThreeChoiceResult.Second)
                .ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage("Backup Restored");

        await _lifecycle.RestoreFromBackupAsync(backup);

        Assert.Equal("from-backup", NewestValue(db));
        var preRestore = Assert.Single(Directory.GetFiles(_shareDir, "dept (before restore *.db"));
        Assert.Equal("original", NewestValue(preRestore));
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        AssertScriptConsumed();
    }

    /// <summary>Restore, Save: the change is saved first, so the pre-restore copy holds it; then D becomes the backup.</summary>
    [Fact]
    public async Task Restore_Save_SavesFirst_ThenRestores()
    {
        var db = NewDb();
        await OpenWithUnsavedEditAsync(db);
        var backup = NewBackup("from-backup");
        _prompts.ExpectThreeChoice(HistoryText.RestoreTitle("dept"), ThreeChoiceResult.First)
                .ExpectWait(RestorePrompt.WaitTitle, RestorePrompt.WaitMessage)
                .ExpectMessage("Backup Restored");

        await _lifecycle.RestoreFromBackupAsync(backup);

        Assert.Equal("from-backup", NewestValue(db));
        var preRestore = Assert.Single(Directory.GetFiles(_shareDir, "dept (before restore *.db"));
        Assert.Equal(SessionEdit, NewestValue(preRestore));
        AssertScriptConsumed();
    }

    /// <summary>Restore, Cancel: nothing is restored or saved, and the session carries on with its unsaved changes.</summary>
    [Fact]
    public async Task Restore_Cancel_DoesNothing()
    {
        var db = NewDb();
        var working = await OpenWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);
        var backup = NewBackup("from-backup");
        _prompts.ExpectThreeChoice(HistoryText.RestoreTitle("dept"), ThreeChoiceResult.Third);

        await _lifecycle.RestoreFromBackupAsync(backup);

        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.Empty(Directory.GetFiles(_shareDir, "dept (before restore *"));
        Assert.True(File.Exists(MarkerOf(working)));
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.False(_lifecycle.IsRestoreInProgress);
        AssertScriptConsumed();
    }

    /// <summary>Without the discard flag the checkout service still refuses a restore while the dirty marker exists.</summary>
    [Fact]
    public async Task CheckoutRestore_WithoutTheFlag_StillRefusesWhileTheMarkerExists()
    {
        var db = NewDb();
        await OpenWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);

        var result = await _checkout.RestoreFromBackupAsync(NewBackup("from-backup"));

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CRASH RECOVERY
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opening and restoring a crashed session's changes no longer saves them: D is unchanged and the kept working copy
    /// and its marker are still there, so the changes stay unsaved until the user presses Save.
    /// </summary>
    [Fact]
    public async Task CrashRecovery_Restore_DoesNotSave_ChangesStayUnsaved()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        var before = DbFileTestHelpers.HashOf(db);
        _prompts.ExpectChoice("Restore Unsaved Changes?", true);

        Assert.Null(await StartupAsync(db));

        Assert.Equal(kept, _checkout.WorkingPath);
        Assert.Equal(UnsavedValue, NewestValue(_checkout.WorkingPath));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.Equal("original", NewestValue(db));
        Assert.True(File.Exists(MarkerOf(kept)));
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.True(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }
}
