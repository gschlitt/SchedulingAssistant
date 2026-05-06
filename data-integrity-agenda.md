# Data Integrity & Concurrency Bug — Implementation Agenda

> Generated 2026-05-04 from a full codebase audit of `src/SchedulingAssistant/`.
> Bring this file to a fresh conversation as your work-order.
> All findings include: scenario, test strategy, fix location, effort estimate.

---

## How to use this document

1. Work P0 findings first — they can cause silent data loss on any save.
2. P1 findings are high-probability correctness bugs; fix before next release.
3. P2 findings are moderate probability or limited blast-radius; fix in a follow-up sprint.
4. P3 findings are low-probability / cosmetic robustness; handle last.
5. After fixing each finding, run `dotnet test` (512 tests in `SchedulingAssistant.Tests`).
6. The test project is at `src/SchedulingAssistant.Tests/`.

---

## P0 — Data Loss / Corruption (Fix Before Shipping)

---

### F1 — BackupService uses shared `SqliteConnection` from a background thread

**Priority:** P0  
**File:** `src/SchedulingAssistant/Services/BackupService.cs`  
**Lines:** ~190 (`PerformBackupAsync`, VACUUM INTO step) and ~266 (`TakeDbSnapshot`)

**Scenario:**
`SqliteConnection` is not thread-safe. `BackupService` holds a reference to `_db.Connection` (the
singleton connection shared by all repositories). Two code paths call `VACUUM INTO` using this
connection from non-UI threads:

1. **Periodic timer** — `RunGuardedBackupAsync` fires on a `System.Threading.Timer` thread pool thread
   and calls `PerformBackupAsync`, which does:
   ```csharp
   using var cmd = _db.Connection.CreateCommand();
   cmd.CommandText = "VACUUM INTO $path";
   ```
2. **Pre-save backup** — `CheckoutService.TakePreSaveBackup()` calls `_backupService.TakeDbSnapshot()`
   synchronously from `SaveAsyncCore`, which runs on whatever thread called `SaveAsync` (the autosave
   timer thread or the UI thread).

If a UI write (INSERT/UPDATE from any repository) is in flight at the same moment VACUUM INTO
executes, SQLite can throw `SQLITE_MISUSE` or silently corrupt the backup. The main database is
not at risk from VACUUM INTO itself, but if `BackupSqliteDatabase` in CheckoutService is running
concurrently (it opens fresh connections), and the shared connection is simultaneously mid-write,
you get undefined behaviour.

**Test to write:**
```csharp
// BackupServiceTests.cs
[Fact]
public async Task TakeDbSnapshot_DoesNotUseSharedConnection()
{
    // Arrange: create a BackupService that exposes which connection VACUUM INTO ran on.
    // Assert: the connection used is NOT _db.Connection (i.e., a fresh one).
    // Implementation: subclass or mock IDatabaseContext, track Connection.GetHashCode()
    // called during TakeDbSnapshot vs. the one injected.
}

[Fact]
public async Task PerformBackupAsync_DoesNotBlockUiWriteTransaction()
{
    // Arrange: start a long-running write transaction on _db.Connection in a background thread.
    // Act: call PerformBackupAsync() concurrently.
    // Assert: no SqliteException thrown; backup file exists and is valid.
}
```

**Fix:**
Both `TakeDbSnapshot` and `PerformBackupAsync` must NOT use `_db.Connection`. Instead, open a
fresh, pooled `SqliteConnection` to `_db.DatabasePath` for each VACUUM INTO:

```csharp
// Replace the _db.Connection.CreateCommand() pattern with:
using var conn = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=False");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "VACUUM INTO $path";
```

`IDatabaseContext` will need a `string DatabasePath { get; }` property added (already has the
path at construction time in `DatabaseContext`).

**Effort:** ~2 hours

---

### F2 — `DeleteDirtyMarker` gap: writes between step 4 and step 7 go undetected

**Priority:** P0  
**File:** `src/SchedulingAssistant/Services/CheckoutService.cs`  
**Lines:** ~577 (step 3, TakePreSaveBackup), ~588 (step 4, BackupSqliteDatabase), ~650 (step 7, DeleteDirtyMarker)

