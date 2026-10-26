using System.Text.Json;
using System.Text.RegularExpressions;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.Services.SectionImport;
using TermPoint.ViewModels.Management;
using C = TermPoint.Services.SectionImport.SectionImportColumns;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// A throwaway copy of the private real-data database, opened for reading so that stress files can be built
/// from what it holds (its sections, courses, instructors, rooms, campuses and legal start times). Everything is
/// read at run time; nothing about the data is written into the repository. Dispose it to delete the copy.
/// </summary>
internal sealed class StressContext : IDisposable
{
    /// <summary>The academic year the stress files are aimed at (its Winter, Early Summer, Summer and Late Summer are empty in the real data).</summary>
    public const string TargetYear = "2026-2027";

    /// <summary>The academic year whose Fall semester is the round trip's source.</summary>
    public const string SourceYear = "2025-2026";

    /// <summary>The semester name of the fall semesters.</summary>
    public const string Fall = "Fall";

    /// <summary>The rig over the copy (opened on <see cref="TargetYear"/>'s Winter).</summary>
    public ImportRig Rig { get; }

    /// <summary>The lists that turn ids into names, read once.</summary>
    public ExportLookups Lookups { get; }

    private StressContext(ImportRig rig)
    {
        Rig = rig;
        Lookups = ExportLookups.Build(rig);
    }

    /// <summary>Copies the real database to a temporary folder and opens it.</summary>
    /// <returns>The context; dispose it to delete the copy.</returns>
    public static StressContext Open() => new(RealImportDb.Open(TargetYear, "Winter"));

    /// <summary>The sections of one semester of the copy, in the repository's order.</summary>
    /// <param name="year">The academic year's name.</param>
    /// <param name="term">The semester's name.</param>
    /// <returns>The sections.</returns>
    public List<Section> SectionsOf(string year, string term) =>
        Rig.Sections.GetAll(RealImportDb.FindSemester(Rig, year, term).Semester.Id);

    /// <summary>
    /// Exports a semester's sections into a file that names another semester.
    /// </summary>
    /// <param name="sourceYear">The semester to export.</param>
    /// <param name="sourceTerm">The semester to export.</param>
    /// <param name="targetYear">The academic year the file names.</param>
    /// <param name="targetTerm">The term the file names.</param>
    /// <param name="take">At most this many sections (null for all of them).</param>
    /// <param name="exported">The result for every section taken.</param>
    /// <returns>The file.</returns>
    public StressFile Export(string sourceYear, string sourceTerm, string targetYear, string targetTerm, int? take,
        out List<ExportedSection> exported)
    {
        var sections = SectionsOf(sourceYear, sourceTerm).AsEnumerable();
        if (take is not null)
            sections = sections.Take(take.Value);
        return StressExport.ExportAll(sections, Lookups, targetYear, targetTerm, out exported);
    }

    /// <summary>Deletes the copy.</summary>
    public void Dispose() => Rig.Dispose();
}

/// <summary>
/// What the matching-names file holds: the existing records its rows are written against, and the made-up
/// names (typos, a new instructor) it plants.
/// </summary>
/// <param name="File">The file.</param>
/// <param name="Course">The existing course every row is a section of.</param>
/// <param name="Campus">The existing campus rows 1 and 2 name (with a trailing space, and in lower case).</param>
/// <param name="TypoCampus">The misspelt campus row 3 names (a new campus, with a near-miss warning).</param>
/// <param name="TypoSectionType">The misspelt section type row 6 names (a new section type, with a near-miss warning).</param>
/// <param name="Room">The existing room row 4 names by number alone, or null when the data has no room to try.</param>
/// <param name="InstructorLast">The last name of the new instructor row 5 names; it already belongs to an existing instructor.</param>
/// <param name="NewInstructorFirst">The new instructor's first name.</param>
internal sealed record MatchingCase(
    StressFile File, Course Course, Campus Campus, string TypoCampus, string TypoSectionType,
    Room? Room, string InstructorLast, string NewInstructorFirst);

