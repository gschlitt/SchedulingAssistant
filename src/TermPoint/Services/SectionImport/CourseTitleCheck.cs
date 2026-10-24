namespace TermPoint.Services.SectionImport;

/// <summary>
/// Invariant 5: a new course has one title. A new course takes the first title the file gives it, so
/// a later row that gives it a different title would be silently ignored; it is reported instead.
/// Checked across all the rows of a new course, whichever of its sections they belong to.
/// </summary>
internal static class CourseTitleCheck
{
    /// <summary>
    /// Reports, for each new course, every distinct title after the first, once, on the first row that
    /// gives it. Titles are compared ignoring case and spacing; blank titles are ignored.
    /// </summary>
    /// <param name="newSections">The sections the import will add.</param>
    /// <param name="problems">Where problems are added.</param>
    public static void Run(IEnumerable<SectionDraft> newSections, List<ImportProblem> problems)
    {
        var byCourse = newSections
            .Where(s => s.Course.IsNew)
            .GroupBy(s => s.Course);

        foreach (var course in byCourse)
        {
            var titles = course
                .SelectMany(s => s.CellsByRow(SectionImportColumns.CourseTitle))
                .Where(c => c.Text.Length > 0)
                .OrderBy(c => c.Row)
                .ToList();
            if (titles.Count == 0)
                continue;

            var first = titles[0];
            var reported = new HashSet<string>(NameMatch.Comparer) { first.Text };
            foreach (var (row, text) in titles.Skip(1))
            {
                if (!reported.Add(text))
                    continue;
                var code = course.Key.Record.CalendarCode;
                problems.Add(new ImportProblem(row, SectionImportColumns.CourseTitle,
                    $"{code}'s {SectionImportColumns.CourseTitle} differs from row {first.Row} ({first.Text})."));
            }
        }
    }
}
