namespace TermPoint.ViewModels.GridView;

/// <summary>
/// The sections the administrator has "marked" in Schedule View: a bright frame on every tile line of a marked
/// section, like circling it on paper, so two or three sections stay in view while meetings are rearranged.
/// <para>
/// Marks are never saved and any number can be set at once. They are keyed by section id, so they follow the
/// section through grid reloads, filter changes and semester switches. The set lives as long as its owner
/// (<see cref="ScheduleGridViewModel"/>), which is rebuilt with the rest of the service container when another
/// database is opened, so marks vanish then. Marking changes nothing saved, so it is allowed in read-only mode and
/// while a section card is open.
/// </para>
/// <para>
/// Not to be confused with the folded-day strip's attention marks (<c>AttentionMark</c>, <c>FoldedDayMarks</c>).
/// </para>
/// </summary>
public sealed class SectionMarks
{
    private readonly HashSet<string> _ids = [];

    /// <summary>Raised after any change that actually changed the set. Never raised for a no-op.</summary>
    public event Action? Changed;

    /// <summary>True when at least one section is marked.</summary>
    public bool HasAny => _ids.Count > 0;

    /// <summary>The ids of the marked sections. A live view: it reflects later changes.</summary>
    public IReadOnlySet<string> Ids => _ids;

    /// <summary>Whether the section is marked.</summary>
    /// <param name="sectionId">The section's id.</param>
    /// <returns>True when marked.</returns>
    public bool IsMarked(string sectionId) => _ids.Contains(sectionId);

    /// <summary>Marks the section if it is not marked, otherwise unmarks it, then raises <see cref="Changed"/>.</summary>
    /// <param name="sectionId">The section's id.</param>
    public void Toggle(string sectionId)
    {
        if (!_ids.Remove(sectionId)) _ids.Add(sectionId);
        Changed?.Invoke();
    }

    /// <summary>Unmarks every section. Raises <see cref="Changed"/> only when something was marked.</summary>
    public void Clear()
    {
        if (_ids.Count == 0) return;
        _ids.Clear();
        Changed?.Invoke();
    }
}
