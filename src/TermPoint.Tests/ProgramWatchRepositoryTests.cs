using Microsoft.Data.Sqlite;
using System.Data.Common;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Data.Repositories.Demo;
using TermPoint.Models;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// CRUD tests for <see cref="ProgramWatchRepository"/> (SQLite) and
/// <see cref="DemoProgramWatchRepository"/> (in-memory demo).
/// </summary>
public sealed class ProgramWatchRepositoryTests : IDisposable
{
    private readonly TestDatabaseContext _db;
    private readonly ProgramWatchRepository _repo;

    public ProgramWatchRepositoryTests()
    {
        _db = new TestDatabaseContext();
        _repo = new ProgramWatchRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsEmptyForNewSemester()
    {
        var result = _repo.GetAll("sem-1");
        Assert.Empty(result);
    }

    [Fact]
    public void GetAll_ReturnsOnlyWatchesForRequestedSemester()
    {
        var w1 = MakeWatch("Watch A", ProgramWatchMode.Tag);
        var w2 = MakeWatch("Watch B", ProgramWatchMode.Course);
        _repo.Save("sem-1", w1);
        _repo.Save("sem-2", w2);

        var result = _repo.GetAll("sem-1");
        Assert.Single(result);
        Assert.Equal(w1.Id, result[0].Id);
    }

    [Fact]
    public void GetAll_ReturnsWatchesOrderedByName()
    {
        var w1 = MakeWatch("Zebra Watch", ProgramWatchMode.Tag);
        var w2 = MakeWatch("Alpha Watch", ProgramWatchMode.Course);
        var w3 = MakeWatch("Middle Watch", ProgramWatchMode.Tag);
        _repo.Save("sem-1", w1);
        _repo.Save("sem-1", w2);
        _repo.Save("sem-1", w3);

        var result = _repo.GetAll("sem-1");
        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha Watch", result[0].Name);
        Assert.Equal("Middle Watch", result[1].Name);
        Assert.Equal("Zebra Watch", result[2].Name);
    }

    // ── Save (insert) ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_InsertsNewWatch()
    {
        var watch = MakeWatch("CS Core", ProgramWatchMode.Tag);
        watch.TagIds = ["tag-1", "tag-2"];

        _repo.Save("sem-1", watch);

        var result = _repo.GetAll("sem-1");
        Assert.Single(result);
        var saved = result[0];
        Assert.Equal(watch.Id, saved.Id);
        Assert.Equal("CS Core", saved.Name);
        Assert.Equal(ProgramWatchMode.Tag, saved.Mode);
        Assert.True(saved.IsEnabled);
        Assert.Equal(["tag-1", "tag-2"], saved.TagIds);
        Assert.Empty(saved.CourseIds);
    }

    [Fact]
    public void Save_InsertsCourseBasedWatch()
    {
        var watch = MakeWatch("Year 1 Sciences", ProgramWatchMode.Course);
        watch.CourseIds = ["crs-1", "crs-2", "crs-3"];

        _repo.Save("sem-1", watch);

        var result = _repo.GetAll("sem-1");
        Assert.Single(result);
        var saved = result[0];
        Assert.Equal(ProgramWatchMode.Course, saved.Mode);
        Assert.Equal(["crs-1", "crs-2", "crs-3"], saved.CourseIds);
        Assert.Empty(saved.TagIds);
    }

    // ── Save (update) ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_UpdatesExistingWatch()
    {
        var watch = MakeWatch("Original", ProgramWatchMode.Tag);
        watch.TagIds = ["tag-1"];
        _repo.Save("sem-1", watch);

        watch.Name = "Renamed";
        watch.IsEnabled = false;
        watch.TagIds = ["tag-1", "tag-2"];
        _repo.Save("sem-1", watch);

        var result = _repo.GetAll("sem-1");
        Assert.Single(result);
        Assert.Equal("Renamed", result[0].Name);
        Assert.False(result[0].IsEnabled);
        Assert.Equal(["tag-1", "tag-2"], result[0].TagIds);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesWatch()
    {
        var watch = MakeWatch("Doomed", ProgramWatchMode.Tag);
        _repo.Save("sem-1", watch);

        _repo.Delete(watch.Id);

        Assert.Empty(_repo.GetAll("sem-1"));
    }

    [Fact]
    public void Delete_NonexistentId_DoesNotThrow()
    {
        _repo.Delete("nonexistent-id");
    }

    // ── Demo repository ───────────────────────────────────────────────────────

    [Fact]
    public void Demo_RoundTripsCrud()
    {
        IProgramWatchRepository demo = new DemoProgramWatchRepository();

        var watch = MakeWatch("Demo Watch", ProgramWatchMode.Course);
        watch.CourseIds = ["c-1"];

        demo.Save("sem-1", watch);
        Assert.Single(demo.GetAll("sem-1"));
        Assert.Empty(demo.GetAll("sem-other"));

        watch.Name = "Updated";
        demo.Save("sem-1", watch);
        var all = demo.GetAll("sem-1");
        Assert.Single(all);
        Assert.Equal("Updated", all[0].Name);

        demo.Delete(watch.Id);
        Assert.Empty(demo.GetAll("sem-1"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProgramWatch MakeWatch(string name, ProgramWatchMode mode) => new()
    {
        Name = name,
        Mode = mode,
    };

    /// <summary>
    /// Minimal <see cref="IDatabaseContext"/> backed by an in-memory SQLite database
    /// with the ProgramWatches table pre-created.
    /// </summary>
    private sealed class TestDatabaseContext : IDatabaseContext
    {
        private readonly SqliteConnection _conn;

        public TestDatabaseContext()
        {
            var id = Guid.NewGuid().ToString("N");
            _conn = new SqliteConnection($"Data Source=file:testdb_{id}?mode=memory&cache=shared;");
            _conn.Open();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ProgramWatches (
                    id          TEXT PRIMARY KEY,
                    semester_id TEXT NOT NULL,
                    name        TEXT,
                    mode        TEXT,
                    data        TEXT NOT NULL DEFAULT '{}'
                )
                """;
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
