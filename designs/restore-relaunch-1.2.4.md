# 1.2.4 item 4.3: after a Save & Backup restore, the old TermPoint must end, and the new one reopens the database

Status: **built 2026-11-10** (Greg chose B for every close). By-hand check on the sideload **passed 2026-11-10** (3/3; A's line every time, B never fired; macOS `open -n` still to check). MinimumVersionToWrite: **No** (nothing stored changes).

## The story

**Today.** Alice restores yesterday's backup. TermPoint says the backup was restored, the window closes and a new
TermPoint starts. Two things go wrong:

1. The **old TermPoint never ends**. It has no window, does nothing, holds no file (lock, guard, connection and working
   copy are all released first), but it sits in Task Manager with about 265 MB until Alice signs out. A Store update
   may ask her to close TermPoint although she sees none open. When it finally dies, the next start reports "the last
   session ended unexpectedly" to BugSnag. Seen 3 of 3 times on 1.2.3 (field tests 2026-10-22 and 2026-10-27).
2. The **new TermPoint shows the database chooser** (unless "Always open the most recent database" is on), so Alice
   has to pick the database she just restored.

**With the fix.** The window closes, the old TermPoint ends within a second or two (at most ~15 s in the worst case),
and the new one opens the restored database directly.

## What we know about the cause (field evidence, `docs/testing/network-field-tests-1.2.3.md`, Open issues)

The stuck process logged "Managed teardown complete" but never "Dispatcher loop exited". Its dump: main thread idle in
Avalonia's loop, window list **empty**, `_isShuttingDown` false, `ShutdownMode` OnMainWindowClose. So the shutdown that
closing the main window should start never ran, or was abandoned. Not the chooser (reproduced with no chooser ever
created). Plain closes and the chooser restore (no relaunch) exit fine. Three suspects remain:

- **H1** the relaunch (the new copy starts just before the old main window's final `Close()`);
- **H2** the stale-session teardown (`EndSessionAfterRestoreAsync`);
- **H3** the close itself: on this path `Close()` is called by code right after an owned modal dialog (the result
  message) has closed, while a normal exit is the user's click.

The fix below doesn't depend on which one it is. The new log lines (below) will tell us from the first field run.
**Optional cheaper probe first** (CLAUDE.md debugging rule): a DEBUG-only switch `TERMPOINT_DEBUG_NO_RELAUNCH` and one
restore in a Debug run. If the process then ends, it's H1. ~10 min of your time, but only useful if a Debug build
reproduces it at all (not yet tried). My recommendation: skip it and let the fix's log lines answer.

## The change

**A. Ask Avalonia to shut down once the main window has closed** (`MainWindow.OnClosed`, every close, not only a
restore). Post at background priority: request `Shutdown()` on the desktop lifetime, in a try/catch (Avalonia throws or
ignores when it is already shutting down). In a normal exit the loop has already ended and the posted call never runs.
Log one line when it does run ("the main window closed but shutdown had not started; asking for it"), which is the
evidence for the cause.

**B. A backstop, only after the teardown has finished.** If Avalonia abandons even that request (as it may if a window
is still registered), the process must still end. At "Managed teardown complete", start a background timer (15 s).
If `Main` has not returned by then: wait (bounded, ≤ 5 s) for the write lock's background delete
(`WriteLockService.PendingRelease`; a cut delete would leave a lock file that makes colleagues wait 170 s), mark the
run as ended normally (`SessionRunMarker.End()`, so no false "ended unexpectedly" report), log one line and send one
handled BugSnag event (so we learn how often it happens), then `Environment.Exit(0)`. The timer is a background
thread: a normal exit simply ends it.

**Decision for you:** B for **every close** (my recommendation: by then the teardown has saved, released and
disposed everything in every path, and any other stuck exit gets cured and reported too) or **only after a restore**
(narrowest change; the only path where it has been seen).

**C. The new copy reopens the restored database.** `RelaunchApplication(path)` passes
`--reopen-after-restore "<path>"`. At startup, when that argument is present **and** names the saved most-recent
database (it is: the restored database is the one that was open), the start takes the existing auto-open branch of
`RunReturningUserStartupAsync` once: validate, then open, or fall to the chooser with the usual reason if the check
fails. Mismatch or no argument: today's behaviour. Nothing is written to settings.
macOS: today's `open <bundle>` may only bring the still-running old copy forward; it becomes
`open -n <bundle> --args …` (check on the next signed Mac build, as with 2.13).

## The four rules

1. **Atomic rename of a checked copy:** untouched. The restore itself (`DatabaseRestore`) is unchanged.
2. **Write only with the lock and an unchanged database:** untouched. A and B run after the teardown released the lock;
   B waits for the lock file's background delete before ending, so it never leaves a stale lock behind.
3. **Never delete the only copy of unsaved work:** B starts only at "Managed teardown complete", after the exit save or
   the restore's pre-save, after a failed save's kept changes are left in place with their marker, and after the guard
   release. `Environment.Exit` deletes nothing. A only asks for the shutdown that should have happened anyway.
4. **Never confuse the user:** no new messages. Fewer surprises: no invisible TermPoint blocking an update, no chooser
   asking for the database just restored.

**Cascade check:** at checkpoint #1 the autosave, heartbeat, wake check, backup timer and reader poll are already stopped
and the services disposed. The relaunched copy starts after the guard release (unchanged). Its open of the restored
database takes the lock normally; the old process holds none.

## Tests

- `SessionLifecycle`: after a confirmed restore the relaunch gets the restored path; after RenameUnconfirmed there is no
  relaunch (unchanged).
- Startup argument: matching path → auto-open branch once; different path, missing argument, or a failed check → chooser
  as today (a small parser + decision helper, unit-tested).
- Backstop helper (the timer logic pulled out with an injectable clock and exit action): fires only when Main hasn't
  returned; waits for the pending release; ends the run marker before exiting; never fires on a normal exit.
- By hand (Greg, sideload): restore three times; the old PID is gone each time, the new copy opens the restored
  database; read which new log line appeared (A's or B's), which names the cause.
