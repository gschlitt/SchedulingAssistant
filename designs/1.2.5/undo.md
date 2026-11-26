# Undo and rollback before saving

**Status:** approved by Greg 2026-11-26, design and wording (decision 5, story 7, revised and approved the same
day). Model decided with Greg
2026-11-26: Save is the line, autosave to the shared file is dropped, a deliberate Don't save is the user's choice. Ledger entry: "Undo and rollback before saving" in
[RELEASE_1.2.5.md](../../release_state/1.2.5/RELEASE_1.2.5.md); notes, probe results and the list of write paths in
[undo/NOTES.md](../../release_state/1.2.5/undo/NOTES.md).

**Principle:** TermPoint works like editing a document. Changes stay on Alice's computer until she presses Save. Until
then she can step back through her section changes, or go back to her last save, and colleagues see none of it.
Closing with unsaved changes asks the usual question.

Everything in "quotes" is approved wording (Greg, 2026-11-26), collected in "Wording" below; code comments guarding
it say so. The decisions are listed at the end.

## The stories

Alice is a department administrator, editing the Fall schedule with Schedule View and the Section List side by side.
Bob is a colleague who also edits it from his own laptop. Carol, on a third computer, only reads it.

### 1. Trying something out

Alice wonders whether CHEM 101 B would fit better on Friday. She drags its Thursday meeting to Friday 13:00, then
moves two lab sections to make room. It doesn't work.

- **Today:** there is no undo. She has to remember where the three meetings were and drag them back. Meanwhile
  autosave may already have saved the experiment to the shared database, where Bob and Carol can see it.
- **With undo:** she presses **Ctrl+Z** (or the **Undo** button beside Save). The last lab move is undone, the lab
  section is selected in the list, and a line beside Save says "Undone: Move CHEM 111 L02 Tue 14:30 → Wed 08:30". Two
  more presses and the schedule is as it was. **Redo** (Ctrl+Y) puts a step back if she undid one too many.
- Each drag, right-click edit (instructors, room, tags, flag), card Apply, new section, copy and delete is one step.

### 2. Going back to a point

Alice has spent twenty minutes on drags and room changes, and wants to go back to how things were before she started
dragging.

- **With undo:** she clicks the small arrow beside **Undo**. A list shows her steps since her last save, newest first,
  each with its time: "Move CHEM 101 B Thu 10:00 → Fri 13:00 · 10:58", "Change room of BIOL 210 A · 10:51", and so on.
  Pointing at a line highlights it and every newer line ("These will be undone"). She clicks the step where she
  started: all of those are undone at once. Redo can bring them back.
- The list's last line is always "Your last save · 10:42". Clicking it undoes every section change since then.

### 3. A card is open

Alice is typing a note in HIST 101 A's card.

- Ctrl+Z inside the card's text box undoes her typing, as it does today.
- Ctrl+Z elsewhere, or the Undo button, is refused while the card is open, with the message the grid already uses:
  "Finish editing HIST 101 A in the Section List first." (Undoing reloads the section list, which would close her card
  and lose what she typed.)

### 4. Colleagues don't see experiments

Carol has the database open read-only.

- **Today:** within ten minutes of each change (autosave), Carol's copy refreshes and shows Alice's half-finished
  experiment.
- **With the change:** Carol sees only what Alice saves. Her copy refreshes when Alice presses Save, or answers Save
  when closing.

### 5. Closing with unsaved changes

Alice is done for the day and closes TermPoint.

- **Today:** TermPoint saves without asking, then closes.
- **With the change:** if anything is unsaved, she is asked once:
  "Save Changes? · You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until
  you save. [Save] [Don't Save] [Cancel]". Save saves, then closes. Don't Save closes and throws the changes away, as
  any document program does (no second "are you sure"). Cancel goes back.
- If HIST 101 A's card also has changes she hasn't applied, it is still **one** question, with one more sentence:
  "HIST 101 A's card has changes you haven't applied; Save applies them too." Today's separate "Unapplied Changes"
  question (asked at close and at a switch) is folded into this one.
- Nothing unsaved: TermPoint closes without asking.
- The same question comes before opening another database ("…before opening CHEM?") and before Restore from Backup.

### 6. Undoing everything leaves nothing to save

Alice undoes every step back to her last save.

- The "Unsaved changes" line beside Save goes away, and closing asks nothing: there is nothing to save.
- If she also changed something undo doesn't cover (an instructor's name, a course), "Unsaved changes" stays, because
  that change is still unsaved. Only section changes can be undone; Don't Save at close throws away everything.

### 7. Other work never stops undo; deleted things stay deleted

