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

        // Capture the old provider before overwriting so we can dispose it after the
        // new one is fully initialized. Disposing the root ServiceProvider closes the
        // old SqliteConnection and releases all other IDisposable singletons.
        var oldServices = Services as IDisposable;
        Services = services.BuildServiceProvider();

        // Expose the singleton logger from DI so any code using App.Logger
        // gets the same instance.
        Logger = Services.GetRequiredService<IAppLogger>();

        // Prune old log files (non-throwing).
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();

        // Eagerly initialize the database (schema creation + seeding).
        Services.GetRequiredService<DatabaseContext>();

        // Seed the global semester context from the database,
        // restoring the last-used academic year and semester(s) from local settings.
        var startupSettings = AppSettings.Load();
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

        // Dispose the old provider now that the new one is fully operational.
        oldServices?.Dispose();

        return vm;
    }

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        // Logger — singleton so the same instance is used everywhere.
        // Swap FileAppLogger for a remote/database implementation here when ready.
        services.AddSingleton<IAppLogger>(new FileAppLogger());

        // Services
        services.AddSingleton<SemesterContext>();
        services.AddTransient<AcademicUnitService>();// Services
        services.AddTransient<ScheduleValidationService>();
        services.AddTransient<IDialogService, DialogService>();

        // Data layer — DatabaseContext receives the resolved path directly.
        services.AddSingleton<DatabaseContext>(_ => new DatabaseContext(dbPath));
        services.AddTransient<AcademicYearRepository>();
        services.AddTransient<SemesterRepository>();
        services.AddTransient<InstructorRepository>();
        services.AddTransient<RoomRepository>();
        services.AddTransient<LegalStartTimeRepository>();
        services.AddTransient<BlockPatternRepository>();
        services.AddTransient<SubjectRepository>();
        services.AddTransient<CourseRepository>();
        services.AddTransient<SectionRepository>();
        services.AddTransient<SectionPropertyRepository>();
        services.AddTransient<AcademicUnitRepository>();
        services.AddTransient<ReleaseRepository>();
        services.AddTransient<InstructorCommitmentRepository>();
        services.AddTransient<SectionPrefixRepository>();

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
            sp.GetRequiredService<InstructorCommitmentRepository>()));
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
                sp.GetRequiredService<IDialogService>()));
            
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
