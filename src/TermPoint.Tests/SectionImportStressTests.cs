using System.Diagnostics;
using System.Text;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.Management;
using Xunit;
using Xunit.Abstractions;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Stress tests of the section import against throwaway databases: the largest file the importer accepts, files
/// one row or one byte over the limits, a file in which every row is broken, re-importing at scale, hostile
/// text, Excel's odd cell types, CSV encodings and delimiters, files bent out of the template's shape, and the
/// semantics of rows that are far apart or disagree. They run everywhere (no private data), print their timings
/// to the test output, and use deliberately generous timing ceilings that only catch pathological slowness.
///
/// <para><b>Findings and observations.</b> Where the design says what must happen and the importer does something
/// else, the test keeps the designed assertion and is skipped with a reason that starts <c>FINDING:</c> (there are
/// none at present: the five findings of the first stress run, the formula-looking Copy list, Excel error cells,
/// dates in text columns, Windows-1252 and UTF-16 files, were fixed and their tests now assert the designed
/// behaviour). Where the design is silent, the test asserts (or just prints) what the importer actually does, so a
/// change shows up; those are the <c>Observation_</c> tests. The files are built by the shared generators in
/// <see cref="StressFiles"/>, which the hand-test kit writer uses as well.</para>
/// </summary>
public sealed class SectionImportStressTests : IDisposable
{
    /// <summary>A ceiling for the slowest step on the largest file; far above normal, so only pathological slowness fails.</summary>
    private static readonly TimeSpan GenerousCeiling = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;
    private readonly string _files;

