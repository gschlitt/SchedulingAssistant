# Click-and-drag moving of meetings in Schedule View: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: [designs/1.2.5/click-drag-meetings.md](../../../designs/1.2.5/click-drag-meetings.md) (approved 2026-11-16).

<details>
<summary>Files to read first</summary>

- The design: `designs/1.2.5/click-drag-meetings.md` (stories, then the compact technical detail).
- `Services/MeetingMover.cs`: the pure move logic the drag calls (`MovingIndices`, `CanChangeDay`, `Snap`, `Move`, `TimeLabel`, `OverlapLabel`); tests in `MeetingMoverTests`.
- `Services/ISectionCardState.cs`, `Services/OpenCardMessages.cs`: the open-card check and its approved wording (`FinishFirstToDrag` is for the drag refusal).
- `ViewModels/GridView/SectionContextMenuViewModel.cs`: the right-click edits, the precedent for saving (`_sectionRepo.Update`, then the onSaved reload built in `ScheduleGridViewModel`); `RefusalMessage`; `FindMeeting`.
- `Views/GridView/ScheduleGridView.axaml.cs`: `TileClickContext` (now with `MeetingIndex`), `PointerPressed`, the cached layout (`_cachedDayXOffsets`, `_cachedGridlineYOffsets`, `_cachedFirstRowMinutes`), the Room Browser ghost-tile code. The right-click menu popup is in `ScheduleGridView.axaml`.
- `ScheduleGridViewModel.LastErrorMessage`: the grid's message bar, for the drag refusal.
- Line numbers drift; search by name.
- Source conversation: "Click-and-drag meeting scheduling" (session local_f0e97166, 2026-11-14).

</details>

<details>
<summary>Decisions and why</summary>

- Open card (Greg, 2026-11-16): while a section card is open, grid right-click edits and drops are refused with a message, like the list's `IsEditorBusy`; keeping the card open through a reload was rejected (its later Apply could overwrite the grid's change). Today's right-click bug is fixed in this entry, as the first build piece.
- Design answers (Greg, 2026-11-16): draft wording approved; group rule agreed (same-start meetings move together, time only; Alt/Option moves one alone); marks agreed (frame, any number, cleared by "Clear marks" or another database); default snapping to legal start times, Shift held for free 5-minute steps; **no undo** this release; guard the other reloads too.
- Other `SectionStore.Reload` callers checked (2026-11-16): Course list "Apply tags to all sections" (`CourseListViewModel.ApplyTagsToAllSections` via `CourseEditViewModel`) gets the guard, its refusal shown in `ApplyTagsResult`; the two in `App.axaml.cs` run only while a database opens (no card can exist); the list's own semester-switch reload (`SectionListViewModel.OnSemesterContextChanged`) is not guarded by `IsEditorBusy` and probably discards too: Greg put it on the 1.2.5 backlog (2026-11-16), not this entry.
- Principle (proposed): "Create and edit in the list; arrange and adjust in the grid." Greg's vision (2026-11-16): limited scope, only the common edits already available by right-click, done by mouse; not moving section creation to the grid.
- Marks: a right-click "mark" frames sections brightly, temporary and never saved, like circling them on paper; then drag B, then A.
- Multiple meetings (Greg, 2026-11-14): by default moving a section moves all its meetings together; this must be switchable off. Claude's refinement, agreed by Greg 2026-11-16: group by start time, so dragging a meeting also moves every meeting of that section starting at the same time; group drags are vertical only (changing the day could turn 3 meetings into 2); Alt/Option held during the drag moves just one, with the live preview showing every meeting that will move (ghost tiles on each day, a time label covering folded or off-screen days). A held key, not a toggle, so there is no hidden mode.
- CLAUDE.md says "Manual data entry via forms only, no drag-and-drop scheduling": amend it deliberately (drag adjusts existing meetings; creation stays in the list).

</details>

<details>
<summary>Findings: an open editor is thrown away (2026-11-16, from the code)</summary>

- **Today, any right-click edit in the grid (Instructors, Room, Tags, Flag) silently discards an open section editor
  in the list**, whichever section it is editing. Chain: `SectionContextMenuViewModel.Confirm` → `_sectionRepo.Update`
  → its `onSaved` (built in `ScheduleGridViewModel`, ~line 216) → `SectionStore.Reload` → `SectionsChanged` →
  `SectionListViewModel.Reload` → `LoadCore` → `CollapseEditor()`, which sets `EditVm = null` without saving or
  saying anything. Nothing in the grid checks `SectionListViewModel.IsEditing` first (the import does, via
  `ImportSectionsHost.IsEditingSection`).
- Also reached by other `SectionStore.Reload` callers, e.g. `CourseListViewModel` (~286) and two in `App.axaml.cs`
  (~468, ~596; not yet read).
- Why `LoadCore` collapses: the rebuilt list has fresh card VMs, so the old editor would be orphaned. Keeping it open
  instead would also need care: if it edits the same section, its later Apply would overwrite the grid's change.
