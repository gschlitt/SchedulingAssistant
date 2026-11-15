# Conflict warnings: live notice in the editor, and muting

**Status:** designed 2026-11-06 (Greg + Claude, advice only, nothing built). Two parts:

- **Part A, live notice in the section editor:** 1.2.4 item 2.19. No data change, no version gate.
- **Part B, muting conflicts:** 1.2.4 item 2.15, **moved to the next release** (Greg, 2026-11-06) because it raises
  `MinimumVersionToWrite`.

## How conflict detection works today

- `InstructorConflictService.DetectConflicts` and `RoomConflictService.DetectConflicts` are pure, read-only
  functions: a semester's sections in, conflict descriptions keyed by section ID out.
- They are called only from `SectionListViewModel.ApplyRoomConflicts` / `ApplyInstructorConflicts`, after a full
  `LoadCore` and after `TryUpdateCardInPlace`. So they run **after** a save, and every save re-scans every semester
  in `SectionStore.SectionsBySemester` and resets every card's warning. This is needed (editing B can create or
  clear a conflict on A) and cheap (slots are grouped by (instructor or room, day); comparisons happen only within a
  group, so a few hundred sections take well under a millisecond).
- `WorkloadPanelViewModel` also calls `InstructorConflictService.DetectConflictsByInstructor`.
- Conflicts are hidden in multi-semester mode.
- The section editor (`SectionEditViewModel`) knows nothing about conflicts; the user finds out only from the card
  flag after saving.

## Part A: live notice in the section editor (1.2.4)

**Story.** Alice opens CHEM 101 A and ticks J. Smith. Before she saves, a notice in the editor says
"Instructor conflict: J. Smith Mon 0800–0850 with CHEM101 B". She changes the time and the notice goes away.

1. **Draft without saving.** Move the part of `Save()` that turns the editor's meeting and instructor rows into
   `Schedule` and `InstructorAssignments` into one method used by both `Save()` and the check. The check must not
   change `_section` (Cancel must still discard everything). There must be only one place that builds these lists,
   so `SectionCloneTests` / `ExtensionData*Tests` keep covering it.
2. **One check method.** It takes the section's own semester from `SectionStore`, replaces the saved copy of this
   section with the draft (same ID), runs both detectors, and keeps this section's results. The editor checks its own
   semester even in multi-semester mode, since there it is well defined.
3. **Triggers:** an instructor ticked or unticked; a meeting's day, start, length, frequency or room changed (the
   room-change hook that calls `CheckRoomCapacities` already exists).
4. **Display:** a non-blocking notice in the `CapacityWarning` style, reusing the detectors' wording, with **no
   Dismiss button** (Greg, 2026-11-06): it would look like muting, and the card keeps its flag after saving.
   **No "save anyway?" dialog.** Some conflicts are intended (sections taught together) and a dialog would ask
   about something the user has already dealt with (rule 4).

Risks: none to data; nothing in the save, lock or write path changes. `MinimumVersionToWrite`: No (the same edits
save the same data). The card flags after saving stay as they are.

## Part B: muting conflicts (next release)

**Decided (Greg, 2026-11-06):** muting only, with no rule that detects sections taught together. No
"N muted conflicts" list: the muted line stays on each card, so it is visible where it happened.

**Story.** Alice sees J. Smith's CHEM 101 A conflicts with CHEM 101 L1. She knows Smith drops into the lab at the
end and it's fine. The notice's hover says "Right-click to mute." She right-clicks it and chooses *Mute this
conflict*. On both cards the red notice becomes a light *Conflicts muted* line. In March, Bob gives L1 to another
instructor: that conflict is gone, and the mute stops mattering. Later Bob gives Smith another section that overlaps
CHEM 101 A. That is a new pair, so it shows red.

### Behaviour

- **Red notice:** hover lists the conflicts, ending with "Right-click to mute." Right-click opens:
  - one conflict: *Mute this conflict*;
  - several: one *Mute: J. Smith Mon 0800–0850 with CHEM101 B* item each, plus *Mute all*.
- **Muted:** one light-font *Conflicts muted* line per card (room and instructor together). Hover lists what is
  muted, ending with "Right-click to unmute." Right-click: one *Unmute: …* item each, plus *Unmute all*. If unmuted
  conflicts remain, the red notice stays for those.
- **Both cards change.** Muting A against B also mutes it on B; either card can unmute it.
- **Read-only mode:** no menus and no "Right-click to…" hint.
- **Both conflict display modes** (`AppSettings.SectionConflictDisplay`, Flag or Lines) get the menus and the
  muted line.
- **Everywhere else respects mutes:** filtering lives inside the detector services, so the Workload panel and the
  Part A editor notice skip muted pairs.

### Data

- On `Section`, a list of `{ Kind: Instructor | Room, OtherSectionId, ResourceId }` (ResourceId = instructor or room
  ID); each entry a `PersistedRecord`. A conflict between A and B on resource R is muted if **either** section lists
  the other for R. Stored once; checked from both sides.
- **Section deleted:** the entry never matches again. It is dropped when its section is next saved.
- **Instructor or room changed:** ResourceId no longer matches, so the conflict shows again (wanted).
- **Times shifted but still overlapping:** still muted (same known pair).
- **Section copied:** the copy must **clear** the list, or it would silently hide its own new conflict with B. This
  is an explicit exception to "clones copy every persisted field"; pin it with a test next to `SectionCloneTests`.
- **Semester roll-forward:** new sections have new IDs, so mutes don't carry over and get confirmed again each term.
- Unmuting removes the entry from whichever section holds it.

### UI implementation notes

- The notices are built in code by `PropertyLinesBehavior` (`AddConflictFlag` and the warning-line path). Put the
  right-click handling there, not in code-behind.
- Create the menu when the user right-clicks, not attached to every card in advance (CLAUDE.md item 21: anything
  repeated on every card costs on every card).
- The section list cards have no right-click menu today (the grid's is `SectionContextMenuViewModel`).
- Light-font colour and weight come from `AppColors.axaml` / `App.axaml`.
- If the muted line still distracts, a later option is to show it only on hover or on the selected card.

### Risks

- **The four shared-database rules:** not affected. Mute and unmute are ordinary section edits saved the normal way;
  they need the write lock.
- **`MinimumVersionToWrite`: Yes.** Muting is a new kind of edit. Set `ThisReleaseRequires` / `ThisReleaseReason`,
  update the test that pins them, mark the Store submission mandatory. Older versions would keep the field (extension
  data) but ignore it and show red.
- **Mutes are shared** by every administrator of the database. That is right for a department decision; the
  unmute menu on the card is the safety net.

**Estimate:** about 2–3 days with tests.
