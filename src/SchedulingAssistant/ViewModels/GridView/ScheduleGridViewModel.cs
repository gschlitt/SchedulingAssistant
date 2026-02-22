using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.GridView;

public partial class ScheduleGridViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private GridData _gridData = GridData.Empty;

    public ScheduleGridViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        SemesterContext semesterContext)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _semesterContext = semesterContext;

        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        Reload();
    }

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
            Reload();
    }

    public void Reload()
    {
        var semester = _semesterContext.SelectedSemesterDisplay?.Semester;
        if (semester is null) { GridData = GridData.Empty; return; }

        var sections    = _sectionRepo.GetAll(semester.Id);
        var courseLookup    = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup = _instructorRepo.GetAll().ToDictionary(i => i.Id);

        var includeSaturday = AppSettings.Load().IncludeSaturday;

        // Build day columns: 1=Mon … 5=Fri, optionally 6=Sat
        var dayNumbers = new List<int> { 1, 2, 3, 4, 5 };
        if (includeSaturday) dayNumbers.Add(6);
        var dayNames = new Dictionary<int, string>
        {
            [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday",
            [4] = "Thursday", [5] = "Friday", [6] = "Saturday"
        };

        // Collect all meetings across all sections, tagged with display info
        var allMeetings = new List<(int Day, int Start, int End, string Line1, string Line2, string Line3)>();

        foreach (var section in sections)
        {
            var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                ? course.CalendarCode : null;
            var initials = section.InstructorId is not null && instructorLookup.TryGetValue(section.InstructorId, out var instr)
                ? instr.Initials : string.Empty;

            var line1 = calCode ?? section.SectionCode;
            var line2 = calCode is not null ? section.SectionCode : string.Empty;
            var line3 = initials;

            foreach (var slot in section.Schedule)
                allMeetings.Add((slot.Day, slot.StartMinutes, slot.EndMinutes, line1, line2, line3));
        }

        // Time range: snap first start down to half-hour, always end at 2200
        const int lastRow = 22 * 60; // 1320 = 22:00
        int firstRow = allMeetings.Count > 0
            ? (allMeetings.Min(m => m.Start) / 30) * 30
            : 8 * 60; // default 08:00 when no meetings

        // Build per-day tile lists with overlap layout
        var dayColumns = new List<GridDayColumn>();
        foreach (var dayNum in dayNumbers)
        {
            var dayMeetings = allMeetings
                .Where(m => m.Day == dayNum)
                .OrderBy(m => m.Start).ThenBy(m => m.End)
                .ToList();

            var tiles = ComputeTiles(dayMeetings);
            dayColumns.Add(new GridDayColumn(dayNames[dayNum], tiles));
        }

        GridData = new GridData(firstRow, lastRow, dayColumns);
    }

    /// <summary>
    /// Assigns overlap columns to a sorted list of meetings for one day.
    /// Meetings with identical start+end are stacked (same tile slot, sequential overlap index).
    /// Overlapping meetings (one starts before another ends) are placed side-by-side.
    /// </summary>
    private static List<GridTile> ComputeTiles(
        List<(int Day, int Start, int End, string Line1, string Line2, string Line3)> meetings)
    {
        var tiles = new List<GridTile>();
        if (meetings.Count == 0) return tiles;

        // Group into overlap clusters: a cluster is a maximal set of meetings
        // where each meeting overlaps with at least one other in the set.
        var clusters = new List<List<int>>(); // each list = indices into meetings
        var clusterMaxEnd = new List<int>();

        for (int i = 0; i < meetings.Count; i++)
        {
            var m = meetings[i];
            // Find a cluster this meeting overlaps with (start < cluster's current max end)
            int clusterIdx = -1;
            for (int c = 0; c < clusters.Count; c++)
            {
                if (m.Start < clusterMaxEnd[c])
                {
                    clusterIdx = c;
                    break;
                }
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

        foreach (var cluster in clusters)
        {
            // Within a cluster, assign column slots greedily.
            // Track the end time of the last meeting placed in each column slot.
            var colEnds = new List<int>();

            foreach (var idx in cluster)
            {
                var m = meetings[idx];
                // Find a column where the previous meeting has ended
                int col = -1;
                for (int c = 0; c < colEnds.Count; c++)
                {
                    if (m.Start >= colEnds[c]) { col = c; break; }
                }
                if (col == -1) { col = colEnds.Count; colEnds.Add(0); }
                colEnds[col] = m.End;

                tiles.Add(new GridTile(m.Line1, m.Line2, m.Line3,
                    m.Start, m.End, col, cluster.Count));
            }

            // Now that we know actual number of columns used, fix OverlapCount
            int actualCols = colEnds.Count;
            // Replace tiles from this cluster with correct OverlapCount
            for (int t = tiles.Count - cluster.Count; t < tiles.Count; t++)
            {
                var old = tiles[t];
                tiles[t] = old with { OverlapCount = actualCols };
            }
        }

        return tiles;
    }
}
