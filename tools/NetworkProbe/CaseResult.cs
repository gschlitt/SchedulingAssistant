using System.Text.RegularExpressions;

namespace NetworkProbe;

/// <summary>
/// Everything recorded about one run of one case: the fields of spec section 5.6 plus the verdict, ready for the
/// console table and the Markdown report.
/// </summary>
internal sealed record CaseResult
{
    /// <summary>The case name.</summary>
    public required string Name { get; init; }

    /// <summary><c>CB</c> (cut before the call), <c>CM</c> (cut mid-operation) or <c>control</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>Which repetition this is (1 for cases run once).</summary>
    public int Repetition { get; init; } = 1;

    /// <summary>The verdict.</summary>
    public required Verdict Verdict { get; init; }

    /// <summary>Why, in plain words.</summary>
    public required string Reason { get; init; }

    /// <summary><c>call-return</c>: how long the call took to hand back its task; null if it never did.</summary>
    public TimeSpan? CallReturn { get; init; }

    /// <summary><c>result-after-cut</c>: time from the cut being confirmed until the task completed; null if it never did.</summary>
    public TimeSpan? ResultAfterCut { get; init; }

    /// <summary>True when the task finished only after the network came back (the A1 signature).</summary>
    public bool CompletedAfterRestore { get; init; }

    /// <summary><c>result</c>: the method's return value (or the exception it threw), written out.</summary>
    public string ResultText { get; init; } = "-";

    /// <summary><c>landed-in</c>: the step the stall was reported in ("CopyFile (write)", ...), or "nothing logged".</summary>
    public string LandedIn { get; init; } = "-";

    /// <summary><c>recovery</c>: seconds from stopping clumsy until the share answered again; null when not applicable.</summary>
    public TimeSpan? Recovery { get; init; }

    /// <summary><c>log</c>: the lines NetworkFileOps (or anything else) logged while the case ran.</summary>
    public IReadOnlyList<LogLine> Log { get; init; } = Array.Empty<LogLine>();

    /// <summary>Extra remarks (retries, large-file size, clean-up trouble).</summary>
    public string Notes { get; init; } = "";

    /// <summary>For a mid-operation case: the large file's size in MB used for this run.</summary>
    public int BigMb { get; init; }

    /// <summary>True when the case was NOT TESTED because it finished before the cut, so a run with a larger file may work.</summary>
    public bool RetryWithLargerFile { get; init; }

    /// <summary>The case name with its repetition, for tables: <c>copy-cm #2</c>.</summary>
    public string Label => Mode == "CM" ? $"{Name} #{Repetition}" : Name;

    /// <summary>The verdict as shown to the user.</summary>
    public string VerdictText => Verdict switch
    {
        Verdict.Pass => "PASS",
        Verdict.Fail => "FAIL",
        Verdict.NotTested => "NOT TESTED",
        _ => "SIMULATED"
    };
}

/// <summary>
/// Reads the lines TermPoint logged to see where a stall was reported.
/// </summary>
internal static partial class LogAnalysis
{
    /// <summary>Matches "NetworkFileOps: CopyFile (write) made no progress for 5000ms ..." and captures the step.</summary>
    [GeneratedRegex(@"NetworkFileOps: (?<step>.+?) made no progress for")]
    private static partial Regex StallLine();

    /// <summary>Matches "NetworkFileOps: File.Exists timed out after 5000ms ..." and captures the operation.</summary>
    [GeneratedRegex(@"NetworkFileOps: (?<step>.+?) timed out after")]
    private static partial Regex TimeoutLine();

    /// <summary>
    /// Finds the step a stall or timeout was reported in.
    /// </summary>
    /// <param name="lines">The lines logged during the case.</param>
    /// <returns>For example <c>CopyFile (write)</c>, or <c>nothing logged</c> when no stall or timeout line was written.</returns>
    public static string LandedIn(IReadOnlyList<LogLine> lines)
    {
        foreach (var line in lines)
            if (StallLine().Match(line.Text) is { Success: true } stall)
                return stall.Groups["step"].Value;
        foreach (var line in lines)
            if (TimeoutLine().Match(line.Text) is { Success: true } timeout)
                return timeout.Groups["step"].Value;
        return "nothing logged";
    }
}
