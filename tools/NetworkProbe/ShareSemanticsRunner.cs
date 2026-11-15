using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// The runner role's cases (<c>--share-semantics --role runner</c>): experiments E1 and E2 of
/// <c>designs/network-experiments-5.8.md</c>. Every file call made on this computer is timed with a stopwatch here;
/// the partner's file calls are asked for over the control channel and timed on the runner's clock (round trip) and on
/// the partner's own (the call itself). No clock is ever compared across computers.
///
/// <para>The runner works only in its own folder under the share, <see cref="_probeDir"/>, and the partner only inside its
/// <c>--root</c>, so the experiment never touches anything else on the share.</para>
/// </summary>
internal sealed class ShareSemanticsRunner
{
    /// <summary>How long "not found" is watched for after the partner's create (design: poll every 50 ms up to 15 s).</summary>
    internal static readonly TimeSpan StaleCap = TimeSpan.FromSeconds(15);

    /// <summary>Pause between polls of "is the file found yet".</summary>
    private const int PollMs = 50;

    /// <summary>E2 case 1: how long the rename is retried while the partner still holds the file.</summary>
    private static readonly TimeSpan HeldRetryCap = TimeSpan.FromSeconds(10);

    /// <summary>E2 cases 1 and 5: how long to keep retrying after the partner has closed its handle.</summary>
    private static readonly TimeSpan AfterCloseCap = TimeSpan.FromSeconds(5);

    /// <summary>E2 case 5: how long CreateNew is retried while the partner still holds the deleted file.</summary>
    private static readonly TimeSpan BlockedCap = TimeSpan.FromSeconds(5);

    /// <summary>Pause between retries in E2 (design: every 100 ms).</summary>
    private const int RetryMs = 100;

    /// <summary>Size of the small lock-style files in E2 cases 4 and 5.</summary>
    private const int LockBytes = 2048;

    /// <summary>One E2 test fixture: a folder holding the old file, the new file and their hashes.</summary>
    /// <param name="DirPath">The run's folder, as the runner sees it.</param>
    /// <param name="DirRel">The same folder relative to the share (what the partner is told).</param>
    /// <param name="DestName">File name of the destination (the "database" or the lock file).</param>
    /// <param name="Old">Content of the destination before the rename.</param>
    /// <param name="New">Content of the temp file that is renamed over it.</param>
    /// <param name="OldHash">SHA-256 of <paramref name="Old"/> (hex).</param>
    /// <param name="NewHash">SHA-256 of <paramref name="New"/> (hex).</param>
    private sealed record Fixture(string DirPath, string DirRel, string DestName, byte[] Old, byte[] New, string OldHash, string NewHash)
    {
        /// <summary>The destination as the runner sees it.</summary>
        public string Dest => Path.Combine(DirPath, DestName);

        /// <summary>The temp file (<c>D.tmp</c>) as the runner sees it.</summary>
        public string Tmp => Path.Combine(DirPath, DestName + ".tmp");

        /// <summary>The destination relative to the share, for the partner.</summary>
        public string RelDest => Path.Combine(DirRel, DestName);
    }

    /// <summary>What a partner reader reports (parsed from a HASH reply).</summary>
    /// <param name="Bytes">Bytes read so far.</param>
    /// <param name="Sha">SHA-256 of them (hex) or "-".</param>
    /// <param name="Finished">True when the reader reached end of file or failed.</param>
    /// <param name="Error">The failure as <c>Type:0xHRESULT</c>, or null.</param>
    private sealed record ReaderStatus(long Bytes, string Sha, bool Finished, string? Error);

    /// <summary>The command line.</summary>
    private readonly ProbeOptions _options;

    /// <summary>The runner's own folder on the share.</summary>
    private readonly string _probeDir;

    /// <summary>The runner's folder's name, which is also its path relative to the share (the partner's root).</summary>
    private readonly string _probeName;

    /// <summary>The control channel.</summary>
    private readonly PartnerClient _partner;

    /// <summary>Where results go.</summary>
    private readonly SemanticsReport _report;

    /// <summary>Clean-up problems, copied to the report at the end.</summary>
    private readonly List<string> _notes = new();

