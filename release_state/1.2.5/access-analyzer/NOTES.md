# Access Analyzer and easy access watches: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design: [designs/1.2.5/access-analyzer.md](../../../designs/1.2.5/access-analyzer.md).

<details>
<summary>Files to read first</summary>

- `designs/1.2.5/access-analyzer.md`: the spec. Status block at the top; Terms (D2); decisions D1-D26; Phase 2 rules 1-41; Phase 3 wireframe rounds table.
- `designs/1.2.5/access-analyzer-handoff.md`: older handoff (written 2026-11-10). Its branch and release notes are out of date; the ledger replaces it for state. Its summary of the method and working agreements still holds.
- `designs/wireframes/`: the clickable HTML wireframes (rounds 1-7 settled; r7 is the editor pointer; r8, components in Courses, revised in place through 8d, awaiting review). Each round starts as a copy of the last and keeps its own file as the record. Reviewing them in Claude's browser pane: see `docs/dev/dev-machines.md` (to load an edited file afresh, close the tab and open the `file:///` URL again; reload and a same-tab navigate can keep the old page and its test clicks). `window.__access` exposes the mock's engine (`proposal`, `accessChanges`, `analysesFor`) for checking scenarios from the console.
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
  the Courses list must say which sections each course offers the slates; and a watch of one section per component should
  say "fit together" / "X conflicts with Y", not "1 slate" or a best partial. Built as 6c.
- 6d (Greg, 2026-11-20): a missing component counts as no full slate.
- 6e (Greg, 2026-11-20): a Multi-campus box per watch, off by default, quiet, under the course list, shown only when
  the watch spans campuses. Tried first: mixed slates always allowed and flagged, but that was noisy and counted slates
  most users don't want, and watch setup should be as easy as picking four courses. Off keeps 6d's one-campus rule
  (stranding names the box); ticked counts mixed slates quietly and flags them when every slate mixes. A stored field
  on the watch. Greg agreed the box as built (2026-11-20).
- Round 6 closed (Greg, 2026-11-20): 6b-6e approved, including the section phrases, the one-section wording, the
  mixed-campus notice and the Lost notice wordings; Mark all conflicts got Greg's own hover. Like round 5's, the
  wording may be fine-tuned once the feature runs.
- Round 7 answered by Greg (2026-11-20): wording agreed; with the other advisories; stranded speaks too; no ghost
  on the grid; new notice wordings agreed; try the lines during a meeting drag (built as 7b, with a switch: floating
  message bar or by the pointer). Found while testing: a bar that takes its own row pushes the grid down under the
  pointer mid-drag, so it must float. 7b also keeps one line per watch (its worst change). Greg chose the message
  bar over "by the pointer" (2026-11-20); the switch is gone from the page. Round 7 closed (Greg, 2026-11-20):
  stranded wording and one line per watch approved.
- Round 8 (built 2026-11-20, awaiting review; `access-panel-r8.html`): confirmed one-or-none components read "Any one
  section", not "One section" (round 2's count-like wording); inferred one-or-none is blank because it changes nothing;
  the form's advisory has no Open because Courses is a flyout; rule 36 drawn as Q32's recommended answer only. Found
  while building: in the mock, `.cs-body .new::after` (round 6) badges anything marked `new` inside a flyout body, so
  round 8 uses an explicit `.nw` badge.