Alice removes the tag "Lab" from HIST 101 A (right-click, Tags) and drags a few meetings. Then she edits the year's
legal start times, adds an instructor commitment, deletes room B204 (TermPoint removes it from three meetings, as
today), deletes the now-unused tag "Lab", and imports a few sections. Then she undoes back through her steps.

- **Nothing else she does blocks undoing her section changes.** Settings, preferences, instructors, courses, start
  times, commitments, notes, tags, rooms, imports and semester copies all leave the undo list alone. Those changes
  themselves are not undone (story 6).
- **Anything she deleted stays deleted.** Undoing "Change tags of HIST 101 A" puts the section back as it was, except
  for the tag Lab, which no longer exists: "Undone: Change tags of HIST 101 A. The tag Lab has been deleted, so it wasn't
  put back." Undoing a drag of a meeting that was in B204 puts the meeting back without B204. The same goes for a
  deleted type, campus or instructor. A section whose course or semester has been deleted since stays deleted, and the
  line says so.
- Removing B204 from the three meetings is not a step in the list: it was part of deleting the room, which undo
  doesn't cover.
- (First draft: any delete elsewhere, a semester copy or an import cut the history. Greg, 2026-11-26: too blunt, since
  ordinary work such as editing legal start times deletes rows and would silently end undo.)

### 8. Bob's laptop sleeps with unsaved work

Bob has been rearranging the Winter schedule all morning without saving. He closes his laptop lid and goes to lunch.
After a while his lock looks inactive, and Alice opens the database.

- Alice is asked today's question: "Bob hasn't been active in this database for 25 minutes. Their computer may be
  asleep or disconnected. If you take over, changes they haven't saved yet can't go into the database — they'll only be
  able to save them as a separate copy." She takes over.
- Bob wakes his laptop. Today's banner says the database is now read-only for him and "Your changes since your last
  save are kept on this computer." It offers them back next time, or as a separate copy if Alice changed the database.
- **What changes:** today Bob's kept changes are at most ten minutes of work, because autosave ran. Now they are
  everything since his last save, which could be a whole morning. Nothing is lost, but if Alice changed the database
  first, his morning ends up in a separate copy, not the shared schedule. Greg accepted this cost (2026-11-26).
- **To shrink it:** the line beside Save becomes "Unsaved changes since 10:42", so Bob can see how long it has been.
  Proposed: after two hours unsaved, a one-time notice: "You haven't saved since 10:42. Colleagues can't see your
  changes until you save." (Decision 6.)
- His step history is gone after the lock is lost: the changes are kept, but not as separate steps.

### 9. A crash, or Windows shutting down

- TermPoint crashes, or Windows shuts down and forces it closed while the Save Changes question is showing. Alice's
  unsaved changes stay on her computer exactly as they do today, and the next open asks "Restore Unsaved Changes?".
- **What changes:** today restored changes are saved at once. Now they come back unsaved (the line beside Save shows
  it), so Alice decides when they reach colleagues. They come back as one lump, without steps.

### 10. Several semesters

Alice works in Fall and Winter during one session. The history is one list, in the order she made the changes.
Undoing a step in a semester that isn't shown works, and says so: "Undone: Delete HIST 102 A (Winter 2026, not shown)".

### What is deliberately not there

- **Undo for anything but sections:** instructors, rooms, courses, commitments, notes and settings are not undoable
  (Greg, 2026-11-26). Don't Save at close throws those away too.
- **Undo past a save:** Save is the line. After a save the history starts again empty.
- **An autosave option:** removed, not kept as a setting (Claude's advice; Greg to confirm).
- **Save a Copy…:** backlog item 2.1's third part stays on the backlog.
- **The WASM demo:** no undo in this release (the demo has no SQLite). Its Save is already disabled.

## Technical detail (compact)

- **The change log lives on the working copy's connection only.** TEMP tables on `DatabaseContext`'s one connection to
  D': `undo_steps` (id, label, time, edit count after the step), `section_log` (one row per section row change: step,
  operation, every old column, the new data), and a small state table (the current step, "other changes", "history
  incomplete", a pause flag, the redo list). Created at every writer open (end of the constructor, and
  `ReinitializeConnection(readOnly: false)`), always starting empty. `PRAGMA temp_store=MEMORY`. The probe showed this
  never reaches D' or the shared file, and is gone after a lost lock or a switch (notes, "Probe results").
- **Triggers do the recording, and fail safe.** TEMP triggers on `main.Sections` log every insert, update and delete
  under the current step. Triggers on every other table, generated from `sqlite_master` when the log is created, only
  set "other changes" (insert, update or delete); they never touch the history.
