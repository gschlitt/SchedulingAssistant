# Access Analyzer and Easy Access Watches — Progressive Design Spec

## Status
Current phase: **1 — Problem Statement** (first draft 2026-10-30; terms, partial slates and Greg's review comments
2026-11-06; D1–D8 decided; Q1–Q7, Q11–Q12, Q14–Q16 open; not yet locked by Greg)
Last session: 2026-11-06
Next step: Greg settles the open questions, then locks Phase 1; Phase 2 (Domain Model) follows

---

## Phase 1: Problem Statement

### The problem
TermPoint measures a timetable's quality mainly by **student access**: can the students who need a set of courses
in a term actually take all of them, and how many of them can? Two things stand in the way today.

**1. Access is hard to judge.** Access watches draw a teal box around any two overlapping sections from different
courses. That says "these overlap", even when students have other sections that don't. It does not say whether the
overlap matters: whether students still have a way through, how many of them, or which single section everything
hangs on. Timetables are in flux; sections move for instructors, rooms and emergencies, and each move can quietly cut
students off. Nobody finds out until registration.

**2. Access watches cost too much to set up, so they go unused.** Users want to get in, get the job done and get out.
Some departments don't use custom tags at all. Today a watch:
- lives in a popup off the grid's Access group, where many users will never look;
- opens on **tag mode**, with lists labelled "Tags (AND)" and "Levels (OR)", which is empty for a department without tags;
- in course mode, means ticking boxes in a short scrolling list of every course, with no search;
- belongs to **one semester**: Copy Semester copies sections only, so every watch is rebuilt by hand every term.

Every feature that serves student access (the conflict boxes, this analyzer, the Slot Browser's access check) depends
on watches existing. If setting them up is a chore, none of those features gets used, and timetables are worse for
students. **So this spec treats easy watches as half of the feature, not a side issue.** The course sets the user
sets up in advance are what tell the app what matters (D4).

### Terms (D2)
| Term | Meaning |
|---|---|
| **Access watch** | What the user defines: a named watch over a course set, for whatever group of students needs those courses together in one term (a year of a program, a cohort, a specialised niche) |
| **Course set** | The courses an access watch covers |
| **Slate** (full slate) | A selection of sections, one for each course in the course set, none of them conflicting. Two slates differ if they differ in at least one section |
| **Partial slate** | A selection of non-conflicting sections covering *some* of the courses in the course set. A partial slate for a chosen subset of courses is exactly a full slate of that smaller set |
| **Essential section** | In every full slate. If it moves badly, students may be cut off |
| **Usable section** | In at least one full slate |
| **Stranded section** | In no full slate. Students of the watch can never use it as part of a full slate, so its seats don't count for them |
| **Seats** | The most students who could each hold a full slate at once, within the sections' capacities |
| **Access Watch panel** | Where watches are listed, created and edited, and where each watch's health and notices are shown |

"Program" is not used for any of these: a university program spans several semesters, and course sets also serve
cohorts and niches that are not programs (D2).

### Stories

**Today.** Alice copies last fall's semester into Fall 2027. Last year someone built an "Enviro Y2" watch (BIOL 201,
CHEM 213, GEOG 250, PSYCH 101, STAT 104); it didn't come along, and Alice doesn't know it ever existed. In August,
PSYCH 101 A's instructor changes and Alice moves it to Tue 10:00. It now overlaps CHEM 213 A, the only CHEM 213
section. No box appears. Enviro Y2 students can no longer take all five courses, and the first anyone hears of it is
a student at registration.

**With the feature.**
- Copying the semester brings the Enviro Y2 watch along.
- Alice changes PSYCH 101 A to Tue 10:00 in the editor. A short line appears under the meeting rows: "Affects
  access for Enviro Y2. See the Access Watch panel." The details are not crammed into the editor (D6). Alice glances
  at the panel: "Enviro Y2: this change would leave no full slate. PSYCH 101 A would overlap CHEM 213 A, the only CHEM
  213 section." She tries the finder instead and picks Thu 10:00.
- Had she saved anyway, the panel's notice for Enviro Y2 would say: "Students can no longer take all 5 courses.
  PSYCH 101 A now overlaps CHEM 213 A, the only CHEM 213 section. Move one of them, or open Enviro Y2 to see what's
  left."

