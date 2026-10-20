# Section Import by User — Progressive Design Spec

**Status:** Phases 1–3 LOCKED (2026-10-20). Phase 4 (Data Design) next.
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
- **What a file brings in:** sections, plus any **courses and instructors** they need that don't exist yet.
- **What must already be set up:** subjects, section types, meeting types, tags, rooms and campuses. A file that names
  one that isn't set up fails validation; nothing is created for it.
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
| Subject | section | Must match a subject that is set up. Also the subject of a newly created course |
| Course Number | section | Subject + Course Number identify the course (e.g. CHEM + 101 → CHEM101) |
| Course Title | section | Used when the course is created. For an existing course, a different title is **reported at validation**, and the existing title is kept |
| Section Code | section | Subject + Course Number + Section Code identify the section |
| Section Type | section | Must be set up |
| Instructor(s) | section | Blank = unstaffed. Format and matching: see "Instructors" |
| Tags | section | Separated by semicolons; each must be set up |
| Capacity | section | Whole number or blank |
| Meeting Days | meeting | One or more days, e.g. `M,W` |
| Begin, End | meeting | e.g. `1000`, `10:00`, `10:00 AM`; End after Begin |
| Frequency | meeting | Blank = weekly; `odd`, `even`, or week numbers `1,6,7` (as the editor stores it) |
| Meeting Type | meeting | Blank or set up |
| Building, Room | meeting | Blank = no room. Otherwise must identify one room that is set up |
| Campus | section | Blank = no campus. Otherwise must be set up |

- **Same times on several days (the default):** `M,W` with `1000`–`1200` means a 10:00–12:00 meeting on Monday *and*
  Wednesday.
- **Different times on different days:** use several rows with the same Subject + Course Number + Section Code, one
  per time pattern. They are one section. On the extra rows the section-level columns may be left blank. If one is
  filled in and differs from the section's first row, that's an error.
- Blank optional cells are left empty (null) in TermPoint.

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
  (sections, new courses, new instructors), what will be skipped as already present, and any warnings.
- They confirm, and the sections appear in the open semester, along with any new courses and instructors.
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

### Preliminary checks (confirmed in Phase 4)

- **Four rules:** the import is one large in-app edit to the open copy, saved by the existing save pipeline. Rules 1–2
  hold because it never touches the shared file directly. Rule 3: the edit is unsaved work like any other. Rule 4: the
  validation and result messages are written to it.
- **MinimumVersionToWrite:** it creates ordinary sections, courses and instructors that 1.2.2 can already save, so
  likely **No**.
- **New tables:** none expected.

### Out of scope

- Updating existing sections from a file (reconciling with the registrar's system).
- Importing into several semesters at once.
- Creating rooms, campuses, types, tags or subjects.
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
- **ImportReport:**
  - Errors: row, column, plain message.
  - Warnings: row, message.
  - Skipped sections: already in the semester.
  - The plan: new courses, new instructors and new sections, with counts.

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

### Invariants

1. **All or nothing.** Any error means no write. Writing is one transaction.
2. **Existing records are never changed.** The import only inserts sections, courses and instructors.
3. **A section already in the semester is skipped whole,** including the meetings that rows add to it. (The import
   never adds meetings to an existing section.)
4. **A section's rows agree.** A section-level cell filled on two rows of the same section with different values is
   an error.
5. **A new course has one title.** Two rows giving different titles for the same new course is an error.
6. Required on every row: Academic Year, Term, Subject, Course Number, Section Code. Course Title is required when
   the course is new.
7. **Meetings:** Days, Begin and End are all filled or all blank. All blank = a section with no meeting time
   (online/TBA). Partly filled = error. End must be after Begin. Two meetings of one section on the same day that
   overlap = error.

### What a write creates

- **Course:** SubjectId, CalendarCode, Title, IsActive. Level, Capacity and Tags are left empty.
- **Instructor:** LastName, FirstName, generated Initials (the existing collision-avoiding generator), IsActive.
  Email, notes and staff type are left empty.
- **Section:**
  - CourseId, SemesterId, SectionCode, SectionTypeId, CampusId, TagIds, Capacity;
  - InstructorAssignments with the default workload;
  - one `SectionDaySchedule` per MeetingDraft, with RoomTypeId copied from the room, as the editor does.
- All are new objects of types that already derive from `PersistedRecord`, so there is no extension data to carry.

### Warnings (shown in the preview; they don't block)

- An existing course's title differs from the file's (the existing title is kept).
- A new instructor shares a last name with an existing one.
- A matched course or instructor is inactive.
- A room's campus differs from the section's campus.
- Begin isn't one of the academic year's legal start times (see P1).

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
    first, how to tell apart two people who share a name, and how reloading works.
- The instructions travel with the file, so a colleague preparing the data at their site needs nothing else.
- The import reads the sheet named **Sections**. A CSV is read as that sheet.

### The flyout: "Import Sections"

It opens like the other flyouts and works top to bottom.

1. **Target.** "Sections will be added to **Fall 2026** (2026-2027)." The open semester, always visible.
2. **Choose file…** It reads and checks the file, showing "Checking…" while it works. No changes are made.
3. **Result**, one of three:
   - **Problems.** "This file can't be imported yet. Nothing has been changed." Then a list:
     - Row 12, Room: "SCI 9" isn't set up in TermPoint.
     - Row 15: CHEM101 A has a different Instructor(s) than row 14.

     Below the list:
     - **Copy list** puts the problems on the clipboard as tab-separated text (Row, Column, Problem), so pasting into
       Excel, perhaps beside their own data, gives three columns, and they can sort, tick off and send it on.
       Pasting into an email gives readable lines.
     - **Check again** re-reads the same file after they fix it.
   - **Ready.**
     - "Will add **37 sections**, **5 new courses** and **8 new instructors**. Already in Fall 2026, not changed: 12
       sections."
     - Expandable lists of the new courses, new instructors and sections.
     - Warnings, if any, listed in amber. They don't block.
     - **Import** and **Cancel**.
   - **Nothing to add.** "Every section in this file is already in Fall 2026."
4. **Done.** "Added 37 sections, 5 courses and 8 instructors to Fall 2026." The flyout stays open, so the next
   subject's file can be chosen straight away.

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
  semester (Academic Years)."
