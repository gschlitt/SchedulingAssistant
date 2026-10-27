using TermPoint.Exceptions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="DatabaseDamageReporter"/> (spec item 5, step 5e): one report per kind per run, a named
/// exception with a stable per-kind message, nothing private in anything sent, and never an exception back to the
/// caller. Every test makes its own <see cref="RecordingAppLogger"/>, which also has its own "already reported"
/// memory, so the tests cannot affect each other.
/// </summary>
public sealed class DatabaseDamageReporterTests
{
    // ── Once per run per kind ─────────────────────────────────────────────────

    /// <summary>The first report of a kind is sent: one handled report with the documented shape.</summary>
    [Fact]
    public void Report_FirstOfAKind_IsSent_WithNamedExceptionContextMetadataAndGroupingKey()
    {
        var log = new RecordingAppLogger();

        var sent = DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, "*** in database main ***\nPage 2: btree problem", log);

        Assert.True(sent);
        var report = Assert.Single(log.Handled);
        Assert.IsType<DatabaseDamagedException>(report.Exception);
        Assert.Equal("Database damage: SharedDatabase", report.Context);
        Assert.Equal("damage", report.MetadataTab);
        Assert.Equal("database-damage:SharedDatabase", report.GroupingKey);
        Assert.Equal("SharedDatabase", report.Metadata!["kind"]);
        Assert.Equal("*** in database main ***\nPage 2: btree problem", report.Metadata["summary"]);
        Assert.Empty(log.Errors);         // never LogError: that re-throws in development and shows a banner
        Assert.Empty(log.UserNotices);
    }

    /// <summary>A second report of the same kind in the same run is NOT sent again; it leaves only a local log line.</summary>
    [Fact]
    public void Report_SecondOfTheSameKind_IsSuppressed_ButLogged()
    {
        var log = new RecordingAppLogger();

        Assert.True(DatabaseDamageReporter.Report(DamagedFileKind.LocalCopy, "first", log));
        Assert.False(DatabaseDamageReporter.Report(DamagedFileKind.LocalCopy, "second", log));
        Assert.False(DatabaseDamageReporter.Report(DamagedFileKind.LocalCopy, "third", log));

        Assert.Single(log.Handled);
        Assert.Contains(log.Infos, m => m.Contains("LocalCopy") && m.Contains("already reported"));
    }

    /// <summary>Each kind is reported once: the four kinds all get through, each a separate report.</summary>
    [Fact]
    public void Report_DifferentKinds_AreAllSent()
    {
        var log = new RecordingAppLogger();

        foreach (var kind in Enum.GetValues<DamagedFileKind>())
            Assert.True(DatabaseDamageReporter.Report(kind, "summary", log), $"{kind} should be sent");

        Assert.Equal(4, log.Handled.Count);
        Assert.Equal(
            Enum.GetValues<DamagedFileKind>().Select(k => $"database-damage:{k}").OrderBy(s => s),
            log.Handled.Select(h => h.GroupingKey!).OrderBy(s => s));
    }

    /// <summary>The memory of what was reported belongs to the logger: another logger (another run) is told again.</summary>
    [Fact]
    public void Report_TheMemoryBelongsToTheLogger()
    {
        var one = new RecordingAppLogger();
        var two = new RecordingAppLogger();

        Assert.True(DatabaseDamageReporter.Report(DamagedFileKind.BackupSnapshot, "x", one));
        Assert.True(DatabaseDamageReporter.Report(DamagedFileKind.BackupSnapshot, "x", two));
    }

    /// <summary><see cref="DatabaseDamageReporter.ResetForTests"/> forgets what was reported.</summary>
    [Fact]
    public void ResetForTests_AllowsTheKindToBeReportedAgain()
    {
        var log = new RecordingAppLogger();
        DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, "x", log);

        DatabaseDamageReporter.ResetForTests();

        Assert.True(DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, "x", log));
        Assert.Equal(2, log.Handled.Count);
    }

    /// <summary>Many threads reporting the same kind at once: exactly one report is sent.</summary>
    [Fact]
    public void Report_FromManyThreads_SendsExactlyOnePerKind()
    {
        var log = new RecordingAppLogger();
        var results = new bool[64];

        Parallel.For(0, results.Length, i => results[i] = DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, "x", log));

        Assert.Equal(1, results.Count(r => r));
    }

    // ── The named exception ───────────────────────────────────────────────────

    /// <summary>
    /// The exception is a <see cref="DatabaseDamagedException"/> with a message that depends on the kind and on
    /// nothing else — so BugSnag groups one issue per kind, and the summary is not in the message.
    /// </summary>
    [Theory]
    [InlineData(DamagedFileKind.SharedDatabase,      "Damaged shared database detected by the integrity check")]
    [InlineData(DamagedFileKind.LocalCopy,           "Damaged local copy of the database detected by the integrity check")]
    [InlineData(DamagedFileKind.BackupSnapshot,      "Damaged backup snapshot detected by the integrity check")]
    [InlineData(DamagedFileKind.BackupBeingRestored, "Damaged backup being restored detected by the integrity check")]
    public void TheException_HasAStableMessagePerKind(DamagedFileKind kind, string expectedMessage)
    {
        var a = new RecordingAppLogger();
        var b = new RecordingAppLogger();

        DatabaseDamageReporter.Report(kind, "Page 2: first summary", a);
        DatabaseDamageReporter.Report(kind, "Page 9: a different summary", b);

        var exA = Assert.IsType<DatabaseDamagedException>(Assert.Single(a.Handled).Exception);
        var exB = Assert.IsType<DatabaseDamagedException>(Assert.Single(b.Handled).Exception);
        Assert.Equal(expectedMessage, exA.Message);
        Assert.Equal(expectedMessage, exB.Message);
        Assert.Equal(kind, exA.Kind);
        Assert.Equal("Page 2: first summary", exA.Summary);
        Assert.NotNull(exA.StackTrace);   // thrown and caught on the spot, so the report has a real stack
    }

    // ── Privacy: nothing private is sent ──────────────────────────────────────

    /// <summary>
    /// A summary that (against all expectation) contains a Windows path, a UNC path or a Unix path is sent with every
    /// path replaced — in the metadata AND in the exception.
    /// </summary>
    [Theory]
    [InlineData(@"error in C:\Users\alice\Documents\dept.db page 4", "alice")]
    [InlineData(@"error in C:/Users/alice/Documents/dept.db page 4", "alice")]
    [InlineData(@"error in \\fileserver\share\finance\dept.db page 4", "fileserver")]
    [InlineData("error in /Users/alice/Documents/dept.db page 4", "alice")]
    [InlineData("error in /home/alice/dept.db page 4", "alice")]
    [InlineData("error in ~/alice/dept.db page 4", "alice")]
    [InlineData("error in 'D:\\Backups\\Fall Term\\dept.db' page 4", "Fall Term")]
    [InlineData(@"error in C:\Users\John Smith\Documents\dept.db", "Smith")]
    [InlineData("error opening dept_2026-10-14_13-00-00.db: malformed", "dept_2026")]
    [InlineData("error opening dept.db-wal: malformed", "dept")]
    public void Report_NeverSendsAPathOrAFileName(string summary, string mustNotSurvive)
    {
        var log = new RecordingAppLogger();

        DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, summary, log);

        var report = Assert.Single(log.Handled);
        var sentSummary = report.Metadata!["summary"];
        var exception = Assert.IsType<DatabaseDamagedException>(report.Exception);
        foreach (var text in new[] { sentSummary, exception.Summary!, exception.Message, report.Context })
        {
            Assert.DoesNotContain(mustNotSurvive, text);
            Assert.DoesNotContain(@":\", text);
            Assert.DoesNotContain(@"\\", text);
            Assert.DoesNotContain(".db", text);
        }
    }

    /// <summary>
    /// The three example paths from the specification, in one summary: none of them survives, and each becomes its
    /// shape tag (spec item 6: the summary goes through the shared <see cref="PrivacyScrubber"/>).
    /// </summary>
    [Fact]
    public void Sanitize_RemovesTheThreeKindsOfPath()
    {
        var clean = DatabaseDamageReporter.Sanitize(
            "a C:\\Users\\x\\a.db\nb \\\\server\\share\\b.db\nc /Users/x/c.db");

        Assert.DoesNotContain("Users", clean);
        Assert.DoesNotContain("server", clean);
        Assert.DoesNotContain("share", clean);
        Assert.DoesNotContain("a.db", clean);
        Assert.DoesNotContain("b.db", clean);
        Assert.DoesNotContain("c.db", clean);
        Assert.Equal("a <drive C: path>\nb <unc path>\nc <unix path>", clean);   // the line structure is kept
    }

    /// <summary>A summary the scrubber cannot check in time is withheld, never sent unchecked.</summary>
    [Fact]
    public void Sanitize_WhenTheScrubberTimesOut_WithholdsTheSummary()
    {
        PrivacyScrubber.BeforeScrubForTests = _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException();
        try
        {
            var clean = DatabaseDamageReporter.Sanitize(@"see C:\Users\alice\dept.db");

            Assert.Equal("(summary withheld: it could not be checked for private details)", clean);
        }
        finally
        {
            PrivacyScrubber.BeforeScrubForTests = null;
        }
    }

    /// <summary>
    /// What SQLite's own text looks like — pages, tables, quoted messages, a slash in prose — passes through
    /// unchanged: the sanitizer is a defence, not a rewrite.
    /// </summary>
    [Theory]
    [InlineData("*** in database main ***\nPage 2: btree problem")]
    [InlineData("On tree page 3 cell 1: Rowid 5 out of order")]
    [InlineData("wrong # of entries in index idx_sections_course")]
    [InlineData("SQLite error 11: database disk image is malformed.")]
    [InlineData("SQLite error 26: 'file is not a database'.")]
    [InlineData("offset 3/4 and / or 1/2 are fine")]
    public void Sanitize_LeavesSqliteTextAlone(string summary)
    {
        Assert.Equal(summary, DatabaseDamageReporter.Sanitize(summary));
    }

    /// <summary>A missing summary is sent as a fixed placeholder, never an empty value.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sanitize_NoSummary_IsAPlaceholder(string? summary)
    {
        Assert.Equal("(no summary)", DatabaseDamageReporter.Sanitize(summary));
    }

    /// <summary>A long summary is cut to the cap, marked with "...", and the cut keeps the sanitizing (no half path).</summary>
    [Fact]
    public void Sanitize_CapsTheLength()
    {
        var clean = DatabaseDamageReporter.Sanitize(new string('x', 5000));

        Assert.Equal(DatabaseDamageReporter.MaxSummaryLength, clean.Length);
        Assert.EndsWith("...", clean);
    }

    /// <summary>A summary exactly at the cap is not cut.</summary>
    [Fact]
    public void Sanitize_AtTheCap_IsNotCut()
    {
        var text = new string('y', DatabaseDamageReporter.MaxSummaryLength);

        Assert.Equal(text, DatabaseDamageReporter.Sanitize(text));
    }

    /// <summary>A path that straddles the cut is redacted BEFORE the cut, so no half of it is sent.</summary>
    [Fact]
    public void Sanitize_RedactsBeforeCutting()
    {
        var clean = DatabaseDamageReporter.Sanitize(new string('z', 995) + @" C:\Users\alice\secret\dept.db");

        Assert.DoesNotContain("alice", clean);
        Assert.DoesNotContain("secret", clean);
        Assert.InRange(clean.Length, 1, DatabaseDamageReporter.MaxSummaryLength);
    }

    // ── Never throws ──────────────────────────────────────────────────────────

    /// <summary>A logger whose <c>ReportHandled</c> throws cannot make the reporter throw; the report is simply not sent.</summary>
    [Fact]
    public void Report_WhenTheLoggerThrows_DoesNotThrow()
    {
        var log = new RecordingAppLogger { ThrowFromReportHandled = true };

        var sent = DatabaseDamageReporter.Report(DamagedFileKind.SharedDatabase, "x", log);

        Assert.False(sent);
    }

    /// <summary>The same holds for <see cref="DatabaseDamageReporter.ReportIfDamaged"/>.</summary>
    [Fact]
    public void ReportIfDamaged_WhenTheLoggerThrows_DoesNotThrow()
    {
        var log = new RecordingAppLogger { ThrowFromReportHandled = true };

        var sent = DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.LocalCopy, DamageEvidence.Corrupt, "x", log);

        Assert.False(sent);
    }

    // ── ReportIfDamaged: what is worth a report ───────────────────────────────

    /// <summary>Integrity problems and SQLITE_CORRUPT are reported.</summary>
    [Theory]
    [InlineData(DamageEvidence.IntegrityProblems)]
    [InlineData(DamageEvidence.Corrupt)]
    public void ReportIfDamaged_PositiveEvidence_IsReported(DamageEvidence evidence)
    {
        var log = new RecordingAppLogger();

        Assert.True(DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.SharedDatabase, evidence, "x", log));

        Assert.Single(log.Handled);
    }

    /// <summary>
    /// "Not a database", and no evidence (locked, unreadable, unreachable, healthy), are never reported. The "not a
    /// database" case leaves a local log line saying why.
    /// </summary>
    [Theory]
    [InlineData(DamageEvidence.None)]
    [InlineData(DamageEvidence.NotADatabase)]
    public void ReportIfDamaged_OtherEvidence_IsNotReported(DamageEvidence evidence)
    {
        var log = new RecordingAppLogger();

        Assert.False(DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.SharedDatabase, evidence, "x", log));

        Assert.Empty(log.Handled);
        if (evidence == DamageEvidence.NotADatabase)
            Assert.Contains(log.Infos, m => m.Contains("not recognisable as a SQLite database"));
    }

    /// <summary>Damage injected by a DEBUG fault plan is never reported — and the log says it was suppressed.</summary>
    [Fact]
    public void ReportIfDamaged_Injected_IsNotReported_AndSaysSo()
    {
        var log = new RecordingAppLogger();

        Assert.False(DatabaseDamageReporter.ReportIfDamaged(
            DamagedFileKind.LocalCopy, DamageEvidence.IntegrityProblems, "DEBUG fault injection: damaged", log, injected: true));

        Assert.Empty(log.Handled);
        Assert.Contains(log.Infos, m => m.Contains("injected") && m.Contains("no report is sent"));
    }

    /// <summary>
    /// An injected report does not use up the kind's one report: a REAL damage report of that kind that follows is
    /// still sent.
    /// </summary>
    [Fact]
    public void ReportIfDamaged_Injected_DoesNotSpendTheOneReport()
    {
        var log = new RecordingAppLogger();
        DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.LocalCopy, DamageEvidence.IntegrityProblems, "fake", log, injected: true);

        Assert.True(DatabaseDamageReporter.ReportIfDamaged(DamagedFileKind.LocalCopy, DamageEvidence.IntegrityProblems, "real", log));
    }

    /// <summary>With no logger given, the reporter uses its default logger — which the test assembly points at a silent one.</summary>
    [Fact]
    public void Report_WithNoLogger_UsesTheDefaultLogger_NotTheApplicationLogger()
    {
        var before = TestAssemblyGuards.SilentDamageLogger.Handled.Count;
        DatabaseDamageReporter.ResetForTests();

        DatabaseDamageReporter.Report(DamagedFileKind.BackupSnapshot, "x");

        Assert.Equal(before + 1, TestAssemblyGuards.SilentDamageLogger.Handled.Count);
    }
}
