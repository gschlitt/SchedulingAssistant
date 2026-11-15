# CSV Import Facility — Progressive Design Spec

## Phase 1: Problem Statement

**Status:** LOCKED

### Who needs this and why

When onboarding a new customer, the vendor (Greg) scrapes the department's
public course-listing website to pre-populate their TermPoint database.
This saves the customer hours of manual data entry for instructors, courses,
and sections.

### Workflow

1. **Customer** creates a new database via the startup wizard and configures
   their scheduling environment (campuses, section types, legal start times,
   meeting types, etc.). This is faster than describing the config to Greg.
2. **Customer** emails the `.db` file to Greg.
3. **Greg** scrapes the department's website for one or more recent semesters
   (e.g., UBC Chemistry Fall 2026) and produces three CSV files per semester:
   - `instructors.csv` — faculty teaching that semester
   - `courses.csv` — courses offered that semester
   - `sections.csv` — the section list, in the same rich format as TermPoint's
     backup CSV (18 columns), with blank columns for fields that weren't
     available from scraping
4. **Greg** opens the customer's database in TermPoint and imports the three
   files **in order**: instructors, then courses, then sections.
5. **Greg** ships the populated database back to the customer.

### Key design requirements

**Three-file, ordered import.** Instructors and courses must exist before
sections can reference them. The UI enforces or at least guides this order.

**Rich section format.** The section CSV uses the same column set as the
existing backup CSV:

```
AcademicYear, Semester,
CourseCode, CourseTitle,
SectionCode, Instructors,
SectionType, Campus, Tags, Resources, Reserves,
Day, StartTime, EndTime, DurationMin,
Room, Frequency, MeetingType
```

Blank columns are silently ignored. Populated columns are matched against
the scheduling environment already configured in the database.

**Instructor CSV format.** Minimal — the fields that scraping can
realistically produce:

```
LastName, FirstName, Initials, Email
```

Initials and Email may be blank. FirstName may be blank (some listings show
only last names or full display names).

**Course CSV format.** Minimal:

```
SubjectCode, CalendarCode, Title
```

CalendarCode is the human-readable code (e.g., "CHEM 101"). Title is the
course name. SubjectCode (e.g., "CHEM") maps the course to a pre-created
Subject in the database. Subjects must be set up before import — the
importer does not create them. If SubjectCode doesn't match an existing
Subject, the row is flagged as a warning (course still imported, subject
left unlinked).

### Two categories of matching

The import distinguishes between two fundamentally different matching
scenarios based on what's already in the database:

**A. Imported entities (instructors, courses, sections)** — the database
starts empty for these. On the *first* import there is nothing to match
against; they are simply created. On *subsequent* semester imports, the
importer must match incoming instructors and courses against previously
imported records:

- **Courses** match on `CalendarCode` (case-insensitive, whitespace-
  normalized). Highly reliable — calendar codes are stable identifiers.
- **Instructors** match on name, with some fuzziness:
  - Exact: `LastName + FirstName` (case-insensitive)
  - Fuzzy: handle "J. Smith" vs "John Smith", "MacDonald" vs "Macdonald",
    missing first names, etc.
  - When ambiguous, flag for manual resolution rather than guessing.

**B. Pre-configured entities (rooms, buildings, section types, campuses,
meeting types)** — these *may* already exist in the database from the
customer's scheduling-environment setup. The CSV may contain values for
these fields (e.g., room "MAC310", section type "Lecture").

For these, the import includes a **mapping confirmation step** before
writing any data:

1. Scan the CSV for distinct values in environment-linked columns
   (Room, SectionType, Campus, MeetingType).
2. Compare each against the corresponding list in the database
   (case-insensitive).
3. Present a mapping table to the operator:
   - **Exact matches** auto-fill (e.g., CSV "Lecture" → DB "Lecture").
   - **Ambiguous or unmatched values** require manual selection from
     the DB list, or can be left unmapped (the field is left blank on
     the imported section).
4. The operator can correspond with the customer to resolve ambiguities
   before finalizing the mapping.

This mapping step is the heart of the import UX for sections — it's where
scraped data meets the customer's configured environment.

**Sections reference instructors and courses by display name.** The section
CSV uses "FirstName LastName" for instructors (semicolon-separated if
multiple) and CalendarCode for courses — the same format as the backup CSV.
The import resolves these to the GUID-based records in the database.

**Operator is the vendor (Greg), not the customer.** The UI can be technical.
No wizard needed — a file picker, a progress/mapping log, and an
error/warning report are sufficient.

### What "done" looks like

Greg can take a freshly configured customer database, import 3 CSVs, and
have a populated semester with correct cross-references. A second import
for another semester adds new instructors/courses and reuses existing matches.
Ambiguous matches are surfaced, not silently guessed.

### Out of scope (for now)

- Drag-and-drop or auto-detection of file type
- Undo/rollback of a completed import
- Creating new scheduling-environment entries during import (rooms, section
  types, subjects, etc. must be pre-configured by the customer)
- Any scraping logic — that's a separate, external process

---

## Phase 2: Domain Model

**Status:** LOCKED

The import facility does not introduce new persistent entity types. It writes
standard Instructors, Courses, and Sections. The domain model describes the
**transient structures** that govern parsing, matching, and writing.

### Parsed row types

These are the intermediate representations of CSV rows, before they become
database entities. They carry raw string values exactly as they appear in
the CSV.

**InstructorRow**
```
LastName      : string (required)
FirstName     : string (may be blank)
Initials      : string (may be blank)
Email         : string (may be blank)
```
Invariant: `LastName` must be non-empty after trimming.

**CourseRow**
```
SubjectCode   : string (may be blank)
CalendarCode  : string (required)
Title         : string (may be blank)
```
Invariant: `CalendarCode` must be non-empty after trimming.

**SectionRow** (one per section, not per CSV line)
```
AcademicYear  : string
Semester      : string (required — must resolve to a Semester in the DB)
CourseCode    : string (required — must match an Instructor/Course)
CourseTitle   : string
SectionCode   : string (required)
Instructors   : string (semicolon-separated "FirstName LastName" pairs)
SectionType   : string
Campus        : string
Tags          : string (semicolon-separated)
Resources     : string (semicolon-separated)
Reserves      : string (semicolon-separated "Name (Code)" pairs)
Meetings      : List<MeetingRow>
```

