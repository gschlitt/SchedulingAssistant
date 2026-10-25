using Microsoft.Extensions.DependencyInjection;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.Management;
using Xunit;
using static TermPoint.Tests.SectionImportTestKit;
using static TermPoint.Tests.TestSupport.ImportScenario;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="ImportSectionsViewModel"/>: every stage reached from a real file over a temporary
/// database, the exact text of every message, the commands' enabled state, the live availability rule and
/// the failure paths. The main window, the pickers and the clipboard are replaced by fakes
/// (<see cref="FakeImportSectionsHost"/>, <see cref="FakeImportSectionsPlatform"/>), so no window is needed.
/// </summary>
public sealed class ImportSectionsViewModelTests : IDisposable
{
    private const string Where = "Fall (2026-2027)";

    private readonly string _files;
    private readonly List<ImportSectionsViewModel> _vms = new();

    /// <summary>Creates a temporary folder for the import files.</summary>
    public ImportSectionsViewModelTests()
    {
        _files = Path.Combine(Path.GetTempPath(), $"import_vm_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_files);
    }

    /// <summary>Disposes the view models and deletes the temporary folder.</summary>
    public void Dispose()
    {
        foreach (var vm in _vms) vm.Dispose();
        try { Directory.Delete(_files, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Everything a test needs to drive one view model.</summary>
    private sealed record Setup(
        ImportSectionsViewModel Vm, FakeImportSectionsHost Host, FakeImportSectionsPlatform Platform,
        RecordingImportLogger Logger, FocusService Focus);

    /// <summary>Builds a view model over the rig (or over a given service) with fakes for everything else.</summary>
    private Setup NewVm(ImportRig rig, SectionImportService? service = null, FocusService? focus = null)
    {
        var host = new FakeImportSectionsHost(new ImportTarget(rig.Fall.Id, SectionImportTestKit.TermName, AcademicYearName));
        var platform = new FakeImportSectionsPlatform();
        var logger = new RecordingImportLogger();
        focus ??= FocusTestSupport.AllFocus();
        var vm = new ImportSectionsViewModel(service ?? rig.Service, host, platform, focus, rig.Courses, logger);
        _vms.Add(vm);
        return new Setup(vm, host, platform, logger, focus);
    }

    /// <summary>Writes a sheet as a CSV file in the temporary folder.</summary>
    private string Csv(ImportSheet sheet, string name = "sections.csv") => ImportCsv.Write(_files, name, sheet);

    /// <summary>A sheet with the standard rows (3 new sections, 1 course, 1 instructor, setup values), plus the existing section Z.</summary>
    private static ImportSheet SheetWithSkipped()
    {
        var standard = Sheet();
        return SectionImportTestKit.Sheet(
            standard.Rows.Append(Sec(6, "CHEM", "101", "Z")).ToArray());
    }

    /// <summary>Checks a sheet and asserts it reached Ready-to-import.</summary>
    private async Task<Setup> CheckedStandard(ImportRig rig, ImportSheet? sheet = null, FocusService? focus = null)
    {
        var s = NewVm(rig, focus: focus);
        await s.Vm.CheckFileAsync(Csv(sheet ?? Sheet()));
        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        return s;
    }

    // ── Stages reached from CheckFileAsync ────────────────────────────────────

    /// <summary>A file that is not there: the Problems stage with the one plain file message, no path.</summary>
    [Fact]
    public async Task CheckFile_FileProblem_IsTheProblemsStageWithThatOneMessage()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);

        await s.Vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.True(s.Vm.IsProblems);
        Assert.False(s.Vm.IsReadyToChoose);
        Assert.Equal(new[] { SectionImportService.CouldntOpenMessage }, s.Vm.ProblemLines.ToArray());
        Assert.Equal("This file can't be imported yet. Nothing has been changed.", s.Vm.ProblemsHeading);
    }

    /// <summary>A file with row problems: one line per problem, in the "Row n, Column: message" form.</summary>
    [Fact]
    public async Task CheckFile_RowProblems_AreListedOneLineEach()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        // Row 2 has no Section Code; row 3 names a Begin time that is not a time.
        var sheet = SectionImportTestKit.Sheet(
            Sec(2, "CHEM", "101", ""),
            Mtg(3, "CHEM", "101", "B", "M", "ten", "1100"));
        var expected = rig.Plan(sheet).Problems;
        Assert.True(expected.Count >= 2, "the scenario should have produced problems");
        var s = NewVm(rig);

        await s.Vm.CheckFileAsync(Csv(sheet));

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.Equal(expected.Select(ImportProblemText.Line).ToArray(), s.Vm.ProblemLines.ToArray());
        Assert.Contains(s.Vm.ProblemLines, l => l.StartsWith("Row 2, Section Code: ", StringComparison.Ordinal));
        Assert.Contains(s.Vm.ProblemLines, l => l.StartsWith("Row 3, Begin: ", StringComparison.Ordinal));
    }

    /// <summary>Every section already in the semester: Nothing-to-add, naming the term and year.</summary>
    [Fact]
    public async Task CheckFile_EverySectionAlreadyThere_IsNothingToAdd()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = NewVm(rig);

        await s.Vm.CheckFileAsync(Csv(SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "Z"))));

        Assert.Equal(ImportStage.NothingToAdd, s.Vm.Stage);
        Assert.True(s.Vm.IsNothingToAdd);
        Assert.Equal("Every section in this file is already in Fall (2026-2027).", s.Vm.NothingToAddText);
        Assert.False(s.Vm.ImportCommand.CanExecute(null));
        Assert.True(s.Vm.ChooseFileCommand.CanExecute(null));
    }

    /// <summary>A file with no sections in it must not claim every section is already there.</summary>
    [Fact]
    public async Task CheckFile_NoSectionsAtAll_SaysSo()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);

        await s.Vm.CheckFileAsync(Csv(SectionImportTestKit.Sheet()));

        Assert.Equal(ImportStage.NothingToAdd, s.Vm.Stage);
        Assert.Equal("This file has no sections to add.", s.Vm.NothingToAddText);
    }

    /// <summary>A good file: Ready-to-import with the summary, the skipped line, and every list as display strings.</summary>
    [Fact]
    public async Task CheckFile_GoodFile_IsReadyToImportWithSummaryAndLists()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);

        var s = await CheckedStandard(rig, SheetWithSkipped());
        var vm = s.Vm;

        Assert.True(vm.IsReadyToImport);
        Assert.Equal("Will add 3 sections, 1 new course and 1 new instructor.", vm.SummaryText);
        Assert.Equal("Already in Fall, not changed: 1 section.", vm.SkippedText);
        Assert.True(vm.HasSkippedText);
        Assert.Equal(new[] { "CHEM101 A", "BIOL200 B", "CHEM101 C" }, vm.NewSections.ToArray());
        Assert.Equal(new[] { "BIOL200 — Genetics" }, vm.NewCourses.ToArray());
        Assert.Equal(new[] { "Jones, Mary (MJ)" }, vm.NewInstructors.ToArray());
        Assert.True(vm.HasNewCourses);
        Assert.True(vm.HasNewInstructors);
        Assert.Equal("Sections will be added to Fall (2026-2027).", vm.TargetLine);
        Assert.Equal(ImportStage.ReadyToImport, vm.Stage);
        Assert.True(vm.ImportCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.ChooseFileCommand.CanExecute(null));
    }

    /// <summary>"New in TermPoint" groups every new setup value by kind, with the heading, and shows new subjects with a hint.</summary>
    [Fact]
    public async Task CheckFile_NewSetupValues_AreGroupedByKind()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);

        var s = await CheckedStandard(rig);

        var groups = s.Vm.NewInTermPoint;
        Assert.True(s.Vm.HasNewInTermPoint);
        Assert.Equal(new[] { "Subjects", "Section types", "Meeting types", "Tags", "Rooms" },
            groups.Select(g => g.Heading).ToArray());
        Assert.Equal(new[] { "BIOL (add its full name in Subjects)" }, groups[0].Items.ToArray());
        Assert.Equal(new[] { "Seminar" }, groups[1].Items.ToArray());
        Assert.Equal(new[] { "Studio" }, groups[2].Items.ToArray());
        Assert.Equal(new[] { "Lab Safety" }, groups[3].Items.ToArray());
        Assert.Equal(new[] { "ART 101" }, groups[4].Items.ToArray());
    }

    /// <summary>Warnings are listed in the report's order and do not block the import.</summary>
    [Fact]
    public async Task CheckFile_Warnings_AreListedInReportOrder()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var expected = rig.Plan(Sheet()).Warnings.Select(w => w.Message).ToArray();
        Assert.NotEmpty(expected);   // an empty test database has no legal start times: one file-wide warning

        var s = await CheckedStandard(rig);

        Assert.Equal(expected, s.Vm.Warnings.ToArray());
        Assert.True(s.Vm.HasWarnings);
        Assert.True(s.Vm.ImportCommand.CanExecute(null));
    }

    /// <summary>A unreadable (internal failure) check is logged and shown as one plain message, not exception text.</summary>
    [Fact]
    public async Task CheckFile_AnInternalFailure_IsLoggedAndShownAsAPlainMessage()
    {
        using var rig = new ImportRig();
        // A semester id that is not in the database makes the snapshot throw InvalidOperationException.
        var s = NewVm(rig);
        s.Host.CurrentTarget = new ImportTarget("no-such-semester", "Fall", AcademicYearName);

        await s.Vm.CheckFileAsync(Csv(Sheet()));

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.Equal(new[] { ImportMessages.CheckFailed }, s.Vm.ProblemLines.ToArray());
        Assert.Single(s.Logger.Errors);
        Assert.IsType<InvalidOperationException>(s.Logger.Errors[0]);
    }

    /// <summary>Check again re-reads the same file after it has been fixed.</summary>
    [Fact]
    public async Task CheckAgain_RereadsTheSameFile()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = NewVm(rig);
        var path = Csv(SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "")), "fix-me.csv");
        await s.Vm.CheckFileAsync(path);
        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.True(s.Vm.CheckAgainCommand.CanExecute(null));

        Csv(Sheet(), "fix-me.csv");   // the user fixes the file
        await s.Vm.CheckAgainCommand.ExecuteAsync(null);

        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        Assert.Empty(s.Vm.ProblemLines);
    }

    /// <summary>Cancel goes back to the start and forgets the report; nothing was written.</summary>
    [Fact]
    public async Task Cancel_GoesBackToReadyToChoose_AndClearsTheReport()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var before = rig.DumpAllTables();
        var s = await CheckedStandard(rig);

        s.Vm.CancelCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToChoose, s.Vm.Stage);
        Assert.Equal(string.Empty, s.Vm.SummaryText);
        Assert.Empty(s.Vm.NewSections);
        Assert.Empty(s.Vm.Warnings);
        Assert.Empty(s.Vm.NewInTermPoint);
        Assert.Equal(before, rig.DumpAllTables());
    }

    // ── Commands per stage ────────────────────────────────────────────────────

    /// <summary>The enabled commands in each stage follow the stage table.</summary>
    [Fact]
    public async Task Commands_AreEnabledPerTheStageTable()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = NewVm(rig);
        var vm = s.Vm;

        // Ready to choose: Save template and Choose file.
        Assert.True(vm.SaveTemplateCommand.CanExecute(null));
        Assert.True(vm.ChooseFileCommand.CanExecute(null));
        Assert.False(vm.CheckAgainCommand.CanExecute(null));
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.CopyListCommand.CanExecute(null));

        // Problems: Copy list, Check again, Choose file.
        await vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));
        Assert.False(vm.SaveTemplateCommand.CanExecute(null));
        Assert.True(vm.ChooseFileCommand.CanExecute(null));
        Assert.True(vm.CheckAgainCommand.CanExecute(null));
        Assert.True(vm.CopyListCommand.CanExecute(null));
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));

        // Ready to import: Import and Cancel only.
        await vm.CheckFileAsync(Csv(Sheet()));
        Assert.True(vm.ImportCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.ChooseFileCommand.CanExecute(null));
        Assert.False(vm.CheckAgainCommand.CanExecute(null));
        Assert.False(vm.CopyListCommand.CanExecute(null));
        Assert.False(vm.SaveTemplateCommand.CanExecute(null));

        // Done: Choose file only.
        vm.ImportCommand.Execute(null);
        Assert.Equal(ImportStage.Done, vm.Stage);
        Assert.True(vm.ChooseFileCommand.CanExecute(null));
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.CopyListCommand.CanExecute(null));
    }

    // ── Import: Done ──────────────────────────────────────────────────────────

    /// <summary>Import writes, shows Done with the first line and the setup line, and reloads once.</summary>
    [Fact]
    public async Task Import_Written_IsDoneWithTheMessages_AndReloadsOnce()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.Done, s.Vm.Stage);
        Assert.True(s.Vm.IsDone);
        Assert.Equal($"Added 3 sections, 1 course and 1 instructor to {Where}.", s.Vm.DoneMessage);
        Assert.Equal(
            new[]
            {
                "Also added 1 subject, 1 section type, 1 meeting type, 1 tag and 1 room. "
                + "New rooms need their capacity and room type: see Rooms.",
            },
            s.Vm.DoneNotes.ToArray());
        Assert.Equal(1, s.Host.ReloadCount);
        Assert.Equal(4, rig.Sections.GetAll(rig.Fall.Id).Count);
        Assert.Empty(s.Logger.Errors);
        Assert.Null(s.Vm.LastErrorMessage);
    }

    /// <summary>The stage is Done before the reload runs, and a failing reload is not reported as a failed import.</summary>
    [Fact]
    public async Task Import_AFailingReload_DoesNotUndoDone()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        s.Host.ReloadThrows = new InvalidOperationException("reload broke");

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.Done, s.Vm.Stage);
        Assert.Null(s.Vm.LastErrorMessage);
        Assert.Single(s.Logger.Errors);
    }

    /// <summary>An import with no new courses, instructors or setup values has only the first line.</summary>
    [Fact]
    public async Task Import_OnlySections_HasNoSetupLineAndNoCourseOrInstructorParts()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig, SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "N1"), Sec(3, "CHEM", "101", "N2")));

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal($"Added 2 sections to {Where}.", s.Vm.DoneMessage);
        Assert.Empty(s.Vm.DoneNotes);
        Assert.False(s.Vm.HasDoneNotes);
    }

    /// <summary>The rooms sentence appears only when rooms were added.</summary>
    [Fact]
    public async Task Import_SetupValuesWithoutRooms_HasNoRoomsSentence()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "N1", (C.Tags, "Lab Safety"), (C.SectionType, "Seminar")));
        var s = await CheckedStandard(rig, sheet);

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(new[] { "Also added 1 section type and 1 tag." }, s.Vm.DoneNotes.ToArray());
    }

    /// <summary>
    /// Into an empty semester, the undo hint is the last line. (The empty semester also has no records, so
    /// the setup line comes first.)
    /// </summary>
    [Fact]
    public async Task Import_IntoAnEmptySemester_AddsTheUndoHint()
    {
        using var rig = new ImportRig();
        var s = await CheckedStandard(rig, SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "A", (C.CourseTitle, "Intro Chemistry"))));

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal($"Added 1 section and 1 course to {Where}.", s.Vm.DoneMessage);
        Assert.Equal(
            new[]
            {
                "Also added 1 subject.",
                "To take it back, switch to another semester, then use Empty semester in Academic Years.",
            },
            s.Vm.DoneNotes.ToArray());
    }

    /// <summary>
    /// The undo hint comes from the report Import planned, not the first check: a semester that was empty at
    /// Check but had a section by Import time gets no hint (and the import still goes ahead, since what the
    /// file would do is unchanged).
    /// </summary>
    [Fact]
    public async Task Import_UndoHintFollowsTheImportReport_NotTheFirstCheck()
    {
        using var rig = new ImportRig();
        var math = new Subject { Name = "Mathematics", CalendarAbbreviation = "MATH" };
        rig.Subjects.Insert(math);
        var math101 = new Course { SubjectId = math.Id, CalendarCode = "MATH101", Title = "Calculus" };
        rig.Courses.Insert(math101);
        var s = await CheckedStandard(rig, SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "A")));

        // Another user (or a detached panel) adds an unrelated section after the check.
        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = math101.Id, SectionCode = "Q" });
        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.Done, s.Vm.Stage);
        Assert.DoesNotContain(ImportMessages.UndoHint, s.Vm.DoneNotes);
    }

    /// <summary>
    /// A section outside the chosen subjects is mentioned: the sentence is built from the courses read after
    /// the write and the created sections' courses.
    /// </summary>
    [Fact]
    public async Task Import_SectionsOutsideTheChosenSubjects_AreMentioned()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var focus = FocusTestSupport.AllFocus();
        focus.SetFocus(SubjectFocus.ForSubjects(new[] { existing.Chem.Id }));
        var s = await CheckedStandard(rig, focus: focus);

        s.Vm.ImportCommand.Execute(null);

        // BIOL200 B is in the new subject BIOL; CHEM101 A and C are in CHEM, which is chosen.
        Assert.Contains("1 of them is in a subject you haven't chosen, so it's not shown.", s.Vm.DoneNotes);
        // Order: setup line, then the focus sentence (no undo hint: the semester was not empty).
        Assert.StartsWith("Also added", s.Vm.DoneNotes[0]);
        Assert.Equal("1 of them is in a subject you haven't chosen, so it's not shown.", s.Vm.DoneNotes[1]);
        Assert.Equal(2, s.Vm.DoneNotes.Count);
    }

    /// <summary>After Done the flyout stays open: the next file can be chosen and checked.</summary>
    [Fact]
    public async Task Done_StaysOpen_ForTheNextFile()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        s.Vm.ImportCommand.Execute(null);
        Assert.Equal(ImportStage.Done, s.Vm.Stage);

        s.Platform.PathToOpen = Csv(Sheet(), "again.csv");   // the same file again: nothing left to add
        await s.Vm.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal(ImportStage.NothingToAdd, s.Vm.Stage);
        Assert.Equal(string.Empty, s.Vm.DoneMessage);
    }

    /// <summary>Cancelling the file picker changes nothing.</summary>
    [Fact]
    public async Task ChooseFile_Cancelled_ChangesNothing()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.PathToOpen = null;

        await s.Vm.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal(ImportStage.ReadyToChoose, s.Vm.Stage);
    }

    // ── Import: Changed ───────────────────────────────────────────────────────

    /// <summary>
    /// A section added between Check and Import: the "something changed" heading, the stage for the new
    /// result, and nothing written.
    /// </summary>
    [Fact]
    public async Task Import_TheDatabaseChanged_ShowsTheNewResultAndWritesNothing()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var s = await CheckedStandard(rig);
        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = existing.Chem101.Id, SectionCode = "C" });
        var before = rig.DumpAllTables();

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);   // two sections (A, BIOL200 B) are still new
        Assert.Equal(ImportMessages.ChangedNotice, s.Vm.Notice);
        Assert.Equal("Will add 2 sections, 1 new course and 1 new instructor.", s.Vm.SummaryText);
        Assert.Equal("Already in Fall, not changed: 1 section.", s.Vm.SkippedText);   // C, added meanwhile
        Assert.Equal(0, s.Host.ReloadCount);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>After a change, if the new result has nothing left to add, the stage is Nothing-to-add with the heading.</summary>
    [Fact]
    public async Task Import_TheDatabaseChanged_CanLeaveNothingToAdd()
    {
        using var rig = new ImportRig();
        var existing = SeedExisting(rig);
        var s = await CheckedStandard(rig, SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", "N1")));
        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = existing.Chem101.Id, SectionCode = "N1" });

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.NothingToAdd, s.Vm.Stage);
        Assert.Equal(ImportMessages.ChangedNotice, s.Vm.Notice);
        Assert.True(s.Vm.HasNotice);
        Assert.Equal(0, s.Host.ReloadCount);
    }

    /// <summary>After a change, a new problem gives the Problems stage with the heading.</summary>
    [Fact]
    public async Task Import_TheDatabaseChanged_CanLeaveProblems()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig, SectionImportTestKit.Sheet(
            Mtg(2, "CHEM", "101", "A", "M", "1000", "1100", (C.Room, "210"), (C.Building, "SCI"))));
        // Another user adds a second room that matches the file's "SCI 210" by name, so it is now ambiguous.
        rig.Rooms.Insert(new Room { Building = "SCI", RoomNumber = "210", SortOrder = 2 });

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.Equal(ImportMessages.ChangedNotice, s.Vm.Notice);
        Assert.NotEmpty(s.Vm.ProblemLines);
        Assert.Equal(0, s.Host.ReloadCount);
    }

    // ── Import: failures ──────────────────────────────────────────────────────

    /// <summary>
    /// A refused write in a read-only session: logged, no inline text (the banner tells the user), the stage
    /// stays Ready-to-import, and nothing was written.
    /// </summary>
    [Fact]
    public async Task Import_InAReadOnlySession_LogsAndShowsNoInlineText()
    {
        using var rw = new ImportRig();
        SeedExisting(rw);
        var before = rw.DumpAllTables();

        using (var ro = new ImportRig(rw.Folder, readOnly: true, seed: false, year: rw.Year, semester: rw.Fall, deleteFolder: false))
        {
            var s = await CheckedStandard(ro);

            s.Vm.ImportCommand.Execute(null);

            Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
            Assert.Null(s.Vm.LastErrorMessage);
            Assert.Single(s.Logger.Errors);
            Assert.True(WriteFailureText.IsWriteAccessLost(s.Logger.Errors[0]));
            Assert.Equal(0, s.Host.ReloadCount);
        }

        Assert.Equal(before, rw.DumpAllTables());
    }

    /// <summary>Any other write failure: the plain "nothing was changed" line, the stage stays, the database is as it was.</summary>
    [Fact]
    public async Task Import_AWriterFailure_ShowsThePlainMessageAndWritesNothing()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var (rooms, _) = RecordingRepositoryProxy<IRoomRepository>.Wrap(rig.Rooms, (name, n) => name == "Insert");
        var service = new SectionImportService(new ImportFileReader(), rig.SnapshotBuilder, rig.NewWriter(rooms: rooms));
        var s = NewVm(rig, service);
        await s.Vm.CheckFileAsync(Csv(Sheet()));
        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        var before = rig.DumpAllTables();

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        Assert.Equal("The sections couldn't be added. Nothing was changed.", s.Vm.LastErrorMessage);
        Assert.Single(s.Logger.Errors);
        Assert.Equal(0, s.Host.ReloadCount);
        Assert.Equal(before, rig.DumpAllTables());
    }

    // ── Availability ──────────────────────────────────────────────────────────

    /// <summary>Each unavailable state, arriving while the result is on screen, disables Import and sets the reason.</summary>
    [Theory]
    [InlineData(false, 1, false, ImportAvailability.ReadOnlyReason)]
    [InlineData(true, 0, false, ImportAvailability.NoSemesterReason)]
    [InlineData(true, 2, false, ImportAvailability.SeveralSemestersReason)]
    [InlineData(true, 1, true, ImportAvailability.EditingReason)]
    public async Task Availability_ChangingWhileOpen_DisablesImportAndSetsTheReason(
        bool write, int semesters, bool editing, string reason)
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        Assert.True(s.Vm.ImportCommand.CanExecute(null));
        Assert.Null(s.Vm.UnavailableReason);
        Assert.True(s.Vm.IsImportAvailable);

        s.Host.Change(isWriteEnabled: write, shownSemesterCount: semesters, isEditingSection: editing);

        Assert.False(s.Vm.ImportCommand.CanExecute(null));
        Assert.Equal(reason, s.Vm.UnavailableReason);
        Assert.False(s.Vm.IsImportAvailable);
    }

    /// <summary>Editing starting and then finishing disables and re-enables Import.</summary>
    [Fact]
    public async Task Availability_EditingStartsAndStops_TogglesImport()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);

        s.Host.Change(isEditingSection: true);
        Assert.False(s.Vm.ImportCommand.CanExecute(null));
        Assert.Equal("Finish editing the section first.", s.Vm.UnavailableReason);

        s.Host.Change(isEditingSection: false);
        Assert.True(s.Vm.ImportCommand.CanExecute(null));
        Assert.Null(s.Vm.UnavailableReason);
    }

    /// <summary>
    /// Import re-evaluates availability itself: even if the button's state is stale (the host changed without
    /// telling anyone), nothing is written and the reason is set.
    /// </summary>
    [Fact]
    public async Task Import_ReChecksAvailabilityItself()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        var before = rig.DumpAllTables();
        Assert.Null(s.Vm.UnavailableReason);   // the button believes Import is available
        s.Host.SetStateWithoutNotifying(isEditingSection: true);   // ...but a section editor has just opened

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        Assert.Equal(ImportAvailability.EditingReason, s.Vm.UnavailableReason);
        Assert.Equal(0, s.Host.ReloadCount);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>The target line follows the shown semester, and says what to do when there isn't exactly one.</summary>
    [Fact]
    public void TargetLine_FollowsTheShownSemester()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        Assert.Equal("Sections will be added to Fall (2026-2027).", s.Vm.TargetLine);

        s.Host.ShowOtherSemester(new ImportTarget("sem-2", "Winter", "2026-2027"));
        Assert.Equal("Sections will be added to Winter (2026-2027).", s.Vm.TargetLine);

        s.Host.Change(shownSemesterCount: 2);
        Assert.Equal(ImportAvailability.SeveralSemestersReason, s.Vm.TargetLine);
        Assert.False(s.Vm.ChooseFileCommand.CanExecute(null));

        s.Host.Change(shownSemesterCount: 0);
        Assert.Equal(ImportAvailability.NoSemesterReason, s.Vm.TargetLine);
    }

    /// <summary>
    /// Showing another semester while a result is on screen drops the result (it was checked against the
    /// other semester), tells the user, and writes nothing.
    /// </summary>
    [Fact]
    public async Task ShownSemesterChanges_WhileAResultIsOnScreen_DropsTheResult()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        var before = rig.DumpAllTables();

        s.Host.ShowOtherSemester(new ImportTarget("sem-2", "Winter", "2026-2027"));

        Assert.Equal(ImportStage.ReadyToChoose, s.Vm.Stage);
        Assert.Equal(ImportMessages.SemesterChangedNotice, s.Vm.Notice);
        Assert.Empty(s.Vm.NewSections);
        Assert.Equal(before, rig.DumpAllTables());
    }

    /// <summary>The same guard holds if the change arrives without a notification: Import refuses and starts over.</summary>
    [Fact]
    public async Task Import_ToADifferentSemesterThanChecked_IsRefused()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var s = await CheckedStandard(rig);
        var before = rig.DumpAllTables();
        s.Host.SetStateWithoutNotifying(target: new ImportTarget("sem-2", "Winter", "2026-2027"));

        s.Vm.ImportCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToChoose, s.Vm.Stage);
        Assert.Equal(ImportMessages.SemesterChangedNotice, s.Vm.Notice);
        Assert.Equal(before, rig.DumpAllTables());
        Assert.Equal(0, s.Host.ReloadCount);
    }

    /// <summary>Disposing the view model stops it listening to the host.</summary>
    [Fact]
    public void Dispose_StopsListeningToTheHost()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        Assert.Equal(1, s.Host.WatcherCount);

        s.Vm.Dispose();

        Assert.Equal(0, s.Host.WatcherCount);
    }

    // ── Copy list ─────────────────────────────────────────────────────────────

    /// <summary>Copy list puts a header and one tab-separated line per problem on the clipboard, and confirms it.</summary>
    [Fact]
    public async Task CopyList_PutsTheProblemsOnTheClipboardAsTabSeparatedText()
    {
        using var rig = new ImportRig();
        SeedExisting(rig);
        var sheet = SectionImportTestKit.Sheet(Sec(2, "CHEM", "101", ""), Mtg(3, "CHEM", "101", "B", "M", "ten", "1100"));
        var problems = rig.Plan(sheet).Problems;
        var s = NewVm(rig);
        await s.Vm.CheckFileAsync(Csv(sheet));

        await s.Vm.CopyListCommand.ExecuteAsync(null);

        var expected = "Row\tColumn\tProblem\r\n" + string.Join("\r\n",
            problems.Select(p => $"{p.Row}\t{p.Column}\t{p.Message}"));
        Assert.Equal(expected, s.Platform.ClipboardText);
        Assert.Equal("Copied.", s.Vm.StatusMessage);
        Assert.True(s.Vm.HasStatusMessage);
    }

    /// <summary>A problem with the file as a whole is copied with a blank row and column.</summary>
    [Fact]
    public async Task CopyList_AFileProblem_HasBlankRowAndColumn()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        await s.Vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));

        await s.Vm.CopyListCommand.ExecuteAsync(null);

        Assert.Equal($"Row\tColumn\tProblem\r\n\t\t{SectionImportService.CouldntOpenMessage}", s.Platform.ClipboardText);
    }

    /// <summary>An unavailable clipboard gives a plain message, not an exception.</summary>
    [Fact]
    public async Task CopyList_WithNoClipboard_SaysSo()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.ClipboardWorks = false;
        await s.Vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));

        await s.Vm.CopyListCommand.ExecuteAsync(null);

        Assert.Null(s.Platform.ClipboardText);
        Assert.Equal(ImportMessages.ListNotCopied, s.Vm.LastErrorMessage);
        Assert.Null(s.Vm.StatusMessage);
    }

    // ── Save template ─────────────────────────────────────────────────────────

    /// <summary>Save template writes the shipped workbook's bytes to the chosen path and confirms it.</summary>
    [Fact]
    public async Task SaveTemplate_WritesTheTemplateBytes()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        var path = Path.Combine(_files, "My Template.xlsx");
        s.Platform.PathToSave = path;

        await s.Vm.SaveTemplateCommand.ExecuteAsync(null);

        Assert.Equal(FakeImportSectionsPlatform.ShippedTemplateBytes(), File.ReadAllBytes(path));
        Assert.Equal("Template saved.", s.Vm.StatusMessage);
        Assert.Null(s.Vm.LastErrorMessage);
        Assert.Equal("TermPoint Sections Template.xlsx", s.Platform.SuggestedFileName);
        Assert.Equal(ImportMessages.TemplateSaveRequest, s.Platform.LastSaveRequest);
    }

    /// <summary>Cancelling the Save dialog writes nothing and says nothing.</summary>
    [Fact]
    public async Task SaveTemplate_Cancelled_WritesNothing()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.PathToSave = null;

        await s.Vm.SaveTemplateCommand.ExecuteAsync(null);

        Assert.Null(s.Vm.StatusMessage);
        Assert.Null(s.Vm.LastErrorMessage);
        Assert.Empty(Directory.GetFiles(_files));
    }

    /// <summary>A folder that can't be written gives the "try another folder" message, with no path or exception text.</summary>
    [Fact]
    public async Task SaveTemplate_ToAnUnwritableLocation_SaysToTryAnotherFolder()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        var path = Path.Combine(_files, "no-such-folder", "Template.xlsx");

        await s.Vm.SaveTemplateToAsync(path);

        Assert.Equal("TermPoint couldn't save the template there. Try another folder, such as Documents.", s.Vm.LastErrorMessage);
        Assert.Null(s.Vm.StatusMessage);
        Assert.DoesNotContain(s.Logger.Lines, l => l.Contains("no-such-folder", StringComparison.Ordinal));
    }

    /// <summary>A missing template asset gives the same message.</summary>
    [Fact]
    public async Task SaveTemplate_WhenTheAssetCannotBeRead_SaysTheSameThing()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.TemplateReadThrows = new FileNotFoundException();

        await s.Vm.SaveTemplateToAsync(Path.Combine(_files, "Template.xlsx"));

        Assert.Equal(ImportMessages.TemplateNotSaved, s.Vm.LastErrorMessage);
        Assert.False(File.Exists(Path.Combine(_files, "Template.xlsx")));
    }

    // ── Instructions: reading, showing ────────────────────────────────────────

    /// <summary>The blocks come from the platform's text, are read once however often they are asked for, and nothing is "unavailable".</summary>
    [Fact]
    public void InstructionBlocks_ComeFromThePlatformText_AndAreReadOnce()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        Assert.Equal(0, s.Platform.InstructionsReadCount);   // lazy: nothing is read until asked for

        var first = s.Vm.InstructionBlocks;
        var second = s.Vm.InstructionBlocks;

        var expected = ImportInstructions.Parse(FakeImportSectionsPlatform.ShippedInstructionsText());
        Assert.NotEmpty(first);
        Assert.Equal(expected.ToArray(), first.ToArray());
        Assert.Same(first, second);
        Assert.Null(s.Vm.InstructionsUnavailableText);
        Assert.Equal(1, s.Platform.InstructionsReadCount);
    }

    /// <summary>
    /// A read failure gives an empty list, the one "couldn't show" line, and a log line that names what failed
    /// but holds neither a path nor the exception's text. It is tried once, not on every access, and the answer
    /// is the same whichever property is read first.
    /// </summary>
    [Fact]
    public void InstructionBlocks_WhenTheReadFails_AreEmpty_SayUnavailable_AndLogQuietly()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.InstructionsReadThrows = new FileNotFoundException(@"secret C:\Users\someone\missing.txt");

        // Ask for the line first: it must load the instructions itself.
        var text = s.Vm.InstructionsUnavailableText;
        var blocks = s.Vm.InstructionBlocks;
        _ = s.Vm.InstructionBlocks;

        Assert.Equal(ImportMessages.InstructionsUnavailable, text);
        Assert.Equal(
            "TermPoint couldn't open its copy of the instructions. They're also on the Instructions sheet of the template.", text);
        Assert.Empty(blocks);
        Assert.Equal(1, s.Platform.InstructionsReadCount);
        var line = Assert.Single(s.Logger.Lines, l => l.Contains("reading the instructions", StringComparison.Ordinal));
        Assert.Contains("FileNotFoundException", line);
        Assert.DoesNotContain("secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", line, StringComparison.Ordinal);
        Assert.Empty(s.Logger.Errors);   // a quiet log line, not a logged error
    }

    /// <summary>The instructions start hidden, and the toggle flips both the flag and the button's text, telling the view.</summary>
    [Fact]
    public void ShowInstructions_StartsHidden_AndTheToggleFlipsTheTexts()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        var changed = new List<string?>();
        s.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(s.Vm.ShowInstructions);
        Assert.Equal("Show instructions", s.Vm.InstructionsToggleText);
        Assert.True(s.Vm.ToggleInstructionsCommand.CanExecute(null));

        s.Vm.ToggleInstructionsCommand.Execute(null);

        Assert.True(s.Vm.ShowInstructions);
        Assert.Equal("Hide instructions", s.Vm.InstructionsToggleText);
        Assert.Contains(nameof(ImportSectionsViewModel.InstructionsToggleText), changed);

        s.Vm.ToggleInstructionsCommand.Execute(null);

        Assert.False(s.Vm.ShowInstructions);
        Assert.Equal("Show instructions", s.Vm.InstructionsToggleText);
    }

    /// <summary>Open instructions stay open when the stage changes, so the Problems screen shows what the first screen showed.</summary>
    [Fact]
    public async Task ShowInstructions_IsNotResetByAStageChange()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Vm.ToggleInstructionsCommand.Execute(null);

        await s.Vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.True(s.Vm.ShowInstructions);
        Assert.Equal("Hide instructions", s.Vm.InstructionsToggleText);
    }

    // ── Instructions: saving ──────────────────────────────────────────────────

    /// <summary>Save instructions writes a UTF-8 web page of the instructions to the chosen path and confirms it.</summary>
    [Fact]
    public async Task SaveInstructions_WritesAUtf8WebPage()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        var path = Path.Combine(_files, "My Instructions.html");
        s.Platform.PathToSave = path;

        await s.Vm.SaveInstructionsCommand.ExecuteAsync(null);

        // A strict decoder fails on bytes that are not valid UTF-8.
        var html = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path));
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);

        // The page holds the shipped text. Check its first heading, and its first block with a non-ASCII
        // character (such as the "\u2026" of "Import Sections\u2026") if it has one, without pinning their wording,
        // so editing the instructions doesn't break this test.
        var blocks = ImportInstructions.Parse(FakeImportSectionsPlatform.ShippedInstructionsText());
        var firstHeading = blocks.First(b => b.IsHeading).Text;
        Assert.Contains($"<h2>{System.Net.WebUtility.HtmlEncode(firstHeading)}</h2>", html);
        var nonAscii = blocks.FirstOrDefault(b => b.Text.Any(c => c > 127));
        if (nonAscii is not null)
            Assert.Contains(System.Net.WebUtility.HtmlEncode(nonAscii.Text), html);   // survives the UTF-8 round trip
        Assert.Equal("Instructions saved.", s.Vm.StatusMessage);
        Assert.Null(s.Vm.LastErrorMessage);
    }

    /// <summary>The Save dialog is offered the instructions' name and the web page file type.</summary>
    [Fact]
    public async Task SaveInstructions_OffersTheInstructionsNameAndFileType()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.PathToSave = Path.Combine(_files, "Instructions.html");

        await s.Vm.SaveInstructionsCommand.ExecuteAsync(null);

        Assert.Equal("TermPoint Section Import Instructions.html", s.Platform.SuggestedFileName);
        Assert.Equal(ImportMessages.InstructionsSaveRequest, s.Platform.LastSaveRequest);
        Assert.Equal("html", s.Platform.LastSaveRequest!.DefaultExtension);
        Assert.Equal("*.html", s.Platform.LastSaveRequest.Pattern);
    }

    /// <summary>Cancelling the Save dialog writes nothing and says nothing.</summary>
    [Fact]
    public async Task SaveInstructions_Cancelled_WritesNothing()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.PathToSave = null;

        await s.Vm.SaveInstructionsCommand.ExecuteAsync(null);

        Assert.Null(s.Vm.StatusMessage);
        Assert.Null(s.Vm.LastErrorMessage);
        Assert.Empty(Directory.GetFiles(_files));
    }

    /// <summary>A folder that doesn't exist gives the "try another folder" message, with no path in the log.</summary>
    [Fact]
    public async Task SaveInstructions_IntoAFolderThatDoesNotExist_SaysToTryAnotherFolder()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        var path = Path.Combine(_files, "no-such-folder", "Instructions.html");

        await s.Vm.SaveInstructionsToAsync(path);

        Assert.Equal("TermPoint couldn't save the instructions there. Try another folder, such as Documents.", s.Vm.LastErrorMessage);
        Assert.Equal(ImportMessages.InstructionsNotSaved, s.Vm.LastErrorMessage);
        Assert.Null(s.Vm.StatusMessage);
        Assert.DoesNotContain(s.Logger.Lines, l => l.Contains("no-such-folder", StringComparison.Ordinal));
    }

    /// <summary>
    /// When the instructions could not be loaded, saving writes no empty page and says the instructions couldn't
    /// be opened (not "try another folder", which would not help).
    /// </summary>
    [Fact]
    public async Task SaveInstructions_WhenTheInstructionsCannotBeLoaded_WritesNoPage()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        s.Platform.InstructionsReadThrows = new FileNotFoundException();
        var path = Path.Combine(_files, "Instructions.html");

        await s.Vm.SaveInstructionsToAsync(path);

        Assert.Equal(ImportMessages.InstructionsUnavailable, s.Vm.LastErrorMessage);
        Assert.Null(s.Vm.StatusMessage);
        Assert.False(File.Exists(path));
    }

    /// <summary>A save clears the previous confirmation or error before it reports its own.</summary>
    [Fact]
    public async Task SaveInstructions_ClearsThePreviousStatusAndError()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);

        await s.Vm.SaveInstructionsToAsync(Path.Combine(_files, "no-such-folder", "a.html"));
        Assert.NotNull(s.Vm.LastErrorMessage);

        await s.Vm.SaveInstructionsToAsync(Path.Combine(_files, "b.html"));

        Assert.Null(s.Vm.LastErrorMessage);
        Assert.Equal(ImportMessages.InstructionsSaved, s.Vm.StatusMessage);
    }

    /// <summary>Save instructions runs on the first screen and is off on Problems (where Show instructions is the offer).</summary>
    [Fact]
    public async Task SaveInstructionsCommand_RunsOnReadyToChoose_NotOnProblems()
    {
        using var rig = new ImportRig();
        var s = NewVm(rig);
        Assert.True(s.Vm.SaveInstructionsCommand.CanExecute(null));

        await s.Vm.CheckFileAsync(Path.Combine(_files, "gone.csv"));

        Assert.Equal(ImportStage.Problems, s.Vm.Stage);
        Assert.False(s.Vm.SaveInstructionsCommand.CanExecute(null));
        Assert.True(s.Vm.ToggleInstructionsCommand.CanExecute(null));   // the toggle is on both screens

        await s.Vm.CheckFileAsync(Csv(Sheet()));

        Assert.Equal(ImportStage.ReadyToImport, s.Vm.Stage);
        Assert.False(s.Vm.SaveInstructionsCommand.CanExecute(null));

        s.Vm.CancelCommand.Execute(null);

        Assert.Equal(ImportStage.ReadyToChoose, s.Vm.Stage);
        Assert.True(s.Vm.SaveInstructionsCommand.CanExecute(null));
    }

    /// <summary>
    /// The view model and everything the service needs resolve from a container registered the way the desktop
    /// <c>ConfigureServices</c> registers them (transient classes over singleton repositories). The platform
    /// and host are the fakes here, because the real ones need the main window.
    /// </summary>
    [Fact]
    public void TheViewModelResolvesFromTheContainer()
    {
        using var rig = new ImportRig();
        var services = new ServiceCollection();
        services.AddSingleton<IDatabaseContext>(rig.Db);
        services.AddSingleton<ISemesterRepository>(new SemesterRepository(rig.Db));
        services.AddSingleton<IAcademicYearRepository>(new AcademicYearRepository(rig.Db));
        services.AddSingleton<ILegalStartTimeRepository>(new LegalStartTimeRepository(rig.Db));
        services.AddSingleton<ISubjectRepository>(rig.Subjects);
        services.AddSingleton<ICampusRepository>(rig.Campuses);
        services.AddSingleton<ISchedulingEnvironmentRepository>(rig.Environment);
        services.AddSingleton<IRoomRepository>(rig.Rooms);
        services.AddSingleton<ICourseRepository>(rig.Courses);
        services.AddSingleton<IInstructorRepository>(rig.Instructors);
        services.AddSingleton<ISectionRepository>(rig.Sections);
        services.AddSingleton<IAppLogger>(new RecordingImportLogger());
        services.AddSingleton(FocusTestSupport.AllFocus());
        services.AddSingleton<IImportSectionsHost>(
            new FakeImportSectionsHost(new ImportTarget(rig.Fall.Id, SectionImportTestKit.TermName, AcademicYearName)));
        services.AddSingleton<IImportSectionsPlatform>(new FakeImportSectionsPlatform());
        services.AddTransient<ImportFileReader>();
        services.AddTransient<ImportSetupSnapshotBuilder>();
        services.AddTransient<SectionImportWriter>();
        services.AddTransient<SectionImportService>();
        services.AddTransient<ImportSectionsViewModel>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var vm = provider.GetRequiredService<ImportSectionsViewModel>();
        _vms.Add(vm);

        Assert.Equal(ImportStage.ReadyToChoose, vm.Stage);
        Assert.Equal("Sections will be added to Fall (2026-2027).", vm.TargetLine);
    }

    /// <summary>
    /// The real platform loads the template by an <c>avares://</c> address that Avalonia's asset loader can't
    /// resolve without a running app, so the tests read the same file through the test project's copy. This
    /// pins that the address names that file.
    /// </summary>
    [Fact]
    public void TheRealPlatform_LoadsTheTemplateByTheShippedFilesName()
    {
        var uri = AvaloniaImportSectionsPlatform.TemplateAssetUri;

        Assert.Equal("avares", uri.Scheme);
        Assert.Equal("termpoint", uri.Host);   // System.Uri lower-cases the host, as it does for every avares:// address in the app
        Assert.Equal(FakeImportSectionsPlatform.TemplateFileName, Path.GetFileName(uri.AbsolutePath));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "TestData", FakeImportSectionsPlatform.TemplateFileName)));
    }

    /// <summary>The real platform loads the instructions by the address of the shipped text file, whose copy the tests read.</summary>
    [Fact]
    public void TheRealPlatform_LoadsTheInstructionsByTheShippedFilesName()
    {
        var uri = AvaloniaImportSectionsPlatform.InstructionsAssetUri;

        Assert.Equal("avares", uri.Scheme);
        Assert.Equal("termpoint", uri.Host);
        Assert.Equal(FakeImportSectionsPlatform.InstructionsFileName, Path.GetFileName(uri.AbsolutePath));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "TestData", FakeImportSectionsPlatform.InstructionsFileName)));
    }
}
