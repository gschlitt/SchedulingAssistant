using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using TermPoint.Controls;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Exceptions;
using TermPoint.Services;
using TermPoint.ViewModels;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using TermPoint.Views;
using TermPoint.Views.GridView;
using TermPoint.Views.Wizard;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Reflection;

namespace TermPoint;

public partial class MainWindow : Window, ILifecyclePrompts, ISessionHost
{
    private DetachedPanelWindow? _sectionViewWindow;
    private DetachedPanelWindow? _workloadWindow;
    private DetachedPanelWindow? _scheduleGridWindow;

    // ── Loading curtain ──────────────────────────────────────────────────────
    // The curtain (a full-window gradient overlay defined in MainWindow.axaml)
    // covers MainView from app launch until the schedule grid renders its first
    // frame, so the user never sees the window assemble itself. See LiftCurtainAsync.
    private DateTime _curtainShownAt = DateTime.UtcNow;
    private bool _curtainLifted;
    private DispatcherTimer? _curtainSafetyTimer;
    private EventHandler<SizeChangedEventArgs>? _curtainGridHandler;

    /// <summary>Minimum time the loading curtain stays up, to avoid a flicker on fast loads.</summary>
    private static readonly TimeSpan CurtainMinDisplay = TimeSpan.FromMilliseconds(600);

    /// <summary>Fade-out duration; must match the DoubleTransition on LoadingCurtain in the AXAML.</summary>
    private static readonly TimeSpan CurtainFadeDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>Backstop: lift the curtain even if the grid never reports a first layout pass.</summary>
    private static readonly TimeSpan CurtainSafetyTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// True from the moment <see cref="SetupMainWindowAsync"/> finishes wiring a database to the window
    /// until the next switch starts taking it apart (or its wiring fails part-way). Tracked explicitly,
    /// never inferred from <c>App.Services</c> or <c>DataContext</c>: whether a refused or failed switch
    /// may leave the user on the current database, or must send them to the database chooser because
    /// nothing is open, hangs on it (see <see cref="DatabaseSwitchRecovery.Decide"/>). False at launch,
    /// during the startup wizard, and after any failed switch.
    /// </summary>
    private bool _isDatabaseOpen;

    /// <summary>
    /// The database-session lifecycle (open, switch, recovery at open), which lives in
    /// <see cref="SessionLifecycle"/> and reaches this window only through <see cref="ILifecyclePrompts"/> and
    /// <see cref="ISessionHost"/>. Built on first use, not in the constructor: the window is also constructed by
    /// the XAML previewer, and building the lifecycle touches <c>App</c>'s static services (logger, lock and
    /// checkout), which the constructor never did before. The logger and settings are passed as getters because
    /// both are replaced at runtime.
    /// </summary>
    private SessionLifecycle? _lifecycleInstance;

    /// <summary>The session lifecycle, created on first use (see <see cref="_lifecycleInstance"/>).</summary>
    private SessionLifecycle Lifecycle => _lifecycleInstance ??= new SessionLifecycle(
        App.Checkout, App.InstanceGuard, () => App.Logger, () => AppSettings.Current,
        App.EvaluateLicense, () => App.IntegrityFaults, this, this);

    public ScheduleGridView? ScheduleGridViewInstance => MainViewControl?.ScheduleGridViewInstance;

    public MainWindow()
    {
        InitializeComponent();

        // The title shows Major.Minor.Patch ("TermPoint v1.2.3"), plus the fourth number when it isn't 0, so a
        // field-test build such as 1.2.3.1 can be told apart from the release at a glance.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var shown = version is null ? "0.0.0" : version.ToString(version.Revision > 0 ? 4 : 3);
        Title = $"TermPoint v{shown}";



        // Ctrl+S is handled here to save editors in management views (works globally).
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    if (TrySaveEditor(vm.SectionListVm?.EditVm?.SaveCommand, ref e)) return;
                    if (TrySaveEditor(vm.MeetingListVm?.EditVm?.SaveCommand, ref e)) return;

                    if (vm.FlyoutPage is CourseListViewModel courseVm)
                    {
                        if (TrySaveEditor(courseVm.EditVm?.SaveCommand, ref e)) return;
                        if (TrySaveEditor(courseVm.SubjectEditVm?.SaveCommand, ref e)) return;
                    }
                    else if (vm.FlyoutPage is InstructorListViewModel instructorVm)
                    {
                        if (TrySaveEditor(instructorVm.EditVm?.SaveCommand, ref e)) return;
                    }
                    else if (vm.FlyoutPage is SchedulingEnvironmentViewModel schedEnvVm)
                    {
                        if (schedEnvVm.SelectedCategory is SchedulingEnvironmentListViewModel listVm)
                            if (TrySaveEditor(listVm.EditVm?.SaveCommand, ref e)) return;
                        if (schedEnvVm.SelectedCategory is RoomListViewModel roomVm)
                            if (TrySaveEditor(roomVm.EditVm?.SaveCommand, ref e)) return;
                        if (schedEnvVm.SelectedCategory is CampusListViewModel campusVm)
                            if (TrySaveEditor(campusVm.EditVm?.SaveCommand, ref e)) return;
                    }
                }
            }

            // Ctrl+Shift+D toggles the Dev Tools menu (all builds, session-only).
            if (e.Key == Key.D
                && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
                && DataContext is MainWindowViewModel dtVm)
            {
                dtVm.IsDevToolsVisible = !dtVm.IsDevToolsVisible;
                e.Handled = true;
            }

#if DEBUG
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
                && DataContext is MainWindowViewModel debugVm)
            {
                if (e.Key == Key.E)
                {
                    debugVm.ScheduleGridVm.SimulateReloadError();
                    e.Handled = true;
                }
                else if (e.Key == Key.W)
                {
                    debugVm.SectionListVm.SimulateLoadError();
                    e.Handled = true;
                }
                else if (e.Key == Key.T)
                {
                    // Re-scan AXAML resources so HotAvalonia edits take effect immediately
                    TourCatalog.Reset();
                    TourCatalog.Initialize(Application.Current!.Resources, App.TourActions);

                    if (App.Services.GetService(typeof(TourRunner)) is TourRunner runner)
                        runner.Start("desktop-tour");

                    e.Handled = true;
                }
            }
