# Access Analyzer and easy access watches: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design: [designs/1.2.5/access-analyzer.md](../../../designs/1.2.5/access-analyzer.md).

<details>
<summary>Files to read first</summary>

- `designs/1.2.5/access-analyzer.md`: the spec. Status block at the top; Terms (D2); decisions D1-D29; Phase 2 rules 1-41; Phase 3 wireframe rounds table; Phase 4 (what is stored, the watch upgrade) and Phase 5 (layers, saving, tests) at the end.
- `designs/1.2.5/access-analyzer-handoff.md`: older handoff (written 2026-11-10). Its branch and release notes are out of date; the ledger replaces it for state. Its summary of the method and working agreements still holds.
- `designs/wireframes/`: the clickable HTML wireframes (rounds 1-8 settled; r8 is components in Courses, the setup wizard and D28; r9, Check all courses, settled 2026-11-23; all planned rounds done). Each round starts as a copy of the last and keeps its own file as the record. Reviewing them in Claude's browser pane: see `docs/dev/dev-machines.md` (to load an edited file afresh, close the tab and open the `file:///` URL again; reload and a same-tab navigate can keep the old page and its test clicks). `window.__access` exposes the mock's engine (`proposal`, `accessChanges`, `analysesFor`) for checking scenarios from the console.
- For the open no-full-slate question: `ConflictMarks.For`, `AccessReasons.Best`, `SlateSearch` (backtracking, shortest
  component first, a compatibility table), `GridAccessPicture.BlockingSections`, and the section "No full slate: what
  to mark" below.
- `src/TermPoint/Services/Access/`: the engine (piece C); start with `AccessAnalyzer.cs`, `AccessInputs.cs`, `ConflictMarks.cs`. Shape in "Piece C: engine shape" below. Tests: `src/TermPoint.Tests/Access*Tests.cs`; `AccessEngineMeasurementTests` times it on a copy of the UBC demo database.
- Piece D's session state and UI (to build E on): `Services/Access/AccessState.cs` (invalidate it after any change to
  component types, courses' components or the set-up mark), `AccessWatchToggler.cs`, `ViewModels/Access/`
  (`AccessText` holds all user wording; `AccessTabViewModel`), `Views/AccessTabView.axaml`, `Views/BottomLeftTabs.axaml`.
  Piece E's steps are Phase 6 "E. Components" in the spec; its code starts at `Services/Access/AccessSetupService.cs`
  and `ViewModels/Access/AccessSetupWizardViewModel.cs`.
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
- Round 9 (Greg's answers 2026-11-23, before building): settle the results screen, a course on the grid, before setup,
  and Q28; missing components and rule 8a sections in a quiet "Worth a look" list; button stays in the Access header.
  Built 2026-11-23 in `access-panel-r9.html`, awaiting review (spec: "Round 9" under the rounds table). Choices made
  while building, for Greg to confirm: a course alone follows the one-campus rule (so BIOL 201 L3 is reported stranded);
  out of date keeps the old results; a course with a missing
  component isn't also judged for slates (rule 40). Mock data: BIOL 340 (untagged, lab L1 on its only lecture) added;
  Semester changes 8 drops BMEG 230's tutorials and adds ENGL 105 T1 (Discussion); the suggestion now reads this term
  plus "an earlier term" (As planned), so a component not offered this term is still suggested. Found: "(Multi-campus
  is off)" in the stranded reason makes no sense for a course alone, so the check drops it.
- Round 9, Greg (2026-11-23): before setup the check doesn't run on the suggestion, since new watches don't either;
  the button opens the wizard first (as making a watch does). Read-only before setup: no check, a quiet comment that
  someone who can edit needs to set up first (draft wording). Built. Greg kept the other three as built (one-campus
  rule for a course alone, missing components quiet, out of date keeps old results): **round 9 settled 2026-11-23**.
- **Phase 3 locked by Greg (2026-11-23).** Q29: slot fine, `DetachablePanel` can do tabs; Q30 and Q31 as recommended
  (keep ▲ / ● / ○; no app notification for notices); Q32 answered earlier; Q33 moot; Q34: stable order, course then
  section code.
- **Phase 4 drafted (2026-11-23), awaiting Greg.** No new table: `isAccessWatch` and `watchMultiCampus` on the tag's
  record, `isComponent` on section types, `componentTypeIds` (null = never set) on courses, `deliveryTypeId` on sections,
  `AccessChecksSetUp` in `AppConfiguration`. The watch conversion goes in `DatabaseContext.Migrate` (schema version 2),
  so a reader's snapshot converts too and nothing new decides when; new tag IDs = old watch IDs (deterministic); old
  rows deleted (semester delete counts them). Found: `ReinitializeConnection` skips `Migrate` (must fix); Copy Semester
  without "Include all tags" would empty every watch (Q40); Edit Course's "apply tags to all sections" overwrites
  cohort tags (existing hazard). Local test databases hold 6 watches, 3 disabled, only one a course list.
- Q37-Q40 answered by Greg as recommended (2026-11-23): "(watch)" suffix on a name clash; own semester plus courses;
  disabled watches become unwatched tags; Copy Semester always copies watched tags, with a line (wording to approve).
  **Phase 4 locked by Greg (2026-11-23).**
- **Phase 5 drafted (2026-11-23), awaiting Greg.** Four layers: data; a pure engine in `Services/Access/` (WASM-safe);
  `AccessState` (session analyses, notices, what-if); UI. The editor pointer hooks in like `editVm.CheckConflicts`
  (`SectionListViewModel`). Found: today's grid scans only visible sections on purpose (comment in
  `ScheduleGridViewModel`), now Q41; multi-semester mode was never designed (Q43); `DetachablePanel` has no tabs yet
  (tab strip hosting two panels). To confirm before building: migration writes skip `MarkDirty`, but check
  `CheckoutService`'s unchanged-save logic.
