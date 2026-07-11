# Feature Design Spec: Interactive Walkthrough Tour

## Phase 1 — Problem Statement

### The Problem
A new user who finishes the startup wizard lands on the full 3-panel layout with no guidance on what they're looking at or what to do next. The app has many features (schedule grid, section list, filters, workload view, management flyouts) but nothing connects them into a coherent first experience. Users must explore on their own or find the Help topic browser — which itself is discoverable only if they already know to look under the menu.

For the WASM demo, the problem is sharper: visitors arrive to evaluate the app and need to understand its value proposition quickly. There's no wizard, no help articles — just the raw UI with demo data.

### Who It's For
- **Primary**: Brand-new users who just completed the startup wizard (desktop)
- **Secondary**: WASM demo visitors evaluating the app for the first time
- **Tertiary**: Returning users who want a refresher (via Help menu replay)

### Core Architectural Requirement: Composable Tour Toolkit
The tour system must be a **composable toolkit**, not a monolithic sequence. Individual tour steps and step groups (segments) are independent building blocks that can be assembled into different tours for different contexts. This is the foundational design constraint — every other decision follows from it.

**Why**: Users arrive in different states of knowledge. A post-wizard user needs the full orientation. A WASM demo visitor needs a value-proposition-focused subset. A user who skipped the wizard and regrets it needs configuration-focused guidance. Future features may need their own mini-tours. A monolithic tour can't serve these cases; a composable one can.

**Known tour scenarios** (not exhaustive — the system should support future ones without architectural changes):

| Scenario | Trigger | Content |
|---|---|---|
| Post-wizard orientation | Auto on first main-window launch | Full layout + key workflows (~15-20 steps) |
| WASM demo tour | Auto on every demo load (dismiss button for return visitors) | Value-proposition-focused subset |
| "I skipped the wizard" recovery | On-demand (Help menu or prompted when app detects empty config) | Configuration guidance + abbreviated orientation |
| Feature spotlight | On-demand or after feature update | Mini-tour of a specific feature area |
| Help menu replay | On-demand | Any previously seen tour, re-playable |

### What "Done" Looks Like
A tour toolkit and a set of pre-built tours that:

1. **Steps are atomic units** — each step identifies a UI element and provides explanatory content. Steps are defined independently and can appear in multiple tours.
2. **Segments group related steps** — e.g. "Layout Orientation" (3-4 steps), "Adding a Section" (4-5 steps), "Filtering" (3 steps). Segments are reusable building blocks.
3. **Tours are ordered sequences of segments** — a tour is just a named list of segment references. Different tours cherry-pick different segments.
4. **Draws the user's attention to UI elements** one at a time, explaining what each area does and why it matters (exact presentation mechanism is a Phase 3 decision)
5. **The post-wizard tour covers layout + key workflows** — roughly 15–20 steps, ~2 minutes
6. **Auto-triggers appropriately** per scenario (see table above)
7. **Is re-accessible** from Help > Take a Tour at any time (shows available tours)
8. **Is skippable/dismissable** at any step — never blocks the user from using the app
9. **Persists completion state per tour** so desktop users don't see auto-triggered tours again (WASM resets each session)

### What It Is NOT
- Not a video tutorial (though the Help system may link videos separately)
- Not a guided "do it with me" task wizard — it shows and explains, it doesn't require the user to perform actions
- Not a replacement for the Help topic browser — it's a quick orientation, not a reference
- Not a monolithic feature — it's a toolkit that happens to ship with a set of pre-built tours
- **Not committed to a specific UX mechanism yet** — tooltips, spotlights, side panels, coach marks, etc. are all Phase 3 decisions

### Success Criteria
- A first-time user can identify all three panels, understand filtering, and know how to add a section after completing the post-wizard tour
- WASM demo visitors can evaluate the app's purpose and capabilities within 2 minutes
- A user who skipped the wizard can get oriented without starting over
- The tour can be dismissed at any step without leaving the app in a broken state
- **Adding a new tour requires only defining step/segment content and a tour manifest — no new views, services, or architectural changes**
- Segments can be reused across tours without duplication
- Individual steps can be added, removed, or reordered within a segment without affecting other segments or tours

