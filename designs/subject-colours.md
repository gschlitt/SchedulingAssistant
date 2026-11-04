# Colour by Subject — Progressive Design Spec

**Status:** ✅ Phase 1 (Problem Statement) complete 2026-11-03: Greg's starting decisions **G1–G7** and the answers
to **Q1–Q9** are recorded below.
✅ **Phase 2 (Domain Model)** complete 2026-11-03 (P2-1–P2-4 settled).
✅ **Phase 3 (UX Sketch)** complete 2026-11-04 (P3-1–P3-7 settled).
✅ **Phase 4 (Data Design)** complete 2026-11-04 (P4-1–P4-5 settled).
✅ **Phase 5 (Architecture)** complete 2026-11-04 (P5-1–P5-5 settled).
✅ **Phase 6 (Implementation Plan)** signed off 2026-11-04. **Spec locked; next: task 1 (foundations).**
**Target release:** 1.2.4. **Replaces** summary item 2.3, "Section-type colours" (Q1).

---

## Phase 1: Problem Statement

### The problem

Since Load by subject (1.2.3), a scheduler typically has several subjects in focus at once: a dean's office
running BIOL, CHEM and PHYS together, or a department with two or three subjects. In the Schedule view every tile
looks the same: bold black "BIOL 105 AB1" on the same pale blue tile fill. Telling the subjects apart means reading
every label. In the Section list the card headings are the same: all bold black text.

The user wants to see at a glance which subject a section belongs to, in both views, without losing the dense
one-line layout.

### Stories

**Today.** Alice schedules BIOL, CHEM and PHYS for the Faculty of Science. She opens Fall 2026 in the Schedule view
and wants to know whether Tuesday morning is crowded with Chemistry. Every tile reads "XXXX 1nn AB1" in the same bold
black on the same pale blue tile. She reads each label in turn.

**With subject colours.** Alice opens Courses → Subjects and clicks **Suggest colours**. BIOL gets a pale green,
CHEM a pale amber, PHYS a pale blue. She doesn't like the amber, clicks CHEM's swatch, and picks a pale pink in the
colour chooser. Back in the Schedule view, every "CHEM 1nn AB1" sits on pink, so Tuesday morning's Chemistry load
shows at a glance. The section list headings carry the same colours.

**One subject only.** Bob, in History, has only HIST in his database. He never visits the colour settings, and
nothing changes for him. If he did set a colour for HIST and picked "Only when more than one subject is shown", he
would still see no colours, because only one subject is on screen.

**Multi-semester mode.** Alice shows Fall and Winter together. Semesters are told apart by **borders**, exactly as
today: each Schedule view tile has a 3 px border in its semester's colour, and each section card a coloured left
border. The subject colour sits only behind the "CHEM 105 AB1" text, inside the tile or card, so both readings
survive: border = semester, chip = subject.

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
- **Clutter:** the Schedule view already uses colour for the tile fill (one pale blue for every tile, `TileFill`),
  semester borders (multi-semester mode), the red overlay border, the "Emphasize unstaffed" row fill, the conflict
  glow and flags. A subject chip that looks like one of these misleads (Q8).
- **Width:** padding around the coloured text in a tile widens the measured label, which feeds the grid's
  column-width calculation (`ScheduleGridView.axaml.cs`, `MeasureTextWidth`). Small, but it must be measured, not
  guessed.
- **Performance:** one extra brush per card and per tile entry. Fine if set directly; not if done with a style class
  per subject.

---

## Phase 1 decisions (all settled 2026-11-03)

| # | Question | Decision |
|---|---|---|
| Q1 | Does this replace section-type colours (summary item 2.3)? | **Yes.** If section-type colours come back later, they need a different channel (for example a thin left stripe), decided then. |
| Q2 | What does "more than one subject is shown" count? | Subjects **in the focus** that have sections in the open semester(s), not what the grid filters leave on screen. Changes only when the focus or semester changes, so colours never flicker. With focus All, every subject with sections in the open semester(s) counts. |
| Q3 | What is a colour tied to? | The subject's **calendar abbreviation** ("BIOL"), case-insensitive, for every database on this computer. Survives restored copies and mapped-drive vs network paths. Renaming an abbreviation in TermPoint's subject editor carries its colour over; a rename this app doesn't see drops it. |
| Q4 | Colours are personal (G5): acceptable for colleagues and second computers? | **Yes.** **Suggest colours is deterministic**: the same subjects always get the same suggestions, so two people who both click Suggest see matching colours. |
| Q5 | What does Suggest colours touch? | It works in the Courses flyout on **all subjects in the database, regardless of focus**, and **fills only subjects without a colour**, steering away from colours already chosen. A separate **Clear colours** resets them all. Suggestions are edits like any other: the user can change any of them. |
| Q6 | A colour too dark for bold black text? | A short note beside the swatch: "Black text may be hard to read on this colour." The colour and black text are kept; the text never switches to white. |
| Q7 | Which views, and which text? | The Section list card heading and the Schedule view tile (G7). In a tile, the colour sits behind **the section text only** ("BIO 105 AB1"); instructor initials and frequency notes stay on the tile's normal background. Other views can follow later. |
| Q8 | How does the chip sit with the tile's other colours? | The chip draws on top of the row's background and keeps the strikethrough (staffed, under "Emphasize unstaffed") and the selection weight. Suggestions avoid hues close to the overlay red, the conflict glow and the flag colours, and stand clearly apart from the pale blue tile fill (`TileFill`), so a chip never disappears into its tile. |
| Q9 | The display option (G6): where, and which choices? | Directly under the Subjects list in the Courses flyout, shown once at least one colour is set: **Always**, **Only when more than one subject is shown** (default), **Never**. "Never" hides colours without losing them. Stored in AppSettings with the colours. |

