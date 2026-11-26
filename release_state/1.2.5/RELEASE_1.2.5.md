# TermPoint 1.2.5: release ledger

**Goal:** to be set by Greg (the unassigned candidate work is in [backlog-1.2.5.md](backlog-1.2.5.md)).
**MinimumVersionToWrite:** not asked yet.
**Updated:** 2026-11-26

## Work

<details>
<summary><b>Undo and rollback for section changes</b> · Not started · **ACTIVE**</summary>

**Goal:** an administrator can experiment with sections and get back: step back through recent section changes (drags, right-click edits, card Applies, creates, deletes, copies), or return in one go to an earlier point, such as before they started dragging. Works after a save, until the app closes. Done means the feature in the app, field-tested.

**State:** Not started. Greg's answers (2026-11-26) are in the notes: undo and rollback, all section changes, undoable after a save until the app closes.

<details>
<summary>Steps (6 left)</summary>

1. Probe the TEMP-trigger change log on the working copy's connection: it survives a save, never reaches the shared file, and is gone after a lost lock or a database switch.
2. List every section write path (drag, right-click edits, card Apply, create, delete, copy) and what reloads after each.
3. Write the design doc as Alice-and-Bob stories (undo, rollback points, what clears the history, refusals, the controls); check it against the four rules; answer `MinimumVersionToWrite`.
4. Greg approves the design and all wording.
5. Build (Opus briefs, Sonnet codes) with unit tests, including a re-inserted section keeping its meetings and extension data.
6. Both compile checks, full suite and harness; Greg's hand check; field test alongside the drag's field test.

</details>

**Open questions:**
- Rollback points: set automatically (database opened, before a run of drags), marked by the user, or both?
- Where the controls go: toolbar buttons, a menu, Ctrl+Z outside text boxes?
- Does the history span semesters (undo a change made in a semester not on screen)?
- How many steps to keep?

Notes: [undo/NOTES.md](undo/NOTES.md). Design: `designs/1.2.5/undo.md` (not written yet).

</details>

<details>
<summary><b>Late lock acquire holds a lock nobody uses</b> · Not started</summary>

**Goal:** a lock acquire that the app gave up on (5 s deadline) can never leave this computer holding a live lock it doesn't know about. Found by the 2026-11-14 hang/deadlock audit; backlog item N2.

**State:** Not started. The audit says the code allows it (high confidence); a real share doing it needs a stall over 5 s that then recovers (medium). Pinned as today by a harness test.

<details>
<summary>Steps (5 left)</summary>

1. Reproduce on the NetworkProbe rig: an ~8 s stall on the lock-file create; look for "Acquired write lock" after "lock acquire timed out".
2. Write the design as an Alice-and-Bob story, checked against the four rules.
3. Greg approves the design, including any user-facing wording.
4. Implement (a per-attempt token; a given-up attempt releases what it created; no concurrent second acquire), then flip the pinned test.
5. Run both compile checks, the full suite and the harness; field test; answer the `MinimumVersionToWrite` question.

</details>

**Open questions:**
- Fix as the backlog suggests (per-attempt token), or something simpler? Decide in the design.
- Ship in 1.2.5, or defer because it touches the save path?

Notes: [late-lock-acquire/NOTES.md](late-lock-acquire/NOTES.md)

</details>

<details>
<summary><b>Notices lost behind licence banners (WN1 and related)</b> · Not started</summary>

**Goal:** no notice can be lost for good, or hidden indefinitely, because a licence or trial banner is occupying the one banner slot. The "What's new" banner must be shown (or dismissed) before it counts as seen, and an undismissable licence notice must not block errors and warnings behind it.

**State:** Not started. WN1 was seen on a Store flight (2026-11-13): the announcements are marked seen when queued, so a notice ahead of them can hide them for good. Related danger, from reading the code (not yet reproduced): the expired and trial-ended notices cannot be dismissed, so they may hold the slot and queue every later normal notice behind them.

<details>
<summary>Steps (5 left)</summary>

1. Reproduce both: a trial countdown ahead of the announcement (WN1), and an undismissable notice ahead of an error or warning.
2. Decide the behaviour: what outranks what, and whether a licence notice can share the banner or sit in its own place. Wording for Greg if any text changes.
3. Fix WN1: advance `LastAcknowledgedVersion` only once an announcement has been displayed or dismissed.
4. Fix the slot problem as decided; unit tests for the queue order and for the undismissable case.
5. Compile checks, full suite, hand check on a trial, an expired and a licensed database; answer the `MinimumVersionToWrite` question.

</details>

**Open questions:**
- Should errors and warnings ever jump ahead of an undismissable licence notice, or should the licence notice move out of the banner?
- Do the trial countdown and expiring-licence notices dismiss, or only expired and trial-ended? (Read the code first.)

Notes: [lost-notices/NOTES.md](lost-notices/NOTES.md). Source: backlog WN1, plus Greg's concern about the trial advisory banner.

</details>

<details>
<summary><b>Access Analyzer and easy access watches</b> · Waiting on Greg</summary>

**Goal:** administrators see at a glance whether students can still take every course of a watched set (a full "slate" of one section per component), get notices when a change breaks it, and create watches easily. Done means the feature in the app, field-tested.

