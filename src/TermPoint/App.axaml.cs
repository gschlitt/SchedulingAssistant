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
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Data.Repositories.Demo;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
#if !BROWSER
using TermPoint.Exceptions;
#endif
using TermPoint.Licensing;
using TermPoint.ViewModels;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using TermPoint.Views;
using TermPoint.Views.Management;
using Avalonia.Controls;
using System.Linq;

namespace TermPoint;

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
        BugsnagClient = CreateBugsnagClient()
    };

    /// <summary>
    /// Builds the BugSnag client. Every error report and session batch it sends goes through
    /// <see cref="ScrubbingDelivery"/> (spec item 6, step 6b), which removes paths, email addresses and known user and
    /// computer names, takes out the computer's name, stamps the anonymous install ID, and drops anything it can't check.
    /// </summary>
    /// <returns>The configured client.</returns>
    private static Bugsnag.Client CreateBugsnagClient()
    {
        var configuration = new Configuration("0433a4d8b6fc7e95c43cbb6a87935c31")
        {
            ReleaseStage = DetectReleaseStage(),
            NotifyReleaseStages = new[] { "production", "development" },
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly()
                             .GetName().Version?.ToString() ?? "0.0.0",
            ProjectNamespaces = new[] { "TermPoint" },
        };

        // The real delivery takes its settings from the configuration; with a delivery of our own in front of it, make
        // sure it has them (configuring it twice is harmless).
        DefaultDelivery.Instance.Configure(configuration);
        var delivery = new ScrubbingDelivery(
            DefaultDelivery.Instance,
            installId:  () => AppSettings.Current.InstallId,
            logLocally: message => Logger.LogInfo(message));

        return new Bugsnag.Client(configuration, delivery);
    }

    /// <summary>
    /// Returns "development" for debug builds, "production" for release builds.
    /// MSIX vs sideload is not distinguished here — both are release builds.
    /// </summary>
    private static string DetectReleaseStage()
    {
#if DEBUG
        return "development";
#else
        return "production";
#endif
    }
#else
    public static IAppLogger Logger { get; private set; } = new ConsoleAppLogger();
#endif

