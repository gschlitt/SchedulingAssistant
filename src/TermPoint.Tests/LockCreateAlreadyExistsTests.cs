using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 5.1 (designs/network-experiments-5.8.md): a failed lock <c>CreateNew</c> is classified as
/// "someone else holds the lock" from its own error code, never from a follow-up <c>File.Exists</c>, which
/// SMB can answer from a cached "not found" (two readers race for a released lock; the loser was shown the
/// can't-write message). These tests pin the classification; the cache itself can't be reproduced locally,
/// but the rig measured SMB's "already exists" as 0x80070050, the same code local NTFS gives.
/// </summary>
public sealed class LockCreateAlreadyExistsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lock_exists_{Guid.NewGuid():N}");

    /// <summary>Creates the temp folder for the real-file test.</summary>
    public LockCreateAlreadyExistsTests() => Directory.CreateDirectory(_dir);

    /// <summary>Removes the temp folder.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A real CreateNew over an existing file is classified as "already exists".</summary>
    [Fact]
    public void RealCreateNewOverExistingFile_IsAlreadyExists()
    {
        var path = Path.Combine(_dir, "X.lock");
        File.WriteAllText(path, "{}");

        var ex = Record.Exception(() =>
        {
            using var _ = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        });

        Assert.NotNull(ex);
        Assert.True(WriteLockService.IsAlreadyExistsError(ex!), $"0x{ex!.HResult:X8} {ex.GetType().Name}");
    }

    /// <summary>The two Win32 "exists" codes, as SMB and NTFS report them, are "already exists".</summary>
    [Theory]
    [InlineData(unchecked((int)0x80070050))]   // ERROR_FILE_EXISTS (measured over SMB on the rig)
    [InlineData(unchecked((int)0x800700B7))]   // ERROR_ALREADY_EXISTS
    public void ExistsCodes_AreAlreadyExists(int hresult)
        => Assert.True(WriteLockService.IsAlreadyExistsError(new IOException("exists", hresult)));

    /// <summary>
    /// Blocked writes (Controlled Folder Access, ACLs, a deleted-but-still-open lock file), sharing violations
    /// and disk full are NOT "already exists": they keep the File.Exists disambiguation.
    /// </summary>
    [Theory]
    [InlineData(unchecked((int)0x80070005))]   // ERROR_ACCESS_DENIED (also "delete pending", rig E2 case 5)
    [InlineData(unchecked((int)0x80070020))]   // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070070))]   // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070035))]   // ERROR_BAD_NETPATH
    public void OtherIoCodes_AreNotAlreadyExists(int hresult)
        => Assert.False(WriteLockService.IsAlreadyExistsError(new IOException("other", hresult)));

    /// <summary>Not-found and access-denied exception types are never "already exists", whatever their code.</summary>
    [Fact]
    public void NotFoundAndAccessDeniedTypes_AreNotAlreadyExists()
    {
        Assert.False(WriteLockService.IsAlreadyExistsError(new FileNotFoundException("cfa")));
        Assert.False(WriteLockService.IsAlreadyExistsError(new DirectoryNotFoundException("gone")));
        Assert.False(WriteLockService.IsAlreadyExistsError(new UnauthorizedAccessException("denied")));
    }
}