### Carried into Phase 3 (UX sketch): all resolved

Resolved by Phase 2 rule 4 (alphabetical order, colours repeat in palette order) and Phase 3 P3-1 (Colour column in
the Subjects list) and P3-5 (a note when colours repeat).

- **More subjects than distinct colours.** Only about 10–12 light colours stay clearly apart, and Q5 colours every
  subject. Phase 3 decides what Suggest does past that point: reuse colours in a fixed order (most likely), and
  whether to say so.
- **Where the swatch sits.** G1 puts it in the subject editor; Phase 3 decides whether the Subjects list also shows
  each subject's swatch (needed to see a suggested set at a glance), and where Suggest and Clear sit.
  **P2-1 adds a constraint:** the colour controls must work while subject editing is locked (read-only database),
  so they can't live only inside the subject editor.
- **What "deterministic" is based on** (Q4): for example the subjects' abbreviations in alphabetical order, so adding
  a subject doesn't reshuffle colours already given.

---

## Phase 2: Domain Model

Settled 2026-11-03. What the user owns, what the app works out from it, and the rules that tie them together. No
code yet. The decisions are in *Phase 2 decisions* at the end.

### Stories that shape the model

**Read-only Alice.** Bob holds the write lock, so Alice's TermPoint is read-only. Subject editing is greyed out for
her today (`SubjectListViewModel.IsWriteEnabled` follows the lock). She still wants colours to tell BIOL from CHEM
while she looks. Colours are hers alone and never touch the database, so she can set them
(P2-1).

**Two databases, one BIOL.** Alice also opens the Faculty of Health database, which has its own BIOL. Because a
colour belongs to the abbreviation (Q3), BIOL is pale green there too, with no extra work. If Health's BIOL meant
something else, it would still be green. That is accepted.

**A rename.** In the Science database Alice renames PHYS to PHYX. PHYX keeps PHYS's pale blue. The Engineering
database still has a PHYS subject, and it stays pale blue as well, because the rename copies the colour rather than
moving it (P2-3).

**Clear colours.** In the Science database Alice clicks **Clear colours** to start over. BIOL, CHEM and PHYX lose
their colours. Her Health database's NURS keeps its colour, because Clear only touches the subjects in the database
she is looking at. Health's BIOL does lose its colour, since it shares the abbreviation (P2-2).

**An import.** Carol imports sections that bring a new subject, MICR. If MICR already has a colour on her computer, it
shows straight away; otherwise it has none until someone picks one or clicks Suggest.

### What the user owns (stored in AppSettings, per computer user)

| Concept | What it is | Notes |
|---|---|---|
| **Subject colour** | One pair: subject abbreviation → colour. | Kept as a set, at most one colour per abbreviation. The abbreviation is trimmed and compared ignoring case, the same rule the database uses for uniqueness (`SubjectRepository.ExistsByAbbreviation`). |
| **Colour** | An opaque RGB value. | No transparency (P2-4): a see-through colour would blend with whatever is behind it (the tile fill, an "Emphasize unstaffed" row, the conflict glow) and change from line to line, so readability could no longer be checked. |
| **Display mode** | Always / Only when more than one subject is shown / Never. | One setting for the computer user, not per database. Default: Only when more than one. It is kept even when no colours exist, but is offered only once one does (G6). |
| **Suggestion palette** | A fixed, ordered list of light colours chosen in advance. | Not user data. It is part of the app, ordered so that each colour is as different as possible from those before it. That makes Suggest deterministic (Q4). Exact colours are decided in Phase 4. |

### What the app works out

| Concept | Rule |
|---|---|
| **Subjects shown** | The number of distinct subjects among the sections in the open semester(s) whose subject is in the focus (Q2). Focus All: all those subjects. Focus None: zero. The grid filters don't change it. |
| **A section's colour** | Section → its course → the course's subject → the abbreviation → that abbreviation's colour, if any. If any link is missing (no subject, no colour), the section has none. |
| **Colours on?** | Off if no subject colours exist (G4). Otherwise: Always → on; Never → off; Only when more than one → on when subjects shown ≥ 2. |

### Rules (invariants)

1. **Nothing is set, nothing is shown.** With no subject colours, both views look exactly as they do today,
   whatever the display mode.
2. **Never in the database.** Subject colours and the display mode are never written to the database. Setting them
   needs no write lock, no save and no `MinimumVersionToWrite` question.
3. **Both views agree.** "Colours on?" and each section's colour are worked out once and used by the Section list
   and the Schedule view alike. One never shows colours while the other doesn't.