**Scenario:**
`SaveAsyncCore` proceeds through these steps:

```
Step 3: TakePreSaveBackup()           ← snapshot of D' taken HERE
Step 4: BackupSqliteDatabase D'→D.tmp  ← consistent copy of D' taken HERE
Step 5: hash D.tmp
Step 6: rename D.tmp → D              ← D is now updated
Step 7: DeleteDirtyMarker()           ← marker deleted HERE
```

The backup API (step 4) works cooperatively with the SQLite WAL. After it returns, the user
or autosave can write to D' — those writes would land in D' **after** the snapshot at step 4.

`DatabaseContext.MarkDirty()` (which re-writes the marker) uses `Interlocked.CompareExchange`
on a `_dirtyFired` flag. After step 7, `_dirtyFired` is still `1` (it was set during the save).
`ResetDirty()` is called via the `SaveCompleted` event, which fires **after** step 7. This means
there is a window:

```
Thread A (save):   BackupSqliteDatabase → ... → DeleteDirtyMarker → SaveCompleted → ResetDirty
Thread B (UI):     user types → MarkDirty() → Interlocked CAS fails (flag still 1) → marker NOT written
```

If the app crashes between Thread B's write and `ResetDirty()` running, D'.dirty does not exist,
so crash recovery reports "clean exit" and the user's last edit is silently discarded.

The window is narrow but closes only after `SaveCompleted` propagates and `ResetDirty` resets
`_dirtyFired` to 0. On a slow UI thread (e.g., grid rebuild during semester switch), this can be
hundreds of milliseconds.

**Test to write:**
```csharp
[Fact]
public async Task SaveAsync_WriteDuringStep4ToStep7_DirtyMarkerRewritten()
{
    // Arrange: fake CheckoutService with synchronous dispatcher.
    //          Wire DatabaseContext so MarkDirty() is called after BackupSqliteDatabase returns
    //          but before DeleteDirtyMarker() executes (use a hook/subclass).
    // Act: SaveAsync().
    // Assert: after ResetDirty fires, MarkDirty() can successfully write the marker.
    //         (i.e., _dirtyFired is 0 and the marker file exists if MarkDirty was called in the window)
}
```

**Fix:**
Move `ResetDirty()` to fire **before** `DeleteDirtyMarker()`, not after. Specifically, call
`dbCtx.ResetDirty()` at the top of step 7 so `_dirtyFired` is reset to 0 before the marker is
deleted. Any write that arrives between reset and deletion will call `MarkDirty()`, which will
re-write the marker.

```csharp
// Step 7 — correct ordering:
dbCtx.ResetDirty();       // ← add this; arm MarkDirty before deleting marker
DeleteDirtyMarker();
if (releaseLockAfter) _lockService.Release();
_dispatch(() => SaveCompleted?.Invoke());
```

`CheckoutService` currently does not hold a reference to `IDatabaseContext` (by design — avoids
circular DI). The cleanest fix is to fire an `AboutToDeleteDirtyMarker` event that
`DatabaseContext` subscribes to, calling `ResetDirty()` in the handler. Alternatively, expose
`ResetDirty` as an `Action` callback set at startup (similar to how `SaveCompleted` is used).

**Effort:** ~3 hours

---

## P1 — High-Probability Correctness Bugs

---

### F3 — `_saveInFlight` is a non-volatile, non-Interlocked plain `bool`

**Priority:** P1  
**File:** `src/SchedulingAssistant/Services/CheckoutService.cs`  
**Line:** ~112 (`private bool _saveInFlight;`), ~510–523 (check and set)

**Scenario:**
`SaveAsync` is called from the UI thread (explicit save button) and from a `System.Threading.Timer`
callback (autosave). Both paths read and write `_saveInFlight` without synchronisation:

```csharp
if (_saveInFlight)          // read (thread A)
    return SaveOutcome.CopyError;
_saveInFlight = true;       // write (thread A) — non-atomic check-then-set
```

If thread A (autosave timer) and thread B (UI button) both pass the `if (_saveInFlight)` check
before either sets it, two concurrent saves run. The second call to `BackupSqliteDatabase` and
the subsequent rename will either fail with a sharing violation or produce a half-written D.

