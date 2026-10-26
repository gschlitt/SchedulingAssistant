using TermPoint.Services.SectionImport;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// A generated file together with the sections it describes, so a test can import it and then check that
/// every section arrived as described.
/// </summary>
/// <param name="File">The file.</param>
/// <param name="Sections">The sections the file describes, in file order.</param>
internal sealed record StressSet(StressFile File, IReadOnlyList<ImportSectionSpec> Sections);

/// <summary>
/// The adversarial-text file (every awkward kind of text on its own section) and the specs of the cases a
/// generic check can verify. Cases whose outcome is the point of the test (non-breaking spaces, the
/// zero-width space) are not specs: the test looks at what the importer did with them.
/// </summary>
/// <param name="File">The file.</param>
/// <param name="Verifiable">Cases whose stored values are the expected ones, by case name.</param>
internal sealed record AdversarialSet(StressFile File, IReadOnlyDictionary<string, ImportSectionSpec> Verifiable);

/// <summary>
/// The shared generators of stress files, used by the automated stress tests and by the hand-test kit writer so
/// that both exercise exactly the same files. Everything here is built from code (no fixtures), is
/// deterministic, and names only invented data (subjects, instructors and the like with digits in their names,
/// so that the importer's near-miss warnings stay quiet); data from a real database is handled elsewhere.
/// </summary>
internal static class StressFiles
{
    // ── Sizes of the large file ───────────────────────────────────────────────

    /// <summary>How many distinct new courses the large file creates (its sections are spread over them).</summary>
    private const int LargeCourses = 600;

    /// <summary>How many distinct new instructors the large file creates.</summary>
    private const int LargeInstructors = 400;

    /// <summary>The time the large file's first meetings begin (08:30) and end (09:50), and the second's (13:00, 14:20).</summary>
    private const int FirstBegin = 8 * 60 + 30, FirstEnd = 9 * 60 + 50, SecondBegin = 13 * 60, SecondEnd = 14 * 60 + 20;

    // ── The large file ────────────────────────────────────────────────────────

    /// <summary>
    /// A valid file of exactly <paramref name="dataRows"/> data rows that mixes one-row sections (one meeting
    /// pattern or none) with two-row sections, and creates a lot of new records: hundreds of courses and
    /// instructors, about a hundred rooms, a few dozen tags, and a handful of subjects, section types, meeting
    /// types and campuses. Every section is new and every name is invented.
    /// </summary>
    /// <param name="dataRows">The number of data rows (5,000 is the largest the importer accepts).</param>
    /// <param name="year">The Academic Year every row names.</param>
    /// <param name="term">The Term every row names.</param>
    /// <returns>The file and the specs of its sections.</returns>
    public static StressSet Large(int dataRows = ImportFileReader.MaxDataRows, string year = "2026-2027", string term = "Fall")
    {
        var file = new StressFile(year, term);
        var specs = new List<ImportSectionSpec>();
        for (int s = 0; file.Rows.Count < dataRows; s++)
        {
            var spec = LargeSection(s, dataRows - file.Rows.Count);
            specs.Add(spec);
            file.AddSection(spec);
        }
        return new StressSet(file, specs);
    }