4. **Suggest never overwrites.** Suggest gives colours only to subjects in this database that have none (Q5), in
   alphabetical order of abbreviation. Each gets the first palette colour that is not too close to a colour already
   in use. When the palette runs out, colours repeat in palette order (Phase 3 decides whether to say so). The same
   subjects and colours in give the same suggestions out.
5. **Clear touches only this database's subjects** (P2-2). Because colours belong to abbreviations, a
   subject with the same abbreviation in another database loses its colour too.
6. **Deleting a subject leaves its colour.** Another database may use the abbreviation, and re-creating the subject
   brings the colour back. A leftover entry costs a few bytes.
7. **A rename in TermPoint copies the colour** to the new abbreviation (P2-3). If the new abbreviation
   already has a colour of its own, the renamed subject keeps the colour it had, because that is what the user was
   looking at. A rename TermPoint doesn't see (another computer, an import) carries nothing.
8. **A colour change shows at once** in both views, with no reload, the same way other live settings work
   (`AppSettings.SettingChanged`).

### Edge cases

- **Two TermPoints open on one computer** (two databases). Each holds its own copy of the settings and writes the
  whole file when it saves (`AppSettings.Save`), so the last one to save wins: a colour set in one can be lost when
  the other saves. The remembered focus already has the same weakness. Phase 4 decides whether colour saves re-read
  the file first.
