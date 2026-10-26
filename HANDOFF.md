# Handoff: Section Import by User (item 24), 2026-10-25

Written on the laptop at the end of a long session, for continuing on the dev machine with a new agent. Claude Code's
memory and `CLAUDE.md` don't travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is
here or in the files this points to.

**To start the new agent, say something like:** "Read `HANDOFF.md`, then let's continue item 24."

## Where things are

- **Branch:** `1.2.3_new_features`, pushed to `origin`. It holds item 20 (subject focus) and item 24 (this feature).
- **The other 1.2.3 branch:** `Version_1.2.3` holds the persistence work. Greg still has its field testing to do. It
  is 4 commits ahead of where `1.2.3_new_features` split off (meeting-time validation, meeting-row display, Room
  Browser tests). The two branches are merged later; both already touch `App.axaml`, `AppColors.axaml`,
  `SectionEditViewModel.cs` and `SectionListView.axaml`, so expect small conflicts there.
- **Source of truth for the feature:** `designs/section-import-by-user.md` (all six design phases, locked, with dated
  corrections inline). The implementation plan is Phase 6 there and item 24 in `spec_version_1.2.3.md`.

## What item 24 is

A department administrator fills in a TermPoint template workbook (`.xlsx`, or a CSV with the same columns) and
imports a semester's sections with **Files → Import Sections…**. TermPoint checks the whole file first (all or
nothing), creates any courses, instructors, subjects, section types, meeting types, tags, campuses and rooms it names
that don't exist yet, skips sections already in the semester, and lists everything new before anything is written.
It replaces the hidden Dev Tools CSV import.

## Done: steps 24.1–24.8

All committed and pushed. Full suite after 24.8: **3,600 passed, 0 failed, 79 skipped** (the skips are the
`[FactRequiresLocalDb]` tests).

| Step | Commit | What |
|---|---|---|
| 24.1 | 6cb6f7f | Shared helpers in `Services/`: `MeetingTimeLimits`, `ScheduleText` (days incl. `MWF`/`TTh`, times, week lists, frequencies), `NameMatch` (normalise, codes, near-miss), `InstructorInitials`. The meeting editor's custom-weeks check now uses `ScheduleText.ParseWeekList` (messages unchanged, pinned by a test). |
| 24.2 | fe8d192 | `Services/SectionImport/`: `SectionImportColumns`, `ImportFileReader` (.xlsx via **ExcelDataReader 3.9.0**, .csv via `CsvRecordReader`), limits 10 MB / 5,000 rows. Tests build .xlsx in code (`TestSupport/XlsxTestWorkbook`). |
| 24.3 | 62bbaf2 | Planner part 1: `ImportSetupSnapshot` (+ builder), grouping, matching or creating every value, the whole-file wrong-semester check. |
| 24.4 | 288fe55 | Planner part 2: every validation rule, final section values (D1 capacity, D2 tags, Level), grouped warnings (one line per problem). |
| 24.5 | ccf3c34 | `SectionImportWriter` (one transaction, insert only) and `SectionImportService` (deadline-bounded read that works while the file is open in Excel; re-check at Import, D4). Tests on real temporary databases. |
| 24.6 | 3579bbd | Template: `tools/section-import/make_template.py` (openpyxl) builds `src/TermPoint/Assets/Templates/TermPointSectionsTemplate.xlsx` from `instructions.txt`. Guarded by `TemplateWorkbookTests`. |
| 24.7 | 3231fb6 | `ImportSectionsViewModel` (desktop only), `ImportAvailability`, the main window's command, DI. Seams `IImportSectionsHost` / `IImportSectionsPlatform` keep it testable. |
| 24.8 | 10665ac | `Views/Management/ImportSectionsView.axaml` and **Files → Import Sections...** in `MainView.axaml` (hidden in the WASM demo). |
| 24.8b | (after 0616ec3) | Friendlier first screen, after Greg first saw it (2026-10-25, dev machine): intro, four numbered steps, the instructions shown in the flyout (Show instructions, also on the Problems screen) and saved as a web page (Save instructions…). The instructions text moved to `Assets/Templates/SectionImportInstructions.txt`, the one source for the workbook sheet, the flyout and the page; `TemplateWorkbookTests` fails if the workbook falls behind it. Greg's first wording edits to the screen and the instructions are in; template regenerated. Full suite before Greg's edits: 3,637 passed, 0 failed, 79 skipped; import tests after them: 505/505. |

## Next