Additionally, the C# memory model does not guarantee a non-volatile `bool` write is visible
across threads without a fence.

**Test to write:**
```csharp
[Fact]
public async Task SaveAsync_ConcurrentCalls_OnlyOneSucceeds()
{
    // Arrange: CheckoutService in WriteAccess mode, synchronous dispatcher.
    // Act: call SaveAsync() from 10 concurrent Task.Run() with no delay.
    // Assert: exactly 1 SaveOutcome.Success; rest are SaveOutcome.CopyError.
    //         No exception thrown.
}
```

**Fix:**
Replace the plain bool with `Interlocked.CompareExchange`:

```csharp
private int _saveInFlight; // 0 = idle, 1 = in-flight

public async Task<SaveOutcome> SaveAsync(bool releaseLockAfter = false)
{
    if (Mode != CheckoutMode.WriteAccess)
        return SaveOutcome.NotInWriteMode;

    if (Interlocked.CompareExchange(ref _saveInFlight, 1, 0) != 0)
    {
        _logger.LogInfo("CheckoutService: SaveAsync skipped — a save is already in progress.");
        return SaveOutcome.CopyError;
    }

    try   { return await SaveAsyncCore(releaseLockAfter); }
    finally { Volatile.Write(ref _saveInFlight, 0); }
}
```

**Effort:** ~30 minutes

---

### F8 — Autosave timer can fire against a disposed `BackupService` after DI rebuild

**Priority:** P1  
**File:** `src/SchedulingAssistant/MainWindow.axaml.cs` (calls `StopSession`/`StartAutoSave`),
`src/SchedulingAssistant/Services/CheckoutService.cs` (`StartAutoSave`, `AutoSaveTickAsync`),
`src/SchedulingAssistant/Services/BackupService.cs`

**Scenario:**
When the user switches databases (`SwitchDatabaseAsync`), `App.InitializeServices` disposes the
old DI container and builds a new one. The old `BackupService` singleton is disposed (stopping
its periodic timer). However, `CheckoutService` is a **static singleton** (`App.Checkout`) that
lives outside DI. Its autosave timer is NOT stopped during the DI rebuild.

If the autosave timer fires during the window between old DI disposal and new DI
`SetBackupService()` being called, `_backupService` is null (or stale) inside `CheckoutService`.
`TakePreSaveBackup()` is null-guarded (`_backupService?.TakeDbSnapshot()`) so it silently skips
the backup, which is the intended behaviour for a null reference. **However**, if the timer fires
*while* `InitializeServices` is rebuilding the new `DatabaseContext` (the D' file is being
re-opened), `BackupSqliteDatabase` proceeds against the in-transition connection and can produce
a corrupt D.tmp.

**Test to write:**
```csharp
[Fact]
public async Task CheckoutService_DISwitchDuringAutosave_DoesNotCorrupt()
{
    // Arrange: CheckoutService with autosave timer at 1ms interval.
    //          Simulate DI teardown+rebuild by calling SetBackupService(null) then
    //          SetBackupService(newService) with a 50ms delay between.
    // Act: allow timer to fire during the null window.
    // Assert: SaveAsync returns CopyError or Success, never throws. D file intact.
}
```

**Fix:**
In `MainWindow.axaml.cs` (the `SwitchDatabaseAsync` path), call `App.Checkout.StopAutoSave()`
**before** the old DI container is disposed and call `App.Checkout.StartAutoSave()` **after**
the new `SetupMainWindowAsync` completes and `SetBackupService` has been called with the new
instance.

**Effort:** ~1 hour

---

### F9 — `ReleaseAsync(saveFirst: true)` races with the autosave timer

**Priority:** P1  
**File:** `src/SchedulingAssistant/Services/CheckoutService.cs`  
**Lines:** ~900 (`ReleaseAsync`)

**Scenario:**
`ReleaseAsync(saveFirst: true)` calls `StopAutoSave()` then `SaveAsync(releaseLockAfter: true)`.
`Timer.Dispose()` (inside `StopAutoSave`) is documented to not wait for in-flight timer
callbacks. If the timer fired just before `Dispose()`, `AutoSaveTickAsync` is already queued
on the thread pool. Both the in-flight autosave and the explicit `SaveAsync` from `ReleaseAsync`
can run concurrently.