    /// <summary>Creates a temporary folder for the generated files.</summary>
    /// <param name="output">The test output the timings are written to.</param>
    public SectionImportStressTests(ITestOutputHelper output)
    {
        _output = output;
        _files = Path.Combine(Path.GetTempPath(), $"import_stress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_files);
    }

    /// <summary>Deletes the temporary folder (and the large files in it).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_files, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Writes a stress file into the temporary folder.</summary>
    private string Write(StressFile file, string name) => file.WriteTo(_files, name);

    /// <summary>Writes bytes into the temporary folder under a file name.</summary>
    private string WriteBytes(byte[] bytes, string fileName)
    {
        var path = Path.Combine(_files, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Checks a file through the real service and prints how long it took.</summary>
    private async Task<ImportCheck> Check(ImportRig rig, string path, string what)
    {
        var sw = Stopwatch.StartNew();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        _output.WriteLine($"[time] Check of {what}: {sw.ElapsedMilliseconds} ms");
        return check;
    }

    /// <summary>The first few problems of a report as text, for assertion messages.</summary>
    private static string Describe(ImportCheck check, int max = 5)
    {
        if (check.FileProblem is not null)
            return "file problem: " + check.FileProblem;
        return string.Join(" | ", check.Report!.Problems.Take(max).Select(ImportProblemText.Line)) +
               (check.Report.Problems.Count > max ? $" ... ({check.Report.Problems.Count} problems)" : "");
    }

    /// <summary>Asserts a check found no file problem and no row problems.</summary>
    private static void AssertReady(ImportCheck check)
    {
        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
    }

    /// <summary>Checks and imports a file, asserting both succeed, and returns what Import did.</summary>
    private async Task<ImportOutcome> CheckAndImport(ImportRig rig, string path, string what)
    {
        var check = await Check(rig, path, what);
        AssertReady(check);
        var sw = Stopwatch.StartNew();
        var outcome = rig.Service.Import(check.Sheet!, check.Report!, rig.Fall.Id);
        _output.WriteLine($"[time] Import of {what}: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        return outcome;
    }

    /// <summary>Builds the flyout's view model over a rig, with fakes for everything else.</summary>
    private static (ImportSectionsViewModel Vm, FakeImportSectionsPlatform Platform) NewVm(ImportRig rig)
    {
        var host = new FakeImportSectionsHost(new ImportTarget(rig.Fall.Id, rig.Fall.Name, rig.Year.Name));
        var platform = new FakeImportSectionsPlatform();
        var vm = new ImportSectionsViewModel(rig.Service, host, platform, FocusTestSupport.AllFocus(), rig.Courses, new RecordingImportLogger());
        return (vm, platform);
    }

    /// <summary>The text of every cell of a read file's row, by column.</summary>
    private static string Cell(ImportSheet sheet, int rowIndex, string column) => sheet.Rows[rowIndex].Cells[column];

    // ═════════════════════════════════════════════════════════════════════════
    // A1-A5: scale
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The largest file the importer accepts (exactly 5,000 data rows, one-row and multi-row sections, hundreds of
    /// new instructors and courses, about a hundred new rooms, dozens of new tags, types and campuses): Check says
    /// ready, Import writes everything, and every section reads back through the repositories as the file described it.
    /// </summary>
    [Fact]
    public async Task Fresh_LargestAllowedFile_ChecksAndImportsEverySection()
    {
        using var rig = new ImportRig();
        var sw = Stopwatch.StartNew();
        var set = StressFiles.Large();
        var bytes = set.File.ToBytes();
        _output.WriteLine($"[size] {set.File.Rows.Count} rows, {set.Sections.Count} sections, {bytes.Length / 1024.0 / 1024.0:F2} MB .xlsx (built in {sw.ElapsedMilliseconds} ms)");
        Assert.Equal(ImportFileReader.MaxDataRows, set.File.Rows.Count);
        var path = WriteBytes(bytes, "largest.xlsx");

        // The reader alone (bytes already in memory).
        sw.Restart();
        var read = new ImportFileReader().Read(bytes, path);
        var readMs = sw.Elapsed;
        _output.WriteLine($"[time] Read of 5,000 rows: {readMs.TotalMilliseconds:F0} ms");
        Assert.True(read.IsSuccess, read.Problem);
        Assert.Equal(ImportFileReader.MaxDataRows, read.Sheet!.Rows.Count);

        // The whole check (reads the file again, then builds the snapshot and plans).
        sw.Restart();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        var checkTime = sw.Elapsed;
        _output.WriteLine($"[time] Check of 5,000 rows (read + snapshot + plan): {checkTime.TotalMilliseconds:F0} ms");
        AssertReady(check);
        var plan = check.Report!.Plan;
        _output.WriteLine($"[plan] sections {plan.NewSections.Count}, courses {plan.NewCourses.Count}, instructors {plan.NewInstructors.Count}, " +
                          $"subjects {plan.NewSubjects.Count}, campuses {plan.NewCampuses.Count}, section types {plan.NewSectionTypes.Count}, " +
                          $"meeting types {plan.NewMeetingTypes.Count}, tags {plan.NewTags.Count}, rooms {plan.NewRooms.Count}; " +
                          $"warnings {check.Report.Warnings.Count}: {string.Join(" | ", check.Report.Warnings.Select(w => w.Kind).Distinct())}");
        Assert.Equal(set.Sections.Count, plan.NewSections.Count);
        Assert.True(plan.NewInstructors.Count >= 100 && plan.NewCourses.Count >= 100 && plan.NewRooms.Count >= 50,
            "the file should create hundreds of courses and instructors and many rooms");

        sw.Restart();
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);
        var importTime = sw.Elapsed;
        _output.WriteLine($"[time] Import of 5,000 rows: {importTime.TotalMilliseconds:F0} ms");
        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(set.Sections.Count, outcome.Counts.Sections);
        Assert.Equal(plan.NewCourses.Count, outcome.Counts.Courses);
        Assert.Equal(plan.NewInstructors.Count, outcome.Counts.Instructors);
        Assert.Equal(plan.NewRooms.Count, outcome.Counts.Rooms);

        sw.Restart();
        var verifier = new StressVerifier(rig, rig.Fall.Id);
        var differences = verifier.VerifyAll(set.Sections, checkTitle: true, maxReported: 15, out int total);
        _output.WriteLine($"[time] Read back and verified {set.Sections.Count} sections: {sw.ElapsedMilliseconds} ms ({total} differences)");
        Assert.True(total == 0, string.Join("\n", differences));
        Assert.Equal(set.Sections.Count, rig.Sections.GetAll(rig.Fall.Id).Count);

        Assert.True(readMs < GenerousCeiling && checkTime < GenerousCeiling && importTime < GenerousCeiling,
            $"read {readMs.TotalSeconds:F1} s, check {checkTime.TotalSeconds:F1} s, import {importTime.TotalSeconds:F1} s");
    }

    /// <summary>
    /// One data row over the limit (5,001): exactly one problem, the "larger than TermPoint can import at once"
    /// message, and nothing is written.
    /// </summary>
    [Fact]
    public async Task Fresh_OneRowOverTheLimit_IsOneProblemAndWritesNothing()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Large(ImportFileReader.MaxDataRows + 1);
        var path = Write(set.File, "one-over");
        var before = rig.DumpAllTables();

        var check = await Check(rig, path, "5,001 rows");

        Assert.Equal(ImportFileReader.TooLargeMessage, check.FileProblem);
        Assert.Null(check.Report);
        Assert.Null(check.Sheet);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>
    /// A file over 10 MB (padded with a Notes column of random text): exactly one problem, nothing written, and it
    /// is refused quickly, because the service looks at the file's size before reading or parsing any of it.
    /// </summary>
    [Theory]
    [InlineData(StressFormat.Xlsx)]
    [InlineData(StressFormat.Csv)]
    public async Task Fresh_OverTenMegabytes_IsRefusedQuicklyWithOneProblem(StressFormat format)
    {
        using var rig = new ImportRig();
        var sw = Stopwatch.StartNew();
        var bytes = StressFiles.Oversize(format);
        _output.WriteLine($"[size] {format}: {bytes.Length / 1024.0 / 1024.0:F2} MB (built in {sw.ElapsedMilliseconds} ms)");
        Assert.True(bytes.Length > ImportFileReader.MaxFileBytes);
        var path = WriteBytes(bytes, format == StressFormat.Xlsx ? "oversize.xlsx" : "oversize.csv");
        var before = rig.DumpAllTables();

        sw.Restart();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        var refused = sw.Elapsed;
        _output.WriteLine($"[time] {format} over 10 MB refused in {refused.TotalMilliseconds:F0} ms");

        Assert.Equal(ImportFileReader.TooLargeMessage, check.FileProblem);
        Assert.Null(check.Report);
        Assert.Equal(before, rig.DumpAllTables());
        Assert.True(refused < TimeSpan.FromSeconds(5), $"refusing an oversize file took {refused.TotalSeconds:F1} s");
    }

    /// <summary>
    /// A 5,000-row file in which every row has two or three problems. The problems list is not capped by design,
    /// so this prints how many problems there are, how long Check takes, how long the Copy-list text is and how
    /// many lines the view would put in its non-virtualised list (a UI-freeze risk): the numbers are the point.
    /// Nothing is written.
    /// </summary>
    [Fact]
    public async Task Fresh_EveryRowBroken_ReportsEveryProblemAndWritesNothing()
    {
        using var rig = new ImportRig();
        var file = StressFiles.EveryRowBroken(ImportFileReader.MaxDataRows, out int expectedProblems);
        var path = Write(file, "every-row-broken");
        var before = rig.DumpAllTables();

        var sw = Stopwatch.StartNew();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        var checkTime = sw.Elapsed;
        Assert.Null(check.FileProblem);
        Assert.True(check.Report!.HasProblems);
        var copyText = ImportProblemText.CopyText(check.Report.Problems);
        _output.WriteLine($"[problems] {check.Report.Problems.Count} problems from {ImportFileReader.MaxDataRows} rows (expected {expectedProblems}); " +
                          $"check {checkTime.TotalMilliseconds:F0} ms; Copy list text {copyText.Length:N0} characters");
        Assert.Equal(expectedProblems, check.Report.Problems.Count);

        // The same through the flyout's view model: the lines the view binds to an ItemsControl, and Copy list.
        var (vm, platform) = NewVm(rig);
        sw.Restart();
        await vm.CheckFileAsync(path);
        var vmTime = sw.Elapsed;
        Assert.Equal(ImportStage.Problems, vm.Stage);
        _output.WriteLine($"[problems] view model: Stage {vm.Stage}, ProblemLines.Count {vm.ProblemLines.Count:N0}, " +
                          $"{vm.ProblemLines.Sum(l => l.Length):N0} characters of lines; CheckFileAsync {vmTime.TotalMilliseconds:F0} ms");
        Assert.Equal(check.Report.Problems.Count, vm.ProblemLines.Count);
        await vm.CopyListCommand.ExecuteAsync(null);
        _output.WriteLine($"[problems] clipboard text after Copy list: {platform.ClipboardText!.Length:N0} characters, {platform.ClipboardText.Split("\r\n").Length:N0} lines");
        _output.WriteLine("[UI RISK] the problem list has no cap and the view shows it in an ItemsControl that is not virtualised: " +
                          $"{vm.ProblemLines.Count:N0} rows of controls would be created at once.");

        Assert.Equal(before, rig.DumpAllTables());
        Assert.True(checkTime < GenerousCeiling && vmTime < GenerousCeiling, $"check {checkTime.TotalSeconds:F1} s, view model {vmTime.TotalSeconds:F1} s");
    }

    /// <summary>
    /// Importing the 5,000-row file and then importing it again adds nothing: every table's rows are the same and
    /// every existing row's <c>data</c> text is byte-identical (a snapshot of id to data before and after).
    /// </summary>
    [Fact]
    public async Task Fresh_ReimportingTheLargestFile_AddsNothingAndChangesNoRow()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Large();
        var path = Write(set.File, "largest");
        var first = await CheckAndImport(rig, path, "first import of 5,000 rows");
        Assert.Equal(set.Sections.Count, first.Counts.Sections);
        var idToData = rig.SnapshotIdToData();
        var dump = rig.DumpAllTables();
        var counts = dump.ToDictionary(t => t.Key, t => t.Value.Count);

        var sw = Stopwatch.StartNew();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        _output.WriteLine($"[time] Second check (every section already there): {sw.ElapsedMilliseconds} ms");
        AssertReady(check);
        Assert.Empty(check.Report!.Plan.NewSections);
        Assert.Equal(set.Sections.Count, check.Report.SkippedSections.Count);
        var again = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Written, again.Kind);
        Assert.Equal(ImportCounts.None, again.Counts);
        Assert.Equal(counts, rig.DumpAllTables().ToDictionary(t => t.Key, t => t.Value.Count));
        var violations = ImportRig.FindNonInserts(idToData, rig.SnapshotIdToData(), out var inserted);
        Assert.Empty(violations);
        Assert.Empty(inserted);
        Assert.Equal(dump, rig.DumpAllTables());

        // And the flyout says so.
        var (vm, _) = NewVm(rig);
        await vm.CheckFileAsync(path);
        Assert.Equal(ImportStage.NothingToAdd, vm.Stage);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A6: adversarial text
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Accented, non-Latin and right-to-left names, an emoji and a 4,000-character title, padded values, HTML-looking
    /// text and formula-looking values each on their own section: all are accepted and stored exactly as typed
    /// (padding trimmed at the ends), and HTML is stored as text, not interpreted.
    /// </summary>
    [Fact]
    public async Task Adversarial_EachAwkwardText_IsStoredAsTyped()
    {
        using var rig = new ImportRig();
        var adversarial = StressFiles.Adversarial();
        await CheckAndImport(rig, Write(adversarial.File, "adversarial"), "adversarial text");

        var verifier = new StressVerifier(rig, rig.Fall.Id);
        foreach (var (name, spec) in adversarial.Verifiable)
        {
            var differences = verifier.Verify(spec);
            Assert.True(differences.Count == 0, $"case '{name}': {string.Join("; ", differences)}");
        }

        // Non-breaking spaces match like spaces: one instructor and one section type, not two of each.
        Assert.Single(rig.Instructors.GetAll(), i => NameMatch.Equal(i.LastName, "Van Dyk"));
        Assert.Single(rig.Environment.GetAll(SchedulingEnvironmentTypes.SectionType), t => NameMatch.Equal(t.Name, "Hy brid"));
    }

    /// <summary>
    /// OBSERVATION: names given with a non-breaking space and then with a plain space are one record, and the record
    /// keeps the spelling of the first row. A zero-width space inside a course number is NOT whitespace to .NET, so
    /// it makes a second, visually identical course. Prints both.
    /// </summary>
    [Fact]
    public async Task Observation_NonBreakingAndZeroWidthSpaces()
    {
        using var rig = new ImportRig();
        var adversarial = StressFiles.Adversarial();
        await CheckAndImport(rig, Write(adversarial.File, "adversarial"), "adversarial text");

        var vanDyk = rig.Instructors.GetAll().Single(i => NameMatch.Equal(i.LastName, "Van Dyk"));
        _output.WriteLine($"[observed] instructor stored as last name with U+00A0: {vanDyk.LastName.Contains(' ')} (the first row's spelling is kept)");
        var hybrid = rig.Environment.GetAll(SchedulingEnvironmentTypes.SectionType).Single(t => NameMatch.Equal(t.Name, "Hy brid"));
        _output.WriteLine($"[observed] section type stored with U+00A0: {hybrid.Name.Contains(' ')}");

        var zeroWidthCourses = rig.Courses.GetAll().Where(c => c.CalendarCode.StartsWith("ADV30", StringComparison.Ordinal)).ToList();
        _output.WriteLine($"[observed] courses ADV30x: {zeroWidthCourses.Count}; one has a zero-width space in its code: {zeroWidthCourses.Any(c => c.CalendarCode.Contains('​'))}");
        Assert.Equal(2, zeroWidthCourses.Count);   // the invisible character makes a different course
        Assert.True(vanDyk.LastName.Contains(' '));
    }

    /// <summary>
    /// A name that starts with a character outside the Basic Multilingual Plane (a rare CJK character, written as a
    /// surrogate pair): the importer builds initials from the first character of each name, which here is half a
    /// pair. The test records whether Check and Import cope.
    /// </summary>
    [Fact]
    public async Task Observation_InstructorNameStartingWithASurrogatePair()
    {
        using var rig = new ImportRig();
        var file = new StressFile();
        file.AddRow((C.Subject, "SUR"), (C.CourseNumber, "101"), (C.CourseTitle, "Surrogate"), (C.SectionCode, "A"),
            (C.Instructors, "\U00020BB7田, \U00020BB7太郎"));   // 𠮷田, 𠮷太郎 (U+20BB7 is outside the BMP)
        var check = await Check(rig, Write(file, "surrogate"), "surrogate-pair name");
        AssertReady(check);
        var initials = check.Report!.Plan.NewInstructors.Single().Record.Initials;
        _output.WriteLine($"[observed] initials generated: {initials.Length} chars, well-formed UTF-16: {IsWellFormed(initials)}");

        Exception? failure = null;
        try { rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id); }
        catch (Exception ex) { failure = ex; }
        _output.WriteLine($"[observed] Import: {(failure is null ? "succeeded" : $"threw {failure.GetType().Name}")}; sections now {rig.Sections.GetAll(rig.Fall.Id).Count}");
        if (failure is not null)
            Assert.Empty(rig.Sections.GetAll(rig.Fall.Id));   // a failed import must leave nothing behind
        else
        {
            var stored = rig.Instructors.GetAll().Single();
            _output.WriteLine($"[observed] stored initials as code units: {string.Join(" ", stored.Initials.Select(c => $"U+{(int)c:X4}"))}; " +
                              $"name round-tripped: {stored.LastName == "𠮷田" && stored.FirstName == "𠮷太郎"}");
        }
    }

    /// <summary>True when a string has no unpaired surrogate.</summary>
    private static bool IsWellFormed(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            if (char.IsSurrogate(text[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Values that begin with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> and make problems: no cell of the Copy-list text
    /// may begin with one of them, or Excel runs it as a formula when the list is pasted.
    /// </summary>
    [Fact]
    public async Task CopyList_NoCellBeginsWithAFormulaCharacter()
    {
        using var rig = new ImportRig();
        var check = await Check(rig, Write(StressFiles.AdversarialProblems(), "formula-problems"), "formula-looking problems");
        Assert.Null(check.FileProblem);
        Assert.True(check.Report!.HasProblems);

        var offenders = FormulaCells(ImportProblemText.CopyText(check.Report.Problems));

        Assert.True(offenders.Count == 0, $"{offenders.Count} cell(s) would be run as formulas when pasted into Excel: {string.Join(" || ", offenders)}");
    }

    /// <summary>
    /// Designed behaviour of the Copy list for formula-looking problems: every Problem cell whose message begins
    /// with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is copied with an apostrophe in front (Excel's text marker, hidden
    /// on paste) and is otherwise the message as shown; the problem lines on screen are not changed; and the text the
    /// flyout's Copy list command puts on the clipboard is the same protected list. The test prints every problem line
    /// and how many cells needed protecting.
    /// </summary>
    [Fact]
    public async Task CopyList_FormulaLookingMessages_AreProtectedWithAnApostrophe_AndTheScreenLinesAreUnchanged()
    {
        using var rig = new ImportRig();
        var check = await Check(rig, Write(StressFiles.AdversarialProblems(), "formula-problems"), "formula-looking problems");
        Assert.True(check.Report!.HasProblems);
        var problems = check.Report.Problems;

        foreach (var problem in problems)
            _output.WriteLine($"[problem] {ImportProblemText.Line(problem)}");
        var starting = problems.Where(p => p.Message.Length > 0 && "=+-@".Contains(p.Message[0])).ToList();
        _output.WriteLine($"[designed] {starting.Count} message(s) begin with = + - or @ and are copied with a leading apostrophe");
        Assert.True(starting.Count >= 4, "the file is built so that several messages begin with a formula character");

        // The on-screen lines are the messages as they are; only the copied cells are protected.
        Assert.All(problems, p => Assert.Contains(p.Message, ImportProblemText.Line(p)));

        var copied = ImportProblemText.CopyText(problems);
        var lines = copied.Split("\r\n").Skip(1).ToList();
        Assert.Equal(problems.Count, lines.Count);
        for (int i = 0; i < problems.Count; i++)
        {
            var messageCell = lines[i].Split('\t')[2];
            var message = problems[i].Message.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            Assert.Equal("=+-@".Contains(message[0]) ? "'" + message : message, messageCell);
        }
        Assert.Empty(FormulaCells(copied));

        // Through the view model: the clipboard text is the same protected list.
        var (vm, platform) = NewVm(rig);
        await vm.CheckFileAsync(Path.Combine(_files, "formula-problems.xlsx"));
        await vm.CopyListCommand.ExecuteAsync(null);
        Assert.Equal(copied, platform.ClipboardText);
    }

    /// <summary>The cells of tab-separated text (after the header line) whose first character Excel would treat as the start of a formula.</summary>
    private static List<string> FormulaCells(string copyText) =>
        copyText.Split("\r\n").Skip(1)
            .SelectMany(line => line.Split('\t'))
            .Where(cell => cell.Length > 0 && "=+-@".Contains(cell[0]))
            .ToList();

    // ═════════════════════════════════════════════════════════════════════════
    // A7: Excel cell types
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Numeric Course Number and Section Code read as <c>101</c> and <c>1</c>, Capacity <c>60.0</c> reads as <c>60</c>, and
    /// times stored as Excel day fractions with floating-point error (0.37499999999) read as 09:00 and 10:00 and become
    /// a 09:00-10:00 meeting, per the reader's CellToText rules.
    /// </summary>
    [Fact]
    public void ExcelCells_NumbersAndImpreciseTimes_AreReadAsTheyLook()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: false);
        var path = Write(file, "cell-types");
        var read = new ImportFileReader().Read(File.ReadAllBytes(path), path);
        Assert.True(read.IsSuccess, read.Problem);
        var sheet = read.Sheet!;

        int numeric = rowOf["numeric"];
        Assert.Equal("101", Cell(sheet, numeric, C.CourseNumber));
        Assert.Equal("1", Cell(sheet, numeric, C.SectionCode));
        Assert.Equal("09:00", Cell(sheet, numeric, C.Begin));
        Assert.Equal("10:00", Cell(sheet, numeric, C.End));
        Assert.Equal("60", Cell(sheet, numeric, C.Capacity));

        var report = rig.Plan(sheet);
        var draft = report.Plan.NewSections.Single(s => s.Key.Label == "XLS101 1");
        Assert.Equal(60, draft.Capacity);
        var meeting = Assert.Single(draft.Meetings);
        Assert.Equal(9 * 60, meeting.StartMinutes);
        Assert.Equal(60, meeting.DurationMinutes);
    }

    /// <summary>
    /// Capacity as 60.5 is read as <c>60.5</c> and is a problem on its row ("Capacity must be a whole number"), as designed.
    /// </summary>
    [Fact]
    public async Task ExcelCells_FractionalCapacity_IsAProblemOnItsRow()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: true);
        var check = await Check(rig, Write(file, "cell-types-capacity"), "cell types with 60.5 capacity");
        Assert.Null(check.FileProblem);
        var row = file.RowNumberOf(rowOf["capacity-60.5"]);

        Assert.Contains(check.Report!.Problems, p => p.Row == row && p.Column == C.Capacity && p.Message.Contains("whole number"));
    }

    /// <summary>
    /// The cells that need no fix are still read as they look, and the test prints them: a boolean in a text column
    /// becomes <c>TRUE</c> (and creates a section type of that name), and a formula cell is read as the value Excel
    /// last calculated.
    /// </summary>
    [Fact]
    public void ExcelCells_BooleansAndFormulas_AreReadAsTheyLook()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: false);
        var path = Write(file, "cell-types");
        var sheet = new ImportFileReader().Read(File.ReadAllBytes(path), path).Sheet!;

        var boolean = Cell(sheet, rowOf["boolean"], C.SectionType);
        var formula = Cell(sheet, rowOf["formula"], C.Instructors);
        _output.WriteLine($"[observed] boolean in Section Type -> [{boolean}]");
        _output.WriteLine($"[observed] formula cell -> [{formula}]");

        var report = rig.Plan(sheet);
        _output.WriteLine($"[observed] new section types the file would create: {string.Join(", ", report.Plan.NewSectionTypes.Select(t => t.Record.Name))}");

        Assert.Equal("TRUE", boolean);
        Assert.Equal("Formula, Fred", formula);
        Assert.Contains(report.Plan.NewSectionTypes, t => t.Record.Name == "TRUE");
        Assert.DoesNotContain(report.Problems, p => p.Row == file.RowNumberOf(rowOf["boolean"]) || p.Row == file.RowNumberOf(rowOf["formula"]));
    }

