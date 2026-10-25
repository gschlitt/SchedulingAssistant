namespace TermPoint.Services.SectionImport;

/// <summary>
/// A fingerprint of an <see cref="ImportReport"/> made of everything the user could have seen in the
/// preview, used at Import time to tell whether the database changed since the file was checked
/// (spec decision D4).
///
/// <para>The signature is a list of lines, one for every problem (row, column, message), every warning
/// message, every skipped section's label, every new record's identity (subjects by abbreviation and name;
/// campuses, section types, meeting types and tags by name; rooms by building, number and campus; courses by
/// code and title; instructors by last name, first name and initials) and every new section's label. It
/// holds <b>no</b> ids, sort orders or other values the user does not see, except that an <i>existing</i>
/// campus (whose id is stable) stands for itself in a room's line. Two reports made from the same sheet and
/// an unchanged database therefore have equal signatures even though every run builds its new records with
/// fresh ids.</para>
///
/// <para>The lines are sorted (ordinally), so the signature does not depend on the order the planner happens
/// to list things in. Each line starts with its kind, so equal text of two kinds never collides.</para>
/// </summary>
public static class ImportReportSignature
{
    /// <summary>Separates the parts of one line; a character that cannot appear in a typed cell.</summary>
    private const char Separator = '\u001F';

    /// <summary>
    /// Builds the signature of a report.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <returns>The signature lines, sorted ordinally; never null.</returns>
    public static IReadOnlyList<string> Of(ImportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var plan = report.Plan;

        // A new room names its campus by id; a new campus has a fresh id each run, so it is named by its
        // name instead. An existing campus keeps its id between runs, so the id stands for it.
        var newCampusNames = plan.NewCampuses.ToDictionary(c => c.Record.Id, c => c.Record.Name);
        string CampusText(string? campusId) =>
            string.IsNullOrEmpty(campusId) ? string.Empty
            : newCampusNames.TryGetValue(campusId, out var name) ? "new:" + name
            : "existing:" + campusId;

        var lines = new List<string>();
        lines.AddRange(report.Problems.Select(p => Line("problem", p.Row?.ToString() ?? "", p.Column ?? "", p.Message)));
        lines.AddRange(report.Warnings.Select(w => Line("warning", w.Message)));
        lines.AddRange(report.SkippedSections.Select(s => Line("skipped", s.Key.Label)));
        lines.AddRange(plan.NewSubjects.Select(s => Line("subject", s.Record.CalendarAbbreviation, s.Record.Name)));
        lines.AddRange(plan.NewCampuses.Select(c => Line("campus", c.Record.Name)));
        lines.AddRange(plan.NewSectionTypes.Select(t => Line("sectionType", t.Record.Name)));
        lines.AddRange(plan.NewMeetingTypes.Select(t => Line("meetingType", t.Record.Name)));
        lines.AddRange(plan.NewTags.Select(t => Line("tag", t.Record.Name)));
        lines.AddRange(plan.NewRooms.Select(r => Line("room", r.Record.Building, r.Record.RoomNumber, CampusText(r.Record.CampusId))));
        lines.AddRange(plan.NewCourses.Select(c => Line("course", c.Record.CalendarCode, c.Record.Title)));
        lines.AddRange(plan.NewInstructors.Select(i => Line("instructor", i.Record.LastName, i.Record.FirstName, i.Record.Initials)));
        lines.AddRange(plan.NewSections.Select(s => Line("section", s.Key.Label)));

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>
    /// True when two reports show the user the same thing: their signatures are the same lines.
    /// </summary>
    /// <param name="a">One report.</param>
    /// <param name="b">The other report.</param>
    /// <returns>True when <see cref="Of"/> gives equal lists for both.</returns>
    public static bool AreEqual(ImportReport a, ImportReport b) => Of(a).SequenceEqual(Of(b), StringComparer.Ordinal);

    /// <summary>Joins the parts of one signature line under its kind.</summary>
    /// <param name="kind">The kind of line ("problem", "room", ...).</param>
    /// <param name="parts">The parts, in a fixed order for the kind.</param>
    private static string Line(string kind, params string[] parts) => kind + Separator + string.Join(Separator, parts);
}
