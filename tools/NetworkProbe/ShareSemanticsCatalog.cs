namespace NetworkProbe;

/// <summary>
/// One table of the share-semantics experiment (<c>--share-semantics</c>): its id, what it asks, and the columns of its
/// report table. Mirrors <c>designs/network-experiments-5.8.md</c> (E1 and E2).
/// </summary>
/// <param name="Id">The table's id: <c>E1a</c>, <c>E1b-create</c>, <c>E1b-stale</c>, <c>E2-1</c> to <c>E2-5</c>.</param>
/// <param name="Group">The name <c>--only</c> accepts for it and its siblings: <c>E1a</c>, <c>E1b</c> or <c>E2</c>.</param>
/// <param name="Title">The heading in the report and on the console.</param>
/// <param name="Why">One line on what the table answers, from the design.</param>
/// <param name="Headers">Column titles between the run number and the final outcome and note columns.</param>
/// <param name="MetricNames">Names (with units) of the one or two numbers the summary row shows min / median / max of.</param>
/// <param name="DefaultRuns">Runs when <c>--runs</c> is not given (20 for E1, 5 for E2, as in the design).</param>
internal sealed record SemanticsCase(
    string Id,
    string Group,
    string Title,
    string Why,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string> MetricNames,
    int DefaultRuns);

/// <summary>
/// The fixed words a run's outcome can take. The report's "Reading the result" section counts these, so a case and the
/// reading of it must use the same constants.
/// </summary>
internal static class Outcomes
{
    /// <summary>Something other than the experiment's question went wrong in this run (see its note).</summary>
    public const string Error = "ERROR";

    // E1a
    /// <summary>E1a: both checks after the create found the file.</summary>
    public const string NeverMissing = "never missing";
    /// <summary>E1a: File.Exists or the open said "missing" right after our own create.</summary>
    public const string SaidMissing = "SAID MISSING";

    // E1b-create
    /// <summary>E1b: CreateNew over the partner's file failed with "already exists".</summary>
    public const string RefusedExists = "refused: already exists";
    /// <summary>E1b: CreateNew failed, but with some other error.</summary>
    public const string RefusedOther = "refused: other error";
    /// <summary>E1b: CreateNew SUCCEEDED over the partner's file.</summary>
    public const string Critical = "CRITICAL: lock not exclusive";

    // E1b-stale
    /// <summary>E1b: File.Exists and open found the file on the first look after the partner's create.</summary>
    public const string NotStale = "found at once";
    /// <summary>E1b: at least one of them said "not found" for a while, then found the file.</summary>
    public const string StaleThenFound = "stale, then found";
    /// <summary>E1b: at least one still said "not found" when polling stopped.</summary>
    public const string StillMissing = "STILL MISSING at cap";

    // E2-1
    /// <summary>E2 case 1: the rename was refused while the partner held the file, and went through after it closed.</summary>
    public const string RefusedUntilClose = "refused while held";
    /// <summary>E2 case 1: the rename went through while the partner still held the file.</summary>
    public const string RenamedWhileHeld = "renamed while held";
    /// <summary>E2 case 1: still refused 5 s after the partner closed.</summary>
    public const string RefusedAfterClose = "REFUSED EVEN AFTER CLOSE";

    // E2-2 .. E2-4 (the second half of a case 3/4 outcome follows these)
    /// <summary>E2 cases 2-4: the rename went through (with the partner's handle open).</summary>
    public const string RenameOk = "rename ok";
    /// <summary>E2 cases 2-4: the rename was refused.</summary>
    public const string RenameRefused = "rename REFUSED";
    /// <summary>E2 cases 3-4: text added to an outcome when the reader's hash matched neither file.</summary>
    public const string ReaderMix = "reader MIX";
    /// <summary>E2 cases 3-4: the reader's hash matched the old file.</summary>
    public const string ReaderOld = "reader old";
    /// <summary>E2 cases 3-4: the reader's hash matched the new file.</summary>
    public const string ReaderNew = "reader new";
    /// <summary>E2 cases 3-4: the reader failed with an error.</summary>
    public const string ReaderError = "reader ERROR";
    /// <summary>E2 cases 3-4: the reader had not finished when the wait ended, or finished with fewer bytes than the file.</summary>
    public const string ReaderIncomplete = "reader incomplete";

    // E2-5
    /// <summary>E2 case 5: delete went through and the next CreateNew worked at once.</summary>
    public const string NotBlocked = "delete ok; CreateNew not blocked";
    /// <summary>E2 case 5: delete went through but CreateNew failed for a while.</summary>
    public const string Blocked = "delete ok; CreateNew BLOCKED";
    /// <summary>E2 case 5: the delete itself was refused.</summary>
    public const string DeleteRefused = "delete REFUSED";
}

