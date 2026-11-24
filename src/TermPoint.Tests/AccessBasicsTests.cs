using Microsoft.Data.Sqlite;
using System.Data.Common;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Data.Repositories.Demo;
using TermPoint.Models;
using TermPoint.Services.Access;
using Xunit;
using static TermPoint.Tests.AccessTestKit;

namespace TermPoint.Tests;

/// <summary>
/// Access engine: the conflict test (rule 13), the component-name list (Q18) and the suggestion query
/// (<see cref="ISectionRepository.GetSectionTypeIdsByCourse"/>).
/// </summary>
public sealed class AccessBasicsTests
{
    // ── Rule 13: the conflict test ───────────────────────────────────────────

    [Fact]
    public void Conflicts_SameDayOverlappingTimes_True() =>
        Assert.True(SectionOverlap.Conflicts(Sec("A", "1", meetings: At(0, 9, 10)), Sec("B", "1", meetings: At(0, 9.5, 10.5))));

    [Fact]
    public void Conflicts_HalfOpenEdge_OneEndingWhenTheOtherStartsDoesNotConflict() =>
        Assert.False(SectionOverlap.Conflicts(Sec("A", "1", meetings: At(0, 9, 10)), Sec("B", "1", meetings: At(0, 10, 11))));

    [Fact]
    public void Conflicts_DifferentDays_False() =>
        Assert.False(SectionOverlap.Conflicts(Sec("A", "1", meetings: At(0, 9, 10)), Sec("B", "1", meetings: At(1, 9, 10))));

    [Fact]
    public void Conflicts_OddAndEvenWeeks_False() =>
        Assert.False(SectionOverlap.Conflicts(
            Sec("A", "1", meetings: At(0, 9, 10, "odd")), Sec("B", "1", meetings: At(0, 9, 10, "even"))));

    [Fact]
    public void Conflicts_OddAndEveryWeek_True() =>
        Assert.True(SectionOverlap.Conflicts(
            Sec("A", "1", meetings: At(0, 9, 10, "odd")), Sec("B", "1", meetings: At(0, 9, 10))));

    [Fact]
    public void Conflicts_WeekListsShareAWeek_True() =>
        Assert.True(SectionOverlap.Conflicts(
            Sec("A", "1", meetings: At(0, 9, 10, "1,6,7")), Sec("B", "1", meetings: At(0, 9, 10, "7,8"))));

    [Fact]
    public void Conflicts_WeekListsDisjoint_False() =>
        Assert.False(SectionOverlap.Conflicts(
            Sec("A", "1", meetings: At(0, 9, 10, "1,6")), Sec("B", "1", meetings: At(0, 9, 10, "2,3"))));

    [Fact]
    public void Conflicts_SectionWithNoMeetings_ConflictsWithNothing()
    {
        var none = Sec("A", "1");
        Assert.False(SectionOverlap.Conflicts(none, Sec("B", "1", meetings: At(0, 9, 10))));
        Assert.False(SectionOverlap.Conflicts(none, Sec("C", "1")));
    }

