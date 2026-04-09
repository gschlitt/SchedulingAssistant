# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-04-09T22:06:29.508Z
> Files: 37 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/

- `peaceful-napping-whistle.md` — Plan: Accurate Dirty Marker — Write on First Edit, Not on Checkout (~2323 tok)
- `proud-noodling-beacon.md` — Plan: Split Tile Border Color into TileExternalBorder / TileInternalBorder (~524 tok)
- `unified-crunching-sonnet.md` — Plan: Show only prefix in prefix picker (not prefix + campus) (~281 tok)

## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/

- `feedback_build_vs_lock.md` (~207 tok)
- `MEMORY.md` — SchedulingAssistant Project Memory (~6053 tok)

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

- `CheckoutServiceTests.cs` — Integration tests for <see cref="CheckoutService"/>. <para>Each test uses an isolated temporary dire (~18353 tok)
- `EditorFlowTests.cs` — Integration-style unit tests for the inline editor workflows. Each test constructs the ViewModel und (~5114 tok)
- `WizardDataFlowTests.cs` — End-to-end data-flow tests for <see cref="StartupWizardViewModel"/>. These tests drive the wizard th (~7043 tok)
- `WizardManualPathTests.cs` — Tests for the manual setup path through the startup wizard, focusing on data flowing correctly betwe (~2434 tok)
- `WizardRoutingTests.cs` — Tests for <see cref="StartupWizardViewModel"/> navigation, routing logic, step caching, and button-l (~3635 tok)
- `WizardStepValidationTests.cs` — Unit tests for individual wizard step ViewModels. These tests exercise validation logic, CanAdvance (~5948 tok)
- `WriteLockReadOnlyTests.cs` — Verifies that every write-capable command in every ViewModel refuses execution (<c>CanExecute == fal (~6678 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices (~3508 tok)
- `AppColors.axaml` (~2975 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. Cancels (~11701 tok)

## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~4771 tok)
- `IDatabaseContext.cs` — Abstraction over the application database connection. The SQLite desktop implementation opens a file (~374 tok)

## src/SchedulingAssistant/Data/Repositories/

- `AcademicUnitRepository.cs` — Returns true if an academic unit with this name already exists (case-insensitive). Pass excludeId to (~812 tok)
- `AcademicYearRepository.cs` — Class: AcademicYearRepository (~712 tok)
- `AppConfigurationRepository.cs` — SQLite-backed implementation of <see cref="IAppConfigurationRepository"/>. Reads and writes rows in (~343 tok)
- `BlockPatternRepository.cs` — Class: BlockPatternRepository (~660 tok)
- `CampusRepository.cs` — SQLite-backed implementation of <see cref="ICampusRepository"/>. Uses the project's standard pattern (~906 tok)
- `CourseRepository.cs` — Returns true if any sections reference this course. (~1290 tok)
- `InstructorCommitmentRepository.cs` — Class: InstructorCommitmentRepository (~1031 tok)
- `InstructorRepository.cs` — Returns all instructors, ordered according to the persisted <see cref="AppSettings.InstructorSortMod (~1589 tok)
- `LegalStartTimeRepository.cs` — Copies all legal start times from a previous academic year to a new one. If fromAcademicYearId is nu (~1047 tok)
- `MeetingRepository.cs` — SQLite-backed implementation of <see cref="IMeetingRepository"/>. The <c>Meetings</c> table uses the (~1219 tok)
- `ReleaseRepository.cs` — Class: ReleaseRepository (~864 tok)
- `RoomRepository.cs` — Returns all rooms ordered by <see cref="Room.SortOrder"/> ascending, then by building and room numbe (~792 tok)
- `SchedulingEnvironmentRepository.cs` — Returns all values of the given type, ordered by <see cref="SchedulingEnvironmentValue.SortOrder"/> (~1073 tok)
- `SectionPrefixRepository.cs` — CRUD repository for <see cref="SectionPrefix"/> records stored in the <c>SectionPrefixes</c> table. (~1071 tok)
- `SectionRepository.cs` — Returns all sections for the given course across all semesters, ordered by section code. (~2023 tok)
- `SemesterRepository.cs` — Serializes the extra JSON data for a semester (currently just Color). The key structural fields (id, (~1287 tok)
- `SubjectRepository.cs` — Returns true if any courses belong to this subject. (~1187 tok)

## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/

- `CheckoutService.cs` — Manages the checkout / save lifecycle for every database the app opens. <para><b>Write-access mode:< (~12854 tok)

## src/SchedulingAssistant/ViewModels/


## src/SchedulingAssistant/ViewModels/GridView/


## src/SchedulingAssistant/ViewModels/Management/

- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cr (~10555 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/

- `ScheduleGridView.axaml.cs` — Snapshot of every entry row rendered during the last full <see cref="Render"/> call. Used by <see cr (~12146 tok)

## src/SchedulingAssistant/Views/Management/


## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

