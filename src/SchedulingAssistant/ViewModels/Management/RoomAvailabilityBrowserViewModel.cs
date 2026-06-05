using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Drives the Room Availability Browser panel inside the section editor.
/// Reads partially-specified meeting rows (<see cref="MeetingSpec"/>), fills in
/// the gaps (days, start times, rooms), and lets the user step through ranked solutions
/// displayed as ghost tiles on the schedule grid.
/// </summary>
public partial class RoomAvailabilityBrowserViewModel : ViewModelBase
{
    private readonly RoomAvailabilityService _service = new();
    private readonly IReadOnlyList<MeetingSpec> _specs;
    private readonly IReadOnlyList<Room> _allRooms;

    /// <summary>
    /// Minimum seating capacity required (the section's capacity), or null when the section
    /// has none. Rooms whose KNOWN capacity is below this are excluded; rooms with no capacity
    /// set are kept (unknown gets the benefit of the doubt).
    /// </summary>
    private readonly int? _minCapacity;

    /// <summary>
    /// Campus the edited section/meeting is assigned to, or null if none. When set, the browser
    /// considers only rooms on this campus (hard exclude). Null means no campus restriction.
    /// </summary>
    private readonly string? _campusId;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly IReadOnlyList<BlockPattern> _blockPatterns;
    private readonly string _semesterId;
    private readonly string _semesterName;
    private readonly string _semesterColor;
    private readonly Action<List<GhostBlock>?> _setGhostBlocks;
    private readonly Action<IReadOnlyList<SpecSolution>> _onAccept;
    private readonly Action _onCancel;

    private OccupancyIndex _index = new();
    private List<RoomSolution> _solutions = new();
    private List<RoomSolution> _displayedSolutions = new();

    // ── Observable properties ────────────────────────────────────────────────

    // Solution stepping
    [ObservableProperty] private int _currentSolutionIndex;
    [ObservableProperty] private int _totalSolutions;
    [ObservableProperty] private string _solutionLabel = "No solutions";
    [ObservableProperty] private string _tierLabel = "";
    [ObservableProperty] private bool _hasSolutions;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoPrevious;

    /// <summary>When true, alternative solutions (relaxed constraints) are included in navigation.</summary>
    [ObservableProperty] private bool _showAlternatives;

    /// <summary>True when the solution set contains at least one alternative.</summary>
    [ObservableProperty] private bool _hasAlternatives;

    /// <summary>Number of alternative solutions available (shown in label hint).</summary>
    [ObservableProperty] private int _alternativeCount;

