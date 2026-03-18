using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;
using System.Text;

namespace SchedulingAssistant.Demo;

/// <summary>
/// Debug-only utility that reads data from the open SQLite database and writes the
/// <c>DemoData.*.cs</c> partial-class files to a specified output folder.
///
/// Instructor names are anonymised using a deterministic substitution table built at
/// generation time. IDs are preserved throughout so all cross-entity references remain valid.
///
/// Typical workflow:
///   1. Open the real database in debug mode.
///   2. Select the academic year you want to export via the semester dropdowns.
///   3. Open the Debug flyout and click "Generate Demo Data".
///   4. Point the output folder at <c>src/SchedulingAssistant/Demo/</c> in the repo.
///   5. Rebuild — the WASM browser project will pick up the new data automatically.
/// </summary>
#if DEBUG
public class DemoDataGenerator
{
    // ── Repositories injected via DI ────────────────────────────────────────
    private readonly IAcademicYearRepository   _ayRepo;
    private readonly ISemesterRepository       _semRepo;
    private readonly IInstructorRepository     _instructorRepo;
    private readonly IRoomRepository           _roomRepo;
    private readonly ISubjectRepository        _subjectRepo;
    private readonly ICourseRepository         _courseRepo;
    private readonly ISectionRepository        _sectionRepo;
    private readonly ISectionPropertyRepository _propRepo;
    private readonly ISectionPrefixRepository  _prefixRepo;
    private readonly IBlockPatternRepository   _blockPatternRepo;
    private readonly ILegalStartTimeRepository _legalStartTimeRepo;

    // ── Anonymisation name pool (first names × last names) ──────────────────
    // Names are fictional. Sequential assignment keyed by instructor ID ensures
    // the same instructor always receives the same fake name across re-runs.

    private static readonly string[] FirstNames =
    [
        "Alice",    "Benjamin", "Clara",    "David",    "Elena",
        "Franklin", "Grace",    "Henry",    "Isabel",   "Julian",
        "Karen",    "Lawrence", "Maria",    "Nathan",   "Olivia",
        "Patrick",  "Quinn",    "Rachel",   "Samuel",   "Teresa",
        "Ulrich",   "Vivian",   "Walter",   "Yasmin",   "Arthur",
        "Beatrice", "Calvin",   "Diana",    "Edmund",   "Fiona",
        "Gerald",   "Harriet",  "Ivan",     "Josephine","Kenneth",
        "Lydia",    "Marcus",   "Nadia",    "Oscar",    "Petra",
        "Roland",   "Sylvia",   "Tobias",   "Ursula",   "Victor",
        "Winifred", "Xavier",   "Yolanda",  "Zachary",  "Agnes",
    ];

    private static readonly string[] LastNames =
    [
        "Abbott",   "Brennan",  "Chen",     "Donovan",  "Ellis",
        "Foster",   "Graham",   "Harmon",   "Ingram",   "Janssen",
        "Kelley",   "Lawson",   "Monroe",   "Nash",     "Obrien",
        "Pierce",   "Quinlan",  "Ramos",    "Sterling", "Townsend",
        "Underwood","Vance",    "Warren",   "Xu",       "York",
        "Aldridge", "Burke",    "Callahan", "Daley",    "Everett",
        "Flynn",    "Griffith", "Hayward",  "Irving",   "Jennings",
        "Knox",     "Lennon",   "Malone",   "Norris",   "Okafor",
        "Pryor",    "Reeves",   "Santos",   "Tanner",   "Upton",
        "Vasquez",  "Wolfe",    "Yates",    "Zhu",      "Alderton",
    ];

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="DemoDataGenerator"/> with all required repositories.
    /// </summary>
    public DemoDataGenerator(
        IAcademicYearRepository    ayRepo,
        ISemesterRepository        semRepo,
        IInstructorRepository      instructorRepo,
        IRoomRepository            roomRepo,
        ISubjectRepository         subjectRepo,
        ICourseRepository          courseRepo,
        ISectionRepository         sectionRepo,
        ISectionPropertyRepository propRepo,
        ISectionPrefixRepository   prefixRepo,
        IBlockPatternRepository    blockPatternRepo,
        ILegalStartTimeRepository  legalStartTimeRepo)
    {
        _ayRepo             = ayRepo;
        _semRepo            = semRepo;
        _instructorRepo     = instructorRepo;
        _roomRepo           = roomRepo;
        _subjectRepo        = subjectRepo;
        _courseRepo         = courseRepo;
        _sectionRepo        = sectionRepo;
        _propRepo           = propRepo;
        _prefixRepo         = prefixRepo;
        _blockPatternRepo   = blockPatternRepo;
        _legalStartTimeRepo = legalStartTimeRepo;
    }

