# Section Import by User — Progressive Design Spec

**Status:** Phases 1–3 LOCKED (2026-10-20); amended 2026-10-24 by A4 (setup values are created as needed, marked
inline). Phase 4 (Data Design) LOCKED (2026-10-24). Phase 5 (Architecture) LOCKED (2026-10-24). Phase 6 (Implementation Plan) LOCKED (2026-10-24). **Design complete**; implementation is item 24 in `spec_version_1.2.3.md`.
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
  - Warnings: one per problem, with the rows it affects (none for a file-wide warning); grouped, not one per row (2026-10-24, Phase 4).
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

- **Course:** SubjectId, CalendarCode, Title, IsActive, Level. Capacity and Tags are left empty. *(Changed
  2026-10-25: Level was left empty, but the course editor shows a level suggested from the number when none is
  stored, so the course looked levelled while its sections had none and the level filter never showed them. A new
  course now gets the level the editor would suggest.)*
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

### Warnings (shown in the preview; they don't block; one line per problem, not per row: see Phase 4, 2026-10-24)

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
- *(2026-10-25, after Greg first saw the flyout)* The same instructions can also be read **inside the flyout**
  (Show instructions, expanding in place) and saved as a **web page** (Save instructions…, a self-contained
  `.html` that prints cleanly). One text file, `src/TermPoint/Assets/Templates/SectionImportInstructions.txt`
  (moved from `tools/section-import/instructions.txt`), feeds the Instructions sheet, the flyout and the web page;
  `TemplateWorkbookTests` fails if the workbook falls behind it.
- The import reads the sheet named **Sections**. A CSV is read as that sheet.

### The flyout: "Import Sections"

It opens like the other flyouts and works top to bottom.

*(2026-10-25, after Greg first saw the flyout: the first screen was too bare. It now opens with a short
introduction ("TermPoint can add a whole semester's sections for you from a spreadsheet…") above the target line,
then four numbered steps: **Get the template** (Save template…), **Fill it in** (Show instructions / Save
instructions…, the instructions expanding under this step), **Choose your file** (Choose file…), and **Review and
import**; a faint footer says re-importing a corrected file is safe. The Problems screen also has Show instructions,
sharing one show/hide setting with the first screen.)*

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
     - Warnings, if any, listed in amber. They don't block. *(2026-10-24: one line per problem, listing the rows it affects; see Phase 4.)*
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
    List<ImportWarning> Warnings;   // Kind, Rows (empty = file-wide), Message; one per problem (grouped)
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
| `Level` | `CourseLevelParser.ParseLevel` of the Course Number with spaces removed (`485` → `400`), or `""` when it has no clean three-digit block (added 2026-10-25) |
| `TagIds`, `Capacity` | empty / null |

**Instructor** (one per `NewInstructor`)

| Field | Value |
|---|---|
| `Id` | new GUID |
| `LastName`, `FirstName` | from `Last, First`, trimmed, as typed (case kept). **A last name is required** (the instructor editor won't save one without it): a new instructor with no last name (e.g. `, John`) is a problem on that row (added 2026-10-24) |
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
| `Level` | the course's stored `Level` (as the editor does); for a new course, the level given above |
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

### Warnings are grouped: one line per problem, not per row (added 2026-10-24)
*Correction (2026-10-24, step 24.4): legal start times are edited in Configuration → Scheduling, not Academic Years (which only copies them when a year is created). The messages below say so.*

The user must never be buried under warnings. A file where 30 meetings start at 10:45, and 10:45 isn't a legal start
time, gets **one** warning, not 30:

> 1045 isn't a legal start time for 1.5-hour meetings (30 meetings: rows 4, 9, 12, 15, 18 and 25 more). To make it
> one, add it in Configuration, under Scheduling, and check again.

- **Grouping key:** the warning's kind plus the value(s) it is about. Rows whose warning would read the same, apart
  from the row number, share one line:

  | Warning | Grouped by |
  |---|---|
  | Begin isn't a legal start time | the time and the meeting length |
  | An existing course's title differs | the course |
  | A matched course or instructor is inactive | the course or instructor |
  | A new setup value is close to an existing one | the new value (it is planned once anyway) |
  | A new instructor shares a last name with an existing one | the new instructor (planned once anyway) |
  | A room's campus differs from the section's campus | the room and the two campuses |
  | Two meetings of one section overlap (D11) | the section (one line per section, naming its rows) |
  | No legal start times at all | — (one file-wide line, no rows) |

- **Rows:** listed in file order. The first five are named and the rest counted ("and 25 more"), so a line never
  grows long. The count says what's being counted ("30 meetings", "12 sections").
- **A hint when there's a real fix:** the line says how to make the warning go away, where TermPoint can do it (add
  the start time in Configuration, under Scheduling; reactivate the course in Courses), then check again.
