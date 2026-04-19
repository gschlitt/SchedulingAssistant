using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.Views;
using SchedulingAssistant.ViewModels.GridView;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Platform.Storage;

namespace SchedulingAssistant.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ScheduleGridViewModel _scheduleGridVm;
    private readonly WriteLockService _lockService;
    private readonly AppNotificationService _notificationService;

    /// <summary>
    /// The permanent left-panel section list. Held for app lifetime.
    /// </summary>
    public SectionListViewModel SectionListVm { get; }

    /// <summary>
    /// The permanent left-panel meeting list. Held for app lifetime.
    /// Swaps with <see cref="SectionListVm"/> via <see cref="IsShowingMeetings"/>.
    /// </summary>
    public MeetingListViewModel MeetingListVm { get; }

    /// <summary>
    /// True when the left panel is showing the Events View; false for Section View (default).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPanelTitle))]
    [NotifyPropertyChangedFor(nameof(ToggleMeetingViewLabel))]
    [NotifyPropertyChangedFor(nameof(IsShowingSections))]
    private bool _isShowingMeetings;

    /// <summary>True when the Section View is active in the left panel.</summary>
    public bool IsShowingSections => !IsShowingMeetings;

    /// <summary>Title shown in the left panel header — changes with the active view.</summary>
    public string LeftPanelTitle => IsShowingMeetings ? "Events View" : "Section View";

    /// <summary>Label on the view-toggle button — shows the inactive view name.</summary>
    public string ToggleMeetingViewLabel => IsShowingMeetings ? "← Sections" : "Events →";

    /// <summary>Toggles the left panel between Section View and Events View.</summary>
    [RelayCommand]
    private void ToggleMeetingView() => IsShowingMeetings = !IsShowingMeetings;

    /// <summary>
    /// The permanent right-panel schedule grid. Held for app lifetime.
    /// </summary>
    public ScheduleGridViewModel ScheduleGridVm => _scheduleGridVm;

    /// <summary>
    /// The permanent bottom-left workload panel. Held for app lifetime.
    /// </summary>
    public WorkloadPanelViewModel WorkloadPanelVm { get; }

    /// <summary>
    /// The management ViewModel currently shown in the flyout overlay.
    /// Null when the flyout is hidden.
    /// </summary>
    [ObservableProperty]
    private object? _flyoutPage;

    /// <summary>
    /// Title shown in the flyout header bar.
    /// </summary>
    [ObservableProperty]
    private string _flyoutTitle = string.Empty;

    /// <summary>
    /// Exposed so MainWindow.axaml can bind the semester ComboBox directly
    /// to the singleton context without going through intermediary properties.
    /// </summary>
    public SemesterContext SemesterContext { get; }

    /// <summary>
    /// The display name of the open database (filename without extension).
    /// </summary>
    [ObservableProperty]
    private string _databaseName = string.Empty;

    /// <summary>
    /// The full file-system path of the open database. Used as a tooltip on the
    /// database name label in the menu bar.
    /// </summary>
    [ObservableProperty]
    private string _databasePath = string.Empty;

    /// <summary>
    /// Observable list of recent database files for the Files menu.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RecentDatabaseItem> _recentDatabases = new();

    /// <summary>
    /// Reference to the main window for file operations.
    /// Set by MainWindow.axaml.cs after instantiation.
    /// </summary>
    public MainWindow? MainWindowReference { get; set; }

    // ── Save / checkout properties ────────────────────────────────────────────

    /// <summary>
    /// True when the database has unsaved changes (first write since last save).
    /// Cleared when a save completes. Drives the "Unsaved changes" indicator in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Called by MainWindow when the database first becomes dirty.</summary>
    internal void SetDirty()   => IsDirty = true;

    /// <summary>Called by MainWindow when a save completes and dirty state is cleared.</summary>
    internal void ClearDirty() => IsDirty = false;

    /// <summary>
    /// Human-readable description of a save error, or null when no error is active.
    /// Shown in the save-error banner below the menu bar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveError))]
    private string? _saveErrorMessage;

    /// <summary>True when the save-error banner should be visible.</summary>
    public bool ShowSaveError => !string.IsNullOrEmpty(SaveErrorMessage);

    /// <summary>
    /// Whether the autosave timer is enabled. Persisted to <see cref="AppSettings"/>.
    /// Setting this starts or stops the timer in <see cref="CheckoutService"/> immediately.
    /// </summary>
    public bool AutoSaveEnabled
    {
        get => AppSettings.Current.AutoSaveEnabled;
        set
        {
            if (AppSettings.Current.AutoSaveEnabled == value) return;
            AppSettings.Current.AutoSaveEnabled = value;
            AppSettings.Current.Save();
            if (value) App.Checkout.StartAutoSave();
            else        App.Checkout.StopAutoSave();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Saves D' back to D. Delegates to <see cref="CheckoutService.SaveAsync"/>.
    /// Outcome feedback is delivered through <see cref="SaveErrorMessage"/>.
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        await App.Checkout.SaveAsync();
        // SaveCompleted / SaveFailed events update SaveErrorMessage via MainWindow handlers.
    }

    /// <summary>Dismisses the save-error banner.</summary>
    [RelayCommand]
    private void DismissSaveError() => SaveErrorMessage = null;

    /// <summary>Called by MainWindow when a save succeeds.</summary>
    internal void ClearSaveError() => SaveErrorMessage = null;

    /// <summary>Called by MainWindow when a save fails.</summary>
    /// <param name="message">Human-readable error message.</param>
    internal void SetSaveError(string message) => SaveErrorMessage = message;

    // ── Write-lock properties ─────────────────────────────────────────────────

    /// <summary>
    /// Timestamp of the last manual data refresh performed while in read-only mode.
    /// Shown in the banner tooltip rather than inline, to avoid being confused with
    /// the lock heartbeat.  Null until the user clicks Refresh for the first time.
    /// </summary>
    private DateTime? _lastRefreshedAt;

    /// <summary>
    /// True when this instance holds the write lock and the user may make edits.
    /// False in read-only mode. Bound by container <c>IsEnabled</c> in the view
    /// to gate all write-capable UI in a single binding per container.
    /// </summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>
    /// True when the lock banner should be displayed (i.e., this instance is in
    /// read-only mode).
    /// </summary>
    public bool ShowLockBanner => !_lockService.IsWriter;

    /// <summary>
    /// Human-readable description of the current lock state for the banner.
    /// Shows who holds the lock and since when.
    /// Returns null when this instance is the writer (banner is hidden).
    /// </summary>
    public string? LockStatusMessage
    {
        get
        {
            if (_lockService.IsWriter) return null;
            var h = _lockService.CurrentHolder;
            return h is null
                ? "Read-only — database is locked by another instance."
                : $"Read-only — held by {h.Username} on {h.Machine} since {h.Acquired.ToLocalTime():h:mm tt}";
        }
    }

    /// <summary>
    /// Tooltip text for the Refresh button in the read-only banner.
    /// Shows when data was last refreshed, or a prompt to refresh if it hasn't been yet.
    /// </summary>
    public string RefreshButtonTooltip => _lastRefreshedAt is { } t
        ? $"Last refreshed at {t:h:mm tt}. Click to reload again."
        : "Reload sections from the database to see changes made by the writer.";

    /// <summary>
    /// True when a read-only instance has detected that the write lock is no longer
    /// held and the user should be prompted to switch to edit mode.
    /// </summary>
    public bool ShowWriteLockAvailablePrompt => _lockService.WriteLockBecameAvailable;

    // ── Notification banner properties ────────────────────────────────────────

    /// <summary>True when a notification is currently queued for display.</summary>
    public bool HasNotification => _notificationService.HasNotification;

    /// <summary>Text of the current notification, or null when the banner is hidden.</summary>
    public string? NotificationMessage => _notificationService.Current?.Message;

    /// <summary>True when the current notification is informational (blue banner).</summary>
    public bool NotificationIsInfo    => _notificationService.Current?.IsInfo    ?? true;
    /// <summary>True when the current notification is a warning (amber banner).</summary>
    public bool NotificationIsWarning => _notificationService.Current?.IsWarning ?? false;
    /// <summary>True when the current notification is an error (red banner).</summary>
    public bool NotificationIsError   => _notificationService.Current?.IsError   ?? false;

    /// <summary>True when the current notification may be dismissed by the user.</summary>
    public bool NotificationIsDismissable => _notificationService.Current?.IsDismissable ?? false;

    /// <summary>True when the current notification carries a hyperlink.</summary>
    public bool NotificationHasLink => _notificationService.Current?.HasLink ?? false;

    /// <summary>Display label of the current notification's hyperlink, or null.</summary>
    public string? NotificationLinkText => _notificationService.Current?.LinkText;

    /// <summary>URL of the current notification's hyperlink, or null.</summary>
    public string? NotificationLinkUrl => _notificationService.Current?.LinkUrl;

    /// <summary>Dismisses the current notification, advancing to the next queued one if any.</summary>
    [RelayCommand]
    private void DismissNotification() => _notificationService.Dismiss();

    /// <summary>
    /// Opens the current notification's hyperlink in the default browser.
    /// No-op when the current notification has no link.
    /// </summary>
    [RelayCommand]
    private void OpenNotificationLink()
    {
        var url = _notificationService.Current?.LinkUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                { UseShellExecute = true });
        }
        catch { /* non-critical — link open failure should not crash the app */ }
    }

    /// <summary>
    /// Sets the database display name and full path shown in the menu bar.
    /// </summary>
    /// <param name="name">Filename without extension, shown as the label.</param>
    /// <param name="fullPath">Full file-system path, shown as a tooltip.</param>
    internal void SetDatabaseName(string name, string fullPath = "")
    {
        DatabaseName = name;
        DatabasePath = fullPath;
    }

    /// <summary>
    /// Populate the recent databases list from AppSettings.
    /// Call this after the VM is created and the main window is available.
    /// </summary>
    public void LoadRecentDatabases()
    {
        RecentDatabases.Clear();
        var settings = AppSettings.Current;
        foreach (var path in settings.RecentDatabases)
        {
            if (File.Exists(path))
            {
                var capturedPath = path;
                RecentDatabases.Add(new RecentDatabaseItem
                {
                    Path = path,
                    DisplayName = Path.GetFileName(path),
                    OpenCommand = new AsyncRelayCommand(() => OpenRecentDatabase(capturedPath))
                });
            }
        }
    }

    public MainWindowViewModel(
        IServiceProvider services,
        SemesterContext semesterContext,
        SectionListViewModel sectionListVm,
        MeetingListViewModel meetingListVm,
        ScheduleGridViewModel scheduleGridVm,
        WorkloadPanelViewModel workloadPanelVm,
        WriteLockService lockService,
        AppNotificationService notificationService)
    {
        _services             = services;
        SemesterContext       = semesterContext;
        SectionListVm         = sectionListVm;
        MeetingListVm         = meetingListVm;
        _scheduleGridVm       = scheduleGridVm;
        WorkloadPanelVm       = workloadPanelVm;
        _lockService          = lockService;
        _notificationService  = notificationService;

        // React to lock state changes (e.g., writer released the lock while we are
        // in read-only mode) so the banner updates automatically.
        _lockService.LockStateChanged += OnLockStateChanged;

        // Re-raise notification properties when the current notification changes.
        _notificationService.NotificationChanged += OnNotificationChanged;
    }

    /// <summary>
    /// Called on the UI thread when the lock state changes. Re-raises all lock-derived
    /// properties so the banner and control containers update via data binding.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        OnPropertyChanged(nameof(ShowLockBanner));
        OnPropertyChanged(nameof(LockStatusMessage));
        OnPropertyChanged(nameof(ShowWriteLockAvailablePrompt));
    }

    /// <summary>
    /// Called on the UI thread when the notification queue changes. Re-raises all
    /// notification-derived properties so the banner updates via data binding.
    /// </summary>
    private void OnNotificationChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasNotification));
        OnPropertyChanged(nameof(NotificationMessage));
        OnPropertyChanged(nameof(NotificationIsInfo));
        OnPropertyChanged(nameof(NotificationIsWarning));
        OnPropertyChanged(nameof(NotificationIsError));
        OnPropertyChanged(nameof(NotificationIsDismissable));
        OnPropertyChanged(nameof(NotificationHasLink));
        OnPropertyChanged(nameof(NotificationLinkText));
        OnPropertyChanged(nameof(NotificationLinkUrl));
    }

    /// <summary>
    /// Switches from read-only mode to write mode by delegating to
    /// <see cref="MainWindow.SwitchDatabaseAsync"/>, which handles the full
    /// release → checkout → re-initialize cycle.
    /// Called when the user clicks "Switch to edit mode" after the polling timer
    /// signals the lock is free.
    /// </summary>
    /// <remarks>
    /// Previously this method called <see cref="CheckoutService.CheckoutAsync"/> directly
    /// and then passed <c>WorkingPath</c> (D') to <c>SwitchDatabaseAsync</c>.
    /// That caused a double-checkout: SwitchDatabaseAsync released and deleted D', then
    /// re-ran checkout on the now-missing path, creating an empty database.
    /// The fix is to pass the canonical source path D and let SwitchDatabaseAsync own
    /// the entire checkout sequence via <c>RunCheckoutAsync</c>.
    /// </remarks>
    [RelayCommand]
    private async Task AcquireWriteLock()
    {
        if (MainWindowReference is null) return;

        var sourcePath = App.Checkout.SourcePath;
        if (string.IsNullOrEmpty(sourcePath))
            sourcePath = AppSettings.Current.DatabasePath;
        if (sourcePath is null) return;

        // Delegate the full release → checkout → re-initialize cycle to
        // SwitchDatabaseAsync, passing D (the canonical source path).
        // SwitchDatabaseAsync calls RunCheckoutAsync(D) internally, which acquires
        // the write lock, copies D → D', and re-initializes DI against D'.
        // If the lock is no longer free (someone else grabbed it between the poll
        // and this click), RunCheckoutAsync falls back to read-only mode gracefully.
        await MainWindowReference.SwitchDatabaseAsync(sourcePath);
    }

    /// <summary>
    /// Reloads all section data from the database. In read-only snapshot mode, first refreshes D'' from D,
    /// then reloads the UI from the updated snapshot. In write mode, just reloads from the current D'.
    /// Triggers a full <see cref="SectionStore"/> reload, which cascades to the schedule grid, section list,
    /// and workload panel via their <c>SectionsChanged</c> subscriptions.
    /// </summary>
    [RelayCommand]
    private async Task RefreshData()
    {
        // If in read-only snapshot mode, re-copy D → D'' to fetch latest data.
        if (App.Checkout.IsReadOnlyMode)
        {
            var dbContext = _services.GetRequiredService<IDatabaseContext>() as DatabaseContext;

            try
            {
                // beforeOverwrite: close the connection so CheckoutService can overwrite D''.
                // afterOverwrite: reopen the connection to the now-refreshed D''.
                async Task BeforeOverwrite()
                {
                    await Task.CompletedTask;
                    dbContext?.CloseConnection();
                }

                async Task AfterOverwrite()
                {
                    await Task.CompletedTask;
                    dbContext?.ReinitializeConnection(App.Checkout.WorkingPath);
                }

                var refreshOutcome = await App.Checkout.RefreshReadOnlySnapshotAsync(BeforeOverwrite, AfterOverwrite);

                var msg = refreshOutcome switch
                {
                    RefreshOutcome.Updated           => "Data refreshed from source.",
                    RefreshOutcome.SourceUnavailable => "Could not reach source — view may be stale.",
                    _ => "Refresh completed."
                };
                App.Logger.LogInfo($"MainWindowViewModel: {msg}");
            }
            catch (Exception ex)
            {
                App.Logger.LogError(ex, "MainWindowViewModel: refresh failed");
            }
        }

        // ReloadFromDatabase re-queries the DB and updates the SectionStore cache.
        // The SectionsChanged event then cascades automatically to the schedule grid
        // and workload panel — no additional calls required here.
        SectionListVm.ReloadFromDatabase();
        _lastRefreshedAt = DateTime.Now;
        OnPropertyChanged(nameof(RefreshButtonTooltip));
    }

    partial void OnFlyoutPageChanged(object? oldValue, object? newValue)
    {
        (oldValue as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Handles the Escape key for flyouts. If the current flyout page has an active inline
    /// editor, that editor is dismissed first — the flyout stays open. Only when no editor
    /// is active does a subsequent Escape close the flyout itself. This gives Escape a natural
    /// "inner then outer" feel without relying on Avalonia routed-event phase ordering.
    /// </summary>
    [RelayCommand]
    private void CloseFlyout()
    {
        if (FlyoutPage is IDismissableEditor editor && editor.DismissActiveEditor())
            return;

        FlyoutPage = null;
    }

    private void OpenFlyout<TViewModel>(string title) where TViewModel : ViewModelBase
    {
        try
        {
            FlyoutPage = _services.GetRequiredService<TViewModel>();
            FlyoutTitle = title;
        }
        catch (Exception ex)
        {
            FlyoutPage = new ErrorViewModel($"Failed to open {title}: {ex.Message}\n\n{ex}");
            FlyoutTitle = title;
        }
    }

    [RelayCommand]
    private void NavigateToCourses() => OpenFlyout<CourseListViewModel>("Courses");

    [RelayCommand]
    private void NavigateToInstructors() => OpenFlyout<InstructorListViewModel>("Instructors");

    [RelayCommand]
    private void NavigateToAcademicYears() => OpenFlyout<AcademicYearListViewModel>("Academic Years");

    [RelayCommand]
    private void NavigateToCopySemester()
    {
        var vm = _services.GetRequiredService<CopySemesterViewModel>();
        vm.NavigateToAcademicYears = NavigateToAcademicYears;
        FlyoutPage = vm;
        FlyoutTitle = "Copy Semester";
    }

    [RelayCommand]
    private void NavigateToEmptySemester() => OpenFlyout<EmptySemesterViewModel>("Empty Semester");

    [RelayCommand]
    private void NavigateToBlockLengths() => OpenFlyout<LegalStartTimeListViewModel>("Scheduling");

    [RelayCommand]
    private void NavigateToSchedulingEnvironment() => OpenFlyout<SchedulingEnvironmentViewModel>("Scheduling Environment");

    [RelayCommand]
    private void NavigateToBlockPatterns() => OpenFlyout<BlockPatternListViewModel>("Block Patterns");

    [RelayCommand]
    private void NavigateToAcademicUnits() => OpenFlyout<AcademicUnitListViewModel>("Academic Units");

    [RelayCommand]
    private void NavigateToSectionPrefixes() => OpenFlyout<SectionPrefixListViewModel>("Section Prefixes");

    [RelayCommand]
    private void NavigateToExport() => OpenFlyout<ExportViewModel>("Export");

    [RelayCommand]
    private void NavigateToWorkloadReport() => OpenFlyout<WorkloadReportViewModel>("Workload Report");

    [RelayCommand]
    private void NavigateToWorkloadMailer() => OpenFlyout<WorkloadMailerViewModel>("Workload Mailer");

    [RelayCommand]
    private void NavigateToHelp() => OpenFlyout<HelpViewModel>("Help");

    [RelayCommand]
    private void NavigateToShare() => OpenFlyout<ShareViewModel>("Share");

    /// <summary>
    /// Opens the Save &amp; Backup flyout and wires the restore callback so that
    /// <see cref="SaveAndBackupViewModel"/> can trigger a database restore + app restart
    /// without knowing anything about the view layer.
    /// </summary>
    [RelayCommand]
    private void NavigateToSettings()
    {
        var vm = _services.GetRequiredService<SaveAndBackupViewModel>();
        vm.RestoreCallback = RestoreFromBackupAsync;
        FlyoutPage  = vm;
        FlyoutTitle = "Save & Backup";
    }

    /// <summary>
    /// Restores a backup by copying it over the main database file, then restarting
    /// the application. The write lock is released and the DI container is disposed
    /// before the file copy so the SQLite connection is cleanly closed first.
    /// </summary>
    /// <param name="backupDbPath">Full path to the .db backup file to restore from.</param>
    private async Task RestoreFromBackupAsync(string backupDbPath)
    {
        var mainDbPath = AppSettings.Current.DatabasePath;
        if (mainDbPath is null) return;

        try
        {
            // Release the write lock and close the database connection.
            _services.GetRequiredService<BackupService>().StopSession();
            _services.GetRequiredService<WriteLockService>().Release();
            (App.Services as IDisposable)?.Dispose();

            // Overwrite the main database with the selected backup.
            File.Copy(backupDbPath, mainDbPath, overwrite: true);

            // Restart the application so it opens the restored database cleanly.
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Restore failed — DI may be disposed; showing plain error window");
            // DI is disposed at this point so we cannot use IDialogService.
            // Delegate to MainWindow.ShowFatalAsync so the VM stays free of Avalonia.Controls construction.
            if (MainWindowReference is { } win)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    MainWindow.ShowFatalAsync(
                        win,
                        title:   "Restore Failed",
                        heading: "Could not restore the database.",
                        detail:  ex.Message,
                        footer:  "The application will now close."));
            }

            (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.Shutdown();
        }
    }

#if DEBUG
    [RelayCommand]
    private void OpenDebug() => OpenFlyout<DebugTestDataViewModel>("Debug: Generate Test Data");

    // ONE-TIME MIGRATION UTILITY — remove after migration is complete
    [RelayCommand]
    private void OpenMigration() => OpenFlyout<MigrationViewModel>("Migration: CSV → JSON (one-time utility)");
#endif

    // ── File menu commands ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDatabase()
    {
        if (MainWindowReference is null) return;

        var dialog = new DatabaseLocationDialog(DatabaseLocationMode.OpenExisting);
        await dialog.ShowDialog(MainWindowReference);

        if (dialog.ChosenPath is not null)
        {
            await MainWindowReference.SwitchDatabaseAsync(dialog.ChosenPath);
        }
    }

    [RelayCommand]
    private void NewDatabase()
    {
        if (MainWindowReference is null) return;

        var vm = _services.GetRequiredService<NewDatabaseViewModel>();

        vm.PickFolderAsync = async title =>
        {
            var result = await MainWindowReference.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title       = title,
                    AllowMultiple = false
                });
            return result.Count > 0 ? result[0].TryGetLocalPath() : null;
        };

        vm.SwitchDatabaseAsync = path => MainWindowReference.SwitchDatabaseAsync(path);

        FlyoutPage  = vm;
        FlyoutTitle = "New Database";
    }

    [RelayCommand]
    private async Task OpenRecentDatabase(string? databasePath)
    {
        if (databasePath is null || MainWindowReference is null) return;
        await MainWindowReference.SwitchDatabaseAsync(databasePath);
    }

    /// <summary>
    /// Closes the main window via the Files → Exit menu item.
    /// Triggers the same <see cref="MainWindow.OnClosing"/> path as clicking the title-bar X,
    /// so all shutdown logic lives in one place.
    /// </summary>
    [RelayCommand]
    private void Exit() => MainWindowReference?.Close();
}
