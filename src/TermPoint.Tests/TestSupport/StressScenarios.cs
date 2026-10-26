using TermPoint.Services.SectionImport;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>What Check is expected to show for a scenario's file.</summary>
internal enum StressOutcome
{
    /// <summary>The Ready screen: no problems, at least one section to add.</summary>
    Ready,

    /// <summary>The "Every section in this file is already in ..." screen.</summary>
    NothingToAdd,

    /// <summary>The problems list (the file was read, but its rows have problems).</summary>
    Problems,

    /// <summary>One problem with the file as a whole (wrong shape, too large, unreadable).</summary>
    FileProblem,
}

/// <summary>
/// A scenario's file, and what Check should show for it, in words (for the hand-test checklist) and as data (for
/// the test that proves the checklist right).
/// </summary>
/// <param name="Bytes">The file.</param>
/// <param name="Extension">The file's extension with its dot.</param>
/// <param name="Outcome">The expected screen.</param>
/// <param name="Expect">What the person should see, in plain words.</param>
/// <param name="NewSections">For Ready: how many sections will be added (null: not checked).</param>
/// <param name="SkippedSections">For Ready or Nothing to add: how many are already there (null: not checked).</param>
/// <param name="FileProblem">For a file problem: the exact message.</param>
/// <param name="ProblemCells">For Problems: the (row, column) of every problem, in any order (null: not checked).</param>
/// <param name="ProblemCount">For Problems: how many problems (null: not checked).</param>
/// <param name="NoNewRecords">For Ready: the plan must create no setup value, course or instructor.</param>
/// <param name="WarningKinds">For Ready: kinds of warning that must be present (see <see cref="ImportWarningKinds"/>).</param>
/// <param name="Hint">Extra words for the checklist (what to type in a detached panel for the flow steps, say), or null.</param>
internal sealed record StressBuilt(
    byte[] Bytes, string Extension, StressOutcome Outcome, string Expect,
    int? NewSections = null, int? SkippedSections = null, string? FileProblem = null,
    IReadOnlyList<(int? Row, string? Column)>? ProblemCells = null, int? ProblemCount = null,
    bool NoNewRecords = false, IReadOnlyList<string>? WarningKinds = null, string? Hint = null);

/// <summary>
/// One numbered scenario: a file Greg runs through the flyout by hand, aimed at a semester of the copy of the real
/// database, with the expected outcome. The same scenario is built and checked by an automated test, so the
/// checklist's promises are proven before they are made.
/// </summary>
/// <param name="Number">The number in the file name and the checklist.</param>
/// <param name="Name">The short name in the file name.</param>
/// <param name="Purpose">One line: what the scenario is for.</param>
/// <param name="TargetYear">The academic year of the semester to select before choosing the file.</param>
/// <param name="TargetTerm">The semester to select.</param>
/// <param name="Do">What to do with the file (the buttons to press), in plain words.</param>
/// <param name="ImportFirst">The number of a scenario whose file must be imported into the same semester first, or null.</param>
/// <param name="Build">Builds the file and its expected outcome from the real-data copy.</param>
internal sealed record StressScenario(
    int Number, string Name, string Purpose, string TargetYear, string TargetTerm, string Do, int? ImportFirst,
    Func<StressContext, StressBuilt> Build)
{
    /// <summary>The file name without its extension (<c>01 Winter round trip</c>).</summary>
    public string FileBaseName => $"{Number:00} {Name}";

    /// <summary>The semester as the app names it (<c>Winter (2026-2027)</c>).</summary>
    public string Semester => $"{TargetTerm} ({TargetYear})";
}

/// <summary>
/// The catalog of stress scenarios: every file of the hand-test kit, with its target semester, what to do with it
/// and what to expect. Fresh scenarios (limits, broken files, text, encodings, shapes) use the shared generators
/// in <see cref="StressFiles"/>; real-data scenarios are built from the copy of the real database through
/// <see cref="StressContext"/>. The expectations are written next to the builders so that they cannot drift apart.
/// </summary>
internal static class StressScenarios
{
    /// <summary>The empty semesters of the target year that the first imports go into.</summary>
    private const string Winter = "Winter", EarlySummer = "Early Summer", Summer = "Summer", LateSummer = "Late Summer";

    /// <summary>Check, then Cancel: the step to use when nothing should be added.</summary>
    private const string CheckOnly = "Choose file, look at the result, then press Cancel (or Choose file again for the next step).";

