using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Controls;
using SchedulingAssistant.Data;
using SchedulingAssistant.Exceptions;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Views;
using SchedulingAssistant.Views.GridView;
using SchedulingAssistant.Views.Wizard;
using System.ComponentModel;
using System.Reflection;

namespace SchedulingAssistant;

public partial class MainWindow : Window
{
    private DetachedPanelWindow? _sectionViewWindow;
    private DetachedPanelWindow? _workloadWindow;
    private DetachedPanelWindow? _scheduleGridWindow;
    private MainWindowViewModel? _previousVm;

    public ScheduleGridView? ScheduleGridViewInstance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        // Subscribe to overflow-set changes on the top menu panel so we can mirror them
        // into MoreMenuViewModel. DataContext isn't set yet; the handler guards for null.
        if (this.FindControl<ResponsiveMenuPanel>("TopMenuPanel") is { } topPanel)
            topPanel.HiddenOverflowItemsChanged += OnTopMenuOverflowChanged;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"TermPoint v{version?.Major}.{version?.Minor}.{version?.Build}";

        // Escape-to-close-flyout is handled declaratively by DismissBehaviors.EscapeCommand
        // on the Window element in AXAML.
        // Ctrl+S is handled here to save editors in management views (works globally, not dependent on focus).
        KeyDown += (_, e) =>
        {
            // Handle Ctrl+S (Windows/Linux) or Cmd+S (macOS) to save
            if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    // Always-visible panels: Section View and Meeting View
                    if (TrySaveEditor(vm.SectionListVm?.EditVm?.SaveCommand, ref e)) return;
                    if (TrySaveEditor(vm.MeetingListVm?.EditVm?.SaveCommand, ref e)) return;

                    // Flyout panels: check the currently open flyout ViewModel
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
                        // SelectedCategory can be SchedulingEnvironmentListViewModel, RoomListViewModel, or CampusListViewModel
                        if (schedEnvVm.SelectedCategory is SchedulingEnvironmentListViewModel listVm)
                            if (TrySaveEditor(listVm.EditVm?.SaveCommand, ref e)) return;
                        if (schedEnvVm.SelectedCategory is RoomListViewModel roomVm)
                            if (TrySaveEditor(roomVm.EditVm?.SaveCommand, ref e)) return;
                        if (schedEnvVm.SelectedCategory is CampusListViewModel campusVm)
                            if (TrySaveEditor(campusVm.EditVm?.SaveCommand, ref e)) return;
                    }
                }
            }

