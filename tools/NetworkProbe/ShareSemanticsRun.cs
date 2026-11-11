using System.Net;
using System.Net.Sockets;

namespace NetworkProbe;

/// <summary>
/// Entry point of the share-semantics mode (<c>--share-semantics</c>, experiments E1 and E2 of
/// <c>designs/network-experiments-5.8.md</c>): routes to the partner role (the control-channel server on the second
/// computer) or the runner role (which measures everything on its own clock and writes the report).
///
/// <para>No elevation, no clumsy, no TermPoint code: this mode only makes ordinary file calls in its own folder on the
/// share. The partner never needs a synced clock.</para>
/// </summary>
internal static class ShareSemanticsRun
{
    /// <summary>How many PING round trips are measured at the start.</summary>
    private const int PingCount = 20;

    /// <summary>
    /// Runs the role named by <c>--role</c>.
    /// </summary>
    /// <param name="options">The parsed command line (already checked by <see cref="ProbeOptions.Parse"/>).</param>
    /// <returns>The process exit code: 0 completed, 1 a CRITICAL result, 2 stopped early, 3 refused to start.</returns>
    /// <exception cref="ProbeRefusalException">The root or share is missing, or the partner cannot be reached or does not see the same folder.</exception>
    public static Task<int> RunAsync(ProbeOptions options)
        => options.Role == "partner" ? RunPartnerAsync(options) : RunRunnerAsync(options);

