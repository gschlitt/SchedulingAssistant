using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
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
    /// The subject-focus view model (spec item 20) shared by the indicator control and the Focus picker wherever
    /// they appear. Held for app lifetime.
    /// </summary>
    public FocusViewModel FocusVm { get; }

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
    /// Cleared when a save completes. Drives the "Unsaved changes" indicator in the UI
    /// (via <see cref="ShowDirtyIndicator"/>, which yields to "Saving…" mid-save).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDirtyIndicator))]
    private bool _isDirty;

    /// <summary>
    /// True while a save is writing to the database. Set/cleared by MainWindow from
    /// <see cref="Services.CheckoutService.SaveStarted"/> / <c>SaveFinished</c>.
    /// Drives the "Saving…" footer indicator — with stall-aware chunked transfers, a
    /// large database on a slow link can legitimately save for tens of seconds.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDirtyIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowSavingIndicator))]
    private bool _isSaving;

    /// <summary>
    /// True while the transient "A save is already in progress…" notice is showing —
    /// set when the user triggers Save while a save is already in flight (the click
    /// would otherwise be silently swallowed by <see cref="Services.CheckoutService"/>'s
    /// in-flight gate). Temporarily replaces the plain "Saving…" text, then a one-shot
    /// timer reverts it. Cleared immediately when the save finishes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSavingIndicator))]
    private bool _showSaveBusyNotice;

    /// <summary>
    /// True when the plain "Saving…" indicator should be visible: a save is running
    /// AND the "already in progress" notice isn't currently replacing it (the two
    /// occupy the same footer slot and never appear side by side).
    /// </summary>
    public bool ShowSavingIndicator => IsSaving && !ShowSaveBusyNotice;

    /// <summary>
    /// How long the "A save is already in progress…" notice stays before reverting to
    /// the plain "Saving…" text. Long enough to register, short enough that repeated
    /// clicks keep refreshing it without it feeling stuck.
    /// </summary>
    private static readonly TimeSpan SaveBusyNoticeDismissDelay = TimeSpan.FromSeconds(4);

    /// <summary>
    /// One-shot timer that reverts the busy notice to "Saving…". Null when no revert
    /// is pending. Recreated on demand so another click restarts the clock.
    /// </summary>
    private DispatcherTimer? _saveBusyNoticeTimer;

    /// <summary>Stops and discards any pending busy-notice revert timer.</summary>
    private void StopSaveBusyNoticeTimer()
    {
        if (_saveBusyNoticeTimer is null) return;
        _saveBusyNoticeTimer.Stop();
        _saveBusyNoticeTimer = null;
    }

    /// <summary>Hides the busy notice (reverting to "Saving…" if a save is still running).</summary>
    private void ClearSaveBusyNotice()
    {
        StopSaveBusyNoticeTimer();
        ShowSaveBusyNotice = false;
    }

    /// <summary>
    /// Called by MainWindow when a Save request was skipped because a save is already
    /// in flight. Shows the transient notice so the click is visibly acknowledged.
    /// </summary>
    internal void NotifySaveAlreadyInProgress()
    {
        // Raced past the finish line: the in-flight save completed between the click
        // and this dispatched notification. Its outcome (cleared dirty indicator or an
        // error banner) is already on screen — a busy notice now would be stale.
        if (!IsSaving) return;

        ShowSaveBusyNotice = true;
        StopSaveBusyNoticeTimer();
        _saveBusyNoticeTimer = new DispatcherTimer { Interval = SaveBusyNoticeDismissDelay };
        _saveBusyNoticeTimer.Tick += (_, _) => ClearSaveBusyNotice();
        _saveBusyNoticeTimer.Start();
    }

    /// <summary>
    /// True when the "Unsaved changes" indicator should be visible: there are unsaved
    /// changes AND no save is currently running ("Saving…" takes its place mid-save,
    /// so the two indicators never appear side by side).
    /// </summary>
    public bool ShowDirtyIndicator => IsDirty && !IsSaving;

    /// <summary>Called by MainWindow when the database first becomes dirty.</summary>
    internal void SetDirty()   => IsDirty = true;

    /// <summary>Called by MainWindow when a save completes and dirty state is cleared.</summary>
    internal void ClearDirty() => IsDirty = false;

    /// <summary>Called by MainWindow when a save attempt begins.</summary>
    internal void SetSaving()   => IsSaving = true;

    /// <summary>Called by MainWindow when a save attempt ends (any outcome).</summary>
    internal void ClearSaving()
    {
        IsSaving = false;
        ClearSaveBusyNotice(); // "already in progress" is stale once the save has ended
    }

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

    // ── Save conflict (spec item 10, step 10a) ────────────────────────────────

    /// <summary>
    /// True once a save was refused because the shared database was changed elsewhere since this session opened it
    /// (<see cref="CheckoutService.HasSaveConflict"/>), which shows the persistent save-conflict banner. This is
    /// deliberately separate from <see cref="SaveErrorMessage"/>: NOTHING clears it except ending the session — no
    /// Dismiss button, and <see cref="ClearSaveError"/>, <see cref="DismissSaveError"/> and
    /// <see cref="SetSaveError"/> leave it alone. A reopened database gets a new view model, which starts cleared.
    /// </summary>
    [ObservableProperty]
    private bool _hasSaveConflict;

    /// <summary>
    /// True while the user's choice from the save-conflict banner (Save a Copy… / Discard Changes) is being carried
    /// out. Disables both banner buttons so a double-click cannot start two resolutions.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveConflictCopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConflictDiscardCommand))]
    private bool _isResolvingSaveConflict;

    /// <summary>Text of the save-conflict banner (<see cref="SaveConflictPrompt.BannerMessage"/>).</summary>
    public string SaveConflictMessage => SaveConflictPrompt.BannerMessage;

    /// <summary>Label of the banner's Save a Copy… button (<see cref="SaveConflictPrompt.SaveCopyButton"/>).</summary>
    public string SaveConflictCopyLabel => SaveConflictPrompt.SaveCopyButton;

    /// <summary>Label of the banner's Discard Changes button (<see cref="SaveConflictPrompt.DiscardChangesButton"/>).</summary>
    public string SaveConflictDiscardLabel => SaveConflictPrompt.DiscardChangesButton;

    /// <summary>
    /// Called by MainWindow when <c>CheckoutService.SaveConflictDetected</c> fires. Shows the persistent
    /// save-conflict banner. Idempotent: the event fires on every refused save. Also clears any current save-error
    /// banner, which the conflict banner supersedes (a later, different save error may still appear there).
    /// </summary>
    internal void ShowSaveConflict()
    {
        if (HasSaveConflict) return;
        ClearSaveError();
        HasSaveConflict = true;
    }

    /// <summary>True when a banner button may run: no resolution is already under way.</summary>
    private bool CanResolveSaveConflict() => !IsResolvingSaveConflict;

    /// <summary>
    /// The banner's Save a Copy… button: saves the user's version as a separate file, then reopens the current
    /// database. The work is window-lifecycle work and lives in <see cref="MainWindow.ResolveSaveConflictAsync"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResolveSaveConflict))]
    private Task SaveConflictCopy() => ResolveSaveConflictAsync(saveCopy: true);

    /// <summary>
    /// The banner's Discard Changes button: discards the user's changes after confirmation, then reopens the
    /// current database. The work lives in <see cref="MainWindow.ResolveSaveConflictAsync"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResolveSaveConflict))]
    private Task SaveConflictDiscard() => ResolveSaveConflictAsync(saveCopy: false);

    /// <summary>
    /// Runs the chosen resolution through the main window, keeping <see cref="IsResolvingSaveConflict"/> set
    /// around it so both banner buttons are disabled meanwhile.
    /// </summary>
    /// <param name="saveCopy">True for Save a Copy…, false for Discard Changes.</param>
    private async Task ResolveSaveConflictAsync(bool saveCopy)
    {
#if !BROWSER
        if (MainWindowReference is null || IsResolvingSaveConflict) return;
        IsResolvingSaveConflict = true;
        try { await MainWindowReference.ResolveSaveConflictAsync(saveCopy); }
        finally { IsResolvingSaveConflict = false; }
#else
        await Task.CompletedTask; // the browser demo never saves, so it never has a save conflict
#endif
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
    public bool ShowLockBanner => !_lockService.IsWriter && !_lockBannerDismissed
#if !BROWSER
        && App.LicenseStatus.AccessLevel != Licensing.AccessLevel.ReadOnly
#endif
        ;

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

#if !BROWSER
            // License-gated read-only takes priority — the user can't edit regardless
            // of lock state, so show the licensing reason, not a contention message.
            if (App.LicenseStatus.AccessLevel == Licensing.AccessLevel.ReadOnly)
            {
                return App.LicenseStatus.Reason switch
                {
                    Licensing.AccessReason.Expired => "Read-only — your license has expired. Purchase or renew at termpoint.ca/license.",
                    Licensing.AccessReason.Unlicensed => "Read-only — your trial has ended. Purchase a license at termpoint.ca/license.",
                    _ => "Read-only — no active license."
                };
            }

            // Version-gated read-only: the database's MinimumVersionToWrite stamp is newer than
            // this app. Nobody holds the lock — the fix is to update — so say that, with wording
            // chosen by how TermPoint was installed (Store vs. everything else).
            if (App.Checkout.RequiredAppVersion is { } required)
                return MinimumVersionToWrite.BuildMessage(PlatformCapabilities.InstallChannel, required);
#endif

            // Voluntary observer: read-only by the user's own choice, not because someone
            // else holds the lock. The "locked by another instance" wording would mislead.
            if (AppSettings.Current.OpenInReaderMode)
                return "Reader mode — opened as an observer. Editing is disabled; click Refresh to pull the latest data.";

            // Not contention at all — the lock file could not be created (Controlled Folder Access,
            // an ACL denial, disk full, …). Show the precise, actionable reason rather than implying
            // another instance holds the lock.
            if (_lockService.LockWriteError is { } writeError)
                return writeError;

            // A live TermPoint on this same machine holds the lock. The same user can no longer reach
            // this (the single-instance guard refuses a second copy of the same database), but another
            // Windows user on this PC still can — so name them, and say what to do if they are stuck.
            if (_lockService.HolderIsLiveSameMachine)
                return SameComputerReadOnlyMessage(_lockService.CurrentHolder?.Username);

            var h = _lockService.CurrentHolder;
            return h is null
                ? "Read-only — database is locked by another instance."
                : $"Read-only — held by {h.Username} on {h.Machine} since {h.Acquired.ToLocalTime():h:mm tt}";
        }
    }

    /// <summary>
    /// The read-only banner text for "a live TermPoint on THIS computer holds the write lock" (spec item
    /// 4b). Names the person editing and tells the reader what to do if that window has stopped
    /// responding. Only another operating-system user of the same computer can reach this state now — the
    /// same user's second copy is refused by the single-instance guard before it gets this far.
    /// </summary>
    /// <param name="holderUsername">
    /// The lock holder's user name; null or blank when unknown (the lock was re-read and is gone), which
    /// falls back to the older wording that does not name anyone.
    /// </param>
    /// <returns>The banner / tooltip text.</returns>
    public static string SameComputerReadOnlyMessage(string? holderUsername)
        => string.IsNullOrWhiteSpace(holderUsername)
            ? "Read-only — another TermPoint window is already editing this database on this computer."
            : $"Read-only — {holderUsername} is already editing this database in TermPoint on this computer. " +
              "If that window has stopped responding, it can be ended in Task Manager.";

    /// <summary>
    /// True when the read-only banner should offer an "Open Microsoft Store" button: the database
    /// opened read-only because its <c>MinimumVersionToWrite</c> stamp is newer than this app AND
    /// this copy of TermPoint was installed from the Microsoft Store, so the Store is where the
    /// update comes from. Always false in the browser demo. Read once when the banner is bound
    /// (the checkout that sets <c>RequiredAppVersion</c> precedes view-model creation).
    /// </summary>
    public bool ShowOpenStoreButton
