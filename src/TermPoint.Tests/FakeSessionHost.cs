using Microsoft.Data.Sqlite;
using TermPoint.Services;

namespace TermPoint.Tests;

/// <summary>
/// A recording stand-in for <see cref="IOpenedDatabase"/>: remembers what the lifecycle did to the "opened
/// database" (spec item 9.1, step 9.1b).
/// </summary>
internal sealed class FakeOpenedDatabase : IOpenedDatabase
{
    /// <summary>Where each call is also written (the test's shared, ordered trace).</summary>
    private readonly Action<string> _trace;

    /// <summary>The working file this "database" was opened on (D' or D'').</summary>
    public string WorkingPath { get; }

    /// <summary>Every <see cref="SetDatabaseName"/> call as (name, fullPath).</summary>
    public List<(string Name, string FullPath)> Names { get; } = new();

    /// <summary>Every <see cref="RestoreSemesterSelection"/> call as (yearId, semesterIds).</summary>
    public List<(string YearId, IReadOnlySet<string> SemesterIds)> Restored { get; } = new();

    /// <summary>Creates the fake.</summary>
    /// <param name="workingPath">The working file the database was opened on.</param>
    /// <param name="trace">Receives a line for every call.</param>
    public FakeOpenedDatabase(string workingPath, Action<string> trace)
    {
        WorkingPath = workingPath;
        _trace      = trace;
    }

    /// <inheritdoc/>
    public void SetDatabaseName(string name, string fullPath)
    {
        Names.Add((name, fullPath));
        _trace($"opened:SetDatabaseName({name}, {fullPath})");
    }

    /// <inheritdoc/>
    public void RestoreSemesterSelection(string yearId, IReadOnlySet<string> semesterIds)
    {
        Restored.Add((yearId, semesterIds));
        _trace($"opened:RestoreSemesterSelection({yearId}, {string.Join(",", semesterIds.OrderBy(s => s))})");
    }
}

/// <summary>
/// A recording stand-in for <see cref="ISessionHost"/> (spec item 9.1, step 9.1b): everything the lifecycle uses to
/// touch the window or the service container, with nothing visual behind it.
///
/// <para><b>OpenServices behaves as production does</b> for the parts the lifecycle relies on. The "container" is
/// built first (so a failed open leaves something for <see cref="ReleaseServices"/> to dispose), then the REAL
/// open-time integrity gate runs on the local copy exactly as <c>App.InitializeServices</c> runs it —
/// <see cref="LocalCopyIntegrity.VerifyAtOpen"/> over <see cref="LocalCopyIntegrity.CheckOnce"/>, with the
/// <see cref="IntegrityFaultPlan"/> the test supplies and no real sleeping between attempts — so
/// <see cref="DatabaseInUseException"/> and <see cref="DatabaseCorruptException"/> come from the real code, not
/// from the fake. Only then is a plain WAL-mode SQLite connection opened on the working file (production opens a
/// <c>DatabaseContext</c> there), which a test can write through with <see cref="Edit"/> and which is closed by
/// <see cref="ReleaseServices"/> / <see cref="DisposeServices"/>.</para>
///
/// <para><see cref="SetupWindowAsync"/> sets <see cref="IsDatabaseOpen"/> to true, as the real
/// <c>SetupMainWindowAsync</c> does as its last step.</para>
/// </summary>
internal sealed class FakeSessionHost : ISessionHost, IDisposable
{
    /// <summary>Where each call is also written (the test's shared, ordered trace).</summary>
    private readonly Action<string> _trace;

    /// <summary>The logger the integrity gate writes to.</summary>
    private readonly IAppLogger _logger;

    /// <summary>The service whose <see cref="CheckoutService.MarkDirty"/> an <see cref="Edit"/> calls.</summary>
    private readonly CheckoutService _checkout;

    /// <summary>The integrity fault plan the gate consults (the same plan the test hands the lifecycle), or null.</summary>
    private readonly Func<IntegrityFaultPlan?> _faults;

    /// <summary>True between <see cref="OpenServices"/> building the "container" and a release.</summary>
    private bool _servicesBuilt;

    /// <summary>The open connection to the working file, or null.</summary>
    private SqliteConnection? _connection;

    /// <summary>Whether a connection to the working file is currently open.</summary>
    public bool ConnectionOpen => _connection is not null;

    /// <summary>Exceptions to throw from the next <see cref="OpenServices"/> calls (one per call), after the integrity gate passed.</summary>
    public Queue<Exception> OpenFailures { get; } = new();

    /// <summary>Every <see cref="OpenServices"/> call as (workingPath, restoreYearId, restoreSemesterIds, the checkout mode at that moment).</summary>
    public List<(string WorkingPath, string? YearId, IReadOnlySet<string>? SemesterIds, CheckoutMode Mode)> OpenCalls { get; } = new();

    /// <summary>The "opened database" returned by each successful <see cref="OpenServices"/>, in order.</summary>
    public List<FakeOpenedDatabase> Opened { get; } = new();

    /// <summary>Every <see cref="SetupWindowAsync"/> call as (dbPath, the opened database it received).</summary>
    public List<(string DbPath, IOpenedDatabase Opened)> SetupCalls { get; } = new();

    /// <summary>What <see cref="TryCaptureSemesterSelection"/> reports; null means "nothing captured".</summary>
    public (string YearId, IReadOnlySet<string> SemesterIds)? CapturedSelection { get; set; }

