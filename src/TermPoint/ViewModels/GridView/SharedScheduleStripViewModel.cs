using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _collapsedSummary = string.Empty;

    public ObservableCollection<SharedScheduleSourceGroup> SourceGroups { get; } = new();

    public SharedScheduleStripViewModel(SharedScheduleService service)
    {
        _service = service;
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

        // Collapsed summary: "Chemistry Dept (12) · Biology (8)"
        var parts = _service.Sets.Select(s => $"{s.SourceLabel} ({s.Sections.Count})");
        CollapsedSummary = string.Join(" · ", parts);

        // Rebuild source groups for expanded view
        SourceGroups.Clear();
        foreach (var set in _service.Sets)
        {
            SourceGroups.Add(new SharedScheduleSourceGroup(set));
        }
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

    public SharedScheduleSourceGroup(SharedScheduleSet set)
    {
        Set = set;
        Rows = set.Sections.Select(s => new SharedScheduleSourceRow(s)).ToList();
    }
}

/// <summary>
/// One section row in the expanded strip (course code, section code, schedule, notes).
/// </summary>
public class SharedScheduleSourceRow
{
    public string Label { get; }
    public string Schedule { get; }
    public string? Notes { get; }

    public SharedScheduleSourceRow(SharedSection section)
    {
        Label = $"{section.CourseCode} {section.SectionCode}";
        Schedule = FormatSchedule(section);
        Notes = string.IsNullOrWhiteSpace(section.Notes) ? null : section.Notes;
    }

    private static string FormatSchedule(SharedSection section)
    {
        if (section.Meetings.Count == 0) return "Unscheduled";

        var grouped = section.Meetings
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
        return m == 0 ? $"{h12}:{m:D2} {period}" : $"{h12}:{m:D2} {period}";
    }
}
