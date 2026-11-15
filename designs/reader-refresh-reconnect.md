# 4.6 Reader Refresh: always reopen the connection (1.2.4)

Finishes 1.2.3 item 11 (plan approved 2026-10-14). 1.2.3 shipped the honest messages, but not the reconnection.
MinimumVersionToWrite: **No** (a reader writes nothing to the shared database, and nothing new is stored).

## The story

**Today.** Carol has the database open read-only while Alice edits. Carol clicks Refresh. TermPoint copies the shared
database, closes Carol's connection to her read-only copy and renames the fresh copy over it. Defender is scanning
her old copy at that moment, so the rename fails. Carol sees "TermPoint couldn't refresh and lost its connection to
the database. Close TermPoint and open it again." Nothing reopens the connection, so every later click that reads
the database fails until she restarts.

**With the fix.** The same failed rename leaves Carol's old copy in place, and TermPoint reopens her connection on
it. She sees "Couldn't refresh just now. Showing data from 2:14 PM. Try again in a moment." That is true: her
next Refresh works once Defender lets go. If the reopen itself fails (the file stays locked), TermPoint tries twice
more, a second apart. Only if all three fail does she get the "Close TermPoint and open it again" line.

## What changes

**`CheckoutService.RefreshReadOnlySnapshotAsync`**
1. Once `beforeOverwrite` has been called, the method always ends by reopening through `afterOverwrite`, whatever
   happened after it. The reopen goes to the fresh copy if the rename worked, and to the previous copy if not. (It
   is the same path, D″, either way. A failed `File.Move` leaves the old file in place.) This holds even when
   `beforeOverwrite` itself threw, since the connection may be half-closed. `ReinitializeConnection` disposes
   whatever is there first, so calling it again is safe.
2. A reopen that throws is retried: **3 attempts, 1 s apart** (`Task.Delay`, so the window stays responsive).
3. Before each retry, the method stops if the session has moved on (`SessionId` changed, or `IsEnding`, both added
   in `454a38c`). A close or a switch during the retries must not reopen on a context that teardown disposed.
4. New outcomes in `RefreshOutcome`:
   - `ReplaceFailed`: the swap failed (rename, or `beforeOverwrite` threw), and the connection is open again on the
     previous data.
   - `ConnectionLost`: every reopen attempt failed, or the session ended first.
   - `Updated` and `SourceUnavailable` keep their meaning. `SourceUnavailable` now always means "the connection was
     never closed".
5. The log stays `LogInfo` only. Each attempt is logged (type and message).

**`MainWindowViewModel.RefreshReadOnlySnapshotAsync`.** The `connectionClosed` / `connectionReopened` flags go: the
outcome now says it.

**`RefreshPrompt.FailureLine`** loses its `connectionLost` parameter. The mapping:

| Outcome | Line (all existing, approved wording) |
|---|---|
| `Updated` | none; the list reloads and the tooltip time moves on |
| `SourceUnavailable` | `NotResponding`: "Couldn't refresh: the database location isn't responding. Showing data from …" |
| `ReplaceFailed` | `OtherFailure`: "Couldn't refresh just now. Showing data from … Try again in a moment." |
| `ConnectionLost` | `ConnectionLost`: "TermPoint couldn't refresh and lost its connection… Close TermPoint and open it again." |
| threw | `OtherFailure` |

The `ConnectionLost` constant's comment changes from "nothing in 1.2.3 reopens it" to "three reopen attempts failed";
its text stays the same. The handled events stay as they are (`RefreshConnectionLost` now only after three failures).
No list reload after a failure (unchanged).

**DEBUG switch (from the approved plan):**
`TERMPOINT_DEBUG_FAIL_REFRESH=replace|reopen[:count[:skip]]`, parsed by `DebugFaultBudget` like
`RestoreFaultPlan`. `replace` makes the rename throw. `reopen` makes the reopen throw `count` times (so `reopen:2`
recovers on the third attempt, and `reopen:3` gives ConnectionLost). It is DEBUG-only and compiled out of Release.
`unreachable` is dropped: clumsy or an unplugged cable already covers it.

## The four rules

1. **Atomic rename of a checked copy:** not touched. A reader never writes the shared database.
2. **Lock ours and database as last seen:** not touched. A reader holds no lock and writes nothing shared.
3. **Never delete the only copy of unsaved work:** D″ is a read-only copy of the shared database and holds no
   unsaved work. No delete is added. The `.tmp` clean-up and the WAL-sidecar delete are unchanged. The sidecars
   belong to a closed read-only connection, and D″ is complete without them.
4. **Never confuse the user:** one line per refresh, all existing approved wording. The "close and reopen
   TermPoint" line becomes rare, and when it shows, it's the right advice.

## What runs at the same moment

- **A double click on Refresh:** the `[RelayCommand]` refuses a second run while one is in progress (unchanged).
- **A close or switch during the retries:** step 3 stops the retries and returns `ConnectionLost`. The window is
  going away or now shows another database, and `PostForSession` already drops late messages. Test below.
- **Reads during the retries:** the connection is closed for up to ~2 s instead of milliseconds. A click that reads
  the database in that window (opening a flyout, say) fails as it would today during the swap. That's acceptable:
  the list isn't reloaded, and the second this matters the user has just clicked Refresh and is waiting for it.
- **No save, heartbeat, wake check or demotion runs in a read-only session.**

## Tests

- **Rename fails:** `ReplaceFailed`; `afterOverwrite` called once; the old D″ still readable through it; and
  `HashAtCheckout` unchanged.
- **`beforeOverwrite` throws:** `ReplaceFailed`, and `afterOverwrite` still called. This replaces today's
  `…SkipsAfterOverwrite` test, which pins the old behaviour.
- **Reopen fails once, then works:** `Updated`, with two calls. Reopen fails three times: `ConnectionLost`, three calls.
- **Session ends during the retries:** no further `afterOverwrite` call; `ConnectionLost`.
- **`RefreshPrompt.FailureLine`:** each outcome maps to its line, and a failure leaves the tooltip time alone (VM test).
- **No `LogError` anywhere** (existing `RecordingAppLogger` pattern).
- **Harness:** `Computer.RefreshAsync` already passes both callbacks, so random walks exercise the new path.
  Full suite with the harness before commit.

## Hand test (after the code)

DEBUG build, reader session, run with `TERMPOINT_DEBUG_FAIL_REFRESH`:
- `replace` gives the "just now" line, and the next Refresh works.
- `reopen:2` works and shows no line.
- `reopen:3` gives the close-and-reopen line.

## Who does what

Sonnet codes from a brief: service, enum, VM, prompt and tests; no `.axaml`, no `.md`, no commits. Opus reviews the
diff, runs both compile checks and the full suite with the harness.