    /// <summary>The section at position <paramref name="s"/> of the large file.</summary>
    /// <param name="s">The section's index, from 0.</param>
    /// <param name="rowsLeft">How many rows may still be written; a two-row section needs two.</param>
    private static ImportSectionSpec LargeSection(int s, int rowsLeft)
    {
        int course = s % LargeCourses;
        int round = s / LargeCourses;
        int[][] firstDays = [[1, 3], [2, 4], [1, 3, 5], [5]];
        int[][] otherDays = [[2, 4], [1, 3], [2, 4], [1, 3]];   // never a day of the first pattern, so no overlap
        int pattern = s % 4;
        bool hasRoom = s % 6 != 0;

        var meetings = new List<StressMeeting>();
        if (s % 25 != 7)   // every 25th section has no meeting time at all (online or to be announced)
        {
            meetings.Add(new StressMeeting(firstDays[pattern], FirstBegin, FirstEnd,
                Frequency: (s % 40) switch { 3 => "odd", 13 => "even", 23 => "1,6,7", _ => null },
                MeetingType: $"Meeting type {s % 8 + 1:00}",
                Building: hasRoom ? $"Building {s % 10 + 1:00}" : null,
                Room: hasRoom ? $"{100 + s / 10 % 10}" : null));
            if (s % 3 == 0 && rowsLeft >= 2)
                meetings.Add(new StressMeeting(otherDays[pattern], SecondBegin, SecondEnd,
                    MeetingType: $"Meeting type {(s + 1) % 8 + 1:00}",
                    Building: $"Building {(s + 3) % 10 + 1:00}", Room: $"{100 + (s + 3) / 10 % 10}"));
        }

        var instructors = new List<string>();
        if (s % 11 != 10)
        {
            int first = s % LargeInstructors;
            instructors.Add(InstructorName(first));
            if (s % 4 == 0)
            {
                int second = (s * 7 + 1) % LargeInstructors;
                instructors.Add(InstructorName(second == first ? (second + 1) % LargeInstructors : second));
            }
        }

        var tags = new List<string>();
        if (s % 3 == 0) tags.Add($"Tag {s % 36 + 1:00}");
        if (s % 5 == 0 && !tags.Contains($"Tag {(s + 7) % 36 + 1:00}")) tags.Add($"Tag {(s + 7) % 36 + 1:00}");

        return new ImportSectionSpec
        {
            Subject = $"STR{course % 12 + 1:00}",
            CourseNumber = (100 + course).ToString(),
            CourseTitle = $"Stress course {course}",
            SectionCode = $"A{round + 1}",
            SectionType = s % 10 == 9 ? null : $"Section type {s % 12 + 1:00}",
            Instructors = instructors,
            Tags = tags,
            Capacity = s % 9 == 4 ? null : 20 + s % 80,
            Campus = s % 7 == 5 ? null : $"Campus {s % 6 + 1}",
            Meetings = meetings,
        };
    }

    /// <summary>An invented instructor <c>Last, First</c> with an index in each name so that no two collide.</summary>
    private static string InstructorName(int index) =>
        $"{(char)('A' + index % 26)}ander{index:000}, {(char)('A' + index / 26 % 26)}ohn{index:000}";

    // ── The every-row-broken file ─────────────────────────────────────────────

    /// <summary>
    /// A file whose every row is a new section with two or three problems: a day that is not a day, a Begin or
    /// End that cannot be right (End before Begin, or a time past 23:59), and, on two rows in three, a Capacity
    /// that is not a number or a Frequency that is not one. The importer reports each problem on its own line.
    /// </summary>
    /// <param name="dataRows">The number of rows (all broken).</param>
    /// <param name="expectedProblems">The number of problems the importer should report.</param>
    /// <returns>The file.</returns>
    public static StressFile EveryRowBroken(int dataRows, out int expectedProblems)
    {
        var file = new StressFile();
        expectedProblems = 0;
        for (int i = 0; i < dataRows; i++)
        {
            var row = file.AddRow(
                (C.Subject, "BRK"), (C.CourseNumber, (100 + i).ToString()), (C.CourseTitle, $"Broken course {i}"),
                (C.SectionCode, "A"), (C.MeetingDays, "Someday"));
            expectedProblems += 2;   // the day, and the time below
            if (i % 2 == 0)
            {
                row[C.Begin] = "1300";
                row[C.End] = "0900";           // End must be after Begin
            }
            else
            {
                row[C.Begin] = "25:00";        // not a time
                row[C.End] = "1500";
            }

            if (i % 3 == 1)
            {
                row[C.Capacity] = "lots";      // not a whole number
                expectedProblems++;
            }
            else if (i % 3 == 2)
            {
                row[C.Frequency] = "fortnightly";   // not odd, even or week numbers
                expectedProblems++;
            }
        }
        return file;
    }

    // ── Files over the limits ─────────────────────────────────────────────────

    /// <summary>
    /// A file just over the 10 MB limit: a valid sheet of a few thousand rows padded with a Notes column of
    /// random text, which neither a zip nor a text file shrinks. A reader that parsed the file before looking
    /// at its size would take noticeably longer than one that looks at the size first.
    /// </summary>
    /// <param name="format">The format to write.</param>
    /// <returns>The file's bytes, always longer than <see cref="ImportFileReader.MaxFileBytes"/>.</returns>
    public static byte[] Oversize(StressFormat format)
    {
        var random = new Random(20261025);
        for (int bytesPerRow = 6 * 1024; ; bytesPerRow *= 2)
        {
            var file = new StressFile { Format = format };
            file.Layout.Add(new StressColumn("Notes", "Notes"));
            int rows = 2000;
            for (int i = 0; i < rows; i++)
            {
                var noise = new byte[bytesPerRow];
                random.NextBytes(noise);
                file.AddSection(new ImportSectionSpec
                {
                    Subject = "BIG", CourseNumber = (100 + i).ToString(), CourseTitle = $"Big course {i}", SectionCode = "A",
                });
                file.Rows[^1]["Notes"] = Convert.ToBase64String(noise);
            }
            var bytes = file.ToBytes();
            if (bytes.Length > ImportFileReader.MaxFileBytes)
                return bytes;
        }
    }