- **The browser demo.** Settings are not kept there (as with the focus), so colours last only for the visit.
- **A course whose subject was deleted.** No subject, so no colour (see *A section's colour*).
- **Licence expired, or read-only mode.** Same as read-only Alice: personal settings stay editable (P2-1).

### Phase 2 decisions (settled 2026-11-03)

| # | Question | Decision |
|---|---|---|
| P2-1 | Can colours be set while the database is read-only (another user holds the lock, reader mode, licence expired)? Subject editing is disabled then. | **Yes.** Colours are personal and never saved to the database. This means the colour controls must not sit only inside the subject editor (G1), which is locked then. Phase 3 places them. |
| P2-2 | What does **Clear colours** remove? | **Only the colours of this database's subjects.** Clearing everything on the computer would wipe other databases' colours without warning. |
| P2-3 | What does renaming an abbreviation do to its colour? | **Copy it** to the new abbreviation and keep the old one, so other databases still using the old abbreviation keep their colour. |
| P2-4 | Can a colour be partly transparent? | **No, opaque only.** The colour chooser's transparency control is hidden, or ignored if it can't be hidden. |

---

## Phase 3: UX Sketch

Settled 2026-11-04. The decisions are **P3-1 to P3-7** at the end.

### Stories

**Alice sets colours for the first time.** She opens the Courses flyout. Under **Subjects**, the list now has a narrow
**Colour** column. Every swatch is empty (a dashed outline). Below the list, beside Add / Edit / Delete Subject, are
**Suggest colours** and **Clear colours**. She clicks Suggest colours: every empty swatch fills at once, BIOL green,
CHEM amber, PHYS blue. A new line appears under the buttons: *Show subject colours:* **[Only when more than one
subject is shown ▾]**. She closes the flyout; both views already show the colours.

**Alice changes one.** She clicks CHEM's amber swatch. The same colour chooser the semester colours use opens, with a
**No colour** button under it. She drags to a pale pink; the swatch follows as she drags. When she clicks away, the
chooser closes and the Section list and Schedule view repaint once.

**Alice picks something too dark.** She tries a deep navy. Under the chooser a line appears: *"Black text may be hard
to read on this colour."* She can keep it; nothing stops her.

**Read-only Alice.** Bob holds the lock. Add / Edit / Delete Subject are greyed out as today, but the swatches,
Suggest, Clear and the display choice all still work (P2-1).

**A big database.** Carol's dean's-office database has 18 subjects. Suggest colours fills all 18. The palette has 12
colours, so six subjects share a colour with another. Under the buttons: *"Some subjects share a colour: there are
more subjects than clearly different colours."* Carol changes the two that matter to her.

**Clear colours.** Alice clicks Clear colours. A small confirmation: *"Remove the colours of all 3 subjects in this
database? You can't undo this."* **Remove colours** / **Cancel**. After Remove, the swatches are empty, the display
choice disappears (no colours left), and both views look as they did before.

### Courses flyout (Subjects section)

```
Subjects
┌──────────────────┬──────────────────────┬────────┐
│ Name             │ Calendar Abbreviation│ Colour │
├──────────────────┼──────────────────────┼────────┤
│ Biology          │ BIOL                 │ [████] │  ← click opens the colour chooser
│ Chemistry        │ CHEM                 │ [████] │
│ Physics          │ PHYS                 │ [╌╌╌╌] │  ← dashed = no colour
└──────────────────┴──────────────────────┴────────┘
[Add Subject] [Edit Subject] [Delete Subject]   [Suggest colours] [Clear colours]
Show subject colours: [Only when more than one subject is shown ▾]     ← only once a colour exists
Some subjects share a colour: there are more subjects than clearly different colours.   ← only when true
```

- **The swatch column** is the only place colours are set. It works whatever the lock state. The subject editor
  (Name, Abbreviation) is unchanged, which refines G1: colours sit with the subject, in its row, rather than inside
  its editor (P3-1).
- **The chooser** is the semester one (`SemesterManagerView.axaml`: spectrum, slider, palette; no transparency,
  P2-4), plus **No colour** and the too-dark note underneath.
- **Suggest colours** is enabled when at least one subject has no colour; **Clear colours** when at least one has
  one.
- **Display choice:** Always / Only when more than one subject is shown (default) / Never (Q9).

### Section list card

Row 1 today: `▸ BIOL 105 AB1   JRS   Lab   30`. With colours on, the heading text sits on a chip of its subject's
colour:

```
▸ ▐BIOL 105 AB1▌  JRS   Lab   30
```

- The chip is exactly the heading's text plus a little padding left and right (about 3 px), the height of the text
  line, with slightly rounded corners (P3-3). It does not change the card's height.
- It stays the same when the card is selected, expanded for editing, highlighted by the filter or flagged.

### Schedule view tile

A **tile** is one block in the Schedule view holding one or more co-scheduled sections, one line each. Every tile
has the same pale blue fill (`TileFill`); in multi-semester mode its 3 px border carries the semester colour.

```
┌─────────────────────────┐
│ ▐BIOL 105 AB1▌ JRS      │
│─────────────────────────│   ← co-scheduled sections, each line has its own chip
│ ▐CHEM 110 A▌ MKL (odd)  │
└─────────────────────────┘
```

- The chip covers "BIOL 105 AB1" only (Q7); initials and the frequency note stay on the tile's normal fill.
- Everything else on the tile is unchanged: the common pale blue fill, the semester border (multi-semester mode),
  the red overlay border, the "Emphasize unstaffed" row fill and the conflict glow behind the chip. The
  strikethrough on staffed sections runs through the chip text too. Selection keeps its bolder weight.
- The chip's padding counts in the tile's width, so a tile is a few pixels wider than today when colours are on, and
  exactly as today when they are off.

### When colours appear and disappear

| Situation | Section list and Schedule view |
|---|---|
| No colours set | As today |
| Colours set, **Never** | As today |
| Colours set, **Always** | Chips on every section whose subject has a colour |
| Colours set, **Only when more than one**, focus has one subject with sections in the open semester(s) | As today |
| Same, focus changes to two subjects | Chips appear, without a reload |
| A subject has no colour while others do | Its sections have no chip |

### Phase 3 decisions (settled 2026-11-04)

| # | Question | Decision |
|---|---|---|
| P3-1 | Where is a colour set? G1 said "as part of editing a subject", but the editor is locked when read-only (P2-1). | **A Colour column in the Subjects list**, always clickable. The editor stays as it is. One place, works in every lock state, and shows the whole set at a glance. |
| P3-2 | When do the two views repaint while dragging in the chooser? | **When the chooser closes.** The swatch follows the drag live; the views repaint once. Repainting the grid on every drag step would stutter. |
| P3-3 | Chip shape. | **Slightly rounded corners (2 px), about 3 px padding each side**, sized to the text, if that is cheap per card (Phase 5 checks the control count); square corners otherwise. |
| P3-4 | Does Clear colours ask first? | **Yes, one plain confirmation** naming the count. Hand-picked colours can't be got back, and Suggest won't restore them. |
| P3-5 | Does Suggest say when colours repeat? | **Yes, one line under the buttons**, shown only while two subjects in this database share a colour. Disappears once they don't. |
| P3-6 | Should the subject's colour also show in the Courses list (each course row) or the course picker in the section editor? | **No, not now.** Only the two views in G7. |
| P3-7 | Where else is the display choice offered? | **Only in the Courses flyout.** No new item in Settings or on the toolbar. |

---

## Phase 4: Data Design

Drafted 2026-11-04. Nothing goes in the database: no table, no column, no `data` JSON field, no
`MinimumVersionToWrite` question. Everything lives in the user's `settings.json` (`AppSettings`). Decisions **P4-1 to
P4-5** are at the end.

### Stories

**Alice updates to 1.2.4.** Her `settings.json` has no colour keys yet. They read as "no colours, display mode Only
when more than one", so nothing changes until she sets a colour.

**Alice sets BIOL green.** TermPoint writes the settings file once (the same safe write-then-rename every setting
uses), and both views repaint.

**Alice has two TermPoints open** (Science and Health databases). She sets CHEM pink in Science. Later, Health saves
an unrelated setting (say, the remembered focus) and writes its own copy of the whole file, which never heard of
CHEM's pink. CHEM's colour is gone the next time she starts TermPoint. The remembered focus has the same weakness
today. P4-3 decides whether to close this gap now.

**Greg runs 1.2.3 and 1.2.4 on one computer** (development, or a direct-download build beside the Store one). 1.2.3
doesn't know the colour keys. When it saves any setting, it writes only the keys it knows, and the colours are gone.
Store users have one version per computer, so this is rare. P4-4 decides whether 1.2.4 at least protects keys from
*later* versions.

### What is stored (`settings.json`)

```json
{
  "SubjectColors": {
    "BIOL": "#B7E79D",
    "CHEM": "#EBB4E6"
  },
  "SubjectColorDisplay": 0
}
```

| Key | Type | Meaning | Missing or bad value |
|---|---|---|---|
| `SubjectColors` | object: abbreviation → `"#RRGGBB"` | One colour per abbreviation (Q3). Keys stored **trimmed and upper-case**, so `bio`, `Bio ` and `BIO` are one entry. Values opaque, six hex digits (P2-4). | Missing: no colours. An entry whose value doesn't parse is ignored (no colour for that abbreviation) and left in the file untouched. An `#AARRGGBB` value is read with its alpha dropped. |
| `SubjectColorDisplay` | number (enum `SubjectColorDisplay`) | 0 = Only when more than one subject is shown, 1 = Always, 2 = Never. Numbers, like the other enum settings (`FilterPassBehavior`), with explicit values so the order can never shift. | Missing or unknown number: 0, the default. |

Key names follow the code's spelling (`SemesterColor`, `HexColor`); users read "colour" (P4-1).

### The colour store (interface, no code yet)

One small service owns both keys; nothing else reads or writes them.

| Member | Purpose |
|---|---|
| `string? GetColor(string abbreviation)` | The colour for an abbreviation, or null. Normalises the key (trim, upper-case). |
| `void SetColor(string abbreviation, string? hex)` | Set, or (null) remove, one colour. Saves and raises the change event. |
| `void CopyColor(string oldAbbreviation, string newAbbreviation)` | For a rename in the subject editor (P2-3): copies the old colour to the new key, and keeps the old key. If the new key already has a colour, it is replaced by the renamed subject's colour, because that is what the user was looking at (Phase 2, rule 7). If the old key has no colour, nothing changes. |
| `void ClearColors(IEnumerable<string> abbreviations)` | Clears colours for this database's subjects (P2-2). One save. |
| `void ApplySuggestions(IEnumerable<string> abbreviations)` | Gives a colour to each abbreviation that has none, using the suggestion rule below. One save. |
| `bool HasAnyColor(IEnumerable<string> abbreviations)` | Whether any of this database's subjects has a colour: shows the display choice (G6) and enables Clear. |
| `SubjectColorDisplay Display { get; set; }` | The display mode. Setting it saves and raises the change event. |
| `event Action? Changed` | Raised on the UI thread after any change, through `AppSettings.RaiseSettingChanged`, so both views repaint once (Phase 2, rule 8). |

Every change is one in-memory update plus one `AppSettings.Save()` under the existing settings lock. A failed save
(disk full, file locked) leaves the in-memory colours in place for this session and is logged, the way every other
setting behaves.

### Suggestion rule (deterministic: Q4, Phase 2 rule 4)

A pure function, unit-tested on its own:

1. Input: this database's abbreviations that have no colour (alphabetical), and every colour currently used by this
   database's subjects.
2. For each abbreviation in turn, pick the first palette colour that is **not too close** to any colour already in
   use (distance in the OKLab colour space at least a threshold, tuned in Phase 6). It then counts as in use.
3. If no palette colour is far enough away, take palette colours in order, preferring ones not used at all, then
   repeating. This is the "some subjects share a colour" case (P3-5).

Same input, same output, on any computer.

### Draft palette

Ordered so that each colour is as far as possible from the ones before it (greedy, OKLab). Only light colours that
bold black text reads well on, kept away from colours the app already uses: the pale blue tile fill (`TileFill`
#cfecf8), the filter highlight (#9ecbf7), meetings (#bbaada), the overlay purple, the conflict red and the flag
colours. Blues are left out because the tile fill and the filter highlight are blue.

| # | Colour | Hex | Contrast with black |
|---|---|---|---|
| 1 | green | #B7E79D | 14.9 : 1 |
| 2 | orchid | #EBB4E6 | 12.2 : 1 |
| 3 | orange | #F8C58C | 13.4 : 1 |
| 4 | salmon | #F2A89A | 10.8 : 1 |
| 5 | yellow | #F4E98A | 16.9 : 1 |
| 6 | teal | #9EDCC8 | 13.5 : 1 |
| 7 | tan | #DCC6A0 | 12.6 : 1 |
| 8 | lavender | #D8C4EE | 13.1 : 1 |
| 9 | pink | #F5B0C8 | 11.9 : 1 |
| 10 | lime | #D4EC8E | 16.2 : 1 |
| 11 | grey | #D0D0D0 | 13.6 : 1 |

All are far above the 7 : 1 accessibility bar. Colours 9–11 sit noticeably closer to earlier ones, which matches
"about 10–12 clearly different colours" (Phase 1). The palette lives in `AppColors.axaml` as named colours
(CLAUDE.md: no colours in code), read in order by the store. Final hexes are checked by eye on real tiles in Phase 6.

### Too-dark warning (Q6)

Shown when a colour's contrast with black is below **7 : 1** (WCAG AAA for normal text; heading and tile text are
small). Example: mid-grey #808080 is 5.3 : 1 and gets the note; every palette colour passes.

### Phase 4 decisions (settled 2026-11-04)

| # | Question | Decision |
|---|---|---|
| P4-1 | The app's own words say "color" (the semester swatch tooltip is "Click to change color"). Which spelling do users see? | **"Colour"** in everything users read: *Suggest colours*, *Clear colours*, *Show subject colours:*, *No colour*, and the notes. Code names keep the codebase's "Color" (`SubjectColors`, `SubjectColorDisplay`). Existing "color" wording elsewhere in the app is not changed by this feature. |
| P4-2 | Accept the draft palette and its order for now? | **Yes**, as a starting point; Phase 6 checks it on real tiles and may swap a colour. |
| P4-3 | Close the two-TermPoints gap (a colour set in one is lost when the other saves)? | **Not in 1.2.4.** It is an `AppSettings`-wide weakness the remembered focus already has; fixing it for colours alone would add a second save path. Parked as **P12** in the 1.2.4 spec's parking lot (re-read and merge before saving). |
| P4-4 | Make `AppSettings` keep keys it doesn't know (`[JsonExtensionData]`), so a later version's settings survive 1.2.4? | **Yes.** A few lines, and it protects every future setting, not just colours. It can't protect 1.2.4's colours from 1.2.3, which is already released. Low risk: the file is per user, not the shared database. Needs a test that an unknown key survives a load and save. |
| P4-5 | Too-dark threshold. | **7 : 1** contrast with black. |

---

## Phase 5: Architecture

Settled 2026-11-04. Decisions **P5-1 to P5-5** are at the end.

### How a change flows (story)

Alice clicks CHEM's swatch and drags to pink. The swatch follows her (the row's colour is bound to the chooser). She
clicks away; the chooser closes and the row hands the colour to the **colour store**, which saves `settings.json`
and raises `Changed`. The **colour presenter** hears it, forgets its cached answers and raises its own `Changed`.
The Section list refreshes each card's chip colour in place (no reload); the Schedule view reloads its grid once.
Both asked the same presenter, so they agree (Phase 2, rule 3).

