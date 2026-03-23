# Cerebrum

> OpenWolf's learning memory. Updated automatically as the AI learns from interactions.
> Do not edit manually unless correcting an error.
> Last updated: 2026-03-22

## User Preferences

<!-- How the user likes things done. Code style, tools, patterns, communication. -->

- **Military time throughout**: All time displays use HHMM with no colon separator (e.g. "0830", "1230"). `FormatTime(int minutes)` must output `$"{h:D2}{m:D2}"`. `ParseTime(string)` accepts HHMM digits only: `h = hhmm / 100`, `m = hhmm % 100`. Watermarks and error hints use "HHMM" and "0915" style examples. Apply this to every new or modified time-display context.

## Key Learnings

- **Project:** SchedulingAssistant
- **ComputeGridlineOffsets**: Phase 3 of the grid renderer is now `internal static` on `ScheduleGridViewModel`, accepting `IReadOnlyDictionary<(int start, int end), (double timeBasedH, double actualH)>`. The old inline loop used `start <= mins` (wrong for non-aligned tiles) and `1/(span/30)` fraction (over-counts boundary tiles). Fixed: use `start < mins+30 && end > mins` overlap condition; fraction = `overlapMinutes / tileSpanMinutes`.
- **Grid time range**: `AppSettings` now has `GridStartMinutes` (default 510 = 08:30) and `GridEndMinutes` (default 1320 = 22:00). `ScheduleGridViewModel.UpdateDisplayProperties` reads these instead of using `const int`. `GridData.Empty` is now a property (not `static readonly` field) so it reads live settings. `CommitmentEditViewModel.BuildTimeOptions()` uses the same range, clamped so start ≤ 08:00 to always include early-morning options.
- **HotAvalonia `using`**: The `using HotAvalonia;` in `App.axaml.cs` must be inside `#if DEBUG` because the package is Debug-only. Without the guard, Release builds (and test runs in Release config) fail with CS0246. Fixed in this session.
- **Test config for locked exe**: When the app is running in Debug, `dotnet test -c Release` is needed to avoid the locked `TermPoint.exe` copy error during build.

## Do-Not-Repeat

<!-- Mistakes made and corrected. Each entry prevents the same mistake recurring. -->
<!-- Format: [YYYY-MM-DD] Description of what went wrong and what to do instead. -->

- [2026-03-22] Do NOT use `tile.StartMinutes <= mins` to check if a tile covers a 30-min slot. Use `tile.StartMinutes < mins + 30 && tile.EndMinutes > mins` (proper overlap). The old condition silently drops non-aligned tile starts from the preceding slot.
- [2026-03-22] Do NOT use `1.0 / (tileSpanMinutes / 30.0)` as the expansion fraction per slot. Use `overlapMinutes / tileSpanMinutes` to correctly apportion expansion across partial-overlap slots.
- [2026-03-22] Do NOT rely on Avalonia's built-in Tab navigation for `AutoCompleteBox` inside a `DataTemplate`/`ItemsControl`. Two problems combine: (1) `LostFocusEvent` is a bubbling event — `RoutingStrategies.Direct` on a parent never fires; use `Bubble` + `IsKeyboardFocusWithin` guard. (2) Tab from `AutoCompleteBox`'s inner TextBox navigates from the TextBox's position in the *global* visual tree, jumping completely out of the DataTemplate row (confirmed: lands on `GridSplitter`). Fix: use a `RoutingStrategies.Tunnel` KeyDown handler, mark Tab `Handled`, and manually focus the next/previous focusable sibling by walking `acb.Parent` (Panel) children. See `OpenDropDownOnFocusBehavior.cs`.
- [2026-03-22] Do NOT use Avalonia's editable `ComboBox` (`IsEditable="True"`) for fields that need to accept arbitrary typed values. When the typed text doesn't match any item in `ItemsSource`, Avalonia resets `Text` to empty on LostFocus. Use `AutoCompleteBox` instead — it always preserves typed text.

## Key Learnings (continued)

- **LostFocusCommandBehavior**: `LostFocusEvent` is a *bubbling* event in Avalonia. The behavior must use `RoutingStrategies.Bubble` and guard with `c.IsKeyboardFocusWithin` (skip if focus only moved between children, e.g. to the dropdown popup). Direct routing silently never fires on composite controls like `AutoCompleteBox`.
- **OpenDropDownOnFocusBehavior**: Use on `AutoCompleteBox` with `MinimumPrefixLength="0"` and `FilterMode="None"` to give it ComboBox-like click-to-open behaviour. Opens dropdown only on `NavigationMethod.Pointer` focus (not Tab). Owns Tab key entirely via tunnel handler — see Do-Not-Repeat above.
- **SectionMeetingViewModel reverse lookup**: Start time is now chosen first; block lengths are populated by `RefreshBlockLengths()` (reverse lookup: finds all `LegalStartTime` rows where `StartTimes.Contains(selectedStart)`). Falls back to all known block lengths when the chosen start time is not in the table (custom time). Helpers `FormatTime`, `FormatBlockLength`, `ParseTime`, `ParseBlockLength` are `internal static` for testability.

- **Avalonia ToolTip background**: When you pass arbitrary content to `ToolTip.SetTip`, Avalonia wraps it in its own default `ToolTip` control (white background). To control the tooltip's background/border/padding, instantiate a `ToolTip` directly and set its properties, then pass that instance to `ToolTip.SetTip`. The `ToolTip` class extends `ContentControl`; set `Content` for the inner display.
- **TileTooltip pattern**: `TileTooltip(IReadOnlyList<string> Lines)` record in `GridData.cs`. `ScheduleGridViewModel.BuildTileTooltip(GridTile)` builds it (static, testable). `ScheduleGridView.axaml.cs.BuildTileTooltipContent(TileTooltip)` renders it as a `ToolTip` with tile styling. Adding new tooltip lines only requires a change to `BuildTileTooltip`.

## Do-Not-Repeat (continued)

- [2026-03-22] When adding a new constructor parameter to a ViewModel (e.g. `ICampusRepository`), search for ALL call sites — including non-DI instantiation points like `SectionPropertiesViewModel` (which manually `new`s its child VMs) and `SectionListViewModel.GenerateRandomSections()` (which manually `new`s `DebugTestDataGenerator`). DI-registered types auto-resolve but manual `new` calls will silently break at compile time (CS7036).

## Key Learnings (continued)

- **Campus is now a first-class entity**: `Campus` model, `Campuses` table, `ICampusRepository`/`CampusRepository`. No longer a `SectionPropertyValue` row with `type='campus'`. `SeedData.FindOrCreateCampus` is `public static` and targets the `Campuses` table. `SectionPropertyTypes.Campus` constant has been removed.
- **RoomRow display pattern**: `Room` has no `CampusName` string. A `record RoomRow(Room Room, string CampusName)` wraps it for DataGrid display. DataGrid bindings use `Room.Building`, `Room.Name`, `CampusName` etc. (same as `SectionPrefixRow` pattern).
- **CampusListView**: Campus CRUD lives under Settings (not Section Properties flyout). `CampusListViewModel` + `CampusEditViewModel` follow the same inline-edit pattern as `SectionPropertyListViewModel`.

## Decision Log

<!-- Significant technical decisions with rationale. Why X was chosen over Y. -->
