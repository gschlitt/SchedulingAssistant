# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-20T19:43:38.228Z
> Files: 29 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/


## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/


## ./


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


## .vs/SchedulingAssistant/DesignTimeBuild/


## .vs/SchedulingAssistant/config/


## .vs/SchedulingAssistant/v17/


## .vs/SchedulingAssistant/v17/TestStore/0/


## docs/


## src/SchedulingAssistant.Tests/


## src/SchedulingAssistant/

- `App.axaml` (~1587 tok)
- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3588 tok)
- `AppColors.axaml` (~3022 tok)
- `AppLayout.axaml` (~337 tok)
- `MainWindow.axaml` — Declares applied (~6583 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/

- `BoolToItalicConverter.cs` — Converts a boolean to a <see cref="FontStyle"/>: <c>true</c> → <see cref="FontStyle.Italic"/>; <c>fa (~234 tok)

## src/SchedulingAssistant/Data/


## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/


## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7220 tok)

## src/SchedulingAssistant/ViewModels/GridView/


## src/SchedulingAssistant/ViewModels/Management/

- `AcademicUnitListViewModel.cs` — ViewModel for editing the single Academic Unit in the system. There is always exactly one unit; this (~676 tok)
- `AcademicYearListViewModel.cs` — True when this instance holds the write lock; gates all write-capable buttons. (~2414 tok)
- `BlockPatternListViewModel.cs` — Manages up to five block-pattern favourite slots shown in the Block Patterns flyout. Patterns are st (~1675 tok)
- `ConfigurationViewModel.cs` — ViewModel for the Configuration flyout hub. Hosts a left-sidebar list of configuration categories; s (~559 tok)
- `ExportHubViewModel.cs` — ViewModel for the Export flyout hub. Hosts a left-sidebar list of export categories; selecting one d (~376 tok)
- `ExportViewModel.cs` — Category label shown in the Export flyout sidebar. (~677 tok)
- `LegalStartTimeListViewModel.cs` — Represents one item in the "Preferred block length" ComboBox. (~3414 tok)
- `SaveAndBackupViewModel.cs` — ViewModel for the Save &amp; Backup flyout. Manages automated-backup configuration and restore. Back (~2440 tok)
- `SectionPrefixListViewModel.cs` — ViewModel for the Section Prefixes management flyout. Provides a list of section prefixes with inlin (~2355 tok)
- `WorkloadMailerViewModel.cs` — Represents the current UI step of the Workload Mailer flyout. (~5496 tok)
- `WorkloadReportViewModel.cs` — Category label shown in the Export flyout sidebar. (~2388 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/

- `DatabaseRecoveryWindow.axaml` (~4056 tok)

## src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~6644 tok)
- `GridFilterView.axaml.cs` — Updates a filter dimension's header ToggleButton to show how many items are selected, with active co (~1922 tok)
- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~14112 tok)

## src/SchedulingAssistant/Views/Management/

- `ConfigurationView.axaml` (~384 tok)
- `ConfigurationView.axaml.cs` — Class: ConfigurationView (~55 tok)
- `ExportHubView.axaml` (~382 tok)
- `ExportHubView.axaml.cs` — Class: ExportHubView (~53 tok)
- `InstructorListView.axaml` (~8294 tok)
- `SectionListView.axaml` (~20781 tok)
- `WorkloadHistoryView.axaml` (~781 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