**A course isn't offered this term.** Copying Fall 2026 into Fall 2027 brings Enviro Y2 along, but GEOG 250 has no
sections in Fall 2027 (it alternates years). The app does not silently report "no full slate" for the watch. Instead
the Access Watch panel flags it: "Enviro Y2: GEOG 250 isn't offered in Fall 2027." Alice can remove GEOG 250 from the
watch for this semester, or leave it there if a section is still to be added (Q6).

**How much access is there?** Before finalising the timetable, Alice opens the Access Watch panel. Each watch has one
line: "Enviro Y2: 12 slates · up to 40 students · depends on PSYCH 101 A". Clicking it colours the grid: PSYCH 101 A
stands out as the section everything depends on, and GEOG 250 C is dimmed because it can't be part of any full slate.
Alice can step through the slates one at a time.

**No full slate.** Another watch reads: "First-year Science: no full slate · best partial slate covers 3 of 4 ·
CHEM 101 can't fit with the rest". Alice knows which course to look at.

**A part-time student.** Many students don't take every course in their semester's course set, even when full
slates exist. Dana, an advisor, is helping a student who will take only BIOL 201, GEOG 250 and STAT 104 of the five
Enviro Y2 courses, and who wants BIOL 201 B because a friend is in it. Dana opens the Enviro Y2 watch, ticks those
three courses, pins BIOL 201 B, and pages through the partial slates on the grid that contain it: "Slate 1 of 4:
BIOL 201 B, GEOG 250 A, STAT 104 C". Slates 1–2 use Tuesday mornings, when the student works; slate 3 suits. A
minute, not a session with three printouts.

**The seats trap.** A watch on BIOL 201 + CHEM 213. BIOL 201 A has 10 seats and B has 100; CHEM 213 A has 10 and
B has 100; both B sections meet MWF 10:00. On paper each course has 110 seats. In truth everyone in BIOL B must take
CHEM A, and everyone in CHEM B must take BIOL A: **20 students** can hold a full slate, not 110.

**Shared seats.** PSYCH 101 A is in both Enviro Y2 and First-year Science. Each watch's "up to 40 students" counts
its 40 seats as if they were all its own. The panel says so: "PSYCH 101 A's seats are also counted by First-year
Science." The two numbers can't simply be added (Q4).

**A department with no watches.** Bob schedules a department that has never set one up. He opens the Access Watch
panel and clicks "Add an access watch". He can paste "BIOL 201, CHEM 213, GEOG 250, PSYCH 101, STAT 104" from the
academic calendar, or type "BIO" and pick courses from an autocomplete list. Done. The health line appears at once.

### What "done" looks like

**A. Access watches are easy**
1. A watch set up once carries forward when a semester is copied. Courses in its course set that the new semester
   doesn't offer are flagged, and the user decides what to do with them (Q6).
2. A watch is created by pasting course codes or picking courses with autocomplete. No mode choice up front; tags and
   levels are an advanced option (Q7).
3. Users see the D2 terms, never "tag mode" or "AND/OR".

(Creating a watch from sections selected on the grid is deferred (D5). The app does not prompt users to create
watches (D4).)

**B. Access is visible**
1. Each watch shows one line in the Access Watch panel: how many full slates; up to how many students; which
   section(s) it depends on. With no full slate, the best partial slate and the course(s) that can't fit. Seats
   shared with other watches are flagged (Q4).
2. Saving an edit that makes a watch's access worse puts a notice on that watch in the Access Watch panel: what
   happened and what to do (Q5).
3. While a section is being edited, the editor shows only a short pointer when the unsaved change affects a watch;
   the details are in the Access Watch panel (D6).
4. One grid picture shows essential, usable and stranded sections. The user can step through slates, and pin one or
   more sections to step through only the slates that contain them (Q11).
5. From a watch, the user ticks any subset of its course set and pages through the partial slates for that subset on
   the grid, whether or not full slates exist (D3). Pinning works here too. Nothing is saved: the subset and pins are
   a view, not a new watch.
6. The teal boxes on the grid stop flagging overlaps that leave students other choices (Q16).
7. The Slot Browser ranks placements with the same engine (Q12).
8. Nothing to set up beyond listing the courses.

