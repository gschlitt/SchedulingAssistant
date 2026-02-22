using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents a single scheduled meeting added to a section (one day + block length + start time).</summary>
public partial class SectionMeetingViewModel : ViewModelBase
{
    [ObservableProperty] private int _selectedDay;
    [ObservableProperty] private double? _selectedBlockLength;
    [ObservableProperty] private int? _selectedStartTime;
    [ObservableProperty] private ObservableCollection<int> _availableStartTimes = new();

    public ObservableCollection<DayOption> AvailableDays { get; } = new(
    [
        new DayOption(1, "Monday"),
        new DayOption(2, "Tuesday"),
        new DayOption(3, "Wednesday"),
        new DayOption(4, "Thursday"),
        new DayOption(5, "Friday"),
    ]);

    public ObservableCollection<double> AvailableBlockLengths { get; }

    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;

    public SectionMeetingViewModel(IReadOnlyList<LegalStartTime> legalStartTimes, SectionDaySchedule? existing = null)
    {
        _legalStartTimes = legalStartTimes;
        AvailableBlockLengths = new ObservableCollection<double>(legalStartTimes.Select(l => l.BlockLength));

        if (existing != null)
        {
            _selectedDay = existing.Day;
            double blockLengthHours = existing.DurationMinutes / 60.0;
            _selectedBlockLength = AvailableBlockLengths.FirstOrDefault(b => Math.Abs(b - blockLengthHours) < 0.01);
            RefreshStartTimes();
            _selectedStartTime = existing.StartMinutes;
        }
        else
        {
            _selectedDay = 1;
        }
    }

    partial void OnSelectedBlockLengthChanged(double? value)
    {
        RefreshStartTimes();
        SelectedStartTime = AvailableStartTimes.FirstOrDefault();
    }

    private void RefreshStartTimes()
    {
        if (SelectedBlockLength is null) { AvailableStartTimes = new(); return; }
        var legal = _legalStartTimes.FirstOrDefault(l => Math.Abs(l.BlockLength - SelectedBlockLength.Value) < 0.01);
        AvailableStartTimes = legal is null
            ? new ObservableCollection<int>()
            : new ObservableCollection<int>(legal.StartTimes);
    }

    public SectionDaySchedule? ToSchedule()
    {
        if (SelectedBlockLength is null || SelectedStartTime is null) return null;
        return new SectionDaySchedule
        {
            Day = SelectedDay,
            StartMinutes = SelectedStartTime.Value,
            DurationMinutes = (int)(SelectedBlockLength.Value * 60)
        };
    }
}

public record DayOption(int Day, string Name);
