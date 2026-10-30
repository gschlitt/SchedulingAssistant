# Slot Browser (working name) — Progressive Design Spec

## Status
Current phase: **1 — Problem Statement** (COMPLETE: D1–D4 decided; Q5–Q6 deferred to Phase 3)
Last session: 2026-10-30
Next step: Phase 2 (Domain Model), starting with the points under "Consequences" below

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
- Auto-scheduling, or moving other sections to make room ("move B so A fits").
- The inverse question: *who* is free to teach this at a fixed time. A natural sibling feature; noted for later.
- Student enrolment data or demand.
- Instructor preferences not already in the data ("Z avoids Fridays"), unless entered as commitments.
- Multi-semester mode (disabled, as the Room Browser is).
- Shared schedules imported from other departments as a check (possible later: "Program X students also take STAT 201").

### Production and data safety
Read-only until Accept, and Accept only fills the editor's meeting rows; nothing reaches the database until the user
saves through the existing path. If programs come from existing watches there is no new persisted data, so the four
shared-database rules are untouched and the `MinimumVersionToWrite` answer is No. Revisit if a later phase stores
anything new (for example per-course "must avoid" lists).

### Decisions made

| # | Decision | Rationale |
|---|---|---|
| D1 | **One finder (Q1, 2026-10-30).** The Room Browser is extended, not joined by a second tool. Rooms become one check among several, and optional. It stays available from both the section editor and the Meeting editor (for Meetings, attendees take the instructor check; programs don't apply) | The solver, ghost tiles, stepping and Accept already exist; two tools that each ignore what the other checks would each propose placements the other rejects |
| D2 | **Programs come from program watches (Q2, 2026-10-30).** The program check uses the *enabled* watches that cover this section; its program-mates are the sections of other courses those watches cover. Course tags alone never make a program | Watches are already the user's explicit "these must not overlap". Tags also include things like "evening" or "lab" that are not programs |
| D3 | **Program clashes are ranked, not counted alike (Q3, 2026-10-30).** (1) First, placements that overlap no program-mate section. (2) Then placements that overlap some sections but leave students another section of each course (overlaps 340 A, but 340 B is free). (3) Last, placements that overlap *every* section of some program-mate course. Each placement says what it overlaps and what students can still take, and the finder says what it checked | Overlapping one of two sections costs students much less than overlapping both. Level 3 is still shown, last, so nothing is hidden (ranked, not filtered) |
| D4 | **Musts and preferences (Q4, 2026-10-30).** Musts (a placement that breaks one is never shown): the instructor is free, the start time is legal, anything already set on a meeting row is kept, and a required room type (lab) has a free room. Preferences (ranked): programs (D3), any free room when no type is required, spread across the course's sections | A placement in a room that can't hold the class, or at a time the instructor teaches, is no use to anyone. The lab was decided explicitly; the rest follows the Phase 1 recommendation and can be revisited |

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

Consequences of D4:
- **The musts can leave nothing.** Then the finder must say which must did it, in plain words, with a next step:
  "Prof. Z is free at 6 times this week, but no lab is free at any of them." Never a bare "no results".
- **A room type on a meeting is now a promise the finder keeps.** It already works this way in the Room Browser, and
  it fits the 1.2.4 "meeting room type" item: the stored type is what the meeting *needs*, used only when no room is
  set (a set room is a must anyway).

### Open questions (deferred to Phase 3, UX Sketch)

| Q | Question | Recommendation |
|---|---|---|
| Q5 | **Display.** Step through one placement at a time (the Room Browser way), or also show at a glance every place it fits? | Both, worked out in Phase 3: stepping for multi-meeting patterns; an at-a-glance view helps most for a single meeting |
| Q6 | **Name.** Replaces the "Room Browser" button | Plain words that cover time *and* room, e.g. "Find a Fit" / "Fit Finder". Decide in Phase 3 |
