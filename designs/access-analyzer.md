# Access Analyzer and Easy Programs — Progressive Design Spec

## Status
Current phase: **1 — Problem Statement** (first draft 2026-10-30; D1 decided; Q1–Q12 open; not yet locked by Greg)
Last session: 2026-10-30
Next step: Greg settles the open questions, then locks Phase 1; Phase 2 (Domain Model) follows

---

## Phase 1: Problem Statement

### The problem
TermPoint measures a timetable's quality mainly by **student access**: can the students of a program actually take
every course their program asks of them in a term, and how many of them can? Two things stand in the way today.

**1. Access is hard to judge.** Program watches draw a teal box around any two overlapping sections from different
courses. That says "these overlap". It does not say whether it matters: whether students still have a way through,
how many of them, or which single section everything hangs on. Timetables are in flux; sections move for instructors,
rooms and emergencies, and each move can quietly cut a program off. Nobody finds out until registration.

**2. Programs cost too much to set up, so they go unused.** Users want to get in, get the job done and get out. Some
departments don't use custom tags at all. Today a watch:
- lives in a popup off the grid's Access group, where many users will never look;
- opens on **tag mode**, with lists labelled "Tags (AND)" and "Levels (OR)", which is empty for a department without tags;
- in course mode, means ticking boxes in a short scrolling list of every course, with no search;
- belongs to **one semester**: Copy Semester copies sections only, so every watch is rebuilt by hand every term.

Every feature that serves student access (the conflict boxes, this analyzer, the Slot Browser's program check)
depends on programs existing. If setting them up is a chore, none of those features gets used, and timetables are
worse for students. **So this spec treats easy programs as half of the feature, not a side issue.**

### Stories

**Today.** Alice copies last fall's semester into Fall 2027. Last year someone built an "Enviro Y2" watch (BIOL 201,
CHEM 213, GEOG 250, PSYCH 101, STAT 104); it didn't come along, and Alice doesn't know it ever existed. In August,
PSYCH 101 A's instructor changes and Alice moves it to Tue 10:00. It now overlaps CHEM 213 A, the only CHEM 213
section. No box appears. Enviro Y2 students can no longer take all five courses, and the first anyone hears of it is
a student at registration.

**With the feature.**
- Copying the semester brings Enviro Y2 along.
- Alice changes PSYCH 101 A to Tue 10:00 in the editor. Before she saves, the editor says: "Enviro Y2: students
  could take all 5 courses. With this change they can't: it overlaps CHEM 213 A." Alice tries the finder instead
  and picks Thu 10:00.
- Had she saved anyway, one line would say: "Enviro Y2 students can no longer take all 5 courses. PSYCH 101 A now
  overlaps CHEM 213 A, the only CHEM 213 section. Move one of them, or open Enviro Y2 to see what's left."

**How much access is there?** Before finalising the timetable, Alice opens Access. Each program has one line:
"Enviro Y2: students can take all 5 · up to 40 students · depends on PSYCH 101 A". Clicking it colours the grid:
PSYCH 101 A stands out as the section everything depends on, and GEOG 250 C is dimmed because it can't be combined
with the rest, so Enviro students can never use it. Alice can step through the possible timetables one at a time, or
pin BIOL 201 B to see only the timetables that use it.

**The seats trap.** A program of BIOL 201 + CHEM 213. BIOL 201 A has 10 seats and B has 100; CHEM 213 A has 10 and
B has 100; both B sections meet MWF 10:00. On paper each course has 110 seats. In truth everyone in BIOL B must take
CHEM A, and everyone in CHEM B must take BIOL A: **20 students** can take both courses, not 110.

**A department with no programs.** Bob schedules a department that has never set one up.
- *By hand:* Bob opens Access, clicks "Add a program", and pastes "BIOL 201, CHEM 213, GEOG 250, PSYCH 101, STAT 104"
  from the academic calendar. Done. The health line appears at once.
- *Asked by the app:* Bob moves BIOL 301 A (its only section) on top of BIOL 310 A (also its only section, same
  level). After the save, the app asks once: "BIOL 301 and BIOL 310 each have one section, and now they overlap. Do
  students take them in the same term?" Yes makes a program of the two. No is remembered and never asked again.

### Terms
| Term | Meaning |
|---|---|
| **Program** | A set of courses that a group of students takes together in one term. The user-facing name for what the code calls a program watch (Q10) |
| **Timetable** (spec: *slate*) | One section of every course in the program, none of them overlapping. Two timetables differ if they differ in at least one section |
| **Essential section** | In every timetable. If it moves badly, the program may be cut off |
| **Usable section** | In at least one timetable |
| **Stranded section** | In no timetable. The program's students can never use it, so its seats don't count for them |
| **Seats** | The most students who could each hold a full timetable at once, within the sections' capacities |

