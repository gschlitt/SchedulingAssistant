using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if !BROWSER
using HotAvalonia;
#endif
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Data.Repositories.Demo;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.Management;
using System;
using System.IO;
using System.Linq;

namespace SchedulingAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Logger available app-wide, including before DI is fully initialized.
    /// Set early in InitializeServices so it can be used during startup error handling.
    /// </summary>
    public static IAppLogger Logger { get; private set; } = new FileAppLogger();

    public override void Initialize()
    {
#if !BROWSER
        this.UseHotReload();
#endif
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Desktop path — show MainWindow hidden; it will run async startup in OnOpened,
            // initialize the DB, then make itself visible.
            var win = new MainWindow();
            win.IsVisible = false;
            desktop.MainWindow = win;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Browser (WASM) path — no DB picker, no splash screen.
            // Wire MainView directly with the pre-built demo DI container.
            // See InitializeDemoServices() and ConfigureDemoServices() below.
            var vm = InitializeDemoServices();
            singleView.MainView = new MainView { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

#if !BROWSER
    /// <summary>
    /// Called by MainWindow once a database path has been resolved.
    /// Builds the full DI container (SQLite-backed) and returns the root view model.
    /// </summary>
    /// <param name="dbPath">Absolute path to the SQLite database file.</param>
    /// <returns>The singleton <see cref="MainWindowViewModel"/> for the new container.</returns>
    public static MainWindowViewModel InitializeServices(string dbPath)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, dbPath);

        // Dispose the previous container before replacing it. This triggers Dispose() on every
        // IDisposable singleton — most importantly DatabaseContext, which closes the SqliteConnection
        // and releases the file lock on the old database. Safe on first call: Services is null!,
        // and (null as IDisposable) is null, so ?.Dispose() is a no-op.
        (Services as IDisposable)?.Dispose();

        Services = services.BuildServiceProvider();

        Logger = Services.GetRequiredService<IAppLogger>();
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();

        // Acquire the write lock before touching the database file.
        Services.GetRequiredService<WriteLockService>().TryAcquire(dbPath);

        // Eagerly initialize the database (schema creation + seeding).
        Services.GetRequiredService<IDatabaseContext>();

        // Seed the global semester context from the database.
        var startupSettings = AppSettings.Current;
        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>(),
            restoreAcademicYearId: startupSettings.LastSelectedAcademicYearId,
            restoreSemesterIds:    startupSettings.LastSelectedSemesterIds.Count > 0
                                       ? startupSettings.LastSelectedSemesterIds.ToHashSet()
                                       : null);

        // Seed the section store so ViewModels can read from the cache on first load.
        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<ISectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(dbPath));
        return vm;
    }
#endif // !BROWSER

    /// <summary>
    /// Browser (WASM) entry point — builds a DI container backed by static demo data
    /// instead of SQLite, seeds the semester context and section store, and returns
    /// the root view model ready to bind directly to <see cref="MainView"/>.
    /// No file I/O, no DB picker, no splash screen.
    /// </summary>
    /// <returns>The singleton <see cref="MainWindowViewModel"/> for the demo container.</returns>
    public static MainWindowViewModel InitializeDemoServices()
    {
        var services = new ServiceCollection();
        ConfigureDemoServices(services);

        (Services as IDisposable)?.Dispose();
        Services = services.BuildServiceProvider();
        Logger = Services.GetRequiredService<IAppLogger>();

        // In the demo there is no competing process, so grant write access immediately
        // so the interactive UI is fully enabled (flyouts, section editor, etc.).
        Services.GetRequiredService<WriteLockService>().AcquireDemo();

        // Seed semester context — no saved selection to restore in the demo, so
        // the first academic year and its first semester are selected by default.
        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>());

        // Seed the section store for the default selected semester(s).
        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<ISectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName("Demo");
        return vm;
    }

#if !BROWSER
    // ── Desktop DI ───────────────────────────────────────────────────────────

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IAppLogger>(new FileAppLogger());

        // Services
        services.AddSingleton<SemesterContext>();
        services.AddSingleton<WriteLockService>();
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, DialogService>();

        // Data layer — DatabaseContext receives the resolved path directly.
        services.AddSingleton<IDatabaseContext>(_ => new DatabaseContext(dbPath));
        services.AddSingleton<IAcademicYearRepository, AcademicYearRepository>();
        services.AddSingleton<ISemesterRepository, SemesterRepository>();
        services.AddSingleton<IInstructorRepository, InstructorRepository>();
        services.AddSingleton<IRoomRepository, RoomRepository>();
        services.AddSingleton<ILegalStartTimeRepository, LegalStartTimeRepository>();
        services.AddSingleton<IBlockPatternRepository, BlockPatternRepository>();
        services.AddSingleton<ISubjectRepository, SubjectRepository>();
        services.AddSingleton<ICourseRepository, CourseRepository>();
        services.AddSingleton<ISectionRepository, SectionRepository>();
        services.AddSingleton<ISectionPropertyRepository, SectionPropertyRepository>();
        services.AddSingleton<IAcademicUnitRepository, AcademicUnitRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IInstructorCommitmentRepository, InstructorCommitmentRepository>();
        services.AddSingleton<ISectionPrefixRepository, SectionPrefixRepository>();

        RegisterViewModels(services);

#if DEBUG
        services.AddTransient<DebugTestDataGenerator>();
        services.AddTransient<Demo.DemoDataGenerator>();
        services.AddTransient<DebugTestDataViewModel>();
        // ONE-TIME MIGRATION UTILITY — remove after migration is complete
        services.AddTransient<MigrationViewModel>();
