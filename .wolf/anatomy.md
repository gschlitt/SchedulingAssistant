# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-01T21:48:55.240Z
> Files: 33 tracked | Anatomy hits: 0 | Misses: 0

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

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3390 tok)
- `MainWindow.axaml` — Declares applied (~11854 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~4060 tok)

## src/SchedulingAssistant/Data/Repositories/

- `IMeetingRepository.cs` — Data access contract for <see cref="Meeting"/> entities. (~321 tok)
- `MeetingRepository.cs` — SQLite-backed implementation of <see cref="IMeetingRepository"/>. The <c>Meetings</c> table uses the (~1161 tok)

## src/SchedulingAssistant/Models/

- `Meeting.cs` — A faculty, committee, or other recurring meeting that appears on the weekly schedule grid alongside (~269 tok)
- `SchedulableBase.cs` — Abstract base for any entity that can be placed on the weekly schedule grid. Holds the fields that < (~645 tok)
- `Section.cs` — The core scheduling entity — a course section offered in a specific semester. Inherits all common sc (~408 tok)

## src/SchedulingAssistant/Services/

- `MeetingStore.cs` — Singleton service that holds the in-memory cache of meetings for the currently selected semester(s). (~885 tok)

## src/SchedulingAssistant/ViewModels/

- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~7334 tok)

## src/SchedulingAssistant/ViewModels/GridView/

- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Mond (~2941 tok)
- `GridFilterViewModel.cs` — Holds all filter state for the Schedule Grid. Option lists are rebuilt by PopulateOptions() on each (~6550 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~17709 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `MeetingEditViewModel.cs` — Inline editor for a <see cref="Meeting"/>. No step-gate is required — the Title field is the only pr (~2351 tok)
- `MeetingListItemViewModel.cs` — Display wrapper for a single meeting row in the meeting list panel. Holds pre-formatted strings so t (~726 tok)
- `MeetingListViewModel.cs` — Drives the Meeting List left panel — the counterpart to <see cref="SectionListViewModel"/> when the (~2258 tok)
- `SemesterManagerViewModel.cs` — Display wrapper for a single <see cref="Semester"/> row in the semester manager list. Exposes <see c (~3588 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~9188 tok)

## src/SchedulingAssistant/Views/Management/

- `MeetingListView.axaml` (~4905 tok)
- `MeetingListView.axaml.cs` — Code-behind for <see cref="MeetingListView"/>. No logic required here; the view is fully declarative (~113 tok)
- `SemesterManagerView.axaml` (~1529 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step0WelcomeView.axaml` (~550 tok)
- `Step10ClosingView.axaml` (~487 tok)
- `Step1aExistingDbView.axaml` (~1035 tok)
- `Step1InstitutionView.axaml` (~647 tok)
- `Step2DatabaseView.axaml` (~1456 tok)
- `Step3TpConfigView.axaml` (~992 tok)
- `Step4ManualConfigView.axaml` (~320 tok)
- `Step5AcademicYearView.axaml` (~1266 tok)
- `Step5LegalStartTimesView.axaml` — Declares durations (~2327 tok)
- `Step6BlockPatternsView.axaml` — Declares typically (~373 tok)
- `Step6SemesterColorsView.axaml` (~941 tok)
- `Step7SectionPrefixesView.axaml` (~3636 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

