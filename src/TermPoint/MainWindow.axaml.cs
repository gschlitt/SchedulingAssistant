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

        e.Cancel = true; // Defer: run async save first.
        _shuttingDown = true;

        try
        {
            // Stop the periodic backup timer before disposing the container.
            if (App.Services?.GetService(typeof(BackupService)) is BackupService bs)
                bs.StopSession();

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
        catch (Exception ex)
        {
            // Log but still allow the window to close — don't leave the user
            // stuck with an uncloseable window.
            App.Logger.LogError(ex, "Error during shutdown — some changes may not have been saved");
        }

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

            // Wizard completed. The wizard created D, checked it out to D', and initialized
            // services against D'. SwitchDatabaseAsync will save D' → D (releasing the
            // checkout), then re-checkout D → new D' and reinitialize services. This is
            // a redundant cycle but correct for a one-time operation.
            await SwitchDatabaseAsync(settings.DatabasePath!);
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
                // Saved DB is missing or corrupt — fall through to the chooser in recovery mode.
                var reason = validation == DatabaseValidationResult.Corrupt
                    ? RecoveryReason.Corrupt
                    : RecoveryReason.NotFound;

                var result = await ShowDatabaseChooserAsync(ChooserMode.Recovery, reason, settings.DatabasePath);
                if (result is null) return; // user exited or wizard routed
                dbPath = result;
            }
        }
        else
        {
            // Even in chooser mode, validate the saved path so we can warn the user
            // if their most recent database has gone missing, is corrupt, or its
            // network location is unreachable.
            var validation = await DatabaseValidator.ValidateAsync(settings.DatabasePath);
            var mode = ChooserMode.Normal;
            RecoveryReason? reason = null;

            if (validation != DatabaseValidationResult.Ok
                && !string.IsNullOrEmpty(settings.DatabasePath))
            {
                mode = ChooserMode.Recovery;
                reason = validation switch
                {
                    DatabaseValidationResult.Corrupt     => RecoveryReason.Corrupt,
                    DatabaseValidationResult.Unreachable => RecoveryReason.Unreachable,
                    _                                    => RecoveryReason.NotFound
                };
            }

            var result = await ShowDatabaseChooserAsync(mode, reason, settings.DatabasePath);
            if (result is null) return; // user exited or wizard routed
            dbPath = result;
        }

        settings.AddRecentDatabase(dbPath);

        // ── License evaluation (must precede checkout so the license gate can
        //    block lock acquisition when access is read-only) ──────────────────
#if !BROWSER
        App.EvaluateLicense(dbPath);
