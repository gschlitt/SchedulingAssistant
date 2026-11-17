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
<summary><b>Click-and-drag moving of meetings in Schedule View</b> · In progress · **ACTIVE**</summary>

**Goal:** on a busy schedule, an administrator can rearrange existing meetings by mouse in Schedule View, in the way people expect from other click-and-drag tools, smoothly and reliably. Sections are still created and fully edited in the Section List; the grid only moves meetings (the common quick edits, like today's right-click ones). Done means the feature is in the app and field-tested.

**State:** Everything built so far is hand-checked by Greg. Then, at his request, Alt was inverted: a drag moves one meeting, Alt brings the same-start ones along. The group drag, the inversion and the CLAUDE.md amendment are not yet committed. Next: Greg's quick check of the inverted Alt.

<details>
<summary>Steps (3 left)</summary>

1. Greg checks the inverted Alt (one ghost without Alt; with Alt, every same-start meeting).
2. Compile checks (desktop and browser), full suite; hand check on a busy real schedule, with folded days and several semesters shown.
3. Field test; answer the `MinimumVersionToWrite` question (expected No: same fields as the section editor).

</details>

**Open questions:**
- Undo for grid edits (last 5, in memory): add it to this entry after the group drag, or as a 1.2.6 backlog line? The design says no undo; costs are in the notes.

Notes: [click-drag-meetings/NOTES.md](click-drag-meetings/NOTES.md). Design: [designs/1.2.5/click-drag-meetings.md](../../designs/1.2.5/click-drag-meetings.md).

</details>

## Closed

<details>
<summary><b>Avalonia bug post: section list "jump" under virtualization</b> · Done</summary>

**Goal:** a well-written, reproducible Avalonia bug post (a tiny stand-alone repro plus the text), ready for Greg to file.

**Outcome:** 2026-11-16 (ce663d6): stand-alone repro in the tools folder, bug confirmed on 12.1.6 and on main's nightly; a short comment for the matching open Avalonia issue is ready for Greg to post. Retiring the workaround once Avalonia fixes it is a backlog line. Notes: [avalonia-list-jump-bug/NOTES.md](avalonia-list-jump-bug/NOTES.md).

</details>
