# Data persistence review: "Bob becomes editor" (2026-10-17)

A read-only analysis of TermPoint 1.2.3 as it stands on branch `Version_1.2.3` (HEAD `5cb36fa`, plus the then-uncommitted
step 10b, since shelved to `tmp/shelved/`). No code was changed. Every behaviour below was read from the code, with file and line where it matters.
It replaces path-by-path discovery with one picture: every situation Bob's work can be in, what the code does, and
which ones break a rule.

**The primary concern:** data must never be lost, and when unintended circumstances occur, getting it back must be a
clean and intuitive process.

**The statement every situation is checked against:** at every moment, every edit Bob has made exists in at least one
place that survives a crash, and the shared database is either the old version or the new one, never a mix and never
Bob's older version on top of Alice's. (Known exception: edits in an open inline editor that haven't been applied
live only in memory; item 14.)

**The four rules** (CLAUDE.md): (1) the shared database only changes by one atomic rename of a checked copy;
(2) never write it unless the lock is ours and it is exactly as we last saw it; (3) never delete the only copy of
someone's unsaved work until a copy exists somewhere else, guaranteed by the data layer; (4) never confuse the user.
In this review rule 3 also covers *overwriting* that copy, and rule 4 also covers *promising something that isn't
certain*.

---

## 1. Where Bob's work can be

| Place | What it is | Survives a crash? |
|---|---|---|
| An open inline editor | Typed but not applied | **No** (memory only) |
| Bob's kept file (`%AppData%\TermPoint\working\<8 hex>_<name>.db`, plus its `-wal`) | Applied but not yet saved | Yes, **if** its "unsaved" marker exists; without a marker the next start deletes it as "nothing was edited" |
| The "unsaved" marker (`….db.dirty`) | Says "unsaved work is here", and holds the fingerprint of the shared database Bob started from | Yes |
| `D.tmp` beside the shared database | A save in flight | Not as Bob's work: it is swept or overwritten |
| The shared database | Saved | Yes |
| A separate file Bob saved ("BIOL-TT (recovered).db") | Stranded work kept by hand | Yes, but nothing brings it back into the database |
| Backups | Snapshots of Bob's kept file, every 30 min, while he edits | Yes, 5 kept (about 2.5 hours) |

The **fingerprint** is a SHA-256 of the whole shared database file, taken when Bob opened it and again after each
successful save. It lives in memory during the session and in the marker on disk (`DirtyMarkerData.HashAtCheckout`,
plus `PendingSaveHash` for a save whose rename wasn't confirmed). A restore only goes ahead when the shared database
still matches it (`CheckoutService.cs:1048–1095`).

---

## 2. The lifecycle, situation by situation

Verdicts: **OK** · **Rule n** (breaks it) · **Confusing** (rule 4) · **Theoretical** (possible, very unlikely).

### 2.1 Opening for editing
| Situation | What the code does | Verdict |
|---|---|---|
| Nothing kept, lock free | Copies the shared database to Bob's kept file, checks the copy byte for byte, runs the version gate, takes the lock | OK |
| Kept changes from a crash, **nobody else edited since** | "Restore Unsaved Changes?" → Restore adopts the kept file after the fingerprint matches | OK |
| Kept changes, **someone else saved since** | "Restore Unsaved Changes?" → Restore → fingerprint mismatch → "Can't Restore Unsaved Changes" → Save a Copy… / Discard | **Confusing** (F7) |
| Kept changes, Bob **closes "Restore Unsaved Changes?" with the X** | Counts as Discard: the kept changes are deleted at once, no confirmation (`OfferCrashRecoveryAsync`, `ShowChoiceDialogAsync` treats X as the second button) | **Rule 3** (F1) |
| Kept changes, X on "Can't Restore Unsaved Changes" | Same: deleted, no confirmation (`HandleRecoveryConflictAsync`) | **Rule 3** (F1) |
| Kept changes, someone else holds the lock | Opens read-only; "Unsaved Changes Kept… TermPoint will offer to restore them the next time…" | **Confusing**: the promise may not hold (F6) |
| Kept changes, Bob uses "open as observer" | Recovery is never offered; the changes stay on disk, unmentioned, until he turns observer off | **Confusing** (F10) |
| Kept file damaged | Archived as `.corrupt-…` (kept for support), "unsaved changes lost" message | OK (honest) |
| Shared database unreachable / in use / missing | The open stops with its message; kept changes untouched | OK |
| Could a normal open copy over kept changes? | Every route into a normal open first either offers recovery or deletes the kept files on an explicit choice; observer and licence-read-only opens use the read-only copy | **OK today**. Becomes a rule to keep in any redesign (the "can't tell" case in 10c) |

### 2.2 Editing
| Situation | What the code does | Verdict |
|---|---|---|
| First edit after open or after a save | Repositories call `MarkDirty` **before** the write, so the marker (with the fingerprint) exists before the data changes (`AcademicYearRepository.cs:45` and every repository) | OK |
| The marker can't be written (twice) | Banner: crash protection is reduced. A crash then loses the edits (the start-up sweep deletes the unmarked kept file) | OK (told), but loss is possible |
| Edits in an open inline editor | Memory only until Apply | **Rule 3** on close/switch (F3, item 14) |
| Power loss mid-edit | The kept file uses WAL with SQLite's default full sync; committed edits survive | OK |

### 2.3 Saving (manual or autosave)
Steps (`SaveAsyncCore`, `CheckoutService.cs:1609`): 1 lock is ours → 2 fingerprint matches → 3 snapshot the kept file
locally, stream it to `D.tmp` → 4 check `D.tmp` byte for byte → 5 rename `D.tmp` over the database → 6 update the
fingerprint, delete the marker.

| Situation | What the code does | Verdict |
|---|---|---|
| Lock file removed, nobody else claimed it | Re-creates the lock atomically, carries on, auto-dismissing notice | OK |
| Lock held by someone else | No write; Bob demoted to read-only (2.5) | OK (rule 2) |
| Database changed since Bob's fingerprint | No write; conflict banner (10a) | OK (rule 2) |
| Network drops at any step before 5 | No rename; kept file and marker intact. Yellow banner: "TermPoint couldn't save because the database location isn't responding. Your changes are kept." + "TermPoint will try again automatically." (autosave; clears itself after 30 s) or "Try saving again in a moment." (manual; stays). Variants: "in use by another program or can't be accessed", "writing to the database location failed", "didn't pass its check. The database wasn't changed" | OK for data; **Confusing** (F15) |
| Rename times out | Records the snapshot's fingerprint as "pending" in memory and marker, so a late-landing rename is recognised as Bob's own | OK |
| **An edit lands between step 3 (snapshot) and step 6** | The marker is deleted and "unsaved" cleared although that edit isn't in the database. A crash before the next save → the start-up sweep deletes the kept file → the edit is gone | **Rule 3** (F2, item 15) |
| Crash between step 5 and step 6 (milliseconds) | The database already holds Bob's save, but the marker still has the old fingerprint → next open: "Can't restore", though his changes are already in | **Confusing** (F8) |
| **Nothing was edited** | Saves anyway: every session starts "unsaved" (`SessionDirty = true` at checkout, line 766) and `SaveAsync` never skips. The first save of any editing session rewrites the shared database's bytes even with no edits (confirmed with SQLite's backup: same data, different bytes; later no-edit saves are stable) | **Causes F5** |
| Lock lost **after** step 1 and a delayed rename lands later | Step 5 doesn't re-check the lock. Needs Bob's rename to land after Alice took over a stale lock (≥ 180 s stale + 70 s confirmation) | **Theoretical** (F12) |