    // ── A small valid file, and the variants made from it ─────────────────────

    /// <summary>The instructors of the small file, with accents and a typographic apostrophe (the cases CSV encodings can spoil).</summary>
    public static readonly string[] SmallNames =
    {
        "Gagné, Éloïse",     // Gagné, Éloïse
        "Müller, Jürgen",          // Müller, Jürgen
        "O’Brien, Pat",                 // O’Brien, Pat (U+2019 right single quotation mark)
        "Smith, John",
    };

    /// <summary>
    /// A small valid file of new sections (subject <c>SML</c>), each with an instructor from <see cref="SmallNames"/>,
    /// a meeting with a room, a campus and a section type. The first course's title holds commas and quotes.
    /// </summary>
    /// <param name="count">How many sections.</param>
    /// <param name="year">The Academic Year.</param>
    /// <param name="term">The Term.</param>
    /// <param name="subject">The subject (change it to make a second file that does not collide with the first).</param>
    /// <returns>The file and the specs of its sections.</returns>
    public static StressSet Small(int count = 6, string year = "2026-2027", string term = "Fall", string subject = "SML")
    {
        var file = new StressFile(year, term);
        var specs = new List<ImportSectionSpec>();
        for (int i = 0; i < count; i++)
        {
            var spec = new ImportSectionSpec
            {
                Subject = subject,
                CourseNumber = (100 + i).ToString(),
                CourseTitle = i == 0 ? "Intro to \"Geology\", Part 1" : $"Small course {i}",
                SectionCode = "A",
                SectionType = "Lecture",
                Instructors = new[] { SmallNames[i % SmallNames.Length] },
                Capacity = 30 + i,
                Campus = "Main",
                Meetings = new[] { new StressMeeting(new[] { 1, 3 }, 9 * 60, 10 * 60 + 15, MeetingType: "Class", Building: "SCI", Room: (210 + i).ToString()) },
            };
            specs.Add(spec);
            file.AddSection(spec);
        }
        return new StressSet(file, specs);
    }

    /// <summary>
    /// The small file with a section whose title holds a line break, to see a quoted field that spans lines survive.
    /// </summary>
    /// <returns>The file and its sections; the last section's title holds the line break.</returns>
    public static StressSet SmallWithLineBreakTitle()
    {
        var set = Small();
        var extra = new ImportSectionSpec
        {
            Subject = "SML", CourseNumber = "199", CourseTitle = "Line one\r\nLine two", SectionCode = "A",
        };
        set.File.AddSection(extra);
        return new StressSet(set.File, set.Sections.Append(extra).ToList());
    }

    /// <summary>
    /// Changes the header texts of a file so they differ from the template in case and spacing only (leading
    /// and trailing spaces, doubled spaces, other capitals), the way a hand-edited header looks.
    /// </summary>
    /// <param name="file">The file to change.</param>
    public static void MessyHeaders(StressFile file)
    {
        for (int i = 0; i < file.Layout.Count; i++)
        {
            var column = file.Layout[i];
            string text = (i % 3) switch
            {
                0 => $"  {column.HeaderText.ToUpperInvariant()} ",
                1 => column.HeaderText.ToLowerInvariant().Replace(" ", "   "),
                _ => $" {column.HeaderText}  ",
            };
            file.Layout[i] = column with { HeaderText = text };
        }
    }

    /// <summary>
    /// Reverses the column order and puts the user's own columns (notes, an internal id) among the template's.
    /// </summary>
    /// <param name="file">The file to change.</param>
    public static void ReorderWithExtraColumns(StressFile file)
    {
        file.Layout.Reverse();
        file.Layout.Insert(2, new StressColumn("My notes", null, "see the dean"));
        file.Layout.Insert(7, new StressColumn("Internal id", null, 12345));
        file.Layout.Add(new StressColumn("Reviewed?", null, true));
    }

    /// <summary>Removes a template column from the layout, as when a user deletes one.</summary>
    /// <param name="file">The file to change.</param>
    /// <param name="column">The template column to remove.</param>
    public static void DropColumn(StressFile file, string column) =>
        file.Layout.RemoveAll(c => c.Key == column);