#endif
    }
#endif // !BROWSER

    // ── Demo / Browser DI ────────────────────────────────────────────────────

    /// <summary>
    /// Registers demo (in-memory, read-only) implementations of every repository interface.
    /// Called only from <see cref="InitializeDemoServices"/> in the browser (WASM) build path.
    /// The demo repositories are backed by the static <see cref="Demo.DemoData"/> class and
    /// perform no file I/O — Insert/Update/Delete are silent no-ops.
    /// </summary>
    private static void ConfigureDemoServices(IServiceCollection services)
    {
        // ConsoleAppLogger: no file-system access required — safe in WASM.
        services.AddSingleton<IAppLogger>(new ConsoleAppLogger());

        // Services — WriteLockService is registered but TryAcquire is never called;
        // it is included because ScheduleGridViewModel and InstructorListViewModel
        // depend on it via constructor injection.
        // NullDialogService: native window dialogs are unavailable in WASM.
        services.AddSingleton<SemesterContext>();
        services.AddSingleton<WriteLockService>();
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, NullDialogService>();

        // Demo data layer — static DemoData lists, no SQLite.
        services.AddSingleton<IDatabaseContext, DemoDatabaseContext>();
        services.AddSingleton<IAcademicYearRepository,        DemoAcademicYearRepository>();
        services.AddSingleton<ISemesterRepository,            DemoSemesterRepository>();
        services.AddSingleton<IInstructorRepository,          DemoInstructorRepository>();
        services.AddSingleton<IRoomRepository,                DemoRoomRepository>();
        services.AddSingleton<ILegalStartTimeRepository,      DemoLegalStartTimeRepository>();
        services.AddSingleton<IBlockPatternRepository,        DemoBlockPatternRepository>();
        services.AddSingleton<ISubjectRepository,             DemoSubjectRepository>();
        services.AddSingleton<ICourseRepository,              DemoCourseRepository>();
        services.AddSingleton<ISectionRepository,             DemoSectionRepository>();
        services.AddSingleton<ISectionPropertyRepository,     DemoSectionPropertyRepository>();
        services.AddSingleton<IAcademicUnitRepository,        DemoAcademicUnitRepository>();
        services.AddSingleton<IReleaseRepository,             DemoReleaseRepository>();
        services.AddSingleton<IInstructorCommitmentRepository,DemoInstructorCommitmentRepository>();
        services.AddSingleton<ISectionPrefixRepository,       DemoSectionPrefixRepository>();

        RegisterViewModels(services);
        // Note: debug-only services (DemoDataGenerator, MigrationViewModel, etc.) are
        // intentionally omitted from the demo build.
    }

    // ── Shared ViewModel registration ────────────────────────────────────────

    /// <summary>
    /// Registers all ViewModels that are identical between the desktop and demo builds.
    /// Called from both <see cref="ConfigureServices"/> and <see cref="ConfigureDemoServices"/>
    /// to keep the two DI configurations in sync without duplicating the registration block.
    /// </summary>
    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddSingleton<SectionStore>();
        services.AddSingleton<SectionChangeNotifier>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SectionListViewModel>();
        services.AddSingleton<ScheduleGridViewModel>(sp => new ScheduleGridViewModel(
            sp.GetRequiredService<ISectionRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<IInstructorRepository>(),
            sp.GetRequiredService<IRoomRepository>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<ISectionPropertyRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<AcademicUnitService>(),
            sp.GetRequiredService<SectionStore>(),
            sp.GetRequiredService<SectionChangeNotifier>(),
            sp.GetRequiredService<IInstructorCommitmentRepository>(),
            sp.GetRequiredService<WriteLockService>()));
        services.AddSingleton<WorkloadPanelViewModel>(sp => new WorkloadPanelViewModel(
            sp.GetRequiredService<IInstructorRepository>(),
            sp.GetRequiredService<ISectionRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<IReleaseRepository>(),
            sp.GetRequiredService<ISemesterRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<SectionStore>()));
        services.AddTransient<InstructorListViewModel>(sp =>
            new InstructorListViewModel(
                sp.GetRequiredService<IInstructorRepository>(),
                sp.GetRequiredService<ISectionPropertyRepository>(),
                sp.GetRequiredService<ISectionRepository>(),
                sp.GetRequiredService<ICourseRepository>(),
                sp.GetRequiredService<IReleaseRepository>(),
                sp.GetRequiredService<IInstructorCommitmentRepository>(),
                sp.GetRequiredService<ISemesterRepository>(),
                sp.GetRequiredService<IAcademicYearRepository>(),
                sp.GetRequiredService<SemesterContext>(),
                sp.GetRequiredService<SectionChangeNotifier>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<WriteLockService>()));

        services.AddTransient<RoomListViewModel>();
        services.AddTransient<SemesterListViewModel>();
        services.AddTransient<AcademicYearListViewModel>();
        services.AddTransient<CopySemesterViewModel>();
        services.AddTransient<EmptySemesterViewModel>();
        services.AddTransient<LegalStartTimeListViewModel>();
        services.AddTransient<SubjectListViewModel>();
        services.AddTransient<CourseListViewModel>();
        services.AddTransient<CourseHistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SectionPropertiesViewModel>();
        services.AddTransient<BlockPatternListViewModel>();
        services.AddTransient<AcademicUnitListViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<WorkloadReportViewModel>();
        services.AddTransient<WorkloadMailerViewModel>();
        services.AddTransient<SectionPrefixListViewModel>();

        services.AddTransient<CourseHistoryView>();
        services.AddTransient<LegalStartTimesDataExporter>();
    }
}