#endif
        };
    }

    /// <summary>
    /// Executes a save command if it exists and can currently execute.
    /// Returns true if the command was executed (so the caller can short-circuit).
    /// </summary>
    private static bool TrySaveEditor(System.Windows.Input.ICommand? saveCommand, ref KeyEventArgs e)
    {
        if (saveCommand?.CanExecute(null) != true) return false;
        saveCommand.Execute(null);
        e.Handled = true;
        return true;
    }

    // Prevents double-close when OnClosing cancels itself to run async shutdown.
    private bool _shuttingDown;

    /// <summary>
    /// Called whenever the window is about to close — whether via Files → Exit or the title-bar X.
    /// Cancels the first close event to run async shutdown (save to D, release lock), then
    /// re-triggers Close() once that work is done.
    /// </summary>
    /// <param name="e">Closing event args; set <c>e.Cancel = true</c> to defer the close.</param>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_shuttingDown) return; // Second pass — let it close.

        // A restore is saving the user's changes and replacing the database right now. Closing the window
        // mid-way would race the restore with the exit save and teardown, so refuse; the restore closes the
        // window itself when it is finished (or the user can close it once it has reported a failure).
        if (Lifecycle.IsRestoreInProgress)
        {
            e.Cancel = true;
            App.Logger.LogInfo("MainWindow: close request ignored — a restore from backup is in progress.");
            return;
        }

        // Same rule while the user's choice from the save-conflict banner is ending the session: the changes
        // are being saved elsewhere or discarded, and a close now would race that teardown.
        if (Lifecycle.IsSaveConflictResolutionInProgress)
        {
            e.Cancel = true;
            App.Logger.LogInfo("MainWindow: close request ignored — a save-conflict resolution is in progress.");
            return;
        }

        e.Cancel = true; // Defer: run async save first.
        _shuttingDown = true;

        await Lifecycle.TeardownForCloseAsync();

        Close(); // Re-trigger OnClosing; _shuttingDown is now true so it falls through.
    }   
    
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Never boot the real app inside the Avalonia XAML previewer. The previewer
        // (Avalonia.Designer.HostApp) loads this assembly to render .axaml files in a
        // separate VS-owned process; without this guard it runs the full startup path
        // (DB checkout → WriteLockService.TryAcquire), acquiring and heartbeating the
        // write lock on the user's REAL database and leaving an orphaned .lock file.
        if (Avalonia.Controls.Design.IsDesignMode) return;

        try
        {
            await RunStartupAsync();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Unhandled exception during startup");
            await ShowStartupErrorAsync(ex);
            (Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
        }
    }
    /// <summary>
    /// Startup with one of two paths: a brand-new user setting up for the first time
    /// and a returning user
    /// </summary>
    /// <returns></returns>
    private async Task RunStartupAsync()
    {
        var settings = AppSettings.Current;

        // ── First-run wizard ──────────────────────────────────────────────────
        // When IsInitialSetupComplete is false, show the wizard instead of the normal
        // DB-path dialog. The wizard calls App.InitializeServices() internally (step 2),
        // so we must not call it again below.
        if (!settings.IsInitialSetupComplete)
        {
            // Run the pre-wizard demo tour so the user sees a populated schedule
            // before the setup wizard. The tour is skippable and uses an isolated
            // demo DI container that is disposed when the tour ends.
            if (!settings.CompletedTourKeys.Contains("desktop-tour"))
            {
                try
                {
                    await RunPreWizardTourAsync();
                }
                catch (Exception ex)
                {
                    App.Logger?.LogInfo($"[Tour] Pre-wizard tour failed: {ex.Message}");
                }
            }

            var wizard = new StartupWizardWindow();
            WindowState = WindowState.Minimized;
            wizard.Show(this);

            // RunContinuationsAsynchronously is load-bearing: without it, TrySetResult runs
            // the await-continuation INLINE on the wizard's Closed → Window.Close() callstack,
            // so the whole post-wizard checkout (SwitchDatabaseAsync) executes nested inside
            // that button-click dispatcher job and then suspends mid-job at the first network
            // await. A dispatcher job that suspends without completing wedges the job pump — the
            // UI keeps servicing input but never runs another posted continuation, hanging the
            // app on the loading curtain when the database is remote (a local DB completes the
            // await synchronously, so it never suspends and never trips the wedge). With this
            // flag the continuation is queued as its own top-level job that runs after the
            // wizard fully closes — matching the returning-user path, which already works.
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Completion path: the wizard HIDES itself and raises SetupCompleted (no Close → no
            // composition disposal → no compositor deadlock). Cancel / manual-close path: the
            // window actually closes. Either signal proceeds; we then check IsInitialSetupComplete.
            wizard.SetupCompleted += (_, _) => tcs.TrySetResult();
            wizard.Closed += (_, _) => tcs.TrySetResult();
            await tcs.Task;

            WindowState = WindowState.Normal;

            // Reload settings — the wizard saves DatabasePath and sets IsInitialSetupComplete.
            AppSettings.Load();
            settings = AppSettings.Current;

            if (!settings.IsInitialSetupComplete)
            {
                // User closed the wizard before finishing — exit the app.
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }

            // Wizard completed. New-database path: the wizard created D, checked it out to D',
            // and initialized services against D'. SwitchDatabaseAsync will save D' → D
            // (releasing the checkout), then re-checkout D → new D' and reinitialize services.
            // This is a redundant cycle but correct for a one-time operation.
            // Existing-database (join) path: the wizard only VALIDATED D (without opening it
            // with SQLite) and saved the settings — no checkout, lock or services exist yet, so
            // SwitchDatabaseAsync does the one and only real checkout. It must therefore
            // tolerate a null App.Services (see its remarks).
            // On the new-database path the database was just created, so it opens for editing
            // even if the "open as observer" setting is on (the setting applies to later opens).
            await SwitchDatabaseAsync(settings.DatabasePath!, isNewDatabase: wizard.CreatedNewDatabase);
            return;
        }

        // ── Returning user ────────────────────────────────────────────────────
        await RunReturningUserStartupAsync(settings);
    }

    /// <summary>
    /// Runs the desktop tour with demo data before the startup wizard.
    /// Builds an isolated demo DI container, swaps <see cref="Window.DataContext"/>
    /// to a demo <see cref="MainWindowViewModel"/>, runs the tour, awaits completion
    /// or dismissal, then tears down the demo container. The real <see cref="App.Services"/>
    /// is not yet initialized at this point, so there is nothing to preserve.
    /// </summary>
    private async Task RunPreWizardTourAsync()
    {
        var demoProvider = App.BuildDemoServiceProvider();

        try
        {
            // Initialize demo data (mirrors the WASM InitializeDemoServices path)
            var tourActions = TourActionDefinitions.Build();
            TourCatalog.Initialize(Avalonia.Application.Current!.Resources, tourActions);
            foreach (var err in TourCatalog.Validate())
                App.Logger?.LogInfo($"[TourCatalog] {err}");

            demoProvider.GetRequiredService<WriteLockService>().AcquireDemo();

            var semCtx = demoProvider.GetRequiredService<SemesterContext>();
            semCtx.Reload(
                demoProvider.GetRequiredService<IAcademicYearRepository>(),
                demoProvider.GetRequiredService<ISemesterRepository>(),
                restoreAcademicYearId: "demo-ay-1",
                restoreSemesterIds: new HashSet<string> { "demo-sem-1" });

            var store = demoProvider.GetRequiredService<SectionStore>();
            store.Reload(
                demoProvider.GetRequiredService<ISectionRepository>(),
                semCtx.SelectedSemesters.Select(s => s.Semester.Id));

            // Swap DataContext to the demo VM and make the window visible
            var demoVm = demoProvider.GetRequiredService<MainWindowViewModel>();
            demoVm.SetDatabaseName("Tour Preview");
            DataContext = demoVm;
            IsVisible = true;
            Activate();

            // Hide the loading curtain so the tour overlay is visible and interactive.
            // Without this the opaque gradient sits on top of the tour, blocking all
            // user interaction, and the tour can never complete — hanging the app.
            if (LoadingCurtain is not null)
            {
                LoadingCurtain.IsHitTestVisible = false;
                LoadingCurtain.Opacity = 0;
                LoadingCurtain.IsVisible = false;
            }

            // Start the tour on the next UI tick so layout has settled
            var runner = demoProvider.GetRequiredService<TourRunner>();
            var tcs = new TaskCompletionSource();
            void onDone() => tcs.TrySetResult();
            runner.TourCompleted += onDone;
            runner.TourDismissed += onDone;

            Dispatcher.UIThread.Post(() =>
            {
                if (!runner.Start("desktop-tour"))
                    tcs.TrySetResult();
            }, DispatcherPriority.Background);

            await tcs.Task;
            runner.TourCompleted -= onDone;
            runner.TourDismissed -= onDone;

            // Re-show the loading curtain before transitioning to the wizard so the
            // window doesn't flash bare content while minimized/restoring.
            if (LoadingCurtain is not null)
            {
                LoadingCurtain.Opacity = 1;
                LoadingCurtain.IsHitTestVisible = true;
                LoadingCurtain.IsVisible = true;
            }

            // Minimize and clear the demo VM before transitioning to the wizard.
            // Don't set IsVisible = false — the wizard needs a visible owner window.
            WindowState = WindowState.Minimized;
            DataContext = null;
        }
        finally
        {
            (demoProvider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Shows the database chooser window and waits for the user to make a selection.
    /// Returns the resolved database path, or null if the user exited or chose to
    /// start the wizard (in which case the wizard is already routed to).
    /// </summary>
    /// <param name="mode">Normal chooser or recovery mode.</param>
    /// <param name="reason">Why recovery was triggered (null in Normal mode).</param>
    /// <param name="lastKnownPath">The path from settings, shown in recovery mode.</param>
    private async Task<string?> ShowDatabaseChooserAsync(
        ChooserMode mode, RecoveryReason? reason, string? lastKnownPath)
    {
        var settings = AppSettings.Current;
        var chooser = new DatabaseChooserWindow(mode, reason, lastKnownPath);

        // Show non-modally + await ChooserCompleted.
        // ShowDialog(this) on an invisible owner corrupts the Avalonia
        // dispatcher — subsequent awaits that yield to the thread pool
        // never get their continuations dispatched back to the UI thread.
        //
        // The chooser window never closes — it always hides (both on VM completion
        // and on X-button) to avoid the Avalonia 12 compositor deadlock. It raises
        // ChooserCompleted in both cases; we then check Vm.Outcome.
        WindowState = WindowState.Minimized;
        chooser.Show(this);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        chooser.ChooserCompleted += (_, _) => tcs.TrySetResult();
        await tcs.Task;

        WindowState = WindowState.Normal;

        switch (chooser.Vm.Outcome)
        {
            case ChooserOutcome.Resolved:
                var dbPath = chooser.Vm.ResolvedPath!;
                settings.DatabasePath = dbPath;
                settings.Save();
                return dbPath;

            case ChooserOutcome.StartWizard:
                settings.IsInitialSetupComplete = false;
                settings.DatabasePath           = null;
                settings.Save();
                await RunStartupAsync();
                return null;

            default: // ChooserOutcome.None — user exited
                await ShowCancelMessageAsync();
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return null;
        }
    }

    /// <summary>
    /// Normal startup path for a returning user who already has a database configured.
    /// When AlwaysOpenMostRecentDatabase is true, validates and opens the saved path
    /// directly. Otherwise shows the database chooser so the user can pick from recent
    /// databases, browse, create new, or restore. Falls back to the chooser in recovery
    /// mode if the saved database is missing or corrupt.
    ///
    /// <para><b>Order once a database is chosen (spec item 4b):</b> (1) take the single-instance guard
    /// (<see cref="DatabaseInstanceGuard"/>; if another live TermPoint of this user holds it: message, then
    /// the chooser again, nothing else runs); (2) record the recent database and evaluate the license;
    /// (3) <see cref="SessionLifecycle.CleanUpAndOfferRecoveryAsync"/> — stale-artifact sweep, crash-recovery offer;
    /// (4) <see cref="SessionLifecycle.RunCheckoutAsync"/> (write lock and copy; an orphaned D.tmp is deleted inside it, and
    /// only once write access is confirmed); (5) services and window. The guard is released when the app
    /// exits (<see cref="OnClosing"/>).</para>
    ///
    /// <para><b>Steps (1)–(5) are <see cref="SessionLifecycle.OpenStartupDatabaseAsync"/>, run in a loop (spec item 5, step 5d):</b>
    /// when the shared database turns out to be damaged, the recovery chooser is shown and whatever the user picks
    /// there is opened by running steps (1)–(5) again for it, rather than by a second copy of them.</para>
    /// </summary>
    private async Task RunReturningUserStartupAsync(AppSettings settings)
    {
        string dbPath;

        if (settings.AlwaysOpenMostRecentDatabase)
        {
            // Auto-open mode — validate the saved path and proceed if OK.
            var validation = await DatabaseValidator.ValidateAsync(settings.DatabasePath);
            if (validation == DatabaseValidationResult.Ok)
            {
                dbPath = settings.DatabasePath!;
            }
            else if (validation == DatabaseValidationResult.Unreachable)
            {
                await ShowMessageAsync("Network Unreachable", NetworkFileOps.UnreachableMessage);
                Close();
                return;
            }
            else
            {
                // Saved DB is missing, corrupt or unreadable (held open by another program /
                // access denied) — fall through to the chooser in recovery mode. Unreadable has
                // its own reason: it is never presented as corrupt or not found.
                var reason = DatabaseChooserViewModel.RecoveryReasonFor(validation);

                var result = await ShowDatabaseChooserAsync(ChooserMode.Recovery, reason, settings.DatabasePath);
                if (result is null) return; // user exited or wizard routed
                dbPath = result;
            }
        }
        else
        {
            // Even in chooser mode, validate the saved path so we can warn the user
            // if their most recent database has gone missing, is corrupt, can't be read
            // right now (held open by another program), or its network location is unreachable.
            var validation = await DatabaseValidator.ValidateAsync(settings.DatabasePath);
            var mode = ChooserMode.Normal;
            RecoveryReason? reason = null;

            if (validation != DatabaseValidationResult.Ok
                && !string.IsNullOrEmpty(settings.DatabasePath))
            {
                mode   = ChooserMode.Recovery;
                reason = DatabaseChooserViewModel.RecoveryReasonFor(validation);
            }

            var result = await ShowDatabaseChooserAsync(mode, reason, settings.DatabasePath);
            if (result is null) return; // user exited or wizard routed
            dbPath = result;
        }

        // Open it. An open can hand back ANOTHER database to open (the pick from the recovery chooser that follows
        // a damaged shared database), which goes through exactly the same steps; null means finished — opened, or
        // the app is closing.
        string? next = dbPath;
        while (next is not null)
            next = await Lifecycle.OpenStartupDatabaseAsync(next, settings);
    }

    /// <summary>
    /// Final step of every startup path: wires the main window VM and makes the window visible.
    /// Always receives a fully-initialized <paramref name="vm"/> from the caller — the wizard,
    /// returning-user, and database-switch paths all call <see cref="SwitchDatabaseAsync"/> or
    /// <see cref="App.InitializeServices"/> first and pass the result directly.
    /// </summary>
    private async Task SetupMainWindowAsync(string dbPath, MainWindowViewModel vm)
    {
        App.Logger.LogBreadcrumb("MainWindow ready", new() { ["mode"] = App.Checkout.Mode.ToString() });

        // Start the automated backup session if this instance acquired the write lock.
        // Also wire the backup service into CheckoutService so pre-save snapshots use
        // the same service and naming convention as regular backups.
        // Fire-and-forget: the first backup runs asynchronously in the background.
        if (App.Services.GetService(typeof(BackupService)) is BackupService backup
            && App.Services.GetService(typeof(WriteLockService)) is WriteLockService wl
            && wl.IsWriter)
        {
            _ = backup.StartSessionAsync(dbPath);
        }

        DataContext = vm;

        // Wire CheckoutService events so the VM and window can react to save
        // results and session timeouts. Unsubscribe any previous handlers first
        // to avoid accumulation across database switches.
        App.Checkout.SaveCompleted   -= OnCheckoutSaveCompleted;
        App.Checkout.SaveFailed      -= OnCheckoutSaveFailed;
        App.Checkout.SaveConflictDetected -= OnCheckoutSaveConflictDetected;
        App.Checkout.WriteLockLost -= OnWriteLockLost;
        App.Checkout.BecameDirty     -= OnCheckoutBecameDirty;
        App.Checkout.SaveStarted     -= OnCheckoutSaveStarted;
        App.Checkout.SaveFinished    -= OnCheckoutSaveFinished;
        App.Checkout.SaveAlreadyInProgress -= OnCheckoutSaveAlreadyInProgress;
        App.Checkout.SaveCompleted   += OnCheckoutSaveCompleted;
        App.Checkout.SaveFailed      += OnCheckoutSaveFailed;
        App.Checkout.SaveConflictDetected += OnCheckoutSaveConflictDetected;
        App.Checkout.WriteLockLost += OnWriteLockLost;
        App.Checkout.BecameDirty     += OnCheckoutBecameDirty;
        App.Checkout.SaveStarted     += OnCheckoutSaveStarted;
        App.Checkout.SaveFinished    += OnCheckoutSaveFinished;
        App.Checkout.SaveAlreadyInProgress += OnCheckoutSaveAlreadyInProgress;

        // Start autosave if it was enabled in settings.
        if (AppSettings.Current.AutoSaveEnabled)
            App.Checkout.StartAutoSave();

        // Set the main window reference and load recent databases
        vm.MainWindowReference = this;
        vm.LoadRecentDatabases();

        // Enqueue any feature announcements the user hasn't seen yet.
        if (App.Services.GetService(typeof(AppNotificationService)) is AppNotificationService notifier)
        {
            // If we took write access by reclaiming a crashed previous session on this machine,
            // let the user know it was recovered automatically (no 180s wait, no prompt).
            if (App.Checkout.ReclaimedDeadSession)
                notifier.Enqueue(new AppNotification
                {
                    Message          = "Recovered the lock from a previous session that closed unexpectedly.",
                    Severity         = NotificationSeverity.Info,
                    AutoDismissAfter = TimeSpan.FromSeconds(8)
                });

            // Licensing notification — shown when the user is in trial, expired, or unlicensed.
#if !BROWSER
            var ls = App.LicenseStatus;
            if (ls.Reason == Licensing.AccessReason.Trial && ls.DaysRemaining.HasValue)
                notifier.Enqueue(new AppNotification
                {
                    Message  = $"You have {ls.DaysRemaining} days remaining in your trial.",
                    Severity = NotificationSeverity.Info,
                    LinkText = "Purchase a license",
                    LinkUrl  = "https://termpoint.ca/license"
                });
            else if (ls.Reason == Licensing.AccessReason.Expired)
                notifier.Enqueue(new AppNotification
                {
                    Message       = "Your license has expired. Editing is disabled.",
                    Severity      = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText      = "Purchase or renew",
                    LinkUrl       = "https://termpoint.ca/license"
                });
            else if (ls.Reason == Licensing.AccessReason.Unlicensed)
                notifier.Enqueue(new AppNotification
                {
                    Message       = "Your trial has ended. Editing is disabled.",
                    Severity      = NotificationSeverity.Warning,
                    IsDismissable = false,
                    LinkText      = "Purchase a license",
                    LinkUrl       = "https://termpoint.ca/license"
                });
#endif

            notifier.EnqueueUnseenAnnouncements();
        }

        // Explicitly restore the window. The wizard path minimizes it, and a window left
        // minimized here can stay stuck in the taskbar (clicking it won't expand) — the
        // classic symptom of a modal child opened on a minimized owner.
        WindowState = WindowState.Normal;
        IsVisible = true;
        Activate();
        // Lift the loading curtain after the ScheduleGrid completes its first layout pass.
        // SizeChanged fires after Avalonia measures and arranges the control; by then
        // ScheduleGridView's own SizeChanged handler has already called Render(), so the
        // canvas is fully populated before the curtain lifts. A safety timer is the backstop
        // in case no SizeChanged fires (empty schedule, or a database switch where the grid
        // size is unchanged) so the curtain can never get stuck up. Re-arming here means a
        // database switch from the Files menu also gets a clean curtain over the reload.
        ArmCurtainLift();

        // Evaluate tour auto-triggers after the window is visible and laid out.
        // Deferred to the next dispatcher tick so the overlay has valid bounds.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (App.Services.GetService(typeof(TourRunner)) is TourRunner tourRunner)
                tourRunner.EvaluateAutoTriggers();
        }, Avalonia.Threading.DispatcherPriority.Background);

        // Everything above succeeded: a database is now open and wired to the window. Set LAST, so
        // that a throw part-way through leaves it false and the caller's failure handling treats this
        // as "nothing open" (see DatabaseSwitchRecovery.Decide).
        _isDatabaseOpen = true;

        await Task.CompletedTask;
    }

    /// <summary>
    /// Arms the loading-curtain lift: shows the curtain, then waits for the schedule
    /// grid's first <see cref="Layoutable.SizeChanged"/> (its render-complete signal),
    /// with a safety timer as a backstop. Both routes call <see cref="LiftCurtainAsync"/>,
    /// which is idempotent. Safe to call again on a database switch — it re-shows the
    /// curtain over the reload.
    /// </summary>
    private void ArmCurtainLift()
    {
        // Re-show the curtain (covers the case where this is a database switch and the
        // curtain was already lifted on a previous load) and reset the timing/guard state.
        ShowCurtain();
        _curtainLifted = false;
        _curtainShownAt = DateTime.UtcNow;

        // First grid layout pass → lift.
        var gridView = MainViewControl?.ScheduleGridViewInstance;
        if (gridView is not null)
        {
            if (_curtainGridHandler is not null)
                gridView.SizeChanged -= _curtainGridHandler;

            _curtainGridHandler = (_, _) => _ = LiftCurtainAsync();
            gridView.SizeChanged += _curtainGridHandler;
        }

        // Backstop in case the grid never reports a size change.
        _curtainSafetyTimer?.Stop();
        _curtainSafetyTimer = new DispatcherTimer { Interval = CurtainSafetyTimeout };
        _curtainSafetyTimer.Tick += (_, _) => _ = LiftCurtainAsync();
        _curtainSafetyTimer.Start();
    }

    /// <summary>
    /// Makes the loading curtain fully opaque, visible and hit-testable, so it covers (and blocks input
    /// to) everything beneath it. Does not touch the lift state — see <see cref="ArmCurtainLift"/> (which
    /// arms a lift afterwards) and <see cref="CoverWindowForNoDatabase"/> (which disarms every lift).
    /// </summary>
    private void ShowCurtain()
    {
        if (LoadingCurtain is null) return;

        LoadingCurtain.IsVisible = true;
        LoadingCurtain.IsHitTestVisible = true;
        LoadingCurtain.Opacity = 1;
    }

    /// <summary>
    /// Covers the window for the "no database open" state that follows a failed database switch (spec
    /// item 4d), so nothing bound to the database that was just closed can be seen or clicked: clears
    /// the stale <see cref="Window.DataContext"/> and raises the loading curtain — the same cover the
    /// window wears at launch.
    ///
    /// <para>The curtain's pending lift is DISARMED (the grid's size-changed handler and the safety timer are
    /// removed and <c>_curtainLifted</c> is set, which turns <see cref="LiftCurtainAsync"/> into a no-op), so
    /// a late signal from the database that was just closed cannot uncover the window. The next successful
    /// open re-arms everything through <see cref="ArmCurtainLift"/>. Idempotent.</para>
    /// </summary>
    private void CoverWindowForNoDatabase()
    {
        DataContext = null;

        _curtainSafetyTimer?.Stop();
        _curtainSafetyTimer = null;

        var gridView = MainViewControl?.ScheduleGridViewInstance;
        if (gridView is not null && _curtainGridHandler is not null)
            gridView.SizeChanged -= _curtainGridHandler;
        _curtainGridHandler = null;

        _curtainLifted = true;
        ShowCurtain();
    }

    /// <summary>
    /// Fades out and hides the loading curtain. Idempotent: the grid signal and the
    /// safety timer race to call this, and only the first call has any effect. Honors a
    /// minimum display time so the curtain never flickers on a fast load.
    /// </summary>
    private async Task LiftCurtainAsync()
    {
        if (_curtainLifted) return;
        _curtainLifted = true;

        // Tear down both lift triggers so neither fires again.
        var gridView = MainViewControl?.ScheduleGridViewInstance;
        if (gridView is not null && _curtainGridHandler is not null)
            gridView.SizeChanged -= _curtainGridHandler;
        _curtainGridHandler = null;

        _curtainSafetyTimer?.Stop();
        _curtainSafetyTimer = null;

        if (LoadingCurtain is null) return;

        // Hold the curtain up for the minimum display time so fast loads don't flash.
        var elapsed = DateTime.UtcNow - _curtainShownAt;
        if (elapsed < CurtainMinDisplay)
            await Task.Delay(CurtainMinDisplay - elapsed);

        // Fade out (the DoubleTransition on LoadingCurtain animates the opacity change),
        // then collapse it once the fade has finished.
        LoadingCurtain.IsHitTestVisible = false;
        LoadingCurtain.Opacity = 0;
        await Task.Delay(CurtainFadeDuration);
        LoadingCurtain.IsVisible = false;
    }

    /// <summary>
    /// Switches to a different database without restarting the app. Handles the full
    /// release → checkout → re-initialize cycle. Called from the Files menu, from the
    /// startup wizard completion path, and from <c>AcquireWriteLock</c> in the ViewModel
    /// when upgrading from read-only to write mode.
    /// If the database file does not yet exist AND the caller passed <paramref name="createIfMissing"/>
    /// (File → New only), it is created with the schema before checkout — built locally and copied
    /// to the share by <see cref="NewDatabaseFile.CreateAsync"/>, never opened over the network — so
    /// that <see cref="CheckoutService.CheckoutAsync"/> takes the normal D → D' path and all writes go
    /// to the local working copy.
    ///
    /// <para>One-line delegate to <see cref="SessionLifecycle.SwitchDatabaseAsync"/>, which holds the full
    /// description of the pre-check, the single-instance guard, the failure recovery and the result.</para>
    /// </summary>
    /// <param name="newDatabasePath">Full path of the database (D) to switch to.</param>
    /// <param name="isNewDatabase">True when the caller has already created this database during the current user action (the startup wizard's new-database path); see <see cref="SessionLifecycle.SwitchDatabaseAsync"/>.</param>
    /// <param name="createIfMissing">True ONLY from File → New: a missing <paramref name="newDatabasePath"/> is then created.</param>
    /// <param name="resolveKeptChanges">Null for every caller except the save-conflict banner (<see cref="ResolveSaveConflictAsync"/>).</param>
    /// <returns><c>true</c> ONLY when the REQUESTED database ended up open; <c>false</c> on every refusal or failure (see <see cref="SessionLifecycle.SwitchDatabaseAsync"/>).</returns>
    public Task<bool> SwitchDatabaseAsync(
        string newDatabasePath, bool isNewDatabase = false, bool createIfMissing = false,
        Func<string, Task>? resolveKeptChanges = null)
        => Lifecycle.SwitchDatabaseAsync(newDatabasePath, isNewDatabase, createIfMissing, resolveKeptChanges);

    private Task ShowStartupErrorAsync(Exception ex)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TermPoint", "Logs");

        return ShowFatalAsync(
            this,
            "TermPoint — Startup Error",
            heading: "Scheduling Assistant encountered an error during startup and cannot continue.",
            detail:  ex.Message,
            footer:  $"A full error log has been written to:\n{logDir}");
    }

    /// <summary>
    /// Shows a modal fatal-error dialog as a standalone <see cref="Window"/>. Used in contexts
    /// where the DI container (and therefore <c>IDialogService</c>) is unavailable or already
    /// disposed — e.g. during startup or after a destructive restore/switch operation.
    /// Centralises the programmatic control construction so ViewModels don't need to reference
    /// <c>Avalonia.Controls</c> directly.
    /// </summary>
    /// <param name="owner">Window to parent the dialog to.</param>
    /// <param name="title">Window title bar text.</param>
    /// <param name="heading">Bold headline line at the top of the panel.</param>
    /// <param name="detail">Main error message shown in dark red.</param>
    /// <param name="footer">Optional grey footer line (e.g. log-file path). May be null.</param>
    public static async Task ShowFatalAsync(
        Window owner, string title, string heading, string detail, string? footer = null)
    {
        var msg = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            // Modal dialogs must be Topmost: they block all app input, so if one opens
            // beneath a Topmost sticky note (or loses an activation race and lands behind
            // the main window) the app appears frozen with nothing clickable.
            Topmost = true
        };

        var ok = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brushes.DarkRed
        });
        if (!string.IsNullOrEmpty(footer))
        {
            panel.Children.Add(new TextBlock
            {
                Text = footer,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 11,
                Foreground = Brushes.Gray
            });
        }
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => Dispatcher.UIThread.Post(() => msg.Close());
        await msg.ShowDialog(owner);
    }

    private async Task ShowCancelMessageAsync()
    {
        var msg = new Window
        {
            Title = "TermPoint",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16
        };
        panel.Children.Add(new TextBlock
        {
            Text = "A database location must be chosen before Scheduling Assistant can open. The application will now close.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => Dispatcher.UIThread.Post(() => msg.Close());
        await msg.ShowDialog(this);
    }

    /// <summary>
    /// Shows a dialog with technical IT-detail text. Called from
    /// <see cref="MainWindowViewModel.ShowLockWriteDetailsCommand"/> so the ViewModel
    /// can surface lock-write failure details through the same dialog infrastructure
    /// used by the database-open CFA handler.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">The IT-facing technical detail string.</param>
    public Task ShowItDetailAsync(string title, string message)
        => ShowMessageAsync(title, message);

    /// <summary>
    /// Shows a modal one-button message dialog owned by this window (see <see cref="ShowMessageDialogAsync"/>).
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="buttonLabel">Label of the single button; "OK" unless a message calls for another word (for example "Close").</param>
    private Task ShowMessageAsync(string title, string message, string buttonLabel = "OK")
        => ShowMessageDialogAsync(this, title, message, buttonLabel);

    /// <summary>
    /// Shows a modal one-button message dialog built in code and Topmost (see <see cref="ShowFatalAsync"/>). Static
    /// and given its owner so any window can use it: this window through <see cref="ShowMessageAsync"/>, and the
    /// database chooser, which runs before this window is shown.
    /// </summary>
    /// <param name="owner">Window to parent the dialog to.</param>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="buttonLabel">Label of the single button; "OK" unless a message calls for another word (for example "Close").</param>
    internal static async Task ShowMessageDialogAsync(Window owner, string title, string message, string buttonLabel = "OK")
    {
        var msg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        var ok = new Button { Content = buttonLabel, HorizontalAlignment = HorizontalAlignment.Center };
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(ok);
        msg.Content = panel;

        ok.Click += (_, _) => Dispatcher.UIThread.Post(() => msg.Close());
        await msg.ShowDialog(owner);
    }

    // ── Restore from backup (spec item 5, step 5b) ───────────────────────────

    /// <summary>
    /// Restores <paramref name="backupPath"/> over the open database, safely. Called (after the user has
    /// confirmed) from <see cref="MainWindowViewModel"/>, like <see cref="SwitchDatabaseAsync"/>, because it
    /// is window-lifecycle work: a wait dialog, a blocked close, and a close and restart at the end.
    ///
    /// <para>One-line delegate to <see cref="SessionLifecycle.RestoreFromBackupAsync"/>, which holds the full
    /// sequence and its documentation.</para>
    /// </summary>
    /// <param name="backupPath">Full path of the backup .db file to restore (may be on a share).</param>
    public Task RestoreFromBackupAsync(string backupPath)
        => Lifecycle.RestoreFromBackupAsync(backupPath);

    /// <summary>
    /// Starts a fresh copy of TermPoint (after a confirmed restore). Never throws: if it cannot be started the
    /// application still closes, and the user reopens it by hand.
    /// </summary>
    private static void RelaunchApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null)
            {
                App.Logger.LogInfo("MainWindow: cannot restart after the restore — the executable path is unknown.");
                return;
            }

            PlatformProcess.LaunchExecutable(exePath);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "MainWindow: could not restart TermPoint after the restore");
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> while a modal wait dialog with no buttons is up, and closes the dialog
    /// when the work ends. The user cannot dismiss the dialog (its Closing is cancelled until the work is
    /// over), so nothing can be edited meanwhile. Built in code and Topmost like every other modal dialog here
    /// (see <see cref="ShowFatalAsync"/>), so it can never open behind the main window or a sticky note.
    ///
    /// <para><b>The work is never orphaned:</b> it is started BEFORE the dialog is shown and is always awaited
    /// to completion before this method returns — even if showing the dialog throws, in which case the work
    /// simply runs without a dialog.</para>
    /// </summary>
    /// <typeparam name="T">The type the work produces.</typeparam>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog body text.</param>
    /// <param name="work">The work to run; started immediately on the calling (UI) thread, as usual for an async method.</param>
    /// <returns>Whatever <paramref name="work"/> returns.</returns>
    /// <exception cref="Exception">Any exception <paramref name="work"/> throws is rethrown here, after the dialog has closed.</exception>
    private async Task<T> RunWithWaitDialogAsync<T>(string title, string message, Func<Task<T>> work)
    {
        var workTask = work();
        var workFinished = false;

        var dlg = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)(this.FindResource("FontSizeNormal") ?? 14d)
        });
        dlg.Content = panel;

        // No buttons, and the title-bar X / Alt+F4 are refused until the work has ended.
        dlg.Closing += (_, e) => { if (!workFinished) e.Cancel = true; };

        // When the work ends, one way or the other, let the dialog close. The work's own exception (if any)
        // is not handled here: it is observed so it is never "unobserved", and rethrown by the final await.
        async Task CloseWhenWorkEndsAsync()
        {
            try { await workTask; }
            catch { /* rethrown to the caller by `await workTask` below */ }

            workFinished = true;
            Dispatcher.UIThread.Post(() =>
            {
                try { dlg.Close(); }
                catch (Exception ex) { App.Logger.LogInfo($"MainWindow: closing the wait dialog failed ({ex.GetType().Name}: {ex.Message})"); }
            });
        }
        _ = CloseWhenWorkEndsAsync();

        try
        {
            await dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            // Could not show the dialog. The work is already running and must still be waited for.
            App.Logger.LogError(ex, "MainWindow: could not show the wait dialog; continuing without it");
        }

        return await workTask;
    }

    // ── Save conflict (spec item 10, step 10a) ───────────────────────────────

    /// <summary>
    /// Carries out the user's choice from the save-conflict banner (<see cref="CheckoutService.HasSaveConflict"/>):
    /// <b>Save a Copy…</b> keeps their version as a separate file, <b>Discard Changes</b> gives it up; either way the
    /// session ends and the current database is reopened. Called from <see cref="MainWindowViewModel"/>, like
    /// <see cref="SwitchDatabaseAsync"/>, because it is window-lifecycle work.
    ///
    /// <para>One-line delegate to <see cref="SessionLifecycle.ResolveSaveConflictAsync"/>, which holds the full
    /// sequence and its documentation.</para>
    /// </summary>
    /// <param name="saveCopy">True for Save a Copy…, false for Discard Changes.</param>
    public Task ResolveSaveConflictAsync(bool saveCopy)
        => Lifecycle.ResolveSaveConflictAsync(saveCopy);

    // ── CheckoutService event handlers ───────────────────────────────────────

    private void OnCheckoutSaveCompleted()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearSaveError();
            vm.ClearDirty();
        }
    }

    private void OnCheckoutBecameDirty()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SetDirty();
    }

    private void OnCheckoutSaveStarted()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SetSaving();
    }

    private void OnCheckoutSaveFinished()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ClearSaving();
    }

    private void OnCheckoutSaveAlreadyInProgress()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.NotifySaveAlreadyInProgress();
    }

    private void OnCheckoutSaveFailed(string message, bool autoDismiss)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SetSaveError(message, autoDismiss);
    }

    /// <summary>
    /// Handles <see cref="CheckoutService.SaveConflictDetected"/>: a save was refused because the shared database
    /// was changed elsewhere since this session opened it. Shows the persistent save-conflict banner (idempotent:
    /// the event fires on every refused save).
    /// </summary>
    private void OnCheckoutSaveConflictDetected()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ShowSaveConflict();
    }

    /// <summary>
    /// Handles <see cref="CheckoutService.WriteLockLost"/> — fired when a mid-session
    /// verification discovered that the write lock is no longer ours (taken over,
    /// deleted, or its <c>SessionGuid</c> was modified).
    ///
    /// <para>Logs the loss, hops to the UI thread and hands over to
    /// <see cref="SessionLifecycle.HandleWriteLockLostAsync"/>, which holds the banner, the in-place demotion to
    /// read-only and their documentation.</para>
    /// </summary>
    /// <param name="reason">Why the write lock was lost.</param>
    private async void OnWriteLockLost(WriteLockLostReason reason)
    {
        try
        {
        App.Logger.LogBreadcrumb("WriteLockLost", new() { ["reason"] = reason.ToString() });
        // File-log line (breadcrumbs go only to Bugsnag): makes a genuine MID-SESSION
        // demotion unambiguous in the app log, distinct from a fresh launch that opens
        // read-only because it found an existing lock ("Read-only mode; lock held by ...").
        App.Logger.LogInfo(
            $"MainWindow: mid-session write-lock loss (reason={reason}) — demoting to read-only in place.");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Lifecycle.HandleWriteLockLostAsync(reason);
        });
        }
        catch (System.Exception ex)
        {
            App.Logger.LogError(ex, "OnWriteLockLost: dispatch failed");
        }
    }

    private Task<bool> ShowYesNoAsync(string title, string message)
        => ShowChoiceAsync(title, message, "Yes", "No");

    /// <summary>
    /// Shows a modal two-button dialog with caller-supplied button labels
    /// (e.g. "Restore" / "Discard"). Returns true when the affirmative
    /// (first) button was clicked.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button (returned as true).</param>
    /// <param name="negative">Label of the declining button (returned as false).</param>
    private Task<bool> ShowChoiceAsync(string title, string message, string affirmative, string negative)
        => ShowChoiceDialogAsync(this, title, message, affirmative, negative);

    /// <summary>
    /// Shows a modal two-button dialog built in code and Topmost (see <see cref="ShowFatalAsync"/>). Static and
    /// given its owner so any window can use it: this window through <see cref="ShowChoiceAsync"/>, and the
    /// database chooser, which runs before this window is shown. Closing the dialog without a button counts as
    /// the declining button. A thin wrapper over <see cref="ShowChoiceOrCloseDialogAsync"/>, which keeps the X
    /// apart for the few questions where it must not count as a decision.
    /// </summary>
    /// <param name="owner">Window to parent the dialog to.</param>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button (returned as true).</param>
    /// <param name="negative">Label of the declining button (returned as false).</param>
    /// <returns>True when the affirmative button was clicked; false for the negative button or the window's X.</returns>
    internal static async Task<bool> ShowChoiceDialogAsync(
        Window owner, string title, string message, string affirmative, string negative)
        => await ShowChoiceOrCloseDialogAsync(owner, title, message, affirmative, negative) == ChoiceResult.Affirmative;

    /// <summary>
    /// The core of every two-button dialog: built in code and Topmost (see <see cref="ShowFatalAsync"/>), and it
    /// reports THREE outcomes, so a caller can tell a button press from the window's X. Used directly by the
    /// crash-recovery questions, where the X must never be taken for "discard my unsaved work" (data-persistence
    /// review, F1); every other caller goes through the <c>bool</c> wrapper <see cref="ShowChoiceDialogAsync"/>.
    /// </summary>
    /// <param name="owner">Window to parent the dialog to.</param>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button.</param>
    /// <param name="negative">Label of the declining button.</param>
    /// <returns><see cref="ChoiceResult.Affirmative"/> or <see cref="ChoiceResult.Negative"/> for a button; <see cref="ChoiceResult.Closed"/> when the window was closed without one.</returns>
    internal static async Task<ChoiceResult> ShowChoiceOrCloseDialogAsync(
        Window owner, string title, string message, string affirmative, string negative)
    {
        var result = ChoiceResult.Closed; // stays Closed unless a button sets it first
        var dlg = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        var yes   = new Button { Content = affirmative, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 4) };
        var no    = new Button { Content = negative,    HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 4) };
        var btns  = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        btns.Children.Add(no);
        btns.Children.Add(yes);

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        panel.Children.Add(btns);
        dlg.Content = panel;

        yes.Click += (_, _) => { result = ChoiceResult.Affirmative; Dispatcher.UIThread.Post(() => dlg.Close()); };
        no.Click  += (_, _) => { result = ChoiceResult.Negative;    Dispatcher.UIThread.Post(() => dlg.Close()); };
        await dlg.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Shows the short "Checking the Lock" wait that follows a confirmed stale-lock takeover when the
    /// holder's heartbeat has not yet stood still long enough (spec item 9b). The dialog is only the
    /// view: the waiting itself is <see cref="CheckoutService.WaitForTakeOverConfirmationAsync"/>, which
    /// never blocks the UI thread. The dialog closes by itself when the wait ends; its Cancel button (or
    /// closing it) cancels the wait. Built in code and Topmost like every other modal dialog here (see
    /// <see cref="ShowFatalAsync"/>), so it can never open behind the main window or a sticky note.
    /// </summary>
    /// <param name="remaining">How long the wait will take at most; shown to the user in seconds.</param>
    /// <returns>
    /// True when the wait ran to the end and the takeover may proceed; false when the user cancelled
    /// (treat exactly like "Open read-only").
    /// </returns>
    private async Task<bool> ShowStaleLockWaitAsync(TimeSpan remaining)
    {
        using var cancellation = new CancellationTokenSource();
        var completed = false;

        var dlg = new Window
        {
            Title = StaleLockPrompt.WaitTitle,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        var cancel = new Button
        {
            Content = StaleLockPrompt.WaitCancelButton,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 4)
        };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = StaleLockPrompt.BuildWaitMessage(App.Checkout.CurrentHolder, remaining),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(cancel);
        dlg.Content = panel;

        // Cancel button or the title-bar X: give up waiting. (The wait finishing closes the dialog too,
        // which also raises Closed; by then `completed` is already settled, so cancelling is harmless.)
        var closed = false;
        cancel.Click += (_, _) => cancellation.Cancel();
        dlg.Closed   += (_, _) => { closed = true; cancellation.Cancel(); };

        // Run the wait alongside the dialog; when it ends, one way or the other, close the dialog
        // (unless the user already closed it with the title-bar X).
        async Task CloseWhenWaitEndsAsync()
        {
            try
            {
                completed = await App.Checkout.WaitForTakeOverConfirmationAsync(cancellation.Token);
            }
            catch (Exception ex)
            {
                // Never leave the dialog up with nothing driving it; failing safe means "don't take over".
                App.Logger.LogError(ex, "ShowStaleLockWaitAsync: waiting for the takeover confirmation failed");
                completed = false;
            }
            Dispatcher.UIThread.Post(() => { if (!closed) dlg.Close(); });
        }
        _ = CloseWhenWaitEndsAsync();

        await dlg.ShowDialog(this);
        return completed;
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private bool _detachWired;

    // ── Data-context wiring ──────────────────────────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // Wire DetachRequested events on the panels inside MainView (once).
            // MainView.axaml doesn't declare these handlers since WASM doesn't support
            // secondary windows; the desktop shell wires them programmatically.
            if (!_detachWired && MainViewControl is not null)
            {
                if (MainViewControl.FindControl<DetachablePanel>("SectionViewPanel") is { } svp)
                    svp.DetachRequested += OnSectionViewDetach;
                if (MainViewControl.FindControl<DetachablePanel>("WorkloadPanel") is { } wp)
                    wp.DetachRequested += OnWorkloadDetach;
                if (MainViewControl.FindControl<DetachablePanel>("ScheduleGridPanel") is { } sgp)
                    sgp.DetachRequested += OnScheduleGridDetach;

                _detachWired = true;
            }
        }
    }

    // ── Detach handlers ─────────────────────────────────────────────────────

    private void OnSectionViewDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;

        // Build a toggle button for the detached window header, matching the
        // inline HeaderContext button in MainWindow.axaml.
        var toggleButton = new Button
        {
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = (IBrush)(this.FindResource("ButtonBackground") ?? Brushes.LightGray),
            BorderThickness = new Thickness(0),
            FontSize = 10,
            Foreground = (IBrush)(this.FindResource("TextFaint") ?? Brushes.Gray),
        };
        toggleButton.Bind(Button.CommandProperty,
            new Binding("ToggleMeetingViewCommand") { Source = Vm });
        toggleButton.Bind(ContentControl.ContentProperty,
            new Binding("ToggleMeetingViewLabel") { Source = Vm });

        // The detached header mirrors the inline one: the focus statement fills the width and the Events toggle
        // docks right. The statement shows in the Section View only (the wrapper Panel carries that condition,
        // because FocusIndicator's own DataContext is the FocusViewModel) and hides itself under focus All.
        // Prototype 2026-10-23: subjects shown in the top bar instead; restore if the header statement is wanted back.
        //var focusPanel = new Panel
        //{
        //    VerticalAlignment = VerticalAlignment.Center,
        //    Children = { new FocusIndicator { DataContext = Vm.FocusVm } }
        //};
        //focusPanel.Bind(IsVisibleProperty, new Binding("IsShowingSections") { Source = Vm });
        toggleButton.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(toggleButton, Dock.Right);
        var headerContent = new DockPanel();
        headerContent.Children.Add(toggleButton);
        //headerContent.Children.Add(focusPanel);

        // Null out PanelContent to prevent dual event firing while detached.
        slot.PanelContent = null;

        DetachSlot(slot, ref _sectionViewWindow,
            () => new SectionPanelContent { DataContext = Vm },
            () =>
            {
                // No explicit DataContext — inherit from the visual tree (MainWindowViewModel).
                // Setting a local value here would pin the SPC to the current VM instance,
                // surviving database switches where MainWindow.DataContext changes to a new VM.
                slot.PanelContent = new SectionPanelContent();
                slot.IsVisible = true;
                _sectionViewWindow = null;
            },
            headerContent);
    }

    private void OnWorkloadDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        DetachSlot(slot, ref _workloadWindow,
            () => new WorkloadPanelView { DataContext = Vm.WorkloadPanelVm },
            () => { slot.IsVisible = true; _workloadWindow = null; });
    }

    private void OnScheduleGridDetach(object? sender, EventArgs e)
    {
        var slot = (DetachablePanel)sender!;
        var headerContext = CreateScheduleGridHeaderContext();

        // Remove the view from the ContentPresenter entirely while detached.
        // Just hiding the slot leaves the view in the visual tree with live bindings,
        // which causes the right-click popup (and others) to fire on both the hidden
        // view and the detached window simultaneously.
        slot.PanelContent = null;

        DetachSlot(slot, ref _scheduleGridWindow,
            () => new ScheduleGridView { DataContext = Vm.ScheduleGridVm },
            () =>
            {
                // On reattach, create a fresh view with a DataContext BINDING (not local value)
                // so it tracks database switches. A local value would pin it to the current
                // ScheduleGridVm, surviving MainWindow.DataContext changes on DB switch.
                var freshView = new ScheduleGridView();
                freshView.Bind(DataContextProperty, new Binding("ScheduleGridVm"));
                if (MainViewControl is not null) MainViewControl.ScheduleGridViewInstance = freshView;
                slot.PanelContent = freshView;
                slot.IsVisible = true;
                _scheduleGridWindow = null;
            },
            headerContext);
    }

    /// <summary>
    /// Builds the header shown above a detached Schedule View window: academic unit, semesters, stats, focus,
    /// filter, overlay and shared-schedule statements, mirroring the inline header in MainView.axaml.
    /// A <see cref="WrapPanel"/>, like the inline header, so in a narrow window the statements move onto further
    /// lines instead of running off the edge (a horizontal StackPanel never wraps).
    /// </summary>
    /// <returns>The header panel, bound to the main window's view model.</returns>
    private WrapPanel CreateScheduleGridHeaderContext()
    {
        var stack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Academic Unit name
        var auTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        auTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.AcademicUnitName") { Source = Vm });
        auTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auTextBlock);

        // Separator (only shown when Academic Unit is available)
        var auSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        auSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.AcademicUnitName")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(auSeparator);

        // Semester — ItemsControl of colored segment pills, matching the main-window AXAML layout.
        var semFontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d);
        var semItemsControl = new ItemsControl
        {
            VerticalAlignment = VerticalAlignment.Center,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            }),
            ItemTemplate = new FuncDataTemplate<SemesterLineSegment>((seg, _) =>
            {
                var (bg, bd) = SemesterBrushResolver.ResolvePair(seg.SemesterName, seg.HexColor);
                // Academic-year / single-semester rows pass empty name+hex → leave brushes null
                // so the pill renders without a background (matches previous behavior).
                if (string.IsNullOrEmpty(seg.SemesterName) && string.IsNullOrEmpty(seg.HexColor))
                    bg = bd = null;

                return new Border
                {
                    Padding          = new Thickness(6, 2),
                    Background       = bg,
                    BorderBrush      = bd,
                    BorderThickness  = new Thickness(1),
                    CornerRadius     = new CornerRadius(2),
                    Child = new TextBlock
                    {
                        FontSize = semFontSize,
                        Text     = seg.DisplayText
                    }
                };
            })
        };
        semItemsControl.Bind(ItemsControl.ItemsSourceProperty,
            new Binding("ScheduleGridVm.SemesterLineSegments") { Source = Vm });
        stack.Children.Add(semItemsControl);

        // Stats separator
        var statsSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        statsSeparator.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsSeparator);

        // Stats
        var statsTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            VerticalAlignment = VerticalAlignment.Center
        };
        statsTextBlock.Bind(TextBlock.TextProperty, new Binding("ScheduleGridVm.StatsLine") { Source = Vm });
        statsTextBlock.Bind(IsVisibleProperty, new Binding("ScheduleGridVm.StatsLine")
        {
            Source = Vm,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        stack.Children.Add(statsTextBlock);

        // Subject focus (outermost layer, so before "Filtered by:"): separator + the shared statement control,
        // both hidden under focus All.
        // Prototype 2026-10-23: subjects shown in the top bar instead; restore if the header statement is wanted back.
        //var focusSeparator = new Border
        //{
        //    Width = 1,
        //    Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
        //    Margin = new Thickness(0, 3)
        //};
        //focusSeparator.Bind(IsVisibleProperty, new Binding("IsIndicatorVisible") { Source = Vm.FocusVm });
        //stack.Children.Add(focusSeparator);
        //stack.Children.Add(new FocusIndicator { DataContext = Vm.FocusVm, VerticalAlignment = VerticalAlignment.Center });

        // Filter separator
        var filterSeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        filterSeparator.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filterSeparator);

        // "Filtered by:" label
        var filteredByLabel = new TextBlock
        {
            Text = "Filtered by:",
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center
        };
        filteredByLabel.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filteredByLabel);

        // Filter summary
        var filterTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.Bold,
            Foreground = (IBrush)(this.FindResource("FilterBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        };
        filterTextBlock.Bind(TextBlock.TextProperty, new Binding("Filter.ActiveSummary") { Source = Vm.ScheduleGridVm });
        filterTextBlock.Bind(IsVisibleProperty, new Binding("Filter.IsActive") { Source = Vm.ScheduleGridVm });
        stack.Children.Add(filterTextBlock);

        // Overlay
        var overlaySeparator = new Border
        {
            Width = 1,
            Background = (IBrush)(this.FindResource("SeparatorLine") ?? Brushes.Gray),
            Margin = new Thickness(0, 3)
        };
        overlaySeparator.Bind(IsVisibleProperty, new Binding("Filter.OverlaySummary") { Source = Vm.ScheduleGridVm, Converter = StringConverters.IsNotNullOrEmpty });
        stack.Children.Add(overlaySeparator);

        var overlayTextBlock = new TextBlock
        {
            FontSize = (double)(this.FindResource("FontSizeLarge") ?? 14d),
            FontWeight = FontWeight.SemiBold,
            Foreground = (IBrush)(this.FindResource("OverlayBadgeText") ?? Brushes.Black),
            VerticalAlignment = VerticalAlignment.Center
        };
        overlayTextBlock.Bind(TextBlock.TextProperty, new Binding("Filter.OverlaySummary") { Source = Vm.ScheduleGridVm });
        overlayTextBlock.Bind(IsVisibleProperty, new Binding("Filter.OverlaySummary") { Source = Vm.ScheduleGridVm, Converter = StringConverters.IsNotNullOrEmpty });
        stack.Children.Add(overlayTextBlock);

        return stack;
    }

    /// <summary>
    /// Closes all secondary top-level windows owned by this main window during shutdown:
    /// the three detachable-panel windows and any floating workflow sticky notes. Reattach
    /// is suppressed on the detached panels so their close handlers don't try to reattach to
    /// a main window that is itself tearing down. Called from <see cref="OnClosing"/>.
    /// </summary>
    private void CloseSecondaryWindows()
    {
        foreach (var w in new[] { _sectionViewWindow, _workloadWindow, _scheduleGridWindow })
        {
            if (w is null) continue;
            w.SuppressReattach = true; // don't reattach into a tearing-down main window
            w.Close();
        }
        _sectionViewWindow  = null;
        _workloadWindow     = null;
        _scheduleGridWindow = null;

        Views.Management.WorkflowsView.CloseAllNotes();
    }

    // ── Core detach mechanism ───────────────────────────────────────────────

    private void DetachSlot(
        DetachablePanel slot,
        ref DetachedPanelWindow? existingWindow,
        Func<Control> buildContent,
        Action onReattach,
        object? headerContext = null)
    {
        if (existingWindow is not null)
        {
            existingWindow.Activate();
            return;
        }

        slot.IsVisible = false;

        var win = new DetachedPanelWindow { OnReattach = onReattach };
        win.SetContent(slot.Header, buildContent(), headerContext);
        existingWindow = win;
        win.Show(this);
    }

    // ── Lifecycle seams (designs/data-persistence-review-2026-10.md, step 9.1a) ─────────────────────────────────────
    // ILifecyclePrompts and ISessionHost are the two interfaces a future non-UI session-lifecycle class will
    // depend on. This window implements both EXPLICITLY, each member delegating to the code that already
    // existed, so introducing them changes no behaviour. The lifecycle methods below call themselves through
    // the two accessors, which is what lets those methods later move out of this file unchanged.

    /// <summary>This window seen as the lifecycle's way of asking or telling the user.</summary>
    private ILifecyclePrompts Prompts => this;

    /// <summary>This window seen as the lifecycle's way of touching the window and the service container.</summary>
    private ISessionHost Host => this;

    /// <inheritdoc cref="ILifecyclePrompts.ShowMessageAsync"/>
    Task ILifecyclePrompts.ShowMessageAsync(string title, string message, string buttonLabel)
        => ShowMessageAsync(title, message, buttonLabel);

    /// <inheritdoc cref="ILifecyclePrompts.ShowChoiceAsync"/>
    Task<bool> ILifecyclePrompts.ShowChoiceAsync(string title, string message, string affirmative, string negative)
        => ShowChoiceAsync(title, message, affirmative, negative);

    /// <inheritdoc cref="ILifecyclePrompts.ShowChoiceOrCloseAsync"/>
    Task<ChoiceResult> ILifecyclePrompts.ShowChoiceOrCloseAsync(string title, string message, string affirmative, string negative)
        => ShowChoiceOrCloseDialogAsync(this, title, message, affirmative, negative);

    /// <inheritdoc cref="ILifecyclePrompts.RunWithWaitAsync{T}"/>
    Task<T> ILifecyclePrompts.RunWithWaitAsync<T>(string title, string message, Func<Task<T>> work)
        => RunWithWaitDialogAsync(title, message, work);

    /// <inheritdoc cref="ILifecyclePrompts.PickSaveFileAsync"/>
    async Task<string?> ILifecyclePrompts.PickSaveFileAsync(string title, string suggestedFileName, string defaultExtension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title               = title,
            SuggestedFileName   = suggestedFileName,
            DefaultExtension    = defaultExtension,
            ShowOverwritePrompt = false,
        });

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc cref="ILifecyclePrompts.ShowStaleLockWaitAsync"/>
    Task<bool> ILifecyclePrompts.ShowStaleLockWaitAsync(TimeSpan remaining)
        => ShowStaleLockWaitAsync(remaining);

    /// <inheritdoc cref="ILifecyclePrompts.ChooseDatabaseAsync"/>
    Task<string?> ILifecyclePrompts.ChooseDatabaseAsync(ChooserMode mode, RecoveryReason? reason, string? lastKnownPath)
        => ShowDatabaseChooserAsync(mode, reason, lastKnownPath);

    /// <inheritdoc cref="ISessionHost.OpenServices"/>
    IOpenedDatabase ISessionHost.OpenServices(
        string workingPath, string? restoreAcademicYearId, IReadOnlySet<string>? restoreSemesterIds)
        => new OpenedDatabase(App.InitializeServices(workingPath, restoreAcademicYearId, restoreSemesterIds));

    /// <inheritdoc cref="ISessionHost.ReleaseServices"/>
    void ISessionHost.ReleaseServices() => App.ReleaseServices();

    /// <inheritdoc cref="ISessionHost.DisposeServices"/>
    void ISessionHost.DisposeServices() => (App.Services as IDisposable)?.Dispose();

    /// <inheritdoc cref="ISessionHost.StopBackupSession"/>
    void ISessionHost.StopBackupSession()
    {
        if (App.Services?.GetService(typeof(BackupService)) is BackupService backup)
            backup.StopSession();
    }

    /// <inheritdoc cref="ISessionHost.CloseSecondaryWindows"/>
    void ISessionHost.CloseSecondaryWindows() => CloseSecondaryWindows();

    /// <inheritdoc cref="ISessionHost.IsInlineEditorOpen"/>
    bool ISessionHost.IsInlineEditorOpen => DataContext is MainWindowViewModel { IsAnyPanelEditing: true };

    /// <inheritdoc cref="ISessionHost.SetupWindowAsync"/>
    /// <exception cref="InvalidCastException"><paramref name="opened"/> was not produced by <see cref="ISessionHost.OpenServices"/> of this window.</exception>
    Task ISessionHost.SetupWindowAsync(string dbPath, IOpenedDatabase opened)
        => SetupMainWindowAsync(dbPath, ((OpenedDatabase)opened).ViewModel);

    /// <inheritdoc cref="ISessionHost.CoverWindowForNoDatabase"/>
    void ISessionHost.CoverWindowForNoDatabase() => CoverWindowForNoDatabase();

    /// <inheritdoc cref="ISessionHost.IsDatabaseOpen"/>
    bool ISessionHost.IsDatabaseOpen
    {
        get => _isDatabaseOpen;
        set => _isDatabaseOpen = value;
    }

    /// <inheritdoc cref="ISessionHost.CloseWindow"/>
    void ISessionHost.CloseWindow() => Close();

    /// <inheritdoc cref="ISessionHost.ShutDown"/>
    void ISessionHost.ShutDown()
        => (Avalonia.Application.Current?.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Shutdown();

    /// <inheritdoc cref="ISessionHost.ReportFatalAndShutDownAsync"/>
    async Task ISessionHost.ReportFatalAndShutDownAsync(Exception ex)
    {
        WindowState = WindowState.Normal;
        await ShowStartupErrorAsync(ex);
        (Avalonia.Application.Current?.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Shutdown();
    }

    /// <inheritdoc cref="ISessionHost.TryCaptureSemesterSelection"/>
    bool ISessionHost.TryCaptureSemesterSelection(out string? yearId, out IReadOnlySet<string>? semesterIds)
    {
        yearId      = null;
        semesterIds = null;

        // App.Services is null when no database has been opened yet (wizard join path).
        if (App.Services?.GetService(typeof(SemesterContext)) is SemesterContext priorCtx
            && priorCtx.SelectedAcademicYear is not null)
        {
            yearId      = priorCtx.SelectedAcademicYear.Id;
            semesterIds = priorCtx.SelectedSemesters.Select(s => s.Semester.Id).ToHashSet();
            return true;
        }

        return false;
    }

    /// <inheritdoc cref="ISessionHost.RelaunchApplication"/>
    void ISessionHost.RelaunchApplication() => RelaunchApplication();

    /// <inheritdoc cref="ISessionHost.CaptureLockLossSurface"/>
    ILockLossSurface ISessionHost.CaptureLockLossSurface()
        => new LockLossSurface(
            DataContext as MainWindowViewModel,
            App.Services?.GetService<IDatabaseContext>() as DatabaseContext);

    /// <summary>
    /// Adapter that lets the session lifecycle use the root view model without referencing its type: it
    /// implements <see cref="IOpenedDatabase"/> by calling exactly the members the lifecycle used to call on the
    /// view model directly, and hands the view model back to this window to wire up
    /// (<see cref="ISessionHost.SetupWindowAsync"/>).
    /// </summary>
    private sealed class OpenedDatabase : IOpenedDatabase
    {
        /// <summary>The root view model that <c>App.InitializeServices</c> returned.</summary>
        public MainWindowViewModel ViewModel { get; }

        /// <summary>Wraps the root view model of a freshly opened database.</summary>
        /// <param name="viewModel">The view model returned by <c>App.InitializeServices</c>.</param>
        public OpenedDatabase(MainWindowViewModel viewModel) => ViewModel = viewModel;

        /// <inheritdoc cref="IOpenedDatabase.SetDatabaseName"/>
        public void SetDatabaseName(string name, string fullPath) => ViewModel.SetDatabaseName(name, fullPath);

        /// <inheritdoc cref="IOpenedDatabase.RestoreSemesterSelection"/>
        public void RestoreSemesterSelection(string yearId, IReadOnlySet<string> semesterIds)
            => ViewModel.SemesterContext.RestoreSelection(yearId, semesterIds);
    }

    /// <summary>
    /// The view model and data connection the lost-write-lock handler captured once at its start
    /// (<see cref="ISessionHost.CaptureLockLossSurface"/>), exposed through <see cref="ILockLossSurface"/> so the
    /// lifecycle never references either type. Each member is the verbatim use the handler made of the captured
    /// locals, including the null-propagation.
    /// </summary>
    private sealed class LockLossSurface : ILockLossSurface
    {
        /// <summary>The window's root view model at the moment of capture, or null.</summary>
        private readonly MainWindowViewModel? _vm;

        /// <summary>The open data connection at the moment of capture, or null.</summary>
        private readonly DatabaseContext? _dbContext;

        /// <summary>Holds the two captured objects.</summary>
        /// <param name="vm">The window's root view model, or null when the data context was something else.</param>
        /// <param name="dbContext">The open data connection, or null when no container exists.</param>
        public LockLossSurface(MainWindowViewModel? vm, DatabaseContext? dbContext)
        {
            _vm        = vm;
            _dbContext = dbContext;
        }

        /// <inheritdoc cref="ILockLossSurface.HasConnection"/>
        public bool HasConnection => _dbContext is not null;

        /// <inheritdoc cref="ILockLossSurface.HasViewModel"/>
        public bool HasViewModel => _vm is not null;

        /// <inheritdoc cref="ILockLossSurface.ShowSaveError"/>
        public void ShowSaveError(string text) => _vm?.SetSaveError(text, autoDismiss: false);

        /// <inheritdoc cref="ILockLossSurface.MarkReadOnly"/>
        public void MarkReadOnly() => _dbContext?.MarkReadOnly();

        /// <inheritdoc cref="ILockLossSurface.CloseConnection"/>
        public void CloseConnection() => _dbContext?.CloseConnection();

        /// <inheritdoc cref="ILockLossSurface.ReinitializeConnection"/>
        /// <exception cref="NullReferenceException">No connection was captured (<see cref="HasConnection"/> is false).</exception>
        public void ReinitializeConnection(string path, bool readOnly) => _dbContext!.ReinitializeConnection(path, readOnly);

        /// <inheritdoc cref="ILockLossSurface.ReloadSections"/>
        /// <exception cref="NullReferenceException">No view model was captured (<see cref="HasViewModel"/> is false).</exception>
        public void ReloadSections() => _vm!.SectionListVm.ReloadFromDatabase();
    }
}
