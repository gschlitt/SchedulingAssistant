# Tour Authoring Guide

A reference for creating walkthrough tour content in TermPoint. All tour
text and structure is defined in **`TourContent.axaml`** (hot-reloadable via
HotAvalonia). PreAction/PostAction callbacks live in C# and are merged by
TourCatalog at startup.

---

## Architecture at a Glance

```
TourStepData  ──►  TourSegmentData  ──►  TourData
(one card)         (group of steps)       (complete tour)
```

- **Step**: One moment of explanation — a title, body, and a pointer to a UI element.
- **Segment**: A named, ordered group of steps that belong together thematically.
- **Tour**: A named, ordered sequence of segments forming a complete experience.

All three are declared in `TourContent.axaml` as resource entries. The `x:Key` on
each entry becomes the key used everywhere in the system.

---

## Quick-Start Template

Copy and adapt this block into `TourContent.axaml`:

```xml
<!-- ── Welcome Step ── -->

<tour:TourStepData x:Key="myfeature.welcome"
    Title="Welcome"
    Body="This tour covers My Feature. We'll walk through the key controls."
    TargetKind="None"
    IsWelcome="True" />

<!-- ── My Feature Steps ── -->

<tour:TourStepData x:Key="myfeature.first-thing"
    Title="The First Thing"
    Body="Explain what this control does and why the user cares."
    TargetKind="NamedControl"
    TargetValue="SomeNamedControl"
    Placement="Right" />

<tour:TourStepData x:Key="myfeature.second-thing"
    Title="The Second Thing"
    Body="Keep it to 1-3 sentences. Be conversational."
    TargetKind="NamedControl"
    TargetValue="AnotherControl"
    Placement="Left" />

<!-- ── My Feature Segment ── -->

<tour:TourSegmentData x:Key="myfeature"
    Title="My Feature"
    StepKeys="myfeature.welcome,myfeature.first-thing,myfeature.second-thing" />

<!-- ── Add segment key to a tour's SegmentKeys ── -->
```

---

## Step Reference (`TourStepData`)

| Property      | Type   | Required | Description |
|---------------|--------|----------|-------------|
| `x:Key`       | string | Yes      | Unique step key. Convention: `group.topic` (dots for visual grouping only). |
| `Title`       | string | Yes      | Short heading shown in the card (e.g. "The Section List"). |
| `Body`        | string | Yes      | 1-3 sentences of explanation. Plain text, no markup. |
| `TargetKind`  | string | Yes      | How to find the UI element. See **Target Kinds** below. |
| `TargetValue` | string | Varies   | The identifier for the target, interpreted per `TargetKind`. Not needed when `TargetKind="None"`. |
| `Placement`   | string | No       | Preferred card position. Defaults to `Auto`. See **Placement** below. |
| `IsWelcome`   | bool   | No       | When `True`, renders a wider (480px) centered card for tour introductions. Defaults to `False`. |

### Target Kinds

| Value           | TargetValue format     | When to use |
|-----------------|------------------------|-------------|
| `NamedControl`  | `x:Name` of a control  | Most cases. Any control with an `x:Name` in MainView. |
| `Region`        | `Parent.Child` (dot notation) | When the outer container is too large and you need a sub-region. The code-behind finds the parent by name, then searches its descendants for the child. |
| `MenuButton`    | `x:Name` of the button | For menu/nav buttons — positions the card below/beside the button rather than beside a large panel. |
| `None`          | *(omit or leave empty)* | No UI target. The card is centered in the overlay with no highlight ring or arrow. Ideal for welcome/introduction steps. Pair with `IsWelcome="True"` for a wider card. |

### Available Named Controls (MainView.axaml)

These are the `x:Name` values currently available for `TargetKind="NamedControl"`:

