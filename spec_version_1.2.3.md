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
| 1 | Avalonia 12.0.2 → 12.1.3 upgrade | Dependency / bug fix | **Accepted** (12.1.3) |
| 2 | Bundled SQLite engine security update (NU1903) | Dependency / security | Accepted |
| 3 | Preserve unknown JSON fields across app versions, with a minimum-version gate | Data integrity | Accepted |
| 4 | Stop opening the shared DB directly; fix startup ordering; single-instance guard | Data integrity | Accepted |
| 5 | Make Restore from backup safe | Data integrity | Accepted |
| 6 | Strip paths and usernames from BugSnag reports | Privacy | Accepted |
| 7 | Report handled save/lock failures and unclean exits to BugSnag | Observability | Accepted |
| 8 | Empty / delete semester and academic year: consistent rules, no orphans | Bug / data integrity | Accepted (rules decided) |
| 9 | Make stale-lock takeover safe | Data integrity | Accepted |
| 10 | "Save aborted" dead end: give a next step | UX | Accepted |
| 11 | Reader Refresh: report failures honestly and recover the connection | UX / reliability | Accepted |
| 12 | Save-specific error banner for exceptions during save | UX | Accepted |
| 13 | Cloud-sync folder detection gaps | Data integrity / UX | Accepted |
| 14 | Unapplied editor changes are lost on exit or database switch | Data loss | Accepted |
| 15 | Exit-path hardening and the mid-save edit gap | Data loss | Accepted |
| 16 | Remaining network I/O on the UI thread | Responsiveness | Accepted |
| 17 | CSV import blow-up on an unmatched quote | Responsiveness | Accepted |
| 18 | Wizard Cancel: hide instead of close (defence in depth) | Hang | Accepted (optional) |
| 19 | Mouse-wheel past the end of a dropdown scrolls the section list underneath it | UX bug (user-reported) | Accepted |
| 20 | **Load by subject / focus**: work on chosen subjects and levels only (dean's-office scale) | **New feature** | **PARKED: revisit after all other items are done, especially item 21** |
| 21 | Lightweight section card: ~93 → ~15–20 controls per card | Performance / memory | Accepted |
| 22 | AutoCompleteBox crash when revising a meeting's start time (BugSnag, Aug 21) | Bug (production, reproduced) | Accepted |

Item numbers are for reference only. The implementation order is below.

### Implementation order (decided 2026-10-09)

Items 2 and 19 were accepted into scope on 2026-10-09. Avalonia stays at **12.1.3**: 12.1.4 was
published on 2026-10-09 and has had no field exposure. Review its release notes before release.

| Order | Item | Why here |
|---|---|---|
| **Phase 1: Foundation** | | |
| 1 | 1: Avalonia 12.1.3 (its 5 steps, one commit each), **with item 22 folded into step 1** | Every later manual test then runs on the framework we ship. Unblocks item 18. **Item 22 moved here (2026-10-09):** the bump broke mouse picks in the meeting editor, and item 22's fix is the cure (see item 1, "Regression found in step 1"). |
| 2 | 2: SQLite security bump | The other dependency change, kept next to item 1. |
| 3 | 18: Wizard Cancel hides instead of closing | Trivial once item 1 has landed. |
| **Phase 2: Contained bug fixes** | | |
| 4 | ~~22: AutoCompleteBox crash~~ | Moved into item 1. |
| 5 | 17: CSV reader | Pure logic, unit-testable. |
| 6 | 19: Wheel containment in popups | Input routing only. |
| **Phase 3: Big UI change, early** | | |
| 7 | 21: Lightweight section card | Moved early so the big visual change gets used throughout the rest of development. |
| **Phase 4: Data model** | | |
| 8 | 3: Unknown-field preservation + version gate | Release-order gate for future releases. Touches every entity, so it should land early. |
| 9 | 8: Semester / AY empty and delete rules | Repository-level, unit-testable. |
| **Phase 5: Startup, lock and restore** (shared files, strictly in sequence) | | |
| 10 | 4: No direct opens; startup ordering; single-instance guard | |
| 11 | 9: Stale-lock takeover | |
| 12 | 5: Safe Restore | Builds the off-UI-thread copy helper. |
| 13 | 16: Remaining UI-thread network I/O | Reuses item 5's helper. |
| **Phase 6: Save messaging and exit** | | |
| 14 | 10 + 11 + 12, as one piece | |
| 15 | 15: Exit hardening + edit generation counter | After item 4, which also edits the end of `SaveAsync`. Sets up the close sequence that item 14 plugs into. |
| 16 | 14: Unapplied editor changes on exit / DB switch | Adds the Apply / Discard / Cancel step to the start of item 15's close sequence. |
| **Phase 7: Detection and observability** | | |
| 17 | 13: Cloud-sync folder detection | |
| 18 | 6: BugSnag scrubber | Must come before item 7. |
| 19 | 7: Handled-failure BugSnag events | Last of the reliability items, so it reports from the finished code of items 4, 9 and 15. |
| **Phase 8** | | |
| 20 | 20: Load by subject | Parked. Re-measure memory after item 21, then decide. |

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

| Change | Our exposure | Assessment |
|---|---|---|
| AutoCompleteBox focus handling rewritten. `GotFocus`/`LostFocus` are now the source of truth (#21749). | `LostFocusCommandBehavior` commits Start Time and Block Length on LostFocus. `OpenDropDownOnFocusBehavior` opens on click and runs manual Tab-through. Used in `SectionListView.axaml` and `MeetingListView.axaml`. | **Medium. This is the one data-entry path at risk.** If LostFocus now fires at a different moment (for example when clicking into the dropdown), a meeting time could be committed early or with a stale value. Must test by hand (see Verification). |
| `SelectionChanged` is now raised when a collection `Reset` clears the selection (#20942). | `SelectionCommandBehavior` is the only routed `SelectionChanged` handler **in our code**. **Missed at review:** AutoCompleteBox's internal selection adapter also handles it. | ~~None~~ **Regression found in the step 1 manual pass (2026-10-09). Fixed by pulling item 22 into item 1.** See "Regression found in step 1" below. |
| `TopLevel.Closed` is now raised from managed `Dispose` paths, with a double-call guard (#22045). | `wizard.Closed` → `tcs.TrySetResult()`; `note.Closed` → `_openNotes.Remove(note)`; `DetachedPanelWindow.OnClosed`. | **Low.** Handlers are idempotent. Hidden-not-closed windows may now raise `Closed` at app exit, after their work is done. Watch the detached-panel shutdown path. |
| Dispatcher yields on every operation once shutdown starts (#22240). Queued work after shutdown is aborted, not drained. | Exit save runs inside `MainWindow.OnClosing` (`ReleaseAsync` → cleanup) **before** the final `Close()`, so nothing of ours is queued when shutdown starts. | **None for save integrity.** Verified by code read (`MainWindow.axaml.cs:153-215`). |
| Focus traversal skips non-focusable containers (#21640); Tab-stop search loop fix (#21864). | Manual Tab-through in `OpenDropDownOnFocusBehavior`, which works around focus jumping to a `GridSplitter`. | **Low.** May change Tab order in editors and may make our workaround unnecessary. Test Tab and Shift+Tab through a meeting row. |
| Rendering: region dirty-rect clipping off by default (12.1.0); Unicode/text-layout rework (12.0.5). | Schedule grid tiles pack text tightly on one line. | **Low.** Visual check that grid tile text, ellipsis, and wrapping are unchanged. |

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
- **Still to do for item 1:**
  - remote-share / UNC picker checks;
  - macOS;
  - WASM;
  - the Harmony 10-second test after step 3.

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

_Details to be confirmed when we take this item up._

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

**Open decisions.** Where the pre-restore copy goes: the backup folder, which some joiners may not
have, or beside `D`. How long it is kept.

**Verification.** A restore by a reader is unavailable. A severed network mid-restore leaves `D`
intact. The pre-restore copy exists and opens.

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

---

## 18 — Wizard Cancel: hide instead of close (defence in depth)

**Problem.** Wizard Cancel calls `_window.Close()`
([StartupWizardViewModel.cs:135](src/TermPoint/ViewModels/Wizard/StartupWizardViewModel.cs:135)).
On Avalonia 12.0.2 that can deadlock right after a long network picker session. Item 1 fixes the
root cause.

**Fix direction.** Apply the same hide-and-signal pattern used for wizard finish. Optional: do it
only if it's trivial once item 1 has landed.

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
| Restore from backup with the link black-holed mid-copy: `D` intact, pre-restore copy kept; reader cannot restore | 5 |
| Writer laptop sleeps more than 3 min; reader's takeover dialog left open while the writer wakes; clock skew between machines | 9 |
| `D` modified externally: "Save aborted" banner offers a next step | 10 |
| Reader Refresh with the share down, and with the file locked mid-overwrite: failure shown, session recovers | 11 |
| Exit on a slow link: repeated X clicks, edits during the exit save | 15 |
| Close and DB-switch with an open editor holding unapplied changes | 14 |
| New-DB folder creation and recovered-copy export with the share down: no UI freeze | 16 |
| Cloud-synced folder (OneDrive/SharePoint, Google Drive, Box; macOS CloudStorage) is detected and warned | 13 |
| **Mixed versions:** a 1.2.2 client and a 1.2.3 client on the same shared DB, in both directions; unknown fields survive; version gate applies | 2, 3 |
| Remote DB via the wizard with a long UNC picker session; File → Open on a remote share; wizard Cancel after browsing | 1, 18 |
| BugSnag: handled failures and the "previous session ended uncleanly" event arrive, with paths and usernames scrubbed | 6, 7 |

**Platforms.** Windows 10 and Windows 11 (the compositor deadlock fix in item 1 is Win10-specific),
plus macOS for items 4, 9 and 13 and dialog layering.

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
