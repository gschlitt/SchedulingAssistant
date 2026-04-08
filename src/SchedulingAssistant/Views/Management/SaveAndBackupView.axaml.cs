using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.Views.Management;

/// <summary>
/// Code-behind for <see cref="SaveAndBackupView"/>. Kept minimal — only the folder-picker
/// button handler lives here, because the Avalonia StorageProvider must be reached from
/// the view layer and cannot be called directly from a ViewModel.
/// </summary>
public partial class SaveAndBackupView : UserControl
{
    public SaveAndBackupView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens an OS folder-picker so the user can choose where backups are stored.
    /// Updates <see cref="SaveAndBackupViewModel.BackupFolderPath"/> directly on the ViewModel,
    /// which auto-saves to <see cref="Services.AppSettings"/> via its property-change handler.
    /// </summary>
    private async void OnBrowseBackupFolder(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        // Pre-select the current folder if it exists on disk.
        IStorageFolder? suggestedFolder = null;
        if (DataContext is SaveAndBackupViewModel vm
            && !string.IsNullOrWhiteSpace(vm.BackupFolderPath)
            && Directory.Exists(vm.BackupFolderPath))
        {
            suggestedFolder = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(vm.BackupFolderPath);
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title                  = "Choose backup folder",
                AllowMultiple          = false,
                SuggestedStartLocation = suggestedFolder
            });

        if (folders.Count > 0 && DataContext is SaveAndBackupViewModel settingsVm)
        {
            // TryGetLocalPath() handles drive roots (e.g. E:\) and other edge cases
            // that cause Uri.LocalPath to throw InvalidOperationException.
            var localPath = folders[0].TryGetLocalPath();
            if (localPath is not null)
                settingsVm.BackupFolderPath = localPath;
        }
    }
}
