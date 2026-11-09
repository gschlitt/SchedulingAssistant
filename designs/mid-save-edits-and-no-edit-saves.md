# 1.2.4 item E: edits made during a save (3.2) and saves with no edits (3.4)

Status: **3.2 done: tasks 1–3 (2026-11-09, uncommitted).** Full suite with harness: 4537 passed, 0 failed, 2 skipped.
F2 is retired. Next: tasks 4–6 (3.4). The restore backstop answers `RestoreOutcome.Failed`
("Something went wrong, so the restore was stopped. Your database is unchanged."), because no existing outcome maps to
the save-failed text. The save-first refusal comes first, so that text is shown only to a caller that skips the
save-first.
Summary items in `designs/spec_version_1.2.4_summary.md`: 3.2 (known finding F2) and 3.4 (F5, plus F6 where it has
the F5 shape). 3.4's second route (the post-open save after Can't Restore) was already fixed as F23 in `454a38c`.
3.4's design and risk review come from the 1.2.3 spec, section 29; only the changes are repeated here.
`MinimumVersionToWrite`: **No** for both. 3.2 changes when a local marker file is deleted, and 3.4 means some saves
write nothing, so no save puts anything new in the database.
**No new wording.**

## The stories

### 3.2: Bob's edit during a slow save

**Today.** The share is very slow, so Bob's autosave takes 20 seconds. Partway through, Bob changes BIOL 210 B's room.
The save had already taken its snapshot, so the change isn't in it. The save lands and deletes the "unsaved changes"
marker. The change now exists only in Bob's copy on his computer, with nothing marking it as unsaved. Only the first
edit after a save writes the marker, and the one that wrote it this time came before the save. Then any of these
throws the change away:
- **The network gets worse** and Bob's heartbeats stop landing. After 170 s Alice takes over, and Bob's TermPoint goes
  read-only. It sees no marker, deletes his copy and opens a fresh read-only copy, which never had the change. Bob is
  probably told nothing was at stake, because the lost-lock message picks its words from the marker. The slowness that
  made the save long is the same slowness that leads to the takeover, so this is the likely route.
- **Bob closes TermPoint while the share is unreachable.** The exit save fails, and the close deletes his copy because
  there's no marker.
- **The laptop crashes or loses power.** At the next start, the leftover copy has no marker, so it's cleaned up as if
  it held nothing.

**With the fix.** The save notices that an edit arrived while it ran. It still lands, but it keeps the marker and only
updates the marker's record of what the shared database now holds. In all three cases Bob's copy is kept. At his next
open he's offered the change back, and the restore works, because the shared database is exactly what his own save
left there. Most of the time none of that happens: the next autosave carries the change and deletes the marker as
usual. The "Unsaved changes" indicator stays on until then, which is true.

### 3.4: a session with no edits ruins Bob's restore

**Today.** Alice restores `BIOL` from last week's backup. Bob had a crash earlier, so his kept changes are waiting on
his laptop. Carol opens `BIOL` to look something up, changes nothing, and closes. Her exit save rewrites the shared
database anyway, because the restored file's header differs in five bookkeeping fields. Bob opens `BIOL` and is
offered his changes. He chooses Restore and gets "Can't Restore Unsaved Changes", although nobody changed any data.

**With the fix.** Carol's save sees that her copy holds the same data as the shared database, and she has no marker,
so it writes nothing. The shared database is untouched, and Bob's restore works.

## How it works

### 3.2: an edit counter plus SQLite's own change counter

Two counters cover the two edges of the save. Neither one alone is enough.

1. **Edit counter `EditGeneration`** (a `long` in `CheckoutService`, changed with `Interlocked` only). Every user write
   increments it *before* it touches the database. `DatabaseContext.MarkDirty` gets a second callback, `onEveryWrite`,
   called on every write after the read-only check and **before** the first-write compare-and-swap. App wiring:
   `App.axaml.cs:467` passes `App.Checkout.NoteEdit`. Every other constructor passes null, as `onFirstWrite` already
   does.
