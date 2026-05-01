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

    /// <summary>The raw text currently showing in the Start time field (e.g. "0830").</summary>
    [ObservableProperty] private string _startTimeText = "";

    /// <summary>The raw text currently showing in the Length field (e.g. "1.5" or "90").</summary>
    [ObservableProperty] private string _blockLengthText = "";

    /// <summary>Validation error message for the Start field. Null when valid.</summary>
    [ObservableProperty] private string? _startTimeError;

    /// <summary>Validation error message for the Length field. Null when valid.</summary>
    [ObservableProperty] private string? _blockLengthError;

    [ObservableProperty] private string? _selectedMeetingTypeId;
    [ObservableProperty] private string? _selectedRoomId;

    public ObservableCollection<DayOption> AvailableDays { get; }
    public ObservableCollection<SchedulingEnvironmentValue> MeetingTypes { get; }
    public ObservableCollection<Room> Rooms { get; }

    /// <summary>
    /// All distinct start times available across all block-length rows, formatted as "HHMM".
    /// Shown as suggestions in the Start AutoCompleteBox regardless of which block length
    /// is selected — the user picks start time first, then sees corresponding lengths.
    /// </summary>
    public IReadOnlyList<string> AllStartTimeStrings { get; }

    /// <summary>
    /// Block lengths valid for the currently selected start time, formatted in the active unit
    /// (e.g. "1.5", "2" for hours; "90", "120" for minutes).
    /// Populated by <see cref="RefreshBlockLengths"/> after the start time is committed.
    /// Empty when no start time is set or none of the legal start-time entries include the current start time.
    /// </summary>
    public ObservableCollection<string> AvailableBlockLengthStrings { get; } = new();

    /// <summary>
    /// Watermark text for the Length AutoCompleteBox, reflecting the active unit ("hours" or "min").
    /// </summary>
    public string BlockLengthWatermark => BlockLengthFormatter.BlockLengthWatermark(_unit);

    // ── Time bound constants ──────────────────────────────────────────────────

    /// <summary>Absolute earliest a meeting may start: 07:30 (450 min from midnight).</summary>
    public const int MinStartMinutes = 7 * 60 + 30;   // 450

    /// <summary>Absolute latest a meeting may end: 22:00 (1320 min from midnight).</summary>
    public const int MaxEndMinutes   = 22 * 60;        // 1320

    // ─────────────────────────────────────────────────────────────────────────

    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly BlockLengthUnit _unit;

    /// <summary>
    /// The preferred block length from Settings. When the user commits a start time,
    /// the block length is auto-filled with this value if it is valid for that start time.
    /// Null means no preference is set.
    /// </summary>
    private readonly double? _defaultBlockLength;

    /// <summary>
    /// Optional callback invoked when a value is clamped to a hard bound.
    /// Receives the message to show the user (e.g. in a popup dialog).
    /// </summary>
    private readonly Func<string, Task>? _onWarning;

    /// <param name="legalStartTimes">Academic-year legal start times — used for both the start-time suggestion list and the reverse block-length lookup.</param>
    /// <param name="includeSaturday">Whether Saturday should appear as a day option.</param>
    /// <param name="includeSunday">Whether Sunday should appear as a day option.</param>
    /// <param name="meetingTypes">Available meeting type property values.</param>
    /// <param name="rooms">Available rooms for selection.</param>
    /// <param name="existing">If non-null, the form is populated from this existing schedule entry.</param>
    /// <param name="defaultBlockLength">
    /// Preferred block length from Settings. After the user commits a start time, the block
    /// length is auto-filled with this value when it is valid for the chosen start time.
    /// </param>
    /// <param name="onWarning">
    /// Optional callback invoked when a start time or block length is silently clamped to a
    /// hard bound (07:30 earliest, 22:00 latest end). Receives the message to display.
    /// </param>
    /// <param name="unit">
    /// The block-length display unit (Hours or Minutes). Controls formatting and parsing of
    /// the Length field throughout this meeting's lifetime.
    /// </param>
    public SectionMeetingViewModel(
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        bool includeSunday,
        IReadOnlyList<SchedulingEnvironmentValue> meetingTypes,
        IReadOnlyList<Room> rooms,
        SectionDaySchedule? existing = null,
        double? defaultBlockLength = null,
        Func<string, Task>? onWarning = null,
        BlockLengthUnit unit = BlockLengthUnit.Hours)
    {
        _legalStartTimes    = legalStartTimes;
        _defaultBlockLength = defaultBlockLength;
        _onWarning          = onWarning;
        _unit               = unit;

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
        if (includeSunday)
            days.Add(new(7, "Sunday"));
        AvailableDays = new ObservableCollection<DayOption>(days);

        // Prepend a "(none)" sentinel so the user can clear the meeting type
        var mtList = new List<SchedulingEnvironmentValue>
            { new SchedulingEnvironmentValue { Id = "", Name = "(none)" } };
        mtList.AddRange(meetingTypes);
        MeetingTypes = new ObservableCollection<SchedulingEnvironmentValue>(mtList);

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
    /// clears any previously-set block length that is no longer valid, and auto-fills the
    /// preferred block length when none is set.
    /// </summary>
    partial void OnSelectedStartTimeChanged(int? value)
    {
        // Sync the text field only when the value was set programmatically (e.g. pattern
        // coupling). Skip the write-back when the text already matches — the AutoCompleteBox
        // just set it via the two-way binding, and a spurious OnPropertyChanged would cause
        // Avalonia's internal TextUpdated path to set IsDropDownOpen = true, which prevents
        // the user from immediately reopening the dropdown after making a selection.
        string newText = value.HasValue ? FormatTime(value.Value) : "";
        if (_startTimeText != newText)
        {
            _startTimeText = newText;
            OnPropertyChanged(nameof(StartTimeText));
        }

        StartTimeError = null;
        RefreshBlockLengths();

        if (SelectedBlockLength.HasValue)
        {
            // Keep the current block length when it is still valid for the new start time.
            // This preserves a coupling-propagated value without redundantly re-setting it
            // (which would otherwise open the AutoCompleteBox dropdown on follower meetings).
            // Clear it only when the length is no longer in the valid list, or is a custom value.
            string current = FormatBlockLength(SelectedBlockLength.Value);
            if (!AvailableBlockLengthStrings.Contains(current))
                SelectedBlockLength = null;
        }

        // Auto-fill the preferred block length only when no block length is already set.
        // OnBlockLengthTextChanged will auto-commit it since it matches a preset value.
        if (!SelectedBlockLength.HasValue && value.HasValue && _defaultBlockLength.HasValue)
        {
            string preferred = FormatBlockLength(_defaultBlockLength.Value);
            if (AvailableBlockLengthStrings.Contains(preferred))
                BlockLengthText = preferred;
        }
    }

    /// <summary>
    /// When the committed block length changes (via user commit or pattern propagation):
    /// keeps <see cref="BlockLengthText"/> in sync.
    /// </summary>
    partial void OnSelectedBlockLengthChanged(double? value)
    {
        // Sync the text field only when the value differs — same reasoning as
        // OnSelectedStartTimeChanged: skip spurious write-backs to avoid triggering
        // Avalonia's AutoCompleteBox TextUpdated path after a user selection.
        string newText = value.HasValue ? FormatBlockLength(value.Value) : "";
        if (_blockLengthText != newText)
        {
            _blockLengthText = newText;
            OnPropertyChanged(nameof(BlockLengthText));
        }

        BlockLengthError = null;
    }

    /// <summary>
    /// When the start-time text changes, auto-commits if the text exactly matches a preset.
    /// This provides immediate feedback when the user selects from the suggestion dropdown —
    /// the block-length list refreshes immediately without requiring the user to tab away first.
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
    /// Validates <see cref="CustomWeeksText"/> as a comma-separated list of positive integers (≥ 1).
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
            if (!int.TryParse(token, out var week) || week < 1)
            {
                CustomWeeksError = "Each week must be a whole number ≥ 1";
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
    /// Parses <see cref="StartTimeText"/> and commits it as <see cref="SelectedStartTime"/>.
    /// Called on LostFocus from the Start AutoCompleteBox and when the text exactly matches
    /// a preset suggestion.
    /// <para>
    /// Hard bounds: 07:30 (<see cref="MinStartMinutes"/>) to 21:59 (one minute before
    /// <see cref="MaxEndMinutes"/>). If the entered time is earlier than 07:30 it is clamped
    /// to 07:30 and the user is warned via <c>onWarning</c>. Times at or past 22:00 are
    /// rejected as invalid.
    /// </para>
    /// </summary>
    [RelayCommand]
    public async Task CommitStartTime()
    {
        if (string.IsNullOrWhiteSpace(StartTimeText))
        {
            StartTimeError    = null;
            SelectedStartTime = null;
            return;
        }

        var parsed = ParseTime(StartTimeText);
        if (parsed is null)
        {
            StartTimeError = "Enter a time like 0915";
            return;
        }

        int value = parsed.Value;

        // Clamp to the earliest supported start time, warning the user.
        if (value < MinStartMinutes)
        {
            if (_onWarning != null)
                await _onWarning($"0730 is the earliest supported start time. The time has been set to 0730.");
            value = MinStartMinutes;
        }

        // Reject times that leave no room for even the shortest meeting.
        if (value >= MaxEndMinutes)
        {
            StartTimeError = $"Must be before {FormatTime(MaxEndMinutes)}";
            return;
        }

        StartTimeError    = null;
        SelectedStartTime = value;

        // Update the text box to show the (possibly clamped) committed value.
        StartTimeText = FormatTime(value);
    }

    /// <summary>
    /// Parses <see cref="BlockLengthText"/> and commits it as <see cref="SelectedBlockLength"/>.
    /// Called on LostFocus from the Length AutoCompleteBox and when the text exactly matches
    /// a preset suggestion.
    /// <para>
    /// If a start time is already set and the requested duration would push the end past
    /// 22:00 (<see cref="MaxEndMinutes"/>), the block length is silently clamped to whatever
    /// brings the end exactly to 22:00 and the user is warned via <c>onWarning</c>.
    /// </para>
    /// </summary>
    [RelayCommand]
    public async Task CommitBlockLength()
    {
        if (string.IsNullOrWhiteSpace(BlockLengthText))
        {
            BlockLengthError    = null;
            SelectedBlockLength = null;
            return;
        }

        var parsed = BlockLengthFormatter.ParseBlockLength(BlockLengthText, _unit);
        if (parsed is null || parsed.Value <= 0)
        {
            BlockLengthError = BlockLengthFormatter.ParseErrorHint(_unit);
            return;
        }

        double hours = parsed.Value;

        // Clamp to the latest permitted end time when a start time is known.
        if (SelectedStartTime.HasValue)
        {
            int endMinutes = SelectedStartTime.Value + (int)Math.Round(hours * 60);
            if (endMinutes > MaxEndMinutes)
            {
                double maxHours = (MaxEndMinutes - SelectedStartTime.Value) / 60.0;
                if (_onWarning != null)
                    await _onWarning(
                        $"That duration would end after 2200, which is the latest supported end time. " +
                        $"The length has been set to {FormatBlockLength(maxHours)} so the meeting ends at 2200.");
                hours = maxHours;
            }
        }

        BlockLengthError    = null;
        SelectedBlockLength = hours;

        // Update the text box to show the (possibly clamped) committed value.
        BlockLengthText = FormatBlockLength(hours);
    }

    // ── Block-length refresh (reverse lookup) ─────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="AvailableBlockLengthStrings"/> to contain every block length for
    /// which the currently selected start time is a legal start, formatted in the active unit.
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
    /// Formats a block length in hours using the active unit.
    /// Hours: compact decimal ("1.5", "2"). Minutes: whole integer ("90", "120").
    /// </summary>
    /// <param name="hours">Block length in hours.</param>
    private string FormatBlockLength(double hours) =>
        BlockLengthFormatter.FormatBlockLength(hours, _unit);

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