`_saveInFlight` (the plain bool from F3) is the only guard. Without the `Interlocked` fix from
F3, both saves can proceed simultaneously. With the F3 fix, one will be correctly skipped.

This finding is **automatically resolved** by fixing F3. It is listed separately because it is
an independent execution path that should be tested in isolation.

**Test to write:**
```csharp
[Fact]
public async Task ReleaseAsync_AutoSaveFiredDuringRelease_OnlyOneSaveCompletes()
{
    // Arrange: 1ms autosave timer, WriteAccess mode.
    // Act: call ReleaseAsync(saveFirst: true) while timer is firing.
    // Assert: exactly one SaveOutcome.Success emitted via SaveCompleted event.
    //         Mode transitions to ReadOnly.
}
```

**Fix:** Implement F3 first. Then add `await Task.Yield()` after `StopAutoSave()` in `ReleaseAsync`
to allow any in-flight timer callback to complete before proceeding.

**Effort:** ~1 hour (after F3 is done)

---

## P2 — Moderate Risk / Limited Blast Radius

---

### F4 — `RotateBackups` deletes pre-save snapshots produced by `TakeDbSnapshot`

**Priority:** P2  
**File:** `src/SchedulingAssistant/Services/BackupService.cs`  
**Lines:** ~542 (`RotateBackups`), ~263 (`TakeDbSnapshot`)

**Scenario:**
`TakeDbSnapshot()` writes `{prefix}_{timestamp}.db` — the same naming pattern as files produced
by `PerformBackupAsync`. `RotateBackups` gathers all `{prefix}_*.db` files in the backup folder
and deletes the oldest until only `MaxBackupCount` remain. Pre-save snapshots are
indistinguishable from periodic backups in this glob.

Result: after 10 saves on a database with `MaxBackupCount = 10`, periodic backup files are
silently rotated out ahead of schedule because pre-save snapshots consumed slots.

**Test to write:**
```csharp
[Fact]
public void RotateBackups_PreSaveSnapshotsDoNotConsumeRotationSlots()
{
    // Arrange: create 9 {prefix}_{ts}.db files (periodic) and 3 {prefix}_{ts}_presave.db.
    // Act: call RotateBackups(folder, prefix, maxCount: 9).
    // Assert: all 9 periodic backups survive; presave files are not rotated.
    //         (or: only presave files over the limit are rotated, not periodic ones)
}
```

**Fix:**
Distinguish pre-save snapshots with a filename suffix, e.g. `{prefix}_{timestamp}_presave.db`.
Change `TakeDbSnapshot` to use this naming convention. Update `RotateBackups` to
sort the two lists separately and apply the count cap only to periodic backups.

**Effort:** ~2 hours

---

### F5 — `SectionRepository.Delete` has no `DbTransaction?` parameter

**Priority:** P2  
**File:** `src/SchedulingAssistant/Data/Repositories/SectionRepository.cs`  
**Line:** ~128

**Scenario:**
`SectionRepository.Insert` and `SectionRepository.Update` both accept an optional
`DbTransaction? tx` parameter that callers use for multi-step atomic operations (e.g.,
`RoomListViewModel.Delete` wraps section updates + room delete in a single transaction).
`SectionRepository.Delete` does not. Callers that need to delete a section as part of a
larger atomic operation (e.g., delete a semester and all its sections together) cannot enlist
the deletion in their transaction, creating a window where sections are gone but the semester
record still exists (or vice versa) on a crash.

**Test to write:**
```csharp
[Fact]
public void Delete_WithTransaction_RollsBackOnFailure()
{
    // Arrange: in-memory SQLite DB with one section.
    // Act: begin tx, call Delete(id, tx), then throw before commit, then rollback.
    // Assert: section still exists after rollback.
}
```

**Fix:**
Add the optional parameter consistent with `Insert`/`Update`:

```csharp
public void Delete(string id, System.Data.Common.DbTransaction? tx = null)
{
    db.MarkDirty();
    using var cmd = db.Connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "DELETE FROM Sections WHERE id = $id";
    cmd.AddParam("$id", id);
    cmd.ExecuteNonQuery();
}
```