### 2.4 Leaving: close, switch, restore
| Situation | What the code does | Verdict |
|---|---|---|
| Normal close, save succeeds | Save, release, dispose, delete kept file | OK |
| Close, save fails (network) | Lock parked (left in place so nobody writes under Bob's changes until it goes stale), kept file and marker kept, "Changes Not Saved… TermPoint will offer to restore them" | **Confusing**: the promise may not hold (F6) |
| Second click on X during the exit save | The process ends mid-save; edits made during the exit save are deleted with the kept file | **Rule 3** (item 15) |
| Close or switch with an open inline editor | Unapplied edits silently dropped | **Rule 3** (item 14) |
| Close/switch with a save conflict (10b) | One question; saved as a file or kept | OK (10b) |
| Demoted + conflicted Bob clicks "Take over as writer" | Goes through the switch path, so 10b asks "before **switching databases**?" for the same database | **Confusing** (F11, 10b uncommitted) |
| Switch, save fails | As close: kept, "Changes Not Saved" | **Confusing** (F6) |
| Restore from a backup | Saves first; refuses if the save fails; keeps the replaced database beside it | OK (item 5) |

### 2.5 Losing the lock, the network, or the computer's attention
| Situation | What the code does | Verdict |
|---|---|---|
| Network down while editing | Saves fail softly; heartbeat failures (about 3 min) → check → "unreachable" → Bob stays editor, plus a second, auto-dismissing banner: "Network connection interrupted — your changes are safe locally but cannot be saved until the connection is restored." | OK for data; **Confusing** (F15) |
| Network down > ~3 min | Others see a stale lock and may take over (with a warning naming Bob). When Bob's network returns, his next save finds their lock → demoted | OK for the database; see next rows for Bob's work |
| Demotion | Banner; connection closed before anything else; kept file and marker **preserved**; Bob reads from a read-only copy | OK for files |
| **Writes after demotion** | The read-only copy is opened read-**write** (`DatabaseContext.ReinitializeConnection`, `Mode=ReadWrite`). Write blocking is UI-only (`IsWriteEnabled`/`CanWrite`). An inline editor that was already open has an `onSave` that writes without checking (`SectionListViewModel.cs:1104`). If it stays open across the demotion, Apply "succeeds" into the read-only copy, which is thrown away at the next refresh or close | **Rule 3 / 4**, to verify (F4) |
| Lock-lost banner | "TermPoint will offer to restore them the next time you open this database" | **Confusing** (F6) |
| Switching database while the demotion is still copying | The old demotion finishes against the new session: read-only while it holds the new lock | **Rule 3 / 4**, confirmed by test (F16) |
| Sleep, then wake | Lock re-checked off the UI thread; same outcomes as above | OK |
| Lock file damaged (not deleted) | Treated like removal; can't be re-created; message says "removed by another program" | **Confusing**, minor (F13) |

### 2.6 Bob's computer crashes or loses power
| Situation | What the code does | Verdict |
|---|---|---|
| Unsaved edits, Bob returns to **the same computer**, nobody took over | His dead lock is reclaimed automatically (same machine, process gone); Restore works | OK |
| …someone took over the stale lock and saved meanwhile | Restore refused → separate file only | OK for data; F7 for the order of questions |
| …someone took over and **changed nothing** | Their first save still rewrites the database (F5) → Bob's restore is refused anyway | **F5** |
| Bob moves to **another computer** | His kept changes stay on the first computer; the second sees his stale lock (naming himself) and can take over | OK for data, but nothing tells him his changes are on the other computer (F14) |
| Crash mid-save | See 2.3: every cut point leaves either the old database or the new one; leftovers are swept | OK, except F8 |

### 2.7 What other people and programs do
| Situation | What the code does | Verdict |
|---|---|---|
| Alice on 1.2.2 joins through the wizard | Rewrites the database header → Bob's saves refused → 10a/10b | OK (release note: update everyone together) |
| Alice on 1.2.2 edits records | 1.2.2 has no unknown-field catch-all; fields newer versions add could be dropped. 1.2.3 adds none yet; the version gate exists for when it does | OK for 1.2.3 |
| IT restores an old backup over the database | Fingerprint changes → Bob's saves refused → conflict banner | OK |
| Antivirus or backup tools hold the file | Saves report "in use" and retry | OK |
| The database is in a cloud-synced folder | Two people can both get the lock; the fingerprint check turns that into stranded work | Item 13 |

### 2.8 What the environment does to Bob's local files
Unknown which sites use these, so they are assumed to happen somewhere.

| Effect | What happens | Verdict |
|---|---|---|
| Kept files vanish between sessions (non-persistent VDI, profile reset) | Unsaved changes after a crash or Close Anyway are gone | Unavoidable while they live in the profile |
| Kept files roam to another computer | Recovery is offered there too; the fingerprint keeps it safe | OK for data |
| Kept files roam **back older**, after being dealt with | Bob is asked again about changes he already saved or discarded | **Confusing** (F9) |
| `AppData\Roaming` redirected to a server | Bob's "local" kept file, its WAL and the save snapshot all sit on the network: slow, can go offline, and SQLite's WAL is not meant for network files | **Risk** (F9) |

All of 2.8 stems from one choice: the working folder is `Environment.SpecialFolder.ApplicationData`, which is
`AppData\Roaming` (`CheckoutService.cs:531`). `%LocalAppData%` never roams and isn't redirected.

---

## 3. Findings, most serious first

| # | Finding | Rule | New or known | Size of fix |
|---|---|---|---|---|
| **F1** | The X on "Restore Unsaved Changes?" and on "Can't Restore Unsaved Changes" deletes the kept changes, with no confirmation. Discard itself is never confirmed either | 3 | **New. In 1.2.2 today** (same code on `master`) | Small: X re-asks; Discard confirms |
| **F2** | An edit made during a save loses its marker; a crash before the next save deletes it. **Confirmed by the harness (9.3c-2b):** open, Save held at `Save.AfterLocalSnapshot`, Bob edits meanwhile, Bob's computer crashes: his kept working file has no marker and the edit is nowhere (3 events; `KnownSequences`). The clean-close face (item 15's "edits made during the exit save are deleted") needs no crash: a close held at `Save.AfterMarkerDelete` or `Release.AfterFinalSave` while Bob edits deletes the edit with the working copy | 3 | Known (item 15) | Medium: edit counter |
| **F3** | Unapplied inline edits dropped at close or switch | 3 | Known (item 14) | Medium |
| **F4** | After a lost lock, writes still reach the read-only copy (connection is read-write; only the UI blocks), so an editor left open could "save" into a file that is thrown away | 3/4 | **New**, needs one check (does the editor stay open across demotion?) | Small: open read-only copies with `Mode=ReadOnly` so any write fails loudly |
| **F5** | Every editing session rewrites the shared database at its first save, even with no edits. So *anyone* opening it for editing after Bob's crash or lost lock makes his changes unrestorable, and the "restore if nobody changed anything" path almost never happens | 4 (and makes C harder) | **New** | Small to medium: skip a save when nothing changed (check what the version-stamp raise relies on first) |
| **F6** | Four messages promise a restore that may not be possible: the lock-lost banner, "Unsaved Changes Kept", and both "Changes Not Saved" messages | 4 | Partly found in 10c | Small: one honest wording, in a prompt class |
| **F7** | "Restore?" is asked before checking, then "impossible" | 4 | Found (10c) | Small/medium (10c's early check) |
| **F8** | A crash in the milliseconds between rename and marker delete leads to "can't restore" though the changes are already in the database. **Confirmed 2026-10-20 by the crash sweep (step 9.3c-2a):** open, edit, Save with the power cut at `Save.AfterRename`, open again and choose Restore ends in "Can't Restore Unsaved Changes" while D already holds the edit (4 events; `KnownSequences`) | 4 | **New** | Small: always record the save's fingerprint as pending in the marker *before* the rename |
| **F9** | Kept files live in roaming `AppData`: they can roam back older, or sit on a server under folder redirection | 3/4 | **New** | Medium: move to `%LocalAppData%`, with a one-time migration that still finds 1.2.2's kept files |
| **F10** | Observer mode never mentions kept changes | 4 | **New** | Small |
| **F11** | 10b's switch wording on "Take over as writer" for the same database | 4 | **Moot**: 10b shelved 2026-10-18 | — |
| **F12** | The save doesn't re-check the lock (or the database) just before the rename. **Confirmed by the harness 2026-10-19:** with another session's lock placed in the instant before Bob's rename, his save still lands over the database. Realistic route: Bob's laptop sleeps mid-save (after `D.tmp` is checked), Alice takes over the stale lock and saves, Bob wakes and the rename lands over Alice's save. Her edits leave the database (I1) | 1, 2 | **New, raised from theoretical** | Small: re-check the lock and the fingerprint immediately before step 5, as the restore already does |
| **F13** | A damaged lock file is reported as "removed"; jargon | 4 | Known | Small |
| **F14** | Kept changes on another computer are invisible from this one | 4 | **New** | Hard; probably a message only |
| **F15** | Messages during a network outage: the autosave banner clears itself after 30 s while saves keep failing (nothing on screen until the next autosave, 10 min later); "Try saving again in a moment" is wrong advice in a long outage; the heartbeat adds a second message for the same problem, worded "safe locally"; closing then gets "Changes Not Saved" with its restore promise (F6) | 4 | **New** | Small/medium: one persistent "can't reach the database" state that clears only when a save works, one wording |
| **F16** | A lost-lock demotion that is still running when Bob switches database lands on the NEW session. **Confirmed 2026-10-20 by `CheckoutServiceTests.F16_ADemotionStillRunningWhenTheUserSwitches_DoesNotTouchTheNewSession` (skipped until fixed).** The demotion (`DemoteToReadOnlyAsync`, `CheckoutService.cs:3508`) stays at `Mode = WriteAccess` until its D'' snapshot is made (`SetupReadOnlySnapshotAsync`, a network copy and hash, `:3967`/`:3990`). If Bob switches during that copy, the switch's own release and the checkout of the new database (`:597`, `:790`) complete first. The demotion then goes on with the service's CURRENT `SourcePath` (it re-reads it at `:3990` and `:4013`: the log shows "D'' copy hash mismatch, retrying"), copies the NEW database, and sets `WorkingPath = D''`, `Mode = ReadOnly` and `IsReadOnlyMode` (`:4048-4050`). Result: the new session holds the new database's lock but is read-only to the checkout service. Saves return `NotInWriteMode` (`:1423`), `MarkDirty` writes no marker (`:2444`), and the exit release skips its final save (`:3030`), while the lock service still says writer, so the window may still allow editing. **Data may be at risk (not yet demonstrated end to end):** edits made in that session after the demotion lands would not be saved at close and would have no marker, so the next start would clear the working copy as "no edits". The window is the duration of the D'' copy, and it needs the demotion to have STARTED before the switch (a heartbeat check, a wake check, an autosave or the Save button found the loss) and Bob to switch during the copy. The switch's own final save also raises the loss, but in production that handler is posted (`Dispatcher.UIThread.Post`, then `InvokeAsync`) and, by code reading, runs after `ReleaseAsync` has set `Mode = ReadOnly` (`:3054`), where the guard at `:3512` makes it a no-op. The harness dispatches synchronously, so there that same handler runs inline and can show the clobber (the dept.db to other.db story); that story is partly a harness artifact, the unit test above is the production-shaped route. **Confirmed by the harness (9.3c-2b)** in the pause form: Bob's computer wakes to find his lock taken over, the demotion that starts is held before its D'' copy (`Demote.AfterWorkingCopyHandled`), Bob switches to the other database and gets write access and its lock, and when the demotion goes on it copies the NEW database and leaves Bob's session read-only while it holds the new lock. Both clauses of invariant I6 fire (a demotion that started on dept.db finds the session on other.db; a read-only session holds other.db's lock); 4 events, on `KnownSequences`. The harness-inline form is also reached by the search (`Open`, `AdvanceTime(4m)`, `Open(alice)`, `SwitchAway`), so three older proof tests that run it now declare F16 | 3 (possible), 4 (read-only while holding the lock) | **New** | Small/medium: tie a demotion to the session it was started for (a session number captured at the start, checked before each state change), and refuse a switch until a running demotion has finished |

---

## 4. Beyond the session: the rest of the primary concern (inventory, not full analysis)

### A. Saved data that gets lost
- **Backups** (`BackupService`): every 30 min, 5 kept (about 2.5 hours), only while someone has edit access, only if
  that user has set a backup folder (none otherwise; joining through the wizard doesn't require one).
  - No "skip if unchanged", so an idle editing session pushes out older, different backups within 2.5 hours.
  - A snapshot that fails its integrity check is still copied and counted, so it can push out a good one.
  - Plus a sections CSV beside each backup.
- **Restore from a backup** is safe (item 5) and keeps the replaced database, but choosing one means reading
  timestamps; nothing says what changed between them.
- **Deleting a semester or academic year** is confirmed (item 8). There is no undo.
- **Not checked yet:** whether the CSV and shared-schedule imports can replace or delete existing data, and what they
  keep.

### B. Data dropped by TermPoint's own operations
- Fixed this release: section Flag/RoomTypeId and course Capacity/Level lost on edit. Guarded by `SectionCloneTests`
  and the `ExtensionData*Tests`. Persisted records derive from `PersistedRecord`, and 19 editor and list view-model
  files call `WithExtensionDataFrom`. I did not check that *every* rebuild path in them does; the tests enforce it
  for sections and courses.
- Every repository marks unsaved work before writing; no writes bypass the repositories (checked).
- Open: Copy Semester drops meeting room types (parking lot P3); campus queries use the wrong JSON key case (P8).
- **Not checked yet:** Copy Semester, Empty Semester and the imports, field by field.

### D. Losses nobody would hear about
Recorded only in the local log (item 7 would report them): refused saves, lost locks, refused restores, **kept
changes deleted by the X** (F1), and **the start-up sweep deleting an unmarked kept file**. Once 1.2.3 ships, there
is no way to know whether any of these happen in the field.

## 5. C: what "restore" should mean for stranded work (design question)

Today the best ending for stranded work is "saved as a separate file". There's no clean way back into the database:
Bob retypes his changes, or replaces the database and loses Alice's.

TermPoint keeps only a *fingerprint* of the version Bob started from, not the version itself, so it can tell that
the database changed but can't show **what Bob changed**. A future "review my changes and bring them in" needs that
starting version.

**Cheap step to decide now:** keep a local copy of the starting version beside the kept file (one extra local copy
per editing session, deleted with the kept file). F5's fix makes this more valuable: with no-edit saves skipped, "the
database didn't change" becomes true far more often, and restores work far more often.

---

## 6. Not verified, and how to check

| Item | Check |
|---|---|
| F4: does an open inline editor survive a demotion? | Hand test: open a section editor, delete the lock file and take the lock from another session, wait for the lock-lost banner, then Apply |
| F5 in TermPoint itself (tested with Python's SQLite, same backup API) | Probe test: check out, save with no edits, compare hashes |
| Roaming-profile sync semantics (which copy wins) | None available here; treated as possible |
| macOS equivalents of 2.8 | Not reviewed |
| Imports and Copy/Empty Semester field preservation | A field-by-field read, as done for the editors |

## 7. Suggested order, for discussion

> Superseded on 2026-10-18 by sections 8 and 9: the redesign removes F1, F6 and F7, and 9.5 sets the order. Kept for
> the record.

1. **F1**, plus a decision on a 1.2.2 hotfix. It's small, it's in the field, and it deletes the only copy.
2. **F4** check, then fix (one connection flag).
3. **F5, F6, F7, F8** together: one redesign of the kept-changes lifecycle (supersedes 10c).
4. **Items 14 and 15** (F3, F2).
5. **F9**, the move to `%LocalAppData%`, with migration.
6. **Item 7** observability, including the D list.
7. Then the rest: F10–F15, the A/B checks, and the C decision.

---

## 8. Decision (2026-10-18): kept changes are never asked about

**Status:** design note, agreed 2026-10-18 (decisions in 8.7). It replaces the rest of spec item 10's plan
(10b and the 10c outline are shelved in `tmp/shelved/`).

### 8.1 The idea
Today Bob's kept changes live in a single **slot**: the working-file path derived from the database's path, plus its
"unsaved" marker. A normal open needs that same path, so while kept changes sit there nothing can open until someone
decides their fate. That is why Bob is asked questions, and why a wrong answer (or the X) can delete his work.

From now on **checks decide, not Bob**, and work that can't go into the database is **moved out of the slot** into
**Recovered Versions**, a folder TermPoint manages. With the slot free, the open just proceeds; nothing is ever deleted
as a side effect of opening; deleting a recovered version is a separate, deliberate act.

### 8.2 The lifecycle of Bob's work (one database, one computer)
| State | Where the work is | Leaves this state when |
|---|---|---|
| **In session** | The slot, marked "unsaved" after the first edit | A save works (marker cleared) · the session ends without a save (→ Kept) · a save is refused because the database changed (→ Conflict) |
| **Conflict** | The slot; the session goes on, every save is refused | The session ends in any way (Reopen button, close, switch, take over): the work is **moved** (→ Recovered) |
| **Kept** | The slot, after a crash, power loss, a failed exit save or a lost lock: whether it can still go in isn't known yet | The next open on this computer checks (8.3) |
| **Recovered** | Recovered Versions | Bob deletes it (no expiry in 1.2.3; 8.7) |

### 8.3 What the next open does with Kept work
The check compares the shared database with the fingerprint in the marker (byte-level today; data-level later, 8.6).

| Check says | What happens | Message |
|---|---|---|
| **Unchanged** | The work is adopted and saved straight away, as today's Restore does. No question: these were applied edits that autosave would have saved anyway | M1 |
| **Changed**, or the marker has no usable fingerprint | Moved to Recovered Versions, then the normal open | M2 |
| **Can't tell** (location not responding, file in use) | Nothing moves; the open stops with its usual message. **A normal open never runs while work is in the slot** (it would copy the database over it) | Usual message + "Your unsaved changes are kept." |
| Someone else holds the lock | Nothing moves (it may still go in later); opens read-only | M4 |
| Observer mode, unchanged | Nothing moves; opens read-only | M6 |
| Observer mode, changed | Moved (no edit access is needed to move it) | M2 |
| Kept file damaged | Archived for support, as today | Today's "unsaved changes lost" message |

### 8.4 Moving work to Recovered Versions, safely
1. Snapshot the **closed** slot file with SQLite's backup (16a's routine; captures changes still in the WAL) into a
   temporary file inside Recovered Versions.
2. Check it (integrity), then rename it into place, beside a small `about.json`: database name, when the changes were
   made, why they couldn't go in (and who changed the database, when known), and the marker's identity.
3. **Only then** delete the slot and its marker.

A crash at any point leaves the slot intact, so the next open moves it again; the marker's identity in `about.json`
stops a duplicate. Location: `%LocalAppData%\TermPoint\recovered\` (never roams, never redirected; see F9). The
destination is TermPoint's own, so there are no name clashes, no picker and no "choose a new name".

### 8.5 Messages (draft wording)
**Wording rule:** never "safe" or "kept" in a way that suggests all is well. When work can't go in, say what it is
good for: **for reference**. A recovered version can be opened and read, but nothing in TermPoint brings it back
into the database.

- **M1, added:** "TermPoint added the changes you hadn't saved the last time you used "BIOL-TT"."
- **M2, moved at open:** "Your changes from the last time you used "BIOL-TT" couldn't be added, because someone else
  changed the database in the meantime. TermPoint has kept your version in Recovered Versions for reference."
- **M3, the conflict banner** (replaces 10a's buttons): "TermPoint can't save your changes: "BIOL-TT" was changed by
  someone else since you opened it. Reopen the database to see the latest version. TermPoint will keep your recent
  version in Recovered Versions for reference." **[Reopen the Database]**. Closing or switching does the same, with
  no question.
- **M4, outcome not known yet** (lost lock, failed exit save, someone else editing): "Your latest changes couldn't be
  saved to "BIOL-TT". TermPoint will add them the next time you open it, unless someone else changes the database
  first; then it will keep your version in Recovered Versions for reference." Replaces the four restore promises
  of F6.
- **M6, observer, unchanged:** "Your changes from last time aren't in "BIOL-TT" yet. Open it for editing to add them.
  If someone else changes the database first, they can't be added, and TermPoint will keep your version in
  Recovered Versions for reference."
- **Recovered Versions list rows:** "BIOL-TT: your version from Oct 17, 2:14 PM (not added: the database was changed
  by Alice)".

### 8.6 Not in the first steps (decide after step 4)
- **Data-level "did it really change?"**: compare the records rather than the file's bytes, so header-only changes
  and no-edit saves don't count. Needs the starting version on disk (keep each save's local snapshot instead of
  deleting it). Also the base for a later merge.
- **F5, skip saves when nothing changed**: small and independent; worth doing in 1.2.3 either way.

### 8.7 Decisions (settled 2026-10-18, as recommended)
1. **Expiry: none in 1.2.3.** Recovered versions stay until Bob deletes them. The files are small, and deleting
   work automatically sits badly with rule 3.
2. **The Recovered Versions list offers:** Open (read-only, as a separate database) · Save a Copy… · Delete…
   (confirmed).
3. **M3 reopens with the button**, so Bob isn't pulled out of what he is looking at. Closing or switching does it
   anyway.

### 8.8 Against the four rules
1–2: nothing here writes the shared database; the save pipeline, the lock and the byte check before every save are
unchanged. 3: work is deleted only after a checked copy exists in Recovered Versions, or by Bob's confirmed Delete;
no dialog answer and no X can delete it. 4: no questions about kept work; one message per event; no promise that
isn't certain (M4 and M6 say both outcomes); nothing calls stranded work "safe".

### 8.9 Steps (each removes what it replaces in the same commit)
1. **Recovered Versions store**: service and tests, no UI. Reuses 16a's snapshot routine.
2. **The open decides by itself** (8.3). Removes "Restore Unsaved Changes?", "Can't Restore Unsaved Changes",
   `HandleRecoveryConflictAsync`'s loop and the picker. F1, F6 (at open) and F7 go with them.
3. **The conflict banner moves and reopens** (M3). Removes 10a's Save a Copy… / Discard paths; keeps
   `HasSaveConflict`, the banner state and the end-the-session route. Close and switch move without asking.
4. **Recovered Versions list** (8.7.2), and M4 replacing the lock-lost banner's and the exit/switch messages'
   promises.

---

## 9. Verification: a new assessment, and exhaustive automated tests

### 9.1 Make the lifecycle testable first
Most decisions that matter live in `MainWindow.axaml.cs` (3,555 lines, 187 references to `App.Checkout`,
`App.Services`, `App.InstanceGuard` and friends), which no unit test can drive. Before the rework they move into one
non-UI class. **Pure refactor: no behaviour change, no wording change, no reordering.**

**What moves** (into `Services/SessionLifecycle.cs`, desktop only, excluded from the browser build like
`CheckoutService`):

| Group | `MainWindow` methods today |
|---|---|
| Open (startup and switch share it) | `OpenStartupDatabaseAsync` (from the guard onward), `CleanUpAndOfferRecoveryAsync`, `OfferCrashRecoveryAsync`, `RunCheckoutAsync` (every outcome, the stale-lock prompt and wait), `AskTryAgainAfterVersionCheckAsync`, `SetupSnapshotOrAskAsync`, `ShowRecoveryDeferredMessageAsync`, `OpenWithLocalCopyCheckAsync`, `GiveUpLocalCopyOpenAsync` |
| Kept and stranded work | `HandleRecoveryConflictAsync`, `ExportRecoveredCopyAsync`, `ShowExportOutcomeAsync`, `ResolveSaveConflictAsync`, `PickRecoveredCopyPathAsync`, `ResolveKeptChangesAsync` |
| Switch | `SwitchCoreAsync`, `AbandonFailedSwitchAsync`, `RecoverToDatabaseChooserAsync`, `ReleaseSwitchTargetGuard` |
| Leave | `OnClosing`'s teardown body (both branches), `EndSessionAfterRestoreAsync`, `SaveThenRestoreAsync`, `RestoreFromBackupAsync`'s sequence |
| Lost lock | `OnWriteLockLost`'s body, `BuildLockLossBanner` |

**What stays in `MainWindow`:** the window events themselves (`OnOpened` and `OnClosing` become thin shells that call
the lifecycle and keep the close-refusal guards), the loading curtain, the first-run wizard and tour, the dialog
implementations, the wiring of `CheckoutService` events to the view model, and the detached panels.

**The new class's shape:**
- **Constructor:** the real services it already uses: `CheckoutService`, `DatabaseInstanceGuard`, the logger, the
  settings, the licence evaluation (passed as a delegate), plus two seams:
- **`ILifecyclePrompts`**: everything that asks or tells the user. Show a message; ask a two-button choice; run work
  under a wait dialog; pick a file to save; the stale-lock wait; the database chooser. `MainWindow` implements it
  with today's dialog code, unchanged. Tests use a scripted fake that answers from a script and **records every
  title and text** (this is also what invariant I5 needs).
- **`ISessionHost`**: everything that touches the window or the container. Open the services on a working file
  (today `App.InitializeServices`) and return the session; release them; close secondary windows; stop the backup
  timer; wire up the window; cover it when nothing is open; is an inline editor open; close and reopen the data
  connection (for demotion); capture and restore the semester selection. `MainWindow` and `App` implement it. Tests
  use a fake host that opens a plain `DatabaseContext` on the working file, so a test can edit through real
  repositories.
- **Results, not flags:** each entry point returns what happened (opened / stayed / nothing open, and why), so the
  window only reacts.
- **Same thread, same order:** it runs on the UI thread exactly as the code does today. No `ConfigureAwait` changes,
  no new background work.

**How we prove nothing changed:**
1. **Moves are verbatim.** Each method moves as is. The only edits are call targets (`ShowMessageAsync(…)` becomes
   `_prompts.ShowMessageAsync(…)`, `App.Checkout` becomes `_checkout`). The diff is reviewed with
   `git diff --color-moved=dimmed-zebra`, so every moved block shows as unchanged apart from those targets. No logic,
   wording or ordering edit rides along; anything found that needs fixing is noted for later steps.
2. **Characterisation tests pin today's behaviour** through the fakes: one scripted scenario per branch, about 40.
   They pin the exact dialog texts, the answers, the order of operations and the files left on disk. They
   deliberately include today's known-bad behaviour (for example, F1: X on "Restore Unsaved Changes?" deletes the
   kept changes), named as such, so the later steps change them visibly and on purpose.
   - **Ordering invariants pinned:** the guard before any sweep; recovery offered before checkout; the services
     disposed before any working file is deleted; the lock released without saving on the restore and conflict
     paths.
3. **The existing suite stays green** (2604 passed + 21 skipped), and the compile check passes for both targets.
4. **A short hand smoke test** on a database copy, after the last move:
   - startup open;
   - switch;
   - close with and without edits;
   - crash recovery (Restore and Discard);
   - stale-lock takeover;
   - lock lost mid-session;
   - the 10a conflict banner;
   - restore from a backup.

   The DEBUG fault switches that already exist (`TERMPOINT_DEBUG_FAIL_CHECKOUT`, `…_INTEGRITY`, `…_RESTORE`) cover
   the failure branches.

**Sub-steps, one commit each:**
- **9.1a:** the two seams. `MainWindow` implements them and calls its own dialogs through them. No moves yet; tiny.
  **Done 2026-10-18 (uncommitted):** `Services/ILifecyclePrompts.cs` (6 members) and `Services/ISessionHost.cs`
  (8 members), both excluded from the browser build like `MainWindow`. `MainWindow` implements them explicitly,
  each member delegating to the existing code, and the methods 9.1b/9.1c will move now call through `Prompts` and
  `Host` (64 call sites). The two identical save-picker blocks became one member. The suite is unchanged: 2604
  passed + 21 skipped.
- **9.1b:** move the open and switch groups, with their characterisation tests.
  **Done 2026-10-18 (uncommitted):** `Services/SessionLifecycle.cs` holds the open, recovery-at-open and switch
  code (about 1,260 lines out of `MainWindow`), plus `Services/IOpenedDatabase.cs` (the view model stays out of the
  lifecycle) and five new `ISessionHost` members (`IsDatabaseOpen`, `CloseWindow`, `ShutDown`,
  `ReportFatalAndShutDownAsync`, `TryCaptureSemesterSelection`). A method-by-method comparison against the old code
  shows only the allowed edits. `MainWindow` creates the lifecycle on first use, because the XAML previewer
  constructs the window and the constructor never touched `App`'s statics before.
  - **Characterisation tests:** 32 in `SessionLifecycleTests`, with `ScriptedPrompts` and `FakeSessionHost`. The
    host runs the real open-time integrity gate. Known-bad behaviour is pinned by name (`Pins_F1`, `Pins_F6`,
    `Pins_F10`). Suite: 2636 passed + 21 skipped.
  - **Quirks pinned, existing behaviour:** after "Can't Restore" → Save a Copy, the open still runs a needless save
    of the fresh copy (harmless; goes with the redesign); the startup failure paths leave the single-instance guard
    held while the app closes.
  - **P5 is live:** the new tests leave the real `settings.json` untouched (they use `SettingsFileGuard`), but the
    existing wizard tests rewrite its `DatabasePath` to a temp test file on every run. Fixing P5 (a test-only
    settings folder) is a prerequisite for the 9.3 harness.
- **9.1c:** move kept and stranded work, leave and lost lock, with their tests.
  **Done 2026-10-18:** move committed as `2af8f06`: the close teardown (`TeardownForCloseAsync`), restore from a
  backup, 10a's conflict-banner resolution and the lost-lock body (`HandleWriteLockLostAsync`), with their shared
  state. New seams: `ISessionHost.RelaunchApplication` (so tests never start a process) and `ILockLossSurface`
  (captures the view model and connection once, exactly as the handler did). `MainWindow` keeps thin shells for
  `OnClosing`, `OnWriteLockLost` and the view models' delegates. A block-by-block comparison against the old code
  shows only the allowed edits.
  - **Characterisation tests:** 28 in `SessionLifecycleLeaveTests`, sharing `SessionLifecycleTestBase` with the 9.1b
    tests. Suite: 2664 passed + 21 skipped.
  - **Quirks pinned, existing behaviour:**
    - after a lost lock, a failed reconnect is still followed by a section-list reload on the closed connection
      (logged, harmless);
    - if disposing the services throws during close, the working-copy cleanup after it is skipped. The save had
      already succeeded, so no data is lost, but a stale working file is left behind;
    - when 10a's late copy fails, the reopen shows "Can't Restore Unsaved Changes" (goes with the redesign);
    - the tests read `CheckoutService`'s private autosave timer by reflection; there is no public "is autosave
      running" property.
- **9.1d:** the hand smoke test, then commit.
  **Done 2026-10-18:** the user ran all 12 rows on database copies (open, close and reopen, switch, crash with
  Restore and with Discard, stale lock, lost lock, conflict with Save a Copy and with Discard, close with the
  conflict pending, restore from a backup, two DEBUG failure switches). All behaved as before. Helper:
  `tmp/handtest_lock.py` (gitignored) plays Alice holding the lock. **Step 9.1 is complete.**

Each sub-step is implemented by Sonnet from a brief; Opus reviews the diff (moved-lines view) and re-runs the
compile check and the full suite independently. `MainWindow.axaml` itself is not touched.

### 9.2 Seams for faults, identity and time
What the harness needs that the code can't do today, found by reading it (2026-10-18):
- **Every actor would look like the same TermPoint.** The lock file records `Environment.MachineName`,
  `Environment.ProcessId` and `Environment.UserName` (`WriteLockService.cs:1175–1180`), and the same-machine checks
  compare against them (`:857`, `:864`). Alice and Bob in one test process would share a machine and a process: Bob's
  lock would look like Alice's own, and a "dead" Bob could never be reclaimed.
- **Timers run on their own.** The heartbeat (60 s), the reader poll (60 s), wake detection (30 s) and autosave are
  real `System.Threading.Timer`s, so they would fire at random moments in a run, and some times still come from
  `DateTime.UtcNow` rather than the injected `TimeProvider` (`:1172`, `:1235`, `:1330`).
- **No way to fail the network or stop the process at a chosen step.**

The seams, all test-only, each a no-op (or the current behaviour) in the app:
1. **Lock identity** (`WriteLockService`): an optional identity (machine, process ID, user name, and "is this
   process alive?") passed at construction. Default: exactly today's `Environment` values and `Process` check.
2. **Time**: the remaining `DateTime.UtcNow` uses in `WriteLockService` go through its existing `TimeProvider`.
   Production passes `TimeProvider.System`, so nothing changes.
3. **Manual ticks**: an option not to start the four timers, plus internal methods that run one heartbeat, one poll,
   one wake check or one autosave tick on demand. Default: timers start as today.
4. **Network faults** (`NetworkFileOps`): an internal static fault injector, consulted at the start of every
   operation, that can make an operation on the share time out (report "not completed") or throw. It is scoped by
   path, so the share can fail while local files keep working. Default: null, no checks.
5. **Crash points**: an internal static hook, `CrashPoints.Hit("Save.AfterRename")`, at named points in every
   multi-step operation: the save's six steps, the checkout, recovery adoption, release, the close teardown,
   demotion, the recovered-copy export, the restore's stages and the deletes of `DiscardCrash`. Default: null, no
   cost beyond a null check.
   - **How a crash is simulated:** throwing an exception isn't a crash, because the code's own `catch` and `finally`
     blocks would run and tidy up, which a killed process never does. Instead the hook **freezes the disk**: it
     copies the share folder and that computer's local folder at that instant, then stops the operation. The harness
     restores that copy and starts a fresh TermPoint on it, exactly as after a kill at that point. A power cut that
     loses data the OS hadn't written to disk yet is out of scope, as SQLite's durability already covers it.

**9.2a done 2026-10-18 (seams 1–3, uncommitted):**
- **Identity:** `Services/LockIdentity.cs`. `LockIdentity.Current` holds today's machine, user, process ID and start
  time, and a lookup wrapping `Process.GetProcessById`. `GetHolderLiveness` keeps its decision code; only three data
  sources changed.
- **Time:** every `DateTime.UtcNow` in `WriteLockService` reads its `TimeProvider`.
- **Manual ticks:** `startTimers: false` skips the four timers. `TickHeartbeatAsync`, `TickPollAsync`,
  `TickWakeCheck` and `CheckoutService.TickAutoSaveAsync` run one tick each, through the same extracted bodies the
  timers use.
- **Construction:** the identity and timer options are on an internal constructor, so existing callers are
  unchanged.
- **Tests:** 15 `LockSeamTests`. Suite: 2679 passed + 21 skipped.
- **Noticed, existing:** when `StartPolling` runs while a poll timer already exists (the takeover's lost race), the
  old timer is replaced without being disposed.

**9.2b done 2026-10-18 (seams 4–5, uncommitted):**
- **Network faults:** `Services/NetworkFaults.cs` (`TimeOut`, `TimeOutAfterRunning`, `Throw`, scoped by label and
  path). It is consulted once per operation: in `RunAsync`'s two forms, which the named helpers now give their paths
  to, and in `ComputeHashAsync` and `CopyAsync`. With no injector the public methods run the unchanged bodies.
- **Crash points:** `Services/CrashPoints.cs`, a catalogue of 45 named points across the save, checkout, recovery,
  release, demotion, discard, export and placement, restore, refresh, close, switch and the lock file. A test checks
  that the catalogue matches every `Hit` in the source.
  - Two restructures, both equivalent: the lock create closes its file before its crash point, and the heartbeat's
    two-step write is split in two.
- **Tests:** 15 `FaultSeamTests`. Suite: 2694 passed + 21 skipped.
- **For the harness:**
  - A ghost rename needs two faults. The save probes the database after a timed-out rename, so `File.Move` must run
    and then time out, and the database's hash must time out too.
  - Share I/O inside `WriteLockService` (create, reclaim, force, reader reads) bypasses `NetworkFileOps`. Fault it
    through the wrapping `RunAsync` labels ("lock acquire", "force lock acquire", "enter reader mode").
- **One unidentified failure:** in a slow full run (3 min 34 s instead of about 2 min 15 s) one test failed, and the
  run did not record which. Three later full runs and six runs of the new and lifecycle tests were all green. It is
  most likely an existing timing-sensitive test under load. Watch for it.

**Size and proof:** one small commit per pair of seams (1–3, then 4–5). Each is checked like 9.1: default behaviour
unchanged, the full suite green, and production diffs limited to the seam itself.

### 9.3 The harness
**Where:** a separate test project, `src/TermPoint.Harness.Tests`, so the normal suite stays fast and unchanged. It
runs on demand and before each release, for minutes; a variable sets the depth for longer runs.

**The world:**
- one temp folder playing the share;
- per **computer** (Bob's laptop, Bob's desktop, Alice's): a local folder (working files, instance guard), a lock
  identity, and its own `CheckoutService`, `WriteLockService`, `SessionLifecycle`, host fake and prompts;
- one shared `FakeTimeProvider`, so a "stale lock" means minutes of fake time, not real waiting.

**Edits are traceable:** every edit inserts a uniquely named row (`bob-17`, `alice-3`) into a test-only table, through
a real connection, so `MarkDirty` and the marker behave exactly as for real data. After any event the harness knows
exactly which edits exist in the shared database, in each slot, in each recovered or exported file.

**Events,** each enabled only when it makes sense in the current state:
- Bob or Alice: open (startup or switch), edit, save, autosave tick, close, switch away, reopen, Restore / Discard /
  Save a Copy / X at each prompt, the conflict banner's buttons, take over a stale lock;
- the world: the network to the share drops or returns; a computer sleeps and wakes; a crash at any named point;
  fake time passes (heartbeats go stale); the lock file is deleted; IT restores an old backup over the database.

**Prompts:** a policy fake answers each dialog from the event sequence (so "X on this dialog" is just another event)
and logs every message with what it claims ("your changes are kept", "will offer to restore") for I5.

**Invariants, checked after every event:**
- **I1, nothing committed disappears:** an edit that reached the shared database never leaves it, except through an
  explicit "IT restores a backup" or "restore from a backup" event. This is the check that catches Bob's older
  version landing on top of Alice's.
- **I2, nothing applied is lost:** every edit Bob applied is in the shared database, in his live session, in a slot
  with its marker, or in a recovered or exported file, unless an explicit, confirmed discard covers it. An X is
  never counted as a confirmed discard.
- **I3, the database is always whole:** the shared file always passes `PRAGMA integrity_check`.
- **I4, only the lock holder writes:** at the moment of every rename onto the shared database (a crash-point hook),
  the lock is the saver's.
- **I5, messages tell the truth:** every logged promise must come true later in the run. "Kept" means I2 holds and
  the work is offered or moved at the next open; "will offer to restore" means a restore really is offered.

**Strategies:**
- **Bounded exhaustive:** every sequence of enabled events up to a short length (about 3–4).
- **Crash sweep:** for each multi-step operation, a crash at each of its named points, then a restart.
- **Random walks:** long sequences from fixed seeds, so every run is repeatable.

**When something fails:** the failing sequence is shortened automatically (drop events while it still fails) and
printed as an Alice-and-Bob story: what each person did, the dialogs they saw, the files at the end, and the
invariant that broke.

**Known problems:** the harness keeps a list of the findings already known (F1, F2, F5, F6, F8, …). A failure that
matches one is reported as **expected**, so the run stays green while showing them. The redesign steps then remove
them from the list one by one, and the harness proves each is gone.

**Sub-steps, one commit each:**
- **9.3a:** the world, the actors and the traceable edits, with five hand-written walks (the hand smoke test rows,
  automated).
  **Done 2026-10-19 (uncommitted):**
  - **Location:** `src/TermPoint.Tests/Harness/` (`World`, `Computer`, `Inspector`, `HarnessWalkTests`). It sits in the
    existing test project, not a new one, so it reuses the lifecycle fakes. `[HarnessFact]` skips it unless
    `TERMPOINT_HARNESS=1` is set.
  - **Crashes:** between actions, by freezing the disk, discarding the objects and restoring the frozen files.
  - **The five walks pass:** clean hand-over, crash and Restore, stale-lock takeover, lost lock, and the 1.2.2
    colleague conflict. Normal suite: 2694 passed + 26 skipped (the 5 walks).
  - **Walk 3 needed one adaptation:** Alice closes before Bob restarts. While she still holds the lock, Bob's
    Restore ends in "Unsaved Changes Kept" (F6) instead of reaching "Can't Restore".
  - **Gaps for 9.3b/c:**
    - no sleep/wake event yet;
    - heartbeat and poll ticks only when a walk calls them;
    - the stale-lock prompt's age text reads the real clock (`StaleLockPrompt.BuildTakeOverMessage(…,
      DateTime.UtcNow)` in the lifecycle), so under fake time it reads "for a while";
    - edits per file are inspected on request, not tracked automatically.
- **9.3b:** the invariants and the story printer.
  **Done 2026-10-19 (uncommitted):**
  - **The checks:**
    - one funnel (`World.Do`) runs I1–I5 after every step;
    - the ledger of where every edit is;
    - dialog answers record Affirmative / Negative / closed with the X;
    - I4 observes the rename through the crash-point hook;
    - I5 uses a catalogue of message promises, and flags unclassified ones;
    - a findings classifier, with each walk declaring the findings it expects;
    - the story printer.
  - **Findings produced:** walks 1, 2 and 5 none; walk 3 F7; walk 4 F6 and F7.
  - **11 proof tests:** each invariant fires as intended, and the X on "Restore?" is classified as F1. **F12 is
    confirmed:** with Alice's lock placed just before Bob's rename, his save returns Success and lands over the
    database (raised in section 3).
  - **Results:** harness 16/16 green; normal suite 2694 passed + 37 skipped.
  - **Limitations:** checks run at step ends (I4 excepted); a restore-side I4 breach is not exercised; the
    support-archive and save-failure promises are wired but not yet driven by a walk.
- **9.3c:** the three strategies, the shrinker and the known-problems list.
  - **9.3c-1:** bounded exhaustive search, random walks, the shrinker and the known-findings list. Test code only.
    **Done 2026-10-20 (uncommitted):**
    - **Budget (the default):** exhaustive depth 3; 300 random walks of 30 events. Both can be raised with
      `TERMPOINT_HARNESS_DEPTH`, `_SEEDS` and `_STEPS`.
    - **Runs:**
      - exhaustive: 757 runs, 2205 events, 247 distinct states, 538 runs cut short by the memo;
      - random: 300 runs, 9000 events, 138 shrink replays.
    - **Known findings found, with the smallest story:**
      - F1: 84 runs, 4 events (crash, restart, Discard on "Restore Unsaved Changes?").
      - F5: 1 run, 7 events, reached only indirectly: IT restores an older copy of the file while a kept
        change waits, so the restore is offered and then refused.
      - F7: 5 runs, 7 events (a colleague changes the file, IT restores an older copy, crash, Restore, "Can't Restore").
      - F12: not found, as expected. It needs a pause in the middle of a save, which is step 9.3c-2.
      - F6: **not found by the search at the default budget**; rediscovered by the known-sequence replay list (see below).
    - **Planted-bug check:** a hook deletes the dirty marker at `Dirty.AfterMarkerWritten`, as if `MarkDirty`
      forgot it. A kept working file without its marker is deleted at the next start, so I2 fires. No known finding
      explains it, so it is classified new, and the shrinker reduces the failing walk to 4 events. This proves
      the "new violation fails the run" path.
    - **Every event enabled:** every event in the catalogue was enabled at some point in the runs; none is dead.
    - **F6 correction found in review.** The first full run listed F6 in 6 runs, smallest story 5 events: Bob edits,
      Alice takes over his stale lock, Bob switches to other.db. That was a misjudgement by the tracker, not a
      finding. The message ("Changes Not Saved") is about dept.db, and TermPoint keeps unsaved changes per database
      (the working copy is named after the normalised source path). The tracker judged the promise at Bob's open of
      **other.db** and found no restore offered, which is correct for other.db.
      - Fix: each "will offer to restore" promise now records the database it is about (read from the owner's
        checkout while the message is shown, normalised as production normalises it) and is judged only at the
        owner's next edit-access open of **that** database. Until then it stays pending, and the story's closing notes
        name the database.
      - The same story listed the identical F6 violation twice. Cause: one failure shows the promise in two messages
        (the lock-lost banner and "Changes Not Saved"), and each became its own promise. They are now one claim per
        person and database. Without that merge the new proof tests fail with two reports.
      - Proof tests (in `InvariantProofTests`): a promise about dept.db is not judged at the open of other.db and
        stays pending; two messages make one pending promise; the promise is judged once, at the next open of
        dept.db, where today "Can't Restore" follows (F6, with F5 as the cause), and exactly one violation is
        reported.
      - After the fix, F6 is **not found** by the default generated runs. The hand-written route exists and is
        reported correctly (open, edit, takeover, switch away, the taker closes, switch back), but it needs about 7
        events in a particular order, beyond depth 3, and 300 random walks of 30 events did not hit it. As a result
        `Generated_RediscoversKnownFindings` failed at the default budget until the replay list below was added; its
        assertion has not been weakened.
    - **Results:** normal suite 2698 passed + 49 skipped (the harness tests need `TERMPOINT_HARNESS=1`; one more
      is the skipped F16 test); harness 27 of 27 passed; no new violations. Findings by source: F1 search (84
      runs); F5 search (1) and replay (1); F6 replay only (1 run, 7 events); F7 search (5); F12 not found.
    - **Limitations:**
      - the exhaustive search's memo is an approximation: it leaves out the clock, service counters and exact bytes,
        so it can skip a path whose outcome depends on them;
      - one real-time wait of about 50 ms remains, in `WakeCheckAsync`, which waits for the fire-and-forget wake
        handler;
      - the shrinker is greedy: it drops events while the same violation remains, so the result is small but not
        always the smallest;
      - sleep cannot yet model a takeover that happens during the sleep;
      - F5 is reached only indirectly (through IT restoring an older copy), not through the no-edit save that causes it.
    - **Known-sequence replay list:** `KnownSequences.cs` holds named decision sequences (name, finding, one-line
      reason, event keys) that every generated exploration replays before the search, through the same replay path as
      a recorded run. Seeded with F6's route (open, edit, stale takeover, switch away, the taker closes, switch
      back). Findings found by a replay count toward `Generated_RediscoversKnownFindings`, and the report says for
      each known finding whether the search found it, a replay found it, or both, so the search is never credited
      with what only a replay reached. `KnownSequences_EachShowsItsFinding` fails when an entry stops reproducing its
      finding.
    - **F16 found:** the read-only-while-holding-the-lock outcome first seen in the dept.db to other.db story. A
      deterministic unit test (`CheckoutServiceTests.F16_...`, skipped until fixed) confirms the real route: a demotion
      already running (held in its D'' copy) when the user switches lands on the new session. The story itself is partly
      a harness artifact (the harness runs the lost-lock handler inline, production posts it); see F16 in section 3.
  - **9.3c-2:** split in two. 2a is the crash sweep (a power cut in the middle of an operation, below). 2b is a *pause*
    in the middle of an operation so that another event happens meanwhile: F2, F12 and F16 rediscovery, and the I6
    invariant.
  - **9.3c-2a:** the crash sweep. Test code only; nothing under `src/TermPoint/` changed. **Done 2026-10-20 (uncommitted):**
    - **The cut (`World.RunWithPowerCutAsync`, `PowerCut.cs`):** one operation of one computer runs with a cut armed for
      "the n-th time this computer reaches crash point P". At that instant the hook (1) marks the computer's process dead,
      (2) freezes the disk, which is a copy of the share folder, of that computer's local folder (the single-instance
      guard folder excepted, as in `CrashComputer`) and of the folder "Save a Copy" files go to, and (3) throws
      `PowerCutException` to stop the operation. When the operation returns (production may have caught the exception and
      carried on; whatever it threw is ignored), the harness waits for the dead process's own background work (the lock
      release, lost-lock handlers), abandons the computer's objects and puts the frozen disk back over the share and the
      local folder. Everything production did after the cut (cleanup, retries, a `finally` deleting a temp file) is thrown
      away, so what the next start sees is exactly the frozen state. The operation and the restore are ONE funnel step:
      the operation's own story lines are nested under it and the invariants run once, after the disk is back.
    - **Dead after the cut:** the process's `ProcessLife.PowerIsOut` stays true. Every later crash-point hit of that
      process throws again, every later network operation throws (`NetworkFaults` answers Throw, so nothing waits or times
      out), its dialogs are stopped before they reach the run's decision source (so a dead process's dialog is never
      recorded as a decision), and its banners, lost-lock handling and conflict flag are ignored. A restart is a new
      process with power.
    - **Which computer reached the hook:** an `AsyncLocal` set by `Computer.ActAsync` at the start of every action, so
      the value follows `Task.Run`, the thread pool, timers, new threads and awaits that resume elsewhere (a proof test
      checks all five outside any action, where the old "who is acting now" field has no answer). The old field is the
      fallback for code outside an action. A launch inside an action (`Open` starts the process first) moves the new
      process's record into the flow; without that the first sweep run attributed the restart to the dead process.
    - **The sweep (`CrashSweep.cs`):** 8 starting situations (Bob edited; edited and saved once, then edited again; Bob
      crashed with kept changes; Alice holds a stale lock; Bob lost the lock to Alice; Bob is reading while Alice edits;
      Bob's save was refused (conflict banner); Bob saved after a backup was taken) and 16 operations (Edit, Save, autosave
      tick, heartbeat tick, poll tick, Close, switch away, switch back, Open with the simplest answers (Restore / take
      over), Open answering Discard to the restore question, ask to take over as writer, Save a Copy and Discard Changes on
      the conflict banner, sleep 10 minutes (the wake check), Refresh of the read-only view, restore from a backup). Release
      and the lost-lock demotion are reached through Close, switch, Save and the wake check, not as events of their own. For
      each (situation, computer, operation): a DRY RUN records which points the computer reaches; then one CASE per point:
      replay the prefix, do the operation with the power cut at the point, restart the computer (open again with the
      default answers), every invariant through the funnel. A case is an ordinary decision list: its cut is the event
      `PowerCut(Save(bob) at Save.AfterRename)` (`#2` for the second time a point is reached), built from the keys by
      `EventCatalogue.ExtrasFor`, so replay, the existing shrinker and the story printer work unchanged. The story says
      "Bob's computer lost power at Save.AfterRename".
    - **New events used only by the sweep (not in the catalogue, so the search is unchanged):** Refresh and restore from
      a backup (`CrashEvents.cs`). The edit counts as applied when the cut is at `Dirty.AfterMarkerWritten`, because the
      row was inserted and the marker written before that point. A cut at `Restore.AfterRename` tells the I1 baseline that
      the restore landed, because the operation never returns to say so.
    - **Coverage:** **39 of 39 crash points cut** (not 45: `CrashPoints.All` has 39 names, and a test checks the catalogue
      against the source). Default sweep (width 1): 160 dry runs, 387 cases, 8 situations, 0 planned cuts that failed to
      fire, about 3.5 to 4 minutes. Every point has at least one case. The report has a per-point table (dry runs that reach
      it, cases that cut it, first case) and a list of points never cut with a reason for each
      (`CrashSweepCoverage.MissReasons`; empty now, and a test fails on a miss without a reason or a reason for a point that
      is now cut). The "Crash sweep" section of the report is identical in two full runs (the sweep is deterministic).
    - **Wider sweep:** `TERMPOINT_HARNESS_CRASH_WIDTH=2` adds 4 situations (Bob is on the other database; Bob lost the lock
      and was demoted; Bob crashed after saving once; Bob has kept changes and Alice came and went), cuts at the 2nd and 3rd
      time a point is reached, and a follow-up after the restart (Bob closes, time passes, Alice opens and closes): 834
      runs, about 8 minutes, still 39 of 39 and no new violation. The default full harness (`TERMPOINT_HARNESS=1`, 38 tests)
      takes about 12 minutes.
    - **Findings (sweep counts include its dry runs; "search" is the exhaustive search plus the random walks):**
      - **F8: found.** A cut at `Save.AfterRename`, then a restart that chooses Restore, ends in "Can't Restore" although D
        holds every kept edit. Sweep 13 runs; replay 1; the search cannot reach it (it has no cut). Smallest story, shrunk
        by the existing shrinker: 4 events (open, edit, save cut at `Save.AfterRename`, open again). It is on the
        known-sequence replay list. The classifier rule: when "Can't Restore" follows Restore and every edit the computer
        made that sits in its kept working file is already in D, it is F8, checked before F5 and F7.
      - F1: search 84, sweep 7. F5: search 1, replay 1 (sweep 0 at width 1; 36 at width 2). F6: replay 1, sweep 6 (the
        search still does not reach it). F7: search 5, sweep 35. F12: not found, as expected (needs 2b).
      - **New violations: none**, at width 1 and at width 2. In particular no cut at any of the 39 points left D torn (I3),
        lost a committed edit (I1), or left an applied edit nowhere that survives (I2) when the restart used the default
        answers.
      - F2 (an edit during a save loses its marker) is not found, as expected: it needs an edit *during* a save, which is 2b.
    - **Proof tests (`InvariantProofTests`):** a cut at each of the save's seven points leaves D either the old or the new
      version, never torn, with the edit kept (in D, or in the kept file with its marker, which only the last save step
      deletes); a `finally` block that deletes a file on the share and one on Bob's disk after the cut leaves both there
      after the restore, with the save's `dept.db.tmp` as the cut left it; the computer is dead after the cut (a second
      save by it changes nothing on the live disk and passes no crash point, a hit and a network operation throw) until it
      is restarted; the hook finds the computer on every kind of thread; the F8 route is classified F8 and the story names
      the point; the key of a power-cut event round-trips. Two sweep tests: a case replays to the same story, and a long case
      is shrunk to four events.
    - **Results:** normal suite 2699 passed + 59 skipped (before: 2698 + 49; one new plain test, ten new harness tests);
      harness 38 of 38 passed; the search is unchanged (757 exhaustive runs, 247 states, 300 random walks).
    - **Limitations:**
      - A cut can only happen at the 39 named boundaries. A real power cut can land between two, in the middle of a file
        copy or of a SQLite write, and leave a half-written file. The harness never leaves one: a copy is either not begun
        or finished. SQLite's own durability is assumed (as in 9.2b), and a loss of data the OS had not yet written to disk
        is out of scope.
      - The frozen disk is a file-level copy taken in the hook, not an atomic snapshot. One computer acts at a time, so
        nothing else writes meanwhile, except the lock file's delete, which production does on a background task
        (`Lock.AfterDeleted`). For that one point the other thread is held at its next crash point until the cut has fired
        and the disk is frozen; without that the frozen disk (and a replay) differed from run to run. Other fire-and-forget
        work is not tracked beyond the lock release and the lost-lock handlers.
      - A dead process keeps running ordinary local file code until its operation returns (only crash points, network
        operations and dialogs stop it). That is harmless because the frozen disk is put back afterwards, but it means the
        code after the cut ran, and could in principle hang; a hang is caught by the event timeout.
      - One computer is cut at a time and the other is idle during the cut, so a cut never interleaves with another
        person's action (2b). The restart is the same computer opening with the default answers (Restore, take over); the
        Discard branch is tried only as the *operation* "Open answering Discard", and the restart's other answers
        (the X, "Open read-only") are not swept.
      - The first time each point is reached is cut at width 1 (later times at width 2). The 8 situations are a small fixed
        set, so a point is cut in the states they reach (for example `Recovery.AfterSourceVerified` only in "Bob crashed
        with kept changes"); a state outside them is not swept.
      - Refresh waits about a second of real time (production's SMB-cache delay), so it is swept only where a reader exists.
  - **9.3c-2b (Done 2026-10-21, uncommitted):** pauses part-way through an operation, as laid out and approved on 2026-10-20 (one refinement, below).
    Test code only; nothing under `src/TermPoint/` changed.
    - **The pause (`World.RunWithPauseAsync`, `Pause.cs`, `PauseActions.cs`):** one operation of one computer runs with a pause armed for "the n-th time
      it reaches crash point P". The operation runs on its own task, marked with an `AsyncLocal`, so the mark follows every task and thread it starts.
      At that instant the thread that got there blocks on a gate, and every other thread of the operation stands still at its next crash point or network
      operation (`NetworkFaults.Decide`): the whole process is suspended, as when a laptop sleeps, not one thread of it. The event runs meanwhile on its
      own task, which carries no mark, so it is never held even when it is the same computer's. Then the operation is released and both finish.
      Pause, event and release are ONE funnel step: the operation's and the event's story lines are nested under it and the invariants run once, at the
      end. The story reads "Bob presses Save, and at Save.AfterLocalSnapshot Bob's operation is held while Alice takes over the lock, edits and saves —
      the event happened while the operation was held, then the operation went on".
    - **Real-time limits, and the refinement:** an event may legitimately wait for the held operation. `ReleaseAsync` calls `WaitForInFlightSaveAsync`,
      which polls for up to 60 s, so "Bob switches while his own save is held" waits for the save; an edit waits for the close's lock release in the
      same way. That is designed behaviour, not a violation. So: when the event has not finished after a **grace** of 2 s of real time, the operation is
      released and both are left to finish. If they do, the story says "(the event) waited for (the operation) to finish" and the case counts as
      *waited*, not interleaved. Only when something is STILL unfinished 30 s after the release, or the operation neither reached the point nor returned
      within 30 s, is it a new **Hang** violation (`ViolationKind.Hang`, invariant "Hang", classified "new", so it fails a generated run). The stuck work
      is abandoned the way the power cut abandons a dead process: the computer's `ProcessLife` is cut, so anything that ever moves is stopped at its next
      crash point or network operation, and the run goes on. The test run never hangs. The three limits are `PauseLimits`; the proof tests use shorter ones.
    - **I4 attribution:** `CheckWriterAtRename` now uses the computer whose flow reached the point (`World.ActingComputer`, the `AsyncLocal` from 2a),
      not `_currentActor`, which is wrong while two actions overlap. The funnel's depth counter is changed with `Interlocked`, and the pause puts "who
      is acting now" back when it ends (the two overlapping actions restore it in either order). A proof test leaves it pointing at the wrong computer on purpose.
    - **The pause as a decision:** `PauseEvent`, key `Pause(Save(bob) at Save.AfterTmpVerify; AliceTakesOverAndSaves)` (`#2` after the point for a later
      occurrence), built from keys by `EventCatalogue.ExtrasFor`. Replay, the shrinker and the story printer needed no change. What can happen meanwhile,
      written for "the paused computer" and "the other one": the other computer takes over (time for the lock to look stale, then it opens), takes over and saves
      (opens, edits, saves), IT restores an old copy, the lock file is deleted, time passes, **the paused computer edits**, **the paused computer switches**.
      An event is offered, and run, only if it is possible at the pause: the sweep's dry run installs a survey (`World.CrashPointSurvey`) that asks every
      event at every crash point, in that very state, and a pause whose event turns out not to be possible then is skipped with a story line.
    - **The pause sweep (`PauseSweep.cs`):** reuses the crash sweep's 8 starting situations (12 at width 2) and operations. A dry run records the points the
      operation reaches and which events are possible at each; then one case per (point, possible event): replay the prefix, run the operation paused with
      the event, then the paused computer crashes and starts again with the default answers (that follow-up is what makes F2 visible and judges every
      kept-work promise). **Width 1 (default) is a subset, chosen for time:** the operations Save, Close, switch away, the heartbeat, the wake check,
      Open and restore from a backup; only their `Save.*`, `Demote.*`, `Release.*`, `Restore.*`, `Checkout.*` and `Recovery.*` points, where the lock, the
      shared database and the kept work change hands; the first time each point is reached; each (operation, point, event) once, in the first situation
      where it is possible; a final save (Close, switch) only from its rename on, because before that it is the Save button's code; and the events that are
      known to wait for the held operation (a switch while the same computer's save is in flight) are left out, because each costs the whole grace period
      to show and a proof test pins them. Width 2 (`TERMPOINT_HARNESS_PAUSE_WIDTH=2`) lifts all of that and adds the "close normally" follow-up.
    - **Invariant I6, "nobody holds a lock they can't use"** (checked after every step, in the funnel): (a) a session whose checkout is read-only
      (`Mode == ReadOnly`; `IsReadOnlyMode` goes with it but production never clears it, so it is not read alone) must hold neither its lock service's
      writer flag nor its database's lock file; (b) a demotion applies only to the database it started on: the session's database is noted at the
      demotion's first `Demote.*` point (before a pause holds it there) and compared at every later one. Both fire in the F16 route; neither fires in the
      five hand-written walks (asserted) or in an ordinary demotion that nobody interrupts.
    - **Findings and the classifier:** F2 and F16 join `KnownFindings`. **F2:** an I2 "edit lost" whose edit its computer applied while its own operation was held in
      the *edit-loss window* (from a save's snapshot to the cleanup of the working copy: `Save.AfterLocalSnapshot` to `Save.AfterMarkerDelete`, then
      `Release.*`, `Close.AfterRelease`/`Close.AfterServicesDisposed`, `Switch.AfterRelease`/`Switch.AfterServicesReleased`); recorded in the world at the
      moment the row is inserted. The window is wider than the approved layout said because the first sweep found the clean-close face of the same finding
      (spec item 15, "edits made during the exit save are deleted"): a close held at `Save.AfterMarkerDelete` or `Release.AfterFinalSave` while Bob edits
      deletes the edit with the working copy, with no crash needed. **F12:** the I4 rule is unchanged; an **I1** violation in the same step whose lost edits
      another computer committed while a save was held before its rename is F12 too (the baseline learns what the event committed through
      `InvariantChecker.ObserveSharedMidStep`, otherwise an edit that is committed and overwritten inside one step is never seen). **F16:** any I6 violation.
      Three older proof tests that run the harness-inline form of the switch race (the lost-lock handler runs inline) now declare F16.
    - **Speed (`RandomWalkSharding.cs`):** the random walks run in `TERMPOINT_HARNESS_PROCESSES` separate test processes (default `min(4, processors/2)`,
      at least 1; this laptop has 4 logical processors, so 2). The parent starts `dotnet test <this assembly> --filter RandomWalkShardTests` per shard with a
      job file (the seeds, dealt round-robin) named by an environment variable; the shard test (skipped unless that variable is set) writes its tallies to
      JSON; the parent merges them in shard order into the same `GeneratedLog` the loop fills, so the report and `Generated_RediscoversKnownFindings` read
      as before. The parent keeps each child's `Process`, gives it a time limit (40 minutes, `TERMPOINT_HARNESS_SHARD_MINUTES`) and on a timeout or a
      failure kills only that process's tree (`Kill(entireProcessTree: true)`), never by image name, and fails with the child's output.
    - **Budget and timings (this laptop, 4 logical processors):** pause sweep at width 1: 8 starting situations, 70 dry runs (48 with the operation
      possible), **305 cases**: 277 interleaved, 28 waited, 0 not possible, 0 never reached, **0 hung**; 59 (point, event) combinations of the known-to-wait
      kind are left to width 2; about 3.5 minutes (3:22 to 3:29 in the full runs, about 560 ms per run). **Width 2 plans about 4590 cases, about three hours,
      and was sampled, not run in full:** a test runs every 40th planned case, at most 60 (50 interleaved, 10 waited, 0 hung, about 2.5 minutes), enough to
      show that the wide paths (later occurrences, the wide situations, the events that wait, the close-normally follow-up) execute. The full harness
      (`TERMPOINT_HARNESS=1`) was 38 tests in 13 m 26 s before (random walks 7:16, crash sweep 4:01 in that run, which overlapped two builds); it is now
      **67 tests in about 14 minutes** with the pause sweep (3:29) and the new proof and sharding tests in it (14 m 16 s for the last full run, 66 tests; the
      67th, the wide sample, was added after it and run on its own). The random walks went from 7:16 to **4:29 with 2 processes (1.6 times)**: each shard
      shrinks the breaches it meets by itself, so the shrink replays double (138 to 283), which is why two processes do not give two times. Without the split
      the same run would take about 17 minutes. Only the random walks are sharded; the crash sweep (2:52) and the pause sweep (3:29) are not.
    - **Findings by source** (full run; the counts are runs that showed it): **F2:** replay 1, pause sweep 28; the search cannot reach it. **F12:** replay 2,
      pause sweep 37; the search cannot reach it. **F16:** the search 2, replay 3, crash sweep 2, pause sweep 20 (the search and the crash sweep reach the
      harness-inline form of the race, which invariant I6 now flags; that is why three older proof tests declare F16). Also F7 once more, in the pause sweep
      (Alice takes over and saves while Bob's recovery is held at `Checkout.AfterLockAcquired`). Smallest stories, each on `KnownSequences` and each shown to
      be already as short as the shrinker can make it:
      - **F2, 3 events:** `Open(bob)`, `Pause(Save(bob) at Save.AfterLocalSnapshot; BobEdits)`, `Crash(bob)`. Bob's edit lands after the snapshot; the save's
        last step deletes the marker; the crash leaves "bob's working file WITHOUT its marker" and the next start deletes it.
      - **F12, 2 events:** `Open(bob)`, `Pause(Save(bob) at Save.AfterLocalSnapshot; AliceTakesOverAndSaves)`: I4 (the lock was Alice's at the rename) and I1
        (alice-1 left the shared database), both F12. With `AliceTakesOver` alone the story is I4 only (`Pause(Save(bob) at Save.AfterLockCheck; AliceTakesOver)`, 2 events).
      - **F16, 4 events:** `Open(bob)`, `AdvanceTime(4m)`, `Open(alice)`, `Pause(Sleep(bob,10m) at Demote.AfterWorkingCopyHandled; BobSwitches)`: both I6
        clauses fire. (The search's own 4-event form is `Open(bob)`, `AdvanceTime(4m)`, `Open(alice)`, `SwitchAway(bob)`, the harness-inline variant.)
    - **New violations: none** at width 1, in the full harness. Two things the work found on the way are worth a decision:
      - *The two ways F12 can land.* Both writers use the same temp file name (`<database>.tmp`). When Alice takes over and saves while Bob's save is held
        AFTER his D.tmp was verified, her save takes his D.tmp, so his rename finds nothing and fails (a copy error; nothing is overwritten, and I4 still
        reports that the lock was hers). Only when Bob is held BEFORE his copy to D.tmp (`Save.AfterLockCheck` to `Save.AfterLocalSnapshot`) does his rename land
        over her save. The F12 fix (re-check the lock and the fingerprint just before the rename) closes both.
      - *A candidate finding at width 2, not yet in section 3:* Bob has an edit, a 1.2.2 colleague changes the database, Bob closes (the final save is refused
        and the close keeps his working copy), and a switch overlaps the close (`Pause(Close(bob) at Close.AfterRelease; BobSwitches)`, 4 events). The switch
        starts from a session the close has already released, asks `ReleaseAsync` for no save, is told "Success", and `SessionLifecycle.SwitchCoreAsync` deletes
        the working copy it believes is in the database (the trap its own comment describes for the save-conflict path); bob-1 is nowhere. It needs a switch
        to be possible while the window closes (spec item 15 is about input during the exit save), so it may be only a harness reach, but it is data loss by
        rule 3, so it is reported: the wide-sample test names it and fails on any other unexplained breach. **Not pursued (user,
        2026-10-21):** too unlikely in practice to need a finding of its own; it stays out of section 3.
    - **Proof tests** (`PauseProofTests`, `RandomWalkShardingTests`, `InvariantProofTests`, `HarnessWalkTests`; the harness ones need `TERMPOINT_HARNESS=1`):
      the pause holds the operation (D.tmp verified, D unchanged, the save not returned) and releases it; a pause case replays to the same story; the shrinker
      shortens a long pause case (to 3 events); the pause key round-trips (a plain test, in the normal suite); "Bob switches during his own save" waits as
      designed and is not a violation; a planted deadlock, an operation that never returns, an event that never ends and an operation that is stuck before the
      point are each a Hang within the limits and the test run does not hang; a point the operation never reaches just lets it happen; the rename check is
      attributed to the right computer when "who is acting now" is wrong; F2 (the crash route and both clean-close routes), the F2 negative (an edit before the
      snapshot loses nothing), F12 (I4 and I1 both classified, nothing "new", and the shared-D.tmp case with no loss) and F16 classify as above; I6 (a) and (b)
      each fire on a planted state, both fire on the F16 route, and neither fires on an ordinary demotion or in the five hand-written walks (now asserted);
      the sharded walks give the same tallies as the in-process loop (24 seeds, 2 processes); a failing shard fails the test with its output; a shard past its
      time limit is killed and its process is gone.
    - **Results:** normal suite 2721 passed + 66 skipped (65 harness tests skipped, plus the skipped F16 unit test; this machine also runs the tests that
      need the private database); harness 66 passed + 1 skipped (the shard test, which only a parent run starts) in the last full run, and the wide sample
      and the pause tests passed again after the final change; no new violations at width 1.
    - **Limitations:**
      - A pause stops a process only at crash points and network operations; local file and SQLite work between two of them still runs, so "the process is
        suspended" is exact only to that granularity (as the power cut is exact only to the 39 named boundaries).
      - One event per pause. A longer interleaving (two events, or an event that is itself paused) is not swept.
      - The interleaved/waited split rests on the 2 s grace: an event that normally takes much less is always interleaved, but one that sits close to 2 s on a
        loaded machine could be counted as waited. That changes the label and the order of the story's lines, never a verdict.
      - The default sweep pauses each (operation, point, event) once, in the first situation where it is possible, at the first time the point is reached; a
        state outside the 8 situations, and every case left to width 2, is not swept by default.
      - The harness has no UI thread: a held operation runs on a pool thread and a person's click is an event on another, so what production posts to the UI
        thread (the lost-lock handler) runs inline here, as before (the note under F16).
      - Child processes need the `dotnet` executable and the test assembly's folder on disk.
    - **Seen on the way, not changed:** `CheckoutService.IsReadOnlyMode` is set when D'' is made but cleared only by `ResetAfterFailedOpenAsync`, so after a
      read-only session a later write session still reads `true`; `MainWindowViewModel.RefreshData` branches on it alone, and `RefreshReadOnlySnapshotAsync`
      throws when the mode is not read-only, which the refresh logs as an error. Not checked end to end. I6 therefore reads `Mode`, not the flag.
    - **Against the four rules:** no production change. It closes the harness's gaps on rule 2 (changes by others part-way through: F12), rule 3 (edits
      during a save: F2) and rule 4 (F16, through I6).
      a little more, because a pause is harder to make deterministic than a cut.
- **9.3d:** the first full run against today's code, and a report: every failure found, matched to a finding or
  added to section 3 as a new one.

### 9.4 The new assessment
Once 9.1–9.3 exist, section 2 of this document is redone against the design: each row becomes a harness scenario
(and a hand test where the UI matters), and any sequence the harness finds that no row covers becomes a new row. Then
the inventory in section 4 (backups, imports, Copy/Empty Semester, unreported failures) gets the same field-by-field
treatment.

### 9.5 Order
1. ~~Review and settle this note (8.7).~~ Done 2026-10-18.
2. 9.1, the refactor.
3. 9.2 and 9.3 against **today's** code. Known problems (F1, F2, F5, F8 and so on) show up as expected failures,
   which proves the harness can see them.
4. Steps 8.9.1–4, with the harness turning green.
5. 9.4, the new assessment.
