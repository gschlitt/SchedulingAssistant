using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TermPoint.Exceptions;

namespace TermPoint.Services;

/// <summary>
/// Which database file a damage report is about (spec item 5, step 5e). Each kind is reported at most once per run.
/// </summary>
public enum DamagedFileKind
{
    /// <summary>The shared database D (the front-door check: startup, switching, the chooser).</summary>
    SharedDatabase,

    /// <summary>The local copy made from it — D' for a writer, D'' for a reader — checked when it is opened.</summary>
    LocalCopy,

    /// <summary>The snapshot taken for an automatic backup, checked before it is copied to the backup folder.</summary>
    BackupSnapshot,

    /// <summary>A backup the user is restoring (or has picked to restore), checked before it may replace anything.</summary>
    BackupBeingRestored
}

/// <summary>
/// Reports a DAMAGED database to the developers (spec item 5, step 5e): a local log line and, to BugSnag, one named
/// error per kind of file per run — with no path, file name or user name in anything sent.
///
/// <para><b>What counts as damage.</b> Only positive evidence of damage INSIDE a SQLite database:
/// <c>integrity_check</c> problems or SQLITE_CORRUPT (<see cref="DamageEvidence.IntegrityProblems"/>,
/// <see cref="DamageEvidence.Corrupt"/>; see <see cref="IsReportable"/>). "This is not a SQLite database at all" is
/// far more often a wrong file the user picked than damage, and a file that is locked, unreadable or unreachable
/// says nothing about its health; neither is ever reported (the callers' own local log lines remain).</para>
///
/// <para><b>Once per run per kind.</b> The memory of what has been reported lives with the logger that receives the
/// reports (in the application there is exactly one, <c>App.Logger</c>, so that is once per run), which also keeps
/// tests that pass their own logger from interfering with each other.</para>
///
/// <para><b>Privacy.</b> The only free text sent is SQLite's own summary (page and table names), and it is passed
/// through <see cref="Sanitize"/> first: anything shaped like a Windows, UNC or Unix path, or a database file name,
/// is replaced, and the length is capped. The exception message is fixed text per kind
/// (<see cref="DatabaseDamagedException.MessageFor"/>).</para>
///
/// <para><b>Never throws.</b> Reporting must never turn a handled problem into a crash.</para>
/// </summary>
public static class DatabaseDamageReporter
{
    /// <summary>The longest summary that is sent, in characters (including the "..." that marks a cut).</summary>
    internal const int MaxSummaryLength = 1000;

    /// <summary>What the summary says when there is none.</summary>
    private const string NoSummary = "(no summary)";

    /// <summary>What the summary says when it could not be checked for private details in time.</summary>
    private const string WithheldSummary = "(summary withheld: it could not be checked for private details)";

    /// <summary>
    /// The logger used when a caller passes none. A seam so the test assembly can point it somewhere harmless: no
    /// test may ever reach BugSnag by way of a damaged file. Production leaves it as <c>App.Logger</c>.
    /// </summary>
    internal static Func<IAppLogger> DefaultLogger { get; set; } = static () => App.Logger;

    /// <summary>The kinds each logger has been sent a report for. Entries die with their logger.</summary>
    private static readonly ConditionalWeakTable<IAppLogger, ReportedKinds> Reported = new();

    /// <summary>The set of kinds already reported to one logger (thread-safe).</summary>
    private sealed class ReportedKinds
    {
        private readonly HashSet<DamagedFileKind> _kinds = new();

        /// <summary>Marks <paramref name="kind"/> as reported.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>True the first time, false if it had been marked before.</returns>
        public bool TryAdd(DamagedFileKind kind)
        {
            lock (_kinds)
                return _kinds.Add(kind);
        }
    }