    /// <param name="specs">Partially-specified meetings — duration required, day/start optional.</param>
    /// <param name="allRooms">All rooms in the database.</param>
    /// <param name="legalStartTimes">Legal start times for the current academic year.</param>
    /// <param name="blockPatterns">Configured block patterns (day patterns).</param>
    /// <param name="semesterSections">All sections in the active semester.</param>
    /// <param name="semesterMeetings">All meetings in the active semester.</param>
    /// <param name="excludeSectionId">The section being edited (excluded from occupancy).</param>
    /// <param name="excludeMeetingId">The meeting being edited (excluded from occupancy).</param>
    /// <param name="semesterId">Active semester ID for ghost block placement.</param>
    /// <param name="semesterName">Active semester display name.</param>
    /// <param name="semesterColor">Active semester color hex string.</param>
    /// <param name="setGhostBlocks">Callback to push ghost blocks onto the schedule grid.</param>
    /// <param name="onAccept">Callback when the user accepts a solution.</param>
    /// <param name="onCancel">Callback when the user cancels browsing.</param>
    /// <param name="minCapacity">Section capacity; rooms with a known smaller capacity are excluded.</param>
    /// <param name="campusId">When set, only rooms on this campus are considered (hard exclude).</param>
    public RoomAvailabilityBrowserViewModel(
        IReadOnlyList<MeetingSpec> specs,
        IReadOnlyList<Room> allRooms,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        IReadOnlyList<BlockPattern> blockPatterns,
        IEnumerable<Section> semesterSections,
        IEnumerable<Meeting> semesterMeetings,
        string? excludeSectionId,
        string? excludeMeetingId,
        string semesterId,
        string semesterName,
        string semesterColor,
        Action<List<GhostBlock>?> setGhostBlocks,
        Action<IReadOnlyList<SpecSolution>> onAccept,
        Action onCancel,
        int? minCapacity = null,
        string? campusId = null)
    {
        _specs = specs;
        _allRooms = allRooms;
        _minCapacity = minCapacity;
        _campusId = campusId;
        _legalStartTimes = legalStartTimes;
        _blockPatterns = blockPatterns;
        _semesterId = semesterId;
        _semesterName = semesterName;
        _semesterColor = semesterColor;
        _setGhostBlocks = setGhostBlocks;
        _onAccept = onAccept;
        _onCancel = onCancel;

        _index = _service.BuildOccupancyIndex(semesterSections, semesterMeetings, excludeSectionId, excludeMeetingId);
        Recompute();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NextSolution()
    {
        if (CurrentSolutionIndex < _displayedSolutions.Count - 1)
        {
            CurrentSolutionIndex++;
            ShowCurrentSolution();
        }
    }

    [RelayCommand]
    private void PreviousSolution()
    {
        if (CurrentSolutionIndex > 0)
        {
            CurrentSolutionIndex--;
            ShowCurrentSolution();
        }
    }

    [RelayCommand]
    private void AcceptSolution()
    {
        if (_displayedSolutions.Count == 0 || CurrentSolutionIndex >= _displayedSolutions.Count) return;

        var solution = _displayedSolutions[CurrentSolutionIndex];

        // Map solution slots back to spec indices via greedy day+duration matching.
        var specSolutions = MapSlotsToSpecs(solution.Slots, _specs);

        _setGhostBlocks(null);
        _onAccept(specSolutions);
    }

    [RelayCommand]
    private void Cancel()
    {
        _setGhostBlocks(null);
        _onCancel();
    }

    // ── Internal logic ───────────────────────────────────────────────────────

    private void Recompute()
    {
        // Filter candidate rooms by campus and capacity via the shared helper.
        //  • Campus: when the section/meeting has a campus, only rooms on that campus qualify
        //    (hard exclude — note this also drops rooms with no campus set, unlike capacity).
        //  • Capacity: drop rooms whose known capacity is below the section's; unknown capacity
        //    gets the benefit of the doubt. Both filters are no-ops when their value is null.
        var rooms = RoomAvailabilityService.ApplyFilter(
            _allRooms,
            campusId:    _campusId,
            building:    null,
            roomTypeId:  null,
            minCapacity: _minCapacity);

        _solutions = _service.GenerateSolutionsFromSpecs(
            _specs, rooms, _index, _legalStartTimes, _blockPatterns);

        AlternativeCount = _solutions.Count(s => s.IsAlternative);
        HasAlternatives = AlternativeCount > 0;
        RebuildDisplayList();
    }

    /// <summary>Rebuilds the filtered solution list based on the current toggle state.</summary>
    private void RebuildDisplayList()
    {
        _displayedSolutions = ShowAlternatives
            ? _solutions
            : _solutions.Where(s => !s.IsAlternative).ToList();

        TotalSolutions = _displayedSolutions.Count;
        HasSolutions = _displayedSolutions.Count > 0;
        CurrentSolutionIndex = 0;

        if (HasSolutions)
            ShowCurrentSolution();
        else
            ClearSolutions();
    }

    partial void OnShowAlternativesChanged(bool value) => RebuildDisplayList();

    private void ShowCurrentSolution()
    {
        if (_displayedSolutions.Count == 0 || CurrentSolutionIndex >= _displayedSolutions.Count) return;

        var solution = _displayedSolutions[CurrentSolutionIndex];

        string label = $"{CurrentSolutionIndex + 1} of {TotalSolutions}";
        if (!ShowAlternatives && HasAlternatives)
            label += $" (+{AlternativeCount} alt)";
        SolutionLabel = label;

        TierLabel = solution.TierLabel;
        CanGoNext = CurrentSolutionIndex < _displayedSolutions.Count - 1;
        CanGoPrevious = CurrentSolutionIndex > 0;

        var ghosts = solution.Slots.Select(slot =>
        {
            int endMinutes = slot.StartMinutes + slot.DurationMinutes;
            string timeLabel = $"{FormatTime(slot.StartMinutes)}–{FormatTime(endMinutes)}";
            return new GhostBlock(
                slot.Day, slot.StartMinutes, endMinutes,
                slot.RoomLabel, timeLabel,
                _semesterId, _semesterName, _semesterColor);
        }).ToList();

        _setGhostBlocks(ghosts);
    }

    private void ClearSolutions()
    {
        _solutions.Clear();
        _displayedSolutions.Clear();
        TotalSolutions = 0;
        HasSolutions = false;
        CurrentSolutionIndex = 0;
        SolutionLabel = "No solutions";
        TierLabel = "";
        CanGoNext = false;
        CanGoPrevious = false;
        _setGhostBlocks(null);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Maps solution slots back to spec indices using greedy day+duration matching.
    /// Each slot is matched to the first unmatched spec with the same day and duration.
    /// </summary>
    internal static List<SpecSolution> MapSlotsToSpecs(
        IReadOnlyList<SolutionSlot> slots,
        IReadOnlyList<MeetingSpec> specs)
    {
        var result = new List<SpecSolution>();
        var used = new HashSet<int>();

        foreach (var slot in slots)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (used.Contains(i)) continue;
                var spec = specs[i];

                // Match: day must agree (spec day is null or matches slot day), duration must match.
                bool dayMatch = !spec.Day.HasValue || spec.Day.Value == slot.Day;
                bool durMatch = spec.DurationMinutes == slot.DurationMinutes;

                if (dayMatch && durMatch)
                {
                    used.Add(i);
                    result.Add(new SpecSolution(
                        spec.Index, slot.Day, slot.StartMinutes,
                        slot.DurationMinutes, slot.RoomId, slot.RoomLabel));
                    break;
                }
            }
        }

        return result;
    }

    // ── Formatting helpers ───────────────────────────────────────────────────

    private static string FormatTime(int minutes)
    {
        int h = minutes / 60;
        int m = minutes % 60;
        string ampm = h >= 12 ? "pm" : "am";
        if (h > 12) h -= 12;
        if (h == 0) h = 12;
        return m == 0 ? $"{h}{ampm}" : $"{h}:{m:D2}{ampm}";
    }

    internal static string DayName(int day) => day switch
    {
        1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu",
        5 => "Fri", 6 => "Sat", 7 => "Sun", _ => "?"
    };
}
