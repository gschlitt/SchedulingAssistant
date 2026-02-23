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
        var allMeetings = new List<(int Day, int Start, int End, string Label, string Initials)>();

        foreach (var section in sections)
        {
            var calCode = section.CourseId is not null && courseLookup.TryGetValue(section.CourseId, out var course)
                ? course.CalendarCode : null;
            // For grid display use the first instructor's initials (multi-instructor: join if needed)
            var firstId = section.InstructorIds.FirstOrDefault();
            var initials = firstId is not null && instructorLookup.TryGetValue(firstId, out var instr)
                ? instr.Initials : string.Empty;

            // "HIST101 A" or just section code if no course assigned
            var label = calCode is not null
                ? $"{calCode} {section.SectionCode}"
                : section.SectionCode;

            foreach (var slot in section.Schedule)
                allMeetings.Add((slot.Day, slot.StartMinutes, slot.EndMinutes, label, initials));
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
                .Select(m => (m.Day, m.Start, m.End, m.Label, m.Initials))
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
        List<(int Day, int Start, int End, string Label, string Initials)> meetings)
    {
        var tiles = new List<GridTile>();
        if (meetings.Count == 0) return tiles;

        // Step 1: merge meetings with identical start+end into combined tile entries.
        // Key = (Start, End); value = accumulated entries.
        var merged = new List<(int Start, int End, List<TileEntry> Entries)>();
        var mergeIndex = new Dictionary<(int, int), int>();

        foreach (var m in meetings)
        {
            var key = (m.Start, m.End);
            if (mergeIndex.TryGetValue(key, out int idx))
            {
                merged[idx].Entries.Add(new TileEntry(m.Label, m.Initials));
            }
            else
            {
                mergeIndex[key] = merged.Count;
                merged.Add((m.Start, m.End, [new TileEntry(m.Label, m.Initials)]));
            }
        }

        // Sort merged tiles by start then end (for cluster algorithm)
        merged.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        // Step 2: group into overlap clusters (maximal sets where each tile overlaps at least one other)
        var clusters = new List<List<int>>();
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

        // Step 3: within each cluster assign column slots greedily
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
