using System.Diagnostics;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using TermPoint.ViewModels.Management;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Stress tests of the section import against a throwaway copy of the private real-data database
/// (<c>BIOL-TT.db</c>), which is only ever copied, never opened. Every test is <c>[FactRequiresLocalDb]</c> and
/// skips itself on a machine without that file (CI included). The tests read the data at run time and derive
/// every expectation from it: no name, title, id or count from the data appears in this file.
///
/// <para>The big one is the round trip: a real semester is written out as a template file, aimed at an empty
/// semester, and imported, and each imported section must equal its source. See <see cref="StressExport"/>.</para>
/// </summary>
public sealed class SectionImportStressRealDataTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _files;

    /// <summary>Creates a temporary folder for the generated files.</summary>
    /// <param name="output">The test output the timings and differences are written to.</param>
    public SectionImportStressRealDataTests(ITestOutputHelper output)
    {
        _output = output;
        _files = Path.Combine(Path.GetTempPath(), $"import_stress_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_files);
    }

    /// <summary>Deletes the temporary folder.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_files, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The first few problems of a check as text, for assertion messages.</summary>
    private static string Describe(ImportCheck check, int max = 10)
    {
        if (check.FileProblem is not null)
            return "file problem: " + check.FileProblem;
        return string.Join(" | ", check.Report!.Problems.Take(max).Select(ImportProblemText.Line)) +
               (check.Report.Problems.Count > max ? $" ... ({check.Report.Problems.Count} problems)" : "");
    }

    /// <summary>The names of what a plan would create, by kind, for the kinds that are not empty (used to list anything a round trip creates).</summary>
    private static List<string> NewRecordLines(ImportPlan plan)
    {
        var lines = new List<string>();
        lines.AddRange(plan.NewSubjects.Select(s => $"subject {s.Record.CalendarAbbreviation}"));
        lines.AddRange(plan.NewCampuses.Select(c => $"campus {c.Record.Name}"));
        lines.AddRange(plan.NewSectionTypes.Select(t => $"section type {t.Record.Name}"));
        lines.AddRange(plan.NewMeetingTypes.Select(t => $"meeting type {t.Record.Name}"));
        lines.AddRange(plan.NewTags.Select(t => $"tag {t.Record.Name}"));
        lines.AddRange(plan.NewRooms.Select(r => $"room {r.Record.Building} {r.Record.RoomNumber}"));
        lines.AddRange(plan.NewCourses.Select(c => $"course {c.Record.CalendarCode}"));
        lines.AddRange(plan.NewInstructors.Select(i => $"instructor {i.Record.LastName}, {i.Record.FirstName}"));
        return lines;
    }

    /// <summary>Builds the flyout's view model over a rig, with fakes for everything else.</summary>
    private static ImportSectionsViewModel NewVm(ImportRig rig) =>
        new(rig.Service, new FakeImportSectionsHost(new ImportTarget(rig.Fall.Id, rig.Fall.Name, rig.Year.Name)),
            new FakeImportSectionsPlatform(), FocusTestSupport.AllFocus(), rig.Courses, new RecordingImportLogger());

    /// <summary>Prints how many notes of each category the export made, and how many sections they touch.</summary>
    private void PrintExportNotes(List<ExportedSection> exported)
    {
        _output.WriteLine($"[export] {exported.Count} real sections; {exported.Count(e => e.Spec is not null)} written to the file; " +
                          $"{exported.Count(e => e.Spec is null)} could not be written at all");
        foreach (var group in exported.SelectMany(e => e.Notes.Select(n => (e, n))).GroupBy(x => x.n.Category).OrderBy(g => g.Key))
            _output.WriteLine($"[export] left out ({group.Key}): {group.Count()} time(s) in {group.Select(x => x.e).Distinct().Count()} section(s): {group.First().n.Detail}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B1: the round trip
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports every section of Fall 2025-2026 as template rows aimed at the empty Winter 2026-2027, writes an
    /// <c>.xlsx</c>, checks and imports it. As many sections are added as were written; nothing new is created (no
    /// course, instructor, room, campus, type or tag: every name must match an existing record); only rows were
    /// inserted (no existing row's data changed); and each imported section equals its source in course, code,
    /// instructors, meetings (day, start, length, frequency, meeting type, room), campus, section type, tags and capacity.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task RoundTrip_FallOfLastYear_IntoEmptyWinter_CreatesNothingNewAndMatchesTheSource()
    {
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Winter");
        var (_, sourceSemester) = RealImportDb.FindSemester(rig, StressContext.SourceYear, StressContext.Fall);
        var source = rig.Sections.GetAll(sourceSemester.Id);
        Assert.NotEmpty(source);
        Assert.Empty(rig.Sections.GetAll(rig.Fall.Id));

        var lookups = ExportLookups.Build(rig);
        var file = StressExport.ExportAll(source, lookups, rig.Year.Name, rig.Fall.Name, out var exported);
        PrintExportNotes(exported);
        var written = exported.Where(e => e.Spec is not null).ToList();
        var path = file.WriteTo(_files, "round-trip");
        _output.WriteLine($"[size] {file.Rows.Count} rows for {written.Count} sections, {new FileInfo(path).Length / 1024.0:F0} KB");

        var idToData = rig.SnapshotIdToData();
        var sw = Stopwatch.StartNew();
        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
        _output.WriteLine($"[time] Check: {sw.ElapsedMilliseconds} ms");
        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
        foreach (var warning in check.Report.Warnings)
            _output.WriteLine($"[warning] {warning.Kind}: {warning.Message}");

        Assert.Equal(written.Count, check.Report.Plan.NewSections.Count);
        var created = NewRecordLines(check.Report.Plan);
        foreach (var line in created)
            _output.WriteLine($"[created] {line}");
        Assert.True(created.Count == 0, $"the import would create {created.Count} record(s) that already exist under a name that did not match: {string.Join("; ", created)}");

        sw.Restart();
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);
        _output.WriteLine($"[time] Import: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(written.Count, outcome.Counts.Sections);
        Assert.Equal(written.Count, outcome.Counts.Total);   // sections only: nothing else was inserted

        // Only inserts: no existing row changed, and the only rows added are the sections.
        var violations = ImportRig.FindNonInserts(idToData, rig.SnapshotIdToData(), out var inserted);
        Assert.Empty(violations);
        _output.WriteLine($"[inserted] {string.Join(", ", inserted.Select(kv => $"{kv.Key}: {kv.Value}"))}");
        Assert.Equal(new[] { "Sections" }, inserted.Keys.ToArray());

        // Each imported section equals its source.
        var imported = rig.Sections.GetAll(rig.Fall.Id)
            .GroupBy(s => $"{s.CourseId}|{s.SectionCode.ToLowerInvariant()}").ToDictionary(g => g.Key, g => g.ToList());
        var differences = new List<string>();
        var designed = new List<string>();
        int levelDiffers = 0;
        foreach (var e in written)
        {
            if (!imported.TryGetValue($"{e.Source.CourseId}|{e.Source.SectionCode.ToLowerInvariant()}", out var found) || found.Count != 1)
            {
                differences.Add($"{lookups.Courses[e.Source.CourseId!].CalendarCode} {e.Source.SectionCode}: not found exactly once after the import");
                continue;
            }
            var course = lookups.Courses[e.Source.CourseId!];
            differences.AddRange(StressExport.Compare(e, found[0], course, AppSettings.Current.DefaultSectionCapacity, designed));
            if (found[0].Level != e.Source.Level)
                levelDiffers++;
        }
        _output.WriteLine($"[round trip] {written.Count} sections compared; {differences.Count} difference(s)");
        foreach (var d in differences)
            _output.WriteLine($"[difference] {d}");
        _output.WriteLine($"[designed] {designed.Count} section(s) differ from the source only as the design says (D1 blank capacity, D2 course tags)");
        _output.WriteLine($"[observed] {levelDiffers} section(s) have a different Level from their source (the import copies the course's level, as the editor does)");
        Assert.True(differences.Count == 0, $"{differences.Count} difference(s): {string.Join(" | ", differences.Take(10))}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B2: re-import onto itself
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports Fall 2026-2027 and imports the file into Fall 2026-2027: every section is already there, so the check
    /// says nothing to add, and the database is identical before and after (a snapshot of every table's rows).
    /// </summary>
    [FactRequiresLocalDb]
    public async Task ReimportOntoItself_Fall_IsNothingToAddAndChangesNothing()
    {
        using var rig = RealImportDb.Open(StressContext.TargetYear, StressContext.Fall);
        var source = rig.Sections.GetAll(rig.Fall.Id);
        Assert.NotEmpty(source);
        var file = StressExport.ExportAll(source, ExportLookups.Build(rig), rig.Year.Name, rig.Fall.Name, out var exported);
        PrintExportNotes(exported);
        var written = exported.Count(e => e.Spec is not null);
        var path = file.WriteTo(_files, "reimport");
        var before = rig.DumpAllTables();

        var check = await rig.Service.CheckAsync(path, rig.Fall.Id);

        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
        Assert.Empty(check.Report.Plan.NewSections);
        Assert.Equal(written, check.Report.SkippedSections.Count);
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);
        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(ImportCounts.None, outcome.Counts);
        Assert.Equal(before, rig.DumpAllTables());

        var vm = NewVm(rig);
        await vm.CheckFileAsync(path);
        Assert.Equal(ImportStage.NothingToAdd, vm.Stage);
        Assert.Equal(ImportMessages.NothingToAdd(rig.Fall.Name, rig.Year.Name), vm.NothingToAddText);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B3: matching on real names
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Names written the way people write them match the real records (a campus with a trailing space or in lower
    /// case, a room given by number alone with a blank Building, a course written in lower case or with a space in
    /// its number); a misspelt campus and a misspelt section type are planned as new, each with a near-miss warning;
    /// a new instructor who shares a last name with an existing one gets the "shares a last name" warning.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task Matching_RealNames_MatchOrWarnAsDesigned()
    {
        using var ctx = StressContext.Open();
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Early Summer");
        var match = StressRealCases.Matching(ctx, rig.Year.Name, rig.Fall.Name);
        _output.WriteLine($"[case] campus typo [{match.TypoCampus}], section type typo [{match.TypoSectionType}], instructor [{match.InstructorLast}, {match.NewInstructorFirst}], " +
                          $"room by number alone: {(match.Room is null ? "none available" : match.Room.RoomNumber)}");
        var check = await rig.Service.CheckAsync(match.File.WriteTo(_files, "matching"), rig.Fall.Id);

        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
        var plan = check.Report.Plan;
        Assert.Equal(6, plan.NewSections.Count);

        // The variants matched the real records: nothing else is created.
        Assert.Empty(plan.NewSubjects);
        Assert.Empty(plan.NewCourses);
        Assert.Empty(plan.NewRooms);
        Assert.Empty(plan.NewMeetingTypes);
        Assert.Empty(plan.NewTags);
        Assert.Equal(match.TypoCampus, Assert.Single(plan.NewCampuses).Record.Name);
        Assert.Equal(match.TypoSectionType, Assert.Single(plan.NewSectionTypes).Record.Name);
        var newInstructor = Assert.Single(plan.NewInstructors).Record;
        Assert.Equal(match.InstructorLast, newInstructor.LastName);
        Assert.Equal(match.NewInstructorFirst, newInstructor.FirstName);

        SectionDraft Draft(string code) => plan.NewSections.Single(s => s.Key.SectionCode == code);
        Assert.False(Draft("M1").Campus!.IsNew);                                    // trailing space
        Assert.Equal(match.Campus.Id, Draft("M1").Campus!.Record.Id);
        Assert.False(Draft("M2").Campus!.IsNew);                                    // lower case
        Assert.Equal(match.Campus.Id, Draft("M2").Campus!.Record.Id);
        Assert.True(Draft("M3").Campus!.IsNew);                                     // the typo
        foreach (var code in new[] { "M1", "M2", "M3", "M4", "M5", "M6" })
        {
            Assert.False(Draft(code).Course.IsNew);                                  // the real course written in three spellings all match it
            Assert.Equal(match.Course.Id, Draft(code).Course.Record.Id);
        }
        if (match.Room is not null)
        {
            var meeting = Assert.Single(Draft("M4").Meetings);
            Assert.Equal(match.Room.Id, meeting.Room!.Record.Id);                    // a blank Building matched by number alone
            Assert.False(meeting.Room.IsNew);
        }

        var nearMiss = check.Report.Warnings.Where(w => w.Kind == ImportWarningKinds.NearMiss).ToList();
        foreach (var warning in check.Report.Warnings)
            _output.WriteLine($"[warning] {warning.Kind}: {warning.Message}");
        Assert.Equal(2, nearMiss.Count);
        Assert.Contains(nearMiss, w => w.Message.Contains(match.TypoCampus));
        Assert.Contains(nearMiss, w => w.Message.Contains(match.TypoSectionType));
        var lastName = Assert.Single(check.Report.Warnings, w => w.Kind == ImportWarningKinds.InstructorLastName);
        Assert.Contains(match.NewInstructorFirst, lastName.Message);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B4: fractional legal start times
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fractional block lengths (80, 110, 170 and 230 minutes, stored as 1.333... hours and so on): a meeting that
    /// begins at a legal start time for its length raises no start-time warning, and one that begins at a time not in
    /// the list raises exactly one, naming the time and the length. In the real data these lengths are held under
    /// academic-year ids that no academic year has, so the test first attaches them to 2026-2027 in the temporary copy.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task FractionalLegalStartTimes_LegalGivesNoWarning_IllegalGivesOne()
    {
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Summer");
        var attached = StressRealCases.AttachOrphanBlockLengths(rig, rig.Year.Name);
        _output.WriteLine($"[arrange] {attached.Count} fractional block length(s) attached to {rig.Year.Name} in the copy " +
                          "(the real data holds them only under ids of academic years that no longer exist)");
        var file = StressRealCases.StartTimes(rig, rig.Year.Name, rig.Fall.Name, fractionalOnly: true, includeUnlistedLength: false, out var cases);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
            _output.WriteLine($"[case] {c.Minutes} minutes: legal start {c.LegalStart / 60:00}{c.LegalStart % 60:00}, illegal start {c.IllegalText}");

        var check = await rig.Service.CheckAsync(file.WriteTo(_files, "fractional"), rig.Fall.Id);

        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
        Assert.Equal(cases.Count * 2, check.Report.Plan.NewSections.Count);
        var warnings = check.Report.Warnings.Where(w => w.Kind == ImportWarningKinds.LegalStartTime).ToList();
        foreach (var warning in check.Report.Warnings)
            _output.WriteLine($"[warning] {warning.Kind}: {warning.Message}");
        Assert.Equal(cases.Count, warnings.Count);
        foreach (var c in cases)
        {
            var illegalRow = file.RowNumberOf(c.IllegalRowIndex);
            var legalRow = file.RowNumberOf(c.LegalRowIndex);
            var warning = Assert.Single(warnings, w => w.Rows.Contains(illegalRow));
            Assert.Equal(new[] { illegalRow }, warning.Rows.ToArray());
            Assert.Contains($"{c.IllegalText} isn't a legal start time{c.LengthPhrase}", warning.Message);
            Assert.DoesNotContain(warnings, w => w.Rows.Contains(legalRow));
        }
    }

    /// <summary>
    /// A block length the academic year has no list for (here 80 minutes, in the data as it really is) is checked
    /// against every start time the year has, as the meeting editor's Start list does: a start time in any list is
    /// legal, one in none gives a warning that does not name a length.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task StartTimes_ALengthWithNoListOfItsOwn_IsCheckedAgainstEveryListedTime()
    {
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Summer");
        var file = StressRealCases.StartTimes(rig, rig.Year.Name, rig.Fall.Name, fractionalOnly: false, includeUnlistedLength: true, out var cases);
        var unlisted = Assert.Single(cases, c => !c.HasOwnList);
        _output.WriteLine($"[case] {unlisted.Minutes} minutes has no list: legal {unlisted.LegalStart / 60:00}{unlisted.LegalStart % 60:00}, illegal {unlisted.IllegalText}");

        var check = await rig.Service.CheckAsync(file.WriteTo(_files, "unlisted"), rig.Fall.Id);

        Assert.False(check.Report!.HasProblems, Describe(check));
        var warnings = check.Report.Warnings.Where(w => w.Kind == ImportWarningKinds.LegalStartTime).ToList();
        Assert.Equal(cases.Count, warnings.Count);
        var warning = Assert.Single(warnings, w => w.Rows.Contains(file.RowNumberOf(unlisted.IllegalRowIndex)));
        Assert.Contains($"{unlisted.IllegalText} isn't a legal start time (1 meeting", warning.Message);
        Assert.DoesNotContain(warnings, w => w.Rows.Contains(file.RowNumberOf(unlisted.LegalRowIndex)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B5: change between Check and Import (D4)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks a valid file, then a section with the same course and code as one the file would add is inserted through
    /// the repository (as a detached Sections panel would): Import is refused and nothing is written, and the new
    /// report shows the section as already there.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task ChangeBetweenCheckAndImport_ASectionTheFileWouldAdd_IsRefused()
    {
        using var ctx = StressContext.Open();
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Winter");
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, rig.Year.Name, rig.Fall.Name, take: 20, out var exported);
        var check = await rig.Service.CheckAsync(file.WriteTo(_files, "d4"), rig.Fall.Id);
        Assert.Null(check.FileProblem);
        Assert.False(check.Report!.HasProblems, Describe(check));
        var planned = check.Report.Plan.NewSections[0];

        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = planned.Course.Record.Id, SectionCode = planned.Key.SectionCode });
        var before = rig.DumpAllTables();
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);

        Assert.Equal(ImportOutcomeKind.Changed, outcome.Kind);
        Assert.Equal(ImportCounts.None, outcome.Counts);
        Assert.Equal(before, rig.DumpAllTables());
        Assert.Contains(outcome.Report.SkippedSections, s => s.Key.Label == planned.Key.Label);
        Assert.DoesNotContain(outcome.Report.Plan.NewSections, s => s.Key.Label == planned.Key.Label);
    }

    /// <summary>
    /// OBSERVATION: a section added in the meantime that nothing in the file refers to is not a change (the report the
    /// user saw would read the same), so Import goes ahead and writes the file.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task Observation_ChangeBetweenCheckAndImport_AnUnrelatedSection_DoesNotStopTheImport()
    {
        using var ctx = StressContext.Open();
        using var rig = RealImportDb.Open(StressContext.TargetYear, "Winter");
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, rig.Year.Name, rig.Fall.Name, take: 20, out var exported);
        var check = await rig.Service.CheckAsync(file.WriteTo(_files, "d4-unrelated"), rig.Fall.Id);
        Assert.False(check.Report!.HasProblems, Describe(check));
        int planned = check.Report.Plan.NewSections.Count;

        var course = check.Report.Plan.NewSections[0].Course.Record;
        rig.Sections.Insert(new Section { SemesterId = rig.Fall.Id, CourseId = course.Id, SectionCode = "NOT-IN-THE-FILE" });
        var outcome = rig.Service.Import(check.Sheet!, check.Report, rig.Fall.Id);

        _output.WriteLine($"[observed] outcome {outcome.Kind}, {outcome.Counts.Sections} of {planned} sections written; semester was empty at the check but not at the import: undo hint {(outcome.Report.SemesterWasEmpty ? "shown" : "not shown")}");
        Assert.Equal(ImportOutcomeKind.Written, outcome.Kind);
        Assert.Equal(planned, outcome.Counts.Sections);
        Assert.Equal(planned + 1, rig.Sections.GetAll(rig.Fall.Id).Count);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The kit's scenarios
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Proves the hand-test kit right before it is handed over: every scenario of the catalog is built, aimed at its
    /// semester of a fresh copy of the database (after importing the scenario it depends on, where there is one) and
    /// checked, and the real result must match what the checklist promises: the screen, the counts, the problem rows
    /// and columns, the file problem text, the warnings and "nothing new created".
    /// </summary>
    [FactRequiresLocalDb]
    public async Task Catalog_EveryScenario_BehavesAsTheChecklistSays()
    {
        var built = new Dictionary<int, StressBuilt>();
        using (var ctx = StressContext.Open())
        {
            foreach (var scenario in StressScenarios.All)
            {
                var sw = Stopwatch.StartNew();
                built[scenario.Number] = scenario.Build(ctx);
                _output.WriteLine($"[build] {scenario.FileBaseName}: {built[scenario.Number].Bytes.Length / 1024.0:F0} KB in {sw.ElapsedMilliseconds} ms");
            }
        }

        var failures = new List<string>();
        foreach (var scenario in StressScenarios.All)
        {
            var b = built[scenario.Number];
            using var rig = RealImportDb.Open(scenario.TargetYear, scenario.TargetTerm);
            if (scenario.ImportFirst is int prerequisite)
            {
                var pre = built[prerequisite];
                var prePath = Path.Combine(_files, $"prerequisite-{prerequisite}{pre.Extension}");
                File.WriteAllBytes(prePath, pre.Bytes);
                var preCheck = await rig.Service.CheckAsync(prePath, rig.Fall.Id);
                Assert.False(preCheck.HasFileProblem || preCheck.Report!.HasProblems, $"{scenario.FileBaseName}: the file it depends on did not check clean: {Describe(preCheck)}");
                rig.Service.Import(preCheck.Sheet!, preCheck.Report!, rig.Fall.Id);
            }

            var path = Path.Combine(_files, scenario.FileBaseName + b.Extension);
            File.WriteAllBytes(path, b.Bytes);
            var sw = Stopwatch.StartNew();
            var check = await rig.Service.CheckAsync(path, rig.Fall.Id);
            var found = Judge(scenario, b, check);
            _output.WriteLine($"[check] {scenario.FileBaseName}: {sw.ElapsedMilliseconds} ms, expected {b.Outcome}: {(found.Count == 0 ? "as promised" : "MISMATCH")}");
            foreach (var line in found)
                failures.Add($"{scenario.FileBaseName}: {line}");
            File.Delete(path);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Compares a check with a scenario's promises and describes each way it differs.</summary>
    private static List<string> Judge(StressScenario scenario, StressBuilt b, ImportCheck check)
    {
        var found = new List<string>();
        if (b.Outcome == StressOutcome.FileProblem)
        {
            if (check.FileProblem != b.FileProblem)
                found.Add($"expected the file problem [{b.FileProblem}] but got [{check.FileProblem ?? "no file problem"}]");
            return found;
        }
        if (check.FileProblem is not null)
        {
            found.Add($"unexpected file problem [{check.FileProblem}]");
            return found;
        }

        var report = check.Report!;
        switch (b.Outcome)
        {
            case StressOutcome.Problems:
                if (!report.HasProblems)
                {
                    found.Add("expected problems but there were none");
                    break;
                }
                if (b.ProblemCount is int count && report.Problems.Count != count)
                    found.Add($"expected {count} problems but got {report.Problems.Count}");
                if (b.ProblemCells is { } cells)
                {
                    var expected = cells.Select(c => $"{c.Row}|{c.Column}").OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var actual = report.Problems.Select(p => $"{p.Row}|{p.Column}").OrderBy(x => x, StringComparer.Ordinal).ToList();
                    if (!expected.SequenceEqual(actual))
                        found.Add($"expected problems at [{string.Join(", ", expected)}] but got [{string.Join(", ", actual.Take(30))}]");
                }
                break;

            case StressOutcome.NothingToAdd:
                if (report.HasProblems) found.Add($"expected nothing to add but there were problems: {string.Join(" | ", report.Problems.Take(3).Select(ImportProblemText.Line))}");
                else if (report.Plan.NewSections.Count != 0) found.Add($"expected nothing to add but {report.Plan.NewSections.Count} sections would be added");
                if (b.SkippedSections is int skipped && report.SkippedSections.Count != skipped)
                    found.Add($"expected {skipped} sections already there but got {report.SkippedSections.Count}");
                break;

            case StressOutcome.Ready:
                if (report.HasProblems)
                {
                    found.Add($"expected Ready but there were {report.Problems.Count} problems: {string.Join(" | ", report.Problems.Take(3).Select(ImportProblemText.Line))}");
                    break;
                }
                if (b.NewSections is int sections && report.Plan.NewSections.Count != sections)
                    found.Add($"expected {sections} sections to add but got {report.Plan.NewSections.Count}");
                if (b.SkippedSections is int already && report.SkippedSections.Count != already)
                    found.Add($"expected {already} sections already there but got {report.SkippedSections.Count}");
                if (b.NoNewRecords)
                {
                    var created = NewRecordLines(report.Plan);
                    if (created.Count > 0)
                        found.Add($"expected nothing new to be created but got {created.Count}: {string.Join("; ", created.Take(5))}");
                }
                foreach (var kind in b.WarningKinds ?? Array.Empty<string>())
                {
                    if (!report.Warnings.Any(w => w.Kind == kind))
                        found.Add($"expected a {kind} warning but there was none (warnings: {string.Join(", ", report.Warnings.Select(w => w.Kind))})");
                }
                break;
        }
        return found;
    }
}
