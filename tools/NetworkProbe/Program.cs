namespace NetworkProbe;

/// <summary>
/// NetworkProbe: do TermPoint's real network file calls hand control back within their 5 s deadline when the SMB
/// connection goes dark? See <c>designs/network-probe-spec.md</c> and <c>tools/NetworkProbe/README.md</c>.
///
/// <para>Exit codes: 0 clean (or a dry run that worked); 1 at least one case FAILED; 2 the rig did not work or the run
/// stopped early; 3 refused to start (a safety rule); 64 command-line error.</para>
/// </summary>
internal static class Program
{
    /// <summary>
    /// Parses the command line and routes to <c>--help</c>, <c>--list</c> or a run.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The process exit code (see the class summary).</returns>
    private static async Task<int> Main(string[] args)
    {
        _ = ProbeClock.Now; // start the clock

        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(ProbeOptions.UsageText);
            return 64;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ProbeOptions.UsageText);
            return 0;
        }

        if (options.ListOnly)
        {
            ReportWriter.PrintCaseMatrix(options);
            return 0;
        }

        try
        {
            return await ProbeRun.RunAsync(options).ConfigureAwait(false);
        }
        catch (ProbeRefusalException ex)
        {
            Console.Error.WriteLine($"REFUSED TO RUN: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            // Handled here on purpose: an exception that escaped Main would reach the process-wide handlers.
            Console.Error.WriteLine($"The probe failed unexpectedly: {ex}");
            return 2;
        }
    }
}
