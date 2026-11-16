# Access Analyzer and easy access watches: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design: [designs/1.2.5/access-analyzer.md](../../../designs/1.2.5/access-analyzer.md).

<details>
<summary>Files to read first</summary>

- `designs/1.2.5/access-analyzer.md`: the spec. Status block at the top; Terms (D2); decisions D1-D26; Phase 2 rules 1-41; Phase 3 wireframe rounds table.
- `designs/1.2.5/access-analyzer-handoff.md`: older handoff (written 2026-11-10). Its branch and release notes are out of date; the ledger replaces it for state. Its summary of the method and working agreements still holds.
- `designs/wireframes/`: the clickable HTML wireframes (round 4 awaits Greg).
- `designs/1.2.5/slot-browser.md`: related; its D3 level 2 is judged by whole slates.
- Memory note `project_access_analyzer` (Greg's own working notes).

</details>

<details>
<summary>Decisions and why</summary>

- Method: Progressive Design Spec, six phases, each locked by Greg before the next. Phase 3 is settled one screen per round with a clickable HTML wireframe, not signed off as a batch.
- D17 (agreed): the Access panel is a tab beside Workload; each tab detaches on its own.
- D25 (agreed): an access watch is a filter preset you choose to watch. D26 (agreed): a custom tag can be watched directly.
- Greg (2026-11-16): add the feature to the 1.2.5 ledger. Earlier it was "release undecided, probably not 1.2.4".

</details>

<details>
<summary>Dead ends</summary>

- Separate course-list and tag watches (Q35, Q36): superseded by D25.

</details>

<details>
<summary>Technical issues</summary>

- Old spec header still says "Release: not scheduled" and names branch `Version_1.2.4`; update the Status block at the next spec session.
- Phase 5 risk: `DetachablePanel` may not support tabs today (fallback: a third panel with a splitter).
- Data risk: parts on courses, a delivery type field on sections and watches stored as presets will change what is saved; the `MinimumVersionToWrite` question is almost certainly Yes for the stored pieces. Persisted classes must derive from `PersistedRecord`.
- Real-data checks: UFV BIOL and UBC databases (dev machine only, read from a copy) model labs differently; do not treat one as representative.

</details>
