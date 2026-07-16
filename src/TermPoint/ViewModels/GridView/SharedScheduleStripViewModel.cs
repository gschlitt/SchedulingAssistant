using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Drives the collapsible shared schedule strip between the filter bar and the grid.
/// Shows a collapsed summary or expanded per-source section listings.
/// </summary>
public partial class SharedScheduleStripViewModel : ObservableObject
{
    private readonly SharedScheduleService _service;
    private readonly IRoomRepository _roomRepo;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _collapsedSummary = string.Empty;

    public ObservableCollection<SharedScheduleSourceGroup> SourceGroups { get; } = new();

    public SharedScheduleStripViewModel(SharedScheduleService service, IRoomRepository roomRepo)
    {
        _service = service;
        _roomRepo = roomRepo;
        _service.Changed += Refresh;
        Refresh();
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void DismissAll()
    {
        _service.DismissAll();
    }

    [RelayCommand]
    private void DismissSource(SharedScheduleSet set)
    {
        _service.Dismiss(set);
    }

    private void Refresh()
    {
        IsVisible = _service.HasAny;

        if (!_service.HasAny)
        {
            CollapsedSummary = string.Empty;
            SourceGroups.Clear();
            IsExpanded = false;
            return;
        }

        // Build room lookup for expanded rows
        var rooms = _roomRepo.GetAll().ToDictionary(r => r.Id);

        // Collapsed summary with resolution stats: "Chemistry Dept (12, 3/4 instr) · Biology (8, all matched)"
        var parts = _service.Sets.Select(s =>
        {
            var brief = FormatResolutionBrief(s.ResolutionSummary);
            return string.IsNullOrEmpty(brief)
                ? $"{s.SourceLabel} ({s.Sections.Count})"
                : $"{s.SourceLabel} ({s.Sections.Count}, {brief})";
        });
        CollapsedSummary = string.Join(" · ", parts);

        // Rebuild source groups for expanded view
        SourceGroups.Clear();
        foreach (var set in _service.Sets)
        {
            SourceGroups.Add(new SharedScheduleSourceGroup(set, rooms));
        }
    }

    /// <summary>
    /// Formats a brief resolution indicator for the collapsed summary.
    /// Returns "all matched" when every dimension resolved, "3/4 instr" for the weakest dimension,
    /// or empty when no resolution data exists.
    /// </summary>
    private static string FormatResolutionBrief(ImportResolutionSummary? summary)
    {
        if (summary == null) return "";

        var totalInstr = summary.ResolvedInstructorCount + summary.UnresolvedInstructorCount;
        var totalRooms = summary.ResolvedRoomCount + summary.UnresolvedRoomCount;
        var totalTags = summary.ResolvedTagCount + summary.UnresolvedTagCount;

        if (summary.UnresolvedInstructorCount == 0 &&
            summary.UnresolvedRoomCount == 0 &&
            summary.UnresolvedTagCount == 0)
        {
            return "all matched";
        }

        // Show the weakest dimension (highest unresolved ratio)
        if (summary.UnresolvedInstructorCount > 0 && totalInstr > 0)
            return $"{summary.ResolvedInstructorCount}/{totalInstr} instr";
        if (summary.UnresolvedRoomCount > 0 && totalRooms > 0)
            return $"{summary.ResolvedRoomCount}/{totalRooms} rooms";
        if (summary.UnresolvedTagCount > 0 && totalTags > 0)
            return $"{summary.ResolvedTagCount}/{totalTags} tags";

        return "all matched";
    }
}

/// <summary>
/// Represents one imported shared schedule source in the expanded strip view.
/// </summary>
public class SharedScheduleSourceGroup
{
    public SharedScheduleSet Set { get; }
    public string SourceLabel => Set.SourceLabel;
    public string ExportDate => Set.ExportedAt?.ToString("yyyy-MM-dd") ?? "";
    public List<SharedScheduleSourceRow> Rows { get; }

    /// <summary>Resolution summary line, e.g. "Matched: 3/4 instructors · 8/8 rooms · 4/6 tags".</summary>
    public string? ResolutionLine { get; }

    public SharedScheduleSourceGroup(SharedScheduleSet set, Dictionary<string, Room> rooms)
    {
        Set = set;
        Rows = set.Sections.Select(s => new SharedScheduleSourceRow(s, rooms)).ToList();
        ResolutionLine = FormatResolutionLine(set.ResolutionSummary);
    }

