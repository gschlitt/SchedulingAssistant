using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Drives the Room Availability Browser panel inside the section editor.
/// Computes feasible room+time solutions for a meeting pattern, displays them
/// as ghost tiles on the schedule grid, and lets the user step through and accept.
/// </summary>
public partial class RoomAvailabilityBrowserViewModel : ViewModelBase
{
    private readonly RoomAvailabilityService _service = new();
    private readonly IReadOnlyList<Room> _allRooms;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly IReadOnlyList<BlockPattern> _blockPatterns;
    private readonly IEnumerable<Section> _semesterSections;
    private readonly IEnumerable<Meeting> _semesterMeetings;
    private readonly string? _excludeSectionId;
    private readonly string _semesterId;
    private readonly string _semesterName;
    private readonly string _semesterColor;
    private readonly Action<List<GhostBlock>?> _setGhostBlocks;
    private readonly Action<IReadOnlyList<SolutionSlot>> _onAccept;

    private OccupancyIndex _index = new();
    private List<RoomSolution> _solutions = new();

    // ── Observable properties ────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<MeetingTemplate> _templates = new();
    [ObservableProperty] private MeetingTemplate? _selectedTemplate;

    // Room filters
    [ObservableProperty] private ObservableCollection<string> _campusOptions = new();
    [ObservableProperty] private string? _selectedCampus;
    [ObservableProperty] private ObservableCollection<string> _buildingOptions = new();
    [ObservableProperty] private string? _selectedBuilding;
    [ObservableProperty] private ObservableCollection<string> _roomTypeOptions = new();
    [ObservableProperty] private string? _selectedRoomType;
    [ObservableProperty] private int? _minCapacity;

    // Solution stepping
    [ObservableProperty] private int _currentSolutionIndex;
    [ObservableProperty] private int _totalSolutions;
    [ObservableProperty] private string _solutionLabel = "No solutions";
    [ObservableProperty] private string _tierLabel = "";
    [ObservableProperty] private bool _hasSolutions;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoPrevious;

    // Per-day duration editing
    [ObservableProperty] private ObservableCollection<DayDurationItem> _dayDurations = new();
    [ObservableProperty] private ObservableCollection<double> _availableBlockLengths = new();

    /// <summary>
    /// Creates the browser VM. The browser automatically builds the occupancy index
    /// and generates templates on construction.
    /// </summary>
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
    /// <param name="existingMeetings">Meetings already on the section (augment mode).</param>
    public RoomAvailabilityBrowserViewModel(
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
        Action<IReadOnlyList<SolutionSlot>> onAccept,
        IReadOnlyList<SectionDaySchedule>? existingMeetings = null)
    {
        _allRooms = allRooms;
        _legalStartTimes = legalStartTimes;
        _blockPatterns = blockPatterns;
        _semesterSections = semesterSections;
        _semesterMeetings = semesterMeetings;
        _excludeSectionId = excludeSectionId;
        _semesterId = semesterId;
        _semesterName = semesterName;
        _semesterColor = semesterColor;
        _setGhostBlocks = setGhostBlocks;
        _onAccept = onAccept;
        ExistingMeetings = existingMeetings ?? Array.Empty<SectionDaySchedule>();

        // Build occupancy index
        _index = _service.BuildOccupancyIndex(_semesterSections, _semesterMeetings, _excludeSectionId);

        // Generate templates
        var templates = _service.GenerateTemplates(_blockPatterns, _legalStartTimes);
        Templates = new ObservableCollection<MeetingTemplate>(templates);

        // Populate available block lengths for per-day editing
        AvailableBlockLengths = new ObservableCollection<double>(
            _legalStartTimes.Select(l => l.BlockLength).OrderBy(b => b));

        // Populate filter options
        PopulateFilterOptions();

        // Auto-select first template
        if (Templates.Count > 0)
            SelectedTemplate = Templates[0];
    }

    /// <summary>Existing meetings on the section (for augment mode).</summary>
    public IReadOnlyList<SectionDaySchedule> ExistingMeetings { get; }

    // ── Property change handlers ─────────────────────────────────────────────

    partial void OnSelectedTemplateChanged(MeetingTemplate? value)
    {
        if (value == null)
        {
            DayDurations.Clear();
            ClearSolutions();
            return;
        }

        // Populate per-day duration editors
        DayDurations = new ObservableCollection<DayDurationItem>(
            value.DaySpecs.Select(spec => new DayDurationItem(
                spec.Day,
                DayName(spec.Day),
                spec.BlockLengthHours,
                this)));

        Recompute();
    }

