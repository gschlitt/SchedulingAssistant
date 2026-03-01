using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public DebugTestDataViewModel(
        SectionListViewModel sectionListVm,
        MainWindowViewModel mainWindowVm,
        DebugTestDataGenerator generator)
    {
        _sectionListVm = sectionListVm;
        _mainWindowVm = mainWindowVm;
        _generator = generator;
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
    private void Cancel()
    {
        _mainWindowVm.CloseFlyoutCommand.Execute(null);
    }
#endif
}