/// <summary>
/// What the start-time file holds for one meeting length: a section that begins at a legal start time and one
/// that begins at a time that is not legal.
/// </summary>
/// <param name="Minutes">The meeting length in minutes (80, 90, 110 ...).</param>
/// <param name="LegalStart">A legal start time for that length (minutes from midnight).</param>
/// <param name="IllegalStart">A start time that is not legal for that length.</param>
/// <param name="LegalRowIndex">The data row index of the section that starts at the legal time.</param>
/// <param name="IllegalRowIndex">The data row index of the section that starts at the illegal time.</param>
/// <param name="HasOwnList">True when the academic year has start times configured for exactly this length (the warning then names the length); false when the length has none and the union of all lists applies.</param>
internal sealed record FractionalCase(int Minutes, int LegalStart, int IllegalStart, int LegalRowIndex, int IllegalRowIndex, bool HasOwnList)
{
    /// <summary>How the importer's warning words the length: <c> for 80-minute meetings</c>, or nothing for a length with no list of its own.</summary>
    public string LengthPhrase => HasOwnList ? $" for {ImportText.LengthAdjective(Minutes)} meetings" : string.Empty;

    /// <summary>The start time that is not legal, as the four-digit text the warning uses.</summary>
    public string IllegalText => $"{IllegalStart / 60:00}{IllegalStart % 60:00}";
}

/// <summary>
/// Stress files built from the real data's own names and settings, shared by the real-data tests and the kit
/// so that both use the same cases. Nothing here hard-codes a name from the data: the names are found at run time.
/// </summary>
internal static class StressRealCases
{
    /// <summary>
    /// Builds the matching-names file: six new sections of one existing course, each exercising one way a name
    /// can differ from the existing one. Row 1 names the existing campus with a trailing space, row 2 in lower
    /// case (with the subject in lower case too), row 3 misspells the campus (and writes the course number with
    /// a space inside it), row 4 gives a room by number alone, row 5 a new instructor who shares a last name
    /// with an existing one, and row 6 misspells a section type.
    /// </summary>
    /// <param name="ctx">The real-data copy.</param>
    /// <param name="year">The academic year the file names.</param>
    /// <param name="term">The term the file names.</param>
    /// <returns>The file and the records it was written against.</returns>
    public static MatchingCase Matching(StressContext ctx, string year, string term)
    {
        var lookups = ctx.Lookups;
        var (course, subject, number) = PlainCourse(ctx);

        var campus = lookups.Campuses.Values.OrderByDescending(c => c.Name.Length).ThenBy(c => c.Name, StringComparer.Ordinal).First();
        var typoCampus = Misspell(campus.Name);

        var sectionTypes = ctx.Rig.Environment.GetAll(SchedulingEnvironmentTypes.SectionType);
        var sectionType = sectionTypes.FirstOrDefault(t => t.Name.EndsWith(')'))
            ?? sectionTypes.OrderByDescending(t => t.Name.Length).First();
        var typoSectionType = sectionType.Name[..^1];

        var room = lookups.Rooms.Values.Where(r => string.IsNullOrWhiteSpace(r.Building) && lookups.RoomsPerNumber.GetValueOrDefault(r.RoomNumber) == 1)
            .OrderBy(r => r.RoomNumber, StringComparer.Ordinal).FirstOrDefault();

        var byLast = lookups.Instructors.Values.GroupBy(i => NameMatch.Normalize(i.LastName), NameMatch.Comparer).ToList();
        var shared = byLast.FirstOrDefault(g => g.Count() > 1) ?? byLast.First();
        var lastName = shared.First().LastName;
        var newFirst = "Zed";
        while (lookups.Instructors.Values.Any(i => NameMatch.Equal(i.LastName, lastName) && NameMatch.Equal(i.FirstName, newFirst)))
            newFirst += "x";

        var file = new StressFile(year, term);
        ImportSectionSpec Spec(string code, string? campusName = null,
            string? instructor = null, string? type = null, StressMeeting? meeting = null) => new()
        {
            Subject = subject.CalendarAbbreviation, CourseNumber = number, SectionCode = code,
            Campus = campusName, Instructors = instructor is null ? Array.Empty<string>() : new[] { instructor },
            SectionType = type, Meetings = meeting is null ? Array.Empty<StressMeeting>() : new[] { meeting },
        };

        // Row 1: the existing campus with a trailing space.
        int r1 = file.AddSection(Spec("M1", campusName: campus.Name));
        file.Rows[r1][C.Campus] = campus.Name + " ";
        // Row 2: the campus and the subject in lower case.
        int r2 = file.AddSection(Spec("M2", campusName: campus.Name));
        file.Rows[r2][C.Campus] = campus.Name.ToLowerInvariant();
        file.Rows[r2][C.Subject] = subject.CalendarAbbreviation.ToLowerInvariant();
        // Row 3: a misspelt campus, and a course number with a space inside it.
        int r3 = file.AddSection(Spec("M3", campusName: typoCampus));
        if (number.Length > 1)
            file.Rows[r3][C.CourseNumber] = number.Insert(1, " ");
        // Row 4: a room given by number alone.
        // The room chosen has no building (that is how a blank Building can name it), so the cell is left blank.
        file.AddSection(room is null
            ? Spec("M4")
            : Spec("M4", meeting: new StressMeeting(new[] { 1 }, 8 * 60 + 30, 9 * 60 + 50, Room: room.RoomNumber)));
        // Row 5: a new instructor who shares a last name with an existing one.
        file.AddSection(Spec("M5", instructor: $"{lastName}, {newFirst}"));
        // Row 6: a misspelt section type.
        file.AddSection(Spec("M6", type: typoSectionType));

        return new MatchingCase(file, course, campus, typoCampus, typoSectionType, room, lastName, newFirst);
    }

