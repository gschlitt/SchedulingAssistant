namespace TermPoint.Services;

/// <summary>
/// Generates initials for a new instructor from their name, avoiding initials already in use.
/// Pure logic with no database access: the caller supplies the set of initials that are taken.
/// This is the shared form of the logic the Dev Tools instructor import uses.
/// </summary>
public static class InstructorInitials
{
    /// <summary>The initials used when the instructor has no usable name at all.</summary>
    private const string FallbackInitials = "XX";

    /// <summary>The highest numeric suffix tried (exclusive) before falling back to a random suffix.</summary>
    private const int MaxNumericSuffix = 100;

    /// <summary>
    /// Builds initials for an instructor that are not in <paramref name="taken"/>.
    /// <para>
    /// The base is the first letter of the first name plus the first letter of the last name,
    /// upper-cased. With no first name it is the first two letters of the last name (or the single
    /// letter when the last name has only one). With neither name it is "XX". A first name without a
    /// last name also gives "XX", as the Dev Tools import does. If the base is free it is returned;
    /// otherwise 2, 3, ... 99 is appended until a free value is found, and as a last resort four
    /// characters of a new GUID.
    /// </para>
    /// <para>
    /// <paramref name="taken"/> is compared <b>ignoring case</b> whatever comparer the caller's set
    /// uses, and is never modified: the caller must add the returned initials to its set before
    /// generating the next instructor's, or two instructors can receive the same initials.
    /// </para>
    /// </summary>
    /// <param name="firstName">The instructor's first name. May be null or blank.</param>
    /// <param name="lastName">The instructor's last name. May be null or blank.</param>
    /// <param name="taken">The initials already in use (existing instructors plus any generated earlier in the same import).</param>
    /// <returns>Initials not present in <paramref name="taken"/> (ignoring case).</returns>
    public static string Generate(string? firstName, string? lastName, IReadOnlySet<string> taken)
    {
        // Own case-insensitive copy, so the caller's comparer cannot let "js" and "JS" both through.
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        var baseInitials = BaseInitials((firstName ?? "").Trim(), (lastName ?? "").Trim());
        if (!used.Contains(baseInitials))
            return baseInitials;

        for (var i = 2; i < MaxNumericSuffix; i++)
        {
            var candidate = $"{baseInitials}{i}";
            if (!used.Contains(candidate))
                return candidate;
        }

        return $"{baseInitials}{Guid.NewGuid().ToString()[..4]}";
    }

    /// <summary>Builds the un-suffixed initials from already-trimmed names; see <see cref="Generate"/> for the rules.</summary>
    /// <param name="first">The trimmed first name (may be empty).</param>
    /// <param name="last">The trimmed last name (may be empty).</param>
    /// <returns>The upper-case base initials.</returns>
    private static string BaseInitials(string first, string last)
    {
        if (first.Length > 0 && last.Length > 0)
            return $"{char.ToUpperInvariant(first[0])}{char.ToUpperInvariant(last[0])}";
        if (last.Length >= 2)
            return $"{char.ToUpperInvariant(last[0])}{char.ToUpperInvariant(last[1])}";
        if (last.Length == 1)
            return $"{char.ToUpperInvariant(last[0])}";
        return FallbackInitials;
    }
}
