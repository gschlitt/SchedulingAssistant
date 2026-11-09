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
