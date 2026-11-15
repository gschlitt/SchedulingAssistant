# 1.2.4 items 4.4 + 4.5: a database-level read-only guard, and the "sticky" read-only flag

Status: **built 2026-11-10** (approved by Greg the same day). MinimumVersionToWrite: **No** (nothing new is saved; a read-only
session already saves nothing).

## The story

**Today.** Bob opens the shared database while Alice holds the lock, so Bob is a reader working on a local snapshot.
Every repository write first calls `MarkDirty()`, which refuses with "you can't edit" (`WriteAccessLostException`).
That's the only guard. If a future change adds a write that forgets `MarkDirty()` (a new repository method, a
cleanup run on open, a raw `Connection` command), Bob's edit lands silently in his throw-away snapshot and vanishes at
the next Refresh. Nobody is told: the rule-4 failure 1.2.3 fixed for the known paths.

**With 4.4.** Bob's connection also has SQLite's own switch on (`PRAGMA query_only=ON`). A write that slipped past
`MarkDirty()` fails at SQLite instead of landing. A loud error, never a silent loss. Alice (the writer) is untouched:
her connection never has the switch.

## Survey (2026-11-10)

- Every repository write calls `MarkDirty()` today (18 repositories checked). The writes without it are the open's own
  schema / migrate / seed (must still run on a snapshot of an older database: the switch goes on **after** them, as
  `_readOnly` already does), the wizard / new-database / legacy import seeding (writer or brand-new files only), and
  `MinimumVersionToWrite.Raise` (opens its own connection by path; unaffected, since the pragma is per connection).
- Background readers/writers (backup `VACUUM INTO`, the save's snapshot, CSV export) use their own connections:
  unaffected.
- `query_only` doesn't block WAL checkpoints, `BEGIN`/`COMMIT` of read-only transactions, or `PRAGMA journal_mode`
  (and the WAL pragma runs first anyway).

## Change (4.4)

In `DatabaseContext`, one helper `ApplyReadOnly(bool readOnly)` sets `_readOnly` and, when true, runs
`PRAGMA query_only=ON` on the connection. It replaces the two existing `_readOnly = readOnly;` lines:

1. the constructor, after schema / migrate / seed;
2. `ReinitializeConnection`, after `ConfigureConnection` (reader Refresh, demotion's reopen of D'' or the kept D').

**Not** in `MarkReadOnly()` (start of a demotion). It can run off the UI thread while the UI uses the connection, and
that connection is closed and reopened (with the switch) seconds later. `MarkDirty()` covers the gap as today.

`ConfigureConnection` turns the switch OFF at the start of every open (a pooled connection keeps its pragmas), so a
writer after a reader, or the open's own setup, is never affected.

**Found while building (full-suite run 1, 3 failures):** Microsoft.Data.Sqlite's `BeginTransaction()` sends
`BEGIN IMMEDIATE`, which asks for the write lock. `query_only` refuses that, so the five places that wrap a multi-step
write in a transaction (section import, semester contents, copy semester, room delete, scheduling-environment delete)
got a raw SQLite error in a read-only session, thrown *outside* their `try`, before the first repository write could
give the plain refusal. Fix: `IDatabaseContext.BeginWriteTransaction()` (default: the old nullable-tx pattern, so the
demo is unchanged); `DatabaseContext` overrides it to start a deferred `BEGIN` in a read-only session (allowed under
`query_only`) and the usual `BEGIN IMMEDIATE` for a writer (unchanged). The five sites use it, so a reader sees exactly
what it saw before 4.4. Any new write transaction should use it too.

**If the backstop ever fires**, the user sees whatever that call site shows for a `SqliteException` today (some sites
show raw `ex.Message`, spec follow-up "about 14 error sites"). That's acceptable for a should-never-happen backstop. The
log records it, so the missing `MarkDirty()` gets found. No new wording.

## Change (4.5)

`IsReadOnlyMode` is **no longer sticky**: since `454a38c` (3.3) `BeginSession()` clears it at the start of every
checkout, and a failed open clears it too. The only production reader is the guard in `RefreshReadOnlySnapshotAsync`,
together with `Mode == ReadOnly`, which is correct. So 4.5 is: correct the four stale comments that still call it
sticky (`App.axaml.cs`, `MainWindowViewModel`, `ReadOnlySessionTests`, harness `InvariantChecker`), and add one test:
reader → writer on the same `CheckoutService` leaves `IsReadOnlyMode` false. (A demoted writer has `Mode == ReadOnly`
with `IsReadOnlyMode` false until its D'' is made. That's intended: it means "on a snapshot", not "may not write".)

## Tests

- `DatabaseContext(readOnly: true)`: a raw `INSERT` on `Connection` throws `SqliteException` (SQLITE_READONLY);
  reads work; an older-schema file still migrates (the switch goes on after).
- `readOnly: false`: a raw `INSERT` works.
- `ReinitializeConnection(path, true)` → raw write throws; `ReinitializeConnection(path, false)` → works.
- The 4.5 pin test above. Then the full suite with the harness (read-only sessions run throughout it).

## The four rules

1. **Atomic rename of a checked copy:** untouched; no save, lock or rename path changes.
2. **Write only with the lock and an unchanged database:** strengthened. A session without the lock now can't change
   even its local copy through the main connection.
3. **Never delete the only copy of unsaved work:** untouched. The demotion's kept D' is reopened read-only as today; the
   switch only blocks *new* writes to it, and the save's snapshot / recovery use separate connections.
4. **Never confuse the user:** a silent loss becomes an error in the should-never-happen case; no new messages.

**Cascade check:** the pragma runs only at open / reopen, on the thread already doing the open, before the context is
used. Wake check, heartbeat, save, exit save and reader poll don't touch it.
