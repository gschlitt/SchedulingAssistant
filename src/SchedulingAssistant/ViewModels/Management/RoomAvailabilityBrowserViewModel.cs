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

    // ── Observable properties ────────────────────────────────────────────────

    // Solution stepping
    [ObservableProperty] private int _currentSolutionIndex;
    [ObservableProperty] private int _totalSolutions;
    [ObservableProperty] private string _solutionLabel = "No solutions";
    [ObservableProperty] private string _tierLabel = "";
    [ObservableProperty] private bool _hasSolutions;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoPrevious;

    /// <param name="specs">Partially-specified meetings — duration required, day/start optional.</param>
    /// <param name="allRooms">All rooms in the database.</param>
    /// <param name="legalStartTimes">Legal start times for the current academic year.</param>
    /// <param name="blockPatterns">Configured block patterns (day patterns).</param>
    /// <param name="semesterSections">All sections in the active semester.</param>
    /// <param name="semesterMeetings">All meetings in the active semester.</param>
    /// <param name="excludeSectionId">The section being edited (excluded from occupancy).</param>
    /// <param name="semesterId">Active semester ID for ghost block placement.</param>
    /// <param name="semesterName">Active semester display name.</param>
    /// <param name="semesterColor">Active semester color hex string.</param>
    /// <param name="setGhostBlocks">Callback to push ghost blocks onto the schedule grid.</param>
    /// <param name="onAccept">Callback when the user accepts a solution.</param>
    /// <param name="onCancel">Callback when the user cancels browsing.</param>
    public RoomAvailabilityBrowserViewModel(
        IReadOnlyList<MeetingSpec> specs,
        IReadOnlyList<Room> allRooms,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        IReadOnlyList<BlockPattern> blockPatterns,
        IEnumerable<Section> semesterSections,
        IEnumerable<Meeting> semesterMeetings,
        string? excludeSectionId,
        string semesterId,
        string semesterName,
        string semesterColor,
        Action<List<GhostBlock>?> setGhostBlocks,
        Action<IReadOnlyList<SpecSolution>> onAccept,
        Action onCancel)
    {
        _specs = specs;
        _allRooms = allRooms;
        _legalStartTimes = legalStartTimes;
        _blockPatterns = blockPatterns;
        _semesterId = semesterId;
        _semesterName = semesterName;
        _semesterColor = semesterColor;
        _setGhostBlocks = setGhostBlocks;
        _onAccept = onAccept;
        _onCancel = onCancel;

        _index = _service.BuildOccupancyIndex(semesterSections, semesterMeetings, excludeSectionId);
        Recompute();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NextSolution()
    {
        if (CurrentSolutionIndex < _solutions.Count - 1)
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
        if (_solutions.Count == 0 || CurrentSolutionIndex >= _solutions.Count) return;

        var solution = _solutions[CurrentSolutionIndex];

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
        _solutions = _service.GenerateSolutionsFromSpecs(
            _specs, _allRooms.ToList(), _index, _legalStartTimes, _blockPatterns);

        TotalSolutions = _solutions.Count;
        HasSolutions = _solutions.Count > 0;
        CurrentSolutionIndex = 0;

        if (HasSolutions)
            ShowCurrentSolution();
        else
            ClearSolutions();
    }

    private void ShowCurrentSolution()
    {
        if (_solutions.Count == 0 || CurrentSolutionIndex >= _solutions.Count) return;

        var solution = _solutions[CurrentSolutionIndex];
        SolutionLabel = $"{CurrentSolutionIndex + 1} of {TotalSolutions}";
        TierLabel = solution.TierLabel;
        CanGoNext = CurrentSolutionIndex < _solutions.Count - 1;
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
    private static List<SpecSolution> MapSlotsToSpecs(
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