---

## Phase 2 — Domain Model

### Core Entities

**TourStep** — the atomic unit. One moment of explanation: identifies a UI element and provides content to display there.

- **Identity**: String key, unique across the entire step catalog (e.g. `"layout.section-panel"`, `"filter.instructor-dropdown"`). Dot-separated by convention for visual grouping; the dots carry no structural meaning — grouping is the segment's job.
- **Properties**:
  - `TargetRef` — a `TourTarget` identifying the UI element (see Target Resolution below)
  - `Title` — short heading (e.g. "The Section List")
  - `Body` — one to three sentences of explanatory text
  - `Placement` — preferred position relative to target (Right, Below, Left, Above, Auto). The presentation layer (Phase 3) decides what to do with this hint.
- **Nature**: Pure data record. No behavior, no awareness of which segment or tour it belongs to. No user action required, no branching, no conditional content.

**TourSegment** — a named, ordered group of steps that belong together thematically. The unit of reuse across tours.

- **Identity**: String key, unique across the segment catalog (e.g. `"layout-orientation"`, `"adding-a-section"`, `"using-filters"`).
- **Properties**:
  - `Title` — human-readable name for progress indicators and tour chooser UI (e.g. "Layout Orientation")
  - `StepKeys` — ordered list of TourStep keys. This is the segment's content.
- **Nature**: A coherent teaching unit. "Layout Orientation" contains three steps (section panel, schedule grid, workload panel). "Using Filters" contains four steps (filter bar, a dropdown, AND/OR toggle, clear button). Segments are the natural grain for tour descriptions: "this tour covers: Layout, Filtering, Adding Sections."

**Tour** — a named, ordered sequence of segment references forming a complete walkthrough experience.

- **Identity**: String key, unique across all tours (e.g. `"post-wizard"`, `"wasm-demo"`, `"feature-filters"`).
- **Properties**:
  - `Title` — display name for Help menu and progress UI (e.g. "Getting Started Tour")
  - `Description` — one-sentence summary of what the tour covers
  - `SegmentKeys` — ordered list of TourSegment keys
  - `AutoTrigger` — optional `TourTriggerRule` (see Auto-Trigger Rules below). Null = on-demand only.
  - `IsReplayable` — whether this tour appears in Help > Take a Tour (default true)
- **Nature**: A publishable walkthrough. Post-wizard orientation, WASM demo, and feature spotlights are all just different Tour values referencing overlapping sets of segments.

**TourProgress** — tracks the user's progress through a specific running or completed tour.

- **Identity**: Keyed by Tour key. At most one active progress record at a time.
- **Properties**:
  - `TourKey` — which tour
  - `Status` — NotStarted → InProgress → Completed | Dismissed
  - `CurrentSegmentIndex` — zero-based into the tour's segment list (meaningful only when InProgress)
  - `CurrentStepIndex` — zero-based into the current segment's step list (meaningful only when InProgress)
  - `CompletedAt` — timestamp when completed or dismissed. Null if NotStarted or InProgress.
- **Nature**: The only mutable entity in the model. Steps, segments, and tours are all immutable definitions.

---

### Relationships

**Composition model: reference by key, not containment.**

- A Tour contains an ordered list of segment keys. It does not own segments.
- A Segment contains an ordered list of step keys. It does not own steps.
- A Step is standalone. No back-reference to its segment or tour.

Consequences:
- Same step can appear in multiple segments (uncommon — more natural to share at segment level)
- Same segment can appear in multiple tours (the primary reuse mechanism)
- Adding a new tour is purely additive: define the tour, reference existing segments (and optionally new ones). No existing definitions change.