Update `ISectionRepository` interface accordingly.

**Effort:** ~30 minutes

---

### F6 — `IsMigrationNeeded` proxy check becomes stale when new migrations are added

**Priority:** P2  
**File:** `src/SchedulingAssistant/Data/DatabaseContext.cs`  
**Lines:** ~281 (`IsMigrationNeeded`), ~262 (last `AddColumnIfMissing` call)

**Scenario:**
`IsMigrationNeeded` uses the presence of `InstructorCommitments.instructor_name` as a proxy to
determine whether all `AddColumnIfMissing` migrations have been applied. The comment reads:
> "Check a late-stage readable column as a proxy for 'all columns added'. If this column exists,
> all earlier AddColumnIfMissing calls are also complete."

This assumption holds only as long as `instructor_name` remains the **last** column added in
`Migrate()`. The next developer to add a new `AddColumnIfMissing` call after line 262 will not
see an obvious requirement to also update `IsMigrationNeeded`. The proxy silently lies — the new
column is never added to existing databases, and existing data silently loses the new readable
column without error.

**Test to write:**
```csharp
[Fact]
public void IsMigrationNeeded_NewColumnAddedToMigrate_IsDetected()
{
    // Arrange: open a DB that has instructor_name but is missing a hypothetical
    //          new column "Sections.new_column".
    // Act: call IsMigrationNeeded().
    // Assert: returns true.
    // (This test documents the contract and will fail if the proxy is not updated.)
}
```

**Fix — Option A (recommended):** Replace the proxy with an explicit version stamp. Add a
`schema_version` key to a `DbConfiguration` key-value table (or to `PRAGMA user_version`).
`IsMigrationNeeded` returns `true` when `user_version < CURRENT_SCHEMA_VERSION`. `Migrate()`
increments the version at the end of the transaction. Each future migration bump increments the
constant.

**Fix — Option B (minimal):** Replace the single proxy check with an exhaustive column check
that is regenerated automatically. Write a helper:

```csharp
private static readonly (string table, string column)[] _requiredColumns = [
    ("LegalStartTimes",             "academic_year_id"),
    // ... all AddColumnIfMissing targets ...
    ("InstructorCommitments",       "semester_name"),  // last as of 2026-05-04
];

private bool IsMigrationNeeded()
{
    // ... existing table rename check ...
    foreach (var (table, col) in _requiredColumns)
        if (!ColumnExists(table, col)) return true;
    return false;
}
```

**Effort:** ~2 hours for Option A, ~1 hour for Option B

---

### F7 — `Migrate()` silently drops `SchedulingEnvironmentValues` data if it already exists

**Priority:** P2  
**File:** `src/SchedulingAssistant/Data/DatabaseContext.cs`  
**Lines:** ~226–232 (`DROP TABLE IF EXISTS SchedulingEnvironmentValues`, then rename)

**Scenario:**
The rename migration does:
```csharp
cmd.CommandText = "DROP TABLE IF EXISTS SchedulingEnvironmentValues";
cmd.ExecuteNonQuery();
cmd.CommandText = "ALTER TABLE SectionPropertyValues RENAME TO SchedulingEnvironmentValues";
cmd.ExecuteNonQuery();
```

The intent is: if `InitializeSchema` already created an empty `SchedulingEnvironmentValues`
shell, drop it first so the rename succeeds. The guard `if (oldTableExists)` correctly
prevents this from running on already-migrated databases. However, on a database that was
partially migrated (e.g., `SectionPropertyValues` still exists AND `SchedulingEnvironmentValues`
has real data due to a failed previous migration), the `DROP TABLE` silently destroys the real
data before the rename overwrites it.

**Test to write:**
```csharp
[Fact]
public void Migrate_WhenBothTablesExistWithData_DoesNotLoseSchedulingEnvironmentData()
{
    // Arrange: DB with populated SectionPropertyValues AND SchedulingEnvironmentValues.
    // Act: Migrate().
    // Assert: SchedulingEnvironmentValues rows were not deleted.
    //         (Either abort and throw, or merge, depending on chosen fix.)
}
```

