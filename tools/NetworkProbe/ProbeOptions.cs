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

    /// <summary>Default for <c>--repeat</c> in the late-landing mode (<c>--late-landing</c>).</summary>
    public const int DefaultLateRepeat = 2;

    /// <summary>Default for <c>--holds</c>: seconds the cut is held in the late-landing mode.</summary>
    public static readonly IReadOnlyList<int> DefaultHolds = new[] { 10, 30, 60, 90, 180 };

    /// <summary>Smallest hold allowed in a real late-landing run: the call must have time to give up (5 s) and be seen to.</summary>
    public const int MinLateHoldSeconds = 6;

    /// <summary>True once <c>--repeat</c> was given, so the late-landing mode knows whether to use its own default.</summary>
    private bool _repeatGiven;

    /// <summary>
    /// True for <c>--late-landing</c>: run ONLY the late-landing experiment (after the two rig controls). The question it
    /// answers: when the network drops while a delete or a rename-over-existing is in flight and later returns, does the
    /// abandoned operation still take effect on the server?
    /// </summary>
    public bool LateLanding { get; private set; }

    /// <summary>Seconds the cut is held in each late-landing cycle (<c>--holds</c>).</summary>
    public IReadOnlyList<int> Holds { get; private set; } = DefaultHolds;

    /// <summary>
    /// Repetitions of each late-landing case and hold: <c>--repeat</c> when given, else <see cref="DefaultLateRepeat"/>.
    /// </summary>
    public int LateRepeat => _repeatGiven ? Repeat : DefaultLateRepeat;

    /// <summary>
    /// True for <c>--share-semantics</c>: run ONLY the share-semantics experiments E1 and E2 of
    /// <c>designs/network-experiments-5.8.md</c> (does our own create clear the cached "not found"; does a reader's open
    /// file block the save), in one of two roles (<see cref="Role"/>).
    /// </summary>
    public bool ShareSemantics { get; private set; }

    /// <summary><c>runner</c> or <c>partner</c> (<c>--role</c>), for <c>--share-semantics</c>; null when not given (a dry run is a runner).</summary>
    public string? Role { get; private set; }

    /// <summary>TCP port the partner listens on (<c>--listen</c>); 0 when not given.</summary>
    public int ListenPort { get; private set; }

    /// <summary>The partner's <c>host:port</c> as given to the runner (<c>--partner</c>), or null.</summary>
    public string? Partner { get; private set; }

    /// <summary>The folder the partner is confined to (<c>--root</c>); it must be the runner's <c>--share</c>.</summary>
    public string? Root { get; private set; }

    /// <summary>Runs per table in the share-semantics mode (<c>--runs</c>), or null for each table's own default (20 for E1, 5 for E2).</summary>
    public int? Runs { get; private set; }

    /// <summary>Default for <c>--big-mb</c> in the share-semantics mode (the design's 8 MB file).</summary>
    public const int DefaultSemanticsBigMb = 8;

    /// <summary>True once <c>--big-mb</c> was given, so the share-semantics mode knows whether to use its own default.</summary>
    private bool _bigMbGiven;

    /// <summary>True once <c>--settle</c> was given.</summary>
    private bool _settleGiven;

    /// <summary>The large file's size in MB for the share-semantics mode: <c>--big-mb</c> when given, else 8.</summary>
    public int SemanticsBigMb => _bigMbGiven ? BigMb : DefaultSemanticsBigMb;

    /// <summary>
    /// Seconds between runs of the E1 tables: <c>--settle</c> when given, else 11 (SMB's caches expire), except that a
    /// dry run (a local disk with no SMB cache) uses 0 unless told otherwise.
    /// </summary>
    public int SemanticsSettleSeconds => _settleGiven ? SettleSeconds : IsDryRun ? 0 : DefaultSettleSeconds;

    /// <summary>The raw <c>--only</c> text, checked once the mode is known (the case names differ per mode).</summary>
    private string? _onlyText;

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
        var holdsGiven = false;
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
                case "--big-mb":      o.BigMb = NextInt(args, ref i, arg, 1, 1024); o._bigMbGiven = true; break;
                case "--lag-ms":      o.LagMs = NextInt(args, ref i, arg, 1, 2000); break;
                case "--repeat":      o.Repeat = NextInt(args, ref i, arg, 1, 20); o._repeatGiven = true; break;
                case "--late-landing": o.LateLanding = true; break;
                case "--holds":       o.Holds = ParseHolds(NextValue(args, ref i, arg)); holdsGiven = true; break;
                case "--settle":      o.SettleSeconds = NextInt(args, ref i, arg, 0, 120); o._settleGiven = true; break;
                case "--only":        o._onlyText = NextValue(args, ref i, arg); break;
                case "--share-semantics": o.ShareSemantics = true; break;
                case "--role":        o.Role = NextValue(args, ref i, arg).ToLowerInvariant(); break;
                case "--listen":      o.ListenPort = NextInt(args, ref i, arg, 1, 65535); break;
                case "--partner":     o.Partner = NextValue(args, ref i, arg); break;
                case "--root":        o.Root = NextValue(args, ref i, arg); break;
                case "--runs":        o.Runs = NextInt(args, ref i, arg, 1, 200); break;
                case "--report-dir":  o.ReportDir = NextValue(args, ref i, arg); break;
                case "--dry-run":     o.DryRunFolder = NextValue(args, ref i, arg); break;
                case "--list":        o.ListOnly = true; break;
                case "--help":
                case "-h":
                case "/?":            o.ShowHelp = true; break;
                default: throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        // --only is checked now that the mode is known: each mode has its own case names.
        if (o._onlyText is not null)
            o.Only = o.ShareSemantics ? ParseSemanticsNames(o._onlyText) : ParseCaseNames(o._onlyText);

        if (o.ListOnly || o.ShowHelp)
            return o;

        if (o.ShareSemantics)
        {
            o.ValidateShareSemantics();
            return o;
        }
        if (o.Role is not null || o.ListenPort != 0 || o.Partner is not null || o.Root is not null || o.Runs is not null)
            throw new UsageException("--role, --listen, --partner, --root and --runs belong to --share-semantics; add --share-semantics.");

        if (o.Share is not null && o.DryRunFolder is not null)
            throw new UsageException("Use either --share (the real run) or --dry-run (the plumbing check), not both.");
        if (o.Share is null && o.DryRunFolder is null)
            throw new UsageException("Give --share <UNC folder> for the real run, --dry-run <local folder> for the plumbing check, or --list.");
        if (holdsGiven && !o.LateLanding)
            throw new UsageException("--holds belongs to --late-landing; add --late-landing, or use --hold for the ordinary cases.");
        if (o.LateLanding && o.Share is not null && o.Holds.Any(h => h < MinLateHoldSeconds))
            throw new UsageException($"--holds: every hold must be at least {MinLateHoldSeconds} s in a real run (the call needs 5 s to give up; shorter holds are only allowed with --dry-run).");
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
          --repeat <n>           repetitions of each mid-operation case (default 3); with --late-landing, repetitions of
                                 each case and hold (default 2)
          --settle <s>           seconds between set-up and a cut-before-call case, so SMB caches expire (default 11)
          --late-landing         run ONLY the late-landing experiment (after the two rig controls): cut the network while
                                 a delete / a rename-over-existing is in flight, bring it back, and watch the server to see
                                 whether the abandoned operation still takes effect, and how long after the call (see --list)
          --holds <a,b,...>      late-landing only: seconds the cut is held (default 10,30,60,90,180; at least 6 in a real run)
          --share-semantics      run ONLY the share-semantics experiments E1 and E2 (designs/network-experiments-5.8.md):
                                 no clumsy, no elevation; two roles (below). Dry run: --share-semantics --dry-run <folder>
            --role partner --listen <port> --root <folder>
                                 on the second computer: a control-channel server that does file calls for the runner,
                                 only inside <folder> (the same folder as the runner's --share). Ctrl+C stops it.
            --role runner --share <folder> --partner <host:port> [--runs n] [--settle s] [--big-mb n]
                                 on this computer: runs E1a, E1b and E2 cases 1-5 and writes the report. --runs defaults to
                                 20 (E1) and 5 (E2); --big-mb to 8; --settle to 11 (0 in a dry run).
          --only <a,b,...>       run only these cases (see --list); with --share-semantics: E1a, E1b, E2
                                 (also E1b-create, E1b-stale, E2-1 ... E2-5)
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
    /// Splits and checks the comma-separated list of seconds given to <c>--holds</c>.
    /// </summary>
    /// <param name="text">The raw list, for example <c>10,30,60</c>.</param>
    /// <returns>The holds, in the order given.</returns>
    /// <exception cref="UsageException">The list is empty or a value is not a whole number from 1 to 900.</exception>
    private static IReadOnlyList<int> ParseHolds(string text)
    {
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var holds = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value) || value < 1 || value > 900)
                throw new UsageException($"--holds must be a comma-separated list of whole seconds from 1 to 900 (got '{part}').");
            holds.Add(value);
        }
        if (holds.Count == 0)
            throw new UsageException("--holds needs at least one value, for example --holds 10,30,60.");
        return holds;
    }

    /// <summary>
    /// Checks the combination of options for <c>--share-semantics</c>.
    /// </summary>
    /// <exception cref="UsageException">
    /// A role is missing or unknown, a needed option is absent, or an option from another mode or role is present.
    /// </exception>
    private void ValidateShareSemantics()
    {
        if (LateLanding)
            throw new UsageException("--share-semantics and --late-landing are separate modes; use one.");
        if (Role is not (null or "runner" or "partner"))
            throw new UsageException($"--role must be runner or partner (got '{Role}').");

        if (Role == "partner")
        {
            if (ListenPort == 0)
                throw new UsageException("--role partner needs --listen <port>.");
            if (Root is null)
                throw new UsageException("--role partner needs --root <folder>: the only folder the partner will touch (the same folder as the runner's --share).");
            if (Share is not null || Partner is not null || DryRunFolder is not null)
                throw new UsageException("--role partner takes --listen and --root only (not --share, --partner or --dry-run).");
            return;
        }

        // Runner (the default for a dry run).
        if (Role is null && !IsDryRun)
            throw new UsageException("--share-semantics needs --role runner or --role partner (or --dry-run <local folder>).");
        if (ListenPort != 0 || Root is not null)
            throw new UsageException("--listen and --root belong to --role partner.");
        if (Share is not null && DryRunFolder is not null)
            throw new UsageException("Use either --share (the real run) or --dry-run (the plumbing check), not both.");
        if (IsDryRun)
        {
            if (Partner is not null)
                throw new UsageException("--dry-run starts its own partner inside this process; leave out --partner.");
            return;
        }
        if (Share is null)
            throw new UsageException("--role runner needs --share <folder> (the folder the partner's --root also names).");
        if (Partner is null)
            throw new UsageException("--role runner needs --partner <host:port>.");
        var colon = Partner.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(Partner[(colon + 1)..], out var port) || port is < 1 or > 65535)
            throw new UsageException($"--partner must be host:port (got '{Partner}').");
    }

    /// <summary>
    /// Splits and checks the comma-separated list given to <c>--only</c> in the share-semantics mode.
    /// </summary>
    /// <param name="text">The raw list.</param>
    /// <returns>The case names.</returns>
    /// <exception cref="UsageException">A name is not a share-semantics case.</exception>
    private static IReadOnlyCollection<string> ParseSemanticsNames(string text)
    {
        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = ShareSemanticsCatalog.Unknown(names);
        if (names.Length == 0 || unknown.Count > 0)
            throw new UsageException($"--only: unknown case(s): {string.Join(", ", unknown)}. With --share-semantics use: {string.Join(", ", ShareSemanticsCatalog.Names)}.");
        return names;
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
