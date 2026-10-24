# Load by Subject — Progressive Design Spec

**Status:** ✅ **DESIGN APPROVED (2026-10-20): approach B, filter-based focus.** See
[Approved design](#approved-design-2026-10-20) at the end. Phase 1 below is kept as the record;
its open questions are answered there.
**Revised 2026-10-23** after a code review against `Version_1.2.3` (`6cead7d`): starting value,
demo, tours, one focus-change guard, settings key, semester banners, and a new in-focus
instructor rule (two-year window, last-year-taught fallback; see *Who counts as an in-focus
instructor*).
**Target release:** 1.2.3. A follow-up ("Subjects taught" on instructors) is planned for 1.2.4.

**Where we left it when parked (2026-10-09).**
- **Two candidate approaches:**
  - **A. Load scoping:** read only chosen subjects from the database; this is the approach this
    Phase 1 draft describes.
  - **B. Filter-based focus:** load everything, add a section-list "Hide" mode that doesn't build
    non-matching cards, remember the filter or named presets, and show an active-focus indicator.
- **Leaning B.** Load scoping's only real advantage was memory. The measurements below show
  memory follows *built cards*, not loaded data. Item 21 cuts card cost about 5×, and B's "Hide"
  builds no cards for out-of-focus sections. B also keeps conflicts, room availability, workload
  totals and bulk operations correct with no special handling, so open questions Q1–Q3 below
  largely disappear under B.
- **Still open under B:** one remembered focus per user per DB vs named presets; whether the
  workload panel follows the focus; the indicator's design.

## Phase 1: Problem Statement

### The problem

TermPoint is aimed at a **department**: a few subjects, at most a couple of hundred sections
across a whole year. Some customers are using it differently. A **dean's office** that schedules
centrally for about 10 departments keeps all their subjects in one database, which can easily mean
**800+ sections in a single semester**.

Today, opening a semester loads **every** section in it, and every view works on all of them:
- **Section list:** one full card per section. Virtualization is disabled
  ([SectionListView.axaml:1032](../src/TermPoint/Views/Management/SectionListView.axaml)) because
  the inline editor's expansion breaks Avalonia's virtualizing panel. Filtering only highlights or
  collapses cards ([SectionListViewModel.cs:600](../src/TermPoint/ViewModels/Management/SectionListViewModel.cs));
  it never removes them, so all 800 cards are built on every load, semester switch and full
  refresh.
- **Schedule grid, workload panel, conflict checks:** these all scale with the number of loaded
  sections, and some conflict checks compare sections pairwise.

Section data itself is small; 800 sections is a few MB. The cost and the risk are in the UI built
for each section, and in the user wading through nine other departments' sections to find their
own.

### Measurements (2026-10-08, Debug build, Debug section generator, 2 subjects)

| Measurement | Result |
|---|---|
| 400 sections in one semester | UI responsive |
| 800 sections in one semester | UI responsive; no scroll stutter |
| Database file with 800 sections | **2.2 MB** (network saves are not a concern) |
| Managed heap, fresh start, 800-section semester | **575 MB** |
| Managed heap, same session, near-empty semester | **82 MB** |
| Difference (VS snapshot diff) | **+466 MB, +4.46 M objects** |
| Earlier session after two generator runs (reloads) | 1.1 GB on the large semester, 286 MB on the light one |
| Fresh session, several large ↔ light semester round trips, ending on large | **~575 MB, unchanged.** Semester switching does not leak. The earlier session's extra memory came from something else in that session (most likely the Debug-only generator runs); not pursued. |

**What the diff shows.**
- The growth is all live Avalonia UI: about **74,000 extra controls, roughly 93 per section**.
  Every control carries `Classes`, logical and visual child lists, `KeyBinding`s, and a property
  store.
- Each control costs about **6 KB**. That includes about 3.8 `StyleClassActivator`s per control
  (285 K in total) and the per-control `ValueStore` (296 MB inclusive).
- The section card template is the dominant consumer. It has about 29 `TextBlock`s (many only
  conditionally visible, but always created), 11 `StackPanel`s, 7 `Viewbox`+`Path` icons,
  7 `Border`s, 5 `Button`s (each expands to several controls), a schedule-line `ItemsControl`, and
  a `Popup` per card. Grid tiles add a few controls per meeting.
- No managed-data cost of note: section data for 800 sections is a few MB.

**Card cost is set by the template, not the data.** Every element in the card template is built
for every card, including elements hidden because their value is empty. Only the meeting rows
(one per meeting) grow with the data. Realistic data should cost about the same per section as
the generated data.
- *Instructor conflicts don't add controls.* The warning is a single `TextBlock` bound to one
  newline-joined string
  ([SectionListView.axaml:1404-1410](../src/TermPoint/Views/Management/SectionListView.axaml)).
  Many conflicts only add some text-layout memory, which is minor.

**Lighter-card candidates (not yet a spec item; noted 2026-10-09):**
1. **Per-card right-click flag menu: the clearest win.** Every card builds its own flag-picker
   `Popup` ([SectionListView.axaml:1129-1199](../src/TermPoint/Views/Management/SectionListView.axaml)):
   a Border, a StackPanel, 4 Buttons, 3 Viewbox+Path flag icons, labels and a separator, about
   **20 controls per card**. Popup content is built with the card even though the popup is closed,
   so roughly a fifth of every card is a menu that's almost never open. At 800 sections that is
   about 16,000 controls, an estimated ~100 MB. *Fix:* one shared flag menu at the list level,
   opened for whichever card was right-clicked.
2. Conditionally shown rows (room and instructor conflict warnings, tags, reserves, resources,
   notes) are always built. Each of tags, reserves and resources is a StackPanel, Viewbox, Path
   and TextBlock. Building them only when they have content (for example via a `ContentControl`
   whose content is null when empty) would trim cards that don't use them.
3. Icons are drawn as `Viewbox` + `Path`, two controls each, seven per card. A single
   `PathIcon`, or a `Path` with `Stretch`, would halve that.
4. About 3.8 `StyleClassActivator`s per control suggests app-wide styles with class selectors on
   common types (`TextBlock`, `Border`). Scoping them more narrowly would cut the cost of every
   control in the app, not just cards.

**Conclusion.** Memory scales with **realized section cards**, not with loaded data. Any solution
that keeps every card alive while merely hiding or collapsing it saves nothing. A solution that
removes out-of-scope sections from the list's items (data stays loaded) cuts memory in proportion
to the slice.

### What the feature does

The user chooses **which subjects to load**. The app then loads only sections whose course
belongs to one of those subjects (`Course.SubjectId`). The section list, schedule grid and other
working views contain only that slice. The choice can be changed at any time, and "all subjects"
remains available.

This is a **load scope**, not a filter: unloaded sections are not built, drawn or held by the
working views.

### What "done" looks like (draft)

- The user can choose one or more subjects to work on, or all.
- Only sections in the chosen subjects are loaded into the section list, the schedule grid and the
  workload views.
- The current scope is **always visible**, for example "History, English (2 of 10 subjects)", so
  partial data is never mistaken for the whole semester.
- Checks that must be global stay correct regardless of scope (see Q1).
- Destructive or bulk operations behave predictably under a scope (see Q3).
- A department with one or two subjects never notices the feature.

### Open questions (to settle before Phase 2)

**Q1. Conflicts and availability across the scope boundary.**
- Rooms and instructors are shared across departments. If only History is loaded, an English
  section in the same room at the same time is still a real conflict.
- The same applies to the Room Availability Browser. It would show a room as free when another
  department is using it.
- *Recommendation:* room checks, instructor checks and room availability always read the
  **whole semester** from the database. They need data, not UI, so this is cheap. Only display is
  scoped.

**Q2. Instructor workload.**
- An instructor may teach in more than one subject.
- Should the workload panel and reports show their **full** load, or only the loaded subjects?
- *Recommendation:* show the full total, with the in-scope portion visible. A partial total could
  mislead someone into overloading an instructor.

**Q3. Bulk and destructive operations under a scope.**
- **Empty Semester** (which, after item 8, deletes sections, meetings, releases, commitments,
  notes and watches) must not surprise a scoped user who thinks they are emptying only their
  subjects.
- Also affected: Copy Semester, shared-schedule export, Course History export, Workload Mailer,
  and imports.
- For each: does it act on the scope or on the whole database?
- *Recommendation:* destructive and whole-semester operations always act on the whole semester and
  say so explicitly. Exports and copies default to the scope, with a clear choice.

**Q4. Where the choice lives.**
- Options: per user on this machine (local settings, keyed by database), or stored in the database
  for everyone.
- Is it remembered between sessions?
- What is the default for a new user or a new database? *Recommendation:* all subjects.
- If stored locally, no database or schema change is needed. That also avoids the release-order
  constraint in spec item 3, which applies to new fields on existing entities.

**Q5. Creating and editing under a scope.**
- Should the course picker in the section editor offer only in-scope subjects?
- What happens when a section's course is changed to an out-of-scope subject? It would vanish from
  the list on Apply.

**Q6. Who schedules at the dean's office?**
- Scoping does **not** change the one-editor-at-a-time write lock.
- If several staff need to schedule different departments **at the same time**, that is a separate
  and much larger problem (concurrent editing). It is out of scope here, but we should know if it's
  coming.

**Q7. Performance expectations for "all subjects".**
- A dean's office will sometimes still load everything.
- *Recommendation:* run the Debug-build section generator (`GenerateRandomSections`) to about 800
  sections in one semester, and time load, semester switch, editor open/Apply, filtering, memory
  and a network save. That tells us whether "all subjects" also needs work, or only a warning.

### Out of scope (proposed)

- Concurrent editors or per-subject write locks (see Q6).
- Access control. Scope is a working convenience, not a permission: any user can load any subject.
- Fixing section-list virtualization. It's a separate effort, blocked on Avalonia's
  variable-height reflow.

---

## Approved design (2026-10-20)

Worked through with the user on 2026-10-20, after item 21 landed.

### Approach: B (filter-based focus). No fresh measurement.

- **The item 21 and item 23 numbers settle it.** With all 800 cards built, the heap is 392 MB
  (Release); with item 23's opt-in virtualization it is ~170 MB. Hiding out-of-focus cards
  cuts what is built in proportion to the slice, the same saving load scoping (A) would give.
- **A's remaining cost** was special handling for conflicts, room availability, workload totals
  and bulk operations. B needs none of it: everything still loads, and the focus only decides
  what is **displayed** and **offered**.

### What a focus is

- A focus is **a set of subjects**, or **All**, or **None**. **Levels are not part of it.**
- **One remembered focus per user per database**, including All and None. It lives in local
  settings (`AppSettings`), in a new dictionary from database to focus. Nothing is written to
  the database.
  - **The dictionary key is the shared database's path** (D), normalised with
    `CheckoutService.NormalizeSourcePath`, the same key `DatabaseInstanceGuard` uses. Never the
    path of the local working copy.
  - The same file reached by a mapped drive and by its UNC path gives two keys. The second one
    just starts fresh (see *Starting value*). Accepted.
- **Starting value (revised 2026-10-23).** When a database is seen for the first time (a new
  database, or a path not seen before):
  - **3 subjects or fewer: All.** A department never notices the feature. This includes a new
    database from the wizard, which has no subjects yet.
  - **More than 3 subjects: None**, which shows the empty state. This is the dean's-office case.
  - The rule runs **once**, when the database is first seen. After that the remembered focus is
    used, so a department that later grows past 3 subjects stays at All.
  - **Upgrade note:** every existing user's first launch of 1.2.3 is "not seen before", so the
    rule runs for everyone then. Only databases with more than 3 subjects start at None.
  - **Release notes must explain the feature.** For a large database, the empty-state message
    below is what keeps a user from thinking their sections are gone.
- **The web demo always starts at All.** Its settings live in the browser's in-memory file
  system, so every visit would otherwise be "not seen before".
- **Tours switch the focus to All when they start.** Several tour steps open a section card or
  its editor, and need the cards to exist.
- **Deleted subjects** drop out of the focus silently. If none are left, the focus becomes
  **None**, which shows the empty state.
- **Focus is a separate layer from the ordinary filter.** "Clear filters" doesn't touch it, and
  the ordinary filter works **within** it.
- **The Focus button** is in the section-list header. It opens a list of subjects to tick, plus
  All and None.
- **No focus change while a section editor is open** (tooltip: "Finish editing first"). A focus
  change can then never hide a card that has unapplied edits. This satisfies rule 3
  structurally, not through a dialog.
  - **One guard, not one per control (revised 2026-10-23).** Rebuilding the section list goes
    through `LoadCore`, which calls `CollapseEditor()`. So *any* focus change, not only the Focus
    button, could close an editor with unapplied edits. That includes **Show all** on every
    indicator (grid header, workload panel, Instructors flyout) and the empty-state buttons.
  - `FocusService` exposes a single `CanChangeFocus`, false while an editor is open, and refuses
    a change when it is false. Every control that changes the focus binds its enabled state to
    it.
- **Empty state (rule 4).** When the focus is None, or matches no sections, the section list
  shows a short message instead of an empty panel. For example: *"No subjects in focus. Choose
  the subjects you're working on to see their sections."* It has a **[Choose focus]** button,
  plus **[All subjects]** for a small department. The schedule grid shows the same hint.

### Indicator

- **One reusable control.** It reads, for example, "Focus: BIOL, CHEM · 2 of 10 subjects", with
  a **Show all** link that sets the focus to All. It is hidden when the focus is All.
- **Placed in four places:** the section-list header, the schedule-grid header next to the filter
  summary, the workload panel and the Instructors flyout.
- Fonts, sizes and colours come from `AppColors.axaml` / `App.axaml`.

### Who counts as an in-focus instructor

**Revised 2026-10-23.** The 2026-10-20 rule counted an active instructor with nothing in the
window as in focus for **every** subject, so long-idle instructors from every department piled
into every focus. Its window was also only the *viewed* semesters plus the previous year, so an
instructor teaching the focus subject in another semester of the viewed year was missed.

**One rule, in its own class.** `FocusService.IsInFocus(instructor)` is the only entry point,
so every consumer below agrees. The decision itself lives in a separate class,
`InstructorFocusRule`, a pure function of the focus and the instructor's teaching history. It
can be unit-tested on its own, and changed later if users find it too narrow or too generous,
without touching any view.

**Terms.**
- **Y** is the academic year being viewed. In multi-semester mode every ticked semester is in Y.
- **The window** is every semester of **Y and Y−1** (two whole academic years). It doesn't
  depend on which semesters are ticked, so the lists don't change as the user moves between
  semesters. Y−1 is included because the year being planned is often still mostly empty.
- **A section counts for an instructor** when they are assigned to it. Its subject is its
  course's `SubjectId`.

**The rule, applied in order:**
1. **Focus is All:** in focus.
2. **Focus is None:** not in focus.
3. **Inactive instructor:** not in focus.
4. **Any section in the window:** in focus if at least one of those sections is in a focus
   subject. Earlier years are ignored.
5. **Nothing in the window, but sections in an earlier year:** take the most recent academic
   year before Y−1 in which they had any section. In focus if at least one section that year is
   in a focus subject.
6. **Nothing in the window and nothing earlier** (a new hire, or someone just added in the
   flyout): in focus under every subject focus, so they don't vanish the moment they're
   created. This also covers sections only in years after Y (viewing a past year), which is
   harmless.

**Examples, focus = Biology:**

| Instructor | In focus? | Why |
|---|---|---|
| Teaches BIOL this year or last | Yes | Step 4 |
| Teaches BIOL and ENGL in the window | Yes (and under English too) | Step 4 |
| Only ENGL in the window, taught BIOL 4 years ago | No | Step 4 ignores earlier years |
| Taught BIOL in 2022–23, nothing since | Yes | Step 5 |
| Idle ENGL sessional, last taught 2021–22, still active | No (only under English) | Step 5 |
| New hire, no sections anywhere | Yes, under every focus | Step 6 |
| New hire given a first CHEM section | No (now only under Chemistry) | Step 4 |
| Inactive | No | Step 3 |

**Data and cost.** `FocusService` reads one small table of (instructor, subject, academic year)
across **all** sections in the database, not just the viewed semesters (`SectionStore` only holds
those). It rebuilds it when sections change and when the viewed academic year changes. Academic
years are ordered by `StartYear`. A few thousand rows at dean's-office size.

**Further rules:**
- **Subject only.** Levels play no part.
- **Changes to the rule:** a "Subjects taught" field on the instructor is planned for **1.2.4**
  (see *Later release*). It would slot into `InstructorFocusRule`.

### What follows the focus

| Place | Behaviour |
|---|---|
| **Section list** | **Hide:** sections outside the focus get **no card**. This holds on full load and in the add / apply / copy / replace paths. The ordinary filter's Highlight / Expand work within the focus. In multi-semester mode, a **semester banner stays** even when none of its sections are in focus. |
| **Schedule grid** | In-focus sections only; the ordinary filter narrows further. |
| **Filter panel: Subjects list** | Focus subjects only. |
| **Filter panel: Courses list** | Courses in focus subjects only. |
| **Filter panel: Instructors list** | In-focus instructors only, by `FocusService.IsInFocus` (the same rule every other view uses). |
| **Filter panel: other lists** (rooms, campuses, types, tags, meeting types, levels) | Unchanged. |
| **Filter bar: overlay pick lists** | The instructor overlay list follows the Instructors list above: it already shares its items (`NamedInstructors`). The room and tag overlay lists are unchanged. |
| **Filter bar: summary** | Describes the ordinary filter only. The focus indicator sits next to it. |
| **Section editor: course chooser** | Courses in focus subjects only. Under All, every course. |
| **Section editor: instructor chooser** | In-focus instructors first, then **"Other instructors"** below a divider. The section's **current instructors are always listed**, so an untouched section can never save with its instructor blanked (the item 3 family of bug). |
| **Workload panel** | In-focus instructors, with a "Show all instructors" switch. **Totals always count the whole load.** Sections outside the focus are listed but marked. |
| **Instructors flyout** | Follows the focus, with the indicator and the same "Show all instructors" switch. |
| **Workload Mailer** | Same instructor list as the workload panel; email content shows the whole load. |

**Multi-semester mode:** the focus applies to every semester shown.

### What stays global

- **Conflicts:** room and instructor conflicts are checked against the whole semester. A Biology
  card still warns about an English section in the same room.
- **Rooms:** room availability and the room chooser show every room.
- **Grid overlays (Instructor, Room, Tag) ignore the focus, as they already ignore the ordinary
  filter (decided 2026-10-23).** An overlay shows *all* the matching sections, out-of-focus ones
  included, plus an instructor's commitments. Its job is the whole picture: an Instructor overlay
  that hid a Biology instructor's English section would make them look free when they're busy.
  This is the same reason conflicts stay global.
- **Also outside the focus:** shared schedules imported from other departments (other databases'
  data, imported on purpose), meetings (they have no subject) and program access watches (a
  conflict check).
- **Whole-semester operations:** Copy Semester, the shared-schedule export, the Course History
  export and imports.
- **Empty Semester (item 8)** empties the whole semester. When a focus is set, its confirmation
  adds: *"This empties **all subjects** in Fall 2026, not only the subjects in your focus."*
- **Imports:** if any imported sections fall outside the focus, the import summary says how many,
  with **[Show all]**.

### Release and persistence check

- **MinimumVersionToWrite:** asked *"Could the new version save anything the old version
  wouldn't have?"* **No.** Nothing new is saved to the database, so item 3's release-order
  constraint doesn't apply.
- **Four rules:** the save, lock, switch, restore and recovery code paths are untouched.
  - **Rule 3** is protected by `FocusService.CanChangeFocus`: no control can change the focus
    while an editor is open.
  - **Rule 4** is covered by the indicator, the empty state and the Empty Semester wording.
  - No overlap with `designs/data-persistence-review-2026-10.md` §8.
- **Data-integrity risk:** the instructor chooser blanking a section's out-of-focus instructor on
  Apply. Mitigated by always listing current instructors, and pinned by a test (step 5).

### Later release (1.2.4): "Subjects taught"

- **What:** an explicit "Subjects taught" field on the instructor, saved in the database.
- **Why not 1.2.3:** a 1.2.2 client neither checks MinimumVersionToWrite nor keeps unknown
  fields, so it would wipe the field when it edits an instructor.
- **When:** in 1.2.4, once everyone is on 1.2.3, raising MinimumVersionToWrite as item 3 lays out.
- **The derived rule stays** as the fallback for instructors with nothing set.

### Implementation steps (one Sonnet brief each)

Steps 2–7 edit AXAML: warn the user before each so they can close the tab in VS.

1. **`FocusService` and settings.**
   - Holds the focus (subject set, All or None) and persists it per database path.
   - Exposes `Matches(section)` (by the course's `SubjectId`), `IsInFocus(instructor)`,
     `IsCourseInFocus(course)`, `CanChangeFocus`, and a `FocusChanged` event.
   - `IsInFocus` delegates to `InstructorFocusRule` (the six-step rule above), tested on its own
     with every row of the examples table.
   - Keyed by `CheckoutService.NormalizeSourcePath(D)`. Starting value by the 3-subject rule;
     always All in the demo.
   - The teaching history (instructor, subject, academic year) needs a repository read across
     all sections: `SectionStore` only holds the viewed semesters. Rebuild it when sections
     change and when the viewed academic year changes.
   - Reloads when the database changes. Registered in desktop `ConfigureServices` **and**
     `ConfigureDemoServices`.
   - Unit tests only, no UI.
   - **Done 2026-10-23** (uncommitted when written). Notes for later steps:
     - **Handler order:** `FocusService` clears its caches in its own `SectionStore.SectionsChanged`
       handler. A consumer that asks `Matches` / `IsInFocus` from its own `SectionsChanged` handler
       must take `FocusService` as a constructor dependency, so DI builds it (and subscribes it) first.
     - **Deleted subjects are pruned only when the service is built** (database open). A focused
       subject deleted mid-session keeps its dead id until the next open; it matches nothing. Step 3
       should prune when the subject list changes, so the indicator never names a deleted subject.
     - **No event on a viewed-year change.** `FocusService` re-reads its history but raises nothing,
       so instructor lists (steps 4–6) must also refresh on `SemesterContext` changes.
     - **Course saves don't reload `SectionStore`.** Harmless: a course's subject is locked once it
       has sections, so only a stale course-to-subject map is possible, and the next
       `SectionsChanged` clears it.
> **Progress (2026-10-23).** Steps 2, 3 and 5 are done. Step 4 is half done: the grid follows the
> focus and overlays ignore it; the filter-panel lists don't follow it yet. Changes made after the
> user tried it in the app:
> - **The focus statement is prominent:** "Focus: BIOL, CHEM" (bold, teal `FocusBadgeText`) in the
>   Section View header beside the title and in the Schedule View header before "Filtered by:",
>   with a "Show all" link, including in detached windows (built in `MainWindow.axaml.cs`). The
>   small indicator row and the "N of M subjects" count were dropped. Under None: "Focus: no
>   subjects".
> - **The editor's Subject chooser** lists focus subjects plus the section's current subject.
>   Under All or None it lists every subject.
> - **The editor's instructor chooser** lists in-focus instructors first, then an "Other
>   instructors" heading and rule, then the rest in a lighter font (`TextOutOfFocus`). Nobody is
>   dropped. Under All or None there is no grouping.
> - **Program access watches** run on all filter-passing sections, not only in-focus ones (they
>   are a conflict check).
> - **The detached Schedule View header** now wraps (a WrapPanel, like the inline header).
> - **Still open:** what double-clicking an out-of-focus overlay tile should do (today: nothing);
>   the filter lists (step 4); steps 6–8. The user is watching for "Focus" vs "Filter" confusion.
>
> **Naming and placement (decided 2026-10-23, prototype).** "Focus" read too much like "Filter".
> The focus is a coordinate of *where you're working*, the same kind of setting as the semester:
> picking Fall already hides Winter sections, and nobody calls that a filter. So:
> - **A "Subjects:" picker in the top bar**, after Academic Year and Semester:
>   `Academic Year: [2025-2026]  Semester: [Fall]  Subjects: [BIOL, CHEM]`. It reads "All", "Choose…"
>   (None) or the abbreviations, trimmed at `SubjectsButtonMaxWidth` with the full list in a tooltip.
> - **The word "Focus" is gone from user-visible text.** The empty state reads "No subjects chosen…",
>   with [Choose subjects] / [All subjects]. Code names (`FocusService` and so on) are unchanged.
> - **The header statements and the section panel's Focus button are commented out**, not deleted
>   (MainView.axaml, SectionPanelContent.axaml, MainWindow.axaml.cs), in case they're wanted back.
> - **The top bar's context stands out:** the Academic Year, Semester and Subjects labels and values
>   use `TopBarContextWeight` (SemiBold) from App.axaml; the static menu items don't.
> - In this document "focus" still names the concept; users see "Subjects".
>
> **Further progress (2026-10-23, later).** Step 4 finished (the filter lists follow the subjects;
> ticks and overlays outside them are dropped; double-clicking an out-of-focus tile offers "Add
> {subject} and edit"). Step 6 done, with these decisions:
> - **Workload View:** only in-focus instructors get a row; each row lists the whole load, with
>   out-of-subject sections dimmed; under None a hint. No "Show all instructors" switch anywhere:
>   Subjects: All in the top bar does that job.
> - **Instructors flyout and Workload Mailer:** the editor pattern (in-focus first, "Other
>   instructors", the rest dimmed), sorted inside each group. The mailer ticks only in-focus
>   instructors by default. The grid's right-click Instructors panel uses the same pattern.
> - The instructor rule stays in one place (`InstructorFocusRule` behind `FocusService.IsInFocus`),
>   so every list changes together if the rule changes.
> Step 7 done: Empty Semester says it empties all subjects; the section import summary counts
> sections outside the chosen subjects, with [Show all subjects]. Copy Semester may deserve a
> similar note later. **Still open:** step 8's manual checks, and the memory check on a
> 10-subject, ~800-section database (heap snapshots, Release, virtualization off).

2. **Section-list Hide and empty state.**
   - Build cards only for in-focus sections, on load and in every incremental path, and rebuild on
     `FocusChanged`.
   - Add the empty-state message.
3. **Focus picker and indicator.**
   - The reusable indicator control, and the Focus button and picker (subjects, All, None).
   - The button, **Show all** and the empty-state buttons bind to `CanChangeFocus`.
   - Tours set the focus to All when they start.
   - The indicator goes in the section list and grid header; the empty-state hint goes in the
     grid.
4. **Grid and filter panel.**
   - The grid applies the focus before the ordinary filter.
   - Overlays bypass the focus: `BuildOverlayBlocks` (Pass 2) must still add out-of-focus
     sections that match an Instructor / Room / Tag overlay, and commitments as today.
   - The filter's Subjects, Courses and Instructors lists follow the focus (and so the instructor
     overlay list). They are rebuilt on `FocusChanged` and when the viewed year changes.
   - Shared-schedule blocks, meeting blocks and Room Availability ghosts are untouched.
   - **A filter selection that falls outside a new focus** (for example a ticked Chemistry course
     after the focus moves to Biology) is dropped when the lists rebuild, so the filter never
     hides everything for a reason the user can't see.
5. **Section editor choosers.**
   - The course chooser lists focus subjects only.
   - The instructor chooser shows in-focus instructors, then "Other instructors"; current
     instructors are always included.
   - Test: an untouched section with an out-of-focus instructor saves unchanged.
6. **Workload panel, Instructors flyout, Workload Mailer.**
   - All three follow the focus, with the "Show all instructors" switch and the indicator.
   - Totals cover the whole load; sections outside the focus are marked.
7. **Messages.** The Empty Semester wording when a focus is set, and the import summary's count of
   sections outside the focus.
8. **UI-content tests and verification.**
   - **Tests at the view-model level,** on the collections each list and chooser binds to: the
     section list, grid blocks, the filter's Subjects / Courses / Instructors lists, the editor's
     course and instructor choosers, the workload panel, the Instructors flyout and the mailer's
     list.
   - **Focus cases:** All, None, one subject, two subjects, a deleted subject, and multi-semester
     mode.
   - **Instructor cases:** cross-subject, another semester of the viewed year, previous year
     only, last taught before the window (in and out of focus), never taught, inactive.
   - **Manual checks:** the 800-section database focused on 2 of 10 subjects (heap roughly in
     proportion), switching databases, the first launch on an existing database (None plus the
     empty state), the full test suite and the compile check.
   - **Option, not adopted:** Avalonia.Headless binding-level UI tests would need a new test
     dependency.
