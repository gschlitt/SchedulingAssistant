using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.Globalization;

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

    // ── Schedule fields ───────────────────────────────────────────────────────

    [ObservableProperty] private int _selectedDay;

    /// <summary>
    /// The committed start time in minutes from midnight. Set by <see cref="CommitStartTime"/>
    /// when the user finishes entering a time. Also set directly (e.g., during pattern coupling).
    /// </summary>
    [ObservableProperty] private int? _selectedStartTime;

    /// <summary>
    /// The committed block length in hours. Set by <see cref="CommitBlockLength"/>
    /// when the user finishes entering a duration. Also set directly (e.g., during pattern coupling).
    /// </summary>
    [ObservableProperty] private double? _selectedBlockLength;

    /// <summary>The raw text currently showing in the Start time field (e.g. "08:30").</summary>
    [ObservableProperty] private string _startTimeText = "";

    /// <summary>The raw text currently showing in the Length field (e.g. "1.5").</summary>
    [ObservableProperty] private string _blockLengthText = "";

    /// <summary>Validation error message for the Start field. Null when valid.</summary>
    [ObservableProperty] private string? _startTimeError;

    /// <summary>Validation error message for the Length field. Null when valid.</summary>
    [ObservableProperty] private string? _blockLengthError;

    [ObservableProperty] private string? _selectedMeetingTypeId;
    [ObservableProperty] private string? _selectedRoomId;

    public ObservableCollection<DayOption> AvailableDays { get; }
    public ObservableCollection<SectionPropertyValue> MeetingTypes { get; }
    public ObservableCollection<Room> Rooms { get; }

    /// <summary>
    /// All distinct start times available across all block-length rows, formatted as "HH:MM".
    /// Shown as suggestions in the Start AutoCompleteBox regardless of which block length
    /// is selected — the user picks start time first, then sees corresponding lengths.
    /// </summary>
    public IReadOnlyList<string> AllStartTimeStrings { get; }

    /// <summary>
    /// Block lengths valid for the currently selected start time, formatted as decimal hours
    /// (e.g. "1.5", "2", "3"). Populated by <see cref="RefreshBlockLengths"/> after the
    /// start time is committed. Empty when no start time is set or none of the legal
    /// start-time entries include the current start time.
    /// </summary>
    public ObservableCollection<string> AvailableBlockLengthStrings { get; } = new();

    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;

    /// <param name="legalStartTimes">Academic-year legal start times — used for both the start-time suggestion list and the reverse block-length lookup.</param>
    /// <param name="includeSaturday">Whether Saturday should appear as a day option.</param>
    /// <param name="meetingTypes">Available meeting type property values.</param>
    /// <param name="rooms">Available rooms for selection.</param>
    /// <param name="existing">If non-null, the form is populated from this existing schedule entry.</param>
    /// <param name="defaultBlockLength">Preferred block length hint; ignored in new flow since start time must be chosen first.</param>
    public SectionMeetingViewModel(
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        IReadOnlyList<SectionPropertyValue> meetingTypes,
        IReadOnlyList<Room> rooms,
        SectionDaySchedule? existing = null,
        double? defaultBlockLength = null)
    {
        _legalStartTimes = legalStartTimes;

        // Build the start-time suggestion list from ALL distinct start times across every
        // block-length row, sorted chronologically.
        AllStartTimeStrings = legalStartTimes
            .SelectMany(l => l.StartTimes)
            .Distinct()
            .OrderBy(t => t)
            .Select(FormatTime)
            .ToList()
            .AsReadOnly();

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

            // Load start time first so RefreshBlockLengths produces the right preset list.
            _selectedStartTime = existing.StartMinutes;
            _startTimeText     = FormatTime(existing.StartMinutes);
            RefreshBlockLengths();

            // Load block length — may be a custom value not in the preset list.
            double blockLengthHours = existing.DurationMinutes / 60.0;
            _selectedBlockLength = blockLengthHours;
            _blockLengthText     = FormatBlockLength(blockLengthHours);

            _selectedMeetingTypeId = existing.MeetingTypeId ?? "";
            _selectedRoomId        = existing.RoomId ?? "";
            LoadFrequency(existing.Frequency);
        }
        else
        {
            _selectedDay = 1;
            // null (not "") so that Avalonia's SelectedValue+SelectedValueBinding resolution
            // during ComboBox initialization does not fire a spurious PropertyChanged — which
            // would otherwise consume the pattern-coupling slot before the user picks anything.
            _selectedMeetingTypeId = null;
            _selectedRoomId        = "";
            _selectedFrequencyOption = "Weekly";
            // Start time and block length are left empty; the user picks start time first.
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

    /// <summary>
    /// When the committed start time changes (via user commit or pattern propagation):
    /// keeps <see cref="StartTimeText"/> in sync, refreshes the block-length suggestion list,
    /// and clears the block length if it was a preset that no longer applies.
    /// </summary>
    partial void OnSelectedStartTimeChanged(int? value)
    {
        // Keep the text field in sync so that pattern-propagated values are visible.
        _startTimeText = value.HasValue ? FormatTime(value.Value) : "";
        OnPropertyChanged(nameof(StartTimeText));

        StartTimeError = null;
        RefreshBlockLengths();

        // Clear block length when start time changes so the user re-confirms the length
        // for the new start time. Custom values are cleared too — re-enter if needed.
        if (SelectedBlockLength.HasValue)
            SelectedBlockLength = null;
    }

    /// <summary>
    /// When the committed block length changes (via user commit or pattern propagation):
    /// keeps <see cref="BlockLengthText"/> in sync.
    /// </summary>
    partial void OnSelectedBlockLengthChanged(double? value)
    {
        // Keep the text field in sync so that pattern-propagated values are visible.
        _blockLengthText = value.HasValue ? FormatBlockLength(value.Value) : "";
        OnPropertyChanged(nameof(BlockLengthText));

        BlockLengthError = null;
    }

    /// <summary>
    /// When the start-time text changes, auto-commits if the text exactly matches a preset.
    /// This provides immediate feedback when the user selects from the suggestion dropdown.
    /// </summary>
    partial void OnStartTimeTextChanged(string value)
    {
        if (AllStartTimeStrings.Contains(value))
            CommitStartTime();
    }

    /// <summary>
    /// When the block-length text changes, auto-commits if the text exactly matches a preset.
    /// </summary>
    partial void OnBlockLengthTextChanged(string value)
    {
        if (AvailableBlockLengthStrings.Contains(value))
            CommitBlockLength();
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

    // ── Commit commands ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses and validates <see cref="StartTimeText"/>, then sets <see cref="SelectedStartTime"/>
    /// if the value is valid. Called on LostFocus from the Start AutoCompleteBox and when
    /// the text exactly matches a preset suggestion.
    /// The time must fall within the configured grid range
    /// (<see cref="AppSettings.GridStartMinutes"/>–<see cref="AppSettings.GridEndMinutes"/>).
    /// </summary>
    [RelayCommand]
    public void CommitStartTime()
    {
        if (string.IsNullOrWhiteSpace(StartTimeText))
        {
            StartTimeError = null;
            SelectedStartTime = null;
            return;
        }

        var parsed = ParseTime(StartTimeText);
        if (parsed is null)
        {
            StartTimeError = "Enter a time like 0915";
            return;
        }

        int gridStart = AppSettings.Current.GridStartMinutes;
        int gridEnd   = AppSettings.Current.GridEndMinutes;
        if (parsed.Value < gridStart || parsed.Value >= gridEnd)
        {
            StartTimeError =
                $"Must be between {FormatTime(gridStart)} and {FormatTime(gridEnd - 1)}";
            return;
        }

        StartTimeError = null;
        SelectedStartTime = parsed.Value;
    }

    /// <summary>
    /// Parses and validates <see cref="BlockLengthText"/>, then sets <see cref="SelectedBlockLength"/>
    /// if the value is valid. Called on LostFocus from the Length AutoCompleteBox and when
    /// the text exactly matches a preset suggestion.
    /// Validates that the meeting ends within the configured grid range.
    /// </summary>
    [RelayCommand]
    public void CommitBlockLength()
    {
        if (string.IsNullOrWhiteSpace(BlockLengthText))
        {
            BlockLengthError = null;
            SelectedBlockLength = null;
            return;
        }

        var parsed = ParseBlockLength(BlockLengthText);
        if (parsed is null || parsed.Value <= 0)
        {
            BlockLengthError = "Enter a duration like 1.5 or 1:30";
            return;
        }

        // Validate that the meeting ends within the grid.
        if (SelectedStartTime.HasValue)
        {
            int endMinutes = SelectedStartTime.Value + (int)Math.Round(parsed.Value * 60);
            int gridEnd    = AppSettings.Current.GridEndMinutes;
            if (endMinutes > gridEnd)
            {
                BlockLengthError =
                    $"Meeting would end after {FormatTime(gridEnd)}";
                return;
            }
        }

        BlockLengthError = null;
        SelectedBlockLength = parsed.Value;
    }

    // ── Block-length refresh (reverse lookup) ─────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="AvailableBlockLengthStrings"/> to contain every block length for
    /// which the currently selected start time is a legal start. This is the reverse of the
    /// old flow (pick length → see valid starts); now the user picks start first and sees
    /// which lengths are available.
    /// When the chosen start time is not in the table (e.g. a custom time like 09:15), falls
    /// back to showing every known block length as suggestions so the dropdown is never empty.
    /// </summary>
    private void RefreshBlockLengths()
    {
        AvailableBlockLengthStrings.Clear();
        if (SelectedStartTime is null) return;

        var matched = _legalStartTimes
            .Where(l => l.StartTimes.Contains(SelectedStartTime.Value))
            .Select(l => l.BlockLength)
            .OrderBy(b => b)
            .ToList();

        // Fall back to all known block lengths when none match the custom start time.
        var lengths = matched.Count > 0
            ? matched
            : _legalStartTimes.Select(l => l.BlockLength).Distinct().OrderBy(b => b).ToList();

        foreach (var s in lengths.Select(FormatBlockLength))
            AvailableBlockLengthStrings.Add(s);
    }

    // ── Format / parse helpers ────────────────────────────────────────────────

    /// <summary>
    /// Formats minutes-from-midnight as four-digit military time (e.g. 510 → "0830").
    /// No colon separator — consistent with the app-wide military time convention.
    /// </summary>
    /// <param name="minutes">Time in minutes from midnight.</param>
    internal static string FormatTime(int minutes) =>
        $"{minutes / 60:D2}{minutes % 60:D2}";

    /// <summary>
    /// Formats a block length in hours as a compact decimal string (e.g. 1.5 → "1.5", 2.0 → "2").
    /// Uses the invariant culture so the decimal separator is always ".".
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    internal static string FormatBlockLength(double hours) =>
        hours.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a military-time string to minutes from midnight.
    /// Accepts HHMM with no separator (e.g. "0830", "830", "1230").
    /// The last two digits are always minutes; preceding digits are hours.
    /// Returns null if the string cannot be parsed or represents an invalid time.
    /// </summary>
    /// <param name="text">The input string to parse (e.g. "0830" or "830").</param>
    /// <returns>Minutes from midnight, or null if parsing fails.</returns>
    internal static int? ParseTime(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!int.TryParse(text, out int hhmm) || hhmm < 0) return null;
        int h = hhmm / 100;
        int m = hhmm % 100;
        if (h > 23 || m > 59) return null;
        return h * 60 + m;
    }

    /// <summary>
    /// Parses a block-length string to hours. Accepts:
    /// <list type="bullet">
    ///   <item><term>Decimal hours</term><description>"1.5" → 1.5</description></item>
    ///   <item><term>H:MM format</term><description>"1:30" → 1.5</description></item>
    /// </list>
    /// Returns null if the string cannot be parsed or is non-positive.
    /// </summary>
    /// <param name="text">The input string to parse.</param>
    /// <returns>Block length in hours, or null if parsing fails.</returns>
    internal static double? ParseBlockLength(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        // H:MM format: "1:30" → 1.5h
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int h)
                && int.TryParse(parts[1].Trim(), out int m)
                && h >= 0 && m >= 0 && m < 60)
                return h + m / 60.0;
            return null;
        }

        // Decimal hours: "1.5" → 1.5h
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val)
            && val > 0)
            return val;

        return null;
    }

    // ── Model conversion ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts the form state back to a <see cref="SectionDaySchedule"/> model.
    /// Returns null if the start time or block length have not been committed yet.
    /// </summary>
    /// <returns>A new <see cref="SectionDaySchedule"/>, or null if incomplete.</returns>
    public SectionDaySchedule? ToSchedule()
    {
        if (SelectedStartTime is null || SelectedBlockLength is null) return null;
        return new SectionDaySchedule
        {
            Day             = SelectedDay,
            StartMinutes    = SelectedStartTime.Value,
            DurationMinutes = (int)Math.Round(SelectedBlockLength.Value * 60),
            MeetingTypeId   = string.IsNullOrEmpty(SelectedMeetingTypeId) ? null : SelectedMeetingTypeId,
            RoomId          = string.IsNullOrEmpty(SelectedRoomId)        ? null : SelectedRoomId,
            Frequency       = BuildStoredFrequency(),
        };
    }
}

public record DayOption(int Day, string Name);
