using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Views;
using TermPoint.ViewModels.GridView;
using TermPoint.ViewModels.Management;
using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace TermPoint.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ScheduleGridViewModel _scheduleGridVm;
    private readonly WriteLockService _lockService;
    private readonly AppNotificationService _notificationService;
    private readonly TourRunner _tourRunner;

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

    /// <summary>
    /// True when either the section editor or the meeting editor is open.
    /// Drives the left-panel column width via ConditionalColumnWidthBehavior.
    /// </summary>
    public bool IsAnyPanelEditing => SectionListVm.IsEditing || MeetingListVm.IsEditing;

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
    /// Tour overlay ViewModel. Drives the walkthrough card and highlight ring.
    /// </summary>
    public TourOverlayViewModel TourOverlayVm { get; }

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

#if !BROWSER
    /// <summary>
    /// Reference to the main window for file operations.
    /// Set by MainWindow.axaml.cs after instantiation.
    /// </summary>
    public MainWindow? MainWindowReference { get; set; }
#endif

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
    /// True when the platform supports saving to the database file.
    /// Drives <see cref="SaveCommand"/> CanExecute and the enabled state of the
    /// Auto-save checkbox in the toolbar. Always false in the browser demo.
    /// </summary>
    public bool SupportsSave => PlatformCapabilities.SupportsSave;

    /// <summary>
    /// Whether the autosave timer is enabled. Persisted to <see cref="AppSettings"/>.
    /// Setting this starts or stops the timer in <see cref="CheckoutService"/> immediately.
    /// Always false and a no-op in the browser demo.
    /// </summary>
    public bool AutoSaveEnabled
    {
        get =>
#if !BROWSER
            AppSettings.Current.AutoSaveEnabled;
#else
            false;
#endif
        set
        {
#if !BROWSER
            if (AppSettings.Current.AutoSaveEnabled == value) return;
            AppSettings.Current.AutoSaveEnabled = value;
            AppSettings.Current.Save();
            if (value) App.Checkout.StartAutoSave();
            else        App.Checkout.StopAutoSave();
            OnPropertyChanged();
#endif
        }
    }

    private bool CanSave() => PlatformCapabilities.SupportsSave;

    /// <summary>
    /// Saves D' back to D. Delegates to <see cref="CheckoutService.SaveAsync"/>.
    /// Outcome feedback is delivered through <see cref="SaveErrorMessage"/>.
    /// Disabled in the browser demo (<see cref="SupportsSave"/> is false).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
