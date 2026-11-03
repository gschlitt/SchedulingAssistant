# Colour by Subject — Progressive Design Spec

**Status:** Phase 1 (Problem Statement) drafted 2026-11-03. Greg's starting decisions are recorded as **G1–G7**;
open questions are **Q1–Q9**, each with a recommendation. Nothing is locked yet.
**Target release:** 1.2.4 (replaces or sits beside summary item 2.3, "Section-type colours"; see Q1).

---

## Phase 1: Problem Statement

### The problem

Since Load by subject (1.2.3), a scheduler typically has several subjects in focus at once: a dean's office
running BIOL, CHEM and PHYS together, or a department with two or three subjects. In the Schedule view every tile
looks the same: bold black "BIOL 105 AB1" on the semester's background. Telling the subjects apart means reading
every label. In the Section list the card headings are the same: all bold black text.

The user wants to see at a glance which subject a section belongs to, in both views, without losing the dense
one-line layout.

### Stories

**Today.** Alice schedules BIOL, CHEM and PHYS for the Faculty of Science. She opens Fall 2026 in the Schedule view
and wants to know whether Tuesday morning is crowded with Chemistry. Every tile reads "XXXX 1nn AB1" in the same bold
black on the same Fall background. She reads each label in turn.

**With subject colours.** Alice opens Courses → Subjects and clicks **Suggest colours**. BIOL gets a pale green,
CHEM a pale amber, PHYS a pale blue. She doesn't like the amber, clicks CHEM's swatch, and picks a pale pink in the
colour chooser. Back in the Schedule view, every "CHEM 1nn AB1" sits on pink, so Tuesday morning's Chemistry load
shows at a glance. The section list headings carry the same colours.

**One subject only.** Bob, in History, has only HIST in his database. He never visits the colour settings, and
nothing changes for him. If he did set a colour for HIST and picked "Only when more than one subject is shown", he
would still see no colours, because only one subject is on screen.

**Multi-semester mode.** Alice shows Fall and Winter together. Tiles keep their Fall and Winter backgrounds exactly
as today; the subject colour sits only behind the "CHEM 105 AB1" text, so both readings survive.

**A second computer, or a colleague.** Colours live in Alice's own settings file, not the database (G5). On her
laptop, or when her colleague Carol opens the same database, nobody sees Alice's colours until they set their own
(see Q4).

### Greg's starting decisions (given 2026-11-03)