    /// <summary>
    /// Creates the runner.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <param name="probeDir">The runner's own (already created) folder on the share.</param>
    /// <param name="partner">The connected control channel.</param>
    /// <param name="report">Where results are added.</param>
    public ShareSemanticsRunner(ProbeOptions options, string probeDir, PartnerClient partner, SemanticsReport report)
    {
        _options = options;
        _probeDir = probeDir;
        _probeName = Path.GetFileName(probeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _partner = partner;
        _report = report;
    }

    /// <summary>Clean-up problems met so far (a folder that would not delete).</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>
    /// Runs every selected table, run after run, adding each result to the report and printing one line for it.
    /// </summary>
    /// <returns>A task that ends when all selected tables are done.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    public async Task RunAllAsync()
    {
        foreach (var c in ShareSemanticsCatalog.All)
        {
            if (!ShareSemanticsCatalog.IsSelected(_options.Only, c)) continue;

            var runs = _options.Runs ?? c.DefaultRuns;
            ConsoleOut.Line();
            ConsoleOut.Line($"== {c.Title} ({runs} runs)");
            for (var run = 1; run <= runs; run++)
            {
                var result = await RunOneAsync(c, run).ConfigureAwait(false);
                _report.Runs.Add(result);
                ShareSemanticsReport.PrintRunLine(c, result);

                // E1 needs SMB's caches to expire between runs (design: 11 s apart); E2 uses a fresh folder each run.
                if (run < runs && c.Group is "E1a" or "E1b" && _options.SemanticsSettleSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(_options.SemanticsSettleSeconds)).ConfigureAwait(false);
            }
        }
    }

    // ── dispatch ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one run of one table. An I/O failure that is not the experiment's own result becomes an ERROR row with the
    /// reason; a lost partner propagates and ends the run.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number, from 1.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunOneAsync(SemanticsCase c, int run)
    {
        try
        {
            return c.Id switch
            {
                "E1a" => await RunE1aAsync(c, run).ConfigureAwait(false),
                "E1b-create" => await RunE1bCreateAsync(c, run).ConfigureAwait(false),
                "E1b-stale" => await RunE1bStaleAsync(c, run).ConfigureAwait(false),
                "E2-1" => await RunE2Case1Async(c, run).ConfigureAwait(false),
                "E2-2" => await RunE2Case2Async(c, run).ConfigureAwait(false),
                "E2-3" => await RunReadDuringRenameAsync(c, run, "c3", _options.SemanticsBigMb * 1024 * 1024, 64 * 1024, 25).ConfigureAwait(false),
                "E2-4" => await RunReadDuringRenameAsync(c, run, "c4", LockBytes, 64, 30).ConfigureAwait(false),
                "E2-5" => await RunE2Case5Async(c, run).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"No such table '{c.Id}'.")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ErrorRun(c, run, $"{ex.GetType().Name} {ShareProtocol.HResultText(ex.HResult)}: {ex.Message}");
        }
    }

    /// <summary>Builds an ERROR row.</summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <param name="note">What went wrong, in plain words.</param>
    /// <returns>The row, with "-" in every cell.</returns>
    private static SemanticsRun ErrorRun(SemanticsCase c, int run, string note)
        => new()
        {
            CaseId = c.Id,
            Run = run,
            Outcome = Outcomes.Error,
            Cells = c.Headers.Select(_ => "-").ToList(),
            Note = note
        };

    // ── E1 ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// E1a, one run: prime "not found", create the file ourselves with CreateNew, immediately look again with
    /// File.Exists and with open+read, record whether either said "missing", delete the file.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number (odd runs prime with File.Exists, even runs with a failed open).</param>
    /// <returns>The run's result.</returns>
    private async Task<SemanticsRun> RunE1aAsync(SemanticsCase c, int run)
    {
        var path = Path.Combine(_probeDir, $"e1a-{run:D2}.lock");
        var variant = run % 2 == 1 ? "P1" : "P2";
        try
        {
            var prime = variant == "P1" ? PrimeByExists(path) : PrimeByOpen(path);
            if (!prime.Ok)
                return ErrorRun(c, run, $"The prime did not give \"not found\" ({prime.Text}); a file of this name may already exist.");

            var create = LocalCall.Timed(() => CreateLock(path));
            if (!create.Ok)
                return ErrorRun(c, run, $"CreateNew failed on a fresh name ({create.Text}: {create.Error!.Message}).");
            // The clock for "missing for" starts when our own create has returned.
            var clock = Stopwatch.StartNew();

            // The two checks, as soon as the create has returned.
            var exists = LocalCall.Timed(() => File.Exists(path).ToString());
            var open = LocalCall.Timed(() => OpenRead(path));
            var existsMissing = exists.Detail == "False";
            var openMissing = !open.Ok && open.IsNotFound;

            // If either said "missing", see how long it keeps saying so (information for the design's second row).
            double? staleSeconds = null;
            if (existsMissing || openMissing)
            {
                var poll = await PollUntilFoundAsync(path, clock, StaleCap, !existsMissing, !openMissing).ConfigureAwait(false);
                var last = Math.Max(poll.ExistsAtMs ?? StaleCap.TotalMilliseconds, poll.OpenAtMs ?? StaleCap.TotalMilliseconds);
                staleSeconds = last / 1000.0;
            }

            var which = existsMissing && openMissing ? "both" : existsMissing ? "File.Exists" : openMissing ? "open+read" : "no";
            var note = !open.Ok && !open.IsNotFound ? $"The open+read failed for another reason ({open.Text}: {open.Error!.Message})." : "";
            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = existsMissing || openMissing ? Outcomes.SaidMissing : Outcomes.NeverMissing,
                Cells = new[]
                {
                    variant, prime.Text,
                    $"{create.Text} ({create.Ms:F1} ms)",
                    $"{exists.Detail} ({exists.Ms:F1} ms)",
                    $"{(open.Ok ? open.Detail : open.Text)} ({open.Ms:F1} ms)",
                    staleSeconds is { } s ? $"{which}, for {s:F2} s" : which
                },
                Metric1 = staleSeconds,
                Note = note
            };
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    /// <summary>
    /// E1b, first part, one run: prime "not found" on the runner, ask the partner to create the file, then try CreateNew
    /// on the runner at once. It must fail with "already exists"; if it succeeds the lock is not exclusive.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number (odd runs prime with File.Exists, even runs with a failed open).</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunE1bCreateAsync(SemanticsCase c, int run)
    {
        var name = $"e1b-create-{run:D2}.lock";
        var path = Path.Combine(_probeDir, name);
        var rel = Path.Combine(_probeName, name);
        var variant = run % 2 == 1 ? "P1" : "P2";
        try
        {
            var prime = variant == "P1" ? PrimeByExists(path) : PrimeByOpen(path);
            if (!prime.Ok)
                return ErrorRun(c, run, $"The prime did not give \"not found\" ({prime.Text}); a file of this name may already exist.");

            var theirs = await _partner.SendAsync(ShareProtocol.Verbs.CreateNew, rel).ConfigureAwait(false);
            if (!theirs.Ok)
                return ErrorRun(c, run, $"The partner could not create the file ({theirs.ResultText}: {theirs.Message}).");

            // The partner's create has returned: now our own CreateNew, as fast as we can.
            var attempt = LocalCall.Timed(() => CreateLock(path));
            var outcome = attempt.Ok ? Outcomes.Critical : attempt.IsAlreadyExists ? Outcomes.RefusedExists : Outcomes.RefusedOther;
            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = outcome,
                Cells = new[]
                {
                    variant,
                    $"{theirs.PartnerMs:F1} / {theirs.RoundTripMs:F1}",
                    attempt.Ok ? "SUCCEEDED over the partner's file" : attempt.Text,
                    $"{attempt.Ms:F1}"
                },
                Metric1 = attempt.Ms,
                Note = outcome == Outcomes.RefusedOther ? $"{attempt.Error!.Message}" : ""
            };
        }
        finally
        {
            // Whoever created it, the file goes: ask the partner, then try ourselves (harmless if already gone).
            await _partner.SendAsync(ShareProtocol.Verbs.Delete, rel).ConfigureAwait(false);
            TryDeleteFile(path);
        }
    }

    /// <summary>
    /// E1b, second part, one run (a fresh file name): prime "not found" with BOTH File.Exists and a failed open, ask the
    /// partner to create the file, then poll File.Exists and an open every 50 ms for up to 15 s and record when each first
    /// found it. The clock starts when the partner's reply arrives, so the true "missing for" is up to half a round trip longer.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunE1bStaleAsync(SemanticsCase c, int run)
    {
        var name = $"e1b-stale-{run:D2}.lock";
        var path = Path.Combine(_probeDir, name);
        var rel = Path.Combine(_probeName, name);
        try
        {
            var primeA = PrimeByExists(path);
            var primeB = PrimeByOpen(path);
            if (!primeA.Ok || !primeB.Ok)
                return ErrorRun(c, run, $"The prime did not give \"not found\" ({primeA.Text}; {primeB.Text}); a file of this name may already exist.");

            var theirs = await _partner.SendAsync(ShareProtocol.Verbs.CreateNew, rel).ConfigureAwait(false);
            if (!theirs.Ok)
                return ErrorRun(c, run, $"The partner could not create the file ({theirs.ResultText}: {theirs.Message}).");

            var clock = Stopwatch.StartNew();
            var poll = await PollUntilFoundAsync(path, clock, StaleCap, false, false).ConfigureAwait(false);

            var capMs = StaleCap.TotalMilliseconds;
            var outcome = poll.ExistsAtMs is null || poll.OpenAtMs is null ? Outcomes.StillMissing
                : poll.FoundOnFirstLook ? Outcomes.NotStale
                : Outcomes.StaleThenFound;
            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = outcome,
                Cells = new[]
                {
                    $"{theirs.PartnerMs:F1} / {theirs.RoundTripMs:F1}",
                    FormatFoundAt(poll.ExistsAtMs),
                    FormatFoundAt(poll.OpenAtMs)
                },
                Metric1 = (poll.ExistsAtMs ?? capMs) / 1000.0,
                Metric2 = (poll.OpenAtMs ?? capMs) / 1000.0
            };
        }
        finally
        {
            await _partner.SendAsync(ShareProtocol.Verbs.Delete, rel).ConfigureAwait(false);
            TryDeleteFile(path);
        }
    }