    /// <summary>
    /// Whether <paramref name="evidence"/> is worth a report: integrity problems or SQLITE_CORRUPT — never
    /// "not a database" and never no evidence.
    /// </summary>
    /// <param name="evidence">The evidence behind an integrity verdict.</param>
    /// <returns>True when it is positive evidence of damage inside a SQLite database.</returns>
    public static bool IsReportable(DamageEvidence evidence)
        => evidence is DamageEvidence.IntegrityProblems or DamageEvidence.Corrupt;

    /// <summary>
    /// The call every detection point makes: reports the damage if — and only if — <paramref name="evidence"/>
    /// is reportable, and the check was a real one.
    /// </summary>
    /// <param name="kind">Which file was checked.</param>
    /// <param name="evidence">The evidence behind the integrity verdict (<see cref="IntegrityCheckOutcome.Evidence"/>).</param>
    /// <param name="summary">SQLite's short summary of the damage (<see cref="IntegrityCheckOutcome.Summary"/>), or null.</param>
    /// <param name="logger">Receives the report and the log lines; null uses <see cref="DefaultLogger"/>.</param>
    /// <param name="injected">
    /// True when the verdict came from a DEBUG fault plan (<see cref="IntegrityFaultPlan"/>,
    /// <see cref="RestoreFaultPlan"/>): fake damage is logged as suppressed and never reported, so it can never reach
    /// BugSnag.
    /// </param>
    /// <returns>True when this call sent a report; false when it did not (not reportable, injected, already
    /// reported this run, or a failure to send).</returns>
    public static bool ReportIfDamaged(
        DamagedFileKind kind,
        DamageEvidence evidence,
        string? summary,
        IAppLogger? logger = null,
        bool injected = false)
    {
        try
        {
            if (!IsReportable(evidence))
            {
                if (evidence == DamageEvidence.NotADatabase)
                    (logger ?? DefaultLogger()).LogInfo(
                        $"DatabaseDamageReporter: {kind} is not recognisable as a SQLite database — not reported " +
                        "(more likely a wrong file than damage).");
                return false;
            }

            if (injected)
            {
                (logger ?? DefaultLogger()).LogInfo(
                    $"DatabaseDamageReporter: the damage of {kind} was injected by a DEBUG fault plan — no report is sent.");
                return false;
            }

            return Report(kind, summary, logger);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reports damage to <paramref name="kind"/>, once per run: the first call per kind (per logger) writes a local
    /// log line and sends a handled BugSnag error through <see cref="IAppLogger.ReportHandled"/> — a
    /// <see cref="DatabaseDamagedException"/> with fixed text per kind, context "Database damage: {kind}", a
    /// per-kind grouping key, and a "damage" tab holding the kind and the sanitized summary. Later calls for the
    /// same kind only write a local log line. Prefer <see cref="ReportIfDamaged"/>, which also decides whether the
    /// evidence is reportable.
    /// </summary>
    /// <param name="kind">Which file is damaged.</param>
    /// <param name="summary">SQLite's short summary of the damage, or null. Sanitized (<see cref="Sanitize"/>) before anything is sent.</param>
    /// <param name="logger">Receives the report and the log lines; null uses <see cref="DefaultLogger"/>.</param>
    /// <returns>True when this call sent a report; false when it was a repeat or sending failed. Never throws.</returns>
    public static bool Report(DamagedFileKind kind, string? summary, IAppLogger? logger = null)
    {
        try
        {
            var sink  = logger ?? DefaultLogger();
            var clean = Sanitize(summary);

            if (!Reported.GetValue(sink, static _ => new ReportedKinds()).TryAdd(kind))
            {
                sink.LogInfo(
                    $"DatabaseDamageReporter: damage to {kind} found again; it was already reported this run, " +
                    $"so nothing more is sent. {clean}");
                return false;
            }

            var metadata = new Dictionary<string, string>
            {
                ["kind"]    = kind.ToString(),
                ["summary"] = clean
            };

            // Local log line, then the BugSnag error — ReportHandled does both, and neither re-throws nor shows a banner.
            sink.ReportHandled(
                CreateException(kind, clean),
                context:       $"Database damage: {kind}",
                metadata:      metadata,
                metadataTab:   "damage",
                groupingKey:   $"database-damage:{kind}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the exception for a report with a real stack trace (it is thrown and caught on the spot): an exception
    /// that was never thrown has none, and the report would show where nothing happened.
    /// </summary>
    /// <param name="kind">Which file is damaged.</param>
    /// <param name="summary">The sanitized summary.</param>
    /// <returns>The exception, carrying a stack trace of the reporting call.</returns>
    private static DatabaseDamagedException CreateException(DamagedFileKind kind, string summary)
    {
        try
        {
            throw new DatabaseDamagedException(kind, summary);
        }
        catch (DatabaseDamagedException ex)
        {
            return ex;
        }
    }

    // ── Sanitizing ────────────────────────────────────────────────────────────

    /// <summary>How long a pattern may take on a summary before it is given up on.</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Characters that end a path in free text: line breaks and quotes (SQLite quotes its messages). Spaces do NOT
    /// end a path — a user folder such as "John Smith" would otherwise leak half its name — so a path is redacted
    /// up to the end of its line or its closing quote.
    /// </summary>
    private const string PathEnd = @"\r\n'""<>|*?";

    /// <summary>The redactions, applied in order: what to look for, and what to put in its place.</summary>
    private static readonly (Regex Pattern, string Replacement)[] Redactions =
    {
        // Windows drive path: C:\Users\x\a.db, C:/Users/x/a.db
        (Make(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^" + PathEnd + "]*"), "<path>"),
        // UNC path: \\server\share\b.db
        (Make(@"\\\\[^" + PathEnd + "]+"), "<path>"),
        // Home-relative path: ~/x/c.db
        (Make(@"~[\\/][^" + PathEnd + "]*"), "<path>"),
        // Unix absolute path: /Users/x/c.db  (not the "/" in "1/2" or "and / or")
        (Make(@"(?<![\w.\-])/(?=[\w.~\-])[^" + PathEnd + "]*"), "<path>"),
        // A bare database file name: backup_2026-10-14.db, my.db-wal
        (Make(@"[^\s\\/'""<>|*?:]+\.(?:db|sqlite3?|bak|tmp|backuptmp|journal|wal|shm)(?:-wal|-shm|-journal)?\b",
              RegexOptions.IgnoreCase), "<file>")
    };

    /// <summary>Compiles one redaction pattern with the shared timeout.</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <param name="options">Extra options.</param>
    /// <returns>The compiled pattern.</returns>
    private static Regex Make(string pattern, RegexOptions options = RegexOptions.None)
        => new(pattern, options | RegexOptions.CultureInvariant, PatternTimeout);

    /// <summary>
    /// Makes SQLite's summary safe to send. SQLite's own text names pages and tables and should contain no paths at
    /// all, so this is a defence in depth: anything shaped like a Windows, UNC or Unix path, or like a database file
    /// name, is replaced by "&lt;path&gt;" / "&lt;file&gt;", and the result is cut to <see cref="MaxSummaryLength"/>
    /// characters. A summary that cannot be checked in time is withheld rather than sent unchecked.
    /// </summary>
    /// <param name="summary">The raw summary, or null.</param>
    /// <returns>The text to send; never null or empty, never longer than <see cref="MaxSummaryLength"/>.</returns>
    internal static string Sanitize(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return NoSummary;

        try
        {
            var text = summary;
            foreach (var (pattern, replacement) in Redactions)
                text = pattern.Replace(text, replacement);

            text = text.Trim();
            if (text.Length == 0)
                return NoSummary;

            return text.Length <= MaxSummaryLength
                ? text
                : text[..(MaxSummaryLength - 3)] + "...";
        }
        catch (RegexMatchTimeoutException)
        {
            return WithheldSummary;
        }
    }

    /// <summary>Forgets every report that has been made, so the next one per kind is sent again. For tests.</summary>
    internal static void ResetForTests() => Reported.Clear();
}
