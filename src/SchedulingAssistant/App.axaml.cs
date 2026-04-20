using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if DEBUG
using HotAvalonia;
#endif
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.Exceptions;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views.Management;
using Bugsnag;

namespace SchedulingAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Logger available app-wide, including before DI is fully initialized.
    /// Set early in InitializeServices so it can be used during startup error handling.
    /// </summary>
    public static IAppLogger Logger { get; private set; } = new FileAppLogger
    {        
        BugsnagClient = new Bugsnag.Client(new Configuration("0433a4d8b6fc7e95c43cbb6a87935c31")
        {
            ReleaseStage="production"
        })
    };

    /// <summary>
    /// Write-lock service, created once at app startup and shared across all DI containers.
    /// Registered into DI as a pre-existing instance so the container does not dispose it
    /// when the container is rebuilt on a database switch.
    /// </summary>
    public static WriteLockService LockService { get; } = new WriteLockService();

    /// <summary>
    /// Checkout service, created once at app startup. Manages the D → D' copy (write mode),
    /// the D → D'' snapshot (read-only mode), write lock acquisition, save-back, autosave,
    /// and crash recovery for every database the app opens. Lives outside DI because checkout
    /// must complete before <see cref="InitializeServices"/> is called.
    /// </summary>
    public static CheckoutService Checkout { get; } = new CheckoutService(LockService, Logger);

    public override void Initialize()
    {
#if DEBUG
        this.UseHotReload();
#endif
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

        // Integrity check — run before acquiring the write lock or touching the schema,
        // so any corruption is surfaced on a pristine connection. The check is a no-op
        // when the file does not yet exist (brand-new database).
        if (File.Exists(dbPath) && !BackupService.CheckIntegrity(dbPath))
        {
            // Signal the caller; MainWindow.RunStartupAsync handles the restore dialog.
            throw new DatabaseCorruptException(dbPath);
        }

        // The write lock was already acquired by App.Checkout.CheckoutAsync() before
        // this method was called.  App.LockService is the pre-DI singleton registered
        // into the container, so all code that resolves WriteLockService from DI gets
        // the instance that already has the correct IsWriter / CurrentHolder state.

        // Eagerly initialize the database (schema creation + seeding).
        // Wire SaveCompleted → ResetDirty so the next user write after a save re-arms MarkDirty.
        var dbCtx = Services.GetRequiredService<IDatabaseContext>();
        App.Checkout.SaveCompleted += dbCtx.ResetDirty;

        // Seed the global semester context from the database,
        // restoring the last-used academic year and semester(s) from local settings.
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
        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(dbPath), dbPath);
        return vm;
    }

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        // Logger — singleton so the same instance is used everywhere.
        // Swap FileAppLogger for a remote/database implementation here when ready.
        services.AddSingleton<IAppLogger>(App.Logger);

        // Services
        services.AddSingleton<SemesterContext>();
        // Register the pre-DI WriteLockService instance. Using the overload that
        // takes an existing instance means the container will NOT dispose it when
        // the container is rebuilt on a database switch.
        services.AddSingleton(App.LockService);
        services.AddSingleton<BackupService>();
        services.AddSingleton<AppNotificationService>();
        // These services are stateless wrappers; singletons match their actual lifetime.
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<ScheduleValidationService>();
        services.AddTransient<IDialogService, DialogService>();

        // Data layer — DatabaseContext receives the resolved path directly.
        // Repositories are stateless wrappers around the singleton DatabaseContext,
        // so they are registered as singletons to accurately reflect their actual lifetime.
        // Must use factory (not instance) so the container owns and disposes it on shutdown.
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
        services.AddSingleton<ISectionPrefixRepository, SectionPrefixRepository>();
        services.AddSingleton<ICampusRepository, CampusRepository>();
        services.AddSingleton<IAppConfigurationRepository, AppConfigurationRepository>();
        services.AddSingleton<AppConfigurationService>();

        // ViewModels
        services.AddSingleton<SectionStore>();
        services.AddSingleton<MeetingStore>();
        services.AddSingleton<IMeetingRepository, MeetingRepository>();
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
        services.AddTransient<SaveAndBackupViewModel>();
        services.AddTransient<SchedulingEnvironmentViewModel>();
        services.AddTransient<ConfigurationViewModel>();
        services.AddTransient<BlockPatternListViewModel>();
        services.AddTransient<AcademicUnitListViewModel>();
        services.AddTransient<SectionPrefixListViewModel>();
        services.AddTransient<ExportHubViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<WorkloadReportViewModel>();
        services.AddTransient<WorkloadMailerViewModel>();
        services.AddTransient<CampusListViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<ShareViewModel>();
        services.AddTransient<NewDatabaseViewModel>();

        // Views
        services.AddTransient<CourseHistoryView>();

        //Dialogs


#if DEBUG
        services.AddTransient<DebugTestDataGenerator>();
        services.AddTransient<DebugTestDataViewModel>();
        // ONE-TIME MIGRATION UTILITY — remove after migration is complete
        services.AddTransient<MigrationViewModel>();
#endif
    }
}