    partial void OnSelectedCampusChanged(string? value) => Recompute();
    partial void OnSelectedBuildingChanged(string? value) => Recompute();
    partial void OnSelectedRoomTypeChanged(string? value) => Recompute();
    partial void OnMinCapacityChanged(int? value) => Recompute();

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
        // Only pass slots for gap days (not existing meetings)
        var existingDays = new HashSet<int>(ExistingMeetings.Select(m => m.Day));
        var newSlots = solution.Slots.Where(s => !existingDays.Contains(s.Day)).ToList();

        _setGhostBlocks(null);
        _onAccept(newSlots);
    }

    [RelayCommand]
    private void Cancel()
    {
        _setGhostBlocks(null);
    }

    // ── Day duration editing ─────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="DayDurationItem"/> when the user changes a day's block length.
    /// Rebuilds the template with the new per-day specs and recomputes solutions.
    /// </summary>
    internal void OnDayDurationChanged()
    {
        if (SelectedTemplate == null) return;

        // Rebuild day specs from the current DayDurations
        var newSpecs = DayDurations.Select(dd =>
        {
            var lst = _legalStartTimes.FirstOrDefault(l => Math.Abs(l.BlockLength - dd.BlockLengthHours) < 0.01);
            var starts = lst?.StartTimes ?? new List<int>();
            return new TemplateDaySpec(dd.Day, dd.BlockLengthHours, (int)(dd.BlockLengthHours * 60), starts.AsReadOnly());
        }).ToList();

        // Create a modified template
        SelectedTemplate = new MeetingTemplate(SelectedTemplate.PatternId, SelectedTemplate.PatternName, newSpecs);
    }

    // ── Internal logic ───────────────────────────────────────────────────────

    private void Recompute()
    {
        if (SelectedTemplate == null) { ClearSolutions(); return; }

        var filteredRooms = RoomAvailabilityService.ApplyFilter(
            _allRooms,
            SelectedCampus == "(any)" ? null : _allRooms.FirstOrDefault(r => r.CampusId != null && SelectedCampus != null)?.CampusId,
            SelectedBuilding == "(any)" ? null : SelectedBuilding,
            SelectedRoomType == "(any)" ? null : _allRooms.FirstOrDefault(r => r.RoomTypeId != null && SelectedRoomType != null)?.RoomTypeId,
            MinCapacity);

        _solutions = _service.GenerateSolutions(SelectedTemplate, filteredRooms, _index, ExistingMeetings);

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

        // Convert solution slots to ghost blocks
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

    private void PopulateFilterOptions()
    {
        var buildings = _allRooms
            .Select(r => r.Building)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .OrderBy(b => b)
            .ToList();
        buildings.Insert(0, "(any)");
        BuildingOptions = new ObservableCollection<string>(buildings);
        SelectedBuilding = "(any)";

        CampusOptions = new ObservableCollection<string>(new[] { "(any)" });
        SelectedCampus = "(any)";

        RoomTypeOptions = new ObservableCollection<string>(new[] { "(any)" });
        SelectedRoomType = "(any)";
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

    private static string DayName(int day) => day switch
    {
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        _ => "?"
    };
}

/// <summary>
/// Per-day duration editor item for the Room Availability Browser panel.
/// Allows the user to change the block length for an individual day.
/// </summary>
public partial class DayDurationItem : ObservableObject
{
    private readonly RoomAvailabilityBrowserViewModel _parent;

    public DayDurationItem(int day, string dayName, double blockLengthHours, RoomAvailabilityBrowserViewModel parent)
    {
        Day = day;
        DayName = dayName;
        _blockLengthHours = blockLengthHours;
        _parent = parent;
    }

    public int Day { get; }
    public string DayName { get; }

    /// <summary>Block length options for the per-day dropdown (from the parent VM).</summary>
    public ObservableCollection<double> AvailableBlockLengths => _parent.AvailableBlockLengths;

    [ObservableProperty] private double _blockLengthHours;

    /// <summary>Duration in minutes for display.</summary>
    public int DurationMinutes => (int)(BlockLengthHours * 60);

    partial void OnBlockLengthHoursChanged(double value)
    {
        OnPropertyChanged(nameof(DurationMinutes));
        _parent.OnDayDurationChanged();
    }
}
