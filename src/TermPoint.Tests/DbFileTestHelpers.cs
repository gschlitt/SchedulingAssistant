using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace TermPoint.Tests;

/// <summary>
/// Shared helpers for tests that need real SQLite files in awkward states: healthy and
/// multi-page, with a trashed page, or held open exclusively by "another program". Used by the
/// integrity-check, validator and chooser tests (spec item 4e).
/// </summary>
internal static class DbFileTestHelpers
{
    /// <summary>
    /// Creates a healthy SQLite database at <paramref name="path"/> with a <c>Sections</c> table
    /// holding enough rows to span many pages (so a single trashed page is a real, detectable
    /// structural fault rather than a no-op). The pool is cleared so no handle outlives the call.
    /// </summary>
    /// <param name="path">Where to create the file.</param>
    public static void CreateHealthyDatabase(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using (var create = conn.CreateCommand())
            {
                create.Transaction = tx;
                create.CommandText =
                    "CREATE TABLE Sections (id TEXT PRIMARY KEY, data TEXT NOT NULL DEFAULT '{}');";
                create.ExecuteNonQuery();
            }
            for (var i = 0; i < 500; i++)
            {
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO Sections (id, data) VALUES ($id, $data);";
                insert.Parameters.AddWithValue("$id", $"section-{i:D5}");
                insert.Parameters.AddWithValue("$data", new string('x', 100));
                insert.ExecuteNonQuery();
            }
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Creates a SQLite database that opens normally but for which <c>PRAGMA integrity_check</c> ANSWERS with problems
    /// (rather than SQLite refusing the file as malformed): an index whose stored definition is rewritten, through
    /// <c>writable_schema</c>, to cover a different column than the entries in it were built from, so every row is
    /// "missing from the index". This is the "integrity_check problems" kind of damage, as opposed to the
    /// SQLITE_CORRUPT kind that <see cref="TrashPage"/> usually produces (spec item 5, step 5e).
    /// </summary>
    /// <param name="path">Where to create the file.</param>
    public static void CreateDatabaseWithIntegrityProblems(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE Sections (id INTEGER PRIMARY KEY, code TEXT, title TEXT);" +
                "INSERT INTO Sections VALUES (1, 'A', 'x'), (2, 'B', 'y'), (3, 'C', 'z');" +
                "CREATE INDEX idx_sections_code ON Sections (code);" +
                "PRAGMA writable_schema = ON;" +
                "UPDATE sqlite_master SET sql = 'CREATE INDEX idx_sections_code ON Sections (title)' WHERE name = 'idx_sections_code';";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Overwrites one whole page of a SQLite file with 0xFF bytes, leaving the file header
    /// (page 1) intact: the file still looks like a database but its b-tree is structurally broken.
    /// Page 2 is the root of the first table created, so it is always reachable by
    /// <c>PRAGMA integrity_check</c>.
    /// </summary>
    /// <param name="path">The SQLite file to damage (must not be open elsewhere).</param>
    /// <param name="pageNumber">1-based page to trash; use 2 or higher to keep the header valid.</param>
    public static void TrashPage(string path, int pageNumber)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Page size lives at header offset 16 (big-endian); the value 1 means 65536.
        var header = new byte[18];
        stream.ReadExactly(header);
        var pageSize = (header[16] << 8) | header[17];
        if (pageSize == 1) pageSize = 65536;

        var garbage = new byte[pageSize];
        Array.Fill(garbage, (byte)0xFF);
        stream.Position = (long)(pageNumber - 1) * pageSize;
        stream.Write(garbage);
    }

    /// <summary>
    /// Holds <paramref name="path"/> open the way an exclusive backup or sync tool would
    /// (<c>FileShare.None</c>, the same as PowerShell's <c>[IO.File]::Open(path,'Open','Read','None')</c>).
    /// On Windows every other open of the file fails with a sharing violation until the returned
    /// stream is disposed. Dispose it (a <c>using</c>) to "let go".
    /// </summary>
    /// <param name="path">The existing file to lock.</param>
    /// <returns>The stream holding the lock.</returns>
    public static FileStream LockExclusively(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    /// <summary>
    /// SHA-256 of a file's bytes, opened with sharing so it works alongside other readers.
    /// Not usable while the file is held with <c>FileShare.None</c> — release the lock first.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <returns>Upper-case hex digest.</returns>
    public static string HashOf(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(s));
    }
}
