namespace TermPoint.Services.SectionImport;

/// <summary>
/// A fault in the import file that stops the import: any problem means nothing is imported. Each one
/// is shown to the user in plain words with the row and column to fix.
/// </summary>
/// <param name="Row">
/// The row number as Excel shows it (the header is row 1), or null when the problem is about the file
/// as a whole.
/// </param>
/// <param name="Column">
/// The template column's display name (see <see cref="SectionImportColumns"/>), or null when the
/// problem is not about one column.
/// </param>
/// <param name="Message">One or two plain sentences that say what is wrong and how to fix it.</param>
public sealed record ImportProblem(int? Row, string? Column, string Message);

/// <summary>
/// Something worth a second look that does not stop the import. Warnings are grouped, one line per
/// problem rather than one per row (spec Phase 4, 2026-10-24). Step 24.3 only defines the type; the
/// planner's second part (24.4) creates the warnings.
/// </summary>
/// <param name="Kind">A short identifier of the sort of warning, used to group and order them.</param>
/// <param name="Rows">The affected row numbers in file order; empty for a warning about the whole file.</param>
/// <param name="Message">The plain-English line shown to the user.</param>
public sealed record ImportWarning(string Kind, IReadOnlyList<int> Rows, string Message);
