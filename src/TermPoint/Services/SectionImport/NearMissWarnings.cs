using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// The near-miss warnings for new setup values (design Phase 4, "Near-miss warnings for new setup
/// values"): a value the import will create that looks like a typo of an existing value of the same
/// kind, or of another new one. There is no warning just because something is new; the preview
/// already lists every new record. Subjects are compared by abbreviation; section types, meeting
/// types, tags and campuses by name; rooms only by <b>building</b> (neighbouring room numbers differ
/// by one character by nature), and only when the building is itself new.
/// </summary>
internal sealed class NearMissWarnings
{
    /// <summary>A new value as a warning names it: how it was first typed, and the rows that name it.</summary>
    /// <param name="Name">The value as first typed.</param>
    /// <param name="Rows">The distinct rows (ascending) of the sections that name it.</param>
    private sealed record NewValue(string Name, IReadOnlyList<int> Rows);

    private readonly ImportSetupSnapshot _snapshot;
    private readonly ImportPlan _plan;
    private readonly List<ImportWarning> _warnings = new();

    /// <summary>Creates the check over a snapshot and the plan it applies to.</summary>
    /// <param name="snapshot">Supplies the existing values.</param>
    /// <param name="plan">Supplies the new values and the sections that name them.</param>
    public NearMissWarnings(ImportSetupSnapshot snapshot, ImportPlan plan)
    {
        _snapshot = snapshot;
        _plan = plan;
    }

    /// <summary>Produces the near-miss warnings for every kind of setup value.</summary>
    /// <returns>The warnings, kind by kind; the caller orders them.</returns>
    public IReadOnlyList<ImportWarning> Collect()
    {
        var sections = _plan.NewSections;

        CheckRefs("subject", "subjects", _plan.NewSubjects,
            _snapshot.Subjects.Select(s => s.CalendarAbbreviation), s => s.CalendarAbbreviation,
            RowsBySingle(sections, s => s.Course.Subject is { IsNew: true } subject ? subject : null,
                s => s.RowNumbers[0]));

        CheckRefs("section type", "section types", _plan.NewSectionTypes,
            _snapshot.SectionTypes.Select(v => v.Name), v => v.Name,
            RowsBySingle(sections, s => s.SectionType, s => CellRow(s, SectionImportColumns.SectionType)));

        CheckRefs("meeting type", "meeting types", _plan.NewMeetingTypes,
            _snapshot.MeetingTypes.Select(v => v.Name), v => v.Name,
            RowsByMeetingRow(sections, m => m.MeetingType));

        CheckRefs("tag", "tags", _plan.NewTags,
            _snapshot.Tags.Select(v => v.Name), v => v.Name,
            RowsByMany(sections, s => s.Tags, s => CellRow(s, SectionImportColumns.Tags)));

        CheckRefs("campus", "campuses", _plan.NewCampuses,
            _snapshot.Campuses.Select(c => c.Name), c => c.Name,
            RowsBySingle(sections, s => s.Campus, s => CellRow(s, SectionImportColumns.Campus)));

        CheckBuildings();
        return _warnings;
    }

    // ── One kind of value ─────────────────────────────────────────────────────

    /// <summary>
    /// Checks the new records of one kind (subjects, section types, ...) against the existing values
    /// and each other.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="singular">The kind's name for one value ("tag").</param>
    /// <param name="plural">The kind's name for several ("tags").</param>
    /// <param name="newRefs">The new records of this kind.</param>
    /// <param name="existingNames">The names (or abbreviations) of the existing records of this kind.</param>
    /// <param name="nameOf">Gives a new record's name (or abbreviation).</param>
    /// <param name="rows">The rows that name each new record.</param>
    private void CheckRefs<T>(
        string singular,
        string plural,
        IReadOnlyList<SetupRef<T>> newRefs,
        IEnumerable<string> existingNames,
        Func<T, string> nameOf,
        RowGrouping<object> rows) where T : class
    {
        var added = newRefs
            .Select(r => new NewValue(nameOf(r.Record), rows.Find(r)?.Rows ?? Array.Empty<int>()))
            .Where(v => v.Rows.Count > 0)
            .ToList();
        CheckKind(singular, plural, added, existingNames.ToList());
    }

    /// <summary>
    /// Writes the warnings of one kind: one per new value that is a near miss of existing values, and one
    /// per pair of new values that are near misses of each other.
    /// </summary>
    /// <param name="singular">The kind's name for one value.</param>
    /// <param name="plural">The kind's name for several.</param>
    /// <param name="added">The new values.</param>
    /// <param name="existing">The existing values' names.</param>
    private void CheckKind(string singular, string plural, IReadOnlyList<NewValue> added, IReadOnlyList<string> existing)
    {
        for (var i = 0; i < added.Count; i++)
        {
            var near = existing
                .Where(e => NameMatch.IsNearMiss(added[i].Name, e))
                .Distinct(NameMatch.Comparer)
                .ToList();
            if (near.Count > 0)
                Add(added[i].Rows,
                    $"Will add {singular} {added[i].Name}. {ImportText.AlreadyExist(near)} ({ImportRowList.Format(added[i].Rows)}).");

            // Each new-new pair once: only look at the values after this one.
            for (var j = i + 1; j < added.Count; j++)
            {
                if (!NameMatch.IsNearMiss(added[i].Name, added[j].Name))
                    continue;
                var both = added[i].Rows.Union(added[j].Rows).OrderBy(r => r).ToList();
                Add(both,
                    $"Will add {plural} {added[i].Name} and {added[j].Name}. " +
                    $"Check they're meant to be different ({ImportRowList.Format(both)}).");
            }
        }
    }