    /// <summary>Formats a "found after" time for a table cell.</summary>
    /// <param name="ms">Milliseconds from the clock's start to the first successful look, or null if never within the cap.</param>
    /// <returns>For example <c>0.00 s (first look)</c>, <c>3.20 s</c>, or <c>not found within 15 s</c>.</returns>
    private static string FormatFoundAt(double? ms)
        => ms is null ? $"not found within {StaleCap.TotalSeconds:F0} s" : $"{ms.Value / 1000.0:F2} s";

    /// <summary>The result of polling for a file that should now exist.</summary>
    /// <param name="ExistsAtMs">When File.Exists first said true (ms on the poll clock), or null if it never did before the cap.</param>
    /// <param name="OpenAtMs">When an open first got anything but "not found", or null if never before the cap.</param>
    /// <param name="FoundOnFirstLook">True when both were already found on the very first pass (no waiting was needed).</param>
    private sealed record PollResult(double? ExistsAtMs, double? OpenAtMs, bool FoundOnFirstLook);

    /// <summary>
    /// Polls File.Exists and an open+read every 50 ms until both find the file or the cap is reached.
    /// An open that fails for any reason other than "not found" (for example a sharing violation) counts as found:
    /// the server has answered that the file exists.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="clock">The stopwatch the times are read from (already running).</param>
    /// <param name="cap">When to stop.</param>
    /// <param name="existsAlreadyFound">True to treat File.Exists as found at time zero (it already said so).</param>
    /// <param name="openAlreadyFound">True to treat the open as found at time zero.</param>
    /// <returns>When each first found the file.</returns>
    private static async Task<PollResult> PollUntilFoundAsync(string path, Stopwatch clock, TimeSpan cap, bool existsAlreadyFound, bool openAlreadyFound)
    {
        double? exists = existsAlreadyFound ? 0 : null;
        double? open = openAlreadyFound ? 0 : null;
        var firstPass = true;
        var foundOnFirst = false;
        while (true)
        {
            if (exists is null && File.Exists(path)) exists = clock.Elapsed.TotalMilliseconds;
            if (open is null)
            {
                var call = LocalCall.Timed(() => OpenRead(path));
                if (call.Ok || !call.IsNotFound) open = clock.Elapsed.TotalMilliseconds;
            }
            if (firstPass)
            {
                foundOnFirst = exists is not null && open is not null;
                firstPass = false;
            }
            if ((exists is not null && open is not null) || clock.Elapsed >= cap)
                return new PollResult(exists, open, foundOnFirst);
            await Task.Delay(PollMs).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Prime variant P1: a File.Exists on a name that does not exist, which makes Windows remember "not found".
    /// </summary>
    /// <param name="path">The file (must not exist).</param>
    /// <returns>Whether it answered "not found" as expected, and a description for the table.</returns>
    private static (bool Ok, string Text) PrimeByExists(string path)
    {
        var call = LocalCall.Timed(() => File.Exists(path).ToString());
        return (call.Detail == "False", $"File.Exists={call.Detail} ({call.Ms:F1} ms)");
    }

    /// <summary>
    /// Prime variant P2: a failed open of a name that does not exist.
    /// </summary>
    /// <param name="path">The file (must not exist).</param>
    /// <returns>Whether the open failed with "not found" as expected, and a description for the table.</returns>
    private static (bool Ok, string Text) PrimeByOpen(string path)
    {
        var call = LocalCall.Timed(() => OpenRead(path));
        return (!call.Ok && call.IsNotFound, $"open: {(call.Ok ? "OPENED" : call.Text)} ({call.Ms:F1} ms)");
    }

    /// <summary>
    /// Creates a lock-style file the way TermPoint does: <c>FileMode.CreateNew</c>, write access, share none, a few
    /// bytes, close. Fails if the file exists.
    /// </summary>
    /// <param name="path">The file to create.</param>
    /// <returns>"created".</returns>
    /// <exception cref="IOException">The file already exists, or the create failed.</exception>
    private static string CreateLock(string path)
    {
        using var fs = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetBytes($"runner {Environment.MachineName} {DateTime.UtcNow:O}");
        fs.Write(bytes, 0, bytes.Length);
        return "created";
    }

    /// <summary>
    /// Opens a file for reading (sharing everything, like a lock-file read) and reads up to 256 bytes.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>For example <c>read 41 bytes</c>.</returns>
    /// <exception cref="IOException">The open or read failed (including FileNotFoundException).</exception>
    private static string OpenRead(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[256];
        return $"read {fs.Read(buffer, 0, buffer.Length)} bytes";
    }

    // ── E2 ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// E2 case 1 (the control): the partner holds the file the way TermPoint's readers do today (share ReadWrite); the
    /// runner renames a temp file over it. Expected: refused ("in use"). Measures how long the refusal lasts (retry every
    /// 100 ms up to 10 s), then has the partner close and records when the rename finally goes through.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunE2Case1Async(SemanticsCase c, int run)
    {
        var fix = MakeFixture("c1", run, _options.SemanticsBigMb * 1024 * 1024, "D");
        string? handle = null;
        try
        {
            handle = await HoldAsync(fix.RelDest, "Read", "ReadWrite").ConfigureAwait(false);

            var clock = Stopwatch.StartNew();
            var first = LocalCall.Timed(() => MoveOver(fix));
            var last = first;
            var attempts = 1;
            double? renamedWhileHeldAt = first.Ok ? 0 : null;
            while (!last.Ok && clock.Elapsed < HeldRetryCap)
            {
                await Task.Delay(RetryMs).ConfigureAwait(false);
                last = LocalCall.Timed(() => MoveOver(fix));
                attempts++;
                if (last.Ok) renamedWhileHeldAt = clock.Elapsed.TotalSeconds;
            }

            double? afterClose = null;
            if (!last.Ok)
            {
                await CloseAsync(handle).ConfigureAwait(false);
                handle = null;
                var closeClock = Stopwatch.StartNew();
                while (!last.Ok && closeClock.Elapsed < AfterCloseCap)
                {
                    last = LocalCall.Timed(() => MoveOver(fix));
                    attempts++;
                    if (last.Ok) afterClose = closeClock.Elapsed.TotalSeconds;
                    else await Task.Delay(RetryMs).ConfigureAwait(false);
                }
            }

            var outcome = renamedWhileHeldAt is not null ? Outcomes.RenamedWhileHeld
                : afterClose is not null ? Outcomes.RefusedUntilClose
                : Outcomes.RefusedAfterClose;
            var refusedFor = renamedWhileHeldAt ?? HeldRetryCap.TotalSeconds;
            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = outcome,
                Cells = new[]
                {
                    first.Ok ? $"ok ({first.Ms:F1} ms)" : $"{first.Text} ({first.Ms:F1} ms)",
                    attempts.ToString(),
                    renamedWhileHeldAt is not null
                        ? $"{renamedWhileHeldAt.Value:F1} s, then it went through"
                        : $"all {HeldRetryCap.TotalSeconds:F0} s (never went through while held)",
                    afterClose is { } a ? $"{a:F1} s after close" : renamedWhileHeldAt is not null ? "-" : $"no ({AfterCloseCap.TotalSeconds:F0} s)",
                    DestContent(fix)
                },
                Metric1 = refusedFor,
                Metric2 = afterClose,
                Note = first.Ok ? "The first rename went through although the partner held the file with share ReadWrite: the control did not behave as the design expected." : ""
            };
        }
        finally
        {
            await FinishFixtureAsync(fix, handle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// E2 case 2: the partner holds the file with share ReadWrite|Delete; the runner renames a temp file over it.
    /// Does the rename go through?
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunE2Case2Async(SemanticsCase c, int run)
    {
        var fix = MakeFixture("c2", run, _options.SemanticsBigMb * 1024 * 1024, "D");
        string? handle = null;
        try
        {
            handle = await HoldAsync(fix.RelDest, "Read", "ReadWrite+Delete").ConfigureAwait(false);
            var move = LocalCall.Timed(() => MoveOver(fix));
            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = move.Ok ? Outcomes.RenameOk : Outcomes.RenameRefused,
                Cells = new[] { $"{move.Text} ({move.Ms:F1} ms)", DestContent(fix) },
                Metric1 = move.Ms,
                Note = move.Ok ? "" : move.Error!.Message
            };
        }
        finally
        {
            await FinishFixtureAsync(fix, handle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// E2 cases 3 and 4: as case 2, but the partner is READING through its handle (slowly, in the background) while the
    /// runner renames. Afterwards the partner reports a hash of everything its handle read, which is compared with the old
    /// and the new file's hashes: old, new, a mix of the two, or an error.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <param name="tag">Folder tag (<c>c3</c> or <c>c4</c>).</param>
    /// <param name="size">File size in bytes (the big file for case 3, a small lock-sized one for case 4).</param>
    /// <param name="chunk">Bytes per partner read.</param>
    /// <param name="delayMs">Partner pause between reads, so the read spans the rename.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunReadDuringRenameAsync(SemanticsCase c, int run, string tag, int size, int chunk, int delayMs)
    {
        var fix = MakeFixture(tag, run, size, tag == "c4" ? "X.lock" : "D");
        string? handle = null;
        try
        {
            handle = await HoldAsync(fix.RelDest, "Read", "ReadWrite+Delete").ConfigureAwait(false);
            var started = await _partner.SendAsync(ShareProtocol.Verbs.Read, handle, chunk.ToString(), delayMs.ToString()).ConfigureAwait(false);
            if (!started.Ok)
                return ErrorRun(c, run, $"The partner could not start reading ({started.ResultText}: {started.Message}).");

            var move = LocalCall.Timed(() => MoveOver(fix));

            // A snapshot of the reader right after the rename returned, then wait for it to finish.
            var snapshot = Parse(await _partner.SendAsync(ShareProtocol.Verbs.Hash, handle, "0").ConfigureAwait(false));
            var final = Parse(await _partner.SendAsync(ShareProtocol.Verbs.Hash, handle, "60000").ConfigureAwait(false));

            string kind;
            if (final.Error is not null) kind = Outcomes.ReaderError;
            else if (!final.Finished) kind = Outcomes.ReaderIncomplete;
            else if (final.Sha.Equals(fix.OldHash, StringComparison.OrdinalIgnoreCase)) kind = Outcomes.ReaderOld;
            else if (final.Sha.Equals(fix.NewHash, StringComparison.OrdinalIgnoreCase)) kind = Outcomes.ReaderNew;
            else kind = Outcomes.ReaderMix;

            var notes = new List<string>();
            if (snapshot.Finished)
                notes.Add("The reader had already finished when the rename returned, so the rename did not overlap the read; this run says little about a mid-read rename.");
            if (!move.Ok)
                notes.Add($"The rename was refused: {move.Error!.Message}");
            if (kind == Outcomes.ReaderMix)
                notes.Add($"The reader got {final.Bytes} bytes (old file {fix.Old.Length}, new file {fix.New.Length}) matching neither file.");

            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = $"{(move.Ok ? Outcomes.RenameOk : Outcomes.RenameRefused)}; {kind}",
                Cells = new[]
                {
                    $"{move.Text} ({move.Ms:F1} ms)",
                    $"{snapshot.Bytes} of {fix.Old.Length} bytes{(snapshot.Finished ? ", already finished" : "")}",
                    $"{final.Bytes} bytes, finished={final.Finished}, error={final.Error ?? "-"}",
                    kind.Replace("reader ", "").ToLowerInvariant(),
                    DestContent(fix)
                },
                Metric1 = move.Ms,
                Note = string.Join(" ", notes)
            };
        }
        finally
        {
            await FinishFixtureAsync(fix, handle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// E2 case 5: the partner holds the lock file with share ReadWrite|Delete while the runner deletes it (the release),
    /// then immediately tries CreateNew (and again every 100 ms for up to 5 s). Records whether it was blocked and for
    /// how long; if still blocked after 5 s the partner closes and the time until CreateNew works is recorded.
    /// </summary>
    /// <param name="c">The table.</param>
    /// <param name="run">The run number.</param>
    /// <returns>The run's result.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<SemanticsRun> RunE2Case5Async(SemanticsCase c, int run)
    {
        var fix = MakeFixture("c5", run, LockBytes, "X.lock", withTmp: false);
        string? handle = null;
        try
        {
            handle = await HoldAsync(fix.RelDest, "Read", "ReadWrite+Delete").ConfigureAwait(false);
            var delete = LocalCall.Timed(() => { File.Delete(fix.Dest); return "deleted"; });
            if (!delete.Ok)
            {
                return new SemanticsRun
                {
                    CaseId = c.Id,
                    Run = run,
                    Outcome = Outcomes.DeleteRefused,
                    Cells = new[] { $"{delete.Text} ({delete.Ms:F1} ms)", "-", "-", "-" },
                    Note = delete.Error!.Message
                };
            }

            var clock = Stopwatch.StartNew();
            var first = LocalCall.Timed(() => CreateLock(fix.Dest));
            var last = first;
            while (!last.Ok && clock.Elapsed < BlockedCap)
            {
                await Task.Delay(RetryMs).ConfigureAwait(false);
                last = LocalCall.Timed(() => CreateLock(fix.Dest));
            }
            double? blockedFor = first.Ok ? 0 : last.Ok ? clock.Elapsed.TotalSeconds : null;

            double? afterClose = null;
            if (!last.Ok)
            {
                await CloseAsync(handle).ConfigureAwait(false);
                handle = null;
                var closeClock = Stopwatch.StartNew();
                while (!last.Ok && closeClock.Elapsed < AfterCloseCap)
                {
                    last = LocalCall.Timed(() => CreateLock(fix.Dest));
                    if (last.Ok) afterClose = closeClock.Elapsed.TotalSeconds;
                    else await Task.Delay(RetryMs).ConfigureAwait(false);
                }
            }

            return new SemanticsRun
            {
                CaseId = c.Id,
                Run = run,
                Outcome = first.Ok ? Outcomes.NotBlocked : Outcomes.Blocked,
                Cells = new[]
                {
                    $"{delete.Text} ({delete.Ms:F1} ms)",
                    first.Ok ? $"ok ({first.Ms:F1} ms)" : $"{first.Text} ({first.Ms:F1} ms)",
                    first.Ok ? "0 s (not blocked)"
                        : blockedFor is { } b ? $"{b:F1} s" : $"all {BlockedCap.TotalSeconds:F0} s (still blocked when the partner closed)",
                    afterClose is { } a ? $"{a:F1} s after close" : first.Ok || blockedFor is not null ? "-" : $"no ({AfterCloseCap.TotalSeconds:F0} s)"
                },
                Metric1 = blockedFor ?? BlockedCap.TotalSeconds,
                Metric2 = afterClose,
                Note = first.Ok ? "" : first.Error!.Message
            };
        }
        finally
        {
            await FinishFixtureAsync(fix, handle).ConfigureAwait(false);
        }
    }

    // ── E2 helpers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a run's folder with the old destination file and, unless told otherwise, the new temp file
    /// (<c>D.tmp</c>), both with random content of the same size, and works out their hashes.
    /// </summary>
    /// <param name="tag">Folder tag (case).</param>
    /// <param name="run">The run number.</param>
    /// <param name="size">Size in bytes of each file.</param>
    /// <param name="destName">Destination file name (<c>D</c> or <c>X.lock</c>).</param>
    /// <param name="withTmp">False for case 5, which has no temp file.</param>
    /// <returns>The fixture.</returns>
    /// <exception cref="IOException">The files could not be written.</exception>
    private Fixture MakeFixture(string tag, int run, int size, string destName, bool withTmp = true)
    {
        var dirName = $"e2-{tag}-run{run:D2}";
        var dirPath = Path.Combine(_probeDir, dirName);
        Directory.CreateDirectory(dirPath);

        var rng = new Random(unchecked(run * 7919 + tag.GetHashCode() * 31));
        var oldBytes = new byte[size];
        var newBytes = new byte[size];
        rng.NextBytes(oldBytes);
        rng.NextBytes(newBytes);

        var fix = new Fixture(dirPath, Path.Combine(_probeName, dirName), destName, oldBytes, newBytes,
            Convert.ToHexString(SHA256.HashData(oldBytes)), Convert.ToHexString(SHA256.HashData(newBytes)));
        File.WriteAllBytes(fix.Dest, oldBytes);
        if (withTmp) File.WriteAllBytes(fix.Tmp, newBytes);
        return fix;
    }

    /// <summary>
    /// The save's own call: <c>File.Move(tmp, dest, overwrite: true)</c>, once (the single attempt that
    /// <c>NetworkFileOps.MoveWithRetry</c> repeats).
    /// </summary>
    /// <param name="fix">The fixture.</param>
    /// <returns>"moved".</returns>
    /// <exception cref="IOException">The move was refused.</exception>
    private static string MoveOver(Fixture fix)
    {
        File.Move(fix.Tmp, fix.Dest, overwrite: true);
        return "moved";
    }

    /// <summary>
    /// What the destination holds now, by hash.
    /// </summary>
    /// <param name="fix">The fixture.</param>
    /// <returns><c>new</c>, <c>old</c>, <c>other (n bytes)</c>, <c>missing</c> or <c>unreadable: ...</c>.</returns>
    private static string DestContent(Fixture fix)
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fix.Dest)));
            return hash == fix.NewHash ? "new" : hash == fix.OldHash ? "old" : "other";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return "missing";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"unreadable: {ex.GetType().Name} {ShareProtocol.HResultText(ex.HResult)}";
        }
    }

    /// <summary>Asks the partner to hold a file open.</summary>
    /// <param name="rel">The file, relative to the share.</param>
    /// <param name="access">FileAccess text.</param>
    /// <param name="share">FileShare text (for example <c>ReadWrite+Delete</c>).</param>
    /// <returns>The handle id.</returns>
    /// <exception cref="InvalidOperationException">The partner could not open the file.</exception>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task<string> HoldAsync(string rel, string access, string share)
    {
        var reply = await _partner.SendAsync(ShareProtocol.Verbs.Hold, rel, access, share).ConfigureAwait(false);
        return reply.Ok
            ? reply.Detail
            : throw new InvalidOperationException($"The partner could not open the file ({reply.ResultText}: {reply.Message}).");
    }

    /// <summary>Asks the partner to close a handle.</summary>
    /// <param name="handle">The handle id.</param>
    /// <returns>A task that ends when the partner has answered.</returns>
    /// <exception cref="InvalidOperationException">The partner could not close it.</exception>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private async Task CloseAsync(string handle)
    {
        var reply = await _partner.SendAsync(ShareProtocol.Verbs.Close, handle).ConfigureAwait(false);
        if (!reply.Ok)
            throw new InvalidOperationException($"The partner could not close its handle ({reply.ResultText}: {reply.Message}).");
    }

    /// <summary>
    /// Ends a run: the partner closes its handle (if still open), then the run's folder is deleted.
    /// </summary>
    /// <param name="fix">The fixture.</param>
    /// <param name="handle">The partner's handle still open, or null.</param>
    /// <returns>A task that ends when clean-up is done (problems are noted, not thrown).</returns>
    private async Task FinishFixtureAsync(Fixture fix, string? handle)
    {
        if (handle is not null)
            await _partner.SendAsync(ShareProtocol.Verbs.Close, handle).ConfigureAwait(false);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Directory.Delete(fix.DirPath, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 3) _notes.Add($"Could not delete {fix.DirPath} ({ex.GetType().Name}); delete it by hand.");
                else await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Parses a partner reader's HASH reply.
    /// </summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The reader's status.</returns>
    /// <exception cref="InvalidOperationException">The partner refused, or the reply is not understood.</exception>
    private static ReaderStatus Parse(PartnerReply reply)
    {
        if (!reply.Ok)
            throw new InvalidOperationException($"The partner could not report the reader ({reply.ResultText}: {reply.Message}).");
        var values = reply.Detail.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        if (!values.TryGetValue("bytes", out var bytes) || !long.TryParse(bytes, out var n) ||
            !values.TryGetValue("sha256", out var sha) || !values.TryGetValue("finished", out var fin) ||
            !values.TryGetValue("error", out var err))
            throw new InvalidOperationException($"The partner's reader report is not understood: '{reply.Detail}'.");
        return new ReaderStatus(n, sha, fin.Equals("True", StringComparison.OrdinalIgnoreCase), err == "-" ? null : err);
    }

    /// <summary>Deletes a file, ignoring any failure (it may not exist; the folder is removed at the end anyway).</summary>
    /// <param name="path">The file.</param>
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* removed with the folder */ }
    }
}
