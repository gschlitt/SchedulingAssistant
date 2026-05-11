# Known Workarounds

This file tracks places in the codebase where we've had to work around third-party bugs or
limitations rather than solving the root cause directly. Each entry explains what the workaround
is, why it exists, and what to do if the upstream fix ever arrives.

---

## 1 — Harmony patch to suppress AutoCompleteBox selection crash (Avalonia 12.0.x)

**Files:** `AvaloniaPatches.cs`, `Program.cs` (calls `AvaloniaPatches.Apply()`)
**Package:** `Lib.Harmony` 2.4.2 (desktop-only; excluded from browser build)
**Introduced:** April 30, 2026
**Avalonia versions affected:** 12.0.0 – 12.0.2 (unpatched as of April 2026)
**Upstream issue:** <https://github.com/AvaloniaUI/Avalonia/issues/19892>

### Symptom

Unhandled `System.ArgumentException: Control does not belong to visual tree` thrown ~5 seconds
after clicking a suggestion in the Start Time (or Block Length) AutoCompleteBox dropdown while
editing a meeting inside the section editor. The crash originates in
`VisualExtensions.PointToScreen` → `PointerEventArgs.GetPosition` →
`ItemSelectionEventTriggers.IsPointerEventWithinBounds`.

### Root cause

Two Avalonia bugs intersect:

