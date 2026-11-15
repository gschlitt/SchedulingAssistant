# Demo Data Tour on Desktop

## Context
The walkthrough tour auto-triggers after the startup wizard via `PostWizardFirstLaunch`. On desktop, this fires on an empty database — the user just created it and hasn't entered any data. The schedule grid, section list, and workload panel are all blank, making the tour unhelpful. The fix: temporarily load demo data before the tour runs, then switch back to the user's real database when it ends.

---

## Approach: Side-by-Side Demo Container

Build a temporary `IServiceProvider` from `ConfigureDemoServices` (already exists for WASM), resolve a demo `MainWindowViewModel` from it, swap `MainWindow.DataContext`, run the tour, then swap back and dispose the demo container.

**Why this works:**
- `App.Services` (real DI) stays alive and untouched
- `App.LockService` / `App.Checkout` are pre-DI singletons — unaffected
- `TourCatalog` is static — shared across containers
- `AppSettings.Current` is static — `PersistCompletion` works across containers
- Swapping `MainWindow.DataContext` triggers `MainView.OnDataContextChanged` which re-wires all cross-VM callbacks automatically

---

## Files to Modify (2)

### 1. `App.axaml.cs`
`ConfigureDemoServices` (line 266) is inside `#if BROWSER`. The Demo* classes themselves compile on all platforms (verified: no `#if BROWSER` guards in `Demo/`), but the registration method is guarded.

**Change:** Add a new `internal static` method outside the `#if BROWSER` block that builds a standalone demo service provider without touching `App.Services`:

```csharp
internal static IServiceProvider BuildDemoServiceProvider()
{
    var services = new ServiceCollection();
    ConfigureDemoServices(services);  // call into existing method
    return services.BuildServiceProvider();
}
```

Move `ConfigureDemoServices` itself outside the `#if BROWSER` guard (its body is platform-neutral — all Demo repos + `NullDialogService` + `RegisterViewModels` compile everywhere). Keep `InitializeDemoServices` inside `#if BROWSER` since it mutates `App.Services`.

### 2. `MainWindow.axaml.cs`
Replace the auto-trigger evaluation (lines 472-476) with demo-tour detection + a self-contained demo-tour flow.

**New private method `ShouldRunDemoTour`:**
```csharp
private static bool ShouldRunDemoTour()
{
    var settings = AppSettings.Current;
    if (settings.CompletedTourKeys.Contains("post-wizard")) return false;
    if (!settings.IsInitialSetupComplete) return false;
    if (App.Services.GetService(typeof(SectionStore)) is SectionStore store)
        return store.Sections.Count == 0;
    return false;
}
```

Uses `SectionStore.Sections.Count == 0` as a proxy for "database is empty" — no interface changes needed. Right after the wizard, no sections exist in any semester.

**New private method `RunDemoTourAsync`:**
```csharp
private async Task RunDemoTourAsync(string tourKey)
{
    // 1. Pause autosave (real DB stays checked out, lock held)
    App.Checkout.StopAutoSave();

    // 2. Build side-car demo container
    var demoProvider = App.BuildDemoServiceProvider();

    // 3. Initialize demo data (mirrors WASM's InitializeDemoServices)
    demoProvider.GetRequiredService<WriteLockService>().AcquireDemo();
    var semCtx = demoProvider.GetRequiredService<SemesterContext>();
    semCtx.Reload(
        demoProvider.GetRequiredService<IAcademicYearRepository>(),
        demoProvider.GetRequiredService<ISemesterRepository>(),
        restoreAcademicYearId: "demo-ay-1",
        restoreSemesterIds: new HashSet<string> { "demo-sem-1" });
    var store = demoProvider.GetRequiredService<SectionStore>();
    store.Reload(
        demoProvider.GetRequiredService<ISectionRepository>(),
        semCtx.SelectedSemesters.Select(s => s.Semester.Id));

    // 4. Swap DataContext to demo VM
    var demoVm = demoProvider.GetRequiredService<MainWindowViewModel>();
    demoVm.SetDatabaseName("Tour Preview");
    var realVm = DataContext;
    DataContext = demoVm;

    // 5. Start tour and await completion
    var runner = demoProvider.GetRequiredService<TourRunner>();
    var tcs = new TaskCompletionSource();
    void onDone() => tcs.TrySetResult();
    runner.TourCompleted += onDone;
    runner.TourDismissed += onDone;

    if (!runner.Start(tourKey))
    {
        // Tour failed to start — restore immediately
        DataContext = realVm;
        (demoProvider as IDisposable)?.Dispose();
        if (AppSettings.Current.AutoSaveEnabled) App.Checkout.StartAutoSave();
        return;
    }

    await tcs.Task;
    runner.TourCompleted -= onDone;
    runner.TourDismissed -= onDone;

    // 6. Restore real DataContext and clean up
    DataContext = realVm;
    (demoProvider as IDisposable)?.Dispose();
    if (AppSettings.Current.AutoSaveEnabled) App.Checkout.StartAutoSave();
}
```

