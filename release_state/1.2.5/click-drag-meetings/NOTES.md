# Click-and-drag moving of meetings in Schedule View: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: none yet (to be written at `designs/1.2.5/click-drag-meetings.md`).

<details>
<summary>Files to read first</summary>

- `ViewModels/GridView/SectionContextMenuViewModel.cs` (~line 183): the right-click Room edit; the precedent (find the meeting by day and start, change in place, `_sectionRepo.Update`, `SectionStore.Reload`).
- `Views/GridView/ScheduleGridView.axaml.cs`: `PointerPressed` (~1172); cached layout (`_cachedDayXOffsets`, `_cachedGridlineYOffsets`, `_cachedFirstRowMinutes`); ghost-tile code (~1355) used by the Room Browser.
- `ViewModels/Management/SectionListViewModel.cs` (~520): `LoadCore` calls `CollapseEditor()`; this is why a grid save may discard an open editor's edits.
- `TileClickContext(SectionId, Day, StartMinutes)` and `SectionMeetingBlock` / `TileEntry`.
- Line numbers are from the 2026-11-14 investigation and may have drifted.
- Source conversation: "Click-and-drag meeting scheduling" (session local_f0e97166, 2026-11-14).

</details>

<details>
<summary>Decisions and why</summary>

- Principle (proposed): "Create and edit in the list; arrange and adjust in the grid." Greg's vision (2026-11-16): limited scope, only the common edits already available by right-click, done by mouse; not moving section creation to the grid.
- Marks: a right-click "mark" frames sections brightly, temporary and never saved, like circling them on paper; then drag B, then A.
- Multiple meetings (Greg, 2026-11-14): by default moving a section moves all its meetings together; this must be switchable off. Claude's refinement, not yet agreed: group by start time, so dragging a meeting also moves every meeting of that section starting at the same time; group drags are vertical only (changing the day could turn 3 meetings into 2); Alt/Option held during the drag moves just one, with the live preview showing every meeting that will move (ghost tiles on each day, a time label covering folded or off-screen days). A held key, not a toggle, so there is no hidden mode.
- CLAUDE.md says "Manual data entry via forms only, no drag-and-drop scheduling": amend it deliberately (drag adjusts existing meetings; creation stays in the list).

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
- Meeting identity is (day, start); two meetings of one section on the same day and start (odd/even weeks) pick the first, as the right-click Room edit does today. Clean fix: carry the meeting's index in `SectionMeetingBlock` / `TileEntry`.
- A drop must not close an open section editor with unsaved edits without saying so (rule 4); verify today's behaviour first (open a card, type, right-click a tile, Room, Confirm).
- Co-scheduled tiles: dragging one row moves only that section. The room stays the same even if booked at the new time (no conflict detection; a soft warning is optional).
- After a drop, select the section in the list via `SelectedItem` so the card shows the new time; never scroll the list's ScrollViewer directly.
- Accidental drags: movement threshold before a drag starts, Escape to cancel, live time label; check whether there is any undo.
- A swap (A into B's slot, B elsewhere) happens as two separate drops; a mark just keeps both in front of the user.

</details>
