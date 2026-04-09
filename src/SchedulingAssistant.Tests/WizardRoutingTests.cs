using Moq;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Wizard;
using SchedulingAssistant.ViewModels.Wizard.Steps;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for <see cref="StartupWizardViewModel"/> navigation, routing logic,
/// step caching, and button-label state.
///
/// Current step sequence (indices 0–12):
///   0  Step0WelcomeViewModel
///   1  StepLicenseViewModel           ← license agreement
///   2  Step1aExistingDbViewModel       ← existing-DB vs new-setup choice
///   3  Step1InstitutionViewModel
///   4  Step2DatabaseViewModel
///   5  Step3TpConfigViewModel          ← route choice (import / manual / exit-now)
///   6  Step4CampusesViewModel          ↘ manual path only
///   7  Step5SchedulingViewModel        ↘
///   8  Step6BlockPatternsViewModel     ↘
///   9  Step7SectionPrefixesViewModel   ↘
///  10  Step5AcademicYearViewModel
///  11  Step6SemesterColorsViewModel    (skipped on import path)
///  12  Step10ClosingViewModel
///
/// These tests do not exercise the full finish flow (which writes to disk and
/// builds the DI container). Tests that would cross the database-creation boundary
/// (step 4 / step 2 in the "Yes" branch) are omitted here and covered by
/// WizardDataFlowTests, which use a real temp directory.
/// </summary>
public class WizardRoutingTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WizardServices"/> with no-op InitializeServices and
    /// mock repositories whose GetAll() calls return empty lists.
    /// Suitable for routing tests that never reach WriteDbRecordsAsync.
    /// </summary>
    private static WizardServices BuildTestServices()
    {
        var mockAy  = new Mock<IAcademicYearRepository>();
        var mockSem = new Mock<ISemesterRepository>();
        mockAy.Setup(r => r.GetAll()).Returns(new List<AcademicYear>());
        mockSem.Setup(r => r.GetAll()).Returns(new List<Semester>());

        return new WizardServices(
            InitializeServices:  _ => { /* no-op in tests */ },
            AcademicUnits:       () => new Mock<IAcademicUnitRepository>().Object,
            AcademicYears:       () => mockAy.Object,
            Semesters:           () => mockSem.Object,
            Campuses:            () => new Mock<ICampusRepository>().Object,
            SectionPrefixes:     () => new Mock<ISectionPrefixRepository>().Object,
            BlockPatterns:       () => new Mock<IBlockPatternRepository>().Object,
            Database:            () => new Mock<IDatabaseContext>().Object,
            SemesterContext:     () => new SemesterContext(),
            // Routing tests never reach manual-config steps; these will not be invoked.
            CampusListVm:        () => throw new NotSupportedException("Not used in routing tests"),
            BlockPatternListVm:  () => throw new NotSupportedException("Not used in routing tests"),
            SectionPrefixListVm: () => throw new NotSupportedException("Not used in routing tests")
        );
    }

    /// <summary>Creates a wizard VM in its initial state with test services.</summary>
    private static StartupWizardViewModel CreateWizard() =>
        new(null!, BuildTestServices());

    // ─────────────────────────────────────────────────────────────────────────
    // Initial state
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_CurrentStep_IsStep0Welcome()
    {
        var vm = CreateWizard();
        Assert.IsType<Step0WelcomeViewModel>(vm.CurrentStep);
    }

    [Fact]
    public void InitialState_CanGoBack_IsFalse()
    {
        var vm = CreateWizard();
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void InitialState_NextButtonText_IsNext()
    {
        var vm = CreateWizard();
        Assert.Equal("Next", vm.NextButtonText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Forward navigation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Next_FromStep0_NavigatesToLicense()
    {
        var vm = CreateWizard(); // Step 0 — Welcome (CanAdvance always true)
        await vm.NextCommand.ExecuteAsync(null);
        Assert.IsType<StepLicenseViewModel>(vm.CurrentStep);
    }

    [Fact]
    public async Task Next_FromLicense_NavigatesToStep2a()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)
        Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
    }

    [Fact]
    public async Task Next_FromStep0_EnablesCanGoBack()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.CanGoBack);
    }

    [Fact]
    public async Task Next_Blocked_WhenCurrentStepCannotAdvance()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)

        // Step 2 defaults: new-setup choice — CanAdvance=true.
        // Switch to ExistingDb choice without providing a path → CanAdvance=false.
        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsExistingDbChoice = true;
        step2a.DbPath = string.Empty;

        await vm.NextCommand.ExecuteAsync(null); // should NOT advance
        Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep); // still on step 2
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Back navigation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Back_FromLicense_ReturnsToStep0()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        vm.BackCommand.Execute(null);
        Assert.IsType<Step0WelcomeViewModel>(vm.CurrentStep);
    }

    [Fact]
    public async Task Back_FromLicense_ClearsCanGoBack()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        vm.BackCommand.Execute(null);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public async Task Back_FromStep2a_ReturnsToLicense()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)
        vm.BackCommand.Execute(null);
        Assert.IsType<StepLicenseViewModel>(vm.CurrentStep);
    }

    [Fact]
    public void Back_AtStep0_DoesNothing()
    {
        var vm = CreateWizard();
        vm.BackCommand.Execute(null); // should not throw or navigate below 0
        Assert.IsType<Step0WelcomeViewModel>(vm.CurrentStep);
        Assert.False(vm.CanGoBack);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step caching — going back then forward reuses the same VM instance
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepCache_ForwardBackForward_ReturnsSameVmInstance()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        var licenseFirst = vm.CurrentStep;

        vm.BackCommand.Execute(null);            // back to step 0
        await vm.NextCommand.ExecuteAsync(null); // → step 1 again

        Assert.Same(licenseFirst, vm.CurrentStep);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NextButtonText driven by step-2a choice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NextButtonText_IsFinish_WhenStep2aExistingDbChoiceSelected()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)

        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsExistingDbChoice = true;

        Assert.Equal("Finish", vm.NextButtonText);
    }

    [Fact]
    public async Task NextButtonText_IsNext_WhenStep2aNewSetupChoiceSelected()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)

        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsNewSetupChoice = true;

        Assert.Equal("Next", vm.NextButtonText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NextButtonText driven by step-5 (TpConfig) ExitNow choice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NextButtonText_IsFinish_WhenStep5ExitNowChoiceSelected()
    {
        var vm = CreateWizard();

        // 0 → 1 (license) → 2 (existing-DB check) → 3 (institution) → 4 (database) → 5 (TpConfig)
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)

        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)
        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsNewSetupChoice = true;

        await vm.NextCommand.ExecuteAsync(null); // → step 3 (institution)
        var step3 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step3.InstitutionName   = "Test U";
        step3.InstitutionAbbrev = "TU";
        step3.AcUnitName        = "Engineering";
        step3.AcUnitAbbrev      = "ENG";

        await vm.NextCommand.ExecuteAsync(null); // → step 4 (database)
        var step4 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step4.DbFolder     = Path.GetTempPath();
        step4.DbFilename   = $"test_wizard_{Guid.NewGuid():N}.db";
        step4.BackupFolder = Path.GetTempPath();

        // ValidateStep3 calls InitializeServices (no-op here) + CreateDirectory (temp already exists)
        await vm.NextCommand.ExecuteAsync(null); // → step 5 (TpConfig)
        var step5 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);
        step5.IsExitNowChoice = true;

        Assert.Equal("Finish", vm.NextButtonText);
    }

    [Fact]
    public async Task NextButtonText_IsNext_WhenStep5ManualChoiceSelected()
    {
        var vm = CreateWizard();

        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)

        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)
        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsNewSetupChoice = true;

        await vm.NextCommand.ExecuteAsync(null); // → step 3 (institution)
        var step3 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step3.InstitutionName   = "Test U";
        step3.InstitutionAbbrev = "TU";
        step3.AcUnitName        = "Engineering";
        step3.AcUnitAbbrev      = "ENG";

        await vm.NextCommand.ExecuteAsync(null); // → step 4 (database)
        var step4 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step4.DbFolder     = Path.GetTempPath();
        step4.DbFilename   = $"test_wizard_{Guid.NewGuid():N}.db";
        step4.BackupFolder = Path.GetTempPath();

        await vm.NextCommand.ExecuteAsync(null); // → step 5 (TpConfig)
        var step5 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);
        step5.IsManualChoice = true;

        Assert.Equal("Next", vm.NextButtonText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Institution commit invalidates step-4 cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstitutionCommit_InvalidatesStep4Cache_WhenAbbrevChanges()
    {
        var vm = CreateWizard();

        // Navigate to step 3 (Institution)
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)

        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing-DB check)
        Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);

        await vm.NextCommand.ExecuteAsync(null); // → step 3 (institution)
        var step3 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step3.InstitutionName   = "Test U";
        step3.InstitutionAbbrev = "TU";
        step3.AcUnitName        = "Engineering";
        step3.AcUnitAbbrev      = "ENG";

        // Advance to step 4 (Database) — this commits step 3 into _acUnitAbbrev
        await vm.NextCommand.ExecuteAsync(null);
        var step4First = vm.CurrentStep;
        Assert.IsType<Step2DatabaseViewModel>(step4First);

        // Go back to step 3 (institution)
        vm.BackCommand.Execute(null);

        // Change abbreviation — should invalidate step 4 cache
        step3.AcUnitAbbrev = "CS";

        // Go forward again — should get a NEW step 4 instance
        await vm.NextCommand.ExecuteAsync(null);
        var step4Second = vm.CurrentStep;

        Assert.NotSame(step4First, step4Second);
    }
}
