using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;

namespace TermPoint.ViewModels;

/// <summary>
/// One row of the Support panel's log list: a day's log file and whether it goes into the zip.
/// </summary>
public partial class SupportLogItemViewModel : ObservableObject
{
    /// <summary>Whether this log goes into the zip.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Creates the row.</summary>
    /// <param name="log">The log file.</param>
    /// <param name="label">The text shown beside the tick box (see <see cref="SupportMessages.LogLabel"/>).</param>
    public SupportLogItemViewModel(SupportLogFile log, string label)
    {
        Log = log;
        Label = label;
    }

    /// <summary>The log file this row stands for.</summary>
    public SupportLogFile Log { get; }

    /// <summary>The text shown beside the tick box, e.g. "Wednesday, November 11, 2026 (today) · 42 KB".</summary>
    public string Label { get; }
}

/// <summary>
/// The Support panel (Dev Tools → Support…, desktop only). It lets a user who has emailed about a problem do two
/// things, both voluntary and neither sending anything from the app:
/// <list type="bullet">
///   <item><b>Save logs.</b> Tick one or more daily logs and save them as one zip (the dialog starts in Downloads),
///   to attach to their email.</item>
///   <item><b>Show the Support ID.</b> The anonymous install ID that every BugSnag report carries
///   (<see cref="AppSettings.InstallId"/>, stamped by <see cref="ScrubbingDelivery"/>), so the developer can match
///   reports to the email. One per computer (per settings file).</item>
/// </list>
/// Nothing here reads or writes a database.
/// </summary>
public partial class SupportViewModel : ViewModelBase
{
    private readonly ISupportPlatform _platform;
    private readonly Func<string> _readInstallId;
    private readonly Func<DateTime> _today;

    /// <summary>The daily logs, newest first; the newest is ticked when the panel opens.</summary>
    public ObservableCollection<SupportLogItemViewModel> Logs { get; } = new();

    /// <summary>True when there is at least one log to offer.</summary>
    public bool HasLogs => Logs.Count > 0;

    /// <summary>The result of the last save, shown under the Save button; null before the first save.</summary>
    [ObservableProperty] private string? _logsStatus;

    /// <summary>True when <see cref="LogsStatus"/> reports a problem (shown in the error colour).</summary>
    [ObservableProperty] private bool _isLogsStatusError;

    /// <summary>The zip saved by the last successful save; null until then. Drives "Show in folder".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedZip))]
    private string? _savedZipPath;

    /// <summary>True once a zip has been saved, so "Show in folder" can be offered.</summary>
    public bool HasSavedZip => SavedZipPath is not null;

    /// <summary>The Support ID, once "Show Support ID" has been pressed; null before.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSupportIdShown))]
    private string? _supportId;

    /// <summary>True once the Support ID is showing.</summary>
    public bool IsSupportIdShown => SupportId is not null;

    /// <summary>The result of the last Copy, shown beside the ID; null before.</summary>
    [ObservableProperty] private string? _supportIdStatus;

    /// <summary>Creates the panel for the app (real log folder, real install ID, today's date).</summary>
    /// <param name="platform">The Save dialog, clipboard and file manager.</param>
    public SupportViewModel(ISupportPlatform platform)
        : this(platform, SupportLogs.LogFolder, () => AppSettings.Current.InstallId, () => DateTime.Today)
    {
    }

    /// <summary>Creates the panel over the given log folder, install ID and clock (tests use this one).</summary>
    /// <param name="platform">The Save dialog, clipboard and file manager.</param>
    /// <param name="logFolder">The folder to list logs from.</param>
    /// <param name="readInstallId">Reads the install ID.</param>
    /// <param name="today">Today's date, for the "(today)" mark and the zip's suggested name.</param>
    internal SupportViewModel(ISupportPlatform platform, string logFolder, Func<string> readInstallId,
        Func<DateTime> today)
    {
        _platform = platform;
        _readInstallId = readInstallId;
        _today = today;

        foreach (var log in SupportLogs.List(logFolder))
            Logs.Add(new SupportLogItemViewModel(log, SupportMessages.LogLabel(log, _today())));
        if (Logs.Count > 0)
            Logs[0].IsSelected = true;
    }

