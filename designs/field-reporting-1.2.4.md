# Field reporting (1.2.4 items 7.1, 7.3, 7.4): design

Status: **approved by Greg and implemented** (2026-11-09, uncommitted). Reporting and logging only. MinimumVersionToWrite **No**
(nothing new is saved in the database).

## The story

**Today.** Alice's BugSnag event says "Save failed: the database location did not respond". The breadcrumbs show a run
of `Save: Success`, then the failure. We can't tell whether her server was already slow (saves taking 3-4 s, one finally
passed 5 s) or fast until a real outage stranded her.

**With 7.1.** Each `Save: Success` breadcrumb says how long that save took (`elapsedMs`). The failure report says which
wait ran out, that the wait was 5000 ms, how long this save had run, and the last five successful save times
(`recentSaveMs = "3410,3880,2950,4120,3760"` reads as slow server; `"610,580,640,590,600"` reads as outage).

**With 7.3.** Bob's antivirus holds the database file at the moment of his save's rename. Today only his local log knows.
Now BugSnag gets one event ("Save rename refused: the file was in use or access was denied") the first time it happens
in a run, so we learn whether 5.3/5.4 are worth doing.

**With 7.4.** A kept edit vanished in a field test and the log couldn't say whether Restore or Discard was chosen. Now
every answer on "Restore Unsaved Changes?" (and on "Can't Restore Unsaved Changes", which uses the same code) writes one
INFO line.

## 7.1 Time each save

`CheckoutService`:

- `SaveAsyncCore` starts a `Stopwatch` (via `Stopwatch.GetTimestamp()`) as its first line. Real elapsed time on purpose:
  this measures the share, not the test clock.
- **Success breadcrumb** (the one existing line changes):
  `Save: Success` with metadata `elapsedMs` (whole number, e.g. `"2210"`) and `autoSave` (`"True"`/`"False"`).
  The ghost-rename-confirmed path falls through to it, so it is timed too.
- **Recent successes**: a private 5-entry list of the last successful saves' `elapsedMs` for this session, appended just
  before the breadcrumb, guarded by `lock` (saves are already one at a time through `_saveInFlight`; the lock is only
  belt and braces). No I/O, no await inside the lock.
- **Failure report** (`HandledEvent.SaveLocationNotResponding`, still once per run, tab `event`):
  `RaiseSaveLocationUnreachable(isAutoSave)` gains two parameters, the wait that ran out and this save's start
  timestamp. Metadata, all numbers or fixed names:

  | Field | Example | Meaning |
  |---|---|---|
  | `autoSave` | `True` | as today |
  | `stalledAt` | `Rename` | which wait ran out: `LockCheck`, `SourceStat`, `SourceHash`, `CopyToLocation`, `VerifyAtLocation`, `Rename` (the six existing call sites) |
  | `deadlineMs` | `5000` | `NetworkFileOps.TimeoutMs` (per operation, or per chunk for the copy and hashes) |
  | `elapsedMs` | `5230` | how long this save had run when it gave up |
  | `recentSaveMs` | `3410,3880,2950` | last up to 5 successful saves this session, oldest first; `none` if there were none |

  The metadata is built inside the reporter call's existing `try`; building it reads two in-memory values and cannot
  throw in practice, and `HandledEventReporter.Report` swallows anything anyway.
- Also one breadcrumb at the same moment: `Save: LocationNotResponding` with `stalledAt` and `elapsedMs`. Only the
  first failure per run becomes an event; the breadcrumb records every one, so a later report shows the whole pattern.

## 7.3 A rename blocked by another program

- New `HandledEvent.SaveRenameBlocked`, message **"Save rename refused: the file was in use or access was denied"**
  (fixed text, as for every kind; `IsLockedOrAccessDenied` covers both, the HRESULT tells them apart).
- In `DescribeSaveFileError`, the in-use branch, only when `step == SaveStep.Rename` (the rename has already retried
  `MoveRetryAttempts` = 3 times by then): keep today's local log line, then
  - breadcrumb `Save: rename blocked` with `count` (blocked renames so far this session, an `Interlocked` counter) and
    `hresult`;
  - `HandledEventReporter.Report(SaveRenameBlocked, …)` (once per run) with metadata `autoSave`, `exceptionType`
    (type name only, e.g. `IOException`), `hresult` (e.g. `0x80070020`), `attempts` (`3`).
- "Counted": each run on each computer sends at most one event, so BugSnag's event count per install is the count we
  want; the per-session `count` in the breadcrumbs shows how often it repeated within a run.
- Banner, return value and cleanup are unchanged. The copy and verify steps' in-use cases stay local-only (not asked for).

## 7.4 Log the choice on "Restore Unsaved Changes?"

In `SessionLifecycle.AskKeepOrDiscardAsync`, one `LogInfo` per answer (titles and button labels are our fixed text; no
paths). For "Restore Unsaved Changes?":

