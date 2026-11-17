# Click-and-drag moving of meetings in Schedule View

**Status:** approved by Greg 2026-11-16 (wording, group rule, marks; Shift for free snapping; no undo; guard the other
reloads; the semester switch goes to the backlog). **Revised by Greg 2026-11-16 after trying it:** Alt is inverted;
a drag moves one meeting, and Alt brings along the section's other meetings at the same start (stories 2 and 3). Ledger entry: "Click-and-drag moving of
meetings in Schedule View" in [RELEASE_1.2.5.md](../../release_state/1.2.5/RELEASE_1.2.5.md); notes in
[click-drag-meetings/NOTES.md](../../release_state/1.2.5/click-drag-meetings/NOTES.md).

**Principle:** create and edit in the Section List; arrange and adjust in the grid. The grid only moves existing
meetings (day and start time). Sections, meeting lengths and everything else stay in the section card.

**Settled with Greg (2026-11-16):** while a section card is open in the list, grid edits (right-click edits and
drops) are refused with a message, the way opening a second card is refused today. Today's right-click bug is fixed in
this entry, as its first piece.

Wording in "quotes" below is approved wording (Greg, 2026-11-16); code comments guarding it say so.

## The stories

Alice is a department administrator on a busy Fall schedule with Schedule View and the Section List side by side.

### 1. A card is open (today's bug, and the guard)

Alice is halfway through typing a note in HIST 101 A's card. She notices BIOL 210 B is in the wrong room, right-clicks
its tile in the grid, picks Room, chooses B204 and confirms.

- **Today:** the room changes, and HIST 101 A's card silently closes. Her half-typed note is gone and nothing says so.
- **With the fix:** the right-click menu still opens, but Instructors, Room, Tags and Flag are greyed out, with one line
  at the top: "Finish editing HIST 101 A in the Section List first." She clicks Apply on the card, right-clicks again,
  and the edits are available. Mark (story 4) stays available, because it changes nothing.
- **Dragging with a card open:** Alice starts dragging a tile. The tile does not lift; the grid's message bar says
  "Finish editing HIST 101 A in the Section List first. Apply or Cancel it there, then drag again." The message goes
  away when the card closes.

### 2. Moving a Tuesday/Thursday section

CHEM 101 B meets Tuesday and Thursday at 10:00 for 1.5 hours. Alice wants it at 11:30.

- **Today:** she opens the card, changes both meetings' start times, and applies.
- **With the feature:** she holds **Alt** (Option on macOS), presses on the Tuesday tile and drags down. Once the
  pointer has moved a few pixels the tile lifts: a faint ghost of it follows on Tuesday **and** on Thursday (with Alt,
  every meeting of the section that starts at the same time moves with it), snapping to the year's legal start times for a 1.5-hour block. A small label beside
  the pointer says "Tue, Thu 11:30-13:00". She lets go: both meetings move, the section is selected in the list and its
  card summary shows the new times. One save, exactly as if she had edited the card.
- Holding **Shift** turns snapping off: the ghost moves in 5-minute steps, for the odd section that doesn't sit on a
  legal start time. Releasing Shift mid-drag snaps again.
- A group drag changes the time only; the ghosts stay on their own days even if the pointer wanders sideways.
- Pressing or releasing Alt mid-drag switches the ghosts live, so what she sees is always what will move. A held key,
  not a toggle, so there is no hidden mode left on.
- If Thursday is folded or scrolled out of sight, the label still names it, so she knows it moves too.

### 3. Moving just one meeting

Alice wants CHEM 101 B's Thursday meeting on Friday at 13:00 instead, leaving Tuesday alone.

- She drags the Thursday tile, no key held. Only one ghost shows; it can move to any shown day and time; the label
  says "Fri 13:00-14:30". She lets go: only that meeting moves.
- (First design: the group moved by default and Alt moved one alone. Greg inverted it after trying it, 2026-11-16:
  dragging what you grab is what people expect.)

### 4. Swapping two sections, with marks

Alice wants MATH 120 A (Mon/Wed 08:30) and MATH 120 B (Mon/Wed 14:30) to trade places.

- She right-clicks each and chooses **Mark**. Both sections' tiles get a bright frame on every day, like circling them
  on paper. Marks are never saved; they only help her keep the pair in sight on a crowded grid.
- She drags B to a free slot, then A into B's old slot, then B into A's old slot. Three ordinary drops; the marks keep
  both in view throughout. (No special swap gesture: dropping onto a tile does not swap, it just moves.)
- She right-clicks any tile, or empty grid, and chooses **Clear marks**. Marks also clear when she opens another
  database. They survive grid reloads and semester switches (they follow the section, not the tile).

### 5. Changing her mind mid-drag

Alice lifts a tile, then decides against it. She presses **Esc**, or lets go outside the grid: the ghost vanishes and
nothing is saved. Letting go exactly where it started saves nothing either.

### 6. A drop that can't happen

