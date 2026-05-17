using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.GridView;

namespace SchedulingAssistant.Services;

/// <summary>
/// Holds actively loaded shared schedule sets (cross-department CSV imports).
/// All data is transient (in-memory only). The grid VM subscribes to <see cref="Changed"/>
/// to re-render tiles when sets are added or dismissed.
/// </summary>
public class SharedScheduleService : ObservableObject
{
    private readonly List<SharedScheduleSet> _sets = new();

    /// <summary>All currently loaded shared schedule sets.</summary>
    public IReadOnlyList<SharedScheduleSet> Sets => _sets;

    /// <summary>True when at least one shared schedule is loaded.</summary>
    public bool HasAny => _sets.Count > 0;

    /// <summary>Fires when the set collection changes (import, dismiss, dismiss-all).</summary>
    public event Action? Changed;

    /// <summary>Adds a parsed set and notifies subscribers.</summary>
    public void Add(SharedScheduleSet set)
    {
        _sets.Add(set);
        OnChanged();
    }

    /// <summary>Removes a single set by reference and notifies subscribers.</summary>
    public void Dismiss(SharedScheduleSet set)
    {
        _sets.Remove(set);
        OnChanged();
    }

    /// <summary>Removes all sets and notifies subscribers.</summary>
    public void DismissAll()
    {
        if (_sets.Count == 0) return;
        _sets.Clear();
        OnChanged();
    }

    /// <summary>
    /// Builds <see cref="SharedScheduleBlock"/> instances for all loaded sets.
    /// Called by the grid VM during its block assembly pipeline.
    /// </summary>
    /// <param name="semesterId">Active semester ID for block routing in multi-semester mode.</param>
    /// <param name="semesterName">Display name of the active semester.</param>
    /// <param name="semesterColor">Hex color string for the semester.</param>
    public List<SharedScheduleBlock> BuildBlocks(string semesterId, string semesterName, string semesterColor)
    {
        var blocks = new List<SharedScheduleBlock>();
        foreach (var set in _sets)
        {
            foreach (var section in set.Sections)
            {
                var label = $"{section.CourseCode} {section.SectionCode}";
                foreach (var mtg in section.Meetings)
                {
                    blocks.Add(new SharedScheduleBlock(
                        mtg.Day, mtg.StartMinutes, mtg.EndMinutes,
                        label,
                        SectionDaySchedule.FormatFrequency(mtg.Frequency),
                        set.SourceLabel,
                        section.Notes ?? "",
                        semesterId, semesterName, semesterColor));
                }
            }
        }
        return blocks;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(HasAny));
        Changed?.Invoke();
    }
}
