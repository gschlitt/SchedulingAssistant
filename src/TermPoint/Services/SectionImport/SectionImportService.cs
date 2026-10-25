#if !BROWSER
namespace TermPoint.Services.SectionImport;

/// <summary>
/// Orchestrates the section import for the flyout's view model: <see cref="CheckAsync"/> reads and checks a
/// file, and <see cref="Import"/> re-checks it against the database as it is now and writes it. It adds the
/// file system (the reader) and the database (the snapshot and the writer) to the pure planner.
///
/// <para><b>Threading.</b> Only the file read and parse run off the UI thread, with the usual deadline
/// (<see cref="NetworkFileOps"/>). The snapshot, the planning and the write all run on the caller's (UI)
/// thread, because the repositories share one SQLite connection that is not thread-safe.
/// <see cref="CheckAsync"/> therefore never uses <c>ConfigureAwait(false)</c> on the await that
/// follows the read.</para>
///
/// <para><b>The four rules for the shared database.</b> Nothing here touches a lock, a save or any file but
/// the import file, which is opened read-only and shared. The only database write is
/// <see cref="SectionImportWriter"/>'s.</para>
///
/// <para>Desktop only: it uses <see cref="ImportFileReader"/>.</para>
/// </summary>
public sealed class SectionImportService
{
    /// <summary>Shown when reading the file took longer than the deadline (typically a stalled network drive).</summary>
    internal const string CouldntReadMessage =
        "TermPoint couldn't read this file. If it's on a network drive, copy it to your computer and try again.";

    /// <summary>Shown when the file could not be opened (missing, no permission, locked exclusively by another program).</summary>
    internal const string CouldntOpenMessage =
        "TermPoint couldn't open this file. If it's open in another program, close it there and try again.";

    private readonly ImportFileReader _reader;
    private readonly ImportSetupSnapshotBuilder _snapshotBuilder;
    private readonly SectionImportWriter _writer;

    /// <summary>What the background file operation produced: a parse result, or "could not open".</summary>
    /// <param name="Result">The reader's result; null when the file could not be opened.</param>
    private sealed record FileRead(ImportReadResult? Result);

