using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bogus;
using Microsoft.Data.Sqlite;

namespace SchedulingAssistant.Anonymizer;

/// <summary>
/// Reads a TermPoint SQLite database and produces an anonymized copy.
/// All instructor names, course titles, subject names, campus names, building names,
/// and institution identity are replaced with plausible fake data.
/// Structural relationships (IDs, schedules, workloads) are preserved.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- <input.sqlite> <output.sqlite>");
            Console.WriteLine("Produces an anonymized copy of a TermPoint database.");
            return 1;
        }

        var inputPath = args[0];
        var outputPath = args[1];

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        if (File.Exists(outputPath))
        {
            Console.Error.WriteLine($"Output file already exists: {outputPath}");
            Console.Error.WriteLine("Delete it first or choose a different name.");
            return 1;
        }

        File.Copy(inputPath, outputPath);
        Console.WriteLine($"Copied {inputPath} -> {outputPath}");

        using var conn = new SqliteConnection($"Data Source={outputPath}");
        conn.Open();

        var faker = new Faker();

        var instructorMap = AnonymizeInstructors(conn, faker);
        AnonymizeSubjectsAndCourses(conn);
        var campusMap = AnonymizeCampuses(conn);
        AnonymizeRooms(conn, campusMap);
        AnonymizeAcademicUnits(conn);
        AnonymizeMeetings(conn);
        AnonymizeSections(conn);
        AnonymizeInstructorCommitments(conn, instructorMap);
        AnonymizeReleases(conn, instructorMap);
        AnonymizeAppConfiguration(conn);

        Console.WriteLine("Anonymization complete.");
        return 0;
    }

    // ── Instructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces each instructor's name, initials, email, and notes with Bogus-generated data.
    /// Returns a mapping of instructor ID to (FirstName, LastName, Initials) for updating
    /// denormalized columns in other tables.
    /// </summary>
    private static Dictionary<string, (string First, string Last, string Initials)>
        AnonymizeInstructors(SqliteConnection conn, Faker faker)
    {
        var map = new Dictionary<string, (string, string, string)>();

        var rows = ReadAll(conn, "SELECT id, data FROM Instructors");
        Console.WriteLine($"Anonymizing {rows.Count} instructors...");

        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            var initials = $"{first[0]}{last[0]}";

            node["firstName"] = first;
            node["lastName"] = last;
            node["initials"] = initials;
            node["email"] = $"{first.ToLower()}.{last.ToLower()}@udelphi.edu";
            node["notes"] = "";

            map[id] = (first, last, initials);

            Execute(conn,
                "UPDATE Instructors SET first_name=$f, last_name=$l, initials=$i, data=$d WHERE id=$id",
                ("$f", first), ("$l", last), ("$i", initials),
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }

        return map;
    }

    // ── Subjects & Courses ───────────────────────────────────────────────────

    /// <summary>
    /// Describes how a subject should be anonymized.
    /// </summary>
    private record SubjectMapping(string Name, string Abbreviation,
        Dictionary<string, string[]> TitlePool);

    /// <summary>
    /// The anonymization assignments applied to subjects in the order they appear in the DB.
    /// First subject  → Geography / GEOG
    /// Second subject → Environmental Studies / ENV
    /// Additional subjects cycle through these two.
    /// Declared as a property so the title pool fields are guaranteed initialized first.
    /// </summary>
    private static SubjectMapping[] SubjectMappings =>
    [
        new("Geography",             "GEOG", GeographyTitles),
        new("Environmental Studies", "ENV",  EnvironmentalTitles),
    ];

    /// <summary>
    /// Replaces each subject with a fake academic department (Geography, then Environmental
    /// Studies for the second subject) and assigns each course a matching title.
    /// Course numbers are preserved; only the subject prefix and title change.
    /// </summary>
    private static void AnonymizeSubjectsAndCourses(SqliteConnection conn)
    {
        var subjectRows = ReadAll(conn, "SELECT id, data FROM Subjects ORDER BY rowid");
        Console.WriteLine($"Anonymizing {subjectRows.Count} subjects...");

        // Build a map of subject ID → SubjectMapping so courses know which pool to use
        var subjectMappingById = new Dictionary<string, SubjectMapping>();

        for (var i = 0; i < subjectRows.Count; i++)
        {
            var (id, json) = subjectRows[i];
            var mapping = SubjectMappings[i % SubjectMappings.Length];
            subjectMappingById[id] = mapping;

            var node = JsonNode.Parse(json)!;
            node["name"] = mapping.Name;
            node["calendarAbbreviation"] = mapping.Abbreviation;

            Console.WriteLine($"  Subject {i + 1}: -> {mapping.Name} ({mapping.Abbreviation})");

            Execute(conn,
                "UPDATE Subjects SET name=$n, abbreviation=$a, data=$d WHERE id=$id",
                ("$n", mapping.Name), ("$a", mapping.Abbreviation),
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }

        // Remap courses: keep number, change prefix, assign title from the subject's pool
        // Read subject_id alongside course data so we can look up the right mapping
        var courseRows = ReadAll(conn,
            "SELECT id, subject_id, data FROM Courses ORDER BY calendar_code",
            columnCount: 3);
        Console.WriteLine($"Anonymizing {courseRows.Count} courses...");

        // Each subject gets its own used-titles set so pools don't collide
        var usedTitlesBySubject = new Dictionary<string, HashSet<string>>();

        foreach (var row in courseRows)
        {
            var id = row[0];
            var subjectId = row[1];
            var json = row[2];

            var node = JsonNode.Parse(json)!;
            var oldCode = node["calendarCode"]?.GetValue<string>() ?? "";
            var number = ExtractCourseNumber(oldCode);

            if (!subjectMappingById.TryGetValue(subjectId, out var mapping))
                mapping = SubjectMappings[0]; // fallback to GEOG

            if (!usedTitlesBySubject.TryGetValue(subjectId, out var usedTitles))
            {
                usedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedTitlesBySubject[subjectId] = usedTitles;
            }

            var newCode = $"{mapping.Abbreviation}{number}";
            var level = GuessCourseLevel(number);
            var title = PickTitle(level, usedTitles, mapping.TitlePool);

            node["calendarCode"] = newCode;
            node["title"] = title;

            Execute(conn,
                "UPDATE Courses SET calendar_code=$cc, title=$t, data=$d WHERE id=$id",
                ("$cc", newCode), ("$t", title),
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }
    }

    // ── Campuses ─────────────────────────────────────────────────────────────

    private static readonly string[] FakeCampusNames =
    [
        "Westbrook", "Lakeview", "Ridgemont", "Sundale", "Clearwater",
        "Oakridge", "Bayside", "Northfield", "Stonegate", "Cedarvale"
    ];

    private static readonly string[] FakeCampusAbbrevs =
    [
        "WB", "LV", "RM", "SD", "CW", "OR", "BS", "NF", "SG", "CV"
    ];

    /// <summary>
    /// Replaces campus names and abbreviations with fake names.
    /// Returns a mapping of campus ID to new name for use when anonymizing room buildings.
    /// </summary>
    private static Dictionary<string, string> AnonymizeCampuses(SqliteConnection conn)
    {
        var map = new Dictionary<string, string>();
        var rows = ReadAll(conn, "SELECT id, data FROM Campuses");
        Console.WriteLine($"Anonymizing {rows.Count} campuses...");

        for (var i = 0; i < rows.Count; i++)
        {
            var (id, json) = rows[i];
            var node = JsonNode.Parse(json)!;
            var name = FakeCampusNames[i % FakeCampusNames.Length];
            var abbrev = FakeCampusAbbrevs[i % FakeCampusAbbrevs.Length];

            node["name"] = name;
            node["abbreviation"] = abbrev;
            map[id] = name;

            Execute(conn,
                "UPDATE Campuses SET name=$n, data=$d WHERE id=$id",
                ("$n", name), ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }

        return map;
    }

    // ── Rooms ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces building names with the campus name from the campus map,
    /// and clears features/notes. Room numbers are kept as-is.
    /// </summary>
    private static void AnonymizeRooms(SqliteConnection conn, Dictionary<string, string> campusMap)
    {
        var rows = ReadAll(conn, "SELECT id, data FROM Rooms");
        Console.WriteLine($"Anonymizing {rows.Count} rooms...");

        var buildingCounter = new Dictionary<string, int>();

        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            var campusId = node["campusId"]?.GetValue<string>();

            string building;
            if (campusId != null && campusMap.TryGetValue(campusId, out var campusName))
            {
                building = campusName;
            }
            else
            {
                building = "Main Building";
            }

            node["building"] = building;
            node["features"] = "";
            node["notes"] = "";

            Execute(conn,
                "UPDATE Rooms SET building=$b, data=$d WHERE id=$id",
                ("$b", building), ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }
    }

    // ── Academic Units ───────────────────────────────────────────────────────

    private static void AnonymizeAcademicUnits(SqliteConnection conn)
    {
        var rows = ReadAll(conn, "SELECT id, data FROM AcademicUnits");
        Console.WriteLine($"Anonymizing {rows.Count} academic units...");

        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            node["name"] = "Department of Geography";
            node["abbreviation"] = "GEOG";

            Execute(conn,
                "UPDATE AcademicUnits SET name=$n, data=$d WHERE id=$id",
                ("$n", "Department of Geography"),
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }
    }

    // ── Meetings ─────────────────────────────────────────────────────────────

    private static void AnonymizeMeetings(SqliteConnection conn)
    {
        var rows = ReadAll(conn, "SELECT id, data FROM Meetings");
        Console.WriteLine($"Anonymizing {rows.Count} meetings...");

        var counter = 1;
        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            var title = $"Meeting {counter++}";
            node["title"] = title;
            node["notes"] = "";

            Execute(conn,
                "UPDATE Meetings SET title=$t, data=$d WHERE id=$id",
                ("$t", title), ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears notes from all sections and updates the denormalized course_code column
    /// to reflect the new GEOG prefix.
    /// </summary>
    private static void AnonymizeSections(SqliteConnection conn)
    {
        var rows = ReadAll(conn, "SELECT id, data FROM Sections");
        Console.WriteLine($"Anonymizing {rows.Count} sections...");

        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            node["notes"] = "";

            Execute(conn,
                "UPDATE Sections SET data=$d WHERE id=$id",
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }

        // Update denormalized course_code column from the now-anonymized Courses table
        Execute(conn,
            "UPDATE Sections SET course_code = " +
            "(SELECT calendar_code FROM Courses WHERE Courses.id = Sections.course_id)");
    }

    // ── Instructor Commitments ───────────────────────────────────────────────

    /// <summary>
    /// Updates the denormalized instructor_name column in InstructorCommitments
    /// to match the anonymized instructor names.
    /// </summary>
    private static void AnonymizeInstructorCommitments(
        SqliteConnection conn,
        Dictionary<string, (string First, string Last, string Initials)> instructorMap)
    {
        var rows = ReadAll(conn, "SELECT id, instructor_id, data FROM InstructorCommitments",
            columnCount: 3);
        Console.WriteLine($"Anonymizing {rows.Count} instructor commitments...");

        foreach (var row in rows)
        {
            var id = row[0];
            var instructorId = row[1];

            if (instructorMap.TryGetValue(instructorId, out var names))
            {
                var displayName = $"{names.First} {names.Last}";
                Execute(conn,
                    "UPDATE InstructorCommitments SET instructor_name=$n WHERE id=$id",
                    ("$n", displayName), ("$id", id));
            }
        }
    }

    // ── Releases ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Releases may contain notes or descriptions that reference real people.
    /// Clear any free-text fields in the JSON data.
    /// </summary>
    private static void AnonymizeReleases(
        SqliteConnection conn,
        Dictionary<string, (string First, string Last, string Initials)> instructorMap)
    {
        var rows = ReadAll(conn, "SELECT id, data FROM Releases");
        Console.WriteLine($"Anonymizing {rows.Count} releases...");

        foreach (var (id, json) in rows)
        {
            var node = JsonNode.Parse(json)!;
            if (node["notes"] != null)
                node["notes"] = "";
            if (node["description"] != null)
                node["description"] = "";

            Execute(conn,
                "UPDATE Releases SET data=$d WHERE id=$id",
                ("$d", node.ToJsonString(JsonOpts)), ("$id", id));
        }
    }

    // ── App Configuration ────────────────────────────────────────────────────

    private static void AnonymizeAppConfiguration(SqliteConnection conn)
    {
        Console.WriteLine("Anonymizing app configuration...");

        Execute(conn,
            "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($k, $v)",
            ("$k", "InstitutionName"), ("$v", "University of Delphi"));

        Execute(conn,
            "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($k, $v)",
            ("$k", "InstitutionAbbreviation"), ("$v", "UD"));
    }

    // ── Geography Course Title Pool ──────────────────────────────────────────

    private static readonly Dictionary<string, string[]> GeographyTitles = new()
    {
        ["100"] =
        [
            "Introduction to Physical Geography",
            "Introduction to Human Geography",
            "World Regional Geography",
            "Earth's Natural Environments",
            "Maps and Spatial Thinking",
            "Weather and Climate",
            "Global Environmental Issues",
            "Geography of Natural Hazards",
            "Cultural Landscapes",
            "Introduction to Geographic Information Systems",
            "Oceans and Coastal Environments",
            "Fundamentals of Cartography",
            "Geography of Food and Agriculture",
            "Landforms and Surface Processes",
            "Population and Migration",
        ],
        ["200"] =
        [
            "Geomorphology",
            "Climatology",
            "Biogeography",
            "Urban Geography",
            "Economic Geography",
            "Soil Science",
            "Hydrology and Water Resources",
            "Remote Sensing Fundamentals",
            "Political Geography",
            "Geography of Health and Disease",
            "Transportation Geography",
            "Geographic Data Analysis",
            "Coastal and Marine Geography",
            "Cultural Ecology",
            "Geography of Latin America",
            "Geography of Europe",
            "Geography of East Asia",
            "Mountain Environments",
            "Geography of Africa",
            "Environmental Conservation",
        ],
        ["300"] =
        [
            "Advanced Geographic Information Systems",
            "Glacial and Periglacial Geomorphology",
            "Synoptic Meteorology",
            "Fluvial Geomorphology",
            "Geography of Urbanization",
            "Spatial Statistics",
            "Environmental Remote Sensing",
            "Historical Geography",
            "Landscape Ecology",
            "Geography of Development",
            "Water Resource Management",
            "Advanced Cartography and Visualization",
            "Geography of Energy and Resources",
            "Environmental Impact Assessment",
            "Quaternary Environments",
            "Digital Terrain Analysis",
            "Political Ecology",
            "Geography of Global Trade",
            "Applied Climatology",
            "Geography of Tourism and Recreation",
        ],
        ["400"] =
        [
            "Research Methods in Geography",
            "Senior Seminar in Physical Geography",
            "Senior Seminar in Human Geography",
            "Advanced Spatial Analysis",
            "Climate Change Science and Policy",
            "Geographic Thought and Philosophy",
            "Advanced Environmental Modeling",
            "Directed Studies in Geography",
            "Honours Thesis in Geography",
            "Advanced Topics in Geomorphology",
            "Advanced Topics in Urban Systems",
            "Land Use Planning and Policy",
            "Advanced Remote Sensing Applications",
            "Environmental Governance",
            "Capstone Project in Geography",
        ],
    };

    // ── Environmental Studies Course Title Pool ──────────────────────────────

    private static readonly Dictionary<string, string[]> EnvironmentalTitles = new()
    {
        ["100"] =
        [
            "Introduction to Environmental Science",
            "Environmental Issues and Solutions",
            "Principles of Ecology",
            "Earth Systems Science",
            "Introduction to Sustainability",
            "Environmental Challenges of the 21st Century",
            "Humans and the Natural World",
            "Introduction to Climate Science",
            "Food, Agriculture, and the Environment",
            "Freshwater Resources and Society",
            "Air Quality and Human Health",
            "Introduction to Conservation Biology",
            "Environmental Ethics",
            "Energy and the Environment",
            "Global Environmental Change",
        ],
        ["200"] =
        [
            "Ecology",
            "Environmental Chemistry",
            "Soil and Water Conservation",
            "Wildlife Biology",
            "Environmental Policy and Law",
            "Restoration Ecology",
            "Environmental Toxicology",
            "Forest Ecology",
            "Wetlands Science",
            "Climate and Society",
            "Environmental Data Analysis",
            "Marine Ecology",
            "Urban Ecology",
            "Environmental Economics",
            "Biodiversity and Ecosystems",
            "Renewable Energy Systems",
            "Waste Management and Pollution Control",
            "Environmental Risk Assessment",
            "Grassland and Dryland Ecology",
            "Environmental Communication",
        ],
        ["300"] =
        [
            "Advanced Ecology",
            "Environmental Impact Assessment",
            "Conservation Planning",
            "Watershed Management",
            "Climate Change Adaptation",
            "Environmental Monitoring and Assessment",
            "Sustainable Resource Management",
            "Population and Community Ecology",
            "Environmental Modelling",
            "Land Use and Ecosystem Services",
            "Advanced Environmental Policy",
            "Ecological Restoration Practice",
            "Environmental Geochemistry",
            "Urban Environmental Management",
            "International Environmental Agreements",
            "Advanced Conservation Biology",
            "Applied Environmental Statistics",
            "Carbon and Nutrient Cycles",
            "Environmental Governance",
            "Ecosystem Management",
        ],
        ["400"] =
        [
            "Research Methods in Environmental Science",
            "Senior Seminar in Environmental Studies",
            "Environmental Science Capstone",
            "Honours Thesis in Environmental Studies",
            "Directed Studies in Ecology",
            "Advanced Topics in Climate Change",
            "Advanced Topics in Conservation",
            "Environmental Policy Practicum",
            "Ecological Modelling and Simulation",
            "Sustainable Development Theory and Practice",
            "Environmental Leadership and Communication",
            "Applied Conservation Genetics",
            "Environmental Risk and Decision Making",
            "Advanced Water Resource Management",
            "Interdisciplinary Environmental Problem Solving",
        ],
    };

    /// <summary>
    /// Picks a title from the given pool appropriate for the course level.
    /// Avoids reusing titles within the same subject's anonymization run.
    /// Falls back to a generic numbered title if the pool is exhausted.
    /// </summary>
    private static string PickTitle(string level, HashSet<string> usedTitles,
        Dictionary<string, string[]> pool)
    {
        if (!pool.TryGetValue(level, out var titles))
            titles = pool["100"];

        foreach (var title in titles)
        {
            if (usedTitles.Add(title))
                return title;
        }

        // Pool exhausted — generate a unique fallback
        var fallback = $"Special Topics ({level}-level) [{usedTitles.Count}]";
        usedTitles.Add(fallback);
        return fallback;
    }

    /// <summary>
    /// Extracts the numeric portion from a calendar code.
    /// Handles both spaced formats ("HIST 112" → "112") and
    /// concatenated formats ("MATH221" → "221").
    /// </summary>
    private static string ExtractCourseNumber(string calendarCode)
    {
        var match = Regex.Match(calendarCode, @"\d+");
        return match.Success ? match.Value : calendarCode;
    }

    /// <summary>
    /// Determines the level bucket (100, 200, 300, 400) from a course number string.
    /// </summary>
    private static string GuessCourseLevel(string number)
    {
        if (int.TryParse(number, out var n))
        {
            return n switch
            {
                < 200 => "100",
                < 300 => "200",
                < 400 => "300",
                _ => "400"
            };
        }
        return "100";
    }

    // ── SQLite helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads all rows from a query, returning pairs of (id, data_json).
    /// </summary>
    private static List<(string Id, string Data)> ReadAll(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var results = new List<(string, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    /// <summary>
    /// Reads all rows from a query, returning lists of string columns.
    /// </summary>
    private static List<string[]> ReadAll(SqliteConnection conn, string sql, int columnCount)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
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

    /// <summary>
    /// Executes a parameterized SQL statement.
    /// </summary>
    private static void Execute(SqliteConnection conn, string sql,
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
        cmd.ExecuteNonQuery();
    }
}
