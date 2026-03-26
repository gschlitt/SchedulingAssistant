# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-03-26T17:33:49.264Z
> Files: 27 tracked | Anatomy hits: 0 | Misses: 0

## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/

- `MEMORY.md` — SchedulingAssistant Project Memory (~5120 tok)
- `startup_db_flow_decisions.md` — (1) .tpconfig — colleague sharing only (~830 tok)

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

- `DatabaseValidatorTests.cs` — Unit tests for <see cref="DatabaseValidator"/>. <para>Each test operates on a fresh temporary direct (~2054 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3042 tok)
- `AppColors.axaml` (~2686 tok)
- `MainWindow.axaml` — Declares applied (~10071 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. All shut (~8711 tok)

## src/SchedulingAssistant/Behaviors/

- `OpenDropDownOnFocusBehavior.cs` — Attached behavior for <see cref="AutoCompleteBox"/> that provides two UX improvements needed when th (~1449 tok)

## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/


## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/

- `TpConfigData.cs` — Portable configuration file (.tpconfig) written to the database folder after first-run setup. Contai (~643 tok)

## src/SchedulingAssistant/Services/

- `DatabaseValidator.cs` — The result of a database file validation check. (~501 tok)

## src/SchedulingAssistant/ViewModels/

- `DatabaseRecoveryViewModel.cs` — Indicates why the database recovery window was shown. (~3544 tok)
- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~5480 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `NewDatabaseViewModel.cs` — ViewModel for the File → New flyout. Collects the new database name, location, and backup folder fro (~3873 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cr (~9810 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewMod (~12008 tok)
- `SectionMeetingViewModel.cs` — Represents a single scheduled meeting within a section — day, time, room, meeting type, and frequenc (~6447 tok)
- `ShareViewModel.cs` — ViewModel for the File → Share flyout. Generates a .tpconfig file from the current database so the u (~1908 tok)

## src/SchedulingAssistant/ViewModels/Wizard/

- `StartupWizardViewModel.cs` — Orchestrates the multi-step startup wizard. Step index map: 0 — Welcome 1 — Existing-DB check (Step1 (~5547 tok)

## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/

- `DatabaseRecoveryWindow.axaml` (~4091 tok)
- `DatabaseRecoveryWindow.axaml.cs` — Shown at startup when the configured database is missing or corrupt. Presents three options: browse (~1000 tok)

## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml` (~3288 tok)
- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~11408 tok)

## src/SchedulingAssistant/Views/Management/

- `NewDatabaseView.axaml` (~1762 tok)
- `NewDatabaseView.axaml.cs` — Code-behind for <see cref="NewDatabaseView"/>. (~83 tok)
- `SectionListView.axaml` (~19363 tok)
- `ShareView.axaml` (~614 tok)
- `ShareView.axaml.cs` — Code-behind for <see cref="ShareView"/>. (~78 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