    /// <summary>Every scenario, in checklist order.</summary>
    public static IReadOnlyList<StressScenario> All { get; } = new List<StressScenario>
    {
        new(0, "Base for Excel round trips", "A small valid file to re-save from Excel in other formats (section 3 of the checklist).",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, Base),
        new(1, "Winter round trip", $"The real sections of Fall ({StressContext.SourceYear}), exported and imported into an empty semester: nothing new should be created.",
            StressContext.TargetYear, Winter, "Choose file, check the result, press Import.", null, WinterRoundTrip),
        new(2, "Winter again", "The same file again, after file 01 was imported: nothing to add.",
            StressContext.TargetYear, Winter, "Choose file (it is the same file as 01).", 1, WinterAgain),
        new(3, "Fall re-import", $"The sections of Fall ({StressContext.TargetYear}) imported into that same semester: nothing to add, nothing changes.",
            StressContext.TargetYear, StressContext.Fall, "Choose file.", null, FallOntoItself),
        new(4, "Winter corrected", "File 01 plus two new sections, after 01 was imported: only the two new ones are added.",
            StressContext.TargetYear, Winter, "Choose file, press Import.", 1, WinterCorrected),
        new(5, "Matching names", "Names that differ from existing ones only by spacing or case should match; typos should be new, with warnings.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, MatchingNames),
        new(6, "Start times by length", "Legal start times by meeting length: the 90- and 180-minute lists of the year, and an 80-minute meeting, which has no list of its own.",
            StressContext.TargetYear, Summer, CheckOnly, null, StartTimesByLength),
        new(7, "Problems of many kinds", "One of each kind of problem, to read the messages and use Copy list.",
            StressContext.TargetYear, EarlySummer, "Choose file, read the list, press Copy list, paste into Excel (step 7 below), then Cancel.", null, ProblemsOfManyKinds),
        new(8, "Wrong semester", "The whole file is for another semester: one problem.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, WholeFileWrongSemester),
        new(9, "Some rows wrong semester", "Two rows name another semester: a problem on those rows only.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, SomeRowsWrongSemester),
        new(10, "Missing Tags column", "A template column is missing: one problem.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, MissingTagsColumn),
        new(11, "Two title rows", "Two rows above the header: the header must be the first row.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, TwoTitleRows),
        new(12, "Duplicate header", "A column name that appears twice.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, DuplicateHeader),
        new(13, "Sections sheet on second tab", "The Sections sheet is not the first sheet, and is named in lower case.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, SheetNotFirst),
        new(14, "Messy headers and extra columns", "Headers with odd spacing and capitals, columns in reverse order, and columns of your own.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, MessyAndReordered),
        new(15, "Blank and space rows", "Blank rows between the data, rows of only spaces, and a thousand empty rows at the bottom.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, BlankRows),
        new(16, "Hidden rows and filter", "Two rows are hidden and the sheet has a filter: are the hidden rows imported?",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, HiddenRows),
        new(17, "Excel cell types", "Cells that Excel stores as numbers, dates, times, formulas and errors: dates and errors must be reported, the rest read as they look.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, ExcelCellTypes),
        new(20, "CSV UTF-8 with BOM", "A CSV saved as 'CSV UTF-8' (Excel's name for it).",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, ctx => Csv(StressCsvEncoding.Utf8WithBom)),
        new(21, "CSV UTF-8 no BOM", "A CSV in UTF-8 without a byte-order mark.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, ctx => Csv(StressCsvEncoding.Utf8)),
        new(22, "CSV UTF-16", "A comma-separated CSV in UTF-16 with a byte-order mark (the encoding of Excel's 'Unicode Text').",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, ctx => Csv(StressCsvEncoding.Utf16LeWithBom)),
        new(23, "CSV Windows-1252", "A CSV in the plain Windows 'CSV' encoding, with accented names, a typographic apostrophe and an en dash: all must read exactly as typed.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, CsvWindows1252),
        new(24, "CSV semicolons", "A CSV separated by semicolons (what Excel writes in some locales).",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, CsvSemicolons),
        new(25, "CSV quoted fields", "Quoted fields holding commas, quotation marks and a line break.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, CsvQuoted),
        new(30, "Adversarial text", "Accented, Chinese and Hebrew names, an emoji, a 4,000-character title, odd spaces, HTML and formula-looking text.",
            StressContext.TargetYear, LateSummer, "Choose file, check, press Import, then look at the new sections, courses and instructors.", null, AdversarialText),
        new(31, "Adversarial problems", "Problems whose messages begin with =, +, - or @: paste Copy list into Excel and check they paste as text.",
            StressContext.TargetYear, EarlySummer, "Choose file, press Copy list, paste into Excel (step 31 below), then Cancel.", null, AdversarialProblems),
        new(40, "Largest 5000 rows", "The largest file TermPoint accepts: time Check and Import, and watch whether the window stays responsive.",
            StressContext.TargetYear, LateSummer, "Choose file (time it), press Import (time it).", null, Largest),
        new(41, "Over the limit 5001 rows", "One row more than the limit.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, OverRowLimit),
        new(42, "Over 10 MB", "A file over 10 MB: it should be refused at once.",
            StressContext.TargetYear, EarlySummer, "Choose file (time how long the refusal takes).", null, OverSizeLimit),
        new(43, "Every row broken 5000", "5,000 rows that each have two or three problems: does the problem list scroll, does the window freeze, does Copy list work?",
            StressContext.TargetYear, EarlySummer, "Choose file (time it), scroll the list, press Copy list, paste into Excel.", null, EveryRowBroken),
        new(50, "Rows far apart", "One section whose two rows are row 2 and row 4,000.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, RowsFarApart),
        new(51, "Rows disagree", "Two rows of one section that disagree about the section type and the capacity.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, RowsDisagree),
        new(52, "Duplicate meeting row", "The same meeting written twice.",
            StressContext.TargetYear, EarlySummer, CheckOnly, null, DuplicateMeetingRow),
    };

    // ── Small helpers ─────────────────────────────────────────────────────────

    /// <summary>A built scenario whose file is a <see cref="StressFile"/>.</summary>
    private static StressBuilt Built(StressFile file, StressOutcome outcome, string expect,
        int? newSections = null, int? skipped = null, string? fileProblem = null,
        IReadOnlyList<(int? Row, string? Column)>? problemCells = null, int? problemCount = null,
        bool noNewRecords = false, IReadOnlyList<string>? warningKinds = null, string? hint = null) =>
        new(file.ToBytes(), file.Extension, outcome, expect, newSections, skipped, fileProblem, problemCells, problemCount,
            noNewRecords, warningKinds, hint);

    /// <summary>The small valid file, aimed at the target year's Early Summer.</summary>
    private static StressSet SmallEarlySummer() => StressFiles.Small(6, StressContext.TargetYear, EarlySummer);

    /// <summary>The expected text of the Ready screen's summary for sections that create nothing else.</summary>
    private static string Summary(int sections, int courses = 0, int instructors = 0) =>
        ImportMessages.Summary(sections, courses, instructors);

    /// <summary>The expectation shared by the six-section small-file shapes: a Ready screen that adds six sections.</summary>
    private static StressBuilt SixSections(StressFile file, string extra) =>
        Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(6, 6, 4)}\" {extra}", newSections: 6);

    // ── 00-09: real data ──────────────────────────────────────────────────────

    /// <summary>00: the small file plus six real sections, to re-save from Excel.</summary>
    private static StressBuilt Base(StressContext ctx)
    {
        var small = SmallEarlySummer();
        var real = ctx.Export(StressContext.SourceYear, StressContext.Fall, StressContext.TargetYear, EarlySummer, 6, out var exported);
        foreach (var row in small.File.Rows)
            real.Rows.Add(row);
        int written = exported.Count(e => e.Spec is not null);
        int sections = written + small.Sections.Count;
        return Built(real, StressOutcome.Ready,
            $"Ready: \"{Summary(sections, 6, 4)}\" The four new instructors are the ones with accents and an apostrophe in their names " +
            "(Gagné, Müller, O’Brien and Smith). The six courses SML100 to SML105 are new. This is the file you re-save from Excel.",
            newSections: sections);
    }

    /// <summary>01 and 02: the sections of Fall in the source year (the year before the target year) written as a file for Winter of the target year.</summary>
    private static StressBuilt WinterRoundTrip(StressContext ctx)
    {
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, StressContext.TargetYear, Winter, null, out var exported);
        int n = exported.Count(e => e.Spec is not null);
        return Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(n)}\" There are no new courses, no new instructors, and no \"New in TermPoint\" list: every name already exists. " +
            $"After Import the flyout says \"{ImportMessages.Done(new ImportCounts(0, 0, 0, 0, 0, 0, 0, 0, n), Winter, StressContext.TargetYear)}\" and \"{ImportMessages.UndoHint}\" " +
            $"(Winter was empty). After Import and Save, Winter ({StressContext.TargetYear}) should show the same sections as Fall ({StressContext.SourceYear}) (their meetings, rooms, instructors and campuses; not their notes, flags or workloads).",
            newSections: n, noNewRecords: true);
    }

    /// <summary>02: file 01 again, after it was imported.</summary>
    private static StressBuilt WinterAgain(StressContext ctx)
    {
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, StressContext.TargetYear, Winter, null, out var exported);
        int n = exported.Count(e => e.Spec is not null);
        return Built(file, StressOutcome.NothingToAdd,
            $"\"{ImportMessages.NothingToAdd(Winter, StressContext.TargetYear)}\" The Import button does not appear and nothing changes ({n} sections are skipped).",
            newSections: 0, skipped: n);
    }

    /// <summary>03: the sections of Fall in the target year, written as a file for that same semester.</summary>
    private static StressBuilt FallOntoItself(StressContext ctx)
    {
        var file = ctx.Export(StressContext.TargetYear, StressContext.Fall, StressContext.TargetYear, StressContext.Fall, null, out var exported);
        int n = exported.Count(e => e.Spec is not null);
        return Built(file, StressOutcome.NothingToAdd,
            $"\"{ImportMessages.NothingToAdd(StressContext.Fall, StressContext.TargetYear)}\" The Import button does not appear. Nothing in the database changes ({n} sections are skipped).",
            newSections: 0, skipped: n);
    }

    /// <summary>04: file 01 plus two new sections.</summary>
    private static StressBuilt WinterCorrected(StressContext ctx)
    {
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, StressContext.TargetYear, Winter, null, out var exported);
        var specs = exported.Where(e => e.Spec is not null).Select(e => e.Spec!).ToList();
        int n = specs.Count;
        file.AddSection(specs[0].WithCode("NEW1"));
        file.AddSection(specs[1].WithCode("NEW2"));
        return Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(2)}\" and \"{ImportMessages.Skipped(Winter, n)}\" Press Import: the flyout says \"{ImportMessages.Done(new ImportCounts(0, 0, 0, 0, 0, 0, 0, 0, 2), Winter, StressContext.TargetYear)}\" " +
            "(no undo hint, because Winter was not empty), Winter gains two sections (codes NEW1 and NEW2) and nothing else changes.",
            newSections: 2, skipped: n, noNewRecords: true,
            hint: $"The file adds section NEW1 to course {specs[0].Subject}{specs[0].CourseNumber} and NEW2 to course {specs[1].Subject}{specs[1].CourseNumber}.");
    }

    /// <summary>05: names that match, and typos that do not.</summary>
    private static StressBuilt MatchingNames(StressContext ctx)
    {
        var match = StressRealCases.Matching(ctx, StressContext.TargetYear, EarlySummer);
        return Built(match.File, StressOutcome.Ready,
            $"Ready: \"{Summary(6, 0, 1)}\" Under New in TermPoint: Campuses \"{match.TypoCampus}\" and Section types \"{match.TypoSectionType}\" (nothing else: " +
            $"{match.Campus.Name} with a trailing space, and in lower case, matched the real campus; the room given by number alone matched; the course written in lower case and with a space in its number matched). " +
            $"New instructor: {match.InstructorLast}, {match.NewInstructorFirst}. Three warnings: the two typos look like near misses of existing values, and the new instructor shares a last name with an existing one.",
            newSections: 6, warningKinds: new[] { ImportWarningKinds.NearMiss, ImportWarningKinds.InstructorLastName });
    }

    /// <summary>06: legal and illegal start times for each block length the year lists, and for one it does not.</summary>
    private static StressBuilt StartTimesByLength(StressContext ctx)
    {
        var file = StressRealCases.StartTimes(ctx.Rig, StressContext.TargetYear, Summer, fractionalOnly: false, includeUnlistedLength: true, out var cases);
        var lines = cases.Select(c => $"{c.IllegalText} for the {c.Minutes}-minute meetings" + (c.HasOwnList ? string.Empty : " (this length has no list of its own, so every listed start time counts as legal)"));
        return Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(cases.Count * 2)}\" Exactly {cases.Count} start-time warnings, one per length, each saying that the time isn't a legal start time: {string.Join("; ", lines)}. " +
            "The sections that start at a legal time (codes beginning L) raise no warning. " +
            $"(In this database only the {StressContext.TargetYear} block lengths with their own lists are checked by length; the 80-, 110-, 170- and 230-minute lists in the database belong to an academic year that no longer exists, so they are not used.)",
            newSections: cases.Count * 2, warningKinds: new[] { ImportWarningKinds.LegalStartTime });
    }

    /// <summary>The four-digit text of a time in minutes.</summary>
    private static string Hhmm(int minutes) => $"{minutes / 60:00}{minutes % 60:00}";

    /// <summary>07: one of each kind of problem, against an existing course.</summary>
    private static StressBuilt ProblemsOfManyKinds(StressContext ctx)
    {
        var (_, subject, number) = StressRealCases.PlainCourse(ctx);
        var file = new StressFile(StressContext.TargetYear, EarlySummer);
        var cells = new List<(int? Row, string? Column)>();

        void Row(string? problemColumn, params (string Column, object? Value)[] values)
        {
            var all = new List<(string Column, object? Value)>
            {
                (C.Subject, subject.CalendarAbbreviation), (C.CourseNumber, number),
            };
            all.AddRange(values);
            file.AddRow(all.ToArray());
            if (problemColumn is not null)
                cells.Add((file.RowNumberOf(file.Rows.Count - 1), problemColumn));
        }

        Row(null, (C.SectionCode, "P0"));                                                                              // a good row
        Row(C.SectionCode);                                                                                            // no section code
        Row(C.MeetingDays, (C.SectionCode, "P2"), (C.MeetingDays, "Someday"), (C.Begin, "0900"), (C.End, "1000"));     // not a day
        Row(C.End, (C.SectionCode, "P3"), (C.MeetingDays, "M"), (C.Begin, "1000"), (C.End, "0900"));                   // End before Begin
        Row(C.Begin, (C.SectionCode, "P4"), (C.MeetingDays, "M"), (C.Begin, "0700"), (C.End, "0800"));                 // before 07:30
        Row(C.End, (C.SectionCode, "P5"), (C.MeetingDays, "M"), (C.Begin, "2100"), (C.End, "2230"));                   // after 22:00
        Row(C.Capacity, (C.SectionCode, "P6"), (C.Capacity, "lots"));                                                  // not a number
        Row(C.Building, (C.SectionCode, "P7"), (C.Room, "ZZ999"));                                                     // room not in TermPoint, no building
        Row(C.Room, (C.SectionCode, "P8"), (C.Building, "NOSUCH"));                                                    // building without a room
        Row(null, (C.SectionCode, "P9"), (C.SectionType, "Lecture"));                                                  // two rows that disagree...
        Row(C.SectionType, (C.SectionCode, "P9"), (C.SectionType, "Lab"));                                             // ...here
        file.AddRow((C.Subject, "NEWSUBJ"), (C.CourseNumber, "999"), (C.SectionCode, "A"));                            // new course, no title
        cells.Add((file.RowNumberOf(file.Rows.Count - 1), C.CourseTitle));
        Row(C.Instructors, (C.SectionCode, "P12"), (C.Instructors, ", John"));                                         // no last name
        Row(C.Frequency, (C.SectionCode, "P13"), (C.MeetingDays, "M"), (C.Begin, "0900"), (C.End, "1000"), (C.Frequency, "fortnightly"));
        Row(C.Frequency, (C.SectionCode, "P14"), (C.MeetingDays, "M"), (C.Begin, "0900"), (C.End, "1000"), (C.Frequency, "0,1"));
        Row(C.Begin, (C.SectionCode, "P15"), (C.MeetingDays, "M"));                                                    // days but no times
        Row(C.Tags, (C.SectionCode, "P16"), (C.Tags, "Tag; tag"));                                                     // a tag twice
        Row(C.Instructors, (C.SectionCode, "P17"), (C.Instructors, "Smith, Pat; smith, pat"));                         // a person twice

        return Built(file, StressOutcome.Problems,
            $"The problems list, {cells.Count} lines, one per row below (row number, then column): " +
            string.Join("; ", cells.Select(c => $"row {c.Row} {c.Column}")) +
            ". Each message names the row and column and says how to fix it. Nothing is imported.",
            problemCells: cells, problemCount: cells.Count);
    }

    /// <summary>08: the whole file names another semester.</summary>
    private static StressBuilt WholeFileWrongSemester(StressContext ctx)
    {
        var file = ctx.Export(StressContext.SourceYear, StressContext.Fall, StressContext.SourceYear, StressContext.Fall, 10, out _);
        var message = $"This file is for {StressContext.Fall} ({StressContext.SourceYear}), but {EarlySummer} ({StressContext.TargetYear}) is open. " +
                      $"Open {StressContext.Fall} to import it, or correct the {C.AcademicYear} and {C.Term} columns.";
        return Built(file, StressOutcome.Problems,
            $"One problem, with no row number: \"{message}\"",
            problemCells: new (int? Row, string? Column)[] { (null, null) }, problemCount: 1);
    }

    /// <summary>09: two rows name another semester.</summary>
    private static StressBuilt SomeRowsWrongSemester(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.Rows[1][C.Term] = StressContext.Fall;
        set.File.Rows[3][C.Term] = StressContext.Fall;
        var cells = new (int? Row, string? Column)[] { (set.File.RowNumberOf(1), null), (set.File.RowNumberOf(3), null) };
        return Built(set.File, StressOutcome.Problems,
            $"Two problems, rows {cells[0].Row} and {cells[1].Row}: \"This row is for {StressContext.Fall} ({StressContext.TargetYear}), but {EarlySummer} ({StressContext.TargetYear}) is open.\" " +
            "The other four rows raise nothing.",
            problemCells: cells, problemCount: 2);
    }

    // ── 10-17: the shape of the file ──────────────────────────────────────────

    /// <summary>10: no Tags column.</summary>
    private static StressBuilt MissingTagsColumn(StressContext ctx)
    {
        var set = SmallEarlySummer();
        StressFiles.DropColumn(set.File, C.Tags);
        return Built(set.File, StressOutcome.FileProblem, "One problem about the file: \"The Tags column is missing.\"", fileProblem: "The Tags column is missing.");
    }

    /// <summary>11: two title rows above the header.</summary>
    private static StressBuilt TwoTitleRows(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.TitleRows.Add(new object?[] { "Geology Department", "Fall schedule" });
        set.File.TitleRows.Add(new object?[] { "Prepared by the office" });
        return Built(set.File, StressOutcome.FileProblem,
            $"One problem about the file: \"{ImportFileReader.NoTemplateHeaderMessage}\"", fileProblem: ImportFileReader.NoTemplateHeaderMessage);
    }

    /// <summary>12: Term appears twice.</summary>
    private static StressBuilt DuplicateHeader(StressContext ctx)
    {
        var set = SmallEarlySummer();
        StressFiles.DuplicateColumn(set.File, C.Term);
        return Built(set.File, StressOutcome.FileProblem,
            "One problem about the file: \"The Term column appears more than once.\"", fileProblem: "The Term column appears more than once.");
    }

    /// <summary>13: the Sections sheet is second, in lower case.</summary>
    private static StressBuilt SheetNotFirst(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.SheetName = "sections";
        set.File.SheetsBefore.Add(("Instructions", new object?[][] { new object?[] { "Read me first" } }));
        set.File.SheetsBefore.Add(("Scratch", new object?[][] { new object?[] { "x", "y" } }));
        return SixSections(set.File, "The file has three tabs; the Sections one is last and is called \"sections\". It is found anyway.");
    }

    /// <summary>14: messy headers, reversed columns and extra columns.</summary>
    private static StressBuilt MessyAndReordered(StressContext ctx)
    {
        var set = SmallEarlySummer();
        StressFiles.MessyHeaders(set.File);
        StressFiles.ReorderWithExtraColumns(set.File);
        return SixSections(set.File, "The headers have odd spacing and capitals, the columns are in reverse order, and three columns of your own (My notes, Internal id, Reviewed?) are ignored.");
    }

    /// <summary>15: blank rows, space-only rows and a thousand empty rows at the bottom.</summary>
    private static StressBuilt BlankRows(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.Rows.Insert(2, new Dictionary<string, object?>());
        set.File.Rows.Insert(5, new Dictionary<string, object?> { [C.Subject] = "   ", [C.Instructors] = "  " });
        set.File.TrailingBlankRows = 1000;
        set.File.TrailingBlankCell = "   ";
        return SixSections(set.File, "Blank rows, rows holding only spaces and a thousand empty rows at the bottom are ignored, so there are still six sections.");
    }

    /// <summary>16: hidden rows and an auto-filter.</summary>
    private static StressBuilt HiddenRows(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.HiddenDataRows.Add(1);
        set.File.HiddenDataRows.Add(4);
        set.File.AutoFilter = true;
        return SixSections(set.File,
            "Two of the six rows are hidden and the sheet has a filter, but TermPoint still reads the hidden rows, so it is still six sections. " +
            "(Hidden or filtered-out rows are not skipped. Note this if you ever filter a sheet before importing it.)");
    }

    /// <summary>17: numbers, dates, times, formulas and errors.</summary>
    private static StressBuilt ExcelCellTypes(StressContext ctx)
    {
        var (file, rowOf) = StressFiles.CellTypes(withFractionalCapacity: true);
        file.Retarget(StressContext.TargetYear, EarlySummer);
        var dateRow = file.RowNumberOf(rowOf["date-code"]);
        var errorRow = file.RowNumberOf(rowOf["error"]);
        var optionalRow = file.RowNumberOf(rowOf["error-optional"]);
        var capacityRow = file.RowNumberOf(rowOf["capacity-60.5"]);
        var cells = new (int? Row, string? Column)[]
        {
            (dateRow, C.SectionCode), (errorRow, C.SectionCode),
            (optionalRow, C.SectionType), (optionalRow, C.Instructors), (optionalRow, C.Capacity),
            (capacityRow, C.Capacity),
        };

        string Error(string shown) => ImportMessages.CellIssueProblem(new CellIssue(CellIssueKind.ExcelError, shown));
        var date = ImportMessages.CellIssueProblem(new CellIssue(CellIssueKind.Date, "2 Jan 2026"));
        return Built(file, StressOutcome.Problems,
            $"{cells.Length} problems, one for each bad cell. Row {dateRow}, Section Code: \"{date}\" (Excel turned 1-2 into 2 January). " +
            $"Row {errorRow}, Section Code: \"{Error("#N/A")}\" (the cell holds an Excel error: no extra \"Section Code is empty\"). " +
            $"Row {optionalRow}: the same kind of message for Section Type (#N/A), Instructor(s) (#REF!) and Capacity (#DIV/0!), each naming its own error, three problems in all. " +
            $"Row {capacityRow}, Capacity: \"Capacity must be a whole number, like 30.\" (the cell holds 60.5). " +
            "Nothing else is reported: the numbers, the times and the formula cell are read as they look (see the Ready screen after deleting the four rows above in Excel and choosing the file again: " +
            "the boolean cell creates a section type called TRUE).",
            problemCells: cells, problemCount: cells.Length);
    }

    // ── 20-25: CSV ────────────────────────────────────────────────────────────

    /// <summary>A CSV of the small file in an encoding, expected to import six sections.</summary>
    private static StressBuilt Csv(StressCsvEncoding encoding)
    {
        var set = SmallEarlySummer();
        set.File.Format = StressFormat.Csv;
        set.File.CsvEncoding = encoding;
        return SixSections(set.File, "The new instructors read Gagné, Éloïse; Müller, Jürgen; O’Brien, Pat; Smith, John, exactly as typed (check the accents and the apostrophe).");
    }

    // Scenario 22 (UTF-16) is the plain Csv(StressCsvEncoding.Utf16LeWithBom) shape: UTF-16 is read like UTF-8.

    /// <summary>23: Windows-1252.</summary>
    private static StressBuilt CsvWindows1252(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.Format = StressFormat.Csv;
        set.File.CsvEncoding = StressCsvEncoding.Windows1252;
        set.File.AddRow((C.Subject, "SML"), (C.CourseNumber, "150"), (C.CourseTitle, "Intro – Part 1"), (C.SectionCode, "A"),
            (C.Instructors, "Dvořák, Antonín"));
        return Built(set.File, StressOutcome.Ready,
            $"Ready: \"{Summary(7, 7, 5)}\" Look at the new instructors: Gagné, Müller and O’Brien read exactly as typed, with the curly apostrophe in O’Brien, and no stray characters or boxes. " +
            "Dvořák reads Dvorák: the \"ř\" is not in this encoding, so it was already replaced by an r when the file was saved, which is not TermPoint's doing. " +
            "The new course SML150 reads \"Intro – Part 1\" with its en dash.",
            newSections: 7);
    }

    /// <summary>24: semicolon-separated.</summary>
    private static StressBuilt CsvSemicolons(StressContext ctx)
    {
        var set = SmallEarlySummer();
        set.File.Format = StressFormat.Csv;
        set.File.Delimiter = ';';
        return Built(set.File, StressOutcome.FileProblem,
            $"One problem about the file: \"{ImportFileReader.NoTemplateHeaderMessage}\" (the separator is the real problem and the message does not say so). Nothing is imported.",
            fileProblem: ImportFileReader.NoTemplateHeaderMessage);
    }

    /// <summary>25: quoted fields.</summary>
    private static StressBuilt CsvQuoted(StressContext ctx)
    {
        var set = StressFiles.SmallWithLineBreakTitle();
        set.File.Format = StressFormat.Csv;
        set.File.Retarget(StressContext.TargetYear, EarlySummer);
        return Built(set.File, StressOutcome.Ready,
            $"Ready: \"{Summary(7, 7, 4)}\" The new courses include SML100 — Intro to \"Geology\", Part 1 (the commas and quotation marks are kept) and SML199, whose title was typed on two lines.",
            newSections: 7);
    }

    // ── 30-31: hostile text ───────────────────────────────────────────────────

    /// <summary>30: awkward text on thirteen sections.</summary>
    private static StressBuilt AdversarialText(StressContext ctx)
    {
        var adv = StressFiles.Adversarial();
        adv.File.Retarget(StressContext.TargetYear, LateSummer);
        int n = adv.File.Rows.Count;
        return Built(adv.File, StressOutcome.Ready,
            $"Ready: {n} new sections (subject ADV). After Import, check in the lists: the instructors with accents, the Chinese and Hebrew names, the emoji in a course title, " +
            "the 4,000-character title (course ADV106), the HTML shown as plain text, the values that start with = + - @ shown as typed, and that the two \"Van Dyk\" sections share one instructor " +
            "and the two sections with section type \"Hy brid\" (deliberately spelled with a space: once a non-breaking space, once an ordinary one) share one section type. " +
            "Course ADV301 appears twice (one has an invisible character in its code).",
            newSections: n);
    }

    /// <summary>31: problems whose messages start with formula characters.</summary>
    private static StressBuilt AdversarialProblems(StressContext ctx)
    {
        var file = StressFiles.AdversarialProblems();
        file.Retarget(StressContext.TargetYear, EarlySummer);
        var cells = new (int? Row, string? Column)[]
        {
            (file.RowNumberOf(0), C.CourseTitle), (file.RowNumberOf(2), C.SectionType), (file.RowNumberOf(3), C.Instructors),
            (file.RowNumberOf(3), C.Tags), (file.RowNumberOf(4), C.Capacity), (file.RowNumberOf(4), C.MeetingDays), (file.RowNumberOf(4), C.Frequency),
        };
        return Built(file, StressOutcome.Problems,
            $"Seven problems. Press Copy list and paste into Excel: four of the Problem cells start with = + - or @ (rows {file.RowNumberOf(0)}, {file.RowNumberOf(2)} and {file.RowNumberOf(3)} twice). " +
            "Excel would run such a cell as a formula, so TermPoint puts an apostrophe in front of each one when it copies: they paste as the plain text shown in the flyout " +
            "(Excel hides the apostrophe; the formula bar may show it). There is no #NAME?, no number and no warning in any cell.",
            problemCells: cells, problemCount: 7);
    }

    // ── 40-43: size ───────────────────────────────────────────────────────────

    /// <summary>40: the largest file.</summary>
    private static StressBuilt Largest(StressContext ctx)
    {
        var set = StressFiles.Large(ImportFileReader.MaxDataRows, StressContext.TargetYear, LateSummer);
        var sections = set.Sections;
        int courses = sections.Select(s => s.Subject + s.CourseNumber).Distinct().Count();
        int instructors = sections.SelectMany(s => s.Instructors).Distinct().Count();
        int subjects = sections.Select(s => s.Subject).Distinct().Count();
        int campuses = sections.Select(s => s.Campus).OfType<string>().Distinct().Count();
        int sectionTypes = sections.Select(s => s.SectionType).OfType<string>().Distinct().Count();
        int meetingTypes = sections.SelectMany(s => s.Meetings).Select(m => m.MeetingType).OfType<string>().Distinct().Count();
        int tags = sections.SelectMany(s => s.Tags).Distinct().Count();
        int rooms = sections.SelectMany(s => s.Meetings).Where(m => m.Room is not null).Select(m => (m.Building, m.Room)).Distinct().Count();
        return Built(set.File, StressOutcome.Ready,
            $"Ready: \"{Summary(sections.Count, courses, instructors)}\" Under New in TermPoint: {subjects} subjects, {sectionTypes} section types, {meetingTypes} meeting types, " +
            $"{tags} tags, {campuses} campuses and {rooms} rooms. Write down how long Check and Import took and whether the window kept responding " +
            "(the importer itself takes about a second or two for this file in the automated tests).",
            newSections: sections.Count);
    }

    /// <summary>41: one row over.</summary>
    private static StressBuilt OverRowLimit(StressContext ctx)
    {
        var set = StressFiles.Large(ImportFileReader.MaxDataRows + 1, StressContext.TargetYear, EarlySummer);
        return Built(set.File, StressOutcome.FileProblem,
            $"One problem about the file: \"{ImportFileReader.TooLargeMessage}\" Nothing is imported.", fileProblem: ImportFileReader.TooLargeMessage);
    }

    /// <summary>42: over 10 MB.</summary>
    private static StressBuilt OverSizeLimit(StressContext ctx) =>
        new(StressFiles.Oversize(StressFormat.Xlsx), ".xlsx", StressOutcome.FileProblem,
            $"One problem about the file: \"{ImportFileReader.TooLargeMessage}\" It should appear at once (well under a second). The file is about 12 MB.",
            FileProblem: ImportFileReader.TooLargeMessage);

    /// <summary>43: every row broken.</summary>
    private static StressBuilt EveryRowBroken(StressContext ctx)
    {
        var file = StressFiles.EveryRowBroken(ImportFileReader.MaxDataRows, out int expected);
        file.Retarget(StressContext.TargetYear, EarlySummer);
        return Built(file, StressOutcome.Problems,
            $"The problems list with {expected:N0} lines (two or three for each of the 5,000 rows). It is the stress test of the list itself: how long does it take to appear, " +
            "does the window freeze, does it scroll, and does Copy list paste into Excel as three columns? Nothing is imported.",
            problemCount: expected);
    }

    // ── 50-52: semantics ──────────────────────────────────────────────────────

    /// <summary>50: one section whose rows are far apart.</summary>
    private static StressBuilt RowsFarApart(StressContext ctx)
    {
        var file = StressFiles.FarApart(StressContext.TargetYear, EarlySummer, out _);
        return Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(3998, 3998, 1)}\" (section FAR101 A is one section with four meetings, although its rows are 2 and 4,000).",
            newSections: 3998);
    }

    /// <summary>51: two rows that disagree.</summary>
    private static StressBuilt RowsDisagree(StressContext ctx)
    {
        var file = StressFiles.RowsDisagree(StressContext.TargetYear, EarlySummer);
        var cells = new (int? Row, string? Column)[] { (3, C.SectionType), (3, C.Capacity) };
        return Built(file, StressOutcome.Problems,
            "Two problems on row 3: DIS101 A has a different Section Type than row 2, and a different Capacity than row 2.",
            problemCells: cells, problemCount: 2);
    }

    /// <summary>52: a meeting written twice.</summary>
    private static StressBuilt DuplicateMeetingRow(StressContext ctx)
    {
        var file = StressFiles.DuplicateMeeting(StressContext.TargetYear, EarlySummer);
        return Built(file, StressOutcome.Ready,
            $"Ready: \"{Summary(1, 1, 0)}\" with a warning that DUP101 A has meetings at overlapping times on Monday and Wednesday (rows 2 and 3). " +
            "The duplicated meeting is not an error: after Import the section would show its Monday and Wednesday meetings twice.",
            newSections: 1, warningKinds: new[] { ImportWarningKinds.OverlappingMeetings });
    }
}