1. **Greg tries the rest of the flyout in the running app** (the first screen was seen and reworked in 24.8b). Use a
   database where nothing matters, or press Cancel rather than Import:
   - Files → Import Sections… → **Save template…**, then **Choose file…** with that template. With Fall 2026-2027
     open you get the Ready screen; with another semester open, one "This file is for Fall (2026-2027)…" problem.
   - The menu item should grey out, with a tooltip, while a section is being edited or several semesters are shown.
   - Report anything that looks off or reads wrong; fixes go to a Sonnet brief.
2. **Step 24.9: hand check on a copy of a real database** (design Phase 6, step 9): first import into an empty
   semester; the same file again (nothing added); a corrected file (only new sections); a file with problems, with
   Copy list pasted into Excel; a change made from a detached panel between Check and Import; a read-only session;
   Save, close, reopen. Then the **`MinimumVersionToWrite` check**: open the saved copy in 1.2.2, edit and save, reopen
   in 1.2.3, confirm nothing was lost (expected answer: No raise needed).
3. **Step 24.10: retire the Dev Tools CSV import** in its own commit (list in the design, Phase 5 "Retiring the Dev
   Tools import"). It deletes four views, so warn Greg to close them in Visual Studio first. Keep `CsvRecordReader`
   and `SharedScheduleCsvParser`. `SectionImportFocusTests` goes with the old view model (its helper,
   `OutsideChosenSubjectsNote`, has its own tests).

## Stress test (2026-10-25, dev machine; tests uncommitted at time of writing)

New tests: `SectionImportStressTests` (fresh databases), `SectionImportStressRealDataTests` (`[FactRequiresLocalDb]`,
temp copy of BIOL-TT.db), helpers `TestSupport/Stress*.cs`. Import filter before the fixes below: 551 passed, 3
skipped (2 findings + the opt-in kit writer); after: 671 passed, 1 skipped. Hand-test kit: set `TERMPOINT_IMPORT_STRESS_KIT` to a folder and run
`--filter FullyQualifiedName~WriteImportStressKit`; it writes `BIOL-stress.db`, 35 numbered files and
`CHECKLIST.md`. Not into Documents: Controlled Folder Access blocks the test runner there. The dev machine's kit is at
`C:\Users\gregs\TermPoint import stress kit` (private data; never commit it).

**Fixed the same day (Greg approved; design Phase 4 "Reading the file" and the Copy list note record the rules):**
1. **Copy list** cells that began with `=`, `+`, `-` or `@` (Excel ran them as formulas) now get an apostrophe.
2. **UTF-16 CSV** is read (byte-order mark), instead of refused as "no template header".
3. **Excel error cells** (`#N/A`, `#REF!`, …) are problems, not blanks (they used to import sections silently
   without their instructor, type or capacity).
4. **A date in a text column** (Excel turned `1-2` into a date) is a problem, not the code `2026-01-02`.
5. **Windows-1252 CSV** (Excel's default "CSV" on Windows) is decoded as 1252, not Latin-1, so `O’Brien` and `–`
   come through. Import tests after the fixes: 671 passed, 1 skipped (the opt-in kit writer); no `FINDING:` skips left.
   Full suite: 3,814 passed, 0 failed, 80 skipped (the usual 79 plus the kit writer).

**Still open (1.2.4 candidates, design silent):**
6. **Zero-width space** in a course number silently creates a second, identical-looking course.
7. **Hidden or filtered-out rows** are imported.
8. **Semicolon- or tab-separated CSV** gets the "no template header" message, which doesn't mention the separator.
9. **Non-breaking spaces** match like spaces, but the first spelling (with the NBSP) is what gets stored.

**Sizes and timings:** the 5,000-row file checks in about 0.6 s and imports in about 0.6 s in the tests. Over 10 MB
is refused in under 20 ms. **Every row broken gives 13,333 problem lines**: by hand (Greg, 2026-10-25) they appeared
in about 5 s and stayed responsive, so no cap is needed.

**Greg's hand run of the kit (2026-10-25):** everything passed except as follows. Excel formats his Excel can't save
(CSV UTF-8, Strict Open XML, encrypted, Unicode text) were skipped. **The 5,000-row file took 13 s to check and 10 s
to import in the app, with the window unresponsive during the import**; switching back to that semester took up to
8 s and a cold open 17 s. Unconfirmed whether that run was under the Visual Studio debugger; the next probe is a
Ctrl+F5 (no debugger) timing of Check, Import and the semester switch. If it stays slow, most of it is the section
list/grid/workload reload of 3,787 sections, not the write: then show an "Importing…" status before the work, and
put large-semester slowness on the 1.2.4 list. **Closing without saving after an import keeps the import**:
TermPoint saves on exit without asking (whether it should ask is a separate 1.2.3 question of Greg's).

**Real-data round trip:** all 61 Fall 2025-2026 sections imported into Winter 2026-2027 with zero field
differences, nothing new created, inserts only. The template doesn't carry section notes (13 of 61) or
non-default workloads (28 of 61): imported instructors get workload 1.0. BIOL-TT.db's 80/110/170/230-minute legal
start times belong to academic-year ids that no longer exist (the known AY-delete orphans).

## Loose ends

- **Instructions text:** Greg will revise `src/TermPoint/Assets/Templates/SectionImportInstructions.txt` (moved
  there from `tools/section-import/instructions.txt` on 2026-10-25, so the flyout and "Save instructions…" can use
  it too). Then regenerate with `python tools/section-import/make_template.py` and commit the `.xlsx` with it.
  **openpyxl is needed on whichever machine regenerates it** (`python -m pip install --user openpyxl`; the laptop
  has 3.1.5). **On the dev machine use `py -3.11`** instead of `python` (plain `python` there is Inkscape's bundled
  copy, with no pip); openpyxl 3.1.5 is installed for it, and it was checked to build byte-identical workbooks to
  the laptop's. The tests catch header drift, and `TemplateWorkbookTests` fails if the Instructions sheet no longer
  matches the text file.
- **1.2.4 list** (`spec_version_1.2.4.md`) got four entries this session: show a room's type in the meeting editor;
  switch the shared-schedule parser to `ScheduleText`; one week-sharing rule (`FrequenciesOverlap` only knows weeks
  1–18); a `FaultSeamTests` flake seen once.
- **Item 20's row** in the 1.2.3 Items table still says "Not started" although its steps are committed on this
  branch. Left for Greg.
- **Not pushed, on purpose:** `exception.txt` (untracked, laptop only).

## Facts learned this session (the design now says these; listed so they aren't rediscovered)

- **Legal start times and the Saturday/Sunday switches** are edited in **Configuration → Scheduling**, not Academic
  Years (which only copies start times when a year is created).
- **A meeting's room type** (`SectionDaySchedule.RoomTypeId`) is the type the meeting *needs*, not a copy of its
  room's; the Room Browser uses it only when no room is chosen. The import leaves it null.
- **Empty semester refuses the open semester**, so the undo hint says to switch semesters first.
- **ExcelDataReader needs `CodePagesEncodingProvider` registered even for .xlsx** (done in `ImportFileReader`'s static
  constructor).
- **The editor clamps times to 07:30–22:00**; the import refuses them instead (D3).
- **Disk space on the laptop** fell because Visual Studio's Diagnostic Tools recording (`%TEMP%\dh`) grew to 2.4 GB
  during a day-long debug session. Stopping debugging or switching the recording off avoids it.

## Working agreements (from `CLAUDE.md` and Greg's standing preferences)

- **Coding is done by a Sonnet subagent.** The main agent writes a self-contained brief, Sonnet implements (no
  commits, no branches), the main agent reviews the diff and re-runs the compile check and the full suite, and Greg
  decides when to commit. Don't implement spec steps directly.
- **Commit only when Greg asks; never create branches; push only when asked.**
- **Warn before editing any `.axaml` file** (Visual Studio doesn't reload it); ask Greg to close the tab first.
- **Compile check:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q` (also
  `-f net10.0-browser` when shared code changes).
- **Full suite:** `dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --nologo -v q`, with a `.trx` logger. If
  `bin\Debug` is locked by Visual Studio, add `-o $env:TEMP/sa-build` and delete that folder afterwards.
- **Never kill processes by image name** (`taskkill /IM`), only by PID.
- **Production cautions:** the app is in production. Anything touching save, lock, restore or recovery is checked
  against the four rules for the shared database first (in `CLAUDE.md`). Ask the `MinimumVersionToWrite` question for
  every release.
- **UI rules:** no hard-coded colours, font sizes or weights (use `AppColors.axaml` / `App.axaml`); reflection
  bindings only (no `x:DataType` or compiled bindings); icons only as `Path`/`StreamGeometry`; MVVM with minimal
  code-behind.
- **Messages to users:** plain words, say what happened, whether their work is safe, and what to do next; no paths,
  no exception text, no "working copy".
