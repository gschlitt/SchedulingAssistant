using SchedulingAssistant.Models;
using System.Text.RegularExpressions;

namespace SchedulingAssistant.Services;

/// <summary>
/// Static utility methods for matching and advancing section codes based on
/// the configured list of <see cref="SectionPrefix"/> entries.
///
/// This class is intentionally dependency-free so it can be called from both
/// the section editor (transient view-model) and the Copy operation (singleton
/// view-model) without duplication.  Callers supply a <c>codeExists</c> delegate
/// scoped to the relevant course and semester.
/// </summary>
public static class SectionPrefixHelper
{
    /// <summary>
    /// Returns the longest-matching <see cref="SectionPrefix"/> whose
    /// <see cref="SectionPrefix.Prefix"/> text appears at the start of
    /// <paramref name="sectionCode"/>, immediately followed by at least one digit.
    /// Returns <c>null</c> if no prefix matches.
    ///
    /// Longest-match semantics: if both "A" and "AB" are configured and the code
    /// is "AB1", "AB" is returned rather than "A".
    /// Matching is case-insensitive.
    /// </summary>
    /// <param name="sectionCode">The section code to inspect (e.g. "AB1", "A#3").</param>
    /// <param name="prefixes">All configured section prefixes.</param>
    /// <returns>The best-matching <see cref="SectionPrefix"/>, or <c>null</c>.</returns>
    public static SectionPrefix? MatchPrefix(string sectionCode, IReadOnlyList<SectionPrefix> prefixes)
    {
        if (string.IsNullOrEmpty(sectionCode) || prefixes.Count == 0)
            return null;

        SectionPrefix? best = null;

        foreach (var p in prefixes)
        {
            if (string.IsNullOrEmpty(p.Prefix))
                continue;

            // The code must begin with this prefix (case-insensitive)…
            if (!sectionCode.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // …and the very next character must be a digit (ensures we matched a
            // prefix-integer code, not a code that merely shares a leading substring).
            int afterPrefix = p.Prefix.Length;
            if (afterPrefix >= sectionCode.Length || !char.IsDigit(sectionCode[afterPrefix]))
                continue;

            // Keep the longest match.
            if (best is null || p.Prefix.Length > best.Prefix.Length)
                best = p;
        }

        return best;
    }

    /// <summary>
    /// Finds the first available section code of the form <c>{prefix}{n}</c>
    /// (n = 1, 2, … 999) by scanning for gaps in the existing sequence.
    /// Returns <c>null</c> if every slot from 1 to 999 is already taken.
    /// </summary>
    /// <param name="prefix">The prefix string (e.g. "AB", "A#", "CH").</param>
    /// <param name="codeExists">
    /// A delegate that returns <c>true</c> when the candidate code is already in use.
    /// The caller is responsible for scoping this to the correct course and semester.
    /// </param>
    /// <returns>The first available code string, or <c>null</c>.</returns>
    public static string? FindNextAvailableCode(string prefix, Func<string, bool> codeExists)
    {
        for (int n = 1; n <= 999; n++)
        {
            var candidate = $"{prefix}{n}";
            if (!codeExists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Derives the next available section code for a Copy operation, starting
    /// from <paramref name="sourceCode"/>.
    ///
    /// <list type="bullet">
    ///   <item>
    ///     If <paramref name="sourceCode"/> starts with a known configured prefix
    ///     followed by digits, <see cref="FindNextAvailableCode"/> is used to find
    ///     the first gap in that prefix's integer sequence (gap-fill semantics,
    ///     consistent with the Add behaviour).
    ///   </item>
    ///   <item>
    ///     If no known prefix is detected, the trailing integer is incremented by
    ///     one and checked for availability (simple advance fallback).
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="sourceCode">Section code of the section being copied.</param>
    /// <param name="prefixes">All configured section prefixes.</param>
    /// <param name="codeExists">
    /// A delegate that returns <c>true</c> when the candidate code is already in use.
    /// The caller scopes this to the correct course and semester.
    /// </param>
    /// <returns>
    /// A tuple of (<c>Code</c>, <c>CampusId</c>).  Either field may be <c>null</c>:
    /// <c>Code</c> is <c>null</c> when no slot is available within the allowed range;
    /// <c>CampusId</c> is <c>null</c> when no campus is associated with the matched
    /// prefix, or when no prefix was matched at all.
    /// </returns>
    public static (string? Code, string? CampusId) AdvanceSectionCode(
        string sourceCode,
        IReadOnlyList<SectionPrefix> prefixes,
        Func<string, bool> codeExists)
    {
        if (string.IsNullOrEmpty(sourceCode))
            return (null, null);

        // Attempt to match a configured prefix first.
        var matched = MatchPrefix(sourceCode, prefixes);
        if (matched is not null)
        {
            var next = FindNextAvailableCode(matched.Prefix, codeExists);
            return (next, matched.CampusId);
        }

        // Fallback: strip trailing digits and increment by one.
        var m = Regex.Match(sourceCode, @"^(.*?)(\d+)$");
        if (!m.Success)
            return (null, null);

        var fallbackPrefix = m.Groups[1].Value;
        var number         = int.Parse(m.Groups[2].Value);
        var candidate      = $"{fallbackPrefix}{number + 1}";

        // Only return the candidate if it is actually available.
        return codeExists(candidate) ? (null, null) : (candidate, null);
    }
}
