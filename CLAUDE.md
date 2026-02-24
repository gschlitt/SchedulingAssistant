# SchedulingAssistant — Project Decisions

## What This App Is
A scheduling **visualization and information management tool** for university administrators. It does not auto-schedule — it helps administrators see and manage how course sections fit together across a week.

## Platform & UI
- **Framework**: Avalonia UI (C# / .NET)
- **Target platforms**: Windows and macOS
- **Distribution**: Self-contained executable — no .NET runtime or other infrastructure required for end users

## Data Storage
- **Database**: SQLite (single local file on the user's drive)
- **Schema philosophy**: Treat SQLite as a document store to avoid schema migrations during development
- **Column pattern**: Tables use a stable set of columns for keys/partitioning, with a `data JSON` column for the entity's fields
  - Example: `key TEXT, semester TEXT, data JSON`
- **Library**: `Microsoft.Data.Sqlite`

## Data Entities
- **Section** — the core entity. Standalone, not part of a course catalog. Fields include course code, section code, instructor, room, time slot, semester, and others TBD.
- **Instructor** — faculty who teach sections
- **Room** — physical locations where sections meet
- **Semester** — each semester is a separate schedule (e.g. Fall 2025, Spring 2026)
- **Legal Start Time** — a configurable list of valid begin times shown in the section editor (e.g. 8:00 AM, 8:30 AM, 9:00 AM...). Seeded with defaults, editable by administrators.

## Data Entry
- Manual entry via forms (text fields, comboboxes, etc.)
- No drag-and-drop scheduling

## Visualization
- **Primary view**: Weekly calendar grid (Mon–Fri, time of day on vertical axis)
- Sections appear as blocks on the grid, showing: course code, section code, instructor initials (others TBD)
- Extensive filtering by section attributes (instructor, room, department, etc.)
- No conflict detection required

## Schedule Grid Design Principles
- **Conserve vertical real estate**: pack as much information as possible onto a single line within each tile. Course code, section code, and instructor initials are all written on one line (e.g. "HIST101 A  JRS"). Never use a separate line for initials if it can be avoided.
- Co-scheduled sections (identical start time and duration) share one tile, with entries stacked vertically and separated by a thin rule.
- Overlapping sections (different time spans) appear side-by-side in the same day column.

## Section List Design Principles
- **Conserve vertical space**: the list panel is narrow and tall; minimize unnecessary padding, margins, and font sizes wherever possible
- The section list is an always-visible left panel, not a flyout or modal
- Editing a section expands its card inline within the list — no separate window
- Adding a new section shows a form at the top of the list
- Controls inside the expanded editor are compact (FontSize 10–11, tight padding) to conserve vertical space
- The summary row (heading + schedule lines) remains visible above the expanded form
- Only one section can be open for editing at a time; opening another collapses the previous

## Section Constraints
- A section requires, at minimum, a course and a non-empty section code.
- Within a semester, no two sections of the same course may share the same section code (case-insensitive). Enforced in `SectionRepository.ExistsBySectionCode`.

## Section Editor Step-Gate
The inline section editor enforces a strict two-step unlock sequence:

1. **Course must be selected first.** The Section Code field is disabled (`IsSectionCodeEnabled = false`) until a course is chosen.
2. **Section code must be entered and validated.** All other fields are disabled (`AreOtherFieldsEnabled = false`) until the Section Code field loses focus and `CommitSectionCode()` passes the uniqueness check.

### Implementation: validated snapshot pattern
`AreOtherFieldsEnabled` is a **purely computed property** — it has no mutable flag. It returns true when all of these hold simultaneously:
- `SelectedCourseId` is non-empty
- `SectionCode.Trim()` is non-empty
- `SectionCodeError` is null
- `_validatedCourseId == SelectedCourseId` (snapshot match)
- `_validatedSectionCode == SectionCode.Trim()` (case-insensitive snapshot match)

`_validatedCourseId` and `_validatedSectionCode` are set only by `CommitSectionCode()` (on success) or by the constructor (for sections that already have both fields populated). Any change to course or code clears `SectionCodeError` and triggers re-evaluation; since the live values no longer match the snapshot, `AreOtherFieldsEnabled` immediately becomes false without needing to mutate any flag.

This design is immune to Avalonia's binding lifecycle, which re-fires `SelectedValue` setters after `ItemsSource` loads. Those re-fires cannot corrupt the enabled state because they don't change the snapshot — they just re-evaluate the already-correct computed property.

### LostFocus wiring
The Section Code TextBox lives inside a `DataTemplate`, so it cannot be reached directly. `SectionListView.axaml.cs` registers a bubbled `LostFocusEvent` handler on the `UserControl` itself and forwards it to `EditVm.CommitSectionCode()` when the source control's name is `"SectionCodeBox"`.

### Copy operation
"Copy" adds a new section immediately below the selected one in the list, pre-setting its course and (if derivable) its section code. The code derivation increments the trailing integer suffix of the source section code (e.g. "AB1" → "AB2"). If the candidate code is already taken, a notice is shown and the code is left blank. The editor opens in the same step-gate mode; if both course and code are pre-populated, the snapshot is set at construction so all fields are immediately editable.

## Decisions Not Yet Made
- Exact fields on Section, Instructor, Room (beyond what's described above)
