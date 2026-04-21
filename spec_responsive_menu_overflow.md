# Responsive Menu Bar with Dynamic Overflow — Specification

## Goal
Replace the current `WrapPanel`-based top menu bar with a **single-line, dynamically measured** bar. When horizontal space runs out, low-priority items are hidden from the bar and surfaced behind a **"More…"** button that opens a flyout with a left-rail submenu.

The bar never wraps to two lines.

## Item Priority (Static)

Order in the bar is preserved from the current layout.

### Priority items — always visible, never collapse into More
Left-to-right:
1. **Files** menu
2. Separator
3. Database name (TextBlock)
4. Save button + "Unsaved changes" + Auto-save checkbox group
5. Separator
6. Academic Year picker + Semester picker group
7. Separator
8. **Courses** button
9. **Instructors** button

If the window is narrower than the priority group's total desired width, the bar **clips** (no horizontal scroll, no wrap). This is intentional — a window that small signals the user to widen.

### Low-priority items — collapse into "More…" right-to-left as space shrinks
In declared order (same as their current order in the bar):
1. **Scheduling Environment**
2. **Academic Years**
3. **Configuration**
4. **Export**
5. **Help**

### Debug menu (DEBUG builds only)
Stays in the **main bar**, after Help. Treated as a priority item for overflow purposes (never collapses into More).

## Collapse Rules

- The rightmost low-priority item collapses first, then the next rightmost, and so on, until remaining bar content + the "More…" button fits in the available width.
- "More…" is **hidden** when all low-priority items fit.
- "More…" is **visible** the moment any low-priority item is hidden.
- The More flyout's left rail contains **only the currently-hidden items** — items that are still visible in the main bar do not appear in More. (Rationale: an item should never appear in two places at once.)

## Implementation

### Custom panel: `ResponsiveMenuPanel`
New file: `src/SchedulingAssistant/Controls/ResponsiveMenuPanel.cs`

A `Panel` subclass that arranges children in a single horizontal row. It handles measure/arrange and overflow collection.

**Children**: declared in XAML in full order (priority + low-priority + Debug + a terminal "More…" button marked via attached property).

**Attached properties**:
```csharp
public static readonly AttachedProperty<bool> IsPriorityProperty;   // default false
public static readonly AttachedProperty<bool> IsMoreButtonProperty; // default false
```
- `IsPriority="True"` → never collapsed.
- `IsMoreButton="True"` → the trailing "More…" button; always last in declared order; panel toggles its `IsVisible` based on whether anything is hidden.

**Algorithm** (in `MeasureOverride` + `ArrangeOverride`):
1. Measure every child against infinite width to capture each `DesiredSize.Width`.
2. Start with all low-priority children **visible**; More **hidden**.
3. If `Σ desired widths > availableWidth`:
   - Set More **visible**; reserve `moreButton.DesiredSize.Width`.
   - Walk low-priority children **right-to-left in declared order**, marking each as hidden until `Σ(visible children) + moreWidth ≤ availableWidth` OR no low-priority items remain visible.
4. Priority items and Debug are never hidden (if their total exceeds width, the panel arranges at desired size and the final children clip against the parent `Border`).
5. `ArrangeOverride` places visible children left-to-right at their desired sizes; hidden children get a zero-size arrange rect.
6. Publish the ordered list of currently-hidden low-priority items via a bindable read-only property:
   ```csharp
   public IReadOnlyList<Control> HiddenOverflowItems { get; }
   ```
   The panel raises a `HiddenOverflowItemsChanged` routed event after each arrange pass (coalesced — only when the hidden set actually changes).

**Why a custom panel, not `SizeChanged` + `IsVisible` toggles on the VM**: the panel owns measurement, so the logic is deterministic, reacts correctly to font/DPI/content changes, and requires no manual width constants.

### XAML wiring (`MainWindow.axaml`)
Replace the current `WrapPanel` (lines ~62–277) with a `controls:ResponsiveMenuPanel`. Children stay in the same declared order. Tag each with the appropriate attached property:

