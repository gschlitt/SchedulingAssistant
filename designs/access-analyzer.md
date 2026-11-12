# Access Analyzer and Easy Access Watches — Progressive Design Spec

## Status
Current phase: **3 — UX Sketch, in wireframe rounds** (round 1 settled 2026-11-10; D17 agreed; the rest of the
draft is ideas to try). Phase 2 locked 2026-11-10
(Q18's extra part names and Q28 left open, not blocking); Phase 1 locked 2026-11-06.
Last session: 2026-11-10
Next step: Greg's review of wireframe round 3 (stepping, pins, subsets); round 2 settled 2026-11-10. D26: tags watchable directly. D25 (2026-11-10): an access watch is a
filter preset you choose to watch; round 4 moves to the filter bar
Release: not scheduled. Probably not 1.2.4; the spec continues ahead of coding and the release is decided later

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
9. *(Added 2026-11-10, Phase 2 D16.)* "Check all courses" lists courses whose own parts can't all be taken together
   (a lab that clashes with the only lecture), with no watch needed.

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
  the four shared-database rules are untouched and the `MinimumVersionToWrite` answer is No. *(Phase 2 adds a
  user-edited parts list on courses, D9/D13, which is a Yes; see Phase 2's Production section.)*
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

### Questions settled at lock (2026-11-06)
At the Phase 1 lock Greg adopted each recommendation below as the answer, except **Q2**, which has no recommendation
to adopt and is carried into Phase 2. Later phases may still refine the details each answer leaves to them.

| Q | Question | Answer (recommendation adopted) |
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
0. **Delivery type** (Phase 2 D14). A small section field of its own; the analyzer doesn't depend on it, so it can
   ship in the analyzer's release or earlier. Its details (editor, filter, import column, export, Copy Semester) are
   outside this spec.
1. **Easy access watches** (Q6, Q7, D2 terms). Small, improves the feature users already have, ships alone.
2. **The analyzer** (health line, panel notices, editor pointer, grid picture, pins, subset paging, revised teal
   boxes).
3. **The Slot Browser** uses the engine for D3.
4. Later: expected intake, shared-seat splitting and reserves (Q4); creating watches from the grid (D5).

---

## Phase 2: Domain Model (LOCKED 2026-11-10)

Entities, relationships and the rules that always hold. No code, tables or screens (Phases 5, 4, 3).

### What the real data says about labs (Q2 probe, 2026-11-06)
Two databases, two opposite models. Section types are user-defined, so **what a section type means differs by
institution**.

**UFV BIOL** (336 sections, 15 semesters): labs are **separate courses** (BIO 111 and BIO 111LAB). Section type
means **delivery mode** (F2F, Blend, Online Asynch, Online Synch); BIO 105 has F2F and online sections, which are
alternatives. "Lab" is a meeting type, occasionally inside a lecture section. Codes suggest a campus tie (AB/A# at
Abbotsford, CH/C# at Chilliwack). No capacities set.

**UBC** (563 sections, Winter Term 1 2026-27, from the Workday scrape): labs and tutorials are **sections of the
lecture's course**, told apart by section type (Lecture, Laboratory, Discussion, Studio, Seminar, Workshop, Thesis,
Independent Study). Of 228 courses, 59 are Lecture + Discussion, 51 Lecture + Laboratory, 28 all three. BMEG 230:
lecture 101, labs L1A–L1D, tutorials T1A–T1D; a student takes one of *each*. Seven courses have two or more lectures
(MECH 463: 101, 102, two labs, four tutorials). Nothing links a lab to a lecture. No capacities set; 52 sections have
no times; one watch exists ("Prior Biomed Sequence", course list of six).

Consequences:
- "One section per course" is right for UFV and **wrong for UBC**: a BMEG 230 slate needs a lecture, a lab and a
  tutorial.
- "One per (course, section type)" is right for UBC and **wrong for UFV**: it would demand one F2F *and* one online
  BIO 105.
- So each **course** says which section types are its **parts** (students take one of each: Lecture, Laboratory,
  Discussion). Any other section type, such as F2F or Online, is just a description. The app guesses each course's
  parts from its sections and the user confirms or corrects them under Courses (D9, D13).
- How section type is used elsewhere: Greg's survey of about 20 institutions found that their uses fit into a section
  type, a delivery type and perhaps a tag or two. So sections gain an optional **delivery type** (D14), and section
  type can mean "which part" everywhere.
- Data caveat: in 4 UBC courses a lab and a tutorial meet at exactly the same time (BMEG 230 L1A and T1A, Tue
  15:00). They probably alternate weeks, which the scrape doesn't record; as entered they clash, and the analyzer will
  say so.

### Stories

**Labs as parts of a course (UBC).** Bob's "Biomed Y2" watch lists BMEG 230, CHBE 201 and ELEC 203. A slate for it holds
five sections: BMEG 230's lecture, one of its labs and one of its tutorials, plus a CHBE 201 lecture and an ELEC 203
lecture. Bob moves BMEG 230 L1C to Wed 13:00, onto CHBE 201's only free lecture slot. L1A, L1B and L1D still fit, so
nothing is lost and no notice appears. Had L1C been the only lab free of CHBE 201, it would have been *essential by
timing*, and the panel would have said so before he saved.

**Labs as courses (UFV).** Alice's "Bio Y1" watch lists BIO 111, BIO 111LAB, CHEM 113 and MATH 111. Here the lab is
its own course, so it is in the course set by name. BIO 111's sections are typed F2F and Online; neither name looks
like a part, so the guess gives BIO 111 no parts, and a slate takes any one of its sections. Same engine, same
answers. Nothing in Alice's database changes.

**Checking the guesses.** Bob opens Biomed Y2 for the first time. The watch's detail lists "BMEG 230: lecture + lab +
tutorial (guessed) · CHBE 201: lecture (guessed) · ELEC 203: lecture (guessed)". They look right; he clicks "These
are right", which saves them to the three courses, and the "(guessed)" marks go. Nothing was written before he
clicked.

**A part goes missing.** BMEG 230's parts are lecture, lab and tutorial. Bob deletes the last lab section by
mistake. Without a rule, slates would get *easier* (no lab needed). Instead the panel says: "BMEG 230 has no lab
sections in Winter Term 1. Add one, or edit BMEG 230's parts if it no longer has a lab."

**A first-time user.** Carla starts with an empty database, then imports her sections a week later. The parts are
guessed as soon as the sections exist. She never sees a setup step.

**Chilliwack.** Bio Y1 shows "32 slates". Most of them pair an Abbotsford lecture with a Chilliwack lab, a 30-minute
drive away. Nobody takes those. Whether the app should count them is Q17.

**Only section vs. essential by timing.** CHEM 213 has one section, A. It is in every slate simply because it is the
only one; Alice knows that already. PSYCH 101 has three sections, but B and C both clash with CHEM 213 A, so every
slate uses PSYCH 101 A. *That* is news. The health line names PSYCH 101 A, not CHEM 213 A (Q19).

**What if.** Alice has PSYCH 101 A open in the editor at Tue 10:00, not yet saved. The panel and the editor pointer
both answer from the same picture: the saved semester with that one change laid over it. Bob, viewing the same
database, sees the saved semester only.

### Entities

| Entity | What it is | Relationships |
|---|---|---|
| **Course** | A catalogue course (BIO 111, BMEG 230). Shared by all semesters | Has sections, per semester; has a **parts** list, confirmed or not set (rules 4–6) |
| **Section type** | User-defined kind of section (Lecture, Laboratory, Discussion…; at some institutions still F2F, Online) | Shared by all semesters; a section has at most one; a course's parts are drawn from them |
| **Part** | One section type a student of the course takes one section of (BMEG 230: lecture, lab, tutorial). A course with no parts or one part asks for one section, of any type | Belongs to a course; confirmed by the user or guessed |
| **Delivery type** | User-defined description of how a section is taught (F2F, Online, Blend) | Shared by all semesters; a section has at most one; plays no part in access (D14) |
| **Section** | One offering of a course in one semester | Belongs to one course and one semester; has meetings; may have a section type, a delivery type, a capacity and a campus |
| **Meeting** | One weekly time a section meets: day, start, end, which weeks (every week, odd, even, or a week list). In person or remote | Part of one section |
| **Access watch** | A named, user-defined statement that some students need these courses together this term. Enabled or not | Belongs to one semester; has one course set (or, the advanced kind, a tag/level rule) |
| **Course set** | The courses a course-list watch names | Part of one watch; refers to courses |
| **Semester view** | The semester's sections as the analyzer sees them: the saved sections, with any *proposal* laid over them | One per question asked; never stored |
| **Proposal** | Changes not yet saved: a section moved or edited, added, or removed | Part of a semester view; may be empty |
| **Slate / partial slate** | A choice of sections, one for each part of some courses, none clashing (D2) | Drawn from one semester view, for one watch |
| **Analysis** | Everything reported about one watch in one view: slates, roles, best partial slate, seats, flags | Derived; never stored (D10) |
| **Notice** | A message on a watch saying its access got worse (or came back) after a change | Derived by comparing two analyses (Q21) |

Subsets and pins are not entities: they are choices made while browsing, held only by the screen (D3, D7).

### Rules (invariants)

**Coverage**
1. A course-list watch covers every section in the view whose course is in its course set. A tag/level watch covers
   sections by today's rule (all listed tags, any listed level). One coverage rule, used by every caller.
2. Only **enabled course-list watches** are analysed (Q1). Disabled watches get no analysis, no notices, no boxes.
   Tag/level watches keep today's pairwise boxes and get no health line.
3. A course in the course set is **offered** if the view has at least one section of it, **not offered** otherwise.
   "Offered" is computed every time, never stored. Analysis runs on the offered courses and says so ("4 of 5 courses
   offered"). A course whose sections all lack times is offered.

**Course parts** (D9, D13)
4. Each course's parts list is either **confirmed** (stored, set by the user under Courses; may be empty, meaning
   "students take one section") or **not set**.
5. While not set, the parts are **guessed**: the section types found among the course's sections, in every semester
   of the database, whose names are on the part-name list (Q18). The guess is worked out whenever it's needed, by any
   caller, and never stored. The Courses flyout shows it, marked as guessed.
6. Only a user's action stores a parts list: confirming the guess, or editing it. That is an ordinary edit and save.
7. A course with **no parts or one part**: every section of it counts as a choice, whatever its section type (UFV's
   F2F and Online sections; a Lecture-only course with one Online section).
8. A course with **two or more parts**: each section fills the part matching its section type. A section with no
   type, or a type that isn't one of the parts, fills no part. It isn't counted, and the watch's detail names it
   ("BMEG 210 ab1 has no section type, so it isn't counted").
9. A section has one section type, so it fills at most one part (D15).
10. A part with no sections in the view is **missing**. The course is then treated like a course not offered (rule 3):
    left out of the analysis and named ("BMEG 230 has no lab sections in Winter Term 1"), never quietly made easier.

**Delivery type** (D14)
11. A section may have a delivery type. It describes the section; parts, slates and the clash test ignore it. It is
    separate from a meeting's remote flag.
12. Existing data is never moved: where section type holds delivery mode (UFV), the guess finds no parts, every
    course asks for one section, and that is correct. Users may re-enter delivery modes as delivery types if they
    wish; the app offers no tool for it.

**The clash test**
13. Two sections **clash** if some meeting of one and some meeting of the other fall on the same day, overlap in time
    (half-open: one ending at 10:00 and one starting at 10:00 don't clash), and share at least one week (every week
    shares with everything; odd and even never share; week lists share if they have a week in common). One test, used
    by the detector, the analyzer and the Slot Browser.
14. Remote meetings with times are ordinary meetings. A section with **no meeting times** clashes with nothing; it is
    reported wherever it affects a number (Q3).
15. Campus and travel time play no part in the clash test. But a slate's in-person sections must all be at one
    campus; online sections and sections with no campus fit any campus (Q17).

**Slates**
16. A **slate** for a set of courses has exactly one section for each part of each course, and no two of its sections
    clash. Two sections of the same part are never in one slate; sections of different parts of one course (a lecture
    and its lab) must be. For a course with no parts or one part, this is "one section of the course".
17. A **full slate** is a slate for all offered, complete courses of the course set (rules 3, 10). A **partial slate**
    for a subset is exactly a full slate of that subset (D3); nothing else distinguishes them. Subsets are chosen by
    course, never by part: a student taking BMEG 230 takes all of it.
18. A **pin** is a section that must be in every slate shown. A pinned section is the only candidate for its part.
    Pinning a section whose course isn't in the chosen subset adds nothing; pinning two clashing sections leaves no
    slate, and the screen says why.
19. Slates are counted up to a cap; beyond it the count is "more than N".

**Section roles** (always relative to a set of courses and pins)
20. **Usable:** in at least one slate. **Stranded:** in none. **Essential:** in every slate (only defined when at
    least one slate exists).
21. An essential section is either the **only section** of its part (BMEG 230's single lecture), or **essential by
    timing**: its part has other sections, but each of them is in no slate (Q19).
22. The health line and notices are always about the **full course set with no pins**. The grid picture and stepping
    follow whatever subset and pins the user has chosen (D12).

**Best partial slate**
23. When no full slate exists, the **best partial slate** is a slate for the largest subset that has one. The
    courses outside it are "the courses that can't fit". When several subsets tie, all their left-out courses are
    named ("CHEM 101 or MATH 111 can't fit with the rest").

**Seats**
24. **Seats** for a set of courses is the largest number of students who can each be given a slate such that no
    section is given to more students than its capacity. Never the smallest course total (the seats trap). With parts,
    the smallest part (often the labs) usually sets the limit; the definition covers that without a special case.
25. A section's capacity is a ceiling on *all* its students, not just the watch's: seats is a ceiling, worded "up to".
26. Each watch's seats are computed on their own. A section covered by two analysed watches has its seats counted by
    both; that is flagged, naming the other watch, never split (Q4).
27. Sections with no capacity: see Q20. Reserves play no part yet (Q4).

**What if (D8)**
28. Every answer is a function of (semester view, watch, subset, pins). The saved state is the view with an empty
    proposal; there is no second code path for it.
29. The view is the **whole semester**: grid filters never change it.
30. A proposal can move a section between courses or change its section type; coverage and parts follow the change.

**Notices** (Q5)
31. A notice compares the analysis before a change with the analysis after it, for each analysed watch the change
    touches. Only these count:
    - **Lost:** had a full slate, now has none.
    - **Not offered:** a course in the set lost its last section.
    - **Part missing:** a course lost the last section of one of its parts (rule 10).
    - **Newly essential by timing:** a section is essential by timing now and wasn't before.
    - **Restored:** had no full slate, now has one (good news; it replaces a Lost notice).
    - *(Later)* seats fall below an expected intake.
    A drop in slate count or seats alone is not a notice. Each notice names the sections involved and a next step.
32. Unsaved changes produce the editor pointer, never a notice. Notices come from saved changes.

**Costly overlaps (teal boxes, Q16)**
33. A clash between two covered sections of different parts (different courses, or a lecture and a lab of one
    course) **costs something** if, with that one clash ignored, a stranded section would become usable, or the best
    partial slate would cover more courses. Only costly clashes are boxed. When the watch has no full slate, every
    clash that involves a course that can't fit is also boxed, and the hover says where fixing only one clash isn't
    enough (Q22).

**Carrying forward (Q6)**
34. A copied watch is the same watch (name, enabled, course set, tag/level rule) in the new semester, with its own
    identity. Courses and tags are shared across semesters, so its references stay valid.
35. A course "kept while not offered" is simply a course left in the course set; rule 3 flags it every time. Nothing
    extra is recorded (Q23).
36. Parts live on courses, so they carry across semesters by themselves. But Copy Semester can copy sections without
    their section types; a copied course with two or more parts would then have no section counted (rule 8). Copying
    must keep section types for such courses, or warn first (how: Phase 3).

**Checking guessed parts**
37. Wherever a guessed part changes an answer, the user can see it: a watch's detail lists the parts of each of its
    courses, marked guessed or confirmed, with a way to edit each course and to confirm all of the watch's guesses at
    once. No banner, no setup step, no prompt outside the watch.
38. The app speaks up about a guess only when it looks doubtful, naming the course: a missing part (rule 10);
    and sections not counted (rule 8). Each goes away once that course's parts are confirmed. (A prompt for courses
    with several section types but at most one guessed part was dropped, Q26.)

**Check all courses** (D16)
39. A course set may hold a single course; every rule above applies to it unchanged.
40. "Check all courses" analyses each offered, complete course of the semester view as its own course set, with no
    pins, and reports only courses with no slate or with stranded sections. Only courses with two or more parts can
    have such findings. Courses with a missing part (rule 10) are listed separately and less prominently, since parts
    guessed from earlier semesters can be out of date. It runs when the user asks; its results are never stored.
41. The section editor does **not** point out clashes within a course on its own: it speaks only when a watch is
    affected, as Phase 1 says (D6; Q27).

### Decisions (confirmed by Greg at the Phase 2 lock, 2026-11-10)

| # | Decision | Rationale |
|---|---|---|
| D9 | **One section per part, and parts belong to the course (answers Q2's main part, 2026-11-10).** Each course lists the section types students take one of each of; a slate takes one section of every part of every course (rule 16) | UBC models labs as typed sections of the lecture's course; UFV models them as separate courses and uses section type for delivery mode. Neither "per course" nor "per section type" is right for both. Per course, the app also knows when a part is missing, and the same type can be a part in one course and not in another |
| D10 | **Nothing the analyzer reports is stored.** Slates, roles, seats, flags, subsets and pins are recomputed from the view | No save-path change; nothing to go stale between users |
| D11 | **One view for saved and what-if (rule 28)** | The editor pointer, the panel and the Slot Browser can never disagree with each other |
| D12 | **Health and notices are about the whole course set; the grid picture follows the browsing choices** | An advisor's pins must not change what Alice is told about the watch's health |
| D13 | **Guessed parts are never stored (2026-11-10).** Only the user's confirm or edit stores a course's parts. No migration, no write at open | A write at open is a write the user didn't make, on a shared database, and fails read-only. A stored guess also goes stale: a first-time user with no sections yet would never get guesses. Worked out every time, the guess is always current |
| D14 | **Delivery type is a separate, optional section field (2026-11-10).** User-defined list like section types; access ignores it; existing data is not moved, and there is no tool to move it | Greg's survey of about 20 institutions found section type, delivery type and a tag or two cover their uses. Moving data automatically would rewrite production sections on a guess. A small feature of its own that the analyzer doesn't depend on; it may ship earlier |
| D15 | **One section type per section (2026-11-10).** Several types per section was considered and set aside | It touches every reader of `SectionTypeId` (26 files: filters, list, export, import, Copy Semester, section code patterns tied to one type), and older versions would show sections with no type. Delivery type covers the main reason for wanting two |
| D16 | **Check all courses (Greg, 2026-11-10).** A built-in, on-demand check of each course on its own (rules 39–41), not stored watches | No setup, so it serves departments with no watches. Hundreds of one-course watches would clutter the panel, be copied with every semester and store rows for nothing |

### Open questions

| Q | Question | Recommendation |
|---|---|---|
| Q17 | **Campus. ANSWERED 2026-11-10: one campus per slate (recommendation adopted).** Should a slate be allowed to mix in-person sections at different campuses (an Abbotsford lecture with a Chilliwack lab)? Counting them inflates slates and seats with choices nobody takes | Don't mix campuses by default: a slate's in-person sections share one campus; online and campus-less sections fit any campus. Is that right at UFV, and do some students genuinely split campuses? (UBC has one campus, so it's unaffected. If it must be switchable per watch, that is a stored setting: `MinimumVersionToWrite` Yes) |
| Q18 | **The part-name list. PARTLY ANSWERED 2026-11-10: the proposed list, plus more names Greg will add from his survey later; this need not block the Phase 2 lock (the list is a detail of the guess, settled by Phase 5 at the latest).** Which section type names does the guess treat as parts? | Matched ignoring case, as whole words or common short forms: Lecture/LEC, Laboratory/Lab/LAB, Tutorial/TUT, Discussion/DIS, Recitation/REC, Seminar/SEM, Studio/STU, Workshop/WKS, Practicum/PRA. Greg's survey may add others (Clinical? Activity?). A stand-alone seminar course just gets one part, which is harmless |
| Q19 | **Which essential sections to name. ANSWERED 2026-11-10: only those essential by timing (recommendation adopted).** Every single-section part is trivially essential | The health line names only sections *essential by timing*; only-sections are mentioned in the watch's detail, not the line. Newly-essential notices are for timing only |
| Q20 | **Seats with no capacities. ANSWERED 2026-11-10: no number, one quiet hint (recommendation adopted).** Neither UFV nor UBC has any set, so "seats unknown" would be all either ever shows | If any usable section lacks a capacity, show no seat number; say once, quietly, "Set section capacities to see how many students fit." Treating unknown as unlimited gives numbers that look real and aren't |
| Q21 | **How long notices live, and whose saves make them. ANSWERED 2026-11-10: session only, from any saves (recommendation adopted).** | Kept in memory for the session; cleared when dismissed or when no longer true. They come from any change this app sees (own saves, and other users' saves when the semester reloads). Not stored: the health line already shows the current state after a restart. Storing them would be new data (Yes) |
| Q22 | **Teal boxes when no single clash is to blame. ANSWERED 2026-11-10: box every clash involving a course that can't fit (recommendation adopted).** Rule 33 boxes a clash only if ignoring *that one clash* would help. Example: each of PSYCH 101, CHEM 213 and GEOG 250 has one section. CHEM 213 A (Tue 10:00–11:20) clashes with PSYCH 101 A (Tue 9:00–10:20) and with GEOG 250 A (Tue 10:30–11:50); PSYCH and GEOG don't clash. There's no full slate. Ignore the CHEM–PSYCH clash and CHEM–GEOG still blocks it; ignore CHEM–GEOG and CHEM–PSYCH still blocks it. So neither clash counts as costly, and the grid would show **no boxes** while the watch is broken | Box every clash that involves a course that can't fit (here both CHEM 213 clashes). Hover: "CHEM 213 A clashes with PSYCH 101 A and GEOG 250 A; fixing only one of these isn't enough." Alternative: for such a watch, box every overlap as today, including harmless ones |
| Q23 | **"Keep anyway" for not-offered courses. ANSWERED 2026-11-10: no mark (recommendation adopted).** Phase 1's Q6 mentioned a possible per-semester mark | No mark: leaving the course in the list *is* keeping it, and the flag is computed (rule 35). Phase 1's "one-time offer" to bring watches over needs remembering that it was declined; that is Phase 4's to settle |
| Q24 | **Tied parts. ANSWERED 2026-11-10: no ties in the first version (recommendation adopted).** Is a particular lab ever tied to a particular lecture (MECH 463: lectures 101 and 102; are labs L1A/L1B open to both)? Same question for UFV's AB lectures and A# labs | Leave ties out of the first version: any section of a part goes with any section of another part. Add ties later if real data needs them |
| Q26 | **The "several types, one part" prompt (rule 38). ANSWERED 2026-11-10: dropped (recommendation adopted).** "BIO 105 has F2F and Online sections; they're counted as alternatives. Is that right?" catches a part name the list doesn't know, but at UFV it would come up for many courses | Drop it; rely on the parts being visible in the watch's detail (rule 37). The other two prompts point at a real problem and stay |
| Q27 | **Editor pointer for clashes within a course. ANSWERED 2026-11-10: no; the editor speaks only about watches (rule 41).** | — |
| Q28 | **"Taken together with" list on courses (left open 2026-11-10).** At UFV, lecture and lab are separate courses, so "Check all courses" can't see lecture–lab clashes | Later: a course-level list (BIO 111LAB with BIO 111), guessed from codes and confirmed like parts. Another stored course field (`MinimumVersionToWrite` Yes; best shipped with parts) |

(Q25, untyped sections in a multi-part course, is settled by rule 8.)

### Check all courses (Greg, adopted 2026-11-10)
A course set may hold a single course: a slate for BMEG 230 alone is a lecture, a lab and a tutorial that don't clash.
**"Check all courses"** is a one-click check in the Access Watch panel that treats every offered course in the
semester as its own course set and lists only the courses with a problem. Nothing to set up, nothing stored, works
read-only. See rules 39–41 and D16.

Story: Bob moves BMEG 230's only lecture to Tue 15:00, where lab L1A also meets. He clicks "Check all courses":
"BMEG 230: lab L1A clashes with the only lecture, so no student can take it." Further down, less prominent: "BMEG 310
has no tutorial sections this term (guessed from earlier terms)."

**Gap, left open (Q28):** at UFV, lecture and lab are separate courses (BIO 111, BIO 111LAB), so checking each course
alone never sees a lecture–lab clash; the check finds almost nothing there. A course-level **"taken together with"**
list, guessed from codes and confirmed like parts, would close it. It would be another stored course field.

### Production and data safety (Phase 2)
- **Stored changes, all additive:** a parts list on courses (D9, D13) and a delivery type on sections (D14). Both are
  new kinds of edit, so the release that ships them answers **Yes** to the `MinimumVersionToWrite` question. If they
  ship in one release, that is one Yes. Courses and sections already derive from `PersistedRecord`, so older versions
  keep both fields when they read and write. Code that rebuilds courses or sections (editors, Copy Semester, clones)
  must copy the new fields and call `WithExtensionDataFrom`; `SectionCloneTests` and the extension-data tests should
  cover them.
- **Nothing is written without the user:** no migration, no write at open, no guess stored (D13), no data moved
  (D14). The four shared-database rules are untouched: every new write is an ordinary edit through the existing save.
- **Copy Semester** must not strip section types from multi-part courses (rule 36).
- Two answers would add stored data if chosen: a per-watch campus switch (Q17) and stored notices (Q21); both
  recommendations avoid it.

---

## Phase 3: UX Sketch (DRAFT 2026-11-10, not locked)

Screens, words and interactions. No code, tables or classes (Phases 4–5). Sketches are ASCII; sizes are
indicative. Decisions D17 onward are **proposed** until Greg locks the phase.

**How Phase 3 is being settled (Greg, 2026-11-10):** one screen per round, with a clickable HTML wireframe on mock
data (`designs/wireframes/`), rather than signing off the sections below as a batch. Each round settles only what it
shows; the sections below are ideas to try until a round confirms them.

### Wireframe rounds
| Round | Covers | Status |
|---|---|---|
| 1 | The Access tab's watch list, the filter-bar summary, popping tabs out (`access-panel-r1.html`) | **Settled 2026-11-10** |
| 2 | Opening a watch: what its line and detail say, and the grid picture (`access-panel-r2.html`; numbers computed from a mock semester by a miniature slate search) | Reviewed 2026-11-10; revision 2b built (folded detail, health-line hovers, "any 1 of 3 sections" wording, per-line marks, grey-out-or-keep toggle for other grid marks); 2b reviewed; 2c built (fewer hovers, help icon, other courses hidden while a watch is open). **Settled 2026-11-10** |
| 3 | Stepping through slates, pins, ticking a subset (`access-panel-r3.html`) | Reviewed 2026-11-11; 3b built (no "any 3" line, stepper in the legend bar, stranded sections flagged, yellow cautions for hard-to-reach and heavy-load sections, clickable section names, legend bar without a key), awaiting Greg |
| 4 | Saving a filter preset and watching it, at the filter bar (D25); ticking Watch on a tag (D26); picking several courses easily | — |
| later | Courses (parts), Copy Semester, Check all courses | — |

**Round 1, settled by Greg (2026-11-10).** Judged at the level of the list, not the wording of each health line or
the opened watch (round 2).
1. At the default 250 px height, the list shows at a glance which watches need attention.
2. One line per watch, **wrapping to the tab's width** rather than being cut off. (Two-line rows dropped.)
3. Marks help. ▲ (needs a look) and ○ (fine) stay. The ● for "depends on one section" isn't intuitive; it stays as a
   placeholder. **Parked idea:** a "fragile" icon like the broken-glass packaging symbol, drawn as an AXAML path.
4. The notices box at the top of the list is the right weight.
5. The filter-bar summary ("6 watches · 1 notice") points to the tab well enough.
6. The pop-out icon on the tab strip pops out the tab in front; closing its window puts it back.

**Round 2, Greg's review (2026-11-10).** A good start. Points:
1. The detail is busy with everything shown: **open it folded**. But some facts now behind the fold must be
   brought forward (point 2).
2. The health line is useful; some of its statements need **hover help**. Above all, "up to 40 students" should
   say on hover what else those sections serve that the app knows of (other watches: "PSYCH 101 A's seats are also
   counted by First-year Science"), rather than leaving that to the fold. "Depends on PSYCH 101 B" also needs a
   hover: what it means and how the watch depends on it.
3. **Wording bug found:** the course list said "STAT 104 · One section" although STAT 104 has three sections. "One
   section" meant "students take any one section", but it reads as a count. Reword (e.g. "any 1 of 3 sections";
   for parts "1 of 1 lecture + 1 of 3 labs").
4. Parts and These are right: fine.
5. The legend is useful. **Marks must be per section, not per tile:** co-scheduled sections share a tile (PSYCH 101
   A with STAT 104 C), so fading, hatching and outlining apply to each section's line in a tile.
6. Outlining courses that can't fit (no full slate) is a good idea, subject to the same per-section rule.
7. **The grid picture must fit with the grid's other decorations** (subject colour fill and markers, semester
   borders, shared-schedule overlays in purple outline, flags, teal boxes). Its colours and formats can't fight them.
8. The tile hover giving the reason ("clashes with CHEM 213 A, which every slate uses") is liked.

**Round 2b, Greg's review (2026-11-10).**
1. **Hovers: much less.** Too much detail is flashy, busy and irritating. One help icon covers the whole Access idea;
   users aren't told what a slate is every time. Hovers stay only where needed, one short sentence:
   "up to 60 students" → "Seats shared with First-year Science (PSYCH 101 and STAT 104)"; "depends on PSYCH 101 B" →
   what's wrong with the other sections ("PSYCH 101 A clashes with CHEM 213 A; …"). No hover for "4 of 5 courses
   offered" or "without times": the detail below already says it.
2. **Keep subject colours and the grid's other marks** while a watch is open (no greying out).
3. **Question raised:** why show sections of courses not in the watch at all? 2c tries hiding them while a watch is
   open, with "Show other courses" in the legend to bring them back, faded.

**Round 3, Greg's review (2026-11-11).**
1. **Drop the "any 3 of these 5" line.** Users care about specific cases, which they reach by ticking courses.
2. **Right-click to pin is good.**
3. **The stepper goes in the grid's legend bar,** not the panel: *Step through slates*, then ‹ Slate i of N › with
   the slate's sections, and a **Clear** button back to the overview.
4. **Flag stranded sections** (sections no one taking the watch's courses can use), not only hatch them on the
   grid. 3b: "1 section stranded" in the health line, and a ▲ line in the detail with the reason ("PSYCH 101 C is
   stranded: it clashes with CHEM 213 A, which every slate uses."). **Agreed:** the watch's list mark doesn't change
   for stranded sections alone, since one may be meant for other students.
5. **Yellow cautions from slate shares (Greg, 2026-11-11).** Nothing stranded doesn't mean balanced: CHEM 201 with 4
   lectures and 8 labs can have LAB-A in some slates but only with LEC1. The measure, in the graph picture (one group
   of vertices per course part, an edge between sections that don't clash, a slate = a clique with one vertex per
   group): **c(v)** = the slates section v is in. Within a group every slate uses exactly one section, so the shares
   c(v) ÷ N add to 1 and the **fair share** is 1 ÷ (sections in the group). Compared with it:
   - 0 → **stranded** (▲, point 4); all of it → **depends on** (●, existing).
   - under ⅓ of fair share → **hard to reach** (yellow ◆): "CHEM 201 LAB-A fits only with LEC1: it clashes with
     LEC2, LEC3 and LEC4." The reason is the other group where it fits with the fewest sections.
   - over 2× fair share, in a group of 3 or more → **carries a heavy load** (yellow ◆): "LEC1 carries a heavy load:
     LAB-A and LAB-B fit only with it", or, when no section leans on it alone, "…it is in far more slates than the
     other CHEM 201 labs, which clash more."
   - Never for a single section (its share is always fair), a pinned group, or a section with no times (online
     sections fit anything; flagging them was noise in the mock).
   - Health line: "1 section hard to reach", "2 sections carry a heavy load"; one ◆ line each in the detail; ◆ on the
     section's grid line with the same text on hover. Like stranded sections, cautions don't change the list mark.
   - Shown in 3b (watch Chem Y2; last Semester changes option for heavy loads).
   - For Phases 4–5: shares need exact slate counts, but the search stops at 10,000. Exact per-section counts stay
     cheap because courses that don't interact multiply. Thresholds (⅓, 2×) to be tuned on UBC and UFV copies. The
     measure counts ways to build a slate, not students; a seat-based version could fold into the seats calculation
     if slate share proves not enough. The heavy-load fallback wording is vague and needs a better reason.
6. **Section names in Access messages are clickable (Greg, 2026-11-11).** In ▲ and ◆ lines, pin chips and the
   stepper's slate, a section name selects that section exactly as clicking it anywhere else does
   (`SectionStore.SetSelection`): highlighted on the grid and in Workload, and Section View scrolls to its card
   (`ListScrollAnchorBehavior.ScrollItemIntoView`, item 23). Selecting doesn't open the editor. Light touch: names look
   like the text around them; underline and hand cursor on hover only (dotted underline stays "hover for help").
   For Phases 4–5: the engine returns messages as pieces (text + section id), not finished strings, and one reusable
   view piece shows text with clickable names (Avalonia runs take no clicks: link-styled buttons inline). Read-only
   safe; nothing saved. Shown in 3b.
7. **No key to the marks in the grid's legend bar (Greg, 2026-11-11):** "every slate uses it", "hard to reach" and
   the rest made the reader hunt. The bar holds only the watch's name (and "3 of 5 courses"), Step through slates /
   the stepper, Show other courses and Close. Each mark explains itself on hover; the help article has the key.

**Keep the interfaces open for other departments' data (Greg, 2026-11-10).** Proper sharing with other departments
is coming: their courses will be filterable like the department's own, and the analyzer must handle that data. Not
built now, but nothing in Phases 4–5 may close the door. Concretely, for Data Design and Architecture:
- **The semester view takes sections from more than one source** (this database, plus shared schedules), each
  section saying where it came from. The engine, the clash test and coverage never assume a section is local.
- **A course set may name courses this database doesn't hold** (another department's), identified by calendar code
  rather than only by a local course ID, so a watch can include them once their sections arrive.
- **Parts and capacities may come from elsewhere** or be missing for outside courses; the "not set" and "no
  capacity" paths already cover that and must stay the default, not an error.
- **Outside sections are read-only:** browsing, pins and slates work with them; the editor pointer, notices and
  Check all courses speak about them but never offer to edit them.
- **Display:** the grid picture and "Show other courses" treat shared sections like any others; today's purple
  outline stays their mark.

**An access watch is a filter preset you choose to watch (D25, Greg, 2026-11-10).** Course-list watches and
tag watches were two things to learn, and both overlap with tags. The fix is one thing users already make all the time.
- **Filter Set:** the combination of choices active on the Schedule View's filter bar at once. Inside one choice it
  means "or" (Level 300 or 400); between choices "and" (Upper level *and* Vancouver).
- **Filter preset:** a Filter Set the user names and saves, kept near the filter bar, applied with one click. Useful
  on its own.
- **Watching:** the same place lets the user tick **Watch** on a preset. A watched preset *is* an access watch. Users
  create one thing, not two.

*Why it fits (code read 2026-11-10).* The bar has nine choices: Instructor, Room, Subject, Campus, Section type, Tag,
Meeting type, Level, Course. The Course choice lists every course, not only those offered this term. Tags already
offers "all / any of the checked tags"; Level is "any of these". Both kinds of watch that exist today translate
directly: a course-list watch is Course = those courses; a tag watch is Tags (all) + Level (any). UBC's two watches
("Prior Biomed Sequence", "Mech") convert with no meaning lost.

*What follows:*
- **Course set** (D2 term kept) = the courses of the sections the watched preset *shows* this semester: the result
  of the Filter Set, not what is ticked in it (Greg, 2026-11-10). Tag "BIOMED3" and Subject BIOMED + Level 3 show
  the same sections, so they make the same watch.
- **No "not offered" (Greg, 2026-11-10).** A course with no sections this term isn't in the result, so the watch
  can't know it's missing, and there is no exception for courses named in the Course choice. The only signal is
  the drop-out notice below, when a course leaves the result. This drops Phase 1's "copied watches flag courses not
  offered" and the "4 of 5 courses offered" wording (the round 2 wireframe still shows GEOG 250 as not offered).
- **Parts checks run on the result too.** If BIOMED 310's labs don't carry BIOMED3, the watch has none; the
  message says "BIOMED 310's labs aren't in this watch", never "no labs offered", since they exist but don't match.
- **Slates use only the sections the preset shows,** so section-level picking comes free (Campus = Chilliwack keeps
  slates at Chilliwack).
- **What watching checks is said once, in one sentence, when the user ticks Watch:** "TermPoint will check that
  students can take all 5 of these courses in one term." A pool preset ("Upper level + Vancouver", 23 courses) reads
  absurdly there, so the user doesn't watch it. No "take all / choose from" setting (Q35 closed).
- **Drop-outs:** a course that leaves a tag-based preset (its last tagged section untagged) gets the before-and-after
  notice ("BIOL 310 left 300-level BIOL: no tagged sections"), so access never looks better silently.
- **No semester in a preset,** so a watch applies to whatever semester is open. Copying watches in Copy Semester and
  the "Bring them over" offer go away (most of Q6 and D21).
- **Opening a watch applies its preset to the grid** (what 2c's "hide other courses" reached for); "Show other
  courses" clears it. Closing the watch puts back the filter the user had. Detail for round 3.
- **Other departments' data:** when the bar learns to filter their courses (by calendar code), watches get them
  with no separate design.
- **Easy setup** becomes: pick courses in the Course choice (pasting codes should work), save, Watch (round 4).

*For later rounds:*
- **Shared or personal presets?** Watches must be shared, since everyone should see the same warnings. Simplest:
  all presets live in the database, shared by the department.
- **What a preset saves:** recommend the nine choices plus Tags all/any (today not saved; a preset would save it);
  not overlays or the Meetings toggle.
- **A tag or course a preset names is deleted:** the preset drops it and says so once.
- **Choices that mean little for a watch** (Instructor, Room, Meeting type): allowed; they just narrow the sections.

*Production (Phase 4):* a new stored thing (presets; readable columns to be agreed with Greg) and a lossless
translation of `ProgramWatches`. Older versions read and write `ProgramWatches`, so this is a `MinimumVersionToWrite`
**Yes**, and Phase 4 must decide between a one-time translation (older versions then open read-only) and writing
both. Rules 1–2 untouched: presets ride in the same database file and save path.

**Tags can be watched directly (D26, Greg, 2026-11-11).** Many departments declare a whole focus as one custom tag
("3rd yr CIVL") rather than Subject + Level. A single tag is already a one-choice Filter Set, so:
- **The Watch tick appears on a tag too:** in the Tags list where tags are managed, and in the Tags popup on the
  filter bar.
- **Ticking it quietly creates the one-choice preset behind it** (Tags = that tag); unticking removes it. Underneath
  there is still one kind of watch, so the analyzer, the Access panel and production are unchanged.
- **The watch's name is the tag's, read live:** renaming the tag renames the watch; deleting the tag ends the watch
  with one notice.
- **Tag-made presets do not appear in the presets list** (Greg: it might confuse). Their watch shows in the Access
  panel and on the tag only.

**Subsets (round 3 brief, Greg, 2026-11-11).** Many students don't take every course. In an open watch each course
has a tick box; unticking re-runs the whole analysis (line, slates, weight-bearing sections, seats, grid picture) on
the ticked courses, and the line says so ("3 of 5 courses"). Ticks are never saved: they reset when the watch closes,
so nothing new is stored and no one sees another user's ticks as the watch's state. To try in round 3, at risk of
over-complicating: one line answering "can a 3-course student find *some* 3?". Dropped after round 3.

*Locked items D25 reopens* (to be reworded at the start of Phase 4, not silently contradicted):
- Phase 1 **Q1** (only course-list watches analysed) → every enabled watch is analysed.
- Phase 1 **Q6** and Phase 3 **D21** (copying watches between semesters, flagging courses not offered) → gone.
- Phase 2 entity **Course set** ("the courses a course-list watch names") and **rule 2** ("only enabled course-list
  watches are analysed") → as defined above.
- Phase 3 **D18** (one box for course codes, tags under More options) → setup moves to the filter bar.

### What exists today (read from the code, 2026-11-10)
- **Main window:** left column 440 px wide: Section View on top, **Workload View** (250 px, detachable) below;
  Schedule View on the right. Management screens (Courses, Copy Semester…) are flyouts from the top menu.
- **Watches** live in a popup off the **Access** group on the grid's filter bar. Its button reads "3 watches,
  2 conflicts". The popup lists watches (name, "Courses: 3", conflict pairs in teal), each with an on/off box, edit
  and delete, then "+ New Watch": a form with a "Tag/Level-based / Course-based" choice, "Tags (AND)" and
  "Levels (OR)" lists, a ticked list of every course, and an auto-generated name.
- **Room Browser** (in the section editor): a button, then a stepper "‹ 3 of 12 ›" with a tier label and Accept;
  each solution is drawn as green semi-transparent **ghost tiles** in a light overlay that steps instantly.
- **Section editor** already has one-line warning boxes (capacity, conflict): small text, attention colour.
- **Copy Semester:** "Copy section designations and: ☐ Include any section type assignments ☐ tags ☐ staff…".
- **Courses flyout:** columns Calendar Code, Title, Tags, Capacity, Active; Add / Edit / Delete; an edit form.

### Stories (today vs. with Phase 3)

**Alice sees access without looking for it.** *Today* her watches sit behind "3 watches, 2 conflicts" on the filter
bar; she has never opened it. *With the feature* the bottom-left panel has two tabs, **Workload** and **Access**.
Access shows one line per watch, in view while she works:
```
 Access — Fall 2027                    [+ Add a watch] [Check all courses]
 ● Enviro Y2       12 slates · up to 40 students · depends on PSYCH 101 A
 ▲ First-year Sci  No full slate · best partial covers 3 of 4 · CHEM 101 can't fit
 ○ Bio Y1          32 slates
```
The filter bar's Access group stays as a short summary that brings the tab forward: "3 watches · 1 needs a look".

**Alice moves PSYCH 101 A.** In the editor she sets Tue 10:00. One line appears under the meeting rows: "Affects
access for Enviro Y2. See the Access panel." She clicks it; the Access tab comes forward with Enviro Y2 open and,
above its saved health line, "With your unsaved change: no full slate. PSYCH 101 A would overlap CHEM 213 A, the only
CHEM 213 section." She picks another day. Nothing else in the editor changed.

**Bob checks Biomed Y2's guesses.** He clicks Biomed Y2. The panel shows its detail and the grid takes on the watch's
picture. The detail says "BMEG 230: Lecture + Lab + Tutorial (guessed)", and so on. He clicks **These are right**;
the "(guessed)" marks go.

**Dana pages slates for a part-time student.** She opens Enviro Y2, unticks CHEM 213 and PSYCH 101, right-clicks
BIOL 201 B on the grid and picks **Pin in slates**, then clicks **Step through slates**: "Slate 1 of 4", drawn in the
Room Browser's ghost style; she pages with ‹ ›. Closing the watch clears it all. Nothing was saved, and it worked on
a read-only copy.

**Carla copies Fall into next Fall.** Copy Semester now has "☑ Include access watches". After the copy one message
names the watches with courses that aren't offered; the panel keeps flagging them until she deals with them.

**Bob checks all courses.** He clicks **Check all courses**: "BMEG 230: lab L1A clashes with the only lecture, so no
student can take it." He clicks the line and the grid shows BMEG 230's picture.

### 1. The Access panel (D17)

**Where:** a second tab, **Access**, beside **Workload** in the bottom-left panel, detachable like Workload. That
slot is always on screen, so the health lines are seen without a click, which the popup never achieved. The panel can
be dragged taller, or detached for a large detail view. The filter-bar popup goes away (Q29).

**Filter-bar summary** (replaces "3 watches, 2 conflicts"): "No watches" · "3 watches" · "3 watches · 1 needs a
look" (attention colour) · "3 watches · 2 notices". Clicking brings the Access tab forward.

**The list (top level of the tab):**
```
 Access — Fall 2027                    [+ Add a watch] [Check all courses]
 ┌ Notices ───────────────────────────────────────────────────────────────┐
 │ Enviro Y2: students can no longer take all 5 courses. PSYCH 101 A now    │
 │ overlaps CHEM 213 A, the only CHEM 213 section. Move one of them, or     │
 │ open Enviro Y2 to see what's left.                        [Open] [✕]     │
 └────────────────────────────────────────────────────────────────────────┘
 ▲ Enviro Y2       No full slate · best partial covers 4 of 5 · CHEM 213 can't fit
 ● First-year Sci  12 slates · up to 40 students · depends on PSYCH 101 A
 ○ Bio Y1          32 slates · 4 of 5 courses offered
   300-level BIOL  Tags and levels: overlaps boxed on the grid
   Old Y2          Off                                              (greyed)
                        Set section capacities to see how many students fit.
```
- **One line per watch**, ▲ first, then by name: mark, name, health line (trimmed; full line on hover). On/off,
  edit and delete move into the detail.
- **Marks (Q30):** ▲ attention colour: no full slate, a course not offered, a part missing, or sections not counted.
  ● a section is essential by timing. ○ nothing to report. Colours from AppColors.
- **Health line** (Phase 1/2 rules; segments dropped when they don't apply):
  - "12 slates · up to 40 students · depends on PSYCH 101 A" (essential by timing only, Q19; two named, then
    "+2 more"). At the cap: "More than 10,000 slates".
  - "No full slate · best partial covers 3 of 4 · CHEM 101 can't fit" (tie: "CHEM 101 or MATH 111 can't fit").
  - "· 4 of 5 courses offered" when a course isn't offered or has a missing part. "· 2 sections without times" (Q3).
  - Seats only when every usable section has a capacity (Q20); otherwise one quiet footer hint for the whole panel.
  - Tag/level watch: "Tags and levels: overlaps boxed on the grid"; hover "Slates are counted only for watches that
    list courses." (Q1). Disabled: "Off", greyed.
- **Notices** (rule 31, Q21): a box at the top, newest first, each ending with a next step, with **Open** (that
  watch's detail) and ✕ (dismiss for this session). Restored notices are plain, not attention-coloured: "Bio Y1:
  students can take all 5 courses again." A notice that stops being true goes by itself. No popup or toast (Q31).
- **Empty state:** "No access watches for Fall 2027. An access watch lists courses students take together in one
  term; TermPoint then shows whether they still fit. [+ Add a watch]". If the same-named semester of the previous
  academic year had watches, also: "Fall 2026 had 3 access watches. [Bring them over]" (D21).
- **Read-only:** list, detail, picture, stepping and Check all courses work. Add, Edit, Delete, on/off, These are
  right, Edit parts and Bring them over are disabled with the usual read-only hover (Q15).

### 2. Adding and editing a watch (D18)

Opens inside the tab in place of the list ("‹ Back" returns). No mode choice.
```
 ‹ Back          Add an access watch
 Courses
 ┌─────────────────────────────────────────────────────────────┐
 │ [BIOL 201 ✕] [CHEM 213 ✕] [GEOG 250 ✕] [PSYCH 101 ✕] gene_  │
 └─────────────────────────────────────────────────────────────┘
   BIOL 202  Genetics                     ← autocomplete while typing
 Type or paste course codes, e.g. "BIOL 201, CHEM 213".
 Not found: GEOG 999.                     (only when needed)
 GEOG 250 isn't offered in Fall 2027.     (only when needed; it stays in the list)

 Name  [BIOL 201, CHEM 213 + 2 more   ]   (filled in until you type one)
 With these courses: 12 slates · up to 40 students        (live preview)
 ▸ More options
 [Save]  [Cancel]
```
- **One box** for typing and pasting. Pasting splits on commas, semicolons, new lines and tabs, accepts "BIOL201" or
  "biol 201", matches calendar codes, and makes each match a chip; leftovers are listed once under "Not found:".
  Duplicates are dropped silently.
- **Autocomplete** matches code and title; Enter adds the top match. Inactive courses aren't suggested; a pasted one
  is added with "(inactive)" on its chip.
- **Name** follows the courses until the user types one. Editing keeps the existing name.
- **Live preview:** the health line for the courses in the box, on the saved semester.
- **More options ▸:** "Choose sections by tags and levels instead", today's lists relabelled "Sections with **all**
  of these tags" and "at **any** of these levels"; choosing it replaces the course box and says the watch won't get a
  slate count (Q1). Existing tag/level watches open with it expanded.
- **Save** needs at least one course (or tag/level). "Tag mode", "AND", "OR" and "program" appear nowhere.

### 3. A watch's detail (D19)

Clicking a line opens the watch inside the tab and turns on its **grid picture** (section 4). "‹ All watches" or
Escape closes both: what's on the grid always matches what's open in the panel.
```
 ‹ All watches    Enviro Y2                         [Edit] [☑ On] [Delete]
 No full slate · best partial covers 4 of 5 · CHEM 213 can't fit
 With your unsaved change: …                    (only while the editor has one)

 ⚠ GEOG 250 isn't offered in Fall 2027. Remove it from the watch, or leave it
   if a section is still to be added.                     [Remove from watch]
 ⚠ BMEG 230 has no lab sections in Fall 2027. Add one, or edit BMEG 230's
   parts if it no longer has a lab.                       [Edit parts]
 ⚠ BMEG 210 ab1 has no section type, so it isn't counted. [Edit parts]

 Courses
 ☑ BIOL 201   Lecture + Lab (guessed)          [Edit parts]
 ☑ CHEM 213   One section                       only section: CHEM 213 A
 ☑ PSYCH 101  One section
 ☐ STAT 104   One section                       (unticked: faded on the grid)
 ☐ GEOG 250   not offered
 [These are right]  saves the 1 guessed course as shown

 Pinned: [BIOL 201 B ✕]       right-click a section on the grid to pin it
 Slates with these choices: 4      [Step through slates]

 Seats: up to 40 students. PSYCH 101 A's seats are also counted by First-year Science.
 Assumes 2 sections without times fit anywhere: STAT 104 D, STAT 104 E.
 Slates keep in-person sections at one campus.   (only when sections span campuses)
```
- **Order:** health line; what-if line; flags (rule 38, not offered, missing part, not counted); courses; browsing
  (ticks, pins, stepping); the quieter facts (seats, shared seats, no times, campus).
- **Parts per course** (rule 37): "Lecture + Lab (guessed)" with "(guessed)" in the faint colour; "One section" for
  a confirmed empty list or a course with nothing guessed. **Edit parts** opens that course's parts editor (section 6)
  and returns here.
- **These are right** shows only while some course in the watch is guessed; it stores each guessed course's parts as
  shown, through the ordinary save. Hover: "Saves these parts to the courses. Other watches with these courses use
  them too."
- **Ticks** choose the subset (D3, rule 17); all ticked on opening. The health line never changes with ticks or pins
  (D12); the "Slates with these choices" line does.
- **Pins** are chips; ✕ removes. Two clashing pins: "BIOL 201 B and CHEM 213 A clash, so no slate has both." A pin
  in an unticked course shows "(course not ticked)" and does nothing.
- Only-sections are named here, never in the health line (Q19).

### 4. The grid picture and stepping (D20)

**Picture** (on while a detail is open), for the ticked courses and pins:
- Sections outside the ticked courses fade, as the grid already fades non-matching sections.
- **Usable:** drawn normally.
- **Essential by timing:** a heavier outline in the access colour; hover "Every slate for Enviro Y2 uses PSYCH
  101 A. If it moves badly, students may be cut off." Only-sections get no mark.
- **Stranded:** dimmed; hover "Can't be part of any slate for Enviro Y2: it clashes with CHEM 213 A, the only
  CHEM 213 section." (First reason, then "and N other reasons".)
- A one-line legend on the grid while the picture is on: "Enviro Y2: ▣ every slate uses it · ▢ can be used ·
  ░ can't be used · [✕]". The ✕ closes the detail too.

**Pinning:** while a detail is open, right-clicking a section tile (or one entry of a co-scheduled tile) adds **Pin
in slates** / **Unpin** to the existing menu. Otherwise the item isn't there.

**Stepping:** **Step through slates** swaps the picture for one slate at a time:
```
 ‹  Slate 1 of 4  ›                                  [Back to overview]
 BIOL 201 B, BIOL 201 L2, GEOG 250 A, STAT 104 C
```
- The stepper sits in the panel under the courses, laid out like the Room Browser's.
- The slate's sections get the **Room Browser's ghost look** over their tiles; other ticked-course sections fade.
  Steps are instant (ghost overlay, no grid reload).
- Order is stable: course order in the watch, then section code (Q34). Over the cap: "Slate 1 of more than 10,000".
- No slate: "No slate with these choices · CHEM 101 can't fit with the rest"; the stepper hides.
- A save keeps ticks and pins and goes back to slate 1.

**Teal boxes (rule 33, Q16, Q22).** Drawn only for costly clashes, for every enabled course-list watch, whether or
not a detail is open. Tag/level watches keep today's boxes. No box for harmless overlaps (D24). Hover:
- "PSYCH 101 A clashes with CHEM 213 A. Because of this clash Enviro Y2 has no full slate."
- "PSYCH 101 B clashes with CHEM 213 A, so PSYCH 101 B can't be part of any slate for Enviro Y2."
- "CHEM 213 A clashes with PSYCH 101 A and GEOG 250 A; fixing only one of these isn't enough." (Q22)
- One line per watch when several are involved.
- **What's new:** "Access watches now box only overlaps that cost students something: a section nobody in the watch
  can use, or no way to take every course. Other overlaps are no longer boxed."

### 5. The editor pointer (D6, D22)
- One line in the editor's existing warning style, **under the meeting rows**, only when the unsaved change would
  make a notice (rule 31's kinds) for an analysed watch. A smaller drop in slates or seats says nothing.
- "Affects access for Enviro Y2. See the Access panel." · "Affects access for Enviro Y2 and Bio Y1." · "Affects
  access for Enviro Y2 and 2 other watches." · good news: "Restores access for Enviro Y2."
- Clicking it brings the Access tab forward with that watch open and its what-if line showing.
- Save or Cancel removes it. It never blocks saving.

### 6. Courses flyout: parts (D23)
- New column **Parts** after Title: "Lecture, Lab"; guessed "Lecture, Lab (guessed)" in the faint colour; confirmed
  empty "One section". Nothing confirmed and nothing guessed shows blank (UFV's usual case), so the column is quiet
  where parts don't apply. Header hover: "The kinds of section a student takes one of each of, such as a lecture and
  a lab. Guessed from section types until you confirm them."
- **Edit Course** gains one group below Level:
```
 Parts (students take one section of each)
 ☑ Lecture  ☑ Laboratory  ☐ Discussion  ☐ Seminar  …     (every section type)
 Guessed from this course's sections. Applying confirms them.
 No boxes ticked: students take one section, of any type.
 Section types that only say how a section is taught (F2F, Online) aren't parts.
```
- Ticks start from the confirmed list or the guess. Apply stores the list, even if unchanged; Cancel stores nothing
  (D13). No way back to "guessed" in the first version (Q33).

### 7. Copy Semester (D21; Q32)
- New box under "Copy section designations and:": **☑ Include access watches**, ticked by default. Copied in the
  same transaction as the sections; a watch whose name already exists in the target isn't copied again.
- **Rule 36:** when "Include any section type assignments" is unticked and some copied courses have two or more
  parts, a line shows under it before Copy: "Section types are still copied for 12 courses with more than one part
  (such as Lecture and Lab), so access watches can count their sections."
- **After the copy**, only if a copied watch has courses with no sections in the target, one message:
  > Copied 3 access watches. Some of their courses have no sections in Fall 2027:
  > Enviro Y2: GEOG 250 · Bio Y1: BIO 111LAB.
  > Nothing is lost. In the Access panel, remove these courses from the watches, or leave them if sections are still
  > to be added. [OK]
- **Bring them over** (empty panel only) copies the previous year's same-named semester's watches, with the same
  message. Because it's never pushed, there's no offer to decline and nothing to remember (settles Q23's leftover).

### 8. Delivery type (only where the analyzer touches it)
- The analyzer never shows or uses it (rule 11). The parts editor lists section types only, with the "how a section
  is taught" hint. The "isn't counted" flag names section type only. The rest is delivery type's own small spec.

### 9. Check all courses (D16, rules 39–41)
Opens inside the tab like a detail:
```
 ‹ All watches    Check all courses — Winter Term 1 2026-27     [Check again]
 2 courses can't be taken as scheduled:
 ▲ BMEG 230: lab L1A clashes with the only lecture, so no student can take it.
 ▲ MECH 463: no lab fits with either lecture.
 Missing parts (guessed from earlier terms):
   BMEG 310 has no tutorial sections this term.            [Edit parts]
 Checked 228 courses on their own. Lectures and labs set up as separate
 courses are checked together only by an access watch.
```
- Clicking a course line shows that course's picture on the grid, as a one-course detail would.
- Results are a snapshot: after a save they show "Out of date · [Check again]" rather than rerunning.
- Nothing found: "No problems in 228 courses." No multi-part courses: "No course this term has more than one part,
  so there's nothing to check. Lectures and labs set up as separate courses are checked together by an access watch."

### Production and data safety (Phase 3)
- The UX stores nothing beyond Phases 1–2 (copied watches, course parts, delivery type). Ticks, pins, stepping,
  notices and Check-all results are memory only; which tab is in front is a local app setting, not database data.
- Every write (watch add/edit/delete/on-off, These are right, Edit parts, Bring them over, Copy) is an ordinary edit
  through the existing save: the four shared-database rules are untouched. Rule 4 is the one in play: the Copy
  message and every flag say what happened, that nothing is lost, and what to do next.
- Watch edits rebuild the watch and course edits rebuild the course: both must call `WithExtensionDataFrom`.
- Per-tile marks (essential, stranded, pins) must not use class-conditioned styles (CLAUDE.md gotcha; Phase 5).

### Decisions proposed (Phase 3, awaiting Greg)

| # | Decision | Rationale |
|---|---|---|
| D17 | **AGREED by Greg 2026-11-10: the Access panel is a tab beside Workload in the bottom-left panel. Each tab detaches on its own,** so Workload and Access can be open at once (Access in its own window, Workload left in the slot, or both out). The filter-bar Access group becomes a summary that brings it forward (Q29). The rest of Phase 3 is to be worked out one screen at a time with a clickable HTML wireframe on mock data, not signed off as a batch | Always on screen, so health is seen without a click; the popup is where users never look (Phase 1). Detaching reuses today's per-panel mechanism (`DetachSlot`); tabs are new |
| D18 | **One box for typing and pasting course codes,** chips, autocomplete on code and title, name filled in, live health preview, tags/levels under More options | Phase 1 Q7; a department without tags never sees them |
| D19 | **Opening a watch's detail turns on its grid picture; closing one closes both** | What's on the grid always matches what's open; no stray colouring left behind |
| D20 | **Picture first, then "Step through slates" with the Room Browser's stepper and ghost look;** pins via the grid's right-click menu, only while a detail is open | Phase 1 Q11/D7; reuses a look users know; nothing new on section cards |
| D21 | **Copy Semester copies watches (ticked by default); "Bring them over" lives only in the empty panel** | Q6; an offer that is never pushed needs no "declined" memory and no stored data |
| D22 | **The editor pointer appears only for changes that would make a notice;** clicking it opens the watch with its what-if line | D6; speak when it matters |
| D23 | **Parts appear as a Courses column and an Edit Course group of section-type boxes;** Apply confirms | Rule 37; quiet where parts don't apply |
| D24 | **No box for harmless overlaps** | A faint box on many tiles is clutter; the hover on real boxes says what they cost |
| D25 | **AGREED by Greg 2026-11-10: an access watch is a filter preset you choose to watch.** Users name and save Filter Sets (combinations of filter-bar choices) as presets near the filter bar; ticking Watch on one makes it an access watch. Replaces separate course-list and tag watches; supersedes Q35 and Q36 | One thing to learn, which users already make. Both existing watch kinds are already Filter Sets. Settles the pool problem (watching is the user's statement), section-level picking, semester carry-over and outside departments' courses in one move. Details under "An access watch is a filter preset you choose to watch" |
| D26 | **AGREED by Greg 2026-11-11: a custom tag can be watched directly,** by the same Watch tick, which quietly creates a one-choice preset behind it; named live from the tag; not listed among presets | Departments that declare their focus as one tag never have to meet presets; still one kind of watch underneath |

### Open questions (Phase 3)

| Q | Question | Recommendation |
|---|---|---|
| Q29 | **Where the panel lives.** Tab beside Workload (bottom-left, 440 × 250 by default) vs. its own panel right of the grid vs. a bigger popup | Tab beside Workload (D17). Phase 5 risk: `DetachablePanel` may not do tabs today; fallback is a third panel stacked in the left column with a splitter. Is that slot big enough in your daily use? |
| Q30 | **Marks.** ▲ / ● / ○; ● (essential by timing) may be too loud for "fine, but fragile" | ▲ attention, ● access colour, ○ faint. Or drop ● and let "depends on" in the line say it |
| Q31 | **Should a new notice do more than change the summary and the tab?** (e.g. an app notification, mainly for other users' saves arriving on reload) | No: the pointer already warned before one's own save, and interrupting for others' saves is what D4 avoids. Revisit if testers miss notices |
| Q32 | **Rule 36 in Copy Semester.** Copy section types anyway for multi-part courses (line shown before Copy), warn and let the user choose, or tick-and-lock the box | Copy anyway with the line: unticking section types means "don't carry these labels", not "break access" |
| Q33 | **Un-confirming parts.** Can a confirmed course go back to guessed? | Not in the first version; editing the boxes covers real cases |
| Q35 | **Reopens Q1 (Greg, 2026-11-10): analyse tag/level watches too?** Tags pick sections and the courses follow, but a pool ("300-level BIOL") would read "No full slate" forever, and untagging a course's last section drops it silently | **Superseded by D25 (2026-11-10).** Every watched preset is analysed; watching is the user's statement that students take all its courses, said in one sentence when Watch is ticked; drop-outs get a before-and-after notice |
| Q36 | **One concept, not two? (Greg, 2026-11-10).** Is the split between course-list and tag watches helping? First proposal: an access watch is a tag you choose to watch (course tags as course lists) | **Superseded by D25 (2026-11-10):** a watch is a filter preset you choose to watch. Keeps the tag idea's gains (one idea, no semester copying, section-level picking) without its costs (outside courses can't carry this database's tags; on/off living on a tag) |
| Q34 | **Stepping order.** Stable (course, then section code) or by usefulness (fewest days, earliest finish)? | Stable first; preference ordering belongs with Q14 |