#if !BROWSER
    /// <summary>
    /// Write-lock service, created once at app startup and shared across all DI containers.
    /// </summary>
    public static WriteLockService LockService { get; } = new WriteLockService();

    /// <summary>
    /// DEBUG-ONLY fault injection for the open-time integrity check of the local copy (spec item 5, step 5d) and
    /// for the version gate's stamp read (spec item 3, step 3c), from the <c>TERMPOINT_DEBUG_FAIL_INTEGRITY</c>
    /// environment variable (see <see cref="IntegrityFaultPlan"/>). Consulted by <see cref="InitializeServices"/>
    /// (the <c>locked</c> and <c>damaged</c> faults), by the main window's re-check of the shared database (the
    /// <c>shared</c> fault) and by <see cref="Checkout"/> (the <c>stamp</c> fault). Null in a Release build and in
    /// every ordinary run, so shipped builds never inject anything. Declared after <see cref="Logger"/> because a
    /// plan that is set is announced in the log, so a forgotten variable in a launch profile can't go unnoticed,
    /// and BEFORE <see cref="Checkout"/>, which is handed the same plan when it is built (static initializers run
    /// in textual order).
    /// </summary>
    internal static IntegrityFaultPlan? IntegrityFaults { get; } = CreateIntegrityFaultPlan();

    /// <summary>
    /// Reads the DEBUG-ONLY integrity fault plan from the environment and announces it in the log when one is set.
    /// </summary>
    /// <returns>The plan, or null (no variable, not understood, or a Release build).</returns>
    private static IntegrityFaultPlan? CreateIntegrityFaultPlan()
    {
        var plan = IntegrityFaultPlan.FromEnvironment();
        if (plan is not null)
            Logger.LogInfo(
                $"[DEBUG fault injection] {IntegrityFaultPlan.EnvironmentVariableName} is set: " +
                $"the injected failure is {plan.Fault}; {plan.Description}.");
        return plan;
    }

    /// <summary>
    /// Checkout service — manages D to D' copy, write lock, save-back, autosave, and crash recovery.
    /// </summary>
    public static CheckoutService Checkout { get; } = CreateCheckoutService();

    /// <summary>
    /// The per-database, per-user single-instance guard (spec item 4b). <c>MainWindow</c> takes it for a
    /// database BEFORE any cleanup, crash detection or checkout — at startup and on every database switch
    /// — and releases it when the database is closed and on exit. Declared after <see cref="Logger"/> so
    /// the logger exists when it is created (static initializers run in textual order).
    /// </summary>
    public static DatabaseInstanceGuard InstanceGuard { get; } = new DatabaseInstanceGuard(logger: Logger);

    /// <summary>
    /// Builds the app-wide <see cref="CheckoutService"/>. Declared after <see cref="Logger"/>,
    /// <see cref="LockService"/> and <see cref="IntegrityFaults"/> in this class so all three exist by the
    /// time it runs (static initializers run in textual order).
    ///
    /// <para>Also attaches the DEBUG-ONLY fault plans from the <c>TERMPOINT_DEBUG_FAIL_CHECKOUT</c>,
    /// <c>TERMPOINT_DEBUG_FAIL_RESTORE</c>, <c>TERMPOINT_DEBUG_FAIL_REFRESH</c> and <c>TERMPOINT_DEBUG_FAIL_INTEGRITY</c>
    /// environment variables (see <see cref="CheckoutFaultPlan"/>, <see cref="RestoreFaultPlan"/>,
    /// <see cref="RefreshFaultPlan"/> and <see cref="IntegrityFaultPlan"/>): their
    /// <c>FromEnvironment</c> methods return null in Release builds, so a shipped build never injects a fault. A
    /// plan that is attached is announced in the log, so a forgotten variable in a launch profile can't go
    /// unnoticed. The integrity plan is the SAME object as <see cref="IntegrityFaults"/> (one shared budget); the
    /// checkout service consults it only for the version gate's <c>stamp</c> fault.</para>
    /// </summary>
    /// <returns>The checkout service, with fault plans only in a DEBUG build that has the variables set.</returns>
    private static CheckoutService CreateCheckoutService()
    {
        var service = new CheckoutService(LockService, Logger)
        {
            FaultPlan          = CheckoutFaultPlan.FromEnvironment(),
            RestoreFaultPlan   = RestoreFaultPlan.FromEnvironment(),
            RefreshFaultPlan   = RefreshFaultPlan.FromEnvironment(),
            IntegrityFaultPlan = IntegrityFaults
        };

        if (service.FaultPlan is { } plan)
            Logger.LogInfo(
                $"[DEBUG fault injection] {CheckoutFaultPlan.EnvironmentVariableName} is set: " +
                $"checkouts will fail as {plan.Fault}; {plan.Description}.");

        if (service.RestoreFaultPlan is { } restorePlan)
            Logger.LogInfo(
                $"[DEBUG fault injection] {RestoreFaultPlan.EnvironmentVariableName} is set: " +
                $"restores will fail at the {restorePlan.Fault} stage; {restorePlan.Description}.");

        if (service.RefreshFaultPlan is { } refreshPlan)
            Logger.LogInfo(
                $"[DEBUG fault injection] {RefreshFaultPlan.EnvironmentVariableName} is set: " +
                $"read-only refreshes will fail as {refreshPlan.Fault}; {refreshPlan.Description}.");

        return service;
    }

    /// <summary>
    /// Combined license + trial evaluation result, set once per database open in
    /// <see cref="InitializeServices"/>. ViewModels read this to gate write commands
    /// and display licensing UI.
    /// </summary>
    public static AppAccessResult LicenseStatus { get; private set; } = new()
    {
        AccessLevel = AccessLevel.FullAccess,
        Reason = AccessReason.Trial,
        ShowPurchasePrompt = false
    };

    /// <summary>
    /// The clock licensing runs on: the system clock, or (DEBUG builds only) a fixed date from the
    /// TERMPOINT_DEBUG_DATE environment variable. Shared by the open-time evaluation and the daily check.
    /// </summary>
    internal static IClock LicenseClock { get; } = CreateLicenseClock();

    /// <summary>Builds <see cref="LicenseClock"/>, logging a DEBUG date override if there is one.</summary>
    /// <returns>The clock.</returns>
    private static IClock CreateLicenseClock()
    {
#if DEBUG
        var clock = FixedClock.FromEnvironmentOrSystem();
        if (clock is FixedClock fc)
            Logger.LogInfo($"[Licensing] DEBUG clock override: {fc.UtcNow:yyyy-MM-dd}");
        return clock;
#else
        return new SystemClock();
#endif
    }

    /// <summary>
    /// Builds a licence manager (validator + trial + remembered-licence cache) over the real AppData folder.
    /// The one place this is constructed, used by the open-time evaluation and the daily check.
    /// </summary>
    /// <returns>A new manager.</returns>
    internal static AppLicenseManager CreateLicenseManager()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TermPoint");
        void Log(string line) => Logger.LogInfo(line);
        return new AppLicenseManager(
            new LicenseValidator(LicenseKeys.PublicKeyPem, LicenseClock),
            new TrialService(appData, LicenseClock, Log),
            new LicenseCache(appData, Log),
            Log);
    }

    /// <summary>
    /// Evaluates licence + trial for a share folder under the network deadline: reading
    /// <c>termpoint.lic</c> from a dead share must not freeze the caller for the SMB redirector timeout.
    /// Does not change <see cref="LicenseStatus"/>.
    /// </summary>
    /// <param name="shareDir">The folder holding the shared database and <c>termpoint.lic</c>.</param>
    /// <returns>The access result, or null when the deadline passed.</returns>
    /// <exception cref="Exception">Anything the evaluation itself throws.</exception>
    internal static AppAccessResult? TryEvaluateLicense(string shareDir)
    {
        var manager = CreateLicenseManager();
        var (completed, result) = NetworkFileOps.RunAsync(
            () => manager.EvaluateAccess(shareDir), "EvaluateLicense")
            .GetAwaiter().GetResult();
        return completed ? result : null;
    }

    /// <summary>
    /// Evaluates the license and trial state for the given database path.
    /// Must be called before <see cref="CheckoutService.CheckoutAsync"/> so
    /// the license gate can block lock acquisition when access is read-only.
    /// </summary>
    public static void EvaluateLicense(string dbPath)
    {
        var shareDir = Path.GetDirectoryName(dbPath) ?? string.Empty;

        // On timeout the remembered licence for this folder (if any) is used, else the trial.
        var result = TryEvaluateLicense(shareDir);
        if (result is null)
        {
            Logger.LogInfo("[Licensing] License check timed out — share unreachable; using remembered license or trial");
            result = CreateLicenseManager().EvaluateAccessWithoutLicense(shareDir);
        }
        LicenseStatus = result;

        Logger.LogInfo($"[Licensing] {LicenseStatus.Reason}, access={LicenseStatus.AccessLevel}" +
            (LicenseStatus.DepartmentName != null ? $", dept={LicenseStatus.DepartmentName}" : "") +
            (LicenseStatus.DaysRemaining != null ? $", trial={LicenseStatus.DaysRemaining}d" : ""));
    }
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
            // A write refused because this session is read-only is not a bug and not unexpected: say so in
            // plain words (one banner, no crash report), whether or not the developer switch is on.
            if (WriteFailureText.NotifyIfWriteAccessLost(Logger, e.Exception))
            {
                e.Handled = true;
                return;
            }

            if (Logger.ThrowOnError) return;

            Logger.LogError(e.Exception, "Unexpected error (the app will try to continue)", unhandled: true);
            e.Handled = true;
        };

        // ── Popup wheel containment ─────────────────────────────────────────
        // Stops a mouse-wheel tick inside any popup (dropdowns, pickers, flyouts)
        // from escaping and scrolling the area behind it (spec item 19). Kept
        // outside the #if so the WASM demo gets it too.
        TermPoint.Behaviors.PopupWheelContainment.Register();

