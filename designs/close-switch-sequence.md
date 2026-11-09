# 1.2.4 items A + B: the close and switch sequence

Status: **implemented 2026-11-08 (uncommitted); full suite with harness 4501 passed, 0 failed.** Changes from the draft:
F18 turned out to be a live-session problem (a save that put back a deleted lock file, then failed, showed two banners);
Greg chose to skip the "lock put back" notice when that save fails. F20's remaining route (another program still holds
the lock file when the session goes read-only) is accepted and stays known.
Summary items in `designs/spec_version_1.2.4_summary.md`: 3.3 (F16), 6.11 (F18), 4.13 (F20), 6.10 (F17), 6.13 (F23)
and 3.1 (unapplied editor changes). Known findings in `src/TermPoint.Tests/Harness/KnownFindings.cs`.
`MinimumVersionToWrite`: **No** (nothing new is saved in the database; the lock file format is unchanged).
**3.1 decided (Greg, 2026-11-08): always ask** (Apply / Discard / Cancel).

## One root, three symptoms

Most of A is one bug: **work that belongs to the old session finishes after the switch and acts on the new one.**
A lost-lock demotion (F16), a lock-lost banner (F18, likely) and a save-problem banner (6.10's second half) are all
started for database 1 and land on database 2, because nothing tells them the session they were started for has ended.
The rest is the close/switch's own final save talking twice (F17), a post-open save that runs when nothing was restored
(F23, likely), and our own leftover lock read as someone else's (F20).

## The stories

### F16: a demotion lands on the new database

**Today.** Bob's laptop wakes; Alice took over `BIOL` while he slept. TermPoint starts making Bob read-only: it closes
his connection and starts copying `BIOL` for reading. During that copy Bob, unaware, picks `CHEM` from Recent. The switch
releases `BIOL`, opens `CHEM`, and takes `CHEM`'s lock. Then the old demotion's copy finishes: it carries on with the
*current* database, copies `CHEM` and marks the session read-only. Bob now holds `CHEM`'s lock but can't save. Colleagues
are locked out of `CHEM` by a session that can't write; Bob's edits in `CHEM` (if any get through) have no marker and
may not be saved at close.

**With the fix.** The switch waits for the running demotion to finish (normally a second or two), then releases `BIOL`
(which by then is read-only, so nothing is saved) and opens `CHEM`. If a demotion was *raised* but had not yet started
when Bob switched, it starts, sees that its session has ended, and does nothing. `CHEM` opens as an ordinary writer.

### F18 and 6.10: banners from the old database over the new one

**Today.** Bob switches from `BIOL` to `CHEM` while a heartbeat check of `BIOL` is still waiting on the network. The
check finishes after `CHEM` is open and its result is posted to the window, which shows it on *whatever is open now*:
`CHEM` gets "Another program removed the file…" or "…has taken over editing". The same route can leave `BIOL`'s
"read-only for you now" or "couldn't save" banner showing over `CHEM`.

**With the fix.** Every message the save/lock code posts to the window carries the session it came from. The window
drops a message whose session has ended. `CHEM` shows only `CHEM`'s news.

### F17: two messages for one save problem at close or switch

**Today.** Bob closes TermPoint. The exit save finds the share unreachable (or Alice took over, or the file is in use).
Bob sees the banner "TermPoint can't reach…" flash up, then the dialog "Changes Not Saved… They're kept on this
computer…". Two messages, both about his changes. If the problem was a takeover, the final save also raises a lock loss,
which starts a demotion while the window is being torn down.

**With the fix.** Once a close or switch has begun, its final save reports only to the close/switch, not to the window:
no banner, no lock-loss demotion. Bob sees one message, "Changes Not Saved", worded as today (it already says what
happened, that the work is kept, and what happens next; no new wording).

### F20: our own leftover lock makes us read-only

**Today.** Bob switches from `BIOL` to `CHEM` while backup software has `BIOL.lock` open, so TermPoint can't delete it.
The lock stays, naming Bob's own running TermPoint. Later Bob switches back to `BIOL`. TermPoint sees a live process on
this computer holding the lock (itself), assumes a second copy of TermPoint, and opens read-only: "Someone else is
editing this database right now". His kept changes can't be restored until he restarts.

**With the fix.** When the lock names *this very TermPoint* (same session id), it is ours: TermPoint takes it back
(checked delete, then create) and opens `BIOL` for editing, offering the kept changes as usual. If the backup software
still holds the file, the take-back fails and Bob gets today's read-only open (rare; noted below).

### F23: "changes kept" after Bob discarded them

**Today (from the harness story; to confirm in code first).** Bob switches back to `BIOL` while another program has it
open. His kept changes can't be restored; he chooses Discard and confirms. The open then runs its "save the restored
changes at once" step anyway (nothing was restored), the save finds the file in use, and the banner says "…couldn't
save… your changes are kept". He just threw them away.

**With the fix.** The post-open save runs only when the open really took on kept changes (the second route of item 3.4,
taken here because it sits in the switch path). No save, no banner.

### 3.1: unapplied editor changes

**Today.** Bob is halfway through changing HIST 101 A's room in the section editor and clicks the X (or picks another
database). The window closes and the change is gone without a word.

