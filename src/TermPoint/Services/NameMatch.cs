using System.Text;

namespace TermPoint.Services;

/// <summary>
/// Forgiving comparison of names and codes typed by people: ignores case and stray spacing, and can
/// tell when two names that are not equal are probably typos of each other. Used by the section
/// importer to match typed values against the department's existing setup values and to warn about
/// near misses. Pure static code with no UI dependency.
/// </summary>
public static class NameMatch
{
    // ── Names ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tidies a name for comparison: trims it and collapses every run of whitespace to a single space.
    /// Case is left alone (comparison is case-insensitive; see <see cref="Equal"/>).
    /// </summary>
    /// <param name="value">The text to tidy. May be null.</param>
    /// <returns>The tidied text; empty when <paramref name="value"/> is null or blank.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                // Only emit a space once real text has started, and only before the next real character.
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when the two names are the same after <see cref="Normalize"/>, ignoring case
    /// (<see cref="StringComparison.OrdinalIgnoreCase"/>). Two null or blank names are equal.
    /// </summary>
    /// <param name="a">The first name. May be null.</param>
    /// <param name="b">The second name. May be null.</param>
    /// <returns>True when the names match.</returns>
    public static bool Equal(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A comparer that treats names as equal under <see cref="Equal"/>, for use as a dictionary or
    /// hash-set comparer (for example <c>new Dictionary&lt;string, Room&gt;(NameMatch.Comparer)</c>).
    /// Null and blank are the same key.
    /// </summary>
    public static StringComparer Comparer { get; } = new NormalizedComparer(Normalize);

    // ── Codes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reduces a code (such as a course code) for comparison by removing <b>all</b> whitespace, so
    /// "CHEM 101" and "CHEM101" are the same. Case is left alone; compare with
    /// <see cref="CodeEqual"/> or <see cref="CodeComparer"/>.
    /// </summary>
    /// <param name="value">The code to reduce. May be null.</param>
    /// <returns>The code with no whitespace; empty when <paramref name="value"/> is null or blank.</returns>
    public static string NormalizeCode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>True when the two codes are the same after <see cref="NormalizeCode"/>, ignoring case.</summary>
    /// <param name="a">The first code. May be null.</param>
    /// <param name="b">The second code. May be null.</param>
    /// <returns>True when the codes match.</returns>
    public static bool CodeEqual(string? a, string? b) =>
        string.Equals(NormalizeCode(a), NormalizeCode(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A comparer that treats codes as equal under <see cref="CodeEqual"/>, for dictionary keys.</summary>
    public static StringComparer CodeComparer { get; } = new NormalizedComparer(NormalizeCode);

    // ── Near misses ───────────────────────────────────────────────────────────

    /// <summary>The shortest reduced name that may be matched by containment ("Lec" in "Lecture").</summary>
    private const int MinContainmentLength = 3;

    /// <summary>The shortest reduced name (both sides) that may be matched by edit distance.</summary>
    private const int MinEditDistanceLength = 4;

    /// <summary>The largest edit distance still counted as a typo.</summary>
    private const int MaxEditDistance = 2;

    /// <summary>
    /// Decides whether two names that are <b>not</b> equal under <see cref="Equal"/> look like the same
    /// name typed differently, so the importer can warn instead of silently creating a duplicate.
    /// <para>
    /// Both names are first reduced to lower-case letters and digits only. Then, before anything
    /// else, the digits of each reduced name are taken in order ("lab12b" gives "12"); if the two
    /// digit sequences differ the names are <b>never</b> a near miss. Numbers mark distinct things
    /// ("Lab 1" and "Lab 2", "Year 1" and "Year 2", "Section 1" and "Section 2"), so they must not
    /// produce typo warnings; this also means "Lab" and "Lab 1" are not a near miss. When the digit
    /// sequences are equal (including both empty), the names are a near miss when
    /// (1) the reduced forms are equal ("Lab." and "Lab"); or (2) one contains the other and the
    /// shorter has at least 3 characters ("Lec" and "Lecture"; shorter forms are not tested for
    /// containment so a one-letter tag is not flagged against everything); or (3) both have at least
    /// 4 characters and are within an optimal-string-alignment distance of 2, i.e. at most two
    /// insertions, deletions, substitutions or adjacent swaps ("Writng" and "Writing", "Lectrue" and
    /// "Lecture"). Otherwise they are not: "LAB" and "LAW" are different names.
    /// </para>
    /// </summary>
    /// <param name="a">The first name.</param>
    /// <param name="b">The second name.</param>
    /// <returns>True when the names are probably the same name mistyped.</returns>
    public static bool IsNearMiss(string a, string b)
    {
        var ra = Reduce(a);
        var rb = Reduce(b);

        // Numbers mark distinct things ("Lab 1" / "Lab 2"), so different numbers are never a typo.
        if (DigitsOf(ra) != DigitsOf(rb))
            return false;

        if (ra == rb)
            return true;

        var (shorter, longer) = ra.Length <= rb.Length ? (ra, rb) : (rb, ra);
        if (shorter.Length >= MinContainmentLength && longer.Contains(shorter, StringComparison.Ordinal))
            return true;

        return shorter.Length >= MinEditDistanceLength
               && OptimalStringAlignmentDistance(ra, rb) <= MaxEditDistance;
    }

    /// <summary>Extracts the digits of an already-reduced name, in order ("lab12b" gives "12").</summary>
    /// <param name="reduced">A name reduced by <see cref="Reduce"/>.</param>
    /// <returns>The digits only; empty when the name has none.</returns>
    private static string DigitsOf(string reduced) => new(reduced.Where(char.IsDigit).ToArray());

    /// <summary>Reduces a name to its lower-case letters and digits, dropping spaces and punctuation.</summary>
    /// <param name="value">The name to reduce. May be null.</param>
    /// <returns>The reduced form; empty when nothing remains.</returns>
    private static string Reduce(string? value)
    {
        var sb = new StringBuilder(value?.Length ?? 0);
        foreach (var c in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The optimal-string-alignment distance (Damerau-Levenshtein restricted so no substring is edited
    /// twice): the fewest insertions, deletions, substitutions and swaps of two adjacent characters
    /// needed to turn <paramref name="s"/> into <paramref name="t"/>. Ordinal comparison.
    /// </summary>
    /// <param name="s">The first string.</param>
    /// <param name="t">The second string.</param>
    /// <returns>The distance; 0 when the strings are equal.</returns>
    private static int OptimalStringAlignmentDistance(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                // Adjacent transposition ("ei" typed as "ie").
                if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[s.Length, t.Length];
    }

    // ── Comparer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="StringComparer"/> that first runs each string through a normaliser and then
    /// compares ignoring case. Null is normalised like blank, so it never throws on null keys.
    /// </summary>
    private sealed class NormalizedComparer : StringComparer
    {
        /// <summary>The normaliser applied to both sides before comparing.</summary>
        private readonly Func<string?, string> _normalize;

        /// <summary>Creates a comparer that normalises with <paramref name="normalize"/>.</summary>
        /// <param name="normalize">Maps a string (or null) to the form that is compared.</param>
        public NormalizedComparer(Func<string?, string> normalize) => _normalize = normalize;

        /// <inheritdoc />
        public override int Compare(string? x, string? y) =>
            StringComparer.OrdinalIgnoreCase.Compare(_normalize(x), _normalize(y));

        /// <inheritdoc />
        public override bool Equals(string? x, string? y) =>
            StringComparer.OrdinalIgnoreCase.Equals(_normalize(x), _normalize(y));

        /// <inheritdoc />
        public override int GetHashCode(string obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(_normalize(obj));
    }
}