#if DEBUG
            // DEV-ONLY hotkeys for testing error banners. Remove before shipping.
            // Ctrl+Shift+E → simulate schedule grid error
            // Ctrl+Shift+W → simulate section list error
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

        // Stop the periodic backup timer before disposing the container.
        if (App.Services?.GetService(typeof(BackupService)) is BackupService bs)
            bs.StopSession();

        // Stop autosave and save D' → D (releases lock on success).
        App.Checkout.StopAutoSave();
        await App.Checkout.ReleaseAsync(saveFirst: App.Checkout.Mode == CheckoutMode.WriteAccess);

        // Dispose the DI container, which closes the SQLite connection cleanly.
        (App.Services as IDisposable)?.Dispose();

        // Now that the connection is closed, delete D' from the working directory.
        App.Checkout.CleanupWorkingCopy();

        Close(); // Re-trigger OnClosing; _shuttingDown is now true so it falls through.
    }   
    
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            // var splash = new SplashScreen();
            // splash.Show();
            // await Task.Delay(2000);
            // splash.Close();

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
            var wizard = new StartupWizardWindow();
            WindowState = WindowState.Minimized;
            wizard.Show(this);

            var tcs = new TaskCompletionSource();
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

            // Wizard completed. App.Services was initialized against D (the real DB file)
            // by the wizard. Now run the full checkout flow: acquire the write lock, copy D
            // to D', and reinitialize services against D'. SwitchDatabaseAsync handles the
            // no-op release (nothing held yet) + checkout + re-InitializeServices + SetupMainWindowAsync.
            await SwitchDatabaseAsync(settings.DatabasePath!);
            return;
        }

        // ── Returning user ────────────────────────────────────────────────────
        await RunReturningUserStartupAsync(settings);
    }

    /// <summary>
    /// Normal startup path for a returning user who already has a database configured.
    /// Attempt to re-open the most recently accessed database.
    /// Validates the saved database path, shows the recovery window if needed, then
    /// calls App.InitializeServices and shows the main window.
    /// </summary>
    private async Task RunReturningUserStartupAsync(AppSettings settings)
    {
        string dbPath;
        var validation = await DatabaseValidator.ValidateAsync(settings.DatabasePath);
        if (validation == DatabaseValidationResult.Ok)
        {
            // Happy path — file exists and passes integrity check.
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
            // File is missing or corrupt — show the recovery window.
            var reason = validation == DatabaseValidationResult.Corrupt
                ? RecoveryReason.Corrupt
                : RecoveryReason.NotFound;

            var recovery = new DatabaseRecoveryWindow(reason, settings.DatabasePath);
            await recovery.ShowDialog(this);

            switch (recovery.Vm.Outcome)
            {
                case RecoveryOutcome.Resolved:
                    dbPath = recovery.Vm.ResolvedPath!;
                    settings.DatabasePath = dbPath;
                    settings.Save();
                    break;

                case RecoveryOutcome.StartWizard:
                    // User wants a fresh start — clear the saved path and re-run
                    // startup so the wizard is shown in the normal first-run path.
                    settings.IsInitialSetupComplete = false;
                    settings.DatabasePath           = null;
                    settings.Save();
                    await RunStartupAsync();
                    return;

                default: // RecoveryOutcome.None — user exited
                    await ShowCancelMessageAsync();
                    (Avalonia.Application.Current?.ApplicationLifetime as
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                        ?.Shutdown();
                    return;
            }
        }

        // Record this database in recent list
        settings.AddRecentDatabase(dbPath);
        
        // ── Checkout flow ─────────────────────────────────────────────────────
        // Clean up any orphaned .tmp from a previous crashed save, then check for
        // an orphaned working copy (crash recovery), then acquire the write lock
        // and copy D → D' (or D → D'' for read-only mode).
        // The result determines which path is passed to InitializeServices.
        await App.Checkout.CleanupOrphanedTmpAsync(dbPath);
        App.Checkout.CleanupStaleCrashArtifacts(dbPath);

        if (App.Checkout.DetectCrashRecovery(dbPath))
            await HandleCrashRecoveryAsync(dbPath);

        var checkoutResult = await RunCheckoutAsync(dbPath);
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

        vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);
        await SetupMainWindowAsync(canonicalPath, vm);
    }

    /// <summary>
    /// Runs the full checkout sequence for <paramref name="sourcePath"/> and returns
    /// the path that should be passed to <c>App.InitializeServices</c>.
    /// Handles stale-lock prompting inline.
    /// </summary>
    /// <param name="sourcePath">The database path D chosen by the user.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description><b>Write access:</b> D' (the local working copy).</description></item>
    /// <item><description><b>Read-only:</b> D'' (the local read-only snapshot).</description></item>
    /// <item><description><b>Stale lock — user declined takeover, force-checkout lost the race, or checkout failed:</b>
    /// D'' via <see cref="CheckoutService.SetupReadOnlySnapshotAsync"/>, or D as a last resort if that also fails.</description></item>
    /// </list>
    /// </returns>
    private async Task<string?> RunCheckoutAsync(string sourcePath)
    {
        var outcome = await App.Checkout.CheckoutAsync(sourcePath);

        switch (outcome)
        {
            case CheckoutOutcome.WriteAccess:
                return App.Checkout.WorkingPath;

            case CheckoutOutcome.ReadOnly:                
                return App.Checkout.WorkingPath;

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
                        var forceOutcome = await App.Checkout.ForceCheckoutAsync();
                        if (forceOutcome == CheckoutOutcome.WriteAccess)
                            return App.Checkout.WorkingPath;
                        if (forceOutcome == CheckoutOutcome.NetworkUnreachable)
                        {
                            await ShowMessageAsync("Network Unreachable",
                                "Cannot reach the database — check your network connection and try again.");
                            return null;
                        }
                        // Lost the race to another instance — fall through to read-only setup.
                    }

                    // User declined, or force-checkout lost the race.
                    // Set up D'' so we never hold D open directly.
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
                return await App.Checkout.SetupReadOnlySnapshotAsync() ?? sourcePath;
        }
    }

    /// <summary>
    /// Final step of every startup path: wires the main window VM and makes the window visible.
    /// Always receives a fully-initialized <paramref name="vm"/> from the caller — the wizard,
    /// returning-user, and database-switch paths all call <see cref="SwitchDatabaseAsync"/> or
    /// <see cref="App.InitializeServices"/> first and pass the result directly.
    /// </summary>
    private async Task SetupMainWindowAsync(string dbPath, MainWindowViewModel vm)
    {
        // Start the automated backup session if this instance acquired the write lock.
        // Also wire the backup service into CheckoutService so pre-save snapshots use
        // the same service and naming convention as regular backups.
        // Fire-and-forget: the first backup runs asynchronously in the background.
        if (App.Services.GetService(typeof(BackupService)) is BackupService backup
            && App.Services.GetService(typeof(WriteLockService)) is WriteLockService wl
            && wl.IsWriter)
        {
            App.Checkout.SetBackupService(backup);
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
        App.Checkout.SaveCompleted   += OnCheckoutSaveCompleted;
        App.Checkout.SaveFailed      += OnCheckoutSaveFailed;
        App.Checkout.WriteLockLost += OnWriteLockLost;
        App.Checkout.BecameDirty     += OnCheckoutBecameDirty;

        // Start autosave if it was enabled in settings.
        if (AppSettings.Current.AutoSaveEnabled)
            App.Checkout.StartAutoSave();

        // Set the main window reference and load recent databases
        vm.MainWindowReference = this;
        vm.LoadRecentDatabases();

        // Enqueue any feature announcements the user hasn't seen yet.
        if (App.Services.GetService(typeof(AppNotificationService)) is AppNotificationService notifier)
            notifier.EnqueueUnseenAnnouncements();

        // Hide the loading curtain after the ScheduleGrid completes its first layout pass.
        // SizeChanged fires after Avalonia measures and arranges the control; by then
        // ScheduleGridView's own SizeChanged handler has already called Render(), so the
        // canvas is fully populated before the curtain lifts.
        EventHandler<SizeChangedEventArgs>? curtainHandler = null;
        curtainHandler = (_, _) =>
        {
            ScheduleGridViewControl.SizeChanged -= curtainHandler;
        //    LoadingCurtain.IsVisible = false;
        };
        ScheduleGridViewControl.SizeChanged += curtainHandler;

        IsVisible = true;
        Activate();

        await Task.CompletedTask;
    }

   
    
    /// <summary>
    /// Switches to a different database without restarting the app. Handles the full
    /// release → checkout → re-initialize cycle. Called from the Files menu, from the
    /// startup wizard completion path, and from <c>AcquireWriteLock</c> in the ViewModel
    /// when upgrading from read-only to write mode.
    /// The database file will be created if it doesn't exist.
    /// </summary>
    public async Task SwitchDatabaseAsync(string newDatabasePath)
    {
        try
        {
            // Update settings and record in recent list
            var settings = AppSettings.Current;
            settings.DatabasePath = newDatabasePath;
            settings.Save();
            settings.AddRecentDatabase(newDatabasePath);

            // Release the current checkout (saves if dirty) before opening the new DB.
            App.Checkout.StopAutoSave();
            await App.Checkout.ReleaseAsync(saveFirst: App.Checkout.Mode == CheckoutMode.WriteAccess);

            // Close the old DatabaseContext (and all other DI singletons) NOW, before
            // CheckoutAsync copies D to D'.  InitializeServices also calls Dispose on
            // App.Services, but that runs after the copy — too late on Windows, where
            // SqliteConnection.Dispose() uses sqlite3_close_v2() which can defer the OS
            // file-handle release.  File.Move(D.tmp → D) in SaveAsync would then fail
            // with "Access to the path is denied" because D still has an open handle
            // without FILE_SHARE_DELETE.  Disposing here guarantees D is fully closed
            // before RunCheckoutAsync establishes D as the save target.
            (App.Services as IDisposable)?.Dispose();

            // Now that the connection is closed, delete the old D' from the working directory.
            App.Checkout.CleanupWorkingCopy();

            // Run crash recovery + checkout for the new path.
            await App.Checkout.CleanupOrphanedTmpAsync(newDatabasePath);
            App.Checkout.CleanupStaleCrashArtifacts(newDatabasePath);
            if (App.Checkout.DetectCrashRecovery(newDatabasePath))
                await HandleCrashRecoveryAsync(newDatabasePath);

            var checkoutPath = await RunCheckoutAsync(newDatabasePath);
            if (checkoutPath is null) return; // Network unreachable — dialog already shown.
            newDatabasePath = checkoutPath;

            // Reinitialize DI and set new data context.
            // DatabaseContext will create the file if it doesn't exist.
            var vm = App.InitializeServices(newDatabasePath);

            // Resolve the canonical source path D for the title bar and backup service.
            // newDatabasePath is D' or D'' (the working copy), never the user-facing path.
            // CheckoutService.SourcePath is always D regardless of mode.
            var canonicalPath = App.Checkout.SourcePath;

            vm.SetDatabaseName(Path.GetFileNameWithoutExtension(canonicalPath), canonicalPath);

            // Delegate the rest of window wiring (backup session, DataContext,
            // checkout event (re-)subscription, autosave restart, etc.) to the
            // shared helper used by every startup path.
            await SetupMainWindowAsync(canonicalPath, vm);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Failed to switch database");
            await ShowMessageAsync("Error", $"Failed to switch to database: {ex.Message}");
        }
    }



    private async Task<string?> ShowLocationDialogAsync(DatabaseLocationMode mode)
    {
        var dialog = new DatabaseLocationDialog(mode);
        await dialog.ShowDialog(this);
        return dialog.ChosenPath;
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
            ShowInTaskbar = true
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

        ok.Click += (_, _) => msg.Close();
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
        var folder = AppSettings.Current.BackupFolderPath;
        var dbName = Path.GetFileNameWithoutExtension(dbPath);
        var backups = new List<string>();

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            backups = Directory.GetFiles(folder, $"{dbName}_*.db")
                               .OrderByDescending(f => f)
                               .ToList();
        }

        var dlg = new Window
        {
            Title  = "Database Integrity Check Failed",
            Width  = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
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
                btn.Click += (_, _) => { chosen = backupPath; dlg.Close(); };
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
        continueBtn.Click += (_, _) => { chosen = "continue"; dlg.Close(); };
        exitBtn.Click     += (_, _) => { chosen = null; dlg.Close(); };
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
            ShowInTaskbar = true
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

        ok.Click += (_, _) => msg.Close();
        await msg.ShowDialog(this);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var msg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
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

        ok.Click += (_, _) => msg.Close();
        await msg.ShowDialog(this);
    }

    // ── CheckoutService helpers ──────────────────────────────────────────────

  

    /// <summary>
    /// Handles crash recovery for <paramref name="sourcePath"/>: notifies the user
    /// that unsaved changes were lost, then discards the orphaned working copy.
    /// The working copy is not written back to D — a crash may have left D' corrupt,
    /// and D is the last fully committed state.
    /// </summary>
    /// <param name="sourcePath">The database path with an orphaned working copy.</param>
    private async Task HandleCrashRecoveryAsync(string sourcePath)
    {
        await ShowMessageAsync(
            "Unsaved Changes Lost",
            "The application did not close cleanly. Any unsaved changes from the " +
            "previous session have been discarded.");

        App.Checkout.DiscardCrash(sourcePath);
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

    private void OnCheckoutSaveFailed(string message)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SetSaveError(message);
    }

    /// <summary>
    /// Handles <see cref="CheckoutService.WriteLockLost"/> — fired when
    /// <see cref="CheckoutService.SaveAsync"/> or the wake-check discovered that
    /// the write lock is no longer ours (deleted, stolen, or its <c>SessionGuid</c>
    /// was modified).
    ///
    /// <para>Demotes the session to read-only <b>in place</b>, without swapping the
    /// DataContext. The existing <see cref="MainWindowViewModel"/> is kept so the
    /// banner set by <see cref="MainWindowViewModel.SetSaveError"/> remains visible.
    /// The <c>DatabaseContext</c> is closed, D'' is created from D, and the connection
    /// is reopened against D''. A reload of <see cref="SectionStore"/> then refreshes
    /// every panel from D'' (our unsaved in-memory edits in D' are gone by design).</para>
    ///
    /// <para>The user is directed to restart the app to re-bid for write access;
    /// we deliberately do not auto-reacquire, because doing so would silently trample
    /// whoever now holds the lock.</para>
    /// </summary>
    private async void OnWriteLockLost()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                const string banner =
                    "You have lost write access. You can attempt to regain it " +
                    "by exiting and restarting TermPoint.";

                var vm       = DataContext as MainWindowViewModel;
                var dbContext = App.Services?.GetService<IDatabaseContext>() as DatabaseContext;

                async Task BeforeClose()
                {
                    await Task.CompletedTask;
                    dbContext?.CloseConnection();
                }

                async Task AfterOpen()
                {
                    await Task.CompletedTask;
                    if (dbContext is not null && !string.IsNullOrEmpty(App.Checkout.WorkingPath))
                        dbContext.ReinitializeConnection(App.Checkout.WorkingPath);
                }

                var ok = await App.Checkout.DemoteToReadOnlyAsync(BeforeClose, AfterOpen);

                vm?.SetSaveError(banner);

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

    private async Task<bool> ShowYesNoAsync(string title, string message)
    {
        var result = false;
        var dlg = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true
        };

        var yes   = new Button { Content = "Yes", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 4) };
        var no    = new Button { Content = "No",  HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 4) };
        var btns  = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        btns.Children.Add(no);
        btns.Children.Add(yes);

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        panel.Children.Add(btns);
        dlg.Content = panel;

        yes.Click += (_, _) => { result = true;  dlg.Close(); };
        no.Click  += (_, _) => { result = false; dlg.Close(); };
        await dlg.ShowDialog(this);
        return result;
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    // ── Data-context wiring ──────────────────────────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            // Unsubscribe from the old VM to prevent memory leaks and stale event callbacks
            // when the DataContext is replaced (e.g. on database switch).
            if (_previousVm is not null)
            {
                _previousVm.PropertyChanged -= OnMainWindowVmPropertyChanged;
                _previousVm.WorkloadPanelVm.ItemClicked -= OnWorkloadItemClicked;
            }

            // Wire the grid's double-click-to-edit callback at the view level.
            // This keeps SectionListViewModel and ScheduleGridViewModel decoupled —
            // neither holds a reference to the other.
            vm.ScheduleGridVm.EditRequested        = vm.SectionListVm.EditSectionById;
            vm.ScheduleGridVm.MeetingEditRequested  = vm.MeetingListVm.EditMeetingById;

            vm.PropertyChanged += OnMainWindowVmPropertyChanged;
            vm.WorkloadPanelVm.ItemClicked += OnWorkloadItemClicked;

            _previousVm = vm;

            ScheduleGridViewInstance = this.FindControl<ScheduleGridView>("ScheduleGridViewControl");

