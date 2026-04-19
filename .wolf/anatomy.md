# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-19T20:28:53.636Z
> Files: 26 tracked | Anatomy hits: 0 | Misses: 0

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

- `App.axaml` (~1588 tok)
- `AppColors.axaml` (~2983 tok)
- `MainWindow.axaml` — Declares applied (~6752 tok)
- `MainWindow.axaml.cs` — Executes a save command if it exists and can currently execute. Returns true if the command was exec (~13358 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/

- `DetachablePanel.axaml` (~683 tok)

## src/SchedulingAssistant/Converters/

- `HexToColorConverter.cs` — Two-way converter between a <c>#RRGGBB</c> hex string (on the ViewModel) and an Avalonia <see cref=" (~352 tok)
- `SemesterBackgroundBrushConverter.cs` — Converts a semester name + optional hex color into the background brush resolved from AppColors' <c> (~369 tok)
- `SemesterBorderBrushConverter.cs` — Converts a semester name + optional hex color into the border brush resolved from AppColors' <c>*Bor (~352 tok)

## src/SchedulingAssistant/Data/


## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/

- `SemesterBrushResolver.cs` — View-layer helper that resolves a semester's display <see cref="IBrush"/> from its stored hex color  (~1082 tok)

## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7416 tok)
- `WorkloadPanelViewModel.cs` — Fired when the user clicks a work item chip. (~3010 tok)
- `WorkloadSemesterGroupViewModel.cs` — Represents one semester's workload items (sections and releases) within a <see cref="WorkloadRowView (~324 tok)

## src/SchedulingAssistant/ViewModels/GridView/

- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Mond (~3265 tok)
- `ScheduleGridViewModel.cs` — Represents one semester-line pill. Carries the semester's name and stored hex color only; the view r (~17540 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `SemesterBannerViewModel.cs` — Represents a semester group header row in the Section List and Meeting List. Carries only the semest (~417 tok)
- `SemesterManagerViewModel.cs` — Display wrapper for a single <see cref="Semester"/> row in the semester manager list. Exposes only < (~3446 tok)
- `SemesterPromptItem.cs` — Represents one semester option in the Add section to which semester? inline prompt shown when the us (~366 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/

- `Step6SemesterColorsViewModel.cs` — Editable color row for one semester in the Semester Colors step. Exposes only <see cref="HexColor"/> (~1019 tok)

## src/SchedulingAssistant/Views/

- `WorkloadPanelView.axaml` (~4333 tok)

## src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~6572 tok)
- `ScheduleGridView.axaml` (~2482 tok)
- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~13483 tok)

## src/SchedulingAssistant/Views/Management/

- `MeetingListView.axaml` (~10750 tok)
- `SectionListView.axaml` (~20719 tok)
- `SemesterManagerView.axaml` (~1568 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step6SemesterColorsView.axaml` (~964 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