**Fix:**
Before the DROP, check whether `SchedulingEnvironmentValues` has any rows. If it does, log an
error and abort the migration rather than silently destroying data:

```csharp
cmd.CommandText = "SELECT COUNT(*) FROM SchedulingEnvironmentValues";
var existingRowCount = Convert.ToInt32(cmd.ExecuteScalar());
if (existingRowCount > 0)
    throw new InvalidOperationException(
        "Migration conflict: both SectionPropertyValues and SchedulingEnvironmentValues exist with data.");

cmd.CommandText = "DROP TABLE SchedulingEnvironmentValues";
cmd.ExecuteNonQuery();
```

**Effort:** ~1 hour

---

### F10 — SQLite connection pool holds D' open after DI rebuild, blocking atomic rename on Windows

**Priority:** P2  
**File:** `src/SchedulingAssistant/Data/DatabaseContext.cs`  
**Lines:** Dispose / `CloseConnection` (`ClearAllPools()` call)  
`src/SchedulingAssistant/App.axaml.cs` line ~110 (`(Services as IDisposable)?.Dispose()`)

**Scenario:**
When `InitializeServices` is called for a second time (database switch), it disposes the old
`IServiceProvider`. This triggers `DatabaseContext.Dispose()`, which calls
`SqliteConnection.ClearAllPools()`. However, if the old `BackupService` periodic timer fires
between `(Services as IDisposable)?.Dispose()` and the new container completing construction,
the timer can create a new pooled connection to D' (via `_db.Connection.CreateCommand()`)
after `ClearAllPools()` was called, re-adding D' to the pool. The subsequent
`CheckoutService.SaveAsync` rename of D.tmp → D succeeds, but D' is left with an open pool
handle on Windows — a subsequent `CleanupWorkingCopy()` fails with `IOException: file in use`.

The working copy then accumulates on disk because it can never be deleted cleanly, and the next
crash-recovery check sees a stale D'.

**Test to write:**
```csharp
[Fact]
public async Task DISwitchDuringBackupTimer_WorkingCopyIsDeletedCleanly()
{
    // Arrange: 1ms backup timer, real temp SQLite file.
    // Act: dispose DI container while timer is running; immediately create new container.
    // Assert: D' file no longer exists after CleanupWorkingCopy().
}
```

**Fix:**
Call `App.Checkout.StopAutoSave()` AND stop `BackupService.StopSession()` **before**
`(Services as IDisposable)?.Dispose()` in `InitializeServices`. The new BackupService session
should not start until `SetupMainWindowAsync` completes. (This partially overlaps the F8 fix.)

**Effort:** ~1 hour

---

### F14 — `CopySemesterViewModel.ExecuteCopy` calls `BeginTransaction` without WASM guard

**Priority:** P2  
**File:** `src/SchedulingAssistant/ViewModels/Management/CopySemesterViewModel.cs`  
**Line:** ~249

**Scenario:**
```csharp
using var tx = _db.Connection.BeginTransaction();
```

`DemoDatabaseContext` (WASM) has `SupportsTransactions = false` and throws
`NotSupportedException` on `Connection` access. Calling `ExecuteCopy` in the browser demo
crashes with an unhandled exception rather than gracefully falling back.

**Test to write:**
```csharp
[Fact]
public void ExecuteCopy_WhenTransactionsNotSupported_CompletesWithoutThrowing()
{
    // Arrange: CopySemesterViewModel with DemoDatabaseContext (SupportsTransactions = false).
    // Act: ExecuteCopy(emptySet).
    // Assert: no exception; sections were copied (or gracefully skipped if demo repos are read-only).
}
```

**Fix:** Apply the nullable-tx pattern already used in `RoomListViewModel` and others:

```csharp
using var tx = _db.SupportsTransactions ? _db.Connection.BeginTransaction() : null;
try
{
    // ... copy loop ...
    tx?.Commit();
}
catch
{
    tx?.Rollback();
    throw;
}
finally { tx?.Dispose(); }
```

**Effort:** ~30 minutes

---

## P3 — Low Risk / Robustness

---

### F11 — Heartbeat renewal failures are logged but never escalated

