using System.Security.Cryptography;

namespace TermPoint.Services;

/// <summary>
/// A "content fingerprint" of a SQLite database file: the SHA-256 of the file with the five header fields that hold no
/// data read as zeros (spec item 3.4, 1.2.3 section 29). Two files with the same fingerprint have identical pages, so
/// they hold the same schema and the same data, whichever SQLite version, journal mode or backup routine last wrote them.
///
/// <para><b>Why it exists (finding F5).</b> A save used to rewrite the shared database even when nothing had changed,
/// because a database last written by another route (a new database, a restore from backup made with <c>VACUUM INTO</c>,
/// a direct open in WAL mode, another SQLite version) differs from TermPoint's own snapshot of it in a few header bytes
/// only. The save compares fingerprints and, when D' (snapshotted) and D (as last seen) agree, writes nothing.</para>
///
/// <para><b>The masked fields</b> (offsets are 0-based; SQLite file format, section 1.3):
/// bytes 18-19 (file format write/read version: the journal mode), 24-27 (file change counter), 40-43 (schema cookie),
/// 92-95 (version-valid-for number) and 96-99 (SQLITE_VERSION_NUMBER of the last writer). None of them holds user
/// data: a schema change also changes the <c>sqlite_master</c> page and a data change changes a page, both of which
/// stay in the fingerprint. Every other byte of the file counts.</para>
///
/// <para><b>Pure and streaming:</b> the file is read as plain bytes in fixed-size blocks (so a large database costs no
/// memory) and is NEVER opened with SQLite, so computing a fingerprint cannot change the file, create a journal or a
/// WAL sidecar, or take a SQLite lock.</para>
/// </summary>
public static class SqliteContentHash
{
    /// <summary>
    /// The byte ranges of the 100-byte SQLite header that are read as zeros: (first offset, length). Sorted by offset.
    /// </summary>
    private static readonly (int Start, int Length)[] MaskedRanges =
    {
        (18, 2),   // file format write version, read version (journal mode: rollback = 1, WAL = 2)
        (24, 4),   // file change counter
        (40, 4),   // schema cookie
        (92, 4),   // version-valid-for number
        (96, 4),   // SQLITE_VERSION_NUMBER of the library that last wrote the file
    };

    /// <summary>Size of the read buffer. The header is in the first block, so masking never straddles two reads.</summary>
    private const int BlockSize = 64 * 1024;

    /// <summary>
    /// Computes the content fingerprint of the file at <paramref name="path"/>.
    ///
    /// <para>A file shorter than 100 bytes is not a SQLite database. It is hashed as it is, after masking whichever of
    /// the masked bytes it does contain (a file of 30 bytes has bytes 18-19 and 24-27 zeroed, and nothing else).</para>
    /// </summary>
    /// <param name="path">Full path of a local file. It is opened for reading with sharing allowed, so a live connection does not block it.</param>
    /// <returns>The SHA-256 of the masked file as upper-case hex, in the same format as <c>CheckoutService</c>'s file hash.</returns>
    /// <exception cref="IOException">The file cannot be read (missing, or held without sharing).</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file is denied.</exception>
    public static string Compute(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BlockSize, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer   = new byte[BlockSize];
        long offset  = 0;   // absolute file offset of buffer[0]
        int read;
        while ((read = ReadBlock(stream, buffer)) > 0)
        {
            // Only the first block can contain header bytes (the header is 100 bytes, the block 64 KiB).
            if (offset == 0)
                MaskHeader(buffer, read);
            hash.AppendData(buffer, 0, read);
            offset += read;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Reads until <paramref name="buffer"/> is full or the stream ends, so a short read from the first call can never
    /// leave part of the header unmasked.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>The number of bytes in the buffer; 0 at the end of the stream.</returns>
    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    /// <summary>Zeroes the masked header ranges that lie inside the first <paramref name="length"/> bytes of <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The first block of the file.</param>
    /// <param name="length">How many bytes of it are valid.</param>
    private static void MaskHeader(byte[] buffer, int length)
    {
        foreach (var (start, count) in MaskedRanges)
        {
            for (var i = start; i < start + count && i < length; i++)
                buffer[i] = 0;
        }
    }
}
