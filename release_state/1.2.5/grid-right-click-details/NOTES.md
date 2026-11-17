# Section details in Schedule View's right-click menu: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). No design doc (small enhancement, Greg 2026-11-16).

<details>
<summary>Files to read first</summary>

- `Views/GridView/ScheduleGridView.axaml`: the right-click menu popup (refusal line, the four "Assign/Set" items, Mark, Clear All Marks).
- `ViewModels/GridView/SectionContextMenuViewModel.cs`: the menu's view model (`Load`, `RefusalMessage`, `CanShowSubPanel`, `MarkItemText`).
- `Views/GridView/ScheduleGridView.axaml.cs`: the hover tooltip for shortened initials (`entry.InstructorNames`, `BuildTileTooltipContent`); `TileClickContext`.
- The click-and-drag entry's notes: the menu changes it made (marks, readers get the menu, open-card refusal).
- Line numbers drift; search by name.

</details>

<details>
<summary>Decisions and why</summary>

- Greg, 2026-11-16: show the instructors' full names (even for one instructor), the section type and the tags in the right-click menu; keep the tile's initials shortening as it is.
- Greg, 2026-11-16: no design doc; setting filters from the right-click is skipped for now.
- Mock-up APPROVED as is by Greg, 2026-11-16 (approved wording; readers see it; hover tooltip stays): a details block at the top of the menu, above the refusal line and the edit items:

  ```
  HIST 101 A                      (bold: which line was clicked; matters on co-scheduled tiles)
  Instructors: Jane Smith         (one per line; "Instructor:" when one; "Instructor: none")
               Robert Chen
  Type: Lecture                   (line left out when the section has no type)
  Tags: Online, First-year        (comma list, wraps; line left out when none)
  ───────────────
  Assign Instructor to Section ▶
  ...
  ```

  Proposed: shown to readers too; the hover tooltip for "+N" lines stays (it costs nothing and is quicker).

</details>

<details>
<summary>Build log</summary>

- 2026-11-16, built (Opus, short job), not yet committed: `SectionContextMenuViewModel` gained `DetailHeading`, `DetailInstructorLabel`, `DetailInstructorNames` (newline-joined, stored order, unresolved ids dropped), `DetailSectionType`, `DetailTags` (configured tag order), set by `FillDetails` from `Load`, which took two optional parameters (`detailsHeading`, `sectionTypes`). `ScheduleGridViewModel.PrepareContextMenu` passes the "CALCODE SECTION" heading (same form as the tile label) and the section types. `ScheduleGridView.axaml`: a details Grid (label column + value column, MaxWidth 260, FontSizeFilterPopup, heading in SectionHeadingFontWeight) and a rule above the refusal line. Tests: `GridContextMenuDetailsTests` (7). Both compile checks clean; full suite 4712 passed, 0 failed, 122 skipped.
- Hand-check list: right-click a one-instructor section line: bold "HIST 101 A", "Instructor: Full Name"; a three-instructor "+N" line: "Instructors:" with one name per line, and its hover tooltip still works; a section with no instructors: "Instructor: none"; a section with a type and tags: "Type: …", "Tags: a, b" (long tag lists wrap, menu no wider than ~260); without them the lines are absent; a co-scheduled tile: each line's own details; read-only: the details show, edit items greyed; with a section card open: details, then the yellow refusal line; the sub-panels (Assign Instructor etc.) still open beside the menu.
- Greg hand-checked it on 2026-11-16: all good. `MinimumVersionToWrite` answer: No (the details only read; nothing is saved).
- 2026-11-16, bug found by Greg while checking (also in released 1.2.4): scrolling the Assign Instructor list could make the whole menu jump across the grid. Cause: the popup used `Placement="Pointer"`, which Avalonia re-applies at the *current* pointer (`TopLevel.LastPointerPosition`) whenever the popup resizes; the virtualized list widens when a long name scrolls in. Fix, not yet committed: `AnchorAndGravity` (TopLeft/BottomRight) with `PlacementRect` set at the right-click point by `ScheduleGridView.AnchorTileMenuAt`. Both compile checks and the grid tests pass; Greg hand-checked it: the menu stays put. The Day header menu (also `Pointer`) doesn't resize while open, so it was left alone.

</details>

<details>
<summary>Dead ends</summary>

None yet.

</details>

<details>
<summary>Technical issues</summary>

- Class-conditioned styles on per-line elements are costly (CLAUDE.md); the menu is built once, so styles there are fine, but nothing new should be added to every section line.
- Fonts, sizes and colours from AppColors/App resources; warn Greg before editing `ScheduleGridView.axaml` (VS 2022 won't reload it).

</details>