| Name | What it is |
|------|------------|
| `RootView` | The entire MainView |
| `TopMenuPanel` | The top menu/nav bar |
| `SemesterPickerButton` | Semester dropdown button |
| `NavSchedulingEnvironment` | Nav button: Scheduling Environment |
| `NavAcademicYears` | Nav button: Academic Years |
| `NavConfiguration` | Nav button: Configuration |
| `NavExport` | Nav button: Export |
| `NavSharing` | Nav button: Sharing |
| `NavHelp` | Nav button: Help |
| `DebugMenu` | Debug menu (DEBUG builds only) |
| `MoreButton` | "More..." nav button |
| `ThreePanelGrid` | The three-panel layout container |
| `SectionViewPanel` | Left panel: section list |
| `WorkloadPanel` | Right panel: workload summary |
| `ScheduleGridPanel` | Center panel: schedule grid |
| `ScheduleGridViewControl` | The grid view itself (inside ScheduleGridPanel) |

> **Tip**: To target controls in other views (flyouts, editor panels, etc.), you'll
> need a **PreAction** to open that view first, then use the control's `x:Name`.
> The resolver walks the full visual tree from MainView downward.

### Placement

| Value   | Card position | Arrow points |
|---------|--------------|--------------|
| `Right` | To the right of the target | Left (toward target) |
| `Left`  | To the left of the target | Right (toward target) |
| `Below` | Below the target | Up (toward target) |
| `Above` | Above the target | Down (toward target) |
| `Auto`  | System chooses best fit | Automatic |

The system tries your preferred placement first. If the card doesn't fit
(too close to an edge), it falls through this priority order:
**Right > Below > Left > Above**. If nothing fits, the card is centered
on the overlay with no arrow.

Card width is **320px** for normal steps and **480px** for welcome steps
(`IsWelcome="True"`). The system ensures an **8px** margin from all viewport
edges and a **32px** gap between the card and the target.

---

## Segment Reference (`TourSegmentData`)

| Property   | Type   | Required | Description |
|------------|--------|----------|-------------|
| `x:Key`    | string | Yes      | Unique segment key (e.g. `layout-orientation`). |
| `Title`    | string | Yes      | Shown in the card's segment indicator (e.g. "Layout Orientation"). |
| `StepKeys` | string | Yes      | Comma-separated, ordered list of step keys. |

Segments are the unit of reuse. Different tours can reference the same segment.

---

## Tour Reference (`TourData`)

| Property      | Type   | Required | Description |
|---------------|--------|----------|-------------|
| `x:Key`       | string | Yes      | Unique tour key (e.g. `post-wizard`). |
| `Title`       | string | Yes      | Display name for Help menu and progress UI. |
| `Description` | string | Yes      | One-sentence summary. |
| `SegmentKeys` | string | Yes      | Comma-separated, ordered list of segment keys. |
| `AutoTrigger` | string | No       | When to auto-start. See **Auto-Triggers** below. Empty = on-demand only. |
| `IsReplayable`| string | No       | `"True"` (default) or `"False"`. If true, appears in Help > Take a Tour. |

### Auto-Triggers

| Value                    | Behavior |
|--------------------------|----------|
| `PostWizardFirstLaunch`  | Auto-starts on first main-window load after wizard completion. Suppressed once the tour completes. |
| `EverySession`           | Auto-starts on every session if the tour hasn't been completed. On WASM (where settings reset), fires every visit. |
| *(empty)*                | On-demand only — user must start it from the Help menu. |

---

## Step Action Callbacks

Steps can have C# callbacks that orchestrate UI changes while the tour card is
visible. Three kinds:

- **PreAction** — runs *before* the card appears. Set up UI state: open a popup,
  expand an editor, apply a filter. Runs once per step.
- **MidActions** — an ordered list of actions, each triggered by one user click of
  Next/Done. The card stays visible between mid-actions. Body text auto-advances
  after each mid-action (pipe-delimited body messages stay in lockstep).
- **PostAction** — runs when *leaving* the step. Undo whatever PreAction did.
  Always runs, even on dismiss/skip, to avoid leaving the app in a tour-modified state.

All three are optional. Steps without actions just show their card.

### The action lifecycle

