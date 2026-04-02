# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-02T16:34:45.015Z
> Files: 18 tracked | Anatomy hits: 0 | Misses: 0

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

- `EditorFlowTests.cs` — Integration-style unit tests for the inline editor workflows. Each test constructs the ViewModel und (~4689 tok)
- `WizardStepValidationTests.cs` — Unit tests for individual wizard step ViewModels. These tests exercise validation logic, CanAdvance (~5713 tok)

## src/SchedulingAssistant/

- `App.axaml` (~1128 tok)
- `AppColors.axaml` (~2823 tok)
- `AppLayout.axaml` (~235 tok)
- `MainWindow.axaml` — Declares applied (~11871 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. Cancels (~11704 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/


## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/


## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7332 tok)

## src/SchedulingAssistant/ViewModels/GridView/

- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Mond (~2999 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~17915 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `IMeetingListEntry.cs` — Marker interface for items that can appear in the Meeting List panel. Implemented by both event card (~133 tok)
- `MeetingEditViewModel.cs` — Inline editor for a <see cref="Meeting"/>. No step-gate is required — the Title field is the only pr (~3358 tok)
- `MeetingListItemViewModel.cs` — Display wrapper for a single event row in the meeting list panel. Holds pre-formatted strings so the (~1106 tok)
- `MeetingListViewModel.cs` — Drives the Event List left panel — the counterpart to <see cref="SectionListViewModel"/> when the us (~4822 tok)
- `SemesterBannerViewModel.cs` — Represents a semester group header row in the Section List. Carries the semester identity and displa (~916 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~9355 tok)
- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~11715 tok)

## src/SchedulingAssistant/Views/Management/

- `MeetingListView.axaml` (~8582 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

