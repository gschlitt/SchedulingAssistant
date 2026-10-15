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

public partial class MainWindow : Window
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
    /// Why the database chooser is shown the next time a switch leaves NO database open (see
    /// <see cref="ShowSwitchFailedChooserAsync"/>): <see cref="RecoveryReason.SwitchFailed"/> for every
    /// failure except one refused by the single-instance guard, which sets
    /// <see cref="RecoveryReason.AlreadyOpen"/> so the banner doesn't claim a database was closed or failed.
    /// Reset at the start of every <see cref="SwitchCoreAsync"/> attempt, so a stale value from an earlier
    /// attempt (the chooser loop runs the next pick through the same method) can never be shown.
    /// </summary>
    private RecoveryReason _noDatabaseReason = RecoveryReason.SwitchFailed;

    public ScheduleGridView? ScheduleGridViewInstance => MainViewControl?.ScheduleGridViewInstance;

    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"TermPoint v{version?.Major}.{version?.Minor}.{version?.Build}";



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

    // ── Restore from backup (spec item 5, step 5b) ───────────────────────────
    // RestoreFromBackupAsync sets these; OnClosing reads them. See RestoreFromBackupAsync for the sequence.

    /// <summary>
    /// True from the moment a restore starts saving the user's changes until the restore's outcome is known.
    /// While true, <see cref="OnClosing"/> refuses every close request: the window must never close while the
    /// database is being replaced, or the final save/teardown would race the restore. Reset in a
    /// <c>finally</c>, so it can never stay stuck.
    /// </summary>
    private bool _restoreInProgress;

    /// <summary>
    /// True once a restore has replaced D (or may have: an unconfirmed rename) and the application is about to
    /// close. <see cref="OnClosing"/> then tears the session down WITHOUT saving — the working copy is stale,
    /// and a save would be refused anyway — and discards the working copy and its crash marker: the restored D
    /// supersedes them, and everything was saved BEFORE the restore started, so nothing is lost.
    /// </summary>
    private bool _endSessionAfterRestore;

    /// <summary>
    /// The path of D as it was when the restore began (captured before anything is torn down). Used to discard
    /// the crash marker of the session that is ending (<see cref="CheckoutService.DiscardCrash"/>).
    /// </summary>
    private string? _restoredSourcePath;

    /// <summary>
    /// True when <see cref="OnClosing"/> should start a fresh copy of TermPoint after the single-instance guard
    /// is released: after a confirmed restore ("TermPoint will now restart"). False after an unconfirmed
    /// one, where the user is told to reopen TermPoint themselves once the connection is back.
    /// </summary>
    private bool _relaunchAfterClose;

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
        if (_restoreInProgress)
        {
            e.Cancel = true;
            App.Logger.LogInfo("MainWindow: close request ignored — a restore from backup is in progress.");
            return;
        }

        e.Cancel = true; // Defer: run async save first.
        _shuttingDown = true;

        try
        {
            // Stop the periodic backup timer before disposing the container.
            if (App.Services?.GetService(typeof(BackupService)) is BackupService bs)
                bs.StopSession();

            if (_endSessionAfterRestore)
            {
                // The database has just been replaced by a restore: end the stale session without saving.
                await EndSessionAfterRestoreAsync();
            }
            else
            {
                // Stop autosave and save D' → D (releases lock on success).
                var wasWriteMode = App.Checkout.Mode == CheckoutMode.WriteAccess;
                App.Checkout.StopAutoSave();
                var saveOutcome = await App.Checkout.ReleaseAsync(saveFirst: wasWriteMode);

                // Close any detached panels / sticky notes BEFORE disposing the DI container,
                // so windows whose content binds to view models are torn down while those VMs
                // still exist. ShutdownMode.OnMainWindowClose would close them anyway, but doing
                // it here is deterministic and suppresses the detach-reattach side effect.
                CloseSecondaryWindows();

                // Dispose the DI container, which closes the SQLite connection cleanly.
                (App.Services as IDisposable)?.Dispose();

                // Delete the working copy ONLY when its content is safely in D (save
                // succeeded) or nothing was ever edited (no dirty marker). Otherwise the
                // working copy and its marker are deliberately left behind: they are the
                // only copy of the user's unsaved changes, and the next launch will offer
                // to restore them. (ReleaseAsync has already parked the write lock
                // appropriately for each failure kind.)
                if (saveOutcome == SaveOutcome.Success || !App.Checkout.HasDirtyMarker)
                {
                    App.Checkout.CleanupWorkingCopy();
                }
                else if (wasWriteMode)
                {
                    await ShowMessageAsync("Changes Not Saved",
                        "Your latest changes could not be saved to the database. " +
                        "They have been kept, and TermPoint will offer to restore them " +
                        "the next time you open this database.");
                }
            }
        }
        catch (Exception ex)
        {
            // Log but still allow the window to close — don't leave the user
            // stuck with an uncloseable window.
            App.Logger.LogError(ex, "Error during shutdown — some changes may not have been saved");
        }

        // Give back the single-instance guard LAST: it protects the working copy that was just saved
        // or deliberately kept (a failed final save keeps D' and its dirty marker for the next start), so
        // a second TermPoint must not be able to start sorting through those files until we are done with
        // them. Not strictly needed (the OS drops the guard when the process ends) but explicit, and it
        // runs even when the teardown above threw. Never throws.
        App.InstanceGuard.ReleaseAll();

        // After a confirmed restore, start the fresh copy of TermPoint. Only now: the guard is released (a
        // copy started earlier would be refused as "already open"), the lock is given back and the working
        // copy is gone, so the new copy opens the restored database from a clean state.
        if (_relaunchAfterClose)
            RelaunchApplication();

        // ── Shutdown checkpoint #1 ───────────────────────────────────────────
        // Managed teardown (save → release lock → dispose DI container → cleanup
        // working copy) has finished. Pairs with checkpoint #2 in Program.Main:
        // if BOTH lines are logged but TermPoint.exe lingers, the leftover is a
        // debugger/native artifact, not our code.
        App.Logger.LogInfo("[Shutdown] Managed teardown complete; closing main window.");

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
    /// (3) <see cref="CleanUpAndOfferRecoveryAsync"/> — stale-artifact sweep, crash-recovery offer;
    /// (4) <see cref="RunCheckoutAsync"/> (write lock and copy; an orphaned D.tmp is deleted inside it, and
    /// only once write access is confirmed); (5) services and window. The guard is released when the app
    /// exits (<see cref="OnClosing"/>).</para>
    ///
    /// <para><b>Steps (1)–(5) are <see cref="OpenStartupDatabaseAsync"/>, run in a loop (spec item 5, step 5d):</b>
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
            next = await OpenStartupDatabaseAsync(next, settings);
    }

    /// <summary>
    /// Steps (1)–(5) of the returning-user startup, for one database (see <see cref="RunReturningUserStartupAsync"/>
    /// for their order): the single-instance guard, the recent list and the licence, crash recovery and checkout,
    /// then the services and the window. Extracted so the recovery chooser that follows a damaged shared database
    /// (spec item 5, step 5d) can open its pick through the same steps.
    /// </summary>
    /// <param name="dbPath">The shared database (D) to open.</param>
    /// <param name="settings">The application settings (the recent list is updated).</param>
    /// <returns>
    /// Null when the startup is finished: the database is open, or the app is closing (the user declined,
    /// something could not be opened and the user has been told why, or the user left the chooser). Otherwise
    /// the database the user picked in the recovery chooser, to be opened next.
    /// </returns>
    private async Task<string?> OpenStartupDatabaseAsync(string dbPath, AppSettings settings)
    {
        // ── Single-instance guard (spec item 4b) ──────────────────────────────
        // Taken BEFORE anything below touches shared or local state (the stale artifact sweep, crash
        // detection, the checkout). If another LIVE TermPoint of this user already
        // has this database open on this computer, none of that may run: its dirty marker would look
        // like a crash, "Discard" would delete the live session's marker, and a later sweep could delete
        // its working copy. The user is told, then shown the chooser to pick a different database (or
        // exit); picking the same one again is refused again. Returns the path to open.
        var guardedPath = await App.InstanceGuard.AcquireOrChooseAgainAsync(
            dbPath, ShowAlreadyOpenMessageAsync, ShowAlreadyOpenChooserAsync);
        if (guardedPath is null) return null; // user exited or the wizard took over; nothing was acquired
        dbPath = guardedPath;

        settings.AddRecentDatabase(dbPath);

        // ── License evaluation (must precede checkout so the license gate can
        //    block lock acquisition when access is read-only) ──────────────────
#if !BROWSER
        App.EvaluateLicense(dbPath);
#endif

        // ── Checkout flow ─────────────────────────────────────────────────────
        // Sweep stale local artifacts and check for an orphaned working copy (crash recovery), then
        // acquire the write lock and copy D → D' (or D → D'' for read-only mode). An orphaned D.tmp on
        // the share is deleted by the checkout itself, and only once write access is confirmed.
        // The result determines which path is passed to InitializeServices.
        var recoverUnsaved = await CleanUpAndOfferRecoveryAsync(dbPath, ignoreObserverMode: false);

        var checkoutResult = await RunCheckoutAsync(dbPath, recoverUnsaved);
        if (checkoutResult is null)
        {
            // Checkout cannot proceed (network unreachable, database missing, or no local
            // copy could be made) — dialog already shown. Close the app since this is the
            // initial startup path; there is no previous DB to fall back to.
            //
            // Forget the failed attempt's working-copy bookkeeping first: a stale-lock checkout that
            // was declined leaves WorkingPath naming this database's D', which may hold crash-recovery
            // data kept for the next launch — and the shutdown that Close() starts deletes whatever
            // WorkingPath names. (Keeps any copy that has a dirty marker; see ResetAfterFailedOpenAsync.)
            await App.Checkout.ResetAfterFailedOpenAsync();
            Close();
            return null;
        }
        // ─────────────────────────────────────────────────────────────────────

        // Resolve the canonical source path D for the title bar and backup service.
        // RunCheckoutAsync returns D' (write mode) or D'' (read-only mode), neither of
        // which should be shown to the user. CheckoutService.SourcePath is always D.
        var canonicalPath = App.Checkout.SourcePath;

        // Initialize DI and DB, wire up the view model, then reveal the window. The open-time integrity check of
        // the local copy (spec item 5, step 5d) can stop it: a copy another program is using, or a damaged one.
        // OpenWithLocalCopyCheckAsync handles both — Try again, one fresh copy, or the recovery chooser — and
        // returns no view model only after it has ended the half-opened session WITHOUT saving.
        //
        // The local file to open is read from the checkout at the moment of each call, NOT captured above
        // (spec item 3, step 3c): after a fresh copy the checkout may now hold a different file — a new D', or,
        // when the version gate ran again on it and found the database needs a newer TermPoint, a read-only D''
        // (RequiredAppVersion set, so the usual "needs TermPoint X" banner appears).
        MainWindowViewModel vm;
        try
        {
            var opened = await OpenWithLocalCopyCheckAsync(() => App.InitializeServices(App.Checkout.WorkingPath));
            if (opened.ViewModel is null)
            {
                // Nothing is open and nothing may be saved: the session has been ended and the user told why.
                // Close() runs OnClosing, which saves nothing (Mode is no longer WriteAccess and WorkingPath
                // is forgotten — see ResetAfterFailedOpenAsync).
                if (!opened.SharedDatabaseDamaged)
                {
                    Close();
                    return null;
                }

                // The shared database itself is damaged: offer the recovery chooser (Restore from a backup is
                // there). The guard taken for this database is given back first, so the pick may be this same
                // database after a restore, or another one. The caller opens the pick through the same steps.
                App.InstanceGuard.ReleaseAll();
                return await ShowDatabaseChooserAsync(ChooserMode.Recovery, RecoveryReason.Corrupt, canonicalPath);
            }

            vm = opened.ViewModel;
        }
        catch (DatabaseFolderNotWritableException blockedEx)
        {
            // The database's folder refused writes (most often Windows Controlled Folder Access on a
            // protected known folder such as Documents). Show a neutral, actionable message, offer
            // the technical detail for IT on request, then close — the user can reopen and pick a
            // different location.
            await ShowMessageAsync("Can't Save to This Folder", blockedEx.UserMessage);

            if (!string.IsNullOrEmpty(blockedEx.ItDetail))
            {
                var wantDetails = await ShowYesNoAsync(
                    "Technical Details",
                    "Would you like to see technical details for your IT support?");
                if (wantDetails)
                    await ShowMessageAsync("Technical Details (for IT)", blockedEx.ItDetail);
            }

            (Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
            return null;
        }

        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);
        await SetupMainWindowAsync(canonicalPath, vm);

        // Restored unsaved changes are still only in the working copy — make them
        // durable right away rather than leaving them one crash away from a second
        // recovery. Failure is non-fatal: the standard save-error banner (wired by
        // SetupMainWindowAsync) surfaces it and autosave/manual save will retry.
        if (recoverUnsaved && App.Checkout.Mode == CheckoutMode.WriteAccess)
            await App.Checkout.SaveAsync();

        return null;
    }

    /// <summary>
    /// The housekeeping that precedes every checkout, in its one fixed order — shared by startup and by
    /// <see cref="SwitchCoreAsync"/> so the two can never drift apart again:
    /// <list type="number">
    /// <item><description>sweep stale working-copy artifacts that hold no user data
    /// (<see cref="CheckoutService.CleanupStaleCrashArtifacts"/>);</description></item>
    /// <item><description>if a previous session's unsaved changes remain
    /// (<see cref="CheckoutService.DetectCrashRecovery"/>), ask whether to restore them
    /// (<see cref="OfferCrashRecoveryAsync"/>).</description></item>
    /// </list>
    /// Both steps touch only this user's LOCAL files. The orphaned <c>D.tmp</c> on the share is NOT
    /// deleted here: it may be a colleague's in-flight save, so <see cref="CheckoutService"/> removes it
    /// itself, and only once this process holds the write lock (spec item 4).
    ///
    /// <para><b>Call this ONLY while this process holds the single-instance guard for
    /// <paramref name="dbPath"/></b> (<see cref="DatabaseInstanceGuard"/>): every step assumes no other
    /// live TermPoint of this user is working on the same local files — which is exactly what the guard
    /// guarantees, and what a second copy launched beside a hung first one would violate.</para>
    ///
    /// <para><b>Observer mode skips step 2.</b> Crash recovery means taking over a prior <i>write</i>
    /// session, which is wrong for an observer who never holds the lock. The test mirrors
    /// <see cref="CheckoutService.CheckoutAsync"/>'s own: the "open as observer" setting applies unless
    /// <paramref name="ignoreObserverMode"/> (a database the user has just created). Startup and switching
    /// previously disagreed here (switching offered recovery to observers).</para>
    /// </summary>
    /// <param name="dbPath">The database (D) about to be checked out.</param>
    /// <param name="ignoreObserverMode">True only for a database the user has just created, which opens for editing.</param>
    /// <returns>True when the user chose to restore unsaved changes (the checkout then runs in recovery mode).</returns>
    private async Task<bool> CleanUpAndOfferRecoveryAsync(string dbPath, bool ignoreObserverMode)
    {
        App.Checkout.CleanupStaleCrashArtifacts(dbPath);

        var isObserver = AppSettings.Current.OpenInReaderMode && !ignoreObserverMode;
        if (!isObserver && App.Checkout.DetectCrashRecovery(dbPath))
            return await OfferCrashRecoveryAsync(dbPath);

        return false;
    }

    /// <summary>
    /// Tells the user another TermPoint of theirs is already open on this database
    /// (<see cref="DatabaseInstanceGuard.AlreadyOpenTitle"/> / <see cref="DatabaseInstanceGuard.AlreadyOpenMessage"/>).
    /// A Topmost dialog like every other message here, so it can never open behind the main window.
    /// </summary>
    private Task ShowAlreadyOpenMessageAsync()
        => ShowMessageAsync(DatabaseInstanceGuard.AlreadyOpenTitle, DatabaseInstanceGuard.AlreadyOpenMessage);

    /// <summary>
    /// Startup only: shows the database chooser in recovery mode with <see cref="RecoveryReason.AlreadyOpen"/>
    /// after the guard refused a database, so the user can pick a different one or exit. Goes through the
    /// same <see cref="ShowDatabaseChooserAsync"/> as every other startup chooser (non-modal, hidden and
    /// never closed — the Avalonia 12 compositor deadlock), so Exit and Create New behave exactly as there.
    /// </summary>
    /// <param name="refusedPath">The database that is already open elsewhere; shown beneath the banner.</param>
    /// <returns>The database the user picked, or null when they exited or started the wizard.</returns>
    private Task<string?> ShowAlreadyOpenChooserAsync(string refusedPath)
        => ShowDatabaseChooserAsync(ChooserMode.Recovery, RecoveryReason.AlreadyOpen, refusedPath);

    /// <summary>
    /// Runs the full checkout sequence for <paramref name="sourcePath"/> and returns
    /// the path that should be passed to <c>App.InitializeServices</c>.
    /// Handles stale-lock prompting inline.
    /// </summary>
    /// <param name="sourcePath">The database path D chosen by the user.</param>
    /// <param name="recoverUnsaved">
    /// True when the user chose to restore unsaved changes from a crashed session
    /// (see <see cref="OfferCrashRecoveryAsync"/>). Passed through to
    /// <see cref="CheckoutService.CheckoutAsync"/> / <see cref="CheckoutService.ForceCheckoutAsync"/>;
    /// when the checkout cannot deliver write access, the crash artifacts are kept on
    /// disk so recovery can be offered again at the next opportunity.
    /// </param>
    /// <param name="ignoreObserverMode">
    /// True only when the user has just created this database (File → New, or the wizard's new
    /// database): passed through to <see cref="CheckoutService.CheckoutAsync"/> so the "open as
    /// observer" setting does not make the brand-new database read-only. Everything else (license
    /// gate, lock, version gate) is unaffected. False for every ordinary open.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description><b>Write access:</b> D' (the local working copy).</description></item>
    /// <item><description><b>Read-only:</b> D'' (the local read-only snapshot).</description></item>
    /// <item><description><b>Stale lock — user declined takeover (or cancelled the short confirmation wait,
    /// <see cref="ShowStaleLockWaitAsync"/>), the holder proved to be active again at click
    /// time (<see cref="CheckoutOutcome.HolderActiveAgain"/>), force-checkout lost the race, or checkout failed:</b>
    /// D'' via <see cref="SetupSnapshotOrAskAsync"/>, with the lock file then watched
    /// (<see cref="CheckoutService.StartReaderPollingAsync"/>) so a later release offers "Take over as writer".
    /// D itself is NEVER returned: opening D with SQLite
    /// flips its header (breaking the active editor's next save) and holds it open.</description></item>
    /// <item><description><b><c>null</c>:</b> the checkout cannot proceed and the user has already been told
    /// why (network unreachable, database file missing, no local copy could be made and the user chose
    /// Close, or the version gate could not check the database and the user chose Close in the "in use"
    /// prompt — <see cref="AskTryAgainAfterVersionCheckAsync"/>; Try again repeats the whole checkout).
    /// Both callers treat <c>null</c> as "dialog shown, give up": startup closes the app, and
    /// <see cref="SwitchDatabaseAsync"/> — whose previous database is already closed by then — cleans up
    /// (<see cref="AbandonFailedSwitchAsync"/>) and shows the database chooser. Either way the caller must
    /// reset the working-copy bookkeeping (<see cref="CheckoutService.ResetAfterFailedOpenAsync"/>), because
    /// a failed checkout leaves <see cref="CheckoutService.WorkingPath"/> naming whatever was there before.</description></item>
    /// </list>
    /// </returns>
    private async Task<string?> RunCheckoutAsync(
        string sourcePath, bool recoverUnsaved = false, bool ignoreObserverMode = false)
    {
        var outcome = await App.Checkout.CheckoutAsync(sourcePath, recoverUnsaved, ignoreObserverMode);

        switch (outcome)
        {
            case CheckoutOutcome.WriteAccess:
                return App.Checkout.WorkingPath;

            case CheckoutOutcome.ReadOnly:
                // The "someone else is editing" explanation is wrong when the version gate
                // tripped (RequiredAppVersion set): nobody holds the lock, this app is simply too
                // old. The read-only banner explains that case, and the kept recovery data will be
                // offered again after the user updates.
                if (recoverUnsaved && App.Checkout.RequiredAppVersion is null)
                    await ShowRecoveryDeferredMessageAsync();
                return App.Checkout.WorkingPath;

            case CheckoutOutcome.RecoveryConflict:
                // The unsaved changes can't be applied automatically (D changed since
                // the crash, or the marker is unverifiable). Offer an exported copy,
                // then discard the artifacts and re-run a normal checkout.
                await HandleRecoveryConflictAsync(sourcePath);
                return await RunCheckoutAsync(sourcePath, recoverUnsaved: false, ignoreObserverMode);

            case CheckoutOutcome.StaleHolder:
                {
                    // The wording (who, how long, and the warning that the holder's unsaved changes
                    // can't go into the database) lives in StaleLockPrompt so it can be unit-tested.
                    // Closing the dialog counts as "Open read-only": never take over by accident.
                    var take = await ShowChoiceAsync(
                        StaleLockPrompt.TakeOverTitle,
                        StaleLockPrompt.BuildTakeOverMessage(App.Checkout.CurrentHolder, DateTime.UtcNow),
                        StaleLockPrompt.TakeOverButton,
                        StaleLockPrompt.OpenReadOnlyButton);

                    // Spec item 9b: the takeover also needs the holder's heartbeat to have stood still for
                    // a full confirmation window on THIS computer's clock. Usually reading the prompt took
                    // long enough and there is nothing to wait for; otherwise show a short cancellable
                    // wait. Cancelling (or closing it) is exactly "Open read-only".
                    var wait = take ? App.Checkout.GetTakeOverWait() : TimeSpan.Zero;
                    if (wait > TimeSpan.Zero)
                        take = await ShowStaleLockWaitAsync(wait);

                    if (take)
                    {
                        // ForceCheckoutAsync re-reads the lock file first and only breaks it if it is
                        // still the same lock the prompt was about (spec item 9a).
                        var forceOutcome = await App.Checkout.ForceCheckoutAsync(recoverUnsaved);
                        if (forceOutcome == CheckoutOutcome.WriteAccess)
                            return App.Checkout.WorkingPath;
                        if (forceOutcome == CheckoutOutcome.ReadOnly)
                        {
                            // Version gate tripped during the takeover: D'' is already set up and
                            // the banner explains why. Do not fall through to the "lost the race"
                            // path below, whose recovery message would be misleading.
                            return App.Checkout.WorkingPath;
                        }
                        if (forceOutcome == CheckoutOutcome.RecoveryConflict)
                        {
                            await HandleRecoveryConflictAsync(sourcePath);
                            return await RunCheckoutAsync(sourcePath, recoverUnsaved: false, ignoreObserverMode);
                        }
                        if (forceOutcome == CheckoutOutcome.NetworkUnreachable)
                        {
                            await ShowMessageAsync("Network Unreachable",
                                "Cannot reach the database — check your network connection and try again.");
                            return null;
                        }
                        if (forceOutcome == CheckoutOutcome.SourceUnreadable)
                        {
                            // D is held open by another program (or access denied). The lock was
                            // released again; no read-only fallback is possible (it must read D too).
                            await ShowMessageAsync(DatabaseSwitchPrecheck.UnreadableTitle,
                                DatabaseChooserViewModel.UnreadableDatabaseMessage);
                            return null;
                        }
                        if (forceOutcome == CheckoutOutcome.VersionCheckUnavailable)
                        {
                            // The version gate could not read (or raise) the stamp on the takeover's local copy.
                            // The forced lock was released again, so Try again is a whole new checkout.
                            return await AskTryAgainAfterVersionCheckAsync(sourcePath, recoverUnsaved, ignoreObserverMode);
                        }
                        if (forceOutcome == CheckoutOutcome.HolderActiveAgain)
                        {
                            // The holder woke up (or someone else took the lock) while the prompt was
                            // open; nothing was deleted. Say so, then open read-only below.
                            await ShowMessageAsync(StaleLockPrompt.ActiveAgainTitle,
                                StaleLockPrompt.BuildActiveAgainMessage(App.Checkout.CurrentHolder));
                        }
                        // Otherwise lost the race to another instance — fall through to read-only setup.
                    }

                    // User declined, the holder is active again, or force-checkout lost the race.
                    // Set up D'' so we never hold D open directly. Crash artifacts (if
                    // any) stay on disk so recovery can be offered at the next launch.
                    // If D'' cannot be made, ask the user — never fall back to D itself.
                    if (recoverUnsaved)
                        await ShowRecoveryDeferredMessageAsync();
                    var readOnlyPath = await SetupSnapshotOrAskAsync();

                    // Watch the lock file, so that if the holder later releases cleanly (or goes
                    // stale again) this read-only session is offered "Take over as writer" instead
                    // of staying read-only until restart.
                    if (readOnlyPath is not null)
                        await App.Checkout.StartReaderPollingAsync();
                    return readOnlyPath;
                }

            case CheckoutOutcome.NetworkUnreachable:
                await ShowMessageAsync("Network Unreachable",
                    "Cannot reach the database — check your network connection and try again.");
                return null;

            case CheckoutOutcome.SourceMissing:
                // D vanished between the caller's checks (or its creation) and checkout. Nothing
                // was locked or created, and there is no safe fallback — tell the user and stop.
                await ShowMessageAsync(DatabaseSwitchPrecheck.MissingTitle,
                    DatabaseSwitchPrecheck.MissingMessage(sourcePath));
                return null;

            case CheckoutOutcome.SourceUnreadable:
                // D exists but another program holds it open (or access is denied). The checkout
                // already released any lock it took and left no working copy behind (crash-recovery
                // data, if any, is kept). A retry will usually succeed — and this is NOT damage.
                await ShowMessageAsync(DatabaseSwitchPrecheck.UnreadableTitle,
                    DatabaseChooserViewModel.UnreadableDatabaseMessage);
                return null;

            case CheckoutOutcome.VersionCheckUnavailable:
                // The local copy was made, but the version gate could not read its MinimumVersionToWrite stamp
                // (or raise one) even after its quiet retries — typically antivirus still holding the file that
                // was just copied. Write access was refused, the lock released and (outside recovery) the copy
                // removed; recovery data, if any, is kept. Same prompt as the open-time integrity check.
                return await AskTryAgainAfterVersionCheckAsync(sourcePath, recoverUnsaved, ignoreObserverMode);

            default: // Failed
                await ShowMessageAsync("Checkout Failed",
                    "Could not open the database for editing. " +
                    "The application will open in read-only mode.");
                // Open read-only on D''. Crash artifacts (if any) stay on disk for the next
                // launch. If D'' cannot be made, ask the user — never fall back to D itself.
                return await SetupSnapshotOrAskAsync();
        }
    }

    /// <summary>
    /// Handles <see cref="CheckoutOutcome.VersionCheckUnavailable"/> (spec item 3, step 3c): the version gate could
    /// not read, or raise, the database's stamp even after its quiet retries, so write access was refused. Shows the
    /// same "Couldn't Open the Database" prompt the open-time integrity check uses for a local copy that another
    /// program is holding (<see cref="LocalCopyCheckPrompt"/>; the cause is the same kind of thing and the wording
    /// is the user-approved one) with <b>Try again</b> / <b>Close</b>. Try again runs the whole checkout again with
    /// the same arguments (the previous attempt released its lock and, outside recovery, removed its copy, so it
    /// starts from scratch; a recovery open still finds its kept copy and marker). Close gives up exactly like
    /// the other failed checkouts: nothing is saved and no lock is left.
    /// </summary>
    /// <param name="sourcePath">The database path D chosen by the user.</param>
    /// <param name="recoverUnsaved">As for <see cref="RunCheckoutAsync"/>.</param>
    /// <param name="ignoreObserverMode">As for <see cref="RunCheckoutAsync"/>.</param>
    /// <returns>
    /// The result of the repeated <see cref="RunCheckoutAsync"/> when the user chose Try again; <c>null</c>
    /// (the prompt has been shown — the contract <see cref="RunCheckoutAsync"/> documents) when they chose Close.
    /// </returns>
    private async Task<string?> AskTryAgainAfterVersionCheckAsync(
        string sourcePath, bool recoverUnsaved, bool ignoreObserverMode)
    {
        var tryAgain = await ShowChoiceAsync(
            LocalCopyCheckPrompt.CouldntOpenTitle,
            LocalCopyCheckPrompt.InUseMessage,
            LocalCopyCheckPrompt.TryAgainButton,
            LocalCopyCheckPrompt.CloseButton);

        return tryAgain
            ? await RunCheckoutAsync(sourcePath, recoverUnsaved, ignoreObserverMode)
            : null;
    }

    /// <summary>
    /// Sets up the local read-only snapshot D'' and, when that fails, asks the user whether to
    /// try again — instead of falling back to opening the shared database D itself (which changes
    /// D's header and holds it open; spec item 4). The retry loop is
    /// <see cref="CheckoutService.SetupReadOnlySnapshotWithRetryAsync"/>; this method only supplies
    /// the Topmost "Try again / Close" dialog.
    /// </summary>
    /// <returns>
    /// The path to D'' on success. <c>null</c> when the user chose Close; the dialog has already been
    /// shown, which is the contract <see cref="RunCheckoutAsync"/> documents for a <c>null</c> return.
    /// </returns>
    private Task<string?> SetupSnapshotOrAskAsync()
        => App.Checkout.SetupReadOnlySnapshotWithRetryAsync(() => ShowChoiceAsync(
            LocalCopyCheckPrompt.CouldntOpenTitle,
            LocalCopyCheckPrompt.CouldntMakeCopyMessage,
            LocalCopyCheckPrompt.TryAgainButton, LocalCopyCheckPrompt.CloseButton));

    /// <summary>
    /// Tells the user their unsaved changes are kept but can't be restored right now
    /// because this session did not get write access. Shown when a recovery-mode
    /// checkout lands in read-only (another writer holds the lock, or the user
    /// declined a stale-lock takeover).
    /// </summary>
    private Task ShowRecoveryDeferredMessageAsync()
        => ShowMessageAsync("Unsaved Changes Kept",
            "Someone else is currently editing this database, so your unsaved changes " +
            "can't be restored right now. They have been kept, and TermPoint will offer " +
            "to restore them the next time you open this database with write access.");

    // ── Open-time integrity check of the local copy (spec item 5, step 5d) ─────

    /// <summary>
    /// How <see cref="OpenWithLocalCopyCheckAsync"/> ended: the view model of the opened database, or null when the
    /// open was given up (the half-opened session has then been ended WITHOUT saving).
    /// </summary>
    /// <param name="ViewModel">The root view model, or null when the open was given up.</param>
    /// <param name="SharedDatabaseDamaged">
    /// True when the open was given up because the SHARED database itself is damaged (the user has been told); the
    /// caller then offers the recovery chooser with the damaged reason. False for every other give-up.
    /// </param>
    private sealed record LocalCopyOpenResult(MainWindowViewModel? ViewModel, bool SharedDatabaseDamaged);

    /// <summary>
    /// Runs <paramref name="initialize"/> (a call of <see cref="App.InitializeServices"/>) and handles what its
    /// open-time integrity check of the local copy can say (spec item 5, step 5d) — the ONE handler shared by
    /// startup and by database switching. The decisions are the pure table in
    /// <see cref="LocalCopyCheckDecision"/>; this method gathers the inputs, performs the action and asks again.
    ///
    /// <list type="bullet">
    /// <item><description><b>In use</b> (<see cref="DatabaseInUseException"/>: another program has the files, even
    /// after the quiet retries): "Couldn't Open the Database" with <b>Try again</b> / <b>Close</b>. Try again runs
    /// <paramref name="initialize"/> again (with its quiet retries); Close gives up.</description></item>
    /// <item><description><b>Damaged</b> (<see cref="DatabaseCorruptException"/>):
    /// <list type="number">
    /// <item><description>A copy holding recovered unsaved changes is set aside, never deleted
    /// (<see cref="CheckoutService.ArchiveCorruptWorkingCopy"/>), with the "Unsaved Changes Lost" message.</description></item>
    /// <item><description>The shared database is re-checked read-only (the front-door check). Damaged: say so, give
    /// up, and let the caller show the recovery chooser. Unreadable, unreachable or gone: say what the front door
    /// says for that case and give up. Fine: make ONE fresh copy
    /// (<see cref="CheckoutService.RefreshLocalCopyAfterFailedCheckAsync"/>) and run <paramref name="initialize"/>
    /// again — passing opens normally; failing again, or no fresh copy possible, sets the bad copy aside (kept) and
    /// shows the "keeps failing" message with a single Close.</description></item>
    /// </list></description></item>
    /// </list>
    ///
    /// <para><b>Giving up always ends the session WITHOUT saving</b> (<see cref="GiveUpLocalCopyOpenAsync"/>): the
    /// services are released and <see cref="CheckoutService.ResetAfterFailedOpenAsync"/> gives the lock back and
    /// forgets the working copy, so neither the caller's <c>Close()</c> nor the next switch can save or delete
    /// anything. A copy with a dirty marker is kept for the next start.</para>
    ///
    /// <para>Any other exception from <paramref name="initialize"/> (for example
    /// <see cref="DatabaseFolderNotWritableException"/>) propagates to the caller, unchanged.</para>
    /// </summary>
    /// <param name="initialize">
    /// Builds the services over the local copy and returns the root view model; may throw the two exceptions above.
    /// It is called again after a fresh copy and must therefore read the local file from
    /// <see cref="CheckoutService.WorkingPath"/> at the moment it is called, not from a path captured earlier: a
    /// fresh copy can change which file the checkout holds (spec item 3, step 3c) — a new D', or, if the version
    /// gate then found the database needs a newer TermPoint, a read-only D'' (<see cref="CheckoutService.RequiredAppVersion"/>
    /// set, so the read-only banner explains why).
    /// </param>
    /// <returns>The opened database's view model, or a give-up (see <see cref="LocalCopyOpenResult"/>).</returns>
    private async Task<LocalCopyOpenResult> OpenWithLocalCopyCheckAsync(Func<MainWindowViewModel> initialize)
    {
        // Captured now: giving up forgets the session's paths (ResetAfterFailedOpenAsync).
        var sharedPath = App.Checkout.SourcePath;
        var shared     = SharedDatabaseVerdict.NotChecked;
        var fresh      = FreshCopyState.NotTried;

        // Set once the recovered-changes archive has been attempted, so a marker that somehow cannot be removed
        // can never make the table ask for the archive (and show its message) over and over.
        var recoveredChangesHandled = false;

        while (true)
        {
            LocalCopyVerdict copy;
            try
            {
                var vm = initialize();
                if (fresh == FreshCopyState.Made)
                    App.Logger.LogInfo(App.Checkout.RequiredAppVersion is null
                        ? "LocalCopyCheck: the database opened normally with a fresh copy."
                        : "LocalCopyCheck: the database opened read-only with a fresh copy (the version gate found it needs a newer TermPoint).");
                return new LocalCopyOpenResult(vm, SharedDatabaseDamaged: false);
            }
            catch (DatabaseInUseException ex)
            {
                copy = LocalCopyVerdict.Locked;
                App.Logger.LogInfo($"LocalCopyCheck: the local copy is in use by another program ({ex.Summary}).");
            }
            catch (DatabaseCorruptException ex)
            {
                copy = LocalCopyVerdict.Damaged;
                App.Logger.LogInfo($"LocalCopyCheck: the local copy is damaged ({ex.Summary}).");
            }

            // Ask the table what to do until it says to open again, or to give up.
            var openAgain = false;
            while (!openAgain)
            {
                var holdsRecoveredChanges = !recoveredChangesHandled && App.Checkout.HasDirtyMarker;
                var action = LocalCopyCheckDecision.Decide(copy, holdsRecoveredChanges, shared, fresh);
                App.Logger.LogInfo($"LocalCopyCheck: {copy} copy, shared database {shared}, fresh copy {fresh} -> {action}.");

                switch (action)
                {
                    case LocalCopyAction.ShowInUseDialog:
                        if (!await ShowChoiceAsync(
                                LocalCopyCheckPrompt.CouldntOpenTitle,
                                LocalCopyCheckPrompt.InUseMessage,
                                LocalCopyCheckPrompt.TryAgainButton,
                                LocalCopyCheckPrompt.CloseButton))
                            return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);
                        openAgain = true;
                        break;

                    case LocalCopyAction.ArchiveRecoveredChangesThenContinue:
                        // The recovered unsaved changes are in a damaged file: set it aside (kept, not deleted)
                        // and say so honestly. Asking again then finds no marker.
                        recoveredChangesHandled = true;
                        App.Checkout.ArchiveCorruptWorkingCopy(sharedPath);
                        await ShowMessageAsync(
                            LocalCopyCheckPrompt.UnsavedChangesLostTitle, LocalCopyCheckPrompt.UnsavedChangesLostMessage);
                        break;

                    case LocalCopyAction.CheckSharedDatabase:
                        shared = LocalCopyIntegrity.ToVerdict(
                            await LocalCopyIntegrity.RecheckSharedDatabaseAsync(sharedPath, App.IntegrityFaults, App.Logger));
                        break;

                    case LocalCopyAction.MakeFreshCopy:
                        // Nothing has the local copy open (the gate throws before any connection is made), but
                        // release the half-built services anyway so nothing can.
                        App.ReleaseServices();
                        // Made → open again. Otherwise ask the table again: a copy that couldn't be made because the
                        // shared location stopped responding or a file was held gets the front-door message for that;
                        // any other failure gets "keeps failing".
                        (shared, fresh) = LocalCopyIntegrity.AfterFreshCopy(
                            await App.Checkout.RefreshLocalCopyAfterFailedCheckAsync(), shared);
                        openAgain = fresh == FreshCopyState.Made;
                        break;

                    case LocalCopyAction.ShowKeepsFailing:
                        // Kept, not deleted: the bad file is evidence for IT. Then the session ends.
                        App.Checkout.SetAsideFailedLocalCopy();
                        await ShowMessageAsync(
                            LocalCopyCheckPrompt.CouldntOpenTitle, LocalCopyCheckPrompt.KeepsFailingMessage,
                            LocalCopyCheckPrompt.CloseButton);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);

                    case LocalCopyAction.ShowDamagedThenRecoveryChooser:
                        await ShowMessageAsync(
                            LocalCopyCheckPrompt.DamagedSharedTitle, LocalCopyCheckPrompt.DamagedSharedMessage);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: true);

                    case LocalCopyAction.ShowUnreadableMessage:
                    case LocalCopyAction.ShowUnreachableMessage:
                    case LocalCopyAction.ShowMissingMessage:
                        var (title, message) = LocalCopyCheckPrompt.FrontDoorMessage(shared, sharedPath);
                        await ShowMessageAsync(title, message);
                        return await GiveUpLocalCopyOpenAsync(sharedDatabaseDamaged: false);

                    default:
                        // OpenNormally cannot happen here (the copy failed); treat any surprise as "open again".
                        openAgain = true;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Ends a half-opened session WITHOUT saving, so a give-up in <see cref="OpenWithLocalCopyCheckAsync"/> can
    /// never push a damaged or recovered local copy over the shared database, and nothing later can delete one:
    /// <list type="number">
    /// <item><description><see cref="App.ReleaseServices"/> disposes the container that
    /// <see cref="App.InitializeServices"/> built before its check threw.</description></item>
    /// <item><description><see cref="CheckoutService.ResetAfterFailedOpenAsync"/> stops autosave, gives back the write
    /// lock WITHOUT saving, parks <see cref="CheckoutService.Mode"/> at read-only (so the exit path of
    /// <see cref="OnClosing"/> sees no write session and saves nothing), keeps a working copy that has a dirty marker
    /// (unsaved work is never deleted) and forgets the session's paths (so <c>CleanupWorkingCopy</c> has nothing to
    /// delete).</description></item>
    /// </list>
    /// Each step is attempted even if the other failed; neither throws. The single-instance guard is left to the
    /// caller, as each path already handles it.
    /// </summary>
    /// <param name="sharedDatabaseDamaged">Whether the shared database itself was found damaged (passed through to the result).</param>
    /// <returns>A give-up result (no view model).</returns>
    private async Task<LocalCopyOpenResult> GiveUpLocalCopyOpenAsync(bool sharedDatabaseDamaged)
    {
        App.Logger.LogInfo("LocalCopyCheck: giving up on this open — ending the session without saving.");

        try { App.ReleaseServices(); }
        catch (Exception ex) { App.Logger.LogError(ex, "LocalCopyCheck: could not dispose the services of the failed open"); }

        try { await App.Checkout.ResetAfterFailedOpenAsync(); }
        catch (Exception ex) { App.Logger.LogError(ex, "LocalCopyCheck: could not reset the checkout state after the failed open"); }

        return new LocalCopyOpenResult(null, sharedDatabaseDamaged);
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
        App.Checkout.WriteLockLost -= OnWriteLockLost;
        App.Checkout.BecameDirty     -= OnCheckoutBecameDirty;
        App.Checkout.SaveStarted     -= OnCheckoutSaveStarted;
        App.Checkout.SaveFinished    -= OnCheckoutSaveFinished;
        App.Checkout.SaveAlreadyInProgress -= OnCheckoutSaveAlreadyInProgress;
        App.Checkout.SaveCompleted   += OnCheckoutSaveCompleted;
        App.Checkout.SaveFailed      += OnCheckoutSaveFailed;
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
    /// <para><b>Pre-check before any teardown:</b> the first thing this does is validate the target with
    /// <see cref="DatabaseSwitchPrecheck"/> (the same validator the database chooser uses). If the
    /// target is missing (and may not be created), held open by another program, damaged or
    /// unreachable, the user is told why and this RETURNS with the current database still open and
    /// <see cref="AppSettings.DatabasePath"/> and the recent list unchanged. Previously every one of
    /// those cases was discovered only after the current database had been saved and disposed — and a
    /// missing target silently produced a new, empty database at the old path.</para>
    ///
    /// <para><b>Single-instance guard (spec item 4b), the very first step:</b> the guard for the TARGET
    /// database is taken before the pre-check and before anything is closed
    /// (<see cref="DatabaseInstanceGuard"/>). If another live TermPoint of this user already holds it, the
    /// user is told ("TermPoint Is Already Open") and the switch is refused exactly like a pre-check
    /// refusal: they stay on the current database, or — with none open — go to the chooser. Order of the
    /// guards through a switch from A to B: acquire B (refused → stay on A, nothing touched); pre-check
    /// refusal → release B, keep A; A torn down → release A, keep B (a no-op for A when B is the same
    /// database, e.g. "Take over as writer"); any failure after that
    /// (<see cref="AbandonFailedSwitchAsync"/>) → release B, so the no-database state holds no guard.</para>
    ///
    /// <para>May be called before any DI container exists (the wizard's existing-database path only
    /// validates D and saves settings), so every <see cref="App.Services"/> access before
    /// <see cref="App.InitializeServices"/> is null-tolerant.</para>
    ///
    /// <para><b>A database the user has just created opens for editing</b> even when the "open as
    /// observer" setting is on (they must be able to write into it straight away — File → New
    /// applies the transferred configuration right after this returns). That covers a file this
    /// call creates itself and, via <paramref name="isNewDatabase"/>, the wizard's new database.
    /// Only the observer setting is overridden, for this one checkout; the license gate, the lock
    /// and crash-recovery steps behave as for any other open, and the setting still applies to every
    /// later open of the database.</para>
    ///
    /// <para><b>When the switch fails (spec item 4d):</b> a failure BEFORE the previous database is touched
    /// (the pre-check's refusal) leaves the user on it, exactly as before — provided a database is actually
    /// open (<see cref="_isDatabaseOpen"/>). Once it has been closed, or when no database is open to begin
    /// with (retrying from the chooser, the post-wizard open), a failure ends in the same clean "no database
    /// open" state as startup: the failed attempt is cleaned up (<see cref="AbandonFailedSwitchAsync"/>), the
    /// window is covered, and the startup database chooser is shown in recovery mode
    /// (<see cref="RecoveryReason.SwitchFailed"/>) — repeatedly, until a database opens or the user chooses
    /// Exit (or Create New, which hands over to the setup wizard). See <see cref="DatabaseSwitchRecovery"/>.</para>
    /// </summary>
    /// <param name="newDatabasePath">Full path of the database (D) to switch to.</param>
    /// <param name="isNewDatabase">
    /// True when the caller has already created this database during the current user action (the
    /// startup wizard's new-database path), so the observer setting must not make it read-only.
    /// False for every other caller; a file created by this very call is detected internally.
    /// </param>
    /// <param name="createIfMissing">
    /// True ONLY from File → New: a missing <paramref name="newDatabasePath"/> is then created. False
    /// (the default) for every other caller — Recent…, File → Open, "Switch to edit mode", the
    /// post-wizard open — for which a missing file is an error ("Database Not Found"), never an
    /// invitation to create an empty database at a path that has moved. With <c>true</c>, a target
    /// that already EXISTS is refused too (File → New never adopts an existing file).
    /// </param>
    /// <returns>
    /// <c>true</c> ONLY when the REQUESTED database ended up open: the checkout succeeded (write access
    /// or the read-only snapshot) and the services and window were set up for it. <c>false</c> on every
    /// refusal or failure — pre-check refusal (current database untouched), creation failure
    /// (unreachable / already exists), a checkout that could not proceed (dialog shown), or any
    /// exception (reported in an error dialog).
    ///
    /// <para><b>A <c>false</c> after the failure recovery above does NOT mean nothing is open.</b> The
    /// chooser may have opened a DIFFERENT database before this returns — the original request still failed,
    /// so the result is still <c>false</c>. That is what keeps File → New's configuration transfer from ever
    /// being written into a database the user picked from the chooser afterwards. Callers that go on to
    /// write into "the open database" — File → New — MUST check it; others may ignore it.</para>
    /// </returns>
    public async Task<bool> SwitchDatabaseAsync(
        string newDatabasePath, bool isNewDatabase = false, bool createIfMissing = false)
    {
        var result = await SwitchCoreAsync(newDatabasePath, isNewDatabase, createIfMissing);

        if (result == SwitchResult.NoDatabaseOpen)
            await RecoverToDatabaseChooserAsync(newDatabasePath);

        return result == SwitchResult.Opened;
    }

    /// <summary>
    /// One attempt to switch databases: the pre-check, the teardown of the current database, the checkout
    /// and the wiring of the new one — everything <see cref="SwitchDatabaseAsync"/> documents EXCEPT the
    /// recovery to the chooser. Reports how it ended and, for the one failure that needs it, cleans up
    /// after itself, so the caller (and the chooser loop, which calls this again for each pick) only has
    /// to look at the result.
    /// </summary>
    /// <param name="newDatabasePath">Full path of the database (D) to switch to.</param>
    /// <param name="isNewDatabase">See <see cref="SwitchDatabaseAsync"/>.</param>
    /// <param name="createIfMissing">See <see cref="SwitchDatabaseAsync"/>.</param>
    /// <returns>
    /// <see cref="SwitchResult.Opened"/>; <see cref="SwitchResult.StayedOnCurrentDatabase"/> after a refusal
    /// that left the open database untouched; or <see cref="SwitchResult.NoDatabaseOpen"/> (after a failure
    /// past the point of no return, <see cref="AbandonFailedSwitchAsync"/> has already run). A failure of the
    /// switch itself is reported in a dialog and returned, not thrown.
    /// </returns>
    private async Task<SwitchResult> SwitchCoreAsync(
        string newDatabasePath, bool isNewDatabase, bool createIfMissing)
    {
        App.Logger.LogBreadcrumb("SwitchDatabase", new() { ["path"] = newDatabasePath });
        var phase = SwitchPhase.BeforeTeardown;
        _noDatabaseReason = RecoveryReason.SwitchFailed;
        try
        {
            // ── Single-instance guard for the TARGET (spec item 4b) ──────────────────────────────
            // Taken first, before the pre-check and before anything is closed or recorded, and before
            // any cleanup or crash detection below. If another live TermPoint of this user has the
            // target open, nothing at all happens to it (no cleanup, no recovery offer, no lock, no
            // copy) and nothing here changes: the open database stays open, and the settings and the
            // recent list are untouched. With no database open (the chooser retry, the post-wizard
            // open) the chooser follows instead, saying why.
            if (App.InstanceGuard.TryAcquire(newDatabasePath) == InstanceGuardResult.HeldByAnotherProcess)
            {
                App.Logger.LogInfo("SwitchDatabase: refused before teardown — the database is already open in another TermPoint process.");
                _noDatabaseReason = RecoveryReason.AlreadyOpen;
                await ShowAlreadyOpenMessageAsync();
                return DatabaseSwitchRecovery.Decide(phase, _isDatabaseOpen);
            }

            // Refuse BEFORE touching anything — settings, recents, the current database's save and
            // teardown — when the target can't be opened. Only a target that passes updates
            // AppSettings.DatabasePath and the recent list (see DatabaseSwitchPrecheck).
            var precheck = await DatabaseSwitchPrecheck.CheckAndRecordAsync(
                newDatabasePath, createIfMissing, AppSettings.Current);
            if (!precheck.CanProceed)
            {
                // The target will not be opened: give its guard back, keeping the open database's own.
                ReleaseSwitchTargetGuard();

                App.Logger.LogInfo($"SwitchDatabase: refused before teardown — {precheck.Title}");
                await ShowMessageAsync(precheck.Title, precheck.Message);

                // The open database (if any) is untouched: stay on it. With NO database open (retrying
                // from the chooser, the post-wizard open) there is nothing to stay on — the chooser follows.
                return DatabaseSwitchRecovery.Decide(phase, _isDatabaseOpen);
            }

            // ── Point of no return ───────────────────────────────────────────────────────────────
            // From here the previous database is taken apart. Whatever happens next, nothing is open
            // any more, so any failure must end in the no-database state (AbandonFailedSwitchAsync +
            // the chooser) — never in a window still bound to a database that has been closed.
            phase           = SwitchPhase.AfterTeardown;
            _isDatabaseOpen = false;

            // Release the current checkout (saves if dirty) before opening the new DB.
            var oldDbName    = Path.GetFileNameWithoutExtension(App.Checkout.SourcePath);
            var wasWriteMode = App.Checkout.Mode == CheckoutMode.WriteAccess;
            App.Checkout.StopAutoSave();
            var saveOutcome = await App.Checkout.ReleaseAsync(saveFirst: wasWriteMode);

            // Capture the current semester selection before DI teardown so it can be
            // restored after reinit without touching AppSettings (avoids a disk round-trip
            // and preserves the live in-memory state, e.g. when upgrading read→write).
            string? savedYearId = null;
            IReadOnlySet<string>? savedSemesterIds = null;
            // App.Services is null when no database has been opened yet (wizard join path).
            if (App.Services?.GetService(typeof(SemesterContext)) is SemesterContext priorCtx
                && priorCtx.SelectedAcademicYear is not null)
            {
                savedYearId      = priorCtx.SelectedAcademicYear.Id;
                savedSemesterIds = priorCtx.SelectedSemesters.Select(s => s.Semester.Id).ToHashSet();
            }

            // Stop the periodic backup timer BEFORE disposing the DI container.
            if (App.Services?.GetService(typeof(BackupService)) is BackupService oldBackup)
                oldBackup.StopSession();

            // Close the old DatabaseContext (and all other DI singletons) NOW, before
            // CheckoutAsync copies D to D'. Also clears App.Services (the same "nothing open" value
            // as at startup), so the lines above — which this method runs again when the chooser
            // retries — never meet a disposed container.
            App.ReleaseServices();

            // Delete the old D' ONLY when its content is safely in the old D (save
            // succeeded) or nothing was edited (no dirty marker). Otherwise leave the
            // working copy and marker behind — the next time the user opens that
            // database they'll be offered their unsaved changes back.
            if (saveOutcome == SaveOutcome.Success || !App.Checkout.HasDirtyMarker)
            {
                App.Checkout.CleanupWorkingCopy();
            }
            else if (wasWriteMode)
            {
                await ShowMessageAsync("Changes Not Saved",
                    $"Your latest changes to \"{oldDbName}\" could not be saved. " +
                    "They have been kept, and TermPoint will offer to restore them " +
                    "the next time you open that database.");
            }

            // The previous database is completely closed — its final save done, its working copy deleted
            // or deliberately kept — so give ITS guard back and keep only the target's. (When the target
            // is the very database that was open, e.g. "Take over as writer", its guard is the one kept.
            // With nothing open before, there was nothing to release.) From here until a database is
            // wired to the window, the held guard belongs to the database being opened; if that fails,
            // AbandonFailedSwitchAsync releases it.
            App.InstanceGuard.ReleaseAllExcept(newDatabasePath);

            // If D does not exist yet and the caller asked for creation (File → New ONLY), create
            // it with the schema so CheckoutAsync takes the normal D → D' path and all subsequent
            // writes go to the local working copy. Deadline-bounded tri-state probe: only create
            // when the location answered "missing" — an unreachable share must NOT trigger
            // creation attempts; checkout below surfaces the unreachable dialog instead.
            // The file is built in a LOCAL temp file and copied across by NewDatabaseFile:
            // SQLite never opens the shared path (no WAL header left on D, no handle held).
            // When creation is NOT requested, a file that vanished after the pre-check (it passed
            // a moment ago) is left alone: CheckoutAsync reports SourceMissing ("Database Not
            // Found") instead of this method inventing an empty database.
            if (createIfMissing
                && await NetworkFileOps.ProbeFileAsync(newDatabasePath) == FileProbeResult.Missing)
            {
                var created = await NewDatabaseFile.CreateAsync(newDatabasePath);
                switch (created.Outcome)
                {
                    case NewDatabaseOutcome.Created:
                        isNewDatabase = true;   // just created: the observer setting must not make it read-only
                        break;

                    case NewDatabaseOutcome.Unreachable:
                        await ShowMessageAsync("Network Unreachable", NetworkFileOps.UnreachableMessage);
                        return await AbandonFailedSwitchAsync();

                    case NewDatabaseOutcome.AlreadyExists:
                        // Someone created a file with this name while we were building ours. It was
                        // not touched; don't silently open somebody else's database as "the new one".
                        // (Same wording as the pre-check's refusal of an existing file for File → New.)
                        await ShowMessageAsync(DatabaseSwitchPrecheck.AlreadyExistsTitle,
                            DatabaseSwitchPrecheck.AlreadyExistsMessage(newDatabasePath));
                        return await AbandonFailedSwitchAsync();

                    default: // Failed — the outer catch logs it and shows "Failed to switch to database: …"
                        throw new InvalidOperationException(
                            $"The new database could not be created: {created.Error?.Message}", created.Error);
                }
            }

            // Re-evaluate license for the new share directory before checkout.
#if !BROWSER
            App.EvaluateLicense(newDatabasePath);
#endif

            // Run crash recovery + checkout for the new path. The guard for it was taken at the top, so
            // this housekeeping (stale-artifact sweep, recovery offer) cannot disturb a
            // live copy of TermPoint on the same database. Observers are not offered recovery, as at startup.
            var recoverUnsaved = await CleanUpAndOfferRecoveryAsync(newDatabasePath, ignoreObserverMode: isNewDatabase);

            var checkoutPath = await RunCheckoutAsync(
                newDatabasePath, recoverUnsaved, ignoreObserverMode: isNewDatabase);
            if (checkoutPath is null)
            {
                // Checkout cannot proceed — dialog already shown (see RunCheckoutAsync). The previous
                // database is already closed, so this ends in the no-database state, not a dead window.
                return await AbandonFailedSwitchAsync();
            }

            // Reinitialize DI and set new data context. The open-time integrity check of the local copy
            // (spec item 5, step 5d) is handled here exactly as at startup: a copy another program is using
            // (Try again / Close) or a damaged one (one fresh copy, or the recovery chooser). It returns no
            // view model only after ending the half-opened session WITHOUT saving; the previous database is
            // already closed, so that ends in the no-database state — with the damaged reason when it is the
            // shared database that is damaged. The raw "Failed to switch to database: ..." text below can
            // therefore never name the local copy's path for these two failures.
            //
            // As at startup, the local file is read from the checkout at the moment of each call (spec item 3,
            // step 3c), because a fresh copy can leave the checkout holding a different file: a new D', or a
            // read-only D'' when the version gate ran again on the fresh copy and found it needs a newer
            // TermPoint. (checkoutPath above is that file for the first call; it is only used for the null test.)
            var opened = await OpenWithLocalCopyCheckAsync(
                () => App.InitializeServices(App.Checkout.WorkingPath, savedYearId, savedSemesterIds));
            if (opened.ViewModel is null)
            {
                if (opened.SharedDatabaseDamaged)
                    _noDatabaseReason = RecoveryReason.Corrupt;
                return await AbandonFailedSwitchAsync();
            }
            var vm = opened.ViewModel;

            // Resolve the canonical source path D for the title bar and backup service.
            var canonicalPath = App.Checkout.SourcePath;

            vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);

            // Delegate the rest of window wiring (backup session, DataContext,
            // checkout event (re-)subscription, autosave restart, etc.) to the
            // shared helper used by every startup path.
            await SetupMainWindowAsync(canonicalPath, vm);

            // The new database is now open and wired to the window (_isDatabaseOpen). The steps below
            // are refinements: a failure in one of them must not turn an open database into a failed
            // switch (which would send the user to the chooser, or make File → New skip its configuration
            // transfer). Log it — LogError also raises the standard error banner — and carry on.
            try
            {
                // Re-apply the semester selection after the DataContext swap.
                if (savedYearId != null && savedSemesterIds != null)
                    vm.SemesterContext.RestoreSelection(savedYearId, savedSemesterIds);

                // Restored unsaved changes are still only in the working copy — make them
                // durable right away (mirrors the startup path; failure surfaces through
                // the standard save-error banner).
                if (recoverUnsaved && App.Checkout.Mode == CheckoutMode.WriteAccess)
                    await App.Checkout.SaveAsync();
            }
            catch (Exception ex)
            {
                App.Logger.LogError(ex, "SwitchDatabase: a step after the database opened failed");
            }

            return SwitchResult.Opened;   // the new database is open and the window is wired to it
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Failed to switch database");
            await ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");

            // Past the point of no return the previous database is gone: clean up and go to the chooser.
            // Before it, the open database (if any) was never touched and stays — and so does its guard;
            // only the target's, taken at the top, is given back.
            if (phase == SwitchPhase.AfterTeardown)
                return await AbandonFailedSwitchAsync();

            ReleaseSwitchTargetGuard();
            return DatabaseSwitchRecovery.Decide(phase, _isDatabaseOpen);
        }
    }

    /// <summary>
    /// Gives back the single-instance guard that <see cref="SwitchCoreAsync"/> took for a switch target
    /// that is NOT going to be opened (the pre-check refused it, or something threw before the old
    /// database was touched), while keeping the guard of the database that is still open. With no
    /// database open nothing is kept, so the no-database state holds no guard. Never throws.
    /// </summary>
    private void ReleaseSwitchTargetGuard()
        => App.InstanceGuard.ReleaseAllExcept(_isDatabaseOpen ? App.Checkout.SourcePath : null);

    /// <summary>
    /// Cleans up after a switch that FAILED once the previous database had been closed (spec item 4d),
    /// returning the app to the same "no database open" state as startup — the state the database
    /// chooser then starts from. Never throws; each step is attempted even if an earlier one failed.
    ///
    /// <list type="number">
    /// <item><description><see cref="_isDatabaseOpen"/> is cleared (a failure part-way through wiring the
    /// window may have left it set).</description></item>
    /// <item><description><see cref="App.ReleaseServices"/> disposes a container left half-built by a failed
    /// <see cref="App.InitializeServices"/> (closing its connection to D') — a no-op when it is already
    /// gone — and leaves <c>App.Services</c> null.</description></item>
    /// <item><description><see cref="CheckoutService.ResetAfterFailedOpenAsync"/> gives back a lock the failed
    /// attempt may have taken and forgets the working-copy bookkeeping, so that choosing Exit in the
    /// chooser can never delete a kept working copy (the previous database's unsaved changes) through a
    /// stale <c>WorkingPath</c>. A working copy with a dirty marker is never deleted.</description></item>
    /// <item><description><see cref="DatabaseInstanceGuard.ReleaseAll"/> gives back the single-instance guard
    /// that was taken for the database whose open just failed (spec item 4b), so the no-database state holds
    /// no guard and the user can pick that same database again, or another process may open it. Done LAST,
    /// after the working-copy bookkeeping above, because the guard is what keeps a second TermPoint out of
    /// those files while they are being sorted out.</description></item>
    /// </list>
    /// Covering the window is left to the chooser step (<see cref="ShowSwitchFailedChooserAsync"/>), which
    /// runs immediately after and for the refusal path as well.
    /// </summary>
    /// <returns>Always <see cref="SwitchResult.NoDatabaseOpen"/>, so a failure site can <c>return await</c> it.</returns>
    private async Task<SwitchResult> AbandonFailedSwitchAsync()
    {
        _isDatabaseOpen = false;
        App.Logger.LogInfo("SwitchDatabase: failed after the previous database was closed — returning to the no-database state.");

        try { App.ReleaseServices(); }
        catch (Exception ex) { App.Logger.LogError(ex, "SwitchDatabase: could not dispose the services of the failed attempt"); }

        try { await App.Checkout.ResetAfterFailedOpenAsync(); }
        catch (Exception ex) { App.Logger.LogError(ex, "SwitchDatabase: could not reset the checkout state after the failed attempt"); }

        App.InstanceGuard.ReleaseAll();

        return DatabaseSwitchRecovery.Decide(SwitchPhase.AfterTeardown, databaseOpen: false);
    }

    /// <summary>
    /// The "no database open" recovery that follows a failed switch (spec item 4d): shows the startup
    /// database chooser in recovery mode and keeps showing it until a database opens or the user leaves
    /// (<see cref="DatabaseSwitchRecovery.RunChooserLoopAsync"/>). Each pick is opened through
    /// <see cref="SwitchCoreAsync"/> itself — the same pre-check, checkout and wiring as any switch, which
    /// tolerates a null <c>App.Services</c> — and a pick that fails comes back to the chooser.
    ///
    /// <para>If the chooser itself cannot be shown the app has no way forward, so — like a
    /// failure during startup — the error is reported and the app shuts down.</para>
    /// </summary>
    /// <param name="failedPath">The database whose open failed; named in the chooser's banner.</param>
    private async Task RecoverToDatabaseChooserAsync(string failedPath)
    {
        try
        {
            App.Logger.LogInfo("SwitchDatabase: no database is open — showing the database chooser.");
            var opened = await DatabaseSwitchRecovery.RunChooserLoopAsync(
                failedPath,
                ShowSwitchFailedChooserAsync,
                async chosen => await SwitchCoreAsync(chosen, isNewDatabase: false, createIfMissing: false)
                                == SwitchResult.Opened);
            App.Logger.LogInfo(opened
                ? "SwitchDatabase: a database was opened from the chooser."
                : "SwitchDatabase: the chooser ended without opening a database (exit, or the setup wizard took over).");
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SwitchDatabase: could not show the database chooser after a failed switch");
            WindowState = WindowState.Normal;
            await ShowStartupErrorAsync(ex);
            (Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
        }
    }

    /// <summary>
    /// Covers the window and shows the startup database chooser in recovery mode with
    /// <see cref="_noDatabaseReason"/> — <see cref="RecoveryReason.SwitchFailed"/>, or
    /// <see cref="RecoveryReason.AlreadyOpen"/> when the single-instance guard refused the database (spec
    /// item 4b). Chooser outcomes behave exactly as at startup, because
    /// this goes through the very same <see cref="ShowDatabaseChooserAsync"/>: a pick is returned, Create
    /// New hands over to the setup wizard (null), Exit shows the cancel message and shuts down (null). The
    /// chooser is shown NON-modally and never closed, as there (Avalonia 12 compositor deadlock).
    /// </summary>
    /// <param name="failedPath">The database that could not be opened; shown in the banner and kept in the recent list.</param>
    /// <returns>The database the user picked, or null when they exited or started the wizard.</returns>
    private Task<string?> ShowSwitchFailedChooserAsync(string failedPath)
    {
        // Nothing bound to the closed database may be visible or clickable behind the chooser or the
        // error dialogs that follow it. Idempotent, so the repeat visits of the loop cost nothing.
        CoverWindowForNoDatabase();
        return ShowDatabaseChooserAsync(ChooserMode.Recovery, _noDatabaseReason, failedPath);
    }



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
    /// <para><b>Sequence.</b></para>
    /// <list type="number">
    /// <item><description>Only the writer may restore; anyone else is told so and nothing happens.</description></item>
    /// <item><description>Autosave is stopped (an autosave tick must not collide with the restore's use of the
    /// save gate) and window closing is blocked (<see cref="_restoreInProgress"/>).</description></item>
    /// <item><description>Under a wait dialog that blocks edits: the user's changes are SAVED FIRST; if that save
    /// fails the restore is never started. Then <see cref="CheckoutService.RestoreFromBackupAsync"/> runs the
    /// shared safe restore (<see cref="DatabaseRestore"/>), which keeps the replaced database beside it.</description></item>
    /// <item><description>Every outcome that leaves the database unchanged is reported and the session simply
    /// continues (autosave resumes). <see cref="RestoreOutcome.Restored"/> and
    /// <see cref="RestoreOutcome.RenameUnconfirmed"/> are reported and then the window is closed: the normal
    /// close path (<see cref="OnClosing"/>) ends the stale session without saving, and after a confirmed restore
    /// restarts TermPoint.</description></item>
    /// </list>
    ///
    /// <para>Nothing is ever lost: the changes were saved before the restore began, so they are in the
    /// pre-restore copy; a failed save means the restore did not start.</para>
    /// </summary>
    /// <param name="backupPath">Full path of the backup .db file to restore (may be on a share).</param>
    public async Task RestoreFromBackupAsync(string backupPath)
    {
        // Only the writer may restore: it replaces the database for everyone, and only the lock holder has a
        // session that can be saved first and ended cleanly.
        if (App.Checkout.Mode != CheckoutMode.WriteAccess)
        {
            await ShowRestoreMessageAsync(new RestoreResult(RestoreOutcome.NotWriter));
            return;
        }

        if (_restoreInProgress)
        {
            App.Logger.LogInfo("MainWindow: RestoreFromBackupAsync ignored — a restore is already in progress.");
            return;
        }

        // Captured now, before anything is torn down: OnClosing needs it to discard this session's crash marker.
        var sourcePath = App.Checkout.SourcePath;

        _restoreInProgress = true;
        App.Checkout.StopAutoSave();

        // Null means "the save failed, so the restore was never started".
        RestoreResult? result;
        try
        {
            result = await RunWithWaitDialogAsync(
                RestorePrompt.WaitTitle,
                RestorePrompt.WaitMessage,
                () => SaveThenRestoreAsync(backupPath));
        }
        catch (Exception ex)
        {
            // SaveAsync and RestoreFromBackupAsync never throw, so this is a safety net only.
            App.Logger.LogError(ex, "MainWindow: unexpected exception while restoring from a backup");
            result = new RestoreResult(RestoreOutcome.Failed, Error: ex);
        }
        finally
        {
            _restoreInProgress = false;
        }

        // The save failed: the restore was not started. (SaveAsync has already shown its own save banner.)
        if (result is null)
        {
            await ShowMessageAsync(RestorePrompt.NotStartedTitle, RestorePrompt.SaveFailedMessage);
            ResumeAutoSave();
            return;
        }

        // Every other outcome that leaves D alone: say what happened; the session simply continues.
        if (result.TargetUnchanged)
        {
            await ShowRestoreMessageAsync(result);
            ResumeAutoSave();
            return;
        }

        // Restored or RenameUnconfirmed: the session is stale. Arrange the end of the session FIRST, so that
        // even if the window is closed by other means while the message is up (the OS shutting down, say) the
        // normal close path still ends it correctly instead of trying to save the stale working copy.
        _endSessionAfterRestore = true;
        _restoredSourcePath     = sourcePath;
        _relaunchAfterClose     = result.Outcome == RestoreOutcome.Restored;

        // Stop the periodic backup timer: a backup of the now-stale working copy must never appear as the
        // newest backup.
        try
        {
            if (App.Services?.GetService(typeof(BackupService)) is BackupService backupService)
                backupService.StopSession();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "MainWindow: could not stop the periodic backup after a restore");
        }

        try
        {
            await ShowRestoreMessageAsync(result);
        }
        finally
        {
            Close(); // OnClosing ends the stale session and, after a confirmed restore, restarts TermPoint.
        }
    }

    /// <summary>
    /// The work behind the wait dialog: saves the user's current changes, and only if that succeeded runs the
    /// restore. Neither call throws.
    /// </summary>
    /// <param name="backupPath">The backup to restore.</param>
    /// <returns>The restore's result, or null when the save did not succeed (so the restore was not started).</returns>
    private static async Task<RestoreResult?> SaveThenRestoreAsync(string backupPath)
    {
        // Autosave was stopped before this, but stopping the timer doesn't stop a tick that is already
        // saving. Wait for it (bounded), or our save would hit the in-flight gate and be skipped as
        // "already in progress" — reported to the user as a failed save. Same as ReleaseAsync's exit save.
        await App.Checkout.WaitForInFlightSaveAsync();

        var save = await App.Checkout.SaveAsync();
        if (save != SaveOutcome.Success)
        {
            App.Logger.LogInfo($"MainWindow: restore not started — the save before it ended with {save}.");
            return null;
        }

        return await App.Checkout.RestoreFromBackupAsync(backupPath);
    }

    /// <summary>
    /// Shows the message for a restore result (<see cref="RestorePrompt.Describe"/>) in the standard message dialog.
    /// </summary>
    /// <param name="result">The result to describe.</param>
    private Task ShowRestoreMessageAsync(RestoreResult result)
    {
        var (title, message) = RestorePrompt.Describe(result);
        return ShowMessageAsync(title, message);
    }

    /// <summary>
    /// Restarts autosave after a restore that left the database unchanged, if the user has it switched on and
    /// this session is still the writer. (The restore stopped it so no autosave tick could collide with it.)
    /// </summary>
    private static void ResumeAutoSave()
    {
        if (AppSettings.Current.AutoSaveEnabled && App.Checkout.Mode == CheckoutMode.WriteAccess)
            App.Checkout.StartAutoSave();
    }

    /// <summary>
    /// Ends the session of a database that a restore has just replaced. Called by <see cref="OnClosing"/> when
    /// <see cref="_endSessionAfterRestore"/> is set. Unlike the normal exit it does NOT save (the working copy is
    /// stale and a save would be refused): it releases the write lock, closes the secondary windows, disposes
    /// the services, and then ALWAYS deletes the working copy and its crash marker — the restored D supersedes
    /// them, and all the user's changes were saved before the restore started. Deleting the marker also keeps
    /// the next start from mistaking the leftovers for a crash.
    /// </summary>
    private async Task EndSessionAfterRestoreAsync()
    {
        try
        {
            App.Checkout.StopAutoSave();
            await App.Checkout.ReleaseAsync(saveFirst: false);

            CloseSecondaryWindows();

            // Closes the SQLite connection; only then can the working copy be deleted.
            (App.Services as IDisposable)?.Dispose();
        }
        finally
        {
            App.Checkout.CleanupWorkingCopy();
            if (!string.IsNullOrEmpty(_restoredSourcePath))
                App.Checkout.DiscardCrash(_restoredSourcePath);
        }
    }

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

    // ── CheckoutService helpers ──────────────────────────────────────────────

  

    /// <summary>
    /// Offers crash recovery for <paramref name="sourcePath"/> when unsaved changes from
    /// a session that did not end cleanly are found on disk.
    ///
    /// <para>Sequence: verify the recovered data is structurally sound (a damaged file is
    /// archived and the user informed), then ask Restore-or-Discard. Restore does not copy
    /// anything by itself — it makes the subsequent checkout run in recovery mode
    /// (<see cref="CheckoutService.CheckoutAsync"/> with <c>recoverWorkingCopy: true</c>),
    /// which resumes the crashed session only after proving D is unchanged since the crash.</para>
    ///
    /// <para><b>UX rule:</b> the user is never told about working copies or file paths —
    /// from their point of view TermPoint simply "kept their unsaved changes."</para>
    /// </summary>
    /// <param name="sourcePath">The database path with crash artifacts.</param>
    /// <returns>True when the checkout should run in recovery mode (user chose Restore).</returns>
    private async Task<bool> OfferCrashRecoveryAsync(string sourcePath)
    {
        var info = App.Checkout.InspectCrashRecovery(sourcePath);
        if (info is null) return false;

        if (!info.WorkingCopyIntact)
        {
            // The crash damaged the unsaved data beyond SQLite's own journal recovery.
            // Archive it (support-level salvage stays possible) and tell the user honestly.
            App.Checkout.ArchiveCorruptWorkingCopy(sourcePath);
            await ShowMessageAsync(
                LocalCopyCheckPrompt.UnsavedChangesLostTitle, LocalCopyCheckPrompt.UnsavedChangesLostMessage);
            return false;
        }

        // Neutral phrasing: these artifacts can come from a crash, a failed
        // save-on-exit, or a lock-loss demotion — "did not close properly" would
        // be wrong for the latter two.
        var restore = await ShowChoiceAsync("Restore Unsaved Changes?",
            "Unsaved changes from a previous session with this database were kept." +
            "\n\nWould you like to restore them?",
            "Restore", "Discard");

        if (!restore)
        {
            App.Checkout.DiscardCrash(sourcePath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles <see cref="CheckoutOutcome.RecoveryConflict"/>: the unsaved changes exist
    /// and are intact, but D was modified since the crash (or the marker is unverifiable),
    /// so restoring them automatically would overwrite someone else's work. Offers to
    /// export the unsaved version as a separate file, then discards the crash artifacts.
    /// A cancelled save-picker, a name that is already taken, an unreachable location or a
    /// failed copy re-offers the choice rather than silently discarding. The texts live in
    /// <see cref="RecoveryConflictPrompt"/>.
    /// </summary>
    /// <param name="sourcePath">The database path whose recovery hit a conflict.</param>
    private async Task HandleRecoveryConflictAsync(string sourcePath)
    {
        var info = App.Checkout.InspectCrashRecovery(sourcePath);

        while (info is not null)
        {
            var export = await ShowChoiceAsync(
                RecoveryConflictPrompt.ConflictTitle,
                RecoveryConflictPrompt.ConflictMessage,
                RecoveryConflictPrompt.SaveCopyButton, RecoveryConflictPrompt.DiscardButton);

            if (!export)
                break;

            // Only stop asking once the copy actually landed — a cancelled picker, a refused
            // name, an unreachable location or a failed copy must not silently destroy the
            // user's last copy of their edits.
            if (await ExportRecoveredCopyAsync(sourcePath))
                break;
        }

        App.Checkout.DiscardCrash(sourcePath);
    }

    /// <summary>
    /// Shows a save-file picker and saves the unsaved changes held in the crash-recovery file
    /// as a NEW database at the chosen location. The exported file is a complete, standalone
    /// database that can be opened via Files → Open Database.
    ///
    /// <para>The work is done by <see cref="CheckoutService.ExportRecoveredCopyAsync"/>: a
    /// local snapshot (so changes still in the recovery file's WAL are included), placed without
    /// overwrite on a deadline, off the UI thread. The picker's own overwrite prompt is OFF
    /// (<c>ShowOverwritePrompt = false</c>) because an existing file — the shared database
    /// itself included — is never replaced: the user is told to choose a new name and the caller
    /// offers the export again. An unreachable location gets the network message, also followed
    /// by another offer.</para>
    /// </summary>
    /// <param name="sourcePath">Original database path D (used for the suggested filename and to find its recovery file).</param>
    /// <returns>True when the copy was written; false on picker cancel, a refused name, an unreachable location or a failure.</returns>
    private async Task<bool> ExportRecoveredCopyAsync(string sourcePath)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title               = RecoveryConflictPrompt.SaveDialogTitle,
            SuggestedFileName   = RecoveryConflictPrompt.SuggestedFileName(sourcePath),
            DefaultExtension    = "db",
            ShowOverwritePrompt = false,
        });

        var destPath = file?.TryGetLocalPath();
        if (destPath is null) return false; // user cancelled

        var result = await App.Checkout.ExportRecoveredCopyAsync(sourcePath, destPath);

        switch (result.Outcome)
        {
            case NewDatabaseOutcome.Created:
                return true;

            case NewDatabaseOutcome.AlreadyExists:
                await ShowMessageAsync(
                    RecoveryConflictPrompt.AlreadyExistsTitle, RecoveryConflictPrompt.AlreadyExistsMessage);
                return false;

            case NewDatabaseOutcome.Unreachable:
                await ShowMessageAsync(DatabaseSwitchPrecheck.UnreachableTitle, NetworkFileOps.UnreachableMessage);
                return false;

            default:
                App.Logger.LogError(result.Error, "Failed to export recovered copy");
                await ShowMessageAsync(
                    RecoveryConflictPrompt.ExportFailedTitle, RecoveryConflictPrompt.ExportFailedMessage(result.Error));
                return false;
        }
    }

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
    /// Handles <see cref="CheckoutService.WriteLockLost"/> — fired when a mid-session
    /// verification discovered that the write lock is no longer ours (taken over,
    /// deleted, or its <c>SessionGuid</c> was modified).
    ///
    /// <para>Builds a cause-specific banner from <paramref name="reason"/> and
    /// <see cref="CheckoutService.LockLossNewHolder"/>, then demotes the session to
    /// read-only <b>in place</b> without swapping the DataContext. The banner is set
    /// <b>before</b> demotion so that even a failed demotion leaves the user informed.</para>
    ///
    /// <para>The user is directed to restart the app to re-bid for write access;
    /// we deliberately do not auto-reacquire, because doing so would silently trample
    /// whoever now holds the lock.</para>
    /// </summary>
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
            try
            {
                var banner = BuildLockLossBanner(reason);

                var vm       = DataContext as MainWindowViewModel;
                var dbContext = App.Services?.GetService<IDatabaseContext>() as DatabaseContext;

                // Set the banner BEFORE demotion so it is visible even if demotion throws.
                vm?.SetSaveError(banner, autoDismiss: false);

                async Task BeforeClose()
                {
                    await Task.CompletedTask;
                    dbContext?.CloseConnection();
                }

                async Task AfterOpen()
                {
                    await Task.CompletedTask;
                    if (dbContext is not null && !string.IsNullOrEmpty(App.Checkout.WorkingPath))
                    {
                        try
                        {
                            dbContext.ReinitializeConnection(App.Checkout.WorkingPath);
                        }
                        catch (System.Exception ex)
                        {
                            App.Logger.LogError(ex, "OnWriteLockLost: ReinitializeConnection failed — database is unavailable until reopened");
                            vm?.SetSaveError(
                                "The database connection could not be restored after losing write access. " +
                                "Please close and reopen the database file.",
                                autoDismiss: false);
                        }
                    }
                }

                var ok = await App.Checkout.DemoteToReadOnlyAsync(BeforeClose, AfterOpen);

                if (ok && vm is not null)
                {
                    try { vm.SectionListVm.ReloadFromDatabase(); }
                    catch (System.Exception ex)
                    {
                        App.Logger.LogError(ex, "OnWriteLockLost: post-demote reload failed");
                    }
                }
            }
            catch (System.Exception ex)
            {
                App.Logger.LogError(ex, "OnWriteLockLost: demotion failed");
            }
        });
        }
        catch (System.Exception ex)
        {
            App.Logger.LogError(ex, "OnWriteLockLost: dispatch failed");
        }
    }

    /// <summary>
    /// Builds a user-facing banner string explaining why write access was lost,
    /// using the <paramref name="reason"/> enum and the identity of the new holder
    /// (if any) from <see cref="CheckoutService.LockLossNewHolder"/>.
    /// </summary>
    private static string BuildLockLossBanner(WriteLockLostReason reason)
    {
        var sb = new System.Text.StringBuilder();

        if (reason == WriteLockLostReason.TakenOver)
        {
            var holder = App.Checkout.LockLossNewHolder;
            if (holder is not null && !string.IsNullOrWhiteSpace(holder.Username))
                sb.Append($"Write access was taken over by {holder.Username} on {holder.Machine}.");
            else
                sb.Append("Write access was taken over by another user.");
        }
        else
        {
            sb.Append(
                "The database lock file was removed by another program " +
                "(possibly antivirus or cloud sync software).");
        }

        // Built BEFORE demotion runs, so HasDirtyMarker still reflects the write
        // session: DemoteToReadOnlyAsync preserves D' + marker exactly when it is true.
        sb.Append(App.Checkout.HasDirtyMarker
            ? " Your unsaved changes have been kept — TermPoint will offer to restore " +
              "them the next time you open this database with write access."
            : " There were no unsaved changes at the time.");
        sb.Append(" You can regain write access by exiting and restarting TermPoint.");

        return sb.ToString();
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
    /// the declining button.
    /// </summary>
    /// <param name="owner">Window to parent the dialog to.</param>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Body text; wraps automatically.</param>
    /// <param name="affirmative">Label of the confirming button (returned as true).</param>
    /// <param name="negative">Label of the declining button (returned as false).</param>
    /// <returns>True when the affirmative button was clicked.</returns>
    internal static async Task<bool> ShowChoiceDialogAsync(
        Window owner, string title, string message, string affirmative, string negative)
    {
        var result = false;
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

        yes.Click += (_, _) => { result = true;  Dispatcher.UIThread.Post(() => dlg.Close()); };
        no.Click  += (_, _) => { result = false; Dispatcher.UIThread.Post(() => dlg.Close()); };
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
            toggleButton);
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

    private StackPanel CreateScheduleGridHeaderContext()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
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
}