### What "done" looks like

**A. Programs are easy**
1. A program set up once carries forward when a semester is copied (Q6).
2. A program is created by typing or pasting course codes. No mode choice up front; tags and levels are an advanced
   option (Q7).
3. A program can be created from where the user already is: sections selected on the grid, or the section editor (Q8).
4. When an edit creates a likely clash between courses no program covers, the app asks one yes/no question, narrowly,
   and remembers the answer (Q9).
5. Users see "programs" and "timetables", never "watch", "slate", "tag mode" or "AND/OR" (Q10).

**B. Access is visible**
1. Each program shows one line: can students take every course; up to how many; which section(s) it depends on.
2. Saving an edit that makes a program's access worse says so in one line: what happened and what to do (Q5).
3. Before saving, the section editor shows what the unsaved change would do to each program the section serves.
4. One grid picture shows essential, usable and stranded sections; the user can step through timetables and pin a
   section (Q11).
5. The Slot Browser ranks placements with the same engine (Q12).
6. Nothing to set up beyond listing the courses.

### How the analysis works (proposed)
- **The graph.** The sections of the program's courses are the points. Two sections are joined when they belong to
  *different* courses and don't overlap. (Same-course sections are never joined, or a timetable could take two PSYCH
  101 sections.) A timetable is then one section from every course, all joined to each other: an n-clique in an
  n-partite graph.
- **The search is small.** Because every timetable takes exactly one section per course, no general clique algorithm
  is needed: pick one section per course, starting with the course that has the fewest sections, and drop any
  candidate that clashes with what's already picked. Five courses of four sections each is at most 1,024 combinations.
  The count is capped ("more than 10,000").
- **Essential, usable and stranded don't need the full count.** A section is usable if one timetable with it exists
  (search with it fixed; stop at the first). It is essential if no timetable exists without it. Each is a short search.
- **Seats.** Two courses: an exact max-flow. Three or more: a packing problem, hard in general but small here; a
  linear program over the timetables gives a close upper bound, enough given that capacities are estimates. Never the
  smallest course total, which overstates (the seats trap above). Phase 2/5 checks the cost for large programs.
- **One clash test, one coverage rule.** "Do these two sections overlap" is the conflict detector's existing test
  (same day, half-open times, odd/even weeks). Which sections a program covers is the detector's rule. Both live in one
  place that the detector, the analyzer and the Slot Browser share.
