# Persistence Layer Review: Data Corruption, Loss, and UX Risks

## Overall Assessment

The architecture is **well-designed for the target scenario** (small team, network drive, single writer). The D/D' checkout pattern, SQLite Backup API usage, hash-based conflict detection, and atomic rename-into-place are all sound choices. There are, however, several real risks worth addressing, ranked by severity.

---

## Category 1: Data Corruption / Loss Risks

### 1.1 CRITICAL — Reader holds D open, blocking writer's `File.Move(D.tmp -> D)`

**Location**: `CheckoutService.SaveAsync()` line 412, `DatabaseContext.cs` line 23

A read-only instance opens a `SqliteConnection` directly against D (the source file on the network share). `DatabaseContext` is a **singleton** — it opens the connection in its constructor and holds it open for the entire app session. This is not a per-query open/close cycle. The handle to D is held persistently.

When the writer saves, it does `File.Move(tmpPath, SourcePath, overwrite: true)`, which calls `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING`. Because the reader's connection was not opened with `FILE_SHARE_DELETE` (Microsoft.Data.Sqlite does not set this flag by default), the move fails with `ERROR_SHARING_VIOLATION`.

**This is not a timing edge case.** The reader holds D open at all times, so every autosave attempt will fail as long as a reader is running.

**Impact**: Writer sees repeated `CopyError` saves. Data is safe in D', but never propagates to D while a reader is connected.

**WAL mode is not a fix**: SQLite explicitly does not support WAL mode on network filesystems. WAL requires a shared-memory file (`D-shm`) for reader/writer coordination that cannot cross a network boundary. Additionally, `PRAGMA journal_mode=WAL` requires write access — a read-only connection cannot set it. And D is always delivered in DELETE mode (created by the SQLite Online Backup API), so any WAL pragma from the reader would have no effect.

**The correct fix**: Extend the D/D' pattern to read-only mode — see proposed architecture below.

### 1.2 MEDIUM — No SQLite journal mode or synchronous pragma configured

**Location**: `DatabaseContext.cs` line 23 — connection string is bare `Data Source={dbPath}`

SQLite defaults to `journal_mode=DELETE` and `synchronous=FULL`. On network drives (used by the read-only instance reading D directly), `DELETE` journal mode can create a `.db-journal` file on the network share, which could cause `SQLITE_BUSY` errors if multiple readers are connected simultaneously.

### 1.3 MEDIUM — `CopyWithSharing` used at checkout time instead of SQLite Backup API

**Location**: `CheckoutService.cs` lines 233-234, 755-762

At checkout, D->D' uses raw `CopyWithSharing` (file stream copy), not `BackupSqliteDatabase`. If D is being read by another instance during the copy, a mid-read journal flush could produce a partially-inconsistent copy.

