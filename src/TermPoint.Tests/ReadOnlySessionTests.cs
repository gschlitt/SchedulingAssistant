using Microsoft.Data.Sqlite;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// F4 (data-persistence review 2026-10): a read-only session must REFUSE a write before it happens, instead of
/// letting it "succeed" into the throw-away read-only copy where the user's change silently disappears.
///
/// <para>Pins the <see cref="DatabaseContext"/> side of that: the read-only flag set by the constructor and by
/// <see cref="DatabaseContext.ReinitializeConnection"/> and <see cref="DatabaseContext.MarkReadOnly"/>; the
/// refusal at <see cref="DatabaseContext.MarkDirty"/> (which every repository write calls first) with the exact
/// plain message; and that a writer session is untouched. A real SQLite file is used throughout so "the file is
/// unchanged" is checked against the database itself.</para>
/// </summary>
public sealed class ReadOnlySessionTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _dbPath;

    /// <summary>Creates a unique temporary folder holding the database path used by each test.</summary>
    public ReadOnlySessionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ro_session_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    /// <summary>Releases pooled connections and deletes the temporary folder.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a semester for a repository insert.</summary>
    /// <param name="name">The semester name.</param>
    private static Semester MakeSemester(string name) => new() { Name = name, AcademicYearId = "ay-1" };

    /// <summary>
    /// Counts the rows of <paramref name="table"/> through a brand-new, unpooled connection, so the answer comes
    /// from the database file itself and not from the context under test.
    /// </summary>
    /// <param name="table">A table name (test-controlled, not user input).</param>
    private int CountRows(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── The refusal ───────────────────────────────────────────────────────────

    /// <summary>
    /// A read-only context refuses a repository write with <see cref="WriteAccessLostException"/> (whose message is
    /// the exact plain sentence), and nothing is written: the row count in the file is the same afterwards.
    /// </summary>
    [Fact]
    public void ReadOnlyContext_RefusesARepositoryWrite_WithThePlainMessage_AndWritesNothing()
    {
        using var db = new DatabaseContext(_dbPath, readOnly: true);
        var repo = new SemesterRepository(db);
        var before = CountRows("Semesters");

        var ex = Assert.Throws<WriteAccessLostException>(() => repo.Insert(MakeSemester("Fall")));

        Assert.Equal("You no longer have write access, so this change wasn't saved.", ex.Message);
        Assert.Equal(before, CountRows("Semesters"));
        Assert.Empty(repo.GetAll());
    }

    /// <summary>
    /// Every kind of repository write is refused, not just inserts: update and delete go through the same first
    /// step, so the same exception comes back and the stored row is untouched.
    /// </summary>
    [Fact]
    public void ReadOnlyContext_RefusesUpdateAndDelete_AndTheStoredRowIsUntouched()
    {
        var semester = MakeSemester("Fall");
        using (var writer = new DatabaseContext(_dbPath))
            new SemesterRepository(writer).Insert(semester);

        using var db = new DatabaseContext(_dbPath, readOnly: true);
        var repo = new SemesterRepository(db);

        semester.Name = "Renamed";
        Assert.Throws<WriteAccessLostException>(() => repo.Update(semester));
        Assert.Throws<WriteAccessLostException>(() => repo.Delete(semester.Id));

        var stored = Assert.Single(repo.GetAll());
        Assert.Equal("Fall", stored.Name);
    }

    /// <summary>A refused write must not fire the first-write callback: no dirty marker for a change that never happened.</summary>
    [Fact]
    public void ReadOnlyContext_RefusedWrite_DoesNotFireTheDirtyCallback()
    {
        var fired = 0;
        using var db = new DatabaseContext(_dbPath, () => fired++, readOnly: true);

        Assert.Throws<WriteAccessLostException>(() => new SemesterRepository(db).Insert(MakeSemester("Fall")));

        Assert.Equal(0, fired);
    }

    /// <summary>Reading still works in a read-only session (it is only writes that are refused).</summary>
    [Fact]
    public void ReadOnlyContext_StillReads()
    {
        var semester = MakeSemester("Fall");
        using (var writer = new DatabaseContext(_dbPath))
            new SemesterRepository(writer).Insert(semester);

        using var db = new DatabaseContext(_dbPath, readOnly: true);

        Assert.Equal(semester.Id, Assert.Single(new SemesterRepository(db).GetAll()).Id);
    }

    // ── The writer is untouched ───────────────────────────────────────────────

    /// <summary>The default context is a writer: writes succeed and the first-write callback fires exactly once.</summary>
    [Fact]
    public void WriterContext_Default_WritesNormally_AndFiresTheCallbackOnce()
    {
        var fired = 0;
        using var db = new DatabaseContext(_dbPath, () => fired++);
        var repo = new SemesterRepository(db);

        repo.Insert(MakeSemester("Fall"));
        repo.Insert(MakeSemester("Winter"));

        Assert.False(db.IsReadOnly);
        Assert.Equal(2, CountRows("Semesters"));
        Assert.Equal(1, fired);
    }

    /// <summary>
    /// A read-only session that has ended leaves nothing behind for the next writer: a new context on the same
    /// file writes normally (the flag lives on the context, not in the file or the connection pool).
    /// </summary>
    [Fact]
    public void AfterAReadOnlySession_ANewWriterContextOnTheSameFile_Writes()
    {
        using (var reader = new DatabaseContext(_dbPath, readOnly: true))
            Assert.Throws<WriteAccessLostException>(() => new SemesterRepository(reader).Insert(MakeSemester("Fall")));

        using var writer = new DatabaseContext(_dbPath);
        new SemesterRepository(writer).Insert(MakeSemester("Fall"));

        Assert.Equal(1, CountRows("Semesters"));
    }

    // ── Init still runs on an older read-only snapshot ────────────────────────

    /// <summary>
    /// The read-only flag is applied at the END of the constructor: opening a snapshot of an OLDER database read-only
    /// still creates the tables it lacks, runs the migration (stamping <c>user_version</c>) and seeds the default
    /// academic unit, because the readers need them. Only afterwards is a user write refused.
    /// </summary>
    [Fact]
    public void ReadOnlyConstructor_OnAnOlderSchemaCopy_StillCompletesInit_ThenRefusesWrites()
    {
        // An old-format file: only the legacy table that triggers the rename migration.
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE SectionPropertyValues (id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var db = new DatabaseContext(_dbPath, readOnly: true);

        Assert.True(db.IsReadOnly);
        Assert.Equal(0, CountRows("Semesters"));                      // created by the schema init
        Assert.Equal(0, CountRows("ProgramWatches"));                 // ditto
        Assert.Equal(1, CountRows("AcademicUnits"));                  // seeded default unit
        using (var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version";
            Assert.Equal(DatabaseContext.CurrentSchemaVersion, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        Assert.Throws<WriteAccessLostException>(() => new SemesterRepository(db).Insert(MakeSemester("Fall")));
    }

    // ── Setting and clearing the flag ─────────────────────────────────────────

    /// <summary>
    /// <see cref="DatabaseContext.ReinitializeConnection"/> sets the flag when asked for a read-only session and
    /// clears it when asked for a writable one, and the repository follows.
    /// </summary>
    [Fact]
    public void ReinitializeConnection_SetsAndClearsTheFlag()
    {
        using var db = new DatabaseContext(_dbPath);
        var repo = new SemesterRepository(db);
        repo.Insert(MakeSemester("Fall"));

        db.ReinitializeConnection(_dbPath, readOnly: true);
        Assert.True(db.IsReadOnly);
        Assert.Throws<WriteAccessLostException>(() => repo.Insert(MakeSemester("Winter")));
        Assert.Equal(1, CountRows("Semesters"));

        db.ReinitializeConnection(_dbPath, readOnly: false);
        Assert.False(db.IsReadOnly);
        repo.Insert(MakeSemester("Winter"));
        Assert.Equal(2, CountRows("Semesters"));
    }

    /// <summary>
    /// <see cref="DatabaseContext.MarkReadOnly"/> (the first step of a demotion) refuses writes at once, with the
    /// connection still open, so reads keep working while the read-only copy is being made.
    /// </summary>
    [Fact]
    public void MarkReadOnly_RefusesWritesImmediately_AndKeepsTheConnectionReadable()
    {
        using var db = new DatabaseContext(_dbPath);
        var repo = new SemesterRepository(db);
        repo.Insert(MakeSemester("Fall"));

        db.MarkReadOnly();

        Assert.True(db.IsReadOnly);
        Assert.Throws<WriteAccessLostException>(() => repo.Insert(MakeSemester("Winter")));
        Assert.Single(repo.GetAll());
    }

    /// <summary>
    /// <see cref="DatabaseContext.CloseConnection"/> on its own does NOT mark the session read-only: a switch of
    /// database or a restore closes the connection too, and must never produce a "lost write access" message.
    /// </summary>
    [Fact]
    public void CloseConnection_Alone_DoesNotMarkTheSessionReadOnly()
    {
        using var db = new DatabaseContext(_dbPath);

        db.CloseConnection();

        Assert.False(db.IsReadOnly);
        db.ReinitializeConnection(_dbPath, readOnly: false);
        new SemesterRepository(db).Insert(MakeSemester("Fall"));
        Assert.Equal(1, CountRows("Semesters"));
    }

    /// <summary>
    /// A reopen that fails leaves the flag as it was: a demotion that has marked the session read-only and then
    /// cannot reopen the file keeps refusing writes (rather than falling back to writable).
    /// </summary>
    [Fact]
    public void ReinitializeConnection_WhenTheReopenFails_KeepsTheReadOnlyFlag()
    {
        using var db = new DatabaseContext(_dbPath);
        db.MarkReadOnly();

        Assert.Throws<InvalidOperationException>(
            () => db.ReinitializeConnection(Path.Combine(_tempDir, "vanished.db"), readOnly: false));

        Assert.True(db.IsReadOnly);
    }

    // ── The Refresh button ────────────────────────────────────────────────────

    /// <summary>
    /// The Refresh button re-copies D to D'' only in a read-only session, and decides that from the checkout
    /// <see cref="CheckoutMode"/> — never from the sticky <c>IsReadOnlyMode</c> flag, which stays true after the
    /// same window switches from reader to writer.
    /// </summary>
    [Theory]
    [InlineData(CheckoutMode.ReadOnly, true)]
    [InlineData(CheckoutMode.WriteAccess, false)]
    public void RefreshData_ReReadsTheSnapshot_OnlyWhenTheCheckoutModeIsReadOnly(CheckoutMode mode, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.RefreshesReadOnlySnapshot(mode));
}