```xml
<controls:ResponsiveMenuPanel x:Name="TopMenuPanel" VerticalAlignment="Center">
    <!-- Priority items -->
    <Menu controls:ResponsiveMenuPanel.IsPriority="True" ...>Files…</Menu>
    <Border controls:ResponsiveMenuPanel.IsPriority="True" .../>  <!-- separator -->
    <TextBlock controls:ResponsiveMenuPanel.IsPriority="True" Text="{Binding DatabaseName}" .../>
    <StackPanel controls:ResponsiveMenuPanel.IsPriority="True" ...>Save/Unsaved/Auto-save</StackPanel>
    <Border controls:ResponsiveMenuPanel.IsPriority="True" .../>
    <StackPanel controls:ResponsiveMenuPanel.IsPriority="True" ...>AY + Semester pickers</StackPanel>
    <Border controls:ResponsiveMenuPanel.IsPriority="True" .../>
    <Button controls:ResponsiveMenuPanel.IsPriority="True" Content="Courses" .../>
    <Button controls:ResponsiveMenuPanel.IsPriority="True" Content="Instructors" .../>

    <!-- Low-priority items (no IsPriority attribute) -->
    <Button x:Name="NavSchedulingEnvironment" Content="Scheduling Environment" .../>
    <Button x:Name="NavAcademicYears"         Content="Academic Years" .../>
    <Button x:Name="NavConfiguration"         Content="Configuration" .../>
    <Button x:Name="NavExport"                Content="Export" .../>
    <Button x:Name="NavHelp"                  Content="Help" .../>

    <!-- Debug menu — priority in DEBUG builds -->
    <Menu x:Name="DebugMenu" controls:ResponsiveMenuPanel.IsPriority="True" IsVisible="False" ...>…</Menu>

    <!-- More button — always the last child; panel toggles its visibility -->
    <Button x:Name="MoreButton"
            controls:ResponsiveMenuPanel.IsMoreButton="True"
            Classes="nav"
            Content="More…"
            ToolTip.Tip="Show additional menu items"
            Command="{Binding OpenMoreMenuCommand}" />
</controls:ResponsiveMenuPanel>
```

The parent `Border` keeps `ClipToBounds="True"` (Avalonia default for `Border`) so that any priority-group overrun clips cleanly.

### ViewModel changes (`MainWindowViewModel`)

Reuse the existing flyout mechanism — no new overlay.

1. **New view model**: `MoreMenuViewModel` (in `ViewModels/`). Holds:
   - `ObservableCollection<MoreMenuEntry> Entries` — the currently-hidden low-priority items.
   - `MoreMenuEntry? SelectedEntry` — drives the right pane.
   - `object? ContentPage` — the sub-VM displayed in the right pane, set when `SelectedEntry` changes.
   - Each `MoreMenuEntry` exposes `DisplayName`, `NavCommand` (a reference to the existing `NavigateToXxxCommand` on `MainWindowViewModel`), and an internal tag identifying which sub-VM to instantiate for in-flyout hosting.

2. **New command on `MainWindowViewModel`**: `OpenMoreMenuCommand` →
   ```csharp
   MoreMenuVm.Refresh(hiddenEntries);   // populated by codebehind from panel
   FlyoutPage = MoreMenuVm;
   FlyoutTitle = "More";
   IsMoreOpen = true;                   // drives main-bar "More…" highlight
   ```

3. **New bindable property**: `bool IsMoreOpen` — true while `FlyoutPage is MoreMenuViewModel`. Main-bar "More…" binds `Classes.active` to this for the highlight. Cleared automatically when `FlyoutPage` is set to any non-More VM or to `null` (`CloseFlyoutCommand`).

4. **Flyout title updates**: when `MoreMenuVm.SelectedEntry` changes, `MoreMenuVm` raises an event (or uses a delegate injected by `MainWindowViewModel`) that updates `MainWindowViewModel.FlyoutTitle` to `"More › <entry name>"`. When no entry is selected, title is just `"More"`.

### CodeBehind (`MainWindow.axaml.cs`)

Subscribe to `TopMenuPanel.HiddenOverflowItemsChanged` after `InitializeComponent()`. On each change:
- Build a list of `MoreMenuEntry` from the panel's `HiddenOverflowItems`, mapping each control by `x:Name` (or a dictionary keyed on a small `MenuKey` attached property) to:
  - the entry's `DisplayName` (taken from the control's content or a fixed table),
  - the `NavigateToXxxCommand` on the VM (for the "open in main flyout" fallback, if we choose to support it),
  - the sub-VM factory used when hosting in-flyout.
- Push the list into `((MainWindowViewModel)DataContext).MoreMenuVm.AvailableEntries`.
- If the More flyout is currently open and the user resizes the window such that all previous overflow items now fit, `MoreMenuVm.Entries` becomes empty — the spec below covers this case.

### `MoreMenuView` (new)
New files: `Views/Management/MoreMenuView.axaml(.cs)`, structurally mirroring `SchedulingEnvironmentView.axaml`:

```xml
<Grid ColumnDefinitions="160,*">
    <Border Grid.Column="0" Padding="0,0,8,0" BorderBrush="{StaticResource ItemSeparator}" BorderThickness="0,0,1,0">
        <ListBox ItemsSource="{Binding Entries}" SelectedItem="{Binding SelectedEntry}" Background="Transparent" BorderThickness="0">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Padding="8,6" FontSize="13" Text="{Binding DisplayName}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Border>
    <ContentControl Grid.Column="1" Margin="10,0,0,0" Content="{Binding ContentPage}" />
</Grid>
```

