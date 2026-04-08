using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Wizard.Steps;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for individual wizard step ViewModels.
/// These tests exercise validation logic, CanAdvance gating, and computed properties
/// in complete isolation — no database, no DI, no mocks required.
/// </summary>
public class WizardStepValidationTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Step 0 — Welcome
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step0_CanAdvance_IsAlwaysTrue()
    {
        var vm = new Step0WelcomeViewModel();
        Assert.True(vm.CanAdvance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 1a — Existing Database
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step1a_NewSetup_CanAdvance_WithoutDbPath()
    {
        var vm = new Step1aExistingDbViewModel(null!);
        vm.IsNewSetupChoice = true;
        Assert.True(vm.CanAdvance);
    }

    [Fact]
    public void Step1a_ExistingDb_CannotAdvance_WithoutDbPath()
    {
        var vm = new Step1aExistingDbViewModel(null!);
        vm.IsExistingDbChoice = true;
        vm.DbPath = string.Empty;
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step1a_ExistingDb_CanAdvance_WithDbPath()
    {
        var vm = new Step1aExistingDbViewModel(null!);
        vm.IsExistingDbChoice = true;
        vm.DbPath = @"C:\some\file.db";
        Assert.True(vm.CanAdvance);
    }

    [Fact]
    public void Step1a_RadioProxy_ExistingDbChoice_SetsHasExistingDb()
    {
        var vm = new Step1aExistingDbViewModel(null!);
        vm.IsExistingDbChoice = true;
        Assert.True(vm.HasExistingDb);
        Assert.True(vm.IsExistingDbChoice);
        Assert.False(vm.IsNewSetupChoice);
    }

    [Fact]
    public void Step1a_RadioProxy_NewSetupChoice_ClearsHasExistingDb()
    {
        var vm = new Step1aExistingDbViewModel(null!);
        vm.IsExistingDbChoice = true; // set first
        vm.IsNewSetupChoice   = true; // then clear
        Assert.False(vm.HasExistingDb);
        Assert.True(vm.IsNewSetupChoice);
        Assert.False(vm.IsExistingDbChoice);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 1 — Institution & Academic Unit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step1Institution_CannotAdvance_WhenAnyFieldBlank()
    {
        var vm = new Step1InstitutionViewModel();

        // Completely empty
        Assert.False(vm.CanAdvance);

        // Three of four filled
        vm.InstitutionName   = "Test University";
        vm.InstitutionAbbrev = "TU";
        vm.AcUnitName        = "Engineering";
        vm.AcUnitAbbrev      = string.Empty;
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step1Institution_CannotAdvance_WhenFieldIsWhitespaceOnly()
    {
        var vm = new Step1InstitutionViewModel
        {
            InstitutionName   = "  ",
            InstitutionAbbrev = "TU",
            AcUnitName        = "Engineering",
            AcUnitAbbrev      = "ENG"
        };
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step1Institution_CanAdvance_WhenAllFieldsFilled()
    {
        var vm = new Step1InstitutionViewModel
        {
            InstitutionName   = "Test University",
            InstitutionAbbrev = "TU",
            AcUnitName        = "Engineering",
            AcUnitAbbrev      = "ENG"
        };
        Assert.True(vm.CanAdvance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 2 — Database Location
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step2Database_CannotAdvance_WhenDbFolderBlank()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = string.Empty,
            BackupFolder = @"C:\backup"
        };
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step2Database_CannotAdvance_WhenBackupFolderBlank()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = @"C:\data",
            BackupFolder = string.Empty
        };
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step2Database_DbFilenameError_WhenFilenameBlank()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = @"C:\data",
            DbFilename   = "   ",
            BackupFolder = @"C:\backup"
        };
        Assert.NotNull(vm.DbFilenameError);
    }

    [Theory]
    [InlineData("my/db.db")]
    [InlineData(@"my\db.db")]
    [InlineData("my:db.db")]
    [InlineData("my*db.db")]
    [InlineData("my?db.db")]
    [InlineData("my\"db.db")]
    [InlineData("my<db.db")]
    [InlineData("my>db.db")]
    [InlineData("my|db.db")]
    public void Step2Database_DbFilenameError_WhenFilenameContainsInvalidChar(string filename)
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder   = @"C:\data",
            DbFilename = filename
        };
        Assert.NotNull(vm.DbFilenameError);
    }

    [Fact]
    public void Step2Database_DbFilenameError_NullWhenFilenameValid()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder   = @"C:\data",
            DbFilename = "myschool.db"
        };
        Assert.Null(vm.DbFilenameError);
    }

    [Fact]
    public void Step2Database_IsFilenameReady_FalseWhenFolderBlank()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder   = string.Empty,
            DbFilename = "myschool.db"
        };
        Assert.False(vm.IsFilenameReady);
    }

    [Fact]
    public void Step2Database_IsFilenameReady_TrueWhenFolderSetAndFilenameValid()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder   = @"C:\data",
            DbFilename = "myschool.db"
        };
        Assert.True(vm.IsFilenameReady);
    }

    [Fact]
    public void Step2Database_DbFullPath_CombinesFolderAndFilename()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder   = @"C:\data",
            DbFilename = "myschool.db"
        };
        Assert.Equal(@"C:\data\myschool.db", vm.DbFullPath);
    }

    [Fact]
    public void Step2Database_SameFolderWarning_TrueWhenPathsMatch()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = @"C:\data",
            BackupFolder = @"C:\data"
        };
        Assert.True(vm.SameFolderWarning);
    }

    [Fact]
    public void Step2Database_SameFolderWarning_FalseWhenPathsDiffer()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = @"C:\data",
            BackupFolder = @"C:\backup"
        };
        Assert.False(vm.SameFolderWarning);
    }

    [Fact]
    public void Step2Database_CanAdvance_TrueWhenAllValid()
    {
        var vm = new Step2DatabaseViewModel("ENG", null!)
        {
            DbFolder     = @"C:\data",
            DbFilename   = "myschool.db",
            BackupFolder = @"C:\backup"
        };
        Assert.True(vm.CanAdvance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3 — TpConfig / Route choice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step3TpConfig_CanAdvance_TrueForManualChoice()
    {
        var vm = new Step3TpConfigViewModel(null!) { IsManualChoice = true };
        Assert.True(vm.CanAdvance);
    }

    [Fact]
    public void Step3TpConfig_CanAdvance_TrueForExitNowChoice()
    {
        var vm = new Step3TpConfigViewModel(null!) { IsExitNowChoice = true };
        Assert.True(vm.CanAdvance);
    }

    [Fact]
    public void Step3TpConfig_CannotAdvance_ForImportChoice_WhenPathEmpty()
    {
        var vm = new Step3TpConfigViewModel(null!)
        {
            IsImportChoice = true,
            TpConfigPath   = string.Empty
        };
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step3TpConfig_CanAdvance_ForImportChoice_WhenPathSet()
    {
        var vm = new Step3TpConfigViewModel(null!)
        {
            IsImportChoice = true,
            TpConfigPath   = @"C:\config.tpconfig"
        };
        Assert.True(vm.CanAdvance);
    }

    [Fact]
    public void Step3TpConfig_ChoiceProxies_AreMutuallyExclusive()
    {
        var vm = new Step3TpConfigViewModel(null!);

        vm.IsManualChoice = true;
        Assert.True(vm.IsManualChoice);
        Assert.False(vm.IsImportChoice);
        Assert.False(vm.IsExitNowChoice);

        vm.IsImportChoice = true;
        Assert.False(vm.IsManualChoice);
        Assert.True(vm.IsImportChoice);
        Assert.False(vm.IsExitNowChoice);

        vm.IsExitNowChoice = true;
        Assert.False(vm.IsManualChoice);
        Assert.False(vm.IsImportChoice);
        Assert.True(vm.IsExitNowChoice);
    }

    [Fact]
    public void Step3TpConfig_ValidateAndImport_ReturnsTrueForManualWithoutFile()
    {
        var vm = new Step3TpConfigViewModel(null!) { IsManualChoice = true };
        Assert.True(vm.ValidateAndImport());
        Assert.Null(vm.ImportedConfig);
    }

    [Fact]
    public void Step3TpConfig_ValidateAndImport_ReturnsTrueForExitNowWithoutFile()
    {
        var vm = new Step3TpConfigViewModel(null!) { IsExitNowChoice = true };
        Assert.True(vm.ValidateAndImport());
    }

    [Fact]
    public void Step3TpConfig_ValidateAndImport_ReturnsFalseAndSetsError_WhenFileInvalid()
    {
        var vm = new Step3TpConfigViewModel(null!)
        {
            IsImportChoice = true,
            TpConfigPath   = @"C:\does_not_exist.tpconfig"
        };
        Assert.False(vm.ValidateAndImport());
        Assert.NotEmpty(vm.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 5 (wizard index 9) — Academic Year
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc")]
    [InlineData("25")]
    [InlineData("20256")]
    [InlineData("hello")]
    [InlineData("2025-2028")]   // second year is not y1+1
    [InlineData("abcd-efgh")]   // non-numeric range
    public void Step5AcademicYear_NameError_SetForInvalidInput(string input)
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = input };
        Assert.NotNull(vm.AcademicYearNameError);
    }

    [Fact]
    public void Step5AcademicYear_NameError_NullWhenFieldEmpty()
    {
        // No premature error message while user has not typed anything
        var vm = new Step5AcademicYearViewModel { AcademicYearName = string.Empty };
        Assert.Null(vm.AcademicYearNameError);
    }

    [Theory]
    [InlineData("2025")]
    [InlineData("2025-2026")]
    public void Step5AcademicYear_NameError_NullForValidInput(string input)
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = input };
        Assert.Null(vm.AcademicYearNameError);
    }

    [Fact]
    public void Step5AcademicYear_ExpandedName_ExpandsBareYear()
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = "2025" };
        Assert.Equal("2025-2026", vm.ExpandedAcademicYearName);
    }

    [Fact]
    public void Step5AcademicYear_ExpandedName_LeavesFullFormUnchanged()
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = "2025-2026" };
        Assert.Equal("2025-2026", vm.ExpandedAcademicYearName);
    }

    [Fact]
    public void Step5AcademicYear_CannotAdvance_WhenNoSemesters()
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = "2025" };
        vm.Semesters.Clear();
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step5AcademicYear_CannotAdvance_WhenNameInvalid()
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = "bad" };
        Assert.False(vm.CanAdvance);
    }

    [Fact]
    public void Step5AcademicYear_CanAdvance_WhenNameValidAndSemestersPresent()
    {
        var vm = new Step5AcademicYearViewModel { AcademicYearName = "2025" };
        // Default semesters (Fall, Winter, Spring) already present
        Assert.True(vm.CanAdvance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 5 (wizard index 6) — Legal Start Times / Block Lengths
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0800", true)]
    [InlineData("2359", true)]
    [InlineData("0000", true)]
    [InlineData("2400", false)]   // hours > 23
    [InlineData("0860", false)]   // minutes > 59
    [InlineData("800",  false)]   // only 3 digits
    [InlineData("08a0", false)]   // non-digit character
    [InlineData("",     false)]   // empty
    public void WizardBlockLength_IsValidMilitaryTime(string input, bool expected)
    {
        var result = WizardBlockLengthEntry.IsValidMilitaryTime(input, out _);
        Assert.Equal(expected, result);
    }

    // ── Time bound checks (0730 earliest, 2200 latest end) ───────────────────

    [Theory]
    [InlineData("0000")]   // midnight
    [InlineData("0600")]   // well before 0730
    [InlineData("0729")]   // one minute before the hard lower bound
    public void WizardBlockLength_AddTime_RejectsStartTimeBefore0730(string hhmm)
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = hhmm;
        entry.AddTimeCommand.Execute(null);
        Assert.NotNull(entry.AddTimeError);
        Assert.Empty(entry.StartTimes);
    }

    [Fact]
    public void WizardBlockLength_AddTime_Accepts0730_AsEarliestValidStart()
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = "0730";
        entry.AddTimeCommand.Execute(null);
        Assert.Null(entry.AddTimeError);
        Assert.Single(entry.StartTimes);
    }

    [Theory]
    [InlineData("1900", 4.0)]   // 1900 + 4h = 2300 — over by 1h
    [InlineData("2100", 2.0)]   // 2100 + 2h = 2300 — over by 1h
    [InlineData("2001", 2.0)]   // 2001 + 2h = 2201 — just over
    public void WizardBlockLength_AddTime_RejectsStartTimeThatExceedsMaxEnd(string hhmm, double blockHours)
    {
        var entry = new WizardBlockLengthEntry(blockHours, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = hhmm;
        entry.AddTimeCommand.Execute(null);
        Assert.NotNull(entry.AddTimeError);
        Assert.Empty(entry.StartTimes);
    }

    [Theory]
    [InlineData("1800", 4.0)]   // 1800 + 4h = 2200 — exactly at the boundary
    [InlineData("2000", 2.0)]   // 2000 + 2h = 2200 — exactly at the boundary
    [InlineData("1930", 0.5)]   // 1930 + 0.5h = 2000 — well within bounds
    public void WizardBlockLength_AddTime_AcceptsStartTimeThatEndsExactlyAt2200OrEarlier(string hhmm, double blockHours)
    {
        var entry = new WizardBlockLengthEntry(blockHours, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = hhmm;
        entry.AddTimeCommand.Execute(null);
        Assert.Null(entry.AddTimeError);
        Assert.Single(entry.StartTimes);
    }

    [Fact]
    public void WizardBlockLength_AddTime_RejectsDuplicate()
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, ["0800"]);
        entry.NewTimeInput = "0800";
        entry.AddTimeCommand.Execute(null);
        Assert.NotNull(entry.AddTimeError);
        Assert.Single(entry.StartTimes); // count unchanged
    }

    [Fact]
    public void WizardBlockLength_AddTime_ClearsInputOnSuccess()
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = "0800";
        entry.AddTimeCommand.Execute(null);
        Assert.Equal(string.Empty, entry.NewTimeInput);
        Assert.Single(entry.StartTimes);
    }

    [Fact]
    public void WizardBlockLength_AddTime_SetsError_WhenInvalidFormat()
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, []);
        entry.NewTimeInput = "800"; // 3 digits — invalid
        entry.AddTimeCommand.Execute(null);
        Assert.NotNull(entry.AddTimeError);
        Assert.Empty(entry.StartTimes);
    }

    [Fact]
    public void WizardBlockLength_GetStartMinutes_ConvertsCorrectly()
    {
        var entry = new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, ["0830", "1300"]);
        var minutes = entry.GetStartMinutes();
        Assert.Equal(510, minutes[0]);   // 8*60+30
        Assert.Equal(780, minutes[1]);   // 13*60+0
    }

    [Fact]
    public void Step5Scheduling_GetSeedData_OmitsBlocksWithNoStartTimes()
    {
        var vm = new Step5SchedulingViewModel();
        vm.BlockLengths.Clear();
        vm.BlockLengths.Add(new WizardBlockLengthEntry(2.0, BlockLengthUnit.Hours, []));     // no times
        vm.BlockLengths.Add(new WizardBlockLengthEntry(3.0, BlockLengthUnit.Hours, ["0900"])); // has a time
        var seed = vm.GetSeedData();
        Assert.Single(seed);
        Assert.Equal(3.0, seed[0].BlockLengthHours);
    }

    [Fact]
    public void Step5Scheduling_AddBlockLength_RejectsZero()
    {
        var vm = new Step5SchedulingViewModel();
        var countBefore = vm.BlockLengths.Count;
        vm.NewBlockLengthInput = "0";
        vm.AddBlockLengthCommand.Execute(null);
        Assert.Equal(countBefore, vm.BlockLengths.Count);
    }

    [Fact]
    public void Step5Scheduling_AddBlockLength_RejectsNegative()
    {
        var vm = new Step5SchedulingViewModel();
        var countBefore = vm.BlockLengths.Count;
        vm.NewBlockLengthInput = "-1";
        vm.AddBlockLengthCommand.Execute(null);
        Assert.Equal(countBefore, vm.BlockLengths.Count);
    }

    [Fact]
    public void Step5Scheduling_AddBlockLength_RejectsDuplicate()
    {
        var vm = new Step5SchedulingViewModel(); // pre-seeded with 1.5 and 3.0 (AppDefaults)
        var countBefore = vm.BlockLengths.Count;
        vm.NewBlockLengthInput = "3.0";   // already present — must be rejected
        vm.AddBlockLengthCommand.Execute(null);
        Assert.Equal(countBefore, vm.BlockLengths.Count);
    }

    [Fact]
    public void Step5Scheduling_AddBlockLength_AcceptsCommaDecimalSeparator()
    {
        var vm = new Step5SchedulingViewModel(); // pre-seeded with 1.5 and 3.0 (AppDefaults)
        var countBefore = vm.BlockLengths.Count;
        vm.NewBlockLengthInput = "2,0";   // comma decimal, not yet present — must be accepted
        vm.AddBlockLengthCommand.Execute(null);
        Assert.Equal(countBefore + 1, vm.BlockLengths.Count);
        Assert.Equal(2.0, vm.BlockLengths.Last().BlockLengthHours);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6 (wizard index 10) — Semester Colors
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step6SemesterColors_LoadFromSemesters_CreatesOneRowPerSemester()
    {
        var vm = new Step6SemesterColorsViewModel();
        var semesters = new[]
        {
            new SemesterDefViewModel { Name = "Fall"   },
            new SemesterDefViewModel { Name = "Winter" },
            new SemesterDefViewModel { Name = "Spring" }
        };
        vm.LoadFromSemesters(semesters);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("Fall",   vm.Rows[0].Name);
        Assert.Equal("Winter", vm.Rows[1].Name);
        Assert.Equal("Spring", vm.Rows[2].Name);
    }

    [Fact]
    public void Step6SemesterColors_LoadFromSemesters_UsesPositionDefaultWhenColorEmpty()
    {
        var vm = new Step6SemesterColorsViewModel();
        vm.LoadFromSemesters([new SemesterDefViewModel { Name = "Fall", Color = string.Empty }]);
        Assert.Equal(Step6SemesterColorsViewModel.DefaultColors[0], vm.Rows[0].HexColor);
    }

    [Fact]
    public void Step6SemesterColors_LoadFromSemesters_PreservesPresetColor()
    {
        var vm = new Step6SemesterColorsViewModel();
        vm.LoadFromSemesters([new SemesterDefViewModel { Name = "Fall", Color = "#FF0000" }]);
        Assert.Equal("#FF0000", vm.Rows[0].HexColor);
    }

    [Fact]
    public void Step6SemesterColors_AcceptDefaults_ResetsAllRowsToPositionColors()
    {
        var vm = new Step6SemesterColorsViewModel();
        vm.LoadFromSemesters([
            new SemesterDefViewModel { Name = "Fall",   Color = "#FF0000" },
            new SemesterDefViewModel { Name = "Spring", Color = "#00FF00" }
        ]);
        vm.AcceptDefaultsCommand.Execute(null);
        Assert.Equal(Step6SemesterColorsViewModel.DefaultColors[0], vm.Rows[0].HexColor);
        Assert.Equal(Step6SemesterColorsViewModel.DefaultColors[1], vm.Rows[1].HexColor);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 10 — Closing panel
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Step10Closing_CanAdvance_IsAlwaysTrue()
    {
        var vm = new Step10ClosingViewModel();
        Assert.True(vm.CanAdvance);
    }
}