### How the analysis works (proposed)
- **The graph.** The sections of the course set are the points. Two sections are joined when they belong to
  *different* courses and don't overlap. (Same-course sections are never joined, or a slate could take two PSYCH
  101 sections.) A full slate is then one section from every course, all joined to each other: an n-clique in an
  n-partite graph. A partial slate is a smaller clique.
- **The search is small.** Because every slate takes exactly one section per course, no general clique algorithm is
  needed: pick one section per course, starting with the course that has the fewest sections, and drop any candidate
  that clashes with what's already picked. Five courses of four sections each is at most 1,024 combinations. The count
  is capped ("more than 10,000").
- **Pins narrow the search.** A pinned section is the only candidate for its course, so pinning makes the search
  smaller still.
- **Essential, usable and stranded don't need the full count.** A section is usable if one full slate with it exists
  (search with it fixed; stop at the first). It is essential if no full slate exists without it. Each is a short search.
- **Partial slates cost nothing extra.** The slates for a chosen subset are the same search run on that subset
  (D3). When no full slate exists, the same search, allowed to skip courses, also finds the largest partial slate and
  which courses are left out (the health line).
- **Courses not offered.** A course in the course set with no sections this semester is reported as "not offered",
  never folded silently into "no full slate" (Q6).
- **Seats.** Two courses: an exact max-flow. Three or more: a packing problem, hard in general but small here; a
  linear program over the slates gives a close upper bound, enough given that capacities are estimates. Never the
  smallest course total, which overstates (the seats trap above). Each watch is computed on its own, so a section in
  several watches is counted by each; that overlap is flagged, not resolved (Q4). Phase 2/5 checks the cost for large
  watches.
- **One clash test, one coverage rule.** "Do these two sections overlap" is the conflict detector's existing test
  (same day, half-open times, odd/even weeks). Which sections a watch covers is the detector's rule. Both live in one
  place that the detector, the analyzer and the Slot Browser share.
- **A general API, not panel code (D8).** Everything above lives behind one UI-free service that any caller can use:
  which watches cover a section; slates (full or partial, for any subset, with pins); essential / usable / stranded;
  best partial slate; seats; and "what if" — the same answers for a proposed set of sections (a section moved, added
  or removed, unsaved). The Access Watch panel, the editor pointer, the teal boxes and later the Slot Browser all
  call it; none of them computes access on its own.
- **Whole semester.** The analysis ignores grid filters. A filter must never change how much access a watch reports.
- **When it runs.** On section save and semester change (filters don't affect it, so not on every grid redraw), and
  on the unsaved section while the editor is open (B3).

### Design principles
- **Few nouns, used exactly.** Access watch, course set, slate. Everything else follows from those.
- **Nothing set up twice.** What the user tells the app stays told, across semesters.
- **The user says what matters, in advance.** Course sets are the user's statement of what must fit together. The app
  doesn't guess at them or prompt for them (D4).
- **Speak when it matters, otherwise stay quiet.** The app speaks only when an edit changes access for a watch.
- **Details in one place.** The editor points; the Access Watch panel explains (D6).
- **Exact rather than misleading.** Whole slates, not pairs of sections; real seats, not course totals.
- **Honest about limits.** Seats are shared with other students and other watches; courses scheduled elsewhere are
  invisible; courses not offered this term are named; sections without times are assumed to fit. Each limit is said
  in plain words where it affects a number.
- **Informational, not prescriptive.** Shows access; the user decides.

### Relationship to existing and planned features
- **Program conflict detector (teal boxes).** Today it boxes any overlap between sections of two courses in a watch,
  even when each course has other sections that fit. That is useful, but only somewhat: most boxes may not matter. With
  slates available, the boxes should show only overlaps that cost students something (Q16). This changes what existing
  users see, so it needs a What's-new line. (The code's `ProgramWatch` names can stay; only user-facing words follow D2.)
- **Slot Browser** (`designs/slot-browser.md`). Its D2 (the program check uses watches) is unchanged and gains most
  from easy watches; its labels should follow these terms. **Its D3 level 2 is a pairwise shortcut that can be
  wrong.** Story: a watch on STAT 104, GEOG 250 (A, B) and CHEM 213 (A only), where GEOG 250 B clashes with CHEM 213
  A. The finder offers STAT 104 a time that overlaps GEOG 250 A and calls it level 2 because 250 B is free. But no
  slate can hold 250 B with CHEM 213, so no full slate is left. Judged by slates, that placement is level 3. See Q12.