**Ownership and lifecycle**: Steps, segments, and tours are defined statically (see Tour Registry). Created once at startup, never modified at runtime. Only TourProgress is mutable. This separation keeps definitions trivially testable (pure data) and confines mutation to the progress layer.

**Resolution**: A `TourCatalog` holds all definitions and provides lookup by key. At tour start time, the catalog resolves a Tour's segment keys → TourSegment objects, and each segment's step keys → TourStep objects, producing a fully resolved walk sequence. Invalid keys (typos, removed steps) are detected at resolution time and logged — the tour skips missing items rather than crashing.

```
TourCatalog (static singleton)
  ├── AllSteps:    [TourStep, TourStep, ...]
  ├── AllSegments: [TourSegment, TourSegment, ...]
  └── AllTours:    [Tour, Tour, ...]

Tour
  ├── SegmentKeys: ["layout-orientation", "adding-a-section", ...]
  └── AutoTrigger: TourTriggerRule?

TourSegment
  └── StepKeys: ["layout.section-panel", "layout.schedule-grid", ...]

TourStep
  └── TargetRef: TourTarget { Kind = NamedControl, Value = "SectionViewPanel" }

TourRunner (singleton service)
  ├── ActiveTour: Tour?
  ├── Progress: TourProgress?
  └── Events: StepChanged, TourCompleted, TourDismissed

AppSettings
  └── CompletedTourKeys: List<string>
```

---

### Target Resolution

A step identifies "what UI element to point at." This is a domain concern (the step declares its target), even though the highlighting mechanism is Phase 3.

**TourTarget** is a value with two parts:
- `Kind` — the resolution strategy
- `Value` — the identifier, interpreted according to Kind

| Kind | Value example | Resolves to |
|---|---|---|
| `NamedControl` | `"SectionViewPanel"` | A control with matching `x:Name` in the visual tree. Primary mechanism — the app already has well-named controls in MainView.axaml. |
| `Region` | `"ScheduleGrid.Canvas"` | A logical child area within a composite control. Dot notation navigates to a child when the outermost container is too vague. Resolved via a small registry of known paths. |
| `MenuButton` | `"NavExport"` | Semantically distinct from NamedControl so the presentation layer can position content differently (e.g. below a menu button vs. beside a panel). |

**Why separate Kind from Value?** Lets the resolver validate targets at registration time with clear error messages. A `NamedControl:"SectionViewPanel"` is validated by walking the visual tree. A `Region:"ScheduleGrid.Canvas"` requires a different resolution path. One untyped string would push validation into runtime string parsing.

**Resolution timing**: Lazy, one step at a time as the tour advances. Necessary because some controls may not exist when the tour starts (e.g. detached panel, closed flyout). If a target can't be resolved when its step becomes current, the step is skipped with a log message — the tour does not stall.

**Named controls already available** (confirmed in MainView.axaml and GridFilterView.axaml):
`SectionViewPanel`, `ScheduleGridPanel`, `WorkloadPanel`, `ThreePanelGrid`, `SemesterPickerButton`, `NavConfiguration`, `NavHelp`, `NavExport`, `NavSharing`, `NavSchedulingEnvironment`, `NavAcademicYears`, `TopMenuPanel`, `FilterBody`, `MoreButton`

New `x:Name` attributes should only be needed for fine-grained targets within composite controls.

---

### Tour State

**What needs tracking**:
- Per-session: which tour is currently active (at most one)
- Per-tour: has this tour been completed or dismissed?

**In-memory state** — held by a `TourRunner` service (singleton, DI-registered). Coordinates between auto-trigger evaluation (MainWindow startup), Help menu replay commands, the presentation layer (Phase 3), and progress tracking. Follows the pattern of `SharedScheduleService` and `AppNotificationService`.

**Persistent state** — a new `CompletedTourKeys` property on `AppSettings` (`List<string>`). Records tour keys that have been completed or dismissed. This is sufficient for auto-trigger suppression. No per-step or per-segment progress is persisted.