    /// <summary>
    /// Reads all relevant data for the given academic year from the database and
    /// writes one <c>DemoData.*.cs</c> file per entity type into <paramref name="outputFolder"/>.
    /// </summary>
    /// <param name="academicYearId">ID of the academic year to export.</param>
    /// <param name="outputFolder">Destination directory (must exist).</param>
    /// <param name="progress">Optional sink for status messages during generation.</param>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when the academic year cannot be found in the database.
    /// </exception>
    public void Generate(string academicYearId, string outputFolder, IProgress<string>? progress = null)
    {
        progress?.Report("Loading data from database...");

        var ay = _ayRepo.GetById(academicYearId)
            ?? throw new InvalidOperationException($"Academic year not found: {academicYearId}");

        var semesters       = _semRepo.GetByAcademicYear(academicYearId);
        var instructors     = _instructorRepo.GetAll();
        var rooms           = _roomRepo.GetAll();
        var subjects        = _subjectRepo.GetAll();
        var courses         = _courseRepo.GetAll();
        var sections        = semesters.SelectMany(s => _sectionRepo.GetAll(s.Id)).ToList();
        var sectionTypes    = _propRepo.GetAll(SectionPropertyTypes.SectionType);
        var meetingTypes    = _propRepo.GetAll(SectionPropertyTypes.MeetingType);
        var staffTypes      = _propRepo.GetAll(SectionPropertyTypes.StaffType);
        var campuses        = _propRepo.GetAll(SectionPropertyTypes.Campus);
        var tags            = _propRepo.GetAll(SectionPropertyTypes.Tag);
        var resources       = _propRepo.GetAll(SectionPropertyTypes.Resource);
        var reserves        = _propRepo.GetAll(SectionPropertyTypes.Reserve);
        var prefixes        = _prefixRepo.GetAll();
        var blockPatterns   = _blockPatternRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll(academicYearId);

        // Build the instructor name substitution table before writing any files.
        var nameMap = BuildNameMap(instructors);

        progress?.Report($"Writing files for {ay.Name} ({sections.Count} sections, {instructors.Count} instructors)...");

        WriteAcademicYear(outputFolder, ay);
        WriteSemesters(outputFolder, semesters);
        WriteInstructors(outputFolder, instructors, nameMap);
        WriteRooms(outputFolder, rooms);
        WriteSubjects(outputFolder, subjects);
        WriteCourses(outputFolder, courses);
        WriteSectionProperties(outputFolder, sectionTypes, meetingTypes, staffTypes, campuses, tags, resources, reserves);
        WriteSectionPrefixes(outputFolder, prefixes);
        WriteBlockPatterns(outputFolder, blockPatterns);
        WriteLegalStartTimes(outputFolder, legalStartTimes);
        WriteSections(outputFolder, sections);

        progress?.Report(
            $"Done. {sections.Count} sections · {instructors.Count} instructors · " +
            $"{courses.Count} courses · {subjects.Count} subjects · {rooms.Count} rooms.");
    }

