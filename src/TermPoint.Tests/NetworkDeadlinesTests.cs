using Microsoft.Data.Sqlite;
using Moq;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// 1.2.4 item 4.10: network file calls outside the save path get a deadline. Covers backup rotation, the backup
/// list, the sections CSV, <see cref="ExportFileWriter"/>, the first bytes of CSV and .tpconfig files, and folder
/// suggestions on a mapped drive. Time-outs are simulated with the <see cref="NetworkFaults"/> injector (the call
/// reports a time-out at once, without a real five-second wait), exactly as the other <see cref="NetworkFileOps"/>
/// tests do. The injector is restored in <see cref="Dispose"/>.
/// </summary>
public sealed class NetworkDeadlinesTests : IDisposable
{
    /// <summary>"Now" for the rotation tests: Wednesday 11 November 2026, 10:00.</summary>
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0);

    /// <summary>The three bytes of a UTF-8 byte-order mark.</summary>
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Isolated folder playing the backup folder (and the export destination).</summary>
    private readonly string _dir;

    /// <summary>Every operation the injector was asked about, in order.</summary>
    private readonly List<NetworkFaultRequest> _requests = new();

    /// <summary>Creates the folder.</summary>
    public NetworkDeadlinesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"netdl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Clears the injector and deletes the folder (best effort).</summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a small file in the test folder.</summary>
    /// <param name="name">File name.</param>
    /// <returns>The full path.</returns>
    private string Make(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    /// <summary>
    /// Installs an injector that records every request and applies <paramref name="decide"/> to it.
    /// </summary>
    /// <param name="decide">The decision for one request; null for "let it run".</param>
    private void Inject(Func<NetworkFaultRequest, NetworkFaultDecision?> decide)
        => NetworkFaults.Decide = r =>
        {
            lock (_requests) _requests.Add(r);
            return decide(r) ?? NetworkFaultDecision.None;
        };

    /// <summary>
    /// The scenario of Alice's earlier-day story: three backups on 10 November (09:00, 12:00, 18:00) and one on the
    /// 11th. With "keep 1 recent" the 09:00 and 12:00 backups (and their CSVs) are the ones to delete, oldest first.
    /// </summary>
    /// <returns>The paths: (morning db, morning csv, noon db, noon csv, evening db, evening csv, today's db).</returns>
    private (string, string, string, string, string, string, string) MakeRotationScenario() => (
        Make("BIOL_2026-11-10_09-00-00.db"), Make("BIOL_2026-11-10_09-00-00_2026-2027_sections.csv"),
        Make("BIOL_2026-11-10_12-00-00.db"), Make("BIOL_2026-11-10_12-00-00_2026-2027_sections.csv"),
        Make("BIOL_2026-11-10_18-00-00.db"), Make("BIOL_2026-11-10_18-00-00_2026-2027_sections.csv"),
        Make("BIOL_2026-11-11_09-00-00.db"));

    // ── Rotation (A3) ─────────────────────────────────────────────────────────

    /// <summary>The folder listing times out: rotation deletes nothing and says so in one log line.</summary>
    [Fact]
    public async Task Rotation_WhenTheListingTimesOut_DeletesNothing()
    {
        var (am, amCsv, noon, noonCsv, eve, eveCsv, today) = MakeRotationScenario();
        Inject(r => r.Label == "Rotate.GetFiles" ? NetworkFaultDecision.TimeOut : null);
        var logger = new RecordingAppLogger();

        await BackupService.RotateBackupsAsync(_dir, "BIOL", 1, Now, logger);

        foreach (var f in new[] { am, amCsv, noon, noonCsv, eve, eveCsv, today })
            Assert.True(File.Exists(f), f);
        Assert.DoesNotContain(_requests, r => r.Label == "File.Delete");
        Assert.Contains(logger.Infos, l => l.Contains("not rotated"));
    }

    /// <summary>
    /// The first delete times out: rotation stops there (one delete asked about, nothing else touched) and the next
    /// backup's rotation will finish the job.
    /// </summary>
    [Fact]
    public async Task Rotation_StopsAtTheFirstDeleteThatTimesOut()
    {
        var (am, amCsv, noon, noonCsv, eve, eveCsv, today) = MakeRotationScenario();
        Inject(r => r.Label == "File.Delete" ? NetworkFaultDecision.TimeOut : null);
        var logger = new RecordingAppLogger();

        await BackupService.RotateBackupsAsync(_dir, "BIOL", 1, Now, logger);

        Assert.Single(_requests, r => r.Label == "File.Delete");
        foreach (var f in new[] { am, amCsv, noon, noonCsv, eve, eveCsv, today })
            Assert.True(File.Exists(f), f);   // a timed-out delete is not run, and nothing after it is attempted
        Assert.Contains(logger.Infos, l => l.Contains("stopped early"));
    }

    /// <summary>
    /// A delete that is REFUSED (the oldest backup's CSV is open in Excel) is skipped, not a reason to stop: the rest of
    /// rotation still runs. Stopping there would make every later rotation stop at the same locked file, and old
    /// backups would pile up for as long as it stays open.
    /// </summary>
    [Fact]
    public async Task Rotation_SkipsARefusedDelete_AndCarriesOn()
    {
        var (am, amCsv, noon, noonCsv, eve, eveCsv, today) = MakeRotationScenario();
        Inject(_ => null);
        var logger = new RecordingAppLogger();

        // Hold the morning CSV open with no sharing, as Excel does: deleting it fails within the deadline.
        using (new FileStream(amCsv, FileMode.Open, FileAccess.Read, FileShare.None))
            await BackupService.RotateBackupsAsync(_dir, "BIOL", 1, Now, logger);

        Assert.True(File.Exists(amCsv));                                   // the locked file stays
        foreach (var gone in new[] { am, noon, noonCsv }) Assert.False(File.Exists(gone), gone);
        foreach (var kept in new[] { eve, eveCsv, today }) Assert.True(File.Exists(kept), kept);
        Assert.Contains(logger.Infos, l => l.Contains("skipped a file"));
        Assert.DoesNotContain(logger.Infos, l => l.Contains("stopped early"));
    }

    /// <summary>
    /// Healthy folder: one listing only, the companion CSVs are matched in memory (each pair is deleted), and the
    /// kept backups and their CSVs stay.
    /// </summary>
    [Fact]
    public async Task Rotation_ListsOnce_AndMatchesCsvsInMemory()
    {
        var (am, amCsv, noon, noonCsv, eve, eveCsv, today) = MakeRotationScenario();
        Inject(_ => null);

        await BackupService.RotateBackupsAsync(_dir, "BIOL", 1, Now);

        Assert.Single(_requests, r => r.Label == "Rotate.GetFiles");
        Assert.Equal(4, _requests.Count(r => r.Label == "File.Delete"));   // two databases + two CSVs, nothing else
        foreach (var gone in new[] { am, amCsv, noon, noonCsv }) Assert.False(File.Exists(gone), gone);
        foreach (var kept in new[] { eve, eveCsv, today })       Assert.True(File.Exists(kept), kept);
    }

    /// <summary>The in-memory CSV match follows the old glob: slug in the middle, case-insensitive, no cross-matches.</summary>
    [Fact]
    public void MatchCompanionCsvs_FollowsTheOldGlob()
    {
        var db1 = @"C:\b\BIOL_2026-11-10_09-00-00.db";
        var db2 = @"C:\b\BIOL_2026-11-10_12-00-00.db";
        var csv1 = @"C:\b\BIOL_2026-11-10_09-00-00_2026-2027_sections.csv";
        var files = new[]
        {
            db1, db2, csv1,
            @"C:\b\BIOL_2026-11-10_12-00-00_sections.csv",       // no slug: the glob needs two underscores, so no match
            @"C:\b\BIOL_2026-11-10_12-00-00_notes.txt",
        };

        var matches = BackupService.MatchCompanionCsvs(new[] { db1, db2 }, files);

        Assert.Equal(csv1, matches[db1]);
        Assert.False(matches.ContainsKey(db2));

        var upper = BackupService.MatchCompanionCsvs(new[] { db1 }, new[] { db1, csv1.ToUpperInvariant() });
        Assert.Equal(csv1.ToUpperInvariant(), upper[db1]);
    }

    // ── Backup list (A4) ──────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="BackupService.BuildBackupEntries"/>: HasCsv comes from the listing alone (no file check), and an
    /// unparseable name gets its creation time (asked only for that file).
    /// </summary>
    [Fact]
    public void BuildBackupEntries_SetsHasCsvFromTheListing_AndFallsBackToCreationTimeOnlyWhenNeeded()
    {
        var withCsv = @"X:\nowhere\BIOL_2026-11-10_09-00-00.db";
        var without = @"X:\nowhere\BIOL_2026-11-11_09-00-00.db";
        var odd     = @"X:\nowhere\BIOL_manual-copy.db";
        var csv     = @"X:\nowhere\BIOL_2026-11-10_09-00-00_2026-2027_sections.csv";
        var created = new DateTime(2020, 1, 2, 3, 4, 5);
        var asked = new List<string>();

        var entries = BackupService.BuildBackupEntries(
            new[] { withCsv, without, odd, csv }, f => { asked.Add(f); return created; });

        Assert.Equal(3, entries.Count);   // the CSV is not a backup entry of its own
        Assert.True(entries.Single(e => e.DbPath == withCsv).HasCsv);
        Assert.Equal(csv, entries.Single(e => e.DbPath == withCsv).CsvPath);
        Assert.False(entries.Single(e => e.DbPath == without).HasCsv);
        Assert.Equal(new DateTime(2026, 11, 11, 9, 0, 0), entries.Single(e => e.DbPath == without).Timestamp);
        Assert.Equal(created, entries.Single(e => e.DbPath == odd).Timestamp);
        Assert.Equal(new[] { odd }, asked);
    }

    /// <summary>
    /// <see cref="BackupService.GetBackupsAsync"/> lists the folder once, and the rows keep their HasCsv value even
    /// after the CSV is removed from disk (it is a plain value fixed at listing time, never a per-read file check).
    /// </summary>
    [Fact]
    public async Task GetBackupsAsync_ListsOnce_AndHasCsvIsAPlainValue()
    {
        var csv = Make("BIOL_2026-11-10_09-00-00_2026-2027_sections.csv");
        Make("BIOL_2026-11-10_09-00-00.db");
        Make("BIOL_2026-11-11_09-00-00.db");

        var savedFolder = AppSettings.Current.BackupFolderPath;
        var savedDb     = AppSettings.Current.DatabasePath;
        try
        {
            AppSettings.Current.BackupFolderPath = _dir;
            AppSettings.Current.DatabasePath     = Path.Combine(_dir, "BIOL.db");
            Inject(_ => null);
            var db = new Mock<IDatabaseContext>();

            var entries = await new BackupService(
                db.Object, new SemesterContext(), null!, null!, null!, null!, null!, null!, null!, new RecordingAppLogger())
                .GetBackupsAsync();

            Assert.Equal(2, entries.Count);
            Assert.Equal(new DateTime(2026, 11, 11, 9, 0, 0), entries[0].Timestamp);   // newest first
            Assert.Single(_requests, r => r.Label == "GetBackups.GetFiles");
            Assert.False(entries[0].HasCsv);
            Assert.True(entries[1].HasCsv);

            File.Delete(csv);
            Assert.True(entries[1].HasCsv);   // no file check on read
        }
        finally
        {
            AppSettings.Current.BackupFolderPath = savedFolder;
            AppSettings.Current.DatabasePath     = savedDb;
        }
    }

    // ── Sections CSV (A2) ─────────────────────────────────────────────────────

    /// <summary>A working copy with the real schema (created by <see cref="DatabaseContext"/>), and a service over it.</summary>
    /// <param name="workingCopy">Receives the working copy's path.</param>
    /// <param name="logger">The logger the service records into.</param>
    /// <returns>The service.</returns>
    private BackupService CreateServiceOverRealSchema(out string workingCopy, IAppLogger logger)
    {
        workingCopy = Path.Combine(_dir, "working.db");
        new DatabaseContext(workingCopy).Dispose();
        SqliteConnection.ClearAllPools();
        var path = workingCopy;
        var db = new Mock<IDatabaseContext>();
        db.SetupGet(d => d.DatabasePath).Returns(path);
        return new BackupService(db.Object, new SemesterContext(), null!, null!, null!, null!, null!, null!, null!, logger);
    }

    /// <summary>Points the backup-folder setting at <paramref name="folder"/>; returns what restores it.</summary>
    /// <param name="folder">The new backup folder.</param>
    /// <returns>An action that puts the old setting back.</returns>
    private static Action UseBackupFolder(string folder)
    {
        var saved = AppSettings.Current.BackupFolderPath;
        AppSettings.Current.BackupFolderPath = folder;
        BackupService.ResetBackupFailureNoticeForTests();
        return () => AppSettings.Current.BackupFolderPath = saved;
    }

    /// <summary>
    /// The backup folder stops answering the sections-CSV write: the backup is still a success, there is no CSV
    /// (<c>CsvPath</c> is null, no file), and one local log line says so.
    /// </summary>
    [Fact]
    public async Task SectionsCsvWriteTimesOut_BackupStillSucceeds_WithNoCsv()
    {
        var backupDir = Path.Combine(_dir, "share");
        Directory.CreateDirectory(backupDir);
        var restore = UseBackupFolder(backupDir);
        try
        {
            var logger = new RecordingAppLogger();
            var service = CreateServiceOverRealSchema(out _, logger);
            Inject(r => r.Label == "File.WriteAllBytes" && r.Path!.EndsWith("_sections.csv") ? NetworkFaultDecision.TimeOut : null);

            var result = await service.PerformBackupAsync();

            Assert.True(result.Success);
            Assert.Null(result.CsvPath);
            Assert.Empty(Directory.GetFiles(backupDir, "*.csv"));
            Assert.Single(Directory.GetFiles(backupDir, "*.db"));
            Assert.Single(logger.Infos, l => l.Contains("sections CSV was not written"));
        }
        finally { restore(); }
    }

    /// <summary>A healthy backup writes the sections CSV, and its first bytes are the UTF-8 byte-order mark (Excel).</summary>
    [Fact]
    public async Task SectionsCsv_StartsWithTheUtf8Bom()
    {
        var backupDir = Path.Combine(_dir, "share");
        Directory.CreateDirectory(backupDir);
        var restore = UseBackupFolder(backupDir);
        try
        {
            var service = CreateServiceOverRealSchema(out _, new RecordingAppLogger());

            var result = await service.PerformBackupAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.CsvPath);
            var bytes = File.ReadAllBytes(result.CsvPath!);
            Assert.Equal(Bom, bytes.Take(3));
            Assert.StartsWith("AcademicYear,Semester,", System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }
        finally { restore(); }
    }

    // ── ExportFileWriter ──────────────────────────────────────────────────────

    /// <summary>A healthy write lands the exact bytes and reports Written with no message.</summary>
    [Fact]
    public async Task ExportFileWriter_Written()
    {
        var path = Path.Combine(_dir, "workload.csv");
        var bytes = ExportFileWriter.EncodeUtf8("a,b\r\n1,2\r\n", withBom: true);

        var result = await ExportFileWriter.WriteAsync(path, bytes);

        Assert.True(result.Succeeded);
        Assert.Equal(ExportWriteOutcome.Written, result.Outcome);
        Assert.Equal("workload.csv", result.FileName);
        Assert.Null(result.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>A time-out reports TimedOut with the exact agreed wording (file name only, nothing else).</summary>
    [Fact]
    public async Task ExportFileWriter_TimedOut_UsesTheAgreedWording()
    {
        var path = Path.Combine(_dir, "workload.csv");
        Inject(r => r.Label == "File.WriteAllBytes" ? NetworkFaultDecision.TimeOut : null);

        var result = await ExportFileWriter.WriteAsync(path, new byte[] { 1 });

        Assert.Equal(ExportWriteOutcome.TimedOut, result.Outcome);
        Assert.Equal(
            "TermPoint couldn't save workload.csv: the folder didn't respond. " +
            "Nothing else was affected. Try again, or choose a different folder.",
            result.Message);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// A failed write (the folder is gone) reports Failed with a plain reason: no path and no exception text in the
    /// message; the exception goes to the local log only.
    /// </summary>
    [Fact]
    public async Task ExportFileWriter_Failed_GivesAPlainReason_AndNoPathOrExceptionText()
    {
        var folder = Path.Combine(_dir, "gone");
        var path = Path.Combine(folder, "report.csv");

        var result = await ExportFileWriter.WriteAsync(path, new byte[] { 1 });

        Assert.Equal(ExportWriteOutcome.Failed, result.Outcome);
        Assert.Equal(
            "TermPoint couldn't save report.csv. That folder no longer exists. " +
            "Nothing else was affected. Try again, or choose a different folder.",
            result.Message);
        Assert.DoesNotContain(_dir, result.Message);
        Assert.DoesNotContain("Exception", result.Message);
    }

    /// <summary>The plain reasons for the exceptions a write can throw.</summary>
    [Fact]
    public void PlainReason_MapsEachKindOfFailure()
    {
        Assert.Equal("TermPoint isn't allowed to save in that folder", ExportFileWriter.PlainReason(new UnauthorizedAccessException("secret path")));
        Assert.Equal("That folder no longer exists", ExportFileWriter.PlainReason(new DirectoryNotFoundException()));
        Assert.Equal("The file's location is too long", ExportFileWriter.PlainReason(new PathTooLongException()));
        Assert.Equal("The file is open in another program", ExportFileWriter.PlainReason(new IOException("x", unchecked((int)0x80070020))));
        Assert.Equal("The disk is full", ExportFileWriter.PlainReason(new IOException("x", unchecked((int)0x80070070))));
        Assert.Equal("The folder reported a problem", ExportFileWriter.PlainReason(new IOException("x")));
        Assert.Equal(ExportFileWriter.GenericReason, ExportFileWriter.PlainReason(new InvalidOperationException("x")));
    }

    // ── First bytes ───────────────────────────────────────────────────────────

    /// <summary>CSV exports are encoded with the BOM, and match what <c>UTF8Encoding(true)</c> wrote before.</summary>
    [Fact]
    public void EncodeUtf8_WithBom_StartsWithTheBom_AndMatchesTheOldBytes()
    {
        var bytes = ExportFileWriter.EncodeUtf8("é,1\r\n", withBom: true);

        Assert.Equal(Bom, bytes.Take(3));
        var viaFile = Path.Combine(_dir, "old.csv");
        File.WriteAllText(viaFile, "é,1\r\n", new System.Text.UTF8Encoding(true));
        Assert.Equal(File.ReadAllBytes(viaFile), bytes);
    }

    /// <summary>The .tpconfig file is BOM-less JSON, byte for byte what <c>File.WriteAllText</c> wrote before.</summary>
    [Fact]
    public async Task TpConfig_IsWrittenWithoutBom_AndMatchesTheOldBytes()
    {
        var data = new TpConfigData { Campuses = { "Main" } };

        var (path, result) = await TpConfigService.WriteAsync(_dir, data, "BIOL");

        Assert.True(result.Succeeded);
        Assert.Equal("BIOL-TT.tpconfig", Path.GetFileName(path));
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(Bom, bytes.Take(3));
        Assert.Equal((byte)'{', bytes[0]);

        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        var oldPath = Path.Combine(_dir, "old.tpconfig");
        File.WriteAllText(oldPath, System.Text.Json.JsonSerializer.Serialize(data, opts));
        Assert.Equal(File.ReadAllBytes(oldPath), bytes);
    }

    /// <summary>A .tpconfig write that times out reports the agreed wording instead of writing.</summary>
    [Fact]
    public async Task TpConfig_WhenTheFolderDoesNotRespond_SaysSo()
    {
        Inject(r => r.Label == "File.WriteAllBytes" ? NetworkFaultDecision.TimeOut : null);

        var (_, result) = await TpConfigService.WriteAsync(_dir, new TpConfigData(), "BIOL");

        Assert.Equal(ExportWriteOutcome.TimedOut, result.Outcome);
        Assert.Contains("BIOL-TT.tpconfig: the folder didn't respond.", result.Message);
        Assert.Empty(Directory.GetFiles(_dir, "*.tpconfig"));
    }

    // ── Folder suggestions on a mapped drive (A7) ─────────────────────────────

    /// <summary>
    /// A network candidate whose assessment hangs (the writability probe blocks, as a dead mapped drive does) returns
    /// no suggestion once the drive-probe deadline passes, instead of waiting for the share.
    /// </summary>
    [Fact]
    public async Task NetworkCandidate_WhoseAssessmentHangs_GivesNoSuggestion()
    {
        using var gate = new ManualResetEventSlim(false);
        try
        {
            var assessor = new FolderAssessor(Array.Empty<string>(), Array.Empty<CloudSyncRoot>(), _ => { gate.Wait(); return true; });
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var suggestion = await assessor.AssessCandidateAsync(_dir, "Network drive", isNetwork: true);

            Assert.Null(suggestion);
            Assert.True(clock.ElapsedMilliseconds < FolderAssessor.DriveProbeTimeoutMs + 2000, $"took {clock.ElapsedMilliseconds} ms");
        }
        finally { gate.Set(); }
    }

    /// <summary>A healthy network candidate, and a local one, are still suggested as before.</summary>
    [Fact]
    public async Task HealthyCandidates_AreStillSuggested()
    {
        var assessor = new FolderAssessor(Array.Empty<string>(), Array.Empty<CloudSyncRoot>(), _ => true);

        Assert.NotNull(await assessor.AssessCandidateAsync(_dir, "Network drive", isNetwork: true));
        Assert.NotNull(await assessor.AssessCandidateAsync(_dir, "Secondary drive", isNetwork: false));
    }
}
