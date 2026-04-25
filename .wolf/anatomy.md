# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-25T00:30:53.094Z
> Files: 36 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/

- `cheeky-orbiting-patterson.md` — Course History CSV Export (~993 tok)
- `zesty-brewing-feigenbaum.md` — Plan: NetworkFileOps — Centralized Network Timeout Utility (~1780 tok)

## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/

- `MEMORY.md` — SchedulingAssistant Project Memory (~6152 tok)
- `network_timeout_wrappers.md` — Problem (discovered April 2026) (~936 tok)

## ./

- `publish-mac.sh` (~487 tok)
- `publish.ps1` (~936 tok)

## .claude/


## .claude/rules/


## .claude/worktrees/dazzling-bardeen/


## .claude/worktrees/dazzling-bardeen/.claude/


## .claude/worktrees/dazzling-bardeen/docs/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Behaviors/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Controls/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Converters/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/Repositories/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Models/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Services/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/GridView/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/Management/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/GridView/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/Management/


## .claude/worktrees/interesting-wilbur/


## .claude/worktrees/interesting-wilbur/.claude/


## .claude/worktrees/interesting-wilbur/docs/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant.Tests/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Behaviors/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Controls/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Converters/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/Repositories/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Models/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Properties/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Services/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/GridView/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/Management/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/GridView/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/Management/


## .github/workflows/

- `publish-macos.yml` — CI: Publish macOS (~452 tok)

## .vs/SchedulingAssistant/DesignTimeBuild/


## .vs/SchedulingAssistant/config/


## .vs/SchedulingAssistant/v17/


## .vs/SchedulingAssistant/v17/TestStore/0/


## docs/


## src/SchedulingAssistant.Tests/

- `CheckoutServiceTests.cs` — Integration tests for <see cref="CheckoutService"/>. <para>Each test uses an isolated temporary dire (~18366 tok)
- `DatabaseValidatorTests.cs` — Unit tests for <see cref="DatabaseValidator"/>. <para>Each test operates on a fresh temporary direct (~2103 tok)
- `WriteLockServiceTests.cs` — Unit tests for <see cref="WriteLockService"/>. <para>Each test fixture creates an isolated temporary (~6028 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3644 tok)
- `MainWindow.axaml.cs` — Executes a save command if it exists and can currently execute. Returns true if the command was exec (~14702 tok)
- `SchedulingAssistant.csproj` (~804 tok)
- `ViewLocator.cs` — Class: ViewLocator (~370 tok)

## src/SchedulingAssistant/Behaviors/

- `HelpTip.cs` — Attached behavior that provides a styled help tooltip for any <see cref="Control"/>. Set <see cref=" (~1775 tok)

## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~5963 tok)

## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/

- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location t (~2341 tok)
- `CheckoutService.cs` — Manages the checkout / save lifecycle for every database the app opens. <para><b>Write-access mode:< (~18750 tok)
- `DatabaseValidator.cs` — The result of a database file validation check. (~706 tok)
- `NetworkFileOps.cs` — Timeout-aware wrappers for file operations against paths that may be on a network share (D, D.lock, (~2384 tok)
- `PlatformProcess.cs` — Cross-platform helpers for launching URLs, URIs, and executables via the OS default handler. (~585 tok)
- `WriteLockService.cs` — Manages a file-based write lock that prevents two instances of the app from writing to the same SQLi (~7324 tok)

## src/SchedulingAssistant/ViewModels/

- `DatabaseRecoveryViewModel.cs` — Indicates why the database recovery window was shown. (~3654 tok)
- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7696 tok)
- `WorkloadRowViewModel.cs` — Displays the instructor's full name followed by their initials in brackets, e.g. "Jim Bertrand (JB)" (~492 tok)

## src/SchedulingAssistant/ViewModels/GridView/


## src/SchedulingAssistant/ViewModels/Management/

- `CourseHistoryExportViewModel.cs` — Exports the teaching history of a single course as a CSV file. One row per instructor assignment per (~2379 tok)
- `ExportHubViewModel.cs` — ViewModel for the Export flyout hub. Hosts a left-sidebar list of export categories; selecting one d (~425 tok)
- `HelpViewModel.cs` — Represents a single node in the help documentation tree. A node may be a navigable article (when <se (~2562 tok)
- `WorkloadMailerViewModel.cs` — Represents the current UI step of the Workload Mailer flyout. (~5485 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/

- `WorkloadPanelView.axaml` (~2733 tok)

## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~14226 tok)

## src/SchedulingAssistant/Views/Management/

- `CourseHistoryExportView.axaml` (~495 tok)
- `CourseHistoryExportView.axaml.cs` — Class: CourseHistoryExportView (~59 tok)
- `MeetingListView.axaml.cs` — Code-behind for <see cref="MeetingListView"/>. Handles keyboard shortcuts for the inline meeting edi (~1040 tok)
- `SectionListView.axaml.cs` — Responds to property changes on the ViewModel that require the view to take action. Two cases are ha (~2649 tok)
- `WorkloadMailerView.axaml` (~2542 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

