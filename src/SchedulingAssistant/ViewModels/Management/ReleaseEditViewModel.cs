using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class ReleaseEditViewModel : ViewModelBase
{
    private readonly Action _onCancel;
    private readonly Func<Release, Task> _onSave;
    private readonly Release _originalRelease;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _workloadText = string.Empty;
    [ObservableProperty] private string? _validationError;

    public ReleaseEditViewModel(
        Release release,
        Action onCancel,
        Func<Release, Task> onSave)
    {
        _onCancel = onCancel;
        _onSave = onSave;
        _originalRelease = release;

        Title = release.Title;
        WorkloadText = release.WorkloadValue.ToString("F2");

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Title) || e.PropertyName == nameof(WorkloadText))
                ValidationError = null;
        };
    }

    [RelayCommand]
    private async Task Save()
    {
        ValidationError = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationError = "Title is required.";
            return;
        }

        if (!decimal.TryParse(WorkloadText, out var workload) || workload < 0)
        {
            ValidationError = "Workload must be a non-negative number.";
            return;
        }

        var release = new Release
        {
            Id = _originalRelease.Id,
            Title = Title.Trim(),
            WorkloadValue = workload,
            InstructorId = _originalRelease.InstructorId,
            SemesterId = _originalRelease.SemesterId
        };

        await _onSave(release);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
