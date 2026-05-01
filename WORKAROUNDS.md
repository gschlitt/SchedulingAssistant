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
