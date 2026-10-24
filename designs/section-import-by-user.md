# Section Import by User — Progressive Design Spec

**Status:** Phases 1–3 LOCKED (2026-10-20); amended 2026-10-24 by A4 (setup values are created as needed, marked
inline). Phase 4 (Data Design) LOCKED (2026-10-24). Phase 5 (Architecture) next.
**Target release:** 1.2.3.

## Phase 1: Problem Statement

### The problem

A new department's first semester has to be entered before TermPoint is useful. Today there are two ways to do that:
type every section into the editor, or have Greg run the **CSV onboarding import** (`designs/csv-import.md`). That
import uses three CSV files, sits behind the hidden Dev Tools menu, and is built for an operator who understands its
mapping screens. Every new customer therefore waits on Greg, and onboarding doesn't scale.

### Who and what

- **Who:** a department administrator, in the shipped desktop app. Not technical. No Dev Tools. Not in the WASM demo.
- **Source:** a **TermPoint template workbook (.xlsx)**, tightly specified. We provide a filled-in sample and
  instructions. The user reshapes their own data into it at their site. A **CSV with the same columns** is also
  accepted.
- **What a file brings in:** sections, plus anything they name that doesn't exist yet: **courses and instructors**,
  and the **setup values** (subjects, section types, meeting types, tags, campuses and rooms). Each new record is
  listed in the preview before anything is written. *(Changed 2026-10-24, A4: setup values were originally required
  to exist. But a new database has no subjects, types, tags or rooms (the startup wizard sets up only campuses,
  academic years, block lengths and section code patterns), so a new department's first import would have failed on
  nearly every row.)*
- **What must already be set up:** the academic year and semester being imported into. Everything else can come from
  the file.
- **Where it goes:** the semester the user has open.

### How it is used

- **Serially, by subject.** The user is likely to split the semester into several files (one per subject) and import
  them one after another. Only the first file lands in an empty semester.
- **"Oh yes, I forgot something."** The user fixes the file and imports it again. A row whose section already exists
  in the semester (same course and section code, case-insensitive) is **skipped and reported**, never changed. So
  re-importing a whole file only adds what is new, and edits made in the app since are safe.
- **Rigorous validation first, all or nothing.** The whole file is checked before anything is written. Every problem
  is reported in plain words, with its row and column. Any error means nothing is imported; the user fixes the file and
  imports again (cheap, since existing sections are skipped).
- **Undo:** none in 1.2.3. A wrong import is backed out by emptying the semester (an existing command). The result
  message says so.

### The template (one sheet; one row per meeting pattern)

| Column | Level | Rule |
|---|---|---|
| Academic Year | section | With Term, must name the open semester; a mismatch is an error |
| Term | section | e.g. Fall |
| Subject | section | The subject's calendar abbreviation (e.g. CHEM). Created if new. Also the subject of a newly created course |
| Course Number | section | Subject + Course Number identify the course (e.g. CHEM + 101 → CHEM101) |
| Course Title | section | Used when the course is created. For an existing course, a different title is **reported at validation**, and the existing title is kept |
| Section Code | section | Subject + Course Number + Section Code identify the section |
| Section Type | section | Blank or a name. Created if new |
| Instructor(s) | section | Blank = unstaffed. Format and matching: see "Instructors" |
| Tags | section | Separated by semicolons. Each is created if new |
| Capacity | section | Whole number or blank |
| Meeting Days | meeting | One or more days, e.g. `M,W` |
| Begin, End | meeting | e.g. `1000`, `10:00`, `10:00 AM`; End after Begin |
| Frequency | meeting | Blank = weekly; `odd`, `even`, or week numbers `1,6,7` (as the editor stores it) |
| Meeting Type | meeting | Blank or a name. Created if new |
| Building, Room | meeting | Blank = no room. Otherwise identifies a room; created if new (a new room needs both) |
| Campus | section | Blank = no campus. Otherwise a name; created if new |

"Created if new" means: matched to an existing record when the names are equal ignoring case and spacing; otherwise
created once, however many rows name it, and listed in the preview with a warning when it is close to an existing
name ("Will add tag Writng. Writing already exists.").

- **Same times on several days (the default):** `M,W` with `1000`–`1200` means a 10:00–12:00 meeting on Monday *and*
  Wednesday.
- **Different times on different days:** use several rows with the same Subject + Course Number + Section Code, one
  per time pattern. They are one section. On the extra rows the section-level columns may be left blank. If one is
  filled in and differs from the section's first row, that's an error.
- Blank optional cells are left empty (null) in TermPoint. *(Except, 2026-10-24, Phase 4 D1–D2: a blank Capacity takes the course's capacity or the default, and a new section always gets its course's tags, as in the editor.)*

### Instructors

How could a name match more than one person? Only in two ways, and the rules below remove the guesswork the
onboarding import's four-tier matcher had:
1. Two instructors already in the database have the same name. That's rare, and it's an **error**: the import can't
   tell which one is meant.
