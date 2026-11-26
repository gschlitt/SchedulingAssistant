# Backlog: work not assigned to a release

One line each. `/start-work` turns a line into a ledger entry. Older candidates moved from 1.2.4 are in
[1.2.5/backlog-1.2.5.md](1.2.5/backlog-1.2.5.md).

- **Edit Course's "apply tags to all sections" strips cohort tags** (Access Analyzer Q47, 2026-11-24): it overwrites every section's tags in every semester, so it can remove a watched tag from sections a watch relies on. Existing hazard, kept out of the Access Analyzer build. Detail: Phase 4 of [designs/1.2.5/access-analyzer.md](../designs/1.2.5/access-analyzer.md).
- **Room delete or edit closes an open section card** (found during Undo, 2026-11-26): `RoomListViewModel` Delete and Edit's save reload the Section List without the `OpenCardMessages.WhyRefused` check, so typing in an open card is lost. Small fix (the check the environment-value delete already makes). Detail: [1.2.5/undo/NOTES.md](1.2.5/undo/NOTES.md), Technical issues.
