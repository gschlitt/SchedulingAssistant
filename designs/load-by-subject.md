# Load by Subject — Progressive Design Spec

**Status:** ⏸ **PARKED (2026-10-09).** Revisit after every other item in
`spec_version_1.2.3.md` is done, in particular **item 21 (lightweight section card)**.
Then re-measure memory per card on the same 800-section test DB and settle Phase 1 with that data.
**Target release:** 1.2.3 if time allows after re-measurement; otherwise a later release.

**Where we left it.**
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
