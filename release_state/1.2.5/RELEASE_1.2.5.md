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
<summary><b>Avalonia bug post: section list "jump" under virtualization</b> · Not started</summary>

**Goal:** a well-written, reproducible Avalonia bug post (a tiny stand-alone repro plus the issue text), ready for Greg to file. Filing and any later switch to a virtualized section list are separate; this entry ends when the post is ready.

**State:** Not started. Virtualizing the section list cut an 800-section heap from 475 MB to 160 MB but is blocked by a scroll jump when an on-screen item grows (the inline editor expanding). Greg wants the repro so TermPoint can manage memory better and with less fragility than scroll anchoring.

<details>
<summary>Steps (5 left)</summary>

1. Read the scroll-anchor probe and the section list's anchoring code to see what the jump needs (item growing in view, virtualizing panel).
2. Build a minimal Avalonia app (no TermPoint code) that shows the jump with a virtualized list and an item that grows; pin the Avalonia version.
3. Check it against the latest Avalonia release and search existing issues for a duplicate.
4. Write the issue: steps, expected vs actual, versions, screen recording or numbers.
5. Greg reviews and files it; link the issue back here and in the notes.

</details>

**Open questions:**
- Where should the repro live (a `tools/` folder, or its own public repo for the issue)?
- After the bug is filed, does virtualizing the section list wait on a fix, or on a workaround? (A separate entry if so.)

Notes: [avalonia-list-jump-bug/NOTES.md](avalonia-list-jump-bug/NOTES.md). Source: former backlog item 9.8.

</details>

## Closed

_None._