    // ── partner ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The partner role: listens on <c>--listen</c> and serves the runner until Ctrl+C.
    /// </summary>
    /// <param name="options">The command line (<c>--root</c> and <c>--listen</c> are set).</param>
    /// <returns>0 after a Ctrl+C stop; 2 if the port could not be opened.</returns>
    /// <exception cref="ProbeRefusalException">The root does not exist or is a whole drive or share.</exception>
    private static async Task<int> RunPartnerAsync(ProbeOptions options)
    {
        var partner = new SharePartner(options.Root!, ConsoleOut.Line);
        ConsoleOut.Line($"NetworkProbe share-semantics PARTNER on {Environment.MachineName}. Root: {partner.Root}");
        ConsoleOut.Line($"Listening on TCP port {options.ListenPort} (all addresses). Every command is logged below. Ctrl+C stops it.");
        ConsoleOut.Line("It only ever touches files inside the root, and closes everything it holds when the runner disconnects.");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            await partner.RunAsync(IPAddress.Any, options.ListenPort, null, cts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (SocketException ex)
        {
            ConsoleOut.Line($"Could not listen on port {options.ListenPort}: {ex.Message} (is it already in use?)");
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    // ── runner ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The runner role: connects to the partner (for <c>--dry-run</c>, to a partner started inside this process on
    /// 127.0.0.1), measures the round trip, records both computers' SMB client settings, runs the selected tables, cleans up
    /// and writes the report.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <returns>See <see cref="SemanticsReport.ExitCode"/>.</returns>
    /// <exception cref="ProbeRefusalException">The share is missing, the partner cannot be reached, or it does not see the same folder.</exception>
    private static async Task<int> RunRunnerAsync(ProbeOptions options)
    {
        var dry = options.IsDryRun;
        var folder = dry ? Path.GetFullPath(options.DryRunFolder!) : options.Share!;
        var report = new SemanticsReport
        {
            StartedAt = DateTimeOffset.Now,
            Computer = Environment.MachineName,
            UserName = Environment.UserName,
            Options = options,
            DryRun = dry,
            Share = folder
        };

        var createdDryFolder = false;
        if (dry)
        {
            createdDryFolder = !Directory.Exists(folder);
            Directory.CreateDirectory(folder);
        }
        else if (!Directory.Exists(folder))
        {
            throw new ProbeRefusalException($"--share '{folder}' does not exist (or this computer cannot reach it).");
        }

        // A dry run starts the partner inside this process, on loopback; a real run connects to --partner.
        using var partnerStop = new CancellationTokenSource();
        Task? inProcessPartner = null;
        string host;
        int port;
        if (dry)
        {
            var partner = new SharePartner(folder, null);
            var listening = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            inProcessPartner = Task.Run(() => partner.RunAsync(IPAddress.Loopback, 0, listening, partnerStop.Token));
            port = await listening.Task.ConfigureAwait(false);
            host = "127.0.0.1";
            report.Partner = $"{host}:{port} (SIMULATED: in this process)";
        }
        else
        {
            (host, port) = SplitEndpoint(options.Partner!);
            report.Partner = $"{host}:{port}";
        }

        string? probeDir = null;
        ShareSemanticsRunner? runner = null;
        try
        {
            using var client = await PartnerClient.ConnectAsync(host, port).ConfigureAwait(false);
            try
            {
                await MeasureAndRecordAsync(client, report).ConfigureAwait(false);

                // The runner's own folder, which the partner must be able to see under its root.
                var name = $"netprobe-semantics-{report.StartedAt:yyyyMMdd-HHmmss}";
                probeDir = Path.Combine(folder, name);
                Directory.CreateDirectory(probeDir);
                report.ProbeFolder = probeDir;
                var seen = await client.SendAsync(ShareProtocol.Verbs.Exists, name).ConfigureAwait(false);
                if (!seen.Ok || seen.Detail != "dir")
                    throw new ProbeRefusalException(
                        "The partner cannot see the runner's folder under its --root " +
                        $"({(seen.Ok ? "it answered: " + seen.Detail : seen.ResultText + ": " + seen.Message)}). " +
                        "--root on the partner and --share on the runner must be the same folder.");

                runner = new ShareSemanticsRunner(options, probeDir, client, report);
                await runner.RunAllAsync().ConfigureAwait(false);
            }
            catch (ProbeAbortException ex)
            {
                report.AbortMessage = ex.Message;
            }
        }
        finally
        {
            partnerStop.Cancel();
            if (inProcessPartner is not null)
            {
                try { await inProcessPartner.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* it ends with the process */ }
            }
            report.CleanupNotes = CleanUp(probeDir, createdDryFolder ? folder : null, runner?.Notes);
        }

        ShareSemanticsReport.PrintSummary(report);
        var path = ShareSemanticsReport.WriteMarkdown(report, RepoLocator.ReportDirectory(options.ReportDir));
        ConsoleOut.Line();
        ConsoleOut.Line($"Report: {path}");
        return report.ExitCode;
    }

    /// <summary>
    /// The start-of-run measurements: PING round trips (min and median go in the report), what the partner says about
    /// itself, and both computers' SMB client settings.
    /// </summary>
    /// <param name="client">The control channel.</param>
    /// <param name="report">Where the measurements go.</param>
    /// <returns>A task that ends when everything is recorded.</returns>
    /// <exception cref="ProbeAbortException">The partner stopped answering.</exception>
    private static async Task MeasureAndRecordAsync(PartnerClient client, SemanticsReport report)
    {
        var rtts = new List<double>();
        for (var i = 0; i < PingCount; i++)
            rtts.Add((await client.SendAsync(ShareProtocol.Verbs.Ping).ConfigureAwait(false)).RoundTripMs);
        report.RoundTripsMs = rtts;
        var sorted = rtts.OrderBy(x => x).ToList();
        ConsoleOut.Line($"Control channel to {report.Partner}: round trip min {sorted[0]:F2} ms, median {sorted[sorted.Count / 2]:F2} ms ({PingCount} pings).");

        var info = await client.SendAsync(ShareProtocol.Verbs.Info).ConfigureAwait(false);
        report.PartnerInfo = info.Ok ? info.Detail : $"INFO failed ({info.ResultText})";

        ConsoleOut.Line("Reading SMB client settings (runner, then partner)...");
        report.RunnerSmb = await SmbClientConfig.ReadAsync().ConfigureAwait(false);
        var theirs = await client.SendAsync(ShareProtocol.Verbs.SmbConfig).ConfigureAwait(false);
        report.PartnerSmb = theirs.Ok ? (true, theirs.Detail) : (false, $"{theirs.ResultText}: {theirs.Message}");
    }

    /// <summary>
    /// Deletes the runner's folder (and the dry-run folder if this run created it and it is empty).
    /// </summary>
    /// <param name="probeDir">The runner's folder, or null if it was never created.</param>
    /// <param name="createdDryFolder">The dry-run folder to remove if empty, or null.</param>
    /// <param name="earlierNotes">Problems the runner already noted.</param>
    /// <returns>Notes on anything that could not be deleted.</returns>
    private static IReadOnlyList<string> CleanUp(string? probeDir, string? createdDryFolder, IReadOnlyList<string>? earlierNotes)
    {
        var notes = new List<string>(earlierNotes ?? Array.Empty<string>());
        if (probeDir is not null)
        {
            try { Directory.Delete(probeDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already gone */ }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not delete the probe folder {probeDir} ({ex.GetType().Name}); delete it by hand.");
            }
        }
        if (createdDryFolder is not null)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(createdDryFolder).Any())
                    Directory.Delete(createdDryFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not remove the dry-run folder {createdDryFolder} ({ex.GetType().Name}).");
            }
        }
        return notes;
    }

    /// <summary>
    /// Splits <c>host:port</c> (already validated by <see cref="ProbeOptions.Parse"/>).
    /// </summary>
    /// <param name="endpoint">The <c>--partner</c> value.</param>
    /// <returns>The host and port.</returns>
    private static (string Host, int Port) SplitEndpoint(string endpoint)
    {
        var i = endpoint.LastIndexOf(':');
        return (endpoint[..i], int.Parse(endpoint[(i + 1)..], System.Globalization.CultureInfo.InvariantCulture));
    }
}