- Q41-Q43 answered by Greg as recommended (2026-11-23): hidden partner = mark the visible tile, hover names it, no
  line; seats a later piece; several semesters analysed separately (worst named in the row, switch in the detail).
  Q44 recorded as D29 (engine static in `Services/Access/`, only `AccessState` registered). **Phase 5 locked by Greg
  (2026-11-23).** For Phase 6: measure the engine on UBC's real database before building UI; confirm the migration's
  writes reach D with the first save (`CheckoutService` unchanged-save logic); `ReinitializeConnection` must run the
  conversion.
- **Phase 6 drafted (2026-11-23), awaiting Greg.** Pieces A-H in the spec: A delivery type; B data layer and conversion;
  C engine, then measure on UBC; D `AccessState`, marks, eye, first Access tab (list plus Mark all conflicts, since
  What's new points to it); E components; F analyzer screens; G pointer and drag line; H release. A-E is the smallest
  unit that includes the conversion; F-G store nothing new. Confirmed by code read: `ContentHashAtCheckout` is taken
  before the version gate and before `DatabaseContext` opens D' (so before `Migrate`), so a converted D' is never an
  "unchanged" save; the first save carries conversion and stamp together. `ReinitializeConnection`'s three callers
  (reader refresh, `SessionLifecycle.OnWriteLockLost`, `MainWindowViewModel` refresh) all pass `readOnly: true`.
  Open: Q45 (what ships, after C), Q46 (B and D released together), Q47 (Edit Course tag overwrite, backlog), Q48
  (delivery type details as a half-page).
- **Phase 6 locked by Greg (2026-11-24)**, Q45-Q48 as recommended: what ships decided after C's measurement; B and D
  committed separately on 1.2.5 but never released apart; Edit Course tag overwrite went to `release_state/backlog.md`;
  delivery type's details as a half-page at the start of A. **Next: piece A.**
- **Piece A details (A.1) drafted (2026-11-24), awaiting Greg.** Round 6's agreed parts kept; round 6's "round 5's
  preset rule" for deleting is moot (presets gone), so delete follows section types'. Found: Scheduling Environment's
  shared `Delete` (`SchedulingEnvironmentListViewModel`) rewrites sections and reloads the list without
  `OpenCardMessages.WhyRefused`, so deleting any type with a card open drops the typing (Q49). Code map for building A:
  type strings in `SchedulingEnvironmentTypes`; categories and descriptions in `SchedulingEnvironmentViewModel`; filter
  in `GridFilterViewModel` (shared via `SectionStore.FilteredSectionIds`); card text `SectionListItemViewModel.SectionTypeName`;
  right-click `SectionContextMenuViewModel.DetailSectionType`; Copy Semester options in `CopySemesterViewModel`; import
  columns `SectionImport/SectionImportColumns` (plus planner, writer, counts, messages, near-miss); backup CSV in
  `BackupService`; demo lists in `Demo/DemoData.SchedulingEnvironment.cs`. Nothing is seeded for new databases today.
- **A.1 approved (Greg, 2026-11-24), Q49-Q51 as recommended. Piece A built 2026-11-24** (Sonnet the C#, Opus the views;
  `DeliveryTypeTests` plus extended import, clone, context-menu tests). Choices made while building, for Greg's hand
  check: (1) the Delivery Type import column may be left out of a file (`ImportColumn.HeaderOptional`, only that
  column), since every column was required and old templates and the scraper's CSVs would otherwise be rejected;
  (2) the filter has no "No delivery type" entry, mirroring Section Type, which has none (round 6 drew one; adding it
  would be new for both); (3) the filter's Delivery Type button is hidden while the list is empty
  (`GridFilterView.UpdateAllHeaders`); (4) the right-click details label is "Delivery:" beside "Type:"; (5) the
  section Copy command doesn't copy it (it doesn't copy section type either); (6) editor layout (Greg, 2026-11-24, after
  a five-column row overran its headings at 400 px): Tags, Reserve Codes, Resources on one row; Campus and Delivery
  Type side by side on a row below.
  (7) cards (Greg, 2026-11-24): a collapsed card's top line shows the section type only; open cards add the delivery
  type and capacity in the same columns (`SummaryTypeText`, `SummaryCapacity`). Property lines (tags etc.) wrap at
  384 px (`SectionPropertyLinesMaxWidth`) instead of widening the card. Full suite 2026-11-24: one failure,
  `NetworkHangTests.AHungWrite_TimesOutByTheDeadline...` (file in use in its temp folder), passed on rerun: a flake.
  **Piece A hand-checked by Greg and committed 2026-11-24.** Next: piece B (Phase 6 steps B.1-B.6; write the
  fix-on-fix check into these notes before coding). `tools/section-import/README.md` says 19 columns.
- **Piece B built and committed 2026-11-24** (Sonnet the C#, Opus reviewed; full suite with the harness: 4912
  passed, 0 failed). Must not be released without D (Q46): the old watch UI still reads `ProgramWatches`, now empty.
  Conversion is `Data/Migrations/WatchConversion.cs` (excluded from the browser build), called from `Migrate`;
  tests `WatchConversionTests`, `AccessChecksDataTests`, `Harness/WatchConversionHarnessTests`. Choices made while
  building, where the spec left room: old rows store `mode` as a number (0 tag, 1 course; a string or the `mode`
  column is also accepted); "first watch by semester order" = academic year name, then semester sort order, then id,
  missing semesters last; a name shared by single-tag and other watches: single-tag rows mark their own tag, the rest
  share one new tag; a watch that covered nothing still gets its tag (nothing the user made disappears); the name
  clash check includes tags made earlier in the same run; a blank name becomes "Watch"; an old watch ID already used
  by a value row (practically impossible) gets a repeatable derived GUID; only `tagIds` is edited in section and
  course JSON, and only rows whose tags change are rewritten. `AccessChecksSetUp` lives behind
  `IAccessChecksSetupRepository` (demo version for WASM). Found: `IsMigrationNeeded` returned false for every
  version-1 file (the legacy column checks all say "up to date"), so the conversion would never have run; it now
  returns true for any stamped version below current. `ReinitializeConnection` also runs `InitializeSchema`
  (idempotent) before converting, only below version 2. Consequence for release: every 1.2.3/1.2.4 database is version
  1, so the first 1.2.5 open runs the migration (with its backup) even with no watches; 1.2.4 accepts version 2.
  Extension-data and clone tests now skip only unconditional `[JsonIgnore]`, so the written-only-when-set fields are
  covered. **Next: piece C.**
