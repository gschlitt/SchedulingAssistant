namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Remembers what each folded strip was marking, so the view can flash a strip only when something NEW arrives
/// (collapse days, P2-4). Pure: no Avalonia types. One instance per view.
/// <para>
/// A mark's identity is its kind, its section id (or none) and its exact span: the same selection redrawn is the same
/// mark, while a ghost that moves to another time, or a newly selected section, is a new mark.
/// </para>
/// </summary>
public sealed class StripAttention
{
    /// <summary>What makes two marks "the same mark" (kind + section + span).</summary>
    private readonly record struct MarkId(MarkKind Kind, string SectionId, int Start, int End);

    /// <summary>The identities each folded day was marking the last time <see cref="Update"/> ran.</summary>
    private readonly Dictionary<int, HashSet<MarkId>> _last = new();

    /// <summary>
    /// Records the current marks of every folded day and returns the days that should flash: those whose mark set
    /// GAINED a mark it did not have before. A day that was not folded last time is seeded with its marks and does not
    /// flash (folding a day that already holds a selection is not news); a redraw with the same marks, or with marks
    /// removed, does not flash; a day that is no longer folded is forgotten, so folding it again re-seeds it.
    /// </summary>
    /// <param name="foldedDays">Each currently folded day (1 = Monday ... 7 = Sunday) with its resolved marks.</param>
    /// <returns>The days to flash, ascending; empty when nothing new arrived.</returns>
    public IReadOnlyList<int> Update(IReadOnlyDictionary<int, IReadOnlyList<AttentionMark>> foldedDays)
    {
        var flash = new List<int>();

        // Days that opened are forgotten.
        foreach (int day in _last.Keys.Where(d => !foldedDays.ContainsKey(d)).ToList())
            _last.Remove(day);

        foreach (var (day, marks) in foldedDays.OrderBy(kv => kv.Key))
        {
            var current = marks.Select(m => new MarkId(m.Kind, m.SectionId ?? "", m.Start, m.End)).ToHashSet();
            // Seeded days (newly folded) have no previous set, so nothing counts as new.
            if (_last.TryGetValue(day, out var previous) && current.Any(id => !previous.Contains(id)))
                flash.Add(day);
            _last[day] = current;
        }
        return flash;
    }

    /// <summary>Forgets everything (the next <see cref="Update"/> seeds every folded day again).</summary>
    public void Reset() => _last.Clear();
}
