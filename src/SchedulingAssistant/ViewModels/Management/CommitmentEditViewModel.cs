using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class CommitmentEditViewModel : ViewModelBase
{
    public record DayOption(int Day, string Display);
    public record TimeOption(int Minutes, string Display);

    private readonly Action _onCancel;
    private readonly Func<InstructorCommitment, Task> _onSave;
    private readonly InstructorCommitment _originalCommitment;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private DayOption _selectedDayOption = null!;
    [ObservableProperty] private TimeOption _selectedStartOption = null!;
    [ObservableProperty] private TimeOption _selectedEndOption = null!;
    [ObservableProperty] private int _selectedStartMinutes;
    [ObservableProperty] private int _selectedEndMinutes;
    [ObservableProperty] private string? _validationError;

    public IReadOnlyList<DayOption> DayOptions { get; } = new[]
    {
        new DayOption(1, "Monday"),
        new DayOption(2, "Tuesday"),
        new DayOption(3, "Wednesday"),
        new DayOption(4, "Thursday"),
        new DayOption(5, "Friday"),
        new DayOption(6, "Saturday")
    };

    public IReadOnlyList<TimeOption> AllTimeOptions { get; } = BuildTimeOptions();

    [ObservableProperty] private ObservableCollection<TimeOption> _startTimeOptions = new();
    [ObservableProperty] private ObservableCollection<TimeOption> _endTimeOptions = new();

    public CommitmentEditViewModel(
        InstructorCommitment commitment,
        Action onCancel,
        Func<InstructorCommitment, Task> onSave)
    {
        _onCancel = onCancel;
        _onSave = onSave;
        _originalCommitment = commitment;

        Name = commitment.Name;
        SelectedDayOption = DayOptions.First(d => d.Day == commitment.Day);
        SelectedStartOption = AllTimeOptions.First(t => t.Minutes == commitment.StartMinutes);
        SelectedStartMinutes = commitment.StartMinutes;

        // Initialize start/end time options
        UpdateStartTimeOptions();
        UpdateEndTimeOptions();
        SelectedEndOption = AllTimeOptions.First(t => t.Minutes == commitment.EndMinutes);
        SelectedEndMinutes = commitment.EndMinutes;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(SelectedStartOption))
                ValidationError = null;

            if (e.PropertyName == nameof(SelectedStartOption))
                UpdateStartTimeOptions();
        };
    }

    private void UpdateStartTimeOptions()
    {
        StartTimeOptions = new ObservableCollection<TimeOption>(AllTimeOptions);
    }

    private void UpdateEndTimeOptions()
    {
        if (SelectedStartOption == null) return;

        var validEndTimes = AllTimeOptions
            .Where(t => t.Minutes > SelectedStartOption.Minutes)
            .ToList();
        EndTimeOptions = new ObservableCollection<TimeOption>(validEndTimes);

        // Ensure SelectedEndOption is still valid, or select the first valid one
        if (SelectedEndOption == null || !EndTimeOptions.Contains(SelectedEndOption))
            SelectedEndOption = EndTimeOptions.FirstOrDefault() ?? AllTimeOptions.Last();
    }

    partial void OnSelectedStartOptionChanged(TimeOption value)
    {
        if (value != null)
        {
            SelectedStartMinutes = value.Minutes;
            UpdateEndTimeOptions();
        }
    }

    partial void OnSelectedStartMinutesChanged(int value)
    {
        var option = AllTimeOptions.FirstOrDefault(t => t.Minutes == value);
        if (option != null)
        {
            SelectedStartOption = option;
            UpdateEndTimeOptions();
        }
    }

    partial void OnSelectedEndMinutesChanged(int value)
    {
        var option = AllTimeOptions.FirstOrDefault(t => t.Minutes == value);
        if (option != null)
            SelectedEndOption = option;
    }

    [RelayCommand]
    private async Task Save()
    {
        ValidationError = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name is required.";
            return;
        }

        if (SelectedEndMinutes <= SelectedStartMinutes)
        {
            ValidationError = "End time must be after start time.";
            return;
        }

        var commitment = new InstructorCommitment
        {
            Id = _originalCommitment.Id,
            Name = Name.Trim(),
            InstructorId = _originalCommitment.InstructorId,
            SemesterId = _originalCommitment.SemesterId,
            Day = SelectedDayOption.Day,
            StartMinutes = SelectedStartMinutes,
            EndMinutes = SelectedEndMinutes
        };

        await _onSave(commitment);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    private static IReadOnlyList<TimeOption> BuildTimeOptions()
    {
        var options = new List<TimeOption>();
        // Use the configured grid time range, clamped to start no later than 08:00 so that
        // common early-morning commitments (e.g. 08:00 office hours) are always available
        // even if the grid starts at 08:30.
        int start = Math.Min(AppSettings.Current.GridStartMinutes, 8 * 60);
        int end   = AppSettings.Current.GridEndMinutes;
        for (int minutes = start; minutes <= end; minutes += 30)
        {
            var hours = minutes / 60;
            var mins = minutes % 60;
            options.Add(new TimeOption(minutes, $"{hours:D2}:{mins:D2}"));
        }
        return options.AsReadOnly();
    }
}