```
User arrives at step
  ├─ PreAction()                          ← setup (open popup, etc.)
  ├─ Card appears with Body message 0
  │
  ├─ User clicks Next → MidActions[0]()   ← body advances to message 1
  ├─ User clicks Next → MidActions[1]()   ← body advances to message 2
  ├─ ...                                  ← one mid-action per click
  │
  ├─ User clicks Next (no more mid-actions)
  │     ├─ PostAction()                   ← cleanup (close popup, etc.)
  │     └─ Advance to next step
```

Steps without MidActions advance on a single click (Pre → card → click → Post → next).

### Body messages and mid-actions

Body text in AXAML can be pipe-delimited. The first message shows when the card
appears; each mid-action click advances to the next message. Make sure you have
**N + 1 messages for N mid-actions** (the initial message plus one after each action).

```xml
<tour:TourStepData x:Key="filter.tags-open"
    Title="Filtering by Tag"
    Body="This popup shows all your tags.|We just selected a tag — notice the grid filtered.|Now the popup is closed. Only matching sections remain."
    TargetKind="Region"
    TargetValue="ScheduleGridPanel.TagsPanel"
    Placement="Below" />
```

### Exception safety

All action calls are wrapped in try/catch. If an action throws, the error is logged
and the tour continues — it never crashes during a demo.

### Light-dismiss suppression

Popups in the app use `IsLightDismissEnabled="True"`, which means clicking anywhere
outside the popup closes it. During a tour step that opens a popup, this would eat
the user's click on Next. To prevent this:

1. In the **PreAction**, call `SuppressLightDismiss(parentName, toggleName)` after
   opening the popup. This sets `IsLightDismissEnabled = false` and stashes the
   original value.
2. In the **PostAction**, call `RestoreAllLightDismiss()` inside a `finally` block.
   This restores all suppressed popups, even if the PostAction throws.
3. The overlay VM also calls `RestoreAllLightDismiss()` on every step transition,
   dismiss, and hide as a safety net.

### How to wire

Actions live in `TourActionDefinitions.cs`. The dictionary key must exactly match
the step's `x:Key` in `TourContent.axaml`. The dictionary is passed to
`TourCatalog.Initialize` at startup.

### `TourStepActions` record

```csharp
public record TourStepActions(
    Func<Task>? PreAction = null,
    IReadOnlyList<Func<Task>>? MidActions = null,
    Func<Task>? PostAction = null);
```

### Helper methods in TourActionDefinitions

| Helper | What it does |
|--------|-------------|
| `GetViewModel()` | Returns the `MainWindowViewModel` from DI |
| `GetMainView()` | Returns the `MainView` from the visual tree |
| `FindDescendant<T>(parentName, childName)` | Walks the visual tree from a named parent to find a named child control |
| `SuppressLightDismiss(parentName, toggleName)` | Disables light-dismiss on the Popup sibling of a ToggleButton |
| `RestoreAllLightDismiss()` | Restores all suppressed popups (idempotent, safe to call multiple times) |

---

## Examples

### Example 1 — Simple step with no actions

Just a card pointing at a UI element. No C# needed.

```xml
<tour:TourStepData x:Key="layout.schedule-grid"
    Title="The Schedule Grid"
    Body="This is where your weekly schedule appears."
    TargetKind="NamedControl"
    TargetValue="ScheduleGridPanel"
    Placement="Left" />
```

### Example 2 — Step with PreAction and PostAction only

Toggle a ViewModel property to show/hide a panel:

```csharp
["filters.open-bar"] = new(
    PreAction: () =>
    {
        GetViewModel()!.ScheduleGridVm.IsFilterBarVisible = true;
        return Task.CompletedTask;
    },
    PostAction: () =>
    {
        GetViewModel()!.ScheduleGridVm.IsFilterBarVisible = false;
        return Task.CompletedTask;
    })
```

### Example 3 — Popup with mid-actions (the common pattern)

Open a filter popup, let the user click through selecting an item and closing it.
Three body messages for two mid-actions:

```xml
<tour:TourStepData x:Key="filter.tags-open"
    Title="Filtering by Tag"
    Body="This popup shows all your tags. Click Next to select one.|We just selected a tag — notice the grid filtered. Click Next to close the popup.|The popup is closed. Only matching sections remain."
    TargetKind="Region"
    TargetValue="ScheduleGridPanel.TagsPanel"
    Placement="Below" />
```

