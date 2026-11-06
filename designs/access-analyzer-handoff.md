# Handoff: Access Analyzer spec, Phase 2 (Domain Model)

Written 2026-11-06 so a fresh conversation, on any machine, can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so what a new agent needs is here or in the files this points to.

**To start the new agent, say:** "Read `designs/access-analyzer-handoff.md`, then let's do Phase 2 of the Access
Analyzer spec."

## Where things are
- **The spec:** `designs/access-analyzer.md`. **Phase 1 is LOCKED** (Greg, 2026-11-06, commit `24374a6`). Read all
  of it first, especially Terms (D2), Decisions D1–D8, and "Questions settled at lock".
- **Branch:** `Version_1.2.4` (the spec commits are there; not pushed). Greg controls branches, commits and pushes:
  never create a branch, and commit or push only when he asks.
- **Related spec:** `designs/slot-browser.md` (Phase 1, all settled, awaiting Greg's lock). Its D3 level 2 is to be
  judged by whole slates (access-analyzer Q12), and its program check will call the access-watch API (D8).
- **This feature is not scheduled into a release yet.**

## The method (Progressive Design Spec)
Six phases, one per session unless Greg wants to go faster, each written into the spec and locked by Greg before the
next: 1 Problem Statement, **2 Domain Model**, 3 UX Sketch, 4 Data Design, 5 Architecture, 6 Implementation Plan.
Greg decides when a phase is locked, never the agent. Add a `## Phase 2: Domain Model` section after Phase 1, with its
own decisions (continue numbering at D9) and questions (continue at Q17), and keep the Status block current.

**Phase 2 = entities, relationships, invariants. No code, no tables, no UI** (those are Phases 4, 5 and 3).

## What Phase 2 has to settle
1. **Q2 first: lectures and labs.** Carried over from Phase 1 with no answer. Does a slate need one section per
   course, or one per (course, section type)? How are labs modelled in real data: sections of the same course with a
   `SectionTypeId`, separate courses, or meetings inside one section? Are particular labs tied to particular lectures?
   **Cheapest probe:** look at the real-data DB (`C:\ProgramData\TermPoint\UFV\BIOL\BIOL-TT.db`, on Greg's dev machine
   only; read-only, copy it first) for courses with several section types, then ask Greg what other institutions do.
2. **Access watch and course set.** What a watch is (today's `ProgramWatch`: Id, Name, Mode Tag/Course, IsEnabled,
   TagIds, CourseIds, LevelIds; semester-scoped). Per Q1, only course-list watches get slate analysis; how tag-mode
   watches fit. What "course set" means when a course isn't offered this semester (Q6: flagged, user removes or keeps).
3. **Slate, partial slate, pins, subset.** Definitions as invariants: one section per course (or per component, Q2);
   same-course sections never joined; a partial slate for a subset is a full slate of the subset (D3); a pin is the
   only candidate for its course (D7); subsets and pins are view state, never stored.
4. **The clash test and coverage rule.** One definition each, shared by the detector, analyzer and Slot Browser:
   same day, half-open times, odd/even and week-list frequencies (`SectionDaySchedule.FrequenciesOverlap`). Sections
   without meeting times fit everything and are reported (Q3). Online/remote meetings with times are ordinary.
5. **Section roles in a watch:** essential / usable / stranded, and how they relate to partial slates.
6. **Seats.** What capacity means here (`Section.Capacity`, nullable = unknown), shared with other students and with
   other watches (Q4: flag, don't split), and how reserves (`Section.Reserves`: ReserveId + seat count) might relate
   later. Max-flow for two courses, LP bound for more (Phase 5 detail, but the definition belongs here).
7. **The "what if" view (D8).** The domain object the API reasons about: the semester's sections with a proposed
   change (moved, added, removed, unsaved) overlaid. Whole semester, ignoring grid filters.
8. **Notices and health (Q5, Q16):** what changes count as "worse", as domain rules (no full slate left; course not
   offered; newly essential; full slate restored), and which overlaps "cost something" for the teal boxes (leaves a
   section stranded, or the watch with no full slate).
9. **Carrying watches across semesters (Q6):** what a copied watch is, and what "not offered" means for it.

## Code to read (facts, as of 2026-11-06)
- `src/TermPoint/Models/ProgramWatch.cs`, `ProgramWatchMode.cs` — the watch model (`PersistedRecord`).
- `src/TermPoint/Services/ProgramConflictService.cs` — today's pairwise detector: coverage rule
  (`ResolveCoveredSections`) and clash test (`FindOverlappingMeetings`). Runs on grid-visible sections only.
- `src/TermPoint/Data/Repositories/ProgramWatchRepository.cs` — watches are stored per semester (`semester_id`).
- `src/TermPoint/Services/SemesterContentsService.cs` — deletes a semester's watches with it.
- `src/TermPoint/ViewModels/Management/CopySemesterViewModel.cs` — Copy Semester copies sections only, not watches.
- `src/TermPoint/ViewModels/GridView/AccessPanelViewModel.cs`, `WatchCreationViewModel.cs` (defaults to tag mode),
  `src/TermPoint/Views/GridView/AccessPanelView.axaml` — today's watch UI.
- `src/TermPoint/Models/Section.cs` — `CourseId`, `SectionTypeId`, `Capacity`, `Reserves`, `Level`; meetings come
  from `SchedulableBase.Schedule` (`SectionDaySchedule`: Day, StartMinutes, EndMinutes, Frequency).
- Courses and tags are global (not per semester), so copied watches keep valid IDs.

## Working agreements (Greg's)
- **Stories first.** Lead with short concrete stories (Alice the scheduler, Bob another department, Dana the advisor),
  today vs. with the feature; technical detail after, compact. Greg: "Otherwise I'm buried in verbiage."
- **Plain words** in anything users see; use the D2 terms exactly (access watch, course set, slate, partial slate),
  never "program" for these.
- **Ask, don't assume,** for anything only Greg knows (Q2 especially). When there are competing hypotheses, propose
  the cheapest probe that tells them apart.
- **No repetition.** Keep closing messages short; don't re-explain settled points.
- **Production app.** Foreground any data risk; check every stored change against the `MinimumVersionToWrite`
  question and the four shared-database rules (see the spec's Production section). Phase 2 itself stores nothing.
- New tables need Greg's choice of human-readable columns (Phase 4).