    // ── Name anonymisation ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a deterministic mapping from each instructor's real ID to a fake
    /// (FirstName, LastName, Initials) triple. Instructors are sorted by ID before
    /// assignment so re-runs against the same database always produce the same map.
    /// </summary>
    /// <param name="instructors">All instructors loaded from the database.</param>
    /// <returns>
    ///   Dictionary keyed by instructor ID, value is (FirstName, LastName, Initials).
    /// </returns>
    private static Dictionary<string, (string First, string Last, string Initials)> BuildNameMap(
        IReadOnlyList<Instructor> instructors)
    {
        var map = new Dictionary<string, (string, string, string)>();
        int index = 0;

        // Sort by ID for stability across re-runs.
        foreach (var instructor in instructors.OrderBy(i => i.Id))
        {
            var first    = FirstNames[index % FirstNames.Length];
            var last     = LastNames[index % LastNames.Length];
            var initials = $"{first[0]}{last[0]}";
            map[instructor.Id] = (first, last, initials);
            index++;
        }

        return map;
    }

    // ── File writers ─────────────────────────────────────────────────────────

    private static void WriteAcademicYear(string folder, AcademicYear ay)
    {
        var sb = StartFile("The single academic year included in the demo.");
        sb.AppendLine("    public static readonly AcademicYear AcademicYear = new()");
        sb.AppendLine("    {");
        sb.AppendLine($"        Id   = {Str(ay.Id)},");
        sb.AppendLine($"        Name = {Str(ay.Name)},");
        sb.AppendLine("    };");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.AcademicYear.cs"), sb.ToString());
    }