    /// <summary>Repeats a template column's header (the second copy holds a constant).</summary>
    /// <param name="file">The file to change.</param>
    /// <param name="column">The template column to repeat.</param>
    public static void DuplicateColumn(StressFile file, string column) =>
        file.Layout.Add(new StressColumn(column, null, "extra"));

    // ── Row semantics ─────────────────────────────────────────────────────────

    /// <summary>
    /// One section whose two meeting patterns are on row 2 and on row 4,000, with 3,997 other sections between them.
    /// The importer should treat the two rows as one section with both patterns.
    /// </summary>
    /// <param name="year">The Academic Year.</param>
    /// <param name="term">The Term.</param>
    /// <param name="spec">The far-apart section, with both its patterns.</param>
    /// <returns>The file (4,000 data rows... the last at row 4,000).</returns>
    public static StressFile FarApart(string year, string term, out ImportSectionSpec spec)
    {
        var file = new StressFile(year, term);
        spec = new ImportSectionSpec
        {
            Subject = "FAR", CourseNumber = "101", CourseTitle = "Far apart", SectionCode = "A", SectionType = "Lecture",
            Instructors = new[] { "Smith, John" }, Capacity = 40,
            Meetings = new[]
            {
                new StressMeeting(new[] { 1, 3 }, 9 * 60, 10 * 60),
                new StressMeeting(new[] { 2, 4 }, 13 * 60, 14 * 60),
            },
        };
        // The section's first pattern on row 2, the other sections, then its second pattern on row 4,000.
        file.AddSection(new ImportSectionSpec
        {
            Subject = spec.Subject, CourseNumber = spec.CourseNumber, CourseTitle = spec.CourseTitle, SectionCode = spec.SectionCode,
            SectionType = spec.SectionType, Instructors = spec.Instructors, Capacity = spec.Capacity,
            Meetings = new[] { spec.Meetings[0] },
        });
        for (int i = 0; i < 3997; i++)
            file.AddSection(new ImportSectionSpec { Subject = "FIL", CourseNumber = (100 + i).ToString(), CourseTitle = $"Filler {i}", SectionCode = "A" });
        file.AddRow((C.Subject, "FAR"), (C.CourseNumber, "101"), (C.SectionCode, "A"),
            (C.MeetingDays, "T,Th"), (C.Begin, "1300"), (C.End, "1400"));
        return file;
    }

    /// <summary>
    /// Two rows of one section that disagree about its Section Type and its Capacity: a problem on the second row for each (invariant 4).
    /// </summary>
    /// <param name="year">The Academic Year.</param>
    /// <param name="term">The Term.</param>
    /// <returns>The file; the problems are on row 3, Section Type and Capacity.</returns>
    public static StressFile RowsDisagree(string year, string term)
    {
        var file = new StressFile(year, term);
        file.AddRow((C.Subject, "DIS"), (C.CourseNumber, "101"), (C.CourseTitle, "Disagree"), (C.SectionCode, "A"),
            (C.SectionType, "Lecture"), (C.Capacity, 30), (C.MeetingDays, "M"), (C.Begin, "0900"), (C.End, "1000"));
        file.AddRow((C.Subject, "DIS"), (C.CourseNumber, "101"), (C.SectionCode, "A"),
            (C.SectionType, "Lab"), (C.Capacity, 25), (C.MeetingDays, "T"), (C.Begin, "0900"), (C.End, "1000"));
        return file;
    }

    /// <summary>
    /// One section whose meeting row is written twice, exactly the same (days and times).
    /// </summary>
    /// <param name="year">The Academic Year.</param>
    /// <param name="term">The Term.</param>
    /// <returns>The file.</returns>
    public static StressFile DuplicateMeeting(string year, string term)
    {
        var file = new StressFile(year, term);
        file.AddRow((C.Subject, "DUP"), (C.CourseNumber, "101"), (C.CourseTitle, "Duplicated"), (C.SectionCode, "A"),
            (C.MeetingDays, "M,W"), (C.Begin, "0900"), (C.End, "1000"));
        file.AddRow((C.Subject, "DUP"), (C.CourseNumber, "101"), (C.SectionCode, "A"),
            (C.MeetingDays, "M,W"), (C.Begin, "0900"), (C.End, "1000"));
        return file;
    }

    // ── Excel cell types ──────────────────────────────────────────────────────

