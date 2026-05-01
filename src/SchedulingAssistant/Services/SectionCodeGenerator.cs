using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Generates the next available section code for a given <see cref="SectionCodePattern"/>.
/// </summary>
public static class SectionCodeGenerator
{
    /// <summary>
    /// Maximum number of codes to try before giving up. Prevents unbounded loops when every
    /// code in a long sequence is already taken.
    /// </summary>
    private const int MaxAttempts = 500;

    /// <summary>
    /// Returns the first code in the pattern's sequence that is not already taken,
    /// or <c>null</c> if all codes within <see cref="MaxAttempts"/> are exhausted.
    /// </summary>
    /// <param name="pattern">The pattern defining the code structure and increment rule.</param>
    /// <param name="isCodeTaken">
    /// Predicate returning <c>true</c> when the given code already exists in the target
    /// course/semester. Typically a partial application of
    /// <c>ISectionRepository.ExistsBySectionCode</c>.
    /// </param>
    public static string? GetNextCode(SectionCodePattern pattern, Func<string, bool> isCodeTaken)
    {
        foreach (var candidate in EnumerateCodes(pattern))
        {
            if (!isCodeTaken(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Returns the first few codes in the pattern's sequence for display as a preview.
    /// Used by the admin editor to verify the pattern looks correct before saving.
    /// </summary>
    /// <param name="pattern">The pattern to preview.</param>
    /// <param name="count">Number of example codes to return (default 3).</param>
    public static IReadOnlyList<string> GetPreviewCodes(SectionCodePattern pattern, int count = 3)
    {
        return EnumerateCodes(pattern).Take(count).ToList();
    }

    /// <summary>
    /// Lazily enumerates all codes in the sequence up to <see cref="MaxAttempts"/>.
    /// </summary>
    private static IEnumerable<string> EnumerateCodes(SectionCodePattern pattern)
    {
        if (pattern.UseLetters)
        {
            for (int i = 0; i < 26 && i < MaxAttempts; i++)
            {
                char letter = (char)(pattern.FirstLetter + i);
                if (letter > 'Z') yield break;
                yield return $"{pattern.Prefix}{letter}{pattern.Suffix}";
            }
        }
        else
        {
            for (int i = 0; i < MaxAttempts; i++)
            {
                long value = (long)pattern.FirstNumber + (long)pattern.Increment * i;
                string numStr = pattern.PadWidth > 0
                    ? value.ToString().PadLeft(pattern.PadWidth, '0')
                    : value.ToString();
                yield return $"{pattern.Prefix}{numStr}{pattern.Suffix}";
            }
        }
    }
}
