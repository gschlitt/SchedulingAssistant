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
