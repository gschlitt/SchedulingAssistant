# 1.2.4 item 4.1: every save uploads to its own temporary file

Status: **design approved 2026-11-08; implementation in progress**. Summary item 4.1 in `designs/spec_version_1.2.4_summary.md`.
`MinimumVersionToWrite`: **No** (nothing in the database changes; older versions never read these files).

## The stories

**Today, the case seen in the field.** Bob's Surface goes into Modern Standby halfway through uploading
`BIOL-TT.db.tmp`. The server keeps his half-written file open. Alice takes over the lock and edits. Every autosave of
hers fails for six minutes: her upload opens the same `BIOL-TT.db.tmp`, Bob's open handle refuses it, and she is told
another program is using the database. Nothing is lost, but the message is wrong and her work sits unsaved.

**Today, the worse case (found in 1.2.3, not yet seen in the field).** Bob's laptop sleeps during his upload. Alice
takes over and starts her own upload, under the same name. Bob wakes; his upload has failed, so his save deletes
`BIOL-TT.db.tmp`, which is now Alice's half-written or verified file. That is Bob writing to the share without the
lock. Alice's save fails (her file vanished before the rename), and she retries. A late-landing rename of Bob's
(an abandoned move that completes when his network returns) could in principle move *Alice's* file onto the
database; today only the 170 s takeover confirmation keeps that out of reach.

**With the fix.** Each save uploads to a name of its own, for example `BIOL-TT.db.3f2a9c1e.tmp`. Bob's frozen
upload holds only his own file, so Alice's saves go through at once. When Bob wakes, his save deletes only his own
file. A late rename of Bob's can only ever move his own file, which he wrote and verified, and the existing lock
check and pending-hash logic already deal with that. When Alice next opens the database for editing, she sweeps away
Bob's leftover (she holds the lock then).

## The change

1. **One helper** (new, e.g. `ShareTempFile`) for every temporary file we place beside a database on the share:
   - `NewPath(targetPath)` returns `<target>.<8 hex>.tmp`, with a fresh random value per call;
   - `IsLeftoverOf(targetPath, fileName)` is true for `<target>.tmp` (the old name, still written by 1.2.3) and for
     `<target>.<8 hex>.tmp`, and nothing else (strict match, case-insensitive, so a user's own
     `BIOL-TT.db.backup.tmp` is never touched).
2. **The save** (`CheckoutService` step 3 onwards) gets a new name **per save attempt**, not per session. That way an
   abandoned rename from an earlier timed-out save can never move a file that a later save is still writing.
   - Every failure path deletes its own file without a lock check. That includes a refusal at the final check, which
     today leaves `D.tmp` alone when the lock is lost or unknown (spec item 32 (d)). The long comment there shrinks to
     "the file is ours; delete it".
   - A delete that fails (share down) is logged and the file is left for the sweep at the next write-access open
     (Greg, 2026-11-08: no in-session retry).
3. **The restore** (`DatabaseRestore`, the `D.tmp` it streams the backup into) and **new-file placement**
   (`NewFilePlacement.TmpPathFor`: New Database, the restore's create path, Save a Copy) use the same helper. The
   pre-restore copy's `.tmp` already sits beside a unique, timestamped name: unchanged.
4. **The sweep at checkout** (`CleanupOrphanedTmpAsync`, still only at the three write-access exits, i.e. only with
   the lock) lists the database's folder once, under the network deadline, and deletes every file for which
   `IsLeftoverOf` is true. It still can never fail the checkout. A file it can't delete (Bob's server-side handle still
   open) is logged and left for the next sweep.
5. **Untouched:** the heartbeat's `.lock.tmp` (written only after two fresh "ours" checks; a stalled write dies after
   ~85 s, well inside the 170 s confirmation; same reasoning as 4.12/4.14), the write-access probe (already a GUID
   name), and every local temporary file.

## The four rules

1. **The database only changes by one atomic rename of a checked copy.** Unchanged, and tighter: a rename, including
   a late one, can only move the file that same save wrote and verified, never someone else's.
2. **Never write it unless the lock is ours and it is as we last saw it.** The database writes are unchanged. The
   share writes made without the lock shrink to one kind: a session creating and deleting its *own* temporary file.
   Deleting another session's temporary file happens only in the sweep, which runs only with the lock.
3. **Never delete the only copy of unsaved work.** Not touched: a temporary file is always a copy of the local
   working file, which keeps its unsaved-changes marker until a save succeeds.
4. **Never confuse the user.** Removes the wrong "another program is using it" failures from the field case. No new
   messages.

## What runs at the same moment (fix-on-fix check)

- **Heartbeat / wake check:** touch only the lock file. Unaffected.
- **Reader poll / Refresh:** read the database itself, never the temporary files. Unaffected.
- **Exit save:** the same save code, so the same per-attempt name.
- **Sweep vs. our own save:** the sweep runs before the mode becomes write access, so no save of this session is
  running. A save abandoned by an earlier session in the same process (a switch away and back) may lose its file to
  the sweep. Its late rename then fails, which is the safe outcome.
- **1.2.3 and 1.2.4 on one database:** 1.2.4 never writes `D.tmp`, so a 1.2.3 writer's deletes can't hit a 1.2.4
  upload. 1.2.4's sweep still removes `D.tmp` (as today). 1.2.3's sweep doesn't know the new names, so a 1.2.4
  leftover waits for the next 1.2.4 writer: harmless debris.

## Risks

| Risk | Answer |
|---|---|
| Leftovers pile up on a flaky share (one per failed attempt whose delete also failed) | The sweep at every write-access open; a failed upload rarely leaves a file and its delete rarely fails too |
| Listing the folder is slow on a bad link | One listing under the existing deadline; the sweep never fails the checkout |
| The sweep deletes something it shouldn't | Strict name match; runs only with the lock, as today |
| More antivirus/sync activity from new names | Each save already writes a brand-new file today; no change in volume |
| Tests and harness hard-code `dept.db.tmp` | Listed below; the harness's "other program holds the save's temporary file" (8.3) changes meaning |

## Tests

- **Unit:** the helper (name shape, fresh per call, strict leftover match including the 1.2.3 name); every save
  failure path deletes exactly its own file; the sweep deletes
  leftovers, not look-alikes, and survives a listing timeout.
- **Harness:**
  - `World.OtherProgramFile.SaveTemp` resolves to the current save's temporary file (by pattern);
  - update `InvariantProofTests` (lines ~267, ~774) and `PauseProofTests` (~78);
  - new proof 1 (the field case): Bob frozen mid-upload holding his file, Alice takes over, and her next save
    succeeds at once;
  - new proof 2 (the worse case): Bob's late delete and late rename can't touch Alice's file or the database;
  - check `KnownFindings` for anything that assumed the shared name.
- Both compile checks; full suite with `TERMPOINT_HARNESS=1`.

## Tasks (Sonnet codes, Opus reviews)

1. `ShareTempFile` helper and its unit tests.
2. The save: per-attempt name, own-file deletes; update the comments at the final check.
3. Restore and `NewFilePlacement` on the helper.
4. The sweep: one listing, strict match, the old name included.
5. Test and harness updates; the two new proofs.
6. Release note line for `docs/testing/release-tests-1.2.4.md` (which hand test, if any, covers it: M-case with a
   frozen writer mid-upload on the rig).

## Decided (Greg, 2026-11-08)

- Name shape: `BIOL-TT.db.3f2a9c1e.tmp` (8 hex).
- No in-session retry of failed deletes: leftovers are the sweep's job.
- Design approved; Opus briefs and reviews, Sonnet codes.