2. **SQLite's change counter** (`PRAGMA data_version`). Read on the save's own source connection, its value changes
   whenever *another* connection (the app's) has committed. The snapshot step keeps that connection open from before
   the backup until step 6. It's local, holds no transaction between statements and blocks nobody.
3. **Before the backup** (step 3a), the save reads `G0 = EditGeneration` and `v0 = data_version` on the source
   connection, then runs the backup on that same connection.
   - A write committed before `v0` is in the snapshot.
   - A write committed after `v0` changes `data_version`.
   - A write that starts after `G0` changes the edit counter.
4. **Step 6, under one lock `_markerGate`** (which `CheckoutService.MarkDirty` also takes around its marker write):
   1. `BeforeDirtyMarkerDeleted` (which resets `DatabaseContext`'s first-write flag), then
   2. read `G1` and `v1`, then
   3. **unchanged:** `SessionDirty = false`, delete the marker (today's behaviour);
      **changed:** keep `SessionDirty` true and **rewrite** the marker (`WriteDirtyMarker`, which now carries the new
      `HashAtCheckout` and no pending hash). Log one line and the breadcrumb `Save: edit arrived during save`.

   Resetting the flag first makes the next edit write the marker again. That write waits for the lock, so it can't
   fall between our check and our delete.
5. **The window:** `SaveCompleted` is dispatched as today. When the marker was kept, `BecameDirty` is dispatched right
   after it, so the indicator comes back on.
6. **The lock at exit:** with `releaseLockAfter` and a kept marker, step 6 does *not* release the lock. `ReleaseAsync`
   then runs **one more** `SaveAsync(releaseLockAfter: true)`. If that one also keeps the marker (an edit during both
   exit saves while the "Saving and closing…" curtain blocks input; practically impossible), the lock is released
   anyway and `CleanupWorkingCopy` keeps the copy and marker (the F21 guard), so Bob is offered the change at the next
   open. To tell `ReleaseAsync` the marker was kept, the save sets an internal `LastSaveKeptMarker`, not a new
   `SaveOutcome`: every caller tests `== Success`, and the save did succeed.
7. **Restore from backup (found by the audit, 2026-11-09).** `SessionLifecycle.SaveThenRestoreAsync` tests only
   `== Success`. After a save that kept the marker it would replace D, and `EndSessionAfterRestoreAsync`'s
   `DiscardCrash` would then delete D' and the marker, losing the edit. Fix:
   - **One shared helper, `SaveUntilMarkerClearAsync(releaseLockAfter)`:** it runs a save and, if that save kept the
     marker, exactly one more. `ReleaseAsync` (step 6) and `SaveThenRestoreAsync` both use it.
   - **If the marker is still kept,** the restore isn't started, and Bob sees a new message (Greg approved the wording,
     2026-11-09), `RestorePrompt.EditDuringSaveMessage`: "You made a change while TermPoint was saving before the
     restore, so the restore wasn't started. Your change is kept and will be saved as usual. Start the restore again
     when you're ready." Today's save-failed text would have been untrue here, since nothing failed. The double message
     when the save really fails is a separate, existing problem: summary 6.14.
   - **Backstop:** `CheckoutService.RestoreFromBackupAsync` refuses while `HasDirtyMarker`, with the same message.
   - **A gap that exists today, independent of 3.2:** an edit made in a detached panel *after* the save-first, while
     the restore copies the backup, is deleted by `DiscardCrash`. The backstop narrows this but can't close it. That
     would need the session to refuse writes for the length of the restore. **Moved to summary item 3.13** (Greg,
     2026-11-09); not part of E.
8. **Not changed:** steps 1–5, the rename, the lock and conflict handling, every message, the demotion,
   `CleanupWorkingCopy` and the recovery flow. They all keep reading the marker. The fix makes the marker true more
   often.

**What remains (accepted, documented in code).** The only edit that could still be lost is a transaction that is
still uncommitted at the step-6 reads and whose last edit-counter increment came before `G0`, which means one
transaction left open for the whole save. Task 1's audit (2026-11-09) found that every transaction in release builds
runs synchronously on the UI thread from start to commit. There's no `await` inside any of them, and bulk work takes
about 30 ms. So this needs the UI thread frozen for a whole save.

**Audit notes for task 2.**
- The harness uses no `DatabaseContext`: `FakeSessionHost.Edit`/`EditWith` must call `NoteEdit` before `Exec`.
- `BackupSqliteDatabase` must hand back its source connection instead of disposing it. **Rename it to
  `SnapshotWorkingCopy`** (Greg, 2026-11-09). "Backup" there meant SQLite's backup API, and it was easily confused with
  TermPoint's backup files and Restore from Backup. Update both callers (the save, and the export of kept changes),
  every comment and doc reference, and the tests.
- `onEveryWrite` goes last in `DatabaseContext`'s constructor, so positional callers such as `ImportRig.cs:83` still
  compile.
- `BeforeDirtyMarkerDeleted` must still fire before the marker delete (`CheckoutServiceTests` group 9).
- New mid-save-edit tests must call `NoteEdit` as well as `MarkDirty`.

**Rejected alternative (one line).** Keep the marker for the whole session and decide at close or the next open by
comparing contents. That's race-free, but it changes the recovery flow, which is 3.5's territory.

### 3.4: 1.2.3 section 29, with these changes

- **Second guard:** skip only when there is also **no marker**. The 1.2.3 risk review asked for this. A skipped save
  never touches the marker, the first-write flag or `SessionDirty`. The one thing it does at the end is the lock
  release with `releaseLockAfter`, the same as a successful save.
- **With 3.2:** a skipped save reads `G0` and `v0` too. If either moved during it (an edit arrived), it behaves like
  3.2's "changed" branch: the marker the edit wrote stays, and with `releaseLockAfter` the lock isn't released, so
  `ReleaseAsync` saves again. A real save sets `ContentHashAtCheckout` to the snapshot's content fingerprint in both
  of step 6's branches.
- **Content fingerprint:** `SqliteContentHash.Compute(path)`, a pure streaming SHA-256 with bytes 18–19, 24–27,
  40–43, 92–95 and 96–99 read as zeros. Never opened with SQLite.
- **Checklist:** after every SQLite or Avalonia upgrade, run the "open, no edit, save, compare" test (added to the
  local `CLAUDE.md` checklist at the end of task 5).
- **Real data:** the no-edit test also runs on the local real database (`[FactRequiresLocalDb]`).

## The four rules

1. **The shared database only changes by one atomic rename of a checked copy.** 3.2 doesn't touch steps 1–5. 3.4
   writes less often, never differently: a skipped save writes nothing.
2. **Never write it unless the lock is ours and it is exactly as we last saw it.** Both checks still run before
   anything is written. A skip comes after them and writes nothing. 3.2 keeps the lock at exit until the extra save
   has run, which is the same position as during any save.
3. **Never delete the only copy of unsaved work until a copy exists elsewhere.** This is the point of 3.2. The marker
   is deleted only when SQLite's commit counter shows no commit since the snapshot *and* no write started since it,
   which makes the guarantee come from the data layer. 3.4 deletes nothing: it needs no marker to begin with, and a
   marker written during it stays.
4. **Never confuse the user.** No new messages. The indicator stays on while something really is unsaved. Lost-lock
   and exit messages that choose their words from the marker become true in F2's case. Restores succeed after
   no-edit sessions, so the 1.2.3 wording ("unless someone else changes the database first") holds.

## What runs at the same moment (fix-on-fix check)

| Running alongside | With the fix | Covered by |
|---|---|---|
| User edit during a save | marker kept; next save carries it | new unit tests (hooks before the backup, between backup and step 6, inside step 6) |
| Autosave tick | unchanged gate; next tick saves the kept edit | existing tests + new "next save clears it" test |
| Exit/switch final save | lock kept, one extra pass | new `ReleaseAsync` tests |
| Lost-lock demotion | keeps D' because the marker is there (unchanged code) | harness F2 route turns into a proof test |
| Wake check / heartbeat | untouched; step 6 takes no network action | existing |
| `RefreshWriterCopyAsync` | refuses more often (marker kept): conservative | existing refusal test |
| Restore from backup's save-first | shared helper saves again; still marked → restore not started (step 7) | new `SessionLifecycle` test: edit during the save-first |
| `RecordPendingSaveHash` (F8) | unchanged; rewrites the marker if present | existing F8 proof test |
| Reader poll | not involved (writer only) | — |

## Risks (production)

- **The marker is kept when it needn't be.** A crash then offers "Restore Unsaved Changes?" for a change the next
  save would have carried. The restore works, because the marker holds the hash the save just landed. That's a mild
  surprise, not a loss.
- **The marker rewrite fails** (antivirus hold). Retry once, then raise today's crash-protection warning, the same as
  `MarkDirty`.
- **`_markerGate` contention.** It's held for one local file write or delete, so a UI write waits a few milliseconds
  at most. There's no network call under it.
- **The open source connection** during the upload is a WAL reader with no open transaction. It doesn't block writes.
  It's disposed on every path, including failures (task 2 tests).
- **3.4's false "unchanged"** (risk review in section 29) now needs the content check *and* the marker to be wrong at
  once, and even then a skip deletes nothing.

## Order of work (Opus briefs, Sonnet codes, Opus reviews each)

1. **Audit (Sonnet, report only, no edits):**
   - user writes that run off the UI thread or with an `await` inside a transaction (`SectionImportWriter`,
     `SemesterContentsService`, `CopySemesterViewModel`, `MigrationViewModel`'s `Task.Run`s, the list VMs that use
     `BeginTransaction`);
   - what Restore from backup does after a save-first that leaves a marker;
   - how the harness models an edit during a save (what produces F2), and where its fake `DatabaseContext` needs
     `onEveryWrite`.
2. **3.2 in `CheckoutService` and `DatabaseContext`**, with unit tests: an edit at each of three points keeps the
   marker and SessionDirty and is saved by the next save; no edit → marker deleted as today; the exit runs one extra
   pass; the source connection is disposed on every failure path; the marker rewrite carries the new hash.
3. **Harness:** F2 comes off `KnownFindings`, and its route becomes a proof test in `FixedFindingsProofTests`; full
   suite with `TERMPOINT_HARNESS=1`.
4. **3.4a:** `SqliteContentHash` plus tests (pure).
5. **3.4b:** the skip, plus section 29's test list plus the marker guard and a 3.2 edit during a skip. Add the
   checklist line to `CLAUDE.md` (Opus).
6. **Harness:** F5 off; F6 off unless the run finds another route; `KnownSequences` and the two named tests become
   proof tests. Both compile checks, full suite with the harness, and update the summary's Done notes.

**Hand checks (Greg, after task 6):**
- Open a just-restored database, change nothing, close: its modified time doesn't change.
- With `TERMPOINT_DEBUG_SLOW_EXIT_SAVE` unset and test timings on: start an autosave on a slow share, edit during it,
  and freeze TermPoint until Alice takes over. The edit is offered back at the next open.

## Brief for Sonnet (task 1, then task 2 after Opus reviews the audit)

> Working in `C:\Users\gregs\source\repos\SchedulingAssistant`, branch `Version_1.2.4`. Read
> `designs/mid-save-edits-and-no-edit-saves.md` (this file) and `CLAUDE.md`'s four rules first. Rules: no commits, no
> branches, no `.md` or `.axaml` edits, keep CRLF, never `sed -i`, never `taskkill /IM`. Build output goes to drive E.
> **Task 1:** answer the three audit questions in "Order of work", with file:line references, and make no edits.
> **Task 2 (only when told):** implement "3.2: an edit counter plus SQLite's own change counter", steps 1–7, exactly as
> written. Comment every new member (`///`, purpose, parameters, returns, exceptions). Run the compile check and the
> `CheckoutService`/`DatabaseContext` tests, then report the diff summary and test counts. Don't touch the harness's
> `KnownFindings` (that's task 3).
