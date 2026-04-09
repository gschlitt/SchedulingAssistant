using Moq;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Wizard;
using SchedulingAssistant.ViewModels.Wizard.Steps;
using System.Text.Json;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// End-to-end data-flow tests for <see cref="StartupWizardViewModel"/>.
///
/// These tests drive the wizard through the import path (0→1a→2→3→4→9→11→Finish)
/// using injected mock repositories, then assert that the correct <c>Insert</c> calls
/// were made with the expected data.
///
/// Infrastructure notes:
/// <list type="bullet">
///   <item>A real <see cref="DatabaseContext"/> is created in a temp directory so that
///     <c>SeedData.SeedWizardLegalStartTimes</c> can execute against a live schema.</item>
///   <item><c>InitializeServices</c> is a no-op — the wizard never builds the DI container.</item>
///   <item><see cref="AppSettings.Current"/> writes to <c>%AppData%\TermPoint\settings.json</c>
///     as a side effect; this is accepted for now.</item>
/// </list>
/// </summary>
public class WizardDataFlowTests : IDisposable
{
    // ─────────────────────────────────────────────────────────────────────────
    // Per-test temp directory
    // ─────────────────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    public WizardDataFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WizardTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a <see cref="TpConfigData"/> to a temp file and returns its path.
    /// </summary>
    private string WriteTpConfig(TpConfigData data)
    {
        var path = Path.Combine(_tempDir, "test.tpconfig");
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>
    /// Creates a <see cref="WizardServices"/> wired to the supplied mocks plus a real
    /// <see cref="DatabaseContext"/> for the seed-data SQL calls.
    /// </summary>
    private WizardServices BuildServices(
        Mock<IAcademicUnitRepository>    mockAu,
        Mock<IAcademicYearRepository>    mockAy,
        Mock<ISemesterRepository>        mockSem,
        Mock<ICampusRepository>          mockCampus,
        Mock<ISectionPrefixRepository>   mockPrefix,
        Mock<IBlockPatternRepository>    mockPattern)
    {
        // Real in-file DB so SeedData SQL commands have a live schema to write to.
        var dbPath    = Path.Combine(_tempDir, "seed.db");
        var dbContext = new DatabaseContext(dbPath);

        // AcademicYearRepository.GetAll() and SemesterRepository.GetAll() are called
        // by SemesterContext.Reload at the end of WriteDbRecordsAsync.
        mockAy.Setup(r => r.GetAll()).Returns(new List<AcademicYear>());
        mockSem.Setup(r => r.GetAll()).Returns(new List<Semester>());
        mockAu.Setup(r => r.GetAll()).Returns(new List<AcademicUnit>());

        return new WizardServices(
            InitializeServices:  _ => { /* no-op */ },
            AcademicUnits:       () => mockAu.Object,
            AcademicYears:       () => mockAy.Object,
            Semesters:           () => mockSem.Object,
            Campuses:            () => mockCampus.Object,
            SectionPrefixes:     () => mockPrefix.Object,
            BlockPatterns:       () => mockPattern.Object,
            Database:            () => dbContext,
            SemesterContext:     () => new SemesterContext(),
            // Import path never navigates to the manual-config steps; these will not be invoked.
            CampusListVm:        () => throw new NotSupportedException("Not used on import path"),
            BlockPatternListVm:  () => throw new NotSupportedException("Not used on import path"),
            SectionPrefixListVm: () => throw new NotSupportedException("Not used on import path")
        );
    }

    /// <summary>
    /// Drives the wizard along the import path up to and including Finish, using the
    /// provided <see cref="TpConfigData"/> and academic year name.
    /// </summary>
    private async Task RunImportPathAsync(
        WizardServices services,
        string tpConfigPath,
        string academicYearName = "2025")
    {
        var vm = new StartupWizardViewModel(null!, services);

        // Step 0 — Welcome (always CanAdvance)
        await vm.NextCommand.ExecuteAsync(null);

        // Step 1 — License agreement (always CanAdvance)
        Assert.IsType<StepLicenseViewModel>(vm.CurrentStep);
        await vm.NextCommand.ExecuteAsync(null);

        // Step 2 — choose new setup (not existing DB)
        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsNewSetupChoice = true;
        await vm.NextCommand.ExecuteAsync(null);

        // Step 3 — Institution
        var step3 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step3.InstitutionName   = "Test University";
        step3.InstitutionAbbrev = "TU";
        step3.AcUnitName        = "Engineering";
        step3.AcUnitAbbrev      = "ENG";
        await vm.NextCommand.ExecuteAsync(null);

        // Step 4 — Database location
        // Use the temp dir for both DB folder and backup to avoid creating real paths.
        var step4 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step4.DbFolder     = _tempDir;
        step4.DbFilename   = $"wizard_{Guid.NewGuid():N}.db";
        step4.BackupFolder = _tempDir;
        await vm.NextCommand.ExecuteAsync(null); // triggers ValidateStep3 (InitializeServices no-op)

        // Step 5 — TpConfig: choose Import and provide path
        var step5 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);
        step5.IsImportChoice = true;
        step5.TpConfigPath   = tpConfigPath;
        await vm.NextCommand.ExecuteAsync(null); // ValidateAndImport reads the file; skips to step 10

        // Step 10 — Academic Year
        var step10 = Assert.IsType<Step5AcademicYearViewModel>(vm.CurrentStep);
        step10.AcademicYearName = academicYearName;
        await vm.NextCommand.ExecuteAsync(null); // skips step 11 (import path); goes to step 12

        // Step 12 — Closing panel; Finish calls WriteDbRecordsAsync
        Assert.IsType<Step10ClosingViewModel>(vm.CurrentStep);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.True(vm.IsComplete);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Academic year
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_InsertsAcademicYear_WithExpandedName()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall", Color = "#C65D1E" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig), academicYearName: "2025");

        // Bare year "2025" must expand to "2025-2026" before insert
        mockAy.Verify(r => r.Insert(It.Is<AcademicYear>(ay => ay.Name == "2025-2026")), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Institution data
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_WritesAcademicUnitName_FromInstitutionStep()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        // When GetAll returns empty, the AU is inserted (not updated)
        mockAu.Setup(r => r.GetAll()).Returns(new List<AcademicUnit>());

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        // Override the step 2 values by running the path with custom fields;
        // helper sets InstitutionName="Test University", AcUnitName="Engineering".
        var tpConfig = new TpConfigData
        {
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        mockAu.Verify(r => r.Insert(It.Is<AcademicUnit>(au => au.Name == "Engineering")), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Campuses (import path)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_InsertsCampuses_FromTpConfig()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            Campuses     = ["Main", "Satellite"],
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        mockCampus.Verify(r => r.Insert(It.Is<Campus>(c => c.Name == "Main")),      Times.Once);
        mockCampus.Verify(r => r.Insert(It.Is<Campus>(c => c.Name == "Satellite")), Times.Once);
    }

    [Fact]
    public async Task ImportPath_InsertsCampuses_InOrder_WithSortOrders()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var inserted = new List<Campus>();
        mockCampus.Setup(r => r.Insert(It.IsAny<Campus>()))
                  .Callback<Campus>(c => inserted.Add(c));

        var tpConfig = new TpConfigData
        {
            Campuses     = ["Alpha", "Beta", "Gamma"],
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        Assert.Equal(3, inserted.Count);
        Assert.Equal("Alpha", inserted[0].Name);
        Assert.Equal("Beta",  inserted[1].Name);
        Assert.Equal("Gamma", inserted[2].Name);
        Assert.Equal(0, inserted[0].SortOrder);
        Assert.Equal(1, inserted[1].SortOrder);
        Assert.Equal(2, inserted[2].SortOrder);
    }

    [Fact]
    public async Task ImportPath_SkipsBlankCampusNames()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            Campuses     = ["Main", "", "   ", "Satellite"],
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        // Only "Main" and "Satellite" should be inserted — blanks skipped
        mockCampus.Verify(r => r.Insert(It.IsAny<Campus>()), Times.Exactly(2));
        mockCampus.Verify(r => r.Insert(It.Is<Campus>(c => c.Name == "Main")),      Times.Once);
        mockCampus.Verify(r => r.Insert(It.Is<Campus>(c => c.Name == "Satellite")), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section prefixes (import path)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_InsertsSectionPrefix_WithResolvedCampusId()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        // Capture the campus ID assigned at insert time
        string? insertedCampusId = null;
        mockCampus.Setup(r => r.Insert(It.Is<Campus>(c => c.Name == "Main")))
                  .Callback<Campus>(c => insertedCampusId = c.Id);

        SectionPrefix? insertedPrefix = null;
        mockPrefix.Setup(r => r.Insert(It.IsAny<SectionPrefix>()))
                  .Callback<SectionPrefix>(p => insertedPrefix = p);

        var tpConfig = new TpConfigData
        {
            Campuses        = ["Main"],
            SectionPrefixes = [new TpConfigSectionPrefix { Prefix = "AB", CampusName = "Main" }],
            SemesterDefs    = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        Assert.NotNull(insertedPrefix);
        Assert.Equal("AB", insertedPrefix!.Prefix);
        Assert.NotNull(insertedCampusId);
        Assert.Equal(insertedCampusId, insertedPrefix.CampusId);
    }

    [Fact]
    public async Task ImportPath_InsertsSectionPrefix_WithNullCampusId_WhenCampusNameUnknown()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        SectionPrefix? insertedPrefix = null;
        mockPrefix.Setup(r => r.Insert(It.IsAny<SectionPrefix>()))
                  .Callback<SectionPrefix>(p => insertedPrefix = p);

        var tpConfig = new TpConfigData
        {
            Campuses        = [],   // no campuses defined
            SectionPrefixes = [new TpConfigSectionPrefix { Prefix = "AB", CampusName = "Unknown" }],
            SemesterDefs    = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        Assert.NotNull(insertedPrefix);
        Assert.Null(insertedPrefix!.CampusId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Block patterns (import path)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_InsertsBlockPatterns_FromTpConfig()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            BlockPatterns = [
                new TpConfigBlockPattern { Name = "MWF", Days = [1, 3, 5] },
                new TpConfigBlockPattern { Name = "TR",  Days = [2, 4]    }
            ],
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        mockPattern.Verify(
            r => r.Insert(It.Is<BlockPattern>(bp => bp.Name == "MWF" && bp.Days.SequenceEqual(new[] { 1, 3, 5 }))),
            Times.Once);
        mockPattern.Verify(
            r => r.Insert(It.Is<BlockPattern>(bp => bp.Name == "TR" && bp.Days.SequenceEqual(new[] { 2, 4 }))),
            Times.Once);
    }

    [Fact]
    public async Task ImportPath_SkipsBlankBlockPatternNames()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            BlockPatterns = [
                new TpConfigBlockPattern { Name = "MWF", Days = [1, 3, 5] },
                new TpConfigBlockPattern { Name = "",    Days = [2, 4]    }   // blank name
            ],
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        mockPattern.Verify(r => r.Insert(It.IsAny<BlockPattern>()), Times.Once); // only MWF
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Semesters (import path)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportPath_InsertsSemesters_WithColorsFromTpConfig()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var tpConfig = new TpConfigData
        {
            SemesterDefs = [
                new TpConfigSemesterDef { Name = "Fall",   Color = "#C65D1E" },
                new TpConfigSemesterDef { Name = "Spring", Color = "#7ED957" }
            ]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        mockSem.Verify(
            r => r.Insert(It.Is<Semester>(s => s.Name == "Fall" && s.Color == "#C65D1E")),
            Times.Once);
        mockSem.Verify(
            r => r.Insert(It.Is<Semester>(s => s.Name == "Spring" && s.Color == "#7ED957")),
            Times.Once);
    }

    [Fact]
    public async Task ImportPath_InsertsSemesters_WithCorrectSortOrder()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        var inserted = new List<Semester>();
        mockSem.Setup(r => r.Insert(It.IsAny<Semester>()))
               .Callback<Semester>(s => inserted.Add(s));

        var tpConfig = new TpConfigData
        {
            SemesterDefs = [
                new TpConfigSemesterDef { Name = "Fall"   },
                new TpConfigSemesterDef { Name = "Winter" },
                new TpConfigSemesterDef { Name = "Spring" }
            ]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        // Filter out any calls from SemesterContext.Reload's underlying setup
        var wizardInserts = inserted.Where(s => s.Name is "Fall" or "Winter" or "Spring").ToList();
        Assert.Equal(3, wizardInserts.Count);
        Assert.Equal(0, wizardInserts.First(s => s.Name == "Fall").SortOrder);
        Assert.Equal(1, wizardInserts.First(s => s.Name == "Winter").SortOrder);
        Assert.Equal(2, wizardInserts.First(s => s.Name == "Spring").SortOrder);
    }

    [Fact]
    public async Task ImportPath_SemestersLinkedToInsertedAcademicYear()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var services = BuildServices(mockAu, mockAy, mockSem, mockCampus, mockPrefix, mockPattern);

        // Capture the AY ID assigned at insert
        string? insertedAyId = null;
        mockAy.Setup(r => r.Insert(It.IsAny<AcademicYear>()))
              .Callback<AcademicYear>(ay => insertedAyId = ay.Id);

        var inserted = new List<Semester>();
        mockSem.Setup(r => r.Insert(It.IsAny<Semester>()))
               .Callback<Semester>(s => inserted.Add(s));

        var tpConfig = new TpConfigData
        {
            SemesterDefs = [new TpConfigSemesterDef { Name = "Fall" }]
        };

        await RunImportPathAsync(services, WriteTpConfig(tpConfig));

        Assert.NotNull(insertedAyId);
        Assert.All(inserted, s => Assert.Equal(insertedAyId, s.AcademicYearId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExitNow path — no DB writes at all
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExitNowPath_NoRepoInsertsCalled()
    {
        var mockAu      = new Mock<IAcademicUnitRepository>();
        var mockAy      = new Mock<IAcademicYearRepository>();
        var mockSem     = new Mock<ISemesterRepository>();
        var mockCampus  = new Mock<ICampusRepository>();
        var mockPrefix  = new Mock<ISectionPrefixRepository>();
        var mockPattern = new Mock<IBlockPatternRepository>();

        var dbPath    = Path.Combine(_tempDir, "seed_exitnow.db");
        var dbContext = new DatabaseContext(dbPath);
        mockAy.Setup(r => r.GetAll()).Returns(new List<AcademicYear>());
        mockSem.Setup(r => r.GetAll()).Returns(new List<Semester>());
        mockAu.Setup(r => r.GetAll()).Returns(new List<AcademicUnit>());

        var services = new WizardServices(
            InitializeServices:  _ => { },
            AcademicUnits:       () => mockAu.Object,
            AcademicYears:       () => mockAy.Object,
            Semesters:           () => mockSem.Object,
            Campuses:            () => mockCampus.Object,
            SectionPrefixes:     () => mockPrefix.Object,
            BlockPatterns:       () => mockPattern.Object,
            Database:            () => dbContext,
            SemesterContext:     () => new SemesterContext(),
            CampusListVm:        () => throw new NotSupportedException("Not used on ExitNow path"),
            BlockPatternListVm:  () => throw new NotSupportedException("Not used on ExitNow path"),
            SectionPrefixListVm: () => throw new NotSupportedException("Not used on ExitNow path")
        );

        var vm = new StartupWizardViewModel(null!, services);

        // Navigate to step 5 (TpConfig) and choose ExitNow
        await vm.NextCommand.ExecuteAsync(null); // → step 1 (license)

        Assert.IsType<StepLicenseViewModel>(vm.CurrentStep);
        await vm.NextCommand.ExecuteAsync(null); // → step 2 (existing DB check)

        var step2a = Assert.IsType<Step1aExistingDbViewModel>(vm.CurrentStep);
        step2a.IsNewSetupChoice = true;
        await vm.NextCommand.ExecuteAsync(null); // → step 3 (institution)

        var step3 = Assert.IsType<Step1InstitutionViewModel>(vm.CurrentStep);
        step3.InstitutionName   = "Test U";
        step3.InstitutionAbbrev = "TU";
        step3.AcUnitName        = "Eng";
        step3.AcUnitAbbrev      = "ENG";
        await vm.NextCommand.ExecuteAsync(null); // → step 4 (database)

        var step4 = Assert.IsType<Step2DatabaseViewModel>(vm.CurrentStep);
        step4.DbFolder     = _tempDir;
        step4.DbFilename   = $"exitnow_{Guid.NewGuid():N}.db";
        step4.BackupFolder = _tempDir;
        await vm.NextCommand.ExecuteAsync(null); // → step 5 (TpConfig)

        var step5 = Assert.IsType<Step3TpConfigViewModel>(vm.CurrentStep);
        step5.IsExitNowChoice = true;
        await vm.NextCommand.ExecuteAsync(null); // → FinishAsync (ExitNow branch)

        Assert.True(vm.IsComplete);

        // No repository writes should have occurred
        mockAu.Verify(r => r.Insert(It.IsAny<AcademicUnit>()), Times.Never);
        mockAy.Verify(r => r.Insert(It.IsAny<AcademicYear>()), Times.Never);
        mockSem.Verify(r => r.Insert(It.IsAny<Semester>()),    Times.Never);
        mockCampus.Verify(r => r.Insert(It.IsAny<Campus>()),   Times.Never);
    }
}