    /// <summary>
    /// An existing course whose code is its subject's abbreviation followed by digits (so a Subject and a Course
    /// Number can be written for it), the first in code order.
    /// </summary>
    /// <param name="ctx">The real-data copy.</param>
    /// <returns>The course, its subject, and the course number (the code without the abbreviation).</returns>
    public static (Course Course, Subject Subject, string Number) PlainCourse(StressContext ctx)
    {
        var lookups = ctx.Lookups;
        var course = lookups.Courses.Values
            .Where(c => lookups.Subjects.ContainsKey(c.SubjectId) && Regex.IsMatch(NameMatch.NormalizeCode(c.CalendarCode), "^[A-Za-z]+[0-9]{2,}$"))
            .OrderBy(c => c.CalendarCode, StringComparer.Ordinal).First();
        var subject = lookups.Subjects[course.SubjectId];
        var number = NameMatch.NormalizeCode(course.CalendarCode)[NameMatch.NormalizeCode(subject.CalendarAbbreviation).Length..];
        return (course, subject, number);
    }

    /// <summary>
    /// Swaps two adjacent letters near the end of a name (<c>Riverside</c> becomes <c>Riversdie</c>), which is
    /// a typo of one transposition.
    /// </summary>
    /// <param name="name">The name; it needs two adjacent different letters after its first character.</param>
    /// <returns>The misspelt name.</returns>
    public static string Misspell(string name)
    {
        for (int i = name.Length - 3; i >= 1; i--)
        {
            if (char.IsLetter(name[i]) && char.IsLetter(name[i + 1]) && name[i] != name[i + 1])
                return string.Concat(name.AsSpan(0, i), name.AsSpan(i + 1, 1), name.AsSpan(i, 1), name.AsSpan(i + 2));
        }
        throw new InvalidOperationException($"No transposable letters in '{name}'.");
    }