**Why no partial-progress persistence?** Tours are short (under 2 minutes). Persisting step-level progress adds schema complexity for a feature nobody will use — a user who dismissed at step 8 of 15 is unlikely to want to resume at step 9 next week. Starting over is fine.

**Desktop vs. WASM — falls out naturally**:
- Desktop: `AppSettings.Save()` persists `CompletedTourKeys` to `settings.json`. Tours stay completed across sessions.
- WASM: `AppSettings` exists in memory but `Save()` is a no-op. `CompletedTourKeys` starts empty each session. Demo visitors see the auto-triggered tour every visit, with a dismiss button for return visitors. No WASM-specific logic needed — the platform difference falls out of the existing `AppSettings` architecture.

---

### Tour Registry

**Static definitions in code** — a `TourCatalog` class (analogous to `HelpViewModel.BuildTopicTree()`). Exposes:
- `AllSteps`, `AllSegments`, `AllTours` (immutable collections)
- Lookup by key: `GetStep()`, `GetSegment()`, `GetTour()`
- `GetReplayableTours()` — for the Help menu

**Why static, not database or JSON?** Tours are part of the application, not user data. They ship with the binary, change only in new releases, are identical for all users. Static C# definitions are type-checked at compile time, trivially testable, and require no migration path. Follows the same pattern as `HelpViewModel.BuildTopicTree()`.

**Extensibility**: Adding a new tour requires: (1) define any new steps, (2) define any new segments or reuse existing ones, (3) define the tour with its segment list and optional auto-trigger. All in `TourCatalog`. No new views, services, or architectural changes — meeting the Phase 1 success criterion.

---

### Auto-Trigger Rules

**TourTriggerRule** describes when a tour should auto-start. Attached to a Tour definition (null = on-demand only).

| Condition | Meaning | Detection |
|---|---|---|
| `PostWizardFirstLaunch` | First main-window load after wizard completion | `AppSettings.IsInitialSetupComplete == true` AND tour key not in `CompletedTourKeys` |
| `EverySession` | Auto-start at every main-window load (WASM demo) | Fires when tour key not in `CompletedTourKeys`. On WASM, that's always true (resets per session). On desktop, fires once then completion suppresses it. |
| `OnDemand` | Never auto-triggers | Represented by null `AutoTrigger` on the Tour |

**Evaluation flow**: After main window loads and semester context initializes, `TourRunner.EvaluateAutoTriggers()` iterates `TourCatalog.AllTours`, checks each tour's `AutoTrigger` against current app state, filters out tours already in `CompletedTourKeys`, and starts the first match. Multiple simultaneous triggers resolved by catalog order (first match wins).

**"Already seen" tracking**: A tour's key is added to `CompletedTourKeys` when the user either completes (reaches last step) or dismisses it. Dismissing counts as "seen" — the tour won't auto-trigger again on desktop. The user can still replay it from Help > Take a Tour.

---

### Key Invariants

**Definition-time** (validated at startup or by unit tests):

1. Every step key is unique across the step catalog
2. Every segment key is unique across the segment catalog
3. Every tour key is unique across the tour catalog
4. A segment references at least one step key
5. A tour references at least one segment key
6. Every step key referenced by a segment exists in the step catalog
7. Every segment key referenced by a tour exists in the segment catalog
8. Steps within a segment are ordered (list index = display order)
9. Segments within a tour are ordered (list index = display order)
10. A step's `TargetRef` has a non-empty `Value`

**Runtime** (enforced by TourRunner):

11. At most one tour active at a time. Starting a new tour dismisses the current one first.
12. Tour advances strictly forward — no "go back" (Phase 3 may revisit if UX demands it, but the domain model does not require it)
13. Unresolvable step target → step is skipped (logged). Tour does not stall.
14. Dismissing at any point is always possible and leaves the app in a clean state
15. Only terminal states (Completed, Dismissed) are persisted. Crash mid-tour → tour is not marked complete → auto-triggers again next launch.

