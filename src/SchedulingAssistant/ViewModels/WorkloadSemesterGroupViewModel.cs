using Avalonia;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Represents one semester's workload items (sections and releases) within a
/// <see cref="WorkloadRowViewModel"/> when the view is in multi-semester mode.
/// Non-observable: rows are fully rebuilt on every load.
/// </summary>
public class WorkloadSemesterGroupViewModel
{
    /// <summary>Database ID of the semester.</summary>
    public required string SemesterId { get; init; }

    /// <summary>Display name of the semester, e.g., "Fall 2025".</summary>
    public required string SemesterName { get; init; }

    /// <summary>Sections and releases for this instructor in this semester.</summary>
    public ObservableCollection<WorkloadItemViewModel> Items { get; init; } = new();

    /// <summary>Sum of all workload values for this semester.</summary>
    public decimal SemesterTotal => Items.Sum(i => i.WorkloadValue);

    /// <summary>Semester color brush resolved from AppColors, used to frame the group.</summary>
    public IBrush? SemesterColorBrush { get; init; }

    /// <summary>
    /// Maps a semester name (e.g., "Fall 2025", "Summer 2026") to its AppColors background brush.
    /// Names are matched by their first word: Fall → FallBackground, Early Summer → EarlySummerBackground, etc.
    /// </summary>
    public static IBrush? ResolveBrush(string semesterName)
    {
        var firstWord = semesterName.Split(' ')[0];
        var key = firstWord switch
        {
            "Fall" => "FallBackground",
            "Winter" => "WinterBackground",
            "Early" => "EarlySummerBackground",
            "Summer" => "SummerBackground",
            "Late" => "LateSummerBackground",
            _ => "FallBackground"
        };

        if (Application.Current?.Resources.TryGetResource(key, null, out var brush) ?? false)
            return brush as IBrush;

        return null;
    }
}
