using System.IO.Compression;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the Support panel (Dev Tools → Support...): listing the daily logs (<see cref="SupportLogs.List"/>), packing
/// the ticked ones into one zip (<see cref="SupportLogs.WriteZip"/>, including today's log while it is still open for
/// writing), and the panel's own behaviour (<see cref="SupportViewModel"/>: newest log ticked, cancel changes nothing,
/// the Support ID is the install ID).
/// </summary>
public sealed class SupportPanelTests : IDisposable
{
    /// <summary>"Today" for every test.</summary>
    private static readonly DateTime Today = new(2026, 11, 11);

    private readonly string _tempDir;
    private readonly string _logDir;

    /// <summary>Creates an isolated log folder and output folder.</summary>
    public SupportPanelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"support_{Guid.NewGuid():N}");
        _logDir = Path.Combine(_tempDir, "Logs");
        Directory.CreateDirectory(_logDir);
    }

    /// <summary>Removes the temporary folders (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Writes a log file with the given name and text into the test log folder.</summary>
    private string WriteLog(string name, string text = "log line")
    {
        var path = Path.Combine(_logDir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void List_ReturnsDailyLogsNewestFirst_AndSkipsOtherFiles()
    {
        WriteLog("app-2026-11-09.log");
        WriteLog("app-2026-11-11.log");
        WriteLog("app-2026-11-10.log");
        WriteLog("notes.txt");
        WriteLog("app-yesterday.log");

        var logs = SupportLogs.List(_logDir);

        Assert.Equal(new[] { new DateTime(2026, 11, 11), new DateTime(2026, 11, 10), new DateTime(2026, 11, 9) },
            logs.Select(l => l.Day));
    }

    [Fact]
    public void List_MissingFolder_IsEmpty()
    {
        Assert.Empty(SupportLogs.List(Path.Combine(_tempDir, "nope")));
    }

    [Fact]
    public void WriteZip_ContainsEachChosenLogUnderItsOwnName()
    {
        var a = WriteLog("app-2026-11-10.log", "first");
        var b = WriteLog("app-2026-11-11.log", "second");
        var zipPath = Path.Combine(_tempDir, "out.zip");

        SupportLogs.WriteZip(new[] { a, b }, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(new[] { "app-2026-11-10.log", "app-2026-11-11.log" }, zip.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(zip.GetEntry("app-2026-11-11.log")!.Open());
        Assert.Equal("second", reader.ReadToEnd());
    }

    [Fact]
    public void WriteZip_ReadsTodaysLogWhileTheLoggerHasItOpen()
    {
        var today = WriteLog("app-2026-11-11.log", "in use");
        var zipPath = Path.Combine(_tempDir, "out.zip");

        // The logger appends with FileAccess.Write and FileShare.Read (File.AppendAllLines).
        using (new FileStream(today, FileMode.Append, FileAccess.Write, FileShare.Read))
            SupportLogs.WriteZip(new[] { today }, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Single(zip.Entries);
    }

    [Fact]
    public void WriteZip_MissingLog_ThrowsAndLeavesNoPartZip()
    {
        var zipPath = Path.Combine(_tempDir, "out.zip");

        Assert.ThrowsAny<IOException>(() =>
            SupportLogs.WriteZip(new[] { Path.Combine(_logDir, "app-2026-11-11.log") }, zipPath));
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public void SuggestedZipName_HasTheDate()
    {
        Assert.Equal("TermPoint logs 2026-11-11.zip", SupportLogs.SuggestedZipName(Today));
    }

    [Theory]
    [InlineData(0, "1 KB")]
    [InlineData(1, "1 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1025, "2 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    public void Size_IsWholeKbOrMbWithOneDecimal(long bytes, string expected)
    {
        Assert.Equal(expected.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
            SupportMessages.Size(bytes));
    }

    [Fact]
    public void Panel_TicksOnlyTheNewestLog_AndMarksToday()
    {
        WriteLog("app-2026-11-10.log");
        WriteLog("app-2026-11-11.log");

        var vm = NewPanel(new FakePlatform());

        Assert.Equal(new[] { true, false }, vm.Logs.Select(l => l.IsSelected));
        Assert.Contains("(today)", vm.Logs[0].Label);
        Assert.DoesNotContain("(today)", vm.Logs[1].Label);
    }

    [Fact]
    public async Task Panel_Save_WritesTheTickedLogsToTheChosenPath()
    {
        WriteLog("app-2026-11-10.log");
        WriteLog("app-2026-11-11.log");
        var zipPath = Path.Combine(_tempDir, "chosen.zip");
        var platform = new FakePlatform { SavePath = zipPath };
        var vm = NewPanel(platform);
        vm.Logs[1].IsSelected = true;

        await vm.SaveLogsCommand.ExecuteAsync(null);

        Assert.Equal("TermPoint logs 2026-11-11.zip", platform.SuggestedName);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Equal(zipPath, vm.SavedZipPath);
        Assert.False(vm.IsLogsStatusError);
        Assert.Equal(SupportMessages.Saved(zipPath), vm.LogsStatus);
    }

    [Fact]
    public async Task Panel_Save_CancelledDialog_ChangesNothing()
    {
        WriteLog("app-2026-11-11.log");
        var vm = NewPanel(new FakePlatform { SavePath = null });

        await vm.SaveLogsCommand.ExecuteAsync(null);

        Assert.Null(vm.LogsStatus);
        Assert.Null(vm.SavedZipPath);
    }

    [Fact]
    public async Task Panel_Save_NothingTicked_SaysSoWithoutOpeningTheDialog()
    {
        WriteLog("app-2026-11-11.log");
        var platform = new FakePlatform { SavePath = Path.Combine(_tempDir, "x.zip") };
        var vm = NewPanel(platform);
        vm.Logs[0].IsSelected = false;

        await vm.SaveLogsCommand.ExecuteAsync(null);

        Assert.Null(platform.SuggestedName);
        Assert.Equal(SupportMessages.NothingTicked, vm.LogsStatus);
        Assert.True(vm.IsLogsStatusError);
    }

    [Fact]
    public async Task Panel_SupportId_IsTheInstallId_AndCopies()
    {
        var platform = new FakePlatform();
        var vm = NewPanel(platform, installId: "abc123");

        Assert.False(vm.IsSupportIdShown);
        vm.ShowSupportIdCommand.Execute(null);
        await vm.CopySupportIdCommand.ExecuteAsync(null);

        Assert.Equal("abc123", vm.SupportId);
        Assert.Equal("abc123", platform.Clipboard);
        Assert.Equal(SupportMessages.Copied, vm.SupportIdStatus);
    }

    /// <summary>A panel over the test log folder, a fixed install ID and <see cref="Today"/>.</summary>
    private SupportViewModel NewPanel(FakePlatform platform, string installId = "id")
        => new(platform, _logDir, () => installId, () => Today);

    /// <summary>A fake <see cref="ISupportPlatform"/> that records what the panel asked for.</summary>
    private sealed class FakePlatform : ISupportPlatform
    {
        /// <summary>What the Save dialog "returns"; null = cancelled.</summary>
        public string? SavePath { get; init; }

        /// <summary>The name the panel suggested; null if the dialog was never opened.</summary>
        public string? SuggestedName { get; private set; }

        /// <summary>The last text put on the clipboard.</summary>
        public string? Clipboard { get; private set; }

        public Task<string?> PickZipToSaveAsync(string suggestedFileName)
        {
            SuggestedName = suggestedFileName;
            return Task.FromResult(SavePath);
        }

        public Task<bool> SetClipboardTextAsync(string text)
        {
            Clipboard = text;
            return Task.FromResult(true);
        }

        public void ShowInFolder(string filePath) { }
    }
}