- Round 8, Greg's first look (2026-11-20): "part" renamed **component** everywhere (spec, ledger, notes, r8's text;
  r1-r7 left as records; the mock's code keeps `parts`; the rename skipped plain English like "part of any slate").
  The column always lists components, "(inferred)" until confirmed, replacing the blank and "Any one section" above
  (Greg's reason: CHEM 213 and CHBE 201 behaved the same but looked different). Quick confirm: Confirm per row and
  Confirm all inferred, asked once. The heading hover was hidden behind the flyout in the mock (tooltip z-index). Built
  as 8b, awaiting review.
- 8c (Greg, 2026-11-20): the Components column folds (◂/▸ in its heading), remembered per computer; starts folded
  when no course has two or more components (an all-"Lecture" English department), open otherwise.
- 8d (Greg, 2026-11-21; D27): each section type says whether it is a component, asked once per database at the point
  of need (top of Access; a dialog when a watch is made first), pre-ticked from the component-name list; courses follow
  their section types unless set for the course. Replaces 8b's inferred/Confirm/Confirm all; Phase 2 rules 4-6
  revised in the spec. Rejected: a hard gate before any watch (breaks existing users' conflict boxes; friction at the
  moment watches should be easy); writing the inferred ticks when a database opens (changes a database just by opening
  it; read-only opens can't save). Unanswered = not stored, so readers and writers work it out the same way. In the
  mock, STAT 104 D got the section type "Online" (left from before delivery types) so the question has a type to leave
  unticked; ticking it shows the advisory.
- 8e (Greg, 2026-11-21): no Edit components links in the watch detail; with components set per department, per-course
  exceptions are rare and made in Courses.
- 8f (Greg, 2026-11-23; agreed, not yet built; supersedes 8d's model): a setup wizard the first time a watch is made, in
  Greg's terms ("do students register in more than one section of a course?"). No: done. Yes: mark the component
  section types, then check ALL courses' components (pre-filled), saved together. Each course owns its components; the
  course editor has the final say; inference is only a pre-fill. Greg rejected an "only multi-component courses" filter
  (assumes TermPoint is right; could hide things) and suggestions for Add Course (no sections yet). After a section type
  is newly marked Component, the follow-up goes to the course list, not the section editor (Greg: the course decides).
  Rule 8a (agreed): name a section whose component type its course doesn't list, since rule 7 would otherwise count it
  as an alternative and make access look better than it is. Open: suggestions for courses created by a section import.
  Built 2026-11-23 in `access-panel-r8.html`. Found while testing: unmarking a type silently rewrote every course's
  stored list when no watch changed, so unmarking now always asks in the row when any course has the type.
  Greg's review (2026-11-23): no this-term section counts or round-7 lines inside the wizard (cross-semester,
  once-for-all decisions); he'll fine-tune wording himself. Removed.
- D28 (Greg, 2026-11-23; supersedes D25, keeps D26): **filter presets go; the one way to make a watch is Watch on a
  tag**, as built. Reason: two sources of watches confused Greg himself (a watched tag plus a watched preset of the same
  tag); too much load, people give up. Accepted loss of capability for comprehensibility. Stays: the components
  machinery (8f wizard, courses own components, rule 8a), since a tag on a program's sections must still catch missing
  labs; marks, notices, pointer, Multi-campus box, subsets. Greg's answers (2026-11-23): (1) on upgrade each course-list
  watch becomes a tag named as the watch, put on its courses and their sections (stored data changes, so
  `MinimumVersionToWrite` Yes); (2) no new tagging helper, tagging stays as it is today; (3) when a watched course has
  sections without the tag, the watch detail shows one very quiet line, approved wording "Some sections of these courses
  are not tagged", never a list, so it can't mushroom. Accepted loss: other departments' (shared-schedule) courses can't
  carry the tag. Found: `Course.TagIds` exists and is applied to new sections of the course (not checked: existing ones).
  Built as 8g in `access-panel-r8.html` (2026-11-23), awaiting review; spec section "Revision 8g" and D28 row. The mock
  keeps its preset code inert (`presets: []`, no button). Found while building: the watch detail's section list holds
  only the watch's sections, so the untagged check reads the semester's (`secsSem`). Upgrade rule extended in the spec
  to tag watches with several tags or levels (same treatment); Greg agreed (2026-11-23).
- 8f and 8g approved by Greg (2026-11-23). Same day, round 8 closed: Q32 as recommended (Copy Semester copies section
  types anyway for multi-component courses, with a line); new rule 8b: a course created by a section import gets its
  components pre-filled from its imported sections' component types, said in one line of the import summary (wording
  still to draft for Greg; not in the mock). Existing courses' components are never changed by an import.
- Round 7 as first built (2026-11-20): the pointer reads "Access: <clause>" like the
  editor's "Room conflict:" lines, because the draft's "Affects access for Enviro Y2" makes the user click to learn
  anything; at most two lines (the editor is tight; no single edit in the mock touches four watches, so the fold is at
  three); worst first; the open watch's what-if line shows any change to its line, while the pointer speaks only for
  rule 31's kinds; the grid stays on the saved schedule (the real grid shows only saved sections while editing).
  The real editor already has a live advisory box (`ConflictWarning`, from a throwaway draft section) at the top of the
  card: the pointer can follow that pattern.
- Wording (Greg, 2026-11-19): "conflicts", never "clashes"; unconfirmed components are "inferred", never "guessed". In
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
- Data risk: components on courses, a delivery type field on sections and watches stored as presets will change what is saved; the `MinimumVersionToWrite` question is almost certainly Yes for the stored pieces. Persisted classes must derive from `PersistedRecord`.
- Real-data checks: UFV BIOL and UBC databases (dev machine only, read from a copy) model labs differently; do not treat one as representative.

</details>