**Nested sidebars** (e.g. Scheduling Environment's own 160px rail inside More's 160px rail) are accepted per design decision. A follow-up styling pass may want to slightly reduce the inner rail's padding or separator weight for visual clarity — flagged but not scoped here.

## Behavior Details

### Highlighting "More…" in the main bar
- Add CSS class rule in `App.axaml` or `MainWindow.axaml` styles:
  ```xml
  <Style Selector="Button.nav.active">
      <Setter Property="Background" Value="{StaticResource NavButtonActive}" />
  </Style>
  ```
  (Reuse whatever active-state brush the existing nav buttons already use on hover/press — to be chosen during implementation.)
- Bind on the More button: `Classes.active="{Binding IsMoreOpen}"`.

### Flyout title progression
- Opening More (no entry selected): `"More"`.
- Entry selected: `"More › Scheduling Environment"` (etc.).
- Closing the flyout clears the title as usual.

### Dismissal
Reuses the existing `FlyoutPage` overlay — Escape, backdrop click, and the flyout header ✕ all close via `CloseFlyoutCommand`. No new dismissal paths.

### Edge case: overflow items change while More is open
If the user resizes the window while the More flyout is open:
- **Window widens** — `HiddenOverflowItems` shrinks. `MoreMenuVm.Entries` is rebuilt. If the previously-selected entry is no longer in the list (it's now visible in the main bar), keep showing its content page (don't yank it out from under the user); the left rail simply no longer lists it. Title remains `"More › <name>"`. User can still dismiss normally.
- **All overflow items become visible** — `Entries` is empty. Show a centered placeholder in the left rail: *"No additional items."* (small, muted). The right pane keeps whatever was last selected.
- **Window narrows** — `Entries` grows; no special handling.

### Edge case: in-flyout navigation
Clicking an entry in More's left rail sets `ContentPage` to a fresh instance of that management VM (Scheduling Environment, Academic Years, Configuration, Export, Help). The existing top-bar `NavigateToXxxCommand` handlers are *not* invoked — those replace the entire flyout. In-flyout hosting is distinct and stays within More.

## Files to Add
- `src/SchedulingAssistant/Controls/ResponsiveMenuPanel.cs`
- `src/SchedulingAssistant/ViewModels/MoreMenuViewModel.cs`
- `src/SchedulingAssistant/ViewModels/MoreMenuEntry.cs` (small record-like class)
- `src/SchedulingAssistant/Views/Management/MoreMenuView.axaml`
- `src/SchedulingAssistant/Views/Management/MoreMenuView.axaml.cs`

## Files to Modify
- `src/SchedulingAssistant/MainWindow.axaml` — replace top-bar `WrapPanel` with `ResponsiveMenuPanel`; add `x:Name` to low-priority buttons; add More button.
- `src/SchedulingAssistant/MainWindow.axaml.cs` — wire `HiddenOverflowItemsChanged` → `MoreMenuVm`.
- `src/SchedulingAssistant/ViewModels/MainWindowViewModel.cs` — add `MoreMenuVm`, `OpenMoreMenuCommand`, `IsMoreOpen`; route `FlyoutTitle` updates from `MoreMenuVm`.
- `src/SchedulingAssistant/App.axaml` (or `MainWindow.axaml` styles) — add `Button.nav.active` selector.

## Testing Checklist
- [ ] At wide window: no "More…" button visible; all items in bar.
- [ ] Narrowing the window collapses **Help first**, then Export, Configuration, Academic Years, Scheduling Environment, in that order.
- [ ] At each collapse step, the More flyout's left rail lists exactly the currently-hidden items in declared order.
- [ ] Widening the window restores items; "More…" hides when nothing is hidden.
- [ ] Debug menu (DEBUG builds) never collapses into More.
- [ ] Priority group never hides; bar clips when window is absurdly narrow (no wrap, no scroll).
- [ ] Clicking "More…" opens the flyout; main-bar "More…" button stays highlighted until dismissed.
- [ ] Flyout title reads `"More"` with nothing selected, `"More › <name>"` when an entry is selected.
- [ ] Selecting an entry in More's rail hosts its management VM in the right pane (nested sidebar acceptable).
- [ ] Escape / backdrop / ✕ dismiss the More flyout and clear the highlight.
- [ ] Resizing while More is open rebuilds the rail without throwing or losing the current content pane.
- [ ] No double-paint / flicker on rapid resize (panel's hidden-set change event is coalesced).
