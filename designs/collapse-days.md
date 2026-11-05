# Collapse Days in the Schedule View — Progressive Design Spec

**Status:** ✅ Phase 1 (Problem Statement) complete 2026-11-05: Q1–Q10 and Q2a settled (see "Phase 1
decisions"). ✅ Phase 2 (Domain Model) complete
2026-11-05: P2-1–P2-5 settled. ✅ Phase 3 (UX Sketch) complete 2026-11-05:
P3-1–P3-5 settled. ✅ Phase 4 (Data Design): nothing stored, folded into Phase 5 (Greg, 2026-11-05).
✅ Phase 5 (Architecture) complete 2026-11-05: P5-1–P5-3 settled.
✅ Phase 6 (Implementation Plan) signed off 2026-11-05. **Spec locked.** ✅ Task 1 (tile padding) done 2026-11-05 (1eb90c9; see its "Outcome"). ✅ Task 2 (real day
numbers) done 2026-11-05 (233488a). ✅ Task 3 (fold state and commands) done 2026-11-05 (043eaea).
✅ Task 4 (strips and header controls) done 2026-11-05 (68c9a0f). ✅ Task 5 (marks and the flash) done
2026-11-05 (98a02df). ✅ Task 6 (export and release check) done 2026-11-05, uncommitted. **The item is complete,
pending Greg's eye pass on the desktop app and the macOS check on the next signed build.**
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
font size keep the folded days. (Reopening the *same* database resets them after all: P5-1.) These
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

---

## Phase 3: UX Sketch

**Constraint from Q5:** very easy, little clutter. Any set of days, "just this day" and a block pattern each take a
click or two.

### What's on screen today (facts)

- The day headers ("Monday" …) are drawn on the grid canvas, so they **scroll away** when the user scrolls down. They
  handle no clicks.
- The grid's **footer bar** never scrolls: licence text on the left, "Text:" font size and "Zoom:" slider on the
  right. It has room for one more small group.
- Tiles already have a right-click menu (a popup in `ScheduleGridView.axaml`); headers don't.

### Stories

**Placing an MWF lecture.** Alice clicks the "Tuesday" header; Tuesday shrinks to a narrow strip reading "T / 41".
She clicks "Thursday"; same. Monday, Wednesday and Friday now sit side by side. Two clicks. When she's done she clicks
each strip to bring the day back, or right-clicks any header or strip and picks "Show all days".

**Using a block pattern.** Bob right-clicks any header. The menu lists "Show only Tuesday", then the department's
patterns ("Show MWF", "Show TR", "Show MW"), then "Show all days". He picks "Show TR". Two clicks.

**A lab day.** Carol is placing a Thursday lab. Right-click "Thursday" → "Show only Thursday". Two clicks; the other
days become strips.

**Something behind a strip.** With Tuesday folded, Alice clicks MECH 361 (TR) in the Section list. The Tuesday strip
gets a small mark **at 14:00**, the time the section meets, and the strip flashes amber twice. She hovers: "Tuesday is
folded: 41 sections, 2 events. MECH 361 001 (selected) at 14:00. Click to show Tuesday." She clicks; Tuesday opens
and MECH 361's tile is highlighted as usual.

**Scrolled down.** Bob has scrolled to the evening. The headers are off screen, but the strips still run the full
height, so he can see Tuesday is folded, and a Room Browser ghost's mark at 19:00 sits right where he's looking.

### The control: the day headers

| Action | Gesture |
|---|---|
| Fold a day | Click its header |
| Open a folded day | Click its strip (anywhere on it) |
| Only this day | Right-click a header or strip → "Show only Tuesday" |
| A block pattern | Right-click → "Show MWF" (one entry per pattern, from Settings → Block patterns) |
| Everything back | Right-click → "Show all days" (only when something is folded) |

- **Showing it can be clicked:** on hover, the header gets the hover fill and a small fold icon (a `Path` in AXAML,
  per the icon rule) at its right end, plus a tooltip "Click to fold Tuesday away. Right-click for more." The pointer
  becomes a hand.
- **The last open day** (P2-5) shows no fold icon and does nothing on click; its menu has no "Show only".
- **Nothing new in the toolbar or filter bar.**

### The strip

```
 ┌─────┐
 │  T  │  ← one-letter day name, header font (FontSizeXLarge), same row height as other headers
 │ 41  │  ← section count (P2-1), smaller font
 │  •  │  ← "other things" sign: only when the day also has events/commitments/shared tiles (P2-1)
 ├─────┤
 │░░░░░│  ← full height of the grid, a muted hatch fill so it never reads as an empty day
 │░░▬░░│  ← a mark, drawn at the time of what it marks (selection, ghost, conflict, filter match)
 │░░░░░│
 └─────┘
```

- **Width:** just enough for the letter or the count (whichever is wider) plus side padding, about 24 px at the
  default size. Folding two days saves their full width minus about 50 px.
- **Name:** one letter (P3-2), the app's own set: M T W R F (as `ScheduleText` already uses), with **Sa** and **Su**
  for the weekend, since one letter can't tell those apart. The tooltip always gives the full name ("Thursday is
  folded…"), so "R" is never the only clue. The export banner still uses short names ("Mon, Wed, Fri only").
- **Marks:** a short horizontal bar across the strip at the marked item's start time, in that thing's own colour
  (selection blue, ghost dashed, conflict red, filter-highlight blue). Several marks at one time stack as one bar.
  Like the match marks on an editor's scrollbar, they say *when* as well as *that*.
- **Flash** (P2-3, P2-4): the strip's background pulses to an attention colour and back, twice, about one second in
  all. Then it stays as drawn. No sound, no repeated blinking.
- **Hover** shows a tooltip: what's folded, what it holds, what's marked and when.
- **Multi-semester:** one strip for all semester sub-columns (Q6), no semester bars.
- **Font sizes:** the strip's name follows the header font; the count follows the grid's "Text:" size. All sizes,
  colours and the width padding are resources in App.axaml / AppColors.axaml.

### Export (Q7)

The PNG shows the strips as drawn. Its title banner adds the open days: "Mon, Wed, Fri only".

### Phase 3 decisions

| # | Decision (Greg, 2026-11-05) | What it means |
|---|---|---|
| P3-1 | **The day headers are the only control:** click to fold, click a strip to open, right-click for "Show only …", the block patterns and "Show all days". No footer or toolbar picker. | Nothing new in the toolbar, filter bar or footer. The full-height strips show the state at any scroll position. |
| P3-2 | **One letter on the strip** (M T W R F; Sa, Su for the weekend). | About 24 px per strip. The tooltip names the day in full. |
| P3-3 | **Marks sit at their time** on the strip, as a short bar. | The user sees *when*, and marks stay visible when scrolled down. |
| P3-4 | **Muted hatch fill**, full height. | A folded day never reads as an empty one. Colours are resources in AppColors.axaml. |
| P3-5 | **"Show MWF" means exactly MWF:** the pattern's days open and every other shown day folds. | One click gives a known state, whatever was folded before. A pattern day that isn't shown (Saturday turned off) is ignored (Phase 2 edge cases). |

**Revision 1 (Greg, 2026-11-05, after seeing the task 4 strips and mocks): the strip is a folded page.**

| # | Decision | Replaces |
|---|---|---|
| R1-1 | **The strip looks like a sheet folded almost shut down its middle:** a light left face, a shadowed right face, a crease line. **No hatch.** | P3-4's hatch fill. |
| R1-2 | **Remnants of the hidden sections:** a flat band in the tile colour (lighter on the left face, shadowed on the right) wherever any section meets that day, with a hairline where one tile ends and the next starts ("B2"). Every band looks the same: it shows *that* something is there, not how busy it is (no darker shading for overlaps). | The plan to show sections only through task 5's marks. |
| R1-3 | **Other things** (meetings, commitments, shared-schedule tiles, overlay tiles) are **bars at their own times**, in their own colours, instead of one dot under the count. | P2-1's "small sign". |
| R1-4 | **Task 5's marks are bars at their times too**: a selected section, Room Browser ghosts, Access-watch conflicts. **No separate filter mark**: with a filter on, the grid already shows only matching sections, so the remnant bands are the matches. | P2-2's filter mark and P5-2. |

**Revision 2 (Greg, 2026-11-05): what gets marked.** The rule: **mark what the user pointed at; never mark what only
narrows the view.** Narrowing (filters, subject focus, levels) changes the whole grid; marking it would light up
nearly every strip and make users open days just to check. The remnant bands already reflect it.

| What | On a folded strip |
|---|---|
| Selected sections, single or multi (Section list, Events view, clicking a section or an instructor's name in the Workload view; all go through the section store's selection) | **Attention mark**, flashes when it arrives |
| Room Browser ghosts (later: Slot Browser placements) | **Attention mark**, flashes |
| Access-watch conflicts | **Attention mark**, flashes only when new (P2-4) |
| Overlays, meetings, commitments, shared schedules | **Remnant bars** (R1-3); never flash |
| Filters, subject focus, flags | Nothing |

**Two layers on a strip.** *Remnants* are what the day holds, drawn quietly (task 4). *Attention marks* are what the
user pointed at: each is a kind, a start and end time, and optionally the section it is about. Only attention marks
flash, and only when new. One pure function turns a folded day plus the current *pointers* (selection, ghosts,
conflicts) into marks. Ghosts carry a kind, so the Slot Browser adds kinds and colours, not strip code. Kinds it will
need: a proposed placement, the section that would move (its current time and its proposed time, marked
differently), and a section a placement breaks ("overlaps PSYCH 340 A"). Several marks at one time: show the most
important (selection, then a proposal, then a conflict), not a split bar.

---

## Phase 4: Data Design (folded into Phase 5)

Nothing is stored: no table, no JSON field, no AppSettings entry, no `settings.json` change. The folded days live in
memory for the session (Q2). Block patterns are only *read*, through the existing repository.

- **Version gate** ("Could the new version save anything the old version wouldn't have?"): **No.**
- **The four rules for the shared database:** not touched. Nothing here saves, locks, exits, switches, restores or
  recovers; folding only changes what the grid draws.

---

## Phase 5: Architecture

### How a fold flows (story)

Alice clicks the "Tuesday" header. The header's click handler (drawn by the view, like tile handlers today) calls
`ScheduleGridViewModel.ToggleDay(2)`. The VM adds 2 to its folded set and rebuilds `GridData` from the blocks it
already built on the last reload, **without touching the database**. The view sees `GridData` change and redraws:
Tuesday's columns get the strip width, no tiles, a strip with "T", the count and the "other" sign. Selecting MECH 361
in the Section list then changes `SelectedSectionIds` as today; the view's light selection repaint also repaints the
strips' marks, and the Tuesday strip flashes because its selection mark just arrived.

### Where things live

| Piece | Where | What it does |
|---|---|---|
| Folded days | `ScheduleGridViewModel` (a singleton per open database) | `IReadOnlySet<int> FoldedDays`. The docked and detached Schedule views share the VM, so they share it (Q6). |
| Commands | `ScheduleGridViewModel` | `ToggleDay(day)`, `ShowOnlyDay(day)`, `ShowPattern(pattern)`, `ShowAllDays()`. Each refuses to fold the last open day (P2-5). Each rebuilds `GridData` from the cached blocks. |
| Block patterns for the menu | `ScheduleGridViewModel`, read from the existing block-pattern repository when the menu opens | So a pattern edited in Settings shows next time, with no new event. |
| Day columns | `GridDayColumn` gains **`DayNumber`** and **`IsFolded`** | Fixes the gapped-day trap: nothing works out a day from a column position any more. |
| Strip summary | `FoldedDaySummary` on the first column of a folded day: section count, "has other things", and the section meetings there (id + start time) | Built in `UpdateDisplayProperties`, where the day list is built today. |
| Marks | `FoldedDayMarks` (new, pure, no Avalonia): from a summary + selected ids + ghosts + conflicts + "real filter on" → a list of (time, kind) | Pure so it can be unit-tested; called by the view whenever selection, ghosts or `GridData` change. |
| Flash | `StripAttention` (new, pure): remembers each folded day's marks; returns the days whose mark set gained something new | Implements P2-4: a day that was just folded is seeded with its current marks, so it doesn't flash. Plain redraws don't flash. |
| Header and strip clicks, right-click menu | `ScheduleGridView` code-behind (the renderer, which already wires tile clicks), plus a header menu popup in `ScheduleGridView.axaml` beside `TileContextPopup` | The popup's items bind to the VM. |
| Flash animation | The view: animate the strip's background to `FoldedDayFlashBrush` and back, twice (an Avalonia `Animation` in code) | The colour and timing are resources. |
| Export banner | `ScheduleGridViewModel.FoldedDaysCaption` ("Mon, Wed, Fri only", empty when nothing is folded), appended to the PNG title | |
| Look | Resources: `FoldedDayStripPadding`, `FoldedDayHatchBrush`, `FoldedDayMarkHeight`, the mark brushes (reusing selection/conflict/ghost colours), `FoldedDayFlashBrush` | Per CLAUDE.md: no hardcoded sizes or colours. |

### Resets (Q2a)

| Trigger | Hook |
|---|---|
| Semester switch (or which semesters are shown together) | `ReloadCore` already detects this (`_lastSemesterKey`) and clears the filters there; clear the folded days in the same place. |
| Subject-focus change | `FocusService.FocusChanged`: clear the folded days, then reload as today. |
| Opening a database | Free: services are rebuilt on every open (`App.InitializeServices`), so a new VM starts with nothing folded. See P5-1. |
| Saturday/Sunday turned off | `UpdateDisplayProperties` drops folded days that aren't shown. |

### Renderer changes (`ScheduleGridView.axaml.cs`)

- **Measuring:** folded columns are skipped (no tiles, no text measuring), so folding makes rendering a little faster.
- **Widths:** a folded day's columns together get the strip width (wider of the letter and the count, plus padding).
- **Drawing:** one strip per folded day across all its semester sub-columns; no semester bars there.
- **Day from column:** every `(d / semCount) + 1` and `(ghost.Day - 1) * semCount` becomes a lookup on
  `DayNumber`. A ghost on a folded day becomes a strip mark instead of a tile.
- **Conflict lines** (Access watches) between two open days draw as today; an end on a folded day becomes a mark.
- **The tile-padding change (Q10)** is a separate first task in the same file.

### Tests

- VM: the day list with gaps, the last-open-day rule, pattern semantics (P3-5), each reset trigger and the
  non-triggers (filter, font size), Saturday turned off while folded, the export caption.
- `FoldedDayMarks`: each kind of mark, the filter test (`hasActualFilter` only), times, several marks at one time.
- `StripAttention`: new marks flash; folding a day with marks doesn't; redraws don't.
- Columns: clicks, ghosts and conflicts map to the right day with a gap in the week (the trap's regression test).

### Phase 5 decisions (all settled 2026-11-05, Greg accepted each suggestion)

| # | Decision |
|---|---|
| P5-1 | **Any database open resets the folded days**, including reopening the same database (taking the lock back, recovery). This replaces the Phase 1 "Reading of Q2a" on same-database reopens. |
| P5-2 | **With a filter on, a bar at each matching section's time** on a folded strip. |
| P5-3 | **Keep flat per-semester columns with `DayNumber` and `IsFolded`**; `GridData` isn't reshaped. |

The questions as asked:

- **P5-1. Reopening the *same* database resets the folded days.** Phase 2 assumed it would keep them, but every open,
  including taking the lock back or a recovery reopen, rebuilds the services, so the VM starts over. *Suggestion:*
  accept the reset. A reopen is rare and comes with its own message, and keeping the state across it would mean
  holding it outside the services just for this.
- **P5-2. With a filter on, every section on a folded day is a match** (the grid already hides the rest), so the
  filter marks are one bar at each of the day's section times. *Suggestion:* accept: it reads like an editor's
  scrollbar showing where the matches are. The alternative is one filter mark per strip, at the top.
- **P5-3. A folded day keeps its flat sub-columns (one per semester) with a flag,** rather than reshaping `GridData`
  into a list of days. *Suggestion:* the flag. It's a smaller change to a renderer that's been field-tested, at the
  cost of a little special-casing for strips.

---

## Phase 6: Implementation Plan

Signed off 2026-11-05. Six tasks, each about one session, in this order. Each ends with the
compile check, the relevant tests, and a commit only when Greg asks. Tasks that touch `.axaml` files say so: **Greg
closes those tabs in VS first** (CLAUDE.md).

### Release facts

- **`MinimumVersionToWrite`: No.** Nothing reaches the database or `settings.json`.
- **Four rules for the shared database:** not engaged (Phase 4).
- **Production risk:** low, but it's in the renderer every user sees. **With nothing folded, the grid must look
  exactly as in 1.2.3** after tasks 2–5 (task 1 deliberately changes widths). The risks are clipped tile text from
  task 1 and a wrong day from task 2; both get tests and an eye check.

### Order and why

The two changes that help or protect everyone come first and could ship on their own: the padding (Q10), then the
real day numbers (the trap, with no visible change). Then the model without UI (all unit-tested), then the strips,
then marks and the flash, then export and the release check. Each UI task can be checked by eye on the real UBC
slices.

### Task 1: Tighter tile padding (Q10)

- `ScheduleGridView.axaml.cs`: `TileTextOverhead` (28 px) becomes worked out from its parts: the tile border is
  1 px with one semester shown and 3 px with several, and the 8 px safety margin shrinks to what measuring needs.
  The parts move to resources where they're design values.
- **Measure first:** before and after widths for the two UBC slices (2,642 and 2,961 px today) and Monday alone.
- **Check:** no ellipsis on any tile at each "Text:" size, one and several semesters, Windows and the WASM demo
  (its fonts measure differently: `PlatformCapabilities`). macOS on the next signed build.
- **Tests:** the overhead for one vs several semesters.

**Outcome (2026-11-05, Greg's choices after a mock on the UBC data).** The plan's sums were wrong: the gap between
side-by-side tiles (`TilePaddingHorizontal`) was 8 px, not 3, and a selected line's 3 px border was never counted, so
the real spare room was about 4 px (one semester) and 0 (several), not 8. The "safety margin" was really hiding a
measuring bug: labels were measured in the system default font but drawn in Inter (about 5% wider). The flag icon
(15 px) was never counted, so a selected, flagged line in a narrow tile was cut off with "…" (Greg confirmed it in
the app). Built:

- **Measuring uses the drawn font** (`MeasureTextWidth`/`MeasureLineHeight` take the view's font family).
- **`TileChrome`** (`Views/GridView/TileChrome.cs`) works out each tile's width and height from its parts: gap,
  borders (plain, semester colour, overlay, dual overlay), paddings, room for the selection border, the flag icon per
  flagged line, and a 1 px measuring margin. The drawing reads the same values, all in `AppLayout.axaml`.
- **Tile gap 8 → 4 px within a day; 16 px between days** (as in 1.2.3; its own resource, `DayEdgeGap`, after
  Greg's eye check). The 4 px left unused at the right of each column is gone. Room/Slot Browser ghosts now span
  the same edges as the day's tiles. Tile heights now count the tile's own top and bottom border and padding (the old 1 px per
  line over-measure was hiding that; heights are within 2 px of 1.2.3).
- **Widths at text size 11** (1.2.3 → now): 400-level CIVL + MECH 2,471 → 2,389; 300-level CIVL + ELEC + MECH
  2,751 → 2,644; with Winter Terms 1 + 2 shown 2,723 → 2,704. The spec's 2,642 px was measured at size 12
  (2,639 → 2,561; the other slice 2,936 → 2,836).
- **Checked:** no cut-off text, across or at the bottom, at every Text: size, one and two semesters, with lines
  selected and flagged (`tools/TileWidthProbe`, a regression check on a copy of the UBC database). WASM demo checked
  by eye at sizes 8, 11 and 12, including a selected, flagged line in a side-by-side tile. macOS on the next signed
  build.
- **Left as is:** the WASM height fudge `TileHeightMarginPerEntry` may have had the same font cause.

### Task 2: Real day numbers on columns (no visible change)

- `GridDayColumn` gains `DayNumber`; `UpdateDisplayProperties` sets it.
- `ScheduleGridView.axaml.cs`: replace `clickDay = (d / semCount) + 1` and `(ghost.Day - 1) * semCount` with
  lookups on `DayNumber` (one small helper that maps a day to its columns). Check the conflict-line code and the
  Room Browser and Slot Browser callers for the same assumption.
- **Tests:** the helper with a gap in the week (Tuesday missing), with Saturday on, and with several semesters.

**Outcome (2026-11-05).** `GridDayColumn.DayNumber` plus `GridData.DayOfColumn` / `FirstColumnOfDay`; clicks,
Access-watch conflict marks and lines, and Room/Slot Browser ghosts now use them. This fixed a live bug: with Sunday
on and Saturday off, Sunday tiles were treated as Saturday (right-click menu, conflict marks) and Sunday ghosts were
dropped. The remaining position arithmetic only groups one day's semester sub-columns (commented in the view). The
browsers, list view models and meeting rules already used real day numbers. Tests: `GridDayNumberTests` (gap,
Saturday, Sunday without Saturday, two semesters, and the real view model with Sunday on). No visible change with
Mon–Fri or Mon–Sat shown (by code reading; the width probe was not re-run).

### Task 3: Fold state and commands (no UI)

- `ScheduleGridViewModel`: `FoldedDays`, `ToggleDay`, `ShowOnlyDay`, `ShowPattern`, `ShowAllDays`, the last-open-day
  rule, and the cached blocks so a fold rebuilds `GridData` without reloading.
- `GridDayColumn.IsFolded`; `FoldedDaySummary` (section count, "has other things", section meetings with times).
- Resets: in the semester-change branch of `ReloadCore`, on `FocusService.FocusChanged`, and dropping days that
  aren't shown.
- `FoldedDaysCaption` for export.
- **Tests:** everything in Phase 5's VM list.

**Outcome (2026-11-05).** In `ScheduleGridViewModel`: `FoldedDays`, `ToggleDay`, `ShowOnlyDay`, `ShowPattern(days)`,
`ShowAllDays`, `CanFold` (false for the last open day, a hidden day, or a day already folded) and `FoldedDaysCaption`.
A fold rebuilds `GridData` from the last reload's inputs, never the database; everything runs on the UI thread, so it
can't race a reload. Folded columns have no tiles; the first sub-column of a folded day carries `FoldedDaySummary`
(section count, "has other things", section meetings with start times). Resets: semester switch, focus change, and
no semesters shown; Saturday/Sunday turned off drops out (and the week opens up if nothing would be left open).
Opening any database builds a new view model, so it starts unfolded. The shown days are read from `GridData`, so
task 4 should use `GridData.DayColumns`. Tests: `GridFoldDaysTests` (22). The block patterns for the menu are not
read yet (task 4).

### Task 4: Strips and header controls (AXAML edits)

- Renderer: skip measuring folded columns, strip width, the hatch strip with letter, count and "other" sign, the
  tooltip, header hover (fill, fold icon, hand pointer), header and strip clicks.
- `ScheduleGridView.axaml`: the header right-click popup beside `TileContextPopup`, with "Show only …", the block
  patterns and "Show all days" (**AXAML edit**).
- App.axaml / AppColors.axaml: strip padding, hatch, mark height, the fold icon `Path` (**AXAML edit**).
- **Check by eye** on the real slices: MWF and TR fit (or nearly) on one monitor; nothing folded looks like 1.2.3.

**Outcome (2026-11-05).** Click a header to fold its day (hover: fill, fold icon, hand pointer; the last open day
does nothing), click a strip to open it, right-click either for "Show only …", one "Show <pattern>" per block pattern
(read when the menu opens; patterns with no shown day are left out) and "Show all days" (only when something is
folded). Strips (after Revision 1): a folded page (light face, shadowed face, crease), letter, section count, flat
"B2" bands wherever sections meet (`FoldedDayStrip.MergeSpans`: overlaps merge, touching spans keep a hairline),
other things as narrow bars on the crease in their tiles' colours, and a tooltip. New: `FoldedDayStrip` (strip width), `DayHeaderMenuViewModel` (the menu, exposed as
`ScheduleGridViewModel.DayHeaderMenu`), resources in AppLayout/AppColors, `FoldDayIcon` in Icons.axaml. The view
model now takes `IBlockPatternRepository` (both service setups already register it). Strips are about 21–27 px. Open point: the meeting bar uses the pale `MeetingTile` fill and is barely visible.
UBC 400-level CIVL + MECH at size 11: MWF 1,599 px and TR 990 px with one semester (2,419 unfolded); 1,790 / 1,115
with two. Nothing folded renders exactly as before (probe, byte-identical PNGs against the previous commit). Checked
by eye in the WASM demo: fold, unfold, the menu, a pattern, the last-open-day rule, Show all days. Not yet checked
in the desktop app. Ghosts on a folded day are skipped until task 5 makes them marks. (The unfolded width grew from
2,389 to 2,419 because two MWF sections are now flagged in the live UBC database, not because of a code change.)

### Task 5: Marks and the flash

- `FoldedDayMarks` and `StripAttention` (pure classes) with their tests from Phase 5.
- View: draw marks on strips after `Render`, `UpdateSelectionHighlight` and `RenderGhostOverlay`; ghosts and
  conflict ends on folded days become marks; run the flash for the days `StripAttention` returns.
- `FoldedDayFlashBrush` and the timing as resources (**AXAML edit**).
- **Check by eye:** the four stories in Phase 3 (selection, a lab day, a ghost when scrolled down, a filter).

**Outcome (2026-11-05).** Built to Revision 2. `FoldedDayMarks` (pure): a folded day plus the pointers (`MarkInputs`:
selection, ghosts, conflicts) gives attention marks; `MarkKind` (Selection, Proposal, Conflict) with one table of
brush key, tooltip wording and priority, so the Slot Browser adds a row, not strip code; overlapping marks resolve to
the highest priority. `GhostBlock` gained `Kind` (default Proposal). `StripAttention` (pure) returns the days whose
marks gained something new. The view redraws only the strip marks after a selection change (no full render), and
after the ghost overlay; ghosts and conflict ends on folded days become marks. The flash is an amber overlay pulsed
twice (about 1 s, resources), click-through. Tests: `FoldedDayMarksTests` (25). Checked by eye in the WASM demo:
selecting a Mon/Wed section with Monday folded marks and flashes the M strip; folding Wednesday with it selected
shows the mark (no flash seen). Conflict marks were not seen on real data (no watches in the probe or demo). Small
points: the mark bars stick out a pixel or two past the strip edges; stepping through Room Browser slots flashes the
strip at every step (each slot is a new mark); a full redraw within a second of a flash cuts it short.

### Task 6: Export and release check

- PNG export: append `FoldedDaysCaption` to the title banner.
- The WASM demo: fold, unfold, a pattern, a selection mark (no new DI services, but check `ConfigureDemoServices`
  anyway, per CLAUDE.md).
- The full test suite.
- Docs: the 1.2.4 summary item 2.13, repo `HANDOFF.md`, and the user-facing help if it lists the Schedule view's
  controls.

**Outcome (2026-11-05).** The PNG title is now built by the pure `ScheduleGridViewModel.ComposeExportTitle(unit,
semesterLine, foldedDaysCaption)`: parts joined with " — ", empty parts skipped, so a folded export reads
"FASC — 2026-2027 — Winter Term 1 — Tue, Thu only". `ExportTitleTests` (5). Checked with `tools/TileWidthProbe` (new
scenario 7 calls the real `ExportToPng` on a copy of UBC1-TT.db: slice 1, TR open, an MWF section selected, a ghost on
Wednesday 19:00): in the PRINT palette the strips, letters, counts, remnant bands, the selection bar and the ghost bar
are all readable, and the title carries the caption. No flash overlay is in the picture: the export's own `Render`
cancels a running flash and no flash starts while exporting. Release check: nothing in tasks 1-6 touches
`AppSettings`, a repository, a model or a `PersistedRecord` (`MinimumVersionToWrite` stays No, untouched); the only new
constructor dependency, `IBlockPatternRepository`, is registered in both `ConfigureServices` and
`ConfigureDemoServices` (`DemoBlockPatternRepository`); both targets compile. Help: a "Folding days" section in
`navigating.html` (the only listed topic about the Schedule view; `schedule-grid.html` is not in the topic tree) and
one sentence in `exporting.html`. The WASM demo was checked by eye in task 4/5 and not re-run. Not checked: the desktop
app by eye, macOS.

### Pointers for later phases (not decisions)

- Later: the Slot Browser could fold the grid to the days of the pattern it's searching.
- Mocks from the real data (local only, ignored `tmp/` on GregDesktop): `tmp/monday_tile_layouts.html`,
  `tmp/week_fit_mock.html`, `tmp/week_more_mock.html`.
