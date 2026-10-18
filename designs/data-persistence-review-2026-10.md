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
| **F2** | An edit made during a save loses its marker; a crash before the next save deletes it | 3 | Known (item 15) | Medium: edit counter |
| **F3** | Unapplied inline edits dropped at close or switch | 3 | Known (item 14) | Medium |
| **F4** | After a lost lock, writes still reach the read-only copy (connection is read-write; only the UI blocks), so an editor left open could "save" into a file that is thrown away | 3/4 | **New**, needs one check (does the editor stay open across demotion?) | Small: open read-only copies with `Mode=ReadOnly` so any write fails loudly |
| **F5** | Every editing session rewrites the shared database at its first save, even with no edits. So *anyone* opening it for editing after Bob's crash or lost lock makes his changes unrestorable, and the "restore if nobody changed anything" path almost never happens | 4 (and makes C harder) | **New** | Small to medium: skip a save when nothing changed (check what the version-stamp raise relies on first) |
| **F6** | Four messages promise a restore that may not be possible: the lock-lost banner, "Unsaved Changes Kept", and both "Changes Not Saved" messages | 4 | Partly found in 10c | Small: one honest wording, in a prompt class |
| **F7** | "Restore?" is asked before checking, then "impossible" | 4 | Found (10c) | Small/medium (10c's early check) |
| **F8** | A crash in the milliseconds between rename and marker delete leads to "can't restore" though the changes are already in the database | 4 | **New** | Small: always record the save's fingerprint as pending in the marker *before* the rename |
| **F9** | Kept files live in roaming `AppData`: they can roam back older, or sit on a server under folder redirection | 3/4 | **New** | Medium: move to `%LocalAppData%`, with a one-time migration that still finds 1.2.2's kept files |
| **F10** | Observer mode never mentions kept changes | 4 | **New** | Small |
| **F11** | 10b's switch wording on "Take over as writer" for the same database | 4 | **Moot**: 10b shelved 2026-10-18 | — |
| **F12** | The save doesn't re-check the lock just before the rename | 2 | **New**, theoretical | Small: one check before step 5, as the restore already does |
| **F13** | A damaged lock file is reported as "removed"; jargon | 4 | Known | Small |
| **F14** | Kept changes on another computer are invisible from this one | 4 | **New** | Hard; probably a message only |
| **F15** | Messages during a network outage: the autosave banner clears itself after 30 s while saves keep failing (nothing on screen until the next autosave, 10 min later); "Try saving again in a moment" is wrong advice in a long outage; the heartbeat adds a second message for the same problem, worded "safe locally"; closing then gets "Changes Not Saved" with its restore promise (F6) | 4 | **New** | Small/medium: one persistent "can't reach the database" state that clears only when a save works, one wording |

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

Each sub-step is implemented by Sonnet from a brief; Opus reviews the diff (moved-lines view) and re-runs the
compile check and the full suite independently. `MainWindow.axaml` itself is not touched.

### 9.2 Seams for faults
- **Network:** a switch in `NetworkFileOps` making a chosen operation, or all of them, time out or fail.
- **Crash at any step:** named points in the save, the move, the open and the close where the harness can stop
  everything, then start a fresh `CheckoutService` on the same folders: the same as the process being killed there.
- **Other actors:** Alice is another `CheckoutService` with her own local folder, against the same share folder.

### 9.3 The harness
- **World:** one temp "share" folder; one local folder per computer (Bob's laptop, Bob's desktop, Alice's).
- **Events:** Bob edits, saves, autosaves, closes, switches, reopens, crashes (at each named point), loses the
  network and gets it back, sleeps; Alice opens, edits, saves, closes, takes over a stale lock; the lock file is
  deleted; IT restores an old backup.
- **Every event sequence up to a fixed length** is run (bounded exhaustive), plus long random sequences from fixed
  seeds. A failing sequence is shortened automatically and printed as an Alice-and-Bob story.
- **After every event, the invariants are checked:**
  - **I1, lineage:** the shared database is always exactly a version some editor saved, each based on the one before
    (tracked by a test-only record inside the database). Never a mix, never Bob's older version on top of Alice's.
  - **I2, nothing lost:** every edit Bob applied is in the shared database, in the slot with a marker, or in a
    recovered version.
  - **I3, one writer:** no two sessions ever both rename onto the database from the same base.
  - **I4, nothing deleted without a copy:** the slot is removed only when its content is in the database or in a
    checked recovered version.
  - **I5, honest messages:** every message is logged with what it promised ("will be added", "kept"), and the
    harness checks the promise came true.
- It runs as its own test category (minutes, not seconds), on demand and before each release; the normal suite
  stays fast.

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
