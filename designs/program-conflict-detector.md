# Program Conflict Detector — Progressive Design Spec

## Phase 1: Problem Statement

### The problem

Room and instructor conflicts are already detected: if two sections share a room or instructor and overlap in time, the app nudges the user. But the other major timetabling concern — **student access** — has no detection.

A student enrolled in a program needs to take specific courses. If sections of two required courses overlap in time, some students may be unable to build a workable schedule. Today, the user has to spot these overlaps manually.

### What the feature does

The user tags courses/sections with a label representing a program (e.g. "CS-Core"). They then enroll that tag in the program-conflict detector. Once enrolled, the application flags any two sections that:

1. Belong to **different courses** within the same enrolled tag, and
2. **Overlap in time**

The indication is mild — a nudge, not a blocker. The user may have good reasons for the overlap and can simply ignore it. There is no mute/dismiss mechanism in this iteration.

### What "done" looks like

- The user can enroll an existing tag in the program-conflict detector
- When two sections from different courses share an enrolled tag and overlap in time, a visual indication appears
- The indication is consistent in tone with existing room/instructor conflict nudges
- No new entities beyond the enrollment flag — programs are just tags
- No combinatorial analysis, slate-building, or schedule-feasibility checks

### Out of scope (this iteration)

- Muting or dismissing individual conflicts
- "All sections of C1 conflict with all sections of C2" aggregation
- Feasible-slate enumeration or counting
- Any notion of required vs. elective courses within a program

---

## Phase 2: Domain Model

### Entities

**Watch** (new)
- The core new entity. Represents a monitored group of courses whose sections should be checked for time overlaps.
- **Name**: auto-generated from the tag(s) or course list on creation; user-editable.
- **Scope**: per-semester. A watch belongs to a single semester.
- **Enabled**: boolean on/off toggle. Disabled watches are retained but produce no conflict indications.
- **Definition**: one of two modes:
  - *Tag-based*: one or more tags. A section is included if it carries **all** specified tags (AND logic). Example: tags "upper-level" + "BSc" matches only sections tagged with both.
  - *Course-based*: an explicit list of courses.

**Tag** (existing)
- Already in the system. Watches reference tags by identity, not by snapshot — if a section gains or loses a tag, the watch's coverage updates automatically.

**Course** (existing)
- Already in the system. In course-based watches, courses are referenced directly.

**Section** (existing)
- Belongs to a course. Has meeting times (day + time span). A section may appear in multiple watches (via multiple tags or explicit inclusion).

**Meeting** (existing)
- The day/time component of a section. Overlap detection operates at this level.

### Relationships

- A **Watch** → references one or more **Tags** (tag-based mode) or one or more **Courses** (course-based mode)
- A **Watch** → belongs to one **Semester**
- A **Section** → belongs to one **Course**, carries zero or more **Tags**, has one or more **Meetings**
- A **Section** can be covered by multiple watches (no exclusivity)

### Conflict (computed, not stored)

A program conflict is not a persisted entity. It is computed on the fly whenever the grid renders. A conflict exists when:

1. Two sections belong to **different courses**
2. Both sections are covered by the **same enabled watch**
3. At least one meeting of each section **overlaps in time** (same day, overlapping time span)

A single pair of sections may produce conflicts under multiple watches — each watch's indication is independent.

### Invariants

