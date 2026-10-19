using Microsoft.Data.Sqlite;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Reads the traceable edits (see <see cref="World.EditsTable"/>) out of a database file WITHOUT disturbing it
/// (spec item 9.3). Production rule: opening the shared database D with SQLite changes its header, so a file is never
/// opened in place; it is copied, together with any <c>-wal</c>, <c>-shm</c> or <c>-journal</c> beside it, to a
/// scratch folder and the COPY is opened. Copying a working file that a live connection still has open is safe: the
/// copy is made with <see cref="FileShare.ReadWrite"/> and SQLite recovers the copied WAL on its own.
/// </summary>
internal static class Inspector
{
    /// <summary>The suffixes of the files SQLite keeps beside a database file.</summary>
    private static readonly string[] Sidecars = { "-wal", "-shm", "-journal" };

    /// <summary>
    /// Lists the ids of the traceable edits present in a database file, in the order they were inserted.
    /// </summary>
    /// <param name="databaseFile">The database file to read (D, a working copy, an exported or recovered copy).</param>
    /// <param name="scratchRoot">A folder in which a private scratch folder is made for the copy (and removed again).</param>
    /// <returns>The edit ids; empty when the file does not exist or has no edits table.</returns>
    public static List<string> EditIds(string databaseFile, string scratchRoot)
    {
        if (!File.Exists(databaseFile)) return new List<string>();

        var scratch = Path.Combine(scratchRoot, "inspect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var copy = Path.Combine(scratch, "copy.db");
            CopyShared(databaseFile, copy);
            foreach (var suffix in Sidecars)
            {
                if (File.Exists(databaseFile + suffix))
                    CopyShared(databaseFile + suffix, copy + suffix);
            }

            return ReadIds(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c> on a private copy of a database file (never in place; see the class remarks).
    /// </summary>
    /// <param name="databaseFile">The file to check (normally the shared database D).</param>
    /// <param name="scratchRoot">A folder in which a private scratch folder is made for the copy (and removed again).</param>
    /// <returns>Null when the file does not exist or passes the check; otherwise a short description of what is wrong (SQLite's first findings, or the exception that stopped the check).</returns>
    public static string? IntegrityProblem(string databaseFile, string scratchRoot)
    {
        if (!File.Exists(databaseFile)) return null;

        var scratch = Path.Combine(scratchRoot, "integrity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var copy = Path.Combine(scratch, "copy.db");
            CopyShared(databaseFile, copy);
            foreach (var suffix in Sidecars)
            {
                if (File.Exists(databaseFile + suffix))
                    CopyShared(databaseFile + suffix, copy + suffix);
            }

            using var conn = new SqliteConnection($"Data Source={copy};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            var findings = new List<string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read() && findings.Count < 3) findings.Add(reader.GetString(0));
            }
            return findings.Count == 1 && findings[0] == "ok" ? null : string.Join(" / ", findings);
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Like <see cref="EditIds"/> but never throws: a file that cannot be read (damaged, or not a database) answers null,
    /// which the checker treats as "cannot tell", so one damaged file does not hide every other finding.
    /// </summary>
    /// <param name="databaseFile">The database file to read.</param>
    /// <param name="scratchRoot">The scratch root (see <see cref="EditIds"/>).</param>
    /// <returns>The edit ids (empty when the file is missing); null when the file exists but cannot be read.</returns>
    public static List<string>? TryEditIds(string databaseFile, string scratchRoot)
    {
        try { return EditIds(databaseFile, scratchRoot); }
        catch (Exception) { return null; }
    }
    /// <summary>
    /// Copies a file while another handle may have it open for reading and writing (a live SQLite connection).
    /// </summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">Where the copy goes (overwritten).</param>
    internal static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    /// <summary>Reads the edit ids from a private copy of a database.</summary>
    /// <param name="copy">The private copy (it may be modified by SQLite's recovery).</param>
    /// <returns>The ids in insertion order; empty when the edits table does not exist.</returns>
    private static List<string> ReadIds(string copy)
    {
        var ids = new List<string>();
        using var conn = new SqliteConnection($"Data Source={copy};Pooling=False");
        conn.Open();

        using (var exists = conn.CreateCommand())
        {
            exists.CommandText = $"SELECT count(*) FROM sqlite_master WHERE name = '{World.EditsTable}'";
            if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return ids;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id FROM {World.EditsTable} ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }
}