| # | Decision |
|---|---|
| G1 | Colours are chosen per subject, in the **Courses flyout**, as part of editing a subject. |
| G2 | A **Suggest colours** button proposes colours chosen for maximum distinguishability; the user can accept or change them. |
| G3 | A colour is changed with a **colour chooser** (the app already uses Avalonia's `ColorView` in a flyout for semester colours, `SemesterManagerView.axaml`). |
| G4 | **No colours set → nothing is shown.** No subject has a default colour. |
| G5 | Colours are stored in **AppSettings** (the user's `settings.json`), not the database. |
| G6 | Once at least one colour is set, a display option is offered: **always** show colours, or show them **only when more than one subject is shown**. |
| G7 | **Display:** the colour is a solid background behind the section's identifying text ("BIO 105 AB1") in both the Section list heading and the Schedule view tile. Text stays bold black, so colours must be light enough to read it. Start with a solid block; gradient or shading may come later. Semester colouring is unchanged. |

### What "done" looks like

1. With no colours set, both views look exactly as they do today.
2. A user can set, change and clear a colour for any subject in the Courses flyout, and **Suggest colours** gives a
   set they can accept or tweak in a few clicks.
3. Wherever a coloured subject's section shows in the Section list or the Schedule view, its "SUBJ nnn CODE" text
   has that colour behind it, and the bold black text stays readable.
4. The "only when more than one subject is shown" option behaves predictably: colours don't flicker on and off while
   the user types in a filter (see Q2).
5. Nothing is written to the database, so there is no version gate and no `MinimumVersionToWrite` question.
6. Card and tile cost does not grow noticeably (CLAUDE.md: no class-conditioned styles on repeated elements; set the
   brush directly).

### Out of scope (for now)

- Gradients or shading (G7 says start with the block).
- Colouring other views: Workload view, Room browser, Slot browser, exports, the printed grid (see Q7).
- Sharing colours through the database.

### Risks

- **Data:** none to the shared database: nothing new is saved there, and the four rules for the shared database are
  untouched. `settings.json` is written through the existing locked `AppSettings.Save`.
- **Readability:** a dark user-picked colour behind bold black text (Q6).
- **Clutter:** the Schedule view already uses background colour for semesters, the red overlay, "Emphasize
  unstaffed", conflict glow and flags. A subject chip that looks like one of these misleads (Q8).
- **Width:** padding around the coloured text in a tile widens the measured label, which feeds the grid's
  column-width calculation (`ScheduleGridView.axaml.cs`, `MeasureTextWidth`). Small, but it must be measured, not
  guessed.
- **Performance:** one extra brush per card and per tile entry. Fine if set directly; not if done with a style class
  per subject.

---

## Open questions (with recommendations)

**Q1. Does this replace section-type colours (summary item 2.3)?**
Both would colour the same spot. Item 2.3 also stored its colour in the database, so it carries a version gate that
this feature avoids.
*Recommend:* subject colours **replace** 2.3 in 1.2.4. If section-type colours come back later, they need a
different channel (for example a thin left stripe), decided then.

**Q2. What does "more than one subject is shown" count?**
- (a) Subjects in the **focus** that have sections in the open semester(s). Changes only when the focus or
  semester changes.
- (b) Subjects among the sections **on screen right now**, after the grid's filters. Colours would switch on and off
  as the user filters.

*Recommend (a).* It answers "am I working with several subjects?", and it never flickers. With focus All, it counts
every subject with sections in the open semester(s).

**Q3. What is a colour keyed to?**
- (a) Subject **calendar abbreviation** ("BIOL"), case-insensitive, shared across all databases on this computer.
- (b) Shared database path + subject Id, like `FocusByDatabase`.

*Recommend (a).* BIOL is the same colour in every database, and the colour survives a restored copy, a mapped drive
versus a UNC path, and a re-imported database (all of which change (b)'s key). Cost: renaming an abbreviation drops
its colour unless the subject editor moves it (it should, when the rename happens in this app).

**Q4. Colours are personal (G5). Is that acceptable for colleagues and second computers?**
Carol sees none of Alice's colours, and Alice has to set them again on her laptop. **Suggest colours** makes this
quick, and if suggestions are deterministic (same subjects in, same colours out), two people who both just click
Suggest get the same result.
*Recommend:* accept, and make suggestions deterministic. Revisit database storage only if users ask for shared
colours.

**Q5. What does Suggest colours touch, and for which subjects?**
There are only about 10–12 light colours that stay clearly apart; a database with 30 subjects can't have 30.
*Recommend:* Suggest fills only subjects **without** a colour, keeps the user's own choices fixed and steers away
from them, and works on the subjects **in the current focus** (all subjects if the focus is All). A separate
**Clear colours** resets them all. Suggestions stay in the editor until the user clicks Apply, like any other edit.

**Q6. What happens when a user picks a colour too dark for bold black text?**
*Recommend:* a short note beside the swatch, "Black text may be hard to read on this colour", and nothing else.
Don't silently switch the text to white: that would make tiles inconsistent and fight the selection styling.

**Q7. Which views carry the colour?**
*Recommend:* only the two in G7 for now: the Section list card heading and the Schedule view tile label. In the tile
the colour sits behind "BIO 105 AB1" only, not the instructor initials or frequency note, matching G7's wording.
Workload view and the finders can follow if wanted.

**Q8. How does the chip sit with the tile's other colours?**
Tile states today: semester background, red overlay, "Emphasize unstaffed" background, strikethrough for staffed,
conflict glow, flag, selection (bold, black text).
*Recommend:* the chip draws on top of the row's background and keeps the strikethrough and selection weight.
Suggestions avoid hues close to the overlay red, the conflict glow and the flag colours, and stay lighter than the
semester colours in use so the chip reads as a label, not a tile.

**Q9. Where does the display option (G6) live, and is there an "off"?**
*Recommend:* directly under the Subjects list in the Courses flyout, shown only once a colour is set, as three
choices: **Always**, **Only when more than one subject is shown** (default), **Never**. "Never" lets a user hide
colours for a while without losing them. It is stored in AppSettings with the colours.

---

## Pointers for later phases (not decisions)

- `Subject` (`Models/Subject.cs`) stays unchanged: colours live outside the database.
- `AppSettings` already holds per-user display state (`FocusByDatabase`, `TileFontSize`) and has a `SettingChanged`
  event live views use to update immediately.
- Section card heading: `SectionListView.axaml`, the `Heading` TextBlock (SemiBold 12) in the card's row 1;
  `SectionListItemViewModel.Heading`.
- Schedule tile label: built in code in `ScheduleGridView.axaml.cs` (`BuildTileLabel`, `entryLabel`), fed by
  `SectionMeetingBlock.Label`. Colouring only the label part means splitting the text into runs or a bordered label
  plus the initials.
- Colour chooser pattern to reuse: the swatch + `ColorView` flyout in `SemesterManagerView.axaml`.
- The focus, for Q2: `FocusService.Current`.
- Light theme only (`App.axaml`), so one set of suggestions is enough.
