using Microsoft.Data.Sqlite;
using TermPoint.Services;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="MinimumVersionToWrite"/> (spec item 3, step 3b): the pure
/// version comparison (<see cref="MinimumVersionToWrite.Blocks"/>), reading the stamp
/// from a database file without touching it (<see cref="MinimumVersionToWrite.ReadStamp"/>),
/// raising it monotonically (<see cref="MinimumVersionToWrite.Raise"/>), and the banner
/// wording (<see cref="MinimumVersionToWrite.BuildMessage"/>). The checkout-level behaviour
/// (the gate inside <c>CheckoutService</c>) is covered by <c>CheckoutServiceTests</c> Group 13.
/// </summary>
public sealed class MinimumVersionToWriteTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for the database files of one test.</summary>
    public MinimumVersionToWriteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mvtw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory and everything created in it.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a path for a database file inside the temp directory.</summary>
    private string DbPath(string name = "test") => Path.Combine(_tempDir, $"{name}.db");

    /// <summary>Opens a non-pooled connection to <paramref name="path"/> (creating an empty DB if absent).</summary>
    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Creates a SQLite database at <paramref name="path"/> that has an <c>AppConfiguration</c>
    /// table (same schema as <c>DatabaseContext</c>) holding the given key/value rows.
    /// </summary>
    private static void CreateConfigDb(string path, params (string Key, string Value)[] rows)
    {
        using var conn = Open(path);
        using (var create = conn.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS AppConfiguration (key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '')";
            create.ExecuteNonQuery();
        }

        foreach (var (key, value) in rows)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($k, $v)";
            insert.Parameters.AddWithValue("$k", key);
            insert.Parameters.AddWithValue("$v", value);
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>Reads one AppConfiguration value directly (no trimming), or null when the row/table is absent.</summary>
    private static string? RawValue(string path, string key)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>SHA-256 of a file's bytes, hex-encoded — used to prove a file was not modified.</summary>
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ═════════════════════════════════════════════════════════════════════════
    // Blocks — the pure comparison
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>No stamp (null, empty, or whitespace) never blocks — an unstamped database is a normal one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blocks_NoStamp_DoesNotBlock(string? stamp)
        => Assert.False(MinimumVersionToWrite.Blocks(stamp, new Version(1, 2, 3)));

    /// <summary>
    /// A three-part stamp equals the same four-part version in both directions: assembly
    /// versions always have four components, stamps are written as three.
    /// </summary>
    [Fact]
    public void Blocks_EqualVersion_DoesNotBlock_RegardlessOfComponentCount()
    {
        Assert.False(MinimumVersionToWrite.Blocks("1.2.3",   new Version(1, 2, 3, 0)));
        Assert.False(MinimumVersionToWrite.Blocks("1.2.3.0", new Version(1, 2, 3)));
        Assert.False(MinimumVersionToWrite.Blocks("1.2.3",   new Version(1, 2, 3)));
        Assert.False(MinimumVersionToWrite.Blocks("1.2",     new Version(1, 2, 0, 0)));
    }

    /// <summary>A stamp lower than the running app does not block.</summary>
    [Fact]
    public void Blocks_StampLowerThanApp_DoesNotBlock()
        => Assert.False(MinimumVersionToWrite.Blocks("1.2.2", new Version(1, 2, 3, 0)));

    /// <summary>A stamp higher than the running app blocks, at every component.</summary>
    [Theory]
    [InlineData("1.2.4")]
    [InlineData("2.0")]
    [InlineData("1.3.0")]
    [InlineData("1.2.3.1")]
    public void Blocks_StampHigherThanApp_Blocks(string stamp)
        => Assert.True(MinimumVersionToWrite.Blocks(stamp, new Version(1, 2, 3, 0)));

    /// <summary>Versions compare numerically, not lexically (1.10.0 is newer than 1.9.0).</summary>
    [Fact]
    public void Blocks_ComparesNumerically()
    {
        Assert.True(MinimumVersionToWrite.Blocks("1.10.0", new Version(1, 9, 0, 0)));
        Assert.False(MinimumVersionToWrite.Blocks("1.9.0", new Version(1, 10, 0, 0)));
    }

    /// <summary>
    /// A stamp this app cannot parse blocks (fail safe): an app that cannot understand the
    /// requirement must not write. Pre-release suffixes are unparseable by <see cref="Version"/>.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.5-beta")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    public void Blocks_UnparseableStamp_Blocks(string stamp)
        => Assert.True(MinimumVersionToWrite.Blocks(stamp, new Version(99, 0, 0, 0)));

    /// <summary>Surrounding whitespace on a stamp is ignored.</summary>
    [Fact]
    public void Blocks_PaddedStamp_IsTrimmed()
    {
        Assert.True(MinimumVersionToWrite.Blocks(" 1.2.5 ", new Version(1, 2, 3, 0)));
        Assert.False(MinimumVersionToWrite.Blocks(" 1.2.3 ", new Version(1, 2, 3, 0)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ReadStamp — read-only, tolerant of everything
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A database without an AppConfiguration table has no stamp.</summary>
    [Fact]
    public void ReadStamp_TableMissing_ReturnsNull()
    {
        var db = DbPath();
        using (Open(db)) { }   // valid empty SQLite file

        Assert.Null(MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>An AppConfiguration table without the stamp row has no stamp (other rows are ignored).</summary>
    [Fact]
    public void ReadStamp_KeyMissing_ReturnsNull()
    {
        var db = DbPath();
        CreateConfigDb(db, ("SomethingElse", "1.9.9"));

        Assert.Null(MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>A stamped database returns its (trimmed) stamp.</summary>
    [Fact]
    public void ReadStamp_ValuePresent_ReturnsTrimmedValue()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, " 1.2.5 "));

        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>A blank stored value counts as no stamp.</summary>
    [Fact]
    public void ReadStamp_BlankValue_ReturnsNull()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "  "));

        Assert.Null(MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>A file that is not SQLite (the dummy files other tests use as D) reads as unstamped, never throws.</summary>
    [Fact]
    public void ReadStamp_NotASqliteFile_ReturnsNull()
    {
        var db = DbPath();
        File.WriteAllText(db, "dummy-database-content");

        Assert.Null(MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>A missing file reads as unstamped and is NOT created by the probe.</summary>
    [Fact]
    public void ReadStamp_FileMissing_ReturnsNullAndCreatesNothing()
    {
        var db = DbPath("absent");

        Assert.Null(MinimumVersionToWrite.ReadStamp(db));
        Assert.False(File.Exists(db), "Reading a stamp must never create the database file.");
    }

    /// <summary>Reading the stamp must not change a single byte of the file.</summary>
    [Fact]
    public void ReadStamp_DoesNotModifyTheFile()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.2.5"));
        var before = Hash(db);

        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));

        Assert.Equal(before, Hash(db));
        Assert.False(File.Exists(db + "-journal"), "No journal may be left behind by a read.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Raise — monotonic, transactional
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>On a database that has no AppConfiguration table, Raise creates it and writes the stamp.</summary>
    [Fact]
    public void Raise_EmptyDatabaseWithoutTable_CreatesTableAndStamps()
    {
        var db = DbPath();
        using (Open(db)) { }

        var raised = MinimumVersionToWrite.Raise(db, "1.2.5", "Section-type colours are now saved.");

        Assert.True(raised);
        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
        Assert.Equal("Section-type colours are now saved.", RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>A higher version replaces a lower stamp, and the reason travels with it.</summary>
    [Fact]
    public void Raise_LowerToHigher_WritesNewVersionAndReason()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.2.3"), (MinimumVersionToWrite.ReasonKey, "old reason"));

        var raised = MinimumVersionToWrite.Raise(db, "1.2.5", "new reason");

        Assert.True(raised);
        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
        Assert.Equal("new reason", RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>The stamp never goes down: raising to a lower version changes neither the stamp nor the reason.</summary>
    [Fact]
    public void Raise_NeverLowers()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.2.5"), (MinimumVersionToWrite.ReasonKey, "keep me"));

        var raised = MinimumVersionToWrite.Raise(db, "1.2.3", "would-be reason");

        Assert.False(raised);
        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
        Assert.Equal("keep me", RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>Raising to the version already stamped is a no-op (and does not rewrite the reason).</summary>
    [Fact]
    public void Raise_EqualVersion_IsNoOp()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.2.5"), (MinimumVersionToWrite.ReasonKey, "original"));

        var raised = MinimumVersionToWrite.Raise(db, "1.2.5.0", "different wording");

        Assert.False(raised);
        Assert.Equal("1.2.5", RawValue(db, MinimumVersionToWrite.ConfigKey));
        Assert.Equal("original", RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>A null reason writes the stamp but adds no reason row.</summary>
    [Fact]
    public void Raise_NullReason_WritesNoReasonRow()
    {
        var db = DbPath();
        CreateConfigDb(db);   // table, no rows

        Assert.True(MinimumVersionToWrite.Raise(db, "1.2.5", reason: null));

        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
        Assert.Null(RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>
    /// Raising without a reason removes the previous raise's reason, so the stored reason never
    /// describes a stamp it doesn't belong to.
    /// </summary>
    [Fact]
    public void Raise_NullReason_RemovesStaleReason()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.2.3"), (MinimumVersionToWrite.ReasonKey, "old reason"));

        Assert.True(MinimumVersionToWrite.Raise(db, "1.2.5", reason: null));

        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
        Assert.Null(RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>
    /// An existing stamp that does not parse is left exactly as it is: it already blocks every
    /// app, which is the safe state, and replacing it could silently loosen the gate.
    /// </summary>
    [Fact]
    public void Raise_UnparseableExistingStamp_IsLeftAlone()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "garbage"));

        var raised = MinimumVersionToWrite.Raise(db, "9.9.9", "reason");

        Assert.False(raised);
        Assert.Equal("garbage", RawValue(db, MinimumVersionToWrite.ConfigKey));
        Assert.Null(RawValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>A blank existing value counts as no stamp and is overwritten.</summary>
    [Fact]
    public void Raise_BlankExistingValue_IsOverwritten()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, ""));

        Assert.True(MinimumVersionToWrite.Raise(db, "1.2.5", null));
        Assert.Equal("1.2.5", MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>Raise compares numerically: 1.10.0 replaces 1.9.0.</summary>
    [Fact]
    public void Raise_ComparesNumerically()
    {
        var db = DbPath();
        CreateConfigDb(db, (MinimumVersionToWrite.ConfigKey, "1.9.0"));

        Assert.True(MinimumVersionToWrite.Raise(db, "1.10.0", null));
        Assert.Equal("1.10.0", MinimumVersionToWrite.ReadStamp(db));
    }

    /// <summary>A malformed version argument is rejected up front and leaves the file untouched.</summary>
    [Fact]
    public void Raise_InvalidVersionArgument_ThrowsAndWritesNothing()
    {
        var db = DbPath();
        CreateConfigDb(db);
        var before = Hash(db);

        Assert.Throws<ArgumentException>(() => MinimumVersionToWrite.Raise(db, "1.2.5-beta", null));

        Assert.Equal(before, Hash(db));
    }

    /// <summary>Raise must fail (not conjure an empty database) when the target file does not exist.</summary>
    [Fact]
    public void Raise_MissingFile_ThrowsAndCreatesNothing()
    {
        var db = DbPath("absent");

        Assert.ThrowsAny<Exception>(() => MinimumVersionToWrite.Raise(db, "1.2.5", null));
        Assert.False(File.Exists(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BuildMessage — banner wording by install channel
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Store installs are told to click "Get updates" in the Microsoft Store — the page the
    /// banner's button opens (<see cref="MinimumVersionToWrite.StoreUri"/>).
    /// </summary>
    [Fact]
    public void BuildMessage_Store_PointsAtTheStore()
    {
        var message = MinimumVersionToWrite.BuildMessage(InstallChannel.Store, "1.2.5");

        Assert.Equal(
            "Read-only — this database needs TermPoint 1.2.5 or later to edit. " +
            "In the Microsoft Store, click Get updates, then restart TermPoint.",
            message);
    }

    /// <summary>Every non-Store channel is told to install from its usual source.</summary>
    [Theory]
    [InlineData(InstallChannel.Sideload)]
    [InlineData(InstallChannel.DirectDownload)]
    [InlineData(InstallChannel.Development)]
    public void BuildMessage_NonStore_PointsAtUsualSource(InstallChannel channel)
    {
        var message = MinimumVersionToWrite.BuildMessage(channel, "1.2.5");

        Assert.Equal(
            "Read-only — this database needs TermPoint 1.2.5 or later to edit. " +
            "Install the update from your usual source, then restart TermPoint.",
            message);
    }

    /// <summary>A stamp that is not a version is never echoed to the user.</summary>
    [Theory]
    [InlineData(InstallChannel.Store)]
    [InlineData(InstallChannel.DirectDownload)]
    public void BuildMessage_UnparseableStamp_UsesGenericPhrase(InstallChannel channel)
    {
        var message = MinimumVersionToWrite.BuildMessage(channel, "1.2.5-beta");

        Assert.Contains("needs a newer version of TermPoint to edit", message);
        Assert.DoesNotContain("1.2.5-beta", message);
    }

    /// <summary>Stray whitespace around a valid stamp does not leak into the sentence.</summary>
    [Fact]
    public void BuildMessage_PaddedStamp_IsTrimmed()
        => Assert.Contains("TermPoint 1.2.5 or later", MinimumVersionToWrite.BuildMessage(InstallChannel.Store, " 1.2.5 "));

    // ═════════════════════════════════════════════════════════════════════════
    // Constants and environment
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 1.2.3 ships the CHECK only: the code that raises the stamp is dormant. This pins both
    /// release constants to null. If you set them deliberately (this release answers Yes to
    /// "Could the new version save anything the old version wouldn't have?"), update this test —
    /// see the doc comment on <see cref="MinimumVersionToWrite.ThisReleaseRequires"/>.
    /// </summary>
    [Fact]
    public void ThisRelease_Constants_AreNullInThisRelease()
    {
        Assert.True(MinimumVersionToWrite.ThisReleaseRequires is null,
            "MinimumVersionToWrite.ThisReleaseRequires is no longer null. If you set this deliberately, " +
            "update this test — see the doc comment on ThisReleaseRequires.");
        Assert.True(MinimumVersionToWrite.ThisReleaseReason is null,
            "MinimumVersionToWrite.ThisReleaseReason is no longer null. If you set this deliberately, " +
            "update this test — see the doc comment on ThisReleaseRequires.");
    }

    /// <summary>
    /// The banner's button opens the Store's Downloads and updates page (it has "Get updates";
    /// TermPoint's product page does not say what to do), and the Store identity used for channel
    /// detection is the Store listing's package family name.
    /// </summary>
    [Fact]
    public void StoreUri_OpensDownloadsAndUpdates()
    {
        Assert.Equal("Academic-Solutions.TermPoint_9nmm6ygzk390e", PlatformCapabilities.StorePackageFamilyName);
        Assert.Equal("ms-windows-store://downloadsandupdates", MinimumVersionToWrite.StoreUri);
    }

    /// <summary>
    /// In the test host (not MSIX-packaged, not Velopack-installed) the detected channel is
    /// <see cref="InstallChannel.Development"/>. If a developer has set the DEBUG-only
    /// <c>TERMPOINT_INSTALL_CHANNEL</c> override, the detected value must instead equal it.
    /// </summary>
    [Fact]
    public void InstallChannel_InTestHost_IsDevelopmentUnlessOverridden()
    {
        var forced = Environment.GetEnvironmentVariable("TERMPOINT_INSTALL_CHANNEL");

        if (!string.IsNullOrWhiteSpace(forced))
        {
            // Override present in this shell: only verify it is honoured (DEBUG) or ignored (Release).
#if DEBUG
            if (Enum.TryParse<InstallChannel>(forced.Trim(), ignoreCase: true, out var expected)
                && Enum.IsDefined(expected))
            {
                Assert.Equal(expected, PlatformCapabilities.InstallChannel);
            }
#endif
            return;
        }

        Assert.Equal(InstallChannel.Development, PlatformCapabilities.InstallChannel);
    }
}