    [Fact]
    public void OverlappingMeetings_ReturnsEveryOverlappingPair()
    {
        var a = Sec("A", "1", meetings: At(Mwf, 9, 10));
        var b = Sec("B", "1", meetings: At(Mw, 9.5, 10.5));
        var pairs = SectionOverlap.OverlappingMeetings(a, b);
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(p.A.Day, p.B.Day));
        Assert.Empty(SectionOverlap.OverlappingMeetings(a, Sec("C", "1", meetings: At(1, 9, 10))));
    }

    // ── Q18: the component-name list ─────────────────────────────────────────

    [Theory]
    [InlineData("Lecture")] [InlineData("LEC")] [InlineData("Laboratory")] [InlineData("Lab")] [InlineData("LAB")]
    [InlineData("tutorial")] [InlineData("TUT")] [InlineData("Discussion")] [InlineData("DIS")] [InlineData("Recitation")]
    [InlineData("REC")] [InlineData("Seminar")] [InlineData("SEM")] [InlineData("Studio")] [InlineData("STU")]
    [InlineData("Workshop")] [InlineData("WKS")] [InlineData("Practicum")] [InlineData("PRA")]
    [InlineData("Lab Section")] [InlineData("Lecture/Lab")]
    public void LooksLikeComponent_NamesOnTheList_True(string name) => Assert.True(ComponentNames.LooksLikeComponent(name));

    [Theory]
    [InlineData("Online")] [InlineData("Thesis")] [InlineData("Independent Study")] [InlineData("F2F")]
    [InlineData("Labour")] [InlineData("Collaboration")] [InlineData("")] [InlineData("   ")] [InlineData(null)]
    public void LooksLikeComponent_OtherNames_False(string? name) => Assert.False(ComponentNames.LooksLikeComponent(name));

    // ── The suggestion query ─────────────────────────────────────────────────

    [Fact]
    public void Demo_GetSectionTypeIdsByCourse_DistinctNonNullTypesPerCourse()
    {
        var repo = new DemoSectionRepository();
        foreach (var s in repo.GetAll()) repo.Delete(s.Id);
        repo.Insert(Sec("c1", "A", "t-lec"));
        repo.Insert(Sec("c1", "B", "t-lec"));
        repo.Insert(Sec("c1", "L1", "t-lab"));
        repo.Insert(Sec("c1", "X", null));
        repo.Insert(Sec("c2", "A", null));
        var result = repo.GetSectionTypeIdsByCourse();
        Assert.Equal(["t-lab", "t-lec"], result["c1"].Order().ToArray());
        Assert.False(result.ContainsKey("c2"));
    }

    [Fact]
    public void Sqlite_GetSectionTypeIdsByCourse_DistinctAcrossSemestersAndExcludesOrphans()
    {
        using var db = new SuggestionDb();
        db.Exec("INSERT INTO Semesters (id) VALUES ('s1'), ('s2')");
        var repo = new SectionRepository(db);
        repo.Insert(MakeSection("a", "s1", "c1", "t-lec"));
        repo.Insert(MakeSection("b", "s2", "c1", "t-lec"));
        repo.Insert(MakeSection("c", "s2", "c1", "t-lab"));
        repo.Insert(MakeSection("d", "s1", "c1", null));
        repo.Insert(MakeSection("e", "s1", "c2", null));
        repo.Insert(MakeSection("f", "gone", "c3", "t-lec"));   // orphan: its semester no longer exists

        var result = repo.GetSectionTypeIdsByCourse();

        Assert.Equal(["t-lab", "t-lec"], result["c1"].Order().ToArray());
        Assert.False(result.ContainsKey("c2"));
        Assert.False(result.ContainsKey("c3"));
    }

    private static Section MakeSection(string id, string semesterId, string courseId, string? typeId) =>
        new() { Id = id, SemesterId = semesterId, CourseId = courseId, SectionCode = id, SectionTypeId = typeId };

    /// <summary>An in-memory SQLite database with the tables <see cref="SectionRepository"/> reads and writes.</summary>
    private sealed class SuggestionDb : IDatabaseContext
    {
        private readonly SqliteConnection _conn;

        public SuggestionDb()
        {
            _conn = new SqliteConnection($"Data Source=file:sugg_{Guid.NewGuid():N}?mode=memory&cache=shared;");
            _conn.Open();
            Exec("""
                CREATE TABLE Sections (id TEXT NOT NULL PRIMARY KEY, semester_id TEXT NOT NULL, course_id TEXT NOT NULL,
                    room_id TEXT, section_code TEXT, course_code TEXT, semester_name TEXT, academic_year_name TEXT,
                    data TEXT NOT NULL DEFAULT '{}');
                CREATE TABLE Courses (id TEXT NOT NULL PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}');
                CREATE TABLE Semesters (id TEXT NOT NULL PRIMARY KEY, academic_year_id TEXT, name TEXT, data TEXT NOT NULL DEFAULT '{}');
                CREATE TABLE AcademicYears (id TEXT NOT NULL PRIMARY KEY, name TEXT, data TEXT NOT NULL DEFAULT '{}');
                """);
        }

        public void Exec(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public DbConnection Connection => _conn;
        public string DatabasePath => string.Empty;
        public bool SupportsTransactions => true;
        public void MarkDirty() { }
        public void ResetDirty() { }
        public void Dispose() => _conn.Dispose();
    }
}
