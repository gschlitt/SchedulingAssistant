using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TermPoint.ViewModels.Management;

public partial class ExportViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Export flyout sidebar.</summary>
    public string DisplayName => "Print Schedule";

    private readonly MainWindowViewModel _mainVm;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSchedulePngCommand))]
    private bool _isExporting;

    public ExportViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportSchedulePng()
    {
        if (!PlatformCapabilities.SupportsFileDialogs)
        {
            StatusMessage = "File export is not available in the browser demo.";
            return;
        }
        // The await below is unreachable on desktop but satisfies the async signature
        // in WASM builds where the #if !BROWSER block is excluded entirely.
        await Task.CompletedTask;
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        IsExporting = true;
        StatusMessage = null;

        try
        {
            var settings = AppSettings.Current;

            // Reachability-gated so a last-used folder on a dead share can't stall the
            // UI thread before the picker opens (TryGetReachableStartFolderAsync).
            var suggestedFolder = await window.StorageProvider
                .TryGetReachableStartFolderAsync(Path.GetDirectoryName(settings.LastExportPath));

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Schedule PNG",
                SuggestedFileName = "schedule.png",
                DefaultExtension = "png",
                SuggestedStartLocation = suggestedFolder,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (file is null) return;

            // Render on the UI thread (it must), then write the bytes under the network deadline: the folder may be a
            // share that stopped answering (1.2.4 item 4.10, A8).
            var png = window.ScheduleGridViewInstance?.ExportToPngBytes();
            if (png is null)
            {
                StatusMessage = "There is no schedule to export yet.";
                return;
            }

            var result = await ExportFileWriter.WriteAsync(file, png);
            if (!result.Succeeded)
            {
                StatusMessage = result.Message;
                return;
            }

            // Remember the folder only when the file was actually written.
            settings.LastExportPath = file.Path.LocalPath;
            settings.Save();

            StatusMessage = $"Saved: {result.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
#endif
    }

    private bool CanExport() => !IsExporting;
}
