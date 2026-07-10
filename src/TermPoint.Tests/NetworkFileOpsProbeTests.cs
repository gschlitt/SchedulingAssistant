using TermPoint.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="NetworkFileOps.ProbeFileAsync"/>, the tri-state existence
/// probe that distinguishes a genuinely missing file from an unreachable location.
///
/// <para>Only the locally-deterministic outcomes are asserted here: <c>Exists</c> and
/// <c>Missing</c>. The <c>Unreachable</c> outcome depends on environment-specific
/// network error timing (dead SMB share → <see cref="IOException"/> or deadline
/// timeout) and is validated in the field via the clumsy three-machine protocol,
/// not in CI.</para>
/// </summary>
public class NetworkFileOpsProbeTests
{
    [Fact]
    public async Task ProbeFileAsync_ExistingFile_ReturnsExists()
    {
        var dir = Directory.CreateTempSubdirectory("nfo_probe").FullName;
        try
        {
            var path = Path.Combine(dir, "present.db");
            File.WriteAllText(path, "content");

            var result = await NetworkFileOps.ProbeFileAsync(path);

            Assert.Equal(FileProbeResult.Exists, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeFileAsync_MissingFileInExistingDirectory_ReturnsMissing()
    {
        var dir = Directory.CreateTempSubdirectory("nfo_probe").FullName;
        try
        {
            var result = await NetworkFileOps.ProbeFileAsync(Path.Combine(dir, "absent.db"));

            Assert.Equal(FileProbeResult.Missing, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeFileAsync_MissingIntermediateDirectory_ReturnsMissing()
    {
        // A missing intermediate directory on a live volume maps to
        // DirectoryNotFoundException — the location answered, the path is absent.
        var dir = Directory.CreateTempSubdirectory("nfo_probe").FullName;
        try
        {
            var result = await NetworkFileOps.ProbeFileAsync(
                Path.Combine(dir, "no-such-subdir", "absent.db"));

            Assert.Equal(FileProbeResult.Missing, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
