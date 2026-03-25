# Memory

> Chronological action log. Hooks and AI append to this file automatically.
> Old sessions are consolidated by the daemon weekly.

## Session: 2026-03-25 — visual-improvements branch

| Action | Files | Outcome |
|--------|-------|---------|
| Created branch `visual-improvements` | — | New branch |
| Lightened `ChromeBorder` #7AAAD4 → #D4DFE8 | AppColors.axaml | Removes heavy blue border on all panels |
| Lightened `ButtonBorder` #7AAAD4 → #C5D8EC | AppColors.axaml | Softer button definition |
| Lightened `SeparatorLine` → #C4D3E0, `GridSplitterColor` → #E0E8F0 | AppColors.axaml | Lighter structural lines |
| Added `PanelBoxShadow` resource | AppColors.axaml | Reusable shadow key |
| Added BoxShadow + CornerRadius to DetachablePanel outer border | DetachablePanel.axaml | Soft shadow replaces inset border |
| Added BoxShadow + CornerRadius to Section View outer border | MainWindow.axaml | Consistent with DetachablePanel |
| Increased Section View header padding 8,4 → 10,6 | MainWindow.axaml | Breathing room |
| Removed button border (BorderThickness 1 → 0) | App.axaml | Flat fill-only buttons |
| Filter bar separator Width 3 → 1 | GridFilterView.axaml | Thinner rule |
| Section card separator BorderThickness 0,0,0,2 → 0,0,0,1 | SectionListView.axaml | Lighter separator |
| Semester banner bottom border 2 → 1 | SectionListView.axaml | Consistent |
| Summary row margin 2,3,6,2 → 2,4,6,3 | SectionListView.axaml | 1px breathing room |
| Inline editor left accent 3,0,0,0 → 2,0,0,0 | SectionListView.axaml | Less heavy accent |
| Workload semester frame border 2 → 1 | WorkloadPanelView.axaml | Lighter grouping |
| 09:25 | Edited src/SchedulingAssistant/Views/Wizard/StartupWizardWindow.axaml | 7→7 lines | ~86 |

## Session: 2026-03-24 09:41

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 09:43 | Created src/SchedulingAssistant/ViewModels/Wizard/Steps/Step10ClosingViewModel.cs | — | ~132 |
| 09:44 | Created src/SchedulingAssistant/Views/Wizard/Steps/Step10ClosingView.axaml | — | ~363 |
| 09:44 | Created src/SchedulingAssistant/Views/Wizard/Steps/Step10ClosingView.axaml.cs | — | ~56 |
| 09:44 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | 13→9 lines | ~103 |
| 09:44 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | added 1 condition(s) | ~89 |
| 09:44 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | modified steps() | ~128 |
| 09:44 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | modified IsLastStep() | ~84 |
| 09:44 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | modified ExitNow() | ~357 |
| 09:46 | Session end: 8 writes across 4 files (Step10ClosingViewModel.cs, Step10ClosingView.axaml, Step10ClosingView.axaml.cs, StartupWizardViewModel.cs) | 3 reads | ~2927 tok |
| 09:59 | Edited src/SchedulingAssistant/Views/Wizard/Steps/Step2DatabaseView.axaml | 2→2 lines | ~45 |
| 09:59 | Edited src/SchedulingAssistant/ViewModels/Wizard/Steps/Step2DatabaseViewModel.cs | expanded (+8 lines) | ~169 |
| 09:59 | Edited src/SchedulingAssistant/ViewModels/Wizard/Steps/Step2DatabaseViewModel.cs | 5→6 lines | ~76 |
| 09:59 | Edited src/SchedulingAssistant/ViewModels/Wizard/Steps/Step2DatabaseViewModel.cs | 5→6 lines | ~76 |
| 10:00 | Session end: 12 writes across 6 files (Step10ClosingViewModel.cs, Step10ClosingView.axaml, Step10ClosingView.axaml.cs, StartupWizardViewModel.cs, Step2DatabaseView.axaml) | 5 reads | ~3320 tok |