```csharp
["filter.tags-open"] = new(
    PreAction: () =>
    {
        // Open the popup and suppress light-dismiss so Next clicks aren't eaten
        var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
        if (toggle is not null)
            toggle.IsChecked = true;
        SuppressLightDismiss("ScheduleGridPanel", "TagsToggle");
        return Task.CompletedTask;
    },
    MidActions: new Func<Task>[]
    {
        () =>   // Click 1: select a tag
        {
            var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                .FirstOrDefault(t => !t.IsSentinel);
            if (firstTag is not null)
                firstTag.IsSelected = true;
            return Task.CompletedTask;
        },
        () =>   // Click 2: close the popup
        {
            var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
            if (toggle is not null)
                toggle.IsChecked = false;
            return Task.CompletedTask;
        }
    },
    PostAction: () =>
    {
        try
        {
            // Deselect the tag and ensure popup is closed
            var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                .FirstOrDefault(t => !t.IsSentinel);
            if (firstTag is not null)
                firstTag.IsSelected = false;
            var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
            if (toggle is not null)
                toggle.IsChecked = false;
        }
        finally
        {
            RestoreAllLightDismiss();   // Always restore, even if cleanup throws
        }
        return Task.CompletedTask;
    })
```

### Example 4 — Step with a single mid-action (collapse/expand)

Two body messages for one mid-action:

```xml
<tour:TourStepData x:Key="layout.section-panel"
    Title="The Section List"
    Body="All your sections appear here. Click Next to see the compact view.|Sections are now collapsed. Click Next to continue."
    TargetKind="NamedControl"
    TargetValue="SectionViewPanel"
    Placement="Right" />
```

```csharp
["layout.section-panel"] = new(
    MidActions: new Func<Task>[]
    {
        () =>   // Click 1: collapse all sections
        {
            GetViewModel()?.SectionListVm.CollapseAll();
            return Task.CompletedTask;
        }
    },
    PostAction: () =>
    {
        GetViewModel()?.SectionListVm.ExpandAll();
        return Task.CompletedTask;
    })
```

### How actions are matched to steps

Actions are matched **by step key only**. The dictionary key in C# must exactly match
the `x:Key` on the step in `TourContent.axaml`. The catalog merges them at
initialization time. Steps without a matching action entry simply show their card
with no callbacks.

---

## Key Naming Conventions

| Entity  | Pattern | Examples |
|---------|---------|----------|
| Steps   | `group.topic` | `layout.section-panel`, `filters.department-dropdown` |
| Segments | `group-name` | `layout-orientation`, `adding-a-section` |
| Tours    | `descriptive-slug` | `post-wizard`, `wasm-demo`, `filter-spotlight` |

Dots in step keys are a visual convention for grouping — they carry no structural
meaning. Keep keys lowercase with hyphens.

---

## Writing Good Tour Content

### Title
- 3-6 words, noun phrase ("The Section List", "Filtering by Instructor")
- Describes *what*, not *how*

### Body
- 1-3 sentences per message. Normal cards are 320px wide — long text will push the
  card tall. Welcome cards are 480px wide.
- For steps with mid-actions, use pipe-delimited messages. Each message should
  describe what the user is about to see or what just happened.