**Priority:** P3  
**File:** `src/SchedulingAssistant/Services/WriteLockService.cs`  
**Lines:** heartbeat timer callback (look for `LogError` on heartbeat failure)

**Scenario:**
If the heartbeat write fails (e.g., transient network hiccup, disk full), the `.lock` file's
`Heartbeat` field becomes stale. Another instance polls the lock, sees a stale heartbeat,
concludes the session is dead, and acquires write access — while this instance continues editing.
Both instances now believe they hold the write lock. On the next save from either instance,
one of them wins the atomic rename. The loser's edits are silently discarded.

This is the most catastrophic multi-user scenario. Currently, the app's only defence is that
the polling interval (60 s) vs the stale threshold would need to align perfectly.

**Test to write:**
```csharp
[Fact]
public async Task WriteLockService_HeartbeatFailure_RaisesHeartbeatFailedEvent()
{
    // Arrange: WriteLockService with a lock file path on a read-only directory.
    //          Use a 1ms heartbeat interval.
    // Act: let the heartbeat fire.
    // Assert: a HeartbeatFailed event is raised (once added).
    //         IsWriter transitions to false OR a warning flag is set.
}
```

**Fix:**
Add a `HeartbeatFailed` event to `WriteLockService`. After N consecutive failures (e.g., 3),
raise it. `CheckoutService` subscribes and routes through `HandleLockLossAsync` — the same
path already used for wake-check failures. This surfaces a banner without an immediate save
attempt, giving the user a chance to react.

**Effort:** ~3 hours

---

### F12 — `AppSettings.Save()` is non-atomic — crash during write corrupts settings

**Priority:** P3  
**File:** `src/SchedulingAssistant/Services/AppSettings.cs`  
**Line:** ~185

**Scenario:**
```csharp
File.WriteAllText(SettingsPath, json);
```

`SemesterContext.UpdateSelectedSemesters()` calls `AppSettings.Current.Save()` on **every
semester checkbox toggle**. If the app crashes mid-write (or the file is written on a network
drive with a flaky connection), `SettingsPath` contains a partial JSON string. On next startup,
`File.ReadAllText` + `JsonSerializer.Deserialize` throws (or returns null, falling back to
defaults), and the user loses their recent-database list and all preferences.

**Test to write:**
```csharp
[Fact]
public void AppSettings_Load_WithPartialJsonFile_FallsBackToDefaults()
{
    // Arrange: write a truncated JSON string to SettingsPath.
    // Act: AppSettings.Current (re-loads from disk).
    // Assert: no exception; returns a default AppSettings instance.
    //         (The current catch-all does handle this, but test documents the contract.)
}

[Fact]
public void AppSettings_Save_IsAtomicOnCrash()
{
    // NOTE: True atomicity requires tmp+rename. This test documents the gap.
    // Arrange: mock File.WriteAllText to throw halfway.
    // Assert: either the old settings file is intact (tmp+rename pattern),
    //         or the exception is surfaced rather than silently eaten.
}
```

**Fix — Option A (recommended):** Write to a temp file, then rename:

```csharp
public void Save()
{
    Directory.CreateDirectory(SettingsDir);
    var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    var tmp  = SettingsPath + ".tmp";
    File.WriteAllText(tmp, json);
    File.Move(tmp, SettingsPath, overwrite: true);
}
```

**Fix — Option B (minimal):** Throttle `Save()` calls from `UpdateSelectedSemesters` so it
does not write on every keystroke. Use a debounce of 500 ms.

**Effort:** ~30 minutes for Option A

---

### F13 — `BackfillReadableColumns` runs without a time limit on large databases

**Priority:** P3  
**File:** `src/SchedulingAssistant/Data/DatabaseContext.cs`  
**Lines:** `BackfillReadableColumns` method (bulk UPDATE statements on Sections, Instructors, etc.)

**Scenario:**
On a database with thousands of sections imported from legacy data, `BackfillReadableColumns`
runs a series of unbounded `UPDATE` statements inside a single migration transaction on startup.
There is no progress reporting, no timeout, and no cancellation token. On a slow machine with
a large database, this can freeze the splash screen for 10–30 seconds with no visual feedback
and no way to cancel.

