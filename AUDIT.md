# Critical Audit: SchedulingAssistant

**Date**: 2026-03-03

## Executive Summary

The codebase is well-structured and disciplined for a desktop LOB application. Security posture is strong. MVVM adherence is high. The most actionable finding is a **memory leak from transient ViewModels subscribing to singleton events**. The rest are minor pattern inconsistencies that don't affect correctness or maintainability in a meaningful way.

---

## 1. Security

**Verdict: Strong. No vulnerabilities found.**

| Area | Status | Notes |
|------|--------|-------|
| SQL Injection | Secure | 100% parameterized queries across all 12 repositories |
| JSON Deserialization | Safe | `System.Text.Json` with default (safe) options; no `TypeNameHandling` |
| File System | Secure | Native OS file pickers; `Path.GetFullPath()` normalization on recent DB list |
| Resource Disposal | Clean | All `SqliteCommand`/`SqliteDataReader` objects wrapped in `using var` |
| Settings | Appropriate | No credentials stored; only paths and preferences |
| Error Handling | Good | Exceptions logged with context; logger itself is non-throwing (`FileAppLogger`) |
| Transactions | Correct | Delete operations use `BeginTransaction` + rollback on failure |

No further action needed.

---

## 2. CLAUDE.md Adherence

**Verdict: Strict compliance. No deviations from documented architectural decisions.**

- DI registration matches documented singleton/transient split exactly (`App.axaml.cs:76-144`)
- `x:CompileBindings="False"` enforced project-wide via `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` in `.csproj`, plus explicit per-view attributes on all 22 XAML files
- JSON column pattern (`id TEXT PRIMARY KEY` + `data TEXT`) used consistently; foreign keys in separate SQL columns for querying
- Step-gate snapshot pattern in `SectionEditViewModel` implemented exactly as documented
- ViewLocator convention-based resolution working correctly
- Flyout overlay navigation pattern consistent across all management screens

---

## 3. MVVM Pattern Usage

**Verdict: Good. Wise use of MVVM — pragmatic, not ceremonial. A few structural notes below.**

### What's done well
- **CommunityToolkit.Mvvm** used throughout: `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]` — no manual `INotifyPropertyChanged` boilerplate
- **Code-behind is minimal and justified**: `SectionListView.axaml.cs` handles `LostFocus` forwarding (DataTemplate limitation); `ScheduleGridView.axaml.cs` does canvas rendering (inherently imperative); dialog creation delegates (`ShowError`, `ShowConfirmation`) keep ViewModels free of Avalonia Window references
- **No God classes**: ViewModels are focused on their domain. `SectionEditViewModel` is large (~500 lines) but that reflects the complexity of the section editor, not poor decomposition

### Findings

#### 3a. Memory Leak: Transient VM subscribes to singleton event (Real bug)

**File**: `InstructorListViewModel.cs:52-56`

```csharp
_semesterContext.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
        RefreshWorkload();
};
```

`InstructorListViewModel` is **transient** — a new instance is created each time the Instructors flyout opens. `SemesterContext` is a **singleton**. The lambda captures `this`, so the singleton's event delegate list holds a strong reference to every `InstructorListViewModel` ever created. They accumulate and never get GC'd.

**Fix**: Make InstructorListViewModel implement `IDisposable`; have `MainWindowViewModel` dispose old flyout pages when `FlyoutPage` changes.

#### 3b. Cross-view selection sync is split between VM and View layers

Selection propagation is asymmetric:
- **SectionListVm -> ScheduleGridVm**: Direct VM-to-VM (`SectionListViewModel.cs:60`)
- **ScheduleGridVm -> SectionListVm + WorkloadPanelVm**: Via `MainWindow.axaml.cs:315-341`
- **WorkloadPanelVm -> SectionListVm**: Via `MainWindow.axaml.cs:350-365`

This works because all participants are singletons with matching lifetimes. **Not a bug — just an observation.**

#### 3c. Dialog delegates vs. IDialogService

ViewModels expose callback properties for dialogs (`ShowError`, `ShowConfirmation`). Views wire these in code-behind. Pragmatic approach that avoids `IDialogService` ceremony. Tradeoff: forgetting to wire a delegate results in a silent no-op. **Low priority.**

#### 3d. Service locator usage in CopySemesterViewModel

`CopySemesterViewModel.cs:143-144` reaches into the DI container directly for navigation. Documented as an intentional simplification. **Low impact** — single call site.

---

## 4. Other Findings

#### 4a. Debug code marked for removal (already tracked)

- `GenerateRandomSections()` verbose logging (`SectionListViewModel.cs:92-131`)
- `SimulateLoadError()` / `SimulateReloadError()` (`SectionListViewModel.cs:82-87`, `ScheduleGridViewModel.cs`)
- `#if DEBUG` hotkey handlers in `MainWindow.axaml.cs:43-61`

All properly gated behind `#if DEBUG`. No risk of shipping, but should be cleaned up.

#### 4b. No input length validation

User-entered strings have no maximum length constraint before reaching the database. SQLite TEXT columns accept unlimited length, so this won't cause a crash, but very long strings could break UI layout. **Low risk** for an internal admin tool.

#### 4c. Async commands lack try-catch in some paths

Some `[RelayCommand] private async Task` methods don't wrap their body in try-catch. CommunityToolkit's `AsyncRelayCommand` catches exceptions but won't surface them to the user visually. This matters most for save/delete operations where silent failure loses data.

---

## Summary of Actionable Items

| # | Finding | Severity | Effort |
|---|---------|----------|--------|
| 1 | Memory leak: `InstructorListViewModel` subscribes to `SemesterContext.PropertyChanged` without cleanup | **Medium** | Small |
| 2 | Audit async write commands for missing error handling | **Medium** | Small |
| 3 | Pre-shipping debug cleanup (already tracked) | Low | Small |
| 4 | Input length validation | Low | Small |
| 5 | Cross-view sync asymmetry | Informational | N/A |
| 6 | Dialog delegate vs IDialogService | Informational | N/A |
| 7 | Service locator in CopySemesterViewModel | Informational | N/A |

Items 1-2 are worth fixing. Items 3-4 are nice-to-haves. Items 5-7 are design observations, not bugs.