- **Steps.** One call wraps each user action: set the current step and label, run the existing repository writes
  (unchanged), record the edit count, clear the step. Callers: the grid drop, the right-click Confirm, the card's
  `onSave` (add, edit, copy), the list's Delete, and the Courses panel's "apply tags to all sections". A new step
  clears the redo list.
- **Section writes that aren't section edits run with recording paused:** the cleanup when a room, type, tag,
  resource or reserve is deleted, Copy Semester, Import Sections, and deleting a semester or academic year. They are
  never steps and are never undone. Fail safe: a section write with neither a step nor a pause (a path the design
  missed) empties the history and sets "history incomplete", so a forgotten path can only cut the history short, never
  undo the wrong thing. Tests check that every known path is covered.
- **Undo.** Refused unless this is a writer session and `OpenCardMessages.WhyRefused` allows it. Then
  `DatabaseContext.MarkDirty()` (counts the edit, writes the dirty marker, refuses if read-only), one transaction with
  the triggers paused, the step's rows written back newest first, the step moved to the redo list, commit,
  `SectionStore.Reload`, select the section (`SelectedItem`), show the line beside Save. Redo is the same in reverse.
  - Each section is checked first: it must still equal the step's result, apart from references to things deleted
    since (the deletes' own cleanup removed those). Anything else that doesn't match (should never happen) refuses the
    undo and cuts the history.
  - The old row is written back exactly (meetings, unknown JSON fields, the readable columns: probe). If it refers to
    something deleted since (room, type, tag, resource, reserve, campus, instructor), that reference is left out, using
    one helper shared with the deletes' cleanup, and the line names what wasn't put back. A section whose course or
    semester no longer exists stays deleted.
- **Unsaved state.** "Unsaved" = any step, or "other changes", or "history incomplete". It drives the line beside Save
  and the close question, replacing today's `IsDirty` display. After a save that kept no marker: steps made before the
  save's snapshot (edit count at or below the save's) are dropped, and the flags reset if nothing was edited since. A
  save that kept the marker (an edit arrived during it) trims nothing; the next save does.
- **Undo everything leaves the marker.** The file's bytes differ from the saved copy even when the rows don't (probe,
  catch 2), so the dirty marker stays and is never removed by undo (no new deletion path). At close, "nothing unsaved"
  with a marker present releases with `saveFirst: true`, which writes nothing new for colleagues to see.
