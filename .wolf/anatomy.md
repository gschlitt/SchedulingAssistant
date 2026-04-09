# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-09T03:41:44.840Z
> Files: 48 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/

- `jiggly-sprouting-trinket.md` — Plan: Fix Chip Column Misalignment in Workload View (~400 tok)
- `linked-watching-dove.md` — Rename SettingsView → SaveAndBackupView (~643 tok)
- `parsed-jingling-gadget.md` — Plan: Section Prefix Opt-Out (~1864 tok)
- `polished-plotting-chipmunk.md` — Plan: Insert License Page as Wizard Step 1 (~1396 tok)
- `prancy-launching-cosmos.md` — Plan: Attendee Preset Sentinels in Meeting Editor (~1887 tok)
- `sequential-petting-bengio.md` — Plan: Derive and display section prefix at editor load time (~1002 tok)
- `soft-popping-petal.md` — Plan: Human-Readable Columns for LegalStartTimes, Sections, Semesters (~1340 tok)

## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/

- `feedback_new_table_readability.md` (~254 tok)
- `MEMORY.md` — SchedulingAssistant Project Memory (~6017 tok)

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

- `LegalStartTimeEditViewModelTests.cs` — Unit tests for <see cref="LegalStartTimeEditViewModel"/> start-time validation, specifically the 07: (~992 tok)
- `WizardStepValidationTests.cs` — Unit tests for individual wizard step ViewModels. These tests exercise validation logic, CanAdvance (~5905 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3431 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~4492 tok)

## src/SchedulingAssistant/Data/Repositories/

- `AppConfigurationRepository.cs` — SQLite-backed implementation of <see cref="IAppConfigurationRepository"/>. Reads and writes rows in (~327 tok)
- `IAppConfigurationRepository.cs` — Data access contract for the <c>AppConfiguration</c> key-value table. Stores DB-level settings that (~176 tok)
- `LegalStartTimeRepository.cs` — Copies all legal start times from a previous academic year to a new one. If fromAcademicYearId is nu (~1020 tok)
- `SectionRepository.cs` — Returns all sections for the given course across all semesters, ordered by section code. (~1997 tok)
- `SemesterRepository.cs` — Serializes the extra JSON data for a semester (currently just Color). The key structural fields (id, (~1260 tok)

## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/

- `AppConfigurationService.cs` — Singleton service that caches DB-persisted application configuration values. Settings stored here tr (~718 tok)
- `SectionPrefixHelper.cs` — Static utility methods for matching and advancing section codes based on the configured list of <see (~2600 tok)

## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7337 tok)

## src/SchedulingAssistant/ViewModels/GridView/

- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Mond (~3141 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~18186 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `AttendeeSentinelViewModel.cs` — Discriminates the kind of bulk-selection preset a sentinel represents. (~592 tok)
- `CampusListViewModel.cs` — ViewModel for the Campuses settings panel. Supports full CRUD and manual ordering. (~1897 tok)
- `MeetingEditViewModel.cs` — Inline editor for a <see cref="Meeting"/>. No step-gate is required — the Title field is the only pr (~4468 tok)
- `MeetingListItemViewModel.cs` — Display wrapper for a single event row in the meeting list panel. Holds pre-formatted strings so the (~1354 tok)
- `MeetingListViewModel.cs` — Drives the Event List left panel — the counterpart to <see cref="SectionListViewModel"/> when the us (~4985 tok)
- `SaveAndBackupViewModel.cs` — ViewModel for the Save &amp; Backup flyout. Manages automated-backup configuration and restore. Back (~2403 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cr (~10661 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewMod (~12230 tok)
- `SectionPrefixListViewModel.cs` — ViewModel for the Section Prefixes management flyout. Provides a list of section prefixes with inlin (~2261 tok)

## src/SchedulingAssistant/ViewModels/Wizard/

- `StartupWizardViewModel.cs` — Orchestrates the multi-step startup wizard. Step index map: 0 — Welcome 1 — License Agreement ← new (~6039 tok)

## src/SchedulingAssistant/ViewModels/Wizard/Steps/

- `Step2DatabaseViewModel.cs` — Step 2 — choose the database folder, confirm/edit the database filename, and choose the backup folde (~1657 tok)
- `Step5SchedulingViewModel.cs` — An editable start time entry within a block length row. Stored in HHMM military format (e.g. "0800" (~3177 tok)
- `StepLicenseViewModel.cs` — Wizard step 1 — License Agreement. Read-only; no user input required. The user clicks Next to accept (~88 tok)

## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~12098 tok)

## src/SchedulingAssistant/Views/Management/

- `CampusListView.axaml` (~1009 tok)
- `MeetingListView.axaml` (~9914 tok)
- `SaveAndBackupView.axaml` (~2360 tok)
- `SaveAndBackupView.axaml.cs` — Code-behind for <see cref="SaveAndBackupView"/>. Kept minimal — only the folder-picker button handle (~616 tok)
- `SectionListView.axaml` (~19548 tok)
- `SectionPrefixListView.axaml` (~5625 tok)

## src/SchedulingAssistant/Views/Wizard/

- `StartupWizardWindow.axaml` (~1284 tok)

## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step5SchedulingView.axaml` — Declares durations (~2476 tok)
- `Step5SchedulingView.axaml.cs` — Class: Step5SchedulingView (~55 tok)
- `StepLicenseView.axaml` (~384 tok)
- `StepLicenseView.axaml.cs` — Class: StepLicenseView (~50 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

