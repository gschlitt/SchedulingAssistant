using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Settings flyout. Manages automated-backup configuration and restore.
/// Backup entries are loaded lazily and refreshed after every backup via subscription
/// to <see cref="BackupService.BackupCompleted"/>.
/// </summary>
public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly BackupService _backupService;
    private readonly IDialogService _dialogService;

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
    /// Called with the full path of the selected backup .db file.
    /// The caller is responsible for the close-connection / copy-file / restart sequence.
    /// </summary>
    public Func<string, Task>? RestoreCallback { get; set; }

    /// <param name="backupService">Singleton backup service.</param>
    /// <param name="dialogService">Used for backup-now error reporting and restore confirmation.</param>
    public SettingsViewModel(
        BackupService backupService,
        IDialogService dialogService)
    {
        _backupService  = backupService;
        _dialogService  = dialogService;

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

        RefreshBackupList();
    }

    // ── Change handlers (auto-save on every change) ──────────────────────────

    /// <summary>Persists the backup folder path immediately when changed.</summary>
    partial void OnBackupFolderPathChanged(string value)
    {
        var s = AppSettings.Current;
        s.BackupFolderPath = string.IsNullOrWhiteSpace(value) ? null : value;
        s.Save();
        RefreshBackupList();
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
    /// Initiates restoration of <paramref name="entry"/>. Shows a confirmation dialog,
    /// then delegates to <see cref="RestoreCallback"/> (set by <see cref="MainWindowViewModel"/>)
    /// which handles closing the connection, overwriting the database file, and restarting.
    /// </summary>
    /// <param name="entry">The backup entry selected for restore.</param>
    [RelayCommand]
    private async Task Restore(BackupEntryViewModel entry)
    {
        if (RestoreCallback is null) return;

        bool confirmed = await _dialogService.Confirm(
            $"Restore from backup:\n\n{entry.TimestampDisplay}\n\n" +
            "This will replace the current database with the selected backup. " +
            "The application will restart automatically.\n\n" +
            "Continue?",
            confirmLabel: "Restore");

        if (!confirmed) return;

        await RestoreCallback(entry.DbPath);
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>Reloads the backup list from <see cref="BackupService.GetBackups"/>.</summary>
    private void RefreshBackupList()
    {
        BackupEntries = _backupService
            .GetBackups()
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
        RefreshBackupList();
    }

    /// <summary>Unsubscribes from BackupService.BackupCompleted to prevent memory leaks.</summary>
    public void Dispose()
    {
        _backupService.BackupCompleted -= OnBackupCompleted;
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
