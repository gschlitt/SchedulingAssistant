using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.Services;

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
    /// Returns all shared sections across all loaded sets whose <see cref="Section.SemesterId"/>
    /// matches <paramref name="semesterId"/>. Used by the grid pipeline and conflict detection
    /// to merge shared sections into the local section list.
    /// </summary>
    public IReadOnlyList<Section> GetSectionsForSemester(string semesterId)
    {
        var result = new List<Section>();
        foreach (var set in _sets)
            foreach (var section in set.Sections)
                if (section.SemesterId == semesterId)
                    result.Add(section);
        return result;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(HasAny));
        Changed?.Invoke();
    }
}