#endif

        // ── Checkout flow ─────────────────────────────────────────────────────
        // Clean up any orphaned .tmp from a previous crashed save, then check for
        // an orphaned working copy (crash recovery), then acquire the write lock
        // and copy D → D' (or D → D'' for read-only mode).
        // The result determines which path is passed to InitializeServices.
        await App.Checkout.CleanupOrphanedTmpAsync(dbPath);
        App.Checkout.CleanupStaleCrashArtifacts(dbPath);

        // Crash recovery implies taking over a prior *write* session — wrong for an
        // observer, who never holds the lock. Skip it in reader mode.
        var recoverUnsaved = false;
        if (!AppSettings.Current.OpenInReaderMode && App.Checkout.DetectCrashRecovery(dbPath))
            recoverUnsaved = await OfferCrashRecoveryAsync(dbPath);

        var checkoutResult = await RunCheckoutAsync(dbPath, recoverUnsaved);
        if (checkoutResult is null)
        {
            // Network unreachable — dialog already shown. Close the app since this
            // is the initial startup path; there is no previous DB to fall back to.
            Close();
            return;
        }
        dbPath = checkoutResult;
        // ─────────────────────────────────────────────────────────────────────

        // Resolve the canonical source path D for the title bar and backup service.
        // RunCheckoutAsync returns D' (write mode) or D'' (read-only mode), neither of
        // which should be shown to the user. CheckoutService.SourcePath is always D.
        var canonicalPath = App.Checkout.SourcePath;

        // Initialize DI and DB, wire up the view model, then reveal the window.
        // DatabaseCorruptException is thrown when integrity_check fails; we handle
        // it by offering a restore dialog before the app opens normally.
        MainWindowViewModel vm;
        try
        {
            vm = App.InitializeServices(dbPath);
        }
        catch (DatabaseCorruptException corruptEx)
        {
            var restored = await ShowCorruptDatabaseDialogAsync(corruptEx.DatabasePath);
            if (!restored)
            {
                await ShowCancelMessageAsync();
                (Avalonia.Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.Shutdown();
                return;
            }
            // The restore copied a backup over dbPath — re-initialize with the restored file.
            vm = App.InitializeServices(dbPath);
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
            return;
        }

        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);
        await SetupMainWindowAsync(canonicalPath, vm);

        // Restored unsaved changes are still only in the working copy — make them
        // durable right away rather than leaving them one crash away from a second
        // recovery. Failure is non-fatal: the standard save-error banner (wired by
        // SetupMainWindowAsync) surfaces it and autosave/manual save will retry.
        if (recoverUnsaved && App.Checkout.Mode == CheckoutMode.WriteAccess)
            await App.Checkout.SaveAsync();
    }

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
    /// <returns>
    /// <list type="bullet">
    /// <item><description><b>Write access:</b> D' (the local working copy).</description></item>
    /// <item><description><b>Read-only:</b> D'' (the local read-only snapshot).</description></item>
    /// <item><description><b>Stale lock — user declined takeover, force-checkout lost the race, or checkout failed:</b>
    /// D'' via <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/>, or D as a last resort if that also fails.</description></item>
    /// </list>
    /// </returns>
    private async Task<string?> RunCheckoutAsync(string sourcePath, bool recoverUnsaved = false)
    {
        var outcome = await App.Checkout.CheckoutAsync(sourcePath, recoverUnsaved);

        switch (outcome)
        {
            case CheckoutOutcome.WriteAccess:
                return App.Checkout.WorkingPath;

            case CheckoutOutcome.ReadOnly:
                if (recoverUnsaved)
                    await ShowRecoveryDeferredMessageAsync();
                return App.Checkout.WorkingPath;

            case CheckoutOutcome.RecoveryConflict:
                // The unsaved changes can't be applied automatically (D changed since
                // the crash, or the marker is unverifiable). Offer an exported copy,
                // then discard the artifacts and re-run a normal checkout.
                await HandleRecoveryConflictAsync(sourcePath);
                return await RunCheckoutAsync(sourcePath, recoverUnsaved: false);

            case CheckoutOutcome.StaleHolder:
                {
                    var holder = App.Checkout.CurrentHolder;
                    var age = holder is not null
                        ? (int)(DateTime.UtcNow - holder.Heartbeat).TotalMinutes
                        : 0;
                    var who = holder is not null
                        ? $"{holder.Username} on {holder.Machine}"
                        : "an unknown user";

                    var take = await ShowYesNoAsync(
                        "Lock Is Inactive",
                        $"{who}'s lock is {age} minute(s) old and appears to have been abandoned. " +
                        "Take over write access?");

                    if (take)
                    {
                        var forceOutcome = await App.Checkout.ForceCheckoutAsync(recoverUnsaved);
                        if (forceOutcome == CheckoutOutcome.WriteAccess)
                            return App.Checkout.WorkingPath;
                        if (forceOutcome == CheckoutOutcome.RecoveryConflict)
                        {
                            await HandleRecoveryConflictAsync(sourcePath);
                            return await RunCheckoutAsync(sourcePath, recoverUnsaved: false);
                        }
                        if (forceOutcome == CheckoutOutcome.NetworkUnreachable)
                        {
                            await ShowMessageAsync("Network Unreachable",
                                "Cannot reach the database — check your network connection and try again.");
                            return null;
                        }
                        // Lost the race to another instance — fall through to read-only setup.
                    }

                    // User declined, or force-checkout lost the race.
                    // Set up D'' so we never hold D open directly. Crash artifacts (if
                    // any) stay on disk so recovery can be offered at the next launch.
                    if (recoverUnsaved)
                        await ShowRecoveryDeferredMessageAsync();
                    return await App.Checkout.SetupReadOnlySnapshotAsync() ?? sourcePath;
                }

            case CheckoutOutcome.NetworkUnreachable:
                await ShowMessageAsync("Network Unreachable",
                    "Cannot reach the database — check your network connection and try again.");
                return null;

            default: // Failed
                await ShowMessageAsync("Checkout Failed",
                    "Could not open the database for editing. " +
                    "The application will open in read-only mode.");
                // Best-effort: try to set up D'' even after failure so D stays handle-free.
                // Crash artifacts (if any) stay on disk for the next launch.
                return await App.Checkout.SetupReadOnlySnapshotAsync() ?? sourcePath;
        }
    }

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
        if (LoadingCurtain is not null)
        {
            LoadingCurtain.IsVisible = true;
            LoadingCurtain.IsHitTestVisible = true;
            LoadingCurtain.Opacity = 1;
        }
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
    /// If the database file does not yet exist (File → New), it is created with the
    /// schema before checkout so that <see cref="CheckoutService.CheckoutAsync"/> takes
    /// the normal D → D' path and all writes go to the local working copy.
    /// </summary>
    public async Task SwitchDatabaseAsync(string newDatabasePath)
    {
        App.Logger.LogBreadcrumb("SwitchDatabase", new() { ["path"] = newDatabasePath });
        try
        {
            // Update settings and record in recent list
            var settings = AppSettings.Current;
            settings.DatabasePath = newDatabasePath;
            settings.Save();
            settings.AddRecentDatabase(newDatabasePath);

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
            if (App.Services.GetService(typeof(SemesterContext)) is SemesterContext priorCtx
                && priorCtx.SelectedAcademicYear is not null)
            {
                savedYearId      = priorCtx.SelectedAcademicYear.Id;
                savedSemesterIds = priorCtx.SelectedSemesters.Select(s => s.Semester.Id).ToHashSet();
            }

            // Stop the periodic backup timer BEFORE disposing the DI container.
            if (App.Services?.GetService(typeof(BackupService)) is BackupService oldBackup)
                oldBackup.StopSession();

            // Close the old DatabaseContext (and all other DI singletons) NOW, before
            // CheckoutAsync copies D to D'.
            (App.Services as IDisposable)?.Dispose();

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

            // If D does not exist yet (File → New, or a new wizard DB), create it
            // with the schema so CheckoutAsync takes the normal D → D' path and
            // all subsequent writes go to the local working copy. Deadline-bounded
            // tri-state probe: only create when the location answered "missing" —
            // an unreachable share must NOT trigger creation attempts; checkout
            // below surfaces the unreachable dialog instead.
            var probe = await NetworkFileOps.ProbeFileAsync(newDatabasePath);
            if (probe == FileProbeResult.Missing)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newDatabasePath)!);
                using (new Data.DatabaseContext(newDatabasePath)) { }
            }

            // Re-evaluate license for the new share directory before checkout.