### New pieces

| Piece | Kind | Job |
|---|---|---|
| `SubjectColorDisplay` | enum (`Models/`) | OnlyWhenSeveral = 0, Always = 1, Never = 2 (Phase 4). |
| `AppSettings.SubjectColors`, `AppSettings.SubjectColorDisplay` | settings properties | The two stored keys (Phase 4). |
| `AppSettings.ExtensionData` | `[JsonExtensionData]` dictionary | Keeps keys this version doesn't know (P4-4). |
| `ColorMath` | static, pure (`Services/`) | Parses `#RRGGBB` without Avalonia, contrast with black, OKLab distance. Unit-tested. |
| `SubjectColorSuggester` | static, pure | The suggestion rule (Phase 4): abbreviations without colour + colours in use + palette → new assignments. Unit-tested. |
| `SubjectColorStore` | singleton service | The Phase 4 interface. Only code that reads or writes the two keys. Takes the palette as a list of hex strings in its constructor, so tests need no Avalonia. |
| Palette resources | `AppColors.axaml` | `SubjectPalette01` … `SubjectPalette11` (Phase 4 hexes). Read once at startup by the DI registration and passed to the store (CLAUDE.md: colours live in AXAML). |
| `SubjectColorPresenter` | singleton service | Answers the two questions both views ask: **are colours on?** and **what is this section's colour?** (`string ColorFor(Section)`, empty = none). Raises `Changed`. |
| `SubjectRowViewModel` | view model | One row of the Subjects list: the `Subject`, its live `ColorHex`, `IsTooDark`, and `Commit` / `ClearColor`. |
| `FlyoutClosedCommandBehavior` | attached behavior | Runs a command when a `Flyout` closes. Commits the chooser's colour (P3-2) without code-behind. |