**Mitigation**: D is never written to by the app (only D' is written); the hash-verify-and-retry logic (lines 244-259) catches any inconsistent copy. Residual risk is very low.

### 1.4 LOW — Crash during `File.Move(D.tmp -> D)` leaves stale `HashAtCheckout`

If the process dies after `File.Move` succeeds (line 412) but before `HashAtCheckout` is updated (line 424):
- D has been replaced by D.tmp — data is saved
- `HashAtCheckout` still matches the *old* D
- D' exists with its dirty marker

On next startup, crash recovery attempts `SaveAsync`, which hashes D (now the new version) against `HashAtCheckout` (the old version) — hash mismatch → `SaveOutcome.SourceModified` → "Could not save your previous changes. They have been discarded."

**Impact**: No actual data loss (save succeeded), but the user sees a false "discarded" message.

**Likelihood**: Extremely low — power failure in a single-threaded sequence between two adjacent lines.

### 1.5 LOW — `SessionDirty` is set to `true` immediately at checkout, before any edits

**Location**: `CheckoutService.cs` line 263

`SessionDirty = true` is set right after D->D' copy, before the user makes any changes. A no-edit session still runs a full save cycle on close, and a crash during that session still triggers the recovery dialog on next launch.

**Mitigation**: Compare D' hash against D hash before saving and skip if identical.

---

## Category 2: UX Risks — Mysterious Notifications

### 2.1 HIGH — Autosave `CopyError` on transient network blips

**Location**: `CheckoutService.cs` line 367, `MainWindow.axaml.cs` line 715-718

When `SaveAsync` can't reach D (network blip), it raises `SaveFailed` with "Cannot reach the database location: {ex.Message}". The autosave timer keeps running (correct), but the error banner persists until the *next successful save* clears it — up to the full autosave interval (default 10 minutes).

**Recommendation**: Either (a) add "will retry automatically" to the message, (b) auto-dismiss the banner after ~30 seconds, or (c) log CopyError silently and only surface `LockLost`/`SourceModified` to the user.

### 2.2 HIGH — False session timeout after laptop sleep + slow Wi-Fi reconnect

**Location**: `CheckoutService.cs` lines 614-635, `MainWindow.axaml.cs` lines 722-734

After wake-from-sleep, `OnWake()` calls `VerifyLockIsOurs()`, which reads the `.lock` file from the network share. If Wi-Fi hasn't reconnected yet, `File.Exists(lockPath)` returns false (or `File.ReadAllText` throws), and the catch block returns `false` — session treated as timed out.

The user sees: *"Your editing session timed out and another user has taken over. Unsaved changes have been lost."* when in reality the network just hadn't reconnected.

**Recommendation**: Add a short retry loop (e.g., 3 attempts × 5 seconds) in `OnWake()` before declaring the lock lost. The wake detection gap threshold is 90 seconds — a 15-second retry budget is well within that.

### 2.3 MEDIUM — `SourceModified` leaves user stuck with no recovery guidance

**Location**: `CheckoutService.cs` lines 371-377

When the hash check detects external modification of D, autosave stops permanently (line 606) and the banner persists. The user is in write mode with edits in D', but saves are permanently disabled. The only recovery is to close and reopen the app, but the error message doesn't say that.

**Recommendation**: Append "Close and reopen the application to reload the latest data." to the error message.

### 2.4 MEDIUM — `LockLost` in autosave: minor concurrency ordering issue

**Location**: `CheckoutService.cs` lines 351-355, 605-606

`AutoSaveTickAsync` (timer thread) calls `SaveAsync`, which calls `HandleSessionTimeoutAsync` (posts to UI thread), then returns `LockLost`. `AutoSaveTickAsync` then calls `StopAutoSave`. The timer could fire a second time before the UI thread processes the mode change to `ReadOnly`. The second tick sees `Mode == WriteAccess` and calls `SaveAsync` again.

**Impact**: Benign — the second `SaveAsync` will call `VerifyLockIsOurs()` again, fail again, and the double `StopAutoSave` call is a no-op. But it's a latent fragility.

### 2.5 LOW — Backup folder unavailable fails silently

**Location**: `BackupService.cs` lines 164-171

If the backup folder goes offline, `PerformBackupAsync` logs the error and returns `FolderUnavailable` without notifying the user. Backups silently stop.

**Recommendation**: After N consecutive backup failures, surface a one-time warning banner.

---

## Category 3: Design Observations (Not Bugs)

### 3.1 `ClearAllPools()` is a heavy hammer

**Location**: `DatabaseContext.Dispose()` line 298

`SqliteConnection.ClearAllPools()` clears all pools in the process, not just those for this connection string. Safe today (single `DatabaseContext`), but fragile if multiple contexts are ever introduced.

### 3.2 Lock file on network share — SMB oplock caching

The `.lock` file lives on the network share. SMB client-side caching can delay visibility of heartbeat writes to other machines. The 60s heartbeat / 180s stale threshold provides a 3× buffer, but in degraded network conditions a reader could falsely detect a stale lock.

### 3.3 `File.Move` atomicity on network shares

`MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` is atomic on local NTFS but the guarantee is weaker on SMB. In practice, for small SQLite files with a single writer, the risk of a partial replace is negligible.

### 3.4 No `busy_timeout` configured

If any query hits `SQLITE_BUSY` (unlikely in single-writer mode on D', but possible during `VACUUM INTO`), it fails immediately. `PRAGMA busy_timeout = 5000` is cheap insurance.

---

## Proposed Fix for Issue 1.1: Extend D/D' to Read-Only Mode

Rather than patching around the open-handle problem, extend the existing checkout pattern so that read-only instances also work against a local copy:

- On read-only checkout, copy D → local D'' (same working directory, distinct name, e.g. `{hash}_schedule_ro.db`)
- Open `DatabaseContext` against D'', never against D
- D has no open handles from any app instance at any time
- `File.Move(D.tmp → D)` always succeeds

**Complications to resolve:**

| Complication | Resolution |
|---|---|
| Working path collision with writer's D' | Use a `_ro` suffix for read-only copies |
| Staleness — D'' reflects checkout time, not latest save | Existing Refresh button re-copies D → D''; add "last refreshed at HH:MM" indicator |
| Crash recovery confuses D'' for writer's D' | Don't write a dirty marker for read-only copies, or write a `D''.readonly` mode marker |
| Re-copy during refresh races with writer's `File.Move` | Hash-verify-and-retry logic already handles this in `CheckoutAsync` |
| Promoting to write mode from read-only | **Always discard D'' and do a fresh checkout of D** — never promote D'' directly. The existing `SwitchDatabaseAsync` flow handles this correctly. The user's view refreshes to current state on lock acquisition, which is correct. |
| Re-copy tears down `DatabaseContext` singleton | Refresh needs to dispose and reinitialize DI, same as `SwitchDatabaseAsync` — treat it as a lightweight DB switch to the same path |

**Key trade-off**: The reader's view is now an explicit snapshot rather than a quasi-live view. In practice this is already true (in-memory caches are only as fresh as the last `ReloadFromDatabase` call), so making staleness visible is more honest than the current implicit behaviour.

---

## Summary of Recommended Actions

| Priority | Issue | Fix |
|----------|-------|-----|
| **High** | 1.1 — Reader's persistent handle blocks every writer save | Extend D/D' pattern to read-only mode |
| **High** | 2.1 — CopyError banner persists for full autosave interval | Add "will retry" messaging or auto-dismiss |
| **High** | 2.2 — False session timeout after sleep + slow Wi-Fi | Retry loop in `OnWake()` before declaring lock lost |
| **Medium** | 2.3 — SourceModified leaves user stuck | Add "close and reopen" to error message |
| **Low** | 1.5 — `SessionDirty=true` before any edits | Skip save when D' hash matches D hash |
| **Low** | 3.4 — No busy_timeout | Add `PRAGMA busy_timeout = 5000` in `DatabaseContext` |
| **Low** | 2.5 — Silent backup failures | Warn after N consecutive failures |
