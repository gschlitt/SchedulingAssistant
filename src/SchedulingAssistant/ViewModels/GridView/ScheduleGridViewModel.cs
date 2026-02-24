using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.GridView;

public partial class ScheduleGridViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly SectionPropertyRepository _propertyRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private GridData _gridData = GridData.Empty;
    [ObservableProperty] private string? _selectedSectionId;
    [ObservableProperty] private string? _lastErrorMessage;

    /// <summary>Filter state. Exposed so the view can bind to it.</summary>
    public GridFilterViewModel Filter { get; } = new();

    public ScheduleGridViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        SubjectRepository subjectRepo,
        SectionPropertyRepository propertyRepo,
        SemesterContext semesterContext)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _subjectRepo = subjectRepo;
        _propertyRepo = propertyRepo;
        _semesterContext = semesterContext;

        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        Filter.FilterChanged += Reload;
        Reload();
    }

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
        {
            // Clear filter selections so each semester starts fresh.
            // Unsubscribe temporarily so clearing doesn't trigger multiple Reloads.
            Filter.FilterChanged -= Reload;
            Filter.ClearAll();
            Filter.FilterChanged += Reload;
            Reload();
        }
    }

    /// <summary>Called by the view when a tile is clicked; sets SelectedSectionId.</summary>
    [RelayCommand]
    public void SelectSection(string sectionId) => SelectedSectionId = sectionId;

    /// <summary>
    /// Invoked by the view when an entry is double-clicked.
    /// Set by SectionListViewModel to open the section editor.
    /// </summary>
    public Action<string>? EditRequested { get; set; }

    [RelayCommand]
    public void DismissError() => LastErrorMessage = null;

#if DEBUG
    /// <summary>
    /// DEV ONLY — forces a reload failure so the error banner can be tested visually.
    /// Remove before shipping.
    /// </summary>
    [RelayCommand]
    public void SimulateReloadError()
    {
        App.Logger.LogError(new InvalidOperationException("Simulated grid reload error"), "SimulateReloadError");
        GridData = GridData.Empty;
        LastErrorMessage = "An error occurred loading the schedule grid. See logs for details.";
    }
