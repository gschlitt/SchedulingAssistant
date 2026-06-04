using System;
using System.IO;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="WriteAccessProbe"/>, the helper that distinguishes a folder which
/// won't accept writes (e.g. blocked by Controlled Folder Access) from a genuine lock/corruption
/// failure at database open.
/// </summary>
public class WriteAccessProbeTests
{
    [Fact]
    public void CanCreateFileIn_WritableTempFolder_ReturnsTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.True(WriteAccessProbe.CanCreateFileIn(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CanCreateFileIn_NonExistentFolder_ReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wap_missing_{Guid.NewGuid():N}");
        Assert.False(WriteAccessProbe.CanCreateFileIn(dir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanCreateFileIn_NullOrBlank_ReturnsFalse(string? dir)
    {
        Assert.False(WriteAccessProbe.CanCreateFileIn(dir));
    }

    [Fact]
    public void CanCreateFileIn_LeavesNoProbeFileBehind()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wap_clean_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WriteAccessProbe.CanCreateFileIn(dir);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsProtectedKnownFolder_DocumentsItself_ReturnsTrue()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        // On some CI environments the special folder may be empty; only assert when it resolves.
        if (string.IsNullOrEmpty(docs)) return;
        Assert.True(WriteAccessProbe.IsProtectedKnownFolder(docs));
    }

    [Fact]
    public void IsProtectedKnownFolder_SubfolderOfDocuments_ReturnsTrue()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) return;
        var nested = Path.Combine(docs, "TermPointMedia", "SchedulerTest");
        Assert.True(WriteAccessProbe.IsProtectedKnownFolder(nested));
    }

    [Fact]
    public void IsProtectedKnownFolder_TempFolder_ReturnsFalse()
    {
        // The temp path is never one of the CFA-protected known folders.
        Assert.False(WriteAccessProbe.IsProtectedKnownFolder(Path.GetTempPath()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsProtectedKnownFolder_NullOrBlank_ReturnsFalse(string? path)
    {
        Assert.False(WriteAccessProbe.IsProtectedKnownFolder(path));
    }
}
