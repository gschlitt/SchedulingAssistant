# Room Availability Browser — Feature Design

## Status
Current phase: **3 — Implementation** (COMPLETE)  
Next phase: **4 — Runtime Testing**  
Last session: 2026-05-13  
Next step: Close running app, run unit tests, manual verification in app

---

## Phase 1: Problem Statement

### The Problem
When creating a new section, the user must choose meeting times and a room. These decisions are interdependent: the times you want depend on which rooms are free, and the rooms you want depend on which times work. Today, the user has to manually cross-reference room schedules — checking each room's availability mentally or by filtering the grid — which is slow and error-prone.

### Who Needs This
University department administrators building a semester timetable. They are scheduling dozens of sections and need to find room+time combinations quickly while balancing many soft constraints (instructor preferences, time-of-day, building proximity, etc.) that only they can weigh.

### What "Done" Looks Like
The user is partway through creating a section (course and section code are set). They enter a **room browsing mode** where:

1. They specify a **meeting pattern** (e.g., MWF, TR) — but can change it during browsing.
2. They optionally **filter rooms** by building, campus, room type, capacity, or other criteria via a quick dropdown/toolbar.
3. The system computes all feasible **solutions** — where a solution is a complete set of meetings (one per day in the pattern), each with a room and time, that don't conflict with existing sections.
4. Solutions are **ranked** by coherence:
   - **Tier 1 (ideal)**: All days → same room, same start time
   - **Tier 2**: Same room but different times across days, or same time but different rooms
   - **Tier 3**: Different rooms and different times across days
   - Within each tier, secondary sort by room name, then start time (earliest first)
5. The user **steps through solutions** (prev/next) on the schedule grid. Each solution is shown as **ghost blocks** (semi-transparent highlighted tiles) overlaid on the existing schedule, so the user sees context — other sections, potential instructor overlaps, time-of-day positioning.
6. When the user finds a solution that works, they **accept** it. This populates the section's meeting times AND room assignments, and the user continues editing other fields.

### Key Design Principles
- **Informational, not prescriptive**: The tool shows options; it does not recommend. The user's judgment drives the choice.
- **Overlay, not separate view**: Solutions appear on the real schedule grid so the user sees the full picture.
- **Ranked, not filtered**: Show all valid options in a meaningful order rather than hiding "worse" ones. The user decides what trade-offs matter.
- **Flexible entry**: The user can change the meeting pattern mid-browse. They can broaden or narrow room filters at any time. The solution list recomputes.

### What This Feature Does NOT Do
- Auto-schedule sections
- Detect or resolve instructor time conflicts (the user sees those visually)
- Consider enrollment capacity in the initial version (designed to be extensible to this)
- Handle non-standard frequencies (odd/even weeks, custom week lists) — solutions assume weekly meetings

### Existing Model Context

**Rooms** have: Building, RoomNumber, Capacity, CampusId, RoomTypeId, Features, Notes.

**Meeting slots** (`SectionDaySchedule`) have: Day (1–5), StartMinutes, DurationMinutes, RoomId, MeetingTypeId.

**Room availability** = no other section in the active semester(s) occupies that room at that time. Each `SectionDaySchedule` with a non-null `RoomId` constitutes a booking.

**Legal start times**: The system already has a matrix of valid (start time, duration) pairs per academic year. Solutions should respect this matrix — only propose start times that are legal.

### Decisions Made
| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Overlay on existing grid, not separate view | User needs full context to make judgment calls |
| D2 | Solutions are ranked (ideal → loose), user steps through sequentially | Parallel comparison would be visually overwhelming |
| D3 | Meeting pattern is flexible during browsing | User may switch MWF↔TR if options are scarce |
| D4 | Room filtering via dropdown controls (building, type, campus, capacity) | Avoids requiring upfront room-type setup; user steers the search |
| D5 | Accepting a solution populates both meeting times and room assignments | Keeps the interaction atomic — one accept, done |
| D6 | Start with "room not booked" as availability; design for capacity/type extensibility | Gets core value fast; capacity is a filter, not a constraint |

### Open Questions (resolved in Phase 2)
| Q | Resolution |
|---|------------|
| Q1 | User picks a block-length structure. Combined with legal start time matrix, the app knows valid start times. Templates auto-derived from `BlockPattern × LegalStartTime`. |
| Q2 | Silently show Tier 2/3. Tier label on each solution communicates quality. |
| Q3 | Ghost tiles show room name + time range. |
| Q4 | Button in meetings area → slides out a panel below meetings. |
| Q5 | Augment mode — respect existing meetings, fill gaps only. |
| Q6 | **Deferred.** Feature disabled when `IsMultiSemesterMode`. Too complex for v1. |

---

## Phase 2: Domain Model

### Meeting Pattern Templates
Auto-derived from `BlockPattern × LegalStartTime` cross product — no new admin setup. Templates have **per-day specs** because the user can customize durations per day (e.g. MW=90min, F=50min).

```
record TemplateDaySpec(Day, BlockLengthHours, DurationMinutes, LegalStartTimes[])
record MeetingTemplate(PatternId, PatternName, DaySpecs[])
```

