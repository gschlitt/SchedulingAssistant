using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace TermPoint.Services;

/// <summary>
/// Removes private details from free text before it leaves this computer in an error report (spec item 6, step 6a):
/// file paths, database file names, email addresses, and the names of people and computers that TermPoint knows about.
/// Each is replaced by a short tag that keeps what is useful for diagnosis and identifies nobody.
///
/// <para><b>What each value becomes</b> (the replacements are applied in this order):</para>
/// <list type="bullet">
///   <item><b>A Windows drive path</b> (<c>C:\Users\Jo Smith\a.db</c>, <c>H:/x/b.db</c>): <c>&lt;drive C: path&gt;</c>.
///   The drive letter tells a local drive from a mapped one and names nobody.</item>
///   <item><b>A UNC path</b> (<c>\\server\share\b.db</c>): <c>&lt;unc path&gt;</c>.</item>
///   <item><b>A home-relative path</b> (<c>~/x/c.db</c>): <c>&lt;home path&gt;</c>.</item>
///   <item><b>A Unix absolute path</b> (<c>/Users/x/c.db</c>; not the "/" in "1/2" or "and / or"):
///   <c>&lt;unix path&gt;</c>. An <c>https://</c> address loses its path the same way (accepted).</item>
///   <item><b>An email address</b>: <c>&lt;email&gt;</c>.</item>
///   <item><b>A bare database file name</b> (<c>BIOL-TT.db</c>, <c>x.db-wal</c>; they often name the department):
///   <c>&lt;file&gt;</c>.</item>
///   <item><b>A registered name</b> (<see cref="RegisterSensitive"/>): its label, <see cref="UserLabel"/> or
///   <see cref="MachineLabel"/>, matched as a whole word in any letter case. This computer's user name and machine name
///   are registered from the start; the lock service adds every holder name and machine it reads (step 6c).</item>
/// </list>
///
/// <para><b>Paths end at a line break or a quote, not at a space</b>, so a user folder such as "Jo Smith" can't leak
/// half its name; a path is redacted up to the end of its line or its closing quote. Names are matched after the paths
/// so a name inside a path goes with the path.</para>
///
/// <para><b>Deadline.</b> Every pattern has a <see cref="PatternTimeout"/>; on a pattern that takes longer,
/// <see cref="ScrubText"/> throws <see cref="RegexMatchTimeoutException"/>, and its callers send nothing rather than
/// unchecked text (<see cref="DatabaseDamageReporter.Sanitize"/> withholds the summary).</para>
///
/// <para>The local log is never scrubbed: it stays on this computer and keeps full paths for diagnosis.</para>
/// </summary>
public static class PrivacyScrubber
{
    /// <summary>The tag that replaces a registered person's name.</summary>
    public const string UserLabel = "<user>";

    /// <summary>The tag that replaces a registered computer's name.</summary>
    public const string MachineLabel = "<machine>";

    /// <summary>Registered names shorter than this are ignored: they would match ordinary words.</summary>
    internal const int MinimumNameLength = 3;

    /// <summary>How long one pattern may take on one text before the text is given up on.</summary>
    internal static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Characters that end a path in free text: line breaks and quotes (and characters no path may contain). Spaces do
    /// NOT end a path (see the class summary).
    /// </summary>
    private const string PathEnd = @"\r\n'""<>|*?";