    /// <summary>
    /// A Section Code that Excel turned into a date (the user typed <c>1-2</c>) is no longer imported silently as
    /// <c>2026-01-02</c>: the reader records a date issue on the cell (which reads as blank) and the check reports exactly
    /// one problem, on that row and column, with the date message. The section is not added.
    /// </summary>
    [Fact]
    public void ExcelCells_DateInSectionCode_IsOneProblemWithTheDateMessage()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: false);
        var path = Write(file, "cell-types");
        var sheet = new ImportFileReader().Read(File.ReadAllBytes(path), path).Sheet!;
        int dateRow = file.RowNumberOf(rowOf["date-code"]);

        Assert.Equal("", Cell(sheet, rowOf["date-code"], C.SectionCode));
        Assert.Equal(new CellIssue(CellIssueKind.Date, "2 Jan 2026"), sheet.Rows[rowOf["date-code"]].Issue(C.SectionCode));

        var report = rig.Plan(sheet);
        foreach (var problem in report.Problems)
            _output.WriteLine($"[problem] {ImportProblemText.Line(problem)}");
        var problems = report.Problems.Where(p => p.Row == dateRow).ToList();

        var only = Assert.Single(problems);
        Assert.Equal(C.SectionCode, only.Column);
        Assert.Equal("Excel has turned this into a date (2 Jan 2026). Format the column as Text in Excel and type the value again.", only.Message);
        Assert.DoesNotContain(report.Plan.NewSections, s => s.Key.CourseNumber == "102");
    }

    /// <summary>
    /// Excel error cells are problems, not blanks: an error in a required column (Section Code, <c>#N/A</c>) is one
    /// problem on that cell with the new message (not "Section Code is empty"), and errors in three optional columns
    /// (Instructor(s) <c>#REF!</c>, Section Type <c>#N/A</c>, Capacity <c>#DIV/0!</c>) are exactly one problem each, each naming its
    /// own error. Nothing else is reported on those rows, so a section can no longer be imported silently without
    /// its instructor, type or capacity.
    /// </summary>
    [Fact]
    public void ExcelCells_ErrorCells_AreExactlyOneProblemEach_InRequiredAndOptionalColumns()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: false);
        var path = Write(file, "cell-types");
        var sheet = new ImportFileReader().Read(File.ReadAllBytes(path), path).Sheet!;
        int errorRow = file.RowNumberOf(rowOf["error"]);
        int optionalRow = file.RowNumberOf(rowOf["error-optional"]);

        Assert.Equal("", Cell(sheet, rowOf["error"], C.SectionCode));
        Assert.Equal("", Cell(sheet, rowOf["error-optional"], C.Instructors));

        var report = rig.Plan(sheet);
        foreach (var problem in report.Problems)
            _output.WriteLine($"[problem] {ImportProblemText.Line(problem)}");

        var required = Assert.Single(report.Problems, p => p.Row == errorRow);
        Assert.Equal(C.SectionCode, required.Column);
        Assert.Equal("This cell shows an Excel error (#N/A). Fix the formula in Excel, or type the value instead.", required.Message);

        var optional = report.Problems.Where(p => p.Row == optionalRow).ToList();
        Assert.Equal(
            new[]
            {
                (C.SectionType, "This cell shows an Excel error (#N/A). Fix the formula in Excel, or type the value instead."),
                (C.Instructors, "This cell shows an Excel error (#REF!). Fix the formula in Excel, or type the value instead."),
                (C.Capacity, "This cell shows an Excel error (#DIV/0!). Fix the formula in Excel, or type the value instead."),
            },
            optional.Select(p => (p.Column!, p.Message)).ToArray());

        // Those, plus the date in the Section Code, are all the file's problems.
        Assert.Equal(5, report.Problems.Count);
    }

    /// <summary>
    /// A section already in the semester is skipped without its non-key cells being looked at: the same file, planned
    /// after the section with the optional-column error cells has been imported (clean), reports no problem for that
    /// row (the section is listed as already there), while the error and date in key columns are still problems.
    /// </summary>
    [Fact]
    public async Task ExcelCells_ErrorCellsInANonKeyColumn_OfASectionAlreadyThere_AreNotProblems()
    {
        using var rig = new ImportRig();
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: false);
        int optionalRow = file.RowNumberOf(rowOf["error-optional"]);

        // The same section, without the errors, goes in first.
        var already = new StressFile();
        already.AddRow((C.Subject, "XLS"), (C.CourseNumber, 107), (C.CourseTitle, "Errors in optional cells"), (C.SectionCode, "A"));
        await CheckAndImport(rig, Write(already, "already-there"), "the section that is already there");

        var check = await Check(rig, Write(file, "cell-types"), "cell types after the section is already there");
        Assert.Null(check.FileProblem);
        foreach (var problem in check.Report!.Problems)
            _output.WriteLine($"[problem] {ImportProblemText.Line(problem)}");

        Assert.DoesNotContain(check.Report.Problems, p => p.Row == optionalRow);
        Assert.Contains(check.Report.SkippedSections, s => s.Key.Label == "XLS107 A");
        // The Section Code error and the Section Code date are key columns: still problems.
        Assert.Equal(new[] { C.SectionCode, C.SectionCode },
            check.Report.Problems.Select(p => p.Column!).ToArray());
        Assert.Equal(2, check.Report.Problems.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A8: CSV variants
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Imports the small CSV in an encoding and asserts every section, name and title came through exactly.</summary>
    private async Task AssertCsvImportsExactly(StressCsvEncoding encoding, string name)
    {
        using var rig = new ImportRig();
        var set = StressFiles.SmallWithLineBreakTitle();
        set.File.Format = StressFormat.Csv;
        set.File.CsvEncoding = encoding;
        await CheckAndImport(rig, Write(set.File, name), $"small {encoding} CSV");

        var verifier = new StressVerifier(rig, rig.Fall.Id);
        // The title with the line break is checked by its own test (the break may legitimately be normalised).
        var differences = set.Sections.SelectMany(s => verifier.Verify(s, checkTitle: s.CourseNumber != "199")).ToList();
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    /// <summary>A UTF-8 CSV with a byte-order mark (Excel's "CSV UTF-8") imports every name exactly.</summary>
    [Fact]
    public Task Csv_Utf8WithBom_ImportsNamesExactly() => AssertCsvImportsExactly(StressCsvEncoding.Utf8WithBom, "utf8-bom");

    /// <summary>A UTF-8 CSV without a byte-order mark imports every name exactly.</summary>
    [Fact]
    public Task Csv_Utf8WithoutBom_ImportsNamesExactly() => AssertCsvImportsExactly(StressCsvEncoding.Utf8, "utf8-no-bom");

    /// <summary>A UTF-16 little-endian CSV with a byte-order mark imports every name exactly.</summary>
    [Fact]
    public Task Csv_Utf16LittleEndian_ImportsNamesExactly() => AssertCsvImportsExactly(StressCsvEncoding.Utf16LeWithBom, "utf16-le");

    /// <summary>A UTF-16 big-endian CSV with a byte-order mark imports every name exactly.</summary>
    [Fact]
    public Task Csv_Utf16BigEndian_ImportsNamesExactly() => AssertCsvImportsExactly(StressCsvEncoding.Utf16BeWithBom, "utf16-be");

    /// <summary>
    /// A UTF-16 file separated by tabs, which is what Excel's "Unicode Text" really writes: the encoding is now
    /// understood, but a tab-separated file is still refused (a separate, later item) with the "no template header"
    /// message. Nothing is imported.
    /// </summary>
    [Fact]
    public async Task Csv_Utf16TabSeparated_IsStillRefusedAsHavingNoTemplateHeader()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.Format = StressFormat.Csv;
        set.File.CsvEncoding = StressCsvEncoding.Utf16LeWithBom;
        set.File.Delimiter = '\t';
        var before = rig.DumpAllTables();

        var check = await Check(rig, Write(set.File, "utf16-tabs"), "tab-separated UTF-16 CSV");

        _output.WriteLine($"[designed] tab-separated UTF-16: {check.FileProblem}");
        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, check.FileProblem);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>A Windows-1252 CSV (Excel's plain "CSV") imports every name and title exactly.</summary>
    [Fact]
    public Task Csv_Windows1252_ImportsNamesExactly() => AssertCsvImportsExactly(StressCsvEncoding.Windows1252, "windows-1252-exact");

    /// <summary>
    /// A Windows-1252 CSV (Excel's plain "CSV"): the typographic apostrophe (byte 0x92) and en dash (0x96), which the
    /// reader used to turn into invisible control characters, now come through exactly as <c>’</c> and <c>–</c>, in a name and
    /// in a title; accented Latin letters survive too. A letter 1252 lacks (the r with a caron in Dvořák) was already
    /// replaced when Excel saved the file; that is the saving program's doing, not TermPoint's, and is printed rather
    /// than asserted.
    /// </summary>
    [Fact]
    public async Task Csv_Windows1252_TypographicApostropheAndDashComeThroughExactly()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.Format = StressFormat.Csv;
        set.File.CsvEncoding = StressCsvEncoding.Windows1252;
        // Add a title with an en dash (0x96 in 1252) and an instructor with a letter 1252 lacks.
        set.File.AddRow((C.Subject, "SML"), (C.CourseNumber, "150"), (C.CourseTitle, "Intro – Part 1"), (C.SectionCode, "A"),
            (C.Instructors, "Dvořák, Antonín"));
        var path = Write(set.File, "windows-1252");
        Assert.Contains((byte)0x92, File.ReadAllBytes(path));   // really the one-byte Windows apostrophe, not UTF-8
        Assert.Contains((byte)0x96, File.ReadAllBytes(path));

        var sheet = new ImportFileReader().Read(File.ReadAllBytes(path), path).Sheet!;
        var names = sheet.Rows.Select(r => r.Cells[C.Instructors]).Where(n => n.Length > 0).ToList();
        foreach (var n in names)
            _output.WriteLine($"[designed] instructor read as [{Printable(n)}]");
        _output.WriteLine($"[designed] title with en dash read as [{Printable(sheet.Rows[^1].Cells[C.CourseTitle])}]");

        Assert.Contains("Gagné, Éloïse", names);               // plain accents survive
        Assert.Contains("Müller, Jürgen", names);
        Assert.Contains("O’Brien, Pat", names);                 // the typographic apostrophe, exactly
        Assert.Equal("Intro – Part 1", sheet.Rows[^1].Cells[C.CourseTitle]);   // the en dash, exactly
        Assert.DoesNotContain(names, n => n.Any(char.IsControl));
        var check = await Check(rig, path, "Windows-1252 CSV");
        Assert.Null(check.FileProblem);
    }

    /// <summary>Shows control characters as <c>U+XXXX</c> so the output is readable.</summary>
    private static string Printable(string text) =>
        string.Concat(text.Select(c => char.IsControl(c) ? $"<U+{(int)c:X4}>" : c.ToString()));

    /// <summary>
    /// OBSERVATION: a CSV separated by semicolons (what Excel writes in many European and French-Canadian locales) or
    /// tabs is not understood: the whole header is one cell, so the file is refused with the "first row doesn't have the
    /// column names" message, which does not mention the separator. Nothing is imported.
    /// </summary>
    [Theory]
    [InlineData(';', "semicolons")]
    [InlineData('\t', "tabs")]
    public async Task Observation_CsvWithAnotherDelimiter_IsRefusedAsHavingNoTemplateHeader(char delimiter, string name)
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.Format = StressFormat.Csv;
        set.File.Delimiter = delimiter;
        var before = rig.DumpAllTables();

        var check = await Check(rig, Write(set.File, name), $"{name}-separated CSV");

        _output.WriteLine($"[observed] {name}: {check.FileProblem}");
        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, check.FileProblem);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>
    /// Quoted fields holding commas, quotes and a line break import intact (the title with a line break is compared
    /// with its line break normalised, and the test prints whether the break survives as typed).
    /// </summary>
    [Fact]
    public async Task Csv_QuotedFieldsWithCommasQuotesAndLineBreaks_ImportIntact()
    {
        using var rig = new ImportRig();
        var set = StressFiles.SmallWithLineBreakTitle();
        set.File.Format = StressFormat.Csv;
        await CheckAndImport(rig, Write(set.File, "quoted"), "quoted fields");

        var verifier = new StressVerifier(rig, rig.Fall.Id);
        var first = verifier.FindCourse("SML100")!;
        Assert.Equal("Intro to \"Geology\", Part 1", first.Title);
        var broken = verifier.FindCourse("SML199")!;
        _output.WriteLine($"[observed] title with a line break stored as [{Printable(broken.Title).Replace("\r", "<CR>").Replace("\n", "<LF>")}]");
        Assert.Equal("Line one Line two", broken.Title.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " "));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A9: structure variants (.xlsx)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Checks a small file and asserts it is ready with the six sections; returns the check.</summary>
    private async Task<ImportCheck> AssertSixSectionsReady(ImportRig rig, StressFile file, string name)
    {
        var check = await Check(rig, Write(file, name), name);
        AssertReady(check);
        Assert.Equal(6, check.Report!.Plan.NewSections.Count);
        return check;
    }

    /// <summary>Headers that differ from the template only in case and spacing are matched, in any order, as designed.</summary>
    [Fact]
    public async Task Structure_HeadersWithExtraSpacesAndOtherCase_AreMatched()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        StressFiles.MessyHeaders(set.File);

        await AssertSixSectionsReady(rig, set.File, "messy-headers");
    }

    /// <summary>Columns in another order with extra columns of the user's own (text, numbers, booleans) are read, and the sections import intact.</summary>
    [Fact]
    public async Task Structure_ReorderedColumnsWithExtraColumns_AreReadAndImportIntact()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        StressFiles.ReorderWithExtraColumns(set.File);

        var check = await AssertSixSectionsReady(rig, set.File, "reordered");
        rig.Service.Import(check.Sheet!, check.Report!, rig.Fall.Id);
        var verifier = new StressVerifier(rig, rig.Fall.Id);
        Assert.Empty(set.Sections.SelectMany(s => verifier.Verify(s)));
    }

    /// <summary>A missing template column is the single "The Tags column is missing." problem and nothing is checked (D9).</summary>
    [Fact]
    public async Task Structure_MissingColumn_IsOneProblem()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        StressFiles.DropColumn(set.File, C.Tags);

        var check = await Check(rig, Write(set.File, "missing-tags"), "missing Tags column");

        Assert.Equal("The Tags column is missing.", check.FileProblem);
        Assert.Null(check.Report);
    }

    /// <summary>
    /// OBSERVATION: a header that appears twice is refused with "The Term column appears more than once." The design
    /// says nothing about duplicates; refusing is the safe choice and the message names the column.
    /// </summary>
    [Fact]
    public async Task Observation_DuplicatedHeader_IsRefusedByName()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        StressFiles.DuplicateColumn(set.File, C.Term);

        var check = await Check(rig, Write(set.File, "duplicate-header"), "duplicated Term header");

        Assert.Equal("The Term column appears more than once.", check.FileProblem);
    }

    /// <summary>
    /// The Sections sheet need not be first. OBSERVATION: its name is matched ignoring case (a sheet called
    /// <c>sections</c> is read); the design only says the sheet is named Sections.
    /// </summary>
    [Fact]
    public async Task Structure_SectionsSheetNotFirst_AndInLowerCase_IsRead()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.SheetName = "sections";
        set.File.SheetsBefore.Add(("Instructions", new object?[][] { new object?[] { "Read me first" } }));
        set.File.SheetsBefore.Add(("Scratch", new object?[][] { new object?[] { "x", "y" } }));

        await AssertSixSectionsReady(rig, set.File, "sheet-not-first");
    }

    /// <summary>
    /// Two title rows above the header: the header is the first row of the sheet (design), so the file is refused with
    /// one problem and nothing is imported.
    /// </summary>
    [Fact]
    public async Task Structure_TwoTitleRowsAboveTheHeader_IsOneProblem()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.TitleRows.Add(new object?[] { "Geology Department", "Fall schedule" });
        set.File.TitleRows.Add(new object?[] { "Prepared by the office" });
        var before = rig.DumpAllTables();

        var check = await Check(rig, Write(set.File, "title-rows"), "two title rows");

        Assert.Equal(ImportFileReader.NoTemplateHeaderMessage, check.FileProblem);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>
    /// A thousand trailing empty rows (empty text cells, and rows of only spaces) and blank rows between the data are
    /// dropped (design), they do not count toward the 5,000-row limit, and the row numbers in problems still match
    /// what Excel shows.
    /// </summary>
    [Fact]
    public async Task Structure_BlankAndSpaceOnlyRows_AreDropped_AndRowNumbersStayRight()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.AddBlankRow();
        set.File.AddRow();   // a row naming only the semester: not blank, so it will be a problem below
        int semesterOnlyRow = set.File.Rows.Count - 1;
        set.File.Rows[semesterOnlyRow].Clear();                 // make it entirely blank
        set.File.Rows[semesterOnlyRow][C.Subject] = "   ";      // spaces only
        set.File.Rows[semesterOnlyRow][C.Instructors] = "  ";
        set.File.TrailingBlankRows = 1000;
        set.File.TrailingBlankCell = "   ";
        // One bad row after the blanks, so the problem's row number shows how far down it is.
        int badIndex = set.File.Rows.Count;
        set.File.AddRow((C.Subject, "SML"), (C.CourseNumber, "300"), (C.CourseTitle, "No code"));

        var check = await Check(rig, Write(set.File, "blank-rows"), "blank and space-only rows");

        AssertNoFileProblem(check);
        var problem = Assert.Single(check.Report!.Problems);
        Assert.Equal(set.File.RowNumberOf(badIndex), problem.Row);
        Assert.Equal(C.SectionCode, problem.Column);
        Assert.Equal(6, check.Report.Plan.NewSections.Count);
    }

    /// <summary>Asserts a check had no file problem (row problems are allowed).</summary>
    private static void AssertNoFileProblem(ImportCheck check) => Assert.Null(check.FileProblem);

    /// <summary>
    /// OBSERVATION: 50,000 trailing rows of empty text cells (what a sheet looks like after formatting or filling far
    /// down) are read and dropped. Prints how long that takes; a few seconds would be fine, a minute would not.
    /// </summary>
    [Fact]
    public async Task Observation_FiftyThousandTrailingBlankRows_Timing()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.TrailingBlankRows = 50_000;
        set.File.TrailingBlankCell = "";
        var path = Write(set.File, "many-blank-rows");
        _output.WriteLine($"[size] {new FileInfo(path).Length / 1024.0:F0} KB");

        var check = await Check(rig, path, "50,000 trailing blank rows");

        AssertReady(check);
        Assert.Equal(6, check.Report!.Plan.NewSections.Count);
    }

    /// <summary>
    /// OBSERVATION: rows hidden by the user (or filtered out with an auto-filter) are still read and imported. A user
    /// who filtered the sheet to one subject and hid the rest will import the hidden rows too.
    /// </summary>
    [Fact]
    public async Task Observation_HiddenAndFilteredOutRows_AreImported()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        set.File.HiddenDataRows.Add(1);
        set.File.HiddenDataRows.Add(4);
        set.File.AutoFilter = true;

        var check = await Check(rig, Write(set.File, "hidden-rows"), "hidden rows");

        AssertReady(check);
        _output.WriteLine($"[observed] 2 of 6 rows hidden: the importer plans {check.Report!.Plan.NewSections.Count} sections");
        Assert.Equal(6, check.Report.Plan.NewSections.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A10: semantics
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One section whose rows are far apart (row 2 and row 4,000) is still one section, with the meetings of both rows.
    /// </summary>
    [Fact]
    public async Task Semantics_RowsOfOneSectionFarApart_AreOneSection()
    {
        using var rig = new ImportRig();
        var file = StressFiles.FarApart("2026-2027", "Fall", out var spec);
        Assert.Equal(4000, file.RowNumberOf(file.Rows.Count - 1));

        var outcome = await CheckAndImport(rig, Write(file, "far-apart"), "rows 2 and 4,000 of one section");

        Assert.Equal(3998, outcome.Counts.Sections);
        var verifier = new StressVerifier(rig, rig.Fall.Id);
        Assert.Empty(verifier.Verify(spec));
    }

    /// <summary>Two rows of one section that disagree on a section-level cell are a problem on the second row (invariant 4).</summary>
    [Fact]
    public async Task Semantics_TwoRowsOfOneSectionThatDisagree_AreAProblem()
    {
        using var rig = new ImportRig();
        var file = StressFiles.RowsDisagree("2026-2027", "Fall");
        var before = rig.DumpAllTables();

        var check = await Check(rig, Write(file, "disagree"), "rows that disagree");

        Assert.Null(check.FileProblem);
        Assert.Contains(check.Report!.Problems, p => p.Row == 3 && p.Column == C.SectionType && p.Message.Contains("different Section Type than row 2"));
        Assert.Contains(check.Report.Problems, p => p.Row == 3 && p.Column == C.Capacity && p.Message.Contains("different Capacity than row 2"));
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);
        Assert.Equal(ImportOutcomeKind.Changed, outcome.Kind);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>
    /// OBSERVATION: an exact duplicate meeting row (the same days and times written twice) is not an error. The section
    /// gets the meeting twice, and the import warns that the section has meetings at overlapping times.
    /// </summary>
    [Fact]
    public async Task Observation_ExactDuplicateMeetingRow_IsImportedTwiceWithAnOverlapWarning()
    {
        using var rig = new ImportRig();
        var file = StressFiles.DuplicateMeeting("2026-2027", "Fall");

        var outcome = await CheckAndImport(rig, Write(file, "duplicate-meeting"), "duplicate meeting row");

        var section = Assert.Single(rig.Sections.GetAll(rig.Fall.Id));
        _output.WriteLine($"[observed] meetings stored: {section.Schedule.Count}; warnings: {string.Join(" | ", outcome.Report.Warnings.Select(w => w.Message))}");
        Assert.Equal(4, section.Schedule.Count);
        Assert.Contains(outcome.Report.Warnings, w => w.Kind == ImportWarningKinds.OverlappingMeetings);
    }

    /// <summary>
    /// Some rows naming another semester are problems on those rows only; the rows that name the open semester raise nothing.
    /// </summary>
    [Fact]
    public async Task Semantics_SomeRowsNamingAnotherSemester_AreProblemsOnThoseRows()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small(count: 200);
        var wrong = new[] { 7, 90, 199 };
        foreach (var index in wrong)
        {
            set.File.Rows[index][C.Term] = "Winter";
        }

        var check = await Check(rig, Write(set.File, "some-wrong-semester"), "some rows in another semester");

        Assert.Null(check.FileProblem);
        var rows = check.Report!.Problems.Select(p => p.Row).ToList();
        Assert.Equal(wrong.Select(i => (int?)set.File.RowNumberOf(i)).OrderBy(r => r).ToList(), rows.OrderBy(r => r).ToList());
        Assert.All(check.Report.Problems, p => Assert.Contains("is for Winter (2026-2027), but Fall (2026-2027) is open", p.Message));
    }

    /// <summary>A file in which every row names another semester is exactly one problem, however many rows it has (the whole-file check).</summary>
    [Fact]
    public async Task Semantics_EveryRowNamingAnotherSemester_IsExactlyOneProblem()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Large(2000, year: "2026-2027", term: "Winter");

        var check = await Check(rig, Write(set.File, "all-wrong-semester"), "every row in another semester");

        Assert.Null(check.FileProblem);
        var problem = Assert.Single(check.Report!.Problems);
        Assert.Null(problem.Row);
        Assert.Contains("This file is for Winter (2026-2027), but Fall (2026-2027) is open", problem.Message);
    }

    /// <summary>
    /// For a CSV, the row number of a problem is the line the record starts on (design, "Reading the file"), so a quoted
    /// field that spans two lines pushes every later row number down by one.
    /// </summary>
    [Fact]
    public async Task Semantics_CsvRowNumbersAfterAQuotedLineBreak_AreLineNumbers()
    {
        using var rig = new ImportRig();
        var file = new StressFile { Format = StressFormat.Csv };
        file.AddRow((C.Subject, "LIN"), (C.CourseNumber, "101"), (C.CourseTitle, "One"), (C.SectionCode, "A"));         // line 2
        file.AddRow((C.Subject, "LIN"), (C.CourseNumber, "102"), (C.CourseTitle, "Two\r\nlines"), (C.SectionCode, "A")); // lines 3 and 4
        file.AddRow((C.Subject, "LIN"), (C.CourseNumber, "103"), (C.CourseTitle, "Three"));                              // line 5: no section code

        var check = await Check(rig, Write(file, "line-numbers"), "CSV with a quoted line break");

        var problem = Assert.Single(check.Report!.Problems);
        Assert.Equal(5, problem.Row);
        Assert.Equal(C.SectionCode, problem.Column);
    }

    /// <summary>
    /// A Saturday meeting follows the Saturday setting: when it is off, one problem on the row saying how to turn it on
    /// (D10); when it is on, no problem. The test reads the setting, so it holds on any machine.
    /// </summary>
    [Fact]
    public async Task Semantics_SaturdayMeeting_FollowsTheWeekendSetting()
    {
        using var rig = new ImportRig();
        var file = new StressFile();
        file.AddRow((C.Subject, "SAT"), (C.CourseNumber, "101"), (C.CourseTitle, "Weekend"), (C.SectionCode, "A"),
            (C.MeetingDays, "Sa"), (C.Begin, "0900"), (C.End, "1200"));

        var check = await Check(rig, Write(file, "saturday"), "a Saturday meeting");

        Assert.Null(check.FileProblem);
        if (AppSettings.Current.IncludeSaturday)
        {
            Assert.False(check.Report!.HasProblems, Describe(check));
        }
        else
        {
            var problem = Assert.Single(check.Report!.Problems);
            Assert.Equal(2, problem.Row);
            Assert.Equal(C.MeetingDays, problem.Column);
            Assert.Contains("Saturday isn't turned on in TermPoint", problem.Message);
        }
    }

    /// <summary>
    /// A section already in the semester is recognised when the file spells its key differently (subject in lower case,
    /// a space inside the course number, the section code in lower case): importing that file adds nothing.
    /// </summary>
    [Fact]
    public async Task Semantics_AKeyWrittenWithOtherCaseAndSpacing_IsStillTheSameSection()
    {
        using var rig = new ImportRig();
        var set = StressFiles.Small();
        await CheckAndImport(rig, Write(set.File, "first"), "the small file");
        var before = rig.DumpAllTables();

        var again = StressFiles.Small();
        foreach (var row in again.File.Rows)
        {
            row[C.Subject] = "sml";
            row[C.CourseNumber] = ((string)row[C.CourseNumber]!).Insert(1, " ");
            row[C.SectionCode] = "a";
        }
        var check = await Check(rig, Write(again.File, "second"), "the same sections, spelt differently");

        AssertReady(check);
        Assert.Empty(check.Report!.Plan.NewSections);
        Assert.Equal(6, check.Report.SkippedSections.Count);
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);
        Assert.Equal(ImportCounts.None, outcome.Counts);
        Assert.Equal(before, rig.DumpAllTables());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The test files themselves
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The workbook helper's date style: a <see cref="DateTime"/> cell is read back by the importer's reader as a
    /// date (this is how the "Section Code that Excel turned into a date" case is produced): the cell is recorded as a
    /// date issue showing the date, and reads as blank text.
    /// </summary>
    [Fact]
    public void TestWorkbook_DateCell_IsReadAsADate()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s =>
        {
            s.Row(StressFile.TemplateColumnNames.Select(n => (object?)n).ToArray());
            s.Row("2026-2027", "Fall", "DT", "101", "Dates", new DateTime(2026, 3, 4));
        }).ToBytes();

        var sheet = new ImportFileReader().Read(bytes, "dates.xlsx").Sheet!;

        Assert.Equal(new CellIssue(CellIssueKind.Date, "4 Mar 2026"), sheet.Rows[0].Issue(C.SectionCode));
        Assert.Equal("", sheet.Rows[0].Cells[C.SectionCode]);
    }
}
