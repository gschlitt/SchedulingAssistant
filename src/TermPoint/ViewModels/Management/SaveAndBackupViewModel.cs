using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// ViewModel for the Save &amp; Backup flyout. Manages automated-backup configuration and restore.
/// Backup entries are loaded lazily and refreshed after every backup via subscription
/// to <see cref="BackupService.BackupCompleted"/>.
/// </summary>
public partial class SaveAndBackupViewModel : ViewModelBase, IDisposable
{
    /// <summary>Category label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Save & Backup";

    private readonly BackupService _backupService;
    private readonly IDialogService _dialogService;
    private readonly WriteLockService _lockService;

    // ── Restore availability ─────────────────────────────────────────────────

    /// <summary>
    /// True when this session holds the write lock (the person editing the database). Restore replaces the
    /// database for everyone, so it is available to the writer only: this gates <see cref="RestoreCommand"/>
    /// and with it every row's Restore button. Raised again whenever the lock state changes.
    /// </summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>The explanatory text shown above the list of backups (see <see cref="RestorePrompt.PanelDescription"/>).</summary>
    public string RestoreDescription => RestorePrompt.PanelDescription;

    /// <summary>
    /// The note shown while Restore is unavailable, i.e. when <see cref="IsWriteEnabled"/> is false
    /// (see <see cref="RestorePrompt.ReadOnlyNote"/>).
    /// </summary>
    public string RestoreUnavailableNote => RestorePrompt.ReadOnlyNote;

    // ── Autosave settings ────────────────────────────────────────────────────

    /// <summary>Interval in minutes between automatic saves when autosave is enabled.</summary>
    [ObservableProperty]
    private int _autoSaveIntervalMinutes;

    /// <summary>Persists the autosave interval immediately when changed, and restarts the timer if running.</summary>
    partial void OnAutoSaveIntervalMinutesChanged(int value)
    {
        var s = AppSettings.Current;
        s.AutoSaveIntervalMinutes = Math.Max(value, 1);
        s.Save();

        // Restart the timer so the new interval takes effect immediately.
        if (s.AutoSaveEnabled)
        {
            App.Checkout.StopAutoSave();
            App.Checkout.StartAutoSave();
        }
    }

    // ── Backup settings ──────────────────────────────────────────────────────

    /// <summary>Folder path where automated backups are written. Empty string = not configured.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackupConfigured))]
    private string _backupFolderPath = string.Empty;

    /// <summary>Interval in minutes between periodic session backups.</summary>
    [ObservableProperty]
    private int _backupIntervalMinutes;

    /// <summary>Maximum number of backup pairs to retain.</summary>
    [ObservableProperty]
    private int _maxBackupCount;

    /// <summary>Status text from the most recent backup attempt.</summary>
    [ObservableProperty]
    private string _lastBackupStatus = "No backup has been performed this session.";

    /// <summary>True when a backup folder is configured and the service can back up.</summary>
    public bool IsBackupConfigured => !string.IsNullOrWhiteSpace(BackupFolderPath);

    /// <summary>True when the backup entry list is empty (drives the empty-state label in the view).</summary>
    public bool HasNoBackups => BackupEntries.Count == 0;

    /// <summary>Backup entries for the current database, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoBackups))]
    private List<BackupEntryViewModel> _backupEntries = new();

    /// <summary>
    /// Injected by <see cref="MainWindowViewModel"/> when opening the Settings flyout.
    /// Called with the full path of the selected backup .db file, after the user has confirmed.
    /// The callee owns the whole operation (<c>MainWindow.RestoreFromBackupAsync</c>): it saves the user's
    /// current changes first, runs the safe restore (<see cref="DatabaseRestore"/>) under a wait dialog,
    /// reports any failure itself (the database is unchanged and the session simply continues), and on
    /// success closes and restarts TermPoint.
    /// </summary>
    public Func<string, Task>? RestoreCallback { get; set; }

    /// <param name="backupService">Singleton backup service.</param>
    /// <param name="dialogService">Used for backup-now error reporting and restore confirmation.</param>
    /// <param name="lockService">Write-lock state: restore is available to the writer only.</param>
    public SaveAndBackupViewModel(
        BackupService backupService,
        IDialogService dialogService,
        WriteLockService lockService)
    {
        _backupService  = backupService;
        _dialogService  = dialogService;
        _lockService    = lockService;

        // Initialise fields from persisted settings.
        var s = AppSettings.Current;
        _autoSaveIntervalMinutes = s.AutoSaveIntervalMinutes;
        _backupFolderPath        = s.BackupFolderPath ?? string.Empty;
        _backupIntervalMinutes   = s.BackupIntervalMinutes;
        _maxBackupCount          = s.MaxBackupCount;

        // Reflect the most recent backup result from this session (if any).
        UpdateLastBackupStatus();

        // Refresh the status line and entry list after every backup.
        _backupService.BackupCompleted += OnBackupCompleted;

        // Restore is writer-only: re-evaluate the buttons whenever the lock is gained or lost.
        _lockService.LockStateChanged += OnLockStateChanged;

        _ = RefreshBackupListAsync();
    }

    // ── Change handlers (auto-save on every change) ──────────────────────────

    /// <summary>Persists the backup folder path immediately when changed.</summary>
    partial void OnBackupFolderPathChanged(string value)
    {
        var s = AppSettings.Current;
        s.BackupFolderPath = string.IsNullOrWhiteSpace(value) ? null : value;
        s.Save();
        _ = RefreshBackupListAsync();
    }

    /// <summary>Persists the backup interval immediately when changed.</summary>
    partial void OnBackupIntervalMinutesChanged(int value)
    {
        var s = AppSettings.Current;
        s.BackupIntervalMinutes = Math.Max(value, 1);
        s.Save();
    }