    /// <summary>The fixed redactions, applied in order: the pattern, and what to put in place of each match.</summary>
    private static readonly (Regex Pattern, MatchEvaluator Replace)[] Redactions =
    {
        // Windows drive path: C:\Users\x\a.db, C:/Users/x/a.db. The tag keeps the drive letter.
        (Make(@"(?<![A-Za-z0-9])(?<drive>[A-Za-z]):[\\/][^" + PathEnd + "]*"),
            m => $"<drive {char.ToUpperInvariant(m.Groups["drive"].Value[0])}: path>"),
        // UNC path: \\server\share\b.db
        (Make(@"\\\\[^" + PathEnd + "]+"), _ => "<unc path>"),
        // Home-relative path: ~/x/c.db
        (Make(@"~[\\/][^" + PathEnd + "]*"), _ => "<home path>"),
        // Unix absolute path: /Users/x/c.db  (not the "/" in "1/2" or "and / or")
        (Make(@"(?<![\w.\-])/(?=[\w.~\-])[^" + PathEnd + "]*"), _ => "<unix path>"),
        // Email address: jane.doe@example.ca (also inside a mailto: link)
        (Make(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"), _ => "<email>"),
        // A bare database file name: backup_2026-10-14.db, my.db-wal
        (Make(@"[^\s\\/'""<>|*?:]+\.(?:db|sqlite3?|bak|tmp|backuptmp|journal|wal|shm)(?:-wal|-shm|-journal)?\b",
              RegexOptions.IgnoreCase), _ => "<file>"),
    };

    /// <summary>A registered name: the pattern that finds it as a whole word, and its label.</summary>
    /// <param name="Name">The name as registered (trimmed); compared case-insensitively.</param>
    /// <param name="Pattern">Finds the name as a whole word, in any letter case.</param>
    /// <param name="Label">What replaces it.</param>
    private sealed record SensitiveName(string Name, Regex Pattern, string Label);

    /// <summary>Guards <see cref="_names"/>; registrations come from any thread.</summary>
    private static readonly object NamesLock = new();

    /// <summary>
    /// The registered names, longest first (so a longer name is never cut short by a shorter one it contains). Replaced
    /// as a whole on each registration, so <see cref="ScrubText"/> can read it without the lock.
    /// </summary>
    private static IReadOnlyList<SensitiveName> _names = Array.Empty<SensitiveName>();

    /// <summary>
    /// Test seam: called with the text at the start of every <see cref="ScrubText"/>. Lets a test make the scrubber
    /// fail (for example throw <see cref="RegexMatchTimeoutException"/>) without a slow input. Always null in the app.
    /// </summary>
    internal static Action<string>? BeforeScrubForTests { get; set; }

    /// <summary>Registers this computer's own user name and machine name.</summary>
    static PrivacyScrubber()
    {
        RegisterSensitive(TryRead(() => Environment.UserName), UserLabel);
        RegisterSensitive(TryRead(() => Environment.MachineName), MachineLabel);
    }

    /// <summary>
    /// Adds a name that must never appear in an error report: a person's user name (<see cref="UserLabel"/>) or a
    /// computer's name (<see cref="MachineLabel"/>). From then on <see cref="ScrubText"/> replaces it, as a whole word
    /// in any letter case, by <paramref name="label"/>. A null or blank name, one shorter than
    /// <see cref="MinimumNameLength"/> characters after trimming, and a name already registered are ignored. Thread-safe;
    /// never throws.
    /// </summary>
    /// <param name="name">The name, or null.</param>
    /// <param name="label">Its replacement, normally <see cref="UserLabel"/> or <see cref="MachineLabel"/>.</param>
    public static void RegisterSensitive(string? name, string label)
    {
        try
        {
            var trimmed = name?.Trim();
            if (trimmed is null || trimmed.Length < MinimumNameLength)
                return;

            lock (NamesLock)
            {
                if (_names.Any(n => string.Equals(n.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    return;

                // Whole word: not preceded or followed by a letter, digit or underscore (in any script).
                var pattern = Make(@"(?<![\p{L}\p{N}_])" + Regex.Escape(trimmed) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase);
                _names = _names
                    .Append(new SensitiveName(trimmed, pattern, label))
                    .OrderByDescending(n => n.Name.Length)
                    .ToList();
            }
        }
        catch (Exception)
        {
            // A name that can't be registered can't be scrubbed by name, but paths and the rest still are; reporting
            // must never fail because of it.
        }
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every path, email address, database file name and registered name replaced
    /// by its tag (see the class summary). Text with none of them comes back unchanged.
    /// </summary>
    /// <param name="text">The text to scrub, or null.</param>
    /// <returns>The scrubbed text; null when <paramref name="text"/> is null.</returns>
    /// <exception cref="RegexMatchTimeoutException">A pattern took longer than <see cref="PatternTimeout"/>. The caller
    /// must not send the text (nor fall back to the original).</exception>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? ScrubText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        BeforeScrubForTests?.Invoke(text);

        foreach (var (pattern, replace) in Redactions)
            text = pattern.Replace(text, replace);

        foreach (var name in _names)
            text = name.Pattern.Replace(text, name.Label);

        return text;
    }

    /// <summary>Compiles one pattern with the shared timeout and invariant culture.</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <param name="options">Extra options.</param>
    /// <returns>The compiled pattern.</returns>
    private static Regex Make(string pattern, RegexOptions options = RegexOptions.None)
        => new(pattern, options | RegexOptions.CultureInvariant, PatternTimeout);

    /// <summary>Reads an environment value, or null if the platform refuses (the browser build has no user name).</summary>
    /// <param name="read">The read.</param>
    /// <returns>The value, or null.</returns>
    private static string? TryRead(Func<string> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }
}