    /// <summary>The Excel day fraction for 09:00 with a little floating-point error, as Excel stores a typed time.</summary>
    public const double NineOClockWithError = 0.37499999999;

    /// <summary>The Excel day fraction for 10:00 with a little floating-point error.</summary>
    public const double TenOClockWithError = 0.41666666665;

    /// <summary>
    /// A workbook whose cells are not all text: numeric Course Number and Section Code, times as day fractions
    /// with float error, a Section Code that Excel turned into a date, a boolean in a text column, a formula
    /// cell, error cells (in a required column and in optional ones), and Capacity as <c>60.0</c> (and, when asked, as <c>60.5</c>, which is a problem).
    /// The rows are returned in the order of the sections they describe.
    /// </summary>
    /// <param name="withFractionalCapacity">Also add a section whose Capacity is 60.5.</param>
    /// <returns>The file, and the row index of each case by name.</returns>
    public static (StressFile File, Dictionary<string, int> RowOf) CellTypes(bool withFractionalCapacity)
    {
        var file = new StressFile();
        var rowOf = new Dictionary<string, int>();

        rowOf["numeric"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 101), (C.CourseTitle, "Numeric cells"), (C.SectionCode, 1),
            (C.MeetingDays, "M"), (C.Begin, new XlsxTestWorkbook.TimeFraction(NineOClockWithError)),
            (C.End, new XlsxTestWorkbook.TimeFraction(TenOClockWithError)), (C.Capacity, 60.0));

