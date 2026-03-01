using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;

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
        Services = services.BuildServiceProvider();

        // Expose the singleton logger from DI so any code using App.Logger
        // gets the same instance.
        Logger = Services.GetRequiredService<IAppLogger>();

        // Prune old log files (non-throwing).
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();

        // Eagerly initialize the database (schema creation + seeding).
        Services.GetRequiredService<DatabaseContext>();

        // Seed the global semester context from the database.
        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<AcademicYearRepository>(),
            Services.GetRequiredService<SemesterRepository>());

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

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ScheduleGridViewModel>(sp => new ScheduleGridViewModel(
            sp.GetRequiredService<SectionRepository>(),
            sp.GetRequiredService<CourseRepository>(),
            sp.GetRequiredService<InstructorRepository>(),
            sp.GetRequiredService<RoomRepository>(),
            sp.GetRequiredService<SubjectRepository>(),
            sp.GetRequiredService<SectionPropertyRepository>(),
            sp.GetRequiredService<SemesterContext>()));
        services.AddSingleton<SectionListViewModel>();
        services.AddTransient<InstructorListViewModel>();
        services.AddTransient<RoomListViewModel>();
        services.AddTransient<SemesterListViewModel>();
        services.AddTransient<AcademicYearListViewModel>();
        services.AddTransient<LegalStartTimeListViewModel>();
        services.AddTransient<SubjectListViewModel>();
        services.AddTransient<CourseListViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SectionPropertiesViewModel>();
        services.AddTransient<BlockPatternListViewModel>();

#if DEBUG
        services.AddTransient<DebugTestDataGenerator>();
        services.AddTransient<DebugTestDataViewModel>();
#endif
    }
}