2. The file gives a partial or variant name ("Smith, J." or "Jon Smith" when "John Smith" exists). Here the matcher is
   **exact only**: names are compared after case and spacing are normalised. Anything else is a new instructor. Every
   instructor about to be created is listed in the summary. A **warning** flags one whose last name matches an
   existing instructor ("Will add Jon Smith. John Smith already exists."), so a typo is caught before import.

**A name is a person.** Instructors are read off the section rows; there is no instructors sheet. So the import cannot
tell whether "John Smith" on two sections is one person or two, and it doesn't try: the same normalised name is one
instructor, created once. Two real people who share a name must be told apart in the file (e.g. `Smith, John A.` and
`Smith, John B.`). The instructions say so. By the same rule, two instructor records in the database with the same
name are an error. The import can't know whether they're two people or a duplicate, only that it can't choose
between them.

Format: `Last, First`, several separated by semicolons (`Smith, John; Lee, Ann`).

### How it relates to existing work

| Existing | Relation |
|---|---|
| **CSV onboarding import** (`designs/csv-import.md`; Dev Tools; three CSVs, mapping screens) | **Reuses** its parser pieces (RFC 4180 fields, day and time parsing) and write logic. Its fuzzy instructor matcher and mapping screens are not used. **Retired** when this ships. |
| **Shared Schedule overlay** (on this branch; read-only, held in memory) | **Unrelated.** The overlay never writes the database. |
| **Enhanced Imports + recent imports** (`feature/EnhancedImports`, not merged) | **Unrelated, and not needed.** |
| **AI-assisted import** (`designs/ai-assisted-import.md`, Phase 1 only) | **Complements.** The template defined here is the target format that spec lacked. |

### What "done" looks like

- A department administrator can get the sample workbook and the instructions from inside the app.
- They choose a file. TermPoint validates it and shows either the list of problems, or a summary of what will be added
  (sections, new courses, new instructors, new setup values), what will be skipped as already present, and any
  warnings.
- They confirm, and the sections appear in the open semester, along with any new courses, instructors and setup
  values.
- Importing the same file again adds nothing and says so. Importing a corrected file adds only what's new.
- It is unavailable whenever the database is read-only, and it never writes the shared database except through the
  normal save.

### Decisions (2026-10-20)

| Q | Decision |
|---|---|
| Q1 File shape | One sheet, one row per meeting pattern; columns above |
| Q2 Validation failure | All or nothing |
| Q3 Instructor ambiguity | Exact matching; duplicates in the database are an error; near-misses are warnings (see "Instructors") |
| Q4 New course's subject | A Subject column |
| Q5 .xlsx | A small, reliable read-only library, referenced by the desktop build only. No import in the WASM demo. Candidate: ExcelDataReader (MIT, read-only); confirm in Phase 5 |
| Q6 Undo | Nothing new; empty the semester |
| Q7 Dev Tools import | Retire it |
| Q8 Course title differs | Report at validation; keep the existing title |
| A1 Rooms, campus | Optional Building + Room per meeting row, Campus per section; blank = null |
| A2 Academic Year + Term | Every row must name the open semester; mismatch = error |
| A3 Instructor format | `Last, First`, semicolon-separated |
| A4 Setup values (2026-10-24) | Subjects, section types, meeting types, tags, campuses and rooms are created as needed, like courses and instructors, and listed in the preview. Supersedes "must already be set up" |

### Preliminary checks (confirmed in Phase 4)

- **Four rules:** the import is one large in-app edit to the open copy, saved by the existing save pipeline. Rules 1–2
  hold because it never touches the shared file directly. Rule 3: the edit is unsaved work like any other. Rule 4: the
  validation and result messages are written to it.
- **MinimumVersionToWrite:** it creates ordinary sections, courses, instructors and setup values that 1.2.2 can
  already save, so likely **No**.
- **New tables:** none expected.

### Out of scope