**Default generation**: For each `(BlockPattern, LegalStartTime)` pair, create a template where all days share the same duration. Display label: `"MWF 50min"`, `"TR 75min"`.

**Per-day editing**: User can change any day's duration in the browser panel. Changing a day's duration re-fetches legal start times for the new block length and recomputes solutions.

In augment mode, gap days = template days − days with existing meetings.

### Solution Model
```
record SolutionSlot(Day, StartMinutes, DurationMinutes, RoomId, RoomLabel)
record RoomSolution(Slots[], Tier, TierLabel)

enum SolutionTier:
  Tier1_SameRoomSameTime      — all days → same room, same start
  Tier2a_SameRoomDiffTimes    — all days → same room, different starts
  Tier2b_SameTimeDiffRooms    — all days → same start, different rooms
  Tier3_Mixed                 — neither dimension consistent
```
Tier classification considers the full schedule (existing + new slots). "Same time" = same start time even when durations differ. Sort: tier → room label → earliest start.

### Occupancy Index
```
Dictionary<(roomId, day), List<(start, end)>> bookings
```
Built from all `SectionDaySchedule` + `Meeting.Schedule` entries with non-null `RoomId` for the active semester. Excludes the section being edited.

### Solution Generation
Each gap day may have a different duration and different legal start times.

1. **Tier 1 scan**: For each room, find start times legal on ALL gap days AND where the room is free on all gap days at that time (each day's own duration).
2. **Tier 2a**: For each room, find any legal start time per gap day → same room, different times.
3. **Tier 2b**: For each start time legal on ALL gap days, find any room per gap day → same time, different rooms.
4. **Tier 3**: Bounded greedy (3–5 representative solutions with different room-ordering seeds).

De-duplicate by `(day, roomId, startMinutes)` tuple sets.

### Room Filter Model
Four quick-filter controls: Campus, Building, Room Type, Min Capacity. Any change triggers recomputation. In-memory `RoomFilter` observable object.

### Ghost Tile Integration
`GhostBlock` is a subtype of `GridBlock` but does **not** flow through the grid pipeline. Ghost tiles render as a **lightweight overlay** on the canvas, independent of the main `Render()` pass:

- `ScheduleGridViewModel.SetGhostBlocks()` stores the blocks and fires `PropertyChanged("GhostBlocks")` — no `Reload()`.
- `ScheduleGridView.RenderGhostOverlay()` adds/removes ghost `Border` controls on the canvas using layout parameters cached from the last full `Render()`. This makes stepping through solutions near-instant.
- `Render()` calls `RenderGhostOverlay()` at the end so ghosts survive a full re-render triggered by other causes (resize, filter change, etc.).

Ghost appearance: semi-transparent green fill (`#3C4CAF50`), green border, room label + time label text.

`RoomAvailabilityBrowserViewModel` converts current `RoomSolution` to `GhostBlock` list, pushes via callback chain: `BrowserVM → SectionListVM._setGhostBlocks → MainWindowVM → ScheduleGridVM.SetGhostBlocks()`. Accept → populate section meetings + clear ghosts. Cancel → clear ghosts.

**Multi-semester guard**: "Browse Rooms" button hidden when `IsMultiSemesterMode`.

### New Files
- `Models/MeetingTemplate.cs` — `MeetingTemplate`, `TemplateDaySpec`
- `Models/RoomSolution.cs` — `RoomSolution`, `SolutionSlot`, `SolutionTier`
- `Services/RoomAvailabilityService.cs` — `OccupancyIndex` + solution generation
- `ViewModels/Management/RoomAvailabilityBrowserViewModel.cs` — browser panel VM
- `Views/Management/RoomAvailabilityBrowserView.axaml` + `.cs` — browser panel view
- `Tests/RoomAvailabilityTests.cs` — 30 unit tests (OccupancyIndex, Classify, templates, solutions, filters)

### Modified Files
- `GridData.cs` — `GhostBlock` record (no `IsGhost` on `TileEntry` — ghosts bypass pipeline)
- `ScheduleGridViewModel.cs` — `GhostBlocks` read-only property, `SetGhostBlocks()` fires property change
- `ScheduleGridView.axaml.cs` — cached layout fields, `RenderGhostOverlay()` method, ghost brushes
- `SectionEditViewModel.cs` — `RoomBrowserVm` property, `OpenRoomBrowser`/`CloseRoomBrowser` commands, `AcceptBrowserSolution`
- `SectionListViewModel.cs` — `_setGhostBlocks` callback, `CreateRoomBrowser` delegate wiring in `CreateEditVm()`
- `MainWindowViewModel.cs` — wires `_setGhostBlocks` callback to `ScheduleGridVM.SetGhostBlocks()`
- `SectionListView.axaml` — "Browse" button, `ContentControl` for browser panel below meetings

### Known Rough Edges
- `ScanTier2a`: `FirstOrDefault` returns 0 for `int` — could false-positive if midnight (0) is a legal start time
- Room filter UI: building + capacity only; campus/roomType dropdowns not wired to real data yet
