using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents a single scheduled meeting within a section — day, time, room, and meeting type.</summary>
public partial class SectionMeetingViewModel : ViewModelBase
{
    [ObservableProperty] private int _selectedDay;
    [ObservableProperty] private double? _selectedBlockLength;
    [ObservableProperty] private int? _selectedStartTime;
    [ObservableProperty] private ObservableCollection<int> _availableStartTimes = new();
    [ObservableProperty] private string? _selectedMeetingTypeId;
    [ObservableProperty] private string? _selectedRoomId;

    public ObservableCollection<DayOption> AvailableDays { get; }
    public ObservableCollection<double> AvailableBlockLengths { get; }
    public ObservableCollection<SectionPropertyValue> MeetingTypes { get; }
    public ObservableCollection<Room> Rooms { get; }

    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;

    public SectionMeetingViewModel(
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        IReadOnlyList<SectionPropertyValue> meetingTypes,
        IReadOnlyList<Room> rooms,
        SectionDaySchedule? existing = null,
        double? defaultBlockLength = null)
    {
        _legalStartTimes = legalStartTimes;
        AvailableBlockLengths = new ObservableCollection<double>(legalStartTimes.Select(l => l.BlockLength));

        var days = new List<DayOption>
        {
            new(1, "Monday"),
            new(2, "Tuesday"),
            new(3, "Wednesday"),
            new(4, "Thursday"),
            new(5, "Friday"),
        };
        if (includeSaturday)
            days.Add(new(6, "Saturday"));
        AvailableDays = new ObservableCollection<DayOption>(days);

        // Prepend a "(none)" sentinel so the user can clear the meeting type
        var mtList = new List<SectionPropertyValue>
            { new SectionPropertyValue { Id = "", Name = "(none)" } };
        mtList.AddRange(meetingTypes);
        MeetingTypes = new ObservableCollection<SectionPropertyValue>(mtList);

        // Prepend a "(none)" sentinel for room
        var roomList = new List<Room> { new Room { Id = "", Building = "(none)", RoomNumber = "" } };
        roomList.AddRange(rooms);
        Rooms = new ObservableCollection<Room>(roomList);

        if (existing != null)
        {
            _selectedDay = existing.Day;
            double blockLengthHours = existing.DurationMinutes / 60.0;
            _selectedBlockLength = AvailableBlockLengths.FirstOrDefault(b => Math.Abs(b - blockLengthHours) < 0.01);
            RefreshStartTimes();
            _selectedStartTime = existing.StartMinutes;
            _selectedMeetingTypeId = existing.MeetingTypeId ?? "";
            _selectedRoomId = existing.RoomId ?? "";
        }
        else
        {
            _selectedDay = 1;
            _selectedMeetingTypeId = "";
            _selectedRoomId = "";

            // Apply preferred block length if set and available
            if (defaultBlockLength.HasValue
                && AvailableBlockLengths.Any(b => Math.Abs(b - defaultBlockLength.Value) < 0.01))
            {
                _selectedBlockLength = defaultBlockLength;
                RefreshStartTimes();
                // Leave SelectedStartTime unset — user still picks the time
            }
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
            DurationMinutes = (int)(SelectedBlockLength.Value * 60),
            MeetingTypeId = string.IsNullOrEmpty(SelectedMeetingTypeId) ? null : SelectedMeetingTypeId,
            RoomId = string.IsNullOrEmpty(SelectedRoomId) ? null : SelectedRoomId,
        };
    }
}

public record DayOption(int Day, string Name);