Alice drags PHYS 101 A's Monday meeting onto Wednesday at 10:00, where PHYS 101 A already meets.

- While the ghost overlaps another meeting **of the same section**, it shows in the warning colour and the label says
  "Overlaps its Wed 10:00 meeting". Letting go there does nothing.
- Folded days and hidden weekend days are not drop targets: the ghost stays on the nearest shown day.
- Clashes with **other** sections (room, instructor) are allowed, exactly as in the section card; the usual conflict
  warnings appear on the cards afterwards.

### 7. Read-only

Bob opens the database while Alice has it. His TermPoint is read-only: tiles don't lift and nothing in the grid
changes, as the right-click edits are already disabled for him.

### 8. Other places that close an open card

Alice has HIST 101 A's card open and, in the Courses panel, edits HIST 101 and clicks "Apply tags to all sections".

- **Today:** the tags are applied and her open card silently closes, losing what she typed.
- **With the fix:** the tags are not applied; where the result usually says "Tags applied to 4 sections." it says
  "Finish editing HIST 101 A in the Section List first."

### What is deliberately not there

- **Resizing a meeting** by its bottom edge: no resize cursor; pressing near the edge just drags the whole tile. Length
  changes stay in the card (they decide which start times are legal).
- **Creating by dragging** in an empty slot: nothing happens. New sections start in the list.
- **Moving between semesters:** a meeting stays in its own semester's sub-column.
- **Touch:** mouse and trackpad only in this release.
- **Undo:** none (Greg, 2026-11-16); the app has no undo anywhere today. Esc before letting go is the way out, and a
  wrong drop is fixed by dragging it back.

## Technical detail (compact)

- **The guard (first piece).** One check, used by both the right-click menu and the drag:
  `SectionListViewModel.IsEditing`, reached through a small interface (as `ImportSectionsHost.IsEditingSection`
  already does), plus the open card's heading for the message. The grid never calls `SectionStore.Reload` while a card
  is open. Every other caller of `SectionStore.Reload` was checked: the Course list's "Apply tags to all sections"
  gets the same guard (story 8); the two in `App` run only while a database is opening, before any card can exist;
  the list's own reloads are already blocked by `IsEditorBusy`, except the semester switch, which is a backlog
  line (Greg, 2026-11-16).
- **Move method.** A pure, unit-tested function: section + meeting index + new (day, start) + group flag → updated
  meeting list, or a refusal reason (self-overlap, no change). Saved through `_sectionRepo.Update` and the same
  `onSaved` reload as the right-click edits. If a fresh section object is built rather than edited in place, it calls
  `WithExtensionDataFrom(original)` and copies every persisted field (CLAUDE.md, Persisted records).
- **Meeting identity.** Carry the meeting's index in `SectionMeetingBlock` / `TileEntry` instead of finding it by
  (day, start); fixes the odd/even-week twin case for the right-click edits too.
- **Pointer handling** lives in `ScheduleGridView.axaml.cs`, beside the existing pointer and ghost-tile code (the grid is
  built in code-behind; a behavior would be unnatural there). Inverses needed: pointer y → minutes (piecewise linear,
  increasing, from the cached gridline offsets) and pointer x → day (handles semester sub-columns and folded days).
  The new start is taken from the ghost's top edge, not the pointer, since a tile can be taller than its time span.
- **Snapping:** legal start times for the meeting's block length in the academic year; if the length has none (a
  custom length), all legal start times of the year. With Shift held, 5-minute steps. Drag threshold, ghost opacity and the mark frame colour and
  thickness come from AXAML resources, not code.
- **After a drop:** select the section via `SelectedItem` (never scroll the list's ScrollViewer directly).
- **Marks:** an in-memory set of section IDs on the grid's view model, drawn as a frame when tiles are built. Never
  persisted. The tile frame is set directly on each tile (no class-conditioned style; CLAUDE.md, Avalonia gotchas).
  section's stored meetings still equal "after"; otherwise it is silently dropped.

## Checked against the four rules

This feature writes only the local copy, through the same repository call the section card uses. It adds nothing to
saving, locking, exiting, switching, restoring or recovering the shared database.

1. **Atomic rename of a checked copy:** unchanged; drops reach the shared database through the normal save.
2. **Only write with our lock:** drags and right-click edits are refused in read-only mode, as today.
3. **Never delete the only copy of unsaved work:** this is the bug fixed in story 1. The guard sits in the code path
   that reloads (no reload while a card is open), not in a dialog, so detached panels are covered too.
4. **Never confuse the user:** every refusal says why and what to do (stories 1, 6 and 8); nothing changes silently.

**MinimumVersionToWrite:** expected No. A move changes only meeting day and start time, fields every version writes.

## CLAUDE.md change (at build time)

Replace "Manual data entry via forms only — no drag-and-drop scheduling." with:
"Sections are created and edited through forms. In Schedule View, existing meetings can be moved by dragging (day and
start time only); there is no drag-to-create and no resizing."
