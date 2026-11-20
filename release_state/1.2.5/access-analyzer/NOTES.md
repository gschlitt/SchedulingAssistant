# Access Analyzer and easy access watches: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design: [designs/1.2.5/access-analyzer.md](../../../designs/1.2.5/access-analyzer.md).

<details>
<summary>Files to read first</summary>

- `designs/1.2.5/access-analyzer.md`: the spec. Status block at the top; Terms (D2); decisions D1-D26; Phase 2 rules 1-41; Phase 3 wireframe rounds table.
- `designs/1.2.5/access-analyzer-handoff.md`: older handoff (written 2026-11-10). Its branch and release notes are out of date; the ledger replaces it for state. Its summary of the method and working agreements still holds.
- `designs/wireframes/`: the clickable HTML wireframes (rounds 1-6 settled; round 7 starts as a copy of r6). Each round starts as a copy of the last and keeps its own file as the record. Reviewing them in Claude's browser pane: see `docs/dev/dev-machines.md`.
- `designs/1.2.5/slot-browser.md`: related; its D3 level 2 is judged by whole slates.
- Memory note `project_access_analyzer` (Greg's own working notes).

</details>

<details>
<summary>Decisions and why</summary>

- Method: Progressive Design Spec, six phases, each locked by Greg before the next. Phase 3 is settled one screen per round with a clickable HTML wireframe, not signed off as a batch.
- D17 (agreed): the Access panel is a tab beside Workload; each tab detaches on its own.
- D25 (agreed): an access watch is a filter preset you choose to watch. D26 (agreed): a custom tag can be watched directly.
- Round 5 (2026-11-18): Greg approved the wireframes' wording for coding but expects to fine-tune it once the feature runs; don't treat it as final at the Phase 3 lock.
- Round 6 (2026-11-18): Greg chose conflict marks ("teal boxes") plus delivery type, the first piece to ship. Review
  the same day: costly-only marks by default, plus a **Mark all conflicts** box (off by default) that marks every conflict
  as today, because removing one may still help and the user may know things about two sections that TermPoint
  can't (built as 6b); tag watches same as presets; red stays; Delivery Type beside Campus; the reworded Section
  Type and Meeting Type descriptions work. What's new: the upgrade banner, kept, pointing to the box.
- 6c (Greg, 2026-11-19): a watch covers exactly the sections its filter shows (cohort tags on chosen sections), so
  the Courses list must say which sections each course offers the slates; and a watch of one section per part should
  say "fit together" / "X conflicts with Y", not "1 slate" or a best partial. Built as 6c.
- 6d (Greg, 2026-11-20): a missing part counts as no full slate.
- 6e (Greg, 2026-11-20): a Multi-campus box per watch, off by default, quiet, under the course list, shown only when
  the watch spans campuses. Tried first: mixed slates always allowed and flagged, but that was noisy and counted slates
  most users don't want, and watch setup should be as easy as picking four courses. Off keeps 6d's one-campus rule
  (stranding names the box); ticked counts mixed slates quietly and flags them when every slate mixes. A stored field
  on the watch. Greg agreed the box as built (2026-11-20).
- Round 6 closed (Greg, 2026-11-20): 6b-6e approved, including the section phrases, the one-section wording, the
  mixed-campus notice and the Lost notice wordings; Mark all conflicts got Greg's own hover. Like round 5's, the
  wording may be fine-tuned once the feature runs.
- Wording (Greg, 2026-11-19): "conflicts", never "clashes"; unconfirmed parts are "inferred", never "guessed". In
  user-facing text, docs and code.
- Greg (2026-11-16): add the feature to the 1.2.5 ledger. Earlier it was "release undecided, probably not 1.2.4".

</details>

<details>
<summary>Dead ends</summary>

- Separate course-list and tag watches (Q35, Q36): superseded by D25.

</details>

<details>
<summary>Technical issues</summary>

- Phase 5 risk: `DetachablePanel` may not support tabs today (fallback: a third panel with a splitter).
- The "teal boxes" are red in the app: `ScheduleGridView.axaml.cs` gives each conflicting entry row a radial glow
  (`ProgramConflictGlowCenter`/`Edge`) and joins cross-tile pairs with a 2 px `ProgramConflictBrush` line; no hover
  today. Pairs come from `ProgramConflictService.DetectConflicts` (any overlap between two courses of a watch).
- Today's Section Type description in `SchedulingEnvironmentViewModel` gives "Online" as an example, and Meeting Type
  says "In-Person, Hybrid, Remote": both overlap delivery type (r6 rewords them).
- Data risk: parts on courses, a delivery type field on sections and watches stored as presets will change what is saved; the `MinimumVersionToWrite` question is almost certainly Yes for the stored pieces. Persisted classes must derive from `PersistedRecord`.
- Real-data checks: UFV BIOL and UBC databases (dev machine only, read from a copy) model labs differently; do not treat one as representative.

</details>
