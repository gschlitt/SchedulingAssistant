using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="AppSettings.ExtensionData"/> (spec subject-colours P4-4) and the two subject-colour keys:
/// unknown keys survive a load and save, and a file written by 1.2.3 loads and re-saves unchanged.
/// </summary>
public sealed class AppSettingsExtensionDataTests : IDisposable
{
    private static readonly string SettingsPath = AppSettings.SettingsFilePath;
    private readonly string? _savedJson;

    /// <summary>Remembers the current settings file so it can be restored.</summary>
    public AppSettingsExtensionDataTests()
    {
        _savedJson = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
    }

    /// <summary>Restores the settings file and the cached instance.</summary>
    public void Dispose()
    {
        try
        {
            if (_savedJson is not null) File.WriteAllText(SettingsPath, _savedJson);
            else if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
        }
        catch { /* best effort */ }
        AppSettings.Load();
    }

    /// <summary>Replaces the settings file's content.</summary>
    private static void WriteFile(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>An unknown key survives Load then Save, equal in the re-read file.</summary>
    [Fact]
    public void UnknownKey_SurvivesLoadAndSave()
    {
        WriteFile("{\"InstallId\":\"abc\",\"FutureSetting\":{\"a\":[1,2]}}");
        var s = AppSettings.Load();
        Assert.NotNull(s.ExtensionData);
        Assert.True(s.Save());

        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        var fut = doc.RootElement.GetProperty("FutureSetting");
        using var expected = JsonDocument.Parse("{\"a\":[1,2]}");
        Assert.True(JsonElement.DeepEquals(expected.RootElement, fut));
        Assert.Equal("abc", doc.RootElement.GetProperty("InstallId").GetString());
    }

    /// <summary>A 1.2.3-shaped file loads with every value intact, no colours, and survives Save + Load.</summary>
    [Fact]
    public void OldShapedFile_LoadsIntact_AndRoundTrips()
    {
        WriteFile("""
        {
          "InstallId": "0123456789abcdef",
          "IsInitialSetupComplete": true,
          "AlwaysOpenMostRecentDatabase": true,
          "InstitutionName": "Greendale",
          "InstitutionAbbrev": "GCC",
          "DatabasePath": "C:\\data\\x.db",
          "IncludeSaturday": true,
          "BlockLengthUnit": 1,
          "TileFontSize": 12.5,
          "SectionConflictDisplay": 1,
          "RecentDatabases": ["C:\\data\\x.db", "C:\\data\\y.db"],
          "LastSelectedSemesterIds": ["s1", "s2"],
          "BackupIntervalMinutes": 45,
          "VirtualizeSectionList": true
        }
        """);

        var s = AppSettings.Load();
        Assert.Equal("0123456789abcdef", s.InstallId);
        Assert.True(s.IsInitialSetupComplete);
        Assert.True(s.AlwaysOpenMostRecentDatabase);
        Assert.Equal("Greendale", s.InstitutionName);
        Assert.Equal("GCC", s.InstitutionAbbrev);
        Assert.Equal("C:\\data\\x.db", s.DatabasePath);
        Assert.True(s.IncludeSaturday);
        Assert.Equal(12.5, s.TileFontSize);
        Assert.Equal(new[] { "C:\\data\\x.db", "C:\\data\\y.db" }, s.RecentDatabases);
        Assert.Equal(new[] { "s1", "s2" }, s.LastSelectedSemesterIds);
        Assert.Equal(45, s.BackupIntervalMinutes);
        Assert.True(s.VirtualizeSectionList);
        Assert.Empty(s.SubjectColors);
        Assert.Equal(SubjectColorDisplay.OnlyWhenSeveral, s.SubjectColorDisplay);
        Assert.True(s.ExtensionData is null || s.ExtensionData.Count == 0);

        Assert.True(s.Save());
        var again = AppSettings.Load();
        Assert.Equal("0123456789abcdef", again.InstallId);
        Assert.Equal("Greendale", again.InstitutionName);
        Assert.Equal(12.5, again.TileFontSize);
        Assert.Equal(new[] { "C:\\data\\x.db", "C:\\data\\y.db" }, again.RecentDatabases);
        Assert.Equal(45, again.BackupIntervalMinutes);
        Assert.True(again.VirtualizeSectionList);
        Assert.Empty(again.SubjectColors);
        Assert.True(again.ExtensionData is null || again.ExtensionData.Count == 0);
    }

    /// <summary>SubjectColors and SubjectColorDisplay round-trip through the file.</summary>
    [Fact]
    public void SubjectColors_RoundTrip()
    {
        WriteFile("{\"InstallId\":\"abc\"}");
        var s = AppSettings.Load();
        s.SubjectColors["BIOL"] = "#B7E79D";
        s.SubjectColors["CHEM"] = "#EBB4E6";
        s.SubjectColorDisplay = SubjectColorDisplay.Never;
        Assert.True(s.Save());

        var again = AppSettings.Load();
        Assert.Equal("#B7E79D", again.SubjectColors["BIOL"]);
        Assert.Equal("#EBB4E6", again.SubjectColors["CHEM"]);
        Assert.Equal(SubjectColorDisplay.Never, again.SubjectColorDisplay);
    }
}