- **Piece C.1–C.2 built 2026-11-24, not committed** (Sonnet the C#, Opus reviewed): `Services/Access/` (14 files),
  `GetSectionTypeIdsByCourse` on the section repositories, `ProgramConflictService` now uses `SectionOverlap`; 124
  new tests (`Access*Tests`, fixtures `AccessTestKit`, `AccessMockSemester` = the r9 mock's data, variants 0–8, with a
  brute-force slate-count cross-check). Full suite 4914 passed, 0 failed; both compile checks clean. Choices made
  where the spec left room (for Greg's eye): Q18 names match as whole words anywhere in a type name ("Lab Section");
  two pins in one component both stay candidates; nothing covered = no slate but health Ok unless findings; Check all
  courses doesn't check setup itself (its caller does, round 9).
- **C.3 measured 2026-11-24** (`AccessEngineMeasurementTests`, self-skips without `DEMO\Test-TT.db`; numbers also in
  `%TEMP%\access-engine-measurement.txt`). 561 sections, 45 tags as watches (43 program-years, 2 converted):
  **every watch, analysis + marks, 23 ms** (budget 30); worst single watch 3 ms (GEOE-Y3: 7 courses, 15 components);
  Check all courses 4 ms; suggestion query 6 ms. **The one real cost: best partial is exponential in courses** when
  there is no full slate: 12 courses 9 ms, 14 courses 76 ms, 16 courses 790 ms, 18 courses 1.6 s. No program has
  over 9 courses, but a user can tag more. → Q52.
- **Q52 answered by Greg (2026-11-24): bound the search.** Built: a greedy pass gives a lower bound, then the exact
  top-down search runs until 300 subset tests (`AccessReasons.BestPartialSubsetBudget`, rationale in its doc);
  out of tests, it keeps the best found, `BestPartial.Complete = false` (wording later: "at least", F).
  `AccessBestPartialBoundTests`. Re-measured: every watch 25 ms; analysis ≤ 7 ms up to 32 courses, ≤ 70 ms at 126.
  Every real program is still searched exactly. **New, for Greg (Q53):** at that size the conflict marks cost
  instead: 32 courses 19 ms, 110+ courses about 0.5 s (one search per conflict with a can't-fit course). A tag on
  100+ courses can't describe a student's term, so the proposal is a course limit per watch (say 30): above it, no
  analysis or marks, and the watch says it is too big to check (wording for Greg).
- **Q53 answered by Greg (2026-11-24): cap at 30 courses.** Built: `AccessAnalyzer.MaxCourses` (rationale in its
  doc); over it, `AccessHealth.TooLarge` and nothing else, and no marks. The cap counts the chosen courses, so a
  subset of a large watch can still be analysed. `AccessWatchSizeTests`. Wording for the too-big watch: F (draft
  for Greg). Final: every watch 16–25 ms (varies run to run), at 30 courses 14 ms, over 30 nothing. Full suite
  4922 passed, 0 failed.
- **Q45 answered by Greg (2026-11-24): the whole feature (A–H) ships in 1.2.5.** No split into 1.2.6; F and G
  stay in this release.
- Found on real data: 25 of the 45 watches have no full slate, mostly from the scrape (CHBE-Y4: CHBE 453's only
  Studio overlaps its only lecture, Tue/Fri 14:00; before setup Studio counts as a component, Q18). The engine is
  right; expect UBC users to untick types like Studio in the setup wizard, and expect "No full slate" to be common
  on first open of scraped data.
- **D-core built 2026-11-24, not committed** (Sonnet the C#, Opus reviewed; plan in "Piece D: build plan"):
  `AccessState`, `AccessSnapshot`, `GridConflict` (the drawing record, moved and renamed), `ViewModels/Access/AccessText`
  and `GridAccessMarks`. Full suite 4943 passed, 0 failed; both compile checks clean. Choices: the analysis is lazy
  per watch (the grid pays only for marks); a failure computing marks logs and draws the grid without them; a tile's
  tooltip merges its entries' lines; "visible" = on the grid in any section block. Found: a database switch builds a
  new service provider, so `AccessState` can't serve another database; saves and reader refreshes both end in
  `SectionStore.Reload`. Changed on purpose: `GridWatchFocusTests` now asserts a conflict with a hidden subject IS
  marked, with the Q41 line (old rule: not reported). The old popup's badge stays at 0 until D-ui removes it. Not
  audited: which Scheduling Environment edits raise `GridContentChanged` (a tag rename must, for names in hovers).
  D-ui wording approved by Greg (2026-11-24): What's new as the r9 banner ("Access watches now mark only conflicts
  that cost students something: a section no one taking the watch's courses can use, or no way to take every course.
  To see every conflict as before, tick Mark all conflicts in the Access tab."); Q41 "<section> is hidden by the
  filters."; too big "Too many courses to check (more than 30)"; empty tab "No access watches yet. An access watch is
  a tag you watch: TermPoint then shows whether students can still take all its courses together. [+ Add a watch]";
  Add a watch note, Greg's own: "An access watch is a tag watched for student access, to ensure that students can
  register in a full slate of sections without time conflicts. If your tag describes courses (or specific sections)
  you wish students to take together, you can enable the watch by clicking the eye."
- **D-ui built 2026-11-24; hand-checked by Greg and committed with D-core the same day.** Next: piece E. (Sonnet the C#, Opus the views and review).
  Access tab beside Workload: the tab strip is both panels' title (`BottomLeftTabs`; empty `Header`, window titles
  passed to `DetachSlot`), each tab detaches alone, a detached tab hides from the strip, a red dot on Access when a
  watch needs a look. List rows: Path marks (not Unicode, WASM), name, health line (`AccessText.HealthLine`, the
  mock's `healthSegs` without seats, cautions, pins, subsets). Eye in the Tags popup and Scheduling Environment → Tags
  (`AccessWatchToggler`: one tag `Update`, then invalidate and redraw; refuses read-only). Add a watch opens the Tags
  popup with Greg's note. What's new entry for 1.2.5. Old popup, `AccessPanelViewModel`, `WatchCreationViewModel`,
  `ProgramConflictService`, the `ProgramWatch` model and repositories deleted (table and schema kept; the conversion
  reads rows as JSON); Semester contents no longer counts watches. Full suite 4904 passed, 0 failed. Found and fixed:
  Scheduling Environment edit and delete never told the grid (no `GridContentChanged`), so a renamed tag stayed stale.
  Choices for Greg's eye: "N conflicts" only in the general health line (as the mock); "A or B can't fit" sorted by
  course code; the Header's 12 px margin leaves a small gap before the tabs (`DetachablePanel` not edited). Not done:
  campus and course renames don't refresh Access names until the next reload; the old ProgramWatches rows of a
  semester emptied in 1.2.5 are no longer deleted (always empty after the conversion).
- Greg's hand check, first comment (2026-11-24): a partner hidden by the chosen Subjects (top banner), not the
  filters, was said to be "hidden by the filters". Now the hover names the reason (`HiddenReason`, from
  `FocusService.Matches`); draft wording for that case: "<section> isn't shown: its subject isn't one of your chosen
  Subjects." Approved by Greg (2026-11-24).
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
<summary>Piece B: fix-on-fix check for ReinitializeConnection (B.6, 2026-11-24)</summary>

The conversion runs inside `ReinitializeConnection` only when `user_version` < 2, before `ApplyReadOnly`, in
`Migrate`'s own transaction, without the pre-migration backup. Code callers are two (all `readOnly: true`); the
writer's fresh-copy path (`RefreshLocalCopyAfterFailedCheckAsync`) rebuilds the services, so it goes through the
constructor's `Migrate`, not here.

- **Reader refresh** (`MainWindowViewModel.RefreshData`, user-clicked; no automatic refresh exists). At that moment:
  the connection is closed (BeforeOverwrite) and D'' swapped; no save, heartbeat or backup touches D'' (readers don't
  save or back up). The poll compares D against `HashAtCheckout` (D's bytes, set at the swap), never D'''s content,
  so converting D'' can't make the poll see a change and loop. A throwing conversion rolls back and counts as a failed
  reopen: 3 attempts, then the existing "connection lost" line. D'' is a throw-away copy: rule 3 doesn't arise.
- **Demotion after lock loss** (`SessionLifecycle.HandleWriteLockLostAsync` → `DemoteCoreAsync` AfterOpen). Usually
  reopens on a fresh D'' copied from D (unconverted if the last writer was 1.2.4 or Bob never saved): converted as
  above. If the snapshot failed, it reopens on the kept D' (the only copy of unsaved work): that file was opened by
  this session's constructor, so it is already version 2 and the conversion writes nothing. Pinned by a test
  (reopening a version-2 file leaves its bytes unchanged). The poll timer starts just before AfterOpen, but a refresh
  needs the user's click and Mode is already ReadOnly, so this is today's window, made no wider except for an
  unconverted D''.
- **Writer save paths:** untouched. `ContentHashAtCheckout` is taken before the constructor's `Migrate`, so the first
  save after a conversion is a real one (test pins it).
- **Tests:** conversion cases (unit); twice = identical rows; first save real; harness: reader refreshing onto a file a
  1.2.4 writer saved, and a demotion onto an unconverted D.

</details>

<details>
<summary>Piece C: engine shape (Opus, 2026-11-24)</summary>

Reference implementation: the r9 mock's script (`designs/wireframes/access-panel-r9.html`, `groupsFor` … `analyse`,
`conflictMarksFor`, `runCheck`, `mismatches`, `derivedParts`). The engine ports its logic, returns **structured results
only** (ids, kinds, lists; no sentences: wording is F's), and fixes the mock's shortcuts listed here.

- `Services/Access/`, namespace `TermPoint.Services.Access`, static/immutable (D29), models only (WASM).
- `SectionOverlap.Conflicts(a, b)` and `OverlappingMeetings(a, b)` (rule 13); `ProgramConflictService` calls it until D
  deletes that class.
- `ComponentNames.LooksLikeComponent(name)`: Q18's list (Lecture/LEC, Laboratory/Lab, Tutorial/TUT, Discussion/DIS,
  Recitation/REC, Seminar/SEM, Studio/STU, Workshop/WKS, Practicum/PRA), ignoring case, whole words.
- `AccessInputs`: one semester's saved sections + `Proposal` (upserts, removed ids; empty = saved), courses by id,
  section types in sort order, `SetUp`, the suggestion source (section type ids seen per course, every semester).
  Component types: `SetUp` ? `IsComponent` : name list. A course's components: `SetUp` and stored list not null →
  stored ∩ component types; else suggestion = (seen ∪ this view's types) ∩ component types. Sorted by type order.
- Suggestion query: `ISectionRepository.GetSectionTypeIdsByCourse()` (one SQL query, orphans excluded; demo version
  in memory).
- Grouping (rules 7–10, 8a): ≤ 1 component → one group of all sections; ≥ 2 → one group per component, others not
  counted, any empty group → course missing and left out. Mismatches (8a, set up only) over the course's semester
  sections, not just covered ones. Pins narrow a group.
- In person (campus rule 15/6e): has a non-remote meeting and a campus. Mix = two in-person sections, different campus.
- Search on an index-based compatibility matrix built once per analysis. Fix to the mock: when the count hits the cap,
  the used set is incomplete, so each unused section gets an existence search with it fixed (roles stay right).
  Costly marks test only the stranded sections with the pair ignored, not a full recount.
- Stable order (Q34): courses by calendar code, components by type order, sections by code.
- `AccessAnalyzer.Analyze(inputs, covered, multiCampus, subset, pins, cap)` plus a watch overload (tag coverage, D28;
  untagged flag 8g). `ConflictMarks.For(...)` (rule 33, Q22, 6b; Harmless only with Mark all conflicts).
  `CheckAllCourses.Run(inputs)` (rules 39–41, round 9: one-campus rule, missing components quiet).
- Measurement vehicle (Greg, 2026-11-24): `C:\ProgramData\TermPoint\DEMO\Test-TT.db` (always a copy): UBC
  Engineering Winter Term 1, 562 sections, 228 courses, 43 program-year tags (BMEG-Y2 … MINE-Y4, 2–9 courses each)
  to treat as watched tags, plus 2 old watches (schema version 0, so the copy also runs the piece B conversion).
- Not in C: notices (F), what-if (G), seats (Q42: later), cautions (F, from per-section slate counts kept on the
  result), all wording.

</details>

<details>
<summary>Piece D: build plan (Opus, 2026-11-24)</summary>

Split in two because D.3–D.5 edit `.axaml` (Greg closes the files in VS first) and wait on wording.

**D-core (no `.axaml`):**
- `Services/Access/AccessState.cs`, singleton, both DI paths. Holds one snapshot per loaded semester (`AccessInputs`,
  each watched tag's `Analysis` and `ConflictMarks`). **Lazy:** handlers only mark it stale and raise `Changed`;
  `Current` recomputes when stale. Stale on `SectionStore.SectionsChanged` (semester switches, saves and reader
  refreshes all end in a reload), `GridChangeNotifier.GridContentChanged` (tag rename etc.), and `Invalidate()`
  (eye, Mark all, later components). Ordering: `ScheduleGridViewModel` takes `AccessState` as a constructor
  dependency, so DI builds it first and its handlers run before the grid's reload (the codebase's existing pattern
  for `FocusService`, `SubjectColorPresenter`). The suggestion query runs once per recompute.
- **Mark all conflicts:** `AppSettings.MarkAllAccessConflicts` (per computer, default off), set through `AccessState`.
- Grid: `AccessState`'s marks replace `DetectConflicts`, over the whole semester (rule 29), every watched tag. The
  drawing record keeps today's shape (moved out of `ProgramConflictService`); glow and lines unchanged. Hover lines
  (port of the r9 mock's `conflictTips`, approved round 6 wording) go on the tile's existing tooltip. Q41: a partner
  that is filtered off gets the mark on the visible tile, no line, and one hover line (draft wording, for Greg).
- Wording lives in one UI-side static class (`ViewModels/Access/AccessText.cs`), never in the engine (D29).
- Tests: `AccessState` (stale after reload / invalidate, a semester switch, multi-semester separate, Mark all);
  grid marks after a save and after a reader refresh; hover texts.

**D-ui (`.axaml`, after Greg closes the files):** the eye in the Tags popup and Scheduling Environment → Tags
(disabled read-only; calls `Invalidate()` and redraws); the Access tab beside Workload (tab strip over two
`DetachablePanel`s; list rows ▲/●/○, name, health line with conflict count, ported from the mock's `healthSegs`
without seats, cautions, pins or subsets; Mark all conflicts at the foot); remove the filter-bar popup,
`AccessPanelViewModel`, `WatchCreationViewModel`, `ProgramWatchItemViewModel`, `ProgramConflictService`, the
`ProgramWatch` repository (the conversion reads JSON) and their tests; What's new entry. Files: `MainView.axaml`,
`ScheduleGridView.axaml`, `AccessPanelView.axaml` (deleted), the Tags popup's and Scheduling Environment's views.

</details>

<details>
<summary>Piece E: build plan (Opus, 2026-11-24)</summary>

Split like D: **E-core** (Sonnet: services, view models, tests; no `.axaml`) then **E-ui** (Opus: the views, after
Greg closes them in VS: `CourseListView`, `SchedulingEnvironmentListView`, `CopySemesterView`, `ImportSectionsView`,
`AccessTabView`, the Tags popup's view; new `AccessSetupWizardView`).

- **Writes:** one `AccessSetupService` (singleton, both DI paths; refuses without the write lock, like
  `AccessWatchToggler`): wizard No (types unmarked, set-up mark), wizard Yes (type flags, every course's list, mark),
  mark a type, unmark a type (with its affected courses), save several courses' lists. Each in one transaction
  (nullable-tx pattern), only changed rows written; then `AccessState.Invalidate` and `NotifyGridContentChanged`.
- **Wizard as a flyout page** (like Courses), not a window: WASM-safe and no picker/dispose deadlock. Same view model in
  "new type" mode for the follow-up after ticking Component. Wording from the r9 mock (approved for coding, 8f).
- **First eye before setup** (Tags popup, Scheduling Environment → Tags): opens the wizard; the watch is made only
  when it finishes. Access tab: the invite (editing) or the quiet read-only line until set up.
- **Courses:** Components column only after setup, folds (`AppSettings`, null = open when any course has 2+), ▲ with
  rule 8a's sentence. Edit Course group only after setup; Add Course then starts with an empty list (before setup:
  null). A course save invalidates `AccessState`.
- **Copy Semester:** section types kept for courses with 2+ components (rule 36) and watched tags kept (Q40) whenever
  the option that would copy them is off, in both copy modes (choice for Greg); lines before Copy.
- **Import (8b):** after setup, each new course stores its imported sections' component types; existing courses
  untouched; summary line names the courses with 2+.
- Not in E: round 7's what-if lines and notices (F, G).
- **Approved by Greg (2026-11-24):** Q40 "Watched tags are still copied, so access watches carry over."; rule 8b
  "Components were filled in for BMEG 230 and CHEM 201 from their sections' types. You can change them under
  Courses." (up to three named, else "12 new courses"); Copy Semester keeps them in both copy modes.
- **E-core built 2026-11-24** (Sonnet; Opus reviewed): `AccessSetupService`, `ComponentSuggestion`,
  `AccessInputsFactory`, `AccessSetupWizardViewModel`, `AccessTextSetup` (wording from the r9 mock, marked approved),
  `SectionTypeRowViewModel`, `CourseComponentsContext`; 53 tests (`AccessSetup*Tests`). Choices: rule 8b runs in
  the planner (`SectionImportPlanningRun`), so plan, summary and rows agree; "this term" counts and the Courses
  column read every loaded semester; a never-set course opens in Edit Course on the suggestion; the wizard keeps
  course ticks across Back. Not covered: rule 8b through the writer; demo saves aren't one transaction (no tx).
- **E-ui built 2026-11-24** (Opus): wizard view (`Views/Access/AccessSetupWizardView`), Access tab invite, Courses
  column (DataGrid columns can't bind width, so `CourseListView.SyncComponentsColumn` sets it), Edit Course group,
  Copy Semester and import lines, Section Type's Component column. Change from the mock: the "Stop counting it?" ask
  shows under the list, not in the row (the list is a DataGrid of stored values); the box only changes after a save.
- **Greg's hand check (2026-11-24/25), all built; E committed:**
  - "Access checks" is "access watches" in all user text (code comments still say checks).
  - Wizard: his new wording for the parallel-courses note (italics via `InlineFormatter`'s new `*word*`), question 2
    and the course check; course headings stay put (the flyout hosts pages in a ScrollViewer, so the rows' own
    ScrollViewer is capped by `WizardCourseTableMaxHeight`).
  - "Any one section" removed everywhere (wizard rows, Courses column now blank, Edit Course hint): it describes how
    the analyzer counts, not the course, and confused next to a lone ticked type.
  - Section Type description: his two paragraphs.
  - **Component boxes work before the setup** (a user who never makes a watch can still mark types). They show the
    guess (`ComponentBoxChecked`, display only, `[JsonIgnore]`) under a line saying so; the first click saves guess
    plus change as the setup (`SetComponentBeforeSetup`), courses stay null. A tick asks "which courses have it?"
    only when some course has a stored list; that step then returns to Scheduling Environment, not the main panels.
  - Access tab: invite is just "Two quick questions before using access watches" + Set up + "What's an access
    watch?" link (URL is a placeholder, `AccessText.WhatIsAWatchUri`: set in H); the empty-tab paragraph is gone;
    Add a watch and Mark all conflicts hide until set up. Workload | Access tabs: panel-title size, a divider.
  - Test DB: Greg's demo DB is `C:\ProgramData\TermPoint\DEMO\Test-TT.db`; to see the wizard again, delete the
    `AccessChecksSetUp` row of `AppConfiguration` and strip `isComponent` / `componentTypeIds` from the JSON (backup
    first; done once 2026-11-24).

</details>

<details>
<summary>Piece F: build plan (Opus, 2026-11-25)</summary>

Reference: the r9 mock (`listView`, `detailView`, `candText`, `cautionsFor`, `grid`/`entry`, `ctxMenu`, the legend bar in
`render`, `accessChanges`/`causeOf`/`noticeText`, `runCheck`/`checkView`). Its wording was approved for coding in
rounds 5–9. Split like D and E: **F-core** (Sonnet; no `.axaml`) then **F-ui** (Opus, after Greg closes
`AccessTabView.axaml` and `ScheduleGridView.axaml` in VS).

- **Notices (F.2).** Pure `Services/Access/Notices.cs`: `Compare(before, after)` per watch present in both, kinds as
  the mock's `accessChanges` (lost, left, mixes, essential, stranded, restored; one per watch, its worst). `AccessState`
  keeps the previous snapshot and compares **per semester present in both**, so a semester switch never makes one
  (no reason flag needed). Compares only after a section reload or a components/setup save
  (`AccessSetupService` says which courses); not after the eye, Mark all, Multi-campus or ticks. Session-only list,
  newest first; a new notice replaces the watch's older one; ✕ dismisses; a notice that stops being true (rechecked
  after each recompute) goes by itself. Cause from the semester's section diff: one section changed → the mock's
  `causeOf`; several (a reader refresh, an import) → a general cause (draft).
- **Open watch state.** A small singleton (`AccessPicture`) holds the open watch or checked course, ticks, pins,
  stepping index and Show other courses, and raises `Changed`; the tab's detail, the grid's legend bar, the grid's
  tiles and the right-click menu all read it. The analysis with ticks and pins is `AccessAnalyzer.Analyze(inputs,
  watch, subset, pins)` on the snapshot's inputs; slates listed by `SlateSearch.List`.
- **Detail VM (F.1):** port of `detailView` (health, flags incl. stranded, cautions ◆, missing, not counted, rule 8a,
  untagged line, courses with `candText` phrases and ticks, Multi-campus box (a tag write beside the eye in
  `AccessWatchToggler`), the fold of quieter facts, Watching button). Check-mode variant for a course alone.
- **Grid (F.3):** per-entry looks set directly on `TileEntry` (faded, stranded, essential ●, can't fit ▲, caution ◆,
  in-slate ghost, pinned), built by a pure `GridAccessPicture` with the mock's hovers. While a watch is open the grid
  shows its sections only, unless Show other courses (then the usual filter result plus them). Legend bar VM: name,
  subset, pin pill, Step through slates / stepper, Mark all, Show other courses, ✕ Close. Pin / Unpin in the section
  right-click menu while a watch is open.
- **Check all courses (F.4):** `CheckAllCourses.Run` per semester, results a snapshot that goes out of date on any
  `AccessState.Changed`; before setup it opens the wizard first (editing) or shows the read-only comment; a problem
  line opens the course alone (check mode).
- **Drafts for Greg:** read-only comment "Access watches aren't set up for this database yet. Someone who can edit it
  needs to set them up before courses can be checked." (the mock's, with "access checks" → "access watches"); general
  cause "The latest changes mean …"; components cause "Changing BMEG 230's components" (round 8) and, for several
  courses, "Changing courses' components".
- **Not in F:** round 6d's day widths while a watch is open (ask Greg); seats; what-if lines (G); clickable section
  names inside messages (later, if wanted).
- **F-core built 2026-11-25, not committed** (Sonnet; Opus reviewed): `Notices.cs` (`AccessNotices`), `AccessPicture`,
  `Cautions`, `AccessWatchDetailViewModel`, `AccessLegendViewModel` (`ScheduleGridViewModel.AccessLegend`),
  `AccessCheckViewModel`, `GridAccessPicture` (`AccessTileLook` on `TileEntry.Access`), wording partials
  `AccessTextNotices/Detail/Picture/Check`; tab VM pages List/Detail/Check with notices; context-menu Pin
  (`HasPinItem`, `TogglePinCommand`); `AccessWatchToggler.SetMultiCampus`. 102 new tests; full suite 5038 passed,
  0 failed; both compile checks clean. Choices for Greg's eye: notices keyed by watch and semester; a watch open shows
  only its sections (no meetings, commitments, shared schedules) unless Show other courses; an Edit Course components
  change makes a notice too ("Changing X's components"); health phrases plain (no warn colour or hovers yet); list
  lines leave cautions out; Check's Open in Courses doesn't select the course. More drafts: "Adding X", "Deleting X",
  "Changing X's tags", "Changing X's course", the separate-labs footer, and three failure lines "… couldn't be saved /
  checked / changed. Your work is safe; try again." **Next: F-ui** (Opus; Greg closes `AccessTabView.axaml` and
  `ScheduleGridView.axaml` first): detail and check pages, notices box, legend bar, tile looks in
  `ScheduleGridView.axaml.cs`, Pin menu item.
- **F-ui built 2026-11-25, not committed** (Opus): `AccessTabView.axaml` has three pages (list with notices and
  clickable rows; detail; check) and Check all courses beside Add a watch; the legend bar sits in the grid's row 1
  above the error banner (`ScheduleGridView.axaml`); Pin / Unpin under Clear All Marks in the right-click menu; tile
  looks in `ScheduleGridView.axaml.cs` (`AccessOpacity`, `AccessFrame`, `NewAccessMark`: faded and stranded by
  opacity, the essential outline, the slate's ghost fill and outline, ▲ ◆ ● and pin icons after the label). New
  resources: `Access*` brushes in `AppColors`, `Access*` sizes in `AppLayout`, `AccessMarkCautionIcon` and
  `Chevron*Icon` in `Icons` (paths, since the WASM fonts lack ‹ › ▸ ▾). Not hand-checked yet. Not done: Escape to
  close the detail; a few labels in the view, not `AccessText` ("All watches" on the check page, "Unpin all", "Open").
- **Greg's first hand check (2026-11-25), both built:** (1) rows didn't look clickable → hover tint and a ▸ on each
  row (Open and ✕ are only on notices). (2) An open watch replaced the grid's sections privately while the filter bar
  still showed the user's filters, and hid all filtering. **Decided (Greg): opening a watch sets the real filter bar
  to its tag** (a course checked alone: Course), so the header says so and other filters and overlays combine as
  usual (`AccessWatchFilter`; `GridFilterViewModel.CaptureSelection` / `ApplySelection`). Opening remembers and
  replaces the user's filters; Close restores them only if the filters are still just what opening set (changed
  meanwhile = theirs, left alone); switching watches with untouched filters keeps the remembered ones. Show other
  courses is gone (remove the tag filter instead; other sections are drawn faded). The grid draws what the filters
  show, events and shared schedules included. Legend bar: quiet line when filters or the subject focus hide some of
  the watch's sections, DRAFT "2 of Cohort 1's sections are hidden by the filters." (+ " or the chosen Subjects").
  The Section View narrows too (it shares the filter). Full suite 5040 passed, 0 failed.
- **Greg, tile hover (2026-11-25), built:** a tile the Access marks or an open watch speak about is hovered with
  their lines alone, no time range (other tiles keep it); each line's watch name in bold when several watches speak
  (`**W:**`, `InlineFormatter`, `TileTooltip.Markup`); lines 6 px apart (`TileTooltipNoticeSpacing`).
- **Greg (2026-11-25), built:** every mention of a watch's name in the grid's hovers is bold (`AccessText.Bold`, in
  `ConflictTips` and `GridAccessPicture`; a course checked alone stays plain). A section of a course left out for a
  missing component (rules 10, 6d) was faded with the wrong hover ("no section type"); now DRAFT "MANU330 L0A isn't
  counted: MANU330's lecture sections aren't in **CHBE-Y2**." (`MissingComponentTip`). Full suite 5041 passed.
  Found by Greg the same day: with no full slate that case was drawn plainly, with no hover, because the no-slate
  branch returned first; the not-in-any-group check now runs before the slates. Full suite 5043 passed.
  Then (Greg): still no hover. Cause: tiles showed a tooltip only with more than one line (the time used to be line
  one), so a single Access line was dropped after the time was removed. Now `TileTooltip.IsShownOnTile`.
- **Greg (2026-11-25), built:** the hover repeated itself when the open watch's own line and its
  conflict-mark line said the same thing. The look's line is now left out on that day (`AccessTileLook.IsRepeatedBy`,
  `GridAccessMarks.RepeatedLooks`): stranded/Stranded mark on it, can't fit/no-full-slate mark, usable/harmless mark.
  Essential or only section with an Other mark keeps both. Left as is (Greg): two conflicting co-scheduled sections on
  one tile get mirror-image no-full-slate or harmless lines ("A conflicts with B…", "B conflicts with A…"). Full
  suite 5045 passed.
- **Greg, CHBE-Y3 on the UBC import (2026-11-25), built:** ▲ "can't fit" sat on every section of
  CHBE346 and CHBE352, lectures included, with no conflict in sight. Cause (diagnosed first): their only
  discussions (both T1A, Monday 14:00–16:00) clash, so there is no full slate, and the best partial drops either
  course, so both are "can't fit"; the grid then marked the whole course. The analysis was right, the picture misled.
  Greg chose "mark blocking sections": ▲ now goes only on a can't-fit course's sections in a time conflict with
  another component (`GridAccessPicture.BlockingSections`); the course's other sections are drawn plainly. A course
  with no such section (a clash of three or more at once) or a campus-rule cause keeps the ▲ on all its sections.
  Not addressed: nothing on the tiles says "either one" (dropping either course fixes it). Full suite 5045 passed.
  This rule is too broad too (see the next section): it marks any section of the course that clashes with anything.

</details>

<details>
<summary>No full slate: what to mark (open, 2026-11-25)</summary>

**What Greg saw.** CIVL-Y3 on the UBC import (`UBC_Engineering_WinterTerm1_import_tagged.csv` in the UBC demo
folder): red marks and ▲ on Monday, Tuesday, Wednesday and Thursday. Moving CIVL303's Friday meeting to 11:30 cleared
them all except Thursday's. Greg: "highly misleading now, suggesting problems all over the place when there's really
only one."

**Cause.** CIVL303 T1A (Tue/Fri 12:30–13:30, its only discussion) clashes on Friday with CIVL315 101 (MWF 13:00–14:00,
its only lecture): no full slate. Best partial (course-level, `AccessReasons.Best`: tries every set of courses from
the largest down, asking `SlateSearch.ExistsForCourses`) covers 5 of 6, two alternatives, so both courses are
"can't fit". `ConflictMarks.For` then, with no full slate, marks EVERY clash touching a can't-fit course: NoFull when
ignoring that one clash brings a slate back (only Friday's), else NoSingleFix (Q22). So CIVL315's lab clashes with
CIVL311's labs and discussions, which students can get around, all got "Fixing only one of these conflicts isn't
enough", which is false here. The bug: Q22 marks fire per clash without asking whether some OTHER single clash already
fixes the watch. Thursday is a real separate problem: CIVL332 T1A (Thu 12:30–14:30, only discussion) strands CIVL315
L1M/L1N (Thu 14:00–16:00); with a full slate the Stranded rule marks it, rightly.

**Proposal (not agreed; Greg wants a strategy and UI conversation first).** Principle: mark only what's essential: the
conflicts in the SMALLEST fixes, where a fix is a set of clashes whose removal brings a full slate back.
- One problem: only single-clash fixes are marked (here Friday only). Cheap: `ConflictMarks` already runs that test.
- Two problems: no single fix, so test pairs; mark the clashes in every pair that works.
- Q22's CHEM/PSYCH/GEOG example gives the same two marks Greg agreed on 2026-11-10, so the principle includes Q22.
- Pruning: a clash in any fix must touch a can't-fit course (dropping that course would remove the clash), so pairs
  come only from the clashes `ConflictMarks` already considers; a couple of hundred tests for CIVL-Y3. Budget it, and
  time it with `AccessEngineMeasurementTests`.
- ▲ follows the marks (sections in marked clashes), replacing `GridAccessPicture.BlockingSections`'s rule.
- `SlateSearch` ignores one pair today (`Compatible`'s ignoreA/ignoreB); extending to two is small.
- Open: when no fix of size 1–2 is found within budget, no marks (recommended: the detail panel names the courses) or
  today's mark-everything? DRAFT wording for a two-clash fix whose clashes share no section: "CIVL303 T1A conflicts
  with CIVL315 101, so **CIVL-Y3** has no full slate. Fixing this alone isn't enough: CIVL332 T1A's conflict with
  CIVL320 101 must be fixed too." The same gap exists with a full slate: a stranded section freed only by removing two
  clashes gets no mark today.
- Also open from the CHBE-Y3 case: nothing on the tiles says "either course" when the best partial has alternatives.

</details>

<details>
<summary>Dead ends</summary>

- Separate course-list and tag watches (Q35, Q36): superseded by D25.

</details>

<details>
<summary>Technical issues</summary>

- `DetachablePanel` tabs: Greg (2026-11-23) says it can do tabs, so the splitter fallback isn't needed (Q29).
- The "teal boxes" are red in the app: `ScheduleGridView.axaml.cs` gives each conflicting entry row a radial glow
  (`ProgramConflictGlowCenter`/`Edge`) and joins cross-tile pairs with a 2 px `ProgramConflictBrush` line; no hover
  today. Pairs come from `ProgramConflictService.DetectConflicts` (any overlap between two courses of a watch).
- Today's Section Type description in `SchedulingEnvironmentViewModel` gives "Online" as an example, and Meeting Type
  says "In-Person, Hybrid, Remote": both overlap delivery type (r6 rewords them).
- Data risk: components on courses, a delivery type field on sections and watches stored as presets will change what is saved; the `MinimumVersionToWrite` question is almost certainly Yes for the stored pieces. Persisted classes must derive from `PersistedRecord`.
- Real-data checks: UFV BIOL and UBC databases (dev machine only, read from a copy) model labs differently; do not treat one as representative.

</details>