    /// <summary>Creates the service.</summary>
    /// <param name="reader">Turns the file's bytes into an <see cref="ImportSheet"/>.</param>
    /// <param name="snapshotBuilder">Reads the setup snapshot the planner works from.</param>
    /// <param name="writer">Writes a plan to the open database.</param>
    public SectionImportService(
        ImportFileReader reader,
        ImportSetupSnapshotBuilder snapshotBuilder,
        SectionImportWriter writer)
    {
        _reader = reader;
        _snapshotBuilder = snapshotBuilder;
        _writer = writer;
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads and parses the file off the UI thread (bounded by the network deadline), then, back on the
    /// caller's thread, builds the setup snapshot and plans the import.
    /// </summary>
    /// <param name="path">The import file (<c>.xlsx</c> or <c>.csv</c>).</param>
    /// <param name="semesterId">The id of the open semester.</param>
    /// <returns>
    /// A file problem (deadline passed, file could not be opened, or the reader's problem: wrong shape, too
    /// large, no Sections sheet...), or the parsed sheet with its report. Messages hold no path and no
    /// exception text.
    /// </returns>
    /// <exception cref="InvalidOperationException">The semester or its academic year is not in the database (from the snapshot).</exception>
    public async Task<ImportCheck> CheckAsync(string path, string semesterId)
    {
        FileRead read;
        try
        {
            var (completed, result) = await NetworkFileOps.RunAsync(() => ReadFile(path), "section import read");
            if (!completed)
                return ImportCheck.ForFileProblem(CouldntReadMessage);
            read = result!;
        }
        catch (Exception ex)
        {
            // RunAsync rethrows what the operation threw within the deadline (and a test-only fault injector
            // can throw too). ReadFile catches its own failures, so this is a last line of defence.
            LogFailure("section import read", ex);
            return ImportCheck.ForFileProblem(CouldntOpenMessage);
        }

        if (read.Result is null)
            return ImportCheck.ForFileProblem(CouldntOpenMessage);
        if (!read.Result.IsSuccess)
            return ImportCheck.ForFileProblem(read.Result.Problem!);

        // Back on the caller's thread (the await above did not use ConfigureAwait(false)): the snapshot and
        // the planner use the repositories, whose SQLite connection belongs to the UI thread.
        var sheet = read.Result.Sheet!;
        return ImportCheck.ForReport(sheet, Plan(sheet, semesterId));
    }

    /// <summary>
    /// Reads the file's bytes and parses them, on a background thread. The file is opened with
    /// <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/> so it can be read while Excel has
    /// the workbook open (<c>File.ReadAllBytes</c> allows only <see cref="FileShare.Read"/> and would fail).
    /// A file over the size limit is refused without being read into memory. Never throws: a failure to open
    /// or read is logged (type and error code only, no path) and reported as a null result.
    /// </summary>
    /// <param name="path">The import file.</param>
    /// <returns>The reader's result, or a <see cref="FileRead"/> with no result when the file could not be opened or read.</returns>
    private FileRead ReadFile(string path)
    {
        try
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > ImportFileReader.MaxFileBytes)
                    return new FileRead(ImportReadResult.Failure(ImportFileReader.TooLargeMessage));

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            return new FileRead(_reader.Read(bytes, path));
        }
        catch (Exception ex)
        {
            LogFailure("section import read", ex);
            return new FileRead(null);
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Imports the checked file. Rebuilds the snapshot from the database as it is now and re-plans the
    /// <b>same sheet</b> (the file is not read again), then compares the result with what the user was shown
    /// (decision D4, see <see cref="ImportReportSignature"/>). Must be called on the UI thread.
    /// <list type="bullet">
    /// <item>The new report has problems, or reads differently: nothing is written; returns "changed" with the new report.</item>
    /// <item>It has no new sections: nothing is written; returns "written" with zero counts.</item>
    /// <item>Otherwise the plan is written in one transaction and the counts are returned.</item>
    /// </list>
    /// </summary>
    /// <param name="sheet">The parsed file from <see cref="CheckAsync"/>.</param>
    /// <param name="shown">The report the user was shown (<see cref="ImportCheck.Report"/>).</param>
    /// <param name="semesterId">The id of the open semester.</param>
    /// <returns>The outcome, carrying the report planned just now.</returns>
    /// <exception cref="InvalidOperationException">The semester or its academic year is not in the database.</exception>
    /// <exception cref="Exception">
    /// Whatever <see cref="SectionImportWriter.Write"/> throws (for instance a refused write in a read-only
    /// session), after its rollback. The view model decides the message.
    /// </exception>
    public ImportOutcome Import(ImportSheet sheet, ImportReport shown, string semesterId)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(shown);

        var report = Plan(sheet, semesterId);

        if (report.HasProblems || !ImportReportSignature.AreEqual(shown, report))
            return ImportOutcome.ForChanged(report);

        if (report.Plan.NewSections.Count == 0)
            return ImportOutcome.ForWritten(ImportCounts.None, report);

        var counts = _writer.Write(report.Plan, semesterId);
        return ImportOutcome.ForWritten(counts, report);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a fresh snapshot of the database and plans the sheet against it.</summary>
    /// <param name="sheet">The parsed file.</param>
    /// <param name="semesterId">The open semester.</param>
    /// <returns>The report.</returns>
    private ImportReport Plan(ImportSheet sheet, string semesterId) =>
        SectionImportPlanner.Plan(sheet, _snapshotBuilder.Build(semesterId));

    /// <summary>
    /// Logs a file failure without the path or the exception's text (both can name the user's folders):
    /// only the exception type and its error code.
    /// </summary>
    /// <param name="what">What was being done.</param>
    /// <param name="ex">The failure.</param>
    private static void LogFailure(string what, Exception ex) =>
        App.Logger.LogInfo($"SectionImportService: {what} failed: {ex.GetType().Name} (HResult 0x{ex.HResult:X8})");
}
#endif