- Lead with *what it does*, follow with *why the user cares*
- Be conversational, not technical ("Click any section to select it" not "Selection
  is bound bidirectionally to the grid")
- Avoid jargon the user hasn't seen yet in the tour

### Welcome Steps
- Start each tour with a welcome step (`TargetKind="None"` + `IsWelcome="True"`)
- The welcome card appears centered with no highlight or arrow — a clean introduction
- Use the title for a greeting (e.g. "Welcome to TermPoint") and the body for a brief
  overview of what the tour will cover

```xml
<tour:TourStepData x:Key="tour.intro"
    Title="Welcome to TermPoint"
    Body="This short tour will show you the main panels and how to get started."
    TargetKind="None"
    IsWelcome="True" />
```

### Ordering
- Start with a welcome step, then go broad (layout orientation), then zoom into workflows
- Group related steps into segments by theme
- End each segment with a natural resting point

---

## Testing Your Content

### Ctrl+Shift+T (DEBUG builds)
Re-scans `TourContent.axaml` resources and starts the `post-wizard` tour.
This is the primary edit-test loop: edit AXAML, save, press Ctrl+Shift+T.
No need to run the startup wizard or restart the app.

### Debug Toolbar (DEBUG builds)
When the overlay is visible, a toolbar appears above the card with:
- **Step dropdown**: Jump to any step in the tour by key
- **R / B / L / A buttons**: Force placement override (Right / Below / Left / Above)

### Hot Reload Workflow
`TourContent.axaml` is hot-reloadable via HotAvalonia — saving the file updates
the resource dictionary in memory. However, the catalog holds *converted copies*
of the resources, so changes don't appear in a running tour automatically.

**Edit-test loop**: Edit `TourContent.axaml` > Save > **Ctrl+Shift+T**.
The shortcut re-initializes the catalog from the (now-updated) resources and
restarts the tour, so your changes appear immediately.

> **Note**: Action callbacks are C# and require a rebuild.

### Validation
`TourCatalog.Validate()` runs automatically after initialization and logs warnings
for:
- Segments with no step keys
- Tours with no segment keys
- Step keys referenced by segments that don't exist
- Segment keys referenced by tours that don't exist
- Steps with empty target values

Check the application log for `[TourCatalog]` entries.

---

## Overlay Behavior Reference

| Feature | Behavior |
|---------|----------|
| **Keyboard** | Escape = dismiss, Enter/Space/Right Arrow = advance |
| **Step counter** | "Step N of M" where M is total steps across all segments |
| **Last step** | Button changes from "Next >" to "Done" |
| **Skip Tour** | Always available — dismisses the tour at any point |
| **Welcome step** | `IsWelcome="True"` — wider card (480px), centered, no highlight or arrow |
| **Untargeted step** | `TargetKind="None"` — card centered, no highlight ring |
| **Unresolvable target** | Card shown centered at normal width, no highlight ring (3 retries at 100ms) |
| **Window resize** | Card repositions automatically |
| **Tour completion** | Key persisted to `AppSettings.CompletedTourKeys`; suppresses auto-trigger |
| **Mid-actions** | Each click runs next mid-action + auto-advances body text; card stays visible |
| **Exception safety** | All actions wrapped in try/catch; errors logged, tour continues |
| **Light-dismiss** | Suppressed during popup steps; restored on step exit and as safety net on dismiss/hide |

---

## Example: Complete Multi-Segment Tour

```xml
<!-- ── Filtering Steps ── -->

<tour:TourStepData x:Key="filters.bar"
    Title="The Filter Bar"
    Body="Use filters to narrow which sections appear on the grid. Click any filter category to expand it."
    TargetKind="NamedControl"
    TargetValue="FilterBar"
    Placement="Below" />

<tour:TourStepData x:Key="filter.tags-open"
    Title="Filtering by Tag"
    Body="This popup shows all your tags.|We just selected a tag — notice the grid filtered.|The popup is closed. Only matching sections remain."
    TargetKind="Region"
    TargetValue="ScheduleGridPanel.TagsPanel"
    Placement="Below" />

<!-- ── Filtering Segment ── -->

<tour:TourSegmentData x:Key="using-filters"
    Title="Using Filters"
    StepKeys="filters.bar,filter.tags-open" />

<!-- ── Extended Post-Wizard Tour ── -->

<tour:TourData x:Key="post-wizard"
    Title="Getting Started Tour"
    Description="Learn the layout and key workflows"
    SegmentKeys="layout-orientation,using-filters"
    AutoTrigger="PostWizardFirstLaunch"
    IsReplayable="True" />
```

The `filter.tags-open` step has 2 mid-actions and 3 body messages. The user
clicks through: see popup → select tag → close popup → advance to next step.
Each click updates the body text and runs the next action.