- Updating existing sections from a file (reconciling with the registrar's system).
- Importing into several semesters at once.
- Changing existing setup values. A room, tag or subject that already exists is never edited, and the file can't
  give the fields a new one starts without (a room's capacity and type, a subject's full name, abbreviations). Those
  are filled in afterwards in the usual editors. *(Replaces "Creating rooms, campuses, types, tags or subjects",
  2026-10-24, A4.)*
- Arbitrary column layouts and in-app column mapping.
- Instructor workload per assignment (new assignments use the default, as the onboarding import does).

## Phase 2: Domain Model (LOCKED 2026-10-20)

### The pipeline

**Read → Group → Resolve → Validate → Preview → Write.** Nothing touches the database before Write. Every step before
Write is a pure function of the file plus a snapshot of the open semester's setup data, so it can be unit-tested
without the UI.

### Transient types (never persisted)

- **ImportRow:** a sheet row number and its raw cells, keyed by column name. An .xlsx and a CSV both produce these;
  everything downstream ignores which kind of file it came from.
- **SectionDraft:** the key (Subject, Course Number, Section Code, compared case-insensitively), the section-level
  values, the rows it came from, and a list of **MeetingDraft**. One SectionDraft per key; its rows are grouped in
  file order.
- **MeetingDraft:** one per day per row: Day, start minutes, duration (End − Begin), Frequency, Meeting Type,
  Building + Room. `M,W 1000–1200` gives two MeetingDrafts.
- **CourseRef / InstructorRef:** either an existing record, or a "new" entry (course: subject + code + title;
  instructor: last, first) shared by every draft that names it.
- **SetupRef** (2026-10-24, A4): the same for subjects, section types, meeting types, tags, campuses and rooms:
  either an existing record, or a "new" entry shared by every draft that names it.
- **ImportReport:**
  - Errors: row, column, plain message.
  - Warnings: row, message.
  - Skipped sections: already in the semester.
  - The plan: new setup values, new courses, new instructors and new sections, with counts.

### Identity and matching (all exact after case and spacing are normalised)

| Thing | Matched by |
|---|---|
| Semester | Academic Year = `AcademicYear.Name` **and** Term = `Semester.Name`; must be the open semester |
| Subject | `Subject.CalendarAbbreviation` |
| Course | Subject abbreviation + Course Number vs `Course.CalendarCode`, ignoring spaces (CHEM101 = CHEM 101). A new course's code is `{abbrev}{number}`, no space (the app's convention) |
| Section (skip check) | Course + Section Code in the open semester (`ExistsBySectionCode`) |
| Instructor | `Last, First` vs `LastName` + `FirstName`; a name is a person |
| Section Type, Meeting Type, Campus, Tag | Name |
| Room | Building + Room vs `Room.Building` + `Room.RoomNumber`. Building may be blank only if the room number alone is unique |

**When nothing matches** (2026-10-24, A4), the value is new and is created once:
- **Subject:** a new subject with `CalendarAbbreviation` = the cell and `Name` = the cell too (the file has no full
  name). If that name is already another subject's name, it's an error ("CHEM is the name of subject CHM. Use CHM.").
- **Section type, meeting type, tag, campus:** a new record named as first typed in the file.
- **Room:** a new room needs **both** Building and Room. A blank Building is allowed only to match an existing room
  whose number is unique. With no match, it's an error ("Row 9: room 210 isn't in TermPoint yet. To add it, give its
  building too."). A Building with no Room is always an error.

### Invariants

1. **All or nothing.** Any error means no write. Writing is one transaction.
2. **Existing records are never changed.** The import only inserts sections, courses, instructors, and the setup
   values they name that don't exist yet (subjects, section types, meeting types, tags, campuses, rooms). *(Widened
   2026-10-24, A4; it was "sections, courses and instructors".)*
3. **A section already in the semester is skipped whole,** including the meetings that rows add to it. (The import
   never adds meetings to an existing section.) **Its rows create nothing** (2026-10-24): a skipped section's
   non-key cells are ignored (P3), so a room renamed since the first import isn't re-created under its old name.
4. **A section's rows agree.** A section-level cell filled on two rows of the same section with different values is
   an error.
5. **A new course has one title.** Two rows giving different titles for the same new course is an error.
6. Required on every row: Academic Year, Term, Subject, Course Number, Section Code. Course Title is required when
   the course is new.
7. **Meetings:** Days, Begin and End are all filled or all blank. All blank = a section with no meeting time
   (online/TBA). Partly filled = error. End must be after Begin. Two meetings of one section on the same day that
   overlap = error. *(Changed 2026-10-24, Phase 4 D11: overlap is a **warning**, and none when the two never meet in the same week. The editor allows overlaps, and odd/even-week meetings at the same time are legitimate.)*

### What a write creates

- **Course:** SubjectId, CalendarCode, Title, IsActive. Level, Capacity and Tags are left empty.
- **Instructor:** LastName, FirstName, generated Initials (the existing collision-avoiding generator), IsActive.
  Email, notes and staff type are left empty.
