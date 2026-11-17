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
- The drag (piece 4): `ViewModels/GridView/MeetingDragSession.cs` and `GridGeometry.cs`; `ScheduleGridViewModel.TryBeginMeetingDrag` / `CommitMeetingDrag`; in `ScheduleGridView.axaml.cs` the "Dragging a meeting" region (`MeetingDragState`, `UpdateDrag`, `DrawDragVisuals`, `EndDrag`). For the group drag: pass `moveAlone` from Alt in `UpdateDrag` and watch Alt in the key handlers.
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
- SUPERSEDED 2026-11-16 by the Alt inversion (build log). Multiple meetings (Greg, 2026-11-14): by default moving a section moves all its meetings together; this must be switchable off. Claude's refinement, agreed by Greg 2026-11-16: group by start time, so dragging a meeting also moves every meeting of that section starting at the same time; group drags are vertical only (changing the day could turn 3 meetings into 2); Alt/Option held during the drag moves just one, with the live preview showing every meeting that will move (ghost tiles on each day, a time label covering folded or off-screen days). A held key, not a toggle, so there is no hidden mode.
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
- Greg hand-checked piece 1 (open-card guard) on 2026-11-16: all checks pass.
- 2026-11-16, piece 2 (groundwork), committed by the handoff: `MeetingIndex` on `SectionMeetingBlock`/`TileEntry`/`TileClickContext`; the right-click menu finds its meeting through one helper (index if it still matches day and start, else first match), which fixes the odd/even twin case. `SectionDaySchedule.Clone()` is now the one meeting copy (`CloneSection` uses it). `Services/MeetingMover` holds the pure move logic: `MovingIndices`, `CanChangeDay`, `Snap` (Shift = 5-minute steps), `Move` (refusals NoChange, OutOfDay, OverlapsOwnMeeting with the conflicting meeting), and the approved `TimeLabel`/`OverlapLabel`. Tests: `MeetingMoverTests` (20). Both compile checks and the full suite (4651 passed, 0 failed) clean.
- For the drag piece: a group can mix lengths (Tue 1.5 h, Thu 3 h at the same start); snapping and the time label use the dragged meeting's length.
- 2026-11-16, piece 3 (marks), committed by the second handoff: `SectionMarks` (an id set owned by `ScheduleGridViewModel.Marks`, so marks survive reloads and semester switches and vanish with the DI rebuild on a database switch); menu items "Mark Section"/"Unmark Section" and "Clear All Marks" (only while any mark exists); a marked line's row gets the `MarkedSectionBorder` frame (orange, AppColors) at the selection thickness, chosen by one helper `EntryRowFrame` in the grid code-behind; height accounting counts framed rows; a mark change calls `Render()`. Readers now get the right-click menu (to mark); its edit items are disabled for them via `CanShowSubPanel`. Tests: `SectionMarksTests` (9).
- Marks decisions (Greg, 2026-11-16): menu words "Mark Section"/"Unmark Section"/"Clear All Marks"; the tile menu is enough (no empty-grid menu). REVISED by Greg the same day: a marked and selected line shows both frames, the selection ring around the mark frame (was: mark frame only).
- 2026-11-16, piece 3c (both frames), committed by the fourth handoff: a marked line's row is wrapped in an outer ring Border (`SelectionRingBrush`: selection colour when selected, transparent otherwise; kept in `EntryRowInfo.SelectionRing` so `UpdateSelectionHighlight` repaints it). Room for both rings is always reserved on marked lines (`EntryRowFrameCount`: 2 marked, 1 selected-unmarked; height counts frames, width adds one frame for marked lines), so selecting a marked line never resizes it. Unmarked lines are unchanged. Folded-day strips unchanged (Marked still outranks Selection there). Both compile checks and the grid tests (281) pass. Hand check: mark a section, select it: blue ring outside the orange frame; deselect: orange only, no size jump.
- 2026-11-16, piece 3d (marks in the Section List; Greg's request), committed by the fourth handoff: `SectionMarks` is now a DI singleton (shared registration in `App.axaml.cs`) injected into `ScheduleGridViewModel` (still exposed as `Marks`) and `SectionListViewModel`. Cards get `SectionListItemViewModel.IsMarked` (set in `CreateSectionItem` and by `ApplyMarks` on `SectionMarks.Changed`; unsubscribed in `Dispose`). `SectionListView.axaml`: a mark Border (`MarkedSectionBorder`, 3 px via `FilterBorderThicknessConverter`) between the selection Border (outside) and the card. Unlike the grid, the card grows by the frame when marked (as it already does when selected). Test: `GridFilterFocusTests.Marks_ReachTheSectionListCards`; all test constructions pass a `SectionMarks`. Both compile checks, full build with AXAML, full suite (4701 passed, 0 failed) clean. Hand check: mark a section in the grid; its card gets an orange frame, with the blue selection frame outside it when selected; Clear All Marks clears both views.
- Known, existing: a selection change repaints frames without re-measuring, so a row selected after a render grows by the frame inside its tile; marks avoid it by re-rendering.
- Greg checked marks on 2026-11-16: look good (colour may be adjusted later in AppColors, `MarkedSectionColor`). He asked for marks on folded days.
- 2026-11-16, piece 3b (marks on folded days), committed by the third handoff: `MarkKind.Marked`, first in `FoldedDayMarks.Kinds` (outranks Selection, as on tiles; Greg's choice), brush `FoldedDayMarkMarked` from the same `MarkedSectionColor`; tooltip "{label} marked at {time}" / "{n} sections marked, first at {time}" (approved, mirrors Selection). `MarkInputs` gained `MarkedSectionIds`, filled by the grid view from `Marks.Ids`; a mark change re-renders, which redraws the strip marks. Tests added to `FoldedDayMarksTests`. Full build with AXAML, browser compile and full suite (4663 passed, 0 failed) clean. Hand check: mark a section, fold one of its days, the strip shows an orange bar with the tooltip.
- 2026-11-16, piece 4 (single-meeting drag), committed by the fourth handoff (Opus planned and reviewed, Sonnet coded): pure `ViewModels/GridView/GridGeometry` (pointer y ↔ minutes by binary search over the piecewise-linear gridlines; x → nearest unfolded day; the view's `GetGridlineOffset` now delegates to it) and `MeetingDragSession` (snapshot of the schedule at lift; `Preview` clamps to the shown range, snaps, calls `MeetingMover.Move`, builds ghosts and the label; `MatchesSnapshot`). `ScheduleGridViewModel.TryBeginMeetingDrag` / `CommitMeetingDrag` (writer and open-card checks, re-checked at drop; stale schedule → no save, logged; edit in place, `Update`, shared `ReloadSectionStore`, select). `ISectionCardState.OpenCardChanged` (raised by `SectionListViewModel.OnEditVmChanged`) clears the drag refusal from the message bar, only if it is still the one showing. View: the canvas takes pointer capture on a plain left press on a section line (writer only); lift past `MeetingDragThreshold`; ghost in the line's semester sub-column; label flips left at the grid's right edge; Esc and Shift via tunnel key handlers on the TopLevel while lifted; release outside the grid's ScrollViewer cancels; a re-render mid-drag cancels. New resources: AppColors `DragGhostBorder`, `DragGhostRefusedBorder`, `DragLabel*`; AppLayout `MeetingDrag*`. Always `moveAlone: true` in this piece (the session already supports groups).
- Decision (Opus, 2026-11-16): the meeting's own start is a snap point too (ties go to the legal start), so a meeting at a non-legal time dragged back home is "no change" (story 5). Tell Greg at the hand check.
- Tests: `GridGeometryTests`, `MeetingDragSessionTests`, `MeetingDragCommitTests`; both compile checks clean; full suite 4698 passed, 0 failed, 122 skipped (expected local-DB and harness skips).
- Hand-check list for piece 4: drag a section line to another time and day (ghost, label "Fri 13:00-14:30", snaps to legal starts; Shift = 5-minute steps, live on press/release); let go → saved, selected, card summary shows the new time; Esc and letting go outside the grid cancel; drag onto another meeting of the same section → red ghost, "Overlaps its ... meeting", nothing saved; open a section card, drag → no lift, message bar says "Finish editing ... Apply or Cancel it there, then drag again.", which disappears on Apply/Cancel; folded days are never targets; zoomed in and with two semesters shown; read-only → nothing lifts; a click and a double-click still select / open the card.
- 2026-11-16, piece 5 (group drag), not yet committed (Opus, short job): the view now passes `moveAlone` from Alt (`MeetingDragState.Free` / `MoveAlone`, read from each pointer event's modifiers by `ReadDragModifiers`, and flipped live by the tunnel key handlers on Shift/Alt press and release). The session already built one ghost per same-start meeting on its own day; the view already drew every ghost (folded days skipped, the label names them). Commit path unchanged (group-agnostic). CLAUDE.md's drag-and-drop line amended to the design's wording. Tests: two more in `MeetingDragSessionTests` (Alt switching live on one session; mixed lengths). Both compile checks clean; full suite 4702 passed, 1 failed, 122 skipped: the failure was `NetworkHangTests.AHungWrite_TimesOutByTheDeadline_ThenLandsLateWhenTheGateOpens` (temp file in use; untouched code), passing on rerun.
- Alt and the main menu: Avalonia's access-key handler listens to the window's tunnelling key events and, on an Alt press then release, shows menu underlines and opens the main Menu (MainView has one). Probe (scratch console, Avalonia 12.1.3): tunnel handlers on one element run last-added first, so the drag's handlers (added at lift) run before it; the drag marks Alt press and release handled while lifted. Alt held before the press is harmless (its pointer press hides the underlines, so the release opens nothing). Rationale is in `OnDragKeyDown`'s XML doc.
- Hand-check list for piece 5: drag one tile of a Tue/Thu section: ghosts on Tue and Thu, label "Tue, Thu 11:30-13:00", moving sideways keeps both on their days; drop moves both, one save. Hold Alt (Option on macOS) mid-drag: one ghost, follows the pointer to other days; release Alt: both again, live. No menu underlines and no menu opening when Alt is pressed or released during a drag, nor when Alt is released after a drop. Alt held before pressing also works. Fold Thursday: the label still says "Tue, Thu". A section with meetings at different starts, or one meeting, changes day without Alt. Only the grabbed line dims (the other moving meetings' lines don't; say if they should).
- Greg hand-checked everything on 2026-11-16 (marks on folded days, both frames, Section List marks, single-meeting drag, group drag, Alt and the menu): all pass.
- Decision (Greg, 2026-11-16, after trying it): **Alt inverted.** A drag moves only the grabbed meeting, even when others of the section start at the same time; holding Alt (Option) brings those along (time only). Built: `MeetingDragState.MoveGroup` (Alt) and `moveAlone: !MoveGroup`; session and mover unchanged. Design stories 2, 3 and 6 revised. Hand check: drag one tile of a Tue/Thu section, no key: one ghost, can change day; hold Alt: both ghosts, own days; release: one again.
- Greg checked the inverted Alt and the whole feature on a busy real schedule, 2026-11-16: all well.
- Auto-scroll when dragging near the edge of the grid's viewport (not in the design; Greg asked for it 2026-11-16, built the same day, see below). Costed 2026-11-16 (about half a session, no data risk): a timer while lifted; when the pointer is within an edge band of the grid's one ScrollViewer (or past its edge), scroll by a speed that grows with depth, then convert the remembered viewport point to canvas coordinates (`TranslatePoint`, covers zoom) and call `UpdateDrag`. Band and speed as AppLayout resources; the speed curve pure and unit-tested. Nothing listens to the grid's scroll, so scrolling doesn't re-render (which would cancel the drag). Watch WASM: `DrawDragVisuals` rebuilds the ghosts every call; redraw only when the preview changes. Letting go outside the grid still cancels.
- 2026-11-16, piece 6 (auto-scroll), not yet committed (Opus, short job): pure `GridGeometry.AutoScrollSpeed` (0 in the middle, linear ramp inside each edge band, full speed at and past the edge; bands shrink on a tiny viewport); tests in `GridGeometryTests`. View: a `DispatcherTimer` (16 ms) runs while lifted (`StartAutoScroll` on lift, `StopAutoScroll` in `EndDrag`); `MeetingDragState.LastViewportPoint` is the pointer relative to the grid's ScrollViewer (`GridViewport()`); each tick first moves the ghost to follow the previous tick's scroll (`TranslatePoint`, current because layout has run since), then scrolls by speed × elapsed time (capped at 100 ms per tick). `DrawDragVisuals` now rebuilds the ghosts and label only when the preview changes (`SamePicture`); otherwise `PlaceDragLabel` just moves the label. Resources `MeetingDragAutoScrollBand` (32) and `MeetingDragAutoScrollSpeed` (1200 px/s) added to AppLayout (the code keeps the same fallbacks). Both compile checks, full build with AXAML, full suite (4705 passed, 0 failed, 122 skipped) clean.
- Hand-check list for piece 6: drag a meeting toward the bottom edge of Schedule View: the grid starts scrolling inside the last ~32 px, faster nearer the edge and fastest past it; the ghost and label stay under the pointer and keep snapping; same toward the top. Zoomed in so the grid is wider than the panel: left and right scroll too. Hold the pointer still near the edge: scrolling continues and the ghost keeps updating. Stops at the ends of the grid. Letting go inside the grid drops; outside still cancels; Esc cancels. Also try a detached Schedule View window, and the WASM demo if convenient (smoothness).
- Greg checked auto-scroll on 2026-11-16: all well.

</details>

<details>
<summary>Field test plan (2026-11-16)</summary>

Why a field test at all: a drop is an ordinary local edit (same repository call as the section card), so the shared-database paths are unchanged; the field test checks that drops behave like any other edit around saves, readers, takeovers and lock loss, on the real share. Setup per `docs/dev/field-testing.md`: reset the test database; build 1.2.5 at f6eebf3 or later on both computers. Alice = GregDesktop, Bob = GregSurface.

1. **Ordinary use on the share.** Alice opens the test database (writer) and makes a mix of edits: plain drags (one meeting, day change), Alt drags of a Tue/Thu section, a Shift drag to an odd time, an auto-scrolled drag, two right-click edits, marks. Save. Expect: no warnings in her log; every moved meeting shows its new time in its card.
2. **A reader sees the drops.** Bob opens the same database (read-only). Expect: Alice's saved moves are there; Bob's tiles don't lift; he can mark (marks are his alone); his edit items are greyed. Alice drags once more and saves; Bob's view picks it up on its next poll.
3. **Drop during a save.** Alice starts a save with clumsy on ("degraded"), so it takes a while, and drops a meeting while "Saving…" shows. Expect: the save finishes; the drop is kept and goes out with the next save (check from Bob after it). Same with a drop right as the 10-minute autosave fires, if it can be timed.
4. **Exit and reopen.** Alice drops a meeting and closes TermPoint (exit save). Reopen: the move is there, marks are gone (never saved). Close with a meeting lifted (Alt+F4 while holding the button, if Windows allows): no save of the half-done drag, no error.
5. **Takeover.** Alice exits; Bob takes the database and drags; Alice reopens read-only and sees Bob's moves after his save.
6. **Lock lost.** Suspend Alice's TermPoint (Resource Monitor) long enough for Bob to take over, then resume. Expect: Alice gets the usual lost-lock message; afterwards nothing lifts in her grid; nothing she dragged after the takeover reaches the share. (A drop exactly at the moment of loss is refused by the re-check at drop; covered by `MeetingDragCommitTests`, not reproducible by hand.)
7. **Detached Schedule View** on a second monitor: drag, Alt, auto-scroll, open-card refusal message.
8. **macOS, if a Mac is handy** (build from a v* tag): Option for the group drag (and that Option release doesn't open a menu), trackpad drag, auto-scroll.
9. **Afterwards:** copy the shared database to the scratchpad (never query it in place) and check a few moved sections' meetings against what was done; read both computers' logs for "Meeting drag dropped without saving" or errors.

**`MinimumVersionToWrite`:** proposed answer No: a drop changes only the day and start of existing meetings, fields every version writes, and marks are never saved. Greg confirms at the field test.

</details>

<details>
<summary>Undo: costing (advice to Greg, 2026-11-16; backlogged)</summary>

- **Decided (Greg, 2026-11-16): backlogged, not in this entry.** A one-step "Moved X to ... · Undo" notice after a drop (Claude's proposal: reads as part of the drag, not an app feature) was judged short of really useful; the useful form is multi-step undo across section changes, which belongs in its own entry. Line in `release_state/backlog.md`.

- Greg, trying the drag, saw that users will want an undo. The design says no undo (his earlier decision); where it goes is an open question in the ledger.
- Cheap (about one build session): undo for grid edits only (drags and right-click edits), last 5, in memory, never saved (`MinimumVersionToWrite` stays No). Before each grid save keep the section before and after; undo writes "before" back through the same save path, with the open-card guard.
- The one real risk: overwriting later edits. Undo only when the section still equals its "after" copy; otherwise refuse with a message (wording for Greg) and drop the older steps. Clear the history on a database switch and on any reload from the shared copy (lock lost and retaken). The restored copy must keep extension data (the existing section clone does).
- The interface is the cost: Ctrl+Z belongs to text boxes, so prefer a short "Moved X to ... · Undo" notice after a drop plus an Undo button naming what it undoes.
- Moderate (+half a session): also undo a section card's Apply (it saves a whole section too). Leave out creates, deletes and multi-section actions (re-inserting a deleted section with its id is where bugs hide). App-wide undo: weeks and real data risk; not recommended.
- The design doc's stray undo fragment at the end of "Technical detail" was removed when the question was settled (2026-11-16).

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
