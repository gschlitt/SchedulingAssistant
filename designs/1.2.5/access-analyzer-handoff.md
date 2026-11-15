# Handoff: Access Analyzer spec, Phase 3 (UX Sketch)

Written 2026-11-10 so a fresh conversation, on any machine, can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so what a new agent needs is here or in the files this points to.

**To start the new agent, say:** "Read `designs/access-analyzer-handoff.md`, then let's do Phase 3 of the Access
Analyzer spec."

## Where things are
- **The spec:** `designs/access-analyzer.md`. **Phase 1 LOCKED** 2026-11-06; **Phase 2 LOCKED** 2026-11-10. Read
  Terms (D2), D1–D16, the Phase 2 rules (1–41) and the answered questions before starting.
- **Branch:** `Version_1.2.4`. Greg controls branches, commits and pushes: never create a branch; commit or push only
  when he asks.
- **Release:** not scheduled; Greg doubts it makes 1.2.4. Keep specifying ahead of coding.
- **Related spec:** `designs/slot-browser.md` (its D3 level 2 is to be judged by whole slates, Q12).

## The method (Progressive Design Spec)
Six phases, each written into the spec and locked by Greg before the next: 1 Problem Statement, 2 Domain Model,
**3 UX Sketch**, 4 Data Design, 5 Architecture, 6 Implementation Plan. Greg decides when a phase is locked. Add a
`## Phase 3: UX Sketch` section after Phase 2; continue decisions at **D17** and questions at **Q29**; keep the Status
block current.

## Phase 2 in one paragraph
A slate takes one section of every **part** of every course (D9). Parts belong to the **course**: confirmed by the user
under Courses, or, while not set, guessed from section-type names (Lecture, Lab, Tutorial…) across all semesters and
never stored by the app (D13). A course with zero or one part asks for one section of any type. One section type per
section (D15); a new optional **delivery type** field on sections, ignored by access, with no tool to move existing
data (D14). One campus per slate (Q17). "Check all courses" checks each course on its own, on demand (D16). Notices
are session-only (Q21); no seat number when capacities are missing (Q20); teal boxes only for costly clashes, plus
every clash of a course that can't fit when there's no full slate (rule 33, Q22).

## What Phase 3 has to sketch (screens and words, no code)
1. **The Access Watch panel:** where it lives (Phase 1 says today's watch popup off the grid's Access group is where
   many users never look), the one-line health per watch, notices, the "Check all courses" results.
2. **Creating and editing a watch:** paste course codes, autocomplete picker, name filled in, tags/levels under "more
   options" (Q7); no mode choice up front.
3. **A watch's detail:** each course's parts marked guessed/confirmed, Edit per course, "These are right" for the
   watch (rule 37); doubtful-guess messages (rule 38); not-offered and missing-part flags; shared seats.
4. **The grid picture and stepping:** essential/usable/stranded colouring, stepping through slates with the Room
   Browser's ghost tiles and stepper, pins, subset ticking (Q11, D3, D7); revised teal boxes and their hovers.
5. **The editor pointer** (D6): one short line under the meeting rows.
6. **Courses flyout:** a Parts column ("Lecture, Lab (guessed)") and editing a course's parts.
7. **Copy Semester:** carrying watches, the one message listing not-offered courses (Q6), and not stripping section
   types from multi-part courses (rule 36).
8. **Delivery type** only as far as the analyzer touches it (it's its own small feature).

## Working agreements (Greg's)
- **Stories first,** short and concrete (Alice, Bob, Carla, Dana), today vs. with the feature; detail after, compact.
- **Plain words** for users; D2 terms exactly (access watch, course set, slate, partial slate), never "program".
- **Ask, don't assume** for what only Greg knows; propose the cheapest probe when hypotheses compete.
- **Don't treat one database as representative.** UFV BIOL (`C:\ProgramData\TermPoint\UFV\BIOL\BIOL-TT.db`) and UBC
  (`C:\ProgramData\TermPoint\UBC\UBC1-TT.db`) model labs differently; both are on Greg's dev machine only. Read only,
  from a copy.
- **Section cards and the editor are tight on space:** compact flags with hover detail rather than new controls.
- **Warn Greg before editing any .axaml file** (later phases).
- **Production app:** foreground data risk; check stored changes against `MinimumVersionToWrite` and the four
  shared-database rules (spec's Production sections).
- **No repetition;** short closing messages.
