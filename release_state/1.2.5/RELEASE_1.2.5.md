# TermPoint 1.2.5: release ledger

**Goal:** to be set by Greg (the unassigned candidate work is in [backlog-1.2.5.md](backlog-1.2.5.md)).
**MinimumVersionToWrite:** not asked yet.
**Updated:** 2026-11-16

## Work

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

**Goal:** administrators see at a glance whether students can still take every course of a watched set (a full "slate" of one section per part), get notices when a change breaks it, and create watches easily. Done means the feature in the app, field-tested.

**State:** Design in progress (progressive spec). Phases 1 and 2 locked; Phase 3 (UX) in wireframe rounds 1-3 settled, round 4 built and waiting for Greg's review. Phases 4-6 and all code still to come. Greg added it to 1.2.5 on 2026-11-16.

<details>
<summary>Steps (7 left)</summary>

1. Greg reviews wireframe round 4 (presets and Watch at the filter bar, watching a tag, adding a watch by picking or pasting courses).
2. Further wireframe rounds until the rest of Phase 3 is settled; Greg locks Phase 3.
3. Phase 4: data design (ask Greg which readable columns any new table needs; check against `MinimumVersionToWrite`).
4. Phase 5: architecture (including whether `DetachablePanel` can do tabs).
5. Phase 6: implementation plan, in the delivery order (delivery type first, easy watches, then the analyzer).
6. Build in pieces, each with tests and compile checks.
7. Full suite, field test, answer the `MinimumVersionToWrite` question.

</details>

**Open questions:**
- Round 4 review (what the entry waits for).
- Phase 3 questions Q29-Q34, and the left-open Phase 2 items (Q18 extra part names, Q28).
- Does the whole feature fit 1.2.5, or does only the first piece (delivery type, easy watches) ship now?

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

**Open questions:** none. (Undo: backlogged by Greg, 2026-11-16.)

Notes: [click-drag-meetings/NOTES.md](click-drag-meetings/NOTES.md). Design: [designs/1.2.5/click-drag-meetings.md](../../designs/1.2.5/click-drag-meetings.md).

</details>

<details>
<summary><b>Section details in Schedule View's right-click menu</b> · Not started · **ACTIVE**</summary>

**Goal:** right-clicking a section line in Schedule View shows its instructors' full names (always, even for one instructor), its section type and its tags. The tile keeps its shortened initials. Done means it is in the app and hand-checked by Greg.

**State:** Not started. Today only a hover shows full names, and only when the initials were shortened to "+N". Greg's request, 2026-11-16; no design doc (small enhancement).

<details>
<summary>Steps (4 left)</summary>

1. Read the right-click menu and the hover tooltip code; sketch the details block (names, section type, tags) as a text mock-up.
2. Greg approves the layout and wording, and settles the open questions.
3. Build it: the menu's view model supplies the details, the menu shows them above the edit items; unit tests.
4. Both compile checks, the grid tests and full suite, Greg's hand check; answer the `MinimumVersionToWrite` question (likely No: nothing saved).

</details>

**Open questions:**
- Keep the hover tooltip for shortened initials too, or does the right-click replace it?
- Do read-only users see the details? (They already get the menu, for marks.)
- Setting filters from the right-click (Greg's first idea): skipped for now; later in this entry, or its own entry?

Notes: [grid-right-click-details/NOTES.md](grid-right-click-details/NOTES.md)

</details>

## Closed

<details>
<summary><b>Avalonia bug post: section list "jump" under virtualization</b> · Done</summary>

**Goal:** a well-written, reproducible Avalonia bug post (a tiny stand-alone repro plus the text), ready for Greg to file.

**Outcome:** 2026-11-16 (ce663d6): stand-alone repro in the tools folder, bug confirmed on 12.1.6 and on main's nightly; a short comment for the matching open Avalonia issue is ready for Greg to post. Retiring the workaround once Avalonia fixes it is a backlog line. Notes: [avalonia-list-jump-bug/NOTES.md](avalonia-list-jump-bug/NOTES.md).

</details>