        rowOf["date-code"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 102), (C.CourseTitle, "Date as code"),
            (C.SectionCode, new DateTime(2026, 1, 2)));   // the user typed 1-2 and Excel made it 2 January

        rowOf["boolean"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 103), (C.CourseTitle, "Boolean cell"), (C.SectionCode, "A"),
            (C.SectionType, true));

        rowOf["formula"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 104), (C.CourseTitle, "Formula cell"), (C.SectionCode, "A"),
            (C.Instructors, new XlsxTestWorkbook.FormulaCell("\"Formula, \"&\"Fred\"", "Formula, Fred")));

        rowOf["error"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 105), (C.CourseTitle, "Error cell"),
            (C.SectionCode, new XlsxTestWorkbook.ErrorCell("#N/A")));

        rowOf["error-optional"] = file.Rows.Count;
        file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 107), (C.CourseTitle, "Errors in optional cells"), (C.SectionCode, "A"),
            (C.Instructors, new XlsxTestWorkbook.ErrorCell("#REF!")), (C.SectionType, new XlsxTestWorkbook.ErrorCell("#N/A")),
            (C.Capacity, new XlsxTestWorkbook.ErrorCell("#DIV/0!")));

        if (withFractionalCapacity)
        {
            rowOf["capacity-60.5"] = file.Rows.Count;
            file.AddRow((C.Subject, "XLS"), (C.CourseNumber, 106), (C.CourseTitle, "Fractional capacity"),
                (C.SectionCode, "A"), (C.Capacity, 60.5));
        }
        return (file, rowOf);
    }

    // ── Adversarial text ──────────────────────────────────────────────────────

    /// <summary>
    /// A valid file in which each awkward kind of text sits on its own section (subject <c>ADV</c>): accented,
    /// non-Latin and right-to-left names, an emoji and a 4,000-character title, padding and runs of spaces,
    /// non-breaking spaces that should match like spaces, a zero-width space inside a course code, HTML, and
    /// values that look like spreadsheet formulas.
    /// </summary>
    /// <returns>The file and the cases whose stored values a generic check can verify.</returns>
    public static AdversarialSet Adversarial()
    {
        var file = new StressFile();
        var verifiable = new Dictionary<string, ImportSectionSpec>();

        void Add(string name, string number, string title,
            string? sectionType = null, string[]? instructors = null, string[]? tags = null)
        {
            var spec = new ImportSectionSpec
            {
                Subject = "ADV", CourseNumber = number, CourseTitle = title, SectionCode = "A",
                SectionType = sectionType, Instructors = instructors ?? Array.Empty<string>(), Tags = tags ?? Array.Empty<string>(),
            };
            verifiable[name] = spec;
            file.AddSection(spec);
        }

        Add("accents", "101", "Accented name", instructors: new[] { "Gagné, Éloïse" });                 // Gagné, Éloïse
        Add("umlauts", "102", "Umlaut name", instructors: new[] { "Müller, Jürgen" });                       // Müller, Jürgen
        Add("cjk", "103", "Chinese name", instructors: new[] { "王, 小明" });                              // 王, 小明
        Add("rtl", "104", "Hebrew name", instructors: new[] { "כהן, דוד" });             // כהן, דוד
        Add("emoji-title", "105", "Intro \U0001F9EC Geology \U0001F600");                                              // emoji in a title
        Add("long-title", "106", new string('T', 4000));
        Add("html", "108", "<script>alert(\"x\")</script>", sectionType: "<b>Bold</b>",
            instructors: new[] { "<i>Last</i>, <u>First</u>" }, tags: new[] { "<img src=x onerror=alert(1)>" });
        Add("formulas", "109", "=1+1", sectionType: "=1+1",
            instructors: new[] { "=1+1, Pat" }, tags: new[] { "+1", "-1", "@SUM(A1)", "=2+2" });

        // Spaces: padding around every value and runs of spaces inside names. Stored values are trimmed at both
        // ends and keep inner runs, so the spec holds the expected stored text; the file gets the padded text.
        var spaces = new ImportSectionSpec
        {
            Subject = "ADV", CourseNumber = "107", CourseTitle = "Spaced title", SectionCode = "S",
            SectionType = "Spaced   Type", Instructors = new[] { "Spaced, Pat" }, Tags = new[] { "Spaced   Tag" },
        };
        verifiable["spaces"] = spaces;
        int spacesRow = file.AddSection(spaces);
        file.Rows[spacesRow][C.Subject] = " ADV ";
        file.Rows[spacesRow][C.CourseNumber] = "  107 ";
        file.Rows[spacesRow][C.SectionCode] = "  S  ";
        file.Rows[spacesRow][C.SectionType] = "  Spaced   Type  ";
        file.Rows[spacesRow][C.Instructors] = "  Spaced ,   Pat  ";
        file.Rows[spacesRow][C.Tags] = "  Spaced   Tag ";

        // Non-breaking spaces: the same instructor and section type typed once with U+00A0 and once with a plain space.
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "201"), (C.CourseTitle, "NBSP first"), (C.SectionCode, "A"),
            (C.SectionType, "Hy brid"), (C.Instructors, "Van Dyk, Pat"));
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "202"), (C.CourseTitle, "NBSP second"), (C.SectionCode, "A"),
            (C.SectionType, "Hy brid"), (C.Instructors, "Van Dyk, Pat"));

        // A zero-width space (U+200B) inside a course number, next to the same course typed without it.
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "30​1"), (C.CourseTitle, "Zero-width course"), (C.SectionCode, "A"));
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "301"), (C.CourseTitle, "Plain course"), (C.SectionCode, "A"));

        return new AdversarialSet(file, verifiable);
    }

    /// <summary>
    /// A file whose problems are made of formula-looking text: values that begin with <c>=</c>, <c>+</c>,
    /// <c>-</c> or <c>@</c> and that the importer may quote at the start of a message (a new course that needs a
    /// title, a section whose rows disagree, a person or a tag listed twice). It is for checking the "Copy list"
    /// text: pasted into Excel, a cell that begins with one of those characters is run as a formula.
    /// </summary>
    /// <returns>The file; importing it can only fail.</returns>
    public static StressFile AdversarialProblems()
    {
        var file = new StressFile();
        // A new subject "=1+1": the "needs a Course Title" message starts with the course code.
        file.AddRow((C.Subject, "=1+1"), (C.CourseNumber, "1"), (C.SectionCode, "A"));
        // Two rows of one section that disagree: the message starts with the section's label.
        file.AddRow((C.Subject, "+1"), (C.CourseNumber, "2"), (C.CourseTitle, "Disagree"), (C.SectionCode, "A"), (C.SectionType, "Lecture"));
        file.AddRow((C.Subject, "+1"), (C.CourseNumber, "2"), (C.SectionCode, "A"), (C.SectionType, "Lab"));
        // A person listed twice and a tag listed twice: the message starts with the name as typed.
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "400"), (C.CourseTitle, "Twice"), (C.SectionCode, "A"),
            (C.Instructors, "-Smith, Pat; -Smith, Pat"), (C.Tags, "@dup; @dup"));
        // For contrast: messages that quote the value, or start with fixed words.
        file.AddRow((C.Subject, "ADV"), (C.CourseNumber, "401"), (C.CourseTitle, "Quoted"), (C.SectionCode, "A"),
            (C.MeetingDays, "=cmd"), (C.Begin, "1000"), (C.End, "1100"), (C.Frequency, "@weekly"), (C.Capacity, "-5"));
        return file;
    }
}