    /// <summary>Persists the max backup count immediately when changed.</summary>
    partial void OnMaxBackupCountChanged(int value)
    {
        var s = AppSettings.Current;
        s.MaxBackupCount = Math.Max(value, 1);
        s.Save();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers an immediate backup. Called from a "Back Up Now" button in the UI.
    /// Reports any failure to the user via the error dialog.
    /// </summary>
    [RelayCommand]
    private async Task BackUpNow()
    {
        var result = await _backupService.PerformBackupAsync();
        if (!result.Success)
            await _dialogService.ShowError($"Backup failed: {result.ErrorMessage}");
    }

    /// <summary>
    /// CanExecute for <see cref="RestoreCommand"/>: only the person editing the database (the write-lock holder)
    /// may restore, because a restore replaces the database for everyone. A reader, or a licence that is
    /// read-only, gets disabled buttons.
    /// </summary>
    /// <returns>True when this session holds the write lock.</returns>
    private bool CanRestore() => _lockService.IsWriter;

    /// <summary>
    /// Initiates restoration of <paramref name="entry"/>. Refuses outright unless this session is the writer
    /// (the buttons are already disabled then; this is the second line of defence). Otherwise shows the
    /// confirmation dialog (<see cref="RestorePrompt.BuildConfirmMessage"/>) and, if confirmed, hands the
    /// backup's path to <see cref="RestoreCallback"/> (set by <see cref="MainWindowViewModel"/>), which saves
    /// the current changes, restores safely and restarts TermPoint.
    /// </summary>
    /// <param name="entry">The backup entry selected for restore.</param>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task Restore(BackupEntryViewModel entry)
    {
        if (RestoreCallback is null) return;

        if (!_lockService.IsWriter)
        {
            await _dialogService.ShowError(RestorePrompt.Describe(new RestoreResult(RestoreOutcome.NotWriter)).Message);
            return;
        }

        bool confirmed = await _dialogService.Confirm(
            RestorePrompt.BuildConfirmMessage(
                entry.TimestampDisplay,
                DatabaseNameFor(App.Checkout.SourcePath, AppSettings.Current.DatabasePath)),
            confirmLabel: RestorePrompt.ConfirmLabel);

        if (!confirmed) return;

        await RestoreCallback(entry.DbPath);
    }

    /// <summary>
    /// The database's file name without its extension — the start of the name of the "(before restore …)" copy
    /// the confirmation promises.
    /// </summary>
    /// <param name="sourcePath">The open database's path (the checkout's source); may be empty before anything is open.</param>
    /// <param name="fallbackPath">The configured database path, used when <paramref name="sourcePath"/> is empty; may be null.</param>
    /// <returns>The name, or "database" when neither path names a file.</returns>
    internal static string DatabaseNameFor(string? sourcePath, string? fallbackPath)
    {
        var path = string.IsNullOrWhiteSpace(sourcePath) ? fallbackPath : sourcePath;
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty);
        return string.IsNullOrEmpty(name) ? "database" : name;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Reloads the backup list from <see cref="BackupService.GetBackupsAsync"/>.
    /// Async so the directory probe is deadline-bounded and does not freeze the UI
    /// when the backup folder is on an unreachable network share.
    /// </summary>
    private async Task RefreshBackupListAsync()
    {
        var entries = await _backupService.GetBackupsAsync();
        BackupEntries = entries
            .Select(e => new BackupEntryViewModel(e, RestoreCommand))
            .ToList();
    }

    /// <summary>Updates <see cref="LastBackupStatus"/> from the service's last result.</summary>
    private void UpdateLastBackupStatus()
    {
        LastBackupStatus = _backupService.LastBackupResult?.StatusSummary
            ?? "No backup has been performed this session.";
    }

    private void OnBackupCompleted(object? sender, EventArgs e)
    {
        UpdateLastBackupStatus();
        _ = RefreshBackupListAsync();
    }

    /// <summary>
    /// Called when the write lock is gained or lost (on the UI thread): re-evaluates
    /// <see cref="IsWriteEnabled"/> and every Restore button.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        RestoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Unsubscribes from BackupService.BackupCompleted and the lock-state event to prevent memory leaks.</summary>
    public void Dispose()
    {
        _backupService.BackupCompleted -= OnBackupCompleted;
        _lockService.LockStateChanged -= OnLockStateChanged;
    }
}

/// <summary>
/// Display wrapper around a <see cref="BackupEntry"/> for use in the Settings backup list.
/// </summary>
public class BackupEntryViewModel
{
    private readonly BackupEntry _entry;

    /// <param name="entry">The underlying backup data.</param>
    /// <param name="restoreCommand">The command wired to the Restore button for this row.</param>
    public BackupEntryViewModel(BackupEntry entry, IRelayCommand restoreCommand)
    {
        _entry         = entry;
        RestoreCommand = restoreCommand;
    }

    /// <summary>Full path to the .db backup file. Passed to RestoreCallback on restore.</summary>
    public string DbPath => _entry.DbPath;

    /// <summary>Formatted timestamp for display, e.g. "2026-03-20  14:30:00".</summary>
    public string TimestampDisplay => _entry.TimestampDisplay;

    /// <summary>Short filename without extension, e.g. "ComputerScience_2026-03-20_14-30-00".</summary>
    public string DisplayName => _entry.DisplayName;

    /// <summary>Supplementary info line shown below the timestamp.</summary>
    public string Detail =>
        _entry.HasCsv ? "Database + sections CSV" : "Database only";

    /// <summary>Command bound to the Restore button for this row.</summary>
    public IRelayCommand RestoreCommand { get; }
}
