using Microsoft.Data.Sqlite;
using Moq;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Data.Repositories.Demo;
using TermPoint.Models;
using TermPoint.Services;
using System;
using System.Data.Common;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for F5 and F14 from the data-integrity audit (2026-05-04).
///
/// <list type="bullet">
///   <item>
///     <description>F5 — <see cref="SectionRepository.Delete"/> now accepts an optional
///     <see cref="DbTransaction"/> so callers can enlist deletions in a larger atomic
///     operation, consistent with <c>Insert</c> and <c>Update</c>.</description>
///   </item>
///   <item>
///     <description>F14 — <c>CopySemesterViewModel.ExecuteCopy</c> used to call
///     <c>_db.Connection.BeginTransaction()</c> unconditionally, which throws
///     <see cref="NotSupportedException"/> in the WASM demo context
///     (<c>SupportsTransactions = false</c>). The nullable-tx pattern fixes this.</description>
///   </item>
/// </list>
/// </summary>
public sealed class RepositoryTransactionTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly TestDatabaseContext _db;

    public RepositoryTransactionTests() => _db = new TestDatabaseContext();

    /// <summary>Disposes the in-memory SQLite connection.</summary>
    public void Dispose() => _db.Dispose();

    // ── Group 1 — F5: SectionRepository.Delete participates in transactions ──

    /// <summary>
    /// <see cref="SectionRepository.Delete"/> must accept an optional
    /// <see cref="DbTransaction"/>. When the caller's transaction is rolled back,
    /// the deleted section must be restored — it was never committed.
    ///
    /// <para>This tests the F5 fix: without <c>cmd.Transaction = tx</c>, the DELETE would
    /// execute outside the caller's transaction and would be permanent even on rollback.</para>
    /// </summary>
    [Fact]
    public void Delete_WithTransaction_RollsBackOnRollback()
    {
        // Arrange — insert a section outside any transaction so it exists in the DB
        var repo = new SectionRepository(_db);
        var section = new Section
        {
            Id         = Guid.NewGuid().ToString(),
            SemesterId = "sem-1",
            CourseId   = "crs-1",
        };
        repo.Insert(section);   // no tx — permanent

        using var tx = _db.Connection.BeginTransaction();

        // Act — delete inside the transaction, then roll back
        repo.Delete(section.Id, tx);
        tx.Rollback();

        // Assert — section still exists because the delete was rolled back
        var found = repo.GetById(section.Id);
        Assert.NotNull(found);
    }

    /// <summary>
    /// When <see cref="SectionRepository.Delete"/> is called without a transaction (the default),
    /// the deletion is committed immediately. This covers the existing no-transaction call sites.
    /// </summary>
    [Fact]
    public void Delete_WithoutTransaction_IsImmediate()
    {
        // Arrange
        var repo = new SectionRepository(_db);
        var section = new Section
        {
            Id         = Guid.NewGuid().ToString(),
            SemesterId = "sem-1",
            CourseId   = "crs-1",
        };
        repo.Insert(section);

        // Act — no transaction passed (default null)
        repo.Delete(section.Id);

        // Assert — immediately gone
        var found = repo.GetById(section.Id);
        Assert.Null(found);
    }

    /// <summary>
    /// <see cref="SectionRepository.Delete"/> with a committed transaction must actually
    /// remove the section.
    /// </summary>
    [Fact]
    public void Delete_WithCommittedTransaction_RemovesSection()
    {
        // Arrange
        var repo = new SectionRepository(_db);
        var section = new Section
        {
            Id         = Guid.NewGuid().ToString(),
            SemesterId = "sem-1",
            CourseId   = "crs-1",
        };
        repo.Insert(section);

        using var tx = _db.Connection.BeginTransaction();

        // Act
        repo.Delete(section.Id, tx);
        tx.Commit();

        // Assert
        Assert.Null(repo.GetById(section.Id));
    }

    // ── Group 2 — F14: DemoSectionRepository.Delete signature matches interface ──

    /// <summary>
    /// <see cref="DemoSectionRepository"/> must implement <c>Delete(string, DbTransaction?)</c>
    /// to satisfy the updated <see cref="ISectionRepository"/> interface.
    /// This test verifies the demo implementation compiles and behaves correctly when
    /// called with a null transaction (the WASM demo never has a real transaction).
    /// </summary>
    [Fact]
    public void DemoSectionRepository_Delete_WithNullTransaction_RemovesSection()
    {
        // Arrange
        ISectionRepository repo = new DemoSectionRepository();

        var section = new Section
        {
            Id         = Guid.NewGuid().ToString(),
            SemesterId = "sem-demo",
            CourseId   = "crs-demo",
            SectionCode = "A",
        };
        repo.Insert(section);   // no-tx insert (compatible with demo context)

        // Act — explicit null tx (as WASM code will call it)
        repo.Delete(section.Id, tx: null);

        // Assert
        Assert.Null(repo.GetById(section.Id));
    }

    // ── Support types ─────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IDatabaseContext"/> backed by an in-memory SQLite database
    /// with the Sections table pre-created. Suitable for repository unit tests that
    /// need real SQL execution without the full <see cref="DatabaseContext"/> migration stack.
    /// </summary>
    private sealed class TestDatabaseContext : IDatabaseContext
    {
        private readonly SqliteConnection _conn;

        /// <summary>Opens an in-memory connection and creates the tables required by SectionRepository.</summary>
        public TestDatabaseContext()
        {
            // Named in-memory DB so transactions work (shared-cache keeps it alive).
            var id = Guid.NewGuid().ToString("N");
            _conn = new SqliteConnection($"Data Source=file:testdb_{id}?mode=memory&cache=shared;");
            _conn.Open();

            // Create all tables referenced by SectionRepository.Insert / Delete.
            // The subqueries in Insert return NULL for empty lookup tables, which is fine
            // for transaction-behavior tests that don't care about denormalised columns.
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Sections (
                    id                  TEXT NOT NULL PRIMARY KEY,
                    semester_id         TEXT NOT NULL,
                    course_id           TEXT NOT NULL,
                    room_id             TEXT,
                    section_code        TEXT,
                    course_code         TEXT,
                    semester_name       TEXT,
                    academic_year_name  TEXT,
                    data                TEXT NOT NULL DEFAULT '{}'
                );
                CREATE TABLE IF NOT EXISTS Courses (
                    id   TEXT NOT NULL PRIMARY KEY,
                    data TEXT NOT NULL DEFAULT '{}'
                );
                CREATE TABLE IF NOT EXISTS Semesters (
                    id              TEXT NOT NULL PRIMARY KEY,
                    academic_year_id TEXT,
                    name            TEXT,
                    data            TEXT NOT NULL DEFAULT '{}'
                );
                CREATE TABLE IF NOT EXISTS AcademicYears (
                    id   TEXT NOT NULL PRIMARY KEY,
                    name TEXT,
                    data TEXT NOT NULL DEFAULT '{}'
                )";
            cmd.ExecuteNonQuery();
        }

        /// <inheritdoc/>
        public DbConnection Connection => _conn;

        /// <inheritdoc/>
        public string DatabasePath => string.Empty;

        /// <inheritdoc/>
        public bool SupportsTransactions => true;

        /// <inheritdoc/>
        public void MarkDirty() { }

        /// <inheritdoc/>
        public void ResetDirty() { }

        /// <inheritdoc/>
        public void Dispose() => _conn.Dispose();
    }
}