- `Recovery: "Restore Unsaved Changes?" answered Restore.`
- `Recovery: "Restore Unsaved Changes?" answered Discard; asking to confirm.`
- `Recovery: "Restore Unsaved Changes?" closed with the window's X; asking again.`
- `Recovery: "Discard Unsaved Changes?" answered Discard; the kept changes will be discarded.`
- `Recovery: "Discard Unsaved Changes?" answered Go Back; asking again.`

"Can't Restore Unsaved Changes" logs the same shapes with its own title and labels (Save a Copy / Discard). Local log
only; no breadcrumb or event (not asked for).

## What else runs at the same moment, and why nothing can throw or block a save

- **Same save:** the new code runs inside `SaveAsyncCore` on the save's background thread. It reads a timestamp, appends
  to an in-memory list, and calls `LogBreadcrumb` (catches everything) and `HandledEventReporter.Report` (catches
  everything; BugSnag queues delivery, it does not send on this thread). Both are already called from these exact
  places today, so no new blocking kind of call is added. Nothing is awaited.
- **Heartbeat / wake check / reader poll:** they never touch the new fields. The heartbeat's own outage notice keeps
  calling `RaiseLocationUnreachable` directly and is not timed (it isn't a save).
- **Exit save:** same `SaveAsyncCore`, so it is timed and reported the same way; no effect on its outcome.
- **Second save attempt while one runs:** refused by `_saveInFlight` before `SaveAsyncCore`, so it is not timed.
- **Order of effects unchanged:** what is written, when, the banners, the outcomes and every lock and marker step stay
  exactly as today.

## Four rules

1. Atomic rename: untouched (no new write to the shared database).
2. Write only with our lock and unchanged D: untouched (no new write at all).
3. Never delete unsaved work: nothing is deleted.
4. Never confuse the user: no user-visible text changes.

## Privacy

Every value sent is a number, a `True`/`False`, an enum name, an exception type name or an HRESULT; no message text,
path or name. Reports go through `ScrubbingDelivery` as all reports do.

## Tests

- Success breadcrumb carries a non-negative integer `elapsedMs` and `autoSave`.
- A save timing out at each of the six waits sends one `SaveLocationNotResponding` with the right `stalledAt`,
  `deadlineMs = 5000`, integer `elapsedMs`, and `recentSaveMs` = `none` with no prior success, else the earlier saves'
  times (capped at 5, oldest first); a second timeout sends no second event but leaves a second breadcrumb.
- Rename in use: one `SaveRenameBlocked` with `hresult`/`attempts`; a second blocked rename sends no event and the
  breadcrumb `count` is 2. Copy/verify in-use: no event (as today).
- Metadata of both events contains only the fields listed (no value contains `\` or `/`).
- Recovery answers: each path through `AskKeepOrDiscardAsync` writes exactly the listed lines in order.
- Existing save/harness tests unchanged; full suite with `TERMPOINT_HARNESS=1`.

---

# 7.2 Report when a save (or refresh) recovers: design

Status: **direction approved by Greg 2026-11-10 (option A; the per-run summary B is deferred past 1.2.4: BugSnag can't
sum metadata, and a usage-metrics tool is a separate privacy decision). Implemented 2026-11-10.** Reporting only. MinimumVersionToWrite **No**.

## The story

**Today.** Bob's share stops answering for 40 s. Autosave 1 fails: BugSnag gets "Save failed: the database location did
not respond". Autosaves 2-4 fail silently (once per run per kind). Autosave 5 works: a `Save: Success` breadcrumb that
nobody ever sees, because breadcrumbs only travel with a later error. We can't tell "40 s, never noticed" from
"stranded for an hour, then quit".

**With 7.2.** Autosave 5 also sends "Save recovered after failing" (severity **info**), with `failedAttempts = 4`,
`failedSeconds = 41`, `firstKind = NotResponding`, `lastKind = NotResponding`, `autoSave = True`, `spellInRun = 1`.
A failure report with no recovery report after it now means the spell never ended in that run (lock lost, database
changed elsewhere, or the user quit while it was failing).

**Carol, a reader,** clicks Refresh during the same outage: it fails twice (once reported), then works. She sends
"Refresh recovered after failing" with the same fields.

## Design

**`FailureStreakTracker`** (new, `Services/`, pure and reusable, used by both save and refresh): thread-safe (`lock`).

- `RecordFailure(string kind, long timestamp)`: first failure of a spell remembers `firstKind` and the timestamp; every
  failure bumps the count and sets `lastKind`.
- `RecordSuccess(long timestamp)` → `FailureSpell?`: null when no spell was open; otherwise the closed spell
  (`FailedAttempts`, `FailedSeconds` = whole seconds from the first failure to this success, `FirstKind`, `LastKind`,
  `SpellInRun` = 1, 2, 3…) and the tracker is reset.
- `Abandon(string why)` → bool: closes an open spell without recovery (returns whether one was open).
- Timestamps are `Stopwatch.GetTimestamp()` values passed in (tests pass their own); real time, like 7.1.

**Save (`CheckoutService`)**

- **Kind** of a failed save: a per-save field `_saveFailureKind`, cleared when `SaveAsyncCore` starts, set at the four
  existing failure choke points: `RaiseSaveLocationUnreachable` → `NotResponding`; `DescribeSaveFileError` →
  `InUse` (in use / access denied) or `FileError` (any other file error); the torn-transfer branch → `TornTransfer`; `ReportSaveFailure(step)` → the step name (`Snapshot`,
  `SnapshotHash`, `Unexpected`…), **except** `CrashProtectionMarker`, which is reported from successful saves and must not
  set it. A `CopyError` with no kind set counts as `Other`.
- **Where the tracker is fed:** only in `SaveAsync`, after `SaveAsyncCore` (and in its safety-net catch):
  - `CopyError` → `RecordFailure(kind)`; the "a save is already in progress" refusal returns before this and is
    **not** a failure.
  - `Success` → `RecordSuccess`; when it returns a spell: breadcrumb `Save: Recovered` (every time) and
    `HandledEvent.SaveRecovered` (**at most 3 per run**), metadata `failedAttempts`, `failedSeconds`, `firstKind`,
    `lastKind`, `autoSave` (the recovering save), `spellInRun`.
  - `LockLost` / `SourceModified` → `Abandon`, with one local INFO line ("save failures ended without a recovery: …")
    and breadcrumb `Save: FailuresEndedWithoutRecovery` (`failedAttempts`, `reason`). No event: the lock-loss / refused
    save already sends its own.
  - `NotInWriteMode`: returns before, untouched.
- `SaveUntilMarkerClearAsync` and the exit save go through `SaveAsync`, so they are covered for free.

**Refresh (`MainWindowViewModel.RefreshReadOnlySnapshotAsync`)**: a reader's tracker field on the VM.
Failure (`line is not null`) → `RecordFailure(kind)` with kind = the `RefreshOutcome` name, or `Threw`. Success →
`RecordSuccess`; a spell sends breadcrumb `Refresh: Recovered` and `HandledEvent.RefreshRecovered` (at most 3 per
run), same metadata minus `autoSave`. A reader that becomes a writer (or switches database) gets a fresh tracker; an
open spell is abandoned silently.

**Reporting plumbing**

- `HandledEvent.SaveRecovered` = "Save recovered after failing"; `HandledEvent.RefreshRecovered` = "Refresh recovered
  after failing".
- `HandledEventReporter.Report` gains `int maxPerRun = 1` (count per logger per kind instead of a set) and
  `bool informational = false`.
- `IAppLogger.ReportHandled` gains `bool informational = false`; `FileAppLogger` sends those with BugSnag severity
  **Info** (local log level stays REPORT). Fakes in tests get the parameter.

## What else runs at the same moment

- **Saves:** single-flight (`_saveInFlight`), so the tracker is fed by one save at a time; it locks anyway.
- **Heartbeat / wake check:** its outage notice does not go through `SaveAsync`, so it never feeds the tracker (a lock
  check is not a save). A lock loss found by the heartbeat leaves a spell open; the next `SaveAsync` returns
  `NotInWriteMode` and never closes it. Accepted: the lock-loss event already tells the story.
- **Reader poll:** does not call `RefreshReadOnlySnapshotAsync`; untouched.
- **Exit:** a recovery during the exit save queues a BugSnag event that may be lost as the process ends. Accepted.
- **Never throws, never blocks:** every new call is inside try/catch; BugSnag queues delivery off the save thread; nothing
  is awaited; outcomes, banners, writes, lock and marker steps are unchanged.

## Four rules

1-3: no new write, rename or delete anywhere. 4: nothing new is shown to the user.

## Privacy

Values are integers, `True`/`False` and fixed kind names. Through `ScrubbingDelivery` like every report.

## Tests

- `FailureStreakTracker`: success with no spell → null; N failures then success → N, seconds, first/last kind,
  spell numbers 1, 2, 3; `Abandon` closes; thread safety smoke test.
- `HandledEventReporter`: `maxPerRun = 3` sends 3 then refuses; default still once; `informational` reaches the logger.
- Save (beside `SaveFieldReportingTests`): fail ×2 (not responding) then succeed → one `SaveRecovered` with
  `failedAttempts = 2`, kinds `NotResponding`; mixed kinds report first/last right; the in-progress refusal is not
  counted; success with no failure sends nothing; 4 spells → 3 events, 4 breadcrumbs; `LockLost` mid-spell → no event,
  the "ended without recovery" breadcrumb; a successful save whose marker rewrite fails sets no kind; metadata values
  contain no `\` or `/`.
- Refresh: fail, fail, succeed → one `RefreshRecovered` with `failedAttempts = 2`.
- Full suite with `TERMPOINT_HARNESS=1`.