- **Setup values** (2026-10-24, A4): only the identifying fields the file gives (name; a subject's abbreviation; a
  room's building and number) and the next sort order. Everything else is left empty for the user to fill in. Field
  by field: Phase 4.
- **Section:**
  - CourseId, SemesterId, SectionCode, SectionTypeId, CampusId, TagIds, Capacity;
  - InstructorAssignments with the default workload;
  - one `SectionDaySchedule` per MeetingDraft. *(Corrected 2026-10-24: the meeting's RoomTypeId is not copied from the room. See Phase 4.)*
- All are new objects of types that already derive from `PersistedRecord`, so there is no extension data to carry.

### Warnings (shown in the preview; they don't block)

- An existing course's title differs from the file's (the existing title is kept).
- A new instructor shares a last name with an existing one.
- A new setup value is close to an existing one of the same kind, or to another new one (2026-10-24, A4; the rule
  is in Phase 4).
- A matched course or instructor is inactive.
- A room's campus differs from the section's campus.
- Begin isn't one of the academic year's legal start times (see P1).
- Two meetings of one section overlap on the same day in a week they both meet (2026-10-24, Phase 4 D11).

### Decisions

**Decided (2026-10-20):**
- **P1:** a Begin time that isn't in the legal start-time list is a **warning**.
- **P2:** days are separated by commas or spaces, using the onboarding parser's day names (`R` or `Th` = Thursday).
  Compact runs like `MWF` and `TR` are also accepted.

- **P3:** for a section that already exists, **only its key columns are checked** (Academic Year, Term, Subject,
  Course Number, Section Code). Its other cells are ignored. A setup change since the first import (a renamed room, a
  deleted tag) must not block adding the forgotten section. The preview lists these as "Already in Fall 2026, not
  changed: N sections."

## Phase 3: UX Sketch (LOCKED 2026-10-20)

### Getting the template

- The sample workbook ships inside the app. **"Save template…"** writes it wherever the user chooses.
- The workbook has two sheets:
  - **Sections:** the columns, with a few sample rows, including a section on two rows with different times.
  - **Instructions:** what each column means, the formats (`Last, First; …`, `M,W`, `1000`), what must be set up
    first, how to tell apart two people who share a name, and how reloading works. *(2026-10-24, A4: only the
    academic year and semester must be set up first. The instructions say instead that new subjects, types, tags,
    campuses and rooms are created from the file, so spellings should match what's already in TermPoint.)*
- The instructions travel with the file, so a colleague preparing the data at their site needs nothing else.
- The import reads the sheet named **Sections**. A CSV is read as that sheet.

### The flyout: "Import Sections"

It opens like the other flyouts and works top to bottom.

1. **Target.** "Sections will be added to **Fall 2026** (2026-2027)." The open semester, always visible.
2. **Choose file…** It reads and checks the file, showing "Checking…" while it works. No changes are made.
3. **Result**, one of three:
   - **Problems.** "This file can't be imported yet. Nothing has been changed." Then a list:
     - Row 12, Room: "210" isn't in TermPoint yet. To add it, give its building too.
     - Row 15: CHEM101 A has a different Instructor(s) than row 14.

     *(2026-10-24, A4: the first example was "SCI 9 isn't set up in TermPoint". A room that isn't set up is now
     created, so that's no longer a problem.)*

     Below the list:
     - **Copy list** puts the problems on the clipboard as tab-separated text (Row, Column, Problem), so pasting into
       Excel, perhaps beside their own data, gives three columns, and they can sort, tick off and send it on.
       Pasting into an email gives readable lines.
     - **Check again** re-reads the same file after they fix it.
   - **Ready.**
     - "Will add **37 sections**, **5 new courses** and **8 new instructors**. Already in Fall 2026, not changed: 12
       sections."
     - Expandable lists of the new courses, new instructors and sections.
     - *(2026-10-24, A4)* **New in TermPoint:** an expandable list of the subjects, section types, meeting types,
       tags, campuses and rooms that will be created, grouped by kind. It is always shown when non-empty, because a
       typo here creates a record. Its near-miss warnings sit with the other warnings.
     - Warnings, if any, listed in amber. They don't block.
     - **Import** and **Cancel**.
   - **Nothing to add.** "Every section in this file is already in Fall 2026."
4. **Done.** "Added 37 sections, 5 courses and 8 instructors to Fall 2026." The flyout stays open, so the next
   subject's file can be chosen straight away. *(2026-10-24, A4: when setup values were created, a second line
   follows, e.g. "Also added 2 subjects, 3 tags and 14 rooms. New rooms need their capacity and room type: see
   Rooms." The room sentence appears only when rooms were added.)*

Messages name rows, columns and values as the user typed them; no internal terms.

### When it's unavailable (shown disabled, with the reason as a tooltip)

- The database is read-only (licence, older version, or another user is editing it).
- Several semesters are shown: "Show one semester to import into it."
- A section is open for editing: "Finish editing the section first."

### Decisions

- **U1:** **Files → Import Sections…** only. No link in the empty section list.
- **U2:** an import is an ordinary edit, saved with the next save. The existing unsaved-changes and crash-recovery
  handling covers it.
- **U3:** only when the semester was empty before the import, the done message adds "To take it back, use Empty
  semester (Academic Years)." *(Changed 2026-10-24, Phase 4 D5: "To take it back, switch to another semester, then use Empty semester in Academic Years." Empty semester refuses the open semester.)*

## Phase 4: Data Design (LOCKED 2026-10-24)

### Summary

- **No new tables, no new columns, no schema change.** The import inserts rows into `Subjects`, `Campuses`,
  `SchedulingEnvironmentValues` (section types, meeting types, tags), `Rooms`, `Courses`, `Instructors` and `Sections`
  through their existing repository `Insert` methods. Each fills that table's readable columns and calls
  `MarkDirty()` first.
- **No new repository methods.** Lookups load whole lists with existing `GetAll` methods and match in memory. The
  counts are small: at most a few hundred courses and instructors, dozens of rooms and types.
- **Nothing about an import is persisted** apart from the records it creates: no import log, no saved mapping.
- **MinimumVersionToWrite: No.** Every record it creates is an ordinary subject, campus, section type, meeting type,
  tag, room, course, instructor or section, with fields 1.2.2 already reads and saves. A 1.2.2 user who opened the database afterwards would see and save exactly the same
  data. Retiring the Dev Tools import changes nothing that is saved.

### The setup snapshot (read once per Check)

Resolve and Validate are pure functions of the rows plus this snapshot. It is built on the UI thread when the user
chooses a file (or presses Check again), and again at Import (see D4).

| Need | Source | Indexed by (normalised) |
|---|---|---|
| Open semester + its academic year | `SemesterContext` (single semester shown), `IAcademicYearRepository.GetById` | — |
| Subjects | `ISubjectRepository.GetAll()` | `CalendarAbbreviation` |
| Courses | `ICourseRepository.GetAll()` | `CalendarCode` with all spaces removed |
| Instructors | `IInstructorRepository.GetAll()` | `(LastName, FirstName)`; also by `LastName` alone for the near-miss warning; `Initials` set for generation |
| Sections already in the semester | `ISectionRepository.GetAll(semesterId)` | `(CourseId, SectionCode)` |
| Section types / meeting types | `ISchedulingEnvironmentRepository.GetAll(SchedulingEnvironmentTypes.SectionType / .MeetingType)` | `Name` |
| Tags | `ISchedulingEnvironmentRepository.GetAll(SchedulingEnvironmentTypes.Tag)` | `Name` |
| Campuses | `ICampusRepository.GetAll()` | `Name` |
| Rooms | `IRoomRepository.GetAll()` | `(Building, RoomNumber)`; also by `RoomNumber` alone |
| Legal start times | `ILegalStartTimeRepository.GetAll(academicYearId)` | `BlockLength` (hours) → its `StartTimes`, plus the union of all of them for lengths with no entry (as the editor's Start list does) |
| Weekend days | `AppSettings.IncludeSaturday`, `IncludeSunday` | — (D10) |
| Default section capacity | `AppSettings.DefaultSectionCapacity` | — (D1) |

The same lists also give each kind's highest `SortOrder`, so new setup values are appended after the existing ones in
file order, as the editors append a new entry (`Max(SortOrder) + 1`).

The sections already in the semester are read with `GetAll(semesterId)` and compared in memory. That is one query
instead of one `ExistsBySectionCode` call per section, and it uses the same rule: same course, section code equal
ignoring case.

### Normalisation (one shared function, used for every comparison)

Trim the value, collapse runs of whitespace to one space, compare ignoring case (`OrdinalIgnoreCase`). Course codes
also drop every space, so `CHEM 101` = `CHEM101`. Messages always quote the value **as the user typed it**.

### Matching: one record, none, or two

- **One match:** used.
- **No match:** a new record is planned, once per normalised value, and shared by every row that names it (A4). The
  first spelling met in the file names it. A room needs both Building and Room to be new (Phase 2).
- **Two matches:** an error (D7), below.

A file value that matches **two** records can't be resolved, so it is an error on that row, naming both, e.g.
"Row 8, Room: there are two rooms called SCI 210 in TermPoint." This covers: two instructors with the same name (Phase
1 rule), two courses whose codes are equal ignoring spaces (`CHEM101` and `CHEM 101`), two rooms with the same
building and number, two tags / section types / meeting types / campuses with the same name, and a room number given
without a building when several buildings have it. The editors mostly prevent these, but older data may hold them.

### Reading the file into ImportRow

- **Headers:** the first row of the sheet. Names are matched after normalisation, in any order. **Every template
  column must be present** (D9); a missing one is a single error ("The Tags column is missing.") and nothing else is
  checked. Extra columns are ignored, so users can keep their own notes beside the data.
- **Row numbers** are what Excel shows: the header is row 1. For a CSV, the line number of the record (the existing
  `CsvRecordReader` already tracks it, including quoted line breaks).
- **Blank rows** (every cell empty) are skipped silently.
- **Every cell becomes text** before anything else sees it, so .xlsx and CSV behave the same:
  - a whole number → `101`, not `101.0`;
  - a cell Excel stores as a time (typing `10:00` gives the fraction 0.41667, or a DateTime) → `10:00`;
  - text → as is.
- **Leading zeros can't be recovered.** Excel turns a typed `001` into the number 1. The sample workbook formats
  Course Number, Section Code, Begin and End as **Text** so this doesn't happen, and the Instructions sheet says why.
  The import can't tell `1` from a lost `001`, so it doesn't guess.

### Transient shapes (live on the flyout's view model; dropped when it closes)

```csharp
record ImportRow(int RowNumber, IReadOnlyDictionary<string, string> Cells);   // keyed by template column name

record SectionKey(string Subject, string CourseNumber, string SectionCode);   // compared normalised

class SectionDraft {
    SectionKey Key; List<int> RowNumbers;                // rows in file order; first row supplies section values
    string CourseTitle, SectionType, Instructors, Tags, Capacity, Campus;   // raw text from the first filled row
    List<MeetingDraft> Meetings;
    // filled in by Resolve:
    CourseRef Course; List<InstructorRef> Instructors; SetupRef<SchedulingEnvironmentValue>? SectionType;
    SetupRef<Campus>? Campus; List<SetupRef<SchedulingEnvironmentValue>> Tags; int? Capacity; bool AlreadyExists;
}

record MeetingDraft(int RowNumber, int Day, int StartMinutes, int DurationMinutes, string? Frequency,
                    SetupRef<SchedulingEnvironmentValue>? MeetingType, SetupRef<Room>? Room);

abstract record CourseRef;     record ExistingCourse(Course Course) : CourseRef;
                               record NewCourse(SetupRef<Subject> Subject, string Code, string Title) : CourseRef;
abstract record InstructorRef; record ExistingInstructor(Instructor Instructor) : InstructorRef;
                               record NewInstructor(string Last, string First, string Initials) : InstructorRef;

// A4: subjects, section types, meeting types, tags, campuses, rooms. A "new" entry holds the record to insert,
// already built (fresh Id, name as first typed, next SortOrder), so every draft that names it shares its Id.
abstract record SetupRef<T>(T Record);
record ExistingSetup<T>(T Record) : SetupRef<T>(Record);
record NewSetup<T>(T Record, int FirstRow) : SetupRef<T>(Record);

class ImportReport {
    List<ImportProblem> Errors;     // Row, Column (may be null), Message
    List<ImportProblem> Warnings;   // Row, Message
    List<SectionDraft> Skipped;     // already in the semester
    ImportPlan Plan;                // NewSubjects, NewCampuses, NewSectionTypes, NewMeetingTypes, NewTags, NewRooms,
                                    // NewCourses, NewInstructors, NewSections
    bool SemesterWasEmpty;          // for the done message (U3/D5)
}
```

Every new course, instructor and setup value is created **once** and shared by every draft that names it, so "same
normalised name = one record" holds by construction. A new setup value is planned only from rows of sections that
will be **added**. Rows of skipped sections create nothing (Phase 2, invariant 3).

### What a write creates, field by field

**Setup values** (one per `NewSetup`, A4)

| Record | Fields set | Left empty, for the user to fill in |
|---|---|---|
| `Subject` | `Id`; `CalendarAbbreviation` = the cell; `Name` = the cell | the full name (shown in the preview: "CHEM (add its full name in Subjects)") |
| `SchedulingEnvironmentValue`, inserted with `SchedulingEnvironmentTypes.SectionType`, `.MeetingType` or `.Tag` | `Id`, `Name`, `SortOrder` = next | `SectionCodeAbbreviation` |
| `Campus` | `Id`, `Name`, `SortOrder` = next | `Abbreviation` |
| `Room` | `Id`, `Building`, `RoomNumber`, `SortOrder` = next; `CampusId` = the campus named by **every** new section that meets in it, if they all name the same one, else empty | `Capacity`, `RoomTypeId`, `Features`, `Notes` |

A room with no campus is offered on every campus by the editor's room list, so leaving it empty is safe. A room with
no type won't appear when a meeting filters its room list by type, or in a Room Browser search by type, until the
user gives it one. The done message says so (Phase 3, step 4).

**Course** (one per `NewCourse`)

| Field | Value |
|---|---|
| `Id` | new GUID |
| `SubjectId` | the matched or new subject |
| `CalendarCode` | `{CalendarAbbreviation}{Course Number}`, no space (app convention) |
| `Title` | Course Title |
| `IsActive` | true |
| `TagIds`, `Level`, `Capacity` | empty / `""` / null |

**Instructor** (one per `NewInstructor`)

| Field | Value |
|---|---|
| `Id` | new GUID |
| `LastName`, `FirstName` | from `Last, First`, trimmed, as typed (case kept) |
| `Initials` | generated: first + last initial, then `2`, `3`… until unused. "Unused" counts the database **and** the initials given to instructors earlier in this import. Today's generator (`InstructorImportViewModel.ResolveInitials`) asks the database once per candidate and can't see same-import initials, so it is lifted into a shared pure helper that takes the set of taken initials. |
| `IsActive` | true |
| `Email`, `Notes`, `StaffTypeId` | empty / null |

**Section** (one per new `SectionDraft`)

| Field | Value |
|---|---|
| `Id` | new GUID |
| `SemesterId` | the open semester |
| `CourseId` | the matched or new course |
| `SectionCode` | as typed, trimmed |
| `SectionTypeId`, `CampusId` | matched or new, or null when blank |
| `TagIds` | the file's tags (matched or new), plus the course's tags (D2) |
| `Capacity` | the file's number; if blank, the course's capacity, else the default section capacity (D1) |
| `Level` | the course's `Level` (as the editor does) |
| `InstructorAssignments` | one per instructor, in file order, `Workload = 1.0` (the editor's default) |
| `Schedule` | one `SectionDaySchedule` per MeetingDraft |
| `Notes`, `Reserves`, `ResourceIds`, `Flag` | empty / `None` |

**SectionDaySchedule** (one per MeetingDraft)

| Field | Value |
|---|---|
| `Day` | 1 = Monday … 5 = Friday, 6 = Saturday, 7 = Sunday (the editor's numbering). Weekend days only when turned on (D10) |
| `StartMinutes`, `DurationMinutes` | Begin; End − Begin |
| `Frequency` | null (weekly), `odd`, `even`, or sorted comma-separated week numbers |
| `MeetingTypeId` | matched or new, or null |
| `RoomId` | matched or new room, or null |
| `RoomTypeId` | null. On a meeting this is the room type the meeting **needs** (it filters the editor's room list and drives Room Browser searches), not a copy of the room's type, and the editor defaults it to "(none)". The file has no column for it. |

All of these are brand-new objects whose types already derive from `PersistedRecord`. Nothing is cloned from an
existing record, so there is no extension data to carry (`WithExtensionDataFrom` doesn't apply).

### Near-miss warnings for new setup values (A4)

The preview lists every new value. The warning points at the ones most likely to be typos. A new value gets a
warning when, against an existing value **of the same kind** or another new one:
- **subjects, section types, meeting types, tags, campuses:** one contains the other (`Lec` / `Lecture`), or they
  differ by at most two letters (a small edit-distance helper; `Writng` / `Writing`). Comparison ignores case, spaces
  and punctuation, and names under four letters only get the contains check, so `LAB` vs `LAW` isn't flagged;
- **rooms:** the **building** is new. Neighbouring room numbers differ by one character by nature, so comparing
  numbers would flag every room. A new building is rare and worth a look ("Will add building SCIENCE. SCI already
  exists.").

The message names both values as typed: "Will add tag Writng. Writing already exists."

### Meeting-time rules (the full list; extends Phase 2's invariant 7)

A meeting the import writes must be one the meeting editor would accept. Every rule the editor enforces
(`SectionMeetingViewModel`, checked 2026-10-24) is listed here, with how the import treats it. The import can't ask
the user anything mid-file or adjust a value silently, so where the editor clamps a value and warns, the import
reports an error.

| Rule | Editor today | Import |
|---|---|---|
| Days, Begin and End all filled or all blank | Save refuses a row with a start but no length, or the reverse (`MeetingTimeValidator`) | **Error** for a partly filled row (invariant 7). All blank = no meeting time |
| A real day | Day "(any)" is only a placeholder for the Room Browser; saving refuses it | Every day must parse (P2). An unknown day name is an **error** |
| Saturday / Sunday only when turned on | Offered only when `AppSettings.IncludeSaturday` / `IncludeSunday` is on; the grid shows those columns only then (`ScheduleGridViewModel.cs:1301`) | **Error** when the day is off (D10): "Row 7, Meeting Days: Saturday isn't turned on in TermPoint. Turn it on in Settings, then check again." |
| Begin no earlier than 07:30 | Clamps to 07:30 and warns | **Error** (D3) |
| Begin before 22:00 | Refuses | **Error** |
| End no later than 22:00 | Shortens the length to end at 22:00 and warns | **Error** (D3) |
| End after Begin (length > 0) | Refuses a length ≤ 0 | **Error** |
| Times are real clock times | `HHMM`, hours 0–23, minutes 0–59 | Same values. The import also accepts `10:00` and `10:00 AM` (Phase 1), and Excel time cells (Reading the file) |
| Custom weeks: whole numbers ≥ 1, no repeats | Refuses a week < 1, a non-number, or a repeated week | **Error**, same three cases. Stored sorted, as the editor stores it |
| Frequency words | Weekly / Odd weeks / Even weeks / Custom | Blank, `weekly`, `odd`, `even`, `odd weeks`, `even weeks` (any case), or week numbers. Anything else is an **error** |
| Legal start time for the length | The Start list offers the start times configured for the meeting's length. For a length with no configured times, it offers every configured start time. Any time may still be typed | **Warning** (P1), by the same rule: Begin is checked against the start times for its length when that length is configured, else against all of them |
| Two meetings of one section on the same day overlapping | **Not checked.** The editor allows it | **Warning**, not an error (D11). No warning when the two can't meet in the same week (odd vs even, or custom weeks with none in common) |

The limits come from one place: `SectionMeetingViewModel.MinStartMinutes` / `MaxEndMinutes` move to a shared spot in
Phase 5, so the editor and the import can't drift apart. The same goes for the day names, the time parsing and the
custom-week check, which today are copied across the editor, the Dev Tools import and the shared-schedule parser.

*Correction (2026-10-24): an earlier draft checked the legal-start-time warning against every start time in the
academic year. The editor narrows the list by meeting length, and the import now does the same.*

### The write

- **One transaction**, begun with `IDatabaseContext.Connection.BeginTransaction()`, as the Dev Tools import and
  `SemesterContentsService.RunInTransaction` already do. Commands created after it is begun use it automatically. Any
  exception rolls it back and nothing is written.
- **Order: setup values (subjects, campuses, section types, meeting types, tags, rooms), then courses, then
  instructors, then sections**, so nothing is inserted before a record it refers to. Only one step strictly depends
  on order: `SectionRepository.Insert` fills the readable `course_code` column with a subquery on `Courses`, so a new
  course must already be inserted. Following references for the rest keeps the database consistent at every step,
  which makes it easier to reason about.
- **Every insert calls `MarkDirty()` first** (each repository does this). So:
  - **The first write of the session** writes the dirty marker, exactly as a typed edit does, and crash recovery covers
    the import.
  - **In a read-only session** the first insert throws `WriteAccessLostException` before anything is written. The
    transaction rolls back, and the one plain banner appears (`WriteFailureText`).
  - **If the lock is lost during the import:** demotion calls `MarkReadOnly` first, then closes the connection.
    Either the next insert is refused, or the connection closes under an open transaction. In both cases SQLite never
    keeps an uncommitted transaction, so the import lands whole or not at all. Nothing in the demotion path changes.
- **Afterwards** the section list, grid and workload reload, as after any section change (wiring in Phase 5).

### The four rules

1. **Atomic rename of a checked copy.** Unaffected. The import writes only to the open copy. The shared database
   changes only when the normal save copies that file, checks it and renames it into place. Save copies the whole
   file regardless of what changed, so a 300-section import is no different to the save than a one-field edit.
2. **Lock ours, database as last seen.** Unaffected, for the same reason. In a session that doesn't hold the lock,
   `MarkDirty` refuses the first insert.
3. **Never delete the only copy of unsaved work.** The import is unsaved work in the open copy, protected by the
   dirty marker and crash recovery like any edit. The import deletes nothing.
4. **Never confuse the user.** Each problem is one line naming the row, column and value as typed. The done message
   says what was added and where. D4 and D5 below close the two places it could mislead.

### Data-integrity risks (production)

- **A wrong import is a large edit.** Until saved, the user can discard it with the normal unsaved-changes handling.
  After saving, the remedy is Empty semester (D5). Courses, instructors and setup values the import created **stay**
  after emptying. Nothing refers to them any more, so the user can delete them, but the done message doesn't promise
  that.
- **A typo creates a record** (A4). "Lectrue" in the Section Type column becomes a new section type, not an error.
  The defences: the preview lists every new value under "New in TermPoint", and near-miss warnings point at likely
  typos. All before anything is written. If one slips through, it can be fixed afterwards: point the sections at
  the right value and delete the stray one. Phase 6 adds tests for the near-miss rule.
- **New records are incomplete** (A4). Rooms have no capacity or type; subjects have only their abbreviation as a
  name. Nothing breaks, but the room finder and type filters skip such rooms until they're filled in. The done
  message points to Rooms.
- **Existing records are never updated.** The import calls only `Insert`, never `Update`. Phase 6 adds a test that
  asserts no existing row's `data` changes across an import.
- **Unicode case.** In-memory comparison (`OrdinalIgnoreCase`) and SQLite's `LOWER()` (ASCII only) differ on accented
  letters, so `Écol` vs `écol` could match in memory but not in `ExistsBySectionCode`. That matters only if both paths
  are used. The import uses only the in-memory one, and the editor's later checks stay as they are.

### Decisions

**Decided (2026-10-24), all as recommended:**

| # | Decision |
|---|---|
| D1 Blank Capacity | The course's capacity, else the default section capacity, as the editor seeds a new section |
| D2 Tags | The file's tags plus the course's tags, as the editor does for a new section |
| D3 Times outside 07:30–22:00 | An error, not clamped |
| D4 Data changed between Check and Import | At Import, rebuild the snapshot and re-run Resolve and Validate on the rows already read (not the file again). Same result: write. Different: write nothing and show the new result, headed "Something in TermPoint changed since this file was checked. Here is the up-to-date result." |
| D5 Undo hint (U3) | Only when the semester was empty before: "To take it back, switch to another semester, then use Empty semester in Academic Years." (`EmptySemester` refuses the open semester) |
| D6 Subject focus | Import every row regardless of the focus. The done message adds the Dev Tools import's sentence ("3 of them are in subjects you haven't chosen, so they're not shown."); `BuildOutsideChosenSubjectsNote` moves to a shared helper. A new subject is never in the chosen subjects, and the import doesn't change the focus |
| D7 Database ambiguity | A value that matches two records is an error on the row. Matching none creates a record (A4) |
| D8 Timed remote meetings | Out of scope for 1.2.3. A section with no meeting times covers online/TBA. A timed remote meeting imports with no room, and the user marks it remote in the editor |
| D9 Missing columns | Every template column must be present; a missing one is a single error. Extra columns are ignored |
| D10 Weekend day turned off | An error telling the user to turn the day on in Settings and check again |
| D11 Overlapping meetings in one section | A warning, not an error, and none when the two never meet in the same week. Changes Phase 2's invariant 7 |