#if !BROWSER
        await App.Checkout.SaveAsync();
        // SaveCompleted / SaveFailed events update SaveErrorMessage via MainWindow handlers.
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// How long a transient (auto-dismissing) save-error banner stays visible before
    /// it clears itself. Tuned to comfortably outlast a single autosave retry cycle
    /// while not lingering long enough to feel stale.
    /// </summary>
    private static readonly TimeSpan SaveErrorAutoDismissDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// One-shot timer that auto-clears a transient save-error banner. Null when no
    /// auto-dismiss is pending. Recreated on demand so a fresh error restarts the clock.
    /// </summary>
    private DispatcherTimer? _saveErrorDismissTimer;

    /// <summary>Stops and discards any pending auto-dismiss timer.</summary>
    private void StopSaveErrorDismissTimer()
    {
        if (_saveErrorDismissTimer is null) return;
        _saveErrorDismissTimer.Stop();
        _saveErrorDismissTimer = null;
    }

    /// <summary>Dismisses the save-error banner.</summary>
    [RelayCommand]
    private void DismissSaveError()
    {
        StopSaveErrorDismissTimer();
        SaveErrorMessage = null;
    }

    /// <summary>Called by MainWindow when a save succeeds.</summary>
    internal void ClearSaveError()
    {
        StopSaveErrorDismissTimer();
        SaveErrorMessage = null;
    }

    /// <summary>Called by MainWindow when a save fails.</summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="autoDismiss">
    /// When true the error is transient (a background autosave will retry), so the
    /// banner auto-clears after <see cref="SaveErrorAutoDismissDelay"/>. When false the
    /// error is sticky and stays until the next successful save or manual dismissal.
    /// </param>
    internal void SetSaveError(string message, bool autoDismiss)
    {
        SaveErrorMessage = message;

        // Always reset any previous timer first: a sticky error must cancel a pending
        // dismiss, and a new transient error should restart the clock from now.
        StopSaveErrorDismissTimer();

        if (!autoDismiss) return;

        _saveErrorDismissTimer = new DispatcherTimer { Interval = SaveErrorAutoDismissDelay };
        _saveErrorDismissTimer.Tick += (_, _) => DismissSaveError();
        _saveErrorDismissTimer.Start();
    }

    // ── Write-lock properties ─────────────────────────────────────────────────

    /// <summary>
    /// Timestamp of the last manual data refresh performed while in read-only mode.
    /// Shown in the banner tooltip rather than inline, to avoid being confused with
    /// the lock heartbeat.  Null until the user clicks Refresh for the first time.
    /// </summary>
    private DateTime? _lastRefreshedAt;

    /// <summary>
    /// Set to true when the user dismisses the lock banner. Reset to false when the
    /// write lock becomes available so the banner (now with a "take over" prompt) reappears.
    /// </summary>
    private bool _lockBannerDismissed;

    /// <summary>
    /// True when this instance holds the write lock and the user may make edits.
    /// False in read-only mode. Bound by container <c>IsEnabled</c> in the view
    /// to gate all write-capable UI in a single binding per container.
    /// </summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>
    /// True when the lock advisory banner should be shown. Hidden once the user
    /// dismisses it, but reappears when the write lock becomes available.
    /// </summary>
    public bool ShowLockBanner => !_lockService.IsWriter && !_lockBannerDismissed;

    /// <summary>
    /// True when the user has dismissed the lock banner but is still in read-only
    /// mode. Drives the "Reader Mode" indicator in the toolbar.
    /// </summary>
    public bool ShowReaderModeIndicator => !_lockService.IsWriter && _lockBannerDismissed;

    /// <summary>
    /// Human-readable description of the current lock state.
    /// Used by both the banner text and the toolbar "Reader Mode" hover tooltip.
    /// Returns null when this instance is the writer.
    /// </summary>
    public string? LockStatusMessage
    {
        get
        {
            if (_lockService.IsWriter) return null;

            // Voluntary observer: read-only by the user's own choice, not because someone
            // else holds the lock. The "locked by another instance" wording would mislead.
            if (AppSettings.Current.OpenInReaderMode)
                return "Reader mode — opened as an observer. Editing is disabled; click Refresh to pull the latest data.";

            // Not contention at all — the lock file could not be created (Controlled Folder Access,
            // an ACL denial, disk full, …). Show the precise, actionable reason rather than implying
            // another instance holds the lock.
            if (_lockService.LockWriteError is { } writeError)
                return writeError;

            // A second live instance on this same machine — be explicit so the user knows
            // it's their own other window, not a colleague, blocking edit access.
            if (_lockService.HolderIsLiveSameMachine)
                return "Read-only — another TermPoint window is already editing this database on this computer.";

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

    /// <summary>
    /// True when the lock-file failure has a technical IT-detail string that can be shown
    /// via the "Details for IT" button. Only relevant when <see cref="LockWriteError"/> is set
    /// and the cause is a CFA/permission block (not disk-full or other non-CFA failures).
    /// </summary>
    public bool HasLockWriteItDetail => _lockService.LockWriteItDetail is not null;

    /// <summary>
    /// Dismisses the lock advisory banner. A compact "Reader Mode" indicator
    /// appears in the toolbar instead. The banner reappears automatically when
    /// the write lock becomes available.
    /// </summary>
    [RelayCommand]
    private void DismissLockBanner()
    {
        _lockBannerDismissed = true;
        OnPropertyChanged(nameof(ShowLockBanner));
        OnPropertyChanged(nameof(ShowReaderModeIndicator));
    }

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
            Services.PlatformProcess.OpenUri(url);
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
#if !BROWSER
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
#endif
    }

    public MainWindowViewModel(
        IServiceProvider services,
        SemesterContext semesterContext,
        SectionListViewModel sectionListVm,
        MeetingListViewModel meetingListVm,
        ScheduleGridViewModel scheduleGridVm,
        WorkloadPanelViewModel workloadPanelVm,
        WriteLockService lockService,
        AppNotificationService notificationService,
        TourRunner tourRunner)
    {
        _services             = services;
        SemesterContext       = semesterContext;
        SectionListVm         = sectionListVm;
        MeetingListVm         = meetingListVm;
        _scheduleGridVm       = scheduleGridVm;
        WorkloadPanelVm       = workloadPanelVm;
        _lockService          = lockService;
        _notificationService  = notificationService;
        _tourRunner           = tourRunner;
        TourOverlayVm         = new TourOverlayViewModel(tourRunner);

        // Wire the Room Availability Browser ghost block callback
        sectionListVm._setGhostBlocks = ghosts => _scheduleGridVm.SetGhostBlocks(ghosts);
        meetingListVm._setGhostBlocks = ghosts => _scheduleGridVm.SetGhostBlocks(ghosts);

        // Re-raise IsAnyPanelEditing when either child editor opens or closes.
        sectionListVm.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(SectionListViewModel.IsEditing)) OnPropertyChanged(nameof(IsAnyPanelEditing)); };
        meetingListVm.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(MeetingListViewModel.IsEditing)) OnPropertyChanged(nameof(IsAnyPanelEditing)); };

        // React to lock state changes (e.g., writer released the lock while we are
        // in read-only mode) so the banner updates automatically.
        _lockService.LockStateChanged += OnLockStateChanged;

        // Re-raise notification properties when the current notification changes.
        _notificationService.NotificationChanged += OnNotificationChanged;

        // Build the More-menu VM eagerly and wire up cross-VM callbacks. Held for the
        // lifetime of the main window; content pane is resolved on demand.
        MoreMenuVm = new MoreMenuViewModel(_services);
        MoreMenuVm.TitleChangeRequested += OnMoreMenuTitleChangeRequested;
