# Collapse Days in the Schedule View — Progressive Design Spec

**Status:** ✅ Phase 1 (Problem Statement) complete 2026-11-05: Q1–Q10 and Q2a settled (see "Phase 1
decisions"). ✅ Phase 2 (Domain Model) complete
2026-11-05: P2-1–P2-5 settled. **Next: Phase 3 (UX Sketch).**
**Target release:** 1.2.4. **Expands** summary item 2.13. The summary's "Design direction" (toolbar toggles, narrow
strips, MWF/TR/All shortcuts) is treated here as one candidate, not a decision. UI waits for Phase 3.

---

## Phase 1: Problem Statement

### The problem

A busy week doesn't fit across one monitor. In real UBC data (Winter Term 1), 400-level CIVL + MECH needs 2,642 px
and 300-level CIVL + ELEC + MECH needs 2,961 px; a monitor's grid area is about 1,500 px. With readable labels a day
holds about two side-by-side tiles, and real slices need three or more at busy times.

So the user scrolls sideways. While scrolling they lose sight of the days they were comparing with, which is the
whole point of a week grid. Narrowing the tiles themselves was tried five ways and set aside (summary, "Decided
against": cascading, notched tiles, fit-to-window scaling, "+N more", turning the grid).

The observation behind this item: most questions a scheduler asks of the grid involve only some days. Sections that
meet more than once keep to MWF or TR (163 of 199 in the same data; another 310 meet on one day). Someone placing an
MWF section doesn't need Tuesday and Thursday on screen while they do it.

### Stories

**Today.** Alice is placing MECH 360 001, an MWF lecture, for the 300-level CIVL + ELEC + MECH students. She opens
the Schedule view with those subjects in focus. Monday is about 700 px wide, Tuesday 600, and so on: Friday is off
the right edge. She looks for a 10:00 slot that's free on Monday, then scrolls right to check Wednesday, loses
Monday, scrolls to Friday, scrolls back. She does this for each candidate time.

**With the item.** Alice tells the grid she only cares about Monday, Wednesday and Friday for now. Tuesday and
Thursday fold away. Monday, Wednesday and Friday sit side by side (about 1,700–1,800 px; with the tile-padding
change below, close to fitting). She can see that 10:00 is free on all three at once. When she's done she brings
Tuesday and Thursday back.

**The risk story (must not happen).** Bob folds away Tuesday and Thursday on Monday afternoon and closes the app.
On Wednesday morning he opens it to check whether Dr. Ng is free Tuesday at 2:00. He glances at the grid, sees no
Tuesday tiles, and books her. Tuesday was there all along; it was folded. *Whatever we build, Bob must never mistake
a folded day for an empty one.*

**The handout story.** Carol exports the Schedule view to PNG for a department meeting while Tuesday and Thursday
are folded. Either the picture shows all five days, or it plainly says it shows MWF only. It must not look like a
full week with two empty days.

### What "done" looks like

1. The user can make the grid show only the days they're working on, and get the rest back, in one or two clicks.
2. With two or three days shown, the busy slices above fit (or nearly fit) across one monitor without scrolling.
3. Nothing can mistake a hidden day for an empty one: not the user (Bob), not a printout (Carol), and not the code
   (clicks, right-click menus, conflict marks and Room Browser ghost tiles land on the right day).
4. Display only: no change to the database, no version gate, no data risk.

### Out of scope (for now)

- Narrowing tiles any other way (the five options already set aside).
- Hiding time ranges (e.g. evenings). Possible later, separate item.
- Turning the grid sideways (parked, not rejected).
- The Section list. Folding a day affects only the grid.

### Related change bundled with this item

**Less tile padding.** Each tile's width allowance (28 px) assumes 3 px multi-semester borders and includes an 8 px
safety margin. With one semester shown, about 10 px per side-by-side column could come back: roughly 100–150 px on a
busy week. It helps the full week too, so it's worth doing even if folding days changes shape. It's independent of
the rest and could be its own task (Q10).

### What exists today (facts, not decisions)

- **Days shown** are built in one place: `ScheduleGridViewModel.UpdateDisplayProperties` takes Mon–Fri, plus
  Saturday and Sunday when `AppSettings.IncludeSaturday/IncludeSunday` are on. Those two settings mean "the
  institution schedules on this day" (the importer, block patterns and commitments read them too). They aren't a
  display choice and shouldn't be reused for one.
- **Day patterns already exist in the database:** `BlockPattern` ("MWF" = days 1, 3, 5), shared by everyone on the
  database and edited in Settings. Shortcuts could come from these instead of a fixed MWF/TR list (Q5).
- **The view assumes no gaps.** In `ScheduleGridView.axaml.cs`, a tile's day is worked out from its column
  (`clickDay = (d / semCount) + 1`) and ghost tiles go to column `(ghost.Day - 1) * semCount`. Both assume Monday
  first and no day skipped. Clicks, the right-click menu, program-conflict marks and Room Browser ghosts would land
  on the wrong day once any day is hidden. Each column has to carry its real day number. (A Phase 5 concern, noted
  here so it's costed in.)
- **PNG export** renders the grid canvas with a title banner (`ScheduleGridView.axaml.cs`), so it shows whatever the grid shows.

### Risks

| Risk | Story | Guard (to be designed) |
|---|---|---|
| A hidden day read as empty | Bob | Settled: a strip with a hidden count (Q3), and the full week on every launch (Q2) |
| Export misleads | Carol | Settled: export matches the screen and the banner names the days shown (Q7) |
| Clicks/marks land on the wrong day | — | Columns carry real day numbers; tests over a gapped day list |
| Selecting a section whose meetings are all on hidden days shows nothing | Alice clicks MECH 361 (TR) in the list | Settled: the strip shows a mark and flashes once (Q8) |
| Feature creep into a filter | — | Settled: a view of days, not a filter (Q9) |

### Open questions (Phase 1)

Each has my suggestion; nothing is settled until you say so.

- **Q1. What is the user doing when they fold days?** Placing one section with a known pattern (short, minutes)?
  Reviewing "the MWF timetable" for a while (a session)? Producing a handout? The answer drives Q2.
  *Suggestion:* the first, mainly. It's a working aid while placing or checking a section.
- **Q2. Does the choice survive closing the app?** Remembering it is convenient; forgetting it protects Bob.
  *Suggestion:* don't remember it. Every launch starts with the full week. If Q3 makes folded days impossible to
  miss, remembering becomes acceptable, so decide Q3 first.
- **Q3. What does a folded day look like?** Gone entirely; a narrow strip with its letter; a strip that also says
  how much is hidden (e.g. "T · 41").
  *Suggestion:* a strip, never gone (the summary's idea). Whether it carries a count is a Phase 3 detail.
- **Q4. Which days can be folded?** Mon–Fri only, or Saturday/Sunday too when the institution schedules on them?
  *Suggestion:* any day that's shown.
- **Q5. Are there shortcuts, and where do they come from?** Fixed MWF / TR / All, or the database's block patterns
  (already named and maintained by the department)?
  *Suggestion:* block patterns, plus "All days". A department on MW / TR or MTWR gets its own patterns for free.
- **Q6. Does it apply to the detached Schedule view and multi-semester mode the same way?**
  *Suggestion:* yes: one choice, same everywhere the grid is drawn.
- **Q7. Does PNG export follow the folded days?** *Suggestion:* yes (what you see is what you get), and the title
  banner names the days shown, e.g. "Mon, Wed, Fri only".
- **Q8. What happens when something points at a folded day?** The user selects a TR section in the list; a Room
  Browser ghost or an Access-watch conflict mark falls on Tuesday.
  *Suggestion:* nothing unfolds by itself; the strip shows that something there is selected/marked. Unfolding on its
  own would undo the user's choice behind their back.
- **Q9. Is this a view or a filter?** A filter would hide *sections* that don't meet on the chosen days (and their
  other meetings). A view hides *days*, and an MWF section that also has a Tuesday lab still shows its MWF tiles.
  *Suggestion:* a view. Filters live in the filter bar and already have their own rules.
- **Q10. Ship the padding change with this item or on its own?** *Suggestion:* its own small task, done first. It
  helps every user straight away and gives a better measure of how much folding is still needed.

### Phase 1 decisions

| # | Decision (Greg, 2026-11-05) | What it means |
|---|---|---|
| Q1 | **Both, equally.** Folding days is used for placing one section (minutes) *and* for reviewing a pattern (an afternoon on the TR timetable). | It has to be quick to fold and unfold, and must also stay out of the way while it's left on for a long time. |
| Q3 | **A narrow strip with the day letter and how much is hidden** (e.g. "T · 41"). Never gone entirely. | An empty Tuesday reads "T · 0" and a full one "T · 41", so Bob can't take one for the other. What exactly is counted (sections, meetings or tiles) and how the strip looks at each font size are Phase 3 details. |
| Q2 | **Not remembered.** Every launch shows the full week. | Bob's story can't happen across a restart. Nothing is stored in AppSettings for this. |

| Q2a | **Changing what's being looked at resets to the full week:** a semester switch (including changing which semesters are shown together), a subject-focus change, or opening a different database. **Nothing else resets it:** no idle timer, nothing unfolds on its own. If the app is left open overnight, the strip with its count (Q3) is what protects Bob. | Every new timetable or slice opens complete. Alice reviewing TR in Fall and then Winter folds again after switching, which a shortcut (Q5) makes one click. |

| Q4 | **Any day shown can be folded**, including Saturday and Sunday when the institution schedules on them. | One rule for every day. |
| Q5 | **Three ways in:** the database's block patterns (MWF, TR, …), "All days", *and* a quick way to pick any set of days (e.g. M, R, F), **including a single day**. Single-day sections are common (labs), and nobody sets up a one-day block pattern for them, because it saves no clicks when creating a section. **The UI must be very easy and add as little clutter as possible** (Greg). | Block patterns can't be the only shortcut. Picking an arbitrary set of days, and "just this day", must each take a click or two. This is a hard constraint for Phase 3. |
| Q6 | **One choice everywhere.** With several semesters shown, folding Tuesday folds all of Tuesday's semester sub-columns into one strip with one total count. The detached Schedule view uses the same choice. | One set of folded days per session, which simplifies the model. |

| Q7 | **PNG export shows what's on screen**, strips included, and the title banner names the days shown (e.g. "Mon, Wed, Fri only"). | Carol's handout can't pass for a full week, and she can still export just the MWF view. |
| Q8 | **Attention is called to anything selected or marked on a folded day.** The strip shows a mark, *and* draws the eye with a brief flash when it first appears, the way an online form flags a field you missed. Nothing unfolds by itself; clicking the strip opens the day. | Covers a selection from the Section list, Room Browser ghost tiles and Access-watch conflict marks. The flash happens once, when the mark arrives; it doesn't keep blinking. What it looks like is a Phase 3 detail. |
| Q9 | **A view, not a filter.** Folding hides days, never sections. MECH 360 (MWF lectures, Tuesday lab) still shows its MWF tiles when Tuesday is folded. | Filters stay in the filter bar with their own rules. A strip's count is simply what that day holds under the current filters. |
| Q10 | **The tile-padding change is its own task, done first.** | It helps every user straight away and shows how much folding is still needed. It's the first task in Phase 6. |

**Phase 3 candidates for Q5 (ideas, not decisions):** the day headers are already on screen, so they could be the
control. Clicking a header folds or unfolds that day, with no new toolbar controls, which covers any set of days.
"Only this day" (the lab case) and the block patterns could sit on the header's right-click menu, along with "All
days". A strip is the folded header, so clicking it brings the day back. To check in Phase 3: whether clicking a
header is discoverable enough, and how a header shows it can be clicked. (Day headers handle no clicks or right-clicks today, so nothing clashes.)

**Reading of Q2a, to confirm in Phase 3:** filter-bar changes, detaching or reattaching the Schedule view, the grid's
font size, and the app reopening the *same* database (e.g. after taking the lock back) keep the folded days. These
narrow or redraw the same timetable rather than switching to another one. If Saturday or Sunday is turned off while
it's folded, it simply drops out; turned back on, it comes back unfolded.

---

## Phase 2: Domain Model

Small: nothing is stored, and no entity changes. There is one piece of session state, plus the rules for what a strip
says and when it calls for attention.

### Stories that shape the model

**The count.** Alice folds Tuesday with the 300-level CIVL + ELEC + MECH focus. The Tuesday strip reads "T · 41".
She turns on the instructor overlay for Dr. Ng, which adds Dr. Ng's Tuesday committee commitment, and turns on the
Chemistry shared schedule, which adds 12 CHEM tiles on Tuesday. Does the strip still say 41? (P2-1)

**The selection.** Tuesday and Thursday are folded. Alice clicks MECH 361 (TR) in the Section list. Neither of its
meetings is on screen, so the T and R strips show a mark and flash. Then she clicks MECH 360 (MWF lectures, Tuesday
lab). Its lectures are on screen and highlighted; its lab is folded. Should the T strip flash too? (P2-3)

**The steady mark.** Bob has an Access watch on. A Tuesday conflict has been there all week. He folds Tuesday. The
strip shows the conflict mark, but it shouldn't flash: Bob folded the day himself with the conflict on it, so nothing
new has happened. (P2-4)

### What the app holds (per session, never stored)

- **Folded days:** a set of day numbers (1 = Monday … 7 = Sunday). Empty means the full week. There's one set for the
  session, shared by the docked and detached Schedule view and by every semester sub-column (Q6).
- **Days shown** are worked out, not held: the days the institution schedules on (Mon–Fri, plus Saturday/Sunday when
  turned on in Settings), in week order. Each one is either *open* or *folded*.

### What the app works out for each folded day

- **Count:** how much that day holds under the current filters and focus (P2-1 says what counts). Across all shown
  semesters (Q6).
- **Marks:** whether anything on that day calls for attention (P2-2 says what does).
- **Flash:** whether a mark has just *arrived* (P2-3, P2-4).

### Rules (invariants)

1. Folding changes only what's drawn. Sections, meetings, filters, selection and the database never change (Q9).
2. A folded day is always drawn as a strip, never dropped (Q3).
3. The folded set only holds days that are shown. A day that stops being shown (Saturday turned off) drops out of it.
4. **Resets to empty** on a semester switch (including which semesters are shown together), a subject-focus change, or
   opening a different database (Q2a). Nothing else changes it except the user.
5. The app never folds or unfolds a day on its own, apart from rule 4 (Q8).
6. Every column knows its real day number. Nothing works out a day from a column's position (the Phase 1 trap).
7. Export draws exactly what's on screen, and its banner names the open days when any are folded (Q7).

### Edge cases

- **Folding every day.** Allowed, or does at least one day have to stay open? (P2-5)
- **"Only this day" on a day that's already the only one open:** nothing to do.
- **Choosing a block pattern** (e.g. MWF) folds every shown day not in the pattern, and opens the ones in it. A
  pattern day that isn't shown (a Saturday pattern with Saturday turned off) is ignored.
- **A strip that reads 0.** An empty folded day reads "T · 0". It's still a strip, so Bob still knows the day exists.

### Open questions (Phase 2)

- **P2-1. What does the count count?** The grid draws section meetings, meetings (faculty/committee), instructor
  commitments (with an overlay), shared-schedule tiles (other departments) and Room Browser ghosts.
  *Suggestion:* **sections** meeting on that day, counting each section once. That's the number a scheduler means
  ("41 sections on Tuesday"), and it doesn't jump when an overlay or shared schedule is turned on. A day with no
  sections but a meeting or commitment would read "T · 0" and wrongly suggest it's empty for Dr. Ng, so add a small
  second sign when the day holds anything else (Phase 3 decides what it looks like).
- **P2-2. What calls for attention on a strip?** Candidates: (a) a selected section with a meeting there; (b) a Room
  Browser ghost there; (c) an Access-watch conflict there; (d) sections highlighted by the filter bar; (e) flagged
  sections; (f) the instructor overlay's red tiles and commitments.
  *Suggestion:* (a), (b) and (c): the things the user is acting on now. (d), (e) and (f) cover many sections at once
  and are always on while in use, so marking them would mark nearly every strip.
- **P2-3. When a selected section is partly on screen, does the folded part flash?** (MECH 360's Tuesday lab.)
  *Suggestion:* mark the strip, but flash only when **none** of what was just selected or placed is on screen. If the
  lectures are highlighted, Alice already sees her section; the mark is enough to say the lab is behind the strip.
- **P2-4. What counts as a mark "arriving"?** *Suggestion:* a mark flashes when it appears on a strip that was already
  folded: a new selection, new ghosts, a conflict that wasn't there before. Folding a day that already has a mark
  doesn't flash it (the user just saw it). Redraws (filters, font size) don't flash anything.
- **P2-5. Can every day be folded?** *Suggestion:* no. The last open day can't be folded (the header gives no fold
  option). A grid of strips only is never useful, and the counts are no substitute for the grid.

### Phase 2 decisions

| # | Decision (Greg, 2026-11-05) | What it means |
|---|---|---|
| P2-1 | **Count sections**, each once, **plus a small sign when the day also holds anything else** (meetings, commitments, shared-schedule tiles). | "T · 0" never hides Dr. Ng's commitment. The sign's look is Phase 3. |
| P2-2 | **Marks for:** a selected section, Room Browser ghosts, Access-watch conflicts, **and sections that pass a filter** when a filter is active. **Not** flagged sections. | See the note below on filters. |
| P2-3 | **Every new mark flashes**, even when part of the section is on screen (MECH 360's Tuesday lab flashes although its lectures are visible). | Simple rule: a mark arriving on a strip always flashes once. |
| P2-5 | **One day must stay open.** The last open day offers no fold option. | A grid of only strips is never shown. |

**Note on P2-2 and filters (fact from the code).** The grid already *hides* sections that don't pass a filter, so a
folded day's count is already the number of matching sections. The filter mark is what makes "this folded day has
matches" stand out at a glance, the way the Section list highlights matching cards. It shows only for a real filter
(instructor, room, subject, campus, section type, tag, meeting type, level, course), the same test the Section list
uses (`hasActualFilter` in `ScheduleGridViewModel`). "Emphasize unstaffed" excludes nothing, so it doesn't count.
Applying a filter therefore flashes every folded strip that has matches.

**P2-4 (settled 2026-11-05, Greg accepted the suggestion): what counts as a mark arriving?** A mark flashes when it appears on a strip that
was already folded: a new selection, new ghosts, a new conflict, or a filter being applied or changed. Folding a day
that already has a mark doesn't flash it (the user just saw it there). Plain redraws (font size, detaching the view)
flash nothing.

### Pointers for later phases (not decisions)

- Phase 3: where the control lives (toolbar, the day headers themselves, a right-click on a header), and how the
  strip looks at the grid's different font sizes.
- Phase 5: the gapped-day trap above; the Room Browser and Slot Browser both draw onto this grid.
- Later: the Slot Browser could fold the grid to the days of the pattern it's searching.
- Mocks from the real data (local only, ignored `tmp/` on GregDesktop): `tmp/monday_tile_layouts.html`,
  `tmp/week_fit_mock.html`, `tmp/week_more_mock.html`.