**Modified auto-trigger block (replaces lines 472-476):**
```csharp
Dispatcher.UIThread.Post(async () =>
{
    if (ShouldRunDemoTour())
    {
        await RunDemoTourAsync("post-wizard");
    }
    else if (App.Services.GetService(typeof(TourRunner)) is TourRunner tourRunner)
    {
        tourRunner.EvaluateAutoTriggers();
    }
}, DispatcherPriority.Background);
```

After `RunDemoTourAsync` completes, `PersistCompletion` has already added `"post-wizard"` to `AppSettings.CompletedTourKeys`, so the tour won't re-trigger on next launch.

---

## Control Flow

```
SetupMainWindowAsync(dbPath, realVm)
  ├── DataContext = realVm  (empty DB)
  ├── IsVisible = true
  └── Dispatcher.Post(Background) ──┐
                                     │
                ShouldRunDemoTour()? ─┤── NO → EvaluateAutoTriggers() (normal)
                                     │
                                    YES
                                     │
                RunDemoTourAsync("post-wizard")
                  ├── StopAutoSave
                  ├── BuildDemoServiceProvider()
                  ├── Initialize demo SemesterContext + SectionStore
                  ├── DataContext = demoVm  (populated)
                  ├── TourRunner.Start("post-wizard")
                  ├── await TourCompleted | TourDismissed
                  ├── DataContext = realVm  (empty, back to normal)
                  ├── Dispose demo container
                  └── StartAutoSave
```

---

## Edge Cases

| Scenario | Handling |
|---|---|
| Tour fails to start (bad catalog key) | `Start()` returns false → restore real VM immediately |
| Exception during demo container build | try-catch → log, restore real VM, skip tour |
| User closes window mid-tour | `OnClosing` fires; `tcs.Task` resolves if `Dismiss()` is called during teardown |
| Returning user with data | `SectionStore.Sections.Count > 0` → `ShouldRunDemoTour` returns false → normal path |
| Second launch after completing tour | `CompletedTourKeys` contains `"post-wizard"` → normal path |
| Autosave during demo | Paused via `StopAutoSave()`; resumed after restore |

---

## Verification

1. **Fresh install test**: Delete AppSettings → run wizard → tour should auto-start with populated demo data → complete tour → verify empty real DB is shown → restart app → verify tour does NOT re-trigger
2. **Dismiss test**: Same setup but press Escape mid-tour → verify real DB restored → verify tour does NOT re-trigger on restart
3. **Returning user test**: Open existing DB with sections → verify `ShouldRunDemoTour` returns false → normal auto-trigger path runs
4. **WASM test**: Verify browser demo still works independently (uses its own `InitializeDemoServices` path)
5. **Build**: `dotnet build` both `net10.0` and `net10.0-browser` targets — verify `ConfigureDemoServices` compiling outside `#if BROWSER` introduces no errors

---

## Already Done (this session)

- Fixed WASM auto-trigger: added `AppSettings.Current.IsInitialSetupComplete = true` + `EvaluateAutoTriggers()` call in `App.InitializeDemoServices()` — WASM tour now fires correctly.
