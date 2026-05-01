using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if !BROWSER
using HotAvalonia;
using Bugsnag;
#endif
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
#if BROWSER
using SchedulingAssistant.Data.Repositories.Demo;
#endif
using SchedulingAssistant.Services;
#if !BROWSER
using SchedulingAssistant.Exceptions;
#endif
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.Management;

namespace SchedulingAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Logger available app-wide, including before DI is fully initialized.
    /// </summary>
#if !BROWSER
    public static IAppLogger Logger { get; private set; } = new FileAppLogger
    {
        BugsnagClient = new Bugsnag.Client(new Configuration("0433a4d8b6fc7e95c43cbb6a87935c31")
        {
            ReleaseStage="production"
        })
    };
#else
    public static IAppLogger Logger { get; private set; } = new ConsoleAppLogger();
#endif

#if !BROWSER
    /// <summary>
    /// Write-lock service, created once at app startup and shared across all DI containers.
    /// </summary>
    public static WriteLockService LockService { get; } = new WriteLockService();

    /// <summary>
    /// Checkout service — manages D to D' copy, write lock, save-back, autosave, and crash recovery.
    /// </summary>
    public static CheckoutService Checkout { get; } = new CheckoutService(LockService, Logger);
#endif

    public override void Initialize()
    {
#if !BROWSER
#if DEBUG
        this.UseHotReload();
#endif
#endif
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if !BROWSER
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var win = new MainWindow();
            win.IsVisible = false;
            desktop.MainWindow = win;

            _ = new UpdateService().CheckForUpdatesAsync(Logger);
        }
#else
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var vm = InitializeDemoServices();
            singleView.MainView = new MainView { DataContext = vm };
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }

#if !BROWSER
    /// <summary>
    /// Called by MainWindow once a database path has been resolved.
    /// Builds the full DI container and returns the root view model.
    /// </summary>
    public static MainWindowViewModel InitializeServices(string dbPath)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, dbPath);

        (Services as IDisposable)?.Dispose();

        Services = services.BuildServiceProvider();

        Logger = Services.GetRequiredService<IAppLogger>();
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();

        if (File.Exists(dbPath) && !BackupService.CheckIntegrity(dbPath))
            throw new DatabaseCorruptException(dbPath);

        var dbCtx = Services.GetRequiredService<IDatabaseContext>();
        App.Checkout.SaveCompleted += dbCtx.ResetDirty;

        var startupSettings = AppSettings.Current;
        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>(),
            restoreAcademicYearId: startupSettings.LastSelectedAcademicYearId,
            restoreSemesterIds:    startupSettings.LastSelectedSemesterIds.Count > 0
                                       ? startupSettings.LastSelectedSemesterIds.ToHashSet()
                                       : null);

        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<ISectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(dbPath), dbPath);
        return vm;
    }

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IAppLogger>(App.Logger);

        services.AddSingleton<SemesterContext>();
        services.AddSingleton(App.LockService);
        services.AddSingleton<BackupService>();
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, DialogService>();

        services.AddSingleton<IDatabaseContext>(_ => new DatabaseContext(dbPath, App.Checkout.MarkDirty));
        services.AddSingleton<IAcademicYearRepository, AcademicYearRepository>();
        services.AddSingleton<ISemesterRepository, SemesterRepository>();
        services.AddSingleton<IInstructorRepository, InstructorRepository>();
        services.AddSingleton<IRoomRepository, RoomRepository>();
        services.AddSingleton<ILegalStartTimeRepository, LegalStartTimeRepository>();
        services.AddSingleton<IBlockPatternRepository, BlockPatternRepository>();
        services.AddSingleton<ISubjectRepository, SubjectRepository>();
        services.AddSingleton<ICourseRepository, CourseRepository>();
        services.AddSingleton<ISectionRepository, SectionRepository>();
        services.AddSingleton<ISchedulingEnvironmentRepository, SchedulingEnvironmentRepository>();
        services.AddSingleton<IAcademicUnitRepository, AcademicUnitRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IInstructorCommitmentRepository, InstructorCommitmentRepository>();
        services.AddSingleton<ICampusRepository, CampusRepository>();
        services.AddSingleton<IMeetingRepository, MeetingRepository>();
        services.AddSingleton<ISectionCodePatternRepository, SectionCodePatternRepository>();

        RegisterViewModels(services);

        services.AddTransient<SaveAndBackupViewModel>();
        services.AddTransient<NewDatabaseViewModel>();