#if DEBUG
            // Show debug menu in DEBUG mode
            var debugMenu = this.FindControl<Menu>("DebugMenu");
            if (debugMenu is not null)
                debugMenu.IsVisible = true;
#endif

            // Push the panel's current overflow set into the freshly-attached
            // MoreMenuViewModel. Early measure passes may have fired before the VM
            // existed; this catches that case (and DB-switch refreshes the new VM too).
            SyncOverflowToViewModel();
        }
    }

    /// <summary>
    /// Forwards the top menu panel's current hidden-items list into the active
    /// <see cref="MoreMenuViewModel"/>. Called from both the panel's change event and
    /// from <see cref="OnDataContextChanged"/> to handle timing ordering.
    /// </summary>
    private void OnTopMenuOverflowChanged(object? sender, EventArgs e) => SyncOverflowToViewModel();

    private void SyncOverflowToViewModel()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (this.FindControl<ResponsiveMenuPanel>("TopMenuPanel") is not { } panel) return;

        var keys = new List<string>(panel.HiddenOverflowItems.Count);
        foreach (var c in panel.HiddenOverflowItems)
        {
            if (!string.IsNullOrEmpty(c.Name)) keys.Add(c.Name);
        }
        vm.MoreMenuVm.SetHiddenKeys(keys);
    }

    private void OnMainWindowVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When flyout closes (FlyoutPage becomes null), refresh workload to catch any release changes
        if (e.PropertyName == nameof(MainWindowViewModel.FlyoutPage) && Vm.FlyoutPage is null)
            Vm.WorkloadPanelVm.Reload();
    }

    private void OnWorkloadItemClicked(WorkloadItemViewModel item)
    {
        // Only handle section items; releases don't have a direct display in Section View
        if (item.Kind != WorkloadItemKind.Section)
            return;

        // Find the corresponding section in the section list and select it
        var sectionId = item.Id;
        var sectionItem = Vm.SectionListVm.SectionItems.OfType<SectionListItemViewModel>()
            .FirstOrDefault(s => s.Section.Id == sectionId);

        if (sectionItem is not null)
        {
            Vm.SectionListVm.SelectedItem = sectionItem;
            // SelectedItem change will automatically sync to ScheduleGridViewModel.SelectedSectionId
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
                slot.PanelContent = new SectionPanelContent { DataContext = Vm };
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
                // On reattach, create a fresh view and update the instance reference
                // used by ExportViewModel for PNG export.
                var freshView = new ScheduleGridView { DataContext = Vm.ScheduleGridVm };
                ScheduleGridViewInstance = freshView;
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