**MeetingRow** (one per scheduled meeting within a SectionRow)
```
Day           : string (e.g., "Monday")
StartTime     : string (e.g., "8:00 AM")
EndTime       : string (e.g., "9:30 AM")
DurationMin   : string (e.g., "90")
Room          : string (e.g., "Science 101")
Frequency     : string (e.g., "odd", "1,6,7", or blank)
MeetingType   : string
```

The backup CSV format encodes multi-meeting sections as continuation rows
(section-level columns blank). The parser must **group** continuation rows
with their parent section row to produce one SectionRow with multiple
MeetingRows.

### Matching and mapping

#### Match result

Every value that must be resolved against the database produces a match
result:

```
MatchResult<T>
  Status     : Exact | Ambiguous | Unmatched | Skipped
  CsvValue   : string           — the raw value from the CSV
  Resolved   : T?               — the matched DB record (null if unresolved)
  Candidates : List<T>          — populated when Status = Ambiguous
```

`Skipped` means the CSV value was blank — no matching attempted.

#### Environment mapping table

For pre-configured entities (rooms, section types, campuses, meeting types),
the import builds a **mapping table** before writing any data. Each table
maps distinct CSV string values to database record IDs.

```
EnvironmentMappingTable
  Category       : string        — "Room" | "SectionType" | "Campus" | "MeetingType"
  Entries        : List<MappingEntry>

MappingEntry
  CsvValue       : string        — distinct value found in the CSV
  MatchResult    : MatchResult<EnvironmentTarget>
```

`EnvironmentTarget` wraps an ID + display name, since the underlying DB
types differ (Room is its own entity; the others are SchedulingEnvironmentValues).

```
EnvironmentTarget
  Id             : string        — DB record ID (GUID)
  DisplayName    : string        — human-readable label for the mapping UI
```

**How room matching works:** The CSV Room column contains a composite string
`"{Building} {RoomNumber}"` (e.g., "Science 101"). Matching compares this
against `"{Building} {RoomNumber}"` for each Room in the database
(case-insensitive, whitespace-normalized). No parsing into components needed.

**How other environment values match:** SectionType, Campus, and MeetingType
are matched by name against `SchedulingEnvironmentValue.Name`
(case-insensitive).

#### Instructor matching (cross-import)

When the database already contains instructors (second+ semester import),
incoming InstructorRows are matched against existing records:

```
Before matching, names are **normalized**: leading honorifics (Dr., Mr.,
Mrs., Ms., Prof., Professor) are stripped, and the result is trimmed.
This applies to both CSV values and DB records during comparison.

Match tiers (applied in order, first match wins):
1. Exact:  LastName + FirstName both match (case-insensitive)
2. Last-only: LastName matches and CSV FirstName is blank
3. Fuzzy:  LastName matches, FirstName is initial-compatible
           ("J." matches "John", "J" matches "John")
4. Ambiguous: LastName matches multiple records, can't disambiguate
```

When a match is found, the existing DB record is reused (no duplicate
created). When ambiguous, the row is flagged for operator resolution.

#### Course matching (cross-import)

Simpler — `CalendarCode` is the match key:

```
Match tiers:
1. Exact:  CalendarCode matches (case-insensitive, whitespace-normalized)
2. Unmatched: No match — new course will be created
```

No ambiguity tier needed; CalendarCode is a reliable unique identifier.

#### Subject matching (course import)

`SubjectCode` in the course CSV is matched against `Subject.CalendarAbbreviation`
(case-insensitive). If unmatched, a warning is emitted and the course is
imported with no subject link.

#### Semester resolution (section import)

The `Semester` column (e.g., "Fall 2025") must resolve to an existing
Semester record in the database. The `AcademicYear` column (e.g.,
"2025-2026") narrows the search. Both must match — if the semester can't
be resolved, the section row is rejected.

### Import pipeline

The import proceeds in a strict sequence. Each step must complete
successfully before the next begins.

```
1. PARSE         Read CSV → parsed rows (InstructorRow / CourseRow / SectionRow)
                  Reject rows that fail structural validation (missing
                  required fields, unparseable times, etc.)

2. MATCH          For instructor/course CSVs: match against existing DB
                  records (cross-import dedup).
                  For section CSV: build environment mapping tables by
                  scanning distinct values in environment-linked columns.

3. CONFIRM        Present mapping tables and ambiguous matches to operator.
                  Operator resolves ambiguities, confirms or adjusts
                  mappings. This is interactive.

4. WRITE          Create/update DB entities:
                  - Instructors: insert new, skip matched
                  - Courses: insert new, skip matched
                  - Sections: always insert (new GUID), with resolved FKs
                  All writes within a single transaction per file.

5. REPORT         Summary of what was imported, what was skipped, warnings.
```

### Invariants

1. **Import order**: Instructors and courses must be importable before
   sections. The section import depends on instructors and courses
   existing in the database (either pre-existing or just imported).

