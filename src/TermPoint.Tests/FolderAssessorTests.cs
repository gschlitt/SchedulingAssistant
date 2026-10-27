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
    }

    /// <summary>
    /// Spec item 28 (from item 13): the cloud-folder warning says what really happens, in the approved wording, and
    /// keeps its detail line for IT. It names no database engine and does not say "corrupt".
    /// </summary>
    [Fact]
    public void CloudSynced_Warning_IsTheApprovedWording_AndKeepsItsDetail()
    {
        var warning = FolderWarning.CloudSynced("OneDrive");

        Assert.Equal(WarningKind.CloudSynced, warning.Kind);
        Assert.Equal(
            "This folder is kept in sync by OneDrive. Sync services don't respect the way TermPoint marks a database as in use, " +
            "so two people could edit at once and one person's changes could be lost. " +
            "Choose a folder that isn't synced, such as a shared network drive.",
            warning.Message);
        Assert.Equal("Cloud sync provider: OneDrive", warning.Detail);
        Assert.DoesNotContain("SQLite", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corrupt", warning.Message, StringComparison.OrdinalIgnoreCase);
        // The rules for every text of spec item 28 (rule 4 of the four rules for the shared database).
        foreach (var forbidden in new[] { "working copy", "local copy", "safe", "D'", "lock file", "write access", "exception", ":\\" })
            Assert.DoesNotContain(forbidden, warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotWritable_Warning_SuggestsAction()
    {
        var warning = FolderWarning.NotWritable();
        Assert.Contains("permissions", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── SharePoint / Teams libraries synced through OneDrive (spec item 13) ──

    private static readonly string FakeLibrary =
        Path.GetFullPath(@"C:\Users\TestUser\University of Example\BIOL Dept - Documents");

    /// <summary>
    /// A library entry under SyncEngines is recognised by its LibraryType ("teamsite", any case) or by a SharePoint web
    /// address that is not the personal "-my" host; the user's own OneDrive (business or personal) and an entry with
    /// nothing to go on are not libraries.
    /// </summary>
    [Theory]
    [InlineData("teamsite", null, true)]
    [InlineData("TeamSite", null, true)]
    [InlineData(null, "https://example.sharepoint.com/sites/BIOL/Shared Documents/", true)]
    [InlineData("mysite", "https://example-my.sharepoint.com/personal/test_example_ca/Documents/", false)]
    [InlineData(null, "https://example-my.sharepoint.com/personal/test_example_ca/Documents/", false)]
    [InlineData(null, "https://d.docs.live.net/0123456789abcdef/", false)]
    [InlineData(null, "not a web address", false)]
    [InlineData(null, null, false)]
    public void IsSharePointLibrary_TellsALibraryFromTheUsersOwnOneDrive(string? libraryType, string? url, bool expected)
        => Assert.Equal(expected, FolderAssessor.IsSharePointLibrary(libraryType, url));

    /// <summary>
    /// A synced library outside the OneDrive folder becomes a root with the library label; the user's own OneDrive
    /// folder from the registry becomes a plain "OneDrive" root, listed first.
    /// </summary>
    [Fact]
    public void OneDriveRegistryRoots_LabelsLibrariesAndTheUsersOwnFolder()
    {
        var roots = FolderAssessor.OneDriveRegistryRoots(new[]
        {
            new OneDriveSyncedFolder(FakeLibrary, IsLibrary: true),
            new OneDriveSyncedFolder(FakeOneDrive, IsLibrary: false),
        }, Array.Empty<CloudSyncRoot>());

        Assert.Equal(new[]
        {
            new CloudSyncRoot("OneDrive", FakeOneDrive),
            new CloudSyncRoot(FolderAssessor.SharePointLibraryProvider, FakeLibrary),
        }, roots);
    }

    /// <summary>
    /// A library inside a OneDrive folder already known (a shortcut added to "My files") is left to that folder's own
    /// root, whether the folder came from the environment variables or from the registry itself.
    /// </summary>
    [Fact]
    public void OneDriveRegistryRoots_LibraryInsideAKnownOneDriveFolder_IsCoveredByThatFolder()
    {
        var shortcut = Path.Combine(FakeOneDrive, "BIOL Dept - Documents");

        var fromEnvironment = FolderAssessor.OneDriveRegistryRoots(
            new[] { new OneDriveSyncedFolder(shortcut, IsLibrary: true) },
            new[] { new CloudSyncRoot("OneDrive", FakeOneDrive) });
        Assert.Empty(fromEnvironment);

        var fromRegistry = FolderAssessor.OneDriveRegistryRoots(new[]
        {
            new OneDriveSyncedFolder(FakeOneDrive, IsLibrary: false),
            new OneDriveSyncedFolder(shortcut, IsLibrary: true),
        }, Array.Empty<CloudSyncRoot>());
        Assert.Equal(new[] { new CloudSyncRoot("OneDrive", FakeOneDrive) }, fromRegistry);
    }

    /// <summary>A Dropbox (or any other non-OneDrive) root never hides a library, and empty or unparseable paths are skipped.</summary>
    [Fact]
    public void OneDriveRegistryRoots_IgnoresOtherProvidersAndBadPaths()
    {
        var roots = FolderAssessor.OneDriveRegistryRoots(new[]
        {
            new OneDriveSyncedFolder(FakeLibrary, IsLibrary: true),
            new OneDriveSyncedFolder("   ", IsLibrary: true),
            new OneDriveSyncedFolder("bad\0path", IsLibrary: false),
        }, new[] { new CloudSyncRoot("Dropbox", Path.GetFullPath(@"C:\Users\TestUser")) });

        Assert.Equal(new[] { new CloudSyncRoot(FolderAssessor.SharePointLibraryProvider, FakeLibrary) }, roots);
    }

    /// <summary>
    /// A database folder inside a synced library gets the cloud-folder warning (warn only, never a block), in the
    /// approved wording with the library label, and its detail line names it for IT.
    /// </summary>
    [Fact]
    public void Assess_PathInsideASyncedLibrary_WarnsWithTheLibraryLabel()
    {
        var assessor = MakeAssessor(cloudRoots: new[]
        {
            new CloudSyncRoot("OneDrive", FakeOneDrive),
            new CloudSyncRoot(FolderAssessor.SharePointLibraryProvider, FakeLibrary),
        });

        var result = assessor.Assess(Path.Combine(FakeLibrary, "TermPoint"));

        var warning = Assert.Single(result.Warnings, w => w.Kind == WarningKind.CloudSynced);
        Assert.Equal(
            "This folder is kept in sync by OneDrive (a SharePoint or Teams library). Sync services don't respect the way " +
            "TermPoint marks a database as in use, so two people could edit at once and one person's changes could be lost. " +
            "Choose a folder that isn't synced, such as a shared network drive.",
            warning.Message);
        Assert.Equal("Cloud sync provider: OneDrive (a SharePoint or Teams library)", warning.Detail);
        Assert.True(result.IsWritable);
    }

    /// <summary>
    /// The real discovery, on whatever machine runs the tests (with or without OneDrive and its registry entries): it never
    /// throws, every root it finds exists, and no folder is listed twice.
    /// </summary>
    [Fact]
    public void DiscoverCloudSyncRoots_OnThisMachine_DoesNotThrow_AndListsEachFolderOnce()
    {
        var roots = FolderAssessor.DiscoverCloudSyncRoots();

        Assert.All(roots, r => Assert.True(Directory.Exists(r.RootPath), $"{r.Provider} root does not exist: {r.RootPath}"));
        Assert.Equal(roots.Count, roots.Select(r => r.RootPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
