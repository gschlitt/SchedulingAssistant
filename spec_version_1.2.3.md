# TermPoint 1.2.3 — Update Spec

**Branch:** `Version_1.2.3` (from `master` @ `e8c7c1d`, v1.2.2)
**Started:** 2026-10-08

This spec collects the bug fixes, dependency updates, and new feature work planned for 1.2.3.
TermPoint is in production: every item lists its user impact, data-integrity risk, and how it
will be verified.

---

## Items

| # | Item | Type | Status |
|---|------|------|--------|
| 1 | Avalonia 12.0.2 → 12.1.3 upgrade | Dependency / bug fix | **Done** (steps 1–5). Manual checks still open: UNC picker, macOS, Win11 VM |
| 2 | Bundled SQLite engine security update (NU1903) | Dependency / security | **Done** |
| 3 | Preserve unknown JSON fields across app versions, with a minimum-version gate | Data integrity | **Done** (3a unknown-field catch-all; 3b `MinimumVersionToWrite` gate). Field test: the Store link once 1.2.3 is certified |
| 4 | Stop opening the shared DB directly; fix startup ordering; single-instance guard | Data integrity | **Done** (4a, 4e, 4d, 4b, 4c; user-tested 2026-10-13). Open: header-only check on D (kept as built), possible 4f (working copy locked vs damaged). Field tests: Mac guard, clumsy |
| 5 | Make Restore from backup safe | Data integrity | Accepted |
| 6 | Strip paths and usernames from BugSnag reports | Privacy | Accepted |
| 7 | Report handled save/lock failures and unclean exits to BugSnag | Observability | Accepted |
| 8 | Empty / delete semester and academic year: consistent rules, no orphans | Bug / data integrity | **Done** (8a service, 8b screens, 8c orphan-tolerant queries); user-tested 2026-10-12 |
| 9 | Make stale-lock takeover safe | Data integrity | **Done** (9a warning + click re-check, 9b clock-free staleness; user-tested on one machine 2026-10-13). 3-computer integration test pending |
| 10 | "Save aborted" dead end: give a next step | UX | Accepted |
| 11 | Reader Refresh: report failures honestly and recover the connection | UX / reliability | Accepted |
| 12 | Save-specific error banner for exceptions during save | UX | Accepted |
| 13 | Cloud-sync folder detection gaps | Data integrity / UX | Accepted |
| 14 | Unapplied editor changes are lost on exit or database switch | Data loss | Accepted |
| 15 | Exit-path hardening and the mid-save edit gap | Data loss | Accepted |
| 16 | Remaining network I/O on the UI thread | Responsiveness | Accepted |
| 17 | CSV import blow-up on an unmatched quote | Responsiveness | **Done**, plus 2 latent row-loss bugs fixed |
| 18 | Wizard Cancel: hide instead of close (defence in depth) | Hang | **Done: already covered by existing code** |
| 19 | Mouse-wheel past the end of a dropdown scrolls the section list underneath it | UX bug (user-reported) | **Done** |
| 20 | **Load by subject / focus**: work on chosen subjects and levels only (dean's-office scale) | **New feature** | **PARKED: revisit after all other items are done, especially item 21** |
| 21 | Lightweight section card: ~93 → ~15–20 controls per card | Performance / memory | **Done** (~93 → ~27 controls per card; 800-section heap ~390 MB Release); CloneSection data-loss fixed |
| 22 | AutoCompleteBox crash when revising a meeting's start time (BugSnag, Aug 21) | Bug (production, reproduced) | **Done** (folded into item 1) |
| 23 | Section-list virtualization with scroll anchoring, opt-in (Preferences, default off; hidden in WASM) | Performance / memory | **Done** (user-tested on desktop 2026-10-10); field tests pending (touch, macOS trackpad, Win11). Also: room conflicts one per line |

Item numbers are for reference only. The implementation order is below.

### Implementation order (decided 2026-10-09)

Items 2 and 19 were accepted into scope on 2026-10-09. Avalonia stays at **12.1.3**: 12.1.4 was
published on 2026-10-09 and has had no field exposure. Review its release notes before release.
First look (2026-10-09):
- **No `VirtualizingStackPanel` fixes.**
- **One relevant entry, #22394**, "Do not measure TextBlock embedded controls during a render pass".
  The section card's property-line icons are exactly that: `InlineUIContainer`s built by
  `PropertyLinesBehavior` (item 21, step 3). Weigh it when deciding whether to take 12.1.4.

| Order                                                                       | Item                                                                                   | Why here                                                                                                                                                                                                                                       |
| --------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Phase 1: Foundation**                                                     |                                                                                        |                                                                                                                                                                                                                                                |
| 1                                                                           | 1: Avalonia 12.1.3 (its 5 steps, one commit each), **with item 22 folded into step 1** | Every later manual test then runs on the framework we ship. Unblocks item 18. **Item 22 moved here (2026-10-09):** the bump broke mouse picks in the meeting editor, and item 22's fix is the cure (see item 1, "Regression found in step 1"). |
| 2                                                                           | 2: SQLite security bump                                                                | The other dependency change, kept next to item 1.                                                                                                                                                                                              |
| 3                                                                           | 18: Wizard Cancel hides instead of closing                                             | Trivial once item 1 has landed.                                                                                                                                                                                                                |
| **Phase 2: Contained bug fixes**                                            |                                                                                        |                                                                                                                                                                                                                                                |
| 4                                                                           | ~~22: AutoCompleteBox crash~~                                                          | Moved into item 1.                                                                                                                                                                                                                             |
| 5                                                                           | 17: CSV reader                                                                         | Pure logic, unit-testable.                                                                                                                                                                                                                     |
| 6                                                                           | 19: Wheel containment in popups                                                        | Input routing only.                                                                                                                                                                                                                            |
| **Phase 3: Big UI change, early**                                           |                                                                                        |                                                                                                                                                                                                                                                |
| 7                                                                           | 21: Lightweight section card                                                           | Moved early so the big visual change gets used throughout the rest of development.                                                                                                                                                             |
| **Phase 4: Data model**                                                     |                                                                                        |                                                                                                                                                                                                                                                |
| 8                                                                           | 3: Unknown-field preservation + version gate                                           | Release-order gate for future releases. Touches every entity, so it should land early.                                                                                                                                                         |
| 9                                                                           | 8: Semester / AY empty and delete rules                                                | Repository-level, unit-testable.                                                                                                                                                                                                               |
| **Phase 5: Startup, lock and restore** (shared files, strictly in sequence) |                                                                                        |                                                                                                                                                                                                                                                |
| 10                                                                          | 4: No direct opens; startup ordering; single-instance guard                            |                                                                                                                                                                                                                                                |
| 11                                                                          | 9: Stale-lock takeover                                                                 |                                                                                                                                                                                                                                                |
| 12                                                                          | 5: Safe Restore                                                                        | Builds the off-UI-thread copy helper.                                                                                                                                                                                                          |
| 13                                                                          | 16: Remaining UI-thread network I/O                                                    | Reuses item 5's helper.                                                                                                                                                                                                                        |
| **Phase 6: Save messaging and exit**                                        |                                                                                        |                                                                                                                                                                                                                                                |
| 14                                                                          | 10 + 11 + 12, as one piece                                                             |                                                                                                                                                                                                                                                |
| 15                                                                          | 15: Exit hardening + edit generation counter                                           | After item 4, which also edits the end of `SaveAsync`. Sets up the close sequence that item 14 plugs into.                                                                                                                                     |
| 16                                                                          | 14: Unapplied editor changes on exit / DB switch                                       | Adds the Apply / Discard / Cancel step to the start of item 15's close sequence.                                                                                                                                                               |
| **Phase 7: Detection and observability**                                    |                                                                                        |                                                                                                                                                                                                                                                |
| 17                                                                          | 13: Cloud-sync folder detection                                                        |                                                                                                                                                                                                                                                |
| 18                                                                          | 6: BugSnag scrubber                                                                    | Must come before item 7.                                                                                                                                                                                                                       |
| 19                                                                          | 7: Handled-failure BugSnag events                                                      | Last of the reliability items, so it reports from the finished code of items 4, 9 and 15.                                                                                                                                                      |
| **Phase 8**                                                                 |                                                                                        |                                                                                                                                                                                                                                                |
| 20                                                                          | 20: Load by subject                                                                    | Parked. Re-measure memory after item 21, then decide.                                                                                                                                                                                          |

**Workflow.** Opus orchestrates and Sonnet writes the code, one step at a time. For each step:
1. Opus writes a self-contained brief.
2. Sonnet implements it, then runs the compile check and the tests for that area. It never
   commits or creates branches.
3. Opus reviews the diff against this spec and re-runs the compile check and the full test suite
   independently.
4. The user runs the manual checks this spec lists. Each step is committed only on the user's
   request.

Items 3–13 come from the reliability review of 2026-10-07 (session "Repo UX and reliability
review"). Items 14–18, the single-instance guard and messaging in item 4, and the extra restore
sites in item 5 come from the hang review of 2026-10-08 (session "Codebase UX review:
responsiveness and data loss"). File and line references are against `e8c7c1d`.

**This release needs extensive testing on a real network setup.** See
[Release testing: network field tests](#release-testing-network-field-tests) at the end.

### Sequencing constraints

Most items are independent and can go in any order. The exceptions:

- **Item 20 is parked until every other item is done.** In particular, item 21 must land first
  so we can re-measure memory per card. Only then decide item 20's approach and scope (see
  item 20).
- **Item 3 has a release-order constraint, not a branch-order one.** Unknown-field preservation
  only protects data once every client in a department has it. Any release that adds a field to
  an existing entity must ship **after** a release containing item 3 has been deployed. Fields
  added in 1.2.3 itself are still exposed to 1.2.2 clients.
- **Do item 6 before or with item 7.** Item 7 adds new BugSnag events, and they must go through
  the scrubber.
- **Overlapping files: do these in sequence, not in parallel sessions.**
  - Items 4, 5 and 9 all touch the write lock and startup sequence: `WriteLockService`,
    `CheckoutService` and `MainWindow.RunStartupAsync`. Item 1 also touches startup windows.
  - Items 10, 11 and 12 all touch save and refresh messaging in `MainWindowViewModel` and the
    banner. They are naturally one piece of work.
  - Items 14 and 15 both change `MainWindow.OnClosing`. Item 15 also changes the end of
    `CheckoutService.SaveAsync`, which item 4 touches too (step-4 catch).
  - Items 5 and 16 both move file copies off the UI thread through `NetworkFileOps`. Share the
    helper.

---

## 1 — Avalonia 12.0.2 → 12.1.3 upgrade

**Decision (2026-10-08):** upgrade to **12.1.3** (DataGrid 12.1.2). Svg.Controls.Skia.Avalonia
stays at 12.0.0.

### Current vs. available

| Package | Current | Target | Notes |
|---|---|---|---|
| Avalonia, .Desktop, .Themes.Fluent, .Themes.Simple, .Fonts.Inter, .Controls.ColorPicker | 12.0.2 | **12.1.3** | Releases in between: 12.0.3, 12.0.4, 12.0.5, 12.1.0, 12.1.1, 12.1.2, 12.1.3 |
| Avalonia.Browser (`TermPoint.Browser.csproj`) | 12.0.2 | **12.1.3** | Must move in lockstep with core |
| Avalonia.Markup.Xaml.Loader (Debug only) | 12.0.2 | **12.1.3** | Lockstep |
| Avalonia.Controls.DataGrid | 12.0.0 | **12.1.2** | Separate repo and release cadence |
| Svg.Controls.Skia.Avalonia | 12.0.0 | **HOLD at 12.0.0** | See "Companion packages" |
| AvaloniaUI.DiagnosticsSupport, HotAvalonia (Debug only) | 2.2.1 / 3.1.0 | 2.2.3 / 3.1.5 | Dev tooling only; optional |

Target **12.1.3, not 12.1.0 or 12.1.1.** 12.1.0 turned on stencil buffers by default, which caused
an anti-aliasing regression on vector geometry. 12.1.2 made them opt-in again (#21899). The 12.0.x
line stopped at 12.0.5 (June 2026); 12.1.x is the maintained line.

### Options considered

| Option | Gets | Costs | Verdict |
|---|---|---|---|
| **Stay on 12.0.2** | Nothing to test | The compositor deadlock stays live on Win10 for any slow window close. Our fixes are path by path; upstream's bug is generic. Hangs raise no exception, so Bugsnag never sees them, and a hang followed by a relaunch is the entry point to the relaunch-while-hung data-loss trap (hang review 2026-10-08). 12.0.x is end-of-line, so we'd still pay for the upgrade later, at a time we don't choose and with a bigger jump. | Not recommended |
| **12.0.5 (patch only)** | Deadlock fix, `GetPosition` fix, picker STA fix | Small behaviour-change surface. Skips the AutoCompleteBox focus rewrite. End-of-line, so a 12.1 upgrade still comes later. | Low-risk minimum |
| **12.1.3** | All of the above, plus list/focus/popup/shutdown fixes and the maintained line | Manual regression pass, concentrated on meeting-time entry | **Chosen** |

### Upstream fixes that hit issues we have already met

| Our issue | Upstream fix | Release | Effect on us |
|---|---|---|---|
| **UI ⇄ compositor deadlock when a window is disposed** (remote-DB startup freeze after a file picker; Schedule View reattach freeze). Proven stack: UI thread at `WinUiCompositedWindow.Dispose` → `Monitor.Enter`, compositor thread at `WinUiCompositorConnection.RunLoop` → `WaitOne` holding the lock. | [#21591](https://github.com/AvaloniaUI/Avalonia/pull/21591) "Fix `WinUiCompositorConnection` deadlock" (fixes #21531) | 12.0.5 | **Exact match to our stack.** The PR names a "slow close" trigger that is **Windows 10 only**. That explains why it reproduced so readily here (Win10 22H2), and many campus admin PCs still run Win10. Keep our Hide-not-Close patterns as defence in depth. This also covers the still-open "wizard cancel-after-picker" edge case. |
| **AutoCompleteBox selection crash**: `ArgumentException: Control does not belong to visual tree` via `PointerEventArgs.GetPosition` → `PointToScreen` (WORKAROUNDS.md #1, Harmony patch) | [#21301](https://github.com/AvaloniaUI/Avalonia/pull/21301) `GetPosition` no longer throws when `VisualRoot` is null; [#21782](https://github.com/AvaloniaUI/Avalonia/pull/21782) detaches platform input when a presentation source closes. Upstream #19892 is closed. | 12.0.4 / 12.1.1 | Our Harmony patch is probably no longer needed. Removing it also removes `Lib.Harmony` and runtime IL patching of a framework method. |
| **DataGrid column-header sort broken** (WORKAROUNDS.md #3, PointerPressed workaround in `InstructorListView`) | DataGrid [PR #230](https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/pull/230) | DataGrid 12.0.1 | Workaround removable. |
| File picker hangs when opened shortly after a modal closes (the same class of startup and wizard freezes) | [#21266](https://github.com/AvaloniaUI/Avalonia/pull/21266): the Win32 picker now runs on the UI thread's STA instead of an MTA pool thread | 12.0.4 | Removes a second, independent picker-hang path. |
| WASM: ComboBox dropdown floats above its anchor in a scrolled list (WORKAROUNDS.md #2) | [#22001](https://github.com/AvaloniaUI/Avalonia/pull/22001): repeated `BringDescendantIntoView` calls no longer accumulate the offset. Upstream #18203 is **still open**. | 12.1.2 | Fixes the mechanism our workaround describes (two bring-into-view events in a row). **May** allow removal; test only, no commitment. |

### Upstream fixes for problems we are likely to meet

- **ListBox stability.** Crash when scrolling after item removal ([#21838](https://github.com/AvaloniaUI/Avalonia/pull/21838)); crash when the items source changes during `SelectionChanged` ([#22102](https://github.com/AvaloniaUI/Avalonia/pull/22102)); ghost items ([#22165](https://github.com/AvaloniaUI/Avalonia/pull/22165)). `SelectionCommandBehavior` already contains a defensive `SelectedItem = null` "to prevent Avalonia's selection model from crashing when the backing collection is rebuilt". This is the same family of bug.
- **Focus locked after a detached control** ([#22113](https://github.com/AvaloniaUI/Avalonia/pull/22113)): keyboard input stops routing to the main window when focus is restored to an element that has left the tree. Relevant to detached panels, sticky notes, and the inline section editor collapsing.
- **AutoCompleteBox popup not closing on focus loss** ([#21749](https://github.com/AvaloniaUI/Avalonia/pull/21749)). Affects the Start Time and Block Length boxes in the section and meeting editors. See also the behaviour-change risk below.
- **Popup `Topmost` handling on Windows** ([#17841](https://github.com/AvaloniaUI/Avalonia/pull/17841)). All 9 modal dialogs are now `Topmost` (lockup audit), and dropdowns inside them depend on this.
- **macOS child-window layering** ([#22221](https://github.com/AvaloniaUI/Avalonia/pull/22221)): a child window can no longer sit below its parent. This is the "hidden modal: paints but ignores clicks" failure mode on Mac.
- **Shutdown** ([#22240](https://github.com/AvaloniaUI/Avalonia/pull/22240)): the dispatcher yields eagerly once shutdown starts. This may reduce lingering `TermPoint.exe` processes after close.
- **Binding memory leak with observables** ([#22097](https://github.com/AvaloniaUI/Avalonia/pull/22097)).
- **WASM demo**: `BrowserDispatcherImpl` never set its signaled flag ([#22093](https://github.com/AvaloniaUI/Avalonia/pull/22093)); `PointerEvent` JS objects leaked on every pointer move ([#22112](https://github.com/AvaloniaUI/Avalonia/pull/22112)); sync context not set before dispatcher messages ([#21462](https://github.com/AvaloniaUI/Avalonia/pull/21462)).
- **Not counted on:** several `VirtualizingStackPanel` fixes for variable-sized items (#21975, #22014, #22081, #21835). They do not clearly cover the inline-editor reflow bug that blocks section-list virtualization, so treat that as unchanged until tested.

### Behaviour changes: risk review

| Change                                                                                                                  | Our exposure                                                                                                                                                                                                        | Assessment                                                                                                                                                                                                                                            |
| ----------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AutoCompleteBox focus handling rewritten. `GotFocus`/`LostFocus` are now the source of truth (#21749).                  | `LostFocusCommandBehavior` commits Start Time and Block Length on LostFocus. `OpenDropDownOnFocusBehavior` opens on click and runs manual Tab-through. Used in `SectionListView.axaml` and `MeetingListView.axaml`. | **Medium. This is the one data-entry path at risk.** If LostFocus now fires at a different moment (for example when clicking into the dropdown), a meeting time could be committed early or with a stale value. Must test by hand (see Verification). |
| `SelectionChanged` is now raised when a collection `Reset` clears the selection (#20942).                               | `SelectionCommandBehavior` is the only routed `SelectionChanged` handler **in our code**. **Missed at review:** AutoCompleteBox's internal selection adapter also handles it.                                       | ~~None~~ **Regression found in the step 1 manual pass (2026-10-09). Fixed by pulling item 22 into item 1.** See "Regression found in step 1" below.                                                                                                   |
| `TopLevel.Closed` is now raised from managed `Dispose` paths, with a double-call guard (#22045).                        | `wizard.Closed` → `tcs.TrySetResult()`; `note.Closed` → `_openNotes.Remove(note)`; `DetachedPanelWindow.OnClosed`.                                                                                                  | **Low.** Handlers are idempotent. Hidden-not-closed windows may now raise `Closed` at app exit, after their work is done. Watch the detached-panel shutdown path.                                                                                     |
| Dispatcher yields on every operation once shutdown starts (#22240). Queued work after shutdown is aborted, not drained. | Exit save runs inside `MainWindow.OnClosing` (`ReleaseAsync` → cleanup) **before** the final `Close()`, so nothing of ours is queued when shutdown starts.                                                          | **None for save integrity.** Verified by code read (`MainWindow.axaml.cs:153-215`).                                                                                                                                                                   |
| Focus traversal skips non-focusable containers (#21640); Tab-stop search loop fix (#21864).                             | Manual Tab-through in `OpenDropDownOnFocusBehavior`, which works around focus jumping to a `GridSplitter`.                                                                                                          | **Low.** May change Tab order in editors and may make our workaround unnecessary. Test Tab and Shift+Tab through a meeting row.                                                                                                                       |
| Rendering: region dirty-rect clipping off by default (12.1.0); Unicode/text-layout rework (12.0.5).                     | Schedule grid tiles pack text tightly on one line.                                                                                                                                                                  | **Low.** Visual check that grid tile text, ellipsis, and wrapping are unchanged.                                                                                                                                                                      |

**Data integrity:** the upgrade touches no persistence code, schema, or file format. The only
data-adjacent risk is the AutoCompleteBox commit timing above. No migration is needed.

### Regression found in step 1 (2026-10-09)

**Symptom (manual pass, no preferred block length).** In a new meeting:
1. Pick a start time with the mouse, then a length: the **start time blanks**.
2. Pick a start time again: the **length blanks**.

The two fields keep clearing each other. Typed values survive; only mouse picks are lost.

**Root cause (confirmed by stack trace from a temporary probe).**
1. Committing a length called `RefreshStartTimes()`, which clears and refills the Start list.
2. The Start AutoCompleteBox rebuilds its internal dropdown list on any change to its items.
3. After a mouse pick, that internal list still has the picked item selected, even though the
   dropdown has closed. Under 12.1.3 (#20942), clearing it raises "selection lost".
4. The box handles that by setting `SelectedItem = null` and falling back to `Text = SearchText`,
   the last *typed* text. After a mouse pick that is "", and the two-way binding writes "" to
   `StartTimeText`.
5. The same happens to the Length box when a start time is committed.

Only the four Start/Length AutoCompleteBoxes are affected: two in the section editor and two in
the Meetings flyout. No other code of ours consumes Avalonia's `SelectionChanged`.

**Fix: item 22, pulled forward into item 1.** Item 22's rule fixes this too: *a suggestion list is
rebuilt only when its own dropdown opens; commits never touch either list.* Rebuilding when the
dropdown opens is safe. Before raising `DropDownOpening`, the box runs `PopulateDropDown`, which
sets `SearchText = Text`, so any fallback writes the text the box already shows.

**Commit order.** The item 22 fix must not be committed separately after the bump; otherwise
history would contain a commit with broken meeting-time entry.

**Related 12.1.3 effect: checked, harmless.** The same fallback also writes "" to a meeting's
`StartTimeText` / `BlockLengthText` when its boxes are torn down:
- removing a meeting row (`RemoveMeeting`);
- the editor collapsing after Save (`CollapseEditor`).

These writes land on the discarded meeting view model:
- Save writes the section to the database before the editor collapses.
- `RemoveMeeting` unhooks pattern coupling before removing the row.
- Coupling reacts only to committed values, never to field text.

Manually verified: removing the first of three mouse-picked meetings leaves the others intact,
and the saved card shows them.

**Step 1 status (2026-10-09, Windows 10, Debug).** Bump plus item 22 fix.
- **Manual pass done:**
  - meeting-time entry (mouse, typing, Tab) in the section editor and the Meetings flyout;
  - item 22 reproduction with a preferred length;
  - narrowed lists;
  - meeting removal;
  - detach/reattach;
  - Instructors header sort;
  - grid visuals and image export;
  - GroupBox screens;
  - colour picker.
- **Tests:** 1031 passed, 0 skipped, 24 failed. All 24 failures are pre-existing wizard tests,
  broken since `a293b50` on 2026-07-07, when `StepLicenseViewModel` started reading an Avalonia
  asset during construction. Fixed in a separate commit.
- **WASM (2026-10-09, local Release publish):** the demo starts and renders on 12.1.3. Section
  editor ComboBoxes position correctly in a scrolled list, with the step 4 workaround kept.
- **Still to do for item 1:**
  - remote-share / UNC picker checks;
  - macOS;
  - WASM pointer-heavy memory check (optional);
  - ~~the Harmony 10-second test after step 3~~: passed on Windows 10 on 2026-10-09 (commit
    `20e8aab`);
  - **Windows 11:** repeat the 10-second test in a Windows 11 VM. No Win11 machine was at hand
    on 2026-10-09, so step 3 is committed on the strength of the Windows 10 pass. If the crash
    reproduces on Win11, revert the step 3 commit only. Also listed in the release field tests.

### Companion packages

- **Svg.Controls.Skia.Avalonia: hold at 12.0.0.** 12.0.0.17 depends on `Svg.Skia` 5.2.3, which
  requires **SkiaSharp 4.148 / HarfBuzzSharp 14.2**. Avalonia 12.1.3 is built against
  **SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3**. NuGet would resolve the higher major versions and
  give Avalonia's renderer a SkiaSharp it wasn't built for. Revisit when Avalonia moves to
  SkiaSharp 4.
- `AutoCompleteBoxRepro` (investigation project) stays at 12.0.2. It isn't shipped.

### Plan (one step per commit, so each is independently revertible)

1. **Bump only.** All Avalonia packages to 12.1.3, DataGrid to 12.1.2, Browser to 12.1.3. Keep **all**
   workarounds in place. Compile check, full test suite, then manual regression pass.
2. **Remove WORKAROUNDS #3** (DataGrid sort): delete `OnDataGridPointerPressed` and its handler
   registration in `InstructorListView.axaml.cs`, then verify header sort.
   **Correction (2026-10-09):** this also requires setting the Instructors grid back to
   `CanUserSortColumns="True"` (an AXAML edit).
   - It was switched to `"False"` on 2026-05-16 (`0fb957a`) to stop the grid's own in-memory
     sort while `Sorting` was broken.
   - DataGrid 12.1.2 raises `Sorting` only when `CanUserSortColumns && column.CanUserSort`.
     Without the flip, removing the workaround silently disables sorting.
   - So during step 1, header sorting ran through the workaround alone, not "both paths" as
     assumed earlier.
3. **Remove WORKAROUNDS #1** (Harmony): delete `AvaloniaPatches.cs`, the `Apply()` call, the
   `Lib.Harmony` package, and the browser `Compile Remove`. Verify with the 10-second
   AutoCompleteBox wait test on **Windows 10 and Windows 11**. If it reproduces, revert this commit
   only.
4. **(Optional) Trial-remove WORKAROUNDS #2** in the WASM build only if step 1 shows the dropdown
   positioning correctly without the behaviour. Otherwise leave it.
   **Result (2026-10-09): keep the workaround.** Trial run in the local WASM demo (Release
   publish, localhost).
   - **With `SuppressPopupScrollBehavior` (baseline):** the Day and Campus ComboBoxes open
     directly below their boxes in a scrolled list, including when the card heading is scrolled
     out of view.
   - **With it removed:**
     1. Opening the editor scrolled the card heading out of view, breaking the "summary row stays
        visible" rule.
     2. Clicking Campus scrolled the list by about 55 px.
     3. The dropdown opened at the box's pre-scroll position, floating above it with free space
        below.

     That is the original symptom. Upstream #22001 (12.1.2) does not cover our case, and
     #18203 is still open.
   - Reverted, with no commit.
5. Update `WORKAROUNDS.md` to record what was removed and why.

### Verification

- [ ] Compile check and full test suite pass after each step.
- [ ] **Meeting time entry:** in the section editor and the meeting list, for Start Time and Block
      Length: (a) click a dropdown suggestion; (b) type a value and Tab out; (c) type a value and
      click elsewhere; (d) Tab and Shift+Tab across the row. Saved values must match what was
      entered, focus must stay within the row, and the dropdown must close on focus loss.
- [ ] AutoCompleteBox crash test from WORKAROUNDS.md #1 (click a suggestion, wait 10 s) after
      Harmony removal, on Win10 and Win11.
- [ ] Remote DB via the first-run wizard with a long picker session on a UNC path: no freeze.
      File → Open on a remote share: no freeze. Wizard: browse in the picker, then close with X.
- [ ] Detach and reattach all three panels (Schedule, Section, Workload); open and close sticky notes; exit with
      panels detached. No freeze, and no lingering `TermPoint.exe`.
- [ ] Instructors DataGrid: header click sorts.
- [ ] Schedule grid visual pass: tile text, co-scheduled stacks, overlaps.
- [ ] macOS: modal dialogs appear above the main window; dropdowns inside dialogs work.
      Item 19: wheel past the end of a section-editor dropdown; the section list doesn't move
      (native `PopupRoot` path).
- [ ] WASM demo: section editor ComboBoxes in a scrolled list; pointer-heavy use without memory
      growth.

---

## 2 — Bundled SQLite engine security update (NU1903)

`dotnet list package` reports **NU1903, high severity**: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (the
native SQLite engine shipped inside TermPoint) has a vulnerable SQLite dependency
([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q), affects ≤ 2.1.11).

- `Microsoft.Data.Sqlite` 10.0.0 → **10.0.12** brings in `SQLitePCLRaw` **2.1.12**, which is outside
  the affected range.
- **Data integrity:** SQLite keeps its on-disk format backward and forward compatible within
  version 3, so 1.2.2 and 1.2.3 clients can share one database during a staggered rollout. WAL is
  now load-bearing (backup-contention fix), so the verification must include a 1.2.2 client and a
  1.2.3 client using the same network database.
- **Verification:** full test suite (including `[FactRequiresLocalDb]` locally); save, backup, and
  restore round trip; mixed-version checkout and writeback on a shared DB.

**Implemented 2026-10-09.**
- **Advisory:** CVE-2025-6965. In SQLite before 3.50.2, a query with more aggregate terms than
  available columns can corrupt memory.
- **Exposure:** TermPoint's own SQL is fixed, and user input goes in as parameters or JSON. The
  realistic path is a crafted `.db` file whose views or triggers carry the malicious query. Low
  likelihood, but cheap to close.
- **Change:** `Microsoft.Data.Sqlite` 10.0.0 → **10.0.12** in TermPoint, Anonymizer and
  DemoExporter. This resolves the `SQLitePCLRaw.*` packages to **2.1.12**.
- **Bundled engine:** 1.2.2 ships **SQLite 3.49.1** (`2025-02-18`); 1.2.3 ships **SQLite 3.53.3**
  (`2026-06-26`). The WASM build has no SQLite engine and is unaffected.
- **Guard:** `SqliteEngineVersionTests` asserts that `sqlite_version()` is at least 3.50.2, so a
  transitive downgrade fails the build's tests.
- **Results:** the NU1903 warning is gone. Suite: 1057 passed, including the
  `[FactRequiresLocalDb]` real-data tests.
- **Engine comparison on real data (2026-10-09).** Copies of all 52 TermPoint databases on the dev
  machine were checked with both engines. They comprise department DBs, about 40 backups, and
  working copies including a live `-wal`.
  - `PRAGMA integrity_check` returned "ok" on all 52 under both 3.49.1 and 3.53.3, so there is no
    risk of a false "database corrupt, restore?" prompt after upgrading.
  - The app's JSON-function queries return identical results under both engines: `->>`
    ordering and matching, the `isActive` cast, `json_each` instructor assignments,
    `json_extract` course codes and `json_valid`. That covered 19,129 section rows and 18,844
    assignment rows.
- **Manual check:** a save → backup → restore round trip on a DB copy passed.

---

## 3 — Preserve unknown JSON fields across app versions

**Problem.** Entities are stored as JSON in the `data` column. No model has a
`[JsonExtensionData]` catch-all ([JsonHelpers.cs](src/TermPoint/Data/JsonHelpers.cs)), so a client
that doesn't know a field drops it when it re-saves the record. Velopack only applies updates on the
next launch, so mixed versions on one shared DB are normal. Anyone who hasn't restarted silently
strips newer fields (Capacity-style additions) from every record they touch. Nothing is logged.

**Fix direction.**
- Add an extension-data dictionary to every persisted entity, so unknown fields round-trip
  untouched.
- Add a **minimum writer version** stamp to the database. A client older than the stamp opens the
  DB read-only and gets a "please update" message. A release raises the stamp only when it adds
  fields that older clients would destroy.

**Production / data integrity.** Additive; no data migration. Stale keys from fields that were
renamed in the past will now be preserved instead of dropped. That is benign, at a small cost in
size. The gate only works for clients ≥ 1.2.3 (see Sequencing constraints).

**Open decisions.**
- Where the version stamp lives: an existing settings/meta table, or a new table. If new, decide
  which readable business columns it gets.
- Wording and behaviour of the read-only "update required" state.

**Verification.** Round-trip tests per entity: deserialize JSON with an unknown field, re-serialize,
and assert the field survives. Gate test: a DB stamped with a higher version opens read-only.

**Decisions (user, 2026-10-10).**
- **Catch-all:** `[JsonExtensionData]` on every persisted entity **and nested record type**
  (meetings, instructor assignments, reserves, and so on).
  - **Editors that rebuild objects** must carry each original's unknown fields through:
    - the section editor rebuilds `Schedule`, `InstructorAssignments` and `Reserves` from its
      rows;
    - `CloneSection` copies everything;
    - `SectionCloneTests`' reflection guard forces this.
  - **Audit every save path** that builds a fresh object to update an existing record. New records
    (Add, Copy Semester, imports) start with no unknown fields.
- **Minimum writer version** (decisions refined on 2026-10-11; see the step 3b outcome below):
  - **Where:** stored in the existing, previously unused **`AppConfiguration`** key/value table as
    `key = 'MinimumVersionToWrite'` (called `MinWriterVersion` until 2026-10-11),
    `value = '1.2.5'`. It is human-readable as it is, with no new table and no schema change.
  - **1.2.3 ships the CHECK only.** No release raises the stamp yet.
  - **Out-of-date app:** an app older than the stamp opens the database **read-only**, reusing
    the existing reader mode, with a specific message.
- **Message, chosen by an `InstallChannel` value** (new, in `PlatformCapabilities`, worked out
  once at startup):
  - **Store:** packaged, with the Store package identity (family name
    `Academic-Solutions.TermPoint_9nmm6ygzk390e`). *"This database needs TermPoint 1.2.5 or
    later. [Open Microsoft Store], update TermPoint there, then reopen it. Until then you can view
    the schedule but not change it."* The button launches an `ms-windows-store://` link to
    TermPoint's page or to Downloads and updates; verify the exact link on a real install.
  - **DirectDownload (Velopack):** dormant today. When it is reactivated: check for updates
    immediately, then "Update ready — [Restart now]" (`ApplyUpdatesAndRestart`), and the download
    page if the download fails. The "restart TermPoint" wording was **dropped**: the updater
    checks only at launch and applies at the next launch, so one restart isn't always enough.
  - **Sideload (MSIX with a non-Store identity) or Development:** *"Install TermPoint 1.2.5 or
    later from your usual source."*
  - `InstallChannel` will also be useful for BugSnag metadata and support.
- **Microsoft Store update facts** (2026-10-10):
  - Store updates are **not** applied at launch. They install in the background when the app
    isn't running, users can pause them, and IT can control them.
  - Mixed versions are therefore possible among Store users, and across channels. Store
    certification also delays a release by hours to days.
- **Release discipline when a future release raises the stamp:**
  - mark that Store submission **mandatory** in Partner Center;
  - allow time for certification and roll-out;
  - ~~consider raising the stamp lazily, only when the new feature is first used~~ (dropped
    2026-10-11: the stamp is raised per release, by a constant; see the step 3b outcome);
  - Release checklist line: superseded by the deciding question in the step 3b outcome.
- **Parked for later:** in-app Store updates via `StoreContext`, i.e. a periodic check, an
  "Update now" banner and honouring the mandatory flag. They need a Windows-specific build target
  for the MSIX build (parking-lot P7).

**Step 3a outcome (2026-10-10): the catch-all is done.**
- **The base class:** new `Models/PersistedRecord` with
  `[JsonExtensionData] Dictionary<string, JsonElement>? ExtensionData`, plus
  `CopyExtensionDataFrom` / `WithExtensionDataFrom`. It is null when there are no unknown fields,
  so it adds nothing to the JSON.
- **Coverage:** every persisted type derives from it: 16 tables, including `Section` and `Meeting`
  via `SchedulableBase`, plus the nested `SectionDaySchedule`, `InstructorAssignment` and
  `SectionReserve`.
- **Semester** stored its `data` as `{"Color": …}` built ad hoc. It now goes through an internal
  `SemesterData : PersistedRecord` payload. The format is unchanged (still PascalCase `Color`).
- **Save-path audit.** Every path that rebuilds a record now carries the original's unknown fields.
  - Fixed:
    - `CloneSection`;
    - the section editor's meetings (each row keeps its original), assignments and reserves (by
      id);
    - the meeting editor's slots and attendees;
    - grid quick-edit for instructors;
    - the course, subject (both paths), room, instructor, campus, environment-value, code-pattern
      and semester-rename edits;
    - the commitment, release, block-pattern and scheduling-notes editors.
  - Mutate-in-place paths are safe, with control tests.
  - New-record paths start empty by design.
  - **Known edge:** applying a block pattern replaces all meeting rows, so their unknown fields are
    dropped.
- **Tests: +93.** `ExtensionDataRoundTripTests` (reflection over all 21 types, with guards that
  every nested and `Deserialize<T>` type is a `PersistedRecord`), `ExtensionDataRepositoryTests`
  (real schema, every table) and `ExtensionDataSavePathTests` (real view models). `SectionCloneTests`
  now populates all four levels. Removing each carry turns its tests red. Suite: 1315 passed.
- **Existing data is written byte-identically.** Every type's JSON was compared before and after,
  and a read-only check of the real BIOL database (336 sections) found no stale keys and no change
  on re-serialise. The file itself was not modified.
- **Production bug found and fixed alongside (release note):**
  - **Problem:** `CourseListViewModel.Edit` built its editing copy without `Level` and `Capacity`,
    so **every course Apply blanked the course's Capacity and reset its Level override** (1.2.2 and
    earlier).
  - **Fix:** fixed on the same lines, with a test.
  - **Data impact:** capacities already lost are recoverable only from backups.
- **Rule for future code** (recorded in CLAUDE.md, enforced by tests): a new persisted class must
  derive from `PersistedRecord`, and code that rebuilds a record must call `WithExtensionDataFrom`.

**Step 3b outcome (2026-10-11): the `MinimumVersionToWrite` gate is done. 1.2.3 ships the check;
the raising code ships dormant.**

- **Definition** (word for word in the code, here and in CLAUDE.md): *"MinimumVersionToWrite is
  not the database's version, and not the version that last wrote it. It is the oldest TermPoint
  allowed to edit this database; older versions open it read-only. It only ever goes up. It is
  raised only when a release answers Yes to: 'Could the new version save anything the old version
  wouldn't have?'"*
- **Policy (user, 2026-10-11).** The stamp is **not** tied to the app's version numbering.
  - **Rejected:** raising it every release, or tying it to minor-version bumps. Either would lock
    out colleagues who haven't updated after releases that didn't need it, and would tie the
    version number to database rules.
  - **The deciding question for every release:** ***"Could the new version save anything the old
    version wouldn't have?"*** Yes if either of these holds:
    - the same starting database plus the same edits gives a different saved result;
    - the new version offers an edit the old one can't make.
  - **Yes:**
    - section-type colours (a new saved field);
    - a fix that stops an edit wiping a field (1.2.3's Flag/Capacity fixes: same edit, different
      record);
    - a CSV import fix (same file, different records).
  - **No:** card layout, scrolling, a crash fix in a view, performance work.
  - **When Yes:**
    - set `MinimumVersionToWrite.ThisReleaseRequires` to the release's full version, and
      `ThisReleaseReason` to a one-line reason;
    - mark the Store submission mandatory;
    - allow time for certification;
    - **never reset the constants to null or lower them in a later release.** Later releases still
      save the newer data, so they must keep stamping at least that version.
  - **Full procedure:** `UPDATE_NUMBERING_README.md` at the repo root.
  - **Release checklist line:** *"Could this release save anything the previous release wouldn't
    have? If yes, set `MinimumVersionToWrite.ThisReleaseRequires` and `ThisReleaseReason`."*
- **Install channel.** New `InstallChannel` enum: `PlatformCapabilities.InstallChannel`, lazy, and
  logged at startup.
  - **Store:** an MSIX whose package family name is the Store listing's.
  - **Sideload:** any other MSIX.
  - **DirectDownload:** a Velopack install, including the macOS builds.
  - **Development:** everything else, including the browser demo.
  - **Debug override:** Debug builds honour `TERMPOINT_INSTALL_CHANNEL`, for testing; Release
    builds ignore it.
- **The gate.** `CheckoutService` reads the stamp from the local, hash-verified D' at all three
  write-access exits: normal, stale-lock takeover and crash recovery. If the app is older:
  - the lock is released;
  - the lock is **not** polled, so no "Take over" offer can loop back into the gate;
  - D' is discarded. During crash recovery, D' and its marker are kept, so recovery is offered
    again after the update;
  - the session opens read-only on D'', and `RequiredAppVersion` drives the banner.
  - **Model:** the same pattern as the license read-only gate.
  - **Fail-safe rules:**
    - an unreadable file, a missing table or a missing key all count as unstamped, so the gate can
      never lock out a normal database;
    - a stamp that doesn't parse blocks.
- **The stamp, dormant in 1.2.3.**
  - `ThisReleaseRequires` is null, and a test pins it.
  - When it is set, Store and DirectDownload builds raise the stamp in D' (`max(existing, mine)`;
    never lower), plus `MinimumVersionToWrite.Reason`. It then reaches D in the same save as the
    data it protects.
  - Development and Sideload builds never stamp.
  - **Caution:** a locally installed test MSIX with the Store identity counts as Store and would
    stamp. Test such builds on copies only.
- **Banner** (the existing red read-only banner):
  - **Store:** *"Read-only — this database needs TermPoint 1.2.5 or later to edit. In the Microsoft
    Store, click Get updates, then restart TermPoint."*
    - It has an **[Open Microsoft Store]** button that opens `ms-windows-store://downloadsandupdates`.
    - The user checked on a Store install: TermPoint's own product page gave no hint what to do,
      while Downloads and updates has "Get updates".
  - **Other channels:** *"…Install the update from your usual source, then restart TermPoint."*
  - **A stamp that doesn't parse:** "a newer version of TermPoint".
  - **DirectDownload:** the Velopack "[Restart now]" flow stays deferred until Velopack is
    reactivated.
- **Also:** in `RunCheckoutAsync`, a version-gated stale-lock takeover now goes straight to
  read-only, with no misleading "someone else is editing" message and no second copy.
- **Tests: +62.**
  - `MinimumVersionToWriteTests`: the `Blocks` boundaries, `ReadStamp`, `Raise` (never lowers;
    clears a stale reason), the wording, and the null pin.
  - `CheckoutServiceTests` "Group 13": every exit and both outcomes, recovery keeping D', and
    stamping by channel.
  - Suite: 1377 passed.
- **Hand-tested 2026-10-11** on stamped copies of the BIOL database:
  - generic wording, editing disabled, Refresh working, no "Take over", no lock file left;
  - the Store wording and button with the Debug override;
  - a stamp of 1.2.3 opened normally.
- **Known costs and limits:**
  - **A gated open** holds the lock for a few seconds and copies D twice.
  - **Versions older than 1.2.3** ignore the stamp.
  - **If D'' can't be created** after a gate trip, the existing last-resort fallback opens D
    directly. That is item 4's territory.

---

## 4 — Stop opening the shared DB directly; fix startup ordering; single-instance guard

**Problem.**
- **Direct opens change the shared file.** Two paths open the shared file `D` with SQLite:
  - wizard step 1a, "existing database" ([StartupWizardViewModel.cs:307](src/TermPoint/ViewModels/Wizard/StartupWizardViewModel.cs:307));
  - the fallback when a read-only copy can't be made ([MainWindow.axaml.cs:700](src/TermPoint/MainWindow.axaml.cs:700), [:714](src/TermPoint/MainWindow.axaml.cs:714)).

  `DatabaseContext` runs `PRAGMA journal_mode=WAL`
  ([DatabaseContext.cs:130](src/TermPoint/Data/DatabaseContext.cs:130)), which rewrites header
  bytes 18–19, so the hash of `D` changes with no data written. The active editor's next save
  fails "modified outside this session", and autosave stops. That includes the 10-minute autosave
  that runs even with no changes. All later work is recoverable only as "Save a Copy…". The
  fallback path also holds `D` open, so the editor's saves fail outright until that reader closes.
- **Startup touches shared files before checking the lock.** The `D.tmp` cleanup
  ([MainWindow.axaml.cs:538](src/TermPoint/MainWindow.axaml.cs:538)) runs before the lock is
  acquired, so any opener can delete an editor's in-flight save file. Save step 4 doesn't catch
  that ([CheckoutService.cs:1029](src/TermPoint/Services/CheckoutService.cs:1029)), so the
  exception escapes `SaveAsync`.
- **Second instance on the same machine.** The crash-recovery prompt also runs before the lock
  check, so a second instance sees the live session as a crash. Choosing Discard deletes the live
  session's `.dirty` marker, and the startup sweep can later delete the live working copy (the
  "relaunch-while-hung" trap).
- **macOS:** deleting an open file succeeds there. `BackupSqliteDatabase` opens its source with
  the default `ReadWriteCreate`
  ([CheckoutService.cs:2383](src/TermPoint/Services/CheckoutService.cs:2383)), so a deleted
  working copy would be recreated **empty** and pushed over `D`.

**Fix direction.**
- Never open `D` with SQLite. Join and fallback both go through a local copy.
- Run `D.tmp` cleanup and crash-recovery detection only **after** the write-lock check (or under a
  live-instance guard).
- Add a named-mutex single-instance guard keyed on the working-copy path. Acquire it before
  `CleanupOrphanedTmpAsync`, `CleanupStaleCrashArtifacts` and `DetectCrashRecovery`. Apply it on
  **both** paths that run those steps: startup and database switching. Get the release and
  re-acquire order right when switching.
  - If another live process holds the guard, skip all cleanup and recovery and tell the user:
    *"TermPoint is already open on this computer. If it isn't responding, end it in Task Manager
    and start again — your changes will be offered back."*
  - The OS releases a named mutex when its process dies, so a crashed or killed copy never blocks
    a relaunch, while a frozen copy correctly does. Confirm the macOS behaviour matches.
- Fix the read-only banner. When the lock holder is this machine, it currently says another
  TermPoint window is editing, with no hint about what to do if that window is frozen or hidden.
- Catch step-4 failures in `SaveAsync`.
- Add `Mode=ReadWrite` to the `BackupSqliteDatabase` source connection string.

**Production / data integrity.** No migration. This changes the startup sequence, which has a
history of freezes. Run the 3-machine clumsy network protocol.

**Findings and decisions (2026-10-12, before implementation).**
- **Every place `D` is opened directly with SQLite** (re-scan of today's code):

  | # | Path | How often | What it breaks |
  |---|---|---|---|
  | 1 | Wizard join, step 1a (`StartupWizardViewModel` ~313): `InitializeServices(D)` plus `TryAcquire(D)` | Every time someone joins | Header flip, so the editor's saves fail |
  | 2 | Read-only fallback `SetupReadOnlySnapshotAsync() ?? sourcePath` (`RunCheckoutAsync`: stale-lock declined or lost, and `Failed`; now also a version-gate trip whose D'' failed) | Rare | Header flip, and `D` is held open for the whole session |
  | 3 | **New, not in the spec:** new-database creation, `using (new DatabaseContext(D)) {}` in wizard step 3 (~380) and `SwitchDatabaseAsync` (~1007) | Every new database | Opens SQLite over the share and leaves a WAL header on `D` until the first save. No colleague is affected. |
  | 4 | **New, not in the spec:** `CheckoutAsync`'s "D doesn't exist" shortcut runs the whole session on `D` | Only if `D` vanishes between the create/validate step and checkout | A whole editing session over the network |

- **Also confirmed:** `D.tmp` cleanup, the stale-artifact sweep and crash detection run before the
  lock check on both startup (~538) and switching (~1016). Save step 4 (~1209) still doesn't catch an
  exception. The backup source still opens as `ReadWriteCreate` (~2566).
- **Small inconsistency:** switching offers crash recovery even in reader mode; startup skips it.
- **Decisions (user, 2026-10-12):**
  - **A second TermPoint on the same database, same computer: refuse that database** with the
    message above. On a switch, stay on the current database. At startup, show the message and then
    the database chooser, so the user can pick another database or exit (to confirm in the 4b
    design). Opening read-only was rejected: two copies would share the same local snapshot file.
  - **If no local read-only copy can be made,** show *"TermPoint couldn't make a local copy of this
    database"* with **[Try again] [Close]**, handled like the existing "network unreachable" case.
    Never fall back to `D`.
  - **New databases are created locally and then copied to the share,** so "never open `D` with
    SQLite" holds without exception. Shortcut 4 is removed.
  - **A failed database switch ends in a clean "no database open" state, like startup** (user,
    2026-10-12).
    - **Today (pre-existing):** a switch closes the old database's services before checking out
      the new one. If checkout then fails (unreachable, or 4a's Try again / Close, or file not found,
      or a failed checkout), `SwitchDatabaseAsync` just returns. The window keeps showing a database
      that is no longer connected.
    - **New behaviour:** after a failed switch, show the startup database chooser (Open / New /
      Recent) with the reason. Picking a database opens it normally; **Exit** closes TermPoint.
- **Found during 4a hand testing (2026-10-12): a locked database is reported as damaged.**
  - **The cause:** choosing a database (chooser, recent, startup auto-open) first runs
    `DatabaseValidator.ValidateAsync`. That calls a read-only `PRAGMA integrity_check` on `D`, which
    returns false on **any** exception. A healthy file that another program holds exclusively
    therefore shows *"That database failed its integrity check and may be damaged."*
  - **The risk:** the user may then Restore from backup over a good, current `D`.
  - **Decided (user):** fix it now as step 4e. "Can't read it right now" gets its own result and
    wording; only a real integrity failure is called damaged.
  - **Not a problem:** the startup integrity check itself is a brief read-only open, and the handle
    is released (`ClearAllPools` in a `finally`). It is kept, to catch a damaged `D` early.
- **"Open as observer" and new databases (user, 2026-10-12).** A database the user has just created
  (File → New, or wizard step 3) opens for **editing** even with the observer setting on. Observer
  mode still applies when that database, or any other, is opened later. The licence read-only gate
  is not bypassed.
  - Why this came up: 4a removed the "doesn't exist" shortcut, which used to skip both gates for new
    databases.
- **The "Couldn't Open the Database" dialog can't be reached by hand**, because every open runs
  the validator first and a locked file fails there. Its retry logic is unit-tested, and it gets a
  network field-test row (a copy that fails after validation).
- **Step 4a outcome (user-tested 2026-10-12/13).** `D` is never opened with SQLite on any open,
  join, fallback or create path.
  - **Wizard join (step 1a):** uses the new `DatabaseValidator.ValidateWithoutSqliteAsync` (probe
    plus a check of the 16-byte SQLite signature). It starts no services and takes no lock. The real
    open is the post-wizard checkout.
    - Also fixed: `SwitchDatabaseAsync` read `App.Services` without a null check, so every join
      would have crashed once step 1a stopped initialising services.
  - **Read-only fallback:** `CheckoutService.SetupReadOnlySnapshotWithRetryAsync` shows
    "Couldn't Open the Database" with [Try again] [Close]. It never returns `D`.
  - **New databases:** `NewDatabaseFile.CreateAsync` builds the file locally (journal mode DELETE,
    no sidecars), copies it to `D.tmp`, verifies the hash, and moves it to `D` without overwrite.
    The wizard's step 3 now probes first, so Back then Next still works.
  - **Missing file:** `CheckoutOutcome.SourceMissing` gives "Database Not Found". The shortcut that
    ran a session on `D` is gone.
  - **The observer exception:** a `CheckoutAsync(…, ignoreObserverMode)` parameter, used by
    File → New, wizard step 3 and the post-wizard reopen.
  - **Other changes:** `NetworkFileOps.MoveAsync` gained `overwrite`. The tests: +38; suite 1507
    passed.
- **Steps (one commit each):**
  - **4a:** never open `D` with SQLite (paths 1–4), plus the observer exception above. **Done.**
  - **4e (next after 4a):** a locked database is not called damaged. **Done, user-tested
    2026-10-13.**
    - **The three-way check:** `BackupService.CheckIntegrityDetailed` returns Ok, Damaged or
      Unreadable. It is Damaged only when SQLite reports code 11/26/24 or `integrity_check` is not
      "ok"; everything else is Unreadable. A sharing violation gives `SQLITE_CANTOPEN` (14). The bool
      wrapper is unchanged.
    - **Wiring:** `DatabaseValidationResult.Unreadable` and `RecoveryReason.Unreadable`, with their
      own wording at startup, in the chooser (recent, Browse, Continue), in the Restore backup
      picker and in the wizard join. A locked database stays in the recent list.
    - **Found in hand testing: the main window's Recent… menu skipped validation.** A locked file
      leaked the write lock (`.lock` with a live heartbeat until exit), and a technical error was
      shown. Fixed:
      - **Pre-check:** `DatabaseSwitchPrecheck` runs before any teardown in `SwitchDatabaseAsync`.
        A refusal keeps the current database open and leaves settings and recents unchanged.
      - **Checkout:** returns `CheckoutOutcome.SourceUnreadable` and releases the lock when `D`
        can't be read after the lock is taken. In crash recovery it keeps D' and its marker.
      - **Read-only snapshot:** never throws.
    - **Found in hand testing: any switch to a missing file created a new empty database.** Only
      File → New may create now (`createIfMissing`). Recent… / Open / Take over get "Database Not
      Found".
    - **Found in review: a refused File → New would have written its configuration into the
      current database.** Fixed:
      - **Switch result:** `SwitchDatabaseAsync` returns `bool`. `ApplyConfig` runs only on true,
        and a refused switch restores the backup-folder setting.
      - **Existing name:** File → New refuses one with "Database Already Exists". Before, it opened
        the file and added duplicate configuration.
    - **Tests:** +75. Suite: 1582 passed.
    - **Still open (user to decide):**
      - **Header-only check on `D`:** the open-time check could drop from a read-only
        `integrity_check` on `D` to the 16-byte header check, leaving the full check to the local
        copy. It never modifies `D`; its only effect is to block an editor's save rename while it
        reads. Kept as built for now.
      - **Possible step 4f, the working copy:** the same locked-vs-damaged mistake affects the local
        working copy. A locked D' gets the "may be corrupt" dialog with a restore offer
        (`App.InitializeServices` → `DatabaseCorruptException`). In crash recovery, a locked D'
        gets "Unsaved Changes Lost… damaged" plus an attempt to archive it
        (`OfferCrashRecoveryAsync` / `InspectCrashRecovery`).
    - **Left for 4c:** the unguarded hashes and copies in `SaveAsync`.
  - **4d:** failed switch → the database chooser, as above. **Done, user-tested 2026-10-13.**
    - **The flow.** After a failure once the previous database is closed:
      1. The existing failure dialog appears.
      2. `AbandonFailedSwitchAsync` runs. `App.ReleaseServices()` disposes the container and nulls
         `App.Services`. `CheckoutService.ResetAfterFailedOpenAsync()` releases the lock, keeps a
         working copy that has a dirty marker, and clears all checkout state.
      3. The window is covered (`DataContext = null`, curtain raised).
      4. The startup chooser appears with `RecoveryReason.SwitchFailed`: *"TermPoint couldn't open
         the database you chose, and the database you had open has been closed. Choose a database
         to continue."*
      5. The chooser loops until a database opens, or the user chooses Exit or the wizard.
    - **"A database is open" is tracked explicitly** (`_isDatabaseOpen`, set by
      `SetupMainWindowAsync`, cleared at teardown). A pre-check refusal with no database open also
      goes to the chooser.
    - **`SwitchDatabaseAsync` returns false** whenever the originally requested switch failed, so
      File → New never applies its configuration to a database picked from the chooser.
    - **Older data-loss path fixed.** A failed checkout left `WorkingPath` pointing at a stale file
      (a previous database's kept copy, or the target's crash-recovery D'). Exit then deleted it via
      `CleanupWorkingCopy`. `ResetAfterFailedOpenAsync` fixes this. The startup null-checkout path
      now calls it too.
    - **DEBUG-only fault injection:** `TERMPOINT_DEBUG_FAIL_CHECKOUT` = `unreachable` / `missing` /
      `unreadable` / `snapshot`, with `[:count[:skip]]` (`CheckoutFaultPlan`). Release builds ignore
      it. It made 4d and 4a's "Couldn't Open the Database" hand-testable.
    - **Tests:** +77. Suite: 1659 passed.
    - **Notes:**
      - **Banner wording.** It says "the database you had open has been closed" even when nothing
        was open (a chooser retry, or the post-wizard open). An alternative wording was offered; the
        user hasn't asked for it.
      - **Floating windows.** Detached panels and sticky notes are not closed while the chooser is
        up, to avoid the compositor deadlock. They keep stale content, as after any switch.
  - **4b:** a per-database, per-user named-mutex guard taken before cleanup and crash detection, on
    startup and switching, plus the same-computer banner wording. **Done, user-tested 2026-10-13**
    (second copy refused at startup and on a switch; a frozen copy blocks, a killed one doesn't and
    its unsaved changes are restored; normal use unchanged).
    - **The mechanism is an exclusive file handle, not a named mutex.**
      - **What it is:** `DatabaseInstanceGuard` opens
        `%AppData%\TermPoint\working\guards\<SHA-256 of the normalised path>.guard` with
        `FileShare.None` and keeps the handle open while the database is in use.
      - **Why not a mutex:** a mutex is thread-affine, which is fragile with async continuations. A
        handle is released by the OS when the process dies (crash, End Task, power failure).
      - **Per user, local:** it lives in `%AppData%`, so it is per user. It is local, so no SMB handle
        can linger after a crash. The file is never deleted, because its existence means nothing.
      - **Fail-open:** an unexpected error proceeds as before and never locks a user out.
      - **Re-entrant:** "Take over as writer" on the database this process already holds is not
        refused.
    - **Startup order:**
      1. Resolve the database path.
      2. Take the guard. If refused: "TermPoint Is Already Open", then the chooser with
         `RecoveryReason.AlreadyOpen`, looping.
      3. Licence.
      4. The working-folder sweep and the crash check (skipped in observer mode).
      5. Checkout.
    - **Switch order:**
      1. Take the target's guard. If refused: with a database open, stay on it; with none open, go to
         the chooser.
      2. Pre-check.
      3. Tear down the old database.
      4. Release the old guard.
      5. The sweep and the crash check (now also skipped in observer mode, matching startup).
      6. Checkout. A 4d failure releases every guard.
    - **`D.tmp` cleanup moved under the write lock:** it now runs inside `CheckoutService`, only at
      the three WriteAccess exits, after the version gate. Read-only, observer, licence- or
      version-gated, failed and refused opens never touch `D.tmp`, so no open from any computer can
      delete a colleague's in-flight save. A leftover `D.tmp` is simply overwritten by the next
      save.
    - **Restore from backup** releases the guard before relaunching, so the relaunch isn't refused.
    - **Same-computer read-only banner:** "Read-only — {Username} is already editing this database
      in TermPoint on this computer. If that window has stopped responding, it can be ended in Task
      Manager." It is now reachable only by another Windows user on the same PC.
    - **Tests:** +52, including a real cross-process test (a live PowerShell child blocks; once it is
      killed, the guard is free). Suite: 1711 passed.
    - **macOS:** contention is recognised on Unix by `EWOULDBLOCK`. It is unverified on a real Mac;
      the field-test row covers it. If it's misdetected there, the guard does nothing (fail-open).
  - **4c:** catch save step 4; open the backup source as `Mode=ReadWrite`. **Done, user-tested
    2026-10-13.**
    - **File errors in the save pipeline come back as a clean `SaveOutcome.CopyError`**, with the
      normal transient save banner. Covered: step 2 (hash of D, via `HashSourceAsync`), step 3c
      (copy to D.tmp), step 4 (D.tmp verify) and step 5 (rename). Locked or access-denied files
      show "TermPoint couldn't save because the database file is in use by another program or can't
      be accessed." (logged quietly). D.tmp is deleted after a failed verify.
    - **A safety net in `SaveAsync`:** any unexpected exception is sent to BugSnag (`LogError`) and
      shows "The save failed unexpectedly. Your changes are kept." `SaveAsync` no longer throws, so
      the exit save always takes `ReleaseAsync`'s deliberate CopyError path: lock parked, D' and its
      marker kept, "Changes Not Saved", recovery offered next start.
    - **The working copy is never re-created empty.** These now open it with `Mode=ReadWrite` (must
      exist):
      - the save snapshot (`BackupSqliteDatabase` source);
      - the periodic and explicit backup (`BackupService.VacuumIntoFreshConnection`). Before, a
        missing D' would have been shipped as an empty backup, rotating real backups out;
      - `DatabaseContext.ReinitializeConnection` (refresh or demotion).

      A missing working copy gives "TermPoint couldn't save because its local working copy of the
      database is missing." D is never touched.
    - **A test seam** `CheckoutService.BeforeTmpVerifyAsync` (null in production) reaches the
      copy-to-verify window. **Tests:** +12. Suite: 1723 passed.
    - **Not changed (listed):**
      - `RefreshReadOnlySnapshotAsync`'s local hash of D''.tmp (its caller catches; item 11).
      - `SetupReadOnlySnapshotAsync`'s local-only directory/hash calls.
      - Locked-file save failures no longer reach BugSnag (item 7 plans to report handled save
        failures).
    - **macOS residual:** see parking-lot P10.
- **Field test to add:** a named-mutex check on a real Mac (a crashed copy releases it, a frozen
  one holds it).

**Verification.**
- Editor session active, colleague joins via the wizard: the editor's next save succeeds.
- Read-only fallback with `D` unreadable: the editor's saves are unaffected.
- Second instance on the same machine: blocked or redirected, never offered "Restore".
- macOS: delete the working copy under a live session; the save fails safely, with no empty DB
  pushed.

---

## 5 — Make Restore from backup safe

**Problem.** Three restore sites all do a raw, synchronous, non-atomic `File.Copy` onto the
database:
- Save & Backup → Restore
  ([MainWindowViewModel.cs:878-888](src/TermPoint/ViewModels/MainWindowViewModel.cs:878)):
  - not gated on write access, so read-only users can run it
    ([SaveAndBackupView.axaml:174](src/TermPoint/Views/Management/SaveAndBackupView.axaml:174));
  - releases the lock and **then** copies over `D`.
- Database chooser restore
  ([DatabaseChooserViewModel.cs:540-541](src/TermPoint/ViewModels/DatabaseChooserViewModel.cs:540)).
- Startup integrity-failure restore
  ([MainWindow.axaml.cs:1232](src/TermPoint/MainWindow.axaml.cs:1232)).

None keeps a copy of what it replaced. A network blip mid-copy leaves a truncated DB for everyone,
and a reader running a restore wipes out the editor's session. All three copies run **on the UI
thread**, so a downed share freezes the app ("Not Responding") for 30–60 s.

**Fix direction.** One shared restore routine used by all three sites:
- require write access, and hold the lock for the whole operation;
- stream the backup to `D.tmp` via `NetworkFileOps` (off the UI thread, deadline-bounded);
- verify it, then rename it over `D`, reusing the save pipeline's atomic path;
- keep the replaced `D` as a timestamped pre-restore file.

**Findings (2026-10-13 layout).**
- **Save & Backup restore:**
  - discards the editor's unsaved changes;
  - deletes the working copy and disposes services **before** the copy, so a failed copy closes the app with
    the edits already gone.
- **Chooser restore:**
  - overwrites any existing destination with no lock check;
  - its default destination is the **backup's** folder
    ([DatabaseChooserViewModel.cs:526](src/TermPoint/ViewModels/DatabaseChooserViewModel.cs:526)), so it usually
    creates a second database there instead of repairing the shared one.
- **Startup integrity-failure dialog:**
  - since item 4 checks `D` before checkout, it fires only when the **local** copy fails its check (in practice:
    antivirus holding it — 4f);
  - its backup list searches under the local copy's internal name (`<hash8>_<name>_*.db`), so it never finds a
    backup;
  - had it found one, it would have restored over the local copy, not `D`. The restore part is dead code.

**Decisions (user, 2026-10-13).**
- **Pre-restore copy:** beside `D`, named `<name> (before restore yyyy-MM-dd HH-mm).db`, never deleted automatically.
  - That folder is always writable, because the restore writes there anyway, and the whole team can see the file.
  - Backup rotation (`<name>_*.db`) never matches it.
  - To undo a restore, use the chooser's Restore and browse to the file.
- **Chooser default destination:** the database's own location, i.e. the folder of the last database opened.
  - It falls back to the backup's folder when no database was opened before.
  - Step 2 says plainly that the database there is replaced and where the current file is kept.
- **Startup dialog:** remove the dead restore list, and fold in 4f's dialog half: "another program has the local copy
  open — Try again" is told apart from "the local copy is damaged". Crash recovery's "Unsaved Changes Lost" half of 4f
  stays separate.

**Steps.**
- **5a — shared routine plus tests, no UI.** The routine:
  1. stages the backup locally and integrity-checks it, refusing a damaged backup;
  2. keeps a verified pre-restore copy beside `D`;
  3. streams the backup to `D.tmp` and verifies its hash;
  4. checks that the lock is still ours and `D` is unchanged;
  5. renames over `D` with the save's atomic rename, including its check for a rename that lands after its deadline.

  Any failure before the rename leaves `D` as it was. A DEBUG-only `TERMPOINT_DEBUG_FAIL_RESTORE` switch fails a chosen
  stage, for hand tests.
- **5b — Save & Backup → Restore:**
  - writer only: the buttons are disabled for readers and read-only licences;
  - saves first; if that save fails, the restore doesn't start;
  - a "Restoring…" wait dialog blocks edits;
  - success → restart as today;
  - failure → "database unchanged", and the session continues;
  - rename unconfirmed → the app says so and closes. Nothing is lost, because everything was saved first.
- **5c — Chooser restore:**
  - a new destination is created the 4a way;
  - for an existing destination, the routine takes its write lock first:
    - lock held → refused, naming the holder;
    - abandoned lock → refused, with a hint to open normally (which offers take-over) and restore from Save & Backup;
  - then the lock is released and the database opens normally;
  - the default destination is as decided above.
- **5d — Startup integrity dialog:** as decided above. Full plan: "5d/5e plan" below.

**5d/5e plan (user-approved 2026-10-14).** The check on the local copy that runs at open (D′ for a writer, D″ for a
reader), on startup and on switch.

**What happens today (findings).**
- **Locked counts as damaged.** `App.InitializeServices` runs a yes/no check, so a copy that another program
  (antivirus) holds open counts as damaged.
- **The dialog's backup list is dead code** (see Findings above).
- **"Continue anyway (risky)"** opens the failed copy for editing, and every save then goes back to `D`.
- **"Exit" runs the normal exit save on the failed copy.** That save can push a damaged copy over `D`, or, for a locked
  copy, fail and leave the lock file parked (`Suspend`).
- **The switch path shows a raw error with the internal working-copy path:** "Failed to switch to database: Database
  integrity check failed: C:\…\working\…".
- **A damaged `D` is never reported anywhere.**
  - `CheckIntegrityDetailed` logs nothing when `integrity_check` returns problems, and only a `LogInfo` line on
    SQLite codes 11 and 26.
  - The only integrity failure that reaches BugSnag is the backup snapshot, via `LogError(null, <path>)`: a report
    with no exception, which also contains a path.

**5d (behaviour).**
1. **Three-way check at open:** Ok / Locked / Damaged.
   - **Locked:** 3 quiet attempts, 1 s apart (decision), then a new "in use" error.
   - **Damaged:** `DatabaseCorruptException`, carrying SQLite's summary.
   - Also fix the outdated comment in `App` that says the check can run on the shared `D`.
2. **One shared handler** for startup and switch.
   - **Locked:** "Couldn't Open the Database — TermPoint couldn't finish opening the database because another program on
     this computer is using its files, often antivirus or backup software checking a file that was just copied. This
     usually clears in a few seconds." **[Try again] [Close]**.
   - **Damaged:**
     1. A copy holding recovered unsaved changes is set aside (`ArchiveCorruptWorkingCopy`) with the existing "Unsaved
        Changes Lost" message, then step 2.
     2. Re-check `D` (read-only, the front-door check):
        - **`D` damaged:** end the session without saving, show "The database failed its integrity check and may be
          damaged.", then the recovery chooser (Corrupt) with **Restore from a backup**.
        - **`D` locked or unreachable:** end the session, then the front-door messages for those cases.
        - **`D` passes:** make **one** fresh copy (the writer keeps its lock; a reader re-snapshots) and check it
          again:
          - it passes → open normally;
          - it fails → set the copy aside (not deleted), end the session without saving, and show:
            "The shared database is fine, but TermPoint can't open it on this computer: the files it prepares here keep
            failing their safety check. Nothing has been changed, and your colleagues aren't affected. This usually
            points to a problem with this computer, such as a failing disk or security software interfering with
            TermPoint's files. Try restarting the computer. If it keeps happening, contact your IT support and mention
            this message. Meanwhile, you can open the database from another computer." **[Close]**.
   - **Close or giving up always ends the session without saving** (`ResetAfterFailedOpenAsync` semantics).
     - Startup: the app closes, or opens what is picked in the recovery chooser.
     - Switch: the 4d "no database open" chooser, with the damaged reason where it applies.
   - **Removed:** the dead backup list and its `File.Copy`, **"Continue anyway (risky)" (decision)**, and the raw switch
     error.
   - **Unchanged:** the wizard, and crash recovery's own "Unsaved Changes Lost" check.
3. **DEBUG switch `TERMPOINT_DEBUG_FAIL_INTEGRITY`** (`DebugFaultBudget` syntax):
   - `locked[:n]`;
   - `damaged:1` (the fresh copy heals it);
   - `damaged:2` (keeps failing);
   - `shared` (`D`'s re-check reads as damaged).
4. **Tests:**
   - the three-way check, including the retry count;
   - a pure decision table: copy result × recovered changes × `D` result × fresh-copy result → action;
   - the fresh-copy method: the writer keeps its lock, the hash is verified, a copy holding unsaved work is refused;
     the reader snapshot refresh.

**5e (damage reporting).**
- **SQLite summary.** The integrity check also returns the first 5 lines of `integrity_check` output, or SQLite's
  code and message. These name pages and tables, never data.
- **One reporter:**
  - a local log line with the summary;
  - **one BugSnag error per run per kind**, with a named exception and no paths, file names or usernames;
  - kinds: shared database, local copy, backup snapshot, backup being restored.
- **Wired into:**
  - the front-door check on `D`;
  - the 5d local-copy check;
  - the backup-snapshot check, replacing `LogError(null, path)`;
  - the 5a restore routine's backup check.
- **Locked or unreadable stays a local log line only.**
- **Tests:**
  - once per run per kind;
  - no path in the report;
  - a named exception;
  - the backup snapshot goes through the reporter.

**Decisions (user, 2026-10-14):** remove "Continue anyway (risky)"; quiet retry, 3 × 1 s, before the locked dialog.

**Outcome 5d (2026-10-14, user hand-tested the locked, damaged, shared and switch cases).**
- **`LocalCopyIntegrity.VerifyAtOpen`** (called from `App.InitializeServices`) gives the three-way verdict: 3 quiet
  attempts before `DatabaseInUseException`; `DatabaseCorruptException` now carries `Summary`.
  - `BackupService.CheckIntegrityWithSummary` returns the first 5 `integrity_check` lines, or SQLite's code and
    message. `CheckIntegrityDetailed` delegates to it.
- **`LocalCopyCheckDecision.Decide`** is the pure table.
- **`LocalCopyIntegrity.AfterFreshCopy`** maps a fresh copy that stalled or was refused to the front-door messages,
  not "keeps failing".
- **`MainWindow.OpenWithLocalCopyCheckAsync`** is shared by startup and switch. Giving up goes through
  `GiveUpLocalCopyOpenAsync` (`ReleaseServices` + `ResetAfterFailedOpenAsync`) and never saves.
- **Startup** runs its tail as `OpenStartupDatabaseAsync` in a loop (same steps, same order), so a database picked in
  the recovery chooser opens normally.
- **`CheckoutService`:**
  - **`CopyAndVerifyAsync`** is extracted from `CheckoutAsync`. A copy failure of an unexpected kind now releases
    the lock too.
  - **`RefreshLocalCopyAfterFailedCheckAsync`:** the writer keeps its lock, takes the save gate and verifies the
    hash, and refuses if a dirty marker exists. A reader re-snapshots.
  - **`SetAsideFailedLocalCopy`** sets the file aside and never deletes it. Archive names no longer overwrite each
    other.
- **`LocalCopyCheckPrompt`** holds all the texts.
- **`TERMPOINT_DEBUG_FAIL_INTEGRITY`** (`locked`, `damaged`, `shared`).
- **Removed:** `ShowCorruptDatabaseDialogAsync` (the dead backup list, its `File.Copy`, "Continue anyway").
- **Suite:** 2393.
- **Found, to fix after 5e:** `MinimumVersionToWrite.ReadStamp` returns null for a locked or damaged `D′`, so the
  version gate is skipped, and the retry and fresh-copy paths never re-run it. This only matters once a release
  raises the stamp.

**5c layout decisions (user, 2026-10-13).**
- **Confirm before replacing.** When the destination already exists, "Restore and Open" first shows a confirmation in
  the Save & Backup wording ("This replaces the database for everyone who uses it…"). Creating a new file needs no
  confirmation.
- **Default destination, matched by name.**
  - The file name comes from the backup (`<name>_<yyyy-MM-dd_HH-mm-ss>.db` → `<name>.db`); a backup not named that way
    falls back to the last database's name, then the backup's own name.
  - The folder is the last-opened database's folder (the chooser's known path, else `AppSettings.DatabasePath`) only
    when that database has the same name. Otherwise it is the backup's own folder, so a backup of a different
    database can never replace the last database by default.
- **Step 2 text** follows a deadline-bounded probe of the destination: exists → "replaces … for everyone, current file
  kept as …"; missing → "a new database will be created here"; unreachable → "this location isn't responding".
- **Replace path:**
  1. Take the write lock (held → refused, naming the holder, with the take-over hint).
  2. Run the 5a routine.
  3. Release the lock.
  4. Show "Backup Restored", then open normally.
- **Create path:** check the backup the same way, then place the file the 4a way. The `.tmp` → verify → no-overwrite
  move is extracted from `NewDatabaseFile` and shared.
- **While running:** an inline "Restoring the backup…", the chooser's buttons disabled, and its close blocked.
- **Failures:** shown inline, in the RestorePrompt wording; the create path says "Nothing was created.". The user
  stays in the chooser.

**Outcome 5a (2026-10-13).**
- `Services/DatabaseRestore.cs` → `ReplaceAsync`: the nine-stage routine above. It never throws.
  - Every outcome except `Restored` and `RenameUnconfirmed` leaves `D` byte-identical and removes what the run
    created.
  - A name collision never deletes an existing file.
  - A backup with no SQLite header (including an empty file, which SQLite would accept as a valid empty database) is
    refused as damaged.
- `CheckoutService.RestoreFromBackupAsync` wraps the routine:
  - writer only (`NotWriter` otherwise);
  - takes the save's in-flight gate (`Busy` while a save runs, and saves are refused while a restore runs);
  - after `Restored` / `RenameUnconfirmed` it stops autosave and **deliberately leaves `HashAtCheckout` stale**, so
    every later save refuses (`SourceModified`) and the stale working copy can never be pushed over the restored `D`.
    The caller must end the session (5b).
- `TERMPOINT_DEBUG_FAIL_RESTORE` (DEBUG only): `backup | damaged | precopy | copy | verify | lock | rename`, with
  `[:count[:skip]]`. Its parser is shared with `TERMPOINT_DEBUG_FAIL_CHECKOUT` via `DebugFaultBudget`.
- Suite 1904 (+100).
- Notes for 5b:
  - block window close during a restore;
  - an autosave tick during a restore raises the "already in progress" cue;
  - add a TermPoint-schema check on the staged backup.
- Not unit-tested: a timed-out rename that actually landed (no seam).

**Outcome 5b (2026-10-13, user hand-tested A–D on a scratch copy).**
- **`MainWindow.RestoreFromBackupAsync`:** writer only.
  1. Autosave is stopped and the window is blocked from closing.
  2. Under a no-button "Restoring the Backup" wait dialog, it waits for any in-flight autosave
     (`WaitForInFlightSaveAsync`, now internal), saves, and only on success runs the restore.
  3. On a failed save, or any outcome that leaves D unchanged, the user sees a message, autosave resumes and the
     session continues.
  4. On `Restored` / `RenameUnconfirmed`, the backup timer is stopped, the message is shown and the window closes.
     `OnClosing` (`_endSessionAfterRestore`) then releases without saving, closes secondary windows, disposes, and
     always discards the working copy and crash marker. After `Restored` it relaunches TermPoint once the instance
     guard is released.
  5. The window being closed some other way while the result message is up still ends the session this way.
- **`MainWindowViewModel.RestoreFromBackupAsync`** now just forwards to the window. The old `File.Copy` teardown is
  gone.
- **`Services/RestorePrompt.cs`** holds every restore text (approved wording).
- **`Services/RestoreResult.cs`:** the outcome types, browser-safe.
- **New outcome `NotTermPointDatabase`:** the staged backup must contain `Sections`, `Semesters` and `AcademicYears`.
- **`SaveAndBackupViewModel`:** `RestoreCommand` is writer-only (follows `LockStateChanged`). The panel text and
  read-only note are bound in `SaveAndBackupView.axaml`.
- **Suite:** 1951.
- **Known gap (accepted):** detached panels stay usable under the wait dialog. An edit made there during a restore
  that succeeds is lost.

**Outcome 5c (2026-10-14, user hand-tested replace, undo, create, lock held and the `copy:1` fault).**
- **`CheckoutService.RestoreIntoAsync(backup, target, allowReplace)`** restores into a database no session has open.
  - It refuses `Busy` while a write session or the save gate is held, and `Failed` (with `Detail`) when the target is
    the backup itself.
  - It probes the target, then takes one of two paths:
    - **Missing:** `DatabaseRestore.CreateFromBackupAsync` (stages 2–3, then `NewFilePlacement`, which never
      overwrites).
    - **Exists:** takes the write lock on the target (held → `LockHeld` with the holder; a lock file that can't be
      written → `Failed` + `Detail`), hashes the target, runs `ReplaceAsync`, and always releases the lock and awaits
      the lock file's removal.
  - Session state is never touched.
  - `VerifyLockIsOursAtAsync(path, recordNewHolder)` is a mechanical refactor; existing callers are unchanged.
- **`NewFilePlacement`** is extracted from `NewDatabaseFile` (its tests are unchanged).
- **`RestoreDestination.SuggestRestorePath`:**
  - the name comes from a backup name or a "(before restore …)" name, so undoing a restore is natural;
  - the folder is the last database's folder only when the names match;
  - a destination that is the backup file itself is refused.
- **New outcomes:** `Created`, `AlreadyExists`, `LockHeld`.
- **`RestoreContext`:** the chooser wording ("It will open now."; "Nothing was created." on the create path). Save &
  Backup texts are unchanged. Lock-write texts lose the banner's "Read-only — " prefix.
- **Chooser:**
  - a probe-driven step 2 status: replaces / will be created / not responding / is the backup itself;
  - a confirmation only when replacing;
  - `allowReplace` false when step 2 promised a new file;
  - while running, the content and buttons are disabled, "Restoring the backup…" shows, and X/Alt+F4 are ignored;
  - failures show inline.
- **MainWindow's message and choice dialogs** are now static helpers that take an owner.
- **Suite:** 2140.

**Behaviour changes for the release notes:**
- restore is refused while someone else is editing;
- readers can't restore;
- Save & Backup restore keeps current edits in the pre-restore copy;
- a "before restore" file appears beside the database.

**Verification.** A restore by a reader is unavailable. A severed network mid-restore leaves `D`
intact. The pre-restore copy exists and opens. Network field test: cut the link mid-restore with clumsy.

---

## 6 — Strip paths and usernames from BugSnag reports

**Problem.** [PRIVACY.md](PRIVACY.md) states that file paths and personal information are never
sent. But breadcrumbs carry the database `path` and lock holders' Windows usernames
([CheckoutService.cs:487](src/TermPoint/Services/CheckoutService.cs:487),
[:1715](src/TermPoint/Services/CheckoutService.cs:1715)), and error contexts include paths.
Usernames are personal information under BC's FIPPA.

**Fix direction.** A `BeforeNotify` scrubber that redacts paths (keeping useful shape such as
"UNC / local / mapped drive") and usernames from breadcrumbs, metadata and messages. Add a test
that pushes known-sensitive values through the scrubber.

**Open decisions.** Drop usernames entirely, or replace them with a non-identifying marker
("self" / "other user").

---

## 7 — Report handled save/lock failures and unclean exits to BugSnag

**Problem.** These outcomes are handled flows logged at Info, so BugSnag never sees them:
- `SourceModified` save refusals;
- write-lock loss;
- recovery conflicts;
- failed transfers and refreshes.

Hangs aren't exceptions either. `LogError(null, …)` (used for backup integrity failure and "crash
protection degraded") produces a BugSnag report with **zero exceptions**, which the server likely
rejects or shows unnamed.

**Fix direction.**
- Send warning-severity events for the handled outcomes, rate-limited to once per kind per
  session.
- At startup, report "previous session ended uncleanly": a dirty marker was found, or a dead
  session's lock was reclaimed. This is the field detector for hangs and force-quits, including
  the Avalonia compositor deadlock (item 1).
- Give the `LogError(null, …)` call sites a synthetic exception.
- **Log the real exception in `NetworkFileOps.DeleteAsync`** (added 2026-10-09). It currently
  swallows every exception (`catch { return false; }`), and callers log the `false` as "delete
  timed out". A sharing violation therefore looks like a network timeout in the log, as seen in
  `WriteLockService.Release`'s "lock-file delete timed out — abandoning …". Distinguish a timeout
  from an I/O error, and log the exception message.

**Depends on:** item 6.

---

## 8 — Empty / delete semester and academic year: consistent rules, no orphans

**Problem.** Six tables are tied to a semester by `semester_id`:
- `Sections`
- `Meetings`
- `Releases`
- `InstructorCommitments`
- `SchedulingNotes`
- `ProgramWatches`

The three operations treat them inconsistently:

| Operation | Today |
|---|---|
| **Empty Semester** ([EmptySemesterViewModel.cs:150](src/TermPoint/ViewModels/Management/EmptySemesterViewModel.cs:150)) | Deletes **sections only**. Meetings, releases, commitments, notes and watches stay. |
| **Delete semester** ([SemesterManagerViewModel.cs:270](src/TermPoint/ViewModels/Management/SemesterManagerViewModel.cs:270)) | Blocks only if the semester has **sections**. Deleting an emptied semester orphans the other five tables' rows. |
| **Delete academic year** ([AcademicYearListViewModel.cs:191-206](src/TermPoint/ViewModels/Management/AcademicYearListViewModel.cs:191)) | Not guarded. The dialog says the sections "will all be permanently deleted", but only the semesters and the AY are deleted. **Everything** in all six tables is orphaned, along with the AY's `LegalStartTimes`. |

Symptoms of AY-delete orphans:
- "can't delete course: it has sections", with nothing visible;
- "instructor has assigned workload", with nothing visible;
- orphan rows in Course History and Workload History.

Orphans from semester delete are mostly invisible, because those tables are only queried by
semester. They are still dead weight in every copy and backup.

**Decided rules (2026-10-08).**
1. **Emptying a semester deletes everything attached to it:** sections, meetings, releases,
   commitments, scheduling notes and program watches. It runs in one transaction. The existing
   "can't empty the currently loaded semester" guard stays.
2. **A semester can only be deleted once it is empty**, meaning no rows in any of the six tables.
   If it isn't empty, say what remains (for example "12 sections, 3 meetings, 2 releases") and
   point to Empty Semester.
3. **An academic year can only be deleted if every one of its semesters is empty.** If not, list
   the non-empty semesters and point to Empty Semester. The AY delete then removes its (empty)
   semesters, its `LegalStartTimes` rows and the AY itself, in one transaction. The confirmation
   text must no longer promise to delete sections.

**Fix direction.**
- **One emptiness rule.** A single `SemesterContentsService` (or similar) owns:
  - `CountContents(semesterId)`: per-table counts, used by all three UIs;
  - `IsEmpty(semesterId)`;
  - `EmptySemester(semesterId)`: the transactional delete across the six tables.
- **Repository additions.** Add `DeleteBySemesterId` and `CountBySemesterId` where missing
  (Releases, InstructorCommitments, SchedulingNotes, ProgramWatches). Add the same to the **Demo**
  repositories for the WASM build. Add `DeleteByAcademicYear` to `LegalStartTimeRepository`.
- **Confirmation text.** The Empty Semester confirmation lists the per-type counts that will be
  deleted.

**Production / data integrity.**
- **Behaviour change for existing users.** A semester someone already "emptied" under 1.2.2 still
  has its meetings, releases, commitments, notes and watches. Under the new rule it is **not empty**,
  so Delete is refused until they run Empty Semester again. The refusal message must make that
  obvious by listing what remains.
- **Existing orphans: decided to leave them (2026-10-08).** Production databases may already
  contain rows whose semester or AY no longer exists, from past AY deletes and emptied-then-deleted
  semesters. No cleanup, no detection, no user prompt. The new rules stop any more being created.
  - **Known consequence:** for users who previously deleted an academic year with sections in it,
    these symptoms persist:
    - "can't delete course: it has sections" with nothing visible;
    - "instructor has assigned workload" with nothing visible;
    - orphan rows in Course History and Workload History.
  - **Silent mitigation (accepted 2026-10-08):** cross-semester queries ignore rows whose
    semester no longer exists. Nothing is deleted and the user is not asked anything. See
    "Orphan-tolerant queries" below.
- **Edge case.** Deleting the AY that contains the currently loaded semester (even an empty one).
  Block it, or switch semesters first, as Empty Semester already does for the loaded semester.

**Decided:** program watches are included in "everything attached" (confirmed 2026-10-08).

**Decisions and findings (2026-10-11, before implementation).**
- **Empty Semester deletes everything, as decided, re-confirmed with the risk in view.** Someone who
  empties a semester to re-import its sections now also loses its meetings, releases, commitments,
  notes and watches. A "sections only" option was offered and declined. The confirmation must list
  every count.
- **Loaded-semester guard, for both deletes.** Refuse to delete a semester that is currently loaded,
  or an academic year containing one, and say to switch semesters first. This matches Empty
  Semester's existing guard.
- **Bug found:** Empty Semester's loaded-semester guard checks only the *first* selected semester
  (`SelectedSemesterDisplay`). In multi-semester mode another loaded semester can be emptied while
  it is on screen. All three guards must check every selected semester.
- **Re-scan (2026-10-11):** the orphan-tolerant table above is still complete. No other query spans
  all semesters of the six tables.
- **Steps (one commit each):**
  - **8a:** repository methods (and their demo versions), `SemesterContentsService` with counts,
    emptiness and the two transactional deletes, and tests. No visible change.
  - **8b:** the three screens use it, with the guards and messages. This needs one binding change in
    `EmptySemesterView.axaml`.
  - **8c:** the four orphan-tolerant queries, with tests on seeded orphan rows.

**Step 8a outcome (2026-10-11, e90f834).**
- Added `SemesterContentsService` and `SemesterContents`, plus repository count and delete methods,
  and their demo versions.
- Tests: +49.

**Step 8b outcome (2026-10-12): the three screens use the service.**
- **Empty Semester:**
  - The screen lists everything the semester holds: *"This semester contains 17 sections, 2
    meetings."*
  - The confirmation names the year and semester in bold: *"Permanently delete everything in
    **2026-2027 Early Summer**: 17 sections, 2 meetings? This cannot be undone."* The button is
    "Delete Everything".
  - After emptying it says *"Done. Everything has been removed from Winter."* The user dropped a
    second sentence about deleting the semester, because it read as if the semester were an
    academic year.
  - The button is disabled when the semester is empty.
  - The loaded check covers every selected semester (the bug fix).
- **Delete semester:**
  - Refused while anything remains, e.g. *"Winter" still contains 2 releases. Use Empty Semester to
    remove everything before deleting it.*
  - Refused while the semester is loaded.
- **Delete year:**
  - Refused while any semester holds anything; the message names each one.
  - Refused while the year is loaded. The wording is about the year (user, 2026-10-12): *"2025-2026"
    is currently loaded in the main view. Switch to a different academic year there before deleting
    it.*
  - The confirmation no longer promises to delete sections: *"Delete academic year "X" and its 5
    empty semesters?"*
  - After a year is deleted, the main view's year picker falls back to a remaining year (tested).
- **Academic Years toolbar (user, 2026-10-12):**

  | Button | Hover help |
  |---|---|
  | Add academic year | (none) |
  | Delete year | "Each semester must be empty before deleting an academic year" |
  | Copy semester | "Copy sections from one semester to another" |
  | Empty semester | "Remove everything from one semester: sections, meetings, releases, commitments, notes and watches" |

- **Layout:**
  - The Copy Semester and Empty Semester panels get the standard `10,0` margin.
  - The Academic Years refusal message lines up with the year list.
- **Removed:** `SectionRepository.CountByAcademicYear`, which nothing uses any more.
- **Tests:** +28 (`SemesterContentsScreensTests`). Suite: 1454 passed.
- **Wording (user, 2026-10-12):** "program watches", not "watches", in the contents list and the
  Empty semester hover help.

**Step 8c outcome (2026-10-12): the orphan-tolerant queries are in.**
- **The filter.** One shared fragment, `OrphanFilter.SemesterStillExists`
  (`semester_id IN (SELECT id FROM Semesters)`), is applied to four queries:
  - `SectionRepository.GetAll()`;
  - `SectionRepository.GetByCourseId`;
  - `CourseRepository.HasSections`;
  - `InstructorRepository.HasSections`.

  Each one's XML doc says it excludes orphans. The change is read-only: no writes, schema change or
  migration. `Semesters.id` is the primary key, so the extra cost is an index probe per row.
- **Re-scan:** no other query spans all semesters of the six tables. The demo repositories are
  unchanged, because the WASM demo can't create orphans.
- **The spec over-claimed some symptoms.** The real effects:
  - **Fixed:**
    - the course and instructor delete guards ("it has sections" / "assigned workload" with nothing
      visible);
    - a course's apply-tags-to-all-sections, which rewrote orphan rows.
  - **The room and scheduling-environment delete sweeps** were never blocked by orphans, but they
    rewrote orphan rows' JSON and blanked their `semester_name` / `academic_year_name`. That stops
    now.
  - **Course History and Workload History** already hid orphans by joining against existing
    semesters. Unchanged.
  - **Shared-schedule export** already filtered by the selected semesters. Unchanged.
- **Known consequence (accepted):** deleting a course or instructor now succeeds while orphan rows
  still reference it. Nothing reads those rows; a future clean-up or export of orphans would need to
  allow for the dangling ids.
- **Tests:** +15 (`OrphanTolerantQueryTests`).
  - Orphans are made as the old academic-year delete made them.
  - Every orphan row is byte-identical afterwards.
  - The real course, instructor, room and tag screens are exercised.
  - A mutation check failed 10 of the 15, as expected.
  - Suite: 1469 passed.

**Orphan-tolerant queries (silent mitigation).** Queries scoped to one semester are already safe,
because orphans can't match a semester that exists. The exposure is the queries that span
**all** semesters. Add `AND semester_id IN (SELECT id FROM Semesters)` (or the equivalent join)
at the **repository** level, so every caller benefits:

| Repository method | Callers it fixes |
|---|---|
| `SectionRepository.GetAll()`, no-arg ([SectionRepository.cs:11](src/TermPoint/Data/Repositories/SectionRepository.cs:11)) | Workload History ([WorkloadHistoryViewModel.cs:77](src/TermPoint/ViewModels/Management/WorkloadHistoryViewModel.cs:77)), room delete guard ([RoomListViewModel.cs:235](src/TermPoint/ViewModels/Management/RoomListViewModel.cs:235)), scheduling environment delete guard ([SchedulingEnvironmentListViewModel.cs:217](src/TermPoint/ViewModels/Management/SchedulingEnvironmentListViewModel.cs:217)), shared-schedule export ([SharingViewModel.cs:174](src/TermPoint/ViewModels/Management/SharingViewModel.cs:174)) |
| `SectionRepository.GetByCourseId` ([SectionRepository.cs:36](src/TermPoint/Data/Repositories/SectionRepository.cs:36)) | Course list ([CourseListViewModel.cs:133](src/TermPoint/ViewModels/Management/CourseListViewModel.cs:133)), Course History ([CourseHistoryViewModel.cs:57](src/TermPoint/ViewModels/Management/CourseHistoryViewModel.cs:57)), Course History export ([CourseHistoryExportViewModel.cs:95](src/TermPoint/ViewModels/Management/CourseHistoryExportViewModel.cs:95)) |
| `CourseRepository.HasSections` ([CourseRepository.cs:41](src/TermPoint/Data/Repositories/CourseRepository.cs:41)) | Course delete guard and course identity lock ([CourseListViewModel.cs:191](src/TermPoint/ViewModels/Management/CourseListViewModel.cs:191), [:201](src/TermPoint/ViewModels/Management/CourseListViewModel.cs:201)) |
| `InstructorRepository.HasSections` ([InstructorRepository.cs:91](src/TermPoint/Data/Repositories/InstructorRepository.cs:91)) | Instructor delete guard ([InstructorListViewModel.cs:383](src/TermPoint/ViewModels/Management/InstructorListViewModel.cs:383)) |

- Each changed method's XML doc must state that it excludes rows whose semester no longer exists.
- Before changing them, re-grep for any other cross-semester query on the six semester-scoped
  tables. The list above comes from a 2026-10-08 scan.
- **Production note:** the mitigation also stops orphaned sections from leaking into
  shared-schedule exports and room / scheduling-environment delete checks. These were further
  symptoms of the same orphans, beyond the ones the review named.

**Verification.**
- Unit tests:
  - `EmptySemester` removes rows from all six tables for that semester only, and leaves other
    semesters untouched;
  - semester delete is refused while any table has rows;
  - AY delete is refused while any semester is non-empty;
  - AY delete removes its semesters and `LegalStartTimes`;
  - a failure mid-empty rolls back;
  - with seeded orphan sections (semester row missing), each repository method in the
    orphan-tolerant table excludes them, and course, instructor, room and environment deletes are
    no longer blocked by them.
- Demo repositories behave the same.

---

## 9 — Make stale-lock takeover safe

**Problem.**
- After about 3 minutes asleep, a laptop's lock looks abandoned. The takeover prompt
  ([MainWindow.axaml.cs:671](src/TermPoint/MainWindow.axaml.cs:671)) doesn't warn that the holder
  may have unsaved work.
- `ForceAcquire` doesn't re-check staleness at click time, so if the dialog sits open while the
  sleeper wakes, it takes over a live lock.
- Staleness compares the reader's clock against the writer's timestamp
  ([WriteLockService.cs:769](src/TermPoint/Services/WriteLockService.cs:769)), so clock drift over
  3 minutes makes a live lock look dead.

Loss is bounded to about one autosave interval (10 min).

**Fix direction.**
- Re-check staleness immediately before taking over.
- Judge staleness by the heartbeat **not changing** over a locally measured interval, not by
  comparing clocks.
- Add a warning in the prompt that the other person may lose unsaved changes.

**Trade-off.** Stricter takeover means users wait longer behind a genuinely dead lock. Keep the
added wait short.

**Clarified with the user (2026-10-13).** Takeover of an abandoned lock stays possible. A laptop that
sleeps, or someone who leaves on vacation, must never lock everyone else out. Item 9 only makes the
takeover safer.

**Steps (one commit each):**
- **9a:** the warning and a re-check at the click. **Done, user-tested on one machine 2026-10-13.**
- **9b:** staleness judged by an unchanged heartbeat over locally measured time, not by comparing
  clocks. **Done, user-tested on one machine 2026-10-13** (dead lock, a live writer with its clock 1 h
  behind is never taken over, a dead writer with its clock 1 h ahead is found by polling).
  - **Polling (read-only sessions) is clock-free.** A lock is stale only when the same
    (SessionGuid, Heartbeat) pair has been seen unchanged for at least 180 s of local monotonic time
    (`LockHeartbeatWatch`, `TimeProvider`). A missing lock is available immediately.
  - **Open path:** the clock comparison is only a hint for showing the prompt at once. The takeover
    also needs the snapshot to have been unchanged for at least `StaleConfirmationSeconds` = 70 s
    (heartbeat interval + 10). Otherwise "Checking the Lock" waits out the remainder and can be
    cancelled; cancelling equals Open read-only. Then 9a's re-check runs.
  - **Observations carry over** between the prompt, read-only polling and a later "Take over as
    writer". "N minutes" falls back to "for a while" when the clocks give nonsense.
  - **Decided:**
    - A garbled lock is offered after 180 s unchanged, not instantly, so one bad read can't create
      an offer.
    - The 70 s wait is enforced by the UI (its only caller). 9a's re-check stays the hard guard.
  - **Residual risk:** a live writer with a flaky network *and* a clock running behind can be taken
    over after about 70 s of missed heartbeats. That is still stricter than before. The overtaken
    writer keeps its unsaved work (9a).
  - **Tests:** +48. Suite: 1804 passed.

**Step 9a outcome.**
- **The prompt** (`StaleLockPrompt`): title "Database Lock Is Inactive", buttons [Take over] [Open
  read-only]. The text: "{User} on {Computer} hasn't been active in this database for N minute(s).
  Their computer may be asleep or disconnected. If you take over, changes they haven't saved yet
  can't go into the database — they'll only be able to save them as a separate copy." Unknown,
  corrupted or missing fields fall back sensibly.
  - **The "separate copy" promise was verified.** The overtaken writer is demoted with D' and its
    marker kept. At their next write-access open, "Restore Unsaved Changes?" either restores (when
    D is unchanged) or offers "Save a Copy…".
- **The re-check at the click:**
  - `CheckoutAsync` keeps a single-use snapshot of the holder it judged stale.
  - `ForceAcquire(expected)` re-reads the lock immediately before deleting and deletes only if the
    `SessionGuid` and `Heartbeat` are unchanged.
  - A missing lock is just created. Anything else (heartbeat advanced, another session, unreadable,
    newly garbled) returns `CheckoutOutcome.HolderActiveAgain`: "Database Is In Use Again: {User}
    is active in this database again, so it will open read-only." The lock is untouched.
  - **A lock that was already garbled** when judged stale stays takeable.
  - **Residual race:** milliseconds between the re-read and the delete. There is no atomic
    compare-and-delete on a share. Before, the window was the whole time the dialog was open.
- **Polling gap closed:** a read-only session reached from the stale prompt (declined, active again,
  lost race) now polls via `CheckoutService.StartReaderPollingAsync`, so a later release offers
  "Take over as writer".
- **Tests:** +33. Suite: 1756 passed.
- **Integration test required (user):** the 3-computer row in "Release testing: network field
  tests".

---

## 10 — "Save aborted" dead end: give a next step

**Problem.** "The database was modified outside this session. Save aborted." offers no action.
Autosave quietly stops, and the user keeps working into a dead end.

**Fix direction.** A persistent banner that says autosave has stopped, explains what it means, and
offers **Save a Copy now**. Item 4 removes the most common trigger, but this state can still occur.

---

## 11 — Reader Refresh: report failures honestly and recover the connection

**Problem.**
- A failed refresh only goes to the log, yet the tooltip says it refreshed just now
  ([MainWindowViewModel.cs:749-764](src/TermPoint/ViewModels/MainWindowViewModel.cs:749)).
- If the overwrite fails after the connection is closed (for example, Defender scanning the file),
  the connection is never reopened
  ([CheckoutService.cs:1198-1221](src/TermPoint/Services/CheckoutService.cs:1198)). The reader's
  session is dead until restart.

**Fix direction.** Show a refresh failure in the UI with the last *successful* refresh time.
Always reopen the connection on the prior copy when the overwrite fails.

---

## 12 — Save-specific error banner for exceptions during save

**Problem.** Save failures that throw (rather than returning an outcome) show the generic
"Unexpected error" banner.

**Fix direction.** Route save-path exceptions to the save-error banner, with wording consistent
with item 10.

---

## 13 — Cloud-sync folder detection gaps

**Problem.** `FolderAssessor`
([FolderAssessor.cs:352](src/TermPoint/Services/FolderAssessor.cs:352)) misses the likeliest
university setups:
- SharePoint/Teams libraries synced through OneDrive;
- Google Drive for desktop;
- Box;
- everything on macOS (`~/Library/CloudStorage`).

On a synced folder the lock file isn't atomic across machines, so two people can both get write
access. The save hash check turns that into stranded work rather than corruption.

**Fix direction.** Extend detection to these providers on both platforms.

**Open decisions.** Warn or block. Recommendation: warn, because false positives on legitimate
network shares would lock people out.

---

## 14 — Unapplied editor changes are lost on exit or database switch

**Problem.** Edits in an open inline editor (section, meeting, and so on) live only in the view
model until the user clicks Apply. `MainWindow.OnClosing`
([MainWindow.axaml.cs:153](src/TermPoint/MainWindow.axaml.cs:153)) doesn't check for an open
editor, so a normal close silently discards them. The hang review rates this the **most likely
real data loss** in the app. A frozen app also loses open-editor content, since it is only in
memory.

**Fix direction.** Before closing or switching databases, detect an open editor with unapplied
changes and ask: **Apply / Discard / Cancel**.
- Apply runs the editor's normal validation and commit path.
- Validation failure keeps the window open, with the editor showing its error.
- Requires a common "has unapplied changes" contract across the editors. Inventory every inline
  editor first.

**Open decisions.** Always ask, or auto-apply silently when the changes are valid.

**Out of scope.** Protecting unapplied edits against a hang or crash would need draft persistence,
which is a separate feature.

**Verification.** For each editor type: edit, close the window, and confirm all three choices
behave correctly. Same for database switch. A closed editor (no changes) produces no prompt.

---

## 15 — Exit-path hardening and the mid-save edit gap

**Problem.** These share one root cause: an edit or a click arrives while a save is running.
- **A second close kills the exit save.** `OnClosing` cancels the first close and runs the final
  save with the window still open. A second click on X hits `if (_shuttingDown) return;`
  ([MainWindow.axaml.cs:157](src/TermPoint/MainWindow.axaml.cs:157)), which lets the close through
  and ends the process mid-save. The next launch shows "Restore unsaved changes?", or the
  "export a recovered copy" prompt if it died just after the final rename. On a slow link the exit
  save takes 5–60 s, with only the small footer "Saving…" as feedback, so users do click again.
- **Edits made during the exit save are deleted.** The window stays editable during the save, and
  `CleanupWorkingCopy` then deletes the working copy that holds those edits.
- **Mid-save edit gap (any save).** After a successful save, `SaveAsync` sets
  `SessionDirty = false` and deletes the dirty marker
  ([CheckoutService.cs:1093-1105](src/TermPoint/Services/CheckoutService.cs:1093)) without
  checking whether an edit landed after the save's snapshot. That edit exists only in the working
  copy, with no marker and the "Unsaved changes" indicator off. The next save picks it up, but a
  crash before then loses it.

**Fix direction.**
- **Exit:** ignore repeated close requests while the exit save runs, and block input with a
  clear "Saving and closing…" state.
- **Edit generation counter:** bump it on every `MarkDirty`. `SaveAsync` captures it at snapshot
  time and clears `SessionDirty` and the marker only if it is unchanged; otherwise both stay set.
- **On exit:** if the counter moved during the final save, run another save pass, or keep the
  working copy and marker for restore. Never delete them.

**Production / data integrity.** This is the core of the save pipeline. The order save → release
lock → dispose container → cleanup must be preserved. No migration.

**Verification.**
- Unit tests: an edit between snapshot and marker delete leaves the marker and `SessionDirty` set.
- Field (network test protocol): slow-link exit save; click X repeatedly; attempt edits during the
  save. Expect no early exit and no lost edits.

---

## 16 — Remaining network I/O on the UI thread

**Problem.** These raw calls run synchronously on the UI thread, so a downed share freezes the app
for 30–60 s (SMB timeout) before an error:
- new-database folder creation
  ([MainWindow.axaml.cs:995](src/TermPoint/MainWindow.axaml.cs:995); check
  [StartupWizardViewModel.cs:368](src/TermPoint/ViewModels/Wizard/StartupWizardViewModel.cs:368)
  for the same pattern);
- recovered-copy export
  ([MainWindow.axaml.cs:1425](src/TermPoint/MainWindow.axaml.cs:1425)).

The restore copies are covered by item 5.

**Fix direction.** Route them through `NetworkFileOps` with a deadline, off the UI thread, sharing
item 5's helper.

---

## 17 — CSV import blow-up on an unmatched quote

**Problem.** When a CSV has one unmatched `"`, `ReadCsvLine` re-joins and rescans every later line
on the UI thread. This is quadratic: a large file freezes for seconds to a minute before reporting
a parse error. The routine is duplicated in
[CsvImportParser.cs:360](src/TermPoint/Services/CsvImportParser.cs:360) and
[SharedScheduleCsvParser.cs:277](src/TermPoint/Services/SharedScheduleCsvParser.cs:277).

**Fix direction.** Extract one shared CSV line reader. Scan linearly. On an unterminated quote at
end of file, fail fast with the line number where the quote opened.

**Verification.** Unit tests: an unmatched quote in a 10k-line file fails quickly with the correct
line number. Existing CSV import and shared-schedule tests still pass.

**Outcome (2026-10-09).** One shared `CsvRecordReader` replaces both copies.
- **How it works:**
  - It scans linearly, carrying the in-quotes state across physical lines.
  - It returns each record with its physical start line, which is now also the line used in
    warnings and errors.
  - Its quote rules mirror `ParseCsvRow` exactly. A `"` opens a quoted field only at the start of
    a field, `""` inside quotes is an escape, and mid-field quotes are literal. This was verified
    by an exhaustive differential test of the reader against `ParseCsvRow` (97,656 short inputs).
- **The freeze (this item):** 10k lines with an unmatched quote went from about 11 s to under
  10 ms. The new error reads "Line N: Unmatched quote. The file may be malformed or truncated."
  The shared-schedule import previously *accepted* such a file and created a bogus section whose
  Notes field held the rest of the file.
- **Two latent data-loss bugs found and fixed.** Both were in production in both importers, and
  both were proven by tests that failed on the old code.
  1. **Wrong quote parity.** The old line joiner skipped `""` pairs before testing for an odd quote
     count. A field with an odd number of escaped quotes (`"12"" ruler"`), or an empty `""` field,
     made it glue the **next row** into that field, so the row was silently lost.
  2. **Stray quotes.** Unquoted fields with inch marks on two different lines (`5" ruler` …
     `3" tube`) merged every row between them into one record, and those rows were lost.
- **Remaining inherent ambiguity:** a `"` at the very start of a field that was never meant to open
  one still joins lines until a later quote closes it. `ParseCsvRow` would read it the same way.
  At EOF it fails fast with the record's start line.
- **Production / data:**
  - Files that previously imported with silently merged or swallowed rows now import every row.
  - Files with a genuinely unmatched quote are now rejected with a clear message instead of
    importing garbage.
  - No stored data changes.

  Tests: 52 new, 1109 total passing.

---

## 18 — Wizard Cancel: hide instead of close (defence in depth)

**Problem.** Wizard Cancel calls `_window.Close()`
([StartupWizardViewModel.cs:135](src/TermPoint/ViewModels/Wizard/StartupWizardViewModel.cs:135)).
On Avalonia 12.0.2 that can deadlock right after a long network picker session. Item 1 fixes the
root cause.

**Fix direction.** Apply the same hide-and-signal pattern used for wizard finish. Optional: do it
only if it's trivial once item 1 has landed.

**Outcome (2026-10-09): already covered by existing code. Doc comment fixed only.**
- The `Close()` call never closes the window. `StartupWizardWindow`'s `Closing` handler intercepts
  every close request (Cancel, X, Alt+F4) and:
  1. sets `e.Cancel = true`;
  2. calls `Hide()`;
  3. calls `Shutdown()` once (re-entry guarded) when setup is incomplete.
- The window is therefore never disposed mid-session. That is the same hide-not-close rule as
  Finish.
- The deadlock is also fixed upstream (item 1, #21591).
- Cancel is enabled only on the welcome step.
- The field-test row ("wizard Cancel after browsing") remains as the runtime check.

---

## 19 — Mouse-wheel past the end of a dropdown scrolls the section list underneath it

**Reported behaviour.**
1. With a section open for edit, open the Instructor(s) dropdown.
2. Wheel down to the bottom of its list. This works.
3. Wheel a little more, as users easily do. The **section list** scrolls instead, and the dropdown
   stays fixed on screen while its section slides away beneath it.

Selections are still reachable, so nothing is blocked, but it is irritating.

**Cause.** Avalonia routes events from popup content back out through the owning control, so a
wheel event inside a dropdown bubbles up into the section list's `ScrollViewer`.
- The dropdown's own `ScrollViewer` marks the event handled only while it can still move. At the
  end of its range it lets the event go ("scroll chaining"), and the outer list scrolls.
- If the list is short enough not to scroll at all, **every** wheel tick goes to the outer list.
- On desktop the popup is a separate native window positioned once when it opens, so it doesn't
  follow its anchor when the list moves.

**Scope.** Every dropdown inside a scrolling area has this, not just Instructor(s). In the section
editor alone ([SectionListView.axaml](src/TermPoint/Views/Management/SectionListView.axaml)):
- about 10 ComboBoxes;
- 2 AutoCompleteBoxes (Start Time, Block Length);
- 4 custom `Popup` + `ScrollViewer` pickers using the `EditorPopupTrigger` toggle: instructors,
  tags, reserves and resources.

The meeting list and the grid filters use the same patterns. The Avalonia 12.1.x release notes
contain no fix, so item 1 won't resolve it.

**Fix direction: contain wheel events inside popups, app-wide.**
- One static behaviour class (same pattern as `SelectionCommandBehavior`'s static class handler),
  registered once at startup. It adds a class handler for `PointerWheelChangedEvent` on the popup
  host types and marks the event **Handled**:
  - `PopupRoot` (native popups, desktop);
  - `OverlayPopupHost` (overlay popups, WASM).
- The dropdown's own `ScrollViewer` sits deeper in the route, so it still scrolls normally. The
  handler only stops the leftover event from escaping the popup.
- This covers ComboBox, AutoCompleteBox, our custom pickers and any future popup, with **no AXAML
  edits**. A wheel inside any popup never scrolls what's behind it.
- **Alternative, if app-wide is too broad:** an attached behaviour set via a style only on
  editor dropdowns. That means more wiring for the same result. Recommended: app-wide.

**Production / data integrity.** None. Input-routing change only.

**Related case: not a problem (checked by the user 2026-10-08).** With Instructor(s) open,
wheeling anywhere **outside** the dropdown does nothing. That is the desired behaviour, so the
leak happens only from inside the popup. No change needed; keep it as a regression check.

**Verification.**
- In the section editor, for a ComboBox, an AutoCompleteBox and each custom picker:
  - wheel past the end of the list: the section list doesn't move;
  - wheel over a list too short to scroll: nothing moves;
  - with no dropdown open, wheel over the list: it scrolls normally;
  - with a dropdown open, wheel outside it: nothing moves (unchanged from today).
- Grid filter popups and meeting-list dropdowns still scroll internally.
- Repeat in the WASM demo.

**Outcome (2026-10-09): done as recommended (app-wide).**
- **Code:** a new [PopupWheelContainment](src/TermPoint/Behaviors/PopupWheelContainment.cs) static
  class, registered once in `App.OnFrameworkInitializationCompleted` outside the `#if`, so the WASM
  demo gets it too. No AXAML edits.
- **Routing confirmed against the Avalonia 12.1.3 source:** both `PopupRoot` and `OverlayPopupHost`
  set `InteractiveParent` to the `Popup`, so one class handler on each host type catches every
  popup. Windows runs with `OverlayPopups = true`, so it uses `OverlayPopupHost`, as WASM does;
  macOS uses `PopupRoot`.
- **One exception, tooltips:** a popup whose host content is a `ToolTip` is left alone. Tooltips
  are hit-testable and route to their owner, so wheeling over one still scrolls what's underneath,
  as before.
- **Checks:** the user hand-checked desktop and the WASM demo. There's no automated test, because
  the test project has no Avalonia headless setup. Suite: 1109 passed.
- **macOS still to check:** native popups take the `PopupRoot` path. Include it in the macOS pass
  (item 1).

---

## 20 — Load by subject / focus (new feature)

> ### ⏸ PARKED (2026-10-09): REVISIT AFTER ALL OTHER ITEMS ARE DONE
> Decided to wait for better data. **Before resuming:**
> 1. Item 21 (lightweight section card) has landed.
> 2. Re-run the memory measurement on the same 800-section test DB (fresh start, snapshot on the
>    large semester and on a near-empty one). Compare with the 575 MB / 82 MB baseline.
> 3. With those numbers, choose between the two approaches and settle the open questions in
>    [designs/load-by-subject.md](designs/load-by-subject.md):
>    - **A. Load scoping:** read only the chosen subjects from the database. Needs special
>      handling for cross-department conflicts, room availability, workload totals and bulk
>      operations.
>    - **B. Filter-based focus:** load everything as today. Add a "Hide" section-list mode that
>      doesn't build non-matching cards, remember the filter (or named presets), and show a
>      clear active-focus indicator. All global checks stay correct for free.
>    - **Leaning B as of parking.** Load scoping's only advantage was memory, which item 21 and
>      B's "Hide" both address.
>
> Remaining questions: one remembered focus or named presets; whether the workload panel
> follows the focus.

**Problem.** Some customers are dean's offices that schedule about 10 departments centrally from
one database: 800+ sections in a semester. Every view loads every section. The section list builds
one card per section, because virtualization is disabled and filtering doesn't remove cards. The
grid, workload panel and conflict checks also scale with the section count.

**Feature.** The user chooses which subjects to load. Only those subjects' sections are loaded
into the working views, the current scope is always visible, and "all subjects" remains
available.

**Design.** Being developed as a Progressive Design Spec:
[designs/load-by-subject.md](designs/load-by-subject.md). Phase 1 (problem statement) is drafted,
with seven open questions. The key ones:
- conflicts and room availability must stay global across the scope boundary;
- instructor workload totals;
- how Empty Semester and other bulk operations behave under a scope (interacts with item 8);
- whether several staff need to edit at once (the single write lock is unchanged by this feature).

**Sequencing note.** If the scope choice is stored in local settings rather than the database, the
feature adds no fields to existing entities and is not constrained by item 3.

---

## 21 — Lightweight section card

**Problem (measured 2026-10-08/09).** Every section in the loaded semester gets a full card,
because the section list is not virtualized. Each card is about **93 Avalonia controls**, and
**about 42 of them are never seen** on a typical card:
- ~24: a per-card right-click flag menu;
- ~16: always-built hidden rows (reserves, resources, conflict warnings, notes, capacity, Freq
  header, ▶ arrow);
- 2: an empty editor slot.

Each control costs about 6 KB, mostly its property store and style activators, so a card costs
about **0.6 MB**. With 800 sections the managed heap is **575 MB**, against 82 MB for a
near-empty semester. In the WASM demo, building cards is the dominant cost: 40 cards take about
10 s, which is why semester switching is disabled there. Full measurements and the per-part count
are in [designs/load-by-subject.md](designs/load-by-subject.md).

The view model already produces display strings (`Heading`, `InstructorHeaderLine`,
`SectionTypeName`, `TagLine`, `MeetingDetails`, …). The cost is entirely in how the card turns
them into controls.

**Fix direction.** Same look, far fewer controls, target **~15–20 per card**:
1. **Row 1** (heading, instructors, type, capacity): one `TextBlock` built from **Runs** (bold
   heading, plain text, muted capacity). Empty fields get no run.
2. **Meeting table:** one `Grid` + **one `TextBlock` per column** (Day, Start, End, Freq, Room,
   Type). Each holds its header and then every meeting's value, separated by `LineBreak`s. That is
   7 controls regardless of meeting count, instead of 8 per meeting, and alignment comes from equal
   line heights. The Freq column is present only when `HasNonDefaultFrequency`.
3. **Property lines** (tags, reserves, resources, conflict warnings): one wrapping `TextBlock`
   with inlines. Icons are `Path`s in `InlineUIContainer`s, created only for lines that are present
   (icons must stay AXAML `Path` geometry for WASM compatibility). Notes stay a separate
   `TextBlock`, since they need single-line ellipsis trimming.
4. **One shared flag menu** at the list level, opened at the pointer for whichever card was
   right-clicked. Cards keep `RightClickCommandBehavior`.
5. **Editor slot created only for the card being edited.** This is a minor saving of 2 controls
   per card (an empty border and content control). Parking-lot P1 was checked and refuted: no
   hidden duplicate editors.
6. **Collapse toggle:** replace the ▶/▼ Unicode `TextBlock`s with one `Path` (icons-in-AXAML
   rule).
7. Kept as controls: the toggle button, the flag icon (tooltip and right-click target), the
   borders carrying selection, filter tint and semester colour, the hover overlay, and the list
   item container.

**Implementation notes.**
- `TextBlock.Inlines` is not bindable from a view model in XAML. Use one reusable attached
  behaviour that builds inlines from a view-model-provided list of segments (text + style key).
  This follows the MVVM / attached-behaviour preference.
- Font sizes, weights and colours come from `AppColors.axaml` / `App.axaml` resource keys, never
  hard-coded.
- **This edits `SectionListView.axaml`.** Close the tab in VS before the change.

**Production / data integrity.** UI-only; no data, persistence or migration impact. The risk is
visual regression.

**Interactions.**
- **Item 20:** with cards ~5× cheaper, loading all subjects is no longer a memory concern, so load
  by subject becomes purely about focus.
- **WASM demo:** semester switching may become fast enough to re-enable. That is a separate
  decision, to be made after measuring.

**Verification.**
- **Memory:** same 800-section test DB, fresh start, snapshot before and after. Target heap
  ≤ ~200 MB (from 575 MB). Check controls per card in the snapshot (the counts of `Classes` /
  `List<Visual>`).
- **Visual comparison against the current card**, covering:
  - long instructor lists that wrap;
  - non-weekly frequency (Freq column);
  - empty room;
  - 1 and 4+ meetings;
  - tags, reserves, resources and notes, singly and together;
  - room and instructor conflict warnings;
  - collapsed and expanded;
  - "being created" state;
  - multi-semester mode (left border and semester colour);
  - filter highlight;
  - cross-view selection;
  - hover.
- **Interactions:**
  - right-click flag menu on cards at the top and bottom of a scrolled list;
  - flag and capacity tooltips;
  - double-click / Enter to edit;
  - only one editor open at a time;
  - copy places the new card below the source.
- **WASM demo:** time a semester switch with the same data.

**Decisions (2026-10-09, with the user).** These replace parts of the fix direction above.
- **The section editor stays untouched**, except for the flag field below. This reduces regression
  risk. Fix-direction point 5 (create the editor slot only on demand) is **dropped**. The editor
  template and its slot must stay byte-identical; check this with a diff after every step.
- **Flagging moves from the card's right-click into the editor.** This replaces fix-direction
  point 4 (the shared flag menu). Closed cards become view-only, apart from collapse/expand.
  - **Why:** it fits the metaphor. It also removes the per-card menu (~24 controls) without the
    stale-card risk a shared menu brings: a shared menu can outlive a list rebuild and save an old
    copy of the section. It also closes a gap. Card right-click flagging was **not** blocked in
    read-only mode; every other section-list command checks `IsWriter`, and so does the grid's flag
    menu. The editor can't open in read-only mode.
  - **Editor:** a Flag dropdown to the right of the Notes box, styled like the editor's other
    dropdowns. It shows the coloured flag icon and name, using the same options as the grid's flag
    panel. The flag becomes part of the edit: Apply saves it, Cancel discards it. Instant flagging
    from the list goes away.
  - **Card:** remove the right-click handler and the flag popup. Show the flag icon **only when a
    flag is set**, with no faint placeholder, and change its tooltip to name the flag (it used to
    say "right-click the card").
  - **Unchanged:** the schedule grid's right-click menu (Instructors, Room, Tags, Flag) keeps its
    flag option.
- **Revised step order, one commit each:**
  1. flag into the editor and off the card;
  2. inline-segments behaviour, row 1 and property lines;
  3. meeting table as column `TextBlock`s;
  4. ▶/▼ as one `Path`;
  5. memory measurement.
- **Row 1 alignment (user, 2026-10-09).** This supersedes fix-direction point 1, which had row 1
  as one TextBlock of Runs. Runs flow, so nothing could line up across cards.
  - **Rule:** across cards, instructors start in the same column, and so do section types (and
    capacity). Instructors should preferably fit on one line but may wrap.
  - **Chosen layout:** keep the order heading | instructors | type | capacity, with **fixed column
    positions** from AXAML resources local to SectionListView.axaml:
    - heading column: a minimum width, so a rare long heading pushes only its own row instead of
      wrapping the course code;
    - instructor column: a fixed width (~160, up from 130), wrapping;
    - type column: a minimum width.
    - Widths are tuned by eye at the 400 px minimum card width.
  - **Not used:** whole-list `SharedSizeGroup`. With hundreds of cards it would tie every card's
    layout to every other card's.
  - Row 1 keeps separate TextBlocks, so it saves few controls. The savings are in the property
    lines and the meeting table.
- **Re-revised step order:**
  - **2:** row-1 alignment, plus the ▶/▼ glyphs replaced by one fixed-size `Path` (the old step 4).
    The two glyphs differ in width, which would shift collapsed and expanded rows.
  - **3:** property lines through the inline-segments behaviour.
  - **4:** meeting table as column `TextBlock`s. Ask the user then whether meeting columns should
    also align across cards.
  - **5:** memory measurement.

**Found during step 1: production data loss on editor Apply (fixed in 1.2.3, no hotfix).**
- **Bug (1.2.2 and earlier):** the inline editor works on `SectionListViewModel.CloneSection(...)`,
  and Save writes the **whole** clone back (`SectionRepository.Update` serializes the entire
  section). `CloneSection` left out two persisted fields, so every Apply on an existing section
  silently wiped them:
  - the section's **`Flag`**;
  - every meeting's **`RoomTypeId`**, including the "Remote" marker. Users would see this as
    "room type never sticks": it saves, but reopening the editor shows "(none)", and the next Apply
    erases it.
- **Fix:** `CloneSection` now copies both. A new `SectionCloneTests` guard fills every persisted
  property of `Section` and its child types (`InstructorAssignment`, `SectionDaySchedule`,
  `SectionReserve`) by reflection and compares the JSON of the original and the clone, so a field
  added in future can't be silently dropped. A second test opens the editor on a clone, applies it,
  and checks that room type and Remote survive.
- **Data impact:** flags and room types already lost can't be recovered from the live database,
  only from older backups. Nothing more is lost once 1.2.3 is installed. The user decided
  (2026-10-09) to ship the fix with 1.2.3 rather than as a 1.2.2.x hotfix.
- **Release note:** mention that room types (including Remote) and flags now survive editing.

**Step 1 outcome (2026-10-09): done, user hand-checked on desktop and WASM.**
- **Editor:** a Flag dropdown sits right of Notes, offering (None), Red, Blue and Green. It is
  built from the shared `FlagOptionVm.CreateOptions()`, which the grid's flag panel now uses too.
  `SectionEditViewModel.SelectedFlag` is seeded from the section and written only by Save.
- **Card:** no right-click handler and no flag popup. The flag icon shows only when a flag is set.
  Removed: `IsFlagMenuOpen`, `ShowFlagMenu`, `SetFlag`, `FlagOpacity`, `FlagDisplayBrush`,
  `SaveSectionFlag`, and the `onFlagChanged` callback.
- **Kept:** `RightClickCommandBehavior`, which `DetachablePanel.axaml` still uses.
- **Now unreferenced:** the `FlagMuted` brush in AppColors.axaml. Remove it with the next
  AppColors edit.
- **Editor template:** verified identical to HEAD apart from the Notes/Flag block. The editor slot
  is byte-identical.
- **Tests:** 13 in `SectionFlagTests`. Suite: 1129 passed.
- **Follow-up noted:** a meeting whose `RoomTypeId` refers to a deleted room type opens as "(none)",
  and Apply clears it. This is separate from the clone bug.

**Step 2 outcome (2026-10-09): done, user approved the look.**
- **Row-1 column positions** come from AXAML resources local to SectionListView.axaml:
  - `SectionRow1HeadingMinWidth` = 100, used as the heading column's `MinWidth`;
  - `SectionRow1InstructorWidth` = 160, a fixed `GridLength` for the instructor column, which wraps;
  - `SectionRow1TypeMinWidth` = 64, used as the type column's `MinWidth`.
- Instructors, types and capacities now start in the same columns on every card.
- **Toggle:** the ▶/▼ glyphs became one `Path` (`DisclosureTriangleIcon`, a 10×10 square in
  Icons.axaml). The `Path.DisclosureTriangle.expanded` style rotates it 90° when the card is
  expanded. This saves 2 controls per card, and collapsed and expanded rows no longer shift.
- **Checks:** the editor template and slot are byte-identical. Suite: 1129 passed. Checked in the
  WASM demo.

**Step 3 outcome (2026-10-09): done, user accepted.**
- **Card view model:** `SectionListItemViewModel.PropertyLines` is a list of `CardPropertyLine`
  (kind + text) in display order: room conflict, instructor conflict, tags, reserves, resources.
  Blank lines are omitted. It re-raises when a conflict warning changes.
- **Rendering:** `PropertyLinesBehavior` builds one wrapping TextBlock's inlines from that list:
  - a conflict line is a `Run.warning`;
  - an icon line is an `InlineUIContainer` (Viewbox + Path) followed by a Run;
  - the text is never parsed as markup, so user-entered names display as typed.
- **Presentation** comes from the `TextBlock.CardPropertyLines` styles in SectionListView.axaml.
  This works because inlines and the inline container's child are logical children of the
  TextBlock.
- **Controls:** the block went from ~15 always-built controls (including Notes) to 2, plus 2 per
  present icon line.
- **Visual differences, accepted:**
  - long tag, reserve and resource lines now wrap, and the wrapped part starts at the left edge;
  - the first property line sits ~3 px lower;
  - `LineSpacing` was tried and removed, because Avalonia applies it to every line.
- **Tests:** 24 in `SectionCardPropertyLinesTests`. Suite: 1153 passed.

**Step 4 decisions (user, 2026-10-09): meeting table.**
- **Structure:** one `Grid` with one `TextBlock` per column (Day, Start, End, Freq, Room, Type).
  Each holds its header Run, then one line per meeting. That is 7 controls whatever the meeting
  count; today it is 7 + 3 + 8 per meeting.
- **Alignment across cards:**
  - Day, Start and End keep their fixed 45 px columns.
  - **Room gets a minimum width** from an AXAML resource, so Type starts in the same column on
    every card. A very long room name pushes only its own card.
  - **Freq stays where it is.** It appears only on cards with a non-weekly meeting, and on those
    cards it shifts Room and Type right. Moving it last was considered and declined.
- **Spacing: tighter rows accepted.** Natural line spacing makes each meeting row ~3 px tighter,
  and the header gap shrinks slightly. No forced `LineHeight`.
- **Row alignment between columns** relies on every line having the same height:
  - an empty value is a non-breaking space, so its line has the same height as the others;
  - newlines inside values are flattened to spaces.

**Step 4 outcome (2026-10-09): done, user hand-checked.**
- **View model:** `Meeting{Day,Start,End,Frequency,Room,Type}Lines` on `SectionListItemViewModel`,
  built once by `BuildColumnLines`:
  - "\n" + value per meeting, so the string starts on the line below the header;
  - empty → NBSP;
  - embedded newlines → spaces.
- **AXAML:** each column TextBlock holds two Runs written on **one line**. Runs on separate lines
  make the XAML loader insert a stray " " Run between them.
- **Room column:** `SectionMeetingRoomMinWidth` = 90.
- **Controls:** the meetings block went from 11 + 8 per meeting to 7.
- **Off-screen render probe:** all six columns came out the same height with 0, 1 and 3 meetings.
- **Tests:** 21 in `SectionCardMeetingColumnsTests`. Suite: 1174 passed.

**Interim memory measurement (2026-10-09, after step 4).** Same method as the baseline: Debug
build, 800-section DB, VS snapshots.

| | Baseline (2026-10-08) | After step 4 |
|---|---|---|
| Heap, 800-section semester | 575 MB | **475 MB** |
| Heap, near-empty semester | 82 MB | 51 MB |
| Snapshot diff, large − small | +466 MB, +4.46 M objects | **+401 MB, +3.58 M objects** |
| Card template, controls declared | ~72, plus 8 per extra meeting (~93 typical) | ~27 for any number of meetings, plus 12 Runs |

- **Caveats:**
  - Avalonia also went from 12.0.2 to 12.1.3 in between.
  - The user scrolled the list this time, which the baseline steps don't mention.
- **What remains per section (~500 KB)** is spread across Avalonia's per-element bookkeeping:
  - styling, ~60 MB shallow: `StyleInstance` 170 per section and `StyleClassActivator` 314 per
    section;
  - compositor and render objects, ~75 MB;
  - `ValueStore`s, 89 per section, including 13 `ColumnDefinition`s per card;
  - event subscriptions;
  - text shaping.
- **Debug-only overhead, ~25–30 MB:** `AvaloniaXamlCreateSourceInfo` defaults to true only in
  Debug, so every XAML-created element carries a `XamlSourceInfo` with its own `Uri` (59 per
  section, ~23 MB). Release builds don't have it.
- **Our own styles cost too (found here).** A style with a class condition attaches a
  `StyleInstance` and activators to **every** element of that type in the view, matching or not.
  The styles added in steps 2–4 put roughly 60 of them on every card:
  - `Run.warning` and `Run.MeetingHeader` on every Run;
  - the `Path.cardPropertyIcon*` styles and `Path.DisclosureTriangle.expanded` on every Path;
  - `TextBlock.MeetingColumn` on every TextBlock.
  - **Step 4b** replaces them with direct values.
- **Final number:** taken in a **Release** build without scrolling, after 4b.

**Step 4b outcome (2026-10-09): done.**
- **Change:** the nine class-conditioned styles from steps 2–4 were replaced by direct values:
  - **Meeting table:** `FontSize` on the column TextBlocks; size and colour on the header Runs.
  - **Toggle:** a `RotateTransform` whose angle is bound to `IsCollapsed` through
    `CollapsedToDisclosureAngleConverter` (0° collapsed, 90° expanded).
  - **Property lines:** `PropertyLinesBehavior` takes its look from attached properties set in
    AXAML (`WarningForeground`, `WarningFontWeight`, `IconBrush`, `IconSize`, `IconSpacing`,
    `TagIcon`, `ReserveIcon`, `ResourceIcon`) and sets no style classes.
- **Rule recorded in the view:** don't reintroduce class-conditioned styles for elements that
  appear on every card.
- **Checks:**
  - an off-screen probe rendered old and new cards **pixel-identical** in four scenarios;
  - the editor is byte-identical;
  - suite: 1179 passed.

**Item 21 result: final measurement (2026-10-09, Release build, no scrolling, after 4b).**

| | Baseline (Debug) | After step 4 (Debug) | **After 4b (Release)** |
|---|---|---|---|
| Heap, 800-section semester | 575 MB | 475 MB | **392 MB** |
| Heap, near-empty semester | 82 MB | 51 MB | **39 MB** |
| Section-dependent (snapshot diff) | 466 MB | 401 MB | **~355 MB** |
| Per section | ~580 KB | ~500 KB | **~440 KB** |
| `StyleInstance` / `StyleClassActivator` per section | — | 170 / 314 | **119 / 228** |

- **Scrolling** made no difference: draw-list visuals were ±30.6 K in both runs.
- **Object counts** are VS samples, so compare them only loosely.
- **What remains per section (~440 KB)** is Avalonia's per-element bookkeeping:
  - compositor and render objects, ~60 MB;
  - strings and text shaping, ~35 MB;
  - the remaining styles, mostly the Fluent control themes and the view's older hover and
    TextBlock styles.
- **Further card trimming** has diminishing returns. A card that is a single TextBlock was
  estimated at only ~25–35% less, and it would lose column alignment.
- **The big remaining levers:**
  - **list virtualization**, so only on-screen cards are built (next experiment);
  - item 20's "Hide" focus mode.

**Virtualization re-test (2026-10-09): works for memory, still blocked by the editor jump.
Reverted.**
- **Experiment 1:** the ListBox's `ItemsPanel` `StackPanel` → `VirtualizingStackPanel`.
  - It virtualizes even though the list sits inside SectionPanelContent's outer ScrollViewer,
    because VSP uses the effective viewport.
  - **Heap on the 800-section semester: 475 → 160 MB** (Debug, user-measured).
  - But the list **jumps when the inline editor opens**. The motion differs from the June 2026
    probe, but the bug is the same.
- **Experiment 2:** with our `ScrollSelectedItemIntoView` auto-scroll disabled, **still jumps**, so
  our scroll code is not the cause.
- **Experiment 3:** the ListBox scrolls itself. The outer ScrollViewer was removed for the section
  list, `ListContentPanel` became a `Panel`, and `SuppressPopupScrollBehavior` moved onto the VSP.
  It **still jumps**, so the nesting is not the cause.
- **Conclusion:** Avalonia's `VirtualizingStackPanel` mishandles an on-screen item that grows by
  several hundred pixels. This is unchanged in 12.1.3 and 12.1.4 (no VSP fixes in 12.1.4's notes).
  All experiment edits are reverted, and the list's AXAML comment records the re-test.
- **Also noted:** with virtualization, `ScrollSelectedItemIntoView` would need rewriting, because
  `ContainerFromItem` is null for off-screen cards. Use a virtualization-aware `ScrollIntoView`.
- **Possible routes later:**
  1. An upstream fix (see parking-lot P4).
  2. Editing outside the card (e.g. a fixed editor area), so cards never grow. That is a UX change
     to a core design principle; discuss it with item 20. Collapse and expand also change card
     heights.

---

## 22 — AutoCompleteBox crash when revising a meeting's start time

> **Moved into item 1 (2026-10-09).** The Avalonia 12.1.3 bump made the same design flaw (lists
> rebuilt during a commit) blank mouse-picked values. See item 1, "Regression found in step 1".
> Implementation details beyond this section:
> - A new `DropDownOpeningCommandBehavior` runs the view model's `RefreshStartTimesCommand` /
>   `RefreshBlockLengthsCommand` when a box's dropdown opens.
> - Legality checks in the commit path compute legality directly instead of reading the lists.
> - A refresh is skipped when the list's contents are unchanged.
> - The tour refreshes the Length list before picking from it.

**Reported.** BugSnag, 21 Aug 2026, a production user editing a shared departmental DB.
`System.ArgumentOutOfRangeException: Index was out of range`. The breadcrumbs show 11 occurrences
in about 2 minutes: the user kept hitting it. Caught by the safety net ("the app will try to
continue"). **Reproduced locally 2026-10-09** with the identical stack.

**Stack shape (bottom up).**
1. Mouse click on an AutoCompleteBox dropdown item: `OnSelectorPointerReleased` → `OnCommit` →
   `OnAdapterSelectionComplete`.
2. `IsDropDownOpen = false` → `CloseDropDown`.
3. `SelectedItem = null` on the dropdown list → `SelectionModel.CommitOperation` →
   `SelectedItems` enumerator → `ItemsSourceView.GetAt(index)` → **index past the end** of the
   AutoCompleteBox's internal item list.

In short, the dropdown's list was rebuilt while the pick was being committed.

**Root cause (confirmed by a failing view-model test).**
1. A preferred block length P is set. The user picks start time T1 where P is legal, and P is
   auto-filled. The May 2026 fix (`c9ec1d3`) writes P via backing fields **without** refreshing
   the Start list
   ([SectionMeetingViewModel.cs:456-476](src/TermPoint/ViewModels/Management/SectionMeetingViewModel.cs:456)).
   The Start dropdown therefore still offers times where P is **not** legal.
2. The user revises the start time by **clicking** T2 where P isn't legal.
3. `OnSelectedStartTimeChanged` clears the now-invalid length via the property setter
   ([line 447](src/TermPoint/ViewModels/Management/SectionMeetingViewModel.cs:447)).
   That runs `OnSelectedBlockLengthChanged`, which calls `RefreshStartTimes()`
   ([line 498](src/TermPoint/ViewModels/Management/SectionMeetingViewModel.cs:498)).
   That **clears and refills `AvailableStartTimeStrings` mid-commit**, and the dropdown crashes as
   it closes.

All of these are needed together: a preferred block length; a **mouse** pick (typing takes a
different path); and a new start time where the current length isn't legal. Departments whose
block lengths have different start-time sets hit it; others never do.

**Fix direction: fix the family, not just this route.**
- **Rule:** a dropdown's item list (`AvailableStartTimeStrings`, `AvailableBlockLengthStrings`)
  is **never rebuilt as a side effect of committing a pick from a dropdown**.
- **Mechanism:** rebuild the suggestion lists **when a dropdown opens**, never during a commit.
  - The view model keeps the committed values.
  - A reusable attached behaviour on the AutoCompleteBox raises a view-model refresh command on
    `DropDownOpening`, before the box builds its view. `OpenDropDownOnFocusBehavior` already owns
    open-on-click; it could host this, or a sibling behaviour could.
  - Commit paths (`OnSelectedStartTimeChanged`, `OnSelectedBlockLengthChanged`) stop calling
    `RefreshStartTimes()` / `RefreshBlockLengths()` directly. Clearing an invalid length keeps
    happening, but no list is touched.
  - This also removes the **stale Start list** left by the May fix: after auto-fill, the Start
    dropdown would offer only times where P is legal.
- **Scope:** `SectionMeetingViewModel` also backs the Meetings flyout editor (`MeetingListView`),
  so the fix covers both. Verify both.
- **Doc fix:** [SectionEditViewModel.cs:480](src/TermPoint/ViewModels/Management/SectionEditViewModel.cs:480)
  says the preferred length is "pre-filled on new meetings". The actual behaviour is: filled after
  a start time is committed, when legal there.
- **Interaction with item 1:** Avalonia 12.1.1 rewrote AutoCompleteBox focus handling (#21749).
  It doesn't address mutating `ItemsSource` mid-commit, but re-test this item after the upgrade.

**Production / data integrity.** No data risk. The commit has already run when the exception
fires, and the safety net catches it. The user sees an error banner and may need to re-pick the
length. **User-facing annoyance, repeated:** 11 errors in 2 minutes in the field.

**Tests.**
- [PreferredBlockLengthProbeTests.cs](src/TermPoint.Tests/PreferredBlockLengthProbeTests.cs):
  - `Probe_AutoFill_AfterLegalStartTime_FillsPreferredLength`: **passes**; keep as a regression
    test.
  - `Probe_RevisingStartTime_DoesNotRebuildStartListDuringCommit`: **fails today**. Currently
    `Skip`ped with reason "spec item 22"; **un-skip when fixed**.
  - Add the symmetric test: committing a block length must not rebuild `AvailableBlockLengthStrings`.
  - Rename the file and tests from "Probe…" to regression names when the fix lands.

**Verification.**
- The reproduction no longer throws, in both the section editor and the Meetings flyout:
  1. Preferred length P set.
  2. Click a start time where P is legal (auto-fills).
  3. Click a start time where P is not legal.
- After auto-fill, the Start dropdown lists only times where P is legal.
- **The auto-filled length is visible in the Length box.** During investigation it once failed
  to appear, then later worked with no code change, so the cause is unknown. If it recurs: pick a
  start time, leave Length blank and click **Apply**. If the saved card shows P, the view model
  filled it and the Length box failed to display it (UI). If not, the auto-fill didn't run.
- Typing start times and lengths, and Tab-through, still commit correctly. This overlaps
  item 1's AutoCompleteBox checks.

---

## 23 — Section-list virtualization with scroll anchoring (opt-in, new 2026-10-10)

**Why.** Virtualizing the section list builds only the cards on screen.
- **Memory:** at 800 sections the heap drops from ~390 MB (Release) to ~170 MB (Debug, measured
  by the user). In multi-semester mode the old cost multiplies (~1 GB+ for three large
  semesters).
- **Reloads:** full reloads stay fast at any size; without virtualization every card is rebuilt.
- **The blocker** was Avalonia's `VirtualizingStackPanel` **scroll drift**. It was characterised on
  2026-10-10:
  - cards render correctly, but the content under an unchanged offset slides when any on-screen
    card changes height;
  - even selection does it (border 0 → 3 px), so a double-click could land on a different card;
  - details are in item 21, "Virtualization re-test".
- **The user's idea: scroll anchoring.** Keep a chosen on-screen card pinned through layout passes.

**Prototype (2026-10-10).**
- **Layout:** the ListBox scrolls itself. SectionPanelContent's outer ScrollViewer now wraps only
  the Events list.
- **Panel:** a `VirtualizingStackPanel` ItemsPanel, carrying the moved `SuppressPopupScrollBehavior`.
- **`ListScrollAnchorBehavior`:**
  - anchor priority: the pressed card, then the selected card, then the first visible card;
  - only the anchor's **top** is pinned;
  - an offset change counts as a scroll when the content moved rigidly with it, or when
    user-input hints are active: wheel, scrollbar drag, touch/pen pan, plus a 400 ms grace
    period;
  - guards: re-entrancy, and an oscillation limit (more than 20 corrections in 500 ms).
- **`SuppressPopupScrollBehavior.Bypass()`** lets deliberate `ScrollIntoView` calls through.
- **Virtualization-aware `ScrollSelectedItemIntoView`.**
- **Results:**
  - an off-screen probe with 300 cards held the pressed card within 1 px in 17 of 17 positions
    (press, and press + 500 px growth);
  - the user tested desktop with 800 sections: "nimble", heap 170 MB;
  - WASM: the double-click opens the right card, Cancel is clean, cross-view scroll works.

**Decisions (user, 2026-10-10).**
- **Opt-in for 1.2.3.** A **Preferences** checkbox replaces the kill switch and is **off by
  default**.
  - **Off:** the non-virtualizing `StackPanel` panel, with anchoring disabled.
  - **On:** `VirtualizingStackPanel` with anchoring.
  - The self-scrolling list layout applies in **both** modes. With the switch off, this means:
    - the list has its own scrollbar;
    - the multi-semester "Add to which semester?" prompt no longer scrolls with the list;
    - PageDown works (see below).
  - UI text is a working draft for now.
- **WASM:** always off, and the checkbox is **hidden** (no large demo data).
- **Required before release** (Opus's "1 to 5"):
  1. **One scroll path.** Every code-driven scroll of the section list goes through one public
     helper on the behaviour, which pauses anchoring and bypasses suppression. The rule is
     documented on the ListBox and in CLAUDE.md's gotchas: "to show a section, set
     `SelectedItem` or call the helper; never scroll the list directly — anchoring undoes it."
  2. **The switch** (above).
  3. **Keep the probe in the repo** as a re-runnable check. Add "re-run the anchoring probe" to
     the Avalonia-upgrade checklist.
  4. **A simplification and review pass** on `ListScrollAnchorBehavior` (979 lines, ~45% comments).
  5. **Field tests:**
     - macOS trackpad momentum;
     - a touchscreen flick;
     - keyboard PageDown;
     - the editor-recycling data test: type in the editor, click the grid, scroll the editing
       card far away and back, Apply, and check that every value was saved;
     - the Win11 VM;
     - detached Section View;
     - multi-semester mode.
- **Also agreed:**
  - **Touch inertia:** detect user scrolling from Avalonia's scroll-gesture events, which continue
    through inertia, not only from raw pointer moves plus a 400 ms grace period.
  - **Keyboard:** treat PageUp, PageDown, Home, End and the arrow keys in the list as
    user-scroll hints.
  - **PageDown finding:** PageDown never worked in the section list. In the old nested layout the
    ListBox's inner ScrollViewer can't scroll but still marks PageUp/PageDown handled, so the
    outer one never sees it. The self-scrolling layout fixes this.
- **Later (1.2.4 candidate):** turn it on automatically above a size threshold (e.g. 300
  sections), with the checkbox becoming Auto / Always / Never once it has had field time.

**Production / data integrity.** No persistence changes. The only data-entry exposure is the
editor's card being recycled when scrolled far away while unfocused. The view model holds the
committed values, and the field test above checks it.

**Tucked in here (user, 2026-10-10): room conflicts one per line.**
- **Problem:** a card's room-conflict warning joined every clash on one line with `"; "`, while
  instructor conflicts used `"\n"`. A section with many room clashes made a very wide card, which
  pushed the schedule view aside (the left column sizes to its content while editing).
- **Fix:** `SectionListViewModel.ApplyRoomConflicts` now joins with `"\n"`.
- **Test:** `RoomConflictMultiSemesterTests.SingleSemester_MultipleRoomConflicts_AreOnePerLine`.
  Three sections in one room and slot give two lines per card. The test fails with the old join.

---

## Parking lot: unconfirmed, check before finalising the spec

**P1. Possible hidden duplicate section editors (noted 2026-10-09). CHECKED: REFUTED.**

**Result (2026-10-09, fresh Debug instance, 61-section semester).** AutoCompleteBox count:

| Step | Count |
|---|---|
| Before any editor | 0 |
| Open 1 card | 1 |
| Cancel | 1 |
| Open/cancel 4 different cards | 1 |
| Open/cancel the same card ×4 | 1 |

- No growth, so no hidden duplicates and no accumulating leak. Avalonia does not build content in
  a hidden card's editor slot, even after the card has been opened before.
- The 1 (rather than 2 per meeting row while open, or 0 after close) is most likely VS snapshot
  sampling ("representative sample" on larger heaps), or at most one retained instance.
  Harmless.
- An earlier non-fresh session showed 16. Its history was different and it is not reproducible
  from a fresh start; not pursued.
- The original analysis is kept below for reference.
- **Suspicion.** Every section card's editor slot binds to the **list-wide** `EditVm`, not the
  card's own state
  ([SectionListView.axaml:1492](src/TermPoint/Views/Management/SectionListView.axaml:1492)):
  `<ContentControl Content="{Binding $parent[UserControl].DataContext.EditVm}" …/>` inside a
  `Border IsVisible="{Binding IsExpanded}"`.
  - A card that has never been shown expanded never builds its slot, because Avalonia doesn't
    build templates in hidden areas.
  - But once a card has been opened and closed, its slot is already built. When **another**
    section is opened, the hidden slot may silently build a second copy of the editor, bound
    two-way to the same `EditVm`.
- **Possible consequences.** Wasted memory that grows with the number of sections edited since
  the last list reload. Duplicate two-way bindings on one view model could also explain the
  binding re-fires the step-gate design works around.
- **Check (Debug build).**
  1. Open and close the editor on 5 different sections in turn, then open a 6th.
  2. Take a memory snapshot and filter types by `AutoCompleteBox`.
  3. About 2 per meeting row in the open editor means no bug. Much more, growing with the number
     of sections opened, means it's confirmed.
- **Likely fix if confirmed.** Give the slot content only when its own card is the one being
  edited (for example, a per-card property that returns `EditVm` only while `IsExpanded`).
  One binding change.

**P2. Demotion can orphan its own lock file if it still holds the lock (noted 2026-10-09).
LATENT: unreachable today.**
- **Mechanism.**
  1. `DemoteToReadOnlyAsync` calls `WriteLockService.Release()`, which deletes the lock file on
     a background task.
  2. Later in the same call, `EnterReaderMode()` reads the lock file with a raw
     `File.ReadAllText`. That read doesn't allow deletion while the file is open.
  3. If the read overlaps the delete, the delete fails with a sharing violation. The lock file
     stays on disk with our own content and a frozen heartbeat.
  4. Same-machine instances then treat it as live (our PID is alive) until the app exits. Other
     machines reclaim it through stale-lock takeover.
- **Why it can't happen today.** The app demotes only after write access is lost (`TakenOver` /
  `LockFileRemoved`). The background release then finds the file is not ours, or gone, and
  deletes nothing.
- **Where it showed up.** It surfaced as a flaky test (`DemoteThenRecover_RestoresPreservedEdits`),
  the only path that demotes while still holding the lock. The test now waits for the release
  inside `beforeClose`.
- **If a future change ever demotes while still holding the lock**, harden one of these:
  - open the reader-mode read with `FileShare.ReadWrite | FileShare.Delete`;
  - await the pending release before `EnterReaderMode`.

  This interacts with items 4 and 9, which change the lock and startup code.

**P10. macOS: unsaved edits after the working copy is deleted mid-session (noted 2026-10-13,
item 4c). FUTURE.**
- **The situation:** on macOS an open file can be deleted. If D' is deleted while TermPoint runs, 4c
  makes every save and backup fail safely, so D is never touched. But the user's edits then live only
  in the deleted file that the open connection still holds.
- **The consequences:**
  - The edits are lost when the app closes.
  - Autosave keeps showing "will retry automatically", which won't help here.
- **Possible design:** when D' is missing, snapshot through the live connection (`VACUUM INTO` or
  `BackupDatabase` from `DatabaseContext.Connection`) into a recovery file in the working folder,
  write a marker, and tell the user plainly.
- **The open question:** sharing the connection between the UI thread and the save thread.

**P9. Section-type colours (client request, noted 2026-10-11). NEW FEATURE; recommended for
1.2.4.**
- **The request:** attach a colour to each section type, so sections are coloured by type in the
  Section List and the Schedule view (exact presentation to be designed).
- **Storage:** section types are `SchedulingEnvironmentValue` rows, so the colour is one new field
  on that record. 1.2.3's unknown-field catch-all keeps it intact when a 1.2.3 user edits a type:
  that user simply doesn't see colours until they update.
- **Why 1.2.4, not 1.2.3:** 1.2.2 has no catch-all and ignores the stamp, so a 1.2.2 user who
  renames or reorders a section type would silently wipe its colour. That is low harm (cosmetic,
  easy to re-pick, and types are rarely edited), but 1.2.4 avoids it entirely.
- **`MinimumVersionToWrite`:** this is a **Yes** under the deciding question (a new kind of edit,
  a new saved field). The release that ships it sets `ThisReleaseRequires`.
- **Performance (item 21):** colour each card by setting the brush directly. Don't use per-type
  style classes: class-conditioned styles cost memory on every card.
- **Decide with the user:** timing (1.2.4 recommended) and the presentation in each view.

**P8. Campus queries use the wrong JSON key case (noted 2026-10-10 during item 3a; confirmed).
PRE-EXISTING BUG.**
- **The mismatch:** `CampusRepository` and `SeedData.FindOrCreateCampus` query
  `json_extract(data, '$.Name')` and `'$.SortOrder'`. Campus JSON is written by `JsonHelpers` in
  **camelCase** (`name`, `sortOrder`), and SQLite JSON paths are case-sensitive, so the
  expressions return NULL.
- **Likely effects:**
  - campuses are listed in arbitrary order instead of `SortOrder`;
  - the duplicate-campus-name check never matches;
  - seeding from `.tpconfig` may create a duplicate campus instead of finding the existing one.
- **Fix:** use `'$.name'` / `'$.sortOrder'`. Then check the real databases for duplicate campuses
  before deciding on any clean-up. The ordering fix visibly changes campus order to the intended
  one. Decide with the user.

**P7. In-app updates for the Microsoft Store build (noted 2026-10-10, item 3). FUTURE, 1.2.4
candidate; do it before the first release that raises MinimumVersionToWrite.**
- **The API:** `Windows.Services.Store.StoreContext`:
  - `GetAppAndOptionalStorePackageUpdatesAsync`;
  - `RequestDownloadAndInstallStorePackageUpdatesAsync`;
  - the Partner Center **mandatory** flag via `StorePackageUpdate.Mandatory`.
- **What it would add:** a periodic check while running (not only at launch), and "An update is
  available — [Update now]".
- **It needs:**
  - a Windows-specific target framework for the MSIX build (e.g. `net10.0-windows10.0.19041.0`),
    which affects `build-msix.ps1` and packaging;
  - `IInitializeWithWindow` with the main window handle for the Store dialogs.
- **Payoff:** a much smaller mixed-version window for everyone, so item 3's gate rarely triggers.

**P5. Unit tests write the developer's real `settings.json` (noted 2026-10-10). LOW RISK,
PRE-EXISTING.**
- `AppSettingsTests` (and, since item 23, `SettingsFileGuard`-based Preferences tests) save
  `AppSettings` to `%APPDATA%\TermPoint\settings.json` and restore it afterwards.
- **Risk on a dev machine:** if a Debug TermPoint saves its settings during a test run, that save
  is overwritten by the restore. If the test process is killed mid-test, the test value stays.
- **Fix:** an internal test-only override for the settings folder, so tests use a temp dir.
  Small; not done in item 23 to keep scope.

**P6. A selection made while the Section view is hidden isn't revealed when it is shown again
(noted 2026-10-10).** Example: choose a section from the grid while the Events list is showing,
then switch back. It was probably the same before item 23. If wanted, reveal the selected card when
`IsShowingSections` turns true.

**P4. File an Avalonia bug report for the VirtualizingStackPanel item-growth jump (noted
2026-10-09, user request). IDEA, not scheduled.**
- **Why:** virtualizing the section list cut the 800-section heap from 475 to 160 MB. It is blocked
  only by the jump when an on-screen item grows (see item 21, "Virtualization re-test"). An
  upstream fix is the cleanest route to that saving.
- **Plan:**
  1. Have an agent build a tiny stand-alone Avalonia 12.1.x repro: a `ListBox` with the default
     `VirtualizingStackPanel`, a few hundred variable-height items, and one item that grows by
     ~500 px on click. No TermPoint code.
  2. Characterise it first, which takes 30 s in the app with the VSP experiment applied: does the
     jump happen on the **top** card as well as mid-list? Which way does it move?
  3. The user files the issue on GitHub with the repro, video and versions.
- **Cost:** about an hour of agent time, with no risk to the app.

**P3. Copy Semester never carries meeting room types (noted 2026-10-09). DESIGN QUESTION, not
data loss.**
- `CopySemesterViewModel` (~line 248) copies meetings without `RoomTypeId`, even when "include
  meeting times" and "include room assignments" are on. A "Remote" meeting therefore lands in the
  new semester as an ordinary meeting with no room type.
- The source semester is untouched, so nothing is lost. The question is whether room type (and
  Remote in particular) should follow the meeting-time or room-assignment option.
- Decide with the user before changing anything.

---

## Release testing: network field tests

**This release changes startup, exit, save, restore and the write lock, and upgrades the UI
framework underneath them.** Unit tests cannot cover these paths. 1.2.3 must pass a full field
test on a real multi-machine network setup before release.

**Baseline.** Re-run the July protocol in full against the 1.2.3 release build:
[degraded-network-field-tests-2026-07-04.md](docs/testing/degraded-network-field-tests-2026-07-04.md).
That covers Scenarios A (black hole mid-save), B1–B5 (two-instance contention, graceful handoff,
stale takeover, same-machine dead-PID reclaim) and C1–C3 (kill mid-save, crash recovery).

**New scenarios for 1.2.3** (to be written up as `docs/testing/network-field-tests-1.2.3.md`):

| Scenario | Items |
|---|---|
| Colleague joins via the wizard while an editor session is active; editor's next save succeeds | 4 |
| Second instance on the same machine during a live session (and during a frozen one): guard message, no Restore offer, no cleanup | 4 |
| Restore from backup with the link black-holed mid-copy (Save & Backup and the chooser): `D` intact, "database unchanged" and the session continues; on success the "(before restore …)" copy beside `D` opens. Reader cannot restore. A chooser restore onto a database another machine is editing is refused with the holder's name | 5 |
| **Local copy fails after validation** (clumsy drops the link during the D → D'' copy while another machine holds the write lock): "Couldn't Open the Database" appears; Try again succeeds once the link is back; Close leaves no session on `D` | 4 |
| **Named-mutex single-instance guard on a real Mac:** a second copy on the same database is refused; a crashed or killed copy doesn't block a relaunch; a frozen one does | 4 |
| **Stale-lock takeover, 3 networked computers (user asked 2026-10-13: must be integration-tested).** A = writer laptop, B and C = colleagues. **(1)** A edits without saving, then sleeps more than 3 min; B opens the database: "Database Lock Is Inactive" names A's user and computer and the idle minutes. **(2)** B leaves the prompt open, A wakes and resumes; B clicks Take over: "Database Is In Use Again", B is read-only, and A's lock is intact and A keeps editing. **(3)** A sleeps again; B takes over and edits and saves; A wakes: the lock-loss banner, A read-only. At A's next write-access open, "Restore Unsaved Changes?" leads to "Save a Copy…" because D changed. **(4)** As (3), but B saves nothing: A's changes restore into D. **(5)** B chooses "Open read-only"; A later exits cleanly: B's "Take over as writer" appears within about 60 s. **(6)** C opens while B's prompt is open: no double takeover. **(7)** Clock skew: set A's clock several minutes behind and confirm whether an active A is ever offered for takeover (the 9b concern). | 9 |
| `D` modified externally: "Save aborted" banner offers a next step | 10 |
| Reader Refresh with the share down, and with the file locked mid-overwrite: failure shown, session recovers | 11 |
| Exit on a slow link: repeated X clicks, edits during the exit save | 15 |
| Close and DB-switch with an open editor holding unapplied changes | 14 |
| New-DB folder creation and recovered-copy export with the share down: no UI freeze | 16 |
| Cloud-synced folder (OneDrive/SharePoint, Google Drive, Box; macOS CloudStorage) is detected and warned | 13 |
| **Mixed versions:** a 1.2.2 client and a 1.2.3 client on the same shared DB, in both directions; unknown fields survive; version gate applies | 2, 3 |
| **Store update link:** once 1.2.3 is certified in the Store, on a machine still running 1.2.2 (before it updates), open `ms-windows-store://downloadsandupdates`: "Get updates" finds TermPoint 1.2.3. Also note whether TermPoint's product page (`ms-windows-store://pdp/?PFN=Academic-Solutions.TermPoint_9nmm6ygzk390e`) then shows "Update". | 3 |
| Remote DB via the wizard with a long UNC picker session; File → Open on a remote share; wizard Cancel after browsing | 1, 18 |
| **Windows 11 VM:** after the Harmony patch removal, AutoCompleteBox crash test. Click a Start Time and a Length suggestion in the section editor and the Meetings flyout, then wait 10 s each. No crash. Also covers the item 1 meeting-time entry checks on Win11. | 1 |
| BugSnag: handled failures and the "previous session ended uncleanly" event arrive, with paths and usernames scrubbed | 6, 7 |
| **Section-list virtualization ON** (Preferences), 800-section DB. **Clicks:** click, then double-click a card mid-list: the editor opens on that card and it doesn't jump. **Editor:** open and close it near the top, middle and bottom. **Keyboard:** click a card, then arrow Down several times (no drift); PageDown pages the list. **Scrolling away:** wheel and scrollbar-drag away from a selected card and from an edited card; neither snaps back. **Cross-view:** select a grid tile whose card is off-screen. **Other flows:** Add, Copy, collapse/expand, multi-semester mode, detached Section View. **Live toggle:** toggle the preference while the list is open. | 23 |
| **Section-list virtualization: editor-recycling data test.** Open the editor and change several fields (including a typed Start Time). Click the schedule grid, scroll the editing card far out of view and back, then Apply. Every value is saved. | 23 |
| **Section-list virtualization: touch and trackpad.** A touchscreen flick through a large semester doesn't snag after the finger lifts. macOS trackpad momentum scrolling doesn't snap back. | 23 |
| **Section-list virtualization OFF** (default): the list behaves as before, apart from its own scrollbar and PageDown now working. | 23 |

**Platforms.** Windows 10 and Windows 11 (the compositor deadlock fix in item 1 is Win10-specific),
plus macOS for items 4, 9 and 13, dialog layering and popup wheel containment (item 19).

**Test-rig notes carried over from July.**
- Clumsy `ip.DstAddr` filters are IPv4-only and SMB rides IPv6. Black-hole SMB with
  `tcp.DstPort == 445 or tcp.SrcPort == 445`.
- A true crash needs `taskkill /F` or Task Manager's **Details** tab. Processes-tab "End task" sends
  WM_CLOSE, which runs the graceful save.
- Run black-hole tests from the release build or with Ctrl+F5, not under the debugger. Just-My-Code
  breaks on late faults of observed abandoned tasks.
- Bump `<Version>` for every MSIX test build. An equal version won't install over itself, and the
  `[v…]` log stamp is the canary for stale bits.
- Don't browse into a dead share with a native Browse… dialog. The OS dialog itself hangs about 45 s
  (not a TermPoint bug). Use the wizard's suggestion click instead.
