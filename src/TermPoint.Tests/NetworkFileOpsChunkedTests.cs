using System;
using System.IO;
using System.Threading.Tasks;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the chunked, stall-aware bulk transfers in <see cref="NetworkFileOps"/>
/// (finding #7). The stall behavior itself needs a genuinely hung network share and
/// cannot be triggered deterministically on local disk, so these tests pin the
/// correctness properties: multi-chunk fidelity, open-handle coexistence, exception
/// contract, and hash parity with the synchronous implementation.
/// </summary>
public sealed class NetworkFileOpsChunkedTests : IDisposable
{
    private readonly string _dir;

    public NetworkFileOpsChunkedTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nfo_chunk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Creates a file of <paramref name="bytes"/> pseudo-random bytes (seeded for
    /// reproducibility) and returns its path. Sized to span multiple chunks so the
    /// chunk loop, not just the first iteration, is exercised.
    /// </summary>
    private string CreateRandomFile(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        var data = new byte[bytes];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>A 3.5 MB copy (4 chunks, last one partial) must be byte-exact.</summary>
    [Fact]
    public async Task CopyAsync_MultiChunkFile_CopiesExactly()
    {
        var src  = CreateRandomFile("src.bin", (int)(3.5 * NetworkFileOps.ChunkSize));
        var dest = Path.Combine(_dir, "dest.bin");

        var completed = await NetworkFileOps.CopyAsync(src, dest);

        Assert.True(completed);
        Assert.Equal(NetworkFileOps.ComputeHash(src), NetworkFileOps.ComputeHash(dest));
    }

    /// <summary>A file smaller than one chunk (the common case) copies correctly.</summary>
    [Fact]
    public async Task CopyAsync_SubChunkFile_CopiesExactly()
    {
        var src  = CreateRandomFile("small.bin", 12_345);
        var dest = Path.Combine(_dir, "small-dest.bin");

        var completed = await NetworkFileOps.CopyAsync(src, dest);

        Assert.True(completed);
        Assert.Equal(NetworkFileOps.ComputeHash(src), NetworkFileOps.ComputeHash(dest));
    }

    /// <summary>
    /// The source must remain readable while another process holds it open with
    /// ReadWrite access — mirrors DatabaseContext holding D'/D open during copies
    /// (the Group 7 coexistence guarantee, preserved across the chunked rewrite).
    /// </summary>
    [Fact]
    public async Task CopyAsync_SourceHeldOpenWithWriteAccess_Succeeds()
    {
        var src  = CreateRandomFile("held.bin", 100_000);
        var dest = Path.Combine(_dir, "held-dest.bin");

        using var handle = new FileStream(
            src, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var completed = await NetworkFileOps.CopyAsync(src, dest);

        Assert.True(completed);
        Assert.Equal(new FileInfo(src).Length, new FileInfo(dest).Length);
    }

    /// <summary>An existing destination is overwritten (FileMode.Create), as before.</summary>
    [Fact]
    public async Task CopyAsync_DestinationExists_Overwrites()
    {
        var src  = CreateRandomFile("over-src.bin", 50_000);
        var dest = Path.Combine(_dir, "over-dest.bin");
        File.WriteAllText(dest, "old-content-that-must-vanish");

        var completed = await NetworkFileOps.CopyAsync(src, dest);

        Assert.True(completed);
        Assert.Equal(NetworkFileOps.ComputeHash(src), NetworkFileOps.ComputeHash(dest));
    }

    /// <summary>
    /// Exceptions within the deadline propagate unchanged (the documented contract,
    /// relied on by CheckoutService's try/catch blocks) — a missing source throws
    /// rather than reporting a stall.
    /// </summary>
    [Fact]
    public async Task CopyAsync_MissingSource_Throws()
    {
        var dest = Path.Combine(_dir, "never.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => NetworkFileOps.CopyAsync(Path.Combine(_dir, "does-not-exist.bin"), dest));
    }

    /// <summary>
    /// The chunked hash must agree with the synchronous single-pass implementation
    /// — HashAtCheckout values computed by either path must be interchangeable.
    /// </summary>
    [Fact]
    public async Task ComputeHashAsync_MultiChunkFile_MatchesSynchronousHash()
    {
        var path = CreateRandomFile("hash.bin", (int)(2.5 * NetworkFileOps.ChunkSize));

        var (completed, hash) = await NetworkFileOps.ComputeHashAsync(path);

        Assert.True(completed);
        Assert.Equal(NetworkFileOps.ComputeHash(path), hash);
    }

    /// <summary>An empty file hashes successfully (EOF on the first chunk).</summary>
    [Fact]
    public async Task ComputeHashAsync_EmptyFile_MatchesSynchronousHash()
    {
        var path = Path.Combine(_dir, "empty.bin");
        File.WriteAllBytes(path, Array.Empty<byte>());

        var (completed, hash) = await NetworkFileOps.ComputeHashAsync(path);

        Assert.True(completed);
        Assert.Equal(NetworkFileOps.ComputeHash(path), hash);
    }

    /// <summary>A missing file propagates the open exception (contract parity).</summary>
    [Fact]
    public async Task ComputeHashAsync_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => NetworkFileOps.ComputeHashAsync(Path.Combine(_dir, "gone.bin")));
    }
}
