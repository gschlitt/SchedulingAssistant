# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-03-30T16:12:54.103Z
> Files: 62 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/

- `hazy-scribbling-pie.md` — Fix Two Pre-existing Test Failures in CheckoutServiceTests (~707 tok)

## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/

- `MEMORY.md` — SchedulingAssistant Project Memory (~5931 tok)
- `project_network_db_writeback.md` — Plan Summary (~4427 tok)

## ./

- `fix_dynamic_resource.ps1` (~102 tok)

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

- `CheckoutServiceTests.cs` — Integration tests for <see cref="CheckoutService"/>. <para>Each test uses an isolated temporary dire (~13472 tok)
- `ComputeTilesTests.cs` — Unit tests for <see cref="ScheduleGridViewModel.ComputeTiles"/>. ComputeTiles converts a flat list o (~4847 tok)
- `GridPipelineTests.cs` — Unit tests for the internal static pipeline methods extracted from <see cref="ScheduleGridViewModel. (~8235 tok)
- `LegalStartTimeEditViewModelTests.cs` — Unit tests for <see cref="LegalStartTimeEditViewModel"/> start-time validation, specifically the 07: (~972 tok)
- `WizardStepValidationTests.cs` — Unit tests for individual wizard step ViewModels. These tests exercise validation logic, CanAdvance (~5672 tok)
- `WriteLockReadOnlyTests.cs` — Verifies that every write-capable command in every ViewModel refuses execution (<c>CanExecute == fal (~6617 tok)
- `WriteLockServiceTests.cs` — Unit tests for <see cref="WriteLockService"/>. <para>Each test fixture creates an isolated temporary (~5989 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3326 tok)
- `AppColors.axaml` (~2786 tok)
- `Constants.cs` — Application-wide constants for domain rules shared across the codebase. (~45 tok)
- `MainWindow.axaml` — Declares applied (~10787 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. Cancels (~11437 tok)

## src/SchedulingAssistant/Behaviors/

- `HelpTip.cs` — Attached behavior that provides a styled help tooltip for any <see cref="Control"/>. Set <see cref=" (~1783 tok)

## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~3428 tok)

## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/

- `LockFileData.cs` — Data stored in the <c>.lock</c> file that sits alongside the database. Serialized as JSON. Any insta (~402 tok)
- `TpConfigData.cs` — Portable configuration file (.tpconfig) written to the database folder after first-run setup. Contai (~715 tok)

## src/SchedulingAssistant/Services/

- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location t (~2151 tok)
- `BackupService.cs` — Manages automated SQLite backups and companion section CSV exports. <para><b>Backup file naming:</b> (~8277 tok)
- `CheckoutService.cs` — Manages the checkout / save lifecycle for every database the app opens. <para><b>Core concept:</b> W (~8730 tok)
- `FileAppLogger.cs` — Writes log entries to a rolling daily log file under %AppData%\SchedulingAssistant\Logs\app-YYYY-MM- (~1334 tok)
- `IAppLogger.cs` — Application-wide error logger. Implementations can write to a local file, a remote database, a cloud (~492 tok)
- `WriteLockService.cs` — Manages a file-based write lock that prevents two instances of the app from writing to the same SQLi (~5378 tok)

## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~6361 tok)

## src/SchedulingAssistant/ViewModels/GridView/

- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Mond (~2440 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~16583 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `BlockPatternEditViewModel.cs` — Class: BlockPatternEditViewModel (~615 tok)
- `BlockPatternListViewModel.cs` — Manages up to five block-pattern favourite slots shown in the Block Patterns flyout. Patterns are st (~1561 tok)
- `CommitmentEditViewModel.cs` — Class: CommitmentEditViewModel (~1542 tok)
- `LegalStartTimeEditViewModel.cs` — Class: LegalStartTimeEditViewModel (~1115 tok)
- `LegalStartTimeListViewModel.cs` — Represents one item in the "Preferred block length" ComboBox. (~2399 tok)
- `NewDatabaseViewModel.cs` — ViewModel for the File → New flyout. Collects the new database name, location, and backup folder fro (~4719 tok)
- `SchedulingEnvironmentListViewModel.cs` — True when this instance holds the write lock; gates all write-capable buttons. (~2858 tok)
- `SchedulingEnvironmentViewModel.cs` — Class: SchedulingEnvironmentViewModel (~877 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cr (~10025 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewMod (~12069 tok)
- `SectionMeetingViewModel.cs` — Represents a single scheduled meeting within a section — day, time, room, meeting type, and frequenc (~7237 tok)
- `SettingsViewModel.cs` — ViewModel for the Settings flyout. Manages automated-backup configuration and restore. Backup entrie (~2398 tok)
- `ShareViewModel.cs` — ViewModel for the File → Share flyout. Generates a .tpconfig file from the current database so the u (~1951 tok)
- `WorkloadMailerViewModel.cs` — Represents the current UI step of the Workload Mailer flyout. (~5460 tok)

## src/SchedulingAssistant/ViewModels/Wizard/

- `StartupWizardViewModel.cs` — Orchestrates the multi-step startup wizard. Step index map: 0 — Welcome 1 — Existing-DB check (Step1 (~5706 tok)

## src/SchedulingAssistant/ViewModels/Wizard/Steps/

- `Step5LegalStartTimesViewModel.cs` — An editable start time entry within a block length row. Stored in HHMM military format (e.g. "0800" (~2566 tok)

## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~11447 tok)

## src/SchedulingAssistant/Views/Management/

- `AcademicUnitListView.axaml` (~508 tok)
- `AcademicYearListView.axaml` (~1265 tok)
- `BlockPatternListView.axaml` (~2146 tok)
- `CampusListView.axaml` (~1150 tok)
- `CourseListView.axaml` (~4551 tok)
- `InstructorListView.axaml` (~8570 tok)
- `LegalStartTimeListView.axaml` (~1870 tok)
- `NewDatabaseView.axaml` (~1971 tok)
- `RoomListView.axaml` (~1537 tok)
- `SchedulingEnvironmentListView.axaml` — Declares description (~1429 tok)
- `SectionListView.axaml` (~19617 tok)
- `SectionPrefixListView.axaml` (~1431 tok)
- `SemesterManagerView.axaml` (~971 tok)
- `SettingsView.axaml` (~2083 tok)
- `SubjectListView.axaml` (~888 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step5LegalStartTimesView.axaml` — Declares durations (~2328 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