- **Order:** file-wide warnings first, then the rest by the first row they affect.
- **What isn't grouped: problems (errors).** Each problem must be fixed in its own row, and Copy list gives one line
  per row (Row, Column, Problem) so the user can sort it and tick rows off in Excel. But a fault that isn't really
  about rows is reported **once**, and stops the check there:
  - **a missing column** (D9): "The Tags column is missing.";
  - **the whole file names another semester** (A2): when every row's Academic Year and Term agree with each other but
    not with the open semester, one problem: "This file is for Spring 2026 (2026-2027), but Fall 2026 is open. Open
    Spring 2026 to import it, or correct the Academic Year and Term columns." Only rows that disagree with the rest
    of the file are reported row by row.

In the transient shapes this means warnings are their own type, `ImportWarning` (kind, `IReadOnlyList<int> Rows`
empty for a file-wide warning, message), rather than one `ImportProblem` per row. The planner builds the groups; the
view model only formats them.

### Near-miss warnings for new setup values (A4)

The preview lists every new value. The warning points at the ones most likely to be typos. A new value gets a
warning when, against an existing value **of the same kind** or another new one:
- **subjects, section types, meeting types, tags, campuses:** one contains the other (`Lec` / `Lecture`), or they
  differ by at most two letters (a small edit-distance helper; `Writng` / `Writing`). Comparison ignores case, spaces
  and punctuation, and names under four letters only get the contains check, so `LAB` vs `LAW` isn't flagged.
  *Refined in step 24.1 (2026-10-24):* the contains check needs the shorter name to have at least 3 letters (so a
  one-letter tag isn't flagged against everything), and **names with different numbers are never a near miss**
  (`Lab 1` / `Lab 2`, `Year 1` / `Year 2`, `Lab` / `Lab 1`): numbers mark distinct things;
- **rooms:** the **building** is new. Neighbouring room numbers differ by one character by nature, so comparing
  numbers would flag every room. A new building is rare and worth a look ("Will add building SCIENCE. SCI already
  exists."). *Refined in step 24.4 (2026-10-24): only when the new building is a near miss of an existing (or another new) building. Warning about every new building would bury a first import into an empty database, and the preview already lists every new record.*

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
| Legal start time for the length | The Start list offers the start times configured for the meeting's length. For a length with no configured times, it offers every configured start time. Any time may still be typed | **Warning** (P1), by the same rule: Begin is checked against the start times for its length when that length has any, else against all of them. **If the academic year has no legal start times at all**, nothing is checked per row; the preview shows one warning instead (2026-10-24, below) |
| Two meetings of one section on the same day overlapping | **Not checked.** The editor allows it | **Warning**, not an error (D11). No warning when the two can't meet in the same week (odd vs even, or custom weeks with none in common) |

The limits come from one place: `SectionMeetingViewModel.MinStartMinutes` / `MaxEndMinutes` move to a shared spot in
Phase 5, so the editor and the import can't drift apart. The same goes for the day names, the time parsing and the
custom-week check, which today are copied across the editor, the Dev Tools import and the shared-schedule parser.

*Correction (2026-10-24): an earlier draft checked the legal-start-time warning against every start time in the
academic year. The editor narrows the list by meeting length, and the import now does the same.*

**An academic year with no legal start times** (added 2026-10-24, after Phase 4 was locked). This happens when the
user clears every block length in the startup wizard, or says No to copying start times when adding a new academic
year (`AcademicYearListViewModel`; the year then starts empty). The rule above would then warn on every meeting in the
file. A warning on every row tells the user nothing and buries the warnings that matter (rule 4: one message per
problem). So:
- no per-row legal-start-time warnings are produced;
- the preview shows **one** warning, with no row number: "2026-2027 has no legal start times set up, so start times
  weren't checked. You can add them in Configuration, under Scheduling.";
- nothing else changes. The 07:30–22:00 limits (D3) don't depend on legal start times and are always checked.

The editor behaves the same way in that state: its Start list is empty and any time can be typed. "A length that
has start times" means a `LegalStartTime` entry with at least one time; an entry with an empty list counts as none,
as in the editor's `ComputeStartTimeSuggestions`.

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

## Phase 5: Architecture (LOCKED 2026-10-24)

### Shape

A pure core that knows nothing about the UI, a thin service that adds the database and the file system, and one
flyout view model. Everything that decides what gets written can be unit-tested without Avalonia.

```
 Files → Import Sections…  (MainWindowViewModel: command + reason it's unavailable)
        │
        ▼
 ImportSectionsViewModel ──── ImportSectionsView.axaml   (flyout; stages, lists, buttons)
        │  pickers, clipboard: MainWindowReference (existing pattern)
        ▼
 SectionImportService       (orchestration: Check, Import; threading; deadline-bounded file IO)
   ├─ ImportFileReader      (.xlsx via ExcelDataReader | .csv via CsvRecordReader + ParseCsvRow) → ImportSheet
   ├─ ImportSetupSnapshot   (built from the repositories, UI thread)
   ├─ SectionImportPlanner  (pure: Group → Resolve → Validate) → ImportReport
   └─ SectionImportWriter   (one transaction through the repository Inserts)
```

New code goes in `Services/SectionImport/` (namespace `TermPoint.Services.SectionImport`), apart from the view model
and view, which sit with the other flyouts in `ViewModels/Management` and `Views/Management`.

### Components

| Component | Kind | Job | Depends on |
|---|---|---|---|
| `SectionImportColumns` | static | **The one list of template columns:** name, level (section / meeting), required. Used by the reader (header check, D9), the planner, the Copy-list column names, and a test against the shipped workbook | — |
| `ImportFileReader` | class, desktop only | Reads bytes into an `ImportSheet` (headers + `ImportRow`s), or a single file problem ("This file has no sheet named Sections."). `.xlsx`: ExcelDataReader, sheet "Sections", every cell turned into text (Phase 4 rules). `.csv`: `CsvRecordReader` + `SharedScheduleCsvParser.ParseCsvRow`, as today. Blank rows dropped. Enforces the size limits (E4) | ExcelDataReader |
| `ImportSetupSnapshot` | record + `Build(...)` | Every lookup in Phase 4's table, read once from the repositories, indexed by normalised name. Holds no connection after it's built | repositories, `SemesterContext`, `AppSettings` |
| `SectionImportPlanner` | static, pure | `Plan(ImportSheet, ImportSetupSnapshot) → ImportReport`. Groups rows into sections, resolves every value (existing / new / ambiguous), applies every rule in Phases 2 and 4, builds the new records (fresh ids), and produces the problems, warnings, skipped sections and plan | the shared helpers below |
| `SectionImportWriter` | class | `Write(ImportPlan) → ImportCounts`. One transaction, Phase 4's order, repository `Insert`s only. Rolls back and rethrows on any failure | `IDatabaseContext`, the seven repositories |
| `SectionImportService` | class (DI, transient) | `CheckAsync(path)`: reads the file off the UI thread with a deadline, then builds the snapshot and plans on the UI thread. `Import(sheet, shownReport)`: rebuilds the snapshot, re-plans, compares with what the user saw (D4), writes if equal. Returns either the counts or the new report | reader, snapshot, planner, writer |
| `ImportSectionsViewModel` | VM (DI, transient) | The flyout. Stages, commands, the text of every message | service, `SemesterContext`, `FocusService`, `MainWindowViewModel` |
| `ImportSectionsView` | AXAML | Lays out the stages. No code-behind beyond the generated constructor | — |

"Compares with what the user saw" (D4) means: the same problems, warnings, skipped sections and planned new records
(compared by their keys and names, not their fresh ids). If any line the user saw would read differently, it's a
change.

### Shared helpers (extracted, so the editor and the import can't drift)

| Helper | From | Used by |
|---|---|---|
| `MeetingTimeLimits.MinStartMinutes` / `MaxEndMinutes` | `SectionMeetingViewModel` constants (which then refer to these) | editor, planner |
| `ScheduleText.ParseDays` | the three copies of `DayMap` (Dev Tools import, shared-schedule parser, Dev Tools section VM) | planner. Adds the compact runs from P2 (`MWF`, `TR`, `TTh`): two-letter names (`Th`, `Tu`, `Sa`, `Su`) are tried before one-letter ones |
| `ScheduleText.ParseTime` | editor's `ParseTime` (`HHMM`) + Dev Tools `h:mm tt` | planner. Accepts `1000`, `10:00`, `10:00 AM`. The editor keeps calling its own `HHMM`-only method, unchanged |
| `ScheduleText.ParseFrequency` | editor's custom-weeks check + shared-schedule `IsValidFrequency` | planner and the editor's `ValidateCustomWeeks` (same rules, same messages) |
| `NameMatch.Normalize` / `NameMatch.IsNearMiss` | new | planner. Normalisation rule and near-miss rule from Phase 4; a small edit-distance function |
| `InstructorInitials.Generate(first, last, taken)` | `InstructorImportViewModel.ResolveInitials` (asks the database per candidate) | planner, with a set of taken initials (database + this import) |
| `OutsideChosenSubjectsNote.Build(focus, courses, courseIds)` | `SectionImportViewModel.BuildOutsideChosenSubjectsNote` (Dev Tools, retiring) | new VM (D6). Its tests (`SectionImportFocusTests`) move with it |

The shared-schedule parser keeps its own copies for now (E3).

### Threading

- **Reading the file:** `NetworkFileOps.RunAsync` around `File.ReadAllBytes`, off the UI thread, deadline-bounded
  like every other file read (the lockup audit). A file on an unresponsive share gives "TermPoint couldn't read this
  file. If it's on a network drive, copy it to your computer and try again." Parsing the bytes into an `ImportSheet`
  runs on the same background task.
- **Snapshot, planning, writing:** UI thread. The repositories share one SQLite connection, which isn't thread-safe.
  The planner works on in-memory lists of at most a few thousand rows, and the Phase D baseline puts bulk database
  work at ≤ 30 ms. "Checking…" shows during the read; the rest is too quick to need a spinner.
- **Picker:** `await Task.Yield()` before `OpenFilePickerAsync`, as the existing imports do. The flyout is not a
  window, so the picker-then-close deadlock (Avalonia 12) doesn't apply.

### Wiring into the app

- **Menu:** Files → **Import Sections…**, after Save, before the Recent Files separator.
  `NavigateToImportSectionsCommand` opens the flyout with `OpenFlyout<ImportSectionsViewModel>("Import Sections")`.
- **Availability:** one function, `ImportAvailability.WhyUnavailable(isWriteEnabled, semesterContext,
  sectionListVm)`, returns null or the reason (Phase 3's three tooltips, in that order). It drives both the menu
  item's `CanExecute` and tooltip (`ToolTip.ShowOnDisabled`) and the flyout's **Import** button, because any of the
  three can change while the flyout is open (lost lock, another semester chosen, an editor opened from a detached
  panel). Re-evaluated when `IsWriteEnabled`, `SemesterContext.SelectedSemesters` or `SectionListVm.IsEditing`
  changes.
  - **Why an open section editor blocks it:** the import ends with a section-list reload. A reload while an editor is
    open would close it, and its unapplied changes would be lost (F3 in the persistence review).
- **After a write:** `SectionListVm.ReloadFromDatabase()`, which is what the read-only Refresh button calls. It
  reloads `SectionStore`, and `SectionsChanged` refreshes the list, grid and workload. The list rebuilds its course,
  instructor and room lookups on load. The section editor, the subject picker and the management flyouts read their
  lists from the repositories when opened. So nothing else needs telling.
- **DI:** `SectionImportService`, `SectionImportWriter`, `ImportFileReader` and `ImportSectionsViewModel` are
  registered transient in the desktop `ConfigureServices`, inside `#if !BROWSER` beside the registrations they
  replace. Nothing is added to `ConfigureDemoServices`: the feature is desktop only, and the menu item is hidden in
  the demo (`PlatformCapabilities.SupportsFileDialogs`).
- **Package:** `ExcelDataReader` (MIT, read-only) in the existing desktop-only `ItemGroup` of `TermPoint.csproj`
  (`Condition="'$(TargetFramework)' == 'net10.0'"`). Pin the latest stable 3.x version that's been out at least a few
  weeks, and confirm its licence at implementation. *Step 24.2 (2026-10-24): ExcelDataReader 3.9.0 (MIT). Code-page registration **is** needed even for `.xlsx` (its configuration looks up code page 1252 when created), so `ImportFileReader` registers `CodePagesEncodingProvider` (in the shared framework, no package) in its static constructor.*
  Code that names ExcelDataReader sits under `#if !BROWSER`. The desktop build isn't
  trimmed, so no trimming roots are needed.
- **The template workbook:** `Assets/Templates/TermPointSectionsTemplate.xlsx` (no spaces: it's loaded by an `avares://` address; the Save dialog suggests "TermPoint Sections Template.xlsx"; step 24.6), already covered by the
  `AvaloniaResource Include="Assets/**"` glob. **Save template…** opens it with `AssetLoader.Open`, asks where to
  save (`SaveFilePickerAsync`), and writes it with `NetworkFileOps.RunAsync` (deadline-bounded). How the workbook is
  made: E1.
- **Colours and fonts:** the warning list uses the existing amber `NotificationWarning*` brushes, problems use
  `TextError`, and sizes come from `App.axaml`. Nothing is hard-coded.

### The view model's stages

`Stage` is one enum, and the view shows one panel per stage (no class-toggling):

| Stage | Shown | Commands enabled |
|---|---|---|
| `Ready to choose` | target line; **Save template…**, **Choose file…** | Save template, Choose file |
| `Checking` | "Checking…" | — |
| `Problems` | the problems list; **Copy list**, **Check again**, **Choose file…** | Copy list, Check again, Choose file |
| `Ready to import` | the summary, the expandable lists (sections, courses, instructors, "New in TermPoint"), the warnings; **Import**, **Cancel** | Import (if available), Cancel |
| `Nothing to add` | "Every section in this file is already in Fall 2026."; **Choose file…** | Choose file |
| `Done` | the done message (with the D5 and D6 sentences and the new-rooms line when they apply); **Choose file…** | Choose file |

- **Copy list:** tab-separated `Row`, `Column`, `Problem` with a header line, written with the window's clipboard
  (`TopLevel.Clipboard`); confirm the exact Avalonia 12 call at implementation.
- **D4 at Import:** if the service reports that the plan changed, the stage becomes `Problems` or `Ready to import`
  again, showing the new report and its heading. Nothing is written.
- **A failed write** (an exception from the writer, after the rollback): `WriteFailureText` decides the message, the
  same way the editors do. A refused read-only write shows the one plain banner. Anything else shows "The sections
  couldn't be added. Nothing was changed." The log gets the detail, the user doesn't.
- **Closing the flyout** at any stage discards the report. Nothing has been written unless the stage is `Done`.

### The four rules (architecture check)

1. and 2. Unchanged from Phase 4: only `SectionImportWriter` writes, only through repository `Insert`s into the open
   copy; the save pipeline is untouched.
3. The writer's transaction is the only write. A failure rolls it back, so there's never a half-import to lose or
   to mistake for real work.
4. Every string the user can see is built in one place, the view model (with the planner's problem and warning
   lines), and is plain: no paths in messages, no exception text, no "working copy".

### Retiring the Dev Tools import (Q7)

As the **last** implementation step, in a commit of its own so it can be reverted alone:
- **Remove:** the Dev Tools "CSV Import…" menu item and command; `CsvImportViewModel`, `InstructorImportViewModel`,
  `CourseImportViewModel`, `SectionImportViewModel`, their four views, `InstructorPreviewRow`,
  `MappingEntryViewModel`, `CsvImportParser`, `CsvImportMatcher`, `CsvImportModels` (once nothing else uses them);
  `CsvImportParserTests`, `CsvImportMatcherTests`; the DI registration.
- **Keep:** `CsvRecordReader` (and its tests) and `SharedScheduleCsvParser`, which the shared-schedule overlay and the
  new reader use.
- **Move:** `SectionImportFocusTests` → tests of `OutsideChosenSubjectsNote`.

### Decisions

**Decided (2026-10-24), all as recommended:**

| # | Decision |
|---|---|
| E1 Template workbook | Generated by a script in `tools/` (Python + openpyxl) from the column list and a plain-text Instructions file kept beside it. The `.xlsx` is checked in. A test reads the shipped workbook with `ImportFileReader`, checks its headers match `SectionImportColumns`, and checks its sample rows plan with no errors against a test database |
| E2 Retiring the Dev Tools import | The last step of this feature, in 1.2.3, in its own commit |
| E3 Shared-schedule parser | Keeps its own day and frequency rules for now; switched to `ScheduleText` in 1.2.4 |
| E4 File limits | At most 10 MB and 5,000 data rows. Beyond that, one problem: "This file is larger than TermPoint can import at once. Split it by subject." |

## Phase 6: Implementation Plan (LOCKED 2026-10-24)

### How the steps are sized and checked

- **One step = one session's work**, sized for Sonnet to code with Opus reviewing (the 1.2.3 practice). Each step
  ends compiling (`dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`) with its
  own tests passing, and the full suite still green. Each is committed on its own.
- **Nothing a user can see changes until step 8.** Steps 1–7 add code and tests behind no menu item, so the branch
  stays releasable after every step.
- **Shipped behaviour is touched in only four places:** the editor's custom-week check and time limits (step 1), the
  main view model's new command (step 7), `MainView.axaml` (step 8), and the Dev Tools removal (step 10). Each of
  those steps names the existing tests that guard it.
- **AXAML:** steps 8 (new view, `MainView.axaml`) and 10 (deleting views) edit AXAML. Warn the user first, so the
  files can be closed in Visual Studio.

### Steps

| Step | What | Done when | After |
|---|---|---|---|
| **1** | **Shared helpers.** `MeetingTimeLimits` (the editor's `MinStartMinutes` / `MaxEndMinutes` now refer to it); `ScheduleText.ParseDays` (day names + compact runs `MWF`, `TR`, `TTh`), `ParseTime` (`1000`, `10:00`, `10:00 AM`, Excel-style `HH:mm`), `ParseFrequency` (blank / weekly / odd / even / "odd weeks" / week numbers; editor's rules and messages); `NameMatch.Normalize` and `IsNearMiss` (contains, or edit distance ≤ 2, with the four-letter floor); `InstructorInitials.Generate(first, last, taken)`. The editor's `ValidateCustomWeeks` calls `ParseFrequency`. | Unit tests for each helper, including every day alias and compact run, ambiguous input (`S`), and the four-letter floor. **Guard:** `EditorFlowTests` and the meeting-row tests pass unchanged, and the editor shows the same custom-week messages word for word | — |
| **2** | **Reading files.** Add `ExcelDataReader` (desktop-only `ItemGroup`). `SectionImportColumns`; `ImportSheet` / `ImportRow`; `ImportFileReader` for `.csv` (`CsvRecordReader` + `ParseCsvRow`) and `.xlsx` (sheet "Sections"): header matching, missing-column problem (D9), cell-to-text rules (whole numbers, Excel times, text), blank rows dropped, 10 MB / 5,000-row limits (E4), plain problems for an unreadable file or a missing sheet. The tests build their workbooks in code with a small `XlsxTestWorkbook` helper (a zip of minimal OpenXML parts), so there are no binary fixtures and no Python dependency (changed 2026-10-24: openpyxl isn't installed; it's still planned for the template in step 6). | Reader tests over CSV and `.xlsx` fixtures: quoted fields with commas and line breaks, numeric and time cells, a header in another order, extra columns ignored, a missing column, a blank row in the middle, a file over each limit, no Sections sheet, a non-Excel file renamed `.xlsx` | — |
| **3** | **Planner, part 1: group and resolve.** The transient types (Phase 4); `ImportSetupSnapshot` and `Build` from the repositories; `SectionImportPlanner`: group rows into sections; the file-wide semester check (one problem when the whole file names another semester); match or plan every subject, course, instructor, section type, meeting type, tag, campus and room (existing / new / two matches = error, D7); new-record rules (subject name = abbreviation, a new room needs a building, new course code, initials); skipped sections create nothing. | Pure unit tests with hand-built snapshots: each kind matched, created once though named on many rows, and ambiguous; course codes equal ignoring spaces; a new instructor sharing a name with nobody; two new instructors whose initials collide; a skipped section naming an unknown room creates nothing; a whole-file wrong semester gives exactly one problem | 1, 2 |
| **4** | **Planner, part 2: validate and warn.** Required cells; a section's rows agree (invariant 4); a new course has one title; Capacity a whole number; every meeting-time rule in Phase 4's table (days, weekend days off D10, 07:30–22:00 D3, End after Begin, frequency, overlaps as warnings D11); section values (D1 capacity, D2 tags, Level). Warnings, **grouped** (one line per problem, five rows named then counted, fix hints): title differs, inactive, near-miss setup values, new instructor's last name, room campus, legal start times (by length, and the single file-wide warning when there are none). | Pure unit tests for every rule and both sides of each limit (07:29 / 07:30, 22:00 / 22:01); odd and even meetings at the same time give no warning; 30 meetings at an illegal time give one warning naming five rows; a year with no start times gives one file-wide warning; D1 and D2 values; a Saturday meeting with Saturday off | 3 |
| **5** | **Writing.** `SectionImportWriter` (one transaction; setup values, courses, instructors, sections, in that order; `Insert` only). `SectionImportService.CheckAsync` (file read off the UI thread through `NetworkFileOps.RunAsync`) and `Import` (rebuild the snapshot, re-plan, compare with what the user saw (D4), write if unchanged). | Tests against a temporary database (`new DatabaseContext(path)`): everything planned is inserted and readable through the repositories; **no existing row's `data` changes**; importing the same file twice adds nothing the second time; a failure halfway rolls back everything; a read-only context (`readOnly: true`) refuses before writing anything; a change made between Check and Import (a section added) is detected and nothing is written; the readable columns (`course_code`, `semester_name`, …) are filled | 4 |
| **6** | **The template workbook.** `tools/section-import/make_template.py` builds `Assets/Templates/TermPointSectionsTemplate.xlsx` (no spaces: it's loaded by an `avares://` address; the Save dialog suggests "TermPoint Sections Template.xlsx"; step 24.6) from the column list and `instructions.txt` (Sections sheet with sample rows, including one section on two rows; Instructions sheet; code and time columns formatted as Text). *(2026-10-25: the instructions file moved to `src/TermPoint/Assets/Templates/SectionImportInstructions.txt`; see Phase 3.)* | A test reads the shipped workbook with `ImportFileReader`: its headers equal `SectionImportColumns`, and its sample rows plan with **no problems** against a test database. Greg reads the Instructions sheet | 2, 4 |
| **7** | **The flyout's view model and wiring.** Move `BuildOutsideChosenSubjectsNote` to `OutsideChosenSubjectsNote` (its tests move too); `ImportAvailability.WhyUnavailable`; `ImportSectionsViewModel` (stages, every message, Copy list text, Save template, the D4 / D5 / D6 sentences, the new-rooms line, `ReloadFromDatabase` after a write, `WriteFailureText` for failures); `NavigateToImportSectionsCommand` and its tooltip on `MainWindowViewModel`; DI registrations. File picking stays in the command; the work is in methods that take a path, so tests can drive them. | View-model tests with a temporary database and fixture files: each stage reached; the done message with and without the undo hint (semester empty before or not), the outside-the-focus sentence, and the new-rooms line; Copy list's tab-separated text; Import disabled with the right reason for each of the three conditions, including when one changes while the flyout is open | 5 |
| **8** | **The view.** `ImportSectionsView.axaml` (one panel per stage; expandable lists; amber warnings; brushes and sizes from `AppColors.axaml` / `App.axaml`); Files → Import Sections… in `MainView.axaml`, hidden in the demo. **AXAML: warn first.** | Compile check, then run the app (desktop): screenshots of each stage with a fixture file; the menu item disabled with its tooltip in each unavailable state; nothing in the WASM build | 6, 7 |
| **9** | **Verification on real data.** Full test suite (with a `.trx`). Then by hand, on a **copy** of a real database: first import into an empty semester; the same file again (nothing added); a corrected file (only the new sections); a file with problems, Copy list pasted into Excel; a change made from a detached panel between Check and Import; a read-only session; Save, close, reopen. **`MinimumVersionToWrite` check:** open the saved copy in 1.2.2, edit and save, reopen in 1.2.3, and confirm nothing was lost. | Every scenario passes; findings fixed or recorded | 8 |
| **10** | **Retire the Dev Tools import** (E2), in its own commit: remove what Phase 5 lists; keep `CsvRecordReader` and `SharedScheduleCsvParser`. **AXAML: warn first** (four views deleted). | Compile check; full suite green; the shared-schedule overlay still imports a CSV (`SharedScheduleCsvParserTests`, and a quick hand check) | 9 |

Steps 5 and 6 can run side by side, as can 6 and 7.

### Release notes for this feature

- **`MinimumVersionToWrite`: No** (Phase 4). Step 9 confirms it with a 1.2.2 round trip. The Store submission isn't
  mandatory on this feature's account.
- **Data migration:** none. No schema change, no new table, no change to any existing record's shape.
- **Rollback:** the feature is self-contained. Removing the menu item hides it. Step 10 reverts on its own.
- **User-facing:** a new Files menu item; the Dev Tools CSV import is gone (Dev Tools is hidden, so only Greg used
  it).

### Decisions

**Decided (2026-10-24):**

| # | Decision |
|---|---|
| F1 The 1.2.3 spec | Added to `spec_version_1.2.3.md` as item 24 (Items table, implementation order, a section with the step table pointing here). The 1.2.4 candidate list points to item 24 |
