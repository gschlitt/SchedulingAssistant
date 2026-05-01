using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.ViewModels.Wizard;

/// <summary>
/// All external dependencies required by <see cref="StartupWizardViewModel"/> during
/// its finish flow. Each dependency is a factory delegate rather than a direct instance
/// because the DI container is not built until the user reaches the database-creation
/// step mid-wizard. The factories are invoked lazily inside
/// <see cref="StartupWizardViewModel.WriteDbRecordsAsync"/> and the validation methods,
/// after <see cref="InitializeServices"/> has been called.
///
/// Use <see cref="FromApp"/> to create the production instance. In tests, supply
/// factory delegates that return mocks or test doubles.
/// </summary>
/// <param name="InitializeServices">
///   Replacement for <c>App.InitializeServices(dbPath)</c>. Called when the user
///   confirms the database path. In production this builds the DI container; in tests
///   it can be a no-op.
/// </param>
/// <param name="AcademicUnits">Factory for <see cref="IAcademicUnitRepository"/>.</param>
/// <param name="AcademicYears">Factory for <see cref="IAcademicYearRepository"/>.</param>
/// <param name="Semesters">Factory for <see cref="ISemesterRepository"/>.</param>
/// <param name="Campuses">Factory for <see cref="ICampusRepository"/>.</param>
/// <param name="BlockPatterns">Factory for <see cref="IBlockPatternRepository"/>.</param>
/// <param name="Database">Factory for <see cref="IDatabaseContext"/>.</param>
/// <param name="SemesterContext">Factory for <see cref="Services.SemesterContext"/>.</param>
/// <param name="CampusListVm">
///   Factory for the <see cref="CampusListViewModel"/> embedded in the manual-path
///   Campuses wizard step. Resolved after <see cref="InitializeServices"/> has run.
/// </param>
/// <param name="BlockPatternListVm">
///   Factory for the <see cref="BlockPatternListViewModel"/> embedded in the manual-path
///   Block Patterns wizard step.
/// </param>
public record WizardServices(
    Action<string>                      InitializeServices,
    Func<IAcademicUnitRepository>       AcademicUnits,
    Func<IAcademicYearRepository>       AcademicYears,
    Func<ISemesterRepository>           Semesters,
    Func<ICampusRepository>             Campuses,
    Func<IBlockPatternRepository>       BlockPatterns,
    Func<IDatabaseContext>              Database,
    Func<SemesterContext>               SemesterContext,
    Func<CampusListViewModel>           CampusListVm,
    Func<BlockPatternListViewModel>     BlockPatternListVm)
{
    /// <summary>
    /// Creates a <see cref="WizardServices"/> instance wired to the live application
    /// DI container. Each factory resolves its service from <c>App.Services</c> on demand,
    /// so this method is safe to call before <c>App.InitializeServices</c> has run.
    /// </summary>
    public static WizardServices FromApp() => new(
        InitializeServices: path => App.InitializeServices(path),
        AcademicUnits:      () => App.Services.GetRequiredService<IAcademicUnitRepository>(),
        AcademicYears:      () => App.Services.GetRequiredService<IAcademicYearRepository>(),
        Semesters:          () => App.Services.GetRequiredService<ISemesterRepository>(),
        Campuses:           () => App.Services.GetRequiredService<ICampusRepository>(),
        BlockPatterns:      () => App.Services.GetRequiredService<IBlockPatternRepository>(),
        Database:           () => App.Services.GetRequiredService<IDatabaseContext>(),
        SemesterContext:    () => App.Services.GetRequiredService<SemesterContext>(),
        CampusListVm:       () => App.Services.GetRequiredService<CampusListViewModel>(),
        BlockPatternListVm: () => App.Services.GetRequiredService<BlockPatternListViewModel>()
    );
}