**State:** Set aside 2026-11-26 for Undo; waits on Greg's fewest-moves trial verdict and the DRAFT wording (step 1).
On branch 1.2.5, pieces A–F committed, F leftovers too except health-line styling. Greg's hand check of F is part-done.

<details>
<summary>Steps (5 left)</summary>

1. Greg tries the fewest-moves marks and "Don't suggest moving this" on UBC; approve or change the DRAFT wording (notes: "No full slate: what to mark").
2. Settle what the trial leaves open (Access tab line, Mark all conflicts, the spec's rule 33), then finish Greg's hand check of F, including Escape and Open in Courses.
3. F leftover: health-line styling (the mock's warn colour and hovers per phrase), after step 2.
4. G: editor pointer and drag line.
5. H: release (version stamp, WASM demo, help page or video for the "What's an access watch?" link, field test).

</details>

**Open questions:**
- Fewest-moves trial: does it read well on UBC? Keep "Don't suggest moving this"? The DRAFT hover and menu wording.
- The help text drafted in chat for the no-full-slate marks: right length and tone for the help page?
- F's draft wording (in the notes): notice causes, Check all courses' read-only comment, the legend's hidden-sections line, the missing-component hover, three failure lines.
- The uncommitted wizard example wording in the setup text: Greg's to keep? (left out of the commits)
- Round 6d's day widths while a watch is open: build in F, or leave out?
- The left-open Phase 2 items (Q18 extra component names, Q28).
- The too-big-to-check line for a watch over 30 courses (Q53): wording to draft for Greg in F.
- Should the wizard's question 2 start with every section type ticked (his new Section Type text says most are components)?
- A database with converted watches but no setup hides Add a watch and Mark all conflicts: is that right?
- After setup with no watches the Access tab is blank: does it need a line?

Notes: [access-analyzer/NOTES.md](access-analyzer/NOTES.md). Design: [designs/1.2.5/access-analyzer.md](../../designs/1.2.5/access-analyzer.md).

</details>

<details>
<summary><b>Click-and-drag moving of meetings in Schedule View</b> · In progress</summary>

**Goal:** on a busy schedule, an administrator can rearrange existing meetings by mouse in Schedule View, in the way people expect from other click-and-drag tools, smoothly and reliably. Sections are still created and fully edited in the Section List; the grid only moves meetings (the common quick edits, like today's right-click ones). Done means the feature is in the app and field-tested.

**State:** On branch 1.2.5: the whole feature is built and committed (drag, Alt group drag, marks, auto-scroll), hand-checked by Greg on a busy real schedule; full suite passes. Field test planned (notes) but postponed by Greg on 2026-11-16 while other work goes first.

<details>
<summary>Steps (1 left)</summary>

1. Field test (plan in the notes: drops around saves, readers, takeover, lock loss; detached view; macOS if handy); Greg confirms the `MinimumVersionToWrite` answer (proposed No: same fields as the section editor).

</details>

**Open questions:** none. (Undo: its own entry since 2026-11-26.)

Notes: [click-drag-meetings/NOTES.md](click-drag-meetings/NOTES.md). Design: [designs/1.2.5/click-drag-meetings.md](../../designs/1.2.5/click-drag-meetings.md).

</details>

<details>
<summary><b>Section details in Schedule View's right-click menu</b> · Done</summary>

**Goal:** right-clicking a section line in Schedule View shows its instructors' full names (always, even for one instructor), its section type and its tags. The tile keeps its shortened initials. Done means it is in the app and hand-checked by Greg.

**State:** Done 2026-11-16: built to Greg's approved mock-up, full suite passes, Greg's hand check passed. Also fixed: the menu jumped when its instructor list scrolled (bug since 1.2.4). `MinimumVersionToWrite`: No (nothing saved).

<details>
<summary>Steps (0 left)</summary>

None.

</details>

**Open questions:**
- Setting filters from the right-click (Greg's first idea): skipped for now; later in this entry, or its own entry?

Notes: [grid-right-click-details/NOTES.md](grid-right-click-details/NOTES.md)

</details>

## Closed

<details>
<summary><b>Mark sections that carry more than one of the shown tags</b> · Done</summary>

**Goal:** when the Tags filter shows "any of the checked tags" with two or more ticked, a Schedule View section line carrying two or more of them gets a mark at its right edge, with a hover naming the tags; the line's instructor-names hover goes.

**Outcome:** 2026-11-17 (8478980 and the closing commit): two upright orange tags with the approved hover, plus a "Clear all tags" button in the Tags popup. Full suite passes, Greg's hand check passed. `MinimumVersionToWrite`: No (nothing saved). Section View cards were left without the mark (later, if at all). Notes: [multi-tag-mark/NOTES.md](multi-tag-mark/NOTES.md).

</details>

<details>
<summary><b>Avalonia bug post: section list "jump" under virtualization</b> · Done</summary>

**Goal:** a well-written, reproducible Avalonia bug post (a tiny stand-alone repro plus the text), ready for Greg to file.

**Outcome:** 2026-11-16 (ce663d6): stand-alone repro in the tools folder, bug confirmed on 12.1.6 and on main's nightly; a short comment for the matching open Avalonia issue is ready for Greg to post. Retiring the workaround once Avalonia fixes it is a backlog line. Notes: [avalonia-list-jump-bug/NOTES.md](avalonia-list-jump-bug/NOTES.md).

</details>