    // ── Rooms: only a new building ────────────────────────────────────────────

    /// <summary>
    /// Checks the buildings of the new rooms. A building that already exists is never warned about; a
    /// new building is a near miss when it resembles an existing building or another new one.
    /// </summary>
    private void CheckBuildings()
    {
        var existingBuildings = _snapshot.Rooms
            .Select(r => r.Building)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(NameMatch.Comparer)
            .ToList();

        // One entry per new building (ignoring case and spacing), with every row that names a room in it.
        var rowsByBuilding = new RowGrouping<string>(NameMatch.Comparer);
        var typedName = new Dictionary<string, string>(NameMatch.Comparer);
        foreach (var section in _plan.NewSections)
            foreach (var meetingRow in section.MeetingRows.Where(m => m.Room is { IsNew: true }))
            {
                var building = meetingRow.Room!.Record.Building;
                if (existingBuildings.Contains(building, NameMatch.Comparer))
                    continue;
                typedName.TryAdd(building, building);
                rowsByBuilding.Add(building, meetingRow.RowNumber);
            }

        var added = rowsByBuilding.Entries.Select(e => new NewValue(typedName[e.Key], e.Rows)).ToList();
        CheckKind("building", "buildings", added, existingBuildings);
    }

    // ── Which rows name each new record ───────────────────────────────────────

    /// <summary>The row of a section where a column's value was given (its first non-blank cell).</summary>
    /// <param name="section">The section.</param>
    /// <param name="column">The template column.</param>
    /// <returns>The row number, or the section's first row if the column is blank.</returns>
    private static int CellRow(SectionDraft section, string column) =>
        ImportRowText.FirstNonBlank(section.Rows, column)?.Row.RowNumber ?? section.RowNumbers[0];

    /// <summary>Groups, by new record, the rows of sections that name it through one reference each.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="sections">The new sections.</param>
    /// <param name="refOf">Gives the section's reference of this kind, or null.</param>
    /// <param name="rowOf">Gives the row to report for the section.</param>
    private static RowGrouping<object> RowsBySingle<T>(
        IEnumerable<SectionDraft> sections, Func<SectionDraft, SetupRef<T>?> refOf, Func<SectionDraft, int> rowOf)
        where T : class
    {
        var grouping = new RowGrouping<object>(ReferenceEqualityComparer.Instance);
        foreach (var section in sections)
            if (refOf(section) is { IsNew: true } reference)
                grouping.Add(reference, rowOf(section));
        return grouping;
    }

    /// <summary>Groups, by new record, the rows of sections that name it among several references.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="sections">The new sections.</param>
    /// <param name="refsOf">Gives the section's references of this kind.</param>
    /// <param name="rowOf">Gives the row to report for the section.</param>
    private static RowGrouping<object> RowsByMany<T>(
        IEnumerable<SectionDraft> sections, Func<SectionDraft, IEnumerable<SetupRef<T>>> refsOf, Func<SectionDraft, int> rowOf)
        where T : class
    {
        var grouping = new RowGrouping<object>(ReferenceEqualityComparer.Instance);
        foreach (var section in sections)
            foreach (var reference in refsOf(section).Where(r => r.IsNew))
                grouping.Add(reference, rowOf(section));
        return grouping;
    }

    /// <summary>Groups, by new record, the meeting rows that name it (each meeting row reports its own row).</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="sections">The new sections.</param>
    /// <param name="refOf">Gives the meeting row's reference of this kind, or null.</param>
    private static RowGrouping<object> RowsByMeetingRow<T>(
        IEnumerable<SectionDraft> sections, Func<MeetingRowDraft, SetupRef<T>?> refOf) where T : class
    {
        var grouping = new RowGrouping<object>(ReferenceEqualityComparer.Instance);
        foreach (var meetingRow in sections.SelectMany(s => s.MeetingRows))
            if (refOf(meetingRow) is { IsNew: true } reference)
                grouping.Add(reference, meetingRow.RowNumber);
        return grouping;
    }

    /// <summary>Adds a near-miss warning.</summary>
    /// <param name="rows">The rows it affects.</param>
    /// <param name="message">The finished message.</param>
    private void Add(IReadOnlyList<int> rows, string message) =>
        _warnings.Add(new ImportWarning(ImportWarningKinds.NearMiss, rows, message));
}
