using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="FolderAssessor"/>. All tests inject fake CFA roots, cloud sync
/// roots, and writability probes so they run deterministically on any machine.
/// </summary>
public class FolderAssessorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates an assessor with the given roots and a writability probe that always succeeds.</summary>
    private static FolderAssessor MakeAssessor(
        IReadOnlyList<string>? cfaRoots = null,
        IReadOnlyList<CloudSyncRoot>? cloudRoots = null,
        Func<string, bool>? probe = null)
    {
        return new FolderAssessor(
            cfaRoots ?? Array.Empty<string>(),
            cloudRoots ?? Array.Empty<CloudSyncRoot>(),
            probe ?? (_ => true));
    }

    private static readonly string FakeDocs = Path.GetFullPath(@"C:\Users\TestUser\Documents");
    private static readonly string FakeDesktop = Path.GetFullPath(@"C:\Users\TestUser\Desktop");
    private static readonly string FakeOneDrive = Path.GetFullPath(@"C:\Users\TestUser\OneDrive");
    private static readonly string FakeDropbox = Path.GetFullPath(@"C:\Users\TestUser\Dropbox");

    // ── CFA detection ───────────────────────────────────────────────────────

    [Fact]
    public void Assess_PathUnderCfaRoot_ReturnsCfaWarning()
    {
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs });
        var result = assessor.Assess(Path.Combine(FakeDocs, "TermPoint", "MySchedule"));

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
    }

    [Fact]
    public void Assess_PathIsCfaRootItself_ReturnsCfaWarning()
    {
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs });
        var result = assessor.Assess(FakeDocs);

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
    }

    [Fact]
    public void Assess_PathNotUnderAnyCfaRoot_NoCfaWarning()
    {
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs, FakeDesktop });
        var result = assessor.Assess(@"D:\TermPoint\MySchedule");

        Assert.DoesNotContain(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
    }

    [Fact]
    public void Assess_CfaCheckIsCaseInsensitive()
    {
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs });
        var result = assessor.Assess(FakeDocs.ToUpperInvariant() + @"\Schedules");

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
    }

    [Fact]
    public void Assess_SiblingOfCfaRoot_NoCfaWarning()
    {
        // "C:\Users\TestUser\NotDocuments" is a sibling of Documents, not under it.
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs });
        var result = assessor.Assess(@"C:\Users\TestUser\NotDocuments");

        Assert.DoesNotContain(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
    }

    [Fact]
    public void Assess_CfaWarningIncludesRootInDetail()
    {
        var assessor = MakeAssessor(cfaRoots: new[] { FakeDocs });
        var result = assessor.Assess(Path.Combine(FakeDocs, "Data"));

        var warning = result.Warnings.First(w => w.Kind == WarningKind.CfaProtected);
        Assert.NotNull(warning.Detail);
        Assert.Contains(FakeDocs, warning.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cloud sync detection ────────────────────────────────────────────────

    [Fact]
    public void Assess_PathUnderOneDrive_ReturnsCloudSyncWarning()
    {
        var assessor = MakeAssessor(cloudRoots: new[]
        {
            new CloudSyncRoot("OneDrive", FakeOneDrive)
        });
        var result = assessor.Assess(Path.Combine(FakeOneDrive, "Schedules"));

        var warning = result.Warnings.FirstOrDefault(w => w.Kind == WarningKind.CloudSynced);
        Assert.NotNull(warning);
        Assert.Contains("OneDrive", warning!.Message);
    }

    [Fact]
    public void Assess_PathUnderDropbox_ReturnsCloudSyncWarningWithProviderName()
    {
        var assessor = MakeAssessor(cloudRoots: new[]
        {
            new CloudSyncRoot("Dropbox", FakeDropbox)
        });
        var result = assessor.Assess(Path.Combine(FakeDropbox, "Work", "Schedule.db"));

        var warning = result.Warnings.FirstOrDefault(w => w.Kind == WarningKind.CloudSynced);
        Assert.NotNull(warning);
        Assert.Contains("Dropbox", warning!.Message);
    }

    [Fact]
    public void Assess_PathNotUnderAnyCloudRoot_NoCloudWarning()
    {
        var assessor = MakeAssessor(cloudRoots: new[]
        {
            new CloudSyncRoot("OneDrive", FakeOneDrive),
            new CloudSyncRoot("Dropbox", FakeDropbox),
        });
        var result = assessor.Assess(@"D:\TermPoint\Data");

        Assert.DoesNotContain(result.Warnings, w => w.Kind == WarningKind.CloudSynced);
    }

    [Fact]
    public void Assess_CloudSyncCheckIsCaseInsensitive()
    {
        var assessor = MakeAssessor(cloudRoots: new[]
        {
            new CloudSyncRoot("OneDrive", FakeOneDrive)
        });
        var result = assessor.Assess(FakeOneDrive.ToUpperInvariant() + @"\DB");

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CloudSynced);
    }

    // ── Combined warnings ───────────────────────────────────────────────────

    [Fact]
    public void Assess_PathUnderBothCfaAndCloud_ReturnsBothWarnings()
    {
        // OneDrive Known Folder Move can redirect Documents under OneDrive,
        // so a path can be both CFA-protected and cloud-synced.
        var redirectedDocs = Path.Combine(FakeOneDrive, "Documents");
        var assessor = MakeAssessor(
            cfaRoots: new[] { redirectedDocs },
            cloudRoots: new[] { new CloudSyncRoot("OneDrive", FakeOneDrive) });

        var result = assessor.Assess(Path.Combine(redirectedDocs, "Schedule"));

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CloudSynced);
    }

    [Fact]
    public void Assess_CleanPath_NoWarnings()
    {
        var assessor = MakeAssessor(
            cfaRoots: new[] { FakeDocs, FakeDesktop },
            cloudRoots: new[] { new CloudSyncRoot("OneDrive", FakeOneDrive) });

        var result = assessor.Assess(@"D:\TermPoint\MyUniversity");

        Assert.Empty(result.Warnings);
        Assert.True(result.IsSuitable);
    }

    // ── Writability ─────────────────────────────────────────────────────────

    [Fact]
    public void Assess_WritableFolder_IsWritableTrue()
    {
        var assessor = MakeAssessor(probe: _ => true);
        var result = assessor.Assess(@"D:\TermPoint");

        Assert.True(result.IsWritable);
        Assert.DoesNotContain(result.Warnings, w => w.Kind == WarningKind.NotWritable);
    }

    [Fact]
    public void Assess_NonWritableFolder_ReturnsNotWritableWarning()
    {
        var assessor = MakeAssessor(probe: _ => false);
        var result = assessor.Assess(@"D:\TermPoint");

        Assert.False(result.IsWritable);
        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.NotWritable);
    }

    [Fact]
    public void Assess_NonWritableAndCfaProtected_ReturnsBothWarnings()
    {
        var assessor = MakeAssessor(
            cfaRoots: new[] { FakeDocs },
            probe: _ => false);
        var result = assessor.Assess(Path.Combine(FakeDocs, "DB"));

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.CfaProtected);
        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.NotWritable);
    }

    // ── Invalid paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Assess_NullOrBlank_ReturnsInvalidPathWarning(string? path)
    {
        var assessor = MakeAssessor();
        var result = assessor.Assess(path!);

        Assert.Contains(result.Warnings, w => w.Kind == WarningKind.InvalidPath);
        Assert.False(result.IsWritable);
    }

    // ── IsSuitable convenience ──────────────────────────────────────────────

    [Fact]
    public void IsSuitable_TrueOnlyWhenNoWarnings()
    {
        var assessor = MakeAssessor(probe: _ => true);

        var good = assessor.Assess(@"D:\TermPoint");
        Assert.True(good.IsSuitable);

        var bad = assessor.Assess(null!);
        Assert.False(bad.IsSuitable);
    }

    // ── Resolved path ───────────────────────────────────────────────────────

    [Fact]
    public void Assess_ResolvesPathToFullPath()
    {
        var assessor = MakeAssessor();
        var result = assessor.Assess(@"D:\TermPoint\..\TermPoint\Data");

        Assert.Equal(Path.GetFullPath(@"D:\TermPoint\Data"), result.ResolvedPath);
    }

    // ── IsAtOrUnder edge cases ──────────────────────────────────────────────

    [Fact]
    public void IsAtOrUnder_PrefixThatIsNotAncestor_ReturnsFalse()
    {
        // "C:\Users\TestUser\DocumentsBackup" starts with "C:\Users\TestUser\Documents"
        // as a string, but is not actually under it.
        Assert.False(FolderAssessor.IsAtOrUnder(
            @"C:\Users\TestUser\DocumentsBackup",
            @"C:\Users\TestUser\Documents"));
    }

    [Fact]
    public void IsAtOrUnder_TrailingSeparators_StillMatches()
    {
        Assert.True(FolderAssessor.IsAtOrUnder(
            @"C:\Users\TestUser\Documents\",
            @"C:\Users\TestUser\Documents\"));
    }

    // ── Dropbox info.json parsing ───────────────────────────────────────────

    [Fact]
    public void ExtractJsonStringValues_ParsesDropboxInfoJson()
    {
        var json = @"{
            ""personal"": {
                ""path"": ""C:\\Users\\TestUser\\Dropbox"",
                ""host"": 12345
            },
            ""business"": {
                ""path"": ""C:\\Users\\TestUser\\Dropbox (Work)"",
                ""host"": 67890
            }
        }";

        var paths = FolderAssessor.ExtractJsonStringValues(json, "path").ToList();

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Users\TestUser\Dropbox", paths);
        Assert.Contains(@"C:\Users\TestUser\Dropbox (Work)", paths);
    }

    [Fact]
    public void ExtractJsonStringValues_EmptyJson_ReturnsNothing()
    {
        var paths = FolderAssessor.ExtractJsonStringValues("{}", "path").ToList();
        Assert.Empty(paths);
    }

    [Fact]
    public void ExtractJsonStringValues_NoMatchingKey_ReturnsNothing()
    {
        var json = @"{ ""name"": ""test"" }";
        var paths = FolderAssessor.ExtractJsonStringValues(json, "path").ToList();
        Assert.Empty(paths);
    }

    // ── FolderWarning factory messages ──────────────────────────────────────

    [Fact]
    public void CfaProtected_Warning_MentionsWindowsDefender()
    {
        var warning = FolderWarning.CfaProtected(@"C:\Users\TestUser\Documents");
        Assert.Contains("Windows Defender", warning.Message);
    }

    [Fact]
    public void CloudSynced_Warning_MentionsProvider()
    {
        var warning = FolderWarning.CloudSynced("Google Drive");
        Assert.Contains("Google Drive", warning.Message);
        Assert.Contains("corrupt", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotWritable_Warning_SuggestsAction()
    {
        var warning = FolderWarning.NotWritable();
        Assert.Contains("permissions", warning.Message, StringComparison.OrdinalIgnoreCase);
    }
}