#if !BROWSER
            App.EvaluateLicense(newDatabasePath);
#endif

            // Run crash recovery + checkout for the new path.
            await App.Checkout.CleanupOrphanedTmpAsync(newDatabasePath);
            App.Checkout.CleanupStaleCrashArtifacts(newDatabasePath);
            var recoverUnsaved = false;
            if (App.Checkout.DetectCrashRecovery(newDatabasePath))
                recoverUnsaved = await OfferCrashRecoveryAsync(newDatabasePath);

            var checkoutPath = await RunCheckoutAsync(newDatabasePath, recoverUnsaved);
            if (checkoutPath is null)
                return; // Network unreachable — dialog already shown.
            newDatabasePath = checkoutPath;

            // Reinitialize DI and set new data context.
            var vm = App.InitializeServices(newDatabasePath, savedYearId, savedSemesterIds);

            // Resolve the canonical source path D for the title bar and backup service.
            var canonicalPath = App.Checkout.SourcePath;

            vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);

            // Delegate the rest of window wiring (backup session, DataContext,
            // checkout event (re-)subscription, autosave restart, etc.) to the
            // shared helper used by every startup path.
            await SetupMainWindowAsync(canonicalPath, vm);

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
            App.Logger.LogError(ex, "Failed to switch database");
            await ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");
        }
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

    /// <summary>
    /// Shown when the startup integrity check fails. Lists backups from the configured
    /// backup folder and lets the user pick one to restore before the app opens.
    /// Returns true when a backup was successfully restored over <paramref name="dbPath"/>.
    /// Returns false when the user dismisses without restoring (app will shut down).
    /// </summary>
    private async Task<bool> ShowCorruptDatabaseDialogAsync(string dbPath)
    {
        // Enumerate backups for this database from the configured folder.
        // Deadline-bounded probe — the backup folder may be on a network share.
        var folder = AppSettings.Current.BackupFolderPath;
        var dbName = Path.GetFileNameWithoutExtension(dbPath);
        var backups = new List<string>();

        if (!string.IsNullOrWhiteSpace(folder))
        {
            var (dirOk, dirExists) = await NetworkFileOps.DirectoryExistsAsync(folder);
            if (dirOk && dirExists)
            {
                var (filesOk, files) = await NetworkFileOps.RunAsync(
                    () => Directory.GetFiles(folder, $"{dbName}_*.db"), "CorruptDialog.GetFiles");
                if (filesOk && files is not null)
                    backups = files.OrderByDescending(f => f).ToList();
            }
        }

        var dlg = new Window
        {
            Title  = "Database Integrity Check Failed",
            Width  = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true // see ShowFatalAsync — modal dialogs must never be occluded
        };

        string? chosen = null;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = "The database failed its integrity check and may be corrupt.",
            FontSize = 13, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Database: {dbPath}",
            FontSize = 11, Foreground = Brushes.DarkRed,
            TextWrapping = TextWrapping.Wrap
        });

        if (backups.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No backups were found. You can exit and manually replace the database file with a backup, or continue at your own risk.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Select a backup to restore, or exit and handle it manually:",
                TextWrapping = TextWrapping.Wrap, FontSize = 12
            });

            foreach (var backupPath in backups)
            {
                var label = Path.GetFileNameWithoutExtension(backupPath);
                var btn   = new Button
                {
                    Content = $"Restore: {label}",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4)
                };
                btn.Click += (_, _) => { chosen = backupPath; Dispatcher.UIThread.Post(() => dlg.Close()); };
                panel.Children.Add(btn);
            }
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var continueBtn = new Button { Content = "Continue anyway (risky)", FontSize = 11, Padding = new Thickness(8, 4) };
        var exitBtn     = new Button { Content = "Exit", FontSize = 11, Padding = new Thickness(8, 4) };
        continueBtn.Click += (_, _) => { chosen = "continue"; Dispatcher.UIThread.Post(() => dlg.Close()); };
        exitBtn.Click     += (_, _) => { chosen = null; Dispatcher.UIThread.Post(() => dlg.Close()); };
        buttonRow.Children.Add(continueBtn);
        buttonRow.Children.Add(exitBtn);
        panel.Children.Add(buttonRow);

        dlg.Content = panel;
        await dlg.ShowDialog(this);

        if (chosen is null) return false;       // Exit
        if (chosen == "continue") return true;  // Proceed with corrupt DB

        // User chose a backup — copy it over the main database file.
        try
        {
            File.Copy(chosen, dbPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Restore Failed", $"Could not restore backup:\n\n{ex.Message}");
            return false;
        }
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

    private async Task ShowMessageAsync(string title, string message)
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

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center };
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
        await msg.ShowDialog(this);
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
            await ShowMessageAsync("Unsaved Changes Lost",
                "Unsaved changes from a previous session with this database were " +
                "found, but they were damaged and could not be restored.");
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
    /// A cancelled save-picker re-offers the choice rather than silently discarding.
    /// </summary>
    /// <param name="sourcePath">The database path whose recovery hit a conflict.</param>
    private async Task HandleRecoveryConflictAsync(string sourcePath)
    {
        var info = App.Checkout.InspectCrashRecovery(sourcePath);

        while (info is not null)
        {
            var export = await ShowChoiceAsync("Can't Restore Unsaved Changes",
                "The database has been changed since your unsaved changes were made, " +
                "so they can't be restored automatically.\n\n" +
                "Would you like to save your version as a separate file instead? " +
                "You can open it later from Files → Open Database.",
                "Save a Copy…", "Discard");

            if (!export)
                break;

            // Only stop asking once the copy actually landed — a cancelled picker or a
            // failed copy must not silently destroy the user's last copy of their edits.
            if (await ExportRecoveredCopyAsync(sourcePath, info.WorkingPath))
                break;
        }

        App.Checkout.DiscardCrash(sourcePath);
    }

    /// <summary>
    /// Shows a save-file picker and copies the recovered working copy to the chosen
    /// location. The exported file is a complete, standalone database that can be opened
    /// via Files → Open Database.
    /// </summary>
    /// <param name="sourcePath">Original database path D (used for the suggested filename).</param>
    /// <param name="workingPath">The intact working copy holding the unsaved changes.</param>
    /// <returns>True when the copy was written; false on picker cancel or copy failure.</returns>
    private async Task<bool> ExportRecoveredCopyAsync(string sourcePath, string workingPath)
    {
        var suggested = Path.GetFileNameWithoutExtension(sourcePath) + " (recovered).db";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save Recovered Copy",
            SuggestedFileName = suggested,
            DefaultExtension  = "db",
        });

        var destPath = file?.TryGetLocalPath();
        if (destPath is null) return false; // user cancelled

        try
        {
            File.Copy(workingPath, destPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Failed to export recovered copy");
            await ShowMessageAsync("Export Failed",
                $"Could not save the copy: {ex.Message}");
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
    private async Task<bool> ShowChoiceAsync(string title, string message, string affirmative, string negative)
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
        await dlg.ShowDialog(this);
        return result;
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
