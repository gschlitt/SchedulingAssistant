namespace NetworkProbe;

/// <summary>
/// The command line (spec section 3), parsed and checked. Immutable after <see cref="Parse"/>.
/// </summary>
internal sealed class ProbeOptions
{
    /// <summary>Default for <c>--hold</c>: seconds each cut is held.</summary>
    public const int DefaultHoldSeconds = 40;

    /// <summary>Smallest <c>--hold</c> allowed: the known-bad control needs the cut for 15 s plus margin.</summary>
    public const int MinHoldSeconds = 20;

    /// <summary>Default for <c>--big-mb</c>: size of the large test file.</summary>
    public const int DefaultBigMb = 32;

    /// <summary>Default for <c>--lag-ms</c>: lag used to slow a long operation down before the cut.</summary>
    public const int DefaultLagMs = 150;

    /// <summary>Default for <c>--repeat</c>: repetitions of each mid-operation case.</summary>
    public const int DefaultRepeat = 3;

    /// <summary>
    /// Default for <c>--settle</c>: seconds to wait between creating a case's files and cutting the network. SMB
    /// caches file information for about 10 s, and an answer from that cache would look like "the call did not wait".
    /// </summary>
    public const int DefaultSettleSeconds = 11;

    /// <summary>The UNC folder on the server; the probe works only in its own subfolder there. Null for <c>--dry-run</c> and <c>--list</c>.</summary>
    public string? Share { get; private set; }

    /// <summary>Explicit path of clumsy.exe, or null to look for it.</summary>
    public string? ClumsyPath { get; private set; }

    /// <summary>
    /// True to filter on port 445 only (the default), false (<c>--by-address</c>) to filter on port 445 AND the server's
    /// DNS addresses. Port-only is the default because the address filter leaked on the first real run (2026-10-22):
    /// Windows' SMB connection to the server used an IPv6 address DNS does not return (a temporary address), so the
    /// known-bad control got through. <c>--port-only</c> is still accepted, and does nothing more.
    /// </summary>
    public bool PortOnly { get; private set; } = true;

    /// <summary>Seconds each cut is held.</summary>
    public int HoldSeconds { get; private set; } = DefaultHoldSeconds;

    /// <summary>Size in megabytes of the large file used by the long cases.</summary>
    public int BigMb { get; private set; } = DefaultBigMb;

    /// <summary>Lag in milliseconds used to slow a long operation down before the cut.</summary>
    public int LagMs { get; private set; } = DefaultLagMs;

    /// <summary>Repetitions of each mid-operation case.</summary>
    public int Repeat { get; private set; } = DefaultRepeat;

    /// <summary>Seconds to let SMB's caches expire between set-up and a cut-before-call case.</summary>
    public int SettleSeconds { get; private set; } = DefaultSettleSeconds;

    /// <summary>Case names to run, or null for all.</summary>
    public IReadOnlyCollection<string>? Only { get; private set; }

    /// <summary>Where the Markdown report goes, or null for the default.</summary>
    public string? ReportDir { get; private set; }

    /// <summary>Local folder for the plumbing check, or null for a real run.</summary>
    public string? DryRunFolder { get; private set; }

    /// <summary>True for <c>--list</c>: print the case matrix and exit.</summary>
    public bool ListOnly { get; private set; }

    /// <summary>True for <c>--help</c>.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>True when the run is the plumbing check: no clumsy, no server, every case SIMULATED.</summary>
    public bool IsDryRun => DryRunFolder is not null;

