using Microsoft.Data.Sqlite;
using SchedulingAssistant.Data;
using System;
using System.IO;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for F6 from the data-integrity audit (2026-05-04):
/// <c>PRAGMA user_version</c> is written at the end of every successful
/// <see cref="DatabaseContext.Migrate"/> run and read as a fast-path by
/// <c>IsMigrationNeeded</c> to skip re-executing all per-column proxy checks on
/// every subsequent startup.
///
/// <para>The goal of the stamp is robustness: if a developer adds a new
/// <c>AddColumnIfMissing</c> call to <c>Migrate</c> without also updating the
/// legacy proxy check, the stamp ensures the new migration still runs on databases
/// that were last opened before the change. A DB that has already executed all
/// migrations carries <c>user_version = CurrentSchemaVersion</c> and takes the fast
/// path immediately, while a DB that was opened before the stamp was introduced
/// carries <c>user_version = 0</c> and falls through to the legacy checks.</para>
///
/// <para>Observable effects tested here:</para>
/// <list type="bullet">
///   <item><c>user_version</c> equals <see cref="DatabaseContext.CurrentSchemaVersion"/>
///   after migration completes.</item>
///   <item>No <c>.pre-migration.bak</c> is created when migration is skipped (fast path
///   and legacy-false path).</item>
///   <item>The second open of a migrated database never re-runs migration.</item>
/// </list>
/// </summary>
public sealed class SchemaVersionTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    public SchemaVersionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes temporary files created during the test.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a fresh (non-pooled) connection to <paramref name="path"/> and reads the
    /// current <c>PRAGMA user_version</c> value.
    /// </summary>
    private static int ReadUserVersion(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Group 1 — F6: Version stamp is written after migration ────────────────

    /// <summary>
    /// Documents the expected value of the version constant.
    /// If this fails, it means the constant was changed without updating the tests.
    /// </summary>
    [Fact]
    public void CurrentSchemaVersion_IsOne()
    {
        Assert.Equal(1, DatabaseContext.CurrentSchemaVersion);
    }

    /// <summary>
    /// When a database contains <c>SectionPropertyValues</c> (the old table name),
    /// <c>IsMigrationNeeded</c> returns true, <c>Migrate</c> runs, and
    /// <c>PRAGMA user_version</c> is stamped to <see cref="DatabaseContext.CurrentSchemaVersion"/>
    /// by the time the constructor returns.
    ///
    /// <para>This is the primary F6 assertion: after a migration run the version stamp
    /// is in place so every subsequent open takes the fast path.</para>
    /// </summary>
    [Fact]
    public void DatabaseContext_AfterMigration_UserVersionEqualsCurrentSchemaVersion()
    {
        // Arrange — DB with old table name that triggers the rename migration
        var path = Path.Combine(_tempDir, "migration_stamp.db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE SectionPropertyValues " +
                "(id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Act
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();

        // Assert
        Assert.Equal(DatabaseContext.CurrentSchemaVersion, ReadUserVersion(path));
    }

    /// <summary>
    /// When <c>user_version</c> is already set to <see cref="DatabaseContext.CurrentSchemaVersion"/>,
    /// <c>IsMigrationNeeded</c> returns <c>false</c> immediately (fast path). As a result,
    /// <c>TakePreMigrationBackup</c> is never called and no <c>.pre-migration.bak</c> file
    /// is written alongside the database.
    ///
    /// <para>This is the key F6 correctness test: a DB that was migrated in a previous
    /// session must never trigger another migration run, regardless of which legacy proxy
    /// checks are in place.</para>
    /// </summary>
    [Fact]
    public void DatabaseContext_AlreadyStamped_SkipsMigration()
    {
        // Arrange — write user_version directly, simulating a previously migrated DB
        var path = Path.Combine(_tempDir, "already_stamped.db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"PRAGMA user_version = {DatabaseContext.CurrentSchemaVersion}";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // Act — DatabaseContext should take the fast path and skip Migrate entirely
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();

        // Assert — no backup created (TakePreMigrationBackup was not called)
        Assert.False(
            File.Exists(path + ".pre-migration.bak"),
            "Pre-migration backup must not be created when user_version is already current.");

        // Stamp is intact
        Assert.Equal(DatabaseContext.CurrentSchemaVersion, ReadUserVersion(path));
    }

    /// <summary>
    /// A brand-new database path has no tables and no prior data. After
    /// <see cref="DatabaseContext"/> creates it, <c>IsMigrationNeeded</c> returns
    /// <c>false</c> (all required columns are created fresh by <c>InitializeSchema</c>,
    /// no SectionPropertyValues rename is needed, no backfill rows exist) — so
    /// <c>TakePreMigrationBackup</c> is never called.
    /// </summary>
    [Fact]
    public void DatabaseContext_BrandNewDb_NoPreMigrationBackup()
    {
        // Arrange — fresh path
        var path = Path.Combine(_tempDir, "brand_new.db");

        // Act
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();

        // Assert — no migration ran; no backup file
        Assert.False(
            File.Exists(path + ".pre-migration.bak"),
            "Brand-new databases do not need migration — no backup should be created.");
    }

    /// <summary>
    /// After the first open runs a successful migration (stamping <c>user_version</c>),
    /// a second open of the same file must take the fast path: no pre-migration backup
    /// is created, and the stamp remains at <see cref="DatabaseContext.CurrentSchemaVersion"/>.
    ///
    /// <para>Without the F6 stamp, the second open would re-evaluate the legacy proxy
    /// checks on every startup. With the stamp, the fast path returns <c>false</c>
    /// immediately, making startup cheaper and immune to stale proxy logic.</para>
    /// </summary>
    [Fact]
    public void DatabaseContext_OpenedTwice_SecondOpenDoesNotRunMigration()
    {
        // Arrange — DB with old table name so first open triggers migration
        var path = Path.Combine(_tempDir, "twice_open.db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE SectionPropertyValues " +
                "(id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // First open — migration runs, stamp written, backup created
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();

        // Delete the backup so we can detect whether the second open creates a new one
        var bakPath = path + ".pre-migration.bak";
        if (File.Exists(bakPath)) File.Delete(bakPath);

        // Act — second open
        using (var ctx = new DatabaseContext(path)) { }
        SqliteConnection.ClearAllPools();

        // Assert — fast path taken: no new backup, stamp unchanged
        Assert.False(
            File.Exists(bakPath),
            "Second open of an already-migrated DB must not re-run migration (fast path via user_version).");
        Assert.Equal(DatabaseContext.CurrentSchemaVersion, ReadUserVersion(path));
    }
}