/// <summary>
/// The tables of the share-semantics experiment and the names <c>--only</c> accepts.
/// </summary>
internal static class ShareSemanticsCatalog
{
    /// <summary>All tables, in the order they run.</summary>
    public static readonly IReadOnlyList<SemanticsCase> All = new[]
    {
        new SemanticsCase("E1a", "E1a",
            "E1a: does our own create clear our cached \"not found\"? (runner only)",
            "Prime \"not found\" (P1: File.Exists, P2: a failed open), FileMode.CreateNew the file, then at once check with File.Exists and with open+read.",
            new[] { "variant", "prime", "CreateNew", "File.Exists after", "open+read after", "said missing?" },
            new[] { "missing for (s)" }, 20),

        new SemanticsCase("E1b-create", "E1b",
            "E1b: does CreateNew always reach the server? (partner creates, runner tries CreateNew)",
            "Prime \"not found\" on the runner, the partner creates the file, the runner tries FileMode.CreateNew at once: it must fail with \"already exists\".",
            new[] { "variant", "partner CREATENEW (partner ms / round trip ms)", "runner CreateNew", "runner CreateNew ms" },
            new[] { "runner CreateNew (ms)" }, 20),

        new SemanticsCase("E1b-stale", "E1b",
            "E1b: how long does \"not found\" last after the partner creates the file?",
            "Prime \"not found\" (File.Exists and a failed open), the partner creates the file (fresh name), then poll File.Exists and an open every 50 ms for up to 15 s.",
            new[] { "partner CREATENEW (partner ms / round trip ms)", "File.Exists said missing for", "open said missing for" },
            new[] { "File.Exists missing for (s)", "open missing for (s)" }, 20),

        new SemanticsCase("E2-1", "E2",
            "E2 case 1: the reader holds the file as TermPoint does today (share ReadWrite); the writer renames over it",
            "Control: \"in use\" is expected. How long does the rename stay refused (retry every 100 ms, up to 10 s)?",
            new[] { "first rename", "attempts", "refused for (while held)", "ok after partner closed", "dest content after" },
            new[] { "refused for (s)", "ok after close (s)" }, 5),

        new SemanticsCase("E2-2", "E2",
            "E2 case 2: the reader holds the file with share ReadWrite|Delete; the writer renames over it",
            "Does the rename go through?",
            new[] { "rename", "dest content after" },
            new[] { "rename (ms)" }, 5),

        new SemanticsCase("E2-3", "E2",
            "E2 case 3: as case 2, and the reader is reading through its handle during the rename",
            "After it, does the reader's handle give the old content, an error, or a mix (compare hashes)?",
            new[] { "rename", "reader progress when the rename returned", "reader finished with", "reader's bytes match", "dest content after" },
            new[] { "rename (ms)" }, 5),

        new SemanticsCase("E2-4", "E2",
            "E2 case 4: the lock file (small), share ReadWrite|Delete, while the writer's heartbeat replaces it",
            "As cases 2 and 3 for the lock file: does the rename go through, and what does a reader reading during it get?",
            new[] { "rename", "reader progress when the rename returned", "reader finished with", "reader's bytes match", "dest content after" },
            new[] { "rename (ms)" }, 5),

        new SemanticsCase("E2-5", "E2",
            "E2 case 5: the lock file held ReadWrite|Delete while the writer deletes it (release)",
            "Does a CreateNew fail while the reader's handle is open (\"delete pending\")? Retried every 100 ms for up to 5 s.",
            new[] { "delete", "first CreateNew", "CreateNew blocked for", "CreateNew ok after partner closed" },
            new[] { "blocked for (s)", "ok after close (s)" }, 5)
    };

    /// <summary>Every name <c>--only</c> accepts: the three groups and each table's own id.</summary>
    public static IReadOnlyList<string> Names
        => All.Select(c => c.Group).Concat(All.Select(c => c.Id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Which names in a list are not cases.
    /// </summary>
    /// <param name="names">Names from <c>--only</c>.</param>
    /// <returns>The unknown ones (empty when all are known).</returns>
    public static IReadOnlyList<string> Unknown(IEnumerable<string> names)
        => names.Where(n => !Names.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Is a table selected by <c>--only</c>?
    /// </summary>
    /// <param name="only">The <c>--only</c> names, or null for all.</param>
    /// <param name="c">The table.</param>
    /// <returns>True when all are selected or the table's id or group is named.</returns>
    public static bool IsSelected(IReadOnlyCollection<string>? only, SemanticsCase c)
        => only is null ||
           only.Contains(c.Id, StringComparer.OrdinalIgnoreCase) ||
           only.Contains(c.Group, StringComparer.OrdinalIgnoreCase);
}
