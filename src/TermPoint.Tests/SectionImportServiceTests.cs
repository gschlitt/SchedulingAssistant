using System.Text;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.TestSupport.ImportScenario;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SectionImportService"/> (<c>CheckAsync</c> over real temporary files, and
/// <c>Import</c> with its change detection, D4) and for <see cref="ImportReportSignature"/>, against real
/// temporary SQLite databases.
/// </summary>
public sealed class SectionImportServiceTests : IDisposable
{
    private const string CouldntOpen = "TermPoint couldn't open this file. If it's open in another program, close it there and try again.";
    private const string CouldntRead = "TermPoint couldn't read this file. If it's on a network drive, copy it to your computer and try again.";

    private readonly string _files;

    /// <summary>Creates a temporary folder for the import files.</summary>
    public SectionImportServiceTests()
    {
        _files = Path.Combine(Path.GetTempPath(), $"import_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_files);
    }

    /// <summary>Deletes the temporary folder.</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        try { Directory.Delete(_files, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Turns a sheet into CSV text: the template header, then one line per row, with quoting.</summary>
    private static string ToCsv(ImportSheet sheet)
    {
        static string Field(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", C.All.Select(c => Field(c.Name))));
        foreach (var row in sheet.Rows)
            sb.AppendLine(string.Join(",", C.All.Select(c => Field(row.Cells[c.Name]))));
        return sb.ToString();
    }

    /// <summary>Writes a file in the temporary folder and returns its path.</summary>
    private string WriteCsv(string name, string text)
    {
        var path = Path.Combine(_files, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>Counts the rows of every table.</summary>
    private static Dictionary<string, int> RowCounts(ImportRig rig) =>
        rig.DumpAllTables().ToDictionary(t => t.Key, t => t.Value.Count);

    // ── CheckAsync ────────────────────────────────────────────────────────────

    /// <summary>A valid CSV is read and planned: no file problem, the parsed sheet, and the report.</summary>
    [Fact]
    public async Task CheckAsync_ValidCsv_ReturnsTheSheetAndItsReport()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var path = WriteCsv("sections.csv", ToCsv(Sheet()));

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.False(check.HasFileProblem);
        Assert.Null(check.FileProblem);
        Assert.Equal("sections.csv", check.Sheet!.FileName);
        Assert.Equal(4, check.Sheet.Rows.Count);
        Assert.False(check.Report!.HasProblems, string.Join("; ", check.Report.Problems.Select(p => p.Message)));
        Assert.Equal(new[] { "CHEM101 A", "BIOL200 B", "CHEM101 C" }, check.Report.Plan.NewSections.Select(s => s.Key.Label).ToArray());
    }

    /// <summary>A checked file imports end to end: Check, then Import with what Check returned.</summary>
    [Fact]
    public async Task CheckAsync_ThenImport_WritesTheFile()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var check = await rig.Service.CheckAsync(WriteCsv("sections.csv", ToCsv(Sheet())), rig.Fall.Id);

        var outcome = rig.Service.Import(check.Sheet!, check.Report!, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(3, outcome.Counts.Sections);
        Assert.Equal(4, rig.Sections.GetAll(rig.Fall.Id).Count);
    }

    /// <summary>A file that is not there gives the "couldn't open" problem, with no path or exception text.</summary>
    [Fact]
    public async Task CheckAsync_MissingFile_IsTheCouldntOpenProblem()
    {
        using var rig = new ImportRig();
        var path = Path.Combine(_files, "nothing-here.csv");

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal(CouldntOpen, check.FileProblem);
        Assert.Null(check.Sheet);
        Assert.Null(check.Report);
        Assert.DoesNotContain("nothing-here", check.FileProblem);
    }

    /// <summary>A file another program holds exclusively (no sharing) gives the "couldn't open" problem.</summary>
    [Fact]
    public async Task CheckAsync_FileHeldExclusively_IsTheCouldntOpenProblem()
    {
        using var rig = new ImportRig();
        var path = WriteCsv("locked.csv", ToCsv(Sheet()));
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal(CouldntOpen, check.FileProblem);
    }

    /// <summary>
    /// A file another program has open for writing while allowing readers (as Excel does with a workbook it
    /// has open) is read fine. Both a "readers only" and a "readers and writers" holder are covered.
    /// </summary>
    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.ReadWrite)]
    public async Task CheckAsync_FileHeldOpenLikeExcel_IsReadFine(FileShare holderShare)
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var path = WriteCsv("open-in-excel.csv", ToCsv(Sheet()));
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, holderShare);

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems);
        Assert.Equal(3, check.Report.Plan.NewSections.Count);
    }

    /// <summary>An .xlsx workbook built in memory is read and planned the same way.</summary>
    [Fact]
    public async Task CheckAsync_Xlsx_ReturnsTheSheetAndItsReport()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = Sheet();
        var workbook = new XlsxTestWorkbook().Sheet("Sections", s =>
        {
            s.Row(C.All.Select(c => (object?)c.Name).ToArray());
            foreach (var row in sheet.Rows)
                s.RowAt(row.RowNumber, C.All.Select(c => (object?)row.Cells[c.Name]).ToArray());
        });
        var path = Path.Combine(_files, "sections.xlsx");
        File.WriteAllBytes(path, workbook.ToBytes());

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Null(check.FileProblem);
        Assert.Equal(4, check.Sheet!.Rows.Count);
        Assert.False(check.Report!.HasProblems, string.Join("; ", check.Report.Problems.Select(p => p.Message)));
        Assert.Equal(3, check.Report.Plan.NewSections.Count);
    }

    /// <summary>A problem the reader finds (here a missing column) comes back as the file problem, unchanged.</summary>
    [Fact]
    public async Task CheckAsync_ReaderProblem_IsReturnedAsTheFileProblem()
    {
        using var rig = new ImportRig();
        var header = string.Join(",", C.All.Where(c => c.Name != C.Tags).Select(c => c.Name));
        var path = WriteCsv("no-tags.csv", header + "\n");

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal("The Tags column is missing.", check.FileProblem);
        Assert.Null(check.Report);
    }

    /// <summary>An unsupported file type is the reader's plain problem.</summary>
    [Fact]
    public async Task CheckAsync_UnsupportedExtension_IsTheReadersProblem()
    {
        using var rig = new ImportRig();
        var path = WriteCsv("sections.txt", "anything");

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal("TermPoint can import Excel workbooks (.xlsx) and CSV files (.csv).", check.FileProblem);
    }

    /// <summary>A read that outlasts the network deadline gives the "couldn't read" problem.</summary>
    [Fact]
    public async Task CheckAsync_DeadlinePassed_IsTheCouldntReadProblem()
    {
        using var rig = new ImportRig();
        var path = WriteCsv("slow.csv", ToCsv(Sheet()));
        NetworkFaults.Decide = r => r.Label == "section import read" ? NetworkFaultDecision.TimeOut : NetworkFaultDecision.None;

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal(CouldntRead, check.FileProblem);
    }

    /// <summary>An exception thrown by the read operation is not allowed to escape: it becomes the "couldn't open" problem.</summary>
    [Fact]
    public async Task CheckAsync_ExceptionFromTheReadOperation_IsTheCouldntOpenProblem()
    {
        using var rig = new ImportRig();
        var path = WriteCsv("boom.csv", ToCsv(Sheet()));
        NetworkFaults.Decide = r => r.Label == "section import read"
            ? NetworkFaultDecision.Throwing(new IOException("network name deleted"))
            : NetworkFaultDecision.None;

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Equal(CouldntOpen, check.FileProblem);
    }

    // ── Import: D4 ────────────────────────────────────────────────────────────

    /// <summary>
    /// A section added through the repository after the check, with the same key as a planned new section,
    /// makes Import report "changed": nothing is written, and the new report shows the section skipped.
    /// </summary>
    [Fact]
    public void Import_ASectionWithAPlannedKeyAppearsAfterTheCheck_IsChangedAndWritesNothing()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var sheet = Sheet();
        var shown = rig.Plan(sheet);
        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = existing.Chem101.Id, SectionCode = "C" });
        var before = rig.DumpAllTables();

        var outcome = rig.Service.Import(sheet, shown, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Changed, outcome.Kind);
        Assert.False(outcome.WasWritten);
        Assert.Equal(ImportCounts.None, outcome.Counts);
        Assert.Equal(before, rig.DumpAllTables());
        Assert.Contains(outcome.Report.SkippedSections, s => s.Key.Label == "CHEM101 C");
        Assert.DoesNotContain(outcome.Report.Plan.NewSections, s => s.Key.Label == "CHEM101 C");
    }

    /// <summary>
    /// Records nothing in the file refers to, added after the check, do not count as a change: Import
    /// writes normally.
    /// </summary>
    [Fact]
    public void Import_AnUnrelatedRecordAppearsAfterTheCheck_WritesNormally()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = Sheet();
        var shown = rig.Plan(sheet);
        rig.Environment.Insert(SchedulingEnvironmentTypes.Tag,new SchedulingEnvironmentValue { Name = "Unrelated", SortOrder = 9 });
        rig.Instructors.Insert(new Instructor { LastName = "Nobody", FirstName = "Zed", Initials = "ZN" });
        rig.Rooms.Insert(new Room { Building = "ZZZ", RoomNumber = "999", SortOrder = 9 });

        var outcome = rig.Service.Import(sheet, shown, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(3, outcome.Counts.Sections);
        Assert.Equal(4, rig.Sections.GetAll(rig.Fall.Id).Count);
    }

    /// <summary>A change to something the file does refer to (a room the file would have created now exists) is a change.</summary>
    [Fact]
    public void Import_ARecordTheFilePlannedToCreateAppearsAfterTheCheck_IsChanged()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = Sheet();
        var shown = rig.Plan(sheet);
        rig.Rooms.Insert(new Room { Building = "ART", RoomNumber = "101", SortOrder = 9 });
        var before = rig.DumpAllTables();

        var outcome = rig.Service.Import(sheet, shown, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Changed, outcome.Kind);
        Assert.Equal(before, rig.DumpAllTables());
        Assert.DoesNotContain(outcome.Report.Plan.NewRooms, r => r.Record.Building == "ART");
    }

    /// <summary>A file with problems is never written: Import returns "changed" with the report that has them.</summary>
    [Fact]
    public void Import_AReportWithProblems_WritesNothing()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = SectionImportTestKit.Sheet(
            SectionImportTestKit.Sec(2, "CHEM", "101", "A"),
            SectionImportTestKit.Sec(3, "CHEM", "101", "", (C.SectionType, "Lecture")));
        var shown = rig.Plan(sheet);
        Assert.True(shown.HasProblems);
        var before = rig.DumpAllTables();

        var outcome = rig.Service.Import(sheet, shown, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Changed, outcome.Kind);
        Assert.True(outcome.Report.HasProblems);
        Assert.Equal(before, rig.DumpAllTables());
    }

    // ── Import: re-import and read-only ───────────────────────────────────────

    /// <summary>
    /// Importing the same sheet twice: the second time every section is skipped, nothing is inserted (by the
    /// service or by the writer itself) and no table's row count changes.
    /// </summary>
    [Fact]
    public void Import_TheSameSheetTwice_TheSecondTimeAddsNothing()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = Sheet();
        var first = rig.Service.Import(sheet, rig.Plan(sheet), rig.Fall.Id);
        Assert.Equal(ImportOutcomeKind.Written, first.Kind);
        Assert.Equal(3, first.Counts.Sections);
        var countsAfterFirst = RowCounts(rig);
        var dumpAfterFirst = rig.DumpAllTables();

        var shownAgain = rig.Plan(sheet);
        var second = rig.Service.Import(sheet, shownAgain, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Written, second.Kind);
        Assert.Equal(ImportCounts.None, second.Counts);
        Assert.Empty(second.Report.Plan.NewSections);
        Assert.Equal(new[] { "CHEM101 A", "BIOL200 B", "CHEM101 C" }, second.Report.SkippedSections.Select(s => s.Key.Label).ToArray());
        Assert.Equal(countsAfterFirst, RowCounts(rig));
        Assert.Equal(dumpAfterFirst, rig.DumpAllTables());

        // The writer itself, handed the second plan, also inserts nothing.
        Assert.Equal(ImportCounts.None, rig.Writer.Write(shownAgain.Plan, rig.Fall.Id));
        Assert.Equal(countsAfterFirst, RowCounts(rig));
    }

    /// <summary>
    /// A writer exception propagates out of Import (here a refused write in a read-only session), and nothing
    /// is written.
    /// </summary>
    [Fact]
    public void Import_InAReadOnlySession_PropagatesTheRefusal_AndWritesNothing()
    {
        using var rw = new ImportRig();
        SeedExisting(rw);
        var sheet = Sheet();
        var shown = rw.Plan(sheet);
        var before = rw.DumpAllTables();

        using (var ro = new ImportRig(rw.Folder, readOnly: true, seed: false, year: rw.Year, semester: rw.Fall, deleteFolder: false))
        {
            Assert.Throws<WriteAccessLostException>(() => ro.Service.Import(sheet, shown, rw.Fall.Id));
        }

        Assert.Equal(before, rw.DumpAllTables());
    }

    // ── Signature ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Two plans of the same sheet against the same database have equal signatures although every new
    /// record has a fresh id, and the signature holds none of those ids.
    /// </summary>
    [Fact]
    public void Signature_TwoPlansOfTheSameSheet_AreEqual_AndHoldNoIds()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = Sheet();

        var one = rig.Plan(sheet);
        var two = rig.Plan(sheet);

        Assert.NotEqual(one.Plan.NewSubjects[0].Record.Id, two.Plan.NewSubjects[0].Record.Id);   // the test is meaningful
        Assert.Equal(ImportReportSignature.Of(one), ImportReportSignature.Of(two));
        Assert.True(ImportReportSignature.AreEqual(one, two));

        var newIds = one.Plan.NewSubjects.Select(s => s.Record.Id)
            .Concat(one.Plan.NewRooms.Select(r => r.Record.Id))
            .Concat(one.Plan.NewCourses.Select(c => c.Record.Id))
            .Concat(one.Plan.NewInstructors.Select(i => i.Record.Id))
            .ToList();
        Assert.NotEmpty(newIds);
        var signature = string.Join("\n", ImportReportSignature.Of(one));
        foreach (var id in newIds)
            Assert.DoesNotContain(id, signature);
    }

    /// <summary>
    /// The signature names what the user sees: new records by their identity, new and skipped sections by
    /// label, so a different sheet gives a different signature.
    /// </summary>
    [Fact]
    public void Signature_CoversWhatTheUserSees_AndDiffersForADifferentSheet()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var report = rig.Plan(Sheet());

        var lines = ImportReportSignature.Of(report).Select(l => l.Replace('\u001F', '|')).ToList();

        Assert.Contains("subject|BIOL|BIOL", lines);
        Assert.Contains("sectionType|Seminar", lines);
        Assert.Contains("meetingType|Studio", lines);
        Assert.Contains("tag|Lab Safety", lines);
        Assert.Contains(lines, l => l.StartsWith("room|ART|101|existing:"));
        Assert.Contains("course|BIOL200|Genetics", lines);
        Assert.Contains("instructor|Jones|Mary|MJ", lines);
        Assert.Contains("section|CHEM101 A", lines);
        Assert.Contains("section|BIOL200 B", lines);
        Assert.Contains("section|CHEM101 C", lines);

        var fewer = rig.Plan(SectionImportTestKit.Sheet(Sheet().Rows.Take(3).ToArray()));
        Assert.False(ImportReportSignature.AreEqual(report, fewer));
    }
}
