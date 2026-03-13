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

    /// <summary>
    /// When true, exceptions passed to <see cref="IAppLogger.LogError"/> are re-thrown
    /// after being written to the log file, so they surface immediately in the debugger
    /// rather than being silently swallowed by catch blocks.
    /// Mirrors <see cref="IAppLogger.ThrowOnError"/> on the singleton logger.
    /// </summary>
    [ObservableProperty]
    private bool _throwOnError = true;

    partial void OnThrowOnErrorChanged(bool value) => App.Logger.ThrowOnError = value;

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
        // Sync the logger with the default field value — OnThrowOnErrorChanged
        // does not fire for field initializers, so we push the default manually.
        App.Logger.ThrowOnError = _throwOnError;
    }

    /// <summary>
    /// Fires a synthetic exception through <see cref="App.Logger"/> so you can verify
    /// that <see cref="ThrowOnError"/> is working as expected. When the flag is off the
    /// exception is silently logged; when on it propagates back up to the caller.
    /// </summary>
    [RelayCommand]
    private void TestThrow()
    {
        try
        {
            throw new InvalidOperationException("Test exception — ThrowOnError verification.");
        }
        catch (Exception ex)
        {
            // If ThrowOnError is true this will re-throw, which propagates out of this
            // method and will be caught by Avalonia's unhandled-exception handler or
            // break into the debugger, depending on how the IDE is configured.
            App.Logger.LogError(ex, "DebugTestDataViewModel.TestThrow");

            // Only reached when ThrowOnError is false.
            StatusMessage = "Test exception was logged (not re-thrown). Enable the checkbox to re-throw.";
        }
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
