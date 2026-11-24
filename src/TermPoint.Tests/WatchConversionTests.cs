using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>The <c>mode</c> of an old watch row as the old repository serialized it: its numeric value is what the JSON held.</summary>
public enum OldWatchMode { Tag = 0, Course = 1 }

/// <summary>
/// Access Analyzer piece B: the schema version 1 to 2 conversion of today's <c>ProgramWatches</c> rows into watched
/// tags (<c>WatchConversion</c>, run by <c>DatabaseContext.Migrate</c> and by <c>ReinitializeConnection</c>), with one
/// test per rule of the design's "The upgrade of today's watches" (D28, Q37, Q38, Q39), the "twice gives identical
/// rows" guarantee, the reopen paths, and "the first save after a conversion is a real save".
///
/// <para>Each test builds a version 1 file (what a 1.2.4 writer saves): a real <see cref="DatabaseContext"/> creates
/// the tables, then <c>user_version</c> is set back to 1 and the rows are inserted with SQL, so the next
/// <see cref="DatabaseContext"/> to open it runs the conversion exactly as the app would.</para>
/// </summary>
public sealed class WatchConversionTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates a private temp folder.</summary>
    public WatchConversionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"wconv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Releases handles and removes the temp folder.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a version 1 database at a new path: schema from a real context, <c>user_version</c> set back to 1, and the
    /// rollback journal restored so the file is one self-contained file that can be copied.
    /// </summary>
    /// <param name="name">File name stem.</param>
    /// <returns>The path of the new file.</returns>
    private string NewV1(string name)
    {
        var path = Path.Combine(_dir, name + ".db");
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();
        using (var conn = Open(path))
        {
            Exec(conn, "PRAGMA user_version = 1");
            Exec(conn, "PRAGMA journal_mode = DELETE");
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    /// <summary>Opens a plain, unpooled connection.</summary>
    /// <param name="path">The database file.</param>
    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        return c;
    }

    /// <summary>Runs one statement.</summary>
    /// <param name="conn">Open connection.</param>
    /// <param name="sql">The statement.</param>
    /// <param name="args">Parameter values for <c>$0</c>, <c>$1</c>...</param>
    private static void Exec(SqliteConnection conn, string sql, params object?[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue($"${i}", args[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Opens (and so converts) the file with a normal writer context, then closes it.</summary>
    /// <param name="path">The version 1 file.</param>
    private static void Convert(string path)
    {
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Seeds rows into a version 1 file.</summary>
    private sealed class Seeder(string path)
    {
        /// <summary>Adds an academic year and a semester in it.</summary>
        /// <param name="semesterId">Semester id.</param>
        /// <param name="ayName">Academic year name (its id is derived).</param>
        /// <param name="sortOrder">The semester's sort order.</param>
        public Seeder Semester(string semesterId, string ayName = "2024-2025", int sortOrder = 0)
        {
            using var c = Open(path);
            Exec(c, "INSERT OR IGNORE INTO AcademicYears (id, name, data) VALUES ($0, $1, '{}')", "ay-" + ayName, ayName);
            Exec(c, "INSERT INTO Semesters (id, academic_year_id, academic_year_name, name, sort_order, data) VALUES ($0, $1, $2, $0, $3, '{}')",
                semesterId, "ay-" + ayName, ayName, sortOrder);
            return this;
        }

        /// <summary>Adds a tag.</summary>
        /// <param name="id">Tag id.</param>
        /// <param name="name">Tag name.</param>
        /// <param name="sortOrder">Its sort order.</param>
        public Seeder Tag(string id, string name, int sortOrder = 0)
        {
            using var c = Open(path);
            var v = new SchedulingEnvironmentValue { Id = id, Name = name, SortOrder = sortOrder };
            Exec(c, "INSERT INTO SchedulingEnvironmentValues (id, type, name, data) VALUES ($0, 'tag', $1, $2)",
                id, name, JsonHelpers.Serialize(v));
            return this;
        }

        /// <summary>Adds a course.</summary>
        /// <param name="id">Course id.</param>
        /// <param name="tags">Its tag ids.</param>
        public Seeder Course(string id, params string[] tags)
        {
            using var c = Open(path);
            var course = new Course { Id = id, SubjectId = "subj", CalendarCode = id.ToUpperInvariant(), Title = "T " + id, TagIds = [.. tags] };
            Exec(c, "INSERT INTO Courses (id, subject_id, calendar_code, title, data) VALUES ($0, 'subj', $1, $2, $3)",
                id, course.CalendarCode, course.Title, JsonHelpers.Serialize(course));
            return this;
        }

        /// <summary>Adds a section.</summary>
        /// <param name="id">Section id.</param>
        /// <param name="semesterId">Its semester.</param>
        /// <param name="courseId">Its course (may be null).</param>
        /// <param name="level">Its level (may be null).</param>
        /// <param name="tags">Its tag ids.</param>
        /// <param name="futureField">When true, the JSON also carries a field this version does not know.</param>
        public Seeder Section(string id, string semesterId, string? courseId, string? level, string[] tags, bool futureField = false)
        {
            using var c = Open(path);
            var s = new Section { Id = id, SemesterId = semesterId, CourseId = courseId, Level = level, SectionCode = id, TagIds = [.. tags] };
            var json = JsonNode.Parse(JsonHelpers.Serialize(s))!.AsObject();
            if (futureField) json["futureField"] = new JsonObject { ["a"] = 1 };
            Exec(c, "INSERT INTO Sections (id, semester_id, course_id, section_code, data) VALUES ($0, $1, $2, $0, $3)",
                id, semesterId, courseId, json.ToJsonString());
            return this;
        }

        /// <summary>Adds an old watch row exactly as <c>ProgramWatchRepository.Save</c> writes it.</summary>
        /// <param name="id">Watch id.</param>
        /// <param name="semesterId">Its semester.</param>
        /// <param name="name">Its name.</param>
        /// <param name="mode">Tag or course mode.</param>
        /// <param name="enabled">Whether it is enabled.</param>
        /// <param name="tags">Tag ids (tag mode).</param>
        /// <param name="courses">Course ids (course mode).</param>
        /// <param name="levels">Level values (tag mode).</param>
        public Seeder Watch(string id, string semesterId, string name, OldWatchMode mode = OldWatchMode.Tag,
            bool enabled = true, string[]? tags = null, string[]? courses = null, string[]? levels = null)
        {
            using var c = Open(path);
            // The JSON the old ProgramWatchRepository wrote: camelCase members, mode as a number (0 tag, 1 course).
            var w = new JsonObject
            {
                ["id"] = id, ["name"] = name, ["mode"] = (int)mode, ["isEnabled"] = enabled,
                ["tagIds"] = new JsonArray([.. (tags ?? []).Select(t => (JsonNode?)JsonValue.Create(t))]),
                ["courseIds"] = new JsonArray([.. (courses ?? []).Select(t => (JsonNode?)JsonValue.Create(t))]),
                ["levelIds"] = new JsonArray([.. (levels ?? []).Select(t => (JsonNode?)JsonValue.Create(t))]),
            };
            Exec(c, "INSERT INTO ProgramWatches (id, semester_id, name, mode, data) VALUES ($0, $1, $2, $3, $4)",
                id, semesterId, name, mode.ToString().ToLowerInvariant(), w.ToJsonString());
            return this;
        }
    }

    /// <summary>Reads every (id, data) of a table, in id order, from a plain connection.</summary>
    /// <param name="path">The database.</param>
    /// <param name="table">The table.</param>
    /// <param name="where">Optional WHERE clause body.</param>
    private static List<(string Id, string Data)> Rows(string path, string table, string? where = null)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id, data FROM {table}" + (where is null ? "" : " WHERE " + where) + " ORDER BY id";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Every row of the given tables as one comparable text.</summary>
    /// <param name="path">The database.</param>
    /// <param name="tables">The tables to include.</param>
    private static string Dump(string path, params string[] tables)
        => string.Join("\n", tables.SelectMany(t => Rows(path, t).Select(r => $"{t}|{r.Id}|{r.Data}")));

    /// <summary>The tag rows as parsed objects, keyed by id.</summary>
    /// <param name="path">The database.</param>
    private static Dictionary<string, JsonObject> Tags(string path)
        => Rows(path, "SchedulingEnvironmentValues", "type = 'tag'")
            .ToDictionary(r => r.Id, r => JsonNode.Parse(r.Data)!.AsObject());

    /// <summary>The tag ids on a section or course row.</summary>
    /// <param name="path">The database.</param>
    /// <param name="table">Sections or Courses.</param>
    /// <param name="id">The row id.</param>
    private static List<string> TagIdsOf(string path, string table, string id)
        => JsonNode.Parse(Rows(path, table, $"id = '{id}'").Single().Data)!["tagIds"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

    /// <summary>True when a tag row is marked as an access watch.</summary>
    /// <param name="tag">The tag's parsed JSON.</param>
    private static bool IsWatched(JsonObject tag) => tag["isAccessWatch"]?.GetValue<bool>() == true;

    /// <summary>The value of <c>PRAGMA user_version</c>.</summary>
    /// <param name="path">The database.</param>
    private static int UserVersion(string path)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Rule tests ────────────────────────────────────────────────────────────

    /// <summary>The enum is stored as a number in the watch JSON (0 tag, 1 course): pins the format the conversion reads.</summary>
    [Fact]
    public void OldWatchRows_StoreTheModeAsANumber()
    {
        var path = NewV1("mode");
        new Seeder(path).Semester("s1").Watch("w1", "s1", "W", OldWatchMode.Course, courses: ["c1"]);

        Assert.Equal(1, JsonNode.Parse(Rows(path, "ProgramWatches").Single().Data)!["mode"]!.GetValue<int>());
    }

    /// <summary>Rule 1: one tag, no levels, enabled: the tag becomes the watch; no new tag, no section or course writes.</summary>
    [Fact]
    public void SingleTagWatch_MarksThatTag_AndWritesNothingElse()
    {
        var path = NewV1("single");
        new Seeder(path).Semester("s1").Tag("t1", "CIVL Y3").Tag("t2", "Other")
            .Course("c1", "t1").Section("a", "s1", "c1", "300", ["t1"], futureField: true)
            .Watch("w1", "s1", "CIVL Y3", tags: ["t1"]);
        var sectionsBefore = Rows(path, "Sections");
        var coursesBefore = Rows(path, "Courses");

        Convert(path);

        var tags = Tags(path);
        Assert.Equal(2, tags.Count);
        Assert.True(IsWatched(tags["t1"]));
        Assert.False(IsWatched(tags["t2"]));
        Assert.Equal(sectionsBefore, Rows(path, "Sections"));
        Assert.Equal(coursesBefore, Rows(path, "Courses"));
        Assert.Empty(Rows(path, "ProgramWatches"));
    }

    /// <summary>Rule 1 on a missing tag row: nothing to do, nothing invented, the watch row is still removed.</summary>
    [Fact]
    public void SingleTagWatch_WhoseTagIsGone_DoesNothing()
    {
        var path = NewV1("single_missing");
        new Seeder(path).Semester("s1").Watch("w1", "s1", "Ghost", tags: ["gone"]);

        Convert(path);

        Assert.Empty(Tags(path));
        Assert.Empty(Rows(path, "ProgramWatches"));
    }

    /// <summary>
    /// Rule 2 (Q38): a course list gets a new tag named as the watch, added to each listed course and to THOSE courses'
    /// sections in the watch's semester only; existing tags are kept and nothing is added twice.
    /// </summary>
    [Fact]
    public void CourseListWatch_TagsTheCourses_AndOnlyThatSemestersSections()
    {
        var path = NewV1("courses");
        new Seeder(path).Semester("s1").Semester("s2", sortOrder: 1)
            .Tag("x", "Existing", 4)
            .Course("c1", "x").Course("c2").Course("c3")
            .Section("a", "s1", "c1", null, ["x"]).Section("b", "s1", "c2", null, [])
            .Section("c", "s2", "c1", null, ["x"]).Section("d", "s1", "c3", null, [])
            .Section("e", "s1", null, null, [])
            .Watch("w-core", "s1", "Core", OldWatchMode.Course, courses: ["c1", "c2", "c1", "nope"]);

        Convert(path);

        var tags = Tags(path);
        Assert.Equal(2, tags.Count);
        var core = tags["w-core"];
        Assert.Equal("Core", core["name"]!.GetValue<string>());
        Assert.True(IsWatched(core));
        Assert.Equal(5, core["sortOrder"]!.GetValue<int>());
        Assert.False(core.ContainsKey("watchMultiCampus"));
        Assert.Equal(["x", "w-core"], TagIdsOf(path, "Courses", "c1"));
        Assert.Equal(["w-core"], TagIdsOf(path, "Courses", "c2"));
        Assert.Empty(TagIdsOf(path, "Courses", "c3"));
        Assert.Equal(["x", "w-core"], TagIdsOf(path, "Sections", "a"));
        Assert.Equal(["w-core"], TagIdsOf(path, "Sections", "b"));
        Assert.Equal(["x"], TagIdsOf(path, "Sections", "c"));      // other semester: untouched
        Assert.Empty(TagIdsOf(path, "Sections", "d"));             // course not listed
        Assert.Empty(TagIdsOf(path, "Sections", "e"));             // no course
        Assert.Equal("Core", ReadNameColumn(path, "w-core"));      // the readable column is filled in
    }

    /// <summary>Rule 3: several tags: a new tag on the sections that carry ALL of them (in the watch's semester); not on courses.</summary>
    [Fact]
    public void SeveralTagsWatch_TagsTheSectionsThatCarriedAllTags_NotCourses()
    {
        var path = NewV1("several");
        new Seeder(path).Semester("s1").Semester("s2", sortOrder: 1)
            .Tag("a", "A").Tag("b", "B").Tag("z", "Z")
            .Course("c1", "a", "b")
            .Section("s-ab", "s1", "c1", null, ["a", "b"], futureField: true)
            .Section("s-a", "s1", "c1", null, ["a"])
            .Section("s-bza", "s1", "c1", null, ["b", "z", "a"])
            .Section("s-other-sem", "s2", "c1", null, ["a", "b"])
            .Watch("w-civil", "s1", "Civil", tags: ["a", "b"]);
        var untouched = Rows(path, "Sections", "id IN ('s-a','s-other-sem')");
        var courseBefore = Rows(path, "Courses");

        Convert(path);

        Assert.Equal(["a", "b", "w-civil"], TagIdsOf(path, "Sections", "s-ab"));
        Assert.Equal(["b", "z", "a", "w-civil"], TagIdsOf(path, "Sections", "s-bza"));
        Assert.Equal(untouched, Rows(path, "Sections", "id IN ('s-a','s-other-sem')"));
        Assert.Equal(courseBefore, Rows(path, "Courses"));
        Assert.True(IsWatched(Tags(path)["w-civil"]));
        // A field this version does not know survives the rewrite of the section's tagIds.
        Assert.Equal(1, JsonNode.Parse(Rows(path, "Sections", "id = 's-ab'").Single().Data)!["futureField"]!["a"]!.GetValue<int>());
    }

    /// <summary>Rule 3 with levels: tags AND a section level in the list; levels alone work too.</summary>
    [Fact]
    public void LevelsWatch_UsesTheSectionLevel()
    {
        var path = NewV1("levels");
        new Seeder(path).Semester("s1")
            .Tag("a", "A")
            .Section("s1-200", "s1", null, "200", ["a"]).Section("s1-100", "s1", null, "100", ["a"])
            .Section("s2-200", "s1", null, "200", []).Section("s3-none", "s1", null, null, ["a"])
            .Watch("w-tl", "s1", "Tag and level", tags: ["a"], levels: ["200"])
            .Watch("w-lo", "s1", "Levels only", levels: ["200", "300"]);

        Convert(path);

        // Watches are processed in id order within a semester, so w-lo's tag is added before w-tl's.
        Assert.Equal(["a", "w-lo", "w-tl"], TagIdsOf(path, "Sections", "s1-200"));
        Assert.Equal(["a"], TagIdsOf(path, "Sections", "s1-100"));
        Assert.Equal(["w-lo"], TagIdsOf(path, "Sections", "s2-200"));
        Assert.Equal(["a"], TagIdsOf(path, "Sections", "s3-none"));
        Assert.Equal(3, Tags(path).Count);
    }

    /// <summary>
    /// Rule 4 and 5: the same name (case and spaces ignored) in two semesters becomes ONE tag; its id is the first row's by
    /// SEMESTER order (not by watch id); each row tags its own semester's sections; watched when any row is enabled.
    /// </summary>
    [Fact]
    public void SameNameInTwoSemesters_IsOneTag_WithTheFirstSemestersId()
    {
        var path = NewV1("samename");
        new Seeder(path).Semester("fall", "2024-2025", 0).Semester("winter", "2025-2026", 0)
            .Tag("a", "A").Tag("b", "B")
            .Section("f1", "fall", null, null, ["a", "b"]).Section("f2", "fall", null, null, ["a"])
            .Section("w1", "winter", null, null, ["a", "b"]).Section("w2", "winter", null, null, ["b"])
            // watch ids sort the other way round from the semesters, so only semester order gives "w-z"
            .Watch("w-z", "fall", "Civil", enabled: false, tags: ["a", "b"])
            .Watch("w-a", "winter", " civil ", enabled: true, tags: ["a", "b"]);

        Convert(path);

        var tags = Tags(path);
        Assert.Equal(2 + 1, tags.Count);
        Assert.True(IsWatched(tags["w-z"]));
        Assert.Equal("Civil", tags["w-z"]["name"]!.GetValue<string>());
        Assert.Equal(["a", "b", "w-z"], TagIdsOf(path, "Sections", "f1"));
        Assert.Equal(["a"], TagIdsOf(path, "Sections", "f2"));
        Assert.Equal(["a", "b", "w-z"], TagIdsOf(path, "Sections", "w1"));
        Assert.Equal(["b"], TagIdsOf(path, "Sections", "w2"));
    }

    /// <summary>A group where every row is disabled gives an unwatched tag (Q39).</summary>
    [Fact]
    public void SameNameAllDisabled_IsAnUnwatchedTag()
    {
        var path = NewV1("samename_off");
        new Seeder(path).Semester("s1").Semester("s2", "2025-2026")
            .Tag("a", "A").Tag("b", "B")
            .Watch("w1", "s1", "X", enabled: false, tags: ["a", "b"]).Watch("w2", "s2", "X", enabled: false, tags: ["a", "b"]);

        Convert(path);

        Assert.False(IsWatched(Tags(path)["w1"]));
        Assert.False(Tags(path)["w1"].ContainsKey("isAccessWatch"));
    }

    /// <summary>A group that mixes rule 1 with another case: the single-tag row marks its own tag; the other rows share one new tag.</summary>
    [Fact]
    public void SameNameMixingASingleTagAndACourseList_MarksTheTag_AndCreatesOneNewTag()
    {
        var path = NewV1("mixed");
        new Seeder(path).Semester("s1").Semester("s2", "2025-2026")
            .Tag("t1", "CIVL Y3").Course("c1")
            .Section("a", "s2", "c1", null, [])
            .Watch("w1", "s1", "Civil", tags: ["t1"])
            .Watch("w2", "s2", "Civil", OldWatchMode.Course, courses: ["c1"]);

        Convert(path);

        var tags = Tags(path);
        Assert.Equal(2, tags.Count);
        Assert.True(IsWatched(tags["t1"]));
        Assert.Equal("Civil", tags["w2"]["name"]!.GetValue<string>());
        Assert.Equal(["w2"], TagIdsOf(path, "Sections", "a"));
    }

    /// <summary>Q37: a tag already has the watch's name: the new tag is "NAME (watch)", then "(watch 2)".</summary>
    [Fact]
    public void NameClash_GetsWatchThenWatch2()
    {
        var path = NewV1("clash");
        new Seeder(path).Semester("s1").Tag("a", "A").Tag("b", "B")
            .Tag("t-civil", "civil").Tag("t-civil2", "Civil (watch)")
            .Watch("w1", "s1", "Civil", tags: ["a", "b"]);
        var path2 = NewV1("clash1");
        new Seeder(path2).Semester("s1").Tag("a", "A").Tag("b", "B").Tag("t-civil", "Civil")
            .Watch("w1", "s1", "Civil", tags: ["a", "b"]);

        Convert(path);
        Convert(path2);

        Assert.Equal("Civil (watch 2)", Tags(path)["w1"]["name"]!.GetValue<string>());
        Assert.Equal("Civil (watch)", Tags(path2)["w1"]["name"]!.GetValue<string>());
        Assert.Equal("civil", Tags(path)["t-civil"]["name"]!.GetValue<string>());   // the other tag keeps its meaning
    }

    /// <summary>Q39: a disabled several-tags watch converts to an unwatched tag (sections tagged); a disabled single-tag watch does nothing.</summary>
    [Fact]
    public void DisabledWatches_BecomeUnwatchedTags()
    {
        var path = NewV1("disabled");
        new Seeder(path).Semester("s1").Tag("a", "A").Tag("b", "B")
            .Section("x", "s1", null, null, ["a", "b"])
            .Watch("w-multi", "s1", "Multi", enabled: false, tags: ["a", "b"])
            .Watch("w-one", "s1", "One", enabled: false, tags: ["a"]);

        Convert(path);

        var tags = Tags(path);
        Assert.False(IsWatched(tags["w-multi"]));
        Assert.Equal(["a", "b", "w-multi"], TagIdsOf(path, "Sections", "x"));
        Assert.False(IsWatched(tags["a"]));
        Assert.Equal(3, tags.Count);
    }

    /// <summary>A watch whose mode was written as text still converts (the table's own column also says which mode).</summary>
    [Fact]
    public void ModeAsText_IsUnderstood()
    {
        var path = NewV1("modetext");
        new Seeder(path).Semester("s1").Course("c1").Section("a", "s1", "c1", null, []);
        using (var c = Open(path))
            Exec(c, "INSERT INTO ProgramWatches (id, semester_id, name, mode, data) VALUES ('w1', 's1', 'Txt', 'course', $0)",
                "{\"name\":\"Txt\",\"mode\":\"Course\",\"courseIds\":[\"c1\"]}");   // no isEnabled member: enabled

        Convert(path);

        Assert.True(IsWatched(Tags(path)["w1"]));
        Assert.Equal(["w1"], TagIdsOf(path, "Sections", "a"));
    }

    /// <summary>Every watch row is gone, the version is 2, and a database without watches is unchanged by the step.</summary>
    [Fact]
    public void AfterConversion_NoWatchRowsRemain_AndTheVersionIsTwo()
    {
        var path = NewV1("done");
        new Seeder(path).Semester("s1").Tag("a", "A")
            .Watch("w1", "s1", "One", tags: ["a"]).Watch("w2", "s1", "Two", tags: ["a"], levels: ["100"]);

        Convert(path);

        Assert.Empty(Rows(path, "ProgramWatches"));
        Assert.Equal(2, UserVersion(path));
    }

    /// <summary>A version 1 file with no watches just gets the stamp.</summary>
    [Fact]
    public void VersionOneWithoutWatches_IsOnlyStamped()
    {
        var path = NewV1("nowatches");
        new Seeder(path).Semester("s1").Tag("a", "A").Section("x", "s1", null, null, ["a"]);
        var before = Dump(path, "SchedulingEnvironmentValues", "Sections");

        Convert(path);

        Assert.Equal(2, UserVersion(path));
        Assert.Equal(before, Dump(path, "SchedulingEnvironmentValues", "Sections"));
    }

    /// <summary>Two copies of the same unconverted file convert to identical rows (every computer that converts agrees).</summary>
    [Fact]
    public void ConvertingTwoCopies_GivesIdenticalRows()
    {
        var path = NewV1("orig");
        new Seeder(path).Semester("s1").Semester("s2", "2025-2026")
            .Tag("a", "A").Tag("b", "B").Tag("c", "Civil")
            .Course("c1", "a").Course("c2")
            .Section("x1", "s1", "c1", "100", ["a", "b"]).Section("x2", "s1", "c2", "200", ["a"])
            .Section("x3", "s2", "c1", "100", ["a", "b"], futureField: true)
            .Watch("w1", "s1", "Civil", tags: ["a", "b"]).Watch("w2", "s2", "Civil", tags: ["a", "b"], enabled: false)
            .Watch("w3", "s1", "Courses", OldWatchMode.Course, courses: ["c1", "c2"])
            .Watch("w4", "s1", "Just A", tags: ["a"]).Watch("w5", "s2", "Lv", levels: ["100"]);
        var copy = Path.Combine(_dir, "copy.db");
        File.Copy(path, copy);

        Convert(path);
        Convert(copy);

        string[] tables = ["SchedulingEnvironmentValues", "Sections", "Courses", "ProgramWatches"];
        Assert.Equal(Dump(path, tables), Dump(copy, tables));
        Assert.Equal(UserVersion(path), UserVersion(copy));
    }

    // ── Reopen paths ──────────────────────────────────────────────────────────

    /// <summary>A scratch context to reinitialize from.</summary>
    /// <param name="name">File name stem.</param>
    private DatabaseContext Scratch(string name) => new(Path.Combine(_dir, name + ".db"));

    /// <summary>
    /// A version 2 file reopened through <c>ReinitializeConnection</c> is untouched: same bytes, same last-write time,
    /// no backup file (the demotion that reopens the session's own, already converted D').
    /// </summary>
    [Fact]
    public void ReinitializeConnection_OnAVersionTwoFile_WritesNothing()
    {
        var v2 = NewV1("v2");
        new Seeder(v2).Semester("s1").Tag("a", "A").Watch("w1", "s1", "One", tags: ["a"]);
        Convert(v2);
        File.Delete(v2 + ".pre-migration.bak");   // the writer's own open made one; the reopen below must not make another
        var bytes = File.ReadAllBytes(v2);
        var stamp = File.GetLastWriteTimeUtc(v2);

        using (var ctx = Scratch("scratch1"))
        {
            ctx.ReinitializeConnection(v2, readOnly: true);
            Assert.True(ctx.IsReadOnly);
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal(bytes, File.ReadAllBytes(v2));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(v2));
        Assert.False(File.Exists(v2 + ".pre-migration.bak"));
    }

    /// <summary>
    /// A version 1 file reopened through <c>ReinitializeConnection</c> (a reader refreshing onto what a 1.2.4 writer saved)
    /// is converted before the session turns read-only, without a pre-migration backup; the session then refuses writes.
    /// </summary>
    [Fact]
    public void ReinitializeConnection_OnAVersionOneFile_ConvertsIt_ThenIsReadOnly()
    {
        var v1 = NewV1("v1");
        new Seeder(v1).Semester("s1").Tag("a", "A").Watch("w1", "s1", "One", tags: ["a"]);

        using var ctx = Scratch("scratch2");
        ctx.ReinitializeConnection(v1, readOnly: true);

        Assert.True(ctx.IsReadOnly);
        var tag = new SchedulingEnvironmentRepository(ctx).GetById("a")!;
        Assert.True(tag.IsAccessWatch);
        Assert.Equal(0, Count(ctx, "ProgramWatches"));
        Assert.False(File.Exists(v1 + ".pre-migration.bak"));
        Assert.Throws<WriteAccessLostException>(() => new SchedulingEnvironmentRepository(ctx).Insert("tag", new SchedulingEnvironmentValue { Name = "N" }));
        Assert.Equal(2, UserVersion(v1));
    }

    /// <summary>A conversion that fails rolls back, leaves the old rows, and still surfaces as the usual <see cref="InvalidOperationException"/>.</summary>
    [Fact]
    public void ReinitializeConnection_WhenTheConversionFails_ThrowsAndRollsBack()
    {
        var v1 = NewV1("v1fail");
        new Seeder(v1).Semester("s1").Tag("a", "A").Watch("w1", "s1", "One", tags: ["a", "a2"]).Watch("w2", "s1", "Two", tags: ["a", "a2"]);
        using (var c = Open(v1))
            Exec(c, "CREATE TRIGGER boom BEFORE INSERT ON SchedulingEnvironmentValues BEGIN SELECT RAISE(ABORT, 'boom'); END");

        using var ctx = Scratch("scratch3");
        Assert.Throws<InvalidOperationException>(() => ctx.ReinitializeConnection(v1, readOnly: true));
        SqliteConnection.ClearAllPools();

        Assert.Equal(2, Rows(v1, "ProgramWatches").Count);
        Assert.Equal(1, UserVersion(v1));
    }

    /// <summary>Counts the rows of a table through the context's connection.</summary>
    /// <param name="ctx">The context.</param>
    /// <param name="table">The table.</param>
    private static int Count(DatabaseContext ctx, string table)
    {
        using var cmd = ctx.Connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>The readable <c>name</c> column of a value row.</summary>
    /// <param name="path">The database.</param>
    /// <param name="id">The row id.</param>
    private static string ReadNameColumn(string path, string id)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM SchedulingEnvironmentValues WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return (string)cmd.ExecuteScalar()!;
    }

    // ── The first save after a conversion is a real save ─────────────────────

    /// <summary>
    /// The unchanged-save skip compares the snapshot with <c>ContentHashAtCheckout</c>, taken from D' as copied (before the
    /// context's constructor converts it). So a writer who checks out an unconverted D, lets the context convert D', and
    /// then saves with no edit of their own still writes: D ends at version 2 with no watch rows.
    /// </summary>
    [Fact]
    public async Task TheFirstSaveAfterAConversion_IsARealSave()
    {
        var d = Path.Combine(_dir, "shared", "dept.db");
        Directory.CreateDirectory(Path.GetDirectoryName(d)!);
        var built = NewV1("shared_src");
        new Seeder(built).Semester("s1").Tag("a", "A").Watch("w1", "s1", "One", tags: ["a"]);
        File.Copy(built, d);
        var workDir = Path.Combine(_dir, "work");
        Directory.CreateDirectory(workDir);

        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(lockService: lockSvc, logger: new QuietLogger(), dispatch: a => a(), workingDir: workDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(d));
        var hashAtCheckout = svc.ContentHashAtCheckout;

        using var ctx = new DatabaseContext(svc.WorkingPath, onFirstWrite: () => svc.MarkDirty(), onEveryWrite: () => svc.NoteEdit());
        Assert.Equal(0, Count(ctx, "ProgramWatches"));                 // D' is converted; nothing the user did
        Assert.False(File.Exists(svc.WorkingPath + ".dirty"));
        Assert.Equal(1, UserVersion(d));                               // D still unconverted

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        ctx.Dispose();
        SqliteConnection.ClearAllPools();
        Assert.Equal(2, UserVersion(d));
        Assert.Empty(Rows(d, "ProgramWatches"));
        Assert.True(IsWatched(Tags(d)["a"]));
        Assert.NotEqual(hashAtCheckout, svc.ContentHashAtCheckout);
        await svc.ReleaseAsync(saveFirst: false);
    }

    /// <summary>No-op logger.</summary>
    private sealed class QuietLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }
}
