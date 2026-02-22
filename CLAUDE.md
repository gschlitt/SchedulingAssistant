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

## Decisions Not Yet Made
- Exact fields on Section, Instructor, Room (beyond what's described above)
