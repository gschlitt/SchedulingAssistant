# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-03-24T17:29:19.895Z
> Files: 517 tracked | Anatomy hits: 0 | Misses: 0

## ./

- `.gitignore` — Git ignore rules (~95 tok)
- `AUDIT.md` — Critical Audit: SchedulingAssistant (~1600 tok)
- `CLAUDE.md` — OpenWolf (~1755 tok)
- `CodeComments.txt` (~480 tok)
- `migration.txt` (~183 tok)
- `MULTI_USER_STRATEGY.md` — Multi-User Database Access Strategy (~724 tok)
- `nul` (~0 tok)
- `SchedulingAssistant.sln` (~512 tok)
- `TEST_SCENARIO.md` — Test Scenario: Recent Audit Fixes (~3040 tok)

## .claude/

- `settings.json` (~441 tok)
- `settings.local.json` — /*.cs)", (~2290 tok)

## .claude/rules/

- `openwolf.md` (~313 tok)

## .claude/worktrees/dazzling-bardeen/

- `.gitignore` — Git ignore rules (~95 tok)
- `AUDIT.md` — Critical Audit: SchedulingAssistant (~1630 tok)
- `CLAUDE.md` — Coding Practice (~1779 tok)
- `CodeComments.txt` — Declares in (~513 tok)
- `MULTI_USER_STRATEGY.md` — Multi-User Database Access Strategy (~724 tok)
- `SchedulingAssistant.sln` (~348 tok)
- `TEST_SCENARIO.md` — Test Scenario: Recent Audit Fixes (~3124 tok)

## .claude/worktrees/dazzling-bardeen/.claude/

- `settings.local.json` — /*.cs)", (~1831 tok)

## .claude/worktrees/dazzling-bardeen/docs/

- `manual.html` — Scheduling Assistant — User Manual (~11033 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/

- `App.axaml` (~1025 tok)
- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices so it can be used during startup error handling. (~2092 tok)
- `app.manifest` (~250 tok)
- `AppColors.axaml` (~1531 tok)
- `MainWindow.axaml` (~8061 tok)
- `MainWindow.axaml.cs` — Switch to a different database without restarting the app. Call this from the Files menu to open a different database. The database file will be cr... (~5766 tok)
- `Program.cs` — Application entry point (~429 tok)
- `SchedulingAssistant.csproj` (~463 tok)
- `ViewLocator.cs` — ViewLocator: Match (~374 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Behaviors/

- `CollectionItemPropertyWatcher.cs` — Attached behavior that monitors an ObservableCollection of INotifyPropertyChanged items and executes a command whenever any item's specified proper... (~2100 tok)
- `ConditionalColumnWidthBehavior.cs` — Attached behavior for Grid controls that toggles a column's width between two values based on a boolean condition. When the condition is true, the ... (~1463 tok)
- `DismissBehaviors.cs` — Attached behavior that executes a command when the Escape key is pressed on a control. Commonly used on a Window or top-level container to dismiss ... (~1140 tok)
- `DoubleTapCommandBehavior.cs` — Attached behavior that executes a command when a control is double-tapped. Commonly used on ListBox to trigger an edit action when an item is doubl... (~696 tok)
- `LostFocusForwardBehavior.cs` — Attached behavior that listens for the bubbling LostFocus event from any descendant whose Name matches a specified target, and executes a command w... (~1090 tok)
- `RightClickCommandBehavior.cs` — Attached behavior that executes a command when the user right-clicks on a control. The command parameter is the PointerPressedEventArgs, which allo... (~623 tok)
- `SelectionCommandBehavior.cs` — Attached behavior for ListBox controls that converts a single-select action into a command invocation. When an item is selected, the behavior: 1. E... (~1151 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Controls/

- `DetachablePanel.axaml` (~788 tok)
- `DetachablePanel.axaml.cs` — Optional content placed inline to the right of the Header text (left-justified). Used for contextual info like semester name and stats. (~857 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Converters/

- `MinutesToTimeConverter.cs` — MinutesToTimeConverter: Convert, ConvertBack (~180 tok)
- `MultiSemesterLeftBorderThicknessConverter.cs` — Converts a boolean (is multi-semester mode) to a BorderThickness. When true, returns 10,0,0,0 (10pt left border for semester indicator). When false... (~253 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — Handles schema migrations for existing databases. (~3400 tok)
- `JsonHelpers.cs` — Class: JsonHelpers (~123 tok)
- `SeedData.cs` — Called by DatabaseContext after schema initialization. Ensures baseline records exist (Academic Unit, default Section Prefixes). (~2233 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/Repositories/

- `AcademicUnitRepository.cs` — Returns true if an academic unit with this name already exists (case-insensitive). Pass excludeId to ignore the unit currently being edited. (~838 tok)
- `AcademicYearRepository.cs` — AcademicYearRepository: GetAll, ExistsByName, Insert, Update + 1 more (~734 tok)
- `BlockPatternRepository.cs` — BlockPatternRepository: GetAll, Insert, Update, Delete (~664 tok)
- `CourseRepository.cs` — Returns true if any sections reference this course. (~1262 tok)
- `InstructorCommitmentRepository.cs` — InstructorCommitmentRepository: GetByInstructor, Insert, Update, Delete (~1053 tok)
- `InstructorRepository.cs` — Returns true if any sections reference this instructor (searches JSON instructorAssignments array). (~1245 tok)
- `LegalStartTimeRepository.cs` — Copies all legal start times from a previous academic year to a new one. If fromAcademicYearId is null, no copy is performed. (~1027 tok)
- `ReleaseRepository.cs` — ReleaseRepository: GetBySemester, GetByInstructor, Insert, Update + 1 more (~894 tok)
- `RoomRepository.cs` — RoomRepository: GetAll, Insert, Update, Delete (~712 tok)
- `SectionPrefixRepository.cs` — CRUD repository for <see cref="SectionPrefix"/> records stored in the <c>SectionPrefixes</c> table. (~1082 tok)
- `SectionPropertyRepository.cs` — Returns true if a value with this name already exists within the given type (case-insensitive). Pass excludeId to skip the record currently being e... (~956 tok)
- `SectionRepository.cs` — Returns all sections for the given course across all semesters, ordered by section code. (~1941 tok)
- `SemesterRepository.cs` — SemesterRepository: GetAll, GetByAcademicYear, Insert, Update + 2 more (~882 tok)
- `SubjectRepository.cs` — Returns true if any courses belong to this subject. (~1257 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Models/

- `AcademicUnit.cs` — Class: AcademicUnit (~52 tok)
- `AcademicYear.cs` — Extracts the start year from a name formatted as "YYYY-YYYY". Returns int.MaxValue if the format is unrecognised (sorts to end). (~132 tok)
- `BlockPattern.cs` — A named favourite day pattern (e.g. "MWF" = days [1, 3, 5]). Stored in the database so all users of the same database see the same patterns. (~165 tok)
- `Course.cs` — Computes the course level from the calendar code. Levels are "0XX", "1XX", "2XX", "3XX", "4XX", or "5+XX" based on the hundreds digit of the course... (~358 tok)
- `Instructor.cs` — Display-only. Resolved at load time from StaffTypeId; not persisted. (~193 tok)
- `InstructorAssignment.cs` — Links an instructor to a section, with an optional workload share. (~106 tok)
- `InstructorCommitment.cs` — Class: InstructorCommitment (~306 tok)
- `LegalStartTime.cs` — Duration in hours, e.g. 1.5, 2.0, 3.0, 4.0. Also the primary key. (~202 tok)
- `RecentDatabaseItem.cs` — Represents a recent database file for display in the Files menu. (~128 tok)
- `Release.cs` — Class: Release (~96 tok)
- `Room.cs` — Class: Room (~107 tok)
- `Section.cs` — Multi-instructor support with workload. Stored in JSON data column. (~336 tok)
- `SectionDaySchedule.cs` — Day of week: 1=Monday, 2=Tuesday, 3=Wednesday, 4=Thursday, 5=Friday. (~216 tok)
- `SectionPrefix.cs` — A section code prefix — the initial symbols used in a typical section code, such as "AB" or "A#". An optional campus association indicates which ca... (~247 tok)
- `SectionPropertyValue.cs` — Optional short code used as a section code prefix/suffix for this campus. Only meaningful for the Campus property type; null for all others. (~123 tok)
- `SectionReserve.cs` — Class: SectionReserve (~46 tok)
- `SectionSortMode.cs` — Class: SectionSortMode (~43 tok)
- `Semester.cs` — The fixed set of semester names auto-created for each Academic Year. (~143 tok)
- `Subject.cs` — Class: Subject (~69 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Services/

- `AcademicUnitService.cs` — Service for accessing the single Academic Unit in the system. There is always exactly one Academic Unit (created at database initialization if miss... (~271 tok)
- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location the app can always find on startup. (~736 tok)
- `DebugTestDataGenerator.cs` — Debug-only utility for generating random test sections with realistic data. (~3044 tok)
- `DialogService.cs` — DialogService: Confirm, ShowError (~248 tok)
- `FileAppLogger.cs` — Writes log entries to a rolling daily log file under %AppData%\SchedulingAssistant\Logs\app-YYYY-MM-DD.log. Designed to be swapped out for a remote... (~1114 tok)
- `IAppLogger.cs` — Application-wide error logger. Implementations can write to a local file, a remote database, a cloud sink, etc. All methods are non-throwing in pro... (~323 tok)
- `IDialogService.cs` — Interface: IDialogService (1 members) (~60 tok)
- `LegalStartTimesDataExporter.cs` — Utility for exporting LegalStartTimes data to JSON for reference and persistence. Captures the block length / start time configuration for each aca... (~1097 tok)
- `LegalStartTimesDataStore.cs` — Manages persistent storage of legal start times configuration. Data is stored in the project directory and travels with the code. (~877 tok)
- `ScheduleValidationService.cs` — Checks sections against an academic year's legal start-time matrix. Reusable across copy-semester, start-time editing, etc. (~499 tok)
- `SectionChangeNotifier.cs` — Provides a notification mechanism when sections are changed externally (e.g., via the schedule grid context menu). Both SectionListViewModel and Sc... (~125 tok)
- `SectionPrefixHelper.cs` — Static utility methods for matching and advancing section codes based on the configured list of <see cref="SectionPrefix"/> entries. This class is ... (~1603 tok)
- `SemesterContext.cs` — Pairs a Semester with a formatted display label. DisplayName uses the full "Year — Semester" form for legacy contexts; for the semester picker UI p... (~3587 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/

- `DebugTestDataViewModel.cs` — When true, exceptions passed to <see cref="IAppLogger.LogError"/> are re-thrown after being written to the log file, so they surface immediately in... (~1335 tok)
- `ErrorViewModel.cs` — Diagnostic ViewModel — the ViewLocator renders its Message property as red text when navigation or resolution fails. (~85 tok)
- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~1908 tok)
- `ViewModelBase.cs` — Class: ViewModelBase (~42 tok)
- `WorkloadItemViewModel.cs` — Class: WorkloadItemViewModel (~140 tok)
- `WorkloadPanelViewModel.cs` — Fired when the user clicks a work item chip. (~2892 tok)
- `WorkloadRowViewModel.cs` — Sections and releases for this instructor in the current semester(s). In single-semester mode, this contains all items for that semester. In multi-... (~426 tok)
- `WorkloadSemesterGroupViewModel.cs` — Represents one semester's workload items (sections and releases) within a <see cref="WorkloadRowViewModel"/> when the view is in multi-semester mod... (~537 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/GridView/

- `ContextMenuItemVm.cs` — Class: ContextMenuItemVm (~122 tok)
- `FilterItemViewModel.cs` — A single checkable item in a filter group (e.g. one instructor, one room). (~299 tok)
- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Monday … 6=Saturday. Times are minutes from midnigh... (~1940 tok)
- `GridFilterViewModel.cs` — Holds all filter state for the Schedule Grid. Option lists are rebuilt by PopulateOptions() on each Reload(). FilterChanged fires whenever any item... (~6119 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~9981 tok)
- `SectionContextMenuViewModel.cs` — SectionContextMenuViewModel: Load (~1420 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/Management/

- `AcademicUnitListViewModel.cs` — ViewModel for editing the single Academic Unit in the system. There is always exactly one unit; this dialog allows editing its name. (~440 tok)
- `AcademicYearEditViewModel.cs` — Class: AcademicYearEditViewModel (~666 tok)
- `AcademicYearListViewModel.cs` — Set by the view. Called when adding a new academic year to ask if the user wants to copy the start-time/block-length setup from the previous year. ... (~1934 tok)
- `BlockPatternEditViewModel.cs` — Class: BlockPatternEditViewModel (~579 tok)
- `BlockPatternListViewModel.cs` — Manages up to five block-pattern favourite slots shown in the Block Patterns flyout. Patterns are stored in the database so all users of the same d... (~1281 tok)
- `CommitmentEditViewModel.cs` — CommitmentEditViewModel: DayOption, TimeOption (~1473 tok)
- `CommitmentsManagementViewModel.cs` — Manages the CRUD list of InstructorCommitment records for one instructor in one semester. This VM is embedded inside InstructorListViewModel and is... (~2018 tok)
- `CopySemesterViewModel.cs` — Class: CopySemesterViewModel (~4132 tok)
- `CourseEditViewModel.cs` — Class: CourseEditViewModel (~1002 tok)
- `CourseHistoryItemViewModel.cs` — Represents a hierarchical item in the course history tree. Three levels: Academic Year (IsYear), Semester (IsSemester), Section (IsSection). (~376 tok)
- `CourseHistoryViewModel.cs` — Loads and displays the historical sections of a course, organized by academic year and semester. Hierarchical structure: Academic Year → Semester →... (~1383 tok)
- `CourseListViewModel.cs` — Handles course selection change: loads course history when a course is selected. (~1966 tok)
- `DayCheckViewModel.cs` — A checkable day row used in the block-pattern editor. (~144 tok)
- `EmptySemesterViewModel.cs` — Set by the view. Called before deletion with (semesterName, sectionCount). Should return true if the user confirms, false to cancel. (~1310 tok)
- `ExportViewModel.cs` — Class: ExportViewModel (~642 tok)
- `InstructorEditViewModel.cs` — Class: InstructorEditViewModel (~1023 tok)
- `InstructorListViewModel.cs` — All semesters in the currently selected academic year, available in the flyout's local semester picker. This list is independent of the global seme... (~3652 tok)
- `InstructorSelectionViewModel.cs` — Checkbox + workload wrapper for instructor multi-select in the section editor. (~378 tok)
- `InstructorWorkloadViewModel.cs` — Represents a single assigned section's workload contribution. (~483 tok)
- `ISectionListEntry.cs` — Marker interface for items that can appear in the Section List panel. Implemented by both section cards (<see cref=SectionListItemViewModel/>) and ... (~135 tok)
- `LegalStartTimeEditViewModel.cs` — Class: LegalStartTimeEditViewModel (~994 tok)
- `LegalStartTimeListViewModel.cs` — Represents one item in the "Preferred block length" ComboBox. (~1690 tok)
- `ReleaseEditViewModel.cs` — Class: ReleaseEditViewModel (~515 tok)
- `ReleaseManagementViewModel.cs` — Manages CRUD for releases for a specific instructor in a specific semester. Fires ReleasesChanged event when releases are modified so parent can re... (~1150 tok)
- `ReserveSelectionViewModel.cs` — Count as a user-editable string. Validated on save to a positive integer. (~253 tok)
- `ResourceSelectionViewModel.cs` — Class: ResourceSelectionViewModel (~126 tok)
- `RoomEditViewModel.cs` — Class: RoomEditViewModel (~404 tok)
- `RoomListViewModel.cs` — Class: RoomListViewModel (~903 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cref="Prefix"/> == null) represents "no prefix se... (~8876 tok)
- `SectionListItemViewModel.cs` — Display wrapper for a section row in the sections list panel. Holds formatted strings so the view needs no converter logic. (~2524 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewModel"/> (group headers) and <see cref="SectionLis... (~10712 tok)
- `SectionMeetingViewModel.cs` — Represents a single scheduled meeting within a section — day, time, room, and meeting type. (~1410 tok)
- `SectionPrefixEditViewModel.cs` — ViewModel for the inline Add/Edit form in the Section Prefixes flyout. Communicates results back to the parent list via callbacks. (~1142 tok)
- `SectionPrefixListViewModel.cs` — ViewModel for the Section Prefixes management flyout. Provides a list of section prefixes with inline Add/Edit/Delete support. (~1354 tok)
- `SectionPropertiesViewModel.cs` — Class: SectionPropertiesViewModel (~575 tok)
- `SectionPropertyEditViewModel.cs` — The section code abbreviation field value. Only relevant when ShowAbbreviation is true. (~653 tok)
- `SectionPropertyListViewModel.cs` — When true, the "Section Code Abbreviation" field is shown in the edit form and as a column in the list. Currently only true for the Campus property... (~1631 tok)
- `SectionPropertyTypes.cs` — Canonical type discriminator strings for the SectionPropertyValues.type column. (~151 tok)
- `SemesterBannerViewModel.cs` — Represents a semester group header row in the Section List. Carries the semester identity and display colors resolved from application resources (S... (~932 tok)
- `SemesterEditViewModel.cs` — Class: SemesterEditViewModel (~268 tok)
- `SemesterListViewModel.cs` — Class: SemesterListViewModel (~675 tok)
- `SemesterPromptItem.cs` — Represents one semester option in the Add section to which semester? inline prompt shown when the user clicks Add while multiple semesters are load... (~612 tok)
- `SettingsViewModel.cs` — Class: SettingsViewModel (~148 tok)
- `SubjectEditViewModel.cs` — Class: SubjectEditViewModel (~680 tok)
- `SubjectListViewModel.cs` — Class: SubjectListViewModel (~784 tok)
- `TagSelectionViewModel.cs` — Class: TagSelectionViewModel (~123 tok)
- `WorkloadHistoryViewModel.cs` — WorkloadHistoryItemViewModel: LoadHistory, Clear (~1838 tok)
- `WorkloadReportViewModel.cs` — Removes or replaces characters that are invalid in Windows filenames. (~2350 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/

- `DatabaseLocationDialog.axaml` (~1124 tok)
- `DatabaseLocationDialog.axaml.cs` — The chosen full file path after OK. Null means cancelled. (~1466 tok)
- `DebugTestDataView.axaml` (~847 tok)
- `DebugTestDataView.axaml.cs` — Class: DebugTestDataView (~55 tok)
- `DetachedPanelWindow.axaml` (~218 tok)
- `DetachedPanelWindow.axaml.cs` — DetachedPanelWindow: SetContent (~350 tok)
- `SplashScreen.axaml` (~212 tok)
- `SplashScreen.axaml.cs` — Class: SplashScreen (~56 tok)
- `WorkloadPanelView.axaml` (~3478 tok)
- `WorkloadPanelView.axaml.cs` — Class: WorkloadPanelView (~55 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~9557 tok)
- `GridFilterView.axaml.cs` — Updates a filter dimension's header ToggleButton to show how many items are selected, with active colouring when any are. (~1929 tok)
- `ScheduleGridView.axaml` (~3281 tok)
- `ScheduleGridView.axaml.cs` — Padding added to each side of a day header's text when computing the minimum width of a day column. The column will be at least (headerTextWidth + ... (~7776 tok)

## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/Management/

- `AcademicUnitListView.axaml` (~493 tok)
- `AcademicUnitListView.axaml.cs` — Class: AcademicUnitListView (~60 tok)
- `AcademicYearListView.axaml` (~1007 tok)
- `AcademicYearListView.axaml.cs` — Class: AcademicYearListView (~1485 tok)
- `BlockPatternListView.axaml` (~5795 tok)
- `BlockPatternListView.axaml.cs` — Class: BlockPatternListView (~60 tok)
- `ConfirmDialog.axaml` (~320 tok)
- `ConfirmDialog.axaml.cs` — Class: ConfirmDialog (~157 tok)
- `CopySemesterView.axaml` — Declares assignments (~2142 tok)
- `CopySemesterView.axaml.cs` — Class: CopySemesterView (~58 tok)
- `CourseHistoryView.axaml` (~871 tok)
- `CourseHistoryView.axaml.cs` — Class: CourseHistoryView (~58 tok)
- `CourseListView.axaml` (~3394 tok)
- `CourseListView.axaml.cs` — Class: CourseListView (~57 tok)
- `EmptySemesterView.axaml` (~727 tok)
- `EmptySemesterView.axaml.cs` — Class: EmptySemesterView (~903 tok)
- `ErrorDialog.axaml` (~243 tok)
- `ErrorDialog.axaml.cs` — Class: ErrorDialog (~110 tok)
- `ExportView.axaml` (~379 tok)
- `ExportView.axaml.cs` — Class: ExportView (~55 tok)
- `InstructorListView.axaml` (~8196 tok)
- `InstructorListView.axaml.cs` — Class: InstructorListView (~59 tok)
- `LegalStartTimeListView.axaml` (~1831 tok)
- `LegalStartTimeListView.axaml.cs` — Class: LegalStartTimeListView (~61 tok)
- `RoomListView.axaml` (~1109 tok)
- `RoomListView.axaml.cs` — Class: RoomListView (~56 tok)
- `SectionListView.axaml` (~12087 tok)
- `SectionListView.axaml.cs` — Measures the desired (unconstrained) width of the section list's content stack, then widens ThreePanelGrid's left column if the content would be cl... (~913 tok)
- `SectionPrefixListView.axaml` (~1247 tok)
- `SectionPrefixListView.axaml.cs` — Class: SectionPrefixListView (~92 tok)
- `SectionPropertiesView.axaml` (~397 tok)
- `SectionPropertiesView.axaml.cs` — Class: SectionPropertiesView (~60 tok)
- `SectionPropertyListView.axaml` (~1017 tok)
- `SectionPropertyListView.axaml.cs` — Class: SectionPropertyListView (~62 tok)
- `SemesterListView.axaml` (~775 tok)
- `SemesterListView.axaml.cs` — Class: SemesterListView (~58 tok)
- `SettingsView.axaml` (~230 tok)
- `SettingsView.axaml.cs` — Class: SettingsView (~56 tok)
- `SubjectListView.axaml` (~873 tok)
- `SubjectListView.axaml.cs` — Class: SubjectListView (~57 tok)
- `WorkloadHistoryView.axaml` (~754 tok)
- `WorkloadHistoryView.axaml.cs` — Class: WorkloadHistoryView (~59 tok)
- `WorkloadReportView.axaml` (~378 tok)
- `WorkloadReportView.axaml.cs` — Class: WorkloadReportView (~59 tok)

## .claude/worktrees/interesting-wilbur/

- `.gitignore` — Git ignore rules (~95 tok)
- `AUDIT.md` — Critical Audit: SchedulingAssistant (~1630 tok)
- `CLAUDE.md` — Coding Practice (~1779 tok)
- `CodeComments.txt` — Declares in (~513 tok)
- `MULTI_USER_STRATEGY.md` — Multi-User Database Access Strategy (~724 tok)
- `SchedulingAssistant.sln` (~512 tok)
- `TEST_SCENARIO.md` — Test Scenario: Recent Audit Fixes (~3124 tok)

## .claude/worktrees/interesting-wilbur/.claude/

- `settings.local.json` — /*.cs)", (~1831 tok)

## .claude/worktrees/interesting-wilbur/docs/

- `manual.html` — Scheduling Assistant — User Manual (~11033 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant.Tests/

- `ComputeTilesTests.cs` — Unit tests for <see cref="ScheduleGridViewModel.ComputeTiles"/>. ComputeTiles converts a flat list of <see cref="GridBlock"/> objects into a list o... (~3264 tok)
- `GridPipelineTests.cs` — Unit tests for the internal static pipeline methods extracted from <see cref="ScheduleGridViewModel.ReloadCore"/>. Covered methods (all <c>internal... (~6962 tok)
- `SchedulingAssistant.Tests.csproj` (~210 tok)
- `SectionPrefixHelperTests.cs` — Unit tests for <see cref="SectionPrefixHelper"/>. Tests are organized by method: MatchPrefix, FindNextAvailableCode, and AdvanceSectionCode. Each g... (~2839 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/

- `App.axaml` (~1025 tok)
- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices so it can be used during startup error handling. (~2092 tok)
- `app.manifest` (~250 tok)
- `AppColors.axaml` (~1531 tok)
- `MainWindow.axaml` (~8061 tok)
- `MainWindow.axaml.cs` — Switch to a different database without restarting the app. Call this from the Files menu to open a different database. The database file will be cr... (~5766 tok)
- `Program.cs` — Application entry point (~429 tok)
- `SchedulingAssistant.csproj` (~463 tok)
- `ViewLocator.cs` — ViewLocator: Match (~374 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Behaviors/

- `CollectionItemPropertyWatcher.cs` — Attached behavior that monitors an ObservableCollection of INotifyPropertyChanged items and executes a command whenever any item's specified proper... (~2100 tok)
- `ConditionalColumnWidthBehavior.cs` — Attached behavior for Grid controls that toggles a column's width between two values based on a boolean condition. When the condition is true, the ... (~1463 tok)
- `DismissBehaviors.cs` — Attached behavior that executes a command when the Escape key is pressed on a control. Commonly used on a Window or top-level container to dismiss ... (~1140 tok)
- `DoubleTapCommandBehavior.cs` — Attached behavior that executes a command when a control is double-tapped. Commonly used on ListBox to trigger an edit action when an item is doubl... (~696 tok)
- `LostFocusForwardBehavior.cs` — Attached behavior that listens for the bubbling LostFocus event from any descendant whose Name matches a specified target, and executes a command w... (~1090 tok)
- `RightClickCommandBehavior.cs` — Attached behavior that executes a command when the user right-clicks on a control. The command parameter is the PointerPressedEventArgs, which allo... (~623 tok)
- `SelectionCommandBehavior.cs` — Attached behavior for ListBox controls that converts a single-select action into a command invocation. When an item is selected, the behavior: 1. E... (~1151 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Controls/

- `DetachablePanel.axaml` (~788 tok)
- `DetachablePanel.axaml.cs` — Optional content placed inline to the right of the Header text (left-justified). Used for contextual info like semester name and stats. (~857 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Converters/

- `MinutesToTimeConverter.cs` — MinutesToTimeConverter: Convert, ConvertBack (~180 tok)
- `MultiSemesterLeftBorderThicknessConverter.cs` — Converts a boolean (is multi-semester mode) to a BorderThickness. When true, returns 10,0,0,0 (10pt left border for semester indicator). When false... (~253 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — Handles schema migrations for existing databases. (~3400 tok)
- `JsonHelpers.cs` — Class: JsonHelpers (~123 tok)
- `SeedData.cs` — Called by DatabaseContext after schema initialization. Ensures baseline records exist (Academic Unit, default Section Prefixes). (~2233 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/Repositories/

- `AcademicUnitRepository.cs` — Returns true if an academic unit with this name already exists (case-insensitive). Pass excludeId to ignore the unit currently being edited. (~838 tok)
- `AcademicYearRepository.cs` — AcademicYearRepository: GetAll, ExistsByName, Insert, Update + 1 more (~734 tok)
- `BlockPatternRepository.cs` — BlockPatternRepository: GetAll, Insert, Update, Delete (~664 tok)
- `CourseRepository.cs` — Returns true if any sections reference this course. (~1262 tok)
- `InstructorCommitmentRepository.cs` — InstructorCommitmentRepository: GetByInstructor, Insert, Update, Delete (~1053 tok)
- `InstructorRepository.cs` — Returns true if any sections reference this instructor (searches JSON instructorAssignments array). (~1245 tok)
- `LegalStartTimeRepository.cs` — Copies all legal start times from a previous academic year to a new one. If fromAcademicYearId is null, no copy is performed. (~1027 tok)
- `ReleaseRepository.cs` — ReleaseRepository: GetBySemester, GetByInstructor, Insert, Update + 1 more (~894 tok)
- `RoomRepository.cs` — RoomRepository: GetAll, Insert, Update, Delete (~712 tok)
- `SectionPrefixRepository.cs` — CRUD repository for <see cref="SectionPrefix"/> records stored in the <c>SectionPrefixes</c> table. (~1082 tok)
- `SectionPropertyRepository.cs` — Returns true if a value with this name already exists within the given type (case-insensitive). Pass excludeId to skip the record currently being e... (~956 tok)
- `SectionRepository.cs` — Returns all sections for the given course across all semesters, ordered by section code. (~1941 tok)
- `SemesterRepository.cs` — SemesterRepository: GetAll, GetByAcademicYear, Insert, Update + 2 more (~882 tok)
- `SubjectRepository.cs` — Returns true if any courses belong to this subject. (~1257 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Models/

- `AcademicUnit.cs` — Class: AcademicUnit (~52 tok)
- `AcademicYear.cs` — Extracts the start year from a name formatted as "YYYY-YYYY". Returns int.MaxValue if the format is unrecognised (sorts to end). (~132 tok)
- `BlockPattern.cs` — A named favourite day pattern (e.g. "MWF" = days [1, 3, 5]). Stored in the database so all users of the same database see the same patterns. (~165 tok)
- `Course.cs` — Computes the course level from the calendar code. Levels are "0XX", "1XX", "2XX", "3XX", "4XX", or "5+XX" based on the hundreds digit of the course... (~358 tok)
- `Instructor.cs` — Display-only. Resolved at load time from StaffTypeId; not persisted. (~193 tok)
- `InstructorAssignment.cs` — Links an instructor to a section, with an optional workload share. (~106 tok)
- `InstructorCommitment.cs` — Class: InstructorCommitment (~306 tok)
- `LegalStartTime.cs` — Duration in hours, e.g. 1.5, 2.0, 3.0, 4.0. Also the primary key. (~202 tok)
- `RecentDatabaseItem.cs` — Represents a recent database file for display in the Files menu. (~128 tok)
- `Release.cs` — Class: Release (~96 tok)
- `Room.cs` — Class: Room (~107 tok)
- `Section.cs` — Multi-instructor support with workload. Stored in JSON data column. (~336 tok)
- `SectionDaySchedule.cs` — Day of week: 1=Monday, 2=Tuesday, 3=Wednesday, 4=Thursday, 5=Friday. (~216 tok)
- `SectionPrefix.cs` — A section code prefix — the initial symbols used in a typical section code, such as "AB" or "A#". An optional campus association indicates which ca... (~247 tok)
- `SectionPropertyValue.cs` — Optional short code used as a section code prefix/suffix for this campus. Only meaningful for the Campus property type; null for all others. (~123 tok)
- `SectionReserve.cs` — Class: SectionReserve (~46 tok)
- `SectionSortMode.cs` — Class: SectionSortMode (~43 tok)
- `Semester.cs` — The fixed set of semester names auto-created for each Academic Year. (~143 tok)
- `Subject.cs` — Class: Subject (~69 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Properties/

- `AssemblyInfo.cs` — Class: AssemblyInfo (~28 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Services/

- `AcademicUnitService.cs` — Service for accessing the single Academic Unit in the system. There is always exactly one Academic Unit (created at database initialization if miss... (~271 tok)
- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location the app can always find on startup. (~736 tok)
- `DebugTestDataGenerator.cs` — Debug-only utility for generating random test sections with realistic data. (~3044 tok)
- `DialogService.cs` — DialogService: Confirm, ShowError (~248 tok)
- `FileAppLogger.cs` — Writes log entries to a rolling daily log file under %AppData%\SchedulingAssistant\Logs\app-YYYY-MM-DD.log. Designed to be swapped out for a remote... (~1114 tok)
- `IAppLogger.cs` — Application-wide error logger. Implementations can write to a local file, a remote database, a cloud sink, etc. All methods are non-throwing in pro... (~323 tok)
- `IDialogService.cs` — Interface: IDialogService (1 members) (~60 tok)
- `LegalStartTimesDataExporter.cs` — Utility for exporting LegalStartTimes data to JSON for reference and persistence. Captures the block length / start time configuration for each aca... (~1097 tok)
- `LegalStartTimesDataStore.cs` — Manages persistent storage of legal start times configuration. Data is stored in the project directory and travels with the code. (~877 tok)
- `ScheduleValidationService.cs` — Checks sections against an academic year's legal start-time matrix. Reusable across copy-semester, start-time editing, etc. (~499 tok)
- `SectionChangeNotifier.cs` — Provides a notification mechanism when sections are changed externally (e.g., via the schedule grid context menu). Both SectionListViewModel and Sc... (~125 tok)
- `SectionPrefixHelper.cs` — Static utility methods for matching and advancing section codes based on the configured list of <see cref="SectionPrefix"/> entries. This class is ... (~1603 tok)
- `SemesterContext.cs` — Pairs a Semester with a formatted display label. DisplayName uses the full "Year — Semester" form for legacy contexts; for the semester picker UI p... (~3587 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/

- `DebugTestDataViewModel.cs` — When true, exceptions passed to <see cref="IAppLogger.LogError"/> are re-thrown after being written to the log file, so they surface immediately in... (~1335 tok)
- `ErrorViewModel.cs` — Diagnostic ViewModel — the ViewLocator renders its Message property as red text when navigation or resolution fails. (~85 tok)
- `MainWindowViewModel.cs` — The permanent left-panel section list. Held for app lifetime. (~1908 tok)
- `ViewModelBase.cs` — Class: ViewModelBase (~42 tok)
- `WorkloadItemViewModel.cs` — Class: WorkloadItemViewModel (~140 tok)
- `WorkloadPanelViewModel.cs` — Fired when the user clicks a work item chip. (~2892 tok)
- `WorkloadRowViewModel.cs` — Sections and releases for this instructor in the current semester(s). In single-semester mode, this contains all items for that semester. In multi-... (~426 tok)
- `WorkloadSemesterGroupViewModel.cs` — Represents one semester's workload items (sections and releases) within a <see cref="WorkloadRowViewModel"/> when the view is in multi-semester mod... (~537 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/GridView/

- `ContextMenuItemVm.cs` — Class: ContextMenuItemVm (~122 tok)
- `FilterItemViewModel.cs` — A single checkable item in a filter group (e.g. one instructor, one room). (~299 tok)
- `GridData.cs` — Abstract base for any time-positioned block that can be placed on the schedule grid. Day uses 1=Monday … 6=Saturday. Times are minutes from midnigh... (~1940 tok)
- `GridFilterViewModel.cs` — Holds all filter state for the Schedule Grid. Option lists are rebuilt by PopulateOptions() on each Reload(). FilterChanged fires whenever any item... (~6119 tok)
- `GridPipelineTypes.cs` — Aggregates all entity lookup dictionaries that the schedule grid pipeline needs to convert raw <see cref="Section"/> objects into positioned <see c... (~2178 tok)
- `ScheduleGridViewModel.cs` — Represents one colored segment in the semester line display, e.g. "Fall" with orange background. (~13963 tok)
- `SectionContextMenuViewModel.cs` — SectionContextMenuViewModel: Load (~1420 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/Management/

- `AcademicUnitListViewModel.cs` — ViewModel for editing the single Academic Unit in the system. There is always exactly one unit; this dialog allows editing its name. (~440 tok)
- `AcademicYearEditViewModel.cs` — Class: AcademicYearEditViewModel (~666 tok)
- `AcademicYearListViewModel.cs` — Set by the view. Called when adding a new academic year to ask if the user wants to copy the start-time/block-length setup from the previous year. ... (~1934 tok)
- `BlockPatternEditViewModel.cs` — Class: BlockPatternEditViewModel (~579 tok)
- `BlockPatternListViewModel.cs` — Manages up to five block-pattern favourite slots shown in the Block Patterns flyout. Patterns are stored in the database so all users of the same d... (~1281 tok)
- `CommitmentEditViewModel.cs` — CommitmentEditViewModel: DayOption, TimeOption (~1473 tok)
- `CommitmentsManagementViewModel.cs` — Manages the CRUD list of InstructorCommitment records for one instructor in one semester. This VM is embedded inside InstructorListViewModel and is... (~2018 tok)
- `CopySemesterViewModel.cs` — Class: CopySemesterViewModel (~4132 tok)
- `CourseEditViewModel.cs` — Class: CourseEditViewModel (~1002 tok)
- `CourseHistoryItemViewModel.cs` — Represents a hierarchical item in the course history tree. Three levels: Academic Year (IsYear), Semester (IsSemester), Section (IsSection). (~376 tok)
- `CourseHistoryViewModel.cs` — Loads and displays the historical sections of a course, organized by academic year and semester. Hierarchical structure: Academic Year → Semester →... (~1383 tok)
- `CourseListViewModel.cs` — Handles course selection change: loads course history when a course is selected. (~1966 tok)
- `DayCheckViewModel.cs` — A checkable day row used in the block-pattern editor. (~144 tok)
- `EmptySemesterViewModel.cs` — Set by the view. Called before deletion with (semesterName, sectionCount). Should return true if the user confirms, false to cancel. (~1310 tok)
- `ExportViewModel.cs` — Class: ExportViewModel (~642 tok)
- `InstructorEditViewModel.cs` — Class: InstructorEditViewModel (~1023 tok)
- `InstructorListViewModel.cs` — All semesters in the currently selected academic year, available in the flyout's local semester picker. This list is independent of the global seme... (~3652 tok)
- `InstructorSelectionViewModel.cs` — Checkbox + workload wrapper for instructor multi-select in the section editor. (~378 tok)
- `InstructorWorkloadViewModel.cs` — Represents a single assigned section's workload contribution. (~483 tok)
- `ISectionListEntry.cs` — Marker interface for items that can appear in the Section List panel. Implemented by both section cards (<see cref=SectionListItemViewModel/>) and ... (~135 tok)
- `LegalStartTimeEditViewModel.cs` — Class: LegalStartTimeEditViewModel (~994 tok)
- `LegalStartTimeListViewModel.cs` — Represents one item in the "Preferred block length" ComboBox. (~1690 tok)
- `ReleaseEditViewModel.cs` — Class: ReleaseEditViewModel (~515 tok)
- `ReleaseManagementViewModel.cs` — Manages CRUD for releases for a specific instructor in a specific semester. Fires ReleasesChanged event when releases are modified so parent can re... (~1150 tok)
- `ReserveSelectionViewModel.cs` — Count as a user-editable string. Validated on save to a positive integer. (~253 tok)
- `ResourceSelectionViewModel.cs` — Class: ResourceSelectionViewModel (~126 tok)
- `RoomEditViewModel.cs` — Class: RoomEditViewModel (~404 tok)
- `RoomListViewModel.cs` — Class: RoomListViewModel (~903 tok)
- `SectionEditViewModel.cs` — Wrapper used by the Section Prefix picker ComboBox in the section editor. The sentinel item (<see cref="Prefix"/> == null) represents "no prefix se... (~8876 tok)
- `SectionListItemViewModel.cs` — Display wrapper for a section row in the sections list panel. Holds formatted strings so the view needs no converter logic. (~2524 tok)
- `SectionListViewModel.cs` — The flat list of items shown in the Section List. Contains a mix of <see cref="SemesterBannerViewModel"/> (group headers) and <see cref="SectionLis... (~10712 tok)
- `SectionMeetingViewModel.cs` — Represents a single scheduled meeting within a section — day, time, room, and meeting type. (~1410 tok)
- `SectionPrefixEditViewModel.cs` — ViewModel for the inline Add/Edit form in the Section Prefixes flyout. Communicates results back to the parent list via callbacks. (~1142 tok)
- `SectionPrefixListViewModel.cs` — ViewModel for the Section Prefixes management flyout. Provides a list of section prefixes with inline Add/Edit/Delete support. (~1354 tok)
- `SectionPropertiesViewModel.cs` — Class: SectionPropertiesViewModel (~575 tok)
- `SectionPropertyEditViewModel.cs` — The section code abbreviation field value. Only relevant when ShowAbbreviation is true. (~653 tok)
- `SectionPropertyListViewModel.cs` — When true, the "Section Code Abbreviation" field is shown in the edit form and as a column in the list. Currently only true for the Campus property... (~1631 tok)
- `SectionPropertyTypes.cs` — Canonical type discriminator strings for the SectionPropertyValues.type column. (~151 tok)
- `SemesterBannerViewModel.cs` — Represents a semester group header row in the Section List. Carries the semester identity and display colors resolved from application resources (S... (~932 tok)
- `SemesterEditViewModel.cs` — Class: SemesterEditViewModel (~268 tok)
- `SemesterListViewModel.cs` — Class: SemesterListViewModel (~675 tok)
- `SemesterPromptItem.cs` — Represents one semester option in the Add section to which semester? inline prompt shown when the user clicks Add while multiple semesters are load... (~612 tok)
- `SettingsViewModel.cs` — Class: SettingsViewModel (~148 tok)
- `SubjectEditViewModel.cs` — Class: SubjectEditViewModel (~680 tok)
- `SubjectListViewModel.cs` — Class: SubjectListViewModel (~784 tok)
- `TagSelectionViewModel.cs` — Class: TagSelectionViewModel (~123 tok)
- `WorkloadHistoryViewModel.cs` — WorkloadHistoryItemViewModel: LoadHistory, Clear (~1838 tok)
- `WorkloadReportViewModel.cs` — Removes or replaces characters that are invalid in Windows filenames. (~2350 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/

- `DatabaseLocationDialog.axaml` (~1124 tok)
- `DatabaseLocationDialog.axaml.cs` — The chosen full file path after OK. Null means cancelled. (~1466 tok)
- `DebugTestDataView.axaml` (~847 tok)
- `DebugTestDataView.axaml.cs` — Class: DebugTestDataView (~55 tok)
- `DetachedPanelWindow.axaml` (~218 tok)
- `DetachedPanelWindow.axaml.cs` — DetachedPanelWindow: SetContent (~350 tok)
- `SplashScreen.axaml` (~212 tok)
- `SplashScreen.axaml.cs` — Class: SplashScreen (~56 tok)
- `WorkloadPanelView.axaml` (~3478 tok)
- `WorkloadPanelView.axaml.cs` — Class: WorkloadPanelView (~55 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/GridView/

- `GridFilterView.axaml` (~9557 tok)
- `GridFilterView.axaml.cs` — Updates a filter dimension's header ToggleButton to show how many items are selected, with active colouring when any are. (~1929 tok)
- `ScheduleGridView.axaml` (~3281 tok)
- `ScheduleGridView.axaml.cs` — Padding added to each side of a day header's text when computing the minimum width of a day column. The column will be at least (headerTextWidth + ... (~7776 tok)

## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/Management/

- `AcademicUnitListView.axaml` (~493 tok)
- `AcademicUnitListView.axaml.cs` — Class: AcademicUnitListView (~60 tok)
- `AcademicYearListView.axaml` (~1007 tok)
- `AcademicYearListView.axaml.cs` — Class: AcademicYearListView (~1485 tok)
- `BlockPatternListView.axaml` (~5795 tok)
- `BlockPatternListView.axaml.cs` — Class: BlockPatternListView (~60 tok)
- `ConfirmDialog.axaml` (~320 tok)
- `ConfirmDialog.axaml.cs` — Class: ConfirmDialog (~157 tok)
- `CopySemesterView.axaml` — Declares assignments (~2142 tok)
- `CopySemesterView.axaml.cs` — Class: CopySemesterView (~58 tok)
- `CourseHistoryView.axaml` (~871 tok)
- `CourseHistoryView.axaml.cs` — Class: CourseHistoryView (~58 tok)
- `CourseListView.axaml` (~3394 tok)
- `CourseListView.axaml.cs` — Class: CourseListView (~57 tok)
- `EmptySemesterView.axaml` (~727 tok)
- `EmptySemesterView.axaml.cs` — Class: EmptySemesterView (~903 tok)
- `ErrorDialog.axaml` (~243 tok)
- `ErrorDialog.axaml.cs` — Class: ErrorDialog (~110 tok)
- `ExportView.axaml` (~379 tok)
- `ExportView.axaml.cs` — Class: ExportView (~55 tok)
- `InstructorListView.axaml` (~8196 tok)
- `InstructorListView.axaml.cs` — Class: InstructorListView (~59 tok)
- `LegalStartTimeListView.axaml` (~1831 tok)
- `LegalStartTimeListView.axaml.cs` — Class: LegalStartTimeListView (~61 tok)
- `RoomListView.axaml` (~1109 tok)
- `RoomListView.axaml.cs` — Class: RoomListView (~56 tok)
- `SectionListView.axaml` (~12087 tok)
- `SectionListView.axaml.cs` — Measures the desired (unconstrained) width of the section list's content stack, then widens ThreePanelGrid's left column if the content would be cl... (~913 tok)
- `SectionPrefixListView.axaml` (~1247 tok)
- `SectionPrefixListView.axaml.cs` — Class: SectionPrefixListView (~92 tok)
- `SectionPropertiesView.axaml` (~397 tok)
- `SectionPropertiesView.axaml.cs` — Class: SectionPropertiesView (~60 tok)
- `SectionPropertyListView.axaml` (~1017 tok)
- `SectionPropertyListView.axaml.cs` — Class: SectionPropertyListView (~62 tok)
- `SemesterListView.axaml` (~775 tok)
- `SemesterListView.axaml.cs` — Class: SemesterListView (~58 tok)
- `SettingsView.axaml` (~230 tok)
- `SettingsView.axaml.cs` — Class: SettingsView (~56 tok)
- `SubjectListView.axaml` (~873 tok)
- `SubjectListView.axaml.cs` — Class: SubjectListView (~57 tok)
- `WorkloadHistoryView.axaml` (~754 tok)
- `WorkloadHistoryView.axaml.cs` — Class: WorkloadHistoryView (~59 tok)
- `WorkloadReportView.axaml` (~378 tok)
- `WorkloadReportView.axaml.cs` — Class: WorkloadReportView (~59 tok)

## .vs/SchedulingAssistant/DesignTimeBuild/

- `.dtbcache.v2` (~47782 tok)

## .vs/SchedulingAssistant/config/

- `applicationhost.config` (~20410 tok)

## .vs/SchedulingAssistant/v17/

- `.futdcache.v2` (~184 tok)
- `.suo` (~76235 tok)
- `DocumentLayout.json` (~7158 tok)
- `HierarchyCache.v1.txt` (~14085 tok)

## .vs/SchedulingAssistant/v17/TestStore/0/

- `461.testlog` (~119981 tok)
- `testlog.manifest` (~7 tok)

## docs/

- `manual.html` — Scheduling Assistant — User Manual (~10841 tok)

## src/SchedulingAssistant/

- `App.axaml` (~1065 tok)
- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. Set early in InitializeServices so it can be used during startup error handling. (~3013 tok)
- `app.manifest` (~250 tok)
- `AppColors.axaml` (~2155 tok)
- `AssemblyInfo.cs` — Class: AssemblyInfo (~52 tok)
- `Constants.cs` — Application-wide constants for domain rules shared across the codebase. (~106 tok)
- `MainWindow.axaml` — Declares applied (~10112 tok)
- `MainWindow.axaml.cs` — Called whenever the window is about to close — whether via Files → Exit or the title-bar X. All shutdown logic (e.g. backup, save-state) belongs he... (~8770 tok)
- `Program.cs` — Application entry point (~429 tok)
- `SchedulingAssistant.csproj` (~661 tok)
- `ViewLocator.cs` — ViewLocator: Match (~362 tok)

## src/SchedulingAssistant/Behaviors/

- `CollectionItemPropertyWatcher.cs` — Attached behavior that monitors an ObservableCollection of INotifyPropertyChanged items and executes a command whenever any item's specified proper... (~2054 tok)
- `ConditionalColumnWidthBehavior.cs` — Attached behavior for Grid controls that toggles a column's width between two values based on a boolean condition. When the condition is true, the ... (~1432 tok)
- `DismissBehaviors.cs` — Attached behavior that executes a command when the Escape key is pressed on a control. Commonly used on a Window or top-level container to dismiss ... (~1110 tok)
- `DoubleTapCommandBehavior.cs` — Attached behavior that executes a command when a control is double-tapped. Commonly used on ListBox to trigger an edit action when an item is doubl... (~677 tok)
- `LostFocusCommandBehavior.cs` — Attached behavior that executes a command when focus leaves the control it is attached to. Unlike <see cref="LostFocusForwardBehavior"/> (which lis... (~774 tok)
- `LostFocusForwardBehavior.cs` — Attached behavior that listens for the bubbling LostFocus event from any descendant whose Name matches a specified target, and executes a command w... (~1064 tok)
- `OpenDropDownOnFocusBehavior.cs` — Attached behavior for <see cref="AutoCompleteBox"/> that provides two UX improvements needed when the control is used inside a <see cref="DataTempl... (~1371 tok)
- `RightClickCommandBehavior.cs` — Attached behavior that executes a command when the user right-clicks on a control. The command parameter is the PointerPressedEventArgs, which allo... (~606 tok)
- `SelectionCommandBehavior.cs` — Attached behavior for ListBox controls that converts a single-select action into a command invocation. When an item is selected, the behavior: 1. E... (~1125 tok)

## src/SchedulingAssistant/Controls/

- `DetachablePanel.axaml` (~788 tok)
- `DetachablePanel.axaml.cs` — Optional content placed inline to the right of the Header text (left-justified). Used for contextual info like semester name and stats. (~857 tok)

## src/SchedulingAssistant/Converters/

- `FilterBorderThicknessConverter.cs` — Converts a boolean (<see cref="SectionListItemViewModel.IsFilterHighlighted"/>) to a uniform 3 pt <see cref="Thickness"/> used as a section-card bo... (~287 tok)
- `MinutesToTimeConverter.cs` — MinutesToTimeConverter: Convert, ConvertBack (~180 tok)
- `MultiSemesterLeftBorderThicknessConverter.cs` — Converts a boolean (is multi-semester mode) to a BorderThickness. When true, returns 10,0,0,0 (10pt left border for semester indicator). When false... (~253 tok)

## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the schema on first run, applies migrations, and s... (~3150 tok)
- `DbCommandExtensions.cs` — Extension methods for <see cref="DbCommand"/> to provide a convenient parameter-adding API that works across all ADO.NET providers. (~437 tok)
- `IDatabaseContext.cs` — Abstraction over the application database connection. The SQLite desktop implementation opens a file-based database; alternative implementations (e... (~201 tok)
- `JsonHelpers.cs` — Deserializes a JSON string into the specified type. (~316 tok)
- `SeedData.cs` — Called by DatabaseContext after schema initialization. Ensures baseline records exist (Academic Unit, default Section Prefixes). (~2328 tok)

## src/SchedulingAssistant/Data/Repositories/

- `AcademicUnitRepository.cs` — Returns true if an academic unit with this name already exists (case-insensitive). Pass excludeId to ignore the unit currently being edited. (~792 tok)
- `AcademicYearRepository.cs` — AcademicYearRepository: GetAll, ExistsByName, Insert, Update + 1 more (~692 tok)
- `BlockPatternRepository.cs` — BlockPatternRepository: GetAll, Insert, Update, Delete (~639 tok)
- `CampusRepository.cs` — SQLite-backed implementation of <see cref="ICampusRepository"/>. Uses the project's standard pattern: stable identity columns + a <c>data</c> JSON ... (~886 tok)
- `CourseRepository.cs` — Returns true if any sections reference this course. (~1270 tok)
- `IAcademicUnitRepository.cs` — Data access contract for <see cref="AcademicUnit"/> entities (colleges, departments, etc.). (~336 tok)
- `IAcademicYearRepository.cs` — Data access contract for <see cref="AcademicYear"/> entities. (~297 tok)
- `IBlockPatternRepository.cs` — Data access contract for <see cref="BlockPattern"/> entities (standard meeting-time patterns). (~254 tok)
- `ICampusRepository.cs` — Data access contract for <see cref="Campus"/> entities. (~300 tok)
- `ICourseRepository.cs` — Data access contract for <see cref="Course"/> entities. (~484 tok)
- `IInstructorCommitmentRepository.cs` — Data access contract for <see cref="InstructorCommitment"/> entities (blocked-off time on an instructor's schedule for a given semester). (~343 tok)
- `IInstructorRepository.cs` — Data access contract for <see cref="Instructor"/> entities. (~422 tok)
- `ILegalStartTimeRepository.cs` — Data access contract for <see cref="LegalStartTime"/> entities. Each entry defines the allowed section start times for a given block length within ... (~555 tok)
- `InstructorCommitmentRepository.cs` — InstructorCommitmentRepository: GetByInstructor, Insert, Update, Delete (~1011 tok)
- `InstructorRepository.cs` — Returns all instructors, ordered according to the persisted <see cref="AppSettings.InstructorSortMode"/> preference. <para> <b>LastName / FirstName... (~1569 tok)
- `IReleaseRepository.cs` — Data access contract for <see cref="Release"/> entities (workload release/reduction assignments for instructors within a semester). (~358 tok)
- `IRoomRepository.cs` — Data access contract for <see cref="Room"/> entities. (~268 tok)
- `ISectionPrefixRepository.cs` — Data access contract for <see cref="SectionPrefix"/> entities (institutional prefix conventions used to auto-generate section codes). (~364 tok)
- `ISectionPropertyRepository.cs` — Data access contract for <see cref="SectionPropertyValue"/> entities. Property values are typed (e.g. sectionType, tag, campus) and shared across a... (~491 tok)
- `ISectionRepository.cs` — Data access contract for <see cref="Section"/> entities (the core scheduling entity). (~631 tok)
- `ISemesterRepository.cs` — Data access contract for <see cref="Semester"/> entities. (~346 tok)
- `ISubjectRepository.cs` — Data access contract for <see cref="Subject"/> entities (academic disciplines / departments). (~451 tok)
- `LegalStartTimeRepository.cs` — Copies all legal start times from a previous academic year to a new one. If fromAcademicYearId is null, no copy is performed. (~975 tok)
- `ReleaseRepository.cs` — ReleaseRepository: GetBySemester, GetByInstructor, Insert, Update + 1 more (~844 tok)
- `RoomRepository.cs` — Returns all rooms ordered by <see cref="Room.SortOrder"/> ascending, then by building and room number as a tiebreaker. Sorting is done in C# after ... (~772 tok)
- `SectionPrefixRepository.cs` — CRUD repository for <see cref="SectionPrefix"/> records stored in the <c>SectionPrefixes</c> table. (~1050 tok)

## src/SchedulingAssistant/ViewModels/Wizard/

- `StartupWizardViewModel.cs` — Orchestrates the multi-step startup wizard. Step index map: 0 — Welcome 1 — Existing-DB check (Step1 (~4917 tok)

## src/SchedulingAssistant/ViewModels/Wizard/Steps/

- `Step10ClosingViewModel.cs` — Step 10 — closing/congratulations panel shown after all configuration is complete. Clicking Finish h (~132 tok)
- `Step1aExistingDbViewModel.cs` — Step 1a — asks whether the user already has a TermPoint database set up by a colleague. "Yes" path: (~1088 tok)
- `Step2DatabaseViewModel.cs` — Step 2 — choose the database folder, confirm/edit the database filename, and choose the backup folde (~1408 tok)

## src/SchedulingAssistant/Views/Wizard/

- `StartupWizardWindow.axaml` (~1160 tok)

## src/SchedulingAssistant/Views/Wizard/Steps/

- `Step10ClosingView.axaml` (~363 tok)
- `Step10ClosingView.axaml.cs` — Class: Step10ClosingView (~56 tok)
- `Step1aExistingDbView.axaml` (~1031 tok)
- `Step1aExistingDbView.axaml.cs` — Class: Step1aExistingDbView (~53 tok)
- `Step2DatabaseView.axaml` (~1450 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/

- `TermPoint.deps.json` (~13983 tok)
- `TermPoint.pdb` (~154369 tok)
- `TermPoint.runtimeconfig.json` (~118 tok)

## src/SchedulingAssistant/bin/Debug/net8.0/Help/

- `exporting.html` — Exporting — TermPoint Help (~992 tok)
- `getting-started.html` — Getting Started — TermPoint Help (~1215 tok)
- `help.css` — Styles: 23 rules, 15 vars (~1554 tok)
- `managing-instructors.html` — Managing Instructors — TermPoint Help (~983 tok)
- `managing-sections.html` — Managing Sections — TermPoint Help (~1125 tok)
- `schedule-grid-filters.html` — Grid Filters — TermPoint Help (~928 tok)
- `schedule-grid.html` — The Schedule Grid — TermPoint Help (~990 tok)
- `section-editing-tips.html` — Section Editing Tips — TermPoint Help (~1499 tok)
- `welcome.html` — Welcome — TermPoint Help (~906 tok)
- `workload-assignment.html` — Make Workload Easy — TermPoint Help (~1413 tok)

## src/SchedulingAssistant/bin/Release/net8.0-browser/

- `SchedulingAssistant.deps.json` (~2722 tok)
- `SchedulingAssistant.pdb` (~109854 tok)

## src/SchedulingAssistant/bin/Release/net8.0/

- `TermPoint.deps.json` (~13931 tok)
- `TermPoint.pdb` (~116112 tok)
- `TermPoint.runtimeconfig.json` (~139 tok)

## src/SchedulingAssistant/bin/Release/net8.0/Help/

- `exporting.html` — Exporting — TermPoint Help (~992 tok)
- `getting-started.html` — Getting Started — TermPoint Help (~1215 tok)
- `help.css` — Styles: 23 rules, 15 vars (~1554 tok)
- `managing-instructors.html` — Managing Instructors — TermPoint Help (~983 tok)
- `managing-sections.html` — Managing Sections — TermPoint Help (~1125 tok)
- `schedule-grid-filters.html` — Grid Filters — TermPoint Help (~928 tok)
- `schedule-grid.html` — The Schedule Grid — TermPoint Help (~990 tok)
- `section-editing-tips.html` — Section Editing Tips — TermPoint Help (~1499 tok)
- `welcome.html` — Welcome — TermPoint Help (~906 tok)
- `workload-assignment.html` — Make Workload Easy — TermPoint Help (~1413 tok)
