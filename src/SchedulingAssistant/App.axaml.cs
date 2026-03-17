using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HotAvalonia;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views.Management;

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
        this.UseHotReload();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Show MainWindow hidden — it will run async startup in OnOpened,
            // initialize the DB, then make itself visible.
            var win = new MainWindow();
            win.IsVisible = false;
            desktop.MainWindow = win;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Called by MainWindow once a database path has been resolved.
    /// Builds the full DI container and returns the root view model.
    /// </summary>
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

        // Expose the singleton logger from DI so any code using App.Logger
        // gets the same instance.
        Logger = Services.GetRequiredService<IAppLogger>();

        // Prune old log files (non-throwing).
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();

        // Acquire the write lock before touching the database file.
        // This must happen before DatabaseContext is initialized so that if two
        // instances open the same file simultaneously, the loser enters read-only
        // mode before any writes occur.
        Services.GetRequiredService<WriteLockService>().TryAcquire(dbPath);

        // Eagerly initialize the database (schema creation + seeding).
        Services.GetRequiredService<DatabaseContext>();

        // Seed the global semester context from the database,
        // restoring the last-used academic year and semester(s) from local settings.
        var startupSettings = AppSettings.Current;
        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<AcademicYearRepository>(),
            Services.GetRequiredService<SemesterRepository>(),
            restoreAcademicYearId: startupSettings.LastSelectedAcademicYearId,
            restoreSemesterIds:    startupSettings.LastSelectedSemesterIds.Count > 0
                                       ? startupSettings.LastSelectedSemesterIds.ToHashSet()
                                       : null);

        // Seed the section store so ViewModels can read from the cache on first load.
        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<SectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(dbPath));
        return vm;
    }

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        // Logger — singleton so the same instance is used everywhere.
        // Swap FileAppLogger for a remote/database implementation here when ready.
        services.AddSingleton<IAppLogger>(new FileAppLogger());

        // Services
        services.AddSingleton<SemesterContext>();
        services.AddSingleton<WriteLockService>();
        // These services are stateless wrappers; singletons match their actual lifetime.
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, DialogService>();

        // Data layer — DatabaseContext receives the resolved path directly.
        // Repositories are stateless wrappers around the singleton DatabaseContext,
        // so they are registered as singletons to accurately reflect their actual lifetime.
        services.AddSingleton<DatabaseContext>(_ => new DatabaseContext(dbPath));
        services.AddSingleton<AcademicYearRepository>();
        services.AddSingleton<SemesterRepository>();
        services.AddSingleton<InstructorRepository>();
        services.AddSingleton<RoomRepository>();
        services.AddSingleton<LegalStartTimeRepository>();
        services.AddSingleton<BlockPatternRepository>();
        services.AddSingleton<SubjectRepository>();
        services.AddSingleton<CourseRepository>();
        services.AddSingleton<SectionRepository>();
        services.AddSingleton<SectionPropertyRepository>();
        services.AddSingleton<AcademicUnitRepository>();
        services.AddSingleton<ReleaseRepository>();
        services.AddSingleton<InstructorCommitmentRepository>();
        services.AddSingleton<SectionPrefixRepository>();

        // ViewModels
        services.AddSingleton<SectionStore>();
        services.AddSingleton<SectionChangeNotifier>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SectionListViewModel>();
        services.AddSingleton<ScheduleGridViewModel>(sp => new ScheduleGridViewModel(
            sp.GetRequiredService<SectionRepository>(),
            sp.GetRequiredService<CourseRepository>(),
            sp.GetRequiredService<InstructorRepository>(),
            sp.GetRequiredService<RoomRepository>(),
            sp.GetRequiredService<SubjectRepository>(),
            sp.GetRequiredService<SectionPropertyRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<AcademicUnitService>(),
            sp.GetRequiredService<SectionStore>(),
            sp.GetRequiredService<SectionChangeNotifier>(),
            sp.GetRequiredService<InstructorCommitmentRepository>(),
            sp.GetRequiredService<WriteLockService>()));
        services.AddSingleton<WorkloadPanelViewModel>(sp => new WorkloadPanelViewModel(
            sp.GetRequiredService<InstructorRepository>(),
            sp.GetRequiredService<SectionRepository>(),
            sp.GetRequiredService<CourseRepository>(),
            sp.GetRequiredService<ReleaseRepository>(),
            sp.GetRequiredService<SemesterRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<SectionStore>()));
        services.AddTransient<InstructorListViewModel>(sp =>
            new InstructorListViewModel(
                sp.GetRequiredService<InstructorRepository>(),
                sp.GetRequiredService<SectionPropertyRepository>(),
                sp.GetRequiredService<SectionRepository>(),
                sp.GetRequiredService<CourseRepository>(),
                sp.GetRequiredService<ReleaseRepository>(),
                sp.GetRequiredService<InstructorCommitmentRepository>(),
                sp.GetRequiredService<SemesterRepository>(),
                sp.GetRequiredService<AcademicYearRepository>(),
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

        // Views
        services.AddTransient<CourseHistoryView>();

        //Dialogs
        

        // Data export utilities
        services.AddTransient<LegalStartTimesDataExporter>();

#if DEBUG
        services.AddTransient<DebugTestDataGenerator>();
        services.AddTransient<DebugTestDataViewModel>();
#endif
    }
}