1. **PlatformImpl goes null on native popup windows** (regression introduced in Avalonia 11.3.7,
   tracked as [#19892](https://github.com/AvaloniaUI/Avalonia/issues/19892)). The native Win32
   popup window backing an AutoCompleteBox dropdown has its `PlatformImpl` destroyed while the
   popup is still logically open.

2. **Avalonia 12 added `ItemSelectionEventTriggers.IsPointerEventWithinBounds`**, which calls
   `PointToScreen` on the `ListBoxItem` during `PointerReleased`. When `PlatformImpl` is already
   null (bug 1), `PointToScreen` throws.

The selected value has already been committed to the bound property by the time the exception
fires. The exception is purely cosmetic; suppressing it has no effect on application state.

### Current workaround: Harmony prefix patch on PointToScreen

`AvaloniaPatches.cs` uses Harmony to patch `VisualExtensions.PointToScreen(Visual, Point)` with
a prefix that checks whether the visual is still attached to a visual tree. When `VisualRoot` is
null (the visual is detached), the prefix returns `default(PixelPoint)` and skips the original
method entirely, preventing the `ArgumentException` from ever being thrown:

```csharp
static bool PointToScreenPrefix(Visual visual, ref PixelPoint __result)
{
    if (visualRootProp.GetValue(visual) == null)
    {
        __result = default;   // (0,0) — callers treat as "not within bounds"
        return false;          // skip original method
    }
    return true;               // run original normally
}
```

This is a PREFIX (not a finalizer) because the exception crosses the Win32 native/managed
boundary via the message pump. A finalizer (catch-after-throw) cannot suppress it — the CLR
re-surfaces it via SEH on the managed side. A prefix prevents the throw entirely.

Patching at the throw site (`PointToScreen`) rather than higher in the call chain is critical
because multiple independent event handlers in the `PointerReleased` routing all call
`PointToScreen` independently. Patching any single caller leaves other callers unprotected.

`AvaloniaPatches.Apply()` is called once at startup in `Program.Main`, before `BuildAvaloniaApp()`.
If `PointToScreen` is not found (e.g. Avalonia renamed it), a warning is logged and the patch
is skipped — the app still starts but the crash may recur.

### Earlier workarounds (replaced)

**Harmony finalizers on callers** — attempted to catch the exception after it was thrown, at
progressively higher levels: `PointToScreen` (finalizer), `GetPosition`,
`IsPointerEventWithinBounds`, `ShouldTriggerSelection`, `ListBoxItem.OnPointerReleased`,
`MouseDevice.MouseUp`, `MouseDevice.ProcessRawEvent`, `PresentationSource.HandleInput`. Each
patch moved the exception one level higher because: (a) multiple independent handlers throw the
same exception, and (b) the exception crosses the Win32 native/managed boundary where the CLR
re-surfaces it via SEH regardless of managed catch handlers.

**`Win32PlatformOptions { OverlayPopups = true }`** — bypassed the native popup lifecycle entirely.
**Side effect:** ComboBox dropdowns mispositioned inside ScrollViewers (rendered at the pre-scroll
visual position of the anchor). Visible in the section editor's Subject/Course/Section Type
dropdowns when the edited item was scrolled below the top of the list.

**`Dispatcher.UIThread.UnhandledException` handler** — attempted to catch the exception at the
dispatcher level. **Did not work:** the exception bypassed the dispatcher's exception filter
entirely (confirmed by absence of warning in the log file).

### Platform scope

- **Windows:** patch applies and actively suppresses the crash.
- **macOS:** patch applies but is a no-op — the PlatformImpl-null bug is Win32-specific.
- **WASM/browser:** `AvaloniaPatches.cs` is excluded from the browser build; `Lib.Harmony` is
  desktop-only. The bug does not exist in the browser backend.

### How to remove this workaround

1. Upgrade Avalonia to a version where [#19892](https://github.com/AvaloniaUI/Avalonia/issues/19892)
   is resolved.
2. Delete `AvaloniaPatches.cs`.
3. Remove `AvaloniaPatches.Apply()` from `Program.Main`.
4. Remove `Lib.Harmony` from the desktop-only packages in `SchedulingAssistant.csproj`.
5. Remove the `<Compile Remove="AvaloniaPatches.cs" />` line from the browser-build exclusions.
6. Verify the crash no longer reproduces: open the section editor, add a meeting, click a
   start time from the AutoCompleteBox dropdown, wait 10 seconds. Repeat with block length.
7. Update or remove this entry.

---

## 2 — SuppressPopupScrollBehavior: ComboBox dropdown mispositioning in WASM

**Files:** `Behaviors/SuppressPopupScrollBehavior.cs`, `Views/SectionPanelContent.axaml`
**Introduced:** May 11, 2026
**Avalonia versions affected:** 11.2.3+ through 12.0.2 (unpatched as of May 2026)
**Upstream issues:**
- <https://github.com/AvaloniaUI/Avalonia/issues/18203>
- <https://github.com/AvaloniaUI/Avalonia/issues/19356>
- <https://github.com/AvaloniaUI/Avalonia/issues/16762>

### Symptom

ComboBox dropdowns inside the section editor appear at the wrong vertical position ("floating
above" the ComboBox) when the section list is scrolled in the WASM/browser build. AutoCompleteBox
dropdowns in the same visual tree position correctly. Desktop builds are unaffected.

### Root cause

When a ComboBox inside a ListBox is clicked, two `RequestBringIntoViewEvent` events fire in quick
succession — one from the `ListBoxItem` and one from the `ComboBox` itself. The
`ScrollContentPresenter` (inside the ScrollViewer's template) handles each event by scrolling the
content. The popup position was already calculated from pre-scroll coordinates, so the dropdown
appears at the stale position.

On desktop, native popup windows handle their own positioning independently of the visual tree
scroll state. In WASM, overlay popups are rendered inside the same visual tree, so their position
is sensitive to scroll changes that occur between position calculation and rendering.

### Current workaround: suppress RequestBringIntoView on the ScrollViewer's content

`SuppressPopupScrollBehavior` is an attached behavior that catches `RequestBringIntoViewEvent`
and marks it `Handled` before it can reach the `ScrollContentPresenter`.

The behavior must be placed on a control **inside** the ScrollViewer (the content StackPanel),
not on the ScrollViewer itself. The `ScrollContentPresenter` sits between the content and the
ScrollViewer in the visual tree; a handler on the ScrollViewer fires after the scroll has
already happened.

In `SectionPanelContent.axaml`:
```xml
<ScrollViewer ...>
    <StackPanel b:SuppressPopupScrollBehavior.IsEnabled="True">
        ...
    </StackPanel>
</ScrollViewer>
```

### Side effects

Keyboard-driven `BringIntoView` scrolling (e.g. Tab-navigating to an off-screen control) is
suppressed within the section panel's ScrollViewer. Users still scroll manually via mouse wheel
or scrollbar. This is acceptable because the section editor is a mouse/touch-driven UI.

**Cross-view selection sync** (clicking a section in the Schedule Grid scrolls the Section View
to that card) would also be broken, since the ListBox's built-in scroll-to-selected relies on
`RequestBringIntoView`. To compensate, `SectionListView.axaml.cs` listens for `SelectedItem`
property changes on the ViewModel and scrolls via direct `ScrollViewer.Offset` manipulation,
bypassing `RequestBringIntoView` entirely. The same technique is used for `EditVm` changes
(editor open/close layout shifts). See `ScrollSelectedItemIntoView()`.

### Platform scope

- **WASM/browser:** workaround is active and prevents the mispositioning.
- **Windows/macOS:** workaround is active but has no visible effect — desktop popups use native
  windows whose positioning is independent of `RequestBringIntoView` scroll changes.

### How to remove this workaround

1. Upgrade Avalonia to a version where [#18203](https://github.com/AvaloniaUI/Avalonia/issues/18203)
   is resolved.
2. Remove `b:SuppressPopupScrollBehavior.IsEnabled="True"` from the StackPanel in
   `SectionPanelContent.axaml`.
3. Delete `Behaviors/SuppressPopupScrollBehavior.cs`.
4. In `SectionListView.axaml.cs`, the `SelectedItem` and `EditVm` property-change handlers
   that call `ScrollSelectedItemIntoView()` can optionally be removed — the ListBox's built-in
   `BringIntoView` will resume working. However, keeping them is harmless and provides a
   consistent scrolling experience.
5. Verify: in the WASM build, scroll the section list down, open a section editor, click the
   Day ComboBox. The dropdown should appear directly below the ComboBox, not floating above.
6. Verify: click a section in the Schedule Grid — the Section View should scroll to show that
   section's card.
7. Update or remove this entry.