#if !BROWSER
        // Start a BugSnag session so errors during this run contribute to the
        // per-release stability score (% crash-free sessions). The anonymous install ID
        // is stamped on every report by ScrubbingDelivery (see CreateBugsnagClient), so
        // reports from before this point carry it too.
        if (Logger is FileAppLogger { BugsnagClient: { } bugsnag })
            bugsnag.SessionTracking.CreateSession();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // MainWindow is the initial window. It carries its own in-window loading
            // curtain (a gradient "TermPoint / Loading…" overlay) that renders immediately
            // and stays up until the schedule grid finishes its first render, so the user
            // never sees the window assemble itself. See MainWindow.LiftCurtainAsync.
            desktop.MainWindow = new MainWindow();

            // Exit when the MAIN window closes, not when the last window closes (Avalonia's
            // default). TermPoint has secondary top-level windows (sticky notes, detached
            // panels); under the default, one left open would keep the process alive with no
            // visible main window — a "silent zombie" that can perpetually hold the DB write
            // lock. OnMainWindowClose makes a normal close tear the whole app down.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            // Record how this copy was installed (Store / Sideload / DirectDownload / Development).
            // It decides the wording of the "database needs a newer TermPoint" banner and whether
            // this build may stamp databases. Read here, after Program.Main has already run
            // Velopack's startup hooks, because detection may touch Velopack.
            Logger.LogInfo($"[Startup] Install channel: {PlatformCapabilities.InstallChannel}");

            if (!PlatformCapabilities.IsMsixPackage)
                StartBackgroundUpdateCheck();
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
    // Extracted so the JIT only loads Velopack.dll (referenced by UpdateService)
    // when we're actually running outside an MSIX package.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void StartBackgroundUpdateCheck()
        => _ = new UpdateService().CheckForUpdatesAsync(Logger);

    /// <summary>
    /// Disposes the current DI container (closing its database connection) AND clears
    /// <see cref="Services"/>, returning the app to the same "no database open" state it is in at
    /// startup. Used when a database is closed to open another: leaving a disposed container behind
    /// would make every later <c>App.Services?.GetService(...)</c> throw
    /// <see cref="ObjectDisposedException"/> instead of reading "nothing open" — which is what the next
    /// attempt after a failed switch (and the wizard's join path) rely on. Safe to call when no
    /// container exists or it is already disposed.
    /// </summary>
    public static void ReleaseServices()
    {
        (Services as IDisposable)?.Dispose();
        Services = null!;
    }

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
    /// <param name="dbPath">Path to the local database file: D' (writer) or D'' (reader).</param>
    /// <param name="restoreAcademicYearId">Academic year ID to restore. Defaults to the AppSettings value.</param>
    /// <param name="restoreSemesterIds">Semester IDs to restore. Defaults to the AppSettings value.</param>
    /// <returns>The root view model, with the services built over <paramref name="dbPath"/>.</returns>
    /// <exception cref="DatabaseInUseException">
    /// The local copy could not be examined on any of <see cref="LocalCopyIntegrity.MaxAttempts"/> attempts (another
    /// program on this computer has it open). The services have been built but nothing was read; the caller must
    /// release them if it gives up (<see cref="ReleaseServices"/>).
    /// </exception>
    /// <exception cref="DatabaseCorruptException">The local copy shows positive evidence of damage (see above for the services).</exception>
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

        // Integrity gate on the LOCAL copy. dbPath is always the local copy — D' (writer) or D'' (reader): since
        // spec item 4a nothing opens the shared D, and every caller (startup, switch, the wizard) passes the
        // copy that the checkout made. The gate is three-way (spec item 5, step 5d): Ok opens; a copy that
        // another program holds open (antivirus checking a file that was just copied) is retried quietly,
        // 3 attempts a second apart, and then reported as DatabaseInUseException; only positive evidence of
        // damage is DatabaseCorruptException. Both are handled by MainWindow, which decides what to do next.
        // Both probes are deadline-bound: on timeout the check is SKIPPED and the open proceeds — the checkout
        // flow has its own deadlines and surfaces the standard "network unreachable" UX, and a verdict is only
        // meaningful when the file is readable anyway. Blocking on them is safe and bounded: NetworkFileOps
        // chains ConfigureAwait(false) throughout, so nothing re-enters the dispatcher and each wait caps at
        // the deadline (plus at most two one-second pauses between the quiet retries).
        var (existsCompleted, dbExists) =
            NetworkFileOps.ExistsAsync(dbPath).GetAwaiter().GetResult();
        if (existsCompleted && dbExists)
        {
            LocalCopyIntegrity.VerifyAtOpen(
                dbPath, path => LocalCopyIntegrity.CheckOnce(path, IntegrityFaults, Logger), Logger);
        }
        else if (!existsCompleted)
        {
            Logger.LogInfo("App: database existence probe timed out — skipping integrity check (location unreachable).");
        }

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

    /// <summary>
    /// The <see cref="AppSettings.FocusByDatabase"/> key for the database that is open now:
    /// <see cref="CheckoutService.NormalizeSourcePath"/> of the SHARED database path (D, from
    /// <see cref="CheckoutService.SourcePath"/>), never the local working copy. Returns null (an in-memory
    /// focus that is never saved) if the checkout has no source path, which no real open path does: it keeps an
    /// unexpected state from crashing service resolution.
    /// </summary>
    private static string? DesktopFocusDatabaseKey() =>
        string.IsNullOrEmpty(Checkout.SourcePath) ? null : CheckoutService.NormalizeSourcePath(Checkout.SourcePath);

    private static void ConfigureServices(IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IAppLogger>(App.Logger);

        services.AddSingleton<SemesterContext>();
        services.AddSingleton(App.LockService);
        services.AddSingleton<BackupService>();
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<AcademicUnitService>();
        services.AddSingleton<SharedScheduleService>();
        services.AddSingleton<SharedScheduleCsvParser>();
        services.AddSingleton<SharedScheduleCsvExporter>();
        services.AddSingleton<TourRunner>();
        services.AddTransient<IDialogService, DialogService>();

        // readOnly: any session that does not hold the write lock (reader / observer mode, the version-gate
        // or fallback snapshot D''). Checkout has already run by the time this is resolved, so Mode is final.
        // Mode, not IsReadOnlyMode: IsReadOnlyMode means "on a D'' snapshot", and a demoted writer is read-only before
        // its snapshot exists. (It is no longer sticky: every checkout clears it, 1.2.4 items 3.3 / 4.5.)
        services.AddSingleton<IDatabaseContext>(_ => new DatabaseContext(
            dbPath, App.Checkout.MarkDirty, readOnly: App.Checkout.Mode != CheckoutMode.WriteAccess,
            onEveryWrite: App.Checkout.NoteEdit));   // counts every edit so a save can tell one arrived while it ran (spec item 3.2)
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
        services.AddSingleton<ISchedulingNoteRepository, SchedulingNoteRepository>();
        services.AddSingleton<ICampusRepository, CampusRepository>();
        services.AddSingleton<IMeetingRepository, MeetingRepository>();
        services.AddSingleton<ISectionCodePatternRepository, SectionCodePatternRepository>();
        services.AddSingleton<IProgramWatchRepository, ProgramWatchRepository>();

        // Owns the empty / delete semester and academic year rules (spec item 8).
        services.AddSingleton<SemesterContentsService>();

        // Subject focus (spec item 20), remembered per SHARED database. The key is read when the service is first
        // resolved: every path that builds this container (startup, database switch, the wizard, reader mode) has
        // already run CheckoutAsync, which sets Checkout.SourcePath to D, so it names the right database.
        RegisterFocusService(services, DesktopFocusDatabaseKey);

        // Subject colours (colour-by-subject feature): one store over settings.json; palette read from AppColors.axaml.
        RegisterSubjectColorStore(services);
        RegisterSubjectColorPresenter(services);

        RegisterViewModels(services);

        services.AddTransient<SaveAndBackupViewModel>();
        services.AddTransient<NewDatabaseViewModel>();

#if DEBUG
        // The generator class only exists in Debug builds (#if DEBUG in
        // DebugTestDataGenerator.cs). DebugTestDataViewModel itself stays registered in
        // all builds — its debug-only members are compiled out, leaving an empty shell —
        // so the Dev Tools menu (Ctrl+Shift+D) remains functional in Release.
        services.AddTransient<DebugTestDataGenerator>();
#endif
        services.AddTransient<DebugTestDataViewModel>();
        services.AddTransient<MigrationViewModel>();

        // Import Sections (spec item 24, desktop only: the demo has no file system and no Import Sections menu
        // item, so ConfigureDemoServices registers none of these). The file reader, snapshot builder, writer and
        // service are stateless over the singleton repositories; the host and platform are the flyout's narrow
        // seams onto the main window (see IImportSectionsHost, IImportSectionsPlatform).
        services.AddTransient<ImportFileReader>();
        services.AddTransient<ImportSetupSnapshotBuilder>();
        services.AddTransient<SectionImportWriter>();
        services.AddTransient<SectionImportService>();
        services.AddTransient<IImportSectionsHost, ImportSectionsHost>();
        services.AddTransient<IImportSectionsPlatform, AvaloniaImportSectionsPlatform>();
        services.AddTransient<ImportSectionsViewModel>();
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

        TourActions = TourActionDefinitions.Build();
        TourCatalog.Initialize(Current!.Resources, TourActions);
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
#endif // BROWSER

    // ── Demo services (shared by WASM and desktop pre-wizard tour) ───────────

    /// <summary>
    /// Registers demo repositories and services into a service collection.
    /// Platform-neutral — all Demo* classes compile on all targets.
    /// </summary>
    private static void ConfigureDemoServices(IServiceCollection services)
    {
        services.AddSingleton<IAppLogger>(new ConsoleAppLogger());

        services.AddSingleton<SemesterContext>();
        services.AddSingleton<WriteLockService>();
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<AcademicUnitService>();
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
        services.AddSingleton<ISchedulingNoteRepository,      DemoSchedulingNoteRepository>();
        services.AddSingleton<ICampusRepository,              DemoCampusRepository>();
        services.AddSingleton<IMeetingRepository,             DemoMeetingRepository>();
        services.AddSingleton<ISectionCodePatternRepository,  DemoSectionCodePatternRepository>();
        services.AddSingleton<IProgramWatchRepository,       DemoProgramWatchRepository>();

        // Owns the empty / delete semester and academic year rules (spec item 8).
        services.AddSingleton<SemesterContentsService>();

        // Subject focus (spec item 20). A null key means the demo: it always starts at All and is never read from
        // or written to settings (the browser's settings do not survive a visit anyway).
        RegisterFocusService(services, () => null);

        // Subject colours (colour-by-subject feature): one store over settings.json; palette read from AppColors.axaml.
        RegisterSubjectColorStore(services);
        RegisterSubjectColorPresenter(services);

        RegisterViewModels(services);
    }

    /// <summary>
    /// Reads the subject-colour palette from the application resources: <c>SubjectPalette01</c>, <c>SubjectPalette02</c>,
    /// ... in <c>AppColors.axaml</c> (colours live there, never in code: CLAUDE.md), stopping at the first missing
    /// number. Adding a colour is adding the next numbered resource.
    /// </summary>
    /// <returns>The palette in order as <c>#RRGGBB</c> strings (alpha dropped).</returns>
    private static List<string> ReadSubjectPalette()
    {
        var palette = new List<string>();
        var resources = Current!.Resources;
        for (var n = 1; resources.TryGetResource($"SubjectPalette{n:00}", null, out var value)
                        && value is Avalonia.Media.Color c; n++)
            palette.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        return palette;
    }

    /// <summary>
    /// Registers <see cref="SubjectColorStore"/> as a singleton, built lazily (so the application resources exist)
    /// with the palette from <see cref="ReadSubjectPalette"/>. Shared by the desktop and demo registrations.
    /// </summary>
    /// <param name="services">The collection to register into.</param>
    private static void RegisterSubjectColorStore(IServiceCollection services) =>
        services.AddSingleton(_ => new SubjectColorStore(ReadSubjectPalette()));

    /// <summary>
    /// Registers <see cref="SubjectColorPresenter"/> as a singleton: the one place both views ask "are colours on?"
    /// and "what is this section's colour?". Shared by the desktop and demo registrations.
    /// </summary>
    /// <param name="services">The collection to register into.</param>
    private static void RegisterSubjectColorPresenter(IServiceCollection services) =>
        services.AddSingleton(sp => new SubjectColorPresenter(
            sp.GetRequiredService<SubjectColorStore>(),
            // A constructor dependency so FocusService is built, and subscribes to SectionsChanged, first.
            sp.GetRequiredService<FocusService>(),
            sp.GetRequiredService<SectionStore>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<ISubjectRepository>()));

    /// <summary>
    /// Registers <see cref="FocusService"/> as a singleton over whichever repositories the container holds. Shared by
    /// the desktop and demo registrations so the constructor wiring exists once.
    /// </summary>
    /// <param name="services">The collection being configured.</param>
    /// <param name="databaseKey">
    /// Produces the settings key for the open database, called when the service is first resolved; null for the
    /// demo (see <see cref="FocusService"/>).
    /// </param>
    private static void RegisterFocusService(IServiceCollection services, Func<string?> databaseKey)
    {
        services.AddSingleton(sp => new FocusService(
            sp.GetRequiredService<ISubjectRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<ISectionRepository>(),
            sp.GetRequiredService<ISemesterRepository>(),
            sp.GetRequiredService<IAcademicYearRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<SectionStore>(),
            databaseKey()));
    }

    /// <summary>
    /// Builds an isolated demo DI container without touching <see cref="Services"/>.
    /// Used by the desktop pre-wizard tour to show populated demo data.
    /// </summary>
    internal static IServiceProvider BuildDemoServiceProvider()
    {
        var services = new ServiceCollection();
        ConfigureDemoServices(services);
        return services.BuildServiceProvider();
    }

    // ── Shared ViewModel registration ────────────────────────────────────────

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddSingleton<SectionStore>();
        services.AddSingleton<MeetingStore>();
        services.AddSingleton<GridChangeNotifier>();
        services.AddSingleton<AccessPanelViewModel>(sp => new AccessPanelViewModel(
            sp.GetRequiredService<IProgramWatchRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<GridChangeNotifier>(),
            sp.GetRequiredService<ISchedulingEnvironmentRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            // Watches are saved only while the write lock is ours; a reader keeps them in memory (see the class).
            () => sp.GetRequiredService<WriteLockService>().IsWriter));
        // Shared by every indicator and the Focus picker (spec item 20); exposed as MainWindowViewModel.FocusVm.
        services.AddSingleton<FocusViewModel>();
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
            sp.GetRequiredService<SharedScheduleService>(),
            sp.GetRequiredService<AccessPanelViewModel>(),
            // A constructor dependency so FocusService is built, and subscribes to SectionsChanged, first.
            sp.GetRequiredService<FocusService>(),
            // Likewise, so the presenter drops its caches on SectionsChanged/FocusChanged before the grid reloads.
            sp.GetRequiredService<SubjectColorPresenter>(),
            // Read each time the day-header menu opens (both ConfigureServices and ConfigureDemoServices register it).
            sp.GetRequiredService<IBlockPatternRepository>()));
        services.AddSingleton<WorkloadPanelViewModel>(sp => new WorkloadPanelViewModel(
            sp.GetRequiredService<IInstructorRepository>(),
            sp.GetRequiredService<ISectionRepository>(),
            sp.GetRequiredService<ICourseRepository>(),
            sp.GetRequiredService<IReleaseRepository>(),
            sp.GetRequiredService<ISchedulingNoteRepository>(),
            sp.GetRequiredService<ISemesterRepository>(),
            sp.GetRequiredService<SemesterContext>(),
            sp.GetRequiredService<SectionStore>(),
            // A constructor dependency so FocusService is built, and subscribes to SectionsChanged, first.
            sp.GetRequiredService<FocusService>()));
        services.AddTransient<InstructorListViewModel>(sp =>
            new InstructorListViewModel(
                sp.GetRequiredService<IInstructorRepository>(),
                sp.GetRequiredService<ISchedulingEnvironmentRepository>(),
                sp.GetRequiredService<ISectionRepository>(),
                sp.GetRequiredService<ICourseRepository>(),
                sp.GetRequiredService<IReleaseRepository>(),
                sp.GetRequiredService<IInstructorCommitmentRepository>(),
                sp.GetRequiredService<ISchedulingNoteRepository>(),
                sp.GetRequiredService<ISemesterRepository>(),
                sp.GetRequiredService<IAcademicYearRepository>(),
                sp.GetRequiredService<SemesterContext>(),
                sp.GetRequiredService<GridChangeNotifier>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<WriteLockService>(),
                sp.GetRequiredService<WorkloadMailerViewModel>(),
                // A constructor dependency so FocusService is built, and subscribes to SectionsChanged, first.
                sp.GetRequiredService<FocusService>()));

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
        services.AddTransient<WorkflowsViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<ShareViewModel>();

        services.AddTransient<CourseHistoryView>();
    }
}
