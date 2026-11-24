# Backlog: work not assigned to a release

One line each. `/start-work` turns a line into a ledger entry. Older candidates moved from 1.2.4 are in
[1.2.5/backlog-1.2.5.md](1.2.5/backlog-1.2.5.md).

- **Multi-step undo for section changes** (Greg, 2026-11-16): undo several steps back, across grid drags, right-click edits and section card Applies, not just the last drag. A single "undo this drop" notice was judged short of useful and would raise "why not elsewhere?". Costing and risks (refuse when the section changed since, clear on reload or database switch, keep extension data): the undo section of [click-drag-meetings/NOTES.md](1.2.5/click-drag-meetings/NOTES.md). Related: 1.2.5 backlog 2.1 (experimenting without saving).
- **Edit Course's "apply tags to all sections" strips cohort tags** (Access Analyzer Q47, 2026-11-24): it overwrites every section's tags in every semester, so it can remove a watched tag from sections a watch relies on. Existing hazard, kept out of the Access Analyzer build. Detail: Phase 4 of [designs/1.2.5/access-analyzer.md](../designs/1.2.5/access-analyzer.md).