### `SubjectColorPresenter`

- **Depends on** the store, `FocusService`, `SectionStore` (the open semesters' sections) and the course and subject
  repositories (course → subject → abbreviation).
- **Colours on?** As Phase 2: none set → off; Never → off; Always → on; OnlyWhenSeveral → on when the in-focus
  sections of `SectionStore.Sections` span two or more subjects (`FocusService.Matches`). Cached.
- **`ColorFor(section)`**: empty when colours are off; otherwise the store's colour for the section's subject
  abbreviation, or empty.
- **Invalidates** its caches and raises `Changed` on the store's `Changed`, `FocusService.FocusChanged` and
  `SectionStore.SectionsChanged`. It takes `FocusService` as a constructor dependency so that FocusService subscribes
  to `SectionsChanged` first (the ordering rule in `FocusService`'s comments). It raises `Changed` only when its
  answer could differ, so a section edit doesn't repaint every card for nothing.
- **No Avalonia types**, like `SemesterBrushResolver`'s rule for view models: it hands out hex strings; views turn them
  into brushes.

### Section list

- `SectionListItemViewModel` gains `SubjectChipColor` (observable string, empty = none), set when the card is built
  and by `RefreshSubjectChip()`.
- `SectionListViewModel` subscribes to `SubjectColorPresenter.Changed` and calls `RefreshSubjectChip()` on every
  card, the way it already refreshes property lines for `SectionConflictDisplay`. A focus change already reloads the
  list, so it needs nothing extra.
- **View:** the existing heading `TextBlock` gets `Background="{Binding SubjectChipColor, Converter=HexToBrushConverter}"`
  (already exists, transparent for empty) and `Padding="3,0"`, with its margin cut by 3 px each side (`2,0,4,0` →
  `-1,0,1,0`). The text sits exactly where it does today, chip or not. **No new control on the card** (item 21),
  one binding more. `TextBlock` has no corner radius, so card chips are square (P3-3 fallback; P5-1).

### Schedule view

- `SectionMeetingBlock` gains `string SubjectColor = ""`, filled by `ScheduleGridViewModel` from the presenter in all
  three places it builds a block (filtered pass, overlay pass, and the third, ~line 1074). The tile entry it groups
  into carries it through.
- `ScheduleGridViewModel` subscribes to `SubjectColorPresenter.Changed` and reloads.
- **View (`ScheduleGridView.axaml.cs`):** a line with no subject colour is built exactly as today (one `TextBlock`).
  A line with one becomes the chip `TextBlock` (section text, background, 3 px padding) followed by the existing
  trimming `TextBlock` for initials and frequency note, so trimming eats the initials before the section text.
  Width measurement adds the 6 px of padding when the chip is there. `EntryRowInfo` gains the chip `TextBlock` so
  selection restyles both. Brushes come from a small per-hex cache so a reload doesn't allocate one per line.
- Square corners, like the cards (P5-1).

### Courses flyout

- `CourseListViewModel.Subjects` becomes `ObservableCollection<SubjectRowViewModel>`; `SelectedSubject` becomes the
  selected row's `Subject` (P5-3).
- **Colour column:** a `DataGridTemplateColumn` with the semester swatch pattern (`SemesterManagerView.axaml`):
  `ColorView` two-way bound to `ColorHex`, transparency hidden, plus a **No colour** button and the too-dark note
  (`IsTooDark`, from `ColorMath`, 7 : 1) under it. `FlyoutClosedCommandBehavior` runs the row's `Commit`.
- **Commands** `SuggestColoursCommand`, `ClearColoursCommand` (confirms through the existing
  `IDialogService.Confirm(message, "Remove colours")`), and a `SelectedDisplay` property for the display choice.
  **None of them check `CanWrite`** (P2-1), and nothing in the flyout disables them while read-only (only the
  course editor's own panel binds `IsWriteEnabled`).
- `HasAnyColour` (shows the display choice, enables Clear), `CanSuggest` (some subject has none) and
  `HasSharedColours` (the P3-5 note), all re-read from the store on its `Changed`.
- **Rename hook:** `EditSubject`'s save calls `store.CopyColor(old, new)` after a successful `Update` when the
  abbreviation changed (ignoring case).

### Registration

- `SubjectColorStore` and `SubjectColorPresenter` are singletons in **both** `ConfigureServices` and
  `ConfigureDemoServices` (CLAUDE.md gotcha). The presenter's registration resolves `FocusService` first, as the
  existing `SectionStore` consumers do (App.axaml.cs ~line 697).
- The demo keeps colours for the visit only, like its focus.

### What it doesn't touch

The database and its repositories, the save / lock / checkout code, `Subject`, `Course`, `Section`, and every
`PersistedRecord`. The four rules for the shared database are not engaged.

### Phase 5 decisions (settled 2026-11-04)

| # | Question | Decision |
|---|---|---|
| P5-1 | Square chips in both views (no extra control per card or line), or rounded (one `Border` per card heading and per coloured tile line)? | **Square in both**, for now. Consistent, and free on cards. If they look harsh, rounding is a contained change later. |
| P5-2 | `SubjectListViewModel` is registered in DI, but no view seems to use it (the Courses flyout has its own subject list). Leave it? | **Leave it** and give it no colour code. Phase 6's first task confirms it is unused; if so, add a parking-lot note to retire it. |
| P5-3 | Turn the Courses flyout's `Subjects` into row view models (changes `SelectedSubject`'s plumbing), or keep the models and look colours up through a converter? | **Row view models.** A converter can't hold the chooser's live, uncommitted colour, and it matches the semester list. |
| P5-4 | Anything that renders the Schedule view to an image or printout would show chips too. OK? | **Yes.** It shows what the user sees. |
| P5-5 | The Schedule view reloads its whole grid when colours change, rather than repainting chips in place. OK? | **Yes.** It happens once per chooser close, Suggest, Clear or display change, and reloads are already the path for focus changes. |

---

## Phase 6: Implementation Plan

Signed off 2026-11-04. Five tasks, each about one session, in this order. Each ends with the compile check, the
relevant tests, and a commit only when Greg asks. Tasks that touch `.axaml` files say so: **Greg closes those tabs in
VS first** (CLAUDE.md).

### Release facts

- **`MinimumVersionToWrite`: No.** Same database plus same edits saves exactly what 1.2.3 would; nothing new reaches
  the database. Nothing to set in `Services/MinimumVersionToWrite.cs`.
- **Four rules for the shared database:** not engaged. No save, lock, exit, switch, restore or recovery code changes.
- **Production risk:** low and local. The one change every user hits is `AppSettings` keeping unknown keys (task 1),
  which touches every settings save; it gets its own tests. With no colours set, both views must look identical to
  1.2.3 (checked in tasks 3 and 4).

### Order and why

Foundations first (no UI, all unit-tested), then the Courses flyout so colours can be set for real, then each view.
Each view task can be checked by eye with colours set through task 2.

### Task 1: Foundations (no UI)

- `Models/SubjectColorDisplay.cs` (explicit values 0, 1, 2).
- `AppSettings`: `SubjectColors`, `SubjectColorDisplay`, and `[JsonExtensionData] ExtensionData` (P4-4).
- `Services/ColorMath.cs`: parse `#RRGGBB` / `#AARRGGBB` (alpha dropped), contrast with black, OKLab distance.
- `Services/SubjectColorSuggester.cs`: the Phase 4 rule.
- `Services/SubjectColorStore.cs`: the Phase 4 interface; palette passed in as hex strings.
- `AppColors.axaml`: `SubjectPalette01`–`SubjectPalette11` (**AXAML edit**).
- DI: store as a singleton in `ConfigureServices` **and** `ConfigureDemoServices`, palette read from resources.
- **Confirm `SubjectListViewModel` is unused** (P5-2); if so, add a parking-lot line to retire it.
- **Tests:**
  - `ColorMath`: known contrast values (#808080 ≈ 5.3 : 1); every palette colour ≥ 7 : 1.
  - Suggester: fills only blanks; deterministic; keeps away from colours in use; repeats past the palette.
  - Store: keys normalised (`bio`, `Bio `, `BIO` are one entry); `CopyColor` keeps the old key and overwrites the
    new one; `ClearColors` touches only the given abbreviations; a bad value is ignored and kept in the file;
    missing keys read as no colours and OnlyWhenSeveral.
  - `AppSettings`: an unknown key survives a load and save; a 1.2.3-shaped file loads unchanged.

### Task 2: Courses flyout (setting colours)

- `SubjectRowViewModel` (`Subject`, `ColorHex`, `IsTooDark`, `Commit`, `ClearColor`).
- `CourseListViewModel`: row collection for the Subjects grid, Suggest / Clear commands (Clear confirms with
  `IDialogService.Confirm(…, "Remove colours")`), display choice, `HasAnyColour`, `CanSuggest`,
  `HasSharedColours`, rename hook in `EditSubject`'s save. No colour command checks `CanWrite`.
  - **Implementation note on P5-3:** `CourseListViewModel.Subjects` is also read outside the grid (the tour's
    subject step in `TourActionDefinitions.cs` and three test files). Keep `Subjects` (the plain `Subject`
    records) as it is, and add `SubjectRows`: one `SubjectRowViewModel` wrapping each of the same subjects, in the
    same order, rebuilt whenever `Subjects` is. Only the Subjects grid binds to `SubjectRows`, so the other callers
    don't change. Selecting a row sets `SelectedSubject` to that row's subject. This is code-only: the user still
    sees one Subjects list. Same decision (row view models), smaller blast radius.
- `Behaviors/FlyoutClosedCommandBehavior.cs`.
- `CourseListView.axaml`: Colour column (swatch, chooser with transparency hidden, **No colour**, too-dark note),
  Suggest / Clear buttons, display choice, shared-colour note (**AXAML edit**). Wording: "colour" (P4-1).
- **Tests:** commands work while read-only (not the writer); Suggest fills blanks only; Clear asks first and
  clears only this database's subjects; renaming PHYS → PHYX copies the colour; the display choice appears only
  once a colour exists.
- **Check by eye:** set, change, clear, Suggest, Clear, too-dark note, read-only session.

### Task 3: Presenter and Section list chips

- `Services/SubjectColorPresenter.cs` (Phase 5), registered in both DI setups after `FocusService`.
- `SectionListItemViewModel.SubjectChipColor` + `RefreshSubjectChip()`; `SectionListViewModel` subscribes to the
  presenter.
- `SectionListView.axaml`: heading `TextBlock` background binding, `Padding="3,0"`, margin `-1,0,1,0`
  (**AXAML edit**).
- **Tests:** presenter on/off for each display mode × focus All / None / one subject / two subjects, and
  multi-semester; `Changed` fires on store change, focus change and section reload; `ColorFor` empty when off.
- **Check by eye:** no colours → headings pixel-identical to 1.2.3; chips with focus of one vs two subjects;
  selected, expanded, filter-highlighted, flagged and multi-semester cards; virtualized list on and off.

### Task 4: Schedule view chips

- `SectionMeetingBlock` / `TileEntry`: `SubjectColor`; `ScheduleGridViewModel` fills it in all three block builders
  and reloads on the presenter's `Changed`.
- `ScheduleGridView.axaml.cs`: split coloured lines into chip + rest, width measurement (+6 px), `EntryRowInfo`
  chip for selection restyle, per-hex brush cache. Uncoloured lines unchanged.
- **Tests:** blocks carry the colour when on and none when off (VM-level, no rendering).
- **Check by eye:** no colours → tiles identical to 1.2.3; co-scheduled tiles; narrow tiles trim initials first;
  multi-semester borders; overlay; "Emphasize unstaffed" (fill and strikethrough); conflict glow; selection; flags;
  font-size selector; the grid image export.

### Task 5: Palette tuning and release check

- Look at the palette on real tiles and cards (real-data database); swap any colour that reads badly and re-run the
  palette tests.
- Browser demo: colours work for the visit (DI parity).
- Full test suite with a `.trx`.
- Update `designs/spec_version_1.2.4_summary.md` item 2.3 and `HANDOFF.md`.

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
