using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TermPoint.Tests.Harness;

/// <summary>One kind of breach as a shard process found it, in a form that goes into a JSON file and back.</summary>
internal sealed class ShardTally
{
    /// <summary>Invariant, kind and finding (<see cref="FoundViolation.Signature"/>).</summary>
    public string Signature { get; set; } = "";

    /// <summary>"F1", ... or "new".</summary>
    public string FindingId { get; set; } = "";

    /// <summary>"I1" to "I6", "Hang" or "exception".</summary>
    public string Invariant { get; set; } = "";

    /// <summary>The reason text of the first occurrence in the shard.</summary>
    public string Reason { get; set; } = "";

    /// <summary>In how many of the shard's runs it appeared.</summary>
    public int Runs { get; set; }

    /// <summary>The number of events of the shard's smallest run that shows it (after shrinking).</summary>
    public int BestEvents { get; set; } = int.MaxValue;

    /// <summary>The printed story of that run.</summary>
    public string BestStory { get; set; } = "";

    /// <summary>How many candidates the shrinks of this breach replayed in the shard.</summary>
    public int ShrinkReplays { get; set; }

    /// <summary>False when a replay did not reproduce the breach.</summary>
    public bool Reproduced { get; set; } = true;
}

/// <summary>What one shard process found: the tallies and the counts a <see cref="GeneratedLog"/> keeps.</summary>
internal sealed class ShardResult
{
    /// <summary>The number of runs (random walks) the shard made.</summary>
    public int Runs { get; set; }

    /// <summary>The number of runs that ended because their state was already explored (always 0 for random walks).</summary>
    public int RunsEndedByMemo { get; set; }

    /// <summary>The number of events that happened in the shard's runs.</summary>
    public int Events { get; set; }

    /// <summary>The time the shard's runs themselves took, in milliseconds (shrinking replays are counted separately).</summary>
    public double RunTimeMilliseconds { get; set; }

    /// <summary>The number of replays the shrinker made in the shard.</summary>
    public int ShrinkReplays { get; set; }

    /// <summary>The keys of every event that was enabled in some run of the shard.</summary>
    public List<string> EnabledKeys { get; set; } = new();

    /// <summary>The breaches found, one entry per signature.</summary>
    public List<ShardTally> Tallies { get; set; } = new();
}

/// <summary>The work a shard process is given: which random walks to run.</summary>
internal sealed class ShardJob
{
    /// <summary>The seeds to run, in ascending order.</summary>
    public List<int> Seeds { get; set; } = new();

    /// <summary>The number of events in each walk.</summary>
    public int Steps { get; set; }

    /// <summary>The file the shard writes its <see cref="ShardResult"/> to.</summary>
    public string ResultFile { get; set; } = "";
}

/// <summary>
/// Splits the random walks across separate test processes (spec item 9.3, step 9.3c-2b). The crash-point hooks and the network fault
/// hook are process-wide, so a second thread could not run a second world; a second PROCESS can. The parent test launches
/// <c>dotnet test &lt;this test assembly&gt; --filter RandomWalkShardTests</c> once per shard, with the shard's seeds in a job file named
/// by an environment variable; the shard test (<see cref="RandomWalkShardTests"/>) runs them and writes its tallies to a JSON file; the
/// parent merges the files into the same <see cref="GeneratedLog"/> the in-process loop would have filled, so the report and the
/// rediscovery test read exactly as before.
///
/// <para>A walk is a pure function of its seed, so the tallies are the same however the seeds are divided; the seeds are dealt out
/// round-robin (seed s to shard (s-1) mod n) so the shards get alike work.</para>
///
/// <para><b>Processes.</b> The parent keeps every <see cref="Process"/> it started, gives each a time limit, and on a timeout or a
/// failure kills ONLY that process's tree (<c>Kill(entireProcessTree: true)</c> on the object it holds); it never looks a process up by
/// name. A failed or timed-out shard fails the test with the shard's output.</para>
/// </summary>
internal static class RandomWalkSharding
{
    /// <summary>The environment variable that tells a shard process where its job file is. The shard test is skipped when it is not set.</summary>
    public const string JobVariable = "TERMPOINT_HARNESS_SHARD_JOB";