- **The close question** (`UnsavedChangesGate`) replaces `UnappliedChangesGate` at close and switch, and is new
  before a restore (which saves unasked today, and doesn't ask about an open card: check how a card behaves there). Save:
  apply the open card through its existing path, then `SaveUntilMarkerClearAsync`. Don't Save: discard the card,
  `ReleaseAsync(saveFirst: false)`, then remove the working copy and its marker (the deliberate choice; today's cleanup
  keeps them when a marker exists, so this is an explicit new branch). Cancel: nothing.
- **Autosave.** Remove the timer's start at open, the Auto-save checkbox, the interval setting and
  `ResumeAutoSave`. Keep the two `AppSettings` fields, unused, so a downgrade to 1.2.4 keeps the user's old choice.
- **Crash recovery.** `SaveRestoredChangesAsync` no longer saves; a recovered open starts with "history incomplete".
- **What runs at the same moment as an undo** (fix-on-fix check): a save in flight (an undo is an ordinary edit, so
  the existing edit-during-save check covers it: test it); a lost lock (`MarkReadOnly` makes `MarkDirty` refuse before
  anything is written; the build checks whether the demotion's connection close can meet an open undo transaction);
  heartbeat and wake check (lock only); periodic backup (reads D' on its own connection, WAL); reader poll (writers
  don't poll); exit (undo runs synchronously on the UI thread, so a close waits for it).
- **Tests.** The probe tests stay. New: each action makes one step; undo and redo restore exact rows, including a
  deleted section re-inserted with its id, meetings and extension data; deletes, imports, copies and setting changes
  leave the history alone; undo leaves out references to deleted things, and keeps a section deleted when its course or
  semester is gone; every known section write path is a step or paused, and a write that is neither cuts the
  history; save trimming, including a save that kept the marker; history gone after demotion and switch; refusal
  when read-only or a card is open; every close-question combination (unsaved, card, both, neither); harness: Don't
  Save leaves D untouched and removes the working copy only after the choice.

## Checked against the four rules

1. **The shared database only changes by one atomic rename of a checked copy:** unchanged. Only Save (the button, or
   Save in the close question) writes the shared file, through today's save. The log is never in any file that is
   copied (probe).
2. **Only write with the lock, and only when the database is as last seen:** the save path is unchanged. Undo runs
   only in a writer session; `MarkDirty` refuses it otherwise, and the history is gone after a lost lock.
3. **Never delete the only copy of unsaved work:** undo writes only the working copy, and removes nothing. A crash, a
   forced shutdown or a lost lock keep the working copy and its marker exactly as today, in the data layer. The only
   new deletion is a deliberate Don't Save (Greg's exemption, 2026-11-26). Accepted cost: a longer unsaved stretch when
   a lock is lost (story 8).
4. **Never confuse the user:** one question per close, with the card folded in (story 5); no question when nothing is
   unsaved (story 6); every undo says what it undid; refusals say why and what to do; a "What's new" line explains that
   TermPoint no longer saves on its own.

**MinimumVersionToWrite:** No. An undo writes back rows and fields this version already writes, exactly as they were.
The log is TEMP and never in the file; nothing new reaches `AppSettings`.

## Wording (approved by Greg, 2026-11-26)

| Where | Text |
|---|---|
| What's new | "TermPoint no longer saves on its own. Your changes reach colleagues when you press Save, and until then you can undo section changes with Ctrl+Z. When you close with unsaved changes, TermPoint asks whether to save them." |
| Close question | Title "Save Changes?" · "You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save." · [Save] [Don't Save] [Cancel] |
| …with a card open | adds "HIST 101 A's card has changes you haven't applied; Save applies them too." |
| …before a switch or restore | "Save your changes to BIOL before opening CHEM?" / "…before restoring a backup?" (same body and buttons) |
| Unsaved line | "Unsaved changes since 10:42" (was "Unsaved changes") |
| Two-hour notice (decision 6) | "You haven't saved since 10:42. Colleagues can't see your changes until you save." |
| Undo button tooltip | "Undo: Move CHEM 101 B Thu 10:00 → Fri 13:00 (Ctrl+Z)"; disabled: "Nothing to undo since your last save" |
| Redo button tooltip | "Redo: … (Ctrl+Y)" |
| After an undo | "Undone: …"; in a hidden semester "Undone: … (Winter 2026, not shown)" |
| …something was deleted since | "Undone: Change tags of HIST 101 A. The tag Lab has been deleted, so it wasn't put back." · "HIST 101 A wasn't put back: the course HIST 101 has been deleted." |
| History list | "These will be undone" (hover) · last line "Your last save · 10:42" (or "Earlier changes can't be undone one by one" after a history cut) |
| Step labels | "Move CHEM 101 B Thu 10:00 → Fri 13:00" (two meetings: "Tue, Thu 10:00 → 11:30") · "Change instructors of HIST 101 A" · "Change room of HIST 101 A" · "Change tags of HIST 101 A" · "Change flag of HIST 101 A" · "Edit HIST 101 A" · "Add HIST 101 C" · "Copy HIST 101 A as HIST 101 D" · "Delete HIST 101 A" · "Apply tags to 4 HIST 101 sections" |
| Card open | "Finish editing HIST 101 A in the Section List first." (already approved, 2026-11-16) |
| History cut (should never show) | "This change can't be undone because the section was changed another way. Earlier changes can't be undone one by one." |

## Decisions for Greg (recommendation first)

1. **Throw away:** the history's last line undoes every *section* change back to the last save; throwing away
   *everything* is Don't Save at close. (Alternative: a "revert to saved" that also undoes instructor, room and course
   changes: needs a local copy of the last save and a reload of the whole app; more risk, little gain for experiments.)
2. **Controls:** Undo and Redo buttons beside Save, an arrow on Undo for the history list, Ctrl+Z and Ctrl+Y outside
   text boxes. No menu items. (An AXAML change in the main view: Greg to close it in VS first.)
3. **Redo:** include it. It costs little with the same log, and makes a big jump back safe without a confirmation.
4. **History:** one list across semesters, up to 500 steps, emptied by each save.
5. **Other work never stops undo (revised with Greg, 2026-11-26):** anything deleted since stays deleted, and the undo
   says what it couldn't put back. Only a section write the design missed cuts the history (a safety net that tests
   keep from happening).
6. **The lost-lock cost:** show the time on the unsaved line (yes); one notice after two hours unsaved (recommended,
   dismissable); no change to Alice's takeover question (it already warns her).
7. **Switch and restore:** the same question as closing.
8. **Crash recovery:** restored changes stay unsaved until Alice saves.
9. **Autosave:** removed with no setting kept.
10. **WASM demo:** no undo this release.