- **Whole semester.** The analysis ignores grid filters. A filter must never change how much access a program has.
- **When it runs.** On section save and semester change (filters don't affect it, so not on every grid redraw), and
  on the unsaved section while the editor is open (B3).

### Design principles
- **One noun to learn.** A program is the courses students take together. Everything else follows from that.
- **Nothing set up twice.** What the user tells the app stays told, across semesters.
- **Speak when it matters, otherwise stay quiet.** The app speaks only when an edit changes access, or when it
  needs one answer it can't work out.
- **Exact rather than misleading.** Whole timetables, not pairs of sections; real seats, not course totals.
- **Honest about limits.** Seats are shared with other students; courses scheduled elsewhere are invisible; sections
  without times are assumed to fit. Each limit is said in plain words where it affects a number.
- **Informational, not prescriptive.** Shows access; the user decides.

### Relationship to existing and planned features
- **Program conflict detector (teal boxes).** Stays as it is: it says "these overlap". The analyzer says whether that
  costs the program anything. Phase 3 may revisit how the two look together.
- **Slot Browser** (`designs/slot-browser.md`). D2 (programs come from watches) is unchanged and gains most from easy
  programs. **D3's level 2 is a pairwise shortcut that can be wrong.** Story: a program of STAT 104, GEOG 250 (A, B)
  and CHEM 213 (A only), where GEOG 250 B clashes with CHEM 213 A. The finder offers STAT 104 a time that overlaps
  GEOG 250 A and calls it level 2 because 250 B is free. But no student can take 250 B with CHEM 213, so nobody can
  take all three. Judged by timetables, that placement is level 3. See Q12.

### Not in this feature (proposed)
- Auto-scheduling or optimising a timetable for access.
- Enrolment data, demand forecasting, or waitlists.
- Electives: programs where students choose some of the listed courses ("any two of these five").
- A student's year across terms (Fall and Winter together).
- Courses scheduled by other departments, which aren't in the database (possible later, through shared schedules).

### Production and data safety
- **The analyzer itself** only reads sections and programs and computes. No new persisted data, no save-path change:
  the four shared-database rules are untouched and the `MinimumVersionToWrite` answer is No.
- **Copying programs with a semester (Q6)** adds rows to an existing table inside the existing copy, with no new
  field, and older versions read, edit and delete those rows correctly. Courses and tags are shared across semesters,
  so a copied program's course and tag IDs stay valid. The copy should write programs in the same transaction as the
  sections, so a failed copy leaves neither. **For Greg:** the `MinimumVersionToWrite` question taken literally ("could
  the new version save anything the old version wouldn't have?") says Yes, since copying now saves extra rows; the
  intent (nothing an older version would damage) suggests No.
- **Paste-a-list and create-from-the-grid (Q7, Q8)** are new ways to fill in the existing model: No.
- **Anything newly stored is a Yes:** an expected intake per program (Q4), a "students take all of these" mark (Q1),
  remembered "No" answers (Q9), a link from reserves to programs (Q4). `ProgramWatch` already derives from
  `PersistedRecord`; any future code that rebuilds a program (an edit form) must call `WithExtensionDataFrom`. A new
  table for remembered answers needs Greg's readable columns (Phase 4).
- **No new save path** anywhere: every write goes through the existing save.

### Decisions made

| # | Decision | Rationale |
|---|---|---|
| D1 | **One combined spec (2026-10-30).** Easy programs and the Access Analyzer are designed together | Every access feature depends on programs existing; an analyzer nobody sets up produces nothing |

### Open questions

| Q | Question | Recommendation |
|---|---|---|
| Q1 | **Which programs does the analyzer judge?** "Students take every course" fits a course list for one cohort's term. It doesn't fit a tag-mode watch like "all 300-level BIOL". Worse, in tag mode the course list is implicit: untag the only tagged section of a course and the course drops out, so access looks *better* | Course lists only. Tag-mode watches keep the teal boxes but get no health line, with a quiet note saying why |
| Q2 | **Lectures and labs.** If a course has lecture and lab sections, a timetable needs one of each: one section per course *and section type*, not per course. How is this modelled in real data: sections of the same course with a section type, separate courses, or meetings inside one section? Are particular labs tied to particular lectures? | Needs Greg's knowledge of the data. If labs are sections with a section type, group by (course, section type); tied labs are a later step |
| Q3 | **Sections with no meeting times** overlap nothing, so they fit every timetable. Right for asynchronous online, misleading for "not scheduled yet" | Count them as fitting, and say so in the health line ("assumes 2 sections without times fit anywhere") |
| Q4 | **Seats.** Show them? Capacity is shared with other students (40 seats in PSYCH 101 A aren't 40 Enviro seats). Some sections have no capacity set. A number means more against an expected intake ("we expect 30"), and reserved seats could give a guaranteed number | Show "up to N students", worded as a ceiling. If a usable section has no capacity, say "seats unknown" and name it. Expected intake and the reserves link wait for a later release (both are new stored data) |
| Q5 | **What triggers a notice on save, and where?** | Only when a program gets worse in a way that matters: no timetable left; seats fall below expected intake (once that exists); a section becomes newly essential. Also one line when a program that had none gets a timetable back. One non-blocking line naming the sections and a next step |
| Q6 | **Carry programs forward.** Copy Semester copies programs. A semester with none, whose same-named semester last year had some, gets a one-time offer to bring them over | Yes, both. The offer covers departments that already copied their semesters before this release |
| Q7 | **Creating a program.** One box: type or paste course codes, with autocomplete; name filled in automatically; tags and levels under "more options". Codes the department doesn't schedule get a plain note | Yes. Course list becomes the default mode |
| Q8 | **Create from where the user is.** Select sections on the grid, or right-click one, then "Students take these together". The section editor shows which programs its course belongs to | Yes for both. The Slot Browser's "no programs" line (its D2 consequence) becomes the same invitation |
| Q9 | **The app asks.** When an edit makes two single-section courses of the same subject and level overlap, and no program covers both, ask once. "No" is remembered across semesters | Yes, kept that narrow: a noisy prompt gets ignored, then resented. Needs new stored data, so it could follow in a later release |
| Q10 | **Names.** "Program" for a watch and "timetable" for a slate, in every user-facing place | Yes. The Slot Browser spec already says "Program X" in its labels |
| Q11 | **Viewing timetables.** One grid picture (essential / usable / stranded) first, then stepping through timetables with the Room Browser's ghost tiles and stepper, with "pin a section" to narrow | Yes; details in Phase 3 |
| Q12 | **Slot Browser D3.** Judge program levels by whole timetables (level 1: no overlap; level 2: overlaps, but every program still has a timetable; level 3: some program loses its last timetable), using this engine | Yes, one shared engine. Settle it in the Slot Browser spec, whose Phase 1 is still open |

### Delivery order (proposed)
1. **Easy programs** (Q6, Q7, Q8, Q10). Small, improves the feature users already have, ships alone.
2. **The analyzer** (health line, notice on save, editor preview, grid picture).
3. **The Slot Browser** uses the engine for D3.
4. Later: the app asks (Q9), expected intake and reserves (Q4).