- A watch must reference at least one tag or one course (cannot be empty)
- Tag-based watches use AND logic: a section must carry *all* specified tags to be included
- Conflicts are only between sections of *different* courses — two sections of the same course sharing a time slot is not a program conflict (that's just multiple offerings)
- Disabling a watch suppresses all its conflict indications without deleting the watch or its definition

---

## Phase 3: UX Sketch

### Location: the Access group

The watch list lives in **Schedule View**, in a new toolbar group called **"Access"** — alongside the existing Filter and Overlay groups. This group is the future home for tools that help the user assess student access to the schedule. For now, its only content is the program-conflict watch list.

### Watch list control

The watch list is a **pulldown** (collapsed by default) within the Access group. When collapsed, it shows a **summary badge** — a compact text indicator of the current state, e.g. "2 watches, 3 conflicts" or "No active watches." This gives the user at-a-glance awareness without opening the pulldown.

When expanded, the pulldown shows:

- A list of all watches for the current semester
- Each watch displays:
  - Its **name** (auto-generated, editable)
  - Its **definition** (tag list or course list, shown compactly)
  - An **on/off toggle**
  - A **conflict count** for that watch (when enabled)
  - A **delete** action
- A button/link to **create a new watch**

### Watch creation flow

1. The user clicks "New Watch" in the pulldown
2. Chooses **mode**: tag-based or course-based
3. **Tag-based**: a dropdown/combobox to select tags (multi-select; AND logic). As tags are selected, a name is auto-generated (e.g. "upper-level + BSc")
4. **Course-based**: a multi-select of courses in the current semester. Name auto-generated from the course list (e.g. "BIOL101, CHEM101, PHYS101")
5. The user can **edit the name** to something more meaningful (e.g. "BSc Year 1 Core")
6. The watch is created in the **enabled** state

### Schedule Grid indication

Grid-only — no indication in the Section List view.

Because the schedule grid is canvas-drawn, the visual indication can be precise and spatial. Two cases arise naturally from the geometry of conflicting meetings:

**Same card** (co-scheduled meetings from different courses in the same watch):
- Sort the conflicting entries adjacent within the card's content list
- Draw a colored box around the conflicting pair

**Adjacent cards** (overlapping but not co-scheduled):
- Draw a colored box around each conflicting meeting
- Draw a connecting line between the two boxes, making the relationship explicit

### Color differentiation

The program-conflict indicator uses a **distinct hue** from room/instructor conflict indicators. Program conflicts represent a softer concern (student access) vs. a hard physical impossibility (double-booked room), and the color should reflect that — something calm and informational rather than alarming.

### Non-interactions

- No indication in the Section List view
- No click-through from the summary badge to specific conflicts on the grid (this iteration)
- No mute/dismiss on individual conflicts
- No tooltip or detail popup on the grid indication

---

## Phase 4: Data Design

### Table: `ProgramWatches`

```sql
CREATE TABLE IF NOT EXISTS ProgramWatches (
    id          TEXT PRIMARY KEY,
    semester_id TEXT NOT NULL,
    name        TEXT,
    mode        TEXT,
    data        TEXT NOT NULL DEFAULT '{}'
);
```

Human-readable columns: `name` (watch name), `mode` ("tag" or "course") for quick browsing of the raw SQLite file.

### JSON shape (`data` column)

```json
{
  "Name": "BSc Year 1 Core",
  "Mode": "tag",
  "IsEnabled": true,
  "TagIds": ["guid-1", "guid-2"],
  "CourseIds": []
}
```

- **`Mode`**: `"tag"` or `"course"`. Determines which ID list is active.
- **`TagIds`**: populated in tag-based mode. References `SchedulingEnvironmentValues` rows where `type = 'tag'`. AND logic: a section must carry all listed tags.
- **`CourseIds`**: populated in course-based mode. References `Courses` rows by ID.
- Only one of `TagIds`/`CourseIds` is meaningful per watch; the other is empty.
- **`IsEnabled`**: the on/off toggle state.

### Model class: `ProgramWatch`

```
ProgramWatch
  Id          : string
  Name        : string
  Mode        : ProgramWatchMode   (enum: Tag, Course)
  IsEnabled   : bool
  TagIds      : List<string>
  CourseIds   : List<string>
```

### Repository: `ProgramWatchRepository`

Follows the existing repository pattern (see `InstructorRepository`, `CourseRepository`).

| Method | Description |
|--------|-------------|
| `GetAllAsync(semesterId)` | All watches for a semester |
| `SaveAsync(semesterId, watch)` | Upsert a watch (insert or update by ID) |
| `DeleteAsync(id)` | Remove a watch |

No query for conflicts — conflict detection is computed in the view layer from the watch definitions, sections, and their meeting times. The repository is pure CRUD.

### Conflict computation (not persisted)

Conflict detection is a **read-only computation** performed on the fly. Inputs:

1. All enabled `ProgramWatch` entries for the current semester
2. The **filtered** sections — only sections that pass the current grid filters (not all sections in the semester)
3. Meeting times on those sections

For each enabled watch:
1. Resolve the **covered sections**: all sections that carry all of the watch's tags on the **section itself** (AND logic), or whose course is in the watch's course list
2. Group covered sections by course
3. For each pair of sections from **different courses**, check if any of their meetings overlap on the same day
4. Emit a conflict record (not persisted — just a data structure for the grid renderer)

This runs whenever the grid re-renders or a watch is toggled/modified. The section and meeting data is already in memory from the grid pipeline, so the additional cost is the pairwise time-overlap check across watched sections.

---

## Phase 5: Architecture

### Overview

Three new classes, one new grid pipeline step, and a new AXAML group in the toolbar.

### New classes

**`ProgramWatchRepository`** (Data/Repositories)
- Singleton, registered in DI with interface `IProgramWatchRepository`
- Pure CRUD against the `ProgramWatches` table
- Follows the same pattern as `InstructorRepository`, `CourseRepository`

**`ProgramConflictService`** (Services)
- **Static class** — pure computation, no state, no DI registration
- Follows the pattern of `RoomConflictService` / `InstructorConflictService`
- Single method: `DetectConflicts(enabledWatches, sections, tagIdsBySectionId)` → returns conflict data keyed by section meeting identity (for the grid renderer to consume)
- Algorithm: for each enabled watch, resolve covered sections (by section-level tag match or course list), bucket by `(day)`, pairwise overlap check across different courses
- Returns a structure the renderer can use to draw boxes and connecting lines — needs to identify which specific meetings conflict, not just which sections

**`AccessPanelViewModel`** (ViewModels/GridView)
- The VM for the Access group in the toolbar
- Owns the watch list: loads watches from `ProgramWatchRepository` on semester change, exposes them as an observable collection of `ProgramWatchItemViewModel` wrappers
- Handles: create watch, delete watch, toggle enabled, edit name
- Holds computed **conflict summary** (total count across enabled watches) for the collapsed badge text
- Triggers grid reload (via `GridChangeNotifier`) when a watch is created, deleted, toggled, or modified
- Registered in DI as a singleton, constructed with `IProgramWatchRepository`, `SemesterContext`, `SectionStore`, `GridChangeNotifier`

**`ProgramWatchItemViewModel`** (ViewModels/GridView)
- Wraps a single `ProgramWatch` for the watch list UI
- Exposes: `Name` (editable), `IsEnabled` (toggle), `ModeSummary` (compact display of tags or courses), `ConflictCount`
- Writes changes back through `AccessPanelViewModel` → repository

### Grid pipeline integration

A new step is inserted into `ScheduleGridViewModel.ReloadCore()` after **BuildFilteredBlocks** (step 6) and before **DeduplicateBlocks** (step 10):

**Step 6.5: ComputeProgramConflicts**
1. Read enabled watches from `AccessPanelViewModel` (already loaded)
2. Call `ProgramConflictService.DetectConflicts(...)` with the **filtered** sections (output of step 6, BuildFilteredBlocks) and their tag IDs — conflicts are only detected among sections that pass the current grid filters
3. Attach conflict data to `GridData` as a separate structure (e.g. `GridData.ProgramConflicts`)

The renderer in `ScheduleGridView.axaml.cs` consumes `GridData.ProgramConflicts` during the paint pass to draw colored boxes around conflicting meetings and connecting lines between adjacent cards.

### Conflict data structure for the renderer

```
ProgramConflict
  WatchId       : string
  WatchName     : string
  MeetingA      : (SectionId, Day, Start, End)
  MeetingB      : (SectionId, Day, Start, End)
```

The renderer receives a `List<ProgramConflict>`. For each conflict, it locates the two meetings on the canvas:
- **Same tile**: sort the entries adjacent, draw a colored box around the pair
- **Different tiles, same day column**: draw a box around each, connect with a line

### Toolbar AXAML

In `GridFilterView.axaml`, a new `<GroupBox>` is added after the Overlay GroupBox:

```
GroupBox "Access"
  └─ WrapPanel
      └─ Panel
          ├─ ToggleButton (collapsed summary: "2 watches, 3 conflicts")
          └─ Popup (expanded watch list)
              ├─ List of ProgramWatchItemViewModel rows
              │   └─ each: name, mode summary, toggle, conflict count, delete
              └─ "New Watch" button → creation flow
```

The `DataContext` for this group binds to `AccessPanelViewModel`, which is exposed as a property on `ScheduleGridViewModel` (like `Filter` exposes `GridFilterViewModel`).

### Semester change flow

`AccessPanelViewModel` subscribes to `SemesterContext.PropertyChanged` (Pattern A). On semester change, it reloads watches from the repository for the new semester. This reload fires `GridChangeNotifier`, which triggers the grid pipeline to re-run including the conflict detection step.

### DI registration

In `App.ConfigureServices()`:
- `IProgramWatchRepository` → `ProgramWatchRepository` (singleton)
- `AccessPanelViewModel` (singleton, factory lambda with constructor dependencies)

In `App.ConfigureDemoServices()`:
- Same registrations (watches work identically in the WASM demo)

### What stays unchanged

- `RoomConflictService` and `InstructorConflictService` — untouched, they continue operating in the Section List view only
- `GridFilterViewModel` — untouched, the Access group is a sibling, not a child
- `SectionListView` / `SectionListViewModel` — no program conflict indication here
- The existing `GridBlock` hierarchy — no new subtype needed; conflict data is carried separately in `GridData`

---

## Phase 6: Implementation Plan

Six tasks in dependency order. Each is one session's work.

### Task 1: Data layer

**Goal:** Persist watches in SQLite.

- `ProgramWatch` model class + `ProgramWatchMode` enum
- `ProgramWatches` table creation in `DatabaseContext.InitializeSchema()`
- `IProgramWatchRepository` interface + `ProgramWatchRepository` implementation (GetAll, Save, Delete)
- DI registration in both `ConfigureServices()` and `ConfigureDemoServices()`
- Unit tests for repository CRUD

**Verify:** Tests pass; table created on app startup.

### Task 2: Conflict detection service

**Goal:** Pure computation of program conflicts.

- `ProgramConflict` data structure (WatchId, WatchName, MeetingA, MeetingB)
- `ProgramConflictService` static class with `DetectConflicts(enabledWatches, sections, tagIdsBySectionId)`
- Algorithm: resolve covered sections per watch → bucket by day → pairwise overlap check across different courses
- Unit tests with synthetic sections and watches covering same-card and adjacent-card scenarios

**Verify:** Tests pass for tag-based watches, course-based watches, multi-tag AND logic, no false positives for same-course sections.

### Task 3: AccessPanelViewModel

**Goal:** VM layer for the watch list — CRUD, toggle, conflict summary.

- `AccessPanelViewModel`: loads watches from repository on semester change, exposes `ObservableCollection<ProgramWatchItemViewModel>`, computes conflict summary text
- `ProgramWatchItemViewModel`: wraps a single watch with bindable Name, IsEnabled, ModeSummary, ConflictCount
- Wire to `SemesterContext.PropertyChanged` for semester changes
- Wire to `GridChangeNotifier` to trigger grid reload on watch changes
- Expose as a property on `ScheduleGridViewModel` (like `Filter`)
- DI registration

**Verify:** Compile check passes; VM loads/saves watches through the repository.

### Task 4: Access group AXAML — watch list display

**Goal:** The Access toolbar group with the watch list pulldown.

- New `<GroupBox>` in `GridFilterView.axaml` after the Overlay group
- Collapsed state: `<ToggleButton>` showing summary badge text
- Expanded state: `<Popup>` with a list of watches — each row shows name, mode summary, on/off toggle, conflict count, delete button
- Bind to `AccessPanelViewModel` via `ScheduleGridViewModel.Access`

**Verify:** App runs; Access group appears in toolbar; existing watches (if any) display; toggle and delete work.

### Task 5: Watch creation flow

**Goal:** UI for creating new tag-based and course-based watches.

- "New Watch" button in the popup opens an inline creation form
- Mode selector (tag-based / course-based)
- Tag-based: multi-select dropdown populated from existing tags
- Course-based: multi-select of courses in the current semester
- Auto-generated name from selections; editable name field
- Save creates the watch in enabled state, closes the form, triggers grid reload

**Verify:** App runs; can create both tag-based and course-based watches; name auto-generates; watch appears in the list after creation.

### Task 6: Grid pipeline + renderer

**Goal:** Conflict indicators on the schedule grid canvas.

- New pipeline step 6.5 in `ScheduleGridViewModel.ReloadCore()`: call `ProgramConflictService.DetectConflicts()` on filtered sections, attach results to `GridData.ProgramConflicts`
- In `ScheduleGridView.axaml.cs` renderer:
  - Same-tile conflicts: sort conflicting `TileEntry` items adjacent, draw colored box around the pair
  - Cross-tile conflicts (same day column): draw colored box around each meeting, draw connecting line between them
- Choose the program-conflict hue (distinct from room/instructor indicators — calm, informational)

**Verify:** App runs; create a watch with known overlapping sections; colored boxes and connecting lines appear on the grid; toggling the watch off removes the indicators; filtering out a conflicting section removes its indicator.
