using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>Represents a single scheduled meeting within a section — day, time, room, meeting type, and frequency.</summary>
public partial class SectionMeetingViewModel : ViewModelBase
{
    // ── Frequency options ─────────────────────────────────────────────────────

    /// <summary>
    /// The four choices presented in the Frequency dropdown.
    /// "Weekly" maps to a null stored value (no annotation).
    /// "Odd weeks", "Even weeks", and "Custom" map to "odd", "even", and a
    /// comma-separated week list respectively.
    /// </summary>
    public IReadOnlyList<string> FrequencyOptions { get; } =
        ["Weekly", "Odd weeks", "Even weeks", "Custom"];

    [ObservableProperty] private string _selectedFrequencyOption = "Weekly";
    [ObservableProperty] private string _customWeeksText = string.Empty;
    [ObservableProperty] private string? _customWeeksError;

    /// <summary>True when the "Custom" frequency option is selected, causing the Weeks text field to appear.</summary>
    public bool IsCustomFrequency => SelectedFrequencyOption == "Custom";

    // ── Existing fields ───────────────────────────────────────────────────────

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

    /// <param name="legalStartTimes">Academic-year legal start times used to populate the Start dropdown.</param>
    /// <param name="includeSaturday">Whether Saturday should appear as a day option.</param>
    /// <param name="meetingTypes">Available meeting type property values.</param>
    /// <param name="rooms">Available rooms for selection.</param>
    /// <param name="existing">If non-null, the form is populated from this existing schedule entry.</param>
    /// <param name="defaultBlockLength">Pre-selected block length when adding a new meeting.</param>
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
            LoadFrequency(existing.Frequency);
        }
        else
        {
            _selectedDay = 1;
            // null (not "") so that Avalonia's SelectedValue+SelectedValueBinding resolution
            // during ComboBox initialization does not fire a spurious PropertyChanged — which
            // would otherwise consume the pattern-coupling slot before the user picks anything.
            _selectedMeetingTypeId = null;
            _selectedRoomId = "";
            _selectedFrequencyOption = "Weekly";

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

    // ── Frequency helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Populates <see cref="SelectedFrequencyOption"/> and <see cref="CustomWeeksText"/>
    /// from a stored frequency string. Called from the constructor when loading an existing meeting.
    /// </summary>
    /// <param name="frequency">The raw stored frequency value (null/"", "odd", "even", or "1,6,7" etc.).</param>
    private void LoadFrequency(string? frequency)
    {
        if (string.IsNullOrEmpty(frequency))
        {
            _selectedFrequencyOption = "Weekly";
        }
        else if (frequency == "odd")
        {
            _selectedFrequencyOption = "Odd weeks";
        }
        else if (frequency == "even")
        {
            _selectedFrequencyOption = "Even weeks";
        }
        else
        {
            _selectedFrequencyOption = "Custom";
            _customWeeksText = frequency;
        }
    }

    /// <summary>
    /// Converts the current frequency selection to the string stored on <see cref="SectionDaySchedule.Frequency"/>.
    /// Returns null for "Weekly". Returns "odd" / "even" for those options.
    /// For "Custom", parses, validates, sorts and returns the week list (e.g. "1,6,7"),
    /// or null if the text is invalid.
    /// </summary>
    /// <returns>The stored frequency string, or null if weekly or custom input is invalid.</returns>
    private string? BuildStoredFrequency()
    {
        return SelectedFrequencyOption switch
        {
            "Odd weeks"  => "odd",
            "Even weeks" => "even",
            "Custom"     => BuildCustomFrequency(),
            _            => null, // "Weekly" or anything unexpected
        };
    }

    /// <summary>
    /// Parses, validates, deduplicates, and sorts <see cref="CustomWeeksText"/> into the
    /// stored form (e.g. "1,6,7"). Returns null if the text is invalid.
    /// </summary>
    private string? BuildCustomFrequency()
    {
        if (CustomWeeksError is not null || string.IsNullOrWhiteSpace(CustomWeeksText))
            return null;

        var tokens = CustomWeeksText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var weeks = new SortedSet<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var week)) return null;
            weeks.Add(week);
        }
        return string.Join(",", weeks);
    }

    // ── Change notifications ──────────────────────────────────────────────────

    partial void OnSelectedFrequencyOptionChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomFrequency));
        if (value != "Custom")
        {
            CustomWeeksText = string.Empty;
            CustomWeeksError = null;
        }
        else
        {
            // Trigger validation in case text is already populated (e.g. toggled back to Custom)
            ValidateCustomWeeks();
        }
    }

    partial void OnCustomWeeksTextChanged(string value) => ValidateCustomWeeks();

    partial void OnSelectedBlockLengthChanged(double? value)
    {
        RefreshStartTimes();
        // Clear start time if it is no longer valid for the new block length.
        // Do not auto-select — the user picks the start time explicitly.
        if (SelectedStartTime.HasValue && !AvailableStartTimes.Contains(SelectedStartTime.Value))
            SelectedStartTime = null;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates <see cref="CustomWeeksText"/> against the allowed range 1..<see cref="Constants.NumWeeks"/>.
    /// Sets <see cref="CustomWeeksError"/> to a user-facing message on failure, or null on success.
    /// Does nothing when the Custom frequency option is not selected.
    /// </summary>
    private void ValidateCustomWeeks()
    {
        if (!IsCustomFrequency) { CustomWeeksError = null; return; }

        if (string.IsNullOrWhiteSpace(CustomWeeksText))
        {
            CustomWeeksError = $"Enter week numbers, e.g. 1,3,5";
            return;
        }

        var tokens = CustomWeeksText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<int>();

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var week) || week < 1 || week > Constants.NumWeeks)
            {
                CustomWeeksError = $"Each week must be 1–{Constants.NumWeeks}";
                return;
            }
            if (!seen.Add(week))
            {
                CustomWeeksError = "Duplicate week numbers";
                return;
            }
        }

        CustomWeeksError = null;
    }

    // ── Start time helpers ────────────────────────────────────────────────────

    private void RefreshStartTimes()
    {
        if (SelectedBlockLength is null) { AvailableStartTimes = new(); return; }
        var legal = _legalStartTimes.FirstOrDefault(l => Math.Abs(l.BlockLength - SelectedBlockLength.Value) < 0.01);
        AvailableStartTimes = legal is null
            ? new ObservableCollection<int>()
            : new ObservableCollection<int>(legal.StartTimes);
    }

    // ── Model conversion ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts the form state back to a <see cref="SectionDaySchedule"/> model.
    /// Returns null if the block length or start time have not been selected yet.
    /// </summary>
    /// <returns>A new <see cref="SectionDaySchedule"/>, or null if incomplete.</returns>
    public SectionDaySchedule? ToSchedule()
    {
        if (SelectedBlockLength is null || SelectedStartTime is null) return null;
        return new SectionDaySchedule
        {
            Day           = SelectedDay,
            StartMinutes  = SelectedStartTime.Value,
            DurationMinutes = (int)(SelectedBlockLength.Value * 60),
            MeetingTypeId = string.IsNullOrEmpty(SelectedMeetingTypeId) ? null : SelectedMeetingTypeId,
            RoomId        = string.IsNullOrEmpty(SelectedRoomId)        ? null : SelectedRoomId,
            Frequency     = BuildStoredFrequency(),
        };
    }
}

public record DayOption(int Day, string Name);
