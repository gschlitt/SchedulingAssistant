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
    [ObservableProperty] private RoomTypeOption? _selectedRoomType;

    public ObservableCollection<DayOption> AvailableDays { get; }
    public ObservableCollection<SchedulingEnvironmentValue> MeetingTypes { get; }

    /// <summary>Room type options: "(none)", configured types, and "Remote".</summary>
    public ObservableCollection<RoomTypeOption> RoomTypeOptions { get; }

    /// <summary>Master room list (unfiltered). Not bound to UI directly.</summary>
    private readonly IReadOnlyList<Room> _allRooms;

    /// <summary>
    /// The owning section's campus id, or null/empty when no campus is set. When non-empty,
    /// <see cref="RefreshFilteredRooms"/> confines the room list to rooms on that campus plus
    /// rooms that specify no campus. Empty means no campus filter — all rooms are shown.
    /// </summary>
    private string? _sectionCampusId;

    /// <summary>Rooms filtered by the selected room type. Bound to the Room combobox.</summary>
    public ObservableCollection<Room> FilteredRooms { get; } = new();

    /// <summary>True when the selected room type is "Remote" — no physical room needed.</summary>
    public bool IsRemote => SelectedRoomType?.Id == SectionDaySchedule.RemoteRoomTypeId;

    /// <summary>True when a room selection is meaningful (not remote).</summary>
    public bool IsRoomEnabled => !IsRemote;

    /// <summary>
    /// Start time suggestions formatted as "HHMM". When no block length is committed, shows
    /// all distinct legal start times. When a block length is committed, narrows to only
    /// start times where that block length is legal (bidirectional filtering with
    /// <see cref="AvailableBlockLengthStrings"/>).
    /// </summary>
    public ObservableCollection<string> AvailableStartTimeStrings { get; } = new();

    /// <summary>
    /// All distinct start times across every block-length row, sorted chronologically.
    /// Immutable reference set used by <see cref="RefreshStartTimes"/> as the unfiltered pool.
    /// </summary>
    private readonly IReadOnlyList<string> _allStartTimeStrings;

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

    /// <summary>Guards against circular updates between <see cref="RefreshStartTimes"/> and <see cref="RefreshBlockLengths"/>.</summary>
    private bool _isRefreshing;

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
    /// <param name="roomTypeOptions">Room type options including "(none)" sentinel and "Remote".</param>
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
    /// <param name="sectionCampusId">
    /// The owning section's campus id. When non-empty, the room list is confined to rooms on
    /// that campus plus rooms with no campus assigned. Null/empty means show all rooms.
    /// </param>
    public SectionMeetingViewModel(
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        bool includeSunday,
        IReadOnlyList<SchedulingEnvironmentValue> meetingTypes,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<RoomTypeOption> roomTypeOptions,
        SectionDaySchedule? existing = null,
        double? defaultBlockLength = null,
        Func<string, Task>? onWarning = null,
        BlockLengthUnit unit = BlockLengthUnit.Hours,
        string? sectionCampusId = null)
    {
        _legalStartTimes    = legalStartTimes;
        _defaultBlockLength = defaultBlockLength;
        _onWarning          = onWarning;
        _unit               = unit;
        _allRooms           = rooms;
        _sectionCampusId    = sectionCampusId;

        // Build the immutable reference set of all start times for RefreshStartTimes.
        _allStartTimeStrings = legalStartTimes
            .SelectMany(l => l.StartTimes)
            .Distinct()
            .OrderBy(t => t)
            .Select(FormatTime)
            .ToList()
            .AsReadOnly();

        var days = new List<DayOption>
        {
            new(0, "(any)"),
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

        RoomTypeOptions = new ObservableCollection<RoomTypeOption>(roomTypeOptions);

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

            // Populate start time suggestions (filtered by the loaded block length).
            RefreshStartTimes();

            _selectedMeetingTypeId = existing.MeetingTypeId ?? "";
            _selectedRoomId        = existing.RoomId ?? "";
            _selectedRoomType      = roomTypeOptions.FirstOrDefault(o => o.Id == existing.RoomTypeId)
                                     ?? roomTypeOptions[0];
            RefreshFilteredRooms();
            LoadFrequency(existing.Frequency);
        }
        else
        {
            _selectedDay = 0; // "(any)" — user picks a day or leaves it for the browser
            // null (not "") so that Avalonia's SelectedValue+SelectedValueBinding resolution
            // during ComboBox initialization does not fire a spurious PropertyChanged — which
            // would otherwise consume the pattern-coupling slot before the user picks anything.
            _selectedMeetingTypeId = null;
            _selectedRoomId        = "";
            _selectedRoomType      = roomTypeOptions[0]; // "(none)"
            RefreshFilteredRooms();
            _selectedFrequencyOption = "Weekly";
            // Populate suggestion lists immediately so the user can pick either field first.
            RefreshStartTimes();
            RefreshBlockLengths();
        }
    }

    // ── Room type helpers ───────────────────────────────────────────────────────

    partial void OnSelectedRoomTypeChanged(RoomTypeOption? value)
    {
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsRoomEnabled));
        RefreshFilteredRooms();

        if (IsRemote)
        {
            SelectedRoomId = "";
        }
        else if (!string.IsNullOrEmpty(SelectedRoomId) && value?.Id != null)
        {
            // Clear room if it doesn't match the new type
            var currentRoom = _allRooms.FirstOrDefault(r => r.Id == SelectedRoomId);
            if (currentRoom != null && currentRoom.RoomTypeId != value.Id)
                SelectedRoomId = "";
        }
    }

    /// <summary>
    /// Sets the owning section's campus and re-filters the room list. Called by the parent
    /// section editor at construction and whenever the section's campus selection changes.
    /// </summary>
    /// <param name="campusId">The section's campus id, or null/empty for no campus filter.</param>
    public void SetSectionCampus(string? campusId)
    {
        _sectionCampusId = campusId;
        RefreshFilteredRooms();
    }

    /// <summary>
    /// Rebuilds <see cref="FilteredRooms"/> based on the selected room type and the section's
    /// campus. Shows all rooms when "(none)" room type is selected; disables when "Remote".
    /// When the section has a campus, the list is confined to rooms on that campus plus rooms
    /// with no campus assigned. The currently-selected room is always kept in the list so an
    /// existing (possibly off-campus) choice is never silently dropped.
    /// </summary>
    private void RefreshFilteredRooms()
    {
        FilteredRooms.Clear();
        FilteredRooms.Add(new Room { Id = "", Building = "(none)", RoomNumber = "" });

        var typeId = SelectedRoomType?.Id;
        foreach (var room in _allRooms)
        {
            bool typeMatch = typeId == null
                             || typeId == SectionDaySchedule.RemoteRoomTypeId
                             || room.RoomTypeId == typeId;
            if (!typeMatch)
                continue;

            // Apply the campus filter, but always keep the already-selected room visible.
            if (!CampusAllowsRoom(room) && room.Id != SelectedRoomId)
                continue;

            FilteredRooms.Add(room);
        }
    }

    /// <summary>
    /// True when the room may be offered for selection under the current section campus:
    /// either no section campus is set, the room specifies no campus, or the room's campus
    /// matches the section's.
    /// </summary>
    private bool CampusAllowsRoom(Room room) =>
        string.IsNullOrEmpty(_sectionCampusId)
        || string.IsNullOrEmpty(room.CampusId)
        || room.CampusId == _sectionCampusId;

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
        if (!SelectedBlockLength.HasValue && value.HasValue && _defaultBlockLength.HasValue)
        {
            string preferred = FormatBlockLength(_defaultBlockLength.Value);
            if (AvailableBlockLengthStrings.Contains(preferred))
            {
                // BACKING FIELDS intentionally used instead of property setters.
                //
                // Setting SelectedBlockLength via the property setter would trigger the
                // generated OnSelectedBlockLengthChanged partial method, which calls
                // RefreshStartTimes(). That clears and repopulates AvailableStartTimeStrings
                // while the Start AutoCompleteBox is still processing the text change that
                // initiated this whole chain — Avalonia's internal index into the ItemsSource
                // becomes invalid and throws ArgumentOutOfRangeException.
                //
                // Writing _selectedBlockLength directly bypasses the partial method because
                // CommunityToolkit [ObservableProperty] only invokes OnXChanged from the
                // property setter, not from OnPropertyChanged. The OnPropertyChanged calls
                // below update UI bindings (text field display, CanExecute for Room Browser)
                // without re-entering the cascade. RefreshStartTimes() is unnecessary here
                // anyway — we just set the start time, so the suggestion list is already
                // correct for the current state.
                _selectedBlockLength = _defaultBlockLength.Value;
                _blockLengthText = preferred;
                BlockLengthError = null;
                OnPropertyChanged(nameof(SelectedBlockLength));
                OnPropertyChanged(nameof(BlockLengthText));
            }
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
        RefreshStartTimes();
    }

    /// <summary>
    /// When the start-time text changes, auto-commits if the text exactly matches a preset.
    /// This provides immediate feedback when the user selects from the suggestion dropdown —
    /// the block-length list refreshes immediately without requiring the user to tab away first.
    /// </summary>
    partial void OnStartTimeTextChanged(string value)
    {
        if (AvailableStartTimeStrings.Contains(value))
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

    // ── Bidirectional suggestion refresh ─────────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="AvailableBlockLengthStrings"/>. When a start time is committed,
    /// shows only block lengths legal at that time; otherwise shows all distinct block lengths.
    /// Falls back to all lengths when a custom start time matches nothing.
    /// </summary>
    private void RefreshBlockLengths()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            AvailableBlockLengthStrings.Clear();

            if (SelectedStartTime is null)
            {
                // No start time — show all distinct block lengths as suggestions.
                var all = _legalStartTimes
                    .Select(l => l.BlockLength)
                    .Distinct()
                    .OrderBy(b => b)
                    .ToList();
                foreach (var s in all.Select(FormatBlockLength))
                    AvailableBlockLengthStrings.Add(s);
                return;
            }

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
        finally { _isRefreshing = false; }
    }

    /// <summary>
    /// Rebuilds <see cref="AvailableStartTimeStrings"/>. When a block length is committed,
    /// shows only start times where that block length is legal; otherwise shows all start times.
    /// Falls back to all start times when a custom block length matches nothing.
    /// </summary>
    private void RefreshStartTimes()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            AvailableStartTimeStrings.Clear();

            if (SelectedBlockLength is null)
            {
                foreach (var s in _allStartTimeStrings)
                    AvailableStartTimeStrings.Add(s);
                return;
            }

            // Find legal start-time entries whose block length matches the committed value.
            var matched = _legalStartTimes
                .Where(l => Math.Abs(l.BlockLength - SelectedBlockLength.Value) < 0.001)
                .SelectMany(l => l.StartTimes)
                .Distinct()
                .OrderBy(t => t)
                .Select(FormatTime)
                .ToList();

            // Fall back to all start times when the block length is custom (no legal entry matches).
            var times = matched.Count > 0 ? matched : _allStartTimeStrings;

            foreach (var s in times)
                AvailableStartTimeStrings.Add(s);
        }
        finally { _isRefreshing = false; }
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
    /// Converts the form state to a <see cref="MeetingSpec"/> for the Room Availability Browser.
    /// Returns null only when <see cref="SelectedBlockLength"/> is not set (duration is the one
    /// required field for Browse). Day and start time may be null (unspecified).
    /// </summary>
    /// <param name="index">Ordinal position in the Meetings collection, for back-mapping on Accept.</param>
    public MeetingSpec? ToMeetingSpec(int index)
    {
        if (SelectedBlockLength is null) return null;
        bool isRemote = IsRemote;
        var roomTypeId = SelectedRoomType?.Id;
        return new MeetingSpec(
            Index: index,
            Day: SelectedDay == 0 ? null : SelectedDay,
            DurationMinutes: (int)Math.Round(SelectedBlockLength.Value * 60),
            StartMinutes: SelectedStartTime,
            RoomTypeId: isRemote ? null : roomTypeId,
            RoomId: isRemote ? null : (string.IsNullOrEmpty(SelectedRoomId) ? null : SelectedRoomId),
            IsRemote: isRemote,
            Frequency: BuildStoredFrequency());
    }

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
            RoomTypeId      = SelectedRoomType?.Id,
            Frequency       = BuildStoredFrequency(),
        };
    }
}

public record DayOption(int Day, string Name);
