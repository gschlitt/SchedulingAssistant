# Slot Browser (working name) — Progressive Design Spec

## Status
Current phase: **1 — Problem Statement** (reopened 2026-10-30 for one-move suggestions; all questions settled, D1–D8;
Q5–Q6 deferred to Phase 3; not yet locked by Greg)
Last session: 2026-10-30
Next step: Greg locks Phase 1, then Phase 2 (Domain Model), starting with the points under "Consequences" below

---

## Phase 1: Problem Statement

### The problem
Timetables are rarely built from a blank semester. Each one starts as last year's and is adjusted at the edges:
at timetabling time (better student access, new demand, a new program), and later, during the semester, under
pressure (a sudden extra section, an instructor who is no longer available).

Each adjustment is the same puzzle: *where can this one section go?* The answer depends on several things at once:
the instructor's existing schedule, the other courses its students take, the rooms it needs, the institution's start
times. Grid filters help by hiding what's irrelevant, but the user still holds every constraint in their head and
cross-checks one filtered view against another.

The Room Browser solves one slice of this (where is a room free?) and ignores the rest. Today it will propose a time
when the section's instructor is already teaching, or one that clashes with a course its students need. It also
skips remote meetings entirely, though an online section still has an instructor and students.

### Stories

**Today.** Alice needs a second section of PSYCH 301: one 3-hour meeting a week, taught by Prof. Z. PSYCH 301 serves
Program X. Alice filters the grid to Prof. Z and notes the free afternoons, then filters to Program X and crosses off
the afternoons that clash, then opens the Room Browser, which offers Tuesday 13:00 in a free room. Prof. Z teaches
then. Back to the notes. Twenty minutes, and Alice still isn't sure nothing was missed.

**With the feature.** Alice adds the PSYCH 301 section, sets Prof. Z, adds one 3-hour meeting row, and clicks the
finder button. The grid shows the first fit as a ghost tile: "Wed 13:00–16:00, fits everything." Next: "Thu
09:00–12:00, fits everything." Alice picks Wednesday and clicks Accept. The meeting row fills in, and Alice saves as
usual.

**Emergency move.** Mid-semester, PSYCH 210 B's instructor leaves. Prof. W can take it, but teaches at 210 B's current
time. Alice opens 210 B, changes the instructor to W, and clicks the finder. Because 210 B already has a room, times
where W is free *and* that room is still free come first (one change for the students instead of two). 210 B never
counts as clashing with itself.

**Nothing fits cleanly.** The finder never just says "nothing found". It shows the closest fits, each saying what it
breaks: "Thu 14:30, overlaps PSYCH 340 A (Program X). Students can still take PSYCH 340 B (Mon 10:00)." Alice decides
whether that's acceptable.

### What "done" looks like
1. From a section open in the editor, new or existing, one button finds placements using what the section already
   says: meeting lengths (and any day, start or room already set), its instructor(s), the programs its course serves,
   its room needs (type, campus, capacity).
2. No separate input form. The user "enters what's known" by filling in the section as usual; blank means "don't care".
3. Placements appear on the grid as ghost tiles, as in the Room Browser. The user steps through them; Accept fills
   the meeting rows; the user saves through the normal path.
4. Each placement says, in plain words, that it fits everything or exactly what it breaks.
5. When nothing fits everything, the closest placements that meet every must are shown. When the musts themselves
   leave nothing, the finder says which one ruled everything out.
6. A section being moved never conflicts with itself.

### What a placement is checked against (proposed)

| Check | Where the data comes from | Proposed strength |
|---|---|---|
| Instructor free | The instructor's other sections, their commitments (office hours etc.), and Meetings they attend. Every assigned instructor, for team teaching | Hard |
| Start times | The academic year's legal start times, as the Room Browser does | Hard |
| User's pins | Any day, start or room already set on a meeting row | Hard |
| Program-mates clear | Sections of *other* courses in the enabled program watches that cover this section | Soft: ranked in three levels (D3) |
| Required room free | The meeting asks for a room type (lab): a room of that type, on the section's campus and big enough, must be free | Hard (D4) |
| Any room free | The meeting asks for no particular room or type: a free room is looked for and shown, but its absence only demotes the placement. Remote meetings skip rooms | Soft |
| Spread across the course | Not at the same time as other sections of the same course (two sections at one time give students no extra choice) | Soft: preference only |

