# Undo and rollback for section changes: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: `designs/1.2.5/undo.md` (not written yet).

<details>
<summary>Files to read first</summary>

- `src/TermPoint/Data/DatabaseContext.cs`: `_conn`, the one long-lived connection every repository write to D' goes through; `CloseConnection` and `ReinitializeConnection` (the only places it closes, besides `Dispose`); `ConfigureConnection`; `ApplyReadOnly` (`PRAGMA query_only`); the `onEveryWrite` callback that `CheckoutService.NoteEdit` counts edits with.
- `src/TermPoint/Services/SessionLifecycle.cs` ~line 2099 (`OnWriteLockLost`: closes and reopens D' read-only) and `ViewModels/MainWindowViewModel.cs` ~line 1016 (reader refresh of D'').
- `src/TermPoint/Services/CheckoutService.cs` ~line 5659: the save snapshots D' through its own `Pooling=False` connection, so a TEMP log on `_conn` is not part of what it copies.
- `src/TermPoint/Data/Repositories/SectionRepository.cs`, `src/TermPoint/Services/SectionStore.cs`: section writes and the reload that closes an open card.
- `src/TermPoint/Services/OpenCardMessages.cs`: the guard for saving sections from outside the card (CLAUDE.md gotcha).
- [click-drag-meetings/NOTES.md](../click-drag-meetings/NOTES.md), "Undo: costing": the earlier costing and risks (refuse when the section changed since, clear on reload or database switch, keep extension data).
- `designs/1.2.4 and prior/spec_version_1.2.4_summary.md`, item 2.1 (experimenting without saving): related, but stays a separate item.

</details>

<details>
<summary>Decisions and why</summary>

- Greg (2026-11-26), after hand-checking the drag: "the app really really needs an undo/rollback. The user needs to be able to experiment, and the drag and drop quickly leaves them confused about where they were, with no good way to return." Pulled from `release_state/backlog.md` (line of 2026-11-16) into 1.2.5 and made ACTIVE; Access Analyzer set to Waiting on Greg.
- Greg's answers (2026-11-26):
  - **Both kinds:** step-by-step undo, several steps, plus a rollback that returns to an earlier point in one go (e.g. "before I started dragging"). One change log serves both.
  - **All section changes:** grid drags, right-click edits, section card Applies, and creating, deleting and copying sections. Not instructors, rooms, courses, commitments, notes or settings.
  - **Undoable after a save, until the app closes:** an undo writes the old values back as a new change, saved like any other. History clears on close, database switch or lost lock. Colleagues see an experiment until it is rolled back; experimenting without saving (backlog 2.1) stays separate.
- Checked 2026-11-26: D' is written through one long-lived connection, closed only on a lost lock (demotion), a reader refresh or a database switch (a new context). So the backlog's approach (SQLite TEMP triggers writing to a TEMP log table, which never reach D' or the shared file) would clear itself at exactly the moments the history must clear. Not yet probed.

</details>

<details>
<summary>Dead ends</summary>

- A one-step "Moved X to ... · Undo" notice after a drop (2026-11-16): judged short of useful by Greg, and it raises "why not elsewhere?".
- Plain SQLite rollback: every save commits, so there is nothing left to roll back.

</details>

<details>
<summary>Technical issues</summary>

- To probe before designing: TEMP triggers on `_conn` see writes made through that connection only; confirm the log survives a save, that D's bytes and content hash are unchanged by it, and that it is gone after `ReinitializeConnection`.
- Risks to carry into the design (from the earlier costing): an undo must refuse when the section has changed since the step (e.g. edited in its card); undoing writes from outside the card needs the `OpenCardMessages` guard, since the reload closes an open card; re-inserting a deleted section must keep its id, its meetings and its extension data (`WithExtensionDataFrom`, `SectionDaySchedule.Clone()`).
- Writing old rows back through raw SQL would bypass the repositories and stores: the stores must reload, and the edit counter (`NoteEdit`) and dirty marker must see the undo as an edit, or the next save would skip it.

</details>