**State transitions**:

```
NotStarted ──→ InProgress ──→ Completed
                           └─→ Dismissed
```

No other transitions valid. Replaying creates a fresh TourProgress, does not reuse the old one.

---

### Design Decisions Summary

| Decision | Rationale |
|---|---|
| String keys (not GUIDs/ints) | Human-readable in definitions, logs, and `CompletedTourKeys`. No collision risk since tours aren't user-created. |
| Flat catalogs with key references (not nesting) | Simpler to validate and test. Makes reuse explicit. Nested steps would require duplication or indirection to share across segments. |
| No partial-progress persistence | Tours are <2 min. Resume-from-step-9 adds complexity for minimal value. |
| TourProgress in-memory only | Only "has been seen" matters persistently. Served by `CompletedTourKeys`. |
| TourRunner as DI singleton | Needs to coordinate auto-triggers (startup), Help menu (replay), presentation (Phase 3), and progress. Follows `SharedScheduleService` pattern. |
| Static C# definitions (not DB/JSON) | Tours are app code, not user data. Compile-time checking, no migration path, follows `HelpViewModel.BuildTopicTree()` precedent. |

---

## Phase 3 — UX Sketch

### Presentation Decisions

- **Floating card + highlight** — no scrim/dimming; the app stays fully visible
- **Blocking overlay** — a transparent layer captures all pointer events; user must interact with tour controls (Next, Skip, Escape)
- **Passive demos** — steps can programmatically trigger app actions (open flyouts, expand editors, apply filters) before highlighting the result

---

### Overlay Architecture

A new **Layer 2** inside the `Panel Grid.Row="4"` of `MainView.axaml`, above the existing layers:

- Layer 0: `ThreePanelGrid` (the three-panel layout)
- Layer 1: Management flyout overlay (scrim + card)
- **Layer 2: `TourOverlayPanel`** (tour overlay)

```
TourOverlayPanel (UserControl, IsVisible bound to TourRunner.IsActive)
  ├── InputBlocker: Border Background="Transparent" — captures all pointer events
  ├── TourHighlight: Border — colored ring positioned at target bounds
  └── TourCard: Border — floating explanation card with arrow
```

The `InputBlocker` prevents all app interaction while the tour is active. No scrim color — the app stays fully visible. The overlay sits *above* the flyout layer so PreActions that open flyouts still render correctly beneath the tour card.

---

### Highlight Ring

Given a target control (resolved by `x:Name` or region path), compute its bounds relative to the overlay using `TranslatePoint()`. Position a `Border` at those bounds with 4px expansion padding so the ring wraps outside the target without modifying the target's own template.

