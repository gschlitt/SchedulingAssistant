using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace SchedulingAssistant.DemoExporter;

/// <summary>
/// Reads a TermPoint SQLite database (typically already anonymized) and generates the
/// DemoData.*.cs partial-class files consumed by the WASM browser demo.
///
/// Usage: dotnet run -- &lt;path-to.db&gt; [--output &lt;dir&gt;] [--academic-year &lt;name&gt;]
/// </summary>
public static class Program
{
    // ── Entry point ─────────────────────────────────────────────────────────

    public static int Main(string[] args)
    {
        if (args.Length < 1 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Usage: dotnet run -- <path-to.db> [--output <dir>] [--academic-year <name>]");
            Console.WriteLine();
            Console.WriteLine("Reads a TermPoint SQLite database and generates DemoData.*.cs files.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --output <dir>          Output directory (default: ../SchedulingAssistant/Demo/)");
            Console.WriteLine("  --academic-year <name>  Academic year to export (default: most recent)");
            return 1;
        }

        var dbPath = args[0];
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SchedulingAssistant", "Demo");
        string? requestedAy = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--academic-year" when i + 1 < args.Length:
                    requestedAy = args[++i];
                    break;
            }
        }

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        outputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        // ── Select academic year ────────────────────────────────────────────

        var allAys = ReadAll(conn, "SELECT id, name, data FROM AcademicYears", 3);
        if (allAys.Count == 0)
        {
            Console.Error.WriteLine("No academic years found in database.");
            return 1;
        }

        string[] selectedAy;
        if (requestedAy != null)
        {
            selectedAy = allAys.FirstOrDefault(r =>
                r[1].Equals(requestedAy, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Academic year '{requestedAy}' not found. Available: {string.Join(", ", allAys.Select(r => r[1]))}");
        }
        else
        {
            selectedAy = allAys.OrderByDescending(r => ExtractStartYear(r[1])).First();
        }

        var ayId = selectedAy[0];
        var ayName = selectedAy[1];
        Console.WriteLine($"Selected academic year: {ayName} (id: {ayId})");

        // ── Read all entity data ────────────────────────────────────────────

        var semesters = ReadAll(conn,
            $"SELECT id, academic_year_id, name, sort_order, data FROM Semesters WHERE academic_year_id = $ay",
            5, ("$ay", ayId));
        var semesterIds = semesters.Select(r => r[0]).ToHashSet();
        var semesterIdList = string.Join(",", semesterIds.Select(id => $"'{id}'"));

        var campuses = ReadAll(conn, "SELECT id, name, data FROM Campuses", 3);
        var subjects = ReadAll(conn, "SELECT id, name, abbreviation, data FROM Subjects", 4);
        var envValues = ReadAll(conn, "SELECT id, type, name, data FROM SchedulingEnvironmentValues", 4);
        var blockPatterns = ReadAll(conn, "SELECT id, name, data FROM BlockPatterns", 3);
        var legalStartTimes = ReadAll(conn,
            "SELECT block_length, start_times FROM LegalStartTimes WHERE academic_year_id = $ay",
            2, ("$ay", ayId));
        var instructors = ReadAll(conn, "SELECT id, last_name, first_name, initials, data FROM Instructors", 5);
        var rooms = ReadAll(conn, "SELECT id, building, room_number, data FROM Rooms", 4);
        var courses = ReadAll(conn, "SELECT id, subject_id, calendar_code, title, data FROM Courses", 5);

        var sections = semesterIds.Count > 0
            ? ReadAll(conn, $"SELECT id, semester_id, course_id, section_code, data FROM Sections WHERE semester_id IN ({semesterIdList})", 5)
            : [];
        var meetings = semesterIds.Count > 0 && TableExists(conn, "Meetings")
            ? ReadAll(conn, $"SELECT id, semester_id, data FROM Meetings WHERE semester_id IN ({semesterIdList})", 3)
            : [];
        var commitments = semesterIds.Count > 0 && TableExists(conn, "InstructorCommitments")
            ? ReadAll(conn, $"SELECT id, instructor_id, semester_id, data FROM InstructorCommitments WHERE semester_id IN ({semesterIdList})", 4)
            : [];
        var releases = semesterIds.Count > 0 && TableExists(conn, "Releases")
            ? ReadAll(conn, $"SELECT id, semester_id, data FROM Releases WHERE semester_id IN ({semesterIdList})", 3)
            : [];
        var sectionCodePatterns = TableExists(conn, "SectionCodePatterns")
            ? ReadAll(conn, "SELECT id, name, sort_order, data FROM SectionCodePatterns", 4)
            : [];

        // ── Partition scheduling environment values by type ─────────────────

        var sectionTypes = envValues.Where(r => r[1] == "sectionType").ToList();
        var meetingTypes = envValues.Where(r => r[1] == "meetingType").ToList();
        var staffTypes   = envValues.Where(r => r[1] == "staffType").ToList();
        var tags         = envValues.Where(r => r[1] == "tag").ToList();
        var resources    = envValues.Where(r => r[1] == "resource").ToList();
        var reserves     = envValues.Where(r => r[1] == "reserve").ToList();
        var roomTypes    = envValues.Where(r => r[1] == "roomType").ToList();

        // ── Print summary ───────────────────────────────────────────────────

        Console.WriteLine($"  {semesters.Count} semesters, {subjects.Count} subjects, {courses.Count} courses");
        Console.WriteLine($"  {instructors.Count} instructors, {rooms.Count} rooms, {campuses.Count} campuses");
        Console.WriteLine($"  {blockPatterns.Count} block patterns, {legalStartTimes.Count} legal start time rows");
        Console.WriteLine($"  {sectionTypes.Count} section types, {meetingTypes.Count} meeting types, {staffTypes.Count} staff types");
        Console.WriteLine($"  {tags.Count} tags, {resources.Count} resources, {reserves.Count} reserves, {roomTypes.Count} room types");
        Console.WriteLine($"  {sections.Count} sections, {meetings.Count} meetings");
        Console.WriteLine($"  {commitments.Count} commitments, {releases.Count} releases, {sectionCodePatterns.Count} code patterns");
        Console.WriteLine();

        // ── Build ID remap dictionaries ─────────────────────────────────────

        var ayMap = new Dictionary<string, string> { [ayId] = "demo-ay-1" };

        var semMap = BuildIdMap(
            semesters.OrderBy(r => ParseInt(r[3])).ThenBy(r => r[2]),
            "demo-sem");
        var campusMap = BuildIdMap(
            campuses.OrderBy(r => GetSortOrder(r[2])).ThenBy(r => r[1]),
            "demo-campus");
        var subjectMap = BuildIdMap(
            subjects.OrderBy(r => r[1]),
            "demo-subj");
        var courseMap = BuildIdMap(
            courses.OrderBy(r => r[2]),
            "demo-course");
        var instructorMap = BuildIdMap(
            instructors.OrderBy(r => r[1]).ThenBy(r => r[2]),
            "demo-inst");
        var roomMap = BuildIdMap(
            rooms.OrderBy(r => GetSortOrder(r[3])).ThenBy(r => r[1]).ThenBy(r => r[2]),
            "demo-room");
        var bpMap = BuildIdMap(
            blockPatterns.OrderBy(r => r[1]),
            "demo-bp");

        var stMap = BuildIdMap(sectionTypes.OrderBy(r => GetSortOrder(r[3])), "demo-st");
        var mtMap = BuildIdMap(meetingTypes.OrderBy(r => GetSortOrder(r[3])), "demo-mt");
        var staffMap = BuildIdMap(staffTypes.OrderBy(r => GetSortOrder(r[3])), "demo-staff");
        var tagMap = BuildIdMap(tags.OrderBy(r => GetSortOrder(r[3])), "demo-tag");
        var resMap = BuildIdMap(resources.OrderBy(r => GetSortOrder(r[3])), "demo-res");
        var reserveMap = BuildIdMap(reserves.OrderBy(r => GetSortOrder(r[3])), "demo-reserve");
        var roomTypeMap = BuildIdMap(roomTypes.OrderBy(r => GetSortOrder(r[3])), "demo-rt");

        var sectionMap = BuildIdMap(
            sections.OrderBy(r => r[1]).ThenBy(r => r[2]).ThenBy(r => r[3]),
            "demo-sec");
        var meetingMap = BuildIdMap(
            meetings.OrderBy(r => r[1]).ThenBy(r => Json(r[2])["title"]?.GetValue<string>() ?? ""),
            "demo-meeting");
        var commitMap = BuildIdMap(
            commitments.OrderBy(r => r[1]).ThenBy(r => GetJsonInt(r[3], "day")).ThenBy(r => GetJsonInt(r[3], "startMinutes")),
            "demo-commit");
        var releaseMap = BuildIdMap(
            releases.OrderBy(r => r[1]).ThenBy(r => Json(r[2])["instructorId"]?.GetValue<string>() ?? ""),
            "demo-release");
        var scpMap = BuildIdMap(
            sectionCodePatterns.OrderBy(r => ParseInt(r[2])).ThenBy(r => r[1]),
            "demo-scp");

        // Merge all env value maps for FK lookups
        var allEnvMap = new Dictionary<string, string>();
        foreach (var m in new[] { stMap, mtMap, staffMap, tagMap, resMap, reserveMap, roomTypeMap })
            foreach (var kv in m) allEnvMap[kv.Key] = kv.Value;

        // ── Generate and write files ────────────────────────────────────────

        var files = new Dictionary<string, string>
        {
            ["DemoData.AcademicYear.cs"] = GenAcademicYear(ayId, ayName, ayMap),
            ["DemoData.Semesters.cs"] = GenSemesters(semesters, semMap, ayMap),
            ["DemoData.Campuses.cs"] = GenCampuses(campuses, campusMap),
            ["DemoData.Subjects.cs"] = GenSubjects(subjects, subjectMap),
            ["DemoData.Courses.cs"] = GenCourses(courses, courseMap, subjectMap, tagMap),
            ["DemoData.Instructors.cs"] = GenInstructors(instructors, instructorMap, staffMap),
            ["DemoData.Rooms.cs"] = GenRooms(rooms, roomMap, campusMap, roomTypeMap),
            ["DemoData.BlockPatterns.cs"] = GenBlockPatterns(blockPatterns, bpMap),
            ["DemoData.LegalStartTimes.cs"] = GenLegalStartTimes(legalStartTimes),
            ["DemoData.SchedulingEnvironment.cs"] = GenSchedulingEnvironment(
                sectionTypes, meetingTypes, staffTypes, tags, resources, reserves, roomTypes,
                stMap, mtMap, staffMap, tagMap, resMap, reserveMap, roomTypeMap),
            ["DemoData.Sections.cs"] = GenSections(sections, sectionMap, semMap, courseMap, allEnvMap, campusMap, roomMap, instructorMap, tagMap, resMap, reserveMap),
            ["DemoData.Meetings.cs"] = GenMeetings(meetings, meetingMap, semMap, campusMap, roomMap, instructorMap, allEnvMap, tagMap, resMap),
            ["DemoData.InstructorCommitments.cs"] = GenCommitments(commitments, commitMap, instructorMap, semMap),
            ["DemoData.Releases.cs"] = GenReleases(releases, releaseMap, semMap, instructorMap),
            ["DemoData.SectionCodePatterns.cs"] = GenSectionCodePatterns(sectionCodePatterns, scpMap, campusMap, allEnvMap),
        };

        foreach (var (fileName, content) in files)
        {
            var path = Path.Combine(outputDir, fileName);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            Console.WriteLine($"  {fileName}");
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {files.Count} files written to {outputDir}");
        return 0;
    }

    // ── Code generators ─────────────────────────────────────────────────────

    /// <summary>Generates DemoData.AcademicYear.cs — singular entity.</summary>
    private static string GenAcademicYear(string id, string name, Dictionary<string, string> map)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly AcademicYear AcademicYear = new()");
        sb.AppendLine("    {");
        sb.AppendLine($"        Id   = \"{map[id]}\",");
        sb.AppendLine($"        Name = \"{Esc(name)}\"");
        sb.AppendLine("    };");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Semesters.cs.</summary>
    private static string GenSemesters(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> ayMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Semester> Semesters =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => ParseInt(r[3])).ThenBy(r => r[2]))
        {
            var data = Json(r[4]);
            var color = data["color"]?.GetValue<string>() ?? "";

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 14);
            Prop(sb, "AcademicYearId", Q(Remap(ayMap, r[1])), 14);
            Prop(sb, "Name", Q(Esc(r[2])), 14);
            Prop(sb, "SortOrder", ParseInt(r[3]).ToString(), 14);
            if (!string.IsNullOrEmpty(color))
                Prop(sb, "Color", Q(Esc(color)), 14);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Campuses.cs.</summary>
    private static string GenCampuses(List<string[]> rows, Dictionary<string, string> map)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Campus> Campuses =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => GetSortOrder(r[2])).ThenBy(r => r[1]))
        {
            var data = Json(r[2]);
            var abbrev = data["abbreviation"]?.GetValue<string>() ?? "";
            var sort = data["sortOrder"]?.GetValue<int>() ?? 0;

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 12);
            Prop(sb, "Name", Q(Esc(r[1])), 12);
            if (!string.IsNullOrEmpty(abbrev))
                Prop(sb, "Abbreviation", Q(Esc(abbrev)), 12);
            if (sort != 0)
                Prop(sb, "SortOrder", sort.ToString(), 12);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Subjects.cs.</summary>
    private static string GenSubjects(List<string[]> rows, Dictionary<string, string> map)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Subject> Subjects =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]))
        {
            var data = Json(r[3]);
            var calAbbrev = data["calendarAbbreviation"]?.GetValue<string>() ?? r[2];

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 20);
            Prop(sb, "Name", Q(Esc(r[1])), 20);
            Prop(sb, "CalendarAbbreviation", Q(Esc(calAbbrev)), 20);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Courses.cs.</summary>
    private static string GenCourses(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> subjectMap, Dictionary<string, string> tagMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Course> Courses =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[2]))
        {
            var data = Json(r[4]);
            var isActive = data["isActive"]?.GetValue<bool>() ?? true;
            var tagIds = GetStringArray(data, "tagIds");
            var level = data["level"]?.GetValue<string>() ?? "";

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 12);
            Prop(sb, "SubjectId", Q(Remap(subjectMap, r[1])), 12);
            Prop(sb, "CalendarCode", Q(Esc(r[2])), 12);
            Prop(sb, "Title", Q(Esc(r[3])), 12);
            Prop(sb, "IsActive", isActive ? "true" : "false", 12);
            if (!string.IsNullOrEmpty(level))
                Prop(sb, "Level", Q(Esc(level)), 12);
            if (tagIds.Count > 0)
                Prop(sb, "TagIds", RemapList(tagIds, tagMap), 12);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Instructors.cs.</summary>
    private static string GenInstructors(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> staffMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Instructor> Instructors =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]).ThenBy(r => r[2]))
        {
            var data = Json(r[4]);
            var isActive = data["isActive"]?.GetValue<bool>() ?? true;
            var staffTypeId = data["staffTypeId"]?.GetValue<string>();
            var email = data["email"]?.GetValue<string>() ?? "";

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 11);
            Prop(sb, "FirstName", Q(Esc(r[2])), 11);
            Prop(sb, "LastName", Q(Esc(r[1])), 11);
            Prop(sb, "Initials", Q(Esc(r[3])), 11);
            Prop(sb, "IsActive", isActive ? "true" : "false", 11);
            if (!string.IsNullOrEmpty(email))
                Prop(sb, "Email", Q(Esc(email)), 11);
            if (!string.IsNullOrEmpty(staffTypeId))
                Prop(sb, "StaffTypeId", Q(Remap(staffMap, staffTypeId)), 11);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Rooms.cs.</summary>
    private static string GenRooms(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> campusMap, Dictionary<string, string> roomTypeMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Room> Rooms =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => GetSortOrder(r[3])).ThenBy(r => r[1]).ThenBy(r => r[2]))
        {
            var data = Json(r[3]);
            var campusId = data["campusId"]?.GetValue<string>();
            var roomTypeId = data["roomTypeId"]?.GetValue<string>();
            var capacity = data["capacity"]?.GetValue<int>() ?? 0;
            var features = data["features"]?.GetValue<string>() ?? "";
            var notes = data["notes"]?.GetValue<string>() ?? "";
            var sortOrder = data["sortOrder"]?.GetValue<int>() ?? 0;

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 11);
            Prop(sb, "Building", Q(Esc(r[1])), 11);
            Prop(sb, "RoomNumber", Q(Esc(r[2])), 11);
            if (capacity > 0)
                Prop(sb, "Capacity", capacity.ToString(), 11);
            if (!string.IsNullOrEmpty(features))
                Prop(sb, "Features", Q(Esc(features)), 11);
            if (!string.IsNullOrEmpty(notes))
                Prop(sb, "Notes", Q(Esc(notes)), 11);
            if (!string.IsNullOrEmpty(campusId))
                Prop(sb, "CampusId", Q(Remap(campusMap, campusId)), 11);
            if (!string.IsNullOrEmpty(roomTypeId))
                Prop(sb, "RoomTypeId", Q(Remap(roomTypeMap, roomTypeId)), 11);
            if (sortOrder != 0)
                Prop(sb, "SortOrder", sortOrder.ToString(), 11);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.BlockPatterns.cs.</summary>
    private static string GenBlockPatterns(List<string[]> rows, Dictionary<string, string> map)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<BlockPattern> BlockPatterns =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]))
        {
            var data = Json(r[2]);
            var days = GetIntArray(data, "days");

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 4);
            Prop(sb, "Name", Q(Esc(r[1])), 4);
            Prop(sb, "Days", $"[{string.Join(", ", days)}]", 4);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.LegalStartTimes.cs.</summary>
    private static string GenLegalStartTimes(List<string[]> rows)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<LegalStartTime> LegalStartTimes =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => double.Parse(r[0])))
        {
            var blockLength = double.Parse(r[0]);
            var startTimes = JsonNode.Parse(r[1])!.AsArray().Select(n => n!.GetValue<int>()).ToList();

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "BlockLength", FormatDouble(blockLength), 11);
            Prop(sb, "StartTimes", $"[{string.Join(", ", startTimes)}]", 11);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.SchedulingEnvironment.cs with six lists.</summary>
    private static string GenSchedulingEnvironment(
        List<string[]> sectionTypes, List<string[]> meetingTypes, List<string[]> staffTypes,
        List<string[]> tags, List<string[]> resources, List<string[]> reserves, List<string[]> roomTypes,
        Dictionary<string, string> stMap, Dictionary<string, string> mtMap,
        Dictionary<string, string> staffMap, Dictionary<string, string> tagMap,
        Dictionary<string, string> resMap, Dictionary<string, string> reserveMap,
        Dictionary<string, string> roomTypeMap)
    {
        var sb = Header();

        WriteEnvList(sb, "SectionTypes", sectionTypes, stMap);
        sb.AppendLine();
        WriteEnvList(sb, "MeetingTypes", meetingTypes, mtMap);
        sb.AppendLine();
        WriteEnvList(sb, "StaffTypes", staffTypes, staffMap);
        sb.AppendLine();
        WriteEnvList(sb, "Tags", tags, tagMap);
        WriteEnvList(sb, "Resources", resources, resMap);
        WriteEnvList(sb, "Reserves", reserves, reserveMap);
        WriteEnvList(sb, "RoomTypes", roomTypes, roomTypeMap);

        return Footer(sb);
    }

    /// <summary>Writes a single scheduling environment value list.</summary>
    private static void WriteEnvList(StringBuilder sb, string name,
        List<string[]> rows, Dictionary<string, string> map)
    {
        if (rows.Count == 0)
        {
            sb.AppendLine($"    public static readonly List<SchedulingEnvironmentValue> {name} = [];");
            return;
        }

        sb.AppendLine($"    public static readonly List<SchedulingEnvironmentValue> {name} =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => GetSortOrder(r[3])).ThenBy(r => r[2]))
        {
            var data = Json(r[3]);
            var sortOrder = data["sortOrder"]?.GetValue<int>() ?? 0;

            sb.Append($"        new() {{ Id = {Q(map[r[0]])}, ");
            sb.Append($"Name = {Q(Esc(r[2]))}, ");
            sb.Append($"SortOrder = {sortOrder}");
            sb.AppendLine(" },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
    }

    /// <summary>Generates DemoData.Sections.cs with nested Schedule and InstructorAssignments.</summary>
    private static string GenSections(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> semMap, Dictionary<string, string> courseMap,
        Dictionary<string, string> envMap, Dictionary<string, string> campusMap,
        Dictionary<string, string> roomMap, Dictionary<string, string> instMap,
        Dictionary<string, string> tagMap, Dictionary<string, string> resMap,
        Dictionary<string, string> reserveMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Section> Sections =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]).ThenBy(r => r[2]).ThenBy(r => r[3]))
        {
            var data = Json(r[4]);
            var sectionTypeId = data["sectionTypeId"]?.GetValue<string>();
            var campusId = data["campusId"]?.GetValue<string>();
            var tagIds = GetStringArray(data, "tagIds");
            var resourceIds = GetStringArray(data, "resourceIds");

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 13);
            Prop(sb, "SemesterId", Q(Remap(semMap, r[1])), 13);
            Prop(sb, "CourseId", Q(Remap(courseMap, r[2])), 13);
            Prop(sb, "SectionCode", Q(Esc(r[3])), 13);
            if (!string.IsNullOrEmpty(sectionTypeId))
                Prop(sb, "SectionTypeId", Q(Remap(envMap, sectionTypeId)), 13);
            if (!string.IsNullOrEmpty(campusId))
                Prop(sb, "CampusId", Q(Remap(campusMap, campusId)), 13);
            if (tagIds.Count > 0)
                Prop(sb, "TagIds", RemapList(tagIds, tagMap), 13);
            if (resourceIds.Count > 0)
                Prop(sb, "ResourceIds", RemapList(resourceIds, resMap), 13);

            WriteSchedule(sb, data, roomMap, envMap);
            WriteInstructorAssignments(sb, data, instMap);

            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Meetings.cs.</summary>
    private static string GenMeetings(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> semMap, Dictionary<string, string> campusMap,
        Dictionary<string, string> roomMap, Dictionary<string, string> instMap,
        Dictionary<string, string> envMap, Dictionary<string, string> tagMap,
        Dictionary<string, string> resMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Meeting> Meetings =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]).ThenBy(r => Json(r[2])["title"]?.GetValue<string>() ?? ""))
        {
            var data = Json(r[2]);
            var title = data["title"]?.GetValue<string>() ?? "";
            var campusId = data["campusId"]?.GetValue<string>();
            var tagIds = GetStringArray(data, "tagIds");
            var resourceIds = GetStringArray(data, "resourceIds");

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 11);
            Prop(sb, "SemesterId", Q(Remap(semMap, r[1])), 11);
            Prop(sb, "Title", Q(Esc(title)), 11);
            if (!string.IsNullOrEmpty(campusId))
                Prop(sb, "CampusId", Q(Remap(campusMap, campusId)), 11);
            if (tagIds.Count > 0)
                Prop(sb, "TagIds", RemapList(tagIds, tagMap), 11);
            if (resourceIds.Count > 0)
                Prop(sb, "ResourceIds", RemapList(resourceIds, resMap), 11);

            WriteSchedule(sb, data, roomMap, envMap);
            WriteInstructorAssignments(sb, data, instMap);

            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.InstructorCommitments.cs.</summary>
    private static string GenCommitments(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> instMap, Dictionary<string, string> semMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<InstructorCommitment> InstructorCommitments =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]).ThenBy(r => GetJsonInt(r[3], "day")).ThenBy(r => GetJsonInt(r[3], "startMinutes")))
        {
            var data = Json(r[3]);
            var name = data["name"]?.GetValue<string>() ?? "";
            var day = data["day"]?.GetValue<int>() ?? 0;
            var startMins = data["startMinutes"]?.GetValue<int>() ?? 0;
            var endMins = data["endMinutes"]?.GetValue<int>() ?? 0;

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 12);
            Prop(sb, "InstructorId", Q(Remap(instMap, r[1])), 12);
            Prop(sb, "SemesterId", Q(Remap(semMap, r[2])), 12);
            Prop(sb, "Name", Q(Esc(name)), 12);
            Prop(sb, "Day", day.ToString(), 12);
            Prop(sb, "StartMinutes", startMins.ToString(), 12);
            Prop(sb, "EndMinutes", endMins.ToString(), 12);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.Releases.cs.</summary>
    private static string GenReleases(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> semMap, Dictionary<string, string> instMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<Release> Releases =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => r[1]).ThenBy(r => Json(r[2])["instructorId"]?.GetValue<string>() ?? ""))
        {
            var data = Json(r[2]);
            var semesterId = data["semesterId"]?.GetValue<string>() ?? r[1];
            var instructorId = data["instructorId"]?.GetValue<string>() ?? "";
            var title = data["title"]?.GetValue<string>() ?? "";
            var workload = data["workloadValue"]?.GetValue<decimal>() ?? 0m;

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 13);
            Prop(sb, "SemesterId", Q(Remap(semMap, semesterId)), 13);
            Prop(sb, "InstructorId", Q(Remap(instMap, instructorId)), 13);
            Prop(sb, "Title", Q(Esc(title)), 13);
            Prop(sb, "WorkloadValue", $"{workload}m", 13);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    /// <summary>Generates DemoData.SectionCodePatterns.cs.</summary>
    private static string GenSectionCodePatterns(List<string[]> rows, Dictionary<string, string> map,
        Dictionary<string, string> campusMap, Dictionary<string, string> envMap)
    {
        var sb = Header();
        sb.AppendLine("    public static readonly List<SectionCodePattern> SectionCodePatterns =");
        sb.AppendLine("    [");

        foreach (var r in rows.OrderBy(r => ParseInt(r[2])).ThenBy(r => r[1]))
        {
            var data = Json(r[3]);
            var prefix = data["prefix"]?.GetValue<string>() ?? "";
            var suffix = data["suffix"]?.GetValue<string>() ?? "";
            var useLetters = data["useLetters"]?.GetValue<bool>() ?? false;
            var firstNumber = data["firstNumber"]?.GetValue<int>() ?? 1;
            var padWidth = data["padWidth"]?.GetValue<int>() ?? 0;
            var increment = data["increment"]?.GetValue<int>() ?? 1;
            var firstLetter = data["firstLetter"]?.GetValue<string>() ?? "A";
            var campusId = data["campusId"]?.GetValue<string>();
            var sectionTypeId = data["sectionTypeId"]?.GetValue<string>();
            var sortOrder = data["sortOrder"]?.GetValue<int>() ?? 0;
            var examples = data["examples"]?.GetValue<string>() ?? "";

            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            Prop(sb, "Id", Q(map[r[0]]), 13);
            Prop(sb, "Name", Q(Esc(r[1])), 13);
            if (!string.IsNullOrEmpty(prefix))
                Prop(sb, "Prefix", Q(Esc(prefix)), 13);
            if (!string.IsNullOrEmpty(suffix))
                Prop(sb, "Suffix", Q(Esc(suffix)), 13);
            if (useLetters)
                Prop(sb, "UseLetters", "true", 13);
            if (!useLetters && firstNumber != 1)
                Prop(sb, "FirstNumber", firstNumber.ToString(), 13);
            if (!useLetters && padWidth != 0)
                Prop(sb, "PadWidth", padWidth.ToString(), 13);
            if (!useLetters && increment != 1)
                Prop(sb, "Increment", increment.ToString(), 13);
            if (useLetters && firstLetter != "A")
                Prop(sb, "FirstLetter", $"'{firstLetter}'", 13);
            if (!string.IsNullOrEmpty(campusId))
                Prop(sb, "CampusId", Q(Remap(campusMap, campusId)), 13);
            if (!string.IsNullOrEmpty(sectionTypeId))
                Prop(sb, "SectionTypeId", Q(Remap(envMap, sectionTypeId)), 13);
            if (sortOrder != 0)
                Prop(sb, "SortOrder", sortOrder.ToString(), 13);
            if (!string.IsNullOrEmpty(examples))
                Prop(sb, "Examples", Q(Esc(examples)), 13);
            RemoveTrailingComma(sb);
            sb.AppendLine("        },");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("    ];");
        return Footer(sb);
    }

    // ── Shared nested-object writers ────────────────────────────────────────

    /// <summary>Writes the Schedule nested list from JSON data.</summary>
    private static void WriteSchedule(StringBuilder sb, JsonNode data,
        Dictionary<string, string> roomMap, Dictionary<string, string> envMap)
    {
        var schedule = data["schedule"]?.AsArray();
        if (schedule == null || schedule.Count == 0)
        {
            sb.AppendLine("            Schedule = [],");
            return;
        }

        sb.AppendLine("            Schedule =");
        sb.AppendLine("            [");

        foreach (var slot in schedule)
        {
            if (slot == null) continue;
            var day = slot["day"]?.GetValue<int>() ?? 0;
            var start = slot["startMinutes"]?.GetValue<int>() ?? 0;
            var dur = slot["durationMinutes"]?.GetValue<int>() ?? 0;
            var roomId = slot["roomId"]?.GetValue<string>();
            var mtId = slot["meetingTypeId"]?.GetValue<string>();
            var freq = slot["frequency"]?.GetValue<string>();

            var parts = new List<string>
            {
                $"Day = {day}",
                $"StartMinutes = {start}",
                $"DurationMinutes = {dur}"
            };
            if (!string.IsNullOrEmpty(roomId))
                parts.Add($"RoomId = \"{Remap(roomMap, roomId)}\"");
            if (!string.IsNullOrEmpty(mtId))
                parts.Add($"MeetingTypeId = \"{Remap(envMap, mtId)}\"");
            if (!string.IsNullOrEmpty(freq))
                parts.Add($"Frequency = \"{Esc(freq)}\"");

            sb.AppendLine($"                new() {{ {string.Join(", ", parts)} }},");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("            ],");
    }

    /// <summary>Writes the InstructorAssignments nested list from JSON data.</summary>
    private static void WriteInstructorAssignments(StringBuilder sb, JsonNode data,
        Dictionary<string, string> instMap)
    {
        var assignments = data["instructorAssignments"]?.AsArray();
        if (assignments == null || assignments.Count == 0)
        {
            sb.AppendLine("            InstructorAssignments = [],");
            return;
        }

        if (assignments.Count == 1 && assignments[0] != null)
        {
            var a = assignments[0]!;
            var instId = a["instructorId"]?.GetValue<string>() ?? "";
            var workload = a["workload"];

            var parts = $"InstructorId = \"{Remap(instMap, instId)}\"";
            if (workload != null)
                parts += $", Workload = {workload.GetValue<decimal>()}m";

            sb.AppendLine($"            InstructorAssignments = [ new() {{ {parts} }} ],");
            return;
        }

        sb.AppendLine("            InstructorAssignments =");
        sb.AppendLine("            [");

        foreach (var a in assignments)
        {
            if (a == null) continue;
            var instId = a["instructorId"]?.GetValue<string>() ?? "";
            var workload = a["workload"];

            var parts = $"InstructorId = \"{Remap(instMap, instId)}\"";
            if (workload != null)
                parts += $", Workload = {workload.GetValue<decimal>()}m";

            sb.AppendLine($"                new() {{ {parts} }},");
        }

        RemoveTrailingComma(sb);
        sb.AppendLine("            ],");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Builds the standard file header.</summary>
    private static StringBuilder Header()
    {
        var sb = new StringBuilder();
        sb.AppendLine("using SchedulingAssistant.Models;");
        sb.AppendLine();
        sb.AppendLine("namespace SchedulingAssistant.Demo;");
        sb.AppendLine();
        sb.AppendLine("public static partial class DemoData");
        sb.AppendLine("{");
        return sb;
    }

    /// <summary>Appends the closing brace and returns the full source.</summary>
    private static string Footer(StringBuilder sb)
    {
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Appends an aligned property assignment line.</summary>
    private static void Prop(StringBuilder sb, string name, string value, int padTo)
    {
        sb.AppendLine($"            {name.PadRight(padTo)} = {value},");
    }

    /// <summary>Wraps a value in double quotes.</summary>
    private static string Q(string? value) => $"\"{value}\"";

    /// <summary>Escapes a string for use in a C# string literal.</summary>
    private static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    /// <summary>Remaps a GUID to its demo ID, or returns a warning placeholder.</summary>
    private static string Remap(Dictionary<string, string> map, string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (map.TryGetValue(id, out var demoId)) return demoId;
        Console.Error.WriteLine($"  WARNING: ID '{id}' not found in remap dictionary");
        return $"UNMAPPED-{id[..8]}";
    }

    /// <summary>Builds a C# collection expression of remapped IDs.</summary>
    private static string RemapList(List<string> ids, Dictionary<string, string> map)
    {
        var remapped = ids.Select(id => Q(Remap(map, id)));
        return $"[{string.Join(", ", remapped)}]";
    }

    /// <summary>Builds an ID remap dictionary from ordered rows (id is always column 0).</summary>
    private static Dictionary<string, string> BuildIdMap(IEnumerable<string[]> orderedRows, string prefix)
    {
        var map = new Dictionary<string, string>();
        int n = 1;
        foreach (var r in orderedRows)
            map[r[0]] = $"{prefix}-{n++}";
        return map;
    }

    /// <summary>Parses a JsonNode from a JSON string.</summary>
    private static JsonNode Json(string json) => JsonNode.Parse(json) ?? new JsonObject();

    /// <summary>Gets a string array from a JSON node property.</summary>
    private static List<string> GetStringArray(JsonNode data, string prop)
    {
        var arr = data[prop]?.AsArray();
        if (arr == null) return [];
        return arr.Where(n => n != null).Select(n => n!.GetValue<string>()).ToList();
    }

    /// <summary>Gets an int array from a JSON node property.</summary>
    private static List<int> GetIntArray(JsonNode data, string prop)
    {
        var arr = data[prop]?.AsArray();
        if (arr == null) return [];
        return arr.Where(n => n != null).Select(n => n!.GetValue<int>()).ToList();
    }

    /// <summary>Reads the sortOrder from a JSON data column.</summary>
    private static int GetSortOrder(string json)
    {
        try { return Json(json)["sortOrder"]?.GetValue<int>() ?? 0; }
        catch { return 0; }
    }

    /// <summary>Gets an int value from a JSON string by property name.</summary>
    private static int GetJsonInt(string json, string prop)
    {
        try { return Json(json)[prop]?.GetValue<int>() ?? 0; }
        catch { return 0; }
    }

    /// <summary>Parses an int from a string, defaulting to 0.</summary>
    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

    /// <summary>Formats a double with at least one decimal place.</summary>
    private static string FormatDouble(double d) =>
        d == Math.Floor(d) ? $"{d:F1}" : d.ToString();

    /// <summary>Extracts a start year from an academic year name like "2025-2026".</summary>
    private static int ExtractStartYear(string name)
    {
        var parts = name.Split('-');
        return int.TryParse(parts[0], out var y) ? y : 0;
    }

    /// <summary>Removes the trailing comma from the last line in the StringBuilder.</summary>
    private static void RemoveTrailingComma(StringBuilder sb)
    {
        var s = sb.ToString();
        var lastNewline = s.LastIndexOf('\n', s.Length - 2);
        if (lastNewline < 0) return;

        var lastLine = s[(lastNewline + 1)..];
        var trimmed = lastLine.TrimEnd('\r', '\n');
        if (trimmed.EndsWith(','))
        {
            sb.Length = lastNewline + 1;
            sb.AppendLine(trimmed[..^1]);
        }
    }

    /// <summary>Checks whether a table exists in the database.</summary>
    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$t";
        var p = cmd.CreateParameter();
        p.ParameterName = "$t";
        p.Value = table;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Reads all rows from a query, returning lists of string columns.</summary>
    private static List<string[]> ReadAll(SqliteConnection conn, string sql, int columnCount,
        params (string Name, string Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        var results = new List<string[]>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new string[columnCount];
            for (var i = 0; i < columnCount; i++)
                row[i] = reader.IsDBNull(i) ? "" : reader.GetString(i);
            results.Add(row);
        }
        return results;
    }
}
