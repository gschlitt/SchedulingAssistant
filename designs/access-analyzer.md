# Access Analyzer and Easy Access Watches — Progressive Design Spec

## Status
Current phase: **2 — Domain Model, LOCKED by Greg 2026-11-10** (D9–D16 decided; Q17, Q19–Q24, Q26, Q27 answered;
Q18's extra part names and Q28 left open, not blocking). Phase 1 locked 2026-11-06.
Last session: 2026-11-10
Next step: Phase 3 (UX Sketch)
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