    /// <summary>
    /// Parses and validates the command line.
    /// </summary>
    /// <param name="args">The program's arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="UsageException">An option is unknown, lacks its value, or is out of range, or the combination is not allowed.</exception>
    public static ProbeOptions Parse(string[] args)
    {
        var o = new ProbeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--share":       o.Share = NextValue(args, ref i, arg); break;
                case "--clumsy":      o.ClumsyPath = NextValue(args, ref i, arg); break;
                case "--port-only":   o.PortOnly = true; break;
                case "--by-address":  o.PortOnly = false; break;
                case "--hold":        o.HoldSeconds = NextInt(args, ref i, arg, MinHoldSeconds, 600); break;
                case "--big-mb":      o.BigMb = NextInt(args, ref i, arg, 1, 1024); break;
                case "--lag-ms":      o.LagMs = NextInt(args, ref i, arg, 1, 2000); break;
                case "--repeat":      o.Repeat = NextInt(args, ref i, arg, 1, 20); break;
                case "--settle":      o.SettleSeconds = NextInt(args, ref i, arg, 0, 120); break;
                case "--only":        o.Only = ParseCaseNames(NextValue(args, ref i, arg)); break;
                case "--report-dir":  o.ReportDir = NextValue(args, ref i, arg); break;
                case "--dry-run":     o.DryRunFolder = NextValue(args, ref i, arg); break;
                case "--list":        o.ListOnly = true; break;
                case "--help":
                case "-h":
                case "/?":            o.ShowHelp = true; break;
                default: throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        if (o.ListOnly || o.ShowHelp)
            return o;

        if (o.Share is not null && o.DryRunFolder is not null)
            throw new UsageException("Use either --share (the real run) or --dry-run (the plumbing check), not both.");
        if (o.Share is null && o.DryRunFolder is null)
            throw new UsageException("Give --share <UNC folder> for the real run, --dry-run <local folder> for the plumbing check, or --list.");
        return o;
    }

    /// <summary>The usage text printed for <c>--help</c> and after a usage error.</summary>
    public static string UsageText =>
        """
        NetworkProbe: do TermPoint's real network file calls give up within their 5 s deadline when SMB goes dark?

          NetworkProbe --share \\server\share\folder [options]     the real run (elevated, TermPoint closed)
          NetworkProbe --dry-run <local folder> [options]          plumbing check: no clumsy, no server, every case SIMULATED
          NetworkProbe --list                                      print the case matrix and exit

        Options:
          --share <UNC folder>   required for the real run; the probe works only in its own netprobe-<time> subfolder there
          --clumsy <path>        clumsy.exe (x64). Default: looked for in Downloads, Desktop, Documents, C:\tools, Program Files
          --by-address           filter on port 445 AND the server's DNS addresses (default: port 445 only, which also
                                 cuts SMB to any other server; the address filter can leak when SMB uses an address DNS
                                 does not return)
          --hold <s>             seconds each cut is held (default 40, minimum 20)
          --big-mb <n>           size of the large test file in MB (default 32)
          --lag-ms <n>           lag used to slow a long operation before the cut (default 150)
          --repeat <n>           repetitions of each mid-operation case (default 3)
          --settle <s>           seconds between set-up and a cut-before-call case, so SMB caches expire (default 11)
          --only <a,b,...>       run only these cases (see --list)
          --report-dir <dir>     where the Markdown report goes (default <repo>\tmp\netprobe, else .\netprobe-reports)
          --dry-run <folder>     see above
          --list                 see above
        """;

    /// <summary>
    /// Reads the value that follows an option.
    /// </summary>
    /// <param name="args">All arguments.</param>
    /// <param name="index">Index of the option; moved to the value.</param>
    /// <param name="option">The option's name, for messages.</param>
    /// <returns>The value.</returns>
    /// <exception cref="UsageException">There is no value after the option.</exception>
    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new UsageException($"{option} needs a value.");
        return args[++index];
    }

    /// <summary>
    /// Reads and range-checks an integer value that follows an option.
    /// </summary>
    /// <param name="args">All arguments.</param>
    /// <param name="index">Index of the option; moved to the value.</param>
    /// <param name="option">The option's name, for messages.</param>
    /// <param name="min">Smallest allowed value.</param>
    /// <param name="max">Largest allowed value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="UsageException">The value is missing, not a whole number, or out of range.</exception>
    private static int NextInt(string[] args, ref int index, string option, int min, int max)
    {
        var text = NextValue(args, ref index, option);
        if (!int.TryParse(text, out var value) || value < min || value > max)
            throw new UsageException($"{option} must be a whole number from {min} to {max} (got '{text}').");
        return value;
    }

    /// <summary>
    /// Splits and checks the comma-separated list given to <c>--only</c>.
    /// </summary>
    /// <param name="text">The raw list.</param>
    /// <returns>The case names.</returns>
    /// <exception cref="UsageException">A name is not a case.</exception>
    private static IReadOnlyCollection<string> ParseCaseNames(string text)
    {
        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = CaseCatalog.Unknown(names);
        if (names.Length == 0 || unknown.Count > 0)
            throw new UsageException($"--only: unknown case(s): {string.Join(", ", unknown)}. See --list for the case names.");
        return names;
    }
}
