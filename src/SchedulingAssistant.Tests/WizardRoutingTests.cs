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
/// These tests do not exercise the full finish flow (which writes to disk and
/// builds the DI container). Tests that would cross the database-creation boundary
/// (steps 3 and 1a in the "Yes" branch) are omitted here and covered by
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
    public async Task Next_FromStep0_NavigatesToStep1a()
    {
        var vm = CreateWizard(); // Step 0 — Welcome (CanAdvance always true)
        await vm.NextCommand.ExecuteAsync(null);
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
        await vm.NextCommand.ExecuteAsync(null); // advance to step 1a

        // Step 1a defaults: HasExistingDb=false (new setup) — CanAdvance=true.
        // Now switch to ExistingDb choice without providing a path → CanAdvance=false.
        var step1a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step1a.IsExistingDbChoice = true;
        step1a.DbPath = string.Empty;

        await vm.NextCommand.ExecuteAsync(null); // should NOT advance
        Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep); // still on step 1a
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Back navigation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Back_FromStep1a_ReturnsToStep0()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1a
        vm.BackCommand.Execute(null);
        Assert.IsType<Step0WelcomeViewModel>(vm.CurrentStep);
    }

    [Fact]
    public async Task Back_FromStep1a_ClearsCanGoBack()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null);
        vm.BackCommand.Execute(null);
        Assert.False(vm.CanGoBack);
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
        await vm.NextCommand.ExecuteAsync(null); // → step 1a
        var step1aFirst = vm.CurrentStep;

        vm.BackCommand.Execute(null);            // back to step 0
        await vm.NextCommand.ExecuteAsync(null); // → step 1a again

        Assert.Same(step1aFirst, vm.CurrentStep);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NextButtonText driven by step-1a choice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NextButtonText_IsFinish_WhenStep1aExistingDbChoiceSelected()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1a

        var step1a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step1a.IsExistingDbChoice = true;

        Assert.Equal("Finish", vm.NextButtonText);
    }

    [Fact]
    public async Task NextButtonText_IsNext_WhenStep1aNewSetupChoiceSelected()
    {
        var vm = CreateWizard();
        await vm.NextCommand.ExecuteAsync(null); // → step 1a

        var step1a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step1a.IsNewSetupChoice = true;

        Assert.Equal("Next", vm.NextButtonText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NextButtonText driven by step-4 (TpConfig) ExitNow choice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NextButtonText_IsFinish_WhenStep4ExitNowChoiceSelected()
    {
        var vm = CreateWizard();

        // Navigate: 0 → 1a (new setup) → 2 (institution) …
        // Step 1a: choose new setup
        await vm.NextCommand.ExecuteAsync(null);
        var step1a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step1a.IsNewSetupChoice = true;

        // Step 1a → step 2 (Institution)
        await vm.NextCommand.ExecuteAsync(null);
        var step2 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step2.InstitutionName   = "Test U";
        step2.InstitutionAbbrev = "TU";
        step2.AcUnitName        = "Engineering";
        step2.AcUnitAbbrev      = "ENG";

        // Step 2 → step 3 (Database)
        await vm.NextCommand.ExecuteAsync(null);
        var step3 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step3.DbFolder     = Path.GetTempPath();
        step3.DbFilename   = $"test_wizard_{Guid.NewGuid():N}.db";
        step3.BackupFolder = Path.GetTempPath();

        // Step 3 → step 4 (TpConfig). ValidateStep3 calls InitializeServices (no-op)
        // and Directory.CreateDirectory (temp path already exists) — safe.
        await vm.NextCommand.ExecuteAsync(null);
        var step4 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);

        // Now choose ExitNow
        step4.IsExitNowChoice = true;

        Assert.Equal("Finish", vm.NextButtonText);
    }

    [Fact]
    public async Task NextButtonText_IsNext_WhenStep4ManualChoiceSelected()
    {
        var vm = CreateWizard();

        await vm.NextCommand.ExecuteAsync(null);
        var step1a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step1a.IsNewSetupChoice = true;

        await vm.NextCommand.ExecuteAsync(null);
        var step2 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step2.InstitutionName   = "Test U";
        step2.InstitutionAbbrev = "TU";
        step2.AcUnitName        = "Engineering";
        step2.AcUnitAbbrev      = "ENG";

        await vm.NextCommand.ExecuteAsync(null);
        var step3 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step3.DbFolder     = Path.GetTempPath();
        step3.DbFilename   = $"test_wizard_{Guid.NewGuid():N}.db";
        step3.BackupFolder = Path.GetTempPath();

        await vm.NextCommand.ExecuteAsync(null);
        var step4 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);
        step4.IsManualChoice = true;

        Assert.Equal("Next", vm.NextButtonText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Institution commit invalidates step-3 cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstitutionCommit_InvalidatesStep3Cache_WhenAbbrevChanges()
    {
        var vm = CreateWizard();

        // Navigate to step 2 (Institution)
        await vm.NextCommand.ExecuteAsync(null); // → step 1a
        Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);

        await vm.NextCommand.ExecuteAsync(null); // → step 2 institution
        var step2 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step2.InstitutionName   = "Test U";
        step2.InstitutionAbbrev = "TU";
        step2.AcUnitName        = "Engineering";
        step2.AcUnitAbbrev      = "ENG";

        // Advance to step 3 (Database) — this commits step 2 into _acUnitAbbrev
        await vm.NextCommand.ExecuteAsync(null);
        var step3First = vm.CurrentStep;
        Assert.IsType<Step2DatabaseViewModel>(step3First);

        // Go back to step 2
        vm.BackCommand.Execute(null);

        // Change abbreviation — should invalidate step 3 cache
        step2.AcUnitAbbrev = "CS";

        // Go forward again — should get a NEW step 3 instance
        await vm.NextCommand.ExecuteAsync(null);
        var step3Second = vm.CurrentStep;

        Assert.NotSame(step3First, step3Second);
    }
}