2. **No duplicates**: Instructor import skips rows that match existing
   records. Course import skips rows whose CalendarCode already exists.
   Section import always creates new sections (no duplicate detection
   for sections — the assumption is each semester's sections are unique).

3. **Referential integrity**: Every section must resolve its CourseCode to
   a Course record and its Instructor names to Instructor records.
   Unresolvable references are warnings (field left blank), not fatal
   errors. Instructor assignments default to workload 1.0 (full).

4. **Environment values are never created**: If a CSV value (SectionType,
   Campus, MeetingType, Room) doesn't match a DB record and the operator
   doesn't map it, the field is left null on the imported section. The
   import never creates SchedulingEnvironmentValues or Rooms.
   Tags, Resources, and Reserves columns are **deferred** — the import
   ignores them for now. They can be added later using the same
   environment-mapping pattern if needed.

5. **Semester must pre-exist**: The import does not create semesters or
   academic years. The section CSV's Semester + AcademicYear columns
   must resolve to an existing semester.

6. **Transaction safety**: Each file's writes are wrapped in a single
   transaction. If writing fails partway through, the entire file's
   import is rolled back.

7. **Section uniqueness**: Within a semester, no two sections of the same
   course may share a section code. The import must check
   `ExistsBySectionCode` before inserting and reject duplicates.

## Phase 3: UX Sketch

**Status:** LOCKED

### Dev Tools menu activation

**Ctrl+Shift+D** toggles a "Dev Tools" menu item in the top menu bar.
Session-only — not persisted. The menu replaces the existing `#if DEBUG`
Debug menu. The `#if DEBUG` / `#if !BROWSER` gates and the
`ShowDebugMenu` AppSetting are retired; the Dev Tools menu exists in all
builds but is hidden until the shortcut fires.

The key binding is registered on MainView (or MainWindow) as an
`InputBinding` / `KeyBinding` that invokes a `ToggleDevToolsCommand` on
MainWindowViewModel. The command flips a `bool IsDevToolsVisible`
property, which the menu binds to via `IsVisible`.

**Menu structure:**
```
Dev Tools
├── CSV Import...          → opens the Import flyout
├── ─────────
├── Debug View...          → existing debug test-data flyout (moved here)
└── Migration: CSV→JSON... → existing migration flyout (moved here)
```

### CSV Import flyout

Opened via the Dev Tools menu. A standard flyout panel (same pattern as
Sharing, Courses, etc.) titled **"CSV Import"**.

The flyout has a vertical layout with three collapsible sections, one per
import type, presented in the required order:

```
┌─────────────────────────────────────────────────┐
│  CSV Import                                     │
│                                                 │
│  Import instructors, courses, and sections from │
│  CSV files. Import in order: instructors first,  │
│  then courses, then sections.                   │
│                                                 │
│  ┌─ 1. Instructors ──────────────────────────┐  │
│  │  [Choose File...]  instructors.csv  ✓ 24  │  │
│  │                                           │  │
│  │  Preview:                                 │  │
│  │  ┌──────────────────────────────────────┐ │  │
│  │  │ Smith, John        (new)             │ │  │
│  │  │ MacDonald, Alice   (exists - match)  │ │  │
│  │  │ Chen, W.           (ambiguous ⚠)     │ │  │
│  │  └──────────────────────────────────────┘ │  │
│  │                                           │  │
│  │  [Import 22 new · 2 matched · 0 skipped]  │  │
│  └───────────────────────────────────────────┘  │
│                                                 │
│  ┌─ 2. Courses ──────────────────────────────┐  │
│  │  [Choose File...]                         │  │
│  │  (import instructors first)               │  │
│  └───────────────────────────────────────────┘  │
│                                                 │
│  ┌─ 3. Sections ─────────────────────────────┐  │
│  │  [Choose File...]                         │  │
│  │  (import courses first)                   │  │
│  └───────────────────────────────────────────┘  │
│                                                 │
│  ── Log ──────────────────────────────────────  │
│  │ 10:32:01  Imported 22 instructors          │  │
│  │ 10:32:01  Skipped 2 (matched existing)     │  │
│  └────────────────────────────────────────────  │
└─────────────────────────────────────────────────┘
```

### Section-by-section interaction flow

**1. Instructors section**

- **Choose File** opens a file picker filtered to `*.csv`.
- On file load, the CSV is parsed and each row is matched against
  existing instructors in the DB (cross-import matching).
- A **preview list** shows each instructor with their match status:
  - `(new)` — no match, will be created
  - `(exists - match)` — exact match found, will be skipped
  - `(ambiguous ⚠)` — multiple possible matches; row is highlighted.
    Clicking it expands an inline picker showing the candidates, plus
    "Create new" and "Skip" options.
- An **Import** button shows the counts and executes the import.
- After import, a checkmark and count replace the button.

**2. Courses section**

- Same pattern: Choose File → parse → mapping → preview → Import.
- **Subject mapping step** appears first (same pattern as section
  environment mappings): distinct SubjectCode values are matched against
  existing Subjects by `CalendarAbbreviation` (case-insensitive).
  Exact matches auto-fill; unmatched values require manual selection.
  SubjectId is required — courses whose SubjectCode can't be mapped
  are rejected (shown as errors, not imported).
- After subject mapping is confirmed, the **course preview list** shows:
  - `(new)` or `(exists - match)` based on CalendarCode.
  - No ambiguity tier needed for courses.
- The section is enabled as soon as the flyout opens (courses can be
  imported before instructors if needed — the strict ordering is only
  required for sections).

**3. Sections section**

- **Choose File** → parses the section CSV.
- Before showing a preview, the **mapping confirmation step** appears:

```
┌─ Mapping: Rooms ──────────────────────────────┐
│                                                │
│  CSV Value         →  Database Match           │
│  ──────────────────────────────────────────── │
│  Science 101       →  [Science 101      ✓]     │
│  MAC 310           →  [Select room...   ▾]     │
│  Online            →  [Skip (no room)   ▾]     │
│                                                │
└────────────────────────────────────────────────┘

┌─ Mapping: Section Types ──────────────────────┐
│  Lecture           →  [Lecture           ✓]    │
│  Laboratory        →  [Select type...   ▾]    │
└────────────────────────────────────────────────┘

┌─ Mapping: Campuses ───────────────────────────┐
│  (no campus values in CSV)                     │
└────────────────────────────────────────────────┘

┌─ Mapping: Meeting Types ──────────────────────┐
│  (no meeting type values in CSV)               │
└────────────────────────────────────────────────┘
```

- Each mapping row has a **ComboBox** pre-filled with the auto-matched
  DB entry (if exact match found), or showing "Select..." for unmatched
  values. The dropdown lists all entries of that type in the DB, plus
  a "Skip" option that leaves the field null.
- A **Confirm Mappings** button locks the mappings and shows the section
  preview list.
- The section preview shows each section grouped by semester, with
  course code, section code, instructor names, and meeting times.
  Unresolved references are flagged with ⚠.
- An **Import** button executes the section import.

**4. Log panel**

A scrollable text area at the bottom of the flyout, showing timestamped
log entries for all import activity. Warnings and errors are highlighted.
Persists for the session (not cleared between imports).

### Ordering enforcement

The three sections are **not hard-gated** — instructors and courses can
be imported in either order. Only the sections section requires that
its referenced instructors and courses already exist (either from a
prior import or from the current session). If the operator tries to
import sections and some CourseCode or Instructor references can't be
resolved, those are shown as warnings in the preview, not blocked.

This is pragmatic: the operator might import courses first if that's
what they have ready, then instructors, then sections. The only real
dependency is that sections come last.

### Error handling

- **Parse errors** (malformed CSV, missing required columns): shown as
  an error banner above the preview, with the line number. Import
  button disabled.
- **Partial failures**: If some rows fail validation (e.g., duplicate
  section code), they're listed as warnings. Successfully validated
  rows are still importable.
- **Transaction failure**: If the DB write fails, the entire file's
  import is rolled back and an error is shown in the log.

## Phase 4: Data Design

**Status:** LOCKED

### No new tables

The import writes to existing tables (Instructors, Courses, Sections)
using existing repository interfaces. No new persistent tables or schema
changes are required.

Import mappings (e.g., "MAC310" → room ID) are **not persisted**. They are
transient — rebuilt each time a section CSV is loaded. This is acceptable
because the mapping confirmation step is fast and deterministic: the same
CSV values will auto-match the same DB records. Persisting mappings would
add complexity for minimal value.

### No new repository methods

All matching and lookup can be done by loading the full entity list via
existing `GetAll()` methods and filtering in memory. The entity counts
are small enough (instructors < 200, courses < 500, rooms < 100,
scheduling-environment values < 50 per type) that in-memory filtering
is efficient and avoids touching repository interfaces.

Specifically:

| Lookup need                        | Method used                                     |
|------------------------------------|------------------------------------------------|
| All instructors for matching       | `IInstructorRepository.GetAll()`               |
| All courses for matching           | `ICourseRepository.GetAll()`                   |
| All subjects for SubjectCode match | `ISubjectRepository.GetAll()`                  |
| All rooms for Room column match    | `IRoomRepository.GetAll()`                     |
| Section types for mapping          | `ISchedulingEnvironmentRepository.GetAll("sectionType")` |
| Campuses for mapping               | `ICampusRepository.GetAll()`                   |
| Meeting types for mapping          | `ISchedulingEnvironmentRepository.GetAll("meetingType")` |
| Semesters for resolution           | `ISemesterRepository.GetAll()`                 |
| Academic years for resolution      | `IAcademicYearRepository.GetAll()`             |
| Section code uniqueness check      | `ISectionRepository.ExistsBySectionCode()`     |
| Course code uniqueness check       | `ICourseRepository.ExistsByCalendarCode()`     |
| Initials uniqueness check          | `IInstructorRepository.ExistsByInitials()`     |

### Transaction strategy

SQLite auto-enlists commands on the connection's active transaction.
The import service begins a transaction via
`IDatabaseContext.Connection.BeginTransaction()` before a batch of
inserts and commits after. No need to pass `DbTransaction` to individual
`Insert()` calls — the connection's ambient transaction covers them.

Check `IDatabaseContext.SupportsTransactions` before beginning (false in
the WASM demo context, where repositories operate without transactions).

Each file's import is one transaction:
- Instructor import: begin → insert all new instructors → commit
- Course import: begin → insert all new courses → commit
- Section import: begin → insert all new sections → commit

If any insert throws, the transaction is rolled back and the entire
file's import is aborted. Partial writes never reach the database.

### In-memory data shapes

These are the working structures used during the import session. They
are not persisted — they live on the flyout's ViewModel and are
discarded when the flyout closes.

#### Parsed CSV rows

Defined in Phase 2. Plain C# records/classes with string fields.
Constructed by the CSV parser, consumed by the matching and writing
stages.

#### Match index

Built once per import file by loading the relevant `GetAll()` results
into dictionaries keyed by the match field:

```csharp
// Instructor matching — built from IInstructorRepository.GetAll()
Dictionary<string, List<Instructor>> instructorsByLastName;
// Key: normalized lowercase last name
// Value: all instructors with that last name (for disambiguation)

// Course matching — built from ICourseRepository.GetAll()
Dictionary<string, Course> coursesByCalendarCode;
// Key: normalized lowercase calendar code (whitespace-collapsed)
// Value: the unique course (CalendarCode is unique)

// Subject matching — built from ISubjectRepository.GetAll()
Dictionary<string, Subject> subjectsByAbbreviation;
// Key: normalized lowercase CalendarAbbreviation
// Value: the subject
```

#### Environment mapping state

Built when a section CSV is loaded, by scanning its distinct values:

```csharp
// Used by section import:
class SectionMappingState
{
    List<MappingEntry> RoomMappings;
    List<MappingEntry> SectionTypeMappings;
    List<MappingEntry> CampusMappings;
    List<MappingEntry> MeetingTypeMappings;
}

// Used by course import:
class CourseMappingState
{
    List<MappingEntry> SubjectMappings;
    // Key: SubjectCode from CSV → matched Subject.Id
    // SubjectId is required; unmapped entries → course row rejected
}

class MappingEntry
{
    string CsvValue;           // raw value from CSV (e.g., "MAC 310")
    string? ResolvedId;        // DB record ID, or null if unmatched/skipped
    string? ResolvedDisplay;   // display name of matched record (for UI)
    MatchStatus Status;        // Exact, Unmatched, or Skipped
}
```

Each mapping list also holds the **available options** for that category
(loaded from the corresponding repository) so the UI can populate
ComboBox dropdowns.

#### Import result

Returned by each import operation for the log panel:

```csharp
class ImportResult
{
    int Created;         // new records inserted
    int Skipped;         // matched existing records (no action)
    int Warnings;        // rows with unresolved references (imported with nulls)
    int Errors;          // rows rejected (missing required fields, duplicates)
    List<string> Log;    // timestamped log entries
}
```

### CSV parsing details

**No external CSV library.** The existing codebase uses hand-rolled CSV
writing (`AppendCsvRow` in BackupService) and parsing
(`SharedScheduleCsvParser`). The import parser follows the same pattern:
RFC 4180 compliant (quoted fields, escaped quotes, newlines in quotes).

**Header row is required.** Column matching is by header name (case-
insensitive, whitespace-trimmed), not by position. This makes the format
tolerant of column reordering and extra/missing columns.

**Section CSV continuation rows.** A row with blank section-level columns
(CourseCode, SectionCode both empty) is a continuation of the previous
section — it adds a meeting to that section's MeetingRow list. The parser
tracks "current section" state during iteration.

**Time parsing.** StartTime and EndTime are parsed from "h:mm tt" format
(e.g., "8:00 AM"). DurationMin is parsed as an integer. If StartTime and
DurationMin are both present, EndTime is derived (not required). If only
StartTime and EndTime are present, DurationMin is derived.

**Day parsing.** Accepts full names ("Monday"), short names ("Mon", "Mo",
"M"), and numeric (1–7, Monday = 1). Uses the same flexible parsing as
`SharedScheduleCsvParser`.

### Entity construction

When creating new database entities from parsed rows, the import sets:

**Instructor:**
```
Id           = new GUID
LastName     = row.LastName (trimmed)
FirstName    = row.FirstName (trimmed, may be empty)
Initials     = row.Initials (trimmed, may be empty)
               If blank, auto-generate from first/last name initials.
               If generated initials collide (ExistsByInitials), append
               a digit (e.g., "JS" → "JS2").
Email        = row.Email (trimmed, may be empty)
IsActive     = true
StaffTypeId  = null (not available from scraping)
Notes        = empty
```

**Course:**
```
Id           = new GUID
SubjectId    = resolved from SubjectCode mapping (required — row rejected
               if SubjectCode cannot be mapped to a Subject)
CalendarCode = row.CalendarCode (trimmed)
Title        = row.Title (trimmed, may be empty)
IsActive     = true
Level        = empty (not available from scraping)
Capacity     = null
TagIds       = empty list
```

**Section:**
```
Id                    = new GUID
SemesterId            = resolved from Semester + AcademicYear columns
CourseId              = resolved from CourseCode → Course.Id
SectionCode           = row.SectionCode (trimmed)
SectionTypeId         = resolved from mapping, or null
CampusId              = resolved from mapping, or null
InstructorAssignments = resolved instructor IDs, each with Workload = 1.0
TagIds                = empty list (deferred)
ResourceIds           = empty list (deferred)
Reserves              = empty list (deferred)
Notes                 = empty
Level                 = null
Capacity              = null
Flag                  = None
Schedule              = list of SectionDaySchedule built from MeetingRows:
    Day            = parsed day number (1–7)
    StartMinutes   = parsed from StartTime
    DurationMinutes = parsed or derived
    RoomId         = resolved from mapping, or null
    MeetingTypeId  = resolved from mapping, or null
    RoomTypeId     = null (unless Room mapping target has a RoomTypeId)
    Frequency      = row.Frequency (trimmed, null if empty)
```

## Phase 5: Architecture

**Status:** LOCKED

### Component overview

```
MainWindow.axaml.cs          ← Ctrl+Shift+D KeyDown handler (no #if DEBUG gate)
MainWindowViewModel           ← IsDevToolsVisible, ToggleDevTools, NavigateToCsvImport
MainView.axaml                ← Dev Tools menu (IsVisible bound to IsDevToolsVisible)

Services/
  CsvImportParser.cs          ← Stateless CSV parsing (3 file types)
  CsvImportMatcher.cs         ← Stateless matching logic (instructors, courses,
                                 environment values)

ViewModels/Management/
  CsvImportViewModel.cs       ← Top-level flyout VM; owns the three sub-VMs
  InstructorImportViewModel.cs ← Instructor file: parse, preview, match, import
  CourseImportViewModel.cs     ← Course file: parse, subject mapping, preview, import
  SectionImportViewModel.cs   ← Section file: parse, env mapping, preview, import

Views/Management/
  CsvImportView.axaml          ← Flyout view (3 collapsible sections + log)
  InstructorImportView.axaml   ← Instructor section (embedded UserControl)
  CourseImportView.axaml       ← Course section
  SectionImportView.axaml      ← Section section
```

### Dev Tools activation

**MainWindow.axaml.cs** — Add a `KeyDown` handler for Ctrl+Shift+D
(outside the `#if DEBUG` block). Toggles `IsDevToolsVisible` on
`MainWindowViewModel`.

```csharp
// In the existing KeyDown handler, outside #if DEBUG:
if (e.Key == Key.D
    && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
    && DataContext is MainWindowViewModel dtVm)
{
    dtVm.IsDevToolsVisible = !dtVm.IsDevToolsVisible;
    e.Handled = true;
}
```

**MainWindowViewModel** — New property and commands:

```csharp
[ObservableProperty] private bool _isDevToolsVisible;

[RelayCommand]
private void NavigateToCsvImport()
    => OpenFlyout<CsvImportViewModel>("CSV Import");

// Existing debug commands (OpenDebug, OpenMigration) lose their
// #if DEBUG gate and move under Dev Tools menu items.
```

**MainView.axaml** — Replace the existing `#if DEBUG` DebugMenu with:

```xml
<Menu x:Name="DevToolsMenu"
      IsVisible="{Binding IsDevToolsVisible}"
      ...>
    <MenuItem Header="Dev Tools" Classes="nav">
        <MenuItem Command="{Binding NavigateToCsvImportCommand}"
                  Header="CSV Import..." />
        <Separator />
        <MenuItem Command="{Binding OpenDebugCommand}"
                  Header="Debug View..." />
        <MenuItem Command="{Binding OpenMigrationCommand}"
                  Header="Migration: CSV→JSON..." />
    </MenuItem>
</Menu>
```

No `#if DEBUG` conditional compilation on the menu or its commands.
The `ShowDebugMenu` AppSetting and its Preferences toggle are retired.

### Service layer

Two stateless service classes handle parsing and matching. They take
data in, return results out — no side effects, no repository access.
This makes them easy to unit-test.

#### CsvImportParser

Parses raw CSV text into typed row objects. One method per file type.

```csharp
class CsvImportParser
{
    /// Parses instructor CSV. Returns parsed rows + any parse errors.
    CsvParseResult<InstructorRow> ParseInstructors(string csvText);

    /// Parses course CSV. Returns parsed rows + any parse errors.
    CsvParseResult<CourseRow> ParseCourses(string csvText);

    /// Parses section CSV, grouping continuation rows into SectionRows.
    CsvParseResult<SectionRow> ParseSections(string csvText);
}

record CsvParseResult<T>(List<T> Rows, List<CsvParseError> Errors);

record CsvParseError(int LineNumber, string Message);
```

Implementation details:
- Header row is required; columns matched by name (case-insensitive)
- RFC 4180 quoting (double-quote escaping, newlines within quotes)
- Section parser tracks "current section" state: a row with blank
  CourseCode + SectionCode is a continuation (adds a MeetingRow)
- Time/day parsing reuses the flexible patterns from
  `SharedScheduleCsvParser` (full/short/single-letter day names,
  "h:mm tt" time format, numeric day values)

#### CsvImportMatcher

Builds match indices and runs matching logic. Pure functions.

```csharp
class CsvImportMatcher
{
    /// Builds an instructor match index from existing DB instructors.
    InstructorMatchIndex BuildInstructorIndex(List<Instructor> existing);

    /// Matches a single InstructorRow against the index.
    MatchResult<Instructor> MatchInstructor(
        InstructorRow row, InstructorMatchIndex index);

    /// Matches a CalendarCode against existing courses.
    MatchResult<Course> MatchCourse(
        string calendarCode, Dictionary<string, Course> courseIndex);

    /// Matches a SubjectCode against existing subjects.
    MatchResult<Subject> MatchSubject(
        string subjectCode, Dictionary<string, Subject> subjectIndex);

    /// Matches a CSV string value against a list of named DB records.
    MatchResult<EnvironmentTarget> MatchEnvironmentValue(
        string csvValue, List<EnvironmentTarget> dbValues);
}
```

Name normalization (honorific stripping, case folding, whitespace
collapse) lives here as private helpers.

### ViewModel layer

#### CsvImportViewModel (top-level flyout)

Owns the three sub-VMs. Registered as `AddTransient` in DI.

```csharp
public partial class CsvImportViewModel : ViewModelBase
{
    // Sub-VMs — created in constructor, injected with repos + services
    public InstructorImportViewModel InstructorVm { get; }
    public CourseImportViewModel CourseVm { get; }
    public SectionImportViewModel SectionVm { get; }

    // Shared log visible at bottom of flyout
    public ObservableCollection<string> Log { get; }
}
```

Constructor receives all needed repositories and services via DI, then
creates the three sub-VMs, passing them the repos they need plus a
shared `Action<string> addLog` callback.

#### InstructorImportViewModel

```csharp
public partial class InstructorImportViewModel : ViewModelBase
{
    // State
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private bool _isImported;
    [ObservableProperty] private string? _errorBanner;

    // Parsed + matched preview rows
    public ObservableCollection<InstructorPreviewRow> PreviewRows { get; }

    // Commands
    [RelayCommand] private async Task ChooseFile();
    [RelayCommand] private void Import();
}
```

`InstructorPreviewRow` is a display model:
```csharp
class InstructorPreviewRow : ObservableObject
{
    string DisplayName;              // "Smith, John"
    MatchStatus Status;              // New, Matched, Ambiguous
    List<Instructor>? Candidates;    // populated when Ambiguous
    Instructor? ResolvedMatch;       // operator's choice (for Ambiguous)
    bool Skip;                       // operator chose to skip this row
}
```

When `Status == Ambiguous`, the view shows an expander with a list of
candidates. The operator selects one (sets `ResolvedMatch`) or checks
`Skip`.

#### CourseImportViewModel

Same pattern, plus a **subject mapping step**:

```csharp
public partial class CourseImportViewModel : ViewModelBase
{
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private bool _isImported;
    [ObservableProperty] private bool _isMappingConfirmed;
    [ObservableProperty] private string? _errorBanner;

    // Subject mapping (shown before course preview)
    public ObservableCollection<MappingEntryViewModel> SubjectMappings { get; }

    // Course preview (shown after mapping confirmed)
    public ObservableCollection<CoursePreviewRow> PreviewRows { get; }

    [RelayCommand] private async Task ChooseFile();
    [RelayCommand] private void ConfirmMappings();
    [RelayCommand] private void Import();
}
```

#### SectionImportViewModel

Most complex — has environment mapping tables:

```csharp
public partial class SectionImportViewModel : ViewModelBase
{
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private bool _isImported;
    [ObservableProperty] private bool _isMappingConfirmed;
    [ObservableProperty] private string? _errorBanner;

    // Environment mapping tables (shown before section preview)
    public ObservableCollection<MappingEntryViewModel> RoomMappings { get; }
    public ObservableCollection<MappingEntryViewModel> SectionTypeMappings { get; }
    public ObservableCollection<MappingEntryViewModel> CampusMappings { get; }
    public ObservableCollection<MappingEntryViewModel> MeetingTypeMappings { get; }

    // Section preview (shown after mapping confirmed)
    public ObservableCollection<SectionPreviewRow> PreviewRows { get; }

    [RelayCommand] private async Task ChooseFile();
    [RelayCommand] private void ConfirmMappings();
    [RelayCommand] private void Import();
}
```

#### MappingEntryViewModel (shared)

Used by both CourseImportViewModel (subjects) and SectionImportViewModel
(rooms, section types, etc.):

```csharp
public partial class MappingEntryViewModel : ObservableObject
{
    public string CsvValue { get; }                    // raw CSV value
    [ObservableProperty] private EnvironmentTarget? _selectedMatch;
    public MatchStatus AutoMatchStatus { get; }        // Exact or Unmatched
    public List<EnvironmentTarget> AvailableOptions { get; }  // ComboBox items
}
```

`AvailableOptions` includes all DB entries for that category plus a
"Skip (leave blank)" sentinel. Pre-selected to the auto-matched entry
if `AutoMatchStatus == Exact`.

### View layer

#### CsvImportView.axaml

A `UserControl` with a vertical `StackPanel`:
1. Header text (title + instructions)
2. Three `GroupBox` sections, each containing the corresponding
   sub-view as a `ContentControl` with `Content="{Binding InstructorVm}"`.
   The ViewLocator resolves `InstructorImportViewModel` →
   `InstructorImportView`, etc.
3. A `Separator` + scrollable `ItemsControl` for the log.

#### InstructorImportView.axaml

- "Choose File" button
- File name display
- Error banner (if parse errors)
- `ItemsControl` with `InstructorPreviewRow` DataTemplate:
  - Name + status badge
  - Expander for ambiguous rows (candidate list + radio buttons)
- "Import N new · M matched · K skipped" button

#### CourseImportView.axaml

- "Choose File" button
- Subject mapping GroupBox (list of `MappingEntryViewModel` rows,
  each with a label + ComboBox)
- "Confirm Mappings" button
- Course preview `ItemsControl`
- "Import" button

#### SectionImportView.axaml

- "Choose File" button
- Four mapping GroupBoxes (Rooms, Section Types, Campuses, Meeting
  Types), each showing a list of `MappingEntryViewModel` rows.
  Empty categories show "(no values in CSV)".
- "Confirm Mappings" button
- Section preview (grouped by semester)
- "Import" button

### DI registration

In `App.axaml.cs` → `ConfigureServices`:

```csharp
// Services (stateless, singleton is fine)
services.AddSingleton<CsvImportParser>();
services.AddSingleton<CsvImportMatcher>();

// ViewModels (transient — new instance each time flyout opens)
services.AddTransient<CsvImportViewModel>();
```

The three sub-VMs (`InstructorImportViewModel`, `CourseImportViewModel`,
`SectionImportViewModel`) are **not** registered in DI — they are
created by `CsvImportViewModel`'s constructor, which passes them the
repositories and services they need. This keeps the DI surface small
and makes the parent-child relationship explicit.

Remove the `#if DEBUG` gates from:
- `DebugTestDataGenerator` / `DebugTestDataViewModel` / `MigrationViewModel`
  DI registrations
- `OpenDebugCommand` / `OpenMigrationCommand` on `MainWindowViewModel`

### File organization

```
src/TermPoint/
  Services/
    CsvImportParser.cs
    CsvImportMatcher.cs
  Models/
    CsvImportModels.cs       ← InstructorRow, CourseRow, SectionRow,
                               MeetingRow, MatchResult<T>, MatchStatus,
                               EnvironmentTarget, CsvParseResult<T>,
                               CsvParseError, ImportResult
  ViewModels/Management/
    CsvImportViewModel.cs
    InstructorImportViewModel.cs
    CourseImportViewModel.cs
    SectionImportViewModel.cs
    MappingEntryViewModel.cs
    InstructorPreviewRow.cs
    CoursePreviewRow.cs
    SectionPreviewRow.cs
  Views/Management/
    CsvImportView.axaml
    CsvImportView.axaml.cs
    InstructorImportView.axaml
    InstructorImportView.axaml.cs
    CourseImportView.axaml
    CourseImportView.axaml.cs
    SectionImportView.axaml
    SectionImportView.axaml.cs
```

## Phase 6: Implementation Plan

**Status:** LOCKED

Seven sessions. Each is self-contained — compiles and can be tested
independently after completion.

---

### Session 1: Dev Tools menu + scaffolding

**Goal:** Ctrl+Shift+D reveals a Dev Tools menu with a CSV Import item
that opens an empty flyout.

Tasks:
1. **MainWindow.axaml.cs** — Add Ctrl+Shift+D `KeyDown` handler outside
   `#if DEBUG`. Toggles `IsDevToolsVisible` on MainWindowViewModel.
2. **MainWindowViewModel** — Add `[ObservableProperty] bool IsDevToolsVisible`.
   Add `[RelayCommand] NavigateToCsvImport()` → `OpenFlyout<CsvImportViewModel>`.
   Remove `#if DEBUG` from `OpenDebugCommand` and `OpenMigrationCommand`.
3. **MainView.axaml** — Replace the `#if DEBUG` DebugMenu with a Dev Tools
   menu bound to `IsDevToolsVisible`. Three items: CSV Import, Debug View,
   Migration.
4. **CsvImportViewModel** — Empty shell extending `ViewModelBase`. Registered
   as `AddTransient` in DI.
5. **CsvImportView.axaml** — Minimal flyout: title text + "Coming soon"
   placeholder.
6. **App.axaml.cs** — Remove `#if DEBUG` from `DebugTestDataGenerator`,
   `DebugTestDataViewModel`, `MigrationViewModel` registrations. Add
   `CsvImportViewModel` registration.
7. **Retire `ShowDebugMenu`** — Remove the AppSetting and its Preferences
   toggle. Remove the code-behind that reads it in MainView.axaml.cs.
8. Compile check + manual test: Ctrl+Shift+D shows menu, CSV Import
   opens empty flyout, Debug View and Migration still work.

---

### Session 2: Models + CSV parser

**Goal:** All data types defined. Parser can read all three CSV formats.

Tasks:
1. **Models/CsvImportModels.cs** — Define all types:
   - `InstructorRow`, `CourseRow`, `SectionRow`, `MeetingRow`
   - `MatchResult<T>`, `MatchStatus` enum (Exact, Ambiguous, Unmatched,
     Skipped)
   - `EnvironmentTarget` (Id + DisplayName)
   - `CsvParseResult<T>`, `CsvParseError`
   - `ImportResult`
2. **Services/CsvImportParser.cs** — Implement:
   - RFC 4180 line/field parser (shared helper)
   - `ParseInstructors(string csvText)` — header-matched, one row per
     instructor
   - `ParseCourses(string csvText)` — header-matched, one row per course
   - `ParseSections(string csvText)` — header-matched, continuation-row
     grouping, time/day parsing
3. **DI** — Register `CsvImportParser` as singleton.
4. **Tests** — Unit tests for each parse method:
   - Happy path (well-formed CSV → correct rows)
   - Missing required fields → CsvParseError with line number
   - Quoted fields with commas and newlines
   - Section continuation rows grouped correctly
   - Flexible day/time parsing ("Monday", "Mon", "M", "1")

---

### Session 3: Matcher service

**Goal:** All matching logic implemented and tested.

Tasks:
1. **Services/CsvImportMatcher.cs** — Implement:
   - Name normalization: honorific stripping (Dr., Mr., Mrs., Ms., Prof.,
     Professor), case folding, whitespace collapse
   - `BuildInstructorIndex(List<Instructor>)` → dictionary by normalized
     last name
   - `MatchInstructor(InstructorRow, index)` → MatchResult with 4-tier
     logic (exact, last-only, initial-compatible, ambiguous)
   - `MatchCourse(calendarCode, courseIndex)` → MatchResult
   - `MatchSubject(subjectCode, subjectIndex)` → MatchResult
   - `MatchEnvironmentValue(csvValue, dbValues)` → MatchResult
     (case-insensitive name comparison)
   - Room composite matching: compare CSV `"{Building} {RoomNumber}"`
     against same composite built from Room entities
2. **DI** — Register `CsvImportMatcher` as singleton.
3. **Tests** — Unit tests for each matching method:
   - Instructor: exact match, last-only, initial-compatible ("J." →
     "John"), ambiguous (two "Smith"s), honorific stripping
   - Course: exact match, case-insensitive, whitespace-normalized
   - Subject: matched by CalendarAbbreviation
   - Environment: exact match, unmatched, case-insensitive

---

### Session 4: Instructor import (VM + View)

**Goal:** Can choose an instructor CSV, see a preview with match statuses,
resolve ambiguities, and import.

Tasks:
1. **InstructorPreviewRow.cs** — Display model with DisplayName, Status,
   Candidates, ResolvedMatch, Skip properties.
2. **InstructorImportViewModel.cs** — Implement:
   - `ChooseFile()` — file picker (*.csv), read text, parse via
     `CsvImportParser`, match each row via `CsvImportMatcher`, populate
     `PreviewRows`
   - `Import()` — iterate preview rows, create `Instructor` entities for
     `New` rows (auto-generate initials with collision avoidance), insert
     via `IInstructorRepository.Insert()`, wrapped in a transaction.
     Log results.
   - Error banner for parse errors. Import button disabled when no file
     loaded or all rows matched/skipped.
3. **InstructorImportView.axaml** — Layout:
   - Choose File button + file name label
   - Error banner
   - ItemsControl with DataTemplate for preview rows (name + status badge)
   - Expander for ambiguous rows with candidate list
   - Import button with counts
   - Post-import checkmark + summary
4. **Wire into CsvImportViewModel** — Create `InstructorImportViewModel`
   in constructor, expose as property.
5. **Update CsvImportView.axaml** — Add Instructors GroupBox with
   `ContentControl Content="{Binding InstructorVm}"`.
6. Compile check + manual test with a sample instructor CSV.

---

### Session 5: Course import (VM + View + MappingEntryViewModel)

**Goal:** Can import courses with subject mapping confirmation.

Tasks:
1. **MappingEntryViewModel.cs** — Shared mapping row VM: CsvValue,
   SelectedMatch (ObservableProperty), AutoMatchStatus,
   AvailableOptions list.
2. **CoursePreviewRow.cs** — Display model with CalendarCode, Title,
   Status, SubjectDisplay.
3. **CourseImportViewModel.cs** — Implement:
   - `ChooseFile()` — parse, scan distinct SubjectCodes, auto-match
     against subjects, populate `SubjectMappings`
   - `ConfirmMappings()` — validate all subjects mapped (reject rows
     with unmapped subjects), populate `PreviewRows`
   - `Import()` — create `Course` entities, set SubjectId from mapping,
     check `ExistsByCalendarCode` for dedup, insert via
     `ICourseRepository.Insert()`, transaction-wrapped. Log results.
4. **CourseImportView.axaml** — Layout:
   - Choose File button
   - Subject mapping GroupBox (ItemsControl of MappingEntryViewModel
     rows, each with label + ComboBox)
   - Confirm Mappings button
   - Course preview ItemsControl
   - Import button
5. **Wire into CsvImportViewModel** + update CsvImportView.axaml.
6. Compile check + manual test.

---

### Session 6: Section import (VM + View)

**Goal:** Can import sections with environment mapping, instructor/course
resolution, and meeting schedule construction.

Tasks:
1. **SectionPreviewRow.cs** — Display model with CourseCode, SectionCode,
   InstructorDisplay, MeetingSummary, Warnings list.
2. **SectionImportViewModel.cs** — Implement:
   - `ChooseFile()` — parse, scan distinct values in Room/SectionType/
     Campus/MeetingType columns, auto-match each, populate four mapping
     collections. Also resolve Semester + AcademicYear columns.
   - `ConfirmMappings()` — lock mappings, resolve each SectionRow's
     references (CourseCode → Course, Instructor names → Instructors,
     mapped environment values → IDs), populate `PreviewRows` with
     warnings for unresolved references.
   - `Import()` — create `Section` entities with:
     - New GUID
     - CourseId, SemesterId from resolution
     - InstructorAssignments with Workload = 1.0
     - Schedule built from MeetingRows (day/time/duration/room/frequency/
       meeting type)
     - SectionTypeId, CampusId from mappings
     - Check `ExistsBySectionCode` before inserting
     - Transaction-wrapped
   - Log results with per-section detail.
3. **SectionImportView.axaml** — Layout:
   - Choose File button
   - Four mapping GroupBoxes (reusing MappingEntryViewModel pattern)
   - Confirm Mappings button
   - Section preview grouped by semester
   - Import button
4. **Wire into CsvImportViewModel** + update CsvImportView.axaml.
5. **Add log panel** to CsvImportView.axaml — scrollable ItemsControl
   bound to `CsvImportViewModel.Log`, visible across all three imports.
6. Compile check + manual test.

---

### Session 7: Integration testing + polish

**Goal:** End-to-end tested with realistic data. Edge cases handled.
Ready for use.

Tasks:
1. **End-to-end test** — Scrape or hand-craft a realistic set of 3 CSVs
   for a department. Import into a fresh database. Verify:
   - All instructors created with correct fields
   - All courses created with correct subject links
   - All sections created with correct instructor assignments, meeting
     schedules, and environment mappings
   - Cross-references are correct (section's CourseId points to right
     course, etc.)
2. **Second-semester test** — Import a second semester's CSVs. Verify:
   - Existing instructors matched and reused (not duplicated)
   - Existing courses matched and reused
   - New instructors/courses created as needed
   - Sections created in the correct semester
3. **Edge cases** — Test and fix:
   - Empty CSV (header only, no data rows)
   - CSV with only some columns populated
   - Instructor with no first name
   - Ambiguous instructor match (resolve in UI)
   - Section with no meetings (unscheduled)
   - Section with multiple meetings (continuation rows)
   - Duplicate section code in CSV (should warn/reject)
   - Environment value in CSV with no DB match (confirm left null)
4. **Polish** — Adjust spacing, font sizes, error messages. Ensure the
   flyout scrolls correctly with many rows. Verify log panel stays
   readable.
5. **Compile check + full test suite** to verify no regressions.