    /// <summary>
    /// Formats the expanded resolution line: "Matched: 3/4 instructors · 8/8 rooms · 4/6 tags".
    /// Only includes dimensions that had data to resolve; null when no summary exists.
    /// </summary>
    private static string? FormatResolutionLine(ImportResolutionSummary? summary)
    {
        if (summary == null) return null;

        var parts = new List<string>();

        var totalInstr = summary.ResolvedInstructorCount + summary.UnresolvedInstructorCount;
        if (totalInstr > 0)
            parts.Add($"{summary.ResolvedInstructorCount}/{totalInstr} instructors");

        var totalRooms = summary.ResolvedRoomCount + summary.UnresolvedRoomCount;
        if (totalRooms > 0)
            parts.Add($"{summary.ResolvedRoomCount}/{totalRooms} rooms");

        var totalTags = summary.ResolvedTagCount + summary.UnresolvedTagCount;
        if (totalTags > 0)
            parts.Add($"{summary.ResolvedTagCount}/{totalTags} tags");

        if (parts.Count == 0) return null;

        return "Matched: " + string.Join(" · ", parts);
    }
}

/// <summary>
/// One section row in the expanded strip (course code, section code, initials, schedule, room shorthand).
/// </summary>
public class SharedScheduleSourceRow
{
    public string Label { get; }
    public string Initials { get; }
    public string Schedule { get; }
    public string RoomShorthand { get; }
    public string? Notes { get; }

    public SharedScheduleSourceRow(Section section, Dictionary<string, Room> rooms)
    {
        Label = string.IsNullOrEmpty(section.DisplayCourseCode)
            ? section.SectionCode
            : $"{section.DisplayCourseCode} {section.SectionCode}";

        Initials = section.DisplayInstructors != null
            ? string.Join(" ", section.DisplayInstructors.Select(i => i.Initials))
            : "";

        Schedule = FormatSchedule(section);
        RoomShorthand = FormatRoomShorthand(section, rooms);
        Notes = string.IsNullOrWhiteSpace(section.Notes) ? null : section.Notes;
    }

    /// <summary>
    /// Builds a compact room shorthand from the first meeting's room.
    /// Resolved rooms: "Rm 204 Science". Unresolved: "Rm 204 Sci" from import data.
    /// </summary>
    private static string FormatRoomShorthand(Section section, Dictionary<string, Room> rooms)
    {
        if (section.Schedule.Count == 0) return "";

        // Use the first meeting that has room info
        foreach (var meeting in section.Schedule)
        {
            if (!string.IsNullOrEmpty(meeting.RoomId) && rooms.TryGetValue(meeting.RoomId, out var room))
            {
                var building = string.IsNullOrEmpty(room.Building) ? "" : $" {room.Building}";
                return $"Rm {room.RoomNumber}{building}";
            }

            if (!string.IsNullOrEmpty(meeting.ImportedRoomNumber))
            {
                var building = string.IsNullOrEmpty(meeting.ImportedBuilding)
                    ? ""
                    : $" {meeting.ImportedBuilding}";
                return $"Rm {meeting.ImportedRoomNumber}{building}";
            }
        }

        return "";
    }

    private static string FormatSchedule(Section section)
    {
        if (section.Schedule.Count == 0) return "Unscheduled";

        var grouped = section.Schedule
            .GroupBy(m => new { m.StartMinutes, m.DurationMinutes, m.Frequency })
            .Select(g =>
            {
                var days = string.Join("", g.Select(m => DayAbbrev(m.Day)));
                var start = FormatTime(g.Key.StartMinutes);
                var end = FormatTime(g.Key.StartMinutes + g.Key.DurationMinutes);
                var freq = string.IsNullOrEmpty(g.Key.Frequency) ? "" : $" ({g.Key.Frequency})";
                return $"{days} {start}–{end}{freq}";
            });

        return string.Join(" / ", grouped);
    }

    private static string DayAbbrev(int day) => day switch
    {
        1 => "M", 2 => "T", 3 => "W", 4 => "R", 5 => "F", 6 => "S", _ => "?"
    };

    private static string FormatTime(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        var period = h >= 12 ? "PM" : "AM";
        var h12 = h > 12 ? h - 12 : (h == 0 ? 12 : h);
        return $"{h12}:{m:D2} {period}";
    }
}