#if DEBUG
        services.AddTransient<DebugTestDataGenerator>();
        services.AddTransient<DebugTestDataViewModel>();
        services.AddTransient<MigrationViewModel>();
#endif
    }
#endif // !BROWSER

#if BROWSER
    // ── Demo / Browser DI ──────────────────────────────────────��─────────────

    /// <summary>
    /// Browser (WASM) entry point — builds a DI container backed by static demo data.
    /// </summary>
    public static MainWindowViewModel InitializeDemoServices()
    {
        var services = new ServiceCollection();
        ConfigureDemoServices(services);

        (Services as IDisposable)?.Dispose();
        Services = services.BuildServiceProvider();
        Logger = Services.GetRequiredService<IAppLogger>();

        Services.GetRequiredService<WriteLockService>().AcquireDemo();

        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>());

        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<ISectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName("Demo");
        return vm;
    }

    private static void ConfigureDemoServices(IServiceCollection services)
    {
        services.AddSingleton<IAppLogger>(new ConsoleAppLogger());

        services.AddSingleton<SemesterContext>();
        services.AddSingleton<WriteLockService>();
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, NullDialogService>();

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
        services.AddSingleton<ISchedulingEnvironmentRepository, DemoSchedulingEnvironmentRepository>();
        services.AddSingleton<IAcademicUnitRepository,        DemoAcademicUnitRepository>();
        services.AddSingleton<IReleaseRepository,             DemoReleaseRepository>();
        services.AddSingleton<IInstructorCommitmentRepository,DemoInstructorCommitmentRepository>();
        services.AddSingleton<ICampusRepository,              DemoCampusRepository>();
        services.AddSingleton<IMeetingRepository,             DemoMeetingRepository>();
        services.AddSingleton<ISectionCodePatternRepository,  DemoSectionCodePatternRepository>();

        RegisterViewModels(services);
    }
#endif // BROWSER

    // ── Shared ViewModel registration ────────────────────────────────────────

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddSingleton<SectionStore>();
        services.AddSingleton<MeetingStore>();
        services.AddSingleton<SectionChangeNotifier>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SectionListViewModel>();
        services.AddSingleton<MeetingListViewModel>();
        services.AddSingleton<ScheduleGridViewModel>(sp => new ScheduleGridViewModel(
            sp.GetRequiredService<ISectionRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<IInstructorRepository>(),
            sp.GetRequiredService<IRoomRepository>(),
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<ISchedulingEnvironmentRepository>(),
            sp.GetRequiredService<ICampusRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<AcademicUnitService>(),
            sp.GetRequiredService<SectionStore>(),
            sp.GetRequiredService<MeetingStore>(),
            sp.GetRequiredService<IMeetingRepository>(),
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
                sp.GetRequiredService<ISchedulingEnvironmentRepository>(),
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
        services.AddTransient<AcademicYearListViewModel>();
        services.AddTransient<CopySemesterViewModel>();
        services.AddTransient<EmptySemesterViewModel>();
        services.AddTransient<LegalStartTimeListViewModel>();
        services.AddTransient<SubjectListViewModel>();
        services.AddTransient<CourseListViewModel>();
        services.AddTransient<CourseHistoryViewModel>();
        services.AddTransient<SchedulingEnvironmentViewModel>();
        services.AddTransient<ConfigurationViewModel>();
        services.AddTransient<BlockPatternListViewModel>();
        services.AddTransient<SectionCodePatternListViewModel>();
        services.AddTransient<AcademicUnitListViewModel>();
        services.AddTransient<ExportHubViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<WorkloadReportViewModel>();
        services.AddTransient<WorkloadMailerViewModel>();
        services.AddTransient<CourseHistoryExportViewModel>();
        services.AddTransient<CampusListViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<ShareViewModel>();

        services.AddTransient<CourseHistoryView>();
    }
}