    private static void WriteSemesters(string folder, IReadOnlyList<Semester> semesters)
    {
        var sb = StartFile("All semesters in the demo academic year.");
        sb.AppendLine("    public static readonly IReadOnlyList<Semester> Semesters =");
        sb.AppendLine("    [");
        foreach (var s in semesters)
        {
            sb.AppendLine($"        new Semester {{ Id = {Str(s.Id)}, AcademicYearId = {Str(s.AcademicYearId)}, Name = {Str(s.Name)}, SortOrder = {s.SortOrder} }},");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Semesters.cs"), sb.ToString());
    }

    private static void WriteInstructors(
        string folder,
        IReadOnlyList<Instructor> instructors,
        Dictionary<string, (string First, string Last, string Initials)> nameMap)
    {
        var sb = StartFile(
            "All instructors. Names have been anonymised — real names are not stored here.\n" +
            "    /// IDs are preserved so all section references remain valid.");
        sb.AppendLine("    public static readonly IReadOnlyList<Instructor> Instructors =");
        sb.AppendLine("    [");
        foreach (var instr in instructors.OrderBy(i => i.LastName).ThenBy(i => i.FirstName))
        {
            var (first, last, initials) = nameMap[instr.Id];
            sb.AppendLine("        new Instructor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id          = {Str(instr.Id)},");
            sb.AppendLine($"            FirstName   = {Str(first)},");
            sb.AppendLine($"            LastName    = {Str(last)},");
            sb.AppendLine($"            Initials    = {Str(initials)},");
            sb.AppendLine($"            Email       = \"\",");
            sb.AppendLine($"            Notes       = \"\",");
            sb.AppendLine($"            StaffTypeId = {Str(instr.StaffTypeId)},");
            sb.AppendLine($"            IsActive    = {Bool(instr.IsActive)},");
            sb.AppendLine("        },");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Instructors.cs"), sb.ToString());
    }

    private static void WriteRooms(string folder, IReadOnlyList<Room> rooms)
    {
        var sb = StartFile("All rooms referenced by sections in the demo academic year.");
        sb.AppendLine("    public static readonly IReadOnlyList<Room> Rooms =");
        sb.AppendLine("    [");
        foreach (var r in rooms.OrderBy(r => r.SortOrder))
        {
            sb.AppendLine("        new Room");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id         = {Str(r.Id)},");
            sb.AppendLine($"            Building   = {Str(r.Building)},");
            sb.AppendLine($"            RoomNumber = {Str(r.RoomNumber)},");
            sb.AppendLine($"            Capacity   = {r.Capacity},");
            sb.AppendLine($"            Features   = {Str(r.Features)},");
            sb.AppendLine($"            Notes      = {Str(r.Notes)},");
            sb.AppendLine($"            SortOrder  = {r.SortOrder},");
            sb.AppendLine("        },");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Rooms.cs"), sb.ToString());
    }

    private static void WriteSubjects(string folder, IReadOnlyList<Subject> subjects)
    {
        var sb = StartFile("All subjects (academic departments / disciplines) in the demo data.");
        sb.AppendLine("    public static readonly IReadOnlyList<Subject> Subjects =");
        sb.AppendLine("    [");
        foreach (var s in subjects.OrderBy(s => s.Name))
        {
            sb.AppendLine($"        new Subject {{ Id = {Str(s.Id)}, Name = {Str(s.Name)}, CalendarAbbreviation = {Str(s.CalendarAbbreviation)} }},");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Subjects.cs"), sb.ToString());
    }

    private static void WriteCourses(string folder, IReadOnlyList<Course> courses)
    {
        var sb = StartFile("All courses offered in the demo academic year.");
        sb.AppendLine("    public static readonly IReadOnlyList<Course> Courses =");
        sb.AppendLine("    [");
        foreach (var c in courses.OrderBy(c => c.CalendarCode))
        {
            sb.AppendLine("        new Course");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id           = {Str(c.Id)},");
            sb.AppendLine($"            SubjectId    = {Str(c.SubjectId)},");
            sb.AppendLine($"            CalendarCode = {Str(c.CalendarCode)},");
            sb.AppendLine($"            Title        = {Str(c.Title)},");
            sb.AppendLine($"            Level        = {Str(c.Level)},");
            sb.AppendLine($"            IsActive     = {Bool(c.IsActive)},");
            sb.AppendLine($"            TagIds       = {StringList(c.TagIds)},");
            sb.AppendLine("        },");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Courses.cs"), sb.ToString());
    }

    private static void WriteSectionProperties(
        string folder,
        IReadOnlyList<SectionPropertyValue> sectionTypes,
        IReadOnlyList<SectionPropertyValue> meetingTypes,
        IReadOnlyList<SectionPropertyValue> staffTypes,
        IReadOnlyList<SectionPropertyValue> campuses,
        IReadOnlyList<SectionPropertyValue> tags,
        IReadOnlyList<SectionPropertyValue> resources,
        IReadOnlyList<SectionPropertyValue> reserves)
    {
        var sb = StartFile(null);

        void WriteGroup(string fieldName, string summary, IReadOnlyList<SectionPropertyValue> values)
        {
            sb.AppendLine($"    /// <summary>{summary}</summary>");
            sb.AppendLine($"    public static readonly IReadOnlyList<SectionPropertyValue> {fieldName} =");
            sb.AppendLine("    [");
            foreach (var v in values.OrderBy(v => v.SortOrder).ThenBy(v => v.Name))
            {
                sb.AppendLine($"        new SectionPropertyValue {{ Id = {Str(v.Id)}, Name = {Str(v.Name)}, SectionCodeAbbreviation = {Str(v.SectionCodeAbbreviation)}, SortOrder = {v.SortOrder} }},");
            }
            sb.AppendLine("    ];");
            sb.AppendLine();
        }

        WriteGroup("SectionTypes",  "Section type property values (e.g. Lecture, Lab, Tutorial).", sectionTypes);
        WriteGroup("MeetingTypes",  "Meeting type property values (e.g. In-Person, Online, Hybrid).", meetingTypes);
        WriteGroup("StaffTypes",    "Staff type property values (e.g. Full-Time, Sessional).", staffTypes);
        WriteGroup("Campuses",      "Campus property values.", campuses);
        WriteGroup("Tags",          "Tag property values.", tags);
        WriteGroup("Resources",     "Resource property values.", resources);
        WriteGroup("Reserves",      "Reserve property values.", reserves);

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// All section property values across all types, combined into a single list.");
        sb.AppendLine("    /// Used by repositories that need to resolve any property value by ID.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IReadOnlyList<SectionPropertyValue> AllSectionProperties =>");
        sb.AppendLine("        [.. SectionTypes, .. MeetingTypes, .. StaffTypes, .. Campuses, .. Tags, .. Resources, .. Reserves];");

        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.SectionProperties.cs"), sb.ToString());
    }

    private static void WriteSectionPrefixes(string folder, IReadOnlyList<SectionPrefix> prefixes)
    {
        var sb = StartFile("Section code prefixes used to auto-generate section codes.");
        sb.AppendLine("    public static readonly IReadOnlyList<SectionPrefix> SectionPrefixes =");
        sb.AppendLine("    [");
        foreach (var p in prefixes.OrderBy(p => p.Prefix))
        {
            sb.AppendLine($"        new SectionPrefix {{ Id = {Str(p.Id)}, Prefix = {Str(p.Prefix)}, CampusId = {Str(p.CampusId)} }},");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.SectionPrefixes.cs"), sb.ToString());
    }

    private static void WriteBlockPatterns(string folder, IReadOnlyList<BlockPattern> patterns)
    {
        var sb = StartFile("Named day patterns used in the section schedule editor (e.g. MWF, TTh).");
        sb.AppendLine("    public static readonly IReadOnlyList<BlockPattern> BlockPatterns =");
        sb.AppendLine("    [");
        foreach (var p in patterns.OrderBy(p => p.Name))
        {
            sb.AppendLine($"        new BlockPattern {{ Id = {Str(p.Id)}, Name = {Str(p.Name)}, Days = [{string.Join(", ", p.Days)}] }},");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.BlockPatterns.cs"), sb.ToString());
    }

    private static void WriteLegalStartTimes(string folder, IReadOnlyList<LegalStartTime> entries)
    {
        var sb = StartFile(
            "Legal section start times for the demo academic year, keyed by block length in hours.\n" +
            "    /// Used by the section editor to populate the start-time picker.");
        sb.AppendLine("    public static readonly IReadOnlyList<LegalStartTime> LegalStartTimes =");
        sb.AppendLine("    [");
        foreach (var e in entries.OrderBy(e => e.BlockLength))
        {
            var times = string.Join(", ", e.StartTimes.OrderBy(t => t));
            sb.AppendLine($"        new LegalStartTime {{ BlockLength = {e.BlockLength}, StartTimes = [{times}] }},");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.LegalStartTimes.cs"), sb.ToString());
    }

    private static void WriteSections(string folder, IReadOnlyList<Section> sections)
    {
        var sb = StartFile("All sections from all semesters in the demo academic year.");
        sb.AppendLine("    public static readonly IReadOnlyList<Section> Sections =");
        sb.AppendLine("    [");
        foreach (var s in sections.OrderBy(s => s.SemesterId).ThenBy(s => s.SectionCode))
        {
            sb.AppendLine("        new Section");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id          = {Str(s.Id)},");
            sb.AppendLine($"            SemesterId  = {Str(s.SemesterId)},");
            sb.AppendLine($"            CourseId    = {Str(s.CourseId)},");
            sb.AppendLine($"            SectionCode = {Str(s.SectionCode)},");
            sb.AppendLine($"            Level       = {Str(s.Level)},");
            sb.AppendLine($"            Notes       = {Str(s.Notes)},");
            sb.AppendLine($"            SectionTypeId = {Str(s.SectionTypeId)},");
            sb.AppendLine($"            CampusId    = {Str(s.CampusId)},");
            sb.AppendLine($"            TagIds      = {StringList(s.TagIds)},");
            sb.AppendLine($"            ResourceIds = {StringList(s.ResourceIds)},");
            WriteReserves(sb, s.Reserves);
            WriteInstructorAssignments(sb, s.InstructorAssignments);
            WriteSchedule(sb, s.Schedule);
            sb.AppendLine("        },");
        }
        sb.AppendLine("    ];");
        EndFile(sb);
        File.WriteAllText(Path.Combine(folder, "DemoData.Sections.cs"), sb.ToString());
    }

    private static void WriteReserves(StringBuilder sb, List<SectionReserve> reserves)
    {
        if (reserves.Count == 0)
        {
            sb.AppendLine("            Reserves    = [],");
            return;
        }
        sb.AppendLine("            Reserves    =");
        sb.AppendLine("            [");
        foreach (var r in reserves)
            sb.AppendLine($"                new SectionReserve {{ ReserveId = {Str(r.ReserveId)}, Code = {r.Code} }},");
        sb.AppendLine("            ],");
    }

    private static void WriteInstructorAssignments(StringBuilder sb, List<InstructorAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            sb.AppendLine("            InstructorAssignments = [],");
            return;
        }
        sb.AppendLine("            InstructorAssignments =");
        sb.AppendLine("            [");
        foreach (var a in assignments)
            sb.AppendLine($"                new InstructorAssignment {{ InstructorId = {Str(a.InstructorId)}, Workload = {Dec(a.Workload)} }},");
        sb.AppendLine("            ],");
    }

    private static void WriteSchedule(StringBuilder sb, List<SectionDaySchedule> schedule)
    {
        if (schedule.Count == 0)
        {
            sb.AppendLine("            Schedule    = [],");
            return;
        }
        sb.AppendLine("            Schedule    =");
        sb.AppendLine("            [");
        foreach (var day in schedule.OrderBy(d => d.Day).ThenBy(d => d.StartMinutes))
        {
            sb.AppendLine("                new SectionDaySchedule");
            sb.AppendLine("                {");
            sb.AppendLine($"                    Day             = {day.Day},");
            sb.AppendLine($"                    StartMinutes    = {day.StartMinutes},");
            sb.AppendLine($"                    DurationMinutes = {day.DurationMinutes},");
            sb.AppendLine($"                    RoomId          = {Str(day.RoomId)},");
            sb.AppendLine($"                    MeetingTypeId   = {Str(day.MeetingTypeId)},");
            sb.AppendLine($"                    Frequency       = {Str(day.Frequency)},");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
    }

    // ── File scaffolding helpers ──────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="StringBuilder"/> pre-populated with the standard file header
    /// and the opening lines of the partial class.
    /// </summary>
    /// <param name="summary">
    ///   XML doc-comment summary for the first member in the file, or <c>null</c> to omit.
    /// </param>
    private static StringBuilder StartFile(string? summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//   This file was generated by DemoDataGenerator.");
        sb.AppendLine("//   To refresh: open the source database in debug mode → Debug flyout → Generate Demo Data.");
        sb.AppendLine("//   DO NOT edit this file manually — changes will be overwritten on the next generation.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("using SchedulingAssistant.Models;");
        sb.AppendLine();
        sb.AppendLine("namespace SchedulingAssistant.Demo;");
        sb.AppendLine();
        sb.AppendLine("public static partial class DemoData");
        sb.AppendLine("{");
        if (summary is not null)
        {
            sb.AppendLine($"    /// <summary>{summary}</summary>");
        }
        return sb;
    }

    /// <summary>Appends the closing brace of the partial class to <paramref name="sb"/>.</summary>
    private static void EndFile(StringBuilder sb) => sb.AppendLine("}");

    // ── Value emit helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Emits a C# string literal for <paramref name="s"/>, or <c>null</c> when the
    /// value is <c>null</c>. Special characters are escaped.
    /// </summary>
    private static string Str(string? s) =>
        s is null ? "null" : $"\"{Escape(s)}\"";

    /// <summary>
    /// Escapes backslashes, double-quotes, and common control characters so the
    /// emitted string literal is valid C#.
    /// </summary>
    private static string Escape(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\r",  "\\r")
         .Replace("\n",  "\\n")
         .Replace("\t",  "\\t");

    /// <summary>Emits <c>true</c> or <c>false</c>.</summary>
    private static string Bool(bool b) => b ? "true" : "false";

    /// <summary>Emits a nullable decimal literal, e.g. <c>1.5m</c> or <c>null</c>.</summary>
    private static string Dec(decimal? d) => d.HasValue ? $"{d.Value}m" : "null";

    /// <summary>
    /// Emits an inline C# collection expression from a list of string IDs,
    /// e.g. <c>["id1", "id2"]</c> or <c>[]</c> when empty.
    /// </summary>
    private static string StringList(IEnumerable<string> ids)
    {
        var items = ids.Select(id => $"\"{id}\"").ToList();
        return items.Count == 0 ? "[]" : $"[{string.Join(", ", items)}]";
    }
}
#endif