- **Excluded:** the section being edited.
- **Scope:** the whole semester, regardless of grid filters. A filter must never hide a clash; filters only affect what
  is drawn.

### Design principles (carried over from the Room Browser)
- **Informational, not prescriptive.** Shows options; the user decides.
- **On the real grid.** Ghost tiles over the existing schedule, so the user sees the context.
- **Ranked, not filtered.** Clean fits first, then near misses with their reasons. Only hard checks remove a placement.
- **The section is the question.** Inputs come from the section editor's own fields (as in Room Browser phase 3b).
- **Shows its working (D3).** The finder says what it checked (for example "Prof. Z's schedule, Program X, lab
  rooms") and, for each placement, what it overlaps and whether students still have another section.

### Not in this feature (proposed)
- Auto-scheduling. (Suggesting *one* move of another section is now proposed: see "One-move suggestions".)
- Chains of moves (B moves so A fits, C moves so B fits).
- The inverse question: *who* is free to teach this at a fixed time. A natural sibling feature; noted for later.
- Student enrolment data or demand.
- Instructor preferences not already in the data ("Z avoids Fridays"), unless entered as commitments.
- Multi-semester mode (disabled, as the Room Browser is).
- Shared schedules imported from other departments as a check (possible later: "Program X students also take STAT 201").

### Production and data safety
Read-only until Accept, and Accept only fills the editor's meeting rows; nothing reaches the database until the user
saves through the existing path. If programs come from existing watches there is no new persisted data, so the four
shared-database rules are untouched and the `MinimumVersionToWrite` answer is No. Revisit if a later phase stores
anything new (for example per-course "must avoid" lists). One-move suggestions keep all of this because a move is two
ordinary edits (D8); the pending move is held in memory only.

### Decisions made

| # | Decision | Rationale |
|---|---|---|
| D1 | **One finder (Q1, 2026-10-30).** The Room Browser is extended, not joined by a second tool. Rooms become one check among several, and optional. It stays available from both the section editor and the Meeting editor (for Meetings, attendees take the instructor check; programs don't apply) | The solver, ghost tiles, stepping and Accept already exist; two tools that each ignore what the other checks would each propose placements the other rejects |
| D2 | **Programs come from program watches (Q2, 2026-10-30).** The program check uses the *enabled* watches that cover this section; its program-mates are the sections of other courses those watches cover. Course tags alone never make a program | Watches are already the user's explicit "these must not overlap". Tags also include things like "evening" or "lab" that are not programs |
| D3 | **Program clashes are ranked, not counted alike (Q3, 2026-10-30).** (1) First, placements that overlap no program-mate section. (2) Then placements that overlap some sections but leave students another section of each course (overlaps 340 A, but 340 B is free). (3) Last, placements that overlap *every* section of some program-mate course. Each placement says what it overlaps and what students can still take, and the finder says what it checked | Overlapping one of two sections costs students much less than overlapping both. Level 3 is still shown, last, so nothing is hidden (ranked, not filtered) |
| D4 | **Musts and preferences (Q4, 2026-10-30).** Musts (a placement that breaks one is never shown): the instructor is free, the start time is legal, anything already set on a meeting row is kept, and a required room type (lab) has a free room. Preferences (ranked): programs (D3), any free room when no type is required, spread across the course's sections | A placement in a room that can't hold the class, or at a time the instructor teaches, is no use to anyone. The lab was decided explicitly; the rest follows the Phase 1 recommendation and can be revisited |
| D5 | **Any section in the way may be suggested for a move (Q7, 2026-10-30).** A program-mate, the instructor's other class, or the class in the only suitable room: the same test for all, "would this placement be better without B?" | Room swaps and the instructor's own other class are probably the commonest real fixes, and a move is the natural answer when the musts leave nothing |
| D6 | **Moves appear automatically when there is no clean fit (Q8, 2026-10-30).** Otherwise they are one click away ("look for one move") | Keeps the common case short; the finder only reaches further when it has to |
| D7 | **No time window (Q9, 2026-10-30).** The finder treats every legal hour alike; "poor" means only clashes (D3). The user steps past evening slots, or narrows the search by setting a day or start time on a meeting row (already a must, D4) | Keeps the finder free of a new notion of quality and a new control |
| D8 | **A move is two guided edits (Q10, 2026-10-30).** Accept fills the edited section's rows; the user saves it as usual. The finder then offers to open B with its new placement filled in; the user checks it and saves | No new save path, and no section changes without the user opening it. A half-finished move shows on the grid as a clash |

Consequences of D1, to carry into later phases:
- **Room Browser results change in production.** With an instructor assigned, options where that instructor is busy
  disappear or move down. Behaviour only, no data risk, but it needs a What's-new line so users aren't surprised.
- **Remote meetings join the search** (time checks only; today they are skipped).
- **A room becomes optional.** The finder must work for "time only" (remote, or room decided later) as well as
  "room only" (time already set), as today.
- **Q6:** the name must cover time *and* room, so "Find a Time" alone is out.

Consequences of D2:
- **Coverage is judged on the section as it is in the editor**, unsaved: its course, its tags and its level right
  now. A watch matches it by the same rule the detector uses (tag mode: all the watch's tags, any of its levels;
  course mode: the course is listed). That rule should live in one place both use, not be copied.
- **No watches, no program check.** A department with no enabled watch covering the section gets no program check.
  The finder should say so in one quiet line rather than imply the slot is clear for students.
- **The finder sees the whole semester; the grid's teal boxes see only visible sections** (the detector runs on the
  filtered list). So the finder can report a clash the grid isn't showing because a filter hides the other section.
  Each label names that section, so the user can tell why.

Consequences of D3, to settle in Phase 2:
- **A placement is judged across all its meetings.** MWF 10:00 that hits 340 A on Monday and 340 B on Wednesday
  overlaps every section of 340, so it is level 3 even though each day alone looks like level 2.
- **Within a level, fewer is better** (proposed): one affected course before two.
- **The grid will still draw a teal box** after a level-2 placement is accepted, because the detector flags any
  overlap. The label should make that expected ("overlaps 340 A").
- **The section's own course may have other sections too.** A new PSYCH 301 B that overlaps the only 340 section
  takes nothing away if 301 A doesn't overlap it: students can still take 301 A with 340 A. Moving the *only* 301
  section is different. Decide whether "what students can still take" counts the section's own course.
- **How program levels combine with the other preferences** (any free room, spread): one ordering to settle in Phase 2.
- **Level 2 is judged pair by pair, and that can be wrong** (raised 2026-10-30 in `designs/access-analyzer.md`, Q12).
  "Students still have 250 B" ignores whether 250 B fits with the program's *other* courses. If it clashes with the
  only CHEM 213 section, nobody can take every course, so the placement is really level 3. Proposal there: judge levels
  by whole timetables, with one engine shared by the finder and the Access Analyzer. Open; D3 stands until Greg decides.

Consequences of D4:
- **The musts can leave nothing.** Then the finder must say which must did it, in plain words, with a next step:
  "Prof. Z is free at 6 times this week, but no lab is free at any of them." Never a bare "no results".
- **A room type on a meeting is now a promise the finder keeps.** It already works this way in the Room Browser, and
  it fits the 1.2.4 "meeting room type" item: the stored type is what the meeting *needs*, used only when no room is
  set (a set room is a must anyway).

### One-move suggestions (added 2026-10-30; D5–D8)

When nothing good fits, the finder looks one step further: would a placement work if **one other section moved**,
and does that section have somewhere valid to go?

**Story.** Prof. Z is free only on Tuesday afternoon and in the evenings. Tuesday 13:00 overlaps STAT 199 A, the only
STAT 199 section, so Program X students would lose one of the two courses (level 3). The best clean fit is Wednesday
18:30. The finder adds: "If STAT 199 A moves from Tue 13:00 to Thu 10:00, PSYCH 301 B fits Tue 13:00 with no clash.
At Thu 10:00, Prof. Y is free, room B210 is free, and no new program clash." Alice accepts. PSYCH 301 B's meeting row
fills in, and Alice saves. The finder then offers to open STAT 199 A with Thu 10:00 filled in; Alice checks it and
saves. If Alice stops between the two saves, the grid shows the clash, so nothing is silently half-done.

**Rules (proposed):**
- **One move only.** The moved section (B) must fit its new spot as it is. The finder never moves a third section to
  make room for B: the tree stops at depth 1.
- **B is "in the way"** when taking it out of its spot makes a placement for the edited section better: it clears a
  must, or raises the program level (D3).
- **B's new spot meets all of B's own musts** (its instructor free, legal start, its required room type free, its
  meeting lengths kept), counting the edited section in its new place. B's own program level must be no worse than
  it is now: one student problem is never fixed by creating another. B keeps its room when it can.
- **B is an existing section.** Mid-semester its students would have to change too; the finder can't know about
  enrolment, so the label says plainly that B moves, and the user judges.

Consequences of D5:
- **Only sections move.** Meetings (committees, with many attendees) and instructor commitments are never suggested
  for a move; they still count as "in the way" for the musts, so a placement they block simply isn't offered.
- **Only musts and program levels justify a move.** Preferences (any free room, spread across the course) never do:
  disturbing another section for a nicety is not worth it.
- **A move can be room-only.** If B is in the way only because it holds the one free lab, B keeping its time in
  another suitable room is the cheapest move: its students change nothing but the door. Moves are ranked by how much
  they disturb: room only, then time only (same room), then both.
- **More to search.** Every placement blocked by exactly one section now starts a second, smaller search for that
  section. Phase 2/5 should check the cost, especially in the WASM demo.

Consequences of D6:
- **"Clean fit" means: a direct placement that meets every must and is program level 1.** The other preferences
  (any free room, spread) don't count, since D5 says a move can't be justified by them anyway.

Consequences of D7:
- **"Only evening slots are clean" does not bring in moves by itself.** That case, the one that started this idea,
  relies on the "look for one move" button (D6). Moves still appear on their own when nothing is clean at any hour.
- **Order within a level** follows the Room Browser (earliest start first), so daytime placements come before
  evening ones of the same level without the finder judging them.
- **Two groups, not one merged ranking** (proposed): "fits as it is" (the direct placements, levels 2–3 when there is
  no clean fit) and "fits if one section moves". Whether costing Program X students one section beats disturbing
  STAT 199 A is the user's judgement, not a score the finder should invent. The order inside each group follows D3
  and D5.

Consequences of D8:
- **The edited section is saved first, then B.** The other order would mean opening B while the edited section is
  still unsaved in the editor, and only one section is open at a time. The offer to open B appears only after the
  save succeeds, so unsaved work is never put at risk (rule 3).
- **In between, the clash is real and visible**: a program box, an instructor clash, or a room double-booked, until B
  moves. If the user stops there, the grid's existing nudges are the reminder.
- **The pending move lives in memory only.** Nothing new is stored; closing the app forgets the offer, and the clash
  on the grid remains.
- **B's new spot is checked again when B opens.** Something may have changed between the two saves. If the spot no
  longer fits, the finder says so in plain words and searches for B afresh.

### Open questions (deferred to Phase 3, UX Sketch)

| Q | Question | Recommendation |
|---|---|---|
| Q5 | **Display.** Step through one placement at a time (the Room Browser way), or also show at a glance every place it fits? | Both, worked out in Phase 3: stepping for multi-meeting patterns; an at-a-glance view helps most for a single meeting |
| Q6 | **Name.** Replaces the "Room Browser" button | Plain words that cover time *and* room, e.g. "Find a Fit" / "Fit Finder". Decide in Phase 3 |
