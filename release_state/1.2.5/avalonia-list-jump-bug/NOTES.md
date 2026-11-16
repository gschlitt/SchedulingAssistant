# Avalonia bug post: section list "jump": item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: none (no app change; this is a bug report).

<details>
<summary>Files to read first</summary>

- `tools/ScrollAnchorProbe/` (README.md, Scenarios.cs, Rig.cs): the existing probe of scroll anchoring.
- `ListScrollAnchorBehavior` (section list scroll anchoring) and the section list view.
- `designs/1.2.4 and prior/spec_version_1.2.4_summary.md`, item 9.8, for the original wording.
- Memory notes `wasm_semester_switch_disabled` and `perf_observablecollection_reset` mention the same blocker (virtualization vs the inline-editor reflow).

</details>

<details>
<summary>Decisions and why</summary>

- Greg (2026-11-16): add this to 1.2.5. Success is a well-written, reproducible bug post, not a TermPoint change.
- From the original item: virtualizing the 800-section list cut the heap from 475 MB to 160 MB; the blocker is a jump when an on-screen item grows.

</details>

<details>
<summary>Dead ends</summary>

None yet.

</details>

<details>
<summary>Technical issues</summary>

- Not yet reproduced outside TermPoint. Avalonia version in use: check `TermPoint.csproj` and pin the same in the repro.

</details>