    /// <summary>
    /// Asks where to save the ticked logs (starting in Downloads) and writes them there as one zip. Reports the
    /// result in <see cref="LogsStatus"/>; a cancelled dialog changes nothing.
    /// </summary>
    [RelayCommand]
    private async Task SaveLogs()
    {
        var chosen = Logs.Where(l => l.IsSelected).Select(l => l.Log.Path).ToList();
        if (chosen.Count == 0)
        {
            ShowLogsStatus(SupportMessages.NothingTicked, isError: true);
            return;
        }

        var zipPath = await _platform.PickZipToSaveAsync(SupportLogs.SuggestedZipName(_today()));
        if (zipPath is null) return;

        try
        {
            await Task.Run(() => SupportLogs.WriteZip(chosen, zipPath));
            SavedZipPath = zipPath;
            ShowLogsStatus(SupportMessages.Saved(zipPath), isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Logger.LogInfo($"SupportViewModel: saving the logs zip failed ({ex.GetType().Name}: {ex.Message})");
            SavedZipPath = null;
            ShowLogsStatus(SupportMessages.SaveFailed, isError: true);
        }
    }

    /// <summary>Opens the file manager with the saved zip selected, so it's easy to find when attaching it.</summary>
    [RelayCommand]
    private void ShowInFolder()
    {
        if (SavedZipPath is null) return;
        try
        {
            _platform.ShowInFolder(SavedZipPath);
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"SupportViewModel: showing the zip in its folder failed ({ex.GetType().Name}: {ex.Message})");
            ShowLogsStatus(SupportMessages.ShowInFolderFailed(SavedZipPath), isError: true);
        }
    }

    /// <summary>Shows the Support ID (the anonymous install ID BugSnag reports carry).</summary>
    [RelayCommand]
    private void ShowSupportId()
    {
        var id = _readInstallId();
        SupportId = string.IsNullOrWhiteSpace(id) ? SupportMessages.NoSupportId : id;
    }

    /// <summary>Copies the Support ID to the clipboard.</summary>
    [RelayCommand]
    private async Task CopySupportId()
    {
        if (SupportId is null) return;
        var copied = await _platform.SetClipboardTextAsync(SupportId);
        SupportIdStatus = copied ? SupportMessages.Copied : SupportMessages.CopyFailed;
    }

    /// <summary>Sets the save result line and its colour together.</summary>
    /// <param name="text">The message.</param>
    /// <param name="isError">True for a problem.</param>
    private void ShowLogsStatus(string text, bool isError)
    {
        LogsStatus = text;
        IsLogsStatusError = isError;
    }
}

/// <summary>
/// Every sentence the Support panel shows that is built in code (the fixed text is in <c>SupportView.axaml</c>).
/// Kept in one place so the wording is easy to review and to pin in tests.
/// </summary>
public static class SupportMessages
{
    /// <summary>Shown when Save is pressed with no day ticked.</summary>
    public const string NothingTicked = "Tick at least one day first.";

    /// <summary>Shown when the zip couldn't be written.</summary>
    public const string SaveFailed = "The logs couldn't be saved there. Try again and choose another folder, such as Downloads.";

    /// <summary>Shown in place of the ID in the unexpected case that this computer has none.</summary>
    public const string NoSupportId = "Not available yet. Close TermPoint, open it again, and try once more.";

    /// <summary>Shown after the ID was copied.</summary>
    public const string Copied = "Copied. Paste it into your email.";

    /// <summary>Shown when there was no clipboard to copy to.</summary>
    public const string CopyFailed = "It couldn't be copied. Select the number and copy it instead.";

    /// <summary>The line after a successful save, naming the file and its folder (no full path).</summary>
    /// <param name="zipPath">Where the zip was saved.</param>
    /// <returns>The message.</returns>
    public static string Saved(string zipPath)
        => $"Saved as \"{Path.GetFileName(zipPath)}\" in your {FolderName(zipPath)} folder. Attach it to your email.";

    /// <summary>The line when the file manager couldn't be opened: says where to look instead.</summary>
    /// <param name="zipPath">Where the zip was saved.</param>
    /// <returns>The message.</returns>
    public static string ShowInFolderFailed(string zipPath)
        => $"The folder couldn't be opened. The file is \"{Path.GetFileName(zipPath)}\" in your {FolderName(zipPath)} folder.";

    /// <summary>
    /// The text beside a log's tick box: its day written out, "(today)" for today's, and its size,
    /// e.g. "Wednesday, November 11, 2026 (today) · 42 KB".
    /// </summary>
    /// <param name="log">The log file.</param>
    /// <param name="today">Today's date.</param>
    /// <returns>The label.</returns>
    public static string LogLabel(SupportLogFile log, DateTime today)
    {
        var day = log.Day.ToString("D", CultureInfo.CurrentCulture);
        var todayMark = log.Day.Date == today.Date ? " (today)" : string.Empty;
        return $"{day}{todayMark} · {Size(log.SizeBytes)}";
    }

    /// <summary>A file size in whole KB (at least 1) or, from 1 MB, MB with one decimal.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The size as text.</returns>
    internal static string Size(long bytes)
        => bytes >= 1024 * 1024
            ? (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
            : Math.Max(1, (bytes + 1023) / 1024) + " KB";

    /// <summary>The name of the folder holding <paramref name="path"/> (e.g. "Downloads").</summary>
    /// <param name="path">A full file path.</param>
    /// <returns>The folder's name, or the folder itself for a drive root.</returns>
    private static string FolderName(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? folder : name;
    }
}