**Test to write:**
```csharp
[Fact]
public void BackfillReadableColumns_LargeDatabase_CompletesWithinReasonableTime()
{
    // Arrange: insert 10,000 sections into an in-memory SQLite DB.
    // Act: time the call to Migrate().
    // Assert: completes in < 5 seconds. (Documents the perf contract.)
}
```

**Fix:**
Add a progress callback parameter to `Migrate()` (or fire an event) so the splash screen can
show "Upgrading database…" with a progress bar. The UPDATE statements themselves are already
optimal (single bulk UPDATE per table); the fix is purely about user visibility.

**Effort:** ~2 hours

---

### F15 — `RecentDatabases` written from `AddRecent` on any thread, read from UI thread

**Priority:** P3  
**File:** `src/SchedulingAssistant/Services/AppSettings.cs`  
**Lines:** `AddRecent` method

**Scenario:**
`AppSettings.Current` is a lazily-initialized static. `AddRecent` (called during
`SwitchDatabaseAsync`) mutates `RecentDatabases` and calls `Save()`. If autosave fires and
triggers a path that also touches `AppSettings.Current` on a different thread (e.g., via a
`BackupCompleted` handler that updates the last-backup timestamp), two threads can concurrently
write to the same `List<string>` field, which is not thread-safe, and call `File.WriteAllText`
concurrently.

**Test to write:**
```csharp
[Fact]
public void AppSettings_ConcurrentAddRecent_DoesNotThrow()
{
    // Arrange: 10 threads all calling AppSettings.Current.AddRecent() with different paths.
    // Assert: no exception; RecentDatabases has at most 10 entries.
}
```

**Fix:**
Add a `private static readonly Lock _settingsLock = new Lock()` guard around all `_instance`
mutations and `Save()` calls. Or move to `System.Collections.Concurrent.ConcurrentQueue` for
the recent list. Since `AppSettings` is simple JSON, a `lock` is sufficient and lowest-risk.

**Effort:** ~1 hour

---

## Suggested Implementation Order

| Step | Findings | Why |
|------|----------|-----|
| 1 | F3 | Unblocks F9; needed before any concurrency testing |
| 2 | F1 | P0, independent, straightforward fix |
| 3 | F2 | P0, requires F1-style `DatabasePath` property first |
| 4 | F9 | P1, resolved by F3; add test to confirm |
| 5 | F8 | P1, requires coordination with MainWindow |
| 6 | F10 | P2, same MainWindow pass as F8 |
| 7 | F5 | P2, trivial; do it while in SectionRepository |
| 8 | F14 | P2, trivial; do it while reviewing CopySemesterViewModel |
| 9 | F7 | P2, one-time migration hazard |
| 10 | F6 | P2, requires schema version decision |
| 11 | F4 | P2, purely cosmetic naming in BackupService |
| 12 | F12 | P3, atomic settings write |
| 13 | F11 | P3, heartbeat escalation (small feature) |
| 14 | F15 | P3, settings concurrency |
| 15 | F13 | P3, migration progress UI |

---

## Files to read at the start of a fresh session

Load these to establish context before touching any finding:

```
src/SchedulingAssistant/Services/CheckoutService.cs          (~1470 lines)
src/SchedulingAssistant/Services/BackupService.cs            (~738 lines)
src/SchedulingAssistant/Data/DatabaseContext.cs              (~497 lines)
src/SchedulingAssistant/Services/AppSettings.cs
src/SchedulingAssistant/Data/Repositories/SectionRepository.cs
src/SchedulingAssistant/ViewModels/Management/CopySemesterViewModel.cs
src/SchedulingAssistant/App.axaml.cs
src/SchedulingAssistant.Tests/                               (existing test patterns)
```

Key architectural fact to keep in mind: **`App.LockService` and `App.Checkout` are static
singletons that live outside the DI container.** They outlive any individual DI scope. The DI
container is rebuilt on every database switch. All other services (BackupService, DatabaseContext,
all repositories) are DI singletons that are torn down and replaced. CheckoutService holds a
settable `_backupService` reference (set via `SetBackupService`) precisely because BackupService
lives inside DI while CheckoutService does not.
