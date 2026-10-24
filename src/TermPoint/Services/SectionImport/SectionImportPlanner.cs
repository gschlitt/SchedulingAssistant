namespace TermPoint.Services.SectionImport;

/// <summary>
/// The first part of the section-import planner (spec item 24, step 24.3). A pure function of the
/// file's rows and a snapshot of the open semester's setup data: it groups the rows into sections and
/// resolves every value they name (subject, course, instructors, section type, campus, tags, meeting
/// types, rooms) to an existing record, a new record to create, or a problem. It never touches the
/// database. Step 24.4 adds the remaining validation rules and the warnings, building on the
/// <see cref="SectionDraft"/>s produced here.
/// </summary>
public static class SectionImportPlanner
{
    /// <summary>
    /// Plans an import. All value comparisons ignore case and spacing (<see cref="NameMatch"/>); course
    /// codes ignore all spaces. Every problem is collected rather than stopping at the first, except
    /// that a file whose every row names a different semester gets exactly one file-wide problem and
    /// nothing else.
    /// </summary>
    /// <param name="sheet">The rows of the file, as read by <see cref="ImportFileReader"/>.</param>
    /// <param name="snapshot">The database state to match against.</param>
    /// <returns>
    /// The report: problems (any problem means the import must not run), skipped sections (already in
    /// the open semester), the plan of what would be created, and whether the semester was empty.
    /// <see cref="ImportReport.Warnings"/> is empty until step 24.4.
    /// </returns>
    /// <exception cref="ArgumentNullException">The sheet or the snapshot is null.</exception>
    public static ImportReport Plan(ImportSheet sheet, ImportSetupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SectionImportPlanningRun(sheet, snapshot).Execute();
    }
}
