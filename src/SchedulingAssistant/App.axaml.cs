using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if !BROWSER
#if DEBUG
using HotAvalonia;
#endif
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
using Avalonia.Controls;
using System.Linq;

namespace SchedulingAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Tour step actions (PreAction/PostAction callbacks), built once in
    /// <see cref="InitializeServices"/> and reused on Ctrl+Shift+T hot-reload.
    /// </summary>
    internal static Dictionary<string, TourStepActions>? TourActions { get; private set; }

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
        // ── Global UI-thread safety net ─────────────────────────────────────
        // Catches exceptions that escape all local try-catch blocks on the UI
        // thread (property change cascades, binding converters, rendering code,
        // unprotected async void event handlers). Logs via App.Logger, which
        // fires ErrorLogged → AppNotificationService banner, keeping the app
        // alive instead of crashing.
        //
        // In debug mode (ThrowOnError), the exception is NOT handled so it
        // propagates to the debugger as usual.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (Logger.ThrowOnError) return;

            Logger.LogError(e.Exception, "Unexpected error (the app will try to continue)");
            e.Handled = true;
        };

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
    /// <para>
    /// When <paramref name="restoreAcademicYearId"/> and <paramref name="restoreSemesterIds"/>
    /// are supplied (e.g. when switching from read-only to write mode), they take precedence
    /// over the persisted <see cref="AppSettings"/> values, so the semester the user was
    /// viewing is preserved across the DI teardown/rebuild cycle.
    /// </para>
    /// </summary>
    /// <param name="dbPath">Path to the working-copy database file (D').</param>
    /// <param name="restoreAcademicYearId">Academic year ID to restore. Defaults to the AppSettings value.</param>
    /// <param name="restoreSemesterIds">Semester IDs to restore. Defaults to the AppSettings value.</param>
    public static MainWindowViewModel InitializeServices(string dbPath,
        string? restoreAcademicYearId = null,
        IReadOnlySet<string>? restoreSemesterIds = null)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, dbPath);

        (Services as IDisposable)?.Dispose();

        Services = services.BuildServiceProvider();

        Logger = Services.GetRequiredService<IAppLogger>();
        if (Logger is FileAppLogger fal) fal.PruneOldLogs();


        TourActions = TourActionDefinitions.Build();



        TourCatalog.Initialize(Current!.Resources, TourActions);
        foreach (var err in TourCatalog.Validate())
            Logger.LogInfo($"[TourCatalog] {err}");

        if (File.Exists(dbPath) && !BackupService.CheckIntegrity(dbPath))
            throw new DatabaseCorruptException(dbPath);

        var dbCtx = Services.GetRequiredService<IDatabaseContext>();
        // Subscribe ResetDirty to BeforeDirtyMarkerDeleted (synchronous, fires inside
        // SaveAsyncCore step 7 right before the marker file is deleted). The previous
        // wiring used SaveCompleted, which is dispatched to the UI thread AFTER the
        // marker is already gone — opening a window during which a write could fail
        // to re-arm the marker, leading to silent data loss on crash. (F2.)
        App.Checkout.BeforeDirtyMarkerDeleted += dbCtx.ResetDirty;

        // Fall back to persisted AppSettings values when no in-memory IDs were passed.
        var startupSettings = AppSettings.Current;
        restoreAcademicYearId ??= startupSettings.LastSelectedAcademicYearId;
        restoreSemesterIds    ??= startupSettings.LastSelectedSemesterIds.Count > 0
                                      ? startupSettings.LastSelectedSemesterIds.ToHashSet()
                                      : null;

        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>(),
            restoreAcademicYearId: restoreAcademicYearId,
            restoreSemesterIds:    restoreSemesterIds);

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
        services.AddSingleton<SharedScheduleService>();
        services.AddSingleton<SharedScheduleCsvParser>();
        services.AddSingleton<SharedScheduleCsvExporter>();
        services.AddSingleton<TourRunner>();
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

        TourCatalog.Initialize(Current!.Resources);
        foreach (var err in TourCatalog.Validate())
            Logger.LogInfo($"[TourCatalog] {err}");

        Services.GetRequiredService<WriteLockService>().AcquireDemo();

        var semesterContext = Services.GetRequiredService<SemesterContext>();
        semesterContext.Reload(
            Services.GetRequiredService<IAcademicYearRepository>(),
            Services.GetRequiredService<ISemesterRepository>(),
            restoreAcademicYearId: "demo-ay-1",
            restoreSemesterIds: new HashSet<string> { "demo-sem-1" });

        var sectionStore = Services.GetRequiredService<SectionStore>();
        sectionStore.Reload(
            Services.GetRequiredService<ISectionRepository>(),
            semesterContext.SelectedSemesters.Select(s => s.Semester.Id));

        // In demo/WASM mode the startup wizard is bypassed, so mark setup as
        // complete so PostWizardFirstLaunch auto-triggers evaluate correctly.
        AppSettings.Current.IsInitialSetupComplete = true;

        var vm = Services.GetRequiredService<MainWindowViewModel>();
        vm.SetDatabaseName("Demo");

        // Evaluate auto-triggers on the next UI tick so the MainView has loaded
        // and the overlay can resolve target controls.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Services.GetRequiredService<TourRunner>().EvaluateAutoTriggers();
        }, Avalonia.Threading.DispatcherPriority.Background);

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
        services.AddSingleton<SharedScheduleService>();
        services.AddSingleton<SharedScheduleCsvParser>();
        services.AddSingleton<SharedScheduleCsvExporter>();
        services.AddSingleton<TourRunner>();
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
        services.AddSingleton<GridChangeNotifier>();
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
            sp.GetRequiredService<GridChangeNotifier>(),
            sp.GetRequiredService<IInstructorCommitmentRepository>(),
            sp.GetRequiredService<WriteLockService>(),
            sp.GetRequiredService<SharedScheduleService>()));
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
                sp.GetRequiredService<GridChangeNotifier>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<WriteLockService>(),
                sp.GetRequiredService<WorkloadMailerViewModel>()));

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
        services.AddTransient<PreferencesViewModel>();
        services.AddTransient<ExportHubViewModel>();
        services.AddTransient<SharingViewModel>();
        services.AddTransient<WorkloadReportViewModel>();
        services.AddTransient<WorkloadMailerViewModel>();
        services.AddTransient<CourseHistoryExportViewModel>();
        services.AddTransient<CampusListViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<ShareViewModel>();

        services.AddTransient<CourseHistoryView>();
    }
}
