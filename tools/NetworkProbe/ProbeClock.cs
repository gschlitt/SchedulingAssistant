using System.Diagnostics;

namespace NetworkProbe;

/// <summary>
/// The one clock every timing in the probe is read from. A monotonic stopwatch started when the program starts, so
/// timings never jump with the wall clock and every measurement ("the cut was confirmed at +61.2 s", "the call
/// completed at +66.3 s") is on the same scale and can be subtracted directly. One computer, one clock.
/// </summary>
internal static class ProbeClock
{
    /// <summary>Started when the type is first used (the first line of <see cref="Program.Main"/> touches it).</summary>
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    /// <summary>Time since the probe started.</summary>
    public static TimeSpan Now => Watch.Elapsed;

    /// <summary>
    /// Formats a clock reading for a console or report line, for example <c>+61.2s</c>.
    /// </summary>
    /// <param name="at">A reading of <see cref="Now"/>.</param>
    /// <returns>The reading as seconds with one decimal, prefixed with a plus sign.</returns>
    public static string Stamp(TimeSpan at) => $"+{at.TotalSeconds:F1}s";
}
