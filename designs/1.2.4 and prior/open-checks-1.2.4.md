# 4.7 Open checks: locked kept changes are never deleted; a lighter check of the shared database (1.2.4)

Decisions approved by Greg on 2026-11-10: D1 to D3, with wording W1. MinimumVersionToWrite: **No**. Nothing new is
stored, and nothing saved changes.

## D1 (first, data risk): crash recovery tells a locked copy from a damaged one

**Today.** Bob's laptop crashed with unsaved edits. He opens TermPoint just as antivirus is scanning his kept changes
(D′). `CheckoutService.InspectCrashRecovery` uses the yes/no `BackupService.CheckIntegrity`, so a file it can't open
counts as damaged. `SessionLifecycle.OfferCrashRecoveryAsync` then calls `ArchiveCorruptWorkingCopy`. The rename
fails because the file is in use, and `deleteIfMoveFails: true` deletes it, which works when the scanner shares
delete. Bob is told "Unsaved Changes Lost… damaged". His intact, only copy is gone.

**With the fix.**
- The check is three-way: `CheckIntegrityDetailed` / `WithSummary`, read-write as now, so a hot journal is still
  replayed. An unreadable result is retried quietly: 3 attempts in all, 1 s apart, the same numbers as
  `LocalCopyIntegrity`.
- **Damaged:** the same as today (archive, then "Unsaved Changes Lost").
- **Still unreadable after the retries:** nothing is renamed or deleted. Bob sees "Couldn't Open the Database" with
  **Try again / Close**. The text is `LocalCopyCheckPrompt.InUseMessage` plus W1: " Your unsaved changes from last
  time are kept."
  - **Try again:** the quiet retries run again.
  - **Close:** gives up the open before any checkout. No lock is taken, no service is built, and D′ and its marker
    are untouched. Startup and switching handle it like any other open that was given up. The next open asks
    "Restore Unsaved Changes?" again.
- **Ok:** the same as today ("Restore Unsaved Changes?").

`CrashRecoveryInfo.WorkingCopyIntact` (bool) becomes a three-way verdict. `HandleRecoveryConflictAsync` only checks
`info` for null, so it is unaffected.

## D2 (second): the shared database gets the header check at open, not a full read

Every open ran `DatabaseValidator.ValidateAsync` (a full SQLite `integrity_check`) on the shared database. On a slow
link that reads the whole file, and while it reads, the editor's save rename can't happen.
- **Switched to `ValidateWithoutSqliteAsync`** (probe, then the 16-byte signature, opened `FileShare.ReadWrite`):
  - startup, both branches (`MainWindow.RunReturningUserStartupAsync`);
  - the switch pre-check (`DatabaseSwitchPrecheck`);
  - the chooser's three opens (`OpenRecentDatabase`, browse, and the selected recent).
- **Kept full:**
  - `LocalCopyIntegrity.RecheckSharedDatabaseAsync`, which decides whether the shared database itself is damaged;
  - the chooser's check of a backup about to be restored;
  - the full check of the local copy at every open (`VerifyAtOpen`).
- **What the user sees:** a damaged shared database still gets "may be damaged", one step later (copy, then the
  local check fails, then the re-check). A zero-byte file is now refused at the door. The chooser's Recent list no
  longer flags a damaged database before it is clicked.
- **4.8 knock-on:** the slow-link opens stopped at exactly this check, so 4.8 becomes "measure again".

## The four rules

1. **Atomic rename of a checked copy:** not touched.
2. **Lock ours and database as last seen:** not touched. D1's Close happens before the lock is taken. D2 reads less
   of the shared database and no longer blocks the editor's rename while it reads.
3. **Never delete the only copy of unsaved work:** D1 closes the hole. An unreadable D′ is never renamed or deleted.
4. **Never confuse the user:** D1 shows one message saying what happened, that the work is kept, and what to do
   (W1). D2 adds no messages.

## What runs at the same moment

- D1 runs before checkout, under the single-instance guard, with no lock and no services yet. Nothing else touches D′.
- D2's header read opens the shared database `FileShare.ReadWrite` for 16 bytes and coexists with an editor's save.

## Tests

- **D1:**
  - an unreadable D′ (held with `FileShare.None`) is never deleted or archived, and its marker stays;
  - unreadable on the first attempt, then readable, leads to the normal Restore question;
  - damaged gives today's behaviour;
  - the lifecycle "Close" gives up with no checkout;
  - "Try again" checks again;
  - a wording test pins W1.
- **D2:** each switched call site uses the header check (a damaged-but-signed file passes the door and is caught by
  the local check plus the re-check, with the same messages as today); a zero-byte file is refused.
- Full suite with the harness before each commit.

## Order and who does what

D1 then D2, one commit each. Sonnet codes from a brief (no `.md`/`.axaml`, no commits). Opus reviews, runs both
compile checks and the full suite with the harness.