#if !BROWSER
        => App.Checkout.RequiredAppVersion is not null
           && PlatformCapabilities.InstallChannel == InstallChannel.Store;
#else
        => false;
#endif

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
            // No existence probe — File.Exists on a dead network share blocks the
            // UI thread for the full SMB redirector timeout per entry. Opening a
            // stale entry routes through deadline-bounded validation instead.
            var capturedPath = path;
            RecentDatabases.Add(new RecentDatabaseItem
            {
                Path = path,
                DisplayName = Path.GetFileName(path),
                OpenCommand = new AsyncRelayCommand(() => OpenRecentDatabase(capturedPath))
            });
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
        TourRunner tourRunner,
        FocusViewModel focusVm)
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
        FocusVm               = focusVm;
        TourOverlayVm         = new TourOverlayViewModel(tourRunner);

        // Wire the Room Availability Browser ghost block callback
        sectionListVm._setGhostBlocks = ghosts => _scheduleGridVm.SetGhostBlocks(ghosts);
        meetingListVm._setGhostBlocks = ghosts => _scheduleGridVm.SetGhostBlocks(ghosts);

        // Re-raise IsAnyPanelEditing when either child editor opens or closes.
        sectionListVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SectionListViewModel.IsEditing)) return;
            OnPropertyChanged(nameof(IsAnyPanelEditing));
            RefreshImportSectionsAvailability();   // an open section editor blocks Import Sections
        };
        meetingListVm.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(MeetingListViewModel.IsEditing)) OnPropertyChanged(nameof(IsAnyPanelEditing)); };

        // Import Sections is unavailable while several semesters (or none) are shown.
        semesterContext.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(SemesterContext.SelectedSemesters)) RefreshImportSectionsAvailability(); };

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
        RefreshImportSectionsAvailability();
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
    /// Opens the Microsoft Store's Downloads and updates page (<see cref="MinimumVersionToWrite.StoreUri"/>),
    /// where the user clicks "Get updates" to install the update the version-gated read-only
    /// banner asks for. Bound to the banner's "Open Microsoft Store"
    /// button (visible via <see cref="ShowOpenStoreButton"/>). A failure to launch the Store is
    /// logged and otherwise ignored — the banner text already tells the user how to update.
    /// </summary>
    [RelayCommand]
    private void OpenMicrosoftStore()
    {
        try
        {
            PlatformProcess.OpenUri(MinimumVersionToWrite.StoreUri);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"MainWindowViewModel: could not open the Microsoft Store: {ex.Message}");
        }
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

    /// <summary>
    /// True when the session should refresh by re-copying D to D'': it is a read-only (snapshot) session. Decided by
    /// the checkout <see cref="CheckoutService.Mode"/> alone. <see cref="CheckoutService.IsReadOnlyMode"/> is NOT
    /// used: it is set when a snapshot is made but never cleared when the same window later switches to write
    /// access, so a former reader would otherwise be treated as a reader forever.
    /// </summary>
    /// <param name="mode">The current <see cref="CheckoutService.Mode"/>.</param>
    /// <returns>True for <see cref="CheckoutMode.ReadOnly"/>.</returns>
    internal static bool RefreshesReadOnlySnapshot(CheckoutMode mode) => mode == CheckoutMode.ReadOnly;
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
        // If in read-only snapshot mode, re-copy D → D'' to fetch latest data (decided by Mode — see the helper).
        if (RefreshesReadOnlySnapshot(App.Checkout.Mode))
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
                    dbContext?.ReinitializeConnection(App.Checkout.WorkingPath, readOnly: true);
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

    /// <summary>
    /// Why Files, Import Sections… is unavailable right now (read-only, no semester shown, several shown, or a
    /// section open for editing), or null when it is available. The menu item shows it as its tooltip while
    /// disabled. The rule is <see cref="ImportAvailability"/>, the same one the flyout's Import button uses.
    /// Re-raised whenever <see cref="IsWriteEnabled"/>, the shown semesters or <c>SectionListVm.IsEditing</c> change.
    /// </summary>
    public string? ImportSectionsUnavailableReason => ImportAvailability.WhyUnavailable(
        IsWriteEnabled, SemesterContext.SelectedSemesters.Count, SectionListVm.IsEditing);

    /// <summary>
    /// True when <see cref="NavigateToImportSectionsCommand"/> may run: the platform has file dialogs (never in
    /// the browser demo) and <see cref="ImportSectionsUnavailableReason"/> is null.
    /// </summary>
    private bool CanNavigateToImportSections() =>
        PlatformCapabilities.SupportsFileDialogs && ImportSectionsUnavailableReason is null;

    /// <summary>
    /// Opens the Import Sections flyout (Files, Import Sections…). Desktop only: in the browser build the
    /// command exists so the menu can bind to it, but it is never executable.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNavigateToImportSections))]
    private void NavigateToImportSections()
    {
#if !BROWSER
        OpenFlyout<ImportSectionsViewModel>("Import Sections");
#endif
    }

    /// <summary>
    /// Re-evaluates <see cref="ImportSectionsUnavailableReason"/> and the command's enabled state. Called
    /// whenever write access, the shown semesters or section editing changes.
    /// </summary>
    private void RefreshImportSectionsAvailability()
    {
        OnPropertyChanged(nameof(ImportSectionsUnavailableReason));
        NavigateToImportSectionsCommand.NotifyCanExecuteChanged();
    }

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
    /// <summary>
    /// Restores <paramref name="backupDbPath"/> over the open database. Wired to the Save &amp; Backup
    /// flyout's Restore buttons (and the Configuration shortcut in the More menu) after the user has
    /// confirmed. This is only a forward: the work is window-lifecycle work — it saves the user's changes
    /// first, runs the shared safe restore (<see cref="DatabaseRestore"/>) under a wait dialog, and on
    /// success closes and restarts the application — so it lives in
    /// <see cref="MainWindow.RestoreFromBackupAsync"/>, like <see cref="MainWindow.SwitchDatabaseAsync"/>.
    /// A failed restore leaves the database unchanged, tells the user, and the session simply continues.
    /// </summary>
    /// <param name="backupDbPath">Full path of the backup .db file to restore.</param>
    private Task RestoreFromBackupAsync(string backupDbPath)
        => MainWindowReference?.RestoreFromBackupAsync(backupDbPath) ?? Task.CompletedTask;
