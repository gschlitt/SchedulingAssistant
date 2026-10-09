using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Supply-chain regression guard for spec 1.2.3 item 2: asserts that the native SQLite
/// engine bundled with TermPoint (via <c>Microsoft.Data.Sqlite</c> →
/// <c>SQLitePCLRaw.bundle_e_sqlite3</c> → <c>SQLitePCLRaw.lib.e_sqlite3</c>) is new
/// enough to be outside the range affected by CVE-2025-6965 (GHSA-2m69-gcr7-jv3q).
///
/// <para>That vulnerability lets SQLite versions before 3.50.2 corrupt memory when the
/// number of aggregate terms in a query exceeds the number of available columns. The
/// engine ships inside the application (self-contained distribution), so end users get
/// whichever version NuGet resolves at build time — they cannot patch it themselves.
/// <c>SQLitePCLRaw.lib.e_sqlite3</c> 2.1.11 and earlier are affected; 2.1.12 and later
/// carry a fixed engine.</para>
///
/// <para>The risk this test guards against is a silent downgrade: the engine version is
/// not referenced directly anywhere in the project files, it arrives transitively. A
/// future edit that lowers the <c>Microsoft.Data.Sqlite</c> version, pins an older
/// <c>SQLitePCLRaw.*</c> package, or swaps in a different bundle (for example
/// <c>SQLitePCLRaw.bundle_e_sqlite3</c> replaced by <c>bundle_sqlite3</c> or
/// <c>bundle_winsqlite3</c>) would change the engine without any compile error. Asking
/// the running engine for its own version makes such a change fail the suite.</para>
/// </summary>
public sealed class SqliteEngineVersionTests
{
    /// <summary>
    /// The first SQLite release that contains the fix for CVE-2025-6965. Any engine
    /// reporting a version below this value is considered vulnerable.
    /// </summary>
    private static readonly Version MinimumSafeSqliteVersion = new(3, 50, 2);

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// xUnit constructor injection of the per-test output sink, used to record the
    /// engine version in the test log.
    /// </summary>
    /// <param name="output">xUnit's output helper for the currently running test.</param>
    public SqliteEngineVersionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Runs a single scalar query against a throw-away in-memory database and returns
    /// the text of the result. In-memory databases never touch disk or the network, so
    /// this is safe to run on any machine.
    /// </summary>
    /// <param name="sql">A SQL statement that returns exactly one scalar value.</param>
    /// <returns>The scalar result converted to a string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the engine returns NULL, which should never happen for the
    /// <c>sqlite_version()</c> / <c>sqlite_source_id()</c> functions queried here.
    /// </exception>
    private static string QueryScalar(string sql)
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        return Convert.ToString(cmd.ExecuteScalar())
               ?? throw new InvalidOperationException($"'{sql}' returned NULL.");
    }

    /// <summary>
    /// The bundled SQLite engine must be at least 3.50.2, the first release that fixes
    /// CVE-2025-6965. Fails if a transitive dependency change has reintroduced a
    /// vulnerable engine; the fix is to restore a <c>Microsoft.Data.Sqlite</c> version
    /// (10.0.12 or later) whose <c>SQLitePCLRaw.bundle_e_sqlite3</c> is 2.1.12 or later.
    /// </summary>
    [Fact]
    public void BundledSqliteEngine_IsAtLeastFirstVersionFixingCve20256965()
    {
        string reported = QueryScalar("select sqlite_version()");

        Assert.True(
            Version.TryParse(reported, out var actual),
            $"sqlite_version() returned '{reported}', which is not a parseable version number.");

        Assert.True(
            actual >= MinimumSafeSqliteVersion,
            $"Bundled SQLite engine is {actual}, which is older than {MinimumSafeSqliteVersion} " +
            "and is affected by CVE-2025-6965 (GHSA-2m69-gcr7-jv3q). Check that " +
            "Microsoft.Data.Sqlite is 10.0.12 or later and that no SQLitePCLRaw.* package " +
            "is pinned to 2.1.11 or earlier (spec 1.2.3 item 2).");
    }

    /// <summary>
    /// Writes the engine's version and source id to the test output so the exact build
    /// that was exercised is recorded in the run log (visible with
    /// <c>--logger "console;verbosity=detailed"</c>). Asserts only that the values are
    /// non-empty; the threshold check lives in
    /// <see cref="BundledSqliteEngine_IsAtLeastFirstVersionFixingCve20256965"/>.
    /// </summary>
    [Fact]
    public void BundledSqliteEngine_ReportsItsVersionToTestOutput()
    {
        string version  = QueryScalar("select sqlite_version()");
        string sourceId = QueryScalar("select sqlite_source_id()");

        _output.WriteLine($"SQLite engine version:   {version}");
        _output.WriteLine($"SQLite engine source id: {sourceId}");
        _output.WriteLine($"Minimum safe version:    {MinimumSafeSqliteVersion} (CVE-2025-6965)");

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.False(string.IsNullOrWhiteSpace(sourceId));
    }
}
