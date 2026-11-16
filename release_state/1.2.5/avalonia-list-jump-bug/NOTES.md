# Avalonia bug post: section list "jump": item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: none (no app change; this is a bug report).
Draft comment: [COMMENT_DRAFT.md](COMMENT_DRAFT.md). Repro: `tools/VspGrowDriftRepro/` (README there).

<details>
<summary>Files to read first</summary>

- `tools/VspGrowDriftRepro/Program.cs` and its README: the stand-alone repro (no TermPoint code).
- [COMMENT_DRAFT.md](COMMENT_DRAFT.md): the comment as drafted.
- `tools/ScrollAnchorProbe/README.md`: the workaround's probe; its `E1` rows with anchoring off show the same drift,
  `E9` the card recycled by its own growth.
- `ListScrollAnchorBehavior` (the workaround) and `ListVirtualizationBehavior`.

</details>

<details>
<summary>Decisions and why</summary>

- Greg (2026-11-16): add this to 1.2.5. Success is a well-written, reproducible bug post, not a TermPoint change.
- From the original item: virtualizing the 800-section list cut the heap from 475 MB to 160 MB. Since 1.2.3 the app
  ships virtualization as an option, kept usable by `ListScrollAnchorBehavior`; the point of the bug post is to be able
  to retire that fragile workaround.
- The repro builds its item views without bindings (an `ItemView : Border` that follows the item), to stay
  independent of Avalonia 12's binding classes. It measures in off-screen windows, like the probe, so a run doesn't
  disturb the desktop.
- The post is kept short and plain, because of how PR 20993 was received (below).
- Greg (2026-11-16): post it as a comment on #17635 rather than a new issue, very short.
- Greg (2026-11-16): the comment links to the repro in this repo's `tools/` folder (public once pushed to master).

</details>

<details>
<summary>Findings (2026-11-16)</summary>

- **Reproduced stand-alone on the first try**, identical numbers on 12.1.3 and 12.1.6 (the latest stable). 300 items
  50-139 px, scrolled in 250 px steps to 3000-12000, grow a mid-viewport item: +3 px moves it 82 or 413 px (or off
  screen); +500 px sends it off screen every time (at 12000 the view lands about 40 items earlier). Scroll offset
  unchanged. Plain `StackPanel`: 0 px in all 8 cases.
- **Likely duplicate: AvaloniaUI/Avalonia#17635** "ListBox Virtualization Problem" (open, label bug, since 11.2.2):
  expanders in a virtualized ListBox, "scroll jumps up/down after the click". Its repro is a zip; a maintainer once
  could not follow it. Ours adds a minimal measured repro on 12.1.6.
- Related: #17831 (open feature request: estimation makes scroll positions imprecise), #17935 (closed; a different
  symptom of the same estimate averaging).
- **PR #20993** (gentledepp, closed unmerged 2026-08-24) tried the root-cause fix: make the extent estimate independent
  of which items happen to be realized. grokys (VSP's author) closed it as unreviewable: two features in one PR and a
  long LLM-written description. Lesson for our post: short, plain, human, one thing.
- Searches done (titles of all ~140 VSP issues, plus "scroll anchoring", "item height changes", "expand jumps").
- **Still broken on main** (2026-11-16): nightly `12.2.999-cibuild0101388-alpha`, built from main commit 16d8678
  (main's head that day), gives numbers identical to 12.1.3 and 12.1.6. Main had six VSP fixes Aug-Oct 2026
  (including #21975 "offset when scrolling to variable-sized items"); none touch this case. Run it with
  `-p:AvaloniaVersion=<nightly> -p:RestoreAdditionalProjectSources=https://nuget-feed-nightly.avaloniaui.net/v3/index.json`.
- Value of posting (discussed with Greg): 17635 is already accepted as a bug, so a comment only helps as new evidence:
  the small measured repro, and "still on main after the recent VSP fixes", while people are working on this code.

</details>

<details>
<summary>Dead ends</summary>

- Multi-word `gh search issues "..."` returned nothing for this repo; `gh api search/issues?q=...` works.

</details>

<details>
<summary>Technical issues</summary>

- The interactive mode (`-- --interactive`) builds but has not been opened by hand yet; a screen recording from it would
  help the post.

</details>