#if !BROWSER
        MoreMenuVm.ConfigurationRestoreCallback = RestoreFromBackupAsync;
#endif
    }

    /// <summary>
    /// Hosts the "More…" flyout that appears when the top menu bar overflows.
    /// Populated by the top-bar codebehind via <see cref="MoreMenuViewModel.SetHiddenKeys"/>.
    /// </summary>
    public MoreMenuViewModel MoreMenuVm { get; }

    /// <summary>
    /// True while the More flyout is open. Drives the "More…" button's active-state
    /// highlight in the top bar via <c>Classes.active</c>. Updated from
    /// <see cref="OnFlyoutPageChanged"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isMoreOpen;

    /// <summary>
    /// Opens the More flyout by pushing <see cref="MoreMenuVm"/> into <see cref="FlyoutPage"/>.
    /// The top-bar codebehind will already have called <see cref="MoreMenuViewModel.SetHiddenKeys"/>
    /// in response to the panel's HiddenOverflowItemsChanged event.
    /// </summary>
    [RelayCommand]
    private void OpenMoreMenu()
    {
        FlyoutPage = MoreMenuVm;
        FlyoutTitle = "More";
    }

    /// <summary>
    /// Receives title-change requests from <see cref="MoreMenuVm"/> — e.g. when the user
    /// picks a rail entry the title becomes "More › &lt;entry&gt;".
    /// </summary>
    private void OnMoreMenuTitleChangeRequested(string title)
    {
        if (IsMoreOpen) FlyoutTitle = title;
    }

    /// <summary>
    /// Called on the UI thread when the lock state changes. Re-raises all lock-derived
    /// properties so the banner and control containers update via data binding.
    /// When the write lock becomes available, resets the dismiss flag so the banner
    /// reappears with the "take over" prompt.
    /// </summary>
    private void OnLockStateChanged()
    {
        if (_lockService.WriteLockBecameAvailable)
            _lockBannerDismissed = false;

        OnPropertyChanged(nameof(IsWriteEnabled));
        OnPropertyChanged(nameof(ShowLockBanner));
        OnPropertyChanged(nameof(ShowReaderModeIndicator));
        OnPropertyChanged(nameof(LockStatusMessage));
        OnPropertyChanged(nameof(ShowWriteLockAvailablePrompt));
        OnPropertyChanged(nameof(HasLockWriteItDetail));
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
#if !BROWSER
    [RelayCommand]
    private async Task AcquireWriteLock()
    {
        if (MainWindowReference is null) return;

        var sourcePath = App.Checkout.SourcePath;
        if (string.IsNullOrEmpty(sourcePath))
            sourcePath = AppSettings.Current.DatabasePath;
        if (sourcePath is null) return;

        await MainWindowReference.SwitchDatabaseAsync(sourcePath);
    }

    /// <summary>
    /// Shows a dialog with technical details about the lock-file write failure, aimed at IT support.
    /// Matches the "Details for IT" UX used by <see cref="DatabaseFolderNotWritableException"/> on the
    /// database-open path, so both CFA-blocked scenarios present a consistent experience.
    /// </summary>
    [RelayCommand]
    private async Task ShowLockWriteDetails()
    {
        if (MainWindowReference is null || _lockService.LockWriteItDetail is null) return;
        await MainWindowReference.ShowItDetailAsync(
            "Technical Details (for IT)", _lockService.LockWriteItDetail);
    }
#endif

    /// <summary>
    /// Reloads all section data from the database. In read-only snapshot mode, first refreshes D'' from D,
    /// then reloads the UI from the updated snapshot. In write mode, just reloads from the current D'.
    /// Triggers a full <see cref="SectionStore"/> reload, which cascades to the schedule grid, section list,
    /// and workload panel via their <c>SectionsChanged</c> subscriptions.
    /// </summary>
    [RelayCommand]
    private async Task RefreshData()
    {
#if !BROWSER
        // If in read-only snapshot mode, re-copy D → D'' to fetch latest data.
        if (App.Checkout.IsReadOnlyMode)
        {
            var dbContext = _services.GetRequiredService<IDatabaseContext>() as DatabaseContext;

            try
            {
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
#else
        await Task.CompletedTask;
#endif

        SectionListVm.ReloadFromDatabase();
        _lastRefreshedAt = DateTime.Now;
        OnPropertyChanged(nameof(RefreshButtonTooltip));
    }

    partial void OnFlyoutPageChanged(object? oldValue, object? newValue)
    {
        // MoreMenuVm is a long-lived singleton — never dispose it when the flyout closes.
        if (oldValue is not null && !ReferenceEquals(oldValue, MoreMenuVm))
            (oldValue as IDisposable)?.Dispose();

        IsMoreOpen = ReferenceEquals(newValue, MoreMenuVm);
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
    private void NavigateToSchedulingEnvironment() => OpenFlyout<SchedulingEnvironmentViewModel>("Scheduling Environment");

    /// <summary>
    /// Opens the Configuration flyout hub and wires the Save &amp; Backup restore callback
    /// so <see cref="SaveAndBackupViewModel"/> can trigger a database restore without knowing
    /// anything about the view layer.
    /// </summary>
    [RelayCommand]
    private void NavigateToConfiguration()
    {
        var vm = _services.GetRequiredService<ConfigurationViewModel>();
#if !BROWSER
        vm.SaveAndBackupVm.RestoreCallback = RestoreFromBackupAsync;
#endif
        FlyoutPage  = vm;
        FlyoutTitle = "Configuration";
    }

    [RelayCommand]
    private void NavigateToExportHub() => OpenFlyout<ExportHubViewModel>("Reports");

    [RelayCommand]
    private void NavigateToSharing() => OpenFlyout<SharingViewModel>("Sharing");

    [RelayCommand]
    private void NavigateToWorkflows() => OpenFlyout<WorkflowsViewModel>("Workflows");

    [RelayCommand]
    private void NavigateToHelp() => OpenFlyout<HelpViewModel>("Help");

#if !BROWSER
    private async Task RestoreFromBackupAsync(string backupDbPath)
    {
        var mainDbPath = AppSettings.Current.DatabasePath;
        if (mainDbPath is null) return;

        try
        {
            _services.GetRequiredService<BackupService>().StopSession();
            _services.GetRequiredService<WriteLockService>().Release();
            (App.Services as IDisposable)?.Dispose();

            File.Copy(backupDbPath, mainDbPath, overwrite: true);

            var exePath = Environment.ProcessPath;
            if (exePath is not null)
                Services.PlatformProcess.LaunchExecutable(exePath);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "Restore failed — DI may be disposed; showing plain error window");
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
#endif

#if !BROWSER && DEBUG
    [RelayCommand]
    private void OpenDebug() => OpenFlyout<DebugTestDataViewModel>("Debug: Generate Test Data");

    // ONE-TIME MIGRATION UTILITY — remove after migration is complete
    [RelayCommand]
    private void OpenMigration() => OpenFlyout<MigrationViewModel>("Migration: CSV → JSON (one-time utility)");
#endif

#if !BROWSER
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
    /// Exports the schedule grid as a PNG image. Prompts the user for a save location.
    /// Wired to the Files › Print… menu item.
    /// </summary>
    [RelayCommand]
    private async Task PrintSchedule()
    {
        var window = MainWindowReference;
        if (window is null) return;

        try
        {
            var settings = AppSettings.Current;

            IStorageFolder? suggestedFolder = null;
            if (settings.LastExportPath is not null)
            {
                var dir = Path.GetDirectoryName(settings.LastExportPath);
                if (dir is not null)
                    suggestedFolder = await window.StorageProvider.TryGetFolderFromPathAsync(dir);
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Print Schedule to PNG",
                SuggestedFileName = "schedule.png",
                DefaultExtension = "png",
                SuggestedStartLocation = suggestedFolder,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (file is null) return;

            var path = file.Path.LocalPath;
            window.ScheduleGridViewInstance?.ExportToPng(path);

            settings.LastExportPath = path;
            settings.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PrintSchedule error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Exit() => MainWindowReference?.Close();
#endif
}
