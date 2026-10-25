namespace TermPoint.Services.SectionImport;

/// <summary>
/// The result of checking an import file (<c>SectionImportService.CheckAsync</c>): either one problem
/// with the file as a whole (it could not be opened or read, or it is not in the template's shape), or the
/// parsed <see cref="ImportSheet"/> together with the <see cref="ImportReport"/> planned from it. Exactly
/// one of <see cref="FileProblem"/> and <see cref="Report"/> is set.
/// </summary>
public sealed class ImportCheck
{
    /// <summary>One plain-English problem with the file as a whole, or null when the file was read and planned.</summary>
    public string? FileProblem { get; }

    /// <summary>The parsed file, kept so Import can re-plan the same rows without reading the file again; null when <see cref="FileProblem"/> is set.</summary>
    public ImportSheet? Sheet { get; }

    /// <summary>What the file would do to the open semester; null when <see cref="FileProblem"/> is set.</summary>
    public ImportReport? Report { get; }

    /// <summary>True when the file itself could not be used, so there is no sheet and no report.</summary>
    public bool HasFileProblem => FileProblem is not null;

    private ImportCheck(string? fileProblem, ImportSheet? sheet, ImportReport? report)
    {
        FileProblem = fileProblem;
        Sheet = sheet;
        Report = report;
    }

    /// <summary>Creates the result for a file that could not be opened, read or parsed.</summary>
    /// <param name="problem">The plain-English problem, shown to the user as it is.</param>
    /// <returns>A check whose <see cref="FileProblem"/> is <paramref name="problem"/>.</returns>
    public static ImportCheck ForFileProblem(string problem) => new(problem, null, null);

    /// <summary>Creates the result for a file that was read and planned.</summary>
    /// <param name="sheet">The parsed file.</param>
    /// <param name="report">The plan and the problems and warnings found.</param>
    /// <returns>A check holding the sheet and its report.</returns>
    public static ImportCheck ForReport(ImportSheet sheet, ImportReport report) => new(null, sheet, report);
}

/// <summary>What <c>SectionImportService.Import</c> did.</summary>
public enum ImportOutcomeKind
{
    /// <summary>The plan was written (or there was nothing to add, so nothing was written).</summary>
    Written,

    /// <summary>
    /// Something in the database changed since the file was checked, so the new result differs from what the
    /// user saw. Nothing was written; the new report is to be shown instead (spec decision D4).
    /// </summary>
    Changed,
}

/// <summary>
/// The result of <c>SectionImportService.Import</c>: either the import was written, with what was inserted,
/// or the database changed since the check and nothing was written. Either way it carries the report that was
/// planned just now, from the database as it is now.
/// </summary>
public sealed class ImportOutcome
{
    /// <summary>Whether the import was written or the database changed.</summary>
    public ImportOutcomeKind Kind { get; }

    /// <summary>What was inserted; <see cref="ImportCounts.None"/> when <see cref="Kind"/> is <see cref="ImportOutcomeKind.Changed"/> or there was nothing to add.</summary>
    public ImportCounts Counts { get; }

    /// <summary>The report planned at Import time (the up-to-date result the view model shows for a change).</summary>
    public ImportReport Report { get; }

    /// <summary>True when the import was written (or had nothing to add).</summary>
    public bool WasWritten => Kind == ImportOutcomeKind.Written;

    private ImportOutcome(ImportOutcomeKind kind, ImportCounts counts, ImportReport report)
    {
        Kind = kind;
        Counts = counts;
        Report = report;
    }

    /// <summary>Creates the outcome of a write.</summary>
    /// <param name="counts">What was inserted.</param>
    /// <param name="report">The report the write was made from.</param>
    /// <returns>An outcome of kind <see cref="ImportOutcomeKind.Written"/>.</returns>
    public static ImportOutcome ForWritten(ImportCounts counts, ImportReport report) =>
        new(ImportOutcomeKind.Written, counts, report);

    /// <summary>Creates the outcome of an import refused because the database changed.</summary>
    /// <param name="report">The new report.</param>
    /// <returns>An outcome of kind <see cref="ImportOutcomeKind.Changed"/> with zero counts.</returns>
    public static ImportOutcome ForChanged(ImportReport report) =>
        new(ImportOutcomeKind.Changed, ImportCounts.None, report);
}
