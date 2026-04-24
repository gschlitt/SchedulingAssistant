using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.Management;

public partial class ExportViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Export flyout sidebar.</summary>
    public string DisplayName => "Export Schedule";

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
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        IsExporting = true;
        StatusMessage = null;

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

            var path = file.Path.LocalPath;

            window.ScheduleGridViewInstance?.ExportToPng(path);

            settings.LastExportPath = path;
            settings.Save();

            StatusMessage = $"Saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanExport() => !IsExporting;
}