### Not in this feature (proposed)
- Auto-scheduling or optimising a timetable for access.
- Enrolment data, demand forecasting, or waitlists.
- Electives as a watch rule: course sets that *mean* "any two of these five". The watch still means "all of these";
  choosing a subset is something the user does when browsing (D3), and the health line, notices and seats stay
  about full slates.
- A student record: the analyzer never stores who a student is or what they chose.
- A student's year across terms (Fall and Winter together).
- Courses scheduled by other departments, which aren't in the database (possible later, through shared schedules).
- The app suggesting or prompting for watches (D4).
- Creating watches from grid selections or the section editor (D5; possible later).

### Production and data safety
- **The analyzer itself** only reads sections and watches and computes. No new persisted data, no save-path change:
  the four shared-database rules are untouched and the `MinimumVersionToWrite` answer is No.
- **Copying watches with a semester (Q6)** adds rows to an existing table inside the existing copy, with no new
  field, and older versions read, edit and delete those rows correctly. Courses and tags are shared across semesters,
  so a copied watch's course and tag IDs stay valid even when a course isn't offered. The copy should write watches in
  the same transaction as the sections, so a failed copy leaves neither. **For Greg:** the `MinimumVersionToWrite`
  question taken literally ("could the new version save anything the old version wouldn't have?") says Yes, since
  copying now saves extra rows; the intent (nothing an older version would damage) suggests No. If Q6 stores a
  per-semester "not offered, keep anyway" mark, that is new data and a Yes.
- **Paste-a-list and the course picker (Q7)** are new ways to fill in the existing model: No.
- **Changing the teal boxes (Q16)** is display only: No. It does change what existing users see.
- **Anything newly stored is a Yes:** an expected intake per watch (Q4), a "students take all of these" mark (Q1),
  a link from reserves to watches (Q4). `ProgramWatch` already derives from `PersistedRecord`; any future code that
  rebuilds a watch (an edit form) must call `WithExtensionDataFrom`.
- **No new save path** anywhere: every write goes through the existing save.

### Decisions made

| # | Decision | Rationale |
|---|---|---|
| D1 | **One combined spec (2026-10-30).** Easy access watches and the Access Analyzer are designed together | Every access feature depends on watches existing; an analyzer nobody sets up produces nothing |
| D2 | **Terms (Q10, 2026-11-06).** The user defines **access watches**; each specifies a **course set**; a **slate** (full slate) is a selection of non-conflicting sections, one for each course in the set; a **partial slate** covers some of the courses. "Program" and "timetable" are not used for these | A university program spans several semesters, and course sets also serve cohorts and niches. The many section combinations for one course set must not each be called by the course set's name; "slate" is a specific noun for one combination |
| D3 | **Partial slates are first-class (2026-11-06).** The user can pick any subset of a watch's course set and page through its partial slates, even when full slates exist | Students often study part-time and don't take everything in their semester's course set. An advisor helping a student who takes 3 of 5 courses wants to page through the options quickly |
| D4 | **The app does not prompt for watches (Q9, 2026-11-06).** No "do students take these together?" questions. Instead, setting up course sets in advance is made very easy | There is bound to be a lot of overlap, so users would turn the prompt off at once. The course sets the user sets up are the statement of what matters |
| D5 | **Creating watches from the grid or the section editor is deferred (Q8, 2026-11-06)** | Appealing, but not needed at this stage; the Access Watch panel with paste and autocomplete is enough |
| D6 | **The editor points, the panel explains (2026-11-06).** While a section is edited, the editor shows only a short line when the change affects a watch ("Affects access for Enviro Y2. See the Access Watch panel"). Details and notices live in the Access Watch panel | The editor has little room; a full explanation per watch would crowd it |
| D7 | **Pinning is for stepping (2026-11-06).** The user can pin one or more sections and step through only the full or partial slates that contain them | The advisor's case: the student says "I want that one"; show every slate that keeps it |
| D8 | **The access-watch code is a general API (2026-11-06).** Watch coverage, the clash test and the slate analysis are one UI-free service with a "what if" entry point for unsaved or proposed changes (see "How the analysis works") | The Slot Browser will need it later to rank placements (Q12); one engine means the finder and the panel never disagree |

### Open questions