#endif

    public void Reload()
    {
        try
        {
            ReloadCore();
            LastErrorMessage = null;
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "ScheduleGridViewModel.Reload");
            GridData = GridData.Empty;
            LastErrorMessage = "An error occurred loading the schedule grid. See logs for details.";
        }
    }

    private void ReloadCore()
    {
        var semester = _semesterContext.SelectedSemesterDisplay?.Semester;
        if (semester is null) { GridData = GridData.Empty; return; }

        // ── Build lookup tables ────────────────────────────────────────────────
        var sections         = _sectionRepo.GetAll(semester.Id);
        var courseLookup     = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var roomLookup       = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var subjectLookup    = _subjectRepo.GetAll().ToDictionary(s => s.Id);

        var campusLookup      = _propertyRepo.GetAll(SectionPropertyTypes.Campus).ToDictionary(v => v.Id);
        var sectionTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.SectionType).ToDictionary(v => v.Id);
        var tagLookup         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var meetingTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType).ToDictionary(v => v.Id);

        // ── Rebuild filter option lists (preserves selections) ─────────────────
        Filter.PopulateOptions(
            sections,
            instructorLookup,
            roomLookup,
            subjectLookup,
            courseLookup,
            campusLookup,
            sectionTypeLookup,
            tagLookup,
            meetingTypeLookup);

        // ── Snapshot active filter sets (HashSet lookups are O(1)) ─────────────
        var selInstructors  = Filter.SelectedInstructorIds;
        var selRooms        = Filter.SelectedRoomIds;
        var selSubjects     = Filter.SelectedSubjectIds;
        var selCampuses     = Filter.SelectedCampusIds;
        var selSectionTypes = Filter.SelectedSectionTypeIds;
        var selTags         = Filter.SelectedTagIds;
        var selMeetingTypes = Filter.SelectedMeetingTypeIds;

        bool filterInstructor  = selInstructors.Count  > 0;
        bool filterRoom        = selRooms.Count        > 0;
        bool filterSubject     = selSubjects.Count     > 0;
        bool filterCampus      = selCampuses.Count     > 0;
        bool filterSectionType = selSectionTypes.Count > 0;
        bool filterTag         = selTags.Count         > 0;
        bool filterMeetingType = selMeetingTypes.Count > 0;

        var includeSaturday = AppSettings.Load().IncludeSaturday;

        // ── Build day columns ─────────────────────────────────────────────────
        var dayNumbers = new List<int> { 1, 2, 3, 4, 5 };
        if (includeSaturday) dayNumbers.Add(6);
        var dayNames = new Dictionary<int, string>
        {
            [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday",
            [4] = "Thursday", [5] = "Friday", [6] = "Saturday"
        };

        // ── Collect filtered meetings ─────────────────────────────────────────
        var allMeetings = new List<(int Day, int Start, int End, string Label, string Initials, string SectionId)>();

        foreach (var section in sections)
        {
            // ── Section-level filter ───────────────────────────────────────────
            if (filterInstructor && !section.InstructorIds.Any(id => selInstructors.Contains(id)))
                continue;

            if (filterSubject)
            {
                if (string.IsNullOrEmpty(section.CourseId) || !courseLookup.TryGetValue(section.CourseId, out var c))
                    continue;
                if (!selSubjects.Contains(c.SubjectId))
                    continue;
            }

            if (filterCampus && !selCampuses.Contains(section.CampusId ?? string.Empty))
                continue;

            if (filterSectionType && !selSectionTypes.Contains(section.SectionTypeId ?? string.Empty))
                continue;

            // Tags: AND — section must have ALL selected tags
            if (filterTag && !selTags.IsSubsetOf(section.TagIds))
                continue;

            // ── Build display label ────────────────────────────────────────────
            var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                ? course.CalendarCode : null;
            var firstId  = section.InstructorIds.FirstOrDefault();
            var initials = firstId is not null && instructorLookup.TryGetValue(firstId, out var instr)
                ? instr.Initials : string.Empty;
            var label = calCode is not null
                ? $"{calCode} {section.SectionCode}"
                : section.SectionCode;

            // ── Meeting-level filter ──────────────────────────────────────────
            foreach (var slot in section.Schedule)
            {
                if (filterRoom && !selRooms.Contains(slot.RoomId ?? string.Empty))
                    continue;
                if (filterMeetingType && !selMeetingTypes.Contains(slot.MeetingTypeId ?? string.Empty))
                    continue;

                allMeetings.Add((slot.Day, slot.StartMinutes, slot.EndMinutes, label, initials, section.Id));
            }
        }

        // ── Time range: always 08:30–22:00 ────────────────────────────────────
        const int firstRow = 8 * 60 + 30;
        const int lastRow  = 22 * 60;

        // ── Build per-day tile lists ───────────────────────────────────────────
        var dayColumns = new List<GridDayColumn>();
        foreach (var dayNum in dayNumbers)
        {
            var dayMeetings = allMeetings
                .Where(m => m.Day == dayNum)
                .OrderBy(m => m.Start).ThenBy(m => m.End)
                .Select(m => (m.Day, m.Start, m.End, m.Label, m.Initials, m.SectionId))
                .ToList();

            var tiles = ComputeTiles(dayMeetings);
            dayColumns.Add(new GridDayColumn(dayNames[dayNum], tiles));
        }

        GridData = new GridData(firstRow, lastRow, dayColumns);
    }

    /// <summary>
    /// Builds positioned tiles for one day's meetings.
    /// Meetings with identical start+end are merged into a single tile (stacked entries).
    /// Overlapping meetings (different time spans) are placed side-by-side.
    /// </summary>
    private static List<GridTile> ComputeTiles(
        List<(int Day, int Start, int End, string Label, string Initials, string SectionId)> meetings)
    {
        var tiles = new List<GridTile>();
        if (meetings.Count == 0) return tiles;

        // Step 1: merge meetings with identical start+end into combined tile entries.
        var merged = new List<(int Start, int End, List<TileEntry> Entries)>();
        var mergeIndex = new Dictionary<(int, int), int>();

        foreach (var m in meetings)
        {
            var key = (m.Start, m.End);
            if (mergeIndex.TryGetValue(key, out int idx))
            {
                merged[idx].Entries.Add(new TileEntry(m.Label, m.Initials, m.SectionId));
            }
            else
            {
                mergeIndex[key] = merged.Count;
                merged.Add((m.Start, m.End, [new TileEntry(m.Label, m.Initials, m.SectionId)]));
            }
        }

        // Sort merged tiles by start then end
        merged.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        // Step 2: group into overlap clusters
        var clusters     = new List<List<int>>();
        var clusterMaxEnd = new List<int>();

        for (int i = 0; i < merged.Count; i++)
        {
            var m = merged[i];
            int clusterIdx = -1;
            for (int c = 0; c < clusters.Count; c++)
            {
                if (m.Start < clusterMaxEnd[c]) { clusterIdx = c; break; }
            }
            if (clusterIdx == -1)
            {
                clusters.Add([i]);
                clusterMaxEnd.Add(m.End);
            }
            else
            {
                clusters[clusterIdx].Add(i);
                clusterMaxEnd[clusterIdx] = Math.Max(clusterMaxEnd[clusterIdx], m.End);
            }
        }

        // Step 3: assign column slots greedily within each cluster
        foreach (var cluster in clusters)
        {
            var colEnds = new List<int>();

            foreach (var idx in cluster)
            {
                var m = merged[idx];
                int col = -1;
                for (int c = 0; c < colEnds.Count; c++)
                {
                    if (m.Start >= colEnds[c]) { col = c; break; }
                }
                if (col == -1) { col = colEnds.Count; colEnds.Add(0); }
                colEnds[col] = m.End;

                tiles.Add(new GridTile(m.Entries, m.Start, m.End, col, cluster.Count));
            }

            // Fix OverlapCount to actual column count used
            int actualCols = colEnds.Count;
            for (int t = tiles.Count - cluster.Count; t < tiles.Count; t++)
            {
                var old = tiles[t];
                tiles[t] = old with { OverlapCount = actualCols };
            }
        }

        return tiles;
    }
}
