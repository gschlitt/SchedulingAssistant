using Microsoft.Data.Sqlite;
using SchedulingAssistant.Data;
using System;
using System.IO;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for F7 from the data-integrity audit (2026-05-04):
/// the <c>SectionPropertyValues → SchedulingEnvironmentValues</c> rename migration must
/// refuse to run — rather than silently dropping data — when both tables coexist and the
/// destination already contains rows.
///
/// <para>The guard added in <see cref="DatabaseContext"/> checks the row count in
/// <c>SchedulingEnvironmentValues</c> before issuing <c>DROP TABLE</c>. If any rows
/// are present it throws <see cref="InvalidOperationException"/> with a "Migration conflict"
/// message, which the constructor wraps and propagates. This prevents the rare but
/// catastrophic partial-migration scenario where real data would have been silently
/// destroyed by the rename sequence.</para>
/// </summary>
public sealed class MigrationGuardTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    public MigrationGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mgr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes temporary files created during the test.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // release any lingering handles before deletion
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a SQLite database at <paramref name="path"/> that simulates a
    /// partially-migrated database: <c>SectionPropertyValues</c> (old name) still exists,
    /// and <c>SchedulingEnvironmentValues</c> (new name) already contains
    /// <paramref name="rowsInNewTable"/> rows.
    /// </summary>
    /// <param name="path">File path for the new database.</param>
    /// <param name="rowsInNewTable">
    /// Number of rows to pre-seed into <c>SchedulingEnvironmentValues</c>.
    /// Pass 0 to test the safe path (empty destination → rename proceeds normally).
    /// </param>
    private static void CreatePartiallyMigratedDb(string path, int rowsInNewTable)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();

        using var cmd = conn.CreateCommand();

        // Create the old table (rename source)
        cmd.CommandText = "CREATE TABLE SectionPropertyValues (id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}')";
        cmd.ExecuteNonQuery();

        // Create the new table (rename destination — simulates a partial previous migration)
        // Minimal columns sufficient for DatabaseContext.InitializeSchema's CREATE TABLE IF NOT EXISTS
        // to be a no-op (the IF NOT EXISTS guard prevents schema mismatch errors).
        cmd.CommandText = @"
            CREATE TABLE SchedulingEnvironmentValues (
                id          TEXT PRIMARY KEY,
                name        TEXT,
                semester_id TEXT,
                data        TEXT NOT NULL DEFAULT '{}'
            )";
        cmd.ExecuteNonQuery();

        // Optionally seed rows into the new table
        for (int i = 0; i < rowsInNewTable; i++)
        {
            cmd.CommandText = $"INSERT INTO SchedulingEnvironmentValues (id, name) VALUES ('{Guid.NewGuid()}', 'env-{i}')";
            cmd.ExecuteNonQuery();
        }
    }

    // ── Group 1 — F7: Migration conflict guard ────────────────────────────────

    /// <summary>
    /// When <c>SectionPropertyValues</c> and <c>SchedulingEnvironmentValues</c> both exist,
    /// and <c>SchedulingEnvironmentValues</c> already has rows, the migration must throw
    /// <see cref="InvalidOperationException"/> rather than silently dropping the rows.
    ///
    /// <para>The <see cref="DatabaseContext"/> constructor catches migration exceptions and
    /// re-throws them as a wrapping <see cref="InvalidOperationException"/> with an
    /// <c>InnerException</c>. This test asserts both the outer and inner types and checks
    /// for the "Migration conflict" sentinel phrase in the inner message.</para>
    /// </summary>
    [Fact]
    public void DatabaseContext_MigrationConflict_ThrowsRatherThanDropsData()
    {
        // Arrange — partially migrated DB: old table present, new table has 3 rows
        var path = Path.Combine(_tempDir, "partial_migration.db");
        CreatePartiallyMigratedDb(path, rowsInNewTable: 3);

        // Act + Assert
        var outerEx = Assert.Throws<InvalidOperationException>(
            () => new DatabaseContext(path));

        // The outer exception is the constructor's "Failed to open or initialize" wrapper.
        // The inner exception is the F7 guard's "Migration conflict" exception.
        Assert.NotNull(outerEx.InnerException);
        var innerEx = outerEx.InnerException!;
        Assert.IsType<InvalidOperationException>(innerEx);
        Assert.Contains("Migration conflict", innerEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When <c>SchedulingEnvironmentValues</c> is present but empty (the normal state when
    /// <c>InitializeSchema</c> created it as an empty shell before <c>Migrate</c> runs),
    /// the migration proceeds normally — the empty shell is dropped and the rename succeeds.
    /// No exception must be thrown.
    /// </summary>
    [Fact]
    public void DatabaseContext_MigrationWithEmptyDestination_Succeeds()
    {
        // Arrange — SectionPropertyValues exists, SchedulingEnvironmentValues is empty
        var path = Path.Combine(_tempDir, "empty_dest_migration.db");
        CreatePartiallyMigratedDb(path, rowsInNewTable: 0);

        // Act — should complete without throwing
        DatabaseContext? ctx = null;
        var ex = Record.Exception(() => ctx = new DatabaseContext(path));

        // Assert
        Assert.Null(ex);
        ctx?.Dispose();
    }

    /// <summary>
    /// When only <c>SectionPropertyValues</c> exists (normal pre-migration state — the
    /// destination table has not been created yet), <c>InitializeSchema</c> creates an empty
    /// <c>SchedulingEnvironmentValues</c> shell, and <c>Migrate</c> then drops the empty
    /// shell and renames <c>SectionPropertyValues</c>. The whole flow must succeed.
    ///
    /// <para>This is the "happy path" that exercises the same code as the conflict guard but
    /// without triggering it.</para>
    /// </summary>
    [Fact]
    public void DatabaseContext_NormalMigration_Succeeds()
    {
        // Arrange — only the old table; destination not yet created
        var path = Path.Combine(_tempDir, "normal_migration.db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE SectionPropertyValues (id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Act
        DatabaseContext? ctx = null;
        var ex = Record.Exception(() => ctx = new DatabaseContext(path));

        // Assert
        Assert.Null(ex);
        ctx?.Dispose();
    }
}
