using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Access Analyzer piece B, the small data pieces: the "written only when set" rule of the new persisted fields
/// (<c>isAccessWatch</c>, <c>watchMultiCampus</c>, <c>isComponent</c>, <c>componentTypeIds</c>), and the
/// <c>AccessChecksSetUp</c> mark in <c>AppConfiguration</c>.
/// </summary>
public sealed class AccessChecksDataTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates a private temp folder.</summary>
    public AccessChecksDataTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"acd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Releases handles and removes the temp folder.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── New fields: written only when set ─────────────────────────────────────

    /// <summary>A value row that sets none of the three flags is written exactly as before: no new members.</summary>
    [Fact]
    public void ValueFlags_AreNotWrittenWhenFalse()
    {
        var json = JsonHelpers.Serialize(new SchedulingEnvironmentValue { Id = "t", Name = "Tag" });

        Assert.DoesNotContain("isAccessWatch", json);
        Assert.DoesNotContain("watchMultiCampus", json);
        Assert.DoesNotContain("isComponent", json);
    }

    /// <summary>Each flag is written (camelCase) when true and reads back true.</summary>
    [Fact]
    public void ValueFlags_AreWrittenWhenTrue_AndReadBack()
    {
        var json = JsonHelpers.Serialize(new SchedulingEnvironmentValue
        {
            Id = "t", Name = "Tag", IsAccessWatch = true, WatchMultiCampus = true, IsComponent = true,
        });

        Assert.Contains("\"isAccessWatch\":true", json);
        var back = JsonHelpers.Deserialize<SchedulingEnvironmentValue>(json);
        Assert.True(back.IsAccessWatch);
        Assert.True(back.WatchMultiCampus);
        Assert.True(back.IsComponent);
    }

    /// <summary>A course's component list: null is never written, an empty list IS written, and the two read back as different answers.</summary>
    [Fact]
    public void ComponentTypeIds_KeepsNullDistinctFromEmpty()
    {
        var never = JsonHelpers.Serialize(new Course { Id = "c" });
        var empty = JsonHelpers.Serialize(new Course { Id = "c", ComponentTypeIds = [] });
        var some  = JsonHelpers.Serialize(new Course { Id = "c", ComponentTypeIds = ["st-1"] });

        Assert.DoesNotContain("componentTypeIds", never);
        Assert.Contains("\"componentTypeIds\":[]", empty);
        Assert.Null(JsonHelpers.Deserialize<Course>(never).ComponentTypeIds);
        Assert.Empty(JsonHelpers.Deserialize<Course>(empty).ComponentTypeIds!);
        Assert.Equal(["st-1"], JsonHelpers.Deserialize<Course>(some).ComponentTypeIds);
    }

    /// <summary>The fields survive a trip through the real repositories.</summary>
    [Fact]
    public void NewFields_RoundTripThroughTheRepositories()
    {
        using var db = new DatabaseContext(Path.Combine(_dir, "rt.db"));
        var env = new SchedulingEnvironmentRepository(db);
        env.Insert("tag", new SchedulingEnvironmentValue { Id = "t", Name = "Tag", IsAccessWatch = true, WatchMultiCampus = true });
        env.Insert("sectionType", new SchedulingEnvironmentValue { Id = "st", Name = "Lab", IsComponent = true });
        var courses = new CourseRepository(db);
        courses.Insert(new Course { Id = "c", SubjectId = "s", CalendarCode = "X1", Title = "T", ComponentTypeIds = ["st"] });

        Assert.True(env.GetById("t")!.IsAccessWatch);
        Assert.True(env.GetById("t")!.WatchMultiCampus);
        Assert.True(env.GetById("st")!.IsComponent);
        Assert.Equal(["st"], courses.GetById("c")!.ComponentTypeIds);
    }

    // ── AccessChecksSetUp ─────────────────────────────────────────────────────

    /// <summary>No row means "not set up" (the state of every database that has never run the wizard).</summary>
    [Fact]
    public void SetUpMark_IsAbsentByDefault()
    {
        using var db = new DatabaseContext(Path.Combine(_dir, "s1.db"));

        Assert.False(new AccessChecksSetupRepository(db).IsSetUp());
    }

    /// <summary>Marking writes <c>AccessChecksSetUp = 1</c>, counts as a write (the dirty callback fires), can be repeated, and persists.</summary>
    [Fact]
    public void MarkSetUp_WritesTheRow_ThroughMarkDirty_AndPersists()
    {
        var path = Path.Combine(_dir, "s2.db");
        var dirty = 0;
        using (var db = new DatabaseContext(path, onFirstWrite: () => dirty++))
        {
            var repo = new AccessChecksSetupRepository(db);
            repo.MarkSetUp();
            repo.MarkSetUp();
            Assert.True(repo.IsSetUp());
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal(1, dirty);
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = 'AccessChecksSetUp'";
        Assert.Equal("1", cmd.ExecuteScalar());
        using var reopened = new DatabaseContext(path);
        Assert.True(new AccessChecksSetupRepository(reopened).IsSetUp());
    }

    /// <summary>A read-only session cannot set the mark: the usual plain refusal, nothing written.</summary>
    [Fact]
    public void MarkSetUp_IsRefusedInAReadOnlySession()
    {
        using var db = new DatabaseContext(Path.Combine(_dir, "s3.db"), readOnly: true);
        var repo = new AccessChecksSetupRepository(db);

        Assert.Throws<WriteAccessLostException>(() => repo.MarkSetUp());
        Assert.False(repo.IsSetUp());
    }
}