    /// <summary>Environment variable: how many minutes a shard process may take before it is killed (default 40).</summary>
    public const string TimeoutMinutesVariable = "TERMPOINT_HARNESS_SHARD_MINUTES";

    /// <summary>The default time limit of a shard process, in minutes.</summary>
    public const int DefaultTimeoutMinutes = 40;

    /// <summary>The name of the shard test class: the filter that selects it.</summary>
    private const string ShardTestFilter = "FullyQualifiedName~RandomWalkShardTests";

    /// <summary>For a test of the failure path only: the filter the shard processes are given instead of <see cref="ShardTestFilter"/> (a filter that matches nothing makes every shard "fail" without writing its result). Null in a real run.</summary>
    internal static string? FilterOverride { get; set; }

    // ── The parent ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the random walks of a budget in <see cref="HarnessBudget.Processes"/> separate processes and merges their results.
    /// </summary>
    /// <param name="budget">The budget (seeds, steps, processes).</param>
    /// <param name="progress">Receives a progress line when a shard finishes; may be null.</param>
    /// <param name="timeout">How long a shard process may take before it is killed; null for <see cref="TimeoutMinutesVariable"/> or its default (a test of the timeout passes a short one).</param>
    /// <returns>The merged outcome, shaped exactly like the in-process one.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">A shard process failed, or did not finish in time (its output is in the message).</exception>
    public static async Task<StrategyOutcome> RunAsync(HarnessBudget budget, Action<string>? progress, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var shards    = Math.Max(1, Math.Min(budget.Processes, budget.Seeds));
        var folder    = Path.Combine(Path.GetTempPath(), $"termpoint_harness_shards_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var children = new List<ShardProcess>();
        try
        {
            for (var shard = 0; shard < shards; shard++)
            {
                var job = new ShardJob
                {
                    Seeds      = Enumerable.Range(1, budget.Seeds).Where(seed => (seed - 1) % shards == shard).ToList(),
                    Steps      = budget.Steps,
                    ResultFile = Path.Combine(folder, $"shard-{shard}-result.json")
                };
                var jobFile = Path.Combine(folder, $"shard-{shard}-job.json");
                File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
                children.Add(ShardProcess.Start(shard, jobFile, job));
            }

            progress?.Invoke($"random walks: {budget.Seeds} seeds split across {shards} processes");

            var limit = timeout ?? TimeSpan.FromMinutes(
                int.TryParse(Environment.GetEnvironmentVariable(TimeoutMinutesVariable), out var minutes) && minutes > 0 ? minutes : DefaultTimeoutMinutes);
            await WaitForAllAsync(children, limit, progress, stopwatch);

            var log = new GeneratedLog(new RunOptions { Title = "a random walk" });
            foreach (var child in children)   // in shard order, so the merge never depends on which process finished first
                log.Absorb(JsonSerializer.Deserialize<ShardResult>(File.ReadAllText(child.Job.ResultFile))
                           ?? throw new InvalidOperationException($"Shard {child.Index} wrote no result."));

            stopwatch.Stop();
            return new StrategyOutcome
            {
                Name = "Random walks",
                Parameters = $"{budget.Seeds} seeds x {budget.Steps} events, in {shards} processes",
                Log = log, Elapsed = stopwatch.Elapsed
            };
        }
        finally
        {
            foreach (var child in children) child.KillIfRunning();   // only processes this method started
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Waits for every shard process, each under the same limit. The first one to fail or time out fails the whole run: the others are
    /// killed (their trees only) and the failure is thrown with the failing shard's output.
    /// </summary>
    /// <param name="children">The running shards.</param>
    /// <param name="timeout">How long a shard may take in all.</param>
    /// <param name="progress">Receives a line when a shard finishes.</param>
    /// <param name="stopwatch">Time since the shards were started.</param>
    /// <exception cref="Xunit.Sdk.XunitException">A shard failed or timed out.</exception>
    private static async Task WaitForAllAsync(
        List<ShardProcess> children, TimeSpan timeout, Action<string>? progress, Stopwatch stopwatch)
    {
        var pending = children.ToList();
        using var limit = new CancellationTokenSource(timeout);

        while (pending.Count > 0)
        {
            var waits = pending.ToDictionary(c => c.WaitAsync(limit.Token), c => c);
            Task<int> finished;
            try { finished = await Task.WhenAny(waits.Keys); }
            catch (OperationCanceledException) { throw TimedOut(pending, timeout); }

            var child = waits[finished];
            pending.Remove(child);

            int exitCode;
            try { exitCode = await finished; }
            catch (OperationCanceledException) { throw TimedOut(pending.Append(child).ToList(), timeout); }

            if (exitCode != 0 || !File.Exists(child.Job.ResultFile))
            {
                foreach (var other in pending) other.KillIfRunning();
                throw new Xunit.Sdk.XunitException(
                    $"Random-walk shard {child.Index} failed (exit code {exitCode}, result file {(File.Exists(child.Job.ResultFile) ? "written" : "missing")}):\n{child.Output}");
            }
            progress?.Invoke($"random walks: shard {child.Index} finished ({children.Count - pending.Count} of {children.Count}), {stopwatch.Elapsed:mm\\:ss}");
        }
    }

    /// <summary>Builds the failure for shards that did not finish in time, killing them (their trees) first.</summary>
    /// <param name="stuck">The shards still running.</param>
    /// <param name="timeout">The limit that passed.</param>
    private static Xunit.Sdk.XunitException TimedOut(List<ShardProcess> stuck, TimeSpan timeout)
    {
        foreach (var child in stuck) child.KillIfRunning();
        var limit = timeout.TotalMinutes >= 1 ? $"{timeout.TotalMinutes:0.#} minutes" : $"{timeout.TotalSeconds:0.#} seconds";
        return new Xunit.Sdk.XunitException(
            $"Random-walk shard(s) {string.Join(", ", stuck.Select(c => $"{c.Index} (process {c.ProcessId})"))} did not finish within {limit} and were killed. " +
            $"Their output:\n{string.Join("\n---\n", stuck.Select(c => c.Output))}");
    }

    /// <summary>
    /// One shard process: the <see cref="Process"/> this test process started, its captured output, and its job. The object is the only
    /// handle used to wait for it or to kill it.
    /// </summary>
    private sealed class ShardProcess
    {
        /// <summary>The process we started.</summary>
        private readonly Process _process;

        /// <summary>Everything the process wrote (both streams), for the failure message.</summary>
        private readonly StringBuilder _output = new();

        /// <summary>Creates the record around a started process.</summary>
        private ShardProcess(int index, ShardJob job, Process process)
        {
            Index    = index;
            Job      = job;
            _process = process;
        }

        /// <summary>The shard's number (0-based).</summary>
        public int Index { get; }

        /// <summary>The work the shard was given.</summary>
        public ShardJob Job { get; }

        /// <summary>The operating-system id of the process this test process started (the one that is killed on a timeout).</summary>
        public int ProcessId => _process.Id;

        /// <summary>Everything the process has written so far.</summary>
        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        /// <summary>Starts a shard process: <c>dotnet test &lt;this assembly&gt; --filter RandomWalkShardTests</c> with the harness switched on and the job named.</summary>
        /// <param name="index">The shard's number.</param>
        /// <param name="jobFile">The job file the shard reads.</param>
        /// <param name="job">The job (for the record).</param>
        /// <returns>The running shard.</returns>
        public static ShardProcess Start(int index, string jobFile, ShardJob job)
        {
            var info = new ProcessStartInfo(DotnetExecutable())
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                WorkingDirectory       = Path.GetDirectoryName(typeof(World).Assembly.Location)!
            };
            foreach (var argument in new[] { "test", typeof(World).Assembly.Location, "--nologo", "-v", "q", "--filter", FilterOverride ?? ShardTestFilter })
                info.ArgumentList.Add(argument);

            // The child is a fresh test run: it must not inherit the parent run's test-host plumbing, only its own switches.
            foreach (var name in info.Environment.Keys.Where(k => k.StartsWith("VSTEST_", StringComparison.OrdinalIgnoreCase)
                                                              || k.StartsWith("TESTINGPLATFORM_", StringComparison.OrdinalIgnoreCase)).ToList())
                info.Environment.Remove(name);
            info.Environment[HarnessFactAttribute.EnvironmentVariableName] = "1";
            info.Environment[JobVariable] = jobFile;
            info.Environment[HarnessBudget.ProcessesVariable] = "1";

            var process = new Process { StartInfo = info };
            var record  = new ShardProcess(index, job, process);
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (record._output) record._output.AppendLine(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) lock (record._output) record._output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return record;
        }

        /// <summary>Waits for the process to end.</summary>
        /// <param name="cancel">Cancelled when the time limit passes.</param>
        /// <returns>The process's exit code.</returns>
        public async Task<int> WaitAsync(CancellationToken cancel)
        {
            await _process.WaitForExitAsync(cancel);
            _process.WaitForExit();   // lets the asynchronous output readers finish, so Output is complete
            return _process.ExitCode;
        }

        /// <summary>Kills this process and the processes it started (the <c>dotnet test</c> host and its test host), if it is still running. Never touches any other process.</summary>
        public void KillIfRunning()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (Exception) { /* it ended by itself in the meantime */ }
        }
    }

    /// <summary>
    /// Finds the <c>dotnet</c> executable this test process is running on: the SDK's <c>DOTNET_HOST_PATH</c> if it is set, this
    /// process's own executable if it is <c>dotnet</c>, the runtime's install folder (three levels above the framework folder), or
    /// plain <c>dotnet</c> from the PATH.
    /// </summary>
    private static string DotnetExecutable()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } fromSdk && File.Exists(fromSdk)) return fromSdk;

        if (Environment.ProcessPath is { } self
            && string.Equals(Path.GetFileNameWithoutExtension(self), "dotnet", StringComparison.OrdinalIgnoreCase)) return self;

        var runtime = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root    = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        var beside  = Path.Combine(root, fileName);
        return File.Exists(beside) ? beside : "dotnet";
    }

    // ── The shard (the child) ────────────────────────────────────────────────

    /// <summary>
    /// What a shard process does: runs the given random walks one after the other, in ascending seed order, and turns the log into a
    /// result that can be written to a file. Also the in-process loop of <see cref="Explorers.RunRandomAsync"/>.
    /// </summary>
    /// <param name="seeds">The seeds to run.</param>
    /// <param name="steps">The number of events in each walk.</param>
    /// <param name="progress">Receives a progress line now and then; may be null.</param>
    /// <returns>What the walks found.</returns>
    public static async Task<GeneratedLog> RunSeedsAsync(IEnumerable<int> seeds, int steps, Action<string>? progress)
    {
        var options = new RunOptions { Title = "a random walk" };
        var log     = new GeneratedLog(options);
        var watch   = Stopwatch.StartNew();

        foreach (var seed in seeds)
        {
            var result = await RunExecutor.ExecuteAsync(new RandomSource(seed, steps), options);
            await log.ObserveAsync(result);

            if (log.Runs % 10 == 0)
                progress?.Invoke($"random walks: {log.Runs} done (seed {seed}), {log.Tallies.Count} kinds of breach, {watch.Elapsed:mm\\:ss}");
        }
        return log;
    }

    /// <summary>Turns a log into the form a shard writes to its result file.</summary>
    /// <param name="log">What the shard's walks found.</param>
    public static ShardResult ToResult(GeneratedLog log)
        => new()
        {
            Runs                = log.Runs,
            RunsEndedByMemo     = log.RunsEndedByMemo,
            Events              = log.Events,
            RunTimeMilliseconds = log.RunTime.TotalMilliseconds,
            ShrinkReplays       = log.ShrinkReplays,
            EnabledKeys         = log.EnabledKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            Tallies             = log.Tallies.Values.Select(t => new ShardTally
            {
                Signature = t.Signature, FindingId = t.FindingId, Invariant = t.Invariant, Reason = t.Reason, Runs = t.Runs,
                BestEvents = t.BestEvents, BestStory = t.BestStory, ShrinkReplays = t.ShrinkReplays, Reproduced = t.Reproduced
            }).ToList()
        };
}