| Q | Question | Recommendation |
|---|---|---|
| Q1 | **Which watches does the analyzer judge?** "Students take every course" fits an explicit course list. It doesn't fit a tag-mode watch like "all 300-level BIOL". Worse, in tag mode the course set is implicit: untag the only tagged section of a course and the course drops out, so access looks *better* | Course-list watches only. Tag-mode watches keep their boxes but get no health line, with a quiet note saying why |
| Q2 | **Lectures and labs.** If a course has lecture and lab sections, a slate needs one of each: one section per course *and section type*, not per course. How is this modelled in real data: sections of the same course with a section type, separate courses, or meetings inside one section? Are particular labs tied to particular lectures? | Needs Greg's knowledge of the data. If labs are sections with a section type, group by (course, section type); tied labs are a later step |
| Q3 | **Sections with no meeting times** overlap nothing, so they fit every slate. Right for asynchronous online, misleading for "not scheduled yet" | Count them as fitting, and say so in the health line ("assumes 2 sections without times fit anywhere") |
| Q4 | **Seats.** Capacity is shared with other students (40 seats in PSYCH 101 A aren't 40 Enviro seats) and with other watches (PSYCH 101 A may be in two watches, each counting all 40). Some sections have no capacity set. A number means more against an expected intake, and reserved seats could give a guaranteed number | Show "up to N students", worded as a ceiling. Flag any section whose seats another watch also counts, naming the watch. If a usable section has no capacity, say "seats unknown" and name it. Splitting shared seats between watches, expected intake and the reserves link wait for a later release |
| Q5 | **What puts a notice on a watch?** | Only a change that matters: no full slate left; a course in the set not offered; seats fall below expected intake (once that exists); a section becomes newly essential. Also a notice when a watch that had no full slate gets one back. Each notice names the sections and a next step |
| Q6 | **Carrying watches forward.** Copy Semester copies watches. A semester with none, whose same-named semester last year had some, gets a one-time offer to bring them over. A course in a copied watch may not be offered in the new semester | Copy both ways. After the copy, one message lists the watches with courses not offered ("Enviro Y2: GEOG 250 isn't offered in Fall 2027"), and the panel keeps flagging them. The user removes the course from the watch, or leaves it while a section is still to be added. Until then the watch's health is about the courses that are offered, clearly labelled ("4 of 5 courses offered") |
| Q7 | **Creating a watch.** Paste course codes, or type and pick from an autocomplete course picker; name filled in automatically; tags and levels under "more options". Codes the department doesn't schedule get a plain note | Yes. Course list becomes the default mode |
| Q11 | **Viewing slates.** One grid picture (essential / usable / stranded) first, then stepping through slates with the Room Browser's ghost tiles and stepper, with pins (D7) to narrow | Yes; details in Phase 3 |
| Q12 | **Slot Browser D3.** Judge access levels by whole slates (level 1: no overlap; level 2: overlaps, but every watch still has a full slate; level 3: some watch loses its last full slate), using this engine | Yes, one shared engine. Settle it in the Slot Browser spec, whose Phase 1 is still open |
| Q14 | **The student's own constraints.** Dana's student works Tuesday mornings. Should the user be able to block out times so that slates using them are hidden or sorted last? | Not in the first version: paging plus pins is enough to start. If advisors ask, add a simple "student is busy" time block, held in memory only, never saved |
| Q15 | **Who browses slates?** Advisors may not be TermPoint's usual schedulers, and may open the database read-only (or a copy). Browsing slates changes nothing, so it must work read-only | Yes: browsing and paging work in read-only mode and in the WASM demo. Whether advisors are a target audience (install, licensing) is Greg's call, outside this spec |
| Q16 | **What the teal boxes should show.** Today any overlap between two courses in a watch is boxed, even when both sections can still be part of other full slates | Box an overlap only when it costs students something: it leaves a section stranded, or leaves the watch with no full slate. Other overlaps get no box (or a faint one; Phase 3). Hovering a box says what it costs, in D2 terms |

### Delivery order (proposed)
1. **Easy access watches** (Q6, Q7, D2 terms). Small, improves the feature users already have, ships alone.
2. **The analyzer** (health line, panel notices, editor pointer, grid picture, pins, subset paging, revised teal
   boxes).
3. **The Slot Browser** uses the engine for D3.
4. Later: expected intake, shared-seat splitting and reserves (Q4); creating watches from the grid (D5).