- A drop is one more caller of the same path, so the design must decide first: refuse while a card is open (the
  list's own `IsEditorBusy` message is the precedent), or reload without closing the card.
- Not yet hand-confirmed. 30-second check: open a card, type in a field, right-click a tile in the grid, Room,
  Confirm; the card closes and the typing is gone (on a build before piece 1).
- Resolved 2026-11-16: Greg chose to refuse (piece 1 built it); the Course list's "Apply tags to all" is guarded too.

</details>

<details>
<summary>Build log</summary>

- 2026-11-16, piece 1 (open-card guard), committed by the handoff: `ISectionCardState` (implemented by `SectionListViewModel.OpenCardHeading`, also used by its `IsEditorBusy`) and `OpenCardMessages` (approved wording, incl. the drag variant for later) in `Services/`. The grid menu sets `RefusalMessage` on Load (greys the four Show commands) and re-checks in Confirm; `ScheduleGridView.axaml` shows it as a warning line at the top of the menu. The Course editor refuses "Apply tags to all" via a `whyApplyTagsRefused` callback into `ApplyTagsResult`. Tests: `OpenCardRefusalTests` (9). Both compile checks, full build with AXAML, full suite (4631 passed, 0 failed) all clean.
- Greg's hand check of piece 1, still to do: open a card, right-click a grid tile (edits greyed, line shows); open the menu first, then a card, then Confirm (nothing saved, line shows); Courses → Apply tags to all with a card open.
- 2026-11-16, piece 2 (groundwork), committed by the handoff: `MeetingIndex` on `SectionMeetingBlock`/`TileEntry`/`TileClickContext`; the right-click menu finds its meeting through one helper (index if it still matches day and start, else first match), which fixes the odd/even twin case. `SectionDaySchedule.Clone()` is now the one meeting copy (`CloneSection` uses it). `Services/MeetingMover` holds the pure move logic: `MovingIndices`, `CanChangeDay`, `Snap` (Shift = 5-minute steps), `Move` (refusals NoChange, OutOfDay, OverlapsOwnMeeting with the conflicting meeting), and the approved `TimeLabel`/`OverlapLabel`. Tests: `MeetingMoverTests` (20). Both compile checks and the full suite (4651 passed, 0 failed) clean.
- For the drag piece: a group can mix lengths (Tue 1.5 h, Thu 3 h at the same start); snapping and the time label use the dragged meeting's length.
- 2026-11-16, piece 3 (marks), committed by the second handoff: `SectionMarks` (an id set owned by `ScheduleGridViewModel.Marks`, so marks survive reloads and semester switches and vanish with the DI rebuild on a database switch); menu items "Mark Section"/"Unmark Section" and "Clear All Marks" (only while any mark exists); a marked line's row gets the `MarkedSectionBorder` frame (orange, AppColors) at the selection thickness, chosen by one helper `EntryRowFrame` in the grid code-behind; height accounting counts framed rows; a mark change calls `Render()`. Readers now get the right-click menu (to mark); its edit items are disabled for them via `CanShowSubPanel`. Tests: `SectionMarksTests` (9).
- Marks decisions (Greg, 2026-11-16): menu words "Mark Section"/"Unmark Section"/"Clear All Marks"; the tile menu is enough (no empty-grid menu); a marked and selected line shows the mark frame (the label still shows selection).
- Known, existing: a selection change repaints frames without re-measuring, so a row selected after a render grows by the frame inside its tile; marks avoid it by re-rendering.
- Greg's hand check of piece 3, still to do: mark two sections (orange frame on every day, also when selected); Unmark one; Clear All Marks appears only while something is marked; marks survive a filter change and a semester switch; as a reader, the menu opens with edits greyed and marking works.

</details>

<details>
<summary>Dead ends</summary>

None yet.

</details>

<details>
<summary>Technical issues</summary>

- Estimated 1-2 days for the single-meeting version (earlier investigation); the group version adds the time label and the Alt switching. Production risk is low: same fields as the section editor, no shared-database save, lock or recovery path touched.
- Code-behind is acceptable for the pointer handlers because the whole grid is built in code-behind (CLAUDE.md prefers behaviors, but not where unnatural).
- Pointer y to minutes is piecewise linear and increasing (invertible); pointer x to day must handle sub-columns for several semesters and folded days. A tile can be taller than its time span, so take the new time from the tile's top, not the pointer.
- Meeting identity: tiles now carry the meeting's index (piece 2), so odd/even twins on the same day and start are told apart; use `TileEntry.MeetingIndex`, not (day, start).
- A drop must not close an open section card (rule 4): piece 1 added the check (`ISectionCardState`); the drag must use it before lifting a tile.
- Co-scheduled tiles: dragging one row moves only that section. The room stays the same even if booked at the new time (no conflict detection; a soft warning is optional).
- After a drop, select the section in the list via `SelectedItem` so the card shows the new time; never scroll the list's ScrollViewer directly.
- Accidental drags: movement threshold before a drag starts, Escape to cancel, live time label; check whether there is any undo.
- A swap (A into B's slot, B elsewhere) happens as two separate drops; a mark just keeps both in front of the user.
- Marks: Greg settled the tile menu alone for Clear All Marks (2026-11-16); no empty-grid menu.
- Drag refusal while a card is open: `OpenCardMessages.FinishFirstToDrag` into `ScheduleGridViewModel.LastErrorMessage`; clear it when the card closes (`SectionListViewModel.IsEditing` change).

</details>