**Styling**:
- `BorderBrush`: `TourHighlightBorder` (#9ABED8 — HelpTip blue family)
- `BorderThickness`: 2
- `CornerRadius`: 4
- `BoxShadow`: `0 0 8 2 TourHighlightGlow` (#409ABED8)
- `HorizontalAlignment`/`VerticalAlignment`: Left/Top (Margin acts as absolute positioning)

**Off-screen/unresolvable targets**: If either corner falls outside the overlay bounds, or the control doesn't exist in the visual tree, the step is skipped (Phase 2 invariant 13). After a PreAction, retry target resolution up to 3× with 100ms gaps (400ms total) before skipping.

---

### Floating Card

**Layout** (fixed 320px width):

```
+----------------------------------------------+
|  Title (14px, SemiBold)                      |
|  Segment Name (10px, muted)                  |
|                                              |
|  Body text (12px, wraps)                     |
|                                              |
|  Step 3 of 15          [Skip Tour]  [Next >] |
+----------------------------------------------+
        ▼ (arrow pointing at target)
```

**Styling**:
- `Background`: `TourCardBackground` (#F2F6FF)
- `BorderBrush`: `TourCardBorder` (#9ABED8)
- `BorderThickness`: 1
- `CornerRadius`: 6
- `BoxShadow`: `PanelBoxShadow` (1 2 4 0 #80000000)
- `Padding`: 16,12
- Title: `FontSize` 14, `FontWeight` SemiBold, `Foreground` = `TextPrimary`
- Body: `FontSize` 12, `TextWrapping` Wrap, `Foreground` = `TextSecondary`
- Segment name: `FontSize` 10, `Foreground` = `TourStepCounterText` (#64748B)

**Arrow**: A small `Path` triangle (12×8px) rendered outside the card border, pointing toward the target. `Fill` matches card background; `Stroke` matches card border. Direction matches the step's `Placement` value (Right → arrow points left, etc.).

**Positioning logic**: Place card on the step's `Placement` side with 12px gap from the highlight ring. If the preferred side doesn't fit, try all four in priority order (Right → Below → Left → Above) and pick the first that fits within the overlay with 8px edge margin. Clamp the secondary axis to keep the card visible; adjust the arrow offset to still point at the target center.

**Footer**: `DockPanel` — left: step counter ("Step N of M", global across all segments, `FontSize` 10, `TourStepCounterText` color); right: "Skip Tour" (subtle text button) + "Next"/"Done" (primary button, `FontSize` 12, `FontWeight` SemiBold). The last step shows "Done" instead of "Next".

---

### Step Transitions

**Instant** — no animation. When Next is clicked:

1. Hide card and highlight
2. Run departing step's PostAction (cleanup)
3. Advance progress index
4. Run arriving step's PreAction (setup)
5. Wait ~100ms for layout to settle
6. Resolve target → if unresolvable, retry up to 3×, then skip
7. Position highlight and card, show them
8. Raise StepChanged event

**Optional refinement** (deferred): 150ms opacity fade via `DoubleTransition` on the card and highlight `Opacity` property. Trivial to add without architectural changes.

---

### PreAction / PostAction

New optional properties on `TourStep`:
- `PreAction`: `Func<Task>?` — runs before the step displays (open a flyout, expand an editor, apply a filter)
- `PostAction`: `Func<Task>?` — runs when leaving the step (close flyout, collapse editor, clear filter)

**PostAction always runs on dismiss/complete** — prevents the app from being left with tour-opened flyouts or editors.

**ITourActionContext interface**: Abstracts the app actions that PreAction/PostAction callbacks need. Methods: `OpenFlyout(string name)`, `CloseFlyout()`, `SelectSection(string id)`, `ApplyFilter(...)`, `ClearFilter()`. Implemented by `MainWindowViewModel` or a thin adapter. Enables unit testing of PreAction/PostAction logic without a real UI.

---

### Authoring Workflow & Definition Storage

**Split: content in AXAML, behavior in C#.**

Step content (text, targets, placement) lives in an AXAML resource dictionary (`Styles/TourSteps.axaml`), hot-reloadable via HotAvalonia. PreAction/PostAction callbacks stay in C# (lambdas can't be expressed in AXAML). `TourCatalog` merges them at startup.

**AXAML definition format**:

```xml
<ResourceDictionary xmlns:tour="using:SchedulingAssistant.Models.Tour">
    <tour:TourStepData x:Key="layout.section-panel"
                       Title="The Section List"
                       Body="This panel shows all sections for the selected semester(s). Click any section to select it and see it highlighted on the schedule grid."
                       TargetKind="NamedControl"
                       TargetValue="SectionViewPanel"
                       Placement="Right" />

    <tour:TourSegmentData x:Key="layout-orientation"
                          Title="Layout Orientation"
                          StepKeys="layout.section-panel,layout.schedule-grid,layout.workload-panel" />

    <tour:TourData x:Key="post-wizard"
                   Title="Getting Started Tour"
                   Description="Learn the layout and key workflows"
                   SegmentKeys="layout-orientation,adding-a-section,using-filters"
                   AutoTrigger="PostWizardFirstLaunch" />
</ResourceDictionary>
```

`TourStepData`, `TourSegmentData`, `TourData` are plain C# classes with public properties (AXAML-instantiable). `TourCatalog` reads these at startup and merges with a `Dictionary<string, TourStepActions>` keyed by step key for PreAction/PostAction.

**Edit cycle with HotAvalonia**:
1. Launch app in debug mode
2. Trigger tour (or jump to a step via debug tools)
3. Edit `TourSteps.axaml` — change Title, Body, Placement, TargetValue
4. Save → HotAvalonia reloads the resource dictionary
5. Click Next in debug toolbar → card re-renders with updated content
6. No rebuild needed for content changes; only PreAction/PostAction changes require rebuild

---

### Debug Authoring Tools

In `#if DEBUG` mode, the tour overlay shows a compact toolbar above the card:

**Jump-to-step dropdown**: A `ComboBox` listing all step keys (grouped by segment). Selecting a step:
- Runs the PostAction of the current step (cleanup)
- Runs the PreAction of the target step (setup)
- Jumps directly to that step, bypassing all intermediate steps

**Placement cycler**: Four small buttons labeled R / B / L / A (Right, Below, Left, Above). Clicking one immediately repositions the card to that side of the current target, overriding the AXAML-defined placement. This lets you visually test all four positions without editing the file. When you find the right one, update `Placement` in the AXAML.

The debug toolbar is excluded from release builds entirely (not just hidden — the AXAML branch is `#if DEBUG` guarded).

---

### Keyboard

| Key | Action |
|---|---|
| Escape | Dismiss tour (runs PostAction, hides overlay, marks Dismissed) |
| Enter / Space | Advance (Next) or complete (Done on last step) |
| Right Arrow | Advance |

The overlay's `KeyDown` handler intercepts these keys (tunnel phase) and marks them handled. Focus is moved to the Next button when the tour activates; restored to the previous element on deactivation.

---

### Progress Indicator

- **Step counter**: "Step N of M" in card footer (global index across all segments)
- **Segment name**: Small muted label below the step title
- **No progress bar or dots** — counter + segment name is sufficient for tours under 20 steps
- **Segment transitions are seamless** — no interstitial, the segment name simply changes

---

### New AppColors Resources

| Key | Value | Purpose |
|---|---|---|
| `TourHighlightBorder` | `#9ABED8` | Highlight ring border |
| `TourHighlightGlow` | `#409ABED8` | Highlight ring BoxShadow |
| `TourCardBackground` | `#F2F6FF` | Card fill |
| `TourCardBorder` | `#9ABED8` | Card border |
| `TourStepCounterText` | `#64748B` | Step counter and segment name text |

Reuses the HelpTip color family intentionally — tour cards and help tooltips are the same visual family ("informational overlays"). Separate resource keys allow future divergence.

---

### Design Decisions Summary

| Decision | Rationale |
|---|---|
| No scrim/dimming | App should remain fully visible. Highlight ring draws attention without obscuring context. Avoids complexity of a scrim cutout. |
| Transparent InputBlocker | Captures pointer events to prevent accidental interaction. Simpler than a scrim with a cutout hole. |
| Fixed 320px card width | Under 25% of minimum 1400px window width. Readable text without obscuring panels. |
| Instant step transitions | Animations add complexity without proportional UX value when there is no scrim to anchor motion. Fade trivially addable later. |
| HelpTip color family reuse | Same "informational" visual language. Separate resource keys for flexibility. |
| PreAction as `Func<Task>` | Async because some setup operations (data loading, flyout opening) are async. |
| ITourActionContext interface | Decouples tour step definitions from MainWindowViewModel. Enables unit testing. |
| AXAML resource dictionary for definitions | Hot-reloadable via HotAvalonia for fast authoring iteration. |
| C# overlay for PreAction/PostAction | Lambdas can't live in AXAML. Merge at startup is a clean split. |
| Debug toolbar (jump-to-step + placement cycler) | Eliminates "click through 12 steps" and "rebuild to try a different side" pain during authoring. |

---

---

## Implementation Status

### Phase 2 Domain Model — IMPLEMENTED (May 18, 2026)

All domain entities, the static catalog, the runner service, and AXAML content structure are in place.

| Artifact | Location | Notes |
|---|---|---|
| Enums (TourTargetKind, TourPlacement, TourTriggerRule, TourStatus) | `Models/Tour/` | — |
| TourTarget | `Models/Tour/TourTarget.cs` | `readonly record struct`, validates non-empty value |
| TourStep | `Models/Tour/TourStep.cs` | Immutable class, all `{ get; }` via constructor |
| TourSegment | `Models/Tour/TourSegment.cs` | Immutable, `IReadOnlyList<string> StepKeys` |
| TourDefinition | `Models/Tour/TourDefinition.cs` | Spec's "Tour" renamed to avoid `Tour.Tour` namespace collision |
| TourProgress | `Models/Tour/TourProgress.cs` | Mutable, factory `Start(tourKey)` |
| AXAML data classes (TourStepData, TourSegmentData, TourData) | `Models/Tour/` | Parameterless + public setters for AXAML instantiation |
| TourCatalog | `Services/TourCatalog.cs` | Static registry; AXAML scanning + test-friendly overload; `Validate()` checks all 10 invariants |
| TourRunner | `Services/TourRunner.cs` | DI singleton; Start/Advance/Dismiss/EvaluateAutoTriggers; persists to `AppSettings.CompletedTourKeys` |
| ITourActionContext | `Services/ITourActionContext.cs` | Interface only — implementation deferred to Phase 3 |
| TourContent.axaml | project root | Starter content: 3 steps (layout-orientation), 1 segment, 1 tour (post-wizard) |
| Tests | `Tests/TourCatalogTests.cs`, `TourRunnerTests.cs`, `TourProgressTests.cs` | 26 tests |

**Existing files touched (3 only)**: `AppSettings.cs` (+1 property), `App.axaml` (+1 ResourceInclude), `App.axaml.cs` (+DI registration + catalog init in both desktop and WASM paths).

### Phase 3 UX Overlay — IMPLEMENTED (May 18, 2026)

Overlay presentation layer: highlight ring, floating card with arrow, keyboard navigation, debug authoring tools.

| Artifact | Location | Notes |
|---|---|---|
| TourPositionCalculator | `Helpers/TourPositionCalculator.cs` | Pure static positioning logic; CardPosition record; placement fallback priority Right→Below→Left→Above |
| CardPosition | `Helpers/TourPositionCalculator.cs` | Record: CardMargin, ActualPlacement, ArrowOffset |
| TourOverlayViewModel | `ViewModels/TourOverlayViewModel.cs` | Subscribes to TourRunner events; all overlay state; AdvanceAsync/DismissAsync with PostAction/PreAction orchestration; debug jump-to-step + placement cycler |
| TourOverlayView | `Views/TourOverlayView.axaml` + `.cs` | Highlight ring, floating card, arrow (Path with 4 style classes), keyboard (Esc/Enter/Space/Right), target resolution via visual tree walk, debug toolbar |
| AppColors additions | `AppColors.axaml` | 5 new resources: TourHighlightBorder, TourHighlightGlow, TourCardBackground, TourCardBorder, TourStepCounterText |
| Tests | `Tests/TourPositionCalculatorTests.cs`, `Tests/TourOverlayViewModelTests.cs` | 29 tests (15 position calculator + 14 overlay VM) |

**Existing files touched (3)**: `MainView.axaml` (+Layer 2 TourOverlayView), `MainWindowViewModel.cs` (+TourOverlayVm property + TourRunner injection), `MainWindow.axaml.cs` (+EvaluateAutoTriggers on post-setup dispatcher tick).

### Phases 4–6 — NOT YET DESIGNED

*To follow in subsequent sessions.*
