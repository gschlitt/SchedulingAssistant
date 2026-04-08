# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-07T22:05:00.517Z
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

- `fancy-wiggling-newell.md` — Plan: Apply-Save Flash Animation on Schedule Grid (~2500 tok)
- `whimsical-baking-haven.md` — Plan: Block Length Unit Toggle (Hours ↔ Minutes) (~2795 tok)

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

- `AppColors.axaml` (~2890 tok)
- `MainWindow.axaml` — Declares applied (~11874 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. Cancels (~11760 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/


## src/SchedulingAssistant/Data/Repositories/


## src/SchedulingAssistant/Models/

- `BlockLengthUnit.cs` — Controls how block lengths are displayed and entered throughout the application. The underlying stor (~81 tok)

## src/SchedulingAssistant/Services/

- `AcademicUnitService.cs` — Service for accessing the single Academic Unit in the system. There is always exactly one Academic U (~368 tok)
- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location t (~2238 tok)
- `BackupService.cs` — Manages automated SQLite backups and companion section CSV exports. <para><b>Backup file naming:</b> (~8906 tok)
- `BlockLengthFormatter.cs` — Stateless helpers for formatting and parsing block lengths under the current <see cref="BlockLengthU (~1596 tok)
- `CheckoutService.cs` — Manages the checkout / save lifecycle for every database the app opens. <para><b>Write-access mode:< (~12543 tok)
- `SectionStore.cs` — Singleton service that holds the in-memory cache of sections for the currently selected semester(s) (~2240 tok)

## src/SchedulingAssistant/ViewModels/


## src/SchedulingAssistant/ViewModels/GridView/

- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~18256 tok)

## src/SchedulingAssistant/ViewModels/Management/

- `LegalStartTimeEditViewModel.cs` — Block length in hours — internal storage unit. Never exposed directly to the UI. (~1677 tok)
- `LegalStartTimeListViewModel.cs` — Represents one item in the "Preferred block length" ComboBox. (~3296 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cr (~10072 tok)
- `SectionListItemViewModel.cs` — Display wrapper for a section row in the sections list panel. Holds formatted strings so the view ne (~3281 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewMod (~12094 tok)
- `SectionMeetingViewModel.cs` — Represents a single scheduled meeting within a section — day, time, room, meeting type, and frequenc (~6855 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/

- `Step5LegalStartTimesViewModel.cs` — An editable start time entry within a block length row. Stored in HHMM military format (e.g. "0800" (~3180 tok)

## src/SchedulingAssistant/Views/

- `WorkloadPanelView.axaml` (~3406 tok)

## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~13804 tok)

## src/SchedulingAssistant/Views/Management/

- `InstructorListView.axaml` (~8034 tok)
- `LegalStartTimeListView.axaml` (~2613 tok)
- `SectionListView.axaml` (~19529 tok)

## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step5LegalStartTimesView.axaml` — Declares durations (~2478 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

