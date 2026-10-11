/// <summary>Collects the probe's pass / fail checks and turns them into the process exit code.</summary>
static class Report
{
    /// <summary>Descriptions of the checks that failed.</summary>
    private static readonly List<string> Failures = new();

    /// <summary>Number of checks made.</summary>
    private static int _total;

    /// <summary>Records and prints one check.</summary>
    /// <param name="ok">Whether the check passed.</param>
    /// <param name="what">What was checked, in a sentence.</param>
    public static void Check(bool ok, string what)
    {
        _total++;
        if (!ok) Failures.Add(what);
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    /// <summary>Prints the summary.</summary>
    /// <returns>0 when every check passed, 1 otherwise (use as the process exit code).</returns>
    public static int Finish()
    {
        Console.WriteLine();
        Console.WriteLine(Failures.Count == 0
            ? $"ALL {_total} CHECKS PASSED"
            : $"{Failures.Count} OF {_total} CHECKS FAILED:\n  - " + string.Join("\n  - ", Failures));
        return Failures.Count == 0 ? 0 : 1;
    }
}