    /// <summary>
    /// Attaches to an academic year of the rig's database the fractional block lengths (80, 110, 170 and 230
    /// minutes, written 1.333... hours and so on: neither whole nor half hours) that the database holds under academic-year ids no academic year has.
    /// In the real data these are the only fractional lengths: they were left under ids of years that no longer
    /// exist, so no real academic year uses them, and the importer (which reads the start times of the semester's own
    /// year) never sees them. This copies them, in the temporary copy only, as if the department had configured them.
    /// </summary>
    /// <param name="rig">The rig over the temporary copy.</param>
    /// <param name="yearName">The academic year to give the lengths to.</param>
    /// <returns>The entries added (empty when the year already has fractional lengths or the database holds none).</returns>
    public static List<LegalStartTime> AttachOrphanBlockLengths(ImportRig rig, string yearName)
    {
        var years = new AcademicYearRepository(rig.Db).GetAll();
        var year = years.First(y => y.Name == yearName);
        var repository = new LegalStartTimeRepository(rig.Db);
        if (repository.GetAll(year.Id).Any(IsFractional))
            return new List<LegalStartTime>();

        var orphans = new List<LegalStartTime>();
        using (var cmd = rig.Db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT academic_year_id, block_length, start_times FROM LegalStartTimes";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var owner = reader.GetString(0);
                var entry = new LegalStartTime
                {
                    BlockLength = reader.GetDouble(1),
                    StartTimes = JsonSerializer.Deserialize<List<int>>(reader.GetString(2)) ?? new List<int>(),
                };
                if (years.All(y => y.Id != owner) && IsFractional(entry) && entry.StartTimes.Count > 0
                    && orphans.All(o => Math.Abs(o.BlockLength - entry.BlockLength) > 1e-6))
                    orphans.Add(entry);
            }
        }
        foreach (var entry in orphans)
            repository.Insert(entry, year.Id);
        return orphans;
    }

    /// <summary>True when a block length (in hours) is neither a whole nor a half hour: 1.333... hours (80 minutes) is, 1.5 and 3 are not.</summary>
    private static bool IsFractional(LegalStartTime entry) =>
        Math.Abs(entry.BlockLength * 2 - Math.Round(entry.BlockLength * 2)) > 1e-6;

    /// <summary>
    /// Builds the start-time file: for each block length the academic year has start times for, one section that
    /// starts at a legal time and one that starts at a time that is not in the list; with
    /// <paramref name="includeUnlistedLength"/>, also an 80-minute pair (or the nearest length the year has no list for),
    /// for which the importer checks against every start time the year has. All are sections of one existing course,
    /// on Monday, and the illegal start times are never on the hour.
    /// </summary>
    /// <param name="rig">The rig over the copy (its academic year supplies the start times).</param>
    /// <param name="year">The academic year the file names.</param>
    /// <param name="term">The term the file names.</param>
    /// <param name="fractionalOnly">Only the lengths that are neither a whole nor a half hour (needs <see cref="AttachOrphanBlockLengths"/> on the real data).</param>
    /// <param name="includeUnlistedLength">Add the pair for a length that has no list of its own.</param>
    /// <param name="cases">What each length's rows are.</param>
    /// <returns>The file.</returns>
    public static StressFile StartTimes(ImportRig rig, string year, string term, bool fractionalOnly, bool includeUnlistedLength,
        out List<FractionalCase> cases)
    {
        var academicYear = new AcademicYearRepository(rig.Db).GetAll().First(y => y.Name == year);
        var all = new LegalStartTimeRepository(rig.Db).GetAll(academicYear.Id).Where(l => l.StartTimes.Count > 0).ToList();
        var listed = all.Where(l => !fractionalOnly || IsFractional(l)).OrderBy(l => l.BlockLength).ToList();
        var union = all.SelectMany(l => l.StartTimes).ToHashSet();

        var lookups = ExportLookups.Build(rig);
        var course = lookups.Courses.Values
            .Where(c => lookups.Subjects.ContainsKey(c.SubjectId) && Regex.IsMatch(NameMatch.NormalizeCode(c.CalendarCode), "^[A-Za-z]+[0-9]{2,}$"))
            .OrderBy(c => c.CalendarCode, StringComparer.Ordinal).First();
        var subject = lookups.Subjects[course.SubjectId];
        var number = NameMatch.NormalizeCode(course.CalendarCode)[NameMatch.NormalizeCode(subject.CalendarAbbreviation).Length..];

        var file = new StressFile(year, term);
        var made = new List<FractionalCase>();

        void AddPair(int minutes, IReadOnlyCollection<int> legalList, bool hasOwnList)
        {
            int legalStart = legalList.OrderBy(t => t).First(t => t + minutes <= 22 * 60);
            // The first five-minute mark from 08:05 that is not legal (and still ends by 22:00), never on the hour.
            int illegalStart = Enumerable.Range(0, 100).Select(i => 8 * 60 + 5 * i)
                .First(t => !legalList.Contains(t) && t + minutes <= 22 * 60 && t % 60 != 0);
            int legalRow = file.AddSection(Section(subject, number, $"L{minutes}", legalStart, minutes));
            int illegalRow = file.AddSection(Section(subject, number, $"X{minutes}", illegalStart, minutes));
            made.Add(new FractionalCase(minutes, legalStart, illegalStart, legalRow, illegalRow, hasOwnList));
        }

        foreach (var entry in listed)
            AddPair((int)Math.Round(entry.BlockLength * 60), entry.StartTimes, hasOwnList: true);

        if (includeUnlistedLength)
        {
            int minutes = new[] { 80, 70, 65, 55 }.First(m => all.All(l => Math.Abs(l.BlockLength * 60 - m) > 0.06));
            AddPair(minutes, union, hasOwnList: false);
        }
        cases = made;
        return file;
    }

    /// <summary>One section of an existing course meeting on Monday for a length starting at a time.</summary>
    private static ImportSectionSpec Section(Subject subject, string number, string code, int start, int minutes) => new()
    {
        Subject = subject.CalendarAbbreviation, CourseNumber = number, SectionCode = code,
        Meetings = new[] { new StressMeeting(new[] { 1 }, start, start + minutes) },
    };
}
