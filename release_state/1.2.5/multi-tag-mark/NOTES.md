# Mark sections that carry more than one of the shown tags: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). No design doc (small enhancement, Greg 2026-11-17).
Mock-up (step 1, built 2026-11-17, waiting on Greg): [mockup.html](mockup.html). Values in it are copied from
AppColors/AppLayout; open it in a browser (the page draws the tiles with a small script).

<details>
<summary>Files to read first</summary>

- `Views/GridView/ScheduleGridView.axaml.cs`: where a tile line is built (subject marker docked left, flag icon docked right in a DockPanel; the `entry.InstructorNames` hover set with `ToolTip.SetTip` on the line); `TileChrome` widths (`EntryWidth` counts the flag; the new mark must be counted the same way).
- `Views/GridView/TileChrome.cs`: line width and height sums for markers and flags.
- `ViewModels/GridView/GridFilterViewModel.cs`: `TagsMatchAny` / `TagsMatchAll` and the checked tags.
- The grid-right-click-details entry's notes: the right-click menu now always shows instructors' full names, which is why the line hover can go.
- Line numbers drift; search by name.

</details>

<details>
<summary>Decisions and why</summary>

- Greg, 2026-11-17: the problem. BIOY3 and CHEMY3 tag 3rd-year requirements; CHEM 320 is in both. "All" mode hides how CHEM 320 meets the other BIO and CHEM courses; "any" mode shows them but nothing says CHEM 320 must also be checked against the other group. Access watches will help when built; this is the quick visual cue. With three or more groups, one mark means "more than one", and the hover says which; no attempt to show every intersection.
- Only the ticked tags count: a section tagged BIOY3 and ENVY3, viewed with BIOY3 and CHEMY3 ticked, gets no mark. Only in "any" mode with two or more ticked (in "all" mode every shown section has them all).
- Not a tag icon (Greg): it reads as "this section is tagged", not "in more than one". Not green: reads as "fine", and could pass for a subject colour. A hollow mark on the right so it can't be mistaken for the filled subject square on the left. Candidates for the mock-up: two overlapping rings (Venn), or one hollow ring.
- Hover on the mark only, not the whole line (Greg). The mark gets a slightly bigger transparent hover target (a Path is hit only on its fill).
- Remove the line's full-instructor-names hover (Greg found it irritating; the right-click menu shows full names now). Remove what feeds it too if nothing else uses it.
- Hover wording APPROVED (Greg, 2026-11-17): "Tagged with more than one of the tags shown: BIOY3, CHEMY3." Ticked tags it carries, in the filter's order.
- Colour must draw attention (Greg, mock-up round 1): slate and navy were too quiet. Greg sees no risk of confusing it with a subject colour (different shape, other end of the line). Round 2 shows magenta, purple, deep orange, black, white-filled magenta, and magenta/purple badges.
- Greg chose C3 (Venn rings, deep orange #E65100) "for now", kept in AppColors.axaml (`MultiTagMark`) so it can be tuned.
- Shape changed (Greg, 2026-11-17): two upright tags side by side, point and hole at the top ("U2", 12.5×9, filled,
  same orange). Our own drawing, so no credit needed. The rings sat high until their box matched their shape; the
  tags' box matches theirs (12.5:9).
- Build (2026-11-17): the mark's tag names ride on `SectionMeetingBlock`/`TileEntry.MultiTagNames` (replacing
  `InstructorNames`), computed by `FilterSnapshot.MultiTagNames` from `TickedTagsInOrder`; sizes in AppLayout
  (`TileMultiTag*`), counted by `TileChrome` like the flag; the hover wording lives in
  `ScheduleGridViewModel.MultiTagHoverText`. Overlay-only sections get the mark by the same rule.
- Added alongside (Greg, 2026-11-17): a "Clear all tags" button in the Tags popup, under the all/any radio buttons.
  It keeps the all/any choice (the main Clear still resets it to "all") and is greyed out while no tag is ticked.
- "Clear all tags" lives in `GridFilterViewModel.ClearTags` (CanExecute `AnyTagTicked`, refreshed when a tag's
  tick changes and after `PopulateOptions`); it shares `UncheckQuietly` with Clear so the grid re-runs once. Test:
  `GridTagLogicTests.ClearTags_*`.
- Drag: the canvas holds pointer capture during a drag, so other elements' hovers don't open (confirmed in Greg's hand check).
- Closed 2026-11-17: full suite passed (4727), Greg's hand check passed.

</details>

<details>
<summary>Dead ends</summary>

- A single tag icon (see Decisions).
- A Noun Project "multiple tags" icon (2026-11-17): at 10–12px its back tag is a half-pixel outline that blurs away,
  so it reads as one tag; it also needs a credit line. Our own tags lying on their side, or turned 45°, read as
  blobs or sweets at this size.

</details>

<details>
<summary>Technical issues</summary>

- Width: the mark takes text width on narrow lines, as the flag does; `EntryWidth` must count it so tile sizing stays right.
- Check that the hover does not appear mid-drag when the pointer passes over the mark (click-and-drag entry).
- Set the mark directly on the line, never with a class-conditioned style (CLAUDE.md: such styles cost every section line).
- The mark is an AXAML path resource, never a Unicode character (WASM).

</details>