**With the fix.** Before anything else, TermPoint asks once: **"Unapplied Changes"**, "You have changes in the open
editor that you haven't applied." **Apply / Discard / Cancel**. Apply runs the editor's normal checks: if they pass,
the change is applied and the close (or switch) carries on and saves it; if not, the close stops and Bob sees the
editor's own error line. Discard drops the change and carries on. Cancel stops the close and leaves everything as it was.
The question appears only for a writer with an editor that really has unapplied changes.

**Lost-lock variant.** When Bob loses the lock with an editor open, nothing can be applied. Today the reload closes the
editor silently. With the fix, the lock-lost banner (one message) gains one sentence when an editor had unapplied changes.

## How it works

1. **A session id in `CheckoutService`.** A number that goes up when a session ends (the start of `ReleaseAsync`) and
   when a new one starts (checkout). `IsEnding` is true from the start of `ReleaseAsync` until the next checkout.
2. **Every posted event carries the id** it was raised under: `WriteLockLost`, `SaveFailed`, `SaveConflictDetected`,
   `LocationUnreachable`/`LocationReachableAgain`, and the reader-poll's `LockStateChanged`/takeover offer. The window
   and `SessionLifecycle` act only when the id is current and the session is not ending.
3. **The demotion is tied to its session.** `DemoteToReadOnlyAsync` takes the id; after every `await` it checks it, and
   if the session changed it stops without touching `Mode`, `WorkingPath`, reader mode or the connection callbacks.
   `CheckoutService` keeps the running demotion's task; **`ReleaseAsync` waits for it** (bounded, 60 s, the same ceiling
   as the in-flight save wait) before saving or releasing. A demotion that outlasts the wait is stopped by step 3's check.
4. **The ending's own save.** While `IsEnding`, the save raises nothing to the window and `HandleLockLossAsync` raises no
   lock loss (falls out of step 2, since the id has moved on; state it explicitly in code). The outcome goes to the caller,
   which shows "Changes Not Saved" as today.
5. **Own leftover lock.** In `WriteLockService.TryCreateLockFile`, before the liveness check: if the lock names our
   `SessionGuid` and no release of ours is pending (`PendingRelease` finished; wait for it, bounded 10 s), reclaim it with
   the same checked delete + `CreateNew` as `TryReclaimDeadHolder`. If a release is still pending, or the delete fails,
   fall through to today's path (read-only).
6. **Post-open save only after a real restore.** The open reports whether it took on kept changes; the startup and switch
   paths run their "save at once" only then (one shared helper).
7. **3.1 contract.** `IUnappliedChanges` on every editor VM: `HasUnappliedChanges`, `TryApplyAsync()` (runs normal
   validation and Apply; false if refused), `Discard()`. `MainWindowViewModel` collects the open ones. The question runs
   first in `OnClosing` (before the exit guard moves on, re-entry-guarded so a second close request while it shows is
   ignored) and first in `SwitchDatabaseAsync` (before the pre-check, which records the new path in settings and recents).
   Inventory first: the section, meeting, commitment, release and scheduling-environment editors and the flyout editors
   (courses, instructors, rooms, campuses, subjects, academic years, semesters, block patterns, legal start times, section
   code patterns). Scheduling notes already save on focus loss.