    /// <inheritdoc/>
    public bool IsDatabaseOpen { get; set; }

    /// <inheritdoc/>
    public bool IsInlineEditorOpen => false;

    /// <summary>Creates the fake.</summary>
    /// <param name="trace">Receives a line "host:..." for every call.</param>
    /// <param name="logger">The logger for the integrity gate.</param>
    /// <param name="checkout">The checkout service the session runs on.</param>
    /// <param name="faults">Getter for the integrity fault plan.</param>
    public FakeSessionHost(
        Action<string> trace, IAppLogger logger, CheckoutService checkout, Func<IntegrityFaultPlan?> faults)
    {
        _trace    = trace;
        _logger   = logger;
        _checkout = checkout;
        _faults   = faults;
    }

    /// <inheritdoc/>
    public IOpenedDatabase OpenServices(
        string workingPath, string? restoreAcademicYearId = null, IReadOnlySet<string>? restoreSemesterIds = null)
    {
        _trace("host:OpenServices");
        OpenCalls.Add((workingPath, restoreAcademicYearId, restoreSemesterIds, _checkout.Mode));

        // Production disposes any previous container before building the new one.
        CloseConnection();
        _servicesBuilt = true;

        // The same gate, in the same place, as App.InitializeServices (see its remarks).
        var (existsCompleted, dbExists) = NetworkFileOps.ExistsAsync(workingPath).GetAwaiter().GetResult();
        if (existsCompleted && dbExists)
        {
            LocalCopyIntegrity.VerifyAtOpen(
                workingPath, path => LocalCopyIntegrity.CheckOnce(path, _faults(), _logger), _logger,
                sleep: _ => { });
        }

        // The DatabaseContext constructor, which can throw (for example DatabaseFolderNotWritableException).
        if (OpenFailures.Count > 0)
            throw OpenFailures.Dequeue();

        _connection = new SqliteConnection($"Data Source={workingPath};Mode=ReadWrite;Pooling=False");
        _connection.Open();
        Exec("PRAGMA journal_mode=WAL");

        var opened = new FakeOpenedDatabase(workingPath, _trace);
        Opened.Add(opened);
        return opened;
    }

    /// <inheritdoc/>
    public void ReleaseServices()
    {
        _trace($"host:ReleaseServices(built={_servicesBuilt}, connection={ConnectionOpen})");
        CloseConnection();
        _servicesBuilt = false;
    }

    /// <inheritdoc/>
    public void DisposeServices()
    {
        _trace($"host:DisposeServices(built={_servicesBuilt}, connection={ConnectionOpen})");
        CloseConnection();
    }

    /// <inheritdoc/>
    public void StopBackupSession() => _trace("host:StopBackupSession");

    /// <inheritdoc/>
    public void CloseSecondaryWindows() => _trace("host:CloseSecondaryWindows");

    /// <inheritdoc/>
    public Task SetupWindowAsync(string dbPath, IOpenedDatabase opened)
    {
        _trace("host:SetupWindow");
        SetupCalls.Add((dbPath, opened));
        IsDatabaseOpen = true;   // MainWindow.SetupMainWindowAsync sets this last
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void CoverWindowForNoDatabase() => _trace("host:CoverWindowForNoDatabase");

    /// <inheritdoc/>
    public void CloseWindow() => _trace("host:CloseWindow");

    /// <inheritdoc/>
    public void ShutDown() => _trace("host:ShutDown");

    /// <inheritdoc/>
    public Task ReportFatalAndShutDownAsync(Exception ex)
    {
        _trace($"host:ReportFatalAndShutDown({ex.GetType().Name})");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool TryCaptureSemesterSelection(out string? yearId, out IReadOnlySet<string>? semesterIds)
    {
        _trace($"host:TryCaptureSemesterSelection(captured={CapturedSelection is not null})");
        yearId      = CapturedSelection?.YearId;
        semesterIds = CapturedSelection?.SemesterIds;
        return CapturedSelection is not null;
    }

    /// <summary>
    /// Makes a user edit through the open connection and marks the session dirty — what a first write through
    /// the real <c>DatabaseContext</c> does (it calls <see cref="CheckoutService.MarkDirty"/>).
    /// </summary>
    /// <param name="value">The value inserted into the <c>_test</c> table.</param>
    /// <exception cref="InvalidOperationException">No connection is open.</exception>
    public void Edit(string value)
    {
        Exec($"INSERT INTO _test VALUES('{value}')");
        _checkout.MarkDirty();
    }

    /// <summary>Forgets the recorded calls (not the open connection), so a test can start a new step from a clean slate.</summary>
    public void ClearRecords()
    {
        OpenCalls.Clear();
        Opened.Clear();
        SetupCalls.Clear();
    }

    /// <summary>Closes the connection (and clears the SQLite pool so no handle outlives it).</summary>
    public void Dispose() => CloseConnection();

    /// <summary>Executes a statement on the open connection.</summary>
    /// <param name="sql">The statement.</param>
    private void Exec(string sql)
    {
        if (_connection is null) throw new InvalidOperationException("No connection is open.");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Closes and forgets the connection, if any.</summary>
    private void CloseConnection()
    {
        _connection?.Dispose();
        _connection = null;
        SqliteConnection.ClearAllPools();
    }
}