#endif

    // ── Dev Tools ─────────────────────────────────────────────────────────
    // Toggled by Ctrl+Shift+D (see MainWindow.axaml.cs). Session-only, not persisted.

    [ObservableProperty] private bool _isDevToolsVisible;

#if !BROWSER
    [RelayCommand]
    private void NavigateToCsvImport() => OpenFlyout<CsvImportViewModel>("CSV Import");

    [RelayCommand]
    private void OpenDebug() => OpenFlyout<DebugTestDataViewModel>("Debug: Generate Test Data");

    [RelayCommand]
    private void OpenMigration() => OpenFlyout<MigrationViewModel>("Migration: CSV → JSON (one-time utility)");
#endif

#if !BROWSER
    // ── File menu commands ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDatabase()
    {
        if (MainWindowReference is null) return;

        // Yield so the menu popup closes and disposes BEFORE the native picker runs.
        // The picker poisons the Avalonia 12 compositor; if the popup is already gone,
        // its dispose can't deadlock (WinUiCompositedWindow.Dispose vs compositor thread).
        await Task.Yield();

        var files = await MainWindowReference.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Open existing database file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TermPoint Database") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" } },
                new FilePickerFileType("All Files")          { Patterns = new[] { "*" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await MainWindowReference.SwitchDatabaseAsync(path);
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

        // File → New is the ONLY caller allowed to create a missing database file; every other
        // SwitchDatabaseAsync caller (Open, Recent…, Switch to edit mode, post-wizard) leaves
        // createIfMissing at its default of false, so a moved/deleted file is reported, not recreated.
        vm.SwitchDatabaseAsync = path => MainWindowReference.SwitchDatabaseAsync(path, createIfMissing: true);

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

            // Reachability-gated so a last-used folder on a dead share can't stall the
            // UI thread before the picker opens (TryGetReachableStartFolderAsync).
            var suggestedFolder = await window.StorageProvider
                .TryGetReachableStartFolderAsync(Path.GetDirectoryName(settings.LastExportPath));

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
