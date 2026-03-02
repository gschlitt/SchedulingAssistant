using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;

namespace SchedulingAssistant.ViewModels;

public partial class DebugTestDataViewModel : ViewModelBase
{
#if DEBUG
    [ObservableProperty] private int _sectionCount = 10;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isGenerating;

    private readonly SectionListViewModel _sectionListVm;
    private readonly MainWindowViewModel _mainWindowVm;
    private readonly DebugTestDataGenerator _generator;
    private readonly AcademicYearRepository _ayRepo;
    private readonly LegalStartTimeRepository _startTimeRepo;
    private readonly LegalStartTimesDataExporter _exporter;

    public DebugTestDataViewModel(
        SectionListViewModel sectionListVm,
        MainWindowViewModel mainWindowVm,
        DebugTestDataGenerator generator,
        AcademicYearRepository ayRepo,
        LegalStartTimeRepository startTimeRepo,
        LegalStartTimesDataExporter exporter)
    {
        _sectionListVm = sectionListVm;
        _mainWindowVm = mainWindowVm;
        _generator = generator;
        _ayRepo = ayRepo;
        _startTimeRepo = startTimeRepo;
        _exporter = exporter;
    }

    [RelayCommand]
    private void Generate()
    {
        if (SectionCount <= 0)
        {
            StatusMessage = "Count must be greater than 0.";
            return;
        }

        try
        {
            IsGenerating = true;
            StatusMessage = $"Generating {SectionCount} sections...";

            // Call the command on SectionListViewModel
            _sectionListVm.GenerateRandomSectionsCommand.Execute(SectionCount);

            StatusMessage = $"Successfully generated {SectionCount} test sections.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            App.Logger.LogError(ex, "DebugTestDataViewModel.Generate");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void ExportStartTimes()
    {
        try
        {
            IsGenerating = true;
            StatusMessage = "Exporting legal start times...";

            // Export to embedded resource location
            var exportPath = LegalStartTimesDataStore.GetEmbeddedDataPath();
            var directory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _exporter.ExportAndSaveAll(exportPath);

            StatusMessage = $"✓ Exported to: {exportPath}\n\nUsers can now import this configuration when opening a new database.";
            App.Logger.LogInfo($"Exported legal start times to {exportPath}", "ExportStartTimes");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error exporting: {ex.Message}";
            App.Logger.LogError(ex, "DebugTestDataViewModel.ExportStartTimes");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _mainWindowVm.CloseFlyoutCommand.Execute(null);
    }
#endif
}