## Session: 2026-03-24 10:16

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|

## Session: 2026-03-24 10:27

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 10:27 | Created src/SchedulingAssistant/ViewModels/Wizard/Steps/Step1aExistingDbViewModel.cs | — | ~1088 |
| 10:27 | Created src/SchedulingAssistant/Views/Wizard/Steps/Step1aExistingDbView.axaml | — | ~1031 |
| 10:28 | Created src/SchedulingAssistant/Views/Wizard/Steps/Step1aExistingDbView.axaml.cs | — | ~53 |
| 10:29 | Created src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | — | ~4917 |
| 10:30 | Session end: 4 writes across 4 files (Step1aExistingDbViewModel.cs, Step1aExistingDbView.axaml, Step1aExistingDbView.axaml.cs, StartupWizardViewModel.cs) | 0 reads | ~7594 tok |
| 10:00 | Wizard Step3: replaced HasTpConfig bool with Step3Choice enum (Manual/Import/ExitNow); added proxy bool properties for radio binding | Step3TpConfigViewModel.cs, Step3TpConfigView.axaml | Build clean | ~1200 |
| 10:05 | Wizard orchestrator: removed IsInitialSetupComplete from ValidateStep2; deferred to FinishAsync; added ExitNow handling in IsLastStep+FinishAsync | StartupWizardViewModel.cs | Build clean | ~800 |
| 10:15 | Step3TpConfigView: added third radio "exit wizard now"; inline hint text when ExitNow selected | Step3TpConfigView.axaml | Build clean | ~400 |
| 10:30 | Wizard scrolling: fixed ContentControl VerticalAlignment="Top" in wizard window; switched to Grid layout for bounded ScrollViewer height | StartupWizardWindow.axaml | Functional | ~600 |
| 10:45 | Wizard font sizes: replaced all hardcoded FontSize values in wizard step views and 3 embedded mgmt views with DynamicResource keys; bumped Window.Resources to FontSizeNormal=16 (Medium as base) | Steps/*.axaml, Mgmt/BlockPatternListView.axaml, CampusListView.axaml, SectionPrefixListView.axaml, StartupWizardWindow.axaml | Build clean | ~2000 |
| 11:00 | Step7SectionPrefixesView: built 3-group example grid with colored course/prefix/designator tiles; WrapPanel comments span RowSpan=2 and VerticalAlignment=Center | Step7SectionPrefixesView.axaml | Visual | ~1500 |
| 11:15 | Step2DatabaseView: added IsFilenameReady computed property; backup folder section now hidden until DB folder+filename are valid, creating top-to-bottom reveal flow | Step2DatabaseViewModel.cs, Step2DatabaseView.axaml | Build clean | ~600 |
| 11:30 | Wizard: added Step1aExistingDb between Step0 and old Step1; all subsequent steps renumbered +1 (now 0-11); existing-DB path opens DB, saves settings, sets IsInitialSetupComplete=true, closes immediately | Step1aExistingDbViewModel.cs, Step1aExistingDbView.axaml, Step1aExistingDbView.axaml.cs, StartupWizardViewModel.cs | Build clean | ~3000 |
| 11:35 | Wizard orchestrator: PropertyChanged subscriptions on Step1a and Step4 (TpConfig) VMs so NextButtonText updates to "Finish" reactively when terminal choices are selected | StartupWizardViewModel.cs | Build clean | ~300 |
| 10:37 | Session end: 4 writes across 4 files (Step1aExistingDbViewModel.cs, Step1aExistingDbView.axaml, Step1aExistingDbView.axaml.cs, StartupWizardViewModel.cs) | 0 reads | ~7594 tok |

## Session: 2026-03-24 10:40

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 13:10 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | added optional chaining | ~770 |
| 13:11 | Edited src/SchedulingAssistant/ViewModels/Wizard/StartupWizardViewModel.cs | added 1 condition(s) | ~666 |
| 13:11 | Session end: 2 writes across 1 files (StartupWizardViewModel.cs) | 16 reads | ~9448 tok |
| 13:12 | Session end: 2 writes across 1 files (StartupWizardViewModel.cs) | 16 reads | ~9448 tok |

## Session: 2026-03-24 13:34

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 13:42 | Edited src/SchedulingAssistant/Models/SectionPrefix.cs | expanded (+18 lines) | ~406 |
| 13:42 | Edited src/SchedulingAssistant/ViewModels/Management/SectionPrefixEditViewModel.cs | added 2 condition(s) | ~294 |
| 13:42 | Edited src/SchedulingAssistant/ViewModels/Management/SectionPrefixEditViewModel.cs | 3→4 lines | ~63 |
| 13:42 | Edited src/SchedulingAssistant/ViewModels/Management/SectionPrefixEditViewModel.cs | 3→4 lines | ~52 |
| 13:43 | Edited src/SchedulingAssistant/ViewModels/Management/SectionPrefixListViewModel.cs | 7→8 lines | ~96 |
| 13:43 | Edited src/SchedulingAssistant/ViewModels/Management/SectionPrefixListViewModel.cs | modified SectionPrefixRow() | ~169 |
| 13:43 | Edited src/SchedulingAssistant/Views/Management/SectionPrefixListView.axaml | expanded (+19 lines) | ~290 |
| 13:43 | Edited src/SchedulingAssistant/Views/Management/SectionPrefixListView.axaml | 8→12 lines | ~131 |
| 13:43 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | digit() → IsLetter() | ~575 |
| 13:43 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | added 2 condition(s) | ~432 |
| 13:44 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | modified if() | ~82 |
| 13:44 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified new() | ~87 |
| 13:44 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified MatchPrefix_EmptyPrefixEntryInList_IsSkipped() | ~506 |
| 13:44 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified FindNextAvailableCode_AllSlotsTaken_ReturnsNull() | ~331 |
| 13:44 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified AdvanceSectionCode_UnknownPrefixAllDigits_FallbackIncrement() | ~436 |
| 13:46 | Session end: 15 writes across 6 files (SectionPrefix.cs, SectionPrefixEditViewModel.cs, SectionPrefixListViewModel.cs, SectionPrefixListView.axaml, SectionPrefixHelper.cs) | 11 reads | ~7972 tok |
| 13:57 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified MatchPrefix_LetterDesignator_MatchesWhenSuffixIsLetter() | ~936 |
| 13:57 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified FindNextAvailableCode_Letter_NoSlotsTaken_ReturnsA() | ~903 |
| 13:58 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified AdvanceSectionCode_LetterPrefix_GapFill_ReturnsFirstAvailableLetter() | ~718 |
| 13:58 | Edited src/SchedulingAssistant/ViewModels/Management/SectionEditViewModel.cs | 4→7 lines | ~123 |
| 14:00 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | modified if() | ~89 |
| 14:00 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | 3→4 lines | ~82 |
| 14:00 | Edited src/SchedulingAssistant/Services/SectionPrefixHelper.cs | modified LetterDesignatorSequence() | ~292 |
| 14:01 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified FindNextAvailableCode_Letter_AllSlotsTaken_ReturnsNull() | ~395 |
| 14:01 | Edited src/SchedulingAssistant.Tests/SectionPrefixHelperTests.cs | modified FindNextAvailableCode_Letter_GeneratesUppercase() | ~255 |
| 14:02 | Session end: 24 writes across 7 files (SectionPrefix.cs, SectionPrefixEditViewModel.cs, SectionPrefixListViewModel.cs, SectionPrefixListView.axaml, SectionPrefixHelper.cs) | 13 reads | ~17787 tok |

## Session: 2026-03-25 09:24

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|

## Session: 2026-03-25 10:30

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|

## Session: 2026-03-25 10:41

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 10:43 | Edited src/SchedulingAssistant/AppColors.axaml | 3→4 lines | ~82 |
| 10:43 | Edited src/SchedulingAssistant/AppColors.axaml | 5→6 lines | ~114 |
| 10:43 | Edited src/SchedulingAssistant/AppColors.axaml | 5→8 lines | ~152 |
| 10:43 | Edited src/SchedulingAssistant/Controls/DetachablePanel.axaml | 1→6 lines | ~69 |
| 10:43 | Edited src/SchedulingAssistant/MainWindow.axaml | 5→8 lines | ~122 |
| 10:43 | Edited src/SchedulingAssistant/MainWindow.axaml | 8→8 lines | ~126 |
| 10:43 | Edited src/SchedulingAssistant/App.axaml | 7→7 lines | ~108 |
| 10:44 | Edited src/SchedulingAssistant/Views/GridView/GridFilterView.axaml | 5→5 lines | ~66 |
| 10:44 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 7→7 lines | ~115 |
| 10:44 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 2→3 lines | ~106 |
| 10:45 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 4→4 lines | ~80 |
| 12:10 | Edited src/SchedulingAssistant/Views/WorkloadPanelView.axaml | 2→2 lines | ~45 |
| 12:10 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 6→7 lines | ~156 |
| 12:11 | Session end: 13 writes across 7 files (AppColors.axaml, DetachablePanel.axaml, MainWindow.axaml, App.axaml, GridFilterView.axaml) | 8 reads | ~20399 tok |
| 12:16 | Edited src/SchedulingAssistant/App.axaml | 2→7 lines | ~135 |

## Session: 2026-03-25 12:20

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 12:21 | Edited src/SchedulingAssistant/App.axaml | 6→3 lines | ~64 |
| 12:21 | Session end: 1 writes across 1 files (App.axaml) | 0 reads | ~68 tok |

## Session: 2026-03-25 12:25

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
| 12:28 | Created src/SchedulingAssistant/AppColors.axaml | — | ~2619 |
| 12:29 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | 12→11 lines | ~135 |
| 12:29 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | "#CCCCCC" → "{StaticResource InlineLis" | ~13 |
| 12:29 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | "#CCCCCC" → "{StaticResource InlineLis" | ~13 |
| 12:29 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | "#666666" → "{StaticResource TextMuted" | ~11 |
| 12:29 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | 2→2 lines | ~31 |
| 12:30 | Edited src/SchedulingAssistant/Views/Management/InstructorListView.axaml | "White" → "{StaticResource SurfaceBa" | ~13 |
| 12:30 | Edited src/SchedulingAssistant/Views/Management/CourseListView.axaml | 2→2 lines | ~44 |
| 12:30 | Edited src/SchedulingAssistant/Views/Management/CourseListView.axaml | 8→8 lines | ~140 |
| 12:30 | Edited src/SchedulingAssistant/Views/Management/CourseListView.axaml | inline fix | ~23 |
| 12:30 | Edited src/SchedulingAssistant/Views/Management/CourseListView.axaml | 2→2 lines | ~44 |
| 12:30 | Edited src/SchedulingAssistant/Views/WorkloadPanelView.axaml | 3→3 lines | ~42 |
| 12:30 | Edited src/SchedulingAssistant/Views/WorkloadPanelView.axaml | 7→7 lines | ~105 |
| 12:30 | Edited src/SchedulingAssistant/Views/WorkloadPanelView.axaml | "#E8E8E8" → "{StaticResource ItemSepar" | ~29 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/CourseHistoryView.axaml | "#666666" → "{StaticResource TextMuted" | ~20 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/CourseHistoryView.axaml | "#555555" → "{StaticResource TextSecon" | ~12 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/CourseHistoryView.axaml | "#777777" → "{StaticResource TextMuted" | ~11 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/WorkloadHistoryView.axaml | "#555555" → "{StaticResource TextSecon" | ~12 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/WorkloadHistoryView.axaml | "#777777" → "{StaticResource TextMuted" | ~11 |
| 12:31 | Edited src/SchedulingAssistant/Views/GridView/GridFilterView.axaml | "#E8EEF8" → "{StaticResource FilterSen" | ~24 |
| 12:31 | Edited src/SchedulingAssistant/Views/Management/ConfirmDialog.axaml | "#999999" → "{StaticResource SubduedBo" | ~21 |
| 12:31 | Edited src/SchedulingAssistant/Views/DebugTestDataView.axaml | "#333333" → "{StaticResource TextFaint" | ~17 |
| 12:31 | Edited src/SchedulingAssistant/Views/DebugTestDataView.axaml | "#666666" → "{StaticResource TextMuted" | ~17 |
| 12:32 | Edited src/SchedulingAssistant/Views/Management/CopySemesterView.axaml | inline fix | ~30 |
| 12:32 | Edited src/SchedulingAssistant/Views/Management/EmptySemesterView.axaml | inline fix | ~28 |
| 12:32 | Edited src/SchedulingAssistant/Views/Wizard/Steps/Step7SectionPrefixesView.axaml | "#BBDEFB" → "{StaticResource PrefixCam" | ~15 |
| 12:32 | Edited src/SchedulingAssistant/Views/Wizard/Steps/Step7SectionPrefixesView.axaml | "#C8E6C9" → "{StaticResource PrefixCam" | ~15 |
| 12:33 | Session end: 27 writes across 12 files (AppColors.axaml, InstructorListView.axaml, CourseListView.axaml, WorkloadPanelView.axaml, CourseHistoryView.axaml) | 11 reads | ~17351 tok |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 3→3 lines | ~39 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 14→16 lines | ~241 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | expanded (+7 lines) | ~336 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 7→6 lines | ~59 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 4→4 lines | ~80 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 3→3 lines | ~56 |
| 12:42 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 4→4 lines | ~75 |
| 12:43 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 6→7 lines | ~117 |
| 12:43 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 6→6 lines | ~124 |
| 12:44 | Session end: 36 writes across 13 files (AppColors.axaml, InstructorListView.axaml, CourseListView.axaml, WorkloadPanelView.axaml, CourseHistoryView.axaml) | 12 reads | ~37553 tok |
| 12:48 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 8→12 lines | ~222 |
| 12:48 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 8→10 lines | ~237 |
| 12:49 | Session end: 38 writes across 13 files (AppColors.axaml, InstructorListView.axaml, CourseListView.axaml, WorkloadPanelView.axaml, CourseHistoryView.axaml) | 12 reads | ~38196 tok |
| 12:53 | Session end: 38 writes across 13 files (AppColors.axaml, InstructorListView.axaml, CourseListView.axaml, WorkloadPanelView.axaml, CourseHistoryView.axaml) | 12 reads | ~38196 tok |
| 12:56 | Edited src/SchedulingAssistant/ViewModels/Management/SectionListItemViewModel.cs | expanded (+18 lines) | ~327 |
| 12:56 | Edited src/SchedulingAssistant/ViewModels/Management/SectionListViewModel.cs | modified OnSelectedItemChanged() | ~120 |
| 12:56 | Edited src/SchedulingAssistant/ViewModels/Management/SectionListViewModel.cs | modified ApplyFilterHighlights() | ~239 |
| 12:56 | Edited src/SchedulingAssistant/Views/Management/SectionListView.axaml | 7→7 lines | ~148 |
| 12:58 | Session end: 42 writes across 15 files (AppColors.axaml, InstructorListView.axaml, CourseListView.axaml, WorkloadPanelView.axaml, CourseHistoryView.axaml) | 15 reads | ~39090 tok |

## Session: 2026-03-25 14:12

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|

## Session: 2026-03-25 14:21

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|

## Session: 2026-03-25 15:13

| Time | Action | File(s) | Outcome | ~Tokens |
|------|--------|---------|---------|--------|
