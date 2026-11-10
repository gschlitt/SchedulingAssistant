using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SqliteContentHash"/> (spec item 3.4, 1.2.3 section 29a): the content fingerprint of a SQLite file,
/// which reads five header fields that hold no data (bytes 18-19, 24-27, 40-43, 92-95, 96-99) as zeros.
///
/// <para>Two halves: the masking itself on plain byte files (each masked field is ignored, every other byte counts, short
/// files), and the reason it exists (finding F5): a real save snapshot of an UNCHANGED database has the same fingerprint as
/// the database in each of the four cases that used to make a no-edit save rewrite it (a new database, a VACUUM INTO copy, a
/// database whose header says WAL, a header written by another SQLite version), while a real edit changes it.</para>
/// </summary>
public sealed class SqliteContentHashTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates an isolated temporary directory.</summary>
    public SqliteContentHashTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Deletes the temporary directory.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The five masked header ranges, (first offset, length).</summary>
    private static readonly (int Start, int Length)[] Masked = { (18, 2), (24, 4), (40, 4), (92, 4), (96, 4) };

    /// <summary>Writes a file of <paramref name="length"/> deterministic non-zero bytes and returns its path.</summary>
    /// <param name="length">The size in bytes.</param>
    /// <param name="name">The file name.</param>
    private string BytesFile(int length, string name = "plain.bin")
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)(1 + i % 250);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Returns the hash of a copy of <paramref name="path"/> in which byte <paramref name="offset"/> is changed.</summary>
    /// <param name="path">The original file.</param>
    /// <param name="offset">The byte to change.</param>
    private string HashWithByteChanged(string path, int offset)
    {
        var bytes = File.ReadAllBytes(path);
        bytes[offset] = (byte)(bytes[offset] ^ 0xFF);
        var copy = Path.Combine(_dir, $"changed_{offset}.bin");
        File.WriteAllBytes(copy, bytes);
        return SqliteContentHash.Compute(copy);
    }

    /// <summary>Creates a small database with a <c>_t</c> table of 200 rows (several pages) at <paramref name="path"/>.</summary>
    /// <param name="path">Where to create it.</param>
    private static void CreatePopulated(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE _t(id INTEGER PRIMARY KEY, v TEXT)";
        cmd.ExecuteNonQuery();
        using var tx = conn.BeginTransaction();
        for (var i = 0; i < 200; i++)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO _t(v) VALUES($v)";
            ins.Parameters.AddWithValue("$v", new string('x', 100) + i);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Takes a snapshot of <paramref name="path"/> exactly as a save does (SQLite online backup into a new local file, then
    /// back to rollback-journal mode) and returns its path. The source is opened read-write like the app's copy.
    /// </summary>
    /// <param name="path">The database to snapshot (a working copy).</param>
    private string Snapshot(string path)
    {
        var dest = Path.Combine(_dir, $"snap_{Guid.NewGuid():N}.db");
        using (var source = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False"))
        using (var target = new SqliteConnection($"Data Source={dest};Pooling=False"))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
            using var jm = target.CreateCommand();
            jm.CommandText = "PRAGMA journal_mode=DELETE";
            jm.ExecuteScalar();
        }
        return dest;
    }

    /// <summary>Overwrites bytes 96-99 of a database file with a different SQLite version number.</summary>
    /// <param name="path">The database file.</param>
    private static void PokeSqliteVersion(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        fs.Position = 96;
        fs.Write(new byte[] { 0x00, 0x2D, 0xC6, 0xC0 }, 0, 4);   // 3000000, big-endian: some other writer
    }

    // ── The masking ───────────────────────────────────────────────────────────

    /// <summary>Changing any byte of any masked field leaves the fingerprint unchanged.</summary>
    [Fact]
    public void ChangingAMaskedByte_DoesNotChangeTheHash()
    {
        var path = BytesFile(8192);
        var expected = SqliteContentHash.Compute(path);

        foreach (var (start, length) in Masked)
            for (var offset = start; offset < start + length; offset++)
                Assert.Equal(expected, HashWithByteChanged(path, offset));
    }

    /// <summary>Changing any byte just outside a masked field, or deep in the file, changes the fingerprint.</summary>
    [Fact]
    public void ChangingAnyOtherByte_ChangesTheHash()
    {
        var path = BytesFile(8192);
        var expected = SqliteContentHash.Compute(path);

        // The neighbours of every masked range, the first and last header bytes, and bytes in later blocks.
        foreach (var offset in new[] { 0, 17, 20, 23, 28, 39, 44, 91, 100, 4096, 8191 })
            Assert.NotEqual(expected, HashWithByteChanged(path, offset));
    }

    /// <summary>
    /// A file larger than one read block (64 KiB) is hashed in several blocks: the header is still masked, and a change
    /// in a later block still counts.
    /// </summary>
    [Fact]
    public void ALargeFile_IsMaskedInItsFirstBlock_AndHashedToTheEnd()
    {
        var path = BytesFile(200_000);
        var expected = SqliteContentHash.Compute(path);

        Assert.Equal(expected, HashWithByteChanged(path, 25));
        Assert.NotEqual(expected, HashWithByteChanged(path, 65_536));
        Assert.NotEqual(expected, HashWithByteChanged(path, 199_999));
    }

    /// <summary>The hash is the plain SHA-256 of the file with those bytes zeroed, in upper-case hex (the format of <c>CheckoutService</c>'s file hash).</summary>
    [Fact]
    public void TheHash_IsTheSha256OfTheMaskedBytes_InUpperCaseHex()
    {
        var path = BytesFile(1000);
        var bytes = File.ReadAllBytes(path);
        foreach (var (start, length) in Masked)
            Array.Clear(bytes, start, length);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), SqliteContentHash.Compute(path));
    }

    /// <summary>
    /// A file shorter than 100 bytes is not a database: it is hashed as it is after zeroing whichever masked bytes it
    /// contains (a 30-byte file loses bytes 18-19 and 24-27 only); an empty file is the SHA-256 of nothing.
    /// </summary>
    [Fact]
    public void AShortFile_IsHashedAsItIs_AfterMaskingWhatExists()
    {
        var thirty = BytesFile(30, "thirty.bin");
        var expected = SqliteContentHash.Compute(thirty);
        Assert.Equal(expected, HashWithByteChanged(thirty, 19));
        Assert.Equal(expected, HashWithByteChanged(thirty, 26));
        Assert.NotEqual(expected, HashWithByteChanged(thirty, 10));
        Assert.NotEqual(expected, HashWithByteChanged(thirty, 29));

        var empty = Path.Combine(_dir, "empty.bin");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())), SqliteContentHash.Compute(empty));
    }

    /// <summary>A file another program holds open with sharing allowed (a live connection) is still readable.</summary>
    [Fact]
    public void AFileOpenElsewhereForReadingAndWriting_CanBeHashed()
    {
        var path = BytesFile(500);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.NotEmpty(SqliteContentHash.Compute(path));
    }

    /// <summary>A missing file is an <see cref="IOException"/> (a <see cref="FileNotFoundException"/>), as for any file hash.</summary>
    [Fact]
    public void AMissingFile_Throws()
        => Assert.ThrowsAny<IOException>(() => SqliteContentHash.Compute(Path.Combine(_dir, "nope.db")));

    // ── The four "not normalised" cases of finding F5 ─────────────────────────

    /// <summary>
    /// A snapshot of an unchanged D has D's fingerprint in each case that used to make a no-edit save rewrite D, and the raw
    /// bytes DO differ (which is the whole problem): a new database, a VACUUM INTO copy (a restored backup), a database whose
    /// header says WAL (opened directly by a 1.2.2 colleague), and a header written by another SQLite version.
    /// </summary>
    [Fact]
    public async Task ASnapshotOfAnUnchangedD_HasDsFingerprint_InEachNotNormalisedCase()
    {
        // 1. A new database, as File → New builds it.
        var fresh = Path.Combine(_dir, "fresh.db");
        var created = await NewDatabaseFile.CreateAsync(fresh, _dir);
        Assert.Equal(NewDatabaseOutcome.Created, created.Outcome);

        // 2. A VACUUM INTO copy (a restored backup).
        var source = Path.Combine(_dir, "source.db");
        CreatePopulated(source);
        var vacuumed = Path.Combine(_dir, "vacuumed.db");
        using (var conn = new SqliteConnection($"Data Source={source};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $p";
            cmd.Parameters.AddWithValue("$p", vacuumed);
            cmd.ExecuteNonQuery();
        }

        // 3. A database whose header says WAL (written while open in WAL mode, then closed).
        var wal = Path.Combine(_dir, "wal.db");
        CreatePopulated(wal);
        using (var conn = new SqliteConnection($"Data Source={wal};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO _t(v) VALUES('in wal')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(2, File.ReadAllBytes(wal)[18]);   // the header really says WAL

        // 4. A header with a different SQLite version number poked into bytes 96-99.
        var poked = Path.Combine(_dir, "poked.db");
        CreatePopulated(poked);
        PokeSqliteVersion(poked);

        foreach (var d in new[] { fresh, vacuumed, wal, poked })
        {
            var snap = Snapshot(d);
            Assert.Equal(SqliteContentHash.Compute(d), SqliteContentHash.Compute(snap));
        }

        // The raw bytes do differ for at least the cases that motivated this: proof the fingerprint is doing the work.
        Assert.NotEqual(File.ReadAllBytes(poked), File.ReadAllBytes(Snapshot(poked)));
        Assert.NotEqual(File.ReadAllBytes(wal), File.ReadAllBytes(Snapshot(wal)));
    }

    /// <summary>A real edit (one more row) changes the fingerprint of a snapshot.</summary>
    [Fact]
    public void ARealEdit_ChangesTheFingerprint()
    {
        var path = Path.Combine(_dir, "edit.db");
        CreatePopulated(path);
        var before = SqliteContentHash.Compute(Snapshot(path));

        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO _t(v) VALUES('a real edit')";
            cmd.ExecuteNonQuery();
        }

        Assert.NotEqual(before, SqliteContentHash.Compute(Snapshot(path)));
    }
}