## The four rules

1. **Shared database changes only by one atomic rename of a checked copy.** Untouched: no change to the save's upload,
   check or rename. Fewer writes than today: the stray post-open save (F23) and a demotion's actions on the wrong session
   go away. A 3.1 Apply goes through the normal save at close.
2. **Write only when the lock is ours and D is as we last saw it.** The save's own checks are unchanged. F16's fix
   removes a session that holds a lock it can't use. F20's reclaim writes only a lock file that names *this process's*
   session id, using the existing checked delete (2 s freshness) and an atomic `CreateNew`: if Alice took the stale lock
   meanwhile, the check sees her name and we don't delete; if we race her create, one `CreateNew` wins and the loser is
   read-only. A pending release of ours is waited for first, so our own late delete can't remove the lock we just took.
3. **Never delete the only copy of unsaved work.** No delete is added. The demotion still keeps D' with its marker; the
   switch waits for it, so the release's cleanup sees the read-only snapshot, not D'. `CleanupWorkingCopy` still refuses a
   marked copy (F21 guard). 3.1 Discard is the user's explicit answer to a question that named the changes; Cancel and a
   failed Apply keep them. The question runs before any teardown, so no dialog is used to protect data.
4. **Never confuse the user.** One message per problem: F17 and F18 each drop to one, 6.10's stale banner and F23's false
   promise disappear. The 3.1 question appears only when there is something to decide, once per close or switch.

## What runs at the same moment (fix-on-fix check)

| Running when a close/switch starts | Today | With the fix | Covered by |
|---|---|---|---|
| Wake check (old session) | result may post a lock loss onto the new session | dropped: id ended | new unit test; harness I6 / message checks |
| Heartbeat verify in flight | may post a banner (F18); "Ours" calls `ForceRenewHeartbeat` | dropped; renew skipped when id ended (renew is already gated against `Release`) | unit test; replay of the F18 seed |
| Autosave tick | release waits for it (unchanged) | unchanged | existing tests |
| Lost-lock demotion | lands on the new session (F16) | release waits; late part stopped by id check | un-skipped `F16_...` unit test; harness `KnownSequences` F16 |
| Exit/switch final save | banner + dialog; takeover starts a demotion (F17) | reports only to the caller | harness message checks (F17 off the list) |
| Reader poll (demoted session) | takeover offer for the old database could show over the new | dropped: id ended; `Release` stops polling | unit test |
| 3.1 question showing | (no question today) | autosave, heartbeat and lock loss carry on; a lock loss turns Apply into a refusal, the close stops, and the lock-lost banner explains | VM tests with a fake prompt |

## Open points

- **F18 is not investigated.** Cheapest probe first: replay the harness seed that found it with step 2 in place. If it
  is gone, retire it; if not, investigate before anything else.
- **F23 is read from the harness story.** Confirm with its seed that the banner comes from the post-open save before
  coding step 6.
- **F20 while the other program still holds the lock file:** stays read-only with today's "someone else" message. A
  correct message would need new wording; left out unless you want it.
- **The 3.1 dialog needs three buttons.** `ILifecyclePrompts` has two buttons plus X. A third button may mean an AXAML
  change (I'll warn before it).
- **Wording for your approval:** the 3.1 question (title "Unapplied Changes", body above, buttons Apply / Discard /
  Cancel); the lost-lock sentence, proposed: "Changes you hadn't applied in the open editor couldn't be saved."
- **Windows shutdown with an editor open:** the question would hold up shutdown (Windows offers "close anyway"). Accepted
  as is unless you prefer otherwise.

## Order of work (Opus briefs, Sonnet codes, Opus reviews each)

1. Steps 1-3: session id, events carry it, demotion tied and waited for. Un-skip `F16_...`. Replay F18's seed.
2. Step 4: the ending's save talks only to the caller (F17).
3. Step 6 after confirming F23's source.
4. Step 5: own leftover lock (F20).
5. Step 7: 3.1 inventory, contract, question at close and switch, lost-lock sentence.
6. Harness bookkeeping: retire fixed findings to `FixedFindingsProofTests`, update walks that expect them; both compile
   checks; full suite with the harness.
